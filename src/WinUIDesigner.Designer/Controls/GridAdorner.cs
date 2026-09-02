using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;

namespace WinUIDesigner.Designer.Controls
{
    /// <summary>
    /// The blue bar drawn beside a selected Grid's rows/columns; clicking it splits the row or
    /// column under the pointer. Ported from the reference's GridRailAdorner with one structural
    /// change: WinUI has no OnRender/DrawingContext/FormattedText, so the per-row/column size
    /// captions the reference painted immediately are ordinary TextBlocks in a non-hit-testable
    /// child Canvas, repositioned by RefreshLabels(). RefreshLabels must never be called from a
    /// layout override - mutating Canvas.Children during ArrangeOverride throws a reentrancy
    /// COMException out of UpdateLayout; it is called at the same explicit points the reference
    /// called InvalidateVisual(). The reference's row.Offset / column.Offset (absent from
    /// WinUI) becomes the GetOffset running sum. Base is Grid, not FrameworkElement: an
    /// unstyled Grid gives Background/BorderBrush and IsTabStop for Escape-reachable focus, the
    /// same choice Controls/Thumbs/DesignerThumb made.
    /// </summary>
    public class GridRailAdorner : Grid
    {
        readonly DesignItem gridItem;
        readonly Grid grid;
        readonly AdornerPanel adornerPanel;
        readonly GridSplitterAdorner previewAdorner;
        readonly GridUnitSelector unitSelector;
        readonly Orientation orientation;
        readonly Canvas labelCanvas = new Canvas { IsHitTestVisible = false };
        bool displayUnitSelector;

        public const double RailSize = 10;
        public const double RailDistance = 6;
        public const double SplitterWidth = 10;

        public GridRailAdorner(DesignItem gridItem, AdornerPanel adornerPanel, Orientation orientation)
        {
            if (gridItem == null) throw new ArgumentNullException(nameof(gridItem));
            if (adornerPanel == null) throw new ArgumentNullException(nameof(adornerPanel));

            this.gridItem = gridItem;
            this.grid = (Grid)gridItem.Component;
            this.adornerPanel = adornerPanel;
            this.orientation = orientation;

            // Same ARGB the reference's frozen bgBrush used. Brushes.X doesn't exist in WinUI
            // and neither does SolidColorBrush.Freeze.
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x35, 0x1E, 0x90, 0xFF));
            IsTabStop = true;
            Children.Add(labelCanvas);
            // UIElement.Cursor isn't public in WinUI; ProtectedCursor is, and this class is the
            // subclass - same InputSystemCursorShape replacement for CursorProperty.OverrideMetadata
            // that Phase 4a established.
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);

            this.unitSelector = new GridUnitSelector(this);
            adornerPanel.Children.Add(unitSelector);

            if (orientation == Orientation.Horizontal) {
                this.Height = RailSize;
                previewAdorner = new GridColumnSplitterAdorner(this, gridItem, null, null);
            } else {
                this.Width = RailSize;
                previewAdorner = new GridRowSplitterAdorner(this, gridItem, null, null);
            }
            previewAdorner.IsPreview = true;
            previewAdorner.IsHitTestVisible = false;
            unitSelector.Orientation = orientation;

            // WinUI has no tunneling Preview* events, so handledEventsToo: true - the same idiom
            // Controls/DragListener.cs uses.
            AddHandler(UIElement.PointerEnteredEvent, (PointerEventHandler)OnRailPointerEntered, true);
            AddHandler(UIElement.PointerMovedEvent, (PointerEventHandler)OnRailPointerMoved, true);
            AddHandler(UIElement.PointerExitedEvent, (PointerEventHandler)OnRailPointerExited, true);
            AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)OnRailPointerPressed, true);
        }

        /// <summary>Test seam: the size-unit captions this rail currently shows, in row/column
        /// order. Replaces the reference's OnRender/FormattedText output, which had no readable
        /// equivalent at all.</summary>
        internal IEnumerable<string> LabelTexts {
            get { return labelCanvas.Children.OfType<TextBlock>().Select(t => t.Text); }
        }

        internal GridSplitterAdorner PreviewAdorner { get { return previewAdorner; } }
        internal GridUnitSelector UnitSelector { get { return unitSelector; } }

        /// <summary>Replacement for RowDefinition.Offset / ColumnDefinition.Offset, neither of
        /// which exists in WinUI: the running sum of ActualHeight / ActualWidth over the
        /// definitions before <paramref name="index"/>.</summary>
        internal static double GetOffset(Grid grid, Orientation orientation, int index)
        {
            double offset = 0;
            if (orientation == Orientation.Vertical) {
                for (int i = 0; i < index && i < grid.RowDefinitions.Count; i++) offset += grid.RowDefinitions[i].ActualHeight;
            } else {
                for (int i = 0; i < index && i < grid.ColumnDefinitions.Count; i++) offset += grid.ColumnDefinitions[i].ActualWidth;
            }
            return offset;
        }

        /// <summary>The reference's FirstOrDefault(r => pos >= r.Offset && pos <= r.Offset + r.Actual*),
        /// reconstructed against the running sum. Returns -1 when the position is past the last
        /// row/column.</summary>
        int IndexAt(double position)
        {
            double offset = 0;
            if (orientation == Orientation.Vertical) {
                for (int i = 0; i < grid.RowDefinitions.Count; i++) {
                    double h = grid.RowDefinitions[i].ActualHeight;
                    if (position >= offset && position <= offset + h) return i;
                    offset += h;
                }
            } else {
                for (int i = 0; i < grid.ColumnDefinitions.Count; i++) {
                    double w = grid.ColumnDefinitions[i].ActualWidth;
                    if (position >= offset && position <= offset + w) return i;
                    offset += w;
                }
            }
            return -1;
        }

        public void RefreshLabels()
        {
            labelCanvas.Children.Clear();
            var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
            if (orientation == Orientation.Horizontal) {
                double offset = 0;
                foreach (var column in grid.ColumnDefinitions) {
                    double w = column.ActualWidth;
                    if (w >= 0) {
                        var label = CreateLabel(GridLengthToText(column.Width));
                        label.Measure(infinite);
                        Canvas.SetLeft(label, offset + w / 2 - label.DesiredSize.Width / 2);
                        Canvas.SetTop(label, RailSize / 2 - label.DesiredSize.Height / 2);
                        labelCanvas.Children.Add(label);
                    }
                    offset += w;
                }
            } else {
                double offset = 0;
                foreach (var row in grid.RowDefinitions) {
                    double h = row.ActualHeight;
                    if (h >= 0) {
                        var label = CreateLabel(GridLengthToText(row.Height));
                        label.Measure(infinite);
                        // The reference rotated the whole drawing context by -90 before drawing the
                        // row captions; rotating each label about its own centre puts the same text
                        // at the same place without an immediate-mode transform stack.
                        label.RenderTransformOrigin = new Point(0.5, 0.5);
                        label.RenderTransform = new RotateTransform { Angle = -90 };
                        Canvas.SetLeft(label, RailSize / 2 - label.DesiredSize.Width / 2);
                        Canvas.SetTop(label, offset + h / 2 - label.DesiredSize.Height / 2);
                        labelCanvas.Children.Add(label);
                    }
                    offset += h;
                }
            }
        }

        static TextBlock CreateLabel(string text)
        {
            return new TextBlock { Text = text, FontSize = 10, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black), IsHitTestVisible = false };
        }

        double GetCoordinate(Point point)
        {
            return orientation == Orientation.Vertical ? point.Y : point.X;
        }

        #region hover / preview
        void OnRailPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            HoverAt(GetCoordinate(e.GetCurrentPoint(this).Position));
            if (!adornerPanel.Children.Contains(previewAdorner))
                adornerPanel.Children.Add(previewAdorner);
        }

        void OnRailPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            HoverAt(GetCoordinate(e.GetCurrentPoint(this).Position));
        }

        void OnRailPointerExited(object sender, PointerRoutedEventArgs e)
        {
            unitSelector.Visibility = Visibility.Collapsed;
            displayUnitSelector = false;
            if (adornerPanel.Children.Contains(previewAdorner))
                adornerPanel.Children.Remove(previewAdorner);
        }

        /// <summary>Test seam - see SimulateRailPress.</summary>
        internal void SimulateHover(double insertionPosition)
        {
            if (!adornerPanel.Children.Contains(previewAdorner))
                adornerPanel.Children.Add(previewAdorner);
            HoverAt(insertionPosition);
        }

        /// <summary>Parks the ghost splitter where a click would split and positions
        /// GridUnitSelector over the hovered row/column, using the same RelativePlacement
        /// geometry the reference computed in OnMouseMove.</summary>
        void HoverAt(double insertionPosition)
        {
            var rp = new RelativePlacement();
            var rpUnitSelector = new RelativePlacement();
            int index = IndexAt(insertionPosition);

            if (orientation == Orientation.Vertical) {
                rp.XOffset = -(RailSize + RailDistance);
                rp.WidthOffset = RailSize + RailDistance;
                rp.WidthRelativeToContentWidth = 1;
                rp.HeightOffset = SplitterWidth;
                rp.YOffset = insertionPosition - SplitterWidth / 2;

                if (index >= 0) {
                    var row = grid.RowDefinitions[index];
                    DesignItem component = gridItem.Services.Component.GetDesignItem(row);
                    rpUnitSelector.XOffset = -(RailSize + RailDistance) * 2.75 - 6;
                    rpUnitSelector.WidthOffset = RailSize + RailDistance;
                    rpUnitSelector.WidthRelativeToContentWidth = 1;
                    rpUnitSelector.HeightOffset = 55;
                    rpUnitSelector.YOffset = GetOffset(grid, orientation, index) + row.ActualHeight / 2 - 25;
                    unitSelector.SelectedItem = component;
                    if (component != null)
                        unitSelector.Unit = component.Properties["Height"].GetConvertedValueOnInstance<GridLength>().GridUnitType;
                    displayUnitSelector = true;
                } else {
                    displayUnitSelector = false;
                }
            } else {
                rp.YOffset = -(RailSize + RailDistance);
                rp.HeightOffset = RailSize + RailDistance;
                rp.HeightRelativeToContentHeight = 1;
                rp.WidthOffset = SplitterWidth;
                rp.XOffset = insertionPosition - SplitterWidth / 2;

                if (index >= 0) {
                    var column = grid.ColumnDefinitions[index];
                    DesignItem component = gridItem.Services.Component.GetDesignItem(column);
                    rpUnitSelector.YOffset = -(RailSize + RailDistance) * 2.20 - 6;
                    rpUnitSelector.HeightOffset = RailSize + RailDistance;
                    rpUnitSelector.HeightRelativeToContentHeight = 1;
                    rpUnitSelector.WidthOffset = 75;
                    rpUnitSelector.XOffset = GetOffset(grid, orientation, index) + column.ActualWidth / 2 - 35;
                    unitSelector.SelectedItem = component;
                    if (component != null)
                        unitSelector.Unit = component.Properties["Width"].GetConvertedValueOnInstance<GridLength>().GridUnitType;
                    displayUnitSelector = true;
                } else {
                    displayUnitSelector = false;
                }
            }

            AdornerPanel.SetPlacement(previewAdorner, rp);
            unitSelector.Visibility = displayUnitSelector ? Visibility.Visible : Visibility.Collapsed;
            if (displayUnitSelector)
                AdornerPanel.SetPlacement(unitSelector, rpUnitSelector);
        }
        #endregion

        #region create a new row/column
        void OnRailPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Only a primary/left press splits - a right-click must not, same guard DragListener
            // grew in 4a's final review.
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Focus(FocusState.Pointer);
            // The rail is arranged flush with the Grid along its long axis (RelativePlacement
            // Left/Stretch and Stretch/Top), so a rail-local coordinate IS the grid-local one -
            // exactly what the reference's e.GetPosition(this) produced.
            SplitAt(GetCoordinate(e.GetCurrentPoint(this).Position));
        }

        /// <summary>Test seam: drives the same split the pointer handler does.
        /// PointerRoutedEventArgs has no public constructor, so tests cannot synthesize a
        /// press - same reason DragListener exposes SimulateDown/Move/Up.</summary>
        internal void SimulateRailPress(double insertionPosition)
        {
            SplitAt(insertionPosition);
        }

        void SplitAt(double insertionPosition)
        {
            if (adornerPanel.Children.Contains(previewAdorner))
                adornerPanel.Children.Remove(previewAdorner);

            string collectionName = orientation == Orientation.Vertical ? "RowDefinitions" : "ColumnDefinitions";
            string sizeName = orientation == Orientation.Vertical ? "Height" : "Width";
            string indexName = orientation == Orientation.Vertical ? "Row" : "Column";
            string spanName = orientation == Orientation.Vertical ? "RowSpan" : "ColumnSpan";
            string title = orientation == Orientation.Vertical ? "Split grid row" : "Split grid column";

            DesignItemProperty collection = gridItem.Properties[collectionName];
            DesignItem current = null;

            // ONE ChangeGroup for the definition insert, both size writes and every sibling
            // rewrite - a single undo entry reverts the whole split.
            using (ChangeGroup changeGroup = gridItem.OpenGroup(title)) {
                if (collection.CollectionElements.Count == 0) {
                    DesignItem first = gridItem.Services.Component.RegisterComponentForDesigner(
                        orientation == Orientation.Vertical ? (object)new RowDefinition() : new ColumnDefinition());
                    collection.CollectionElements.Add(first);
                    grid.UpdateLayout(); // let WinUI assign the new definition's ActualHeight/Width
                    current = first;
                } else {
                    int hit = IndexAt(insertionPosition);
                    if (hit >= 0) current = collection.CollectionElements[hit];
                }

                if (current == null)
                    current = collection.CollectionElements[collection.CollectionElements.Count - 1];
                unitSelector.SelectedItem = current;

                int count = orientation == Orientation.Vertical ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count;
                double offset = 0;
                for (int i = 0; i < count; i++) {
                    double actual = orientation == Orientation.Vertical
                        ? grid.RowDefinitions[i].ActualHeight
                        : grid.ColumnDefinitions[i].ActualWidth;
                    if (offset > insertionPosition || offset + actual < insertionPosition) {
                        offset += actual;
                        continue;
                    }

                    GridLength oldLength = collection.CollectionElements[i].Properties[sizeName].GetConvertedValueOnInstance<GridLength>();
                    GridLength newLength1, newLength2;
                    SplitLength(oldLength, insertionPosition - offset, actual, out newLength1, out newLength2);

                    DesignItem newDefinition = gridItem.Services.Component.RegisterComponentForDesigner(
                        orientation == Orientation.Vertical ? (object)new RowDefinition() : new ColumnDefinition());
                    collection.CollectionElements.Insert(i + 1, newDefinition);
                    collection.CollectionElements[i].Properties[sizeName].SetValue(newLength1);
                    newDefinition.Properties[sizeName].SetValue(newLength2);
                    // Layout must settle before FixIndicesAfterSplit reads child positions - there
                    // is no DispatcherPriority.Loaded in WinUI, so force it (spec Known gap #3).
                    grid.UpdateLayout();
                    FixIndicesAfterSplit(i, indexName, spanName, insertionPosition);
                    grid.UpdateLayout();
                    changeGroup.Commit();
                    break;
                }
                // No matching row/column: the group is never committed, so leaving the using block
                // disposes and aborts it - the reference's behaviour, kept.
            }
            RefreshLabels();
        }

        /// <summary>The reference's FixIndicesAfterSplit: hold every child's visual position stable
        /// across the split. WinUI substitutions - View.TranslatePoint becomes
        /// TransformToVisual(...).TransformPoint(...), and every DependencyProperty-indexed
        /// Properties[...]/GetAttachedProperty(...) becomes this port's string-keyed form.</summary>
        void FixIndicesAfterSplit(int splitIndex, string indexName, string spanName, double insertionPosition)
        {
            foreach (DesignItem child in gridItem.Properties["Children"].CollectionElements) {
                if (child.View == null) continue;
                Point topLeft = child.View.TransformToVisual(grid).TransformPoint(new Point(0, 0));
                var margin = child.Properties["Margin"].GetConvertedValueOnInstance<Thickness>();
                var start = child.Properties.GetAttachedProperty(typeof(Grid), indexName).GetConvertedValueOnInstance<int>();
                var span = child.Properties.GetAttachedProperty(typeof(Grid), spanName).GetConvertedValueOnInstance<int>();

                if (start <= splitIndex && splitIndex < start + span) {
                    if (orientation == Orientation.Horizontal) {
                        var width = child.Properties["ActualWidth"].GetConvertedValueOnInstance<double>();
                        if (insertionPosition >= topLeft.X + width) continue;
                        if (insertionPosition > topLeft.X) {
                            child.Properties.GetAttachedProperty(typeof(Grid), spanName).SetValue(span + 1);
                        } else {
                            child.Properties.GetAttachedProperty(typeof(Grid), indexName).SetValue(start + 1);
                            margin.Left = topLeft.X - insertionPosition;
                            child.Properties["Margin"].SetValue(margin);
                        }
                    } else {
                        var height = child.Properties["ActualHeight"].GetConvertedValueOnInstance<double>();
                        if (insertionPosition >= topLeft.Y + height) continue;
                        if (insertionPosition > topLeft.Y) {
                            child.Properties.GetAttachedProperty(typeof(Grid), spanName).SetValue(span + 1);
                        } else {
                            child.Properties.GetAttachedProperty(typeof(Grid), indexName).SetValue(start + 1);
                            margin.Top = topLeft.Y - insertionPosition;
                            child.Properties["Margin"].SetValue(margin);
                        }
                    }
                } else if (start > splitIndex) {
                    child.Properties.GetAttachedProperty(typeof(Grid), indexName).SetValue(start + 1);
                }
            }
        }

        internal static void SplitLength(GridLength oldLength, double insertionPosition, double oldActualValue,
                                         out GridLength newLength1, out GridLength newLength2)
        {
            if (oldLength.IsAuto)
                oldLength = new GridLength(oldActualValue);
            // Guard the reference doesn't have: a zero-sized row/column would divide by zero here.
            double percentage = oldActualValue == 0 ? 0 : insertionPosition / oldActualValue;
            newLength1 = new GridLength(oldLength.Value * percentage, oldLength.GridUnitType);
            newLength2 = new GridLength(oldLength.Value - newLength1.Value, oldLength.GridUnitType);
        }
        #endregion

        internal static string GridLengthToText(GridLength len)
        {
            switch (len.GridUnitType) {
                case GridUnitType.Auto: return "Auto";
                case GridUnitType.Star: return len.Value == 1 ? "*" : Math.Round(len.Value, 2) + "*";
                case GridUnitType.Pixel: return Math.Round(len.Value, 2) + "px";
            }
            return string.Empty;
        }

        /// <summary>Called by GridUnitSelector when a unit button is picked. A single SetValue is
        /// already one undo entry (UndoService.Execute), so no ChangeGroup here - the reference
        /// does the same. Guarded against a null SelectedItem, which the reference asserted on.</summary>
        public void SetGridLengthUnit(GridUnitType unit)
        {
            DesignItem item = unitSelector.SelectedItem;
            if (item == null) return;
            grid.UpdateLayout();
            SetGridLengthUnit(unit, item, orientation == Orientation.Vertical ? "Height" : "Width");
            grid.UpdateLayout();
            RefreshLabels();
        }

        static void SetGridLengthUnit(GridUnitType unit, DesignItem item, string propertyName)
        {
            DesignItemProperty itemProperty = item.Properties[propertyName];
            GridLength oldValue = itemProperty.GetConvertedValueOnInstance<GridLength>();
            // The reference's GetNewGridLength: Auto discards the value, every other unit keeps it.
            GridLength value = unit == GridUnitType.Auto ? GridLength.Auto : new GridLength(oldValue.Value, unit);
            if (!value.Equals(oldValue))
                itemProperty.SetValue(value);
        }
    }

    /// <summary>
    /// Drag an existing row/column boundary to resize the two definitions either side of it.
    /// This is a SECOND, INDEPENDENT drag life cycle - deliberately NOT routed through
    /// Controls/DragListener (the primitive 4a's resize/rotate/skew thumbs share): it owns its
    /// pointer capture, its own ChangeGroup and its own Escape-cancel, exactly as the reference's
    /// GridSplitterAdorner did against WPF's mouse capture. It does follow this port's established
    /// pointer idioms - AddHandler(..., handledEventsToo: true) because WinUI has no tunneling
    /// Preview* events, Focus(FocusState.Pointer) so KeyDown/Escape is reachable while captured,
    /// and it reuses DragListener's MinimumHorizontal/VerticalDragDistance constants rather than
    /// inventing a second threshold. Unlike DragListener it also handles PointerCaptureLost (the
    /// reference's OnLostMouseCapture), so an OS-revoked capture aborts rather than stranding an
    /// open ChangeGroup. Base is Grid, not Control: Control's OnPointerPressed virtuals do not
    /// exist on Panel, so handlers are attached rather than overridden - same as DesignerThumb.
    /// </summary>
    public abstract class GridSplitterAdorner : Grid
    {
        public static readonly DependencyProperty IsPreviewProperty =
            DependencyProperty.Register("IsPreview", typeof(bool), typeof(GridSplitterAdorner), new PropertyMetadata(false));

        protected readonly Grid grid;
        protected readonly DesignItem gridItem;
        protected readonly DesignItem firstDefinition, secondDefinition;
        protected readonly GridRailAdorner rail;

        ChangeGroup activeChangeGroup;
        double startPos;
        bool isDown;
        Pointer capturedPointer;

        internal GridSplitterAdorner(GridRailAdorner rail, DesignItem gridItem, DesignItem firstDefinition, DesignItem secondDefinition)
        {
            if (gridItem == null) throw new ArgumentNullException(nameof(gridItem));
            this.grid = (Grid)gridItem.Component;
            this.gridItem = gridItem;
            this.firstDefinition = firstDefinition;
            this.secondDefinition = secondDefinition;
            this.rail = rail;

            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x60, 0x1E, 0x90, 0xFF));
            IsTabStop = true;
            ProtectedCursor = InputSystemCursor.Create(CursorShape);

            AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)OnSplitterPointerPressed, true);
            AddHandler(UIElement.PointerMovedEvent, (PointerEventHandler)OnSplitterPointerMoved, true);
            AddHandler(UIElement.PointerReleasedEvent, (PointerEventHandler)OnSplitterPointerReleased, true);
            AddHandler(UIElement.PointerCaptureLostEvent, (PointerEventHandler)OnSplitterPointerCaptureLost, true);
            KeyDown += OnSplitterKeyDown;
        }

        public bool IsPreview {
            get { return (bool)GetValue(IsPreviewProperty); }
            set { SetValue(IsPreviewProperty, value); }
        }

        public bool IsDragging { get { return isDown; } }
        public bool HasOpenChangeGroup { get { return activeChangeGroup != null; } }

        protected abstract double GetCoordinate(Point point);
        protected abstract void RememberOriginalSize();
        protected abstract string RowColumnSizePropertyName { get; }
        protected abstract InputSystemCursorShape CursorShape { get; }

        protected GridLength original1, original2;
        protected double originalPixelSize1, originalPixelSize2;

        /// <summary>The ghost preview instance is constructed with null definitions; the reference
        /// would NullReferenceException in RememberOriginalSize if it were ever dragged. Guard it.
        /// Named `new` because it otherwise hides UIElement.CanDrag (OS drag-and-drop, unrelated to
        /// this splitter's own pointer-capture drag) - CS0108, harmless but silenced for clarity.</summary>
        new bool CanDrag {
            get { return !IsPreview && firstDefinition != null && secondDefinition != null; }
        }

        void OnSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (!CanDrag) return;
            if (CapturePointer(e.Pointer)) {
                capturedPointer = e.Pointer;
                Focus(FocusState.Pointer);
                BeginAt(GetCoordinate(e.GetCurrentPoint(grid).Position));
            }
        }

        void OnSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!isDown || e.Pointer.PointerId != capturedPointer?.PointerId) return;
            MoveTo(GetCoordinate(e.GetCurrentPoint(grid).Position));
        }

        void OnSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!isDown || e.Pointer.PointerId != capturedPointer?.PointerId) return;
            Commit();
        }

        void OnSplitterPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (!isDown) return;
            Cancel();
        }

        void OnSplitterKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (isDown && e.Key == VirtualKey.Escape) {
                Cancel();
                e.Handled = true;
            }
        }

        void BeginAt(double position)
        {
            startPos = position;
            isDown = true;
        }

        void MoveTo(double position)
        {
            if (!isDown) return;
            if (activeChangeGroup == null) {
                // The reference compared against SystemParameters.Minimum*DragDistance, which has
                // no WinUI equivalent to read at runtime; DragListener already hardcodes WPF's
                // defaults as public consts (4a), so reuse them instead of a second magic number.
                double threshold = GetCoordinate(new Point(DragListener.MinimumHorizontalDragDistance,
                                                           DragListener.MinimumVerticalDragDistance));
                if (Math.Abs(position - startPos) < threshold) return;
                activeChangeGroup = gridItem.OpenGroup("Change grid row/column size");
                RememberOriginalSize();
            }
            ChangeSize(position - startPos);
        }

        void ChangeSize(double delta)
        {
            if (delta < -originalPixelSize1) delta = -originalPixelSize1;
            if (delta > originalPixelSize2) delta = originalPixelSize2;

            if (original1.IsAuto) original1 = new GridLength(originalPixelSize1);
            if (original2.IsAuto) original2 = new GridLength(originalPixelSize2);

            GridLength new1 = original1.IsStar && originalPixelSize1 > 0
                ? new GridLength(original1.Value * (originalPixelSize1 + delta) / originalPixelSize1, GridUnitType.Star)
                : new GridLength(originalPixelSize1 + delta);
            GridLength new2 = original2.IsStar && originalPixelSize2 > 0
                ? new GridLength(original2.Value * (originalPixelSize2 - delta) / originalPixelSize2, GridUnitType.Star)
                : new GridLength(originalPixelSize2 - delta);

            firstDefinition.Properties[RowColumnSizePropertyName].SetValue(new1);
            secondDefinition.Properties[RowColumnSizePropertyName].SetValue(new2);

            var parent = VisualTreeHelper.GetParent(this) as UIElement;
            if (parent != null) parent.InvalidateArrange();
            // The reference's rail.InvalidateVisual(): force layout so the rail's captions and the
            // splitter placements read settled ActualHeight/ActualWidth on this same tick.
            grid.UpdateLayout();
            if (rail != null) rail.RefreshLabels();
        }

        void Commit()
        {
            isDown = false;
            ReleaseCapture();
            if (activeChangeGroup != null) {
                activeChangeGroup.Commit();
                activeChangeGroup = null;
            }
        }

        /// <summary>Aborts the in-progress drag - Escape, a lost pointer capture, or any caller.
        /// The ChangeGroup's Abort rolls both size writes back.</summary>
        public void Cancel()
        {
            isDown = false;
            ReleaseCapture();
            if (activeChangeGroup != null) {
                activeChangeGroup.Abort();
                activeChangeGroup = null;
            }
            grid.UpdateLayout();
            if (rail != null) rail.RefreshLabels();
        }

        void ReleaseCapture()
        {
            if (capturedPointer != null) {
                ReleasePointerCapture(capturedPointer);
                capturedPointer = null;
            }
        }

        /// <summary>Test seam. This controller is not built on DragListener, so it needs its own
        /// equivalent of DragListener.SimulateDown/Move/Up: PointerRoutedEventArgs is a sealed
        /// WinRT type with no public constructor, so no test can synthesize a real pointer event.
        /// The argument is a point in the Grid's own coordinate space - the same thing
        /// e.GetCurrentPoint(grid).Position gives the real handler - which GetCoordinate collapses
        /// to the single axis this splitter tracks.</summary>
        internal void SimulateDown(Point pointInGrid)
        {
            if (!CanDrag) return;
            BeginAt(GetCoordinate(pointInGrid));
        }

        /// <summary>Test seam - see SimulateDown.</summary>
        internal void SimulateMove(Point pointInGrid)
        {
            MoveTo(GetCoordinate(pointInGrid));
        }

        /// <summary>Test seam - see SimulateDown.</summary>
        internal void SimulateUp(Point pointInGrid)
        {
            MoveTo(GetCoordinate(pointInGrid));
            Commit();
        }

        /// <summary>Test seam: the Escape-while-captured path, without a KeyRoutedEventArgs
        /// (also constructor-less).</summary>
        internal void SimulateEscape()
        {
            Cancel();
        }
    }

    public class GridRowSplitterAdorner : GridSplitterAdorner
    {
        internal GridRowSplitterAdorner(GridRailAdorner rail, DesignItem gridItem, DesignItem firstRow, DesignItem secondRow)
            : base(rail, gridItem, firstRow, secondRow)
        {
        }

        protected override InputSystemCursorShape CursorShape { get { return InputSystemCursorShape.SizeNorthSouth; } }
        protected override double GetCoordinate(Point point) { return point.Y; }

        protected override void RememberOriginalSize()
        {
            var r1 = (RowDefinition)firstDefinition.Component;
            var r2 = (RowDefinition)secondDefinition.Component;
            original1 = r1.Height;
            original2 = r2.Height;
            originalPixelSize1 = r1.ActualHeight;
            originalPixelSize2 = r2.ActualHeight;
        }

        protected override string RowColumnSizePropertyName { get { return "Height"; } }
    }

    public sealed class GridColumnSplitterAdorner : GridSplitterAdorner
    {
        internal GridColumnSplitterAdorner(GridRailAdorner rail, DesignItem gridItem, DesignItem firstColumn, DesignItem secondColumn)
            : base(rail, gridItem, firstColumn, secondColumn)
        {
        }

        protected override InputSystemCursorShape CursorShape { get { return InputSystemCursorShape.SizeWestEast; } }
        protected override double GetCoordinate(Point point) { return point.X; }

        protected override void RememberOriginalSize()
        {
            var c1 = (ColumnDefinition)firstDefinition.Component;
            var c2 = (ColumnDefinition)secondDefinition.Component;
            original1 = c1.Width;
            original2 = c2.Width;
            originalPixelSize1 = c1.ActualWidth;
            originalPixelSize2 = c2.ActualWidth;
        }

        protected override string RowColumnSizePropertyName { get { return "Width"; } }
    }
}
