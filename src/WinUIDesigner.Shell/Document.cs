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
            get { return loadErrors; }
        }

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

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            DetachUndo();
            OutlineRoot = null;
            designSurface.UnloadDesigner();
        }

        internal void Name_ForTest_SetTempName(string name)
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
            foreach (var node in Toolbox.Instance.AssemblyNodes)
                settings.DesignerAssemblies.Add(node.Assembly);
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
