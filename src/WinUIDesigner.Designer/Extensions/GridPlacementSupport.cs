using System.Collections.Generic;
using System.Diagnostics;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// Provides IPlacementBehavior for Grid: writes Grid.Row/Column/RowSpan/ColumnSpan from a
    /// placement's bounds, derives HorizontalAlignment/VerticalAlignment/Margin/Width/Height for
    /// the target cell, and cleans up on the way out. Ported from the reference's
    /// Extensions/GridPlacementSupport.cs with three changes:
    ///
    /// 1. RowDefinition.Offset/ColumnDefinition.Offset do not exist in WinUI (spec, compiler-
    ///    confirmed). All six reference call sites go through GetRowOffset/GetColumnOffset below,
    ///    which reconstruct it as a running sum of ActualHeight/ActualWidth in definition order -
    ///    WPF's Offset is the distance from the Grid's edge to the *start* of that definition, so
    ///    the running sum stops one short of the requested index.
    /// 2. Attached properties are addressed by (Type, string), not DependencyProperty - this
    ///    port's established convention (DesignItemProperty.cs:89). Ordinary properties go
    ///    through the string indexer.
    /// 3. The reference's PlacementType.PasteItem branch in EnterContainer is dropped - no
    ///    clipboard/paste feature exists anywhere in this port.
    ///
    /// Note on LeaveContainer: its RowDefinition/ColumnDefinition branch is NOT an "is this row
    /// now empty" check (correcting this spec's own paraphrase, see this task's note in the
    /// plan). It fires when the *placed item itself is* a RowDefinition or ColumnDefinition
    /// DesignItem - how GridAdornerProvider (Tasks 11-13) deletes a row or column - and removes
    /// it from the Grid's definitions collection. base.LeaveContainer has already removed it via
    /// ParentProperty by then, so this second removal is a defensive no-op (confirmed: removing
    /// an absent element from this port's CollectionElements does not throw).
    /// </summary>
    [ExtensionFor(typeof(Grid), OverrideExtension = typeof(DefaultPlacementBehavior))]
    public sealed class GridPlacementSupport : SnaplinePlacementBehavior
    {
        Grid grid;
        bool enteredIntoNewContainer;
        GrayOutDesignerExceptActiveArea grayOut;

        const double epsilon = 0.00000001;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            grid = (Grid)ExtendedItem.Component;
        }

        /// <summary>Distance from the Grid's left edge to the start of column
        /// <paramref name="index"/>, reconstructed as a running sum of
        /// ColumnDefinition.ActualWidth - replaces the reference's
        /// grid.ColumnDefinitions[index].Offset (absent in WinUI).</summary>
        double GetColumnOffset(int index)
        {
            if (index <= 0) return 0;
            if (index >= grid.ColumnDefinitions.Count) return grid.ActualWidth;
            double offset = 0;
            for (int i = 0; i < index; i++) offset += grid.ColumnDefinitions[i].ActualWidth;
            return offset;
        }

        /// <summary>Row equivalent of GetColumnOffset, summing RowDefinition.ActualHeight.</summary>
        double GetRowOffset(int index)
        {
            if (index <= 0) return 0;
            if (index >= grid.RowDefinitions.Count) return grid.ActualHeight;
            double offset = 0;
            for (int i = 0; i < index; i++) offset += grid.RowDefinitions[i].ActualHeight;
            return offset;
        }

        int GetColumnIndex(double x)
        {
            if (grid.ColumnDefinitions.Count == 0) return 0;
            for (int i = 1; i < grid.ColumnDefinitions.Count; i++) {
                if (x < GetColumnOffset(i) - epsilon) return i - 1;
            }
            return grid.ColumnDefinitions.Count - 1;
        }

        int GetRowIndex(double y)
        {
            if (grid.RowDefinitions.Count == 0) return 0;
            for (int i = 1; i < grid.RowDefinitions.Count; i++) {
                if (y < GetRowOffset(i) - epsilon) return i - 1;
            }
            return grid.RowDefinitions.Count - 1;
        }

        int GetEndColumnIndex(double x)
        {
            if (grid.ColumnDefinitions.Count == 0) return 0;
            for (int i = 1; i < grid.ColumnDefinitions.Count; i++) {
                if (x <= GetColumnOffset(i) + epsilon) return i - 1;
            }
            return grid.ColumnDefinitions.Count - 1;
        }

        int GetEndRowIndex(double y)
        {
            if (grid.RowDefinitions.Count == 0) return 0;
            for (int i = 1; i < grid.RowDefinitions.Count; i++) {
                if (y <= GetRowOffset(i) + epsilon) return i - 1;
            }
            return grid.RowDefinitions.Count - 1;
        }

        /// <summary>Publishes a snapline at every row/column boundary so a dragged or resized
        /// child snaps to the grid lines themselves. The reference already computes these via a
        /// running sum of ActualHeight/ActualWidth (not Definition.Offset), so this override is
        /// an unmodified port once Task 6's Snapline/SnaplineMargin/AddContainerSnaplines shape
        /// is available (see this task's Interfaces block).</summary>
        protected override void AddContainerSnaplines(Rect containerRect,
                                                      List<SnaplinePlacementBehavior.Snapline> horizontalMap,
                                                      List<SnaplinePlacementBehavior.Snapline> verticalMap)
        {
            var view = (Grid)ExtendedItem.View;
            double offset = 0;
            foreach (RowDefinition r in view.RowDefinitions) {
                offset += r.ActualHeight;
                horizontalMap.Add(new Snapline { RequireOverlap = false, Offset = offset, Start = offset, End = containerRect.Right });
                if (SnaplineMargin > 0) {
                    horizontalMap.Add(new Snapline { RequireOverlap = false, Offset = offset - SnaplineMargin, Start = offset, End = containerRect.Right });
                    horizontalMap.Add(new Snapline { RequireOverlap = false, Offset = offset + SnaplineMargin, Start = offset, End = containerRect.Right });
                }
            }
            offset = 0;
            foreach (ColumnDefinition c in view.ColumnDefinitions) {
                offset += c.ActualWidth;
                verticalMap.Add(new Snapline { RequireOverlap = false, Offset = offset, Start = containerRect.Top, End = containerRect.Bottom });
                if (SnaplineMargin > 0) {
                    verticalMap.Add(new Snapline { RequireOverlap = false, Offset = offset - SnaplineMargin, Start = containerRect.Top, End = containerRect.Bottom });
                    verticalMap.Add(new Snapline { RequireOverlap = false, Offset = offset + SnaplineMargin, Start = containerRect.Top, End = containerRect.Bottom });
                }
            }
        }

        static void SetColumn(DesignItem item, int column, int columnSpan)
        {
            Debug.Assert(item != null && column >= 0 && columnSpan > 0);
            item.Properties.GetAttachedProperty(typeof(Grid), "Column").SetValue(column);
            if (columnSpan == 1) item.Properties.GetAttachedProperty(typeof(Grid), "ColumnSpan").Reset();
            else item.Properties.GetAttachedProperty(typeof(Grid), "ColumnSpan").SetValue(columnSpan);
        }

        static void SetRow(DesignItem item, int row, int rowSpan)
        {
            Debug.Assert(item != null && row >= 0 && rowSpan > 0);
            item.Properties.GetAttachedProperty(typeof(Grid), "Row").SetValue(row);
            if (rowSpan == 1) item.Properties.GetAttachedProperty(typeof(Grid), "RowSpan").Reset();
            else item.Properties.GetAttachedProperty(typeof(Grid), "RowSpan").SetValue(rowSpan);
        }

        static HorizontalAlignment SuggestHorizontalAlignment(Rect itemBounds, Rect availableSpaceRect)
        {
            bool isLeft = itemBounds.Left < availableSpaceRect.Left + availableSpaceRect.Width / 4;
            bool isRight = itemBounds.Right > availableSpaceRect.Right - availableSpaceRect.Width / 4;
            if (isLeft && isRight) return HorizontalAlignment.Stretch;
            if (isRight) return HorizontalAlignment.Right;
            return HorizontalAlignment.Left;
        }

        static VerticalAlignment SuggestVerticalAlignment(Rect itemBounds, Rect availableSpaceRect)
        {
            bool isTop = itemBounds.Top < availableSpaceRect.Top + availableSpaceRect.Height / 4;
            bool isBottom = itemBounds.Bottom > availableSpaceRect.Bottom - availableSpaceRect.Height / 4;
            if (isTop && isBottom) return VerticalAlignment.Stretch;
            if (isBottom) return VerticalAlignment.Bottom;
            return VerticalAlignment.Top;
        }

        public override void EnterContainer(PlacementOperation operation)
        {
            enteredIntoNewContainer = true;
            // Ensures the ActualWidth/ActualHeight values the offset reconstruction reads are
            // current for the Grid the item is arriving into.
            grid.UpdateLayout();
            base.EnterContainer(operation);
        }

        public override void EndPlacement(PlacementOperation operation)
        {
            GrayOutDesignerExceptActiveArea.Stop(ref grayOut);
            enteredIntoNewContainer = false;
            base.EndPlacement(operation);
        }

        public override void SetPosition(PlacementInformation info)
        {
            base.SetPosition(info);

            int leftColumnIndex = GetColumnIndex(info.Bounds.Left);
            int rightColumnIndex = GetEndColumnIndex(info.Bounds.Right);
            if (rightColumnIndex < leftColumnIndex) rightColumnIndex = leftColumnIndex;
            SetColumn(info.Item, leftColumnIndex, rightColumnIndex - leftColumnIndex + 1);

            int topRowIndex = GetRowIndex(info.Bounds.Top);
            int bottomRowIndex = GetEndRowIndex(info.Bounds.Bottom);
            if (bottomRowIndex < topRowIndex) bottomRowIndex = topRowIndex;
            SetRow(info.Item, topRowIndex, bottomRowIndex - topRowIndex + 1);

            var availableSpaceRect = new Rect(
                new Point(GetColumnOffset(leftColumnIndex), GetRowOffset(topRowIndex)),
                new Point(GetColumnOffset(rightColumnIndex + 1), GetRowOffset(bottomRowIndex + 1)));

            if (info.Item == Services.Selection.PrimarySelection) {
                if (grayOut != null) grayOut.AnimateActiveAreaRectTo(availableSpaceRect);
                else GrayOutDesignerExceptActiveArea.Start(ref grayOut, Services, ExtendedItem.View, availableSpaceRect);
            }

            var ha = info.Item.Properties["HorizontalAlignment"].GetConvertedValueOnInstance<HorizontalAlignment>();
            var va = info.Item.Properties["VerticalAlignment"].GetConvertedValueOnInstance<VerticalAlignment>();
            if (enteredIntoNewContainer) {
                ha = SuggestHorizontalAlignment(info.Bounds, availableSpaceRect);
                va = SuggestVerticalAlignment(info.Bounds, availableSpaceRect);
            }
            info.Item.Properties["HorizontalAlignment"].SetValue(ha);
            info.Item.Properties["VerticalAlignment"].SetValue(va);

            var margin = new Thickness(0, 0, 0, 0);
            if (ha == HorizontalAlignment.Left || ha == HorizontalAlignment.Stretch)
                margin.Left = info.Bounds.Left - GetColumnOffset(leftColumnIndex);
            if (va == VerticalAlignment.Top || va == VerticalAlignment.Stretch)
                margin.Top = info.Bounds.Top - GetRowOffset(topRowIndex);
            if (ha == HorizontalAlignment.Right || ha == HorizontalAlignment.Stretch)
                margin.Right = GetColumnOffset(rightColumnIndex + 1) - info.Bounds.Right;
            if (va == VerticalAlignment.Bottom || va == VerticalAlignment.Stretch)
                margin.Bottom = GetRowOffset(bottomRowIndex + 1) - info.Bounds.Bottom;
            info.Item.Properties["Margin"].SetValue(margin);

            if (ha == HorizontalAlignment.Stretch) info.Item.Properties["Width"].Reset();
            if (va == VerticalAlignment.Stretch) info.Item.Properties["Height"].Reset();
        }

        public override void LeaveContainer(PlacementOperation operation)
        {
            GrayOutDesignerExceptActiveArea.Stop(ref grayOut);
            // Final-review fix (I1): PlacementOperation.ChangeContainer calls LeaveContainer on
            // the OLD container's behavior without ever calling EndPlacement on it (only the
            // FINAL container gets EndPlacement, at Commit/Abort time) - so without resetting the
            // latch here too, dragging an item into this Grid and back out again (without ever
            // committing/aborting while this Grid is the current container) left
            // enteredIntoNewContainer stuck true forever, silently overwriting alignment on the
            // next unrelated placement/resize in this same Grid.
            enteredIntoNewContainer = false;
            base.LeaveContainer(operation);
            foreach (var info in operation.PlacedItems) {
                if (info.Item.ComponentType == typeof(ColumnDefinition)) {
                    ExtendedItem.Properties["ColumnDefinitions"].CollectionElements.Remove(info.Item);
                } else if (info.Item.ComponentType == typeof(RowDefinition)) {
                    ExtendedItem.Properties["RowDefinitions"].CollectionElements.Remove(info.Item);
                } else {
                    info.Item.Properties.GetAttachedProperty(typeof(Grid), "Row").Reset();
                    info.Item.Properties.GetAttachedProperty(typeof(Grid), "Column").Reset();
                    info.Item.Properties.GetAttachedProperty(typeof(Grid), "RowSpan").Reset();
                    info.Item.Properties.GetAttachedProperty(typeof(Grid), "ColumnSpan").Reset();

                    var ha = info.Item.Properties["HorizontalAlignment"].GetConvertedValueOnInstance<HorizontalAlignment>();
                    var va = info.Item.Properties["VerticalAlignment"].GetConvertedValueOnInstance<VerticalAlignment>();
                    if (ha == HorizontalAlignment.Stretch) info.Item.Properties["Width"].SetValue(info.Bounds.Width);
                    if (va == VerticalAlignment.Stretch) info.Item.Properties["Height"].SetValue(info.Bounds.Height);
                }
            }
        }
    }
}
