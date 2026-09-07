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

                    Assert.That(doc.IsDirty, Is.False,
                        "TextBox hands back lone CR where XamlFormatter emitted CRLF (measured); " +
                        "the round trip through the editor must not read as a user edit");
                    Assert.That(view.Editor.Text, Is.Not.Empty);
                    view.Detach();
                }
            });
        }

        [Test]
        public void AProgrammaticTextChangeReachesTheEditorWithoutLooping()
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
