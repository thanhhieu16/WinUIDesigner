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
    /// One open document: the text, the design surface built from it, the mode the user is
    /// viewing it in, and the dirty flag.
    /// </summary>
    /// <remarks>
    /// Ported from the reference's Document.cs with four deliberate deviations: Dispose
    /// unsubscribes and unloads (the reference does neither, and the soak test depends on both);
    /// the constructor does not assign FilePath through a side-effecting property; the file write
    /// lives in ShellModel, not here; and <see cref="Mode"/>'s setter has an equality guard the
    /// reference's does not, because this shell re-asserts the current mode from JumpToError and
    /// from its mode buttons, and an unguarded setter would re-serialize over unsaved typing.
    /// <para>
    /// IsDirty is raised by the undo stack and by a real assignment to <see cref="Text"/>. It is
    /// NOT raised by <see cref="UpdateXaml"/>, which writes the backing field directly because
    /// serializing the tree is not a user edit, nor by a change that only differs in line
    /// endings - WinUI's TextBox hands back lone CR where XamlFormatter emitted CRLF, measured.
    /// </para>
    /// </remarks>
    public sealed class Document : IDisposable, INotifyPropertyChanged
    {
        readonly DesignSurface designSurface = new DesignSurface();
        readonly List<XamlError> loadErrors = new List<XamlError>();

        string text;
        string parsedText;
        string filePath;
        string tempName;
        bool isDirty;
        bool disposed;
        DocumentMode mode;
        UndoService subscribedUndoService;

        /// <summary>
        /// Builds a document from <paramref name="text"/> and parses it immediately.
        /// </summary>
        /// <remarks>
        /// The text is normalised here rather than stored raw, and that is not cosmetic.
        /// <see cref="Mode"/>'s setter decides whether to re-serialize by comparing
        /// <c>text</c> against <see cref="ParsedText"/>, which <see cref="UpdateDesign"/> always
        /// stores normalised. File.ReadAllText hands back whatever line endings the file has, and
        /// LF-only XAML is ordinary - a checkout with core.autocrlf=false, a file authored on
        /// another platform, anything generated - so storing raw text made the two differ from
        /// the moment of construction, and every such document re-serialized on its very first
        /// switch into XAML mode. That is precisely the case the condition exists to spare.
        /// Line and column numbering is unaffected by which line ending is used, so LoadErrors'
        /// coordinates mean the same thing against either form.
        /// </remarks>
        public Document(string filePath, string text)
        {
            this.filePath = filePath;
            this.text = NormaliseLineEndings(text);
            UpdateDesign();
            isDirty = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// The document's XAML text. Assigning a value that differs by more than line endings is
        /// a user edit and marks the document dirty; <see cref="UpdateXaml"/> bypasses this
        /// setter deliberately.
        /// </summary>
        public string Text
        {
            get { return text; }
            set
            {
                var normalised = NormaliseLineEndings(value);
                if (text == normalised)
                    return;
                text = normalised;
                IsDirty = true;
                RaisePropertyChanged("Text");
            }
        }

        /// <summary>
        /// Rewrites every CR, LF and CRLF in <paramref name="value"/> as
        /// <see cref="Environment.NewLine"/>.
        /// </summary>
        /// <remarks>
        /// Measured: assigning "a\r\nb\r\nc" to a WinUI TextBox reads back as "a\rb\rc" - seven
        /// characters in, five out, and the two strings are not equal. XamlFormatter emits
        /// Environment.NewLine. Without this, merely opening a document's XAML view would mark it
        /// dirty and put lone-CR line endings on disk. Spec Decision 14.
        /// </remarks>
        internal static string NormaliseLineEndings(string value)
        {
            if (value == null)
                return null;
            if (value.IndexOf('\r') < 0 && value.IndexOf('\n') < 0)
                return value;

            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\r')
                {
                    sb.Append(Environment.NewLine);
                    if (i + 1 < value.Length && value[i + 1] == '\n')
                        i++;
                }
                else if (c == '\n')
                {
                    sb.Append(Environment.NewLine);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>Which view the user is looking at. Spec Decision 10.</summary>
        /// <remarks>
        /// Entering XAML mode re-serializes the tree only when the document has moved on from
        /// what <see cref="UpdateDesign"/> last parsed. An untouched document keeps its own text
        /// byte for byte, and that is load-bearing rather than a nicety: XamlParser.ReportException
        /// reports and continues whenever an error sink is present, so a file with an unknown
        /// property or a bad conversion parses successfully - LoadFailed false, LoadErrors
        /// non-empty - which is the only way the error strip fills on a freshly opened file, and
        /// those errors' Line/Column address the file's own text. Re-serializing on the way into
        /// XAML mode would destroy exactly that text before DocumentView.JumpToError could select
        /// in it, and JumpToError's staleness guard would then refuse every jump the strip offers.
        /// Both halves of the condition are needed: <c>text != parsedText</c> catches typing that
        /// already reached <see cref="Text"/>, and <see cref="IsDirty"/> catches a surface edit,
        /// which moves the tree without touching <c>text</c> at all. Spec Decision 10.
        /// </remarks>
        public DocumentMode Mode
        {
            get { return mode; }
            set
            {
                if (mode == value)
                    return;
                mode = value;

                if (mode == DocumentMode.Design)
                    UpdateDesign();
                else if (IsDirty || text != parsedText)
                    UpdateXaml();

                RaisePropertyChanged("Mode");
                RaisePropertyChanged("InXamlMode");
                RaisePropertyChanged("InDesignMode");
                RaisePropertyChanged("SelectionService");
            }
        }

        public bool InXamlMode
        {
            get { return mode == DocumentMode.Xaml; }
        }

        public bool InDesignMode
        {
            get { return mode == DocumentMode.Design; }
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
        /// so there is no context left to read the error service from. Phase 5 Decision 16.
        /// </remarks>
        public IReadOnlyList<XamlError> LoadErrors
        {
            get { return loadErrors.AsReadOnly(); }
        }

        /// <summary>
        /// The text <see cref="UpdateDesign"/> last handed to the parser, normalised, or null
        /// before the first parse.
        /// </summary>
        /// <remarks>
        /// The only thing <see cref="LoadErrors"/>' Line/Column coordinates mean anything against.
        /// <see cref="UpdateXaml"/> replaces <see cref="Text"/> with a re-serialization of the
        /// parsed tree - re-indented and attributes re-sorted - and it does that without
        /// reparsing, so once it has run the buffer and these coordinates describe different
        /// documents. <see cref="Mode"/>'s setter no longer runs it for a document that has not
        /// moved on from what was parsed, which is what lets a jump into a freshly opened file
        /// land; a save from Design mode and every edit still put the two out of step, so the
        /// comparison remains necessary. Stored normalised so a caller can compare it against
        /// <see cref="NormaliseLineEndings"/> of a TextBox's text, which is the only text a
        /// selection can be made in. Line and column numbering is unaffected by which line
        /// ending is used, so normalising loses nothing the coordinates depend on.
        /// </remarks>
        internal string ParsedText
        {
            get { return parsedText; }
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

        /// <summary>
        /// The live selection service, or null while in XAML mode.
        /// </summary>
        /// <remarks>
        /// Null in XAML mode on purpose, ported from the reference (XamlDesigner/Document.cs:159-166).
        /// The text is the source of truth there, so a property-grid or outline edit that reached
        /// the tree would be thrown away by the next switch back to Design. Spec Decision 15.
        /// </remarks>
        public ISelectionService SelectionService
        {
            get
            {
                if (InXamlMode)
                    return null;
                return DesignContext == null ? null : DesignContext.Services.Selection;
            }
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

        /// <summary>
        /// Rebuilds the design surface from the document's XAML: the reference's Refresh (F5),
        /// UpdateXaml then UpdateDesign.
        /// </summary>
        /// <remarks>
        /// In Design mode the tree is serialized first only when it has moved on from what was
        /// last parsed - the same condition the Mode setter uses, so refreshing an untouched file
        /// keeps its own text byte for byte. In XAML mode the text is the source of truth and is
        /// never overwritten: the refresh re-parses what the user has typed, which refreshes the
        /// error strip without leaving the editor. Either way the result is a new DesignContext,
        /// so the undo history does not survive a refresh, as it does not survive a mode round
        /// trip (Decision 17), and listeners are told through PropertyChanged("DesignContext").
        /// The dirty flag is left alone: a refresh changes no content.
        /// </remarks>
        public void Refresh()
        {
            if (InDesignMode && (IsDirty || text != parsedText))
                UpdateXaml();
            UpdateDesign();
            RaisePropertyChanged("DesignContext");
            RaisePropertyChanged("SelectionService");
        }

        /// <summary>Clears the dirty flag. Called by ShellModel once the text has reached disk.</summary>
        public void MarkSaved()
        {
            IsDirty = false;
        }

        /// <summary>
        /// Marks the document dirty-but-suspect after an unhandled exception. Phase 5 Decision 18.
        /// </summary>
        /// <remarks>
        /// The handler that catches an unhandled exception keeps the process alive, which means
        /// the design tree survives in whatever half-mutated state the failure left it. Forcing
        /// the dirty flag is what makes the close gate prompt instead of discarding silently:
        /// without it a document mutated by a behaviour that then threw looks clean, because
        /// IsDirty is otherwise only ever raised by a committed change reaching the undo stack or
        /// by an assignment to <see cref="Text"/> - and a behaviour that threw part-way did
        /// neither.
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

            // Recorded before the parse, not after, so it is right on both exits: the errors
            // reported below carry coordinates into exactly this text and nothing else.
            parsedText = NormaliseLineEndings(text);

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

        /// <summary>
        /// Raised after an edit, undo or redo reaches the design tree. A rebuilt tree (a mode
        /// switch or Refresh) is announced through PropertyChanged("Mode"/"DesignContext")
        /// instead. Thumbnail spec Decision 3.
        /// </summary>
        public event EventHandler DesignChanged;

        void OnUndoStackChanged(object sender, EventArgs e)
        {
            IsDirty = true;
            var designChanged = DesignChanged;
            if (designChanged != null)
                designChanged(this, EventArgs.Empty);

            // A change that reaches the tree while the user is looking at the text must be
            // serialized into that text, or the next switch to Design reloads from stale text and
            // discards it. Ported from the reference (XamlDesigner/Document.cs:257-263).
            // Spec Decision 15.
            if (InXamlMode)
                UpdateXaml();
        }

        void RaisePropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
