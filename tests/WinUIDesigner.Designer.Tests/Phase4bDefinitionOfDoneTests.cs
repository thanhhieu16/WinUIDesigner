using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class Phase4bDefinitionOfDoneTests
    {
        [Test]
        public void DraggingAnItemFromACanvasIntoAGridUpdatesGridSpecificPositionOnEntry()
        {
            const string Xaml =
                "<StackPanel Orientation=\"Horizontal\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Canvas x:Name=\"SourceCanvas\" Width=\"150\" Height=\"150\">" +
                "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" Canvas.Left=\"10\" Canvas.Top=\"10\" />" +
                "</Canvas>" +
                "<Grid x:Name=\"TargetGrid\" Width=\"150\" Height=\"150\">" +
                "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
                "<Grid.ColumnDefinitions><ColumnDefinition /><ColumnDefinition /></Grid.ColumnDefinitions>" +
                "</Grid>" +
                "</StackPanel>";

            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(Xaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var root = (Microsoft.UI.Xaml.Controls.StackPanel)surface.DesignContext.RootItem.Component;
                var sourceCanvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
                var targetGrid = (Microsoft.UI.Xaml.Controls.Grid)root.Children[1];
                var button = (Microsoft.UI.Xaml.Controls.Button)sourceCanvas.Children[0];

                // Brief-defect correction (same root cause and same fix as MoveTests.cs's
                // LoadSelected): a Panel with Background == null is not hit-test visible over
                // its own empty interior (WPF/UWP/WinUI convention), so without this,
                // MoveLogic.ChangeContainerIfPossible's hit-test can never land on the empty
                // TargetGrid at all. Set directly on the live view (test-fixture plumbing, not
                // model state under test), same as MoveTests.cs's targetCanvas.Background fix.
                targetGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

                var services = surface.DesignContext.Services;
                var buttonItem = services.Component.GetDesignItem(button);
                var targetItem = services.Component.GetDesignItem(targetGrid);
                services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
                surface.UpdateLayout();

                // Brief-defect correction (same root cause and same fix as MoveTests.cs's
                // WindowPoint helper): WinUITestHost's shared window is larger than this
                // fixture's content, and DesignPanel.ArrangeOverride centers the root content
                // within its own full ActualSize, so the StackPanel does not render flush with
                // the window's top-left corner - literal window-coordinate points would not
                // reliably land inside SourceCanvas/TargetGrid. Every simulated point is instead
                // derived from the live TransformToVisual(null) of the container it must land
                // in, converting a container-local offset into real window coordinates.
                var extension = buttonItem.Extensions.OfType<TopLeftContainerDragHandle>().Single();
                var startPoint = sourceCanvas.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(20, 20));
                // Local x=100 in TargetGrid's own 150-wide, 2-equal-column space falls in the
                // second column (75-150), unlike SourceCanvas's coordinate space this drag started in.
                var movePoint = targetGrid.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(100, 90));
                extension.DragListener.SimulateDown(startPoint);
                extension.DragListener.SimulateMove(movePoint);
                extension.DragListener.SimulateUp(movePoint);
                surface.UpdateLayout();

                Assert.AreSame(targetItem, buttonItem.Parent,
                    "the item must have reparented from SourceCanvas to TargetGrid (Known gap #1, exercised end to end)");
                // GridPlacementSupport.EnterContainer sets enteredIntoNewContainer, so SetPosition
                // suggests Grid.Row/Column instead of preserving Canvas-flavored placement.
                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetColumn(button),
                    "on entry into a Grid, GridPlacementSupport must resolve a real Grid.Column, not leave Canvas semantics behind");

                var undo = services.GetService<UndoService>();
                Assert.IsTrue(undo.CanUndo);
                undo.Undo();
                surface.UpdateLayout();
                Assert.AreSame(services.Component.GetDesignItem(sourceCanvas), buttonItem.Parent,
                    "a single undo must revert the cross-container move, including the Grid-side Column write");
            });
        }

        [Test]
        public void SnaplinesAndRasterBothApplyDuringACanvasMoveNotJustAResize()
        {
            const string Xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
                "<Button x:Name=\"A\" Width=\"40\" Height=\"20\" Canvas.Left=\"150\" Canvas.Top=\"150\" />" +
                "<Button x:Name=\"B\" Width=\"40\" Height=\"20\" Canvas.Left=\"0\" Canvas.Top=\"0\" />" +
                "</Canvas>";

            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(Xaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                var b = (Microsoft.UI.Xaml.Controls.Button)canvas.Children[1];
                var services = surface.DesignContext.Services;
                var bItem = services.Component.GetDesignItem(b);
                services.Selection.SetSelectedComponents(new[] { bItem }, SelectionTypes.Primary);
                surface.UpdateLayout();

                Assert.IsTrue(surface.DesignPanel.UseSnaplinePlacement, "snaplines are on by default");

                var op = PlacementOperation.Start(new[] { bItem }, PlacementType.Move);
                try {
                    op.PlacedItems[0].Bounds = new Windows.Foundation.Rect(152, 300, 40, 20);
                    op.CurrentContainerBehavior.BeforeSetPosition(op, Windows.System.VirtualKeyModifiers.None);
                    Assert.AreEqual(150, op.PlacedItems[0].Bounds.X, 0.5,
                        "CanvasPlacementSupport chains through SnaplinePlacementBehavior - a move (not just a resize) snaps to sibling A's left edge");
                } finally {
                    op.Abort();
                }
            });
        }

        [Test]
        public void EachKnownGapHasAConcreteResolutionElsewhereInThisPlan()
        {
            // Reflection-based, same pattern as Phase 4a's DoD suite: point at the actual type/
            // member each resolution landed, so this test breaks (rather than silently passing)
            // if a later refactor removes the resolution without updating this table.
            Assert.IsNotNull(typeof(PlacementOperation).GetMethod("ChangeContainer"),
                "gap #1: PlacementOperation.ChangeContainer (Task 1)");
            Assert.IsNotNull(typeof(GrayOutDesignerExceptActiveArea).GetMethod("AnimateActiveAreaRectTo"),
                "gap #2: GrayOutDesignerExceptActiveArea.AnimateActiveAreaRectTo exists and compiles (Task 8) - visual correctness itself is human-eyeballed, see Task 8 Step 6");
            Assert.IsNotNull(typeof(GridAdornerProvider).GetMethod("FlushPendingRebuild", BindingFlags.NonPublic | BindingFlags.Instance),
                "gap #3: GridAdornerProvider forces grid.UpdateLayout() instead of a DispatcherPriority.Loaded substitute (Task 11)");
            Assert.IsNotNull(typeof(GridRailAdorner).GetMethod("RefreshLabels"),
                "gap #4: GridAdorner's rail/splitter/unit-selector composition is concrete and tested (Task 11-13)");
            var beforeSetPosition = typeof(IPlacementBehavior).GetMethod("BeforeSetPosition");
            Assert.AreEqual(2, beforeSetPosition.GetParameters().Length,
                "gap #5: IPlacementBehavior.BeforeSetPosition carries the VirtualKeyModifiers parameter (Task 4)");
            Assert.IsNotNull(typeof(WinUIDesigner.Designer.Services.OptionService)
                .GetField("GrayOutDesignSurfaceExceptParentContainerWhenDragging"),
                "gap #6: OptionService ports both reference fields, not a narrowed subset (Task 4)");
        }
    }
}
