using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class StackPanelPlacementSupportTests
    {
        // Three 50px-tall children stacked vertically: rects (0,0,200,50), (0,50,200,50),
        // (0,100,200,50); their centers are at y = 25, 75, 125.
        const string VerticalXaml =
            "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Orientation=\"Vertical\" Width=\"200\" Height=\"300\">" +
            "<Button x:Name=\"A\" Height=\"50\" /><Button x:Name=\"B\" Height=\"50\" /><Button x:Name=\"C\" Height=\"50\" />" +
            "</StackPanel>";

        // Three 60px-wide children in a row: rects (0,0,60,100), (60,0,60,100), (120,0,60,100);
        // their centers are at x = 30, 90, 150.
        const string HorizontalXaml =
            "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Orientation=\"Horizontal\" Width=\"300\" Height=\"100\">" +
            "<Button x:Name=\"A\" Width=\"60\" /><Button x:Name=\"B\" Width=\"60\" /><Button x:Name=\"C\" Width=\"60\" />" +
            "</StackPanel>";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window, string xaml)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        /// <summary>Drives one placement tick the way a drag-move gesture will in Task 14
        /// (MoveLogic doesn't exist yet), and returns the dragged item's new collection index.</summary>
        static int MoveFirstChildTo(DesignSurface surface, Windows.Foundation.Rect bounds)
        {
            var root = surface.DesignContext.RootItem;
            var a = root.ContentProperty.CollectionElements[0];
            var op = PlacementOperation.Start(new[] { a }, PlacementType.Move);
            var info = op.PlacedItems[0];
            info.Bounds = bounds;
            op.CurrentContainerBehavior.SetPosition(info);
            op.Commit();
            surface.UpdateLayout();
            return root.ContentProperty.CollectionElements.IndexOf(a);
        }

        [Test]
        public void AStackPanelResolvesToStackPanelPlacementSupportInsteadOfTheGenericBehavior()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, VerticalXaml);
                var behavior = surface.DesignContext.RootItem.GetBehavior<IPlacementBehavior>();
                Assert.IsInstanceOf<StackPanelPlacementSupport>(behavior);
            });
        }

        [Test]
        public void DraggingTheFirstChildPastBothSiblingsMovesItToTheEndOfTheCollection()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, VerticalXaml);
                // center y = 145, past every sibling center -> insert index 3, adjusted to 2.
                Assert.AreEqual(2, MoveFirstChildTo(surface, new Windows.Foundation.Rect(0, 120, 200, 50)));
                Assert.AreEqual(3, surface.DesignContext.RootItem.ContentProperty.CollectionElements.Count);
            });
        }

        [Test]
        public void DraggingTheFirstChildPastOneSiblingMovesItToTheMiddleOfTheCollection()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, VerticalXaml);
                // center y = 85: first sibling center >= 85 is C's (125) at index 2 -> adjusted to 1.
                Assert.AreEqual(1, MoveFirstChildTo(surface, new Windows.Foundation.Rect(0, 60, 200, 50)));
            });
        }

        // Guards the reference-bug fix in FindVerticalRectanglePlacementOffset: the reference
        // compares (Left + Top)/2 instead of (Left + Right)/2, which for this exact input yields
        // index 2 instead of the correct 1.
        [Test]
        public void DraggingInAHorizontalStackPanelUsesHorizontalCentersNotTheReferencesLeftPlusTop()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, HorizontalXaml);
                // center x = 100: first sibling center >= 100 is C's (150) at index 2 -> adjusted to 1.
                Assert.AreEqual(1, MoveFirstChildTo(surface, new Windows.Foundation.Rect(70, 0, 60, 100)));
            });
        }

        [Test]
        public void TheInsertionIndicatorIsAddedForTheDurationOfThePlacementOperationOnly()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, VerticalXaml);
                var root = surface.DesignContext.RootItem;
                var a = root.ContentProperty.CollectionElements[0];
                var extension = root.Extensions.OfType<StackPanelPlacementSupport>().Single();

                var op = PlacementOperation.Start(new[] { a }, PlacementType.Move);
                Assert.IsTrue(surface.DesignPanel.Adorners.Contains(extension.IndicatorAdornerPanel),
                    "BeginPlacement must add the insertion-point adorner");

                var info = op.PlacedItems[0];
                info.Bounds = new Windows.Foundation.Rect(0, 60, 200, 50);
                op.CurrentContainerBehavior.SetPosition(info);
                Assert.AreEqual(2, extension.IndicatorRectangle.Height, "a vertical StackPanel draws a 2px horizontal bar");

                op.Commit();
                Assert.IsFalse(surface.DesignPanel.Adorners.Contains(extension.IndicatorAdornerPanel),
                    "EndPlacement must remove the insertion-point adorner");
            });
        }

        [Test]
        public void ResizingAChildDoesNotReorderIt()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, VerticalXaml);
                var root = surface.DesignContext.RootItem;
                var a = root.ContentProperty.CollectionElements[0];
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { a }, SelectionTypes.Primary);
                surface.UpdateLayout();

                var resize = a.Extensions.OfType<ResizeThumbExtension>().Single();
                var drag = resize.DragListeners[PlacementAlignment.BottomRight];

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                Assert.IsFalse(resize.IsResizing, "IsResizing only turns on once the drag threshold is passed");

                drag.SimulateMove(new Windows.Foundation.Point(30, 200)); // far enough down to cross both sibling centers
                Assert.IsTrue(resize.IsResizing, "Drag_Started must set IsResizing");
                drag.SimulateUp(new Windows.Foundation.Point(30, 200));
                surface.UpdateLayout();

                Assert.IsFalse(resize.IsResizing, "Drag_Completed must clear IsResizing");
                Assert.AreEqual(0, root.ContentProperty.CollectionElements.IndexOf(a),
                    "a resize must not reorder the item inside its StackPanel");
            });
        }
    }
}
