using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Xml;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// One open document: the text, the design surface built from it, and the dirty flag.
    /// </summary>
    /// <remarks>
    /// Ported from the reference's Document.cs with five deliberate deviations, all recorded in
    /// the Phase 5 spec: no DocumentMode (there is no XAML text view); Dispose unsubscribes and
    /// unloads (the reference does neither, and the soak test depends on both); the constructor
    /// does not assign FilePath through a side-effecting property; the file write lives in
    /// ShellModel, not here; and IsDirty is set only by the undo stack, never by the Text setter.
    /// </remarks>
    public sealed class Document : IDisposable, INotifyPropertyChanged
    {
        readonly DesignSurface designSurface = new DesignSurface();
        readonly List<XamlError> loadErrors = new List<XamlError>();

        string text;
        string filePath;
        string tempName;
        bool isDirty;
        bool disposed;
        UndoService subscribedUndoService;

        public Document(string filePath, string text)
        {
            this.filePath = filePath;
            this.text = text;
            UpdateDesign();
            isDirty = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Text
        {
            get { return text; }
        }

        public string FilePath
        {
            get { return filePath; }
            set
            {
                filePath = value;
                RaisePropertyChanged("FilePath");
                RaisePropertyChanged("FileName");
                RaisePropertyChanged("Name");
                RaisePropertyChanged("Title");
            }
        }

        public string FileName
        {
            get { return filePath == null ? null : Path.GetFileName(filePath); }
        }

        public string Name
        {
            get { return FileName ?? tempName; }
        }

        public string Title
        {
            get { return IsDirty ? Name + "*" : Name; }
        }

        public bool IsDirty
        {
            get { return isDirty; }
            private set
            {
                if (isDirty == value)
                    return;
                isDirty = value;
                RaisePropertyChanged("IsDirty");
                RaisePropertyChanged("Title");
            }
        }

        /// <summary>True when the document could not be parsed at all - see <see cref="LoadErrors"/>.</summary>
        public bool LoadFailed { get; private set; }

        /// <summary>
        /// Errors the parser reported during the last load, fatal or not.
        /// </summary>
        /// <remarks>
        /// Captured through XamlLoadSettings.ReportErrors rather than read from
        /// DesignContext.Services afterwards. That matters: XamlDesignContext calls ReportErrors
        /// on the line before it throws XamlLoadException for a document that is not well-formed
        /// XML, and DesignSurface.LoadDesigner has already unloaded the previous context by then,
        /// so there is no context left to read the error service from. Spec Decision 16.
        /// </remarks>
        public IReadOnlyList<XamlError> LoadErrors
        {
            get { return loadErrors.AsReadOnly(); }
        }

        /// <summary>The live design surface this document owns.</summary>
        /// <remarks>
        /// Callers must not call <c>LoadDesigner</c> on it directly. Every load installs a fresh
        /// UndoService, and this class caches the one it subscribed to in order to unsubscribe
        /// later; a load it did not drive strands that cached service on the old context, so the
        /// document keeps listening to a dead undo stack and silently stops tracking dirty state
        /// on the live one. Go through the constructor - or Dispose and build a new Document.
        /// </remarks>
        public DesignSurface DesignSurface
        {
            get { return designSurface; }
        }

        public DesignContext DesignContext
        {
            get { return designSurface.DesignContext; }
        }

        public UndoService UndoService
        {
            get { return DesignContext == null ? null : DesignContext.Services.GetService<UndoService>(); }
        }

        public ISelectionService SelectionService
        {
            get { return DesignContext == null ? null : DesignContext.Services.Selection; }
        }

        public IOutlineNode OutlineRoot { get; private set; }

        /// <summary>Serializes the live design tree back into <see cref="Text"/>.</summary>
        public void UpdateXaml()
        {
            if (DesignContext == null)
                return;

            var sb = new StringBuilder();
            using (var writer = new XamlXmlWriter(sb))
                designSurface.SaveDesigner(writer);

            text = XamlFormatter.Format(sb.ToString());
            RaisePropertyChanged("Text");
        }

        /// <summary>Clears the dirty flag. Called by ShellModel once the text has reached disk.</summary>
        public void MarkSaved()
        {
            IsDirty = false;
        }

        /// <summary>
        /// Marks the document dirty-but-suspect after an unhandled exception. Spec Decision 18.
        /// </summary>
        /// <remarks>
        /// The handler that catches an unhandled exception keeps the process alive, which means
        /// the design tree survives in whatever half-mutated state the failure left it. Forcing
        /// the dirty flag is what makes the close gate prompt instead of discarding silently:
        /// without it a document mutated by a behaviour that then threw looks clean, because
        /// IsDirty is otherwise only ever raised by a committed change reaching the undo stack.
        /// </remarks>
        public void MarkSuspect()
        {
            IsDirty = true;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            DetachUndo();
            OutlineRoot = null;
            designSurface.UnloadDesigner();
        }

        internal void SetTempName(string name)
        {
            tempName = name;
            RaisePropertyChanged("Name");
            RaisePropertyChanged("Title");
        }

        void UpdateDesign()
        {
            DetachUndo();
            OutlineRoot = null;
            loadErrors.Clear();
            LoadFailed = false;

            var settings = new XamlLoadSettings();

            // Deliberately NOT registering the toolbox's assemblies as DesignerAssemblies, even
            // though the reference implementation does. XamlDesignContext hands every entry to
            // ExtensionManager.RegisterAssembly, whose de-dupe set lives on the ExtensionManager
            // and so is per-DesignContext - meaning every document open would re-reflect over all
            // ~2400 types of Microsoft.WinUI hunting for [ExtensionFor] attributes that assembly
            // does not carry. Measured on the soak test: 85065.7 bytes per open/dispose cycle
            // with the loop, 73285.6 without, for no behavioural difference at all. The design-
            // time extensions all live in WinUIDesigner.Designer, which the engine registers
            // itself. Do not restore this from the reference.
            settings.TypeFinder = ShellTypeFinder.Instance;
            settings.ReportErrors = errorService =>
            {
                if (errorService != null)
                    loadErrors.AddRange(errorService.Errors);
            };

            try
            {
                using (var reader = XmlReader.Create(new StringReader(text)))
                    designSurface.LoadDesigner(reader, settings);
            }
            catch (XamlLoadException)
            {
                // The parser already reported through settings.ReportErrors, one line before the
                // throw. Swallowing the exception here is what lets ShellModel report the errors
                // instead of the process dying on a malformed file.
                LoadFailed = true;
                return;
            }

            if (DesignContext != null && DesignContext.RootItem != null)
            {
                OutlineRoot = DesignContext.RootItem.CreateOutlineNode();
                AttachUndo();
            }
        }

        void AttachUndo()
        {
            subscribedUndoService = UndoService;
            if (subscribedUndoService != null)
                subscribedUndoService.UndoStackChanged += OnUndoStackChanged;
        }

        void DetachUndo()
        {
            if (subscribedUndoService != null)
                subscribedUndoService.UndoStackChanged -= OnUndoStackChanged;
            subscribedUndoService = null;
        }

        void OnUndoStackChanged(object sender, EventArgs e)
        {
            IsDirty = true;
        }

        void RaisePropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
