using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class RightClickAndDefaultCommandsContextMenuExtensionTests
    {
        static (DesignSurface surface, DesignItem canvasItem, DesignItem aItem, DesignItem bItem) Load(Microsoft.UI.Xaml.Window window)
        {
            const string xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" />" +
                "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" />" +
                "</Canvas>";
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
            var aItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[0]);
            var bItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[1]);
            return (surface, surface.DesignContext.RootItem, aItem, bItem);
        }

        [Test]
        public void BringToFrontMovesTheItemToTheEndOfTheCollection()
        {
            // MenuFlyoutItem.Click is a sealed WinRT event with no way to raise it from a test,
            // so RightClickContextMenuExtension exposes internal command methods (BringToFront
            // etc.) that its Click handlers call - the test fetches the live extension instance
            // via DesignItem.Extensions and calls the method directly, exercising the same code
            // a real click would run.
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = Load(window);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });

                var flyout = (MenuFlyout)surface.DesignPanel.ContextFlyout;
                Assert.IsTrue(flyout.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Bring to Front"), "menu construction check");

                var extension = aItem.Extensions.OfType<RightClickContextMenuExtension>().Single();
                extension.BringToFront();

                var elements = canvasItem.ContentProperty.CollectionElements.ToList();
                Assert.AreEqual(aItem, elements[1], "A should now be last (front)");
            });
        }

        [Test]
        public void BringToFrontCommitsAsOneUndoEntryAndUndoRevertsFully()
        {
            // Final-review fix I1: BringToFront used to be two independent Remove/Add undo
            // entries with nothing wrapping them, so a single Undo only reverted the Add half,
            // leaving the item removed from the collection entirely. Mirrors
            // ModelToolsContextMenuTests.DeleteComponentsRemovesItemsAndCommitsAsOneUndoEntry's
            // undo-count-plus-one-and-full-round-trip shape.
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = Load(window);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });
                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                var undoCountBefore = undoService.UndoActions.Count();

                var extension = aItem.Extensions.OfType<RightClickContextMenuExtension>().Single();
                extension.BringToFront();

                var elementsAfter = canvasItem.ContentProperty.CollectionElements.ToList();
                Assert.AreEqual(aItem, elementsAfter[1], "A should now be last (front)");
                Assert.AreEqual(undoCountBefore + 1, undoService.UndoActions.Count());

                undoService.Undo();

                var elementsAfterUndo = canvasItem.ContentProperty.CollectionElements.ToList();
                Assert.AreEqual(aItem, elementsAfterUndo[0], "A should be back at its original index");
                Assert.AreEqual(bItem, elementsAfterUndo[1]);
            });
        }

        [Test]
        public void DeleteMenuItemDeletesTheSelectedItem()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = Load(window);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });

                var extension = aItem.Extensions.OfType<DefaultCommandsContextMenuExtension>().Single();
                extension.Delete();

                Assert.AreEqual(1, canvasItem.ContentProperty.CollectionElements.Count);
            });
        }

        [Test]
        public void UndoMenuItemUndoesTheLastChange()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = Load(window);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });
                var groupBefore = aItem.OpenGroup("test change");
                aItem.Properties.GetProperty("Width").SetValue(99.0);
                groupBefore.Commit();

                var extension = aItem.Extensions.OfType<DefaultCommandsContextMenuExtension>().Single();
                extension.Undo();

                Assert.AreEqual(20.0, ((Microsoft.UI.Xaml.FrameworkElement)aItem.Component).Width);
            });
        }

        [Test]
        public void RedoMenuItemReappliesTheLastUndoneChange()
        {
            // Final-review fix I4: Redo() had zero test coverage anywhere in the branch. Mirrors
            // UndoMenuItemUndoesTheLastChange above, one step further: Undo then Redo the same
            // Width change and confirm it's back in effect.
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = Load(window);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });
                var groupBefore = aItem.OpenGroup("test change");
                aItem.Properties.GetProperty("Width").SetValue(99.0);
                groupBefore.Commit();

                var extension = aItem.Extensions.OfType<DefaultCommandsContextMenuExtension>().Single();
                extension.Undo();
                Assert.AreEqual(20.0, ((Microsoft.UI.Xaml.FrameworkElement)aItem.Component).Width);

                extension.Redo();
                Assert.AreEqual(99.0, ((Microsoft.UI.Xaml.FrameworkElement)aItem.Component).Width);
            });
        }
    }
}
