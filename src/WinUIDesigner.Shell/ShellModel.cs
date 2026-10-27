using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using WinUIDesigner.DesignModel.Xaml;

namespace WinUIDesigner.Shell
{
    public enum SaveChangesResult
    {
        Save,
        Discard,
        Cancel
    }

    /// <summary>Asks the UI what to do about a document with unsaved changes.</summary>
    public sealed class AskSaveChangesEventArgs : EventArgs
    {
        public Document Document { get; set; }

        /// <summary>Defaults to Cancel, so an unhandled event is the safe answer.</summary>
        public SaveChangesResult Result { get; set; }
    }

    /// <summary>Carries a failure to the UI. Never thrown out of ShellModel.</summary>
    public sealed class ShellErrorEventArgs : EventArgs
    {
        public string Message { get; set; }

        /// <summary>Parser errors when the failure came from loading a document; otherwise null.</summary>
        public IReadOnlyList<XamlError> Errors { get; set; }
    }

    /// <summary>
    /// Document lifetime and file I/O.
    /// </summary>
    /// <remarks>
    /// Ported from the reference's Shell.cs, with the dialogs removed. The reference calls
    /// MessageBox.Show inline; WinUI's replacement, ContentDialog, is asynchronous and needs an
    /// XamlRoot, which would drag a live window into every test. This class raises
    /// <see cref="AskSaveChanges"/> and <see cref="ReportError"/> instead, and MainWindow answers
    /// them. Phase 5 Decisions 6 and 7.
    /// <para>
    /// No method here throws. Phase 5 Decision 17: a throw escaping into the engine's transaction
    /// machinery strands a ChangeGroup, and there is no handler that would make that survivable.
    /// </para>
    /// </remarks>
    public sealed class ShellModel : INotifyPropertyChanged
    {
        static int nonameIndex = 1;

        Document currentDocument;
        readonly RecentFileList recentFiles;

        public ShellModel()
            : this(null)
        {
        }

        /// <summary>
        /// A model that adds each file it opens, or saves under a new name, to
        /// <paramref name="recentFiles"/>. Null keeps no list. Recent-files spec Decision 3.
        /// </summary>
        public ShellModel(RecentFileList recentFiles)
            : this(recentFiles, null)
        {
        }

        /// <summary>
        /// A model that also remembers the assemblies it adds to the toolbox in the settings file
        /// at <paramref name="settingsPath"/>. Null remembers nothing. Toolbox-assemblies spec
        /// Decision 5.
        /// </summary>
        public ShellModel(RecentFileList recentFiles, string settingsPath)
        {
            this.recentFiles = recentFiles;
            this.settingsPath = settingsPath;
            Documents = new ObservableCollection<Document>();
        }

        readonly string settingsPath;

        /// <summary>How many skipped control names a report lists before summarizing the rest.</summary>
        const int SkippedListed = 10;

        /// <summary>
        /// Loads the assembly at <paramref name="path"/> and adds its controls to the toolbox.
        /// Reports, rather than throws, a file that is not a loadable assembly, one with no
        /// controls, one already in the toolbox, and the controls that could not be created.
        /// <paramref name="remember"/> records the path for the next start; the startup load
        /// itself passes false, and is quiet about an assembly that is already present.
        /// Returns whether the assembly's controls are in the toolbox. Toolbox-assemblies spec
        /// Decisions 1, 2 and 5.
        /// </summary>
        public bool AddAssembly(string path, bool remember)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                RaiseError(ex.Message, null);
                return false;
            }

            string error;
            var assembly = Toolbox.LoadAssembly(fullPath, out error);
            if (assembly == null)
            {
                RaiseError("Could not add " + Path.GetFileName(fullPath) + " to the toolbox: " + error, null);
                return false;
            }

            AddAssemblyResult result;
            try
            {
                result = Toolbox.Instance.AddAssembly(assembly);
            }
            catch (Exception ex)
            {
                // GetExportedTypes and the activation probes run the library's own code.
                RaiseError("Could not add " + Path.GetFileName(fullPath) + " to the toolbox: " + ex.Message, null);
                return false;
            }

            var name = assembly.GetName().Name;
            if (result.AlreadyPresent)
            {
                if (remember)
                    RaiseError(name + " is already in the toolbox.", null);
            }
            else if (result.Added == null)
            {
                RaiseError(name + " contains no controls that can be placed." + SkippedText(result), null);
                return false;
            }
            else if (result.Skipped.Count > 0)
            {
                RaiseError(name + " was added to the toolbox." + SkippedText(result), null);
            }

            if (remember && settingsPath != null)
            {
                ShellSettings.Update(settingsPath, settings =>
                {
                    if (!settings.ToolboxAssemblies.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                        settings.ToolboxAssemblies.Add(fullPath);
                });
            }
            return true;
        }

        static string SkippedText(AddAssemblyResult result)
        {
            if (result.Skipped.Count == 0)
                return string.Empty;
            var text = new System.Text.StringBuilder();
            text.AppendLine();
            text.AppendLine(result.Skipped.Count == 1
                ? "This control could not be created and was left out:"
                : "These controls could not be created and were left out:");
            foreach (var skipped in result.Skipped.Take(SkippedListed))
                text.AppendLine(skipped);
            if (result.Skipped.Count > SkippedListed)
                text.AppendLine("... and " + (result.Skipped.Count - SkippedListed) + " more.");
            return text.ToString().TrimEnd();
        }

        /// <summary>The list this model records into, or null.</summary>
        public RecentFileList RecentFiles
        {
            get { return recentFiles; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<AskSaveChangesEventArgs> AskSaveChanges;
        public event EventHandler<ShellErrorEventArgs> ReportError;

        public ObservableCollection<Document> Documents { get; private set; }

        public Document CurrentDocument
        {
            get { return currentDocument; }
            set
            {
                if (currentDocument == value)
                    return;
                currentDocument = value;
                RaisePropertyChanged("CurrentDocument");
            }
        }

        public bool IsSomethingDirty
        {
            get
            {
                foreach (var doc in Documents)
                    if (doc.IsDirty)
                        return true;
                return false;
            }
        }

        public Document New()
        {
            var doc = new Document(null, NewDocumentTemplate.Xaml);
            doc.SetTempName("New" + nonameIndex++);
            if (doc.LoadFailed)
            {
                // The template is a constant in this assembly, so this means the engine broke,
                // not the user's input. Report it rather than opening a broken document.
                RaiseError("Could not create a new document.", doc.LoadErrors);
                doc.Dispose();
                return null;
            }
            Add(doc);
            return doc;
        }

        public bool Open(string path)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                // Path.GetFullPath throws for a null, empty, or malformed path. No method on
                // ShellModel may throw - Phase 5 Decision 17 - so report and refuse.
                RaiseError(ex.Message, null);
                return false;
            }

            // A path already open becomes current rather than opening a second live tree over the
            // same file, which is what the reference does (XamlDesigner/Shell.cs:141-146). Without
            // tabs this could not arise; with them, two copies means saving from either silently
            // clobbers the other. Spec Decision 6.
            var alreadyOpen = FindOpen(fullPath);
            if (alreadyOpen != null)
            {
                CurrentDocument = alreadyOpen;
                AddRecent(fullPath);
                return true;
            }

            string text;
            try
            {
                text = File.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                // Broad on purpose: File.ReadAllText can throw ArgumentNullException,
                // ArgumentException, NotSupportedException, PathTooLongException and more,
                // none of which derive from a common I/O base.
                RaiseError(ex.Message, null);
                return false;
            }

            var doc = new Document(fullPath, text);

            if (doc.LoadFailed)
            {
                RaiseError("Could not load " + path, doc.LoadErrors);
                doc.Dispose();
                return false;
            }

            if (doc.LoadErrors.Count > 0)
                RaiseError("The document loaded with errors.", doc.LoadErrors);

            Add(doc);
            AddRecent(fullPath);
            return true;
        }

        public bool Save(Document doc)
        {
            if (doc == null)
                return false;
            if (doc.FilePath == null)
            {
                // SaveAs needs a path from the UI; the caller is expected to have asked already.
                RaiseError("This document has no file path yet.", null);
                return false;
            }
            return WriteToDisk(doc, doc.FilePath);
        }

        public bool SaveAs(Document doc, string path)
        {
            if (doc == null || path == null)
                return false;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                RaiseError(ex.Message, null);
                return false;
            }

            // The other half of Open's dedupe: saving over a file another tab holds would leave
            // two live trees over one path, and the next save from either clobbers the other.
            // Refused before anything is written, so the other tab's file stays as it was.
            var holder = FindOpen(fullPath);
            if (holder != null && holder != doc)
            {
                RaiseError(Path.GetFileName(fullPath) + " is open in another tab. Close it there first, or save under another name.", null);
                return false;
            }

            if (!WriteToDisk(doc, fullPath))
                return false;
            doc.FilePath = fullPath;
            AddRecent(fullPath);
            return true;
        }

        void AddRecent(string fullPath)
        {
            if (recentFiles != null)
                recentFiles.Add(fullPath);
        }

        Document FindOpen(string fullPath)
        {
            foreach (var doc in Documents)
            {
                if (doc.FilePath != null
                    && string.Equals(doc.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                    return doc;
            }
            return null;
        }

        public bool Close(Document doc)
        {
            if (doc == null)
                return false;

            if (doc.IsDirty)
            {
                var args = new AskSaveChangesEventArgs { Document = doc, Result = SaveChangesResult.Cancel };
                var handler = AskSaveChanges;
                if (handler != null)
                    handler(this, args);

                if (args.Result == SaveChangesResult.Cancel)
                    return false;
                if (args.Result == SaveChangesResult.Save && !Save(doc))
                    return false;
            }

            Documents.Remove(doc);
            if (CurrentDocument == doc)
                CurrentDocument = Documents.Count > 0 ? Documents[0] : null;
            doc.Dispose();
            return true;
        }

        bool WriteToDisk(Document doc, string path)
        {
            try
            {
                // UpdateXaml drives DesignSurface.SaveDesigner through XamlXmlWriter - engine
                // serialization code that is not proven exception-free - so it lives inside the
                // same try as the write. Broad catch for the same reason as Open: no method on
                // ShellModel may throw (Phase 5 Decision 17), and File.WriteAllText's own exception
                // set (ArgumentException, NotSupportedException, PathTooLongException, ...)
                // does not share a common base with IOException/UnauthorizedAccessException.
                // Only when the tree is the source of truth. In XAML mode the user's typing is,
                // and re-serializing here would overwrite it with the tree they typed it instead
                // of. The reference guards the same call the same way
                // (XamlDesigner/Document.cs:196-203). Spec Decision 12.
                if (doc.InDesignMode)
                    doc.UpdateXaml();
                File.WriteAllText(path, doc.Text);
            }
            catch (Exception ex)
            {
                RaiseError(ex.Message, null);
                return false;
            }
            doc.MarkSaved();
            return true;
        }

        void Add(Document doc)
        {
            Documents.Add(doc);
            CurrentDocument = doc;
        }

        void RaiseError(string message, IReadOnlyList<XamlError> errors)
        {
            var handler = ReportError;
            if (handler != null)
                handler(this, new ShellErrorEventArgs { Message = message, Errors = errors });
        }

        void RaisePropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
