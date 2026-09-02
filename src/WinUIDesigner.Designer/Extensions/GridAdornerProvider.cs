using System;
using System.Collections.Generic;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Shows the row/column rails on a Grid that is selected, or whose direct child is
    /// selected.</summary>
    [ExtensionFor(typeof(Grid))]
    [ExtensionServer(typeof(LogicalOrExtensionServer<PrimarySelectionExtensionServer, PrimarySelectionParentExtensionServer>))]
    public sealed class GridAdornerProvider : AdornerProvider
    {
        // Unlike 4a's thumbs, which all use the ready-made RelativePlacement, a rail splitter sits
        // at a specific row/column boundary rather than a fixed corner/edge alignment - so it needs
        // its own AdornerPlacement, mirroring the reference's RowSplitterPlacement /
        // ColumnSplitterPlacement. The reference captured row.Offset; WinUI has no Offset, so the
        // index is captured instead and the running sum is recomputed on every Arrange, which also
        // keeps the splitter correct while a drag resizes the rows.
        sealed class RowSplitterPlacement : AdornerPlacement
        {
            readonly Grid grid;
            readonly int rowIndex;
            public RowSplitterPlacement(Grid grid, int rowIndex) { this.grid = grid; this.rowIndex = rowIndex; }

            public override void Arrange(AdornerPanel panel, UIElement adorner, Size adornedElementSize)
            {
                double offset = GridRailAdorner.GetOffset(grid, Orientation.Vertical, rowIndex);
                adorner.Arrange(new Rect(-(GridRailAdorner.RailSize + GridRailAdorner.RailDistance),
                                         offset - GridRailAdorner.SplitterWidth / 2,
                                         GridRailAdorner.RailSize + GridRailAdorner.RailDistance + adornedElementSize.Width,
                                         GridRailAdorner.SplitterWidth));
            }
        }

        sealed class ColumnSplitterPlacement : AdornerPlacement
        {
            readonly Grid grid;
            readonly int columnIndex;
            public ColumnSplitterPlacement(Grid grid, int columnIndex) { this.grid = grid; this.columnIndex = columnIndex; }

            public override void Arrange(AdornerPanel panel, UIElement adorner, Size adornedElementSize)
            {
                double offset = GridRailAdorner.GetOffset(grid, Orientation.Horizontal, columnIndex);
                adorner.Arrange(new Rect(offset - GridRailAdorner.SplitterWidth / 2,
                                         -(GridRailAdorner.RailSize + GridRailAdorner.RailDistance),
                                         GridRailAdorner.SplitterWidth,
                                         GridRailAdorner.RailSize + GridRailAdorner.RailDistance + adornedElementSize.Height));
            }
        }

        readonly AdornerPanel adornerPanel = new AdornerPanel();
        readonly List<GridSplitterAdorner> splitterList = new List<GridSplitterAdorner>();
        GridRailAdorner topBar, leftBar;
        bool requireRebuild;

        internal GridRailAdorner LeftRail { get { return leftBar; } }
        internal GridRailAdorner TopRail { get { return topBar; } }
        internal IList<GridSplitterAdorner> Splitters { get { return splitterList; } }

        protected override void OnInitialized()
        {
            leftBar = new GridRailAdorner(this.ExtendedItem, adornerPanel, Orientation.Vertical);
            topBar = new GridRailAdorner(this.ExtendedItem, adornerPanel, Orientation.Horizontal);

            var rp = new RelativePlacement(HorizontalAlignment.Left, VerticalAlignment.Stretch);
            rp.XOffset -= GridRailAdorner.RailDistance;
            AdornerPanel.SetPlacement(leftBar, rp);
            rp = new RelativePlacement(HorizontalAlignment.Stretch, VerticalAlignment.Top);
            rp.YOffset -= GridRailAdorner.RailDistance;
            AdornerPanel.SetPlacement(topBar, rp);

            adornerPanel.Children.Add(leftBar);
            adornerPanel.Children.Add(topBar);
            this.Adorners.Add(adornerPanel);

            RequestRebuild();
            this.ExtendedItem.PropertyChanged += OnPropertyChanged;

            base.OnInitialized();
        }

        protected override void OnRemove()
        {
            this.ExtendedItem.PropertyChanged -= OnPropertyChanged;
            base.OnRemove();
        }

        void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "RowDefinitions" || e.PropertyName == "ColumnDefinitions")
                RequestRebuild();
        }

        /// <summary>
        /// The reference delayed this with DispatcherPriority.Loaded for two reasons: coalescing
        /// several collection changes into one rebuild, and guaranteeing a new row/column's layout
        /// had settled before reading it. WinUI's DispatcherQueuePriority has no Loaded member
        /// (spec Known gap #3) and .Normal/.Low carry no "after layout" promise, so the two
        /// concerns are separated: the enqueue only coalesces, and Rebuild calls
        /// grid.UpdateLayout() itself to get the settled-layout guarantee. Measured: reading
        /// ActualHeight straight after inserting a RowDefinition returns a stale 0; after an
        /// explicit UpdateLayout it is correct.
        /// </summary>
        void RequestRebuild()
        {
            if (requireRebuild) return;
            requireRebuild = true;

            var queue = this.ExtendedItem.View != null ? this.ExtendedItem.View.DispatcherQueue : null;
            if (queue == null || !queue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Rebuild))
                Rebuild();
        }

        /// <summary>Test seam: a test body runs inside a single dispatcher turn, so the enqueued
        /// Rebuild can never fire during it. Tests call this instead.</summary>
        internal void FlushPendingRebuild()
        {
            if (requireRebuild) Rebuild();
        }

        void Rebuild()
        {
            requireRebuild = false;
            var grid = this.ExtendedItem.Component as Grid;
            if (grid == null) return;

            foreach (GridSplitterAdorner splitter in splitterList)
                adornerPanel.Children.Remove(splitter);
            splitterList.Clear();

            grid.UpdateLayout();

            // The live Grid and the model collection can disagree for a tick while a collection
            // change is still propagating; take the shorter of the two so an index is never out
            // of range on either side.
            IList<DesignItem> rows = this.ExtendedItem.Properties["RowDefinitions"].CollectionElements;
            for (int i = 1; i < grid.RowDefinitions.Count && i < rows.Count; i++) {
                var splitter = new GridRowSplitterAdorner(leftBar, this.ExtendedItem, rows[i - 1], rows[i]);
                AdornerPanel.SetPlacement(splitter, new RowSplitterPlacement(grid, i));
                adornerPanel.Children.Add(splitter);
                splitterList.Add(splitter);
            }

            IList<DesignItem> columns = this.ExtendedItem.Properties["ColumnDefinitions"].CollectionElements;
            for (int i = 1; i < grid.ColumnDefinitions.Count && i < columns.Count; i++) {
                var splitter = new GridColumnSplitterAdorner(topBar, this.ExtendedItem, columns[i - 1], columns[i]);
                AdornerPanel.SetPlacement(splitter, new ColumnSplitterPlacement(grid, i));
                adornerPanel.Children.Add(splitter);
                splitterList.Add(splitter);
            }

            leftBar.RefreshLabels();
            topBar.RefreshLabels();
        }
    }
}
