using System;
using System.IO;
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
                Assert.That(reported, Is.Not.Null, "File.ReadAllText(null) throws " +
                    "ArgumentNullException, which the read must catch and report");
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

                // A NUL character is rejected by File.ReadAllText with ArgumentException
                // ("Null character in path"), verified empirically on this runtime: unlike
                // "<" or ">" - which surface as IOException here and so were already caught
                // by the narrower catch this test exists to replace - ArgumentException does
                // not derive from IOException or UnauthorizedAccessException, so it actually
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
    }
}
