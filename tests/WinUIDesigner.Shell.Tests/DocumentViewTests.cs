using System;
using Microsoft.UI.Xaml;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// Drives <see cref="DocumentView"/> at the property level rather than through layout.
    /// </summary>
    /// <remarks>
    /// Measured: a bare TextBox as the root content of the shared test window throws a
    /// COMException whose Message and every Data entry are empty, so nothing can be seeded
    /// around it. A TextBox inside the laid-out shell tree does work - MainWindowInteractionTests
    /// drives the outline filter today - but nothing here needs layout to be meaningful.
    /// </remarks>
    [TestFixture]
    public class DocumentViewTests
    {
        const string CanvasWithButton =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"640\" Height=\"480\">" +
            "<Button x:Name=\"Ok\" Width=\"75\" Height=\"32\" Content=\"Ok\" />" +
            "</Canvas>";

        /// <summary>
        /// Parses successfully and still reports one error, because XamlParser.ReportException
        /// reports and continues when an error sink is present. Measured on this exact string:
        /// LoadFailed false, LoadErrors.Count 1, "Cannot set Width: nonsense is not a valid value
        /// for Double. (Parameter 'value')" at line 3, column 23 - the W of Width.
        /// </summary>
        const string RecoverableErrorXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\r\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n" +
            "  <Button x:Name=\"Ok\" Width=\"nonsense\" Content=\"Ok\" />\r\n" +
            "</Canvas>";

        [Test]
        public void TypingInTheEditorReachesTheDocument()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    var view = new DocumentView(doc);
                    doc.Mode = DocumentMode.Xaml;

                    view.Editor.Text = view.Editor.Text + "<!-- typed -->";
                    view.PushEditorTextToDocument();

                    StringAssert.Contains("typed", doc.Text);
                    Assert.That(doc.IsDirty, Is.True);
                    view.Detach();
                }
            });
        }

        [Test]
        public void OpeningTheXamlViewDoesNotDirtyACleanDocument()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    var view = new DocumentView(doc);
                    Assert.That(doc.IsDirty, Is.False, "constructing the view is not an edit");

                    doc.Mode = DocumentMode.Xaml;
                    view.PushEditorTextToDocument();

                    Assert.That(doc.IsDirty, Is.False,
                        "TextBox hands back lone CR where XamlFormatter emitted CRLF (measured); " +
                        "the round trip through the editor must not read as a user edit. TextChanged " +
                        "does not fire for a programmatic assignment in a test process (measured), so " +
                        "the explicit push above is the only way to exercise Document.Text's setter here.");
                    Assert.That(view.Editor.Text, Is.Not.Empty);
                    view.Detach();
                }
            });
        }

        [Test]
        public void AProgrammaticTextChangeReachesTheEditor()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    var view = new DocumentView(doc);
                    doc.Mode = DocumentMode.Xaml;

                    doc.Text = "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />";

                    StringAssert.Contains("Canvas", view.Editor.Text);
                    Assert.That(view.Editor.Text.Contains("Button"), Is.False,
                        "the editor should be showing the new text, not the old");
                    view.Detach();
                }
            });
        }

        [Test]
        public void ExactlyOneOfTheSurfaceAndTheEditorIsVisiblePerMode()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    var view = new DocumentView(doc);

                    Assert.That(view.SurfaceVisibility, Is.EqualTo(Visibility.Visible));
                    Assert.That(view.Editor.Visibility, Is.EqualTo(Visibility.Collapsed));

                    doc.Mode = DocumentMode.Xaml;
                    Assert.That(view.SurfaceVisibility, Is.EqualTo(Visibility.Collapsed));
                    Assert.That(view.Editor.Visibility, Is.EqualTo(Visibility.Visible));

                    doc.Mode = DocumentMode.Design;
                    Assert.That(view.SurfaceVisibility, Is.EqualTo(Visibility.Visible));
                    Assert.That(view.Editor.Visibility, Is.EqualTo(Visibility.Collapsed));
                    view.Detach();
                }
            });
        }

        /// <summary>
        /// The first coverage of <c>JumpToError</c>'s own body - the <c>OffsetOf</c> and
        /// <c>TokenLength</c> tests below exercise its helpers, not it.
        /// </summary>
        /// <remarks>
        /// The refusing half is the important one. A document with an unknown property or a bad
        /// conversion parses successfully (XamlParser.ReportException reports and continues when
        /// an error sink is present), which is the only way the error strip fills on a freshly
        /// opened file - and for such a document the mode switch inside JumpToError re-serializes
        /// the tree over the buffer, so the reported line and column address text that no longer
        /// exists.
        /// </remarks>
        [Test]
        public void JumpToErrorSelectsTheReportedTokenOnlyWhileTheBufferIsStillWhatWasParsed()
        {
            WinUITestHost.Run(() =>
            {
                const string malformed =
                    "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";

                // A document that failed to parse keeps its buffer - UpdateXaml early-returns on a
                // null DesignContext - so the coordinates still address what the user is shown.
                using (var doc = new Document(null, malformed))
                {
                    Assert.That(doc.LoadFailed, Is.True);
                    var view = new DocumentView(doc);
                    Assert.That(Document.NormaliseLineEndings(view.Editor.Text), Is.EqualTo(doc.ParsedText));

                    view.JumpToError(new XamlError { Message = "unclosed", Line = 1, Column = 2 });

                    Assert.That(doc.InXamlMode, Is.True, "jumping to an error shows the text");
                    Assert.That(view.Editor.SelectionStart, Is.EqualTo(1));
                    Assert.That(view.Editor.SelectionLength, Is.EqualTo(6), "the token at 1,2 is Canvas");
                    view.Detach();
                }

                // A document that has been edited on the surface has its buffer replaced on the
                // way into XAML mode - Decision 10's condition is met - so the coordinates the
                // parser reported no longer address it.
                using (var doc = new Document(null, CanvasWithButton))
                {
                    var view = new DocumentView(doc);
                    doc.DesignContext.RootItem.ContentProperty.CollectionElements[0]
                        .Properties["Width"].SetValue(123.0);
                    doc.Mode = DocumentMode.Xaml;

                    Assert.That(Document.NormaliseLineEndings(view.Editor.Text), Is.Not.EqualTo(doc.ParsedText),
                        "UpdateXaml re-indents and re-sorts, so the buffer is no longer the text " +
                        "the reported coordinates were measured against");
                    Assert.That(DocumentView.OffsetOf(view.Editor.Text, 1, 2), Is.GreaterThanOrEqualTo(0),
                        "these coordinates are in range for this buffer, so a zero-length " +
                        "selection below can only come from the staleness guard");

                    view.JumpToError(new XamlError { Message = "unknown property", Line = 1, Column = 2 });

                    Assert.That(view.Editor.SelectionLength, Is.EqualTo(0),
                        "selecting at coordinates into text that no longer exists takes the user " +
                        "to an unrelated token; selecting nothing is the honest answer");
                    view.Detach();
                }
            });
        }

        /// <summary>
        /// The branch the error strip actually exercises: a freshly opened file that parsed with
        /// recoverable errors, jumped to from one of those errors.
        /// </summary>
        /// <remarks>
        /// This is the case the staleness guard used to refuse permanently. The strip only ever
        /// fills on open from a document with LoadFailed false and LoadErrors non-empty, and
        /// until Decision 10's condition landed, the mode switch inside JumpToError re-serialized
        /// the tree over the very text those errors' coordinates were measured against - so the
        /// guard fired every single time and the feature was a no-op for its whole audience.
        /// </remarks>
        [Test]
        public void JumpToErrorLandsOnARecoverableErrorInAFreshlyOpenedFile()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, RecoverableErrorXaml))
                {
                    Assert.That(doc.LoadFailed, Is.False, "this document parses");
                    Assert.That(doc.LoadErrors.Count, Is.EqualTo(1));
                    var error = doc.LoadErrors[0];

                    var view = new DocumentView(doc);
                    view.JumpToError(error);

                    Assert.That(doc.InXamlMode, Is.True, "jumping to an error shows the text");

                    // Asserted before the diagnosis below on purpose: this is the defect itself,
                    // so it is what a regression has to trip over first.
                    Assert.That(view.Editor.SelectionLength, Is.Not.EqualTo(0),
                        "an error the strip reports on a freshly opened file must be jumpable to");
                    Assert.That(Document.NormaliseLineEndings(view.Editor.Text), Is.EqualTo(doc.ParsedText),
                        "and it is jumpable only because the mode switch did not re-serialize " +
                        "over the file's own text - the reason a zero length above would be a " +
                        "staleness refusal rather than a bad offset");

                    var selected = view.Editor.Text.Substring(
                        view.Editor.SelectionStart, view.Editor.SelectionLength);
                    Assert.That(selected, Is.EqualTo("Width="),
                        "and it must land on the offending token, not merely somewhere. The "
                        + "trailing '=' is TokenLength's, ported as-is: its delimiter set is "
                        + "space, '.', '<', '>' and '\"', so a scan from the W stops at the "
                        + "opening quote of the value, not at the equals sign");
                    view.Detach();
                }
            });
        }

        [Test]
        public void OffsetOfCountsLinesAndColumnsAndRefusesWhatIsOutOfRange()
        {
            // Pure string arithmetic - no dispatcher needed.
            const string crlf = "one\r\ntwo\r\nthree";
            const string cr = "one\rtwo\rthree";

            Assert.That(DocumentView.OffsetOf(crlf, 1, 1), Is.EqualTo(0));
            Assert.That(DocumentView.OffsetOf(crlf, 2, 1), Is.EqualTo(5));
            Assert.That(DocumentView.OffsetOf(crlf, 3, 3), Is.EqualTo(12));

            // The same coordinates against the normalised form the editor actually holds.
            Assert.That(DocumentView.OffsetOf(cr, 2, 1), Is.EqualTo(4));
            Assert.That(DocumentView.OffsetOf(cr, 3, 3), Is.EqualTo(10));

            Assert.That(DocumentView.OffsetOf(crlf, 9, 1), Is.EqualTo(-1), "line past the end");
            Assert.That(DocumentView.OffsetOf(crlf, 1, 99), Is.EqualTo(-1), "column past the end");

            // A column past the end of its own line but still inside the string. The case above
            // does not catch this: 99 overruns the whole text, so a bound of text.Length rejects
            // it anyway. Here the only thing that can reject 6 is the end of line 1.
            Assert.That(DocumentView.OffsetOf("one\r\ntwo", 1, 6), Is.EqualTo(-1),
                "column past the end of its line, not of the text");
            Assert.That(DocumentView.OffsetOf("one\r\ntwo", 1, 4), Is.EqualTo(3),
                "one past the last character of the line is still in range");

            Assert.That(DocumentView.OffsetOf(crlf, 0, 1), Is.EqualTo(-1));
            Assert.That(DocumentView.OffsetOf(null, 1, 1), Is.EqualTo(-1));
        }

        [Test]
        public void TokenLengthStopsAtADelimiterOrTheEndOfTheText()
        {
            Assert.That(DocumentView.TokenLength("<Button Width=\"1\"", 1), Is.EqualTo(6), "Button");
            Assert.That(DocumentView.TokenLength("abc", 0), Is.EqualTo(3), "runs off the end");
            Assert.That(DocumentView.TokenLength("a.b", 0), Is.EqualTo(1));
            Assert.That(DocumentView.TokenLength("ab", 2), Is.EqualTo(0), "start at the end");
        }
    }
}
