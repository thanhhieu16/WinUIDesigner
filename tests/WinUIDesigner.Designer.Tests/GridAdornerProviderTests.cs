using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class GridAdornerProviderTests
    {
        // Row/column sizes are deliberately left unset (each definition defaults to 1*).
        // GridLength has NO from-string TypeConverter in this port (Global Constraints), so
        // Height="40" in XAML never reaches the live RowDefinition - a fixture must never rely
        // on it. 200x120 with two rows/two columns gives an exact 60/60 and 100/100 split.
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"120\">" +
            "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
            "<Grid.ColumnDefinitions><ColumnDefinition /><ColumnDefinition /></Grid.ColumnDefinitions>" +
            "<Button x:Name=\"Target\" Grid.Row=\"1\" Grid.Column=\"1\" Content=\"x\" />" +
            "</Grid>";

        // HorizontalAlignment/VerticalAlignment="Stretch" is deliberate (review finding I1's
        // regression coverage): a default-aligned Button sizes to its own content and CENTERS
        // within its spanned rows' combined bounding box, so its rendered topLeft/ActualHeight
        // shift with that box's total size regardless of split-read timing - "hold visual
        // position stable" is inherently ambiguous for a child that doesn't fill its cell. A
        // stretched child's rendered rectangle IS its cell (topLeft always (0,0), ActualHeight
        // always the full spanned extent), which is both the common real-world case for a
        // straddling design-time element (a background/container spanning multiple rows) and the
        // only case where FixIndicesAfterSplit's pixel-position heuristic has one unambiguous
        // correct answer.
        const string SpanXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"120\">" +
            "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
            "<Button x:Name=\"Target\" Grid.Row=\"0\" Grid.RowSpan=\"2\" Content=\"x\" " +
            "HorizontalAlignment=\"Stretch\" VerticalAlignment=\"Stretch\" />" +
            "</Grid>";

        internal static DesignSurface Load(Window window, string xaml,
                                           out DesignItem gridItem, out Grid grid, out GridAdornerProvider provider)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();

            gridItem = surface.DesignContext.RootItem;
            grid = (Grid)gridItem.Component;
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { gridItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            provider = gridItem.Extensions.OfType<GridAdornerProvider>().Single();
            // The rebuild is coalesced onto the dispatcher; a test body is one dispatcher turn, so
            // the enqueued callback can never run inside it. Flush it explicitly instead.
            provider.FlushPendingRebuild();
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void SelectingAGridShowsTwoRails()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out _, out var provider);
                var rails = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<GridRailAdorner>().ToList();
                Assert.AreEqual(2, rails.Count);
                Assert.AreEqual(120, provider.LeftRail.ActualHeight, 0.5);
                Assert.AreEqual(10, provider.LeftRail.ActualWidth, 0.5);
                Assert.AreEqual(200, provider.TopRail.ActualWidth, 0.5);
                Assert.AreEqual(10, provider.TopRail.ActualHeight, 0.5);
            });
        }

        [Test]
        public void RailsShowOneSizeUnitCaptionPerRowAndColumn()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out _, out var provider);
                CollectionAssert.AreEqual(new[] { "*", "*" }, provider.LeftRail.LabelTexts.ToList());
                CollectionAssert.AreEqual(new[] { "*", "*" }, provider.TopRail.LabelTexts.ToList());

                provider.LeftRail.SimulateRailPress(30);
                surface.UpdateLayout();
                CollectionAssert.AreEqual(new[] { "0.5*", "0.5*", "*" }, provider.LeftRail.LabelTexts.ToList());
            });
        }

        [Test]
        public void RailClickCreatesRowAndShiftsLaterSiblings()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                var button = (Button)grid.Children[0];
                Assert.AreEqual(60, grid.RowDefinitions[0].ActualHeight, 0.5, "precondition");

                provider.LeftRail.SimulateRailPress(30);
                surface.UpdateLayout();

                Assert.AreEqual(3, grid.RowDefinitions.Count);
                Assert.AreEqual(30, grid.RowDefinitions[0].ActualHeight, 0.5);
                Assert.AreEqual(30, grid.RowDefinitions[1].ActualHeight, 0.5);
                Assert.AreEqual(2, Grid.GetRow(button), "a later sibling shifts down one row");
                Assert.AreEqual(1, undo.UndoActions.Count(), "creation + every sibling rewrite is ONE undo entry");

                undo.Undo();
                surface.UpdateLayout();
                Assert.AreEqual(2, grid.RowDefinitions.Count);
                Assert.AreEqual(1, Grid.GetRow(button));
                Assert.AreEqual(60, grid.RowDefinitions[0].ActualHeight, 0.5,
                    "undo must restore the split row's live size, not just the XAML");
            });
        }

        [Test]
        public void SplittingAStraddlingChildGrowsItsRowSpan()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, SpanXaml, out _, out var grid, out var provider);
                var button = (Button)grid.Children[0];
                provider.LeftRail.SimulateRailPress(30);
                surface.UpdateLayout();
                Assert.AreEqual(3, grid.RowDefinitions.Count);
                Assert.AreEqual(0, Grid.GetRow(button));
                Assert.AreEqual(3, Grid.GetRowSpan(button));
            });
        }

        [Test]
        public void SplittingTheSecondRowOfAStraddlingChildGrowsItsRowSpan()
        {
            // Regression for review finding I1: FixIndicesAfterSplit used to read each child's
            // topLeft/ActualHeight AFTER the split had already been applied and laid out. For a
            // stretched child spanning row 0 through the split row, the post-split ActualHeight
            // always equals exactly (insertionPosition - topLeft.Y), so the
            // "insertionPosition >= topLeft.Y + height" guard misfired and skipped growing
            // RowSpan. SplittingAStraddlingChildGrowsItsRowSpan above splits the FIRST row of the
            // span (insertionPosition=30), where the guard happens to still read correctly by
            // accident; this test splits the SECOND row of the span (insertionPosition=90, inside
            // row index 1 of two 60px rows) - the exact shape the guard misfired on.
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, SpanXaml, out _, out var grid, out var provider);
                var button = (Button)grid.Children[0];
                Assert.AreEqual(60, grid.RowDefinitions[1].ActualHeight, 0.5, "precondition");

                provider.LeftRail.SimulateRailPress(90);
                surface.UpdateLayout();

                Assert.AreEqual(3, grid.RowDefinitions.Count);
                Assert.AreEqual(0, Grid.GetRow(button));
                Assert.AreEqual(3, Grid.GetRowSpan(button), "the child must still cover all three rows after the split");
            });
        }

        [Test]
        public void ColumnRailClickCreatesColumn()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                provider.TopRail.SimulateRailPress(50);
                surface.UpdateLayout();
                Assert.AreEqual(3, grid.ColumnDefinitions.Count);
                Assert.AreEqual(2, Grid.GetColumn((Button)grid.Children[0]));
            });
        }

        [Test]
        public void OneSplitterExistsPerInteriorRowAndColumnBoundary()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out _, out var provider);
                Assert.AreEqual(2, provider.Splitters.Count, "two rows and two columns => one boundary each");
                Assert.AreEqual(1, provider.Splitters.OfType<GridRowSplitterAdorner>().Count());
                Assert.AreEqual(1, provider.Splitters.OfType<GridColumnSplitterAdorner>().Count());
                var inTree = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<GridSplitterAdorner>().ToList();
                CollectionAssert.IsSubsetOf(provider.Splitters.ToList(), inTree);
            });
        }

        [Test]
        public void DraggingRowSplitterResizesBothRowsAndCommitsOneChangeGroup()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                var splitter = provider.Splitters.OfType<GridRowSplitterAdorner>().Single();

                splitter.SimulateDown(new Windows.Foundation.Point(0, 60));
                splitter.SimulateMove(new Windows.Foundation.Point(0, 80));
                splitter.SimulateUp(new Windows.Foundation.Point(0, 80));
                surface.UpdateLayout();

                Assert.AreEqual(80, grid.RowDefinitions[0].ActualHeight, 0.5);
                Assert.AreEqual(40, grid.RowDefinitions[1].ActualHeight, 0.5);
                Assert.AreEqual(1, undo.UndoActions.Count(), "one ChangeGroup for the whole drag");

                undo.Undo();
                surface.UpdateLayout();
                Assert.AreEqual(60, grid.RowDefinitions[0].ActualHeight, 0.5);
                Assert.AreEqual(60, grid.RowDefinitions[1].ActualHeight, 0.5);
            });
        }

        [Test]
        public void DraggingColumnSplitterResizesBothColumns()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                var splitter = provider.Splitters.OfType<GridColumnSplitterAdorner>().Single();

                splitter.SimulateDown(new Windows.Foundation.Point(100, 0));
                splitter.SimulateMove(new Windows.Foundation.Point(140, 0));
                splitter.SimulateUp(new Windows.Foundation.Point(140, 0));
                surface.UpdateLayout();

                Assert.AreEqual(140, grid.ColumnDefinitions[0].ActualWidth, 0.5);
                Assert.AreEqual(60, grid.ColumnDefinitions[1].ActualWidth, 0.5);
            });
        }

        [Test]
        public void EscapeCancelsSplitterDragWithNoUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                var splitter = provider.Splitters.OfType<GridRowSplitterAdorner>().Single();

                splitter.SimulateDown(new Windows.Foundation.Point(0, 60));
                splitter.SimulateMove(new Windows.Foundation.Point(0, 90));
                splitter.SimulateEscape();
                surface.UpdateLayout();

                Assert.AreEqual(60, grid.RowDefinitions[0].ActualHeight, 0.5, "aborted drag reverts the live size");
                Assert.IsFalse(undo.CanUndo, "an aborted ChangeGroup pushes nothing onto the undo stack");
                Assert.IsFalse(splitter.HasOpenChangeGroup);
                Assert.IsFalse(splitter.IsDragging);
            });
        }

        [Test]
        public void MoveBelowTheDragThresholdOpensNoChangeGroup()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                var splitter = provider.Splitters.OfType<GridRowSplitterAdorner>().Single();

                splitter.SimulateDown(new Windows.Foundation.Point(0, 60));
                splitter.SimulateMove(new Windows.Foundation.Point(0, 61)); // < MinimumVerticalDragDistance
                Assert.IsFalse(splitter.HasOpenChangeGroup);
                splitter.SimulateUp(new Windows.Foundation.Point(0, 61));

                Assert.IsFalse(undo.CanUndo);
                Assert.AreEqual(60, grid.RowDefinitions[0].ActualHeight, 0.5);
            });
        }

        [Test]
        public void SplitterListRebuildsAfterRailClickCreatesRowAndColumn()
        {
            // Task 11 review finding M8 (deferred, flagged for this task): the rebuild path
            // triggered by DesignItem.PropertyChanged (PropertyName=="RowDefinitions"/
            // "ColumnDefinitions") firing after a CollectionElements.Insert - as opposed to the
            // *initial* Rebuild() that OnInitialized's own RequestRebuild triggers, which every
            // other test in this file exercises via Load() - was never exercised by Task 11's own
            // tests. A click-created row/column (SimulateRailPress) is exactly the code path that
            // fires that PropertyChanged (see GridRailAdorner.SplitAt -> collection.CollectionElements.Insert),
            // so this test drives that path explicitly and confirms a splitter appears at the new
            // interior boundary once the resulting rebuild is flushed.
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                Assert.AreEqual(1, provider.Splitters.OfType<GridRowSplitterAdorner>().Count(), "precondition: 2 rows => 1 boundary");
                Assert.AreEqual(1, provider.Splitters.OfType<GridColumnSplitterAdorner>().Count(), "precondition: 2 columns => 1 boundary");

                provider.LeftRail.SimulateRailPress(30);
                surface.UpdateLayout();
                provider.FlushPendingRebuild();
                surface.UpdateLayout();

                Assert.AreEqual(3, grid.RowDefinitions.Count, "precondition: the click did create a third row");
                Assert.AreEqual(2, provider.Splitters.OfType<GridRowSplitterAdorner>().Count(),
                    "3 rows => 2 interior boundaries, once DesignItem.PropertyChanged(\"RowDefinitions\") after the Insert triggers and flushes a rebuild");
                var inTree = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<GridSplitterAdorner>().ToList();
                CollectionAssert.IsSubsetOf(provider.Splitters.ToList(), inTree);

                provider.TopRail.SimulateRailPress(50);
                surface.UpdateLayout();
                provider.FlushPendingRebuild();
                surface.UpdateLayout();

                Assert.AreEqual(3, grid.ColumnDefinitions.Count, "precondition: the click did create a third column");
                Assert.AreEqual(2, provider.Splitters.OfType<GridColumnSplitterAdorner>().Count(),
                    "3 columns => 2 interior boundaries, same PropertyChanged(\"ColumnDefinitions\") path");
            });
        }

        [Test]
        public void UnitSelectorSetsAutoStarAndPixelOnTheHoveredRow()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                provider.LeftRail.SimulateHover(30); // inside row 0 (0..60)
                var selector = provider.LeftRail.UnitSelector;
                Assert.IsNotNull(selector.SelectedItem);
                Assert.AreEqual(GridUnitType.Star, selector.Unit, "an unset RowDefinition.Height is 1*");
                Assert.AreEqual(Visibility.Visible, selector.Visibility);

                selector.SimulatePick(GridUnitType.Auto);
                surface.UpdateLayout();
                Assert.IsTrue(grid.RowDefinitions[0].Height.IsAuto);
                Assert.AreEqual(GridUnitType.Auto, selector.Unit);
                CollectionAssert.AreEqual(new[] { "Auto", "*" }, provider.LeftRail.LabelTexts.ToList(),
                    "the rail caption follows the unit change");

                selector.SimulatePick(GridUnitType.Star);
                surface.UpdateLayout();
                Assert.IsTrue(grid.RowDefinitions[0].Height.IsStar);

                selector.SimulatePick(GridUnitType.Pixel);
                surface.UpdateLayout();
                Assert.IsTrue(grid.RowDefinitions[0].Height.IsAbsolute);
            });
        }

        [Test]
        public void UnitSelectorTargetsTheHoveredColumnOnly()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                provider.TopRail.SimulateHover(150); // inside column 1 (100..200)
                var selector = provider.TopRail.UnitSelector;
                Assert.AreSame(surface.DesignContext.Services.Component.GetDesignItem(grid.ColumnDefinitions[1]),
                               selector.SelectedItem);

                selector.SimulatePick(GridUnitType.Auto);
                surface.UpdateLayout();
                Assert.IsTrue(grid.ColumnDefinitions[1].Width.IsAuto);
                Assert.IsFalse(grid.ColumnDefinitions[0].Width.IsAuto, "the other column is untouched");
            });
        }

        [Test]
        public void HoveringPastTheLastRowHidesTheUnitSelector()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out _, out var provider);
                provider.LeftRail.SimulateHover(500); // past the bottom of the grid
                Assert.AreEqual(Visibility.Collapsed, provider.LeftRail.UnitSelector.Visibility);
            });
        }

        [Test]
        public void SwitchingHoverBetweenRowsOfDifferentUnitsDoesNotCorruptTheNewRow()
        {
            // Regression coverage for the suppress-guard (the "updating" field): one
            // GridUnitSelector instance is reused across every row the pointer moves over, so
            // moving from a row already picked as Auto onto a still-default (Star) row drives
            // autoButton from checked to unchecked and starButton from unchecked to checked in
            // the SAME Unit-setter call - exactly the reentrant ToggleButton.Checked/Unchecked
            // sequence "updating" exists to make inert. Asserts the real, observable outcome:
            // the newly hovered row ends up reflecting its OWN unit (not clobbered by whatever
            // was picked on the previously hovered row), and hovering alone never opens an undo
            // entry.
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                var undo = surface.DesignContext.Services.GetService<UndoService>();

                provider.LeftRail.SimulateHover(30); // row 0
                provider.LeftRail.UnitSelector.SimulatePick(GridUnitType.Auto);
                surface.UpdateLayout();
                Assert.IsTrue(grid.RowDefinitions[0].Height.IsAuto, "precondition");
                int undoCountAfterPick = undo.UndoActions.Count();

                provider.LeftRail.SimulateHover(90); // row 1 - still default *
                surface.UpdateLayout();

                Assert.IsTrue(grid.RowDefinitions[1].Height.IsStar, "hovering must not force row 1 to Auto");
                Assert.AreEqual(GridUnitType.Star, provider.LeftRail.UnitSelector.Unit);
                Assert.AreEqual(undoCountAfterPick, undo.UndoActions.Count(), "hovering must not open a new undo entry");
            });
        }

        [Test]
        public void TheTwoRailsUnitSelectorsActIndependently()
        {
            // Regression coverage for the per-instance-isolation adaptation (Task 11 built this as
            // a unique RadioButton GroupName per instance; this port instead uses per-instance
            // ToggleButton fields with hand-rolled mutual exclusion, which has no shared/static
            // state at all - but that structural choice is exactly what this test is guarding:
            // if a future change reintroduced RadioButtons sharing one literal GroupName across
            // the two GridUnitSelector siblings GridAdornerProvider adds to the same AdornerPanel,
            // picking a unit on one rail would clobber the other rail's selection.
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);

                provider.LeftRail.SimulateHover(30); // row 0
                provider.LeftRail.UnitSelector.SimulatePick(GridUnitType.Auto);
                surface.UpdateLayout();

                provider.TopRail.SimulateHover(150); // column 1
                provider.TopRail.UnitSelector.SimulatePick(GridUnitType.Pixel);
                surface.UpdateLayout();

                Assert.IsTrue(grid.RowDefinitions[0].Height.IsAuto, "the column pick must not clobber the row pick");
                Assert.AreEqual(GridUnitType.Auto, provider.LeftRail.UnitSelector.Unit);
                Assert.IsTrue(grid.ColumnDefinitions[1].Width.IsAbsolute);
                Assert.AreEqual(GridUnitType.Pixel, provider.TopRail.UnitSelector.Unit);
            });
        }
    }
}
