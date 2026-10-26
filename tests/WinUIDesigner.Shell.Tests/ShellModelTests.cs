using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class ShellModelTests
    {
        const string GoodXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"100\" />";

        // Not well-formed XML: the parser reports through the sink and returns null, and
        // XamlDesignContext then throws XamlLoadException. The fatal path.
        const string MalformedXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Button></Canvas>";

        // Well-formed, but names a type that does not exist. The non-fatal path: the document
        // loads and the error is reported.
        const string UnknownTypeXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"100\">" +
            "<NoSuchControl />" +
            "</Canvas>";

        static string WriteTemp(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), "shelltest-" + Guid.NewGuid().ToString("N") + ".xaml");
            File.WriteAllText(path, content);
            return path;
        }

        [Test]
        public void NewProducesALoadableBlankCanvas()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var doc = shell.New();

                Assert.That(doc, Is.Not.Null);
                Assert.That(doc.LoadFailed, Is.False);
                var canvas = doc.DesignContext.RootItem.Component as Canvas;
                Assert.That(canvas, Is.Not.Null);
                Assert.That(canvas.Width, Is.EqualTo(640.0));
                Assert.That(canvas.Height, Is.EqualTo(480.0));
                Assert.That(shell.Documents, Has.Count.EqualTo(1));
                Assert.That(shell.CurrentDocument, Is.SameAs(doc));

                shell.Close(doc);
            });
        }

        [Test]
        public void OpeningAMissingFileReportsAndChangesNothing()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                ShellErrorEventArgs reported = null;
                shell.ReportError += (s, e) => reported = e;

                var missing = Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".xaml");
                bool opened = false;
                Assert.DoesNotThrow(() => opened = shell.Open(missing));

                Assert.That(opened, Is.False);
                Assert.That(reported, Is.Not.Null, "the failure must be reported, not thrown");
                Assert.That(shell.Documents, Is.Empty);
                Assert.That(shell.CurrentDocument, Is.Null);
            });
        }

        [Test]
        public void AFatalParseErrorReportsTheParsersOwnMessageAndOpensNothing()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                ShellErrorEventArgs reported = null;
                shell.ReportError += (s, e) => reported = e;

                var path = WriteTemp(MalformedXaml);
                try
                {
                    bool opened = false;
                    Assert.DoesNotThrow(() => opened = shell.Open(path));

                    Assert.That(opened, Is.False);
                    Assert.That(reported, Is.Not.Null);
                    Assert.That(reported.Errors, Is.Not.Null.And.Not.Empty,
                        "the errors must arrive through the ReportErrors callback - after a " +
                        "XamlLoadException there is no DesignContext left to read them from");
                    Assert.That(shell.Documents, Is.Empty);
                }
                finally
                {
                    File.Delete(path);
                }
            });
        }

        [Test]
        public void OpeningANullPathReportsRatherThanThrowing()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                ShellErrorEventArgs reported = null;
                shell.ReportError += (s, e) => reported = e;

                bool opened = true;
                Assert.DoesNotThrow(() => opened = shell.Open(null));

                Assert.That(opened, Is.False);
                Assert.That(reported, Is.Not.Null, "Path.GetFullPath(null) throws " +
                    "ArgumentNullException, which Open's own try/catch must catch and report");
                Assert.That(shell.Documents, Is.Empty);
            });
        }

        [Test]
        public void OpeningAPathWithInvalidCharactersReportsRatherThanThrowing()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                ShellErrorEventArgs reported = null;
                shell.ReportError += (s, e) => reported = e;

                // A NUL character is rejected by Path.GetFullPath with ArgumentException
                // ("Null character in path"), verified empirically on this runtime: unlike
                // "<" or ">" - which Path.GetFullPath lets through unchanged, only to surface
                // as IOException from File.ReadAllText and so were already caught by the
                // narrower catch this test exists to replace - ArgumentException does not
                // derive from IOException or UnauthorizedAccessException, so it actually
                // escaped the old two-catch code.
                var path = "a\0b.xaml";
                bool opened = true;
                Assert.DoesNotThrow(() => opened = shell.Open(path));

                Assert.That(opened, Is.False);
                Assert.That(reported, Is.Not.Null);
                Assert.That(shell.Documents, Is.Empty);
            });
        }

        [Test]
        public void ANonFatalParseErrorOpensTheDocumentAndStillReports()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                ShellErrorEventArgs reported = null;
                shell.ReportError += (s, e) => reported = e;

                var path = WriteTemp(UnknownTypeXaml);
                try
                {
                    bool opened = shell.Open(path);

                    Assert.That(opened, Is.True, "a well-formed document opens even with errors in it");
                    Assert.That(shell.Documents, Has.Count.EqualTo(1));
                    Assert.That(reported, Is.Not.Null);
                    Assert.That(reported.Errors, Is.Not.Null.And.Not.Empty);

                    shell.Close(shell.CurrentDocument);
                }
                finally
                {
                    File.Delete(path);
                }
            });
        }

        [Test]
        public void SaveWritesTheSerializedTreeAndClearsDirty()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var path = WriteTemp(GoodXaml);
                try
                {
                    Assert.That(shell.Open(path), Is.True);
                    var doc = shell.CurrentDocument;

                    doc.DesignContext.RootItem.Properties["Width"].SetValue(321.0);
                    Assert.That(doc.IsDirty, Is.True);

                    Assert.That(shell.Save(doc), Is.True);
                    Assert.That(doc.IsDirty, Is.False);
                    Assert.That(File.ReadAllText(path), Does.Contain("321"));

                    shell.Close(doc);
                }
                finally
                {
                    File.Delete(path);
                }
            });
        }

        [Test]
        public void ClosingADirtyDocumentAsksAndCancelKeepsItOpen()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var doc = shell.New();
                doc.DesignContext.RootItem.Properties["Width"].SetValue(123.0);
                Assert.That(doc.IsDirty, Is.True);

                // One handler reading a variable, not two handlers racing. Registering a second
                // handler with += and relying on it to overwrite the first's answer makes the
                // test depend on multicast invocation order, which is an implementation detail of
                // the delegate rather than anything ShellModel promises.
                int asked = 0;
                var answer = SaveChangesResult.Cancel;
                shell.AskSaveChanges += (s, e) => { asked++; e.Result = answer; };

                Assert.That(shell.Close(doc), Is.False);
                Assert.That(asked, Is.EqualTo(1));
                Assert.That(shell.Documents, Has.Count.EqualTo(1), "Cancel keeps the document open");

                answer = SaveChangesResult.Discard;
                Assert.That(shell.Close(doc), Is.True);
                Assert.That(asked, Is.EqualTo(2));
                Assert.That(shell.Documents, Is.Empty);
            });
        }

        [Test]
        public void DiscardClosesWithoutWritingToDisk()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var path = WriteTemp(GoodXaml);
                var before = File.ReadAllText(path);
                try
                {
                    Assert.That(shell.Open(path), Is.True);
                    var doc = shell.CurrentDocument;
                    doc.DesignContext.RootItem.Properties["Width"].SetValue(999.0);

                    shell.AskSaveChanges += (s, e) => e.Result = SaveChangesResult.Discard;
                    Assert.That(shell.Close(doc), Is.True);

                    Assert.That(shell.Documents, Is.Empty);
                    Assert.That(File.ReadAllText(path), Is.EqualTo(before).Using((System.Collections.Generic.IComparer<string>)StringComparer.Ordinal));
                }
                finally
                {
                    File.Delete(path);
                }
            });
        }

        [Test]
        public void ClosingACleanDocumentDoesNotAsk()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var doc = shell.New();

                int asked = 0;
                shell.AskSaveChanges += (s, e) => { asked++; e.Result = SaveChangesResult.Cancel; };

                Assert.That(shell.Close(doc), Is.True);
                Assert.That(asked, Is.EqualTo(0));
                Assert.That(shell.Documents, Is.Empty);
            });
        }

        [Test]
        public void SavingInXamlModeWritesWhatTheUserTypedNotAReSerialization()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "wd-xamlmode-" + System.Guid.NewGuid().ToString("N") + ".xaml");
                try
                {
                    var doc = shell.New();
                    Assert.That(doc, Is.Not.Null);

                    doc.Mode = DocumentMode.Xaml;
                    doc.Text = doc.Text + System.Environment.NewLine + "<!-- typed by hand -->";

                    Assert.That(shell.SaveAs(doc, path), Is.True);

                    var onDisk = System.IO.File.ReadAllText(path);
                    StringAssert.Contains("typed by hand", onDisk,
                        "WriteToDisk used to call UpdateXaml unconditionally, which replaced the " +
                        "user's typing with a serialization of the tree they typed it instead of");
                }
                finally
                {
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                }
            });
        }

        [Test]
        public void SavingInDesignModeStillSerializesTheLiveTree()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "wd-designmode-" + System.Guid.NewGuid().ToString("N") + ".xaml");
                try
                {
                    var doc = shell.New();
                    var root = doc.DesignContext.RootItem;
                    root.Properties["Width"].SetValue(777.0);

                    Assert.That(shell.SaveAs(doc, path), Is.True);

                    StringAssert.Contains("777", System.IO.File.ReadAllText(path),
                        "in design mode the tree is still the source of truth");
                }
                finally
                {
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                }
            });
        }

        [Test]
        public void OpeningAPathThatIsAlreadyOpenMakesItCurrentInsteadOfAddingASecondCopy()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "wd-dedupe-" + System.Guid.NewGuid().ToString("N") + ".xaml");
                try
                {
                    System.IO.File.WriteAllText(path, NewDocumentTemplate.Xaml);

                    Assert.That(shell.Open(path), Is.True);
                    Assert.That(shell.Documents.Count, Is.EqualTo(1));
                    var first = shell.Documents[0];

                    var other = shell.New();
                    Assert.That(shell.CurrentDocument, Is.SameAs(other));

                    Assert.That(shell.Open(path), Is.True);
                    Assert.That(shell.Documents.Count, Is.EqualTo(2),
                        "two live trees over one file means saving from either clobbers the other");
                    Assert.That(shell.CurrentDocument, Is.SameAs(first));
                }
                finally
                {
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                }
            });
        }

        [Test]
        public void SavingAsAPathAnotherTabHoldsIsRefusedAndLeavesThatFileAlone()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                string reported = null;
                shell.ReportError += (s, e) => reported = e.Message;
                var path = WriteTemp(GoodXaml);
                try
                {
                    Assert.That(shell.Open(path), Is.True);
                    var holder = shell.CurrentDocument;
                    var other = shell.New();

                    Assert.That(shell.SaveAs(other, path), Is.False);
                    Assert.That(reported, Does.Contain("open in another tab"));
                    Assert.That(other.FilePath, Is.Null);
                    Assert.That(File.ReadAllText(path), Is.EqualTo(GoodXaml),
                        "the refused save must not have written over the other tab's file");

                    // Saving a document as its own path is an ordinary save.
                    reported = null;
                    Assert.That(shell.SaveAs(holder, path), Is.True);
                    Assert.That(reported, Is.Null);

                    shell.Close(other);
                    shell.Close(holder);
                }
                finally
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
            });
        }
            [Test]
        public void OpenAndSaveAsRecordTheFileAndAFailedOpenDoesNot()
        {
            WinUITestHost.Run(() =>
            {
                var store = Path.Combine(Path.GetTempPath(), "wd-recent-" + Guid.NewGuid().ToString("N") + ".json");
                var recent = new RecentFileList(store);
                var shell = new ShellModel(recent);
                var opened = WriteTemp(GoodXaml);
                var savedAs = Path.Combine(Path.GetTempPath(), "wd-saveas-" + Guid.NewGuid().ToString("N") + ".xaml");
                try
                {
                    Assert.That(shell.Open(opened), Is.True);
                    Assert.That(recent.Items, Is.EqualTo(new[] { opened }));

                    Assert.That(shell.Open(opened + ".missing"), Is.False);
                    Assert.That(recent.Items, Is.EqualTo(new[] { opened }), "a failed open adds nothing");

                    var doc = shell.New();
                    Assert.That(shell.SaveAs(doc, savedAs), Is.True);
                    Assert.That(recent.Items, Is.EqualTo(new[] { savedAs, opened }));

                    // A plain save keeps the path it already has, which is already listed.
                    Assert.That(shell.Save(shell.Documents[0]), Is.True);
                    Assert.That(recent.Items, Is.EqualTo(new[] { savedAs, opened }));

                    // Opening an already-open file moves it up, though it only switches tabs.
                    Assert.That(shell.Open(opened), Is.True);
                    Assert.That(recent.Items, Is.EqualTo(new[] { opened, savedAs }));

                    foreach (var d in shell.Documents.ToList())
                        shell.Close(d);
                }
                finally
                {
                    foreach (var path in new[] { store, opened, savedAs })
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                }
            });
        }
    }
}
