using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
    /// them. Spec Decisions 6 and 7.
    /// <para>
    /// No method here throws. Spec Decision 17: a throw escaping into the engine's transaction
    /// machinery strands a ChangeGroup, and there is no handler that would make that survivable.
    /// </para>
    /// </remarks>
    public sealed class ShellModel : INotifyPropertyChanged
    {
        static int nonameIndex = 1;

        Document currentDocument;

        public ShellModel()
        {
            Documents = new ObservableCollection<Document>();
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
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                // Broad on purpose: File.ReadAllText can throw ArgumentNullException,
                // ArgumentException, NotSupportedException, PathTooLongException and more,
                // none of which derive from a common I/O base. No method on ShellModel may
                // throw - Spec Decision 17 - so every failure here is reported, not thrown.
                RaiseError(ex.Message, null);
                return false;
            }

            var doc = new Document(Path.GetFullPath(path), text);

            if (doc.LoadFailed)
            {
                RaiseError("Could not load " + path, doc.LoadErrors);
                doc.Dispose();
                return false;
            }

            if (doc.LoadErrors.Count > 0)
                RaiseError("The document loaded with errors.", doc.LoadErrors);

            Add(doc);
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
            if (!WriteToDisk(doc, path))
                return false;
            doc.FilePath = Path.GetFullPath(path);
            return true;
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

        /// <summary>Closes every document, honouring a Cancel. Returns false if the user cancelled.</summary>
        public bool PrepareExit()
        {
            var snapshot = new List<Document>(Documents);
            foreach (var doc in snapshot)
                if (!Close(doc))
                    return false;
            return true;
        }

        bool WriteToDisk(Document doc, string path)
        {
            try
            {
                // UpdateXaml drives DesignSurface.SaveDesigner through XamlXmlWriter - engine
                // serialization code that is not proven exception-free - so it lives inside the
                // same try as the write. Broad catch for the same reason as Open: no method on
                // ShellModel may throw (Spec Decision 17), and File.WriteAllText's own exception
                // set (ArgumentException, NotSupportedException, PathTooLongException, ...)
                // does not share a common base with IOException/UnauthorizedAccessException.
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
