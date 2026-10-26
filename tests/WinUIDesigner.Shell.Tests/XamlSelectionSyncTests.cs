using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// The selection carried across a mode switch: the selected item's start tag in the editor
    /// on the way into XAML, the element under the caret on the way back to Design.
    /// </summary>
    [TestFixture]
    public class XamlSelectionSyncTests
    {
        const string Nested =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"640\" Height=\"480\">\r\n" +
            "  <Grid Width=\"300\" Height=\"200\">\r\n" +
            "    <Grid.RowDefinitions>\r\n" +
            "      <RowDefinition Height=\"40\" />\r\n" +
            "    </Grid.RowDefinitions>\r\n" +
            "    <StackPanel>\r\n" +
            "      <Button Content=\"One\" />\r\n" +
            "      <Button Content=\"Two\" />\r\n" +
            "    </StackPanel>\r\n" +
            "  </Grid>\r\n" +
            "</Canvas>";

        [Test]
        public void PathAtNamesTheInnermostElementAroundTheOffset()
        {
            // Pure string work - no dispatcher needed.
            Assert.That(XamlSelectionSync.PathAt(Nested, Nested.IndexOf("Two")), Is.EqualTo(new[] { 0, 1, 1 }),
                "Canvas > Grid > (RowDefinitions is element 0) StackPanel is element 1 > second Button");
            Assert.That(XamlSelectionSync.PathAt(Nested, Nested.IndexOf("<Button")), Is.EqualTo(new[] { 0, 1, 0 }),
                "the '<' of a start tag is inside its element");
            Assert.That(XamlSelectionSync.PathAt(Nested, Nested.IndexOf("</StackPanel>") + 3), Is.EqualTo(new[] { 0, 1 }),
                "inside an end tag is inside its element");
            Assert.That(XamlSelectionSync.PathAt(Nested, Nested.IndexOf("</StackPanel>") - 1), Is.EqualTo(new[] { 0, 1 }),
                "whitespace after a closed child belongs to the parent");
            Assert.That(XamlSelectionSync.PathAt(Nested, Nested.IndexOf("Height=\"40\"")), Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(XamlSelectionSync.PathAt(Nested, 0), Is.Empty, "the document element itself");
            Assert.That(XamlSelectionSync.PathAt("<a><b>", 4), Is.EqualTo(new[] { 0 }),
                "text that breaks off later still answers for the part that was read");
            Assert.That(XamlSelectionSync.PathAt("", 0), Is.Null);
        }

        [Test]
        public void StartTagOfSpansTheWholeStartTagEvenWithAQuotedAngleBracket()
        {
            const string text = "<Canvas>\r<Button Content=\"a>b\" />\r<Button Content=\"c\" /></Canvas>";

            var tag = XamlSelectionSync.StartTagOf(text, new[] { 1 });
            Assert.That(tag, Is.Not.Null);
            Assert.That(text.Substring(tag.Item1, tag.Item2), Is.EqualTo("<Button Content=\"c\" />"));

            tag = XamlSelectionSync.StartTagOf(text, new[] { 0 });
            Assert.That(text.Substring(tag.Item1, tag.Item2), Is.EqualTo("<Button Content=\"a>b\" />"),
                "lone CR line breaks, as the editor holds them, and a '>' inside a value");

            Assert.That(XamlSelectionSync.StartTagOf(text, new[] { 2 }), Is.Null);
        }

        [Test]
        public void SwitchingToXamlSelectsTheSelectedItemsStartTag()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, Nested))
                {
                    var view = new DocumentView(doc);
                    var two = ButtonItem(doc, "Two");
                    doc.DesignContext.Services.Selection.SetSelectedComponents(new[] { two });

                    doc.Mode = DocumentMode.Xaml;

                    Assert.That(view.Editor.SelectedText, Is.EqualTo("<Button Content=\"Two\" />"));
                    view.Detach();
                }
            });
        }

        [Test]
        public void SwitchingToXamlAfterASurfaceEditStillFindsTheItemInTheReserializedText()
        {
            // A surface edit makes the switch re-serialize: indentation and attribute order change,
            // so line info from the original parse would point at the wrong place. The element
            // path does not change.
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, Nested))
                {
                    var view = new DocumentView(doc);
                    var two = ButtonItem(doc, "Two");
                    two.Properties["Width"].SetValue(120.0);
                    Assert.That(doc.IsDirty, Is.True);
                    doc.DesignContext.Services.Selection.SetSelectedComponents(new[] { two });

                    doc.Mode = DocumentMode.Xaml;

                    StringAssert.StartsWith("<Button", view.Editor.SelectedText);
                    StringAssert.Contains("Content=\"Two\"", view.Editor.SelectedText);
                    StringAssert.Contains("Width=\"120\"", view.Editor.SelectedText);
                    view.Detach();
                }
            });
        }

        [Test]
        public void SwitchingToDesignSelectsTheElementAtTheCaret()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, Nested))
                {
                    var view = new DocumentView(doc);
                    doc.Mode = DocumentMode.Xaml;
                    view.Editor.SelectionStart = view.Editor.Text.IndexOf("Two");
                    view.Editor.SelectionLength = 0;

                    doc.Mode = DocumentMode.Design;

                    var primary = doc.SelectionService.PrimarySelection;
                    Assert.That(primary, Is.Not.Null);
                    Assert.That(((Button)primary.Component).Content, Is.EqualTo("Two"));
                    view.Detach();
                }
            });
        }

        [Test]
        public void ACaretInAPropertyElementOrANonVisualChildSelectsTheOwningControl()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, Nested))
                {
                    var view = new DocumentView(doc);
                    doc.Mode = DocumentMode.Xaml;
                    view.Editor.SelectionStart = view.Editor.Text.IndexOf("Height=\"40\"");

                    doc.Mode = DocumentMode.Design;

                    Assert.That(doc.SelectionService.PrimarySelection?.Component, Is.InstanceOf<Grid>(),
                        "a RowDefinition is not on the surface; its Grid is");
                    view.Detach();
                }
            });
        }

        [Test]
        public void ARoundTripKeepsTheSelection()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, Nested))
                {
                    var view = new DocumentView(doc);
                    doc.DesignContext.Services.Selection.SetSelectedComponents(new[] { ButtonItem(doc, "One") });

                    doc.Mode = DocumentMode.Xaml;
                    doc.Mode = DocumentMode.Design;

                    Assert.That(((Button)doc.SelectionService.PrimarySelection.Component).Content, Is.EqualTo("One"),
                        "the rebuilt surface selects the same element, through the caret the XAML view left on it");
                    view.Detach();
                }
            });
        }

        [Test]
        public void AnUnparseableEditSelectsNothing()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, Nested))
                {
                    var view = new DocumentView(doc);
                    doc.Mode = DocumentMode.Xaml;
                    view.Editor.Text = view.Editor.Text.Replace("</Canvas>", "");
                    view.PushEditorTextToDocument();
                    view.Editor.SelectionStart = view.Editor.Text.IndexOf("Two");

                    doc.Mode = DocumentMode.Design;

                    Assert.That(doc.LoadFailed, Is.True);
                    view.Detach();
                }
            });
        }

        static WinUIDesigner.DesignModel.DesignItem ButtonItem(Document doc, string content)
        {
            var root = doc.DesignContext.RootItem;
            return Descendants(root).First(i => i.Component is Button b && (string)b.Content == content);
        }

        static IEnumerable<WinUIDesigner.DesignModel.DesignItem> Descendants(WinUIDesigner.DesignModel.DesignItem item)
        {
            yield return item;
            var content = item.ContentProperty;
            if (content == null)
                yield break;
            if (content.IsCollection)
            {
                foreach (var child in content.CollectionElements)
                    foreach (var d in Descendants(child))
                        yield return d;
            }
            else if (content.Value != null)
            {
                foreach (var d in Descendants(content.Value))
                    yield return d;
            }
        }
    }
}
