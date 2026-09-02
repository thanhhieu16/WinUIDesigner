using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class GridPlacementSupportTests
    {
        // A 100x100 Grid with deliberately UNEQUAL rows (30/70) and columns (40/60), so every
        // index assertion below distinguishes the running-sum-of-ActualHeight/ActualWidth
        // reconstruction of WPF's RowDefinition.Offset/ColumnDefinition.Offset from a naive
        // "equal shares" calculation. See SetUnequalDefinitionSizes for why the Height="30"/
        // Width="40" attributes below are not enough on their own (Global Constraints).
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"100\" Height=\"100\">" +
            "<Grid.RowDefinitions><RowDefinition Height=\"30\" /><RowDefinition Height=\"70\" /></Grid.RowDefinitions>" +
            "<Grid.ColumnDefinitions><ColumnDefinition Width=\"40\" /><ColumnDefinition Width=\"60\" /></Grid.ColumnDefinitions>" +
            "<Button x:Name=\"Target\" Width=\"20\" Height=\"10\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</Grid>";

        /// <summary>
        /// Re-applies the row/column sizes as typed GridLength values through the design model.
        /// Measured, not assumed (Global Constraints): the XAML Height="30"/Width="40" attributes
        /// round-trip through the model but never reach the live instance - every row/column lays
        /// out at its default star share until set this way.
        /// </summary>
        static void SetUnequalDefinitionSizes(DesignSurface surface)
        {
            var rootItem = surface.DesignContext.RootItem;
            var rows = rootItem.Properties["RowDefinitions"].CollectionElements;
            rows[0].Properties["Height"].SetValue(new Microsoft.UI.Xaml.GridLength(30));
            rows[1].Properties["Height"].SetValue(new Microsoft.UI.Xaml.GridLength(70));
            var cols = rootItem.Properties["ColumnDefinitions"].CollectionElements;
            cols[0].Properties["Width"].SetValue(new Microsoft.UI.Xaml.GridLength(40));
            cols[1].Properties["Width"].SetValue(new Microsoft.UI.Xaml.GridLength(60));
            surface.UpdateLayout();
        }

        static DesignSurface LoadSelected(Microsoft.UI.Xaml.Window window, out DesignItem buttonItem,
                                          out Microsoft.UI.Xaml.Controls.Button button)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(GridXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            SetUnequalDefinitionSizes(surface);

            var grid = (Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component;
            button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
            buttonItem = surface.DesignContext.Services.Component.GetDesignItem(button);
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            return surface;
        }

        static DesignItemProperty Attached(DesignItem item, string name)
        {
            return item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Grid), name);
        }

        [Test]
        public void PlacingAnItemAcrossCellBoundariesSetsRowColumnAndBothSpans()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Resize);
                Assert.IsInstanceOf<GridPlacementSupport>(op.CurrentContainerBehavior,
                    "[ExtensionFor(typeof(Grid), OverrideExtension = typeof(DefaultPlacementBehavior))] " +
                    "must make GridPlacementSupport win over the generic DefaultPlacementBehavior");

                var info = op.PlacedItems[0];
                // Left=20, Top=10, Right=80, Bottom=90 - starts in column 0 / row 0 and ends in
                // column 1 / row 1, so both spans must become 2.
                info.Bounds = new Windows.Foundation.Rect(20, 10, 60, 80);
                op.CurrentContainerBehavior.SetPosition(info);
                op.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(0, Microsoft.UI.Xaml.Controls.Grid.GetRow(button), "Grid.Row");
                Assert.AreEqual(0, Microsoft.UI.Xaml.Controls.Grid.GetColumn(button), "Grid.Column");
                Assert.AreEqual(2, Microsoft.UI.Xaml.Controls.Grid.GetRowSpan(button), "Grid.RowSpan");
                Assert.AreEqual(2, Microsoft.UI.Xaml.Controls.Grid.GetColumnSpan(button), "Grid.ColumnSpan");

                // HorizontalAlignment/VerticalAlignment stay Left/Top (not an EnterContainer), so the
                // margin is measured from the top-left of the spanned area, i.e. offsets (0, 0).
                Assert.AreEqual(new Microsoft.UI.Xaml.Thickness(20, 10, 0, 0), button.Margin, "Margin");
                Assert.AreEqual(60, button.Width, 0.5);
                Assert.AreEqual(80, button.Height, 0.5);
            });
        }

        [Test]
        public void UnequalRowAndColumnSizesAreResolvedThroughTheActualSizeRunningSum()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var grid = (Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component;

                // Guard the fixture itself: if the GridLength values had not taken effect these
                // would be 50/50 and every index assertion below would silently test nothing.
                Assert.AreEqual(30, grid.RowDefinitions[0].ActualHeight, 0.5);
                Assert.AreEqual(70, grid.RowDefinitions[1].ActualHeight, 0.5);
                Assert.AreEqual(40, grid.ColumnDefinitions[0].ActualWidth, 0.5);
                Assert.AreEqual(60, grid.ColumnDefinitions[1].ActualWidth, 0.5);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Resize);
                var info = op.PlacedItems[0];
                // Left=45 > the 40px column boundary and Top=35 > the 30px row boundary, so this
                // lands wholly in row 1 / column 1. With equal 50/50 rows and columns the same
                // point would resolve to row 0 / column 0 - this is the assertion that fails if the
                // Offset replacement is anything but a running sum of the real ActualWidth/
                // ActualHeight values in definition order.
                info.Bounds = new Windows.Foundation.Rect(45, 35, 30, 30);
                op.CurrentContainerBehavior.SetPosition(info);
                op.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetRow(button), "Grid.Row");
                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetColumn(button), "Grid.Column");
                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetRowSpan(button), "Grid.RowSpan");
                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetColumnSpan(button), "Grid.ColumnSpan");
                // A span of 1 is written as a Reset, not as SetValue(1), so no RowSpan/ColumnSpan
                // attribute is left behind in the XAML.
                Assert.IsFalse(Attached(buttonItem, "RowSpan").IsSet, "a span of 1 must Reset, not SetValue(1)");
                Assert.IsFalse(Attached(buttonItem, "ColumnSpan").IsSet, "a span of 1 must Reset, not SetValue(1)");
                // Margin measured from column 1's start (40) and row 1's start (30).
                Assert.AreEqual(new Microsoft.UI.Xaml.Thickness(5, 5, 0, 0), button.Margin, "Margin");
            });
        }

        [Test]
        public void EnteringAGridCellSuggestsAlignmentAndResetsMarginWidthAndHeight()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var grid = (Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component;

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                var behavior = op.CurrentContainerBehavior;
                Assert.IsInstanceOf<GridPlacementSupport>(behavior);

                // Leave and re-enter the same Grid: that is what sets the enteredIntoNewContainer
                // latch, which is the only thing that makes SetPosition *suggest* an alignment
                // rather than preserve the item's existing one. (ChangeContainer, which drives this
                // pair in production - Task 1/14 - exercises the same two calls in the same order.)
                behavior.LeaveContainer(op);
                Assert.AreEqual(0, grid.Children.Count, "LeaveContainer must detach the child");
                behavior.EnterContainer(op);
                Assert.AreEqual(1, grid.Children.Count, "EnterContainer must re-attach the child");

                var info = op.PlacedItems[0];
                // Cell (row 1, column 1) spans x 40..100 and y 30..100. Left=45 is within the
                // left quarter (<55) and Right=90 is within the right quarter (>85), so the
                // horizontal suggestion is Stretch; likewise Top=35 (<47.5) and Bottom=90 (>82.5)
                // give a vertical Stretch. Stretch on both axes exercises all four margin sides
                // and both Width/Height resets in one placement.
                info.Bounds = new Windows.Foundation.Rect(45, 35, 45, 55);
                behavior.SetPosition(info);
                op.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetRow(button), "Grid.Row");
                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetColumn(button), "Grid.Column");
                Assert.AreEqual(Microsoft.UI.Xaml.HorizontalAlignment.Stretch, button.HorizontalAlignment);
                Assert.AreEqual(Microsoft.UI.Xaml.VerticalAlignment.Stretch, button.VerticalAlignment);
                // Left = 45-40, Top = 35-30, Right = 100-90, Bottom = 100-90.
                Assert.AreEqual(new Microsoft.UI.Xaml.Thickness(5, 5, 10, 10), button.Margin, "all four Margin sides");

                // Asserted on the model, not on button.Width (Global Constraints): Reset() clears
                // the model but leaves the live DependencyProperty untouched, so button.Width would
                // still read 20 here and prove nothing.
                Assert.IsFalse(buttonItem.Properties["Width"].IsSet, "Width must be reset when stretched horizontally");
                Assert.IsFalse(buttonItem.Properties["Height"].IsSet, "Height must be reset when stretched vertically");
            });
        }

        [Test]
        public void LeavingTheGridRemovesARowOrColumnDefinitionDesignItemFromItsCollection()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out _, out _);
                var rootItem = surface.DesignContext.RootItem;
                var grid = (Microsoft.UI.Xaml.Controls.Grid)rootItem.Component;

                var rowItem = rootItem.Properties["RowDefinitions"].CollectionElements[1];
                Assert.AreEqual(typeof(Microsoft.UI.Xaml.Controls.RowDefinition), rowItem.ComponentType);

                var rowOp = PlacementOperation.Start(new[] { rowItem }, PlacementType.Delete);
                Assert.IsInstanceOf<GridPlacementSupport>(rowOp.CurrentContainerBehavior,
                    "a RowDefinition DesignItem's container behavior is the Grid's own");
                rowOp.CurrentContainerBehavior.LeaveContainer(rowOp);
                rowOp.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(1, grid.RowDefinitions.Count, "the RowDefinition must be gone from the live Grid");
                Assert.AreEqual(1, rootItem.Properties["RowDefinitions"].CollectionElements.Count,
                    "the RowDefinition DesignItem must be gone from the model collection");

                var colItem = rootItem.Properties["ColumnDefinitions"].CollectionElements[1];
                Assert.AreEqual(typeof(Microsoft.UI.Xaml.Controls.ColumnDefinition), colItem.ComponentType);

                var colOp = PlacementOperation.Start(new[] { colItem }, PlacementType.Delete);
                colOp.CurrentContainerBehavior.LeaveContainer(colOp);
                colOp.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(1, grid.ColumnDefinitions.Count);
                Assert.AreEqual(1, rootItem.Properties["ColumnDefinitions"].CollectionElements.Count);
            });
        }

        [Test]
        public void LeavingTheGridResetsGridAttachedPropertiesAndRestoresStretchedSizes()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);

                Attached(buttonItem, "Row").SetValue(1);
                Attached(buttonItem, "ColumnSpan").SetValue(2);
                // Stretch horizontally only: LeaveContainer must write Width back from the bounds
                // and leave Height alone, since VerticalAlignment is still Top.
                buttonItem.Properties["HorizontalAlignment"].SetValue(Microsoft.UI.Xaml.HorizontalAlignment.Stretch);
                surface.UpdateLayout();

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                op.PlacedItems[0].Bounds = new Windows.Foundation.Rect(0, 0, 33, 22);
                op.CurrentContainerBehavior.LeaveContainer(op);
                op.Commit();
                surface.UpdateLayout();

                Assert.IsFalse(Attached(buttonItem, "Row").IsSet, "Grid.Row must be reset on leaving");
                Assert.IsFalse(Attached(buttonItem, "Column").IsSet, "Grid.Column must be reset on leaving");
                Assert.IsFalse(Attached(buttonItem, "RowSpan").IsSet, "Grid.RowSpan must be reset on leaving");
                Assert.IsFalse(Attached(buttonItem, "ColumnSpan").IsSet, "Grid.ColumnSpan must be reset on leaving");
                Assert.AreEqual(33, button.Width, 0.5, "a horizontally stretched item gets an explicit Width back");
                Assert.AreEqual(10, button.Height, 0.5, "VerticalAlignment is Top, so Height must be untouched");
            });
        }

        // Final-review regression (I1): PlacementOperation.ChangeContainer calls LeaveContainer on
        // the OLD container's behavior without ever calling EndPlacement on it (only the FINAL
        // container gets EndPlacement, at Commit/Abort time). A Canvas and a Grid side by side,
        // each already holding one Button: "Mover" is dragged Canvas -> Grid -> back to Canvas
        // within one operation, exercising GridPlacementSupport.LeaveContainer on TargetGrid's own
        // behavior instance while TargetGrid is the CURRENT (not final) container - the exact path
        // EndPlacement never runs for. A separate, later operation then resizes "Resident", who
        // never left TargetGrid, to prove enteredIntoNewContainer did not leak true from Mover's
        // visit onto TargetGrid's (shared, per-DesignItem) GridPlacementSupport instance.
        const string CanvasAndGridSiblingsXaml =
            "<StackPanel Orientation=\"Horizontal\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Canvas x:Name=\"SourceCanvas\" Width=\"100\" Height=\"100\">" +
            "<Button x:Name=\"Mover\" Width=\"20\" Height=\"10\" Canvas.Left=\"10\" Canvas.Top=\"10\" />" +
            "</Canvas>" +
            "<Grid x:Name=\"TargetGrid\" Width=\"100\" Height=\"100\">" +
            "<Button x:Name=\"Resident\" Width=\"20\" Height=\"10\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</Grid>" +
            "</StackPanel>";

        [Test]
        public void LeavingTheGridAfterAChangeContainerVisitResetsTheJustEnteredLatchForLaterPlacements()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(CanvasAndGridSiblingsXaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var root = (Microsoft.UI.Xaml.Controls.StackPanel)surface.DesignContext.RootItem.Component;
                var sourceCanvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
                var targetGrid = (Microsoft.UI.Xaml.Controls.Grid)root.Children[1];
                var mover = (Microsoft.UI.Xaml.Controls.Button)sourceCanvas.Children[0];
                var resident = (Microsoft.UI.Xaml.Controls.Button)targetGrid.Children[0];

                var services = surface.DesignContext.Services;
                var sourceItem = services.Component.GetDesignItem(sourceCanvas);
                var gridItem = services.Component.GetDesignItem(targetGrid);
                var moverItem = services.Component.GetDesignItem(mover);
                var residentItem = services.Component.GetDesignItem(resident);

                // Drag Mover Canvas -> Grid -> back to Canvas, all within one PlacementOperation.
                var moveOp = PlacementOperation.Start(new[] { moverItem }, PlacementType.Move);
                moveOp.ChangeContainer(gridItem);
                Assert.IsInstanceOf<GridPlacementSupport>(moveOp.CurrentContainerBehavior,
                    "precondition: Mover's current behavior must be TargetGrid's own");
                var gridBehavior = moveOp.CurrentContainerBehavior;
                moveOp.ChangeContainer(sourceItem);
                moveOp.Commit();
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, moverItem.Parent, "precondition: Mover ended back up in SourceCanvas");

                // Resident never left TargetGrid - a fresh, separate PlacementOperation resize on
                // it must not be affected by whatever enteredIntoNewContainer state Mover's visit
                // left behind on TargetGrid's GridPlacementSupport instance.
                var residentOp = PlacementOperation.Start(new[] { residentItem }, PlacementType.Resize);
                Assert.AreSame(gridBehavior, residentOp.CurrentContainerBehavior,
                    "precondition: same GridPlacementSupport instance that Mover's visit touched");
                var info = residentOp.PlacedItems[0];
                // Fills the whole (single implicit-cell) grid - if enteredIntoNewContainer had
                // leaked true, SetPosition would wrongly "suggest" Stretch/Stretch here instead of
                // preserving Resident's existing Left/Top alignment.
                info.Bounds = new Windows.Foundation.Rect(0, 0, 100, 100);
                residentOp.CurrentContainerBehavior.SetPosition(info);
                residentOp.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(Microsoft.UI.Xaml.HorizontalAlignment.Left, resident.HorizontalAlignment,
                    "a stale enteredIntoNewContainer latch must not overwrite Resident's alignment");
                Assert.AreEqual(Microsoft.UI.Xaml.VerticalAlignment.Top, resident.VerticalAlignment);
                Assert.IsTrue(residentItem.Properties["Width"].IsSet,
                    "Width must not be Reset - that only happens when the (wrongly) suggested alignment resolves to Stretch");
                Assert.IsTrue(residentItem.Properties["Height"].IsSet);
            });
        }
    }
}
