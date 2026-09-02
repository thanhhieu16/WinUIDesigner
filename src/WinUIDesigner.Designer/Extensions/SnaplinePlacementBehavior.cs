using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.Designer.Services;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// Snap-to-sibling-edge placement base class. Ported from the reference's
    /// Extensions/SnaplinePlacementBehavior.cs. Like RasterPlacementBehavior (its base, per the
    /// reference's own chain SnaplinePlacementBehavior : RasterPlacementBehavior :
    /// DefaultPlacementBehavior) it carries no [ExtensionFor] of its own; it is the base of
    /// CanvasPlacementSupport/GridPlacementSupport (Tasks 9-10).
    ///
    /// WinUI substitutions beyond the ones RasterPlacementBehavior already documents
    /// (Keyboard.IsKeyDown, Brushes.X, Services.DesignPanel), all compiler-confirmed absent:
    /// - Rect.Inflate(dx, dy) - reconstructed as the private InflateRect helper below, which
    ///   also reproduces WPF's "an over-shrunk rect becomes Rect.Empty" rule that the reference
    ///   relies on for the negative-inflate call in BuildMaps (Windows.Foundation.Rect's
    ///   constructor throws on a negative width, so plain arithmetic is not enough).
    /// - UIElement.TranslatePoint(Point, UIElement) - TransformToVisual(target)
    ///   .TransformPoint(point), the replacement Phase 3/4a already established.
    /// - DoubleCollection(IEnumerable&lt;double&gt;) - the type has no such constructor
    ///   (CS1729); it does implement IList&lt;double&gt;, so a collection initializer produces the
    ///   identical value.
    /// - TextBox.GetRectFromCharacterIndex(int) - this SDK requires the two-argument
    ///   (int index, bool trailingEdge) overload; false selects the leading edge, matching WPF's
    ///   single-argument behavior.
    /// - OptionService is looked up defensively (null =&gt; do not round). The reference
    ///   dereferences GetService&lt;OptionService&gt;() unguarded because its XamlDesignContext
    ///   registers the service unconditionally; here it's registered by DesignSurface.
    ///   LoadDesigner (Task 4), so a bare XamlDesignContext fixture would NRE instead of simply
    ///   not rounding.
    /// </summary>
    public class SnaplinePlacementBehavior : RasterPlacementBehavior
    {
        public static bool GetDisableSnaplines(DependencyObject obj)
        {
            return (bool)obj.GetValue(DisableSnaplinesProperty);
        }

        public static void SetDisableSnaplines(DependencyObject obj, bool value)
        {
            obj.SetValue(DisableSnaplinesProperty, value);
        }

        public static readonly DependencyProperty DisableSnaplinesProperty =
            DependencyProperty.RegisterAttached("DisableSnaplines", typeof(bool), typeof(SnaplinePlacementBehavior), new PropertyMetadata(false));

        AdornerPanel adornerPanel;
        Canvas surface;
        List<Snapline> horizontalMap;
        List<Snapline> verticalMap;
        double? baseline;

        static double _snaplineMargin = 8;
        public static double SnaplineMargin {
            get { return _snaplineMargin; }
            set { _snaplineMargin = value; }
        }

        static double _snaplineAccuracy = 5;
        public static double SnaplineAccuracy {
            get { return _snaplineAccuracy; }
            set { _snaplineAccuracy = value; }
        }

        public override void BeginPlacement(PlacementOperation operation)
        {
            base.BeginPlacement(operation);
            CreateSurface(operation);
        }

        public override void EndPlacement(PlacementOperation operation)
        {
            base.EndPlacement(operation);
            DeleteSurface();
        }

        public override void EnterContainer(PlacementOperation operation)
        {
            base.EnterContainer(operation);
            CreateSurface(operation);
        }

        public override void LeaveContainer(PlacementOperation operation)
        {
            base.LeaveContainer(operation);
            DeleteSurface();
        }

        public override Point PlacePoint(Point point, VirtualKeyModifiers modifiers)
        {
            if (surface == null)
                return base.PlacePoint(point, modifiers);

            DesignPanel designPanel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            if (designPanel == null || !designPanel.UseSnaplinePlacement)
                return base.PlacePoint(point, modifiers);

            surface.Children.Clear();
            if ((modifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control)
                return base.PlacePoint(point, modifiers);

            Rect bounds = new Rect(point.X, point.Y, 0, 0);

            var horizontalInput = new List<Snapline>();
            var verticalInput = new List<Snapline>();

            AddLines(bounds, 0, false, horizontalInput, verticalInput, null);
            if (baseline.HasValue) {
                var textOffset = bounds.Top + baseline.Value;
                horizontalInput.Add(new Snapline() { Group = 1, Offset = textOffset, Start = bounds.Left, End = bounds.Right });
            }

            List<Snapline> drawLines;
            double delta;

            var newPoint = base.PlacePoint(point, modifiers);
            if (Snap(horizontalInput, horizontalMap, SnaplineAccuracy, out drawLines, out delta)) {
                foreach (var d in drawLines)
                    DrawLine(d.Start, d.Offset + d.DrawOffset, d.End, d.Offset + d.DrawOffset);
                point.Y += delta;
            } else
                point.Y = newPoint.Y;

            if (Snap(verticalInput, verticalMap, SnaplineAccuracy, out drawLines, out delta)) {
                foreach (var d in drawLines)
                    DrawLine(d.Offset + d.DrawOffset, d.Start, d.Offset + d.DrawOffset, d.End);
                point.X += delta;
            } else
                point.X = newPoint.X;

            return point;
        }

        public override void BeforeSetPosition(PlacementOperation operation, VirtualKeyModifiers modifiers)
        {
            base.BeforeSetPosition(operation, modifiers);
            if (surface == null) return;

            DesignPanel designPanel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            if (designPanel == null || !designPanel.UseSnaplinePlacement)
                return;

            surface.Children.Clear();
            if ((modifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control) return;

            Rect bounds = Rect.Empty;
            foreach (var item in operation.PlacedItems)
                bounds.Union(item.Bounds);

            var horizontalInput = new List<Snapline>();
            var verticalInput = new List<Snapline>();
            var info = operation.PlacedItems[0];

            if (operation.Type == PlacementType.Resize) {
                AddLines(bounds, 0, false, horizontalInput, verticalInput, info.ResizeThumbAlignment);
            } else {
                AddLines(bounds, 0, false, horizontalInput, verticalInput, null);
                if (baseline.HasValue) {
                    var textOffset = bounds.Top + baseline.Value;
                    horizontalInput.Add(new Snapline() { Group = 1, Offset = textOffset, Start = bounds.Left, End = bounds.Right });
                }
            }

            List<Snapline> drawLines;
            double delta;

            if (Snap(horizontalInput, horizontalMap, SnaplineAccuracy, out drawLines, out delta)) {
                if (operation.Type == PlacementType.Resize) {
                    if (info.ResizeThumbAlignment != null && info.ResizeThumbAlignment.Value.Vertical == VerticalAlignment.Top) {
                        bounds.Y += delta;
                        bounds.Height = Math.Max(0, bounds.Height - delta);
                        if (RoundValues(operation)) { bounds.Y = Math.Round(bounds.Y); bounds.Height = Math.Round(bounds.Height); }
                    } else {
                        bounds.Height = Math.Max(0, bounds.Height + delta);
                        if (RoundValues(operation)) bounds.Height = Math.Round(bounds.Height);
                    }
                    info.Bounds = bounds;
                } else {
                    foreach (var item in operation.PlacedItems) {
                        var r = item.Bounds;
                        r.Y += delta;
                        if (RoundValues(operation)) r.Y = Math.Round(r.Y);
                        item.Bounds = r;
                    }
                }
                foreach (var d in drawLines)
                    DrawLine(d.Start, d.Offset + d.DrawOffset, d.End, d.Offset + d.DrawOffset);
            }

            if (Snap(verticalInput, verticalMap, SnaplineAccuracy, out drawLines, out delta)) {
                if (operation.Type == PlacementType.Resize) {
                    if (info.ResizeThumbAlignment != null && info.ResizeThumbAlignment.Value.Horizontal == HorizontalAlignment.Left) {
                        bounds.X += delta;
                        bounds.Width = Math.Max(0, bounds.Width - delta);
                        if (RoundValues(operation)) { bounds.X = Math.Round(bounds.X); bounds.Width = Math.Round(bounds.Width); }
                    } else {
                        bounds.Width = Math.Max(0, bounds.Width + delta);
                        if (RoundValues(operation)) bounds.Width = Math.Round(bounds.Width);
                    }
                    info.Bounds = bounds;
                } else {
                    foreach (var item in operation.PlacedItems) {
                        var r = item.Bounds;
                        r.X += delta;
                        if (RoundValues(operation)) r.X = Math.Round(r.X);
                        item.Bounds = r;
                    }
                }
                foreach (var d in drawLines)
                    DrawLine(d.Offset + d.DrawOffset, d.Start, d.Offset + d.DrawOffset, d.End);
            }
        }

        /// <summary>Replaces the reference's six inline
        /// operation.CurrentContainer.Services.GetService&lt;OptionService&gt;()
        /// .SnaplinePlacementRoundValues reads - see the class doc comment for why this is
        /// null-guarded here and not in the reference.</summary>
        static bool RoundValues(PlacementOperation operation)
        {
            var options = operation.CurrentContainer.Services.GetService<OptionService>();
            return options != null && options.SnaplinePlacementRoundValues;
        }

        void CreateSurface(PlacementOperation operation)
        {
            if (ExtendedItem.Services.GetService<IDesignPanel>() != null) {
                surface = new Canvas();
                adornerPanel = new AdornerPanel();
                adornerPanel.SetAdornedElement(ExtendedItem.View, ExtendedItem);
                AdornerPanel.SetPlacement(surface, AdornerPlacement.FillContent);
                adornerPanel.Children.Add(surface);
                ExtendedItem.Services.DesignPanel.Adorners.Add(adornerPanel);

                BuildMaps(operation);

                if (operation.Type != PlacementType.Resize && operation.PlacedItems.Count == 1)
                    baseline = GetBaseline(operation.PlacedItems[0].Item.View);
            }
        }

        IEnumerable<DesignItem> AllDesignItems(DesignItem designItem = null)
        {
            if (designItem == null && this.ExtendedItem.Services.GetService<IDesignPanel>() is DesignPanel)
                designItem = this.ExtendedItem.Services.DesignPanel.Context.RootItem;

            if (designItem?.ContentProperty != null) {
                if (designItem.ContentProperty.IsCollection) {
                    foreach (var collectionElement in designItem.ContentProperty.CollectionElements) {
                        if (collectionElement != null) yield return collectionElement;
                        foreach (var el in AllDesignItems(collectionElement))
                            if (el != null) yield return el;
                    }
                } else if (designItem.ContentProperty.Value != null) {
                    yield return designItem.ContentProperty.Value;
                    foreach (var el in AllDesignItems(designItem.ContentProperty.Value))
                        if (el != null) yield return el;
                }
            }
        }

        /// <summary>Returns the DesignItems for which snaplines are created.</summary>
        protected IEnumerable<DesignItem> GetSnapToDesignItems(PlacementOperation operation)
        {
            return AllDesignItems();
        }

        void BuildMaps(PlacementOperation operation)
        {
            horizontalMap = new List<Snapline>();
            verticalMap = new List<Snapline>();

            var containerRect = new Rect(0, 0, ModelTools.GetWidth(ExtendedItem.View), ModelTools.GetHeight(ExtendedItem.View));
            if (SnaplineMargin > 0)
                AddLines(containerRect, -SnaplineMargin, false);

            AddLines(containerRect, 0, false);

            AddContainerSnaplines(containerRect, horizontalMap, verticalMap);

            if (!CanPlace(operation.PlacedItems.Select(x => x.Item), operation.Type, PlacementAlignment.Center))
                return;

            foreach (var item in GetSnapToDesignItems(operation)
                     .Except(operation.PlacedItems.Select(f => f.Item))
                     .Where(x => x.View != null && !GetDisableSnaplines(x.View))) {
                if (item != null) {
                    var bounds = GetPositionRelativeToContainer(operation, item);
                    AddLines(bounds, 0, false);
                    if (SnaplineMargin > 0) AddLines(bounds, SnaplineMargin, true);
                    AddBaseline(item, bounds, horizontalMap);
                }
            }
        }

        protected virtual void AddContainerSnaplines(Rect containerRect, List<Snapline> horizontalMap, List<Snapline> verticalMap)
        {
        }

        void AddLines(Rect r, double inflate, bool requireOverlap)
        {
            AddLines(r, inflate, requireOverlap, horizontalMap, verticalMap, null);
        }

        void AddLines(Rect r, double inflate, bool requireOverlap, List<Snapline> h, List<Snapline> v, PlacementAlignment? filter)
        {
            if (r != Rect.Empty) {
                Rect r2 = InflateRect(r, inflate);

                if (filter == null || filter.Value.Vertical == VerticalAlignment.Top)
                    h.Add(new Snapline() { RequireOverlap = requireOverlap, Offset = r2.Top - 1, Start = r.Left, End = r.Right });
                if (filter == null || filter.Value.Vertical == VerticalAlignment.Bottom)
                    h.Add(new Snapline() { RequireOverlap = requireOverlap, Offset = r2.Bottom - 1, Start = r.Left, End = r.Right });
                if (filter == null || filter.Value.Horizontal == HorizontalAlignment.Left)
                    v.Add(new Snapline() { RequireOverlap = requireOverlap, Offset = r2.Left - 1, Start = r.Top, End = r.Bottom });
                if (filter == null || filter.Value.Horizontal == HorizontalAlignment.Right)
                    v.Add(new Snapline() { RequireOverlap = requireOverlap, Offset = r2.Right - 1, Start = r.Top, End = r.Bottom });

                if (filter == null) {
                    h.Add(new Snapline() { RequireOverlap = requireOverlap, Offset = r2.Top + Math.Abs((r2.Top - r2.Bottom) / 2) - 1, DrawOffset = 1, Start = r.Left, End = r.Right });
                    v.Add(new Snapline() { RequireOverlap = requireOverlap, Offset = r2.Left + Math.Abs((r2.Left - r2.Right) / 2) - 1, DrawOffset = 1, Start = r.Top, End = r.Bottom });
                }
            }
        }

        /// <summary>Windows.Foundation.Rect has no Inflate (CS1061). This reproduces WPF's
        /// Rect.Inflate(width, height) exactly, including its rule that a rect shrunk past zero
        /// in either dimension becomes Rect.Empty - BuildMaps depends on that rule for its
        /// negative-inflate (-SnaplineMargin) call on a small container.</summary>
        static Rect InflateRect(Rect r, double inflate)
        {
            double x = r.X - inflate;
            double y = r.Y - inflate;
            double width = r.Width + inflate + inflate;
            double height = r.Height + inflate + inflate;
            if (!(width >= 0 && height >= 0)) return Rect.Empty;
            return new Rect(x, y, width, height);
        }

        void AddBaseline(DesignItem item, Rect bounds, List<Snapline> list)
        {
            var baseline = GetBaseline(item.View);
            if (baseline.HasValue) {
                var textOffset = item.View.TransformToVisual(ExtendedItem.View).TransformPoint(new Point(0, baseline.Value)).Y;
                list.Add(new Snapline() { Group = 1, Offset = textOffset, Start = bounds.Left, End = bounds.Right });
            }
        }

        void DeleteSurface()
        {
            if (surface != null) {
                ExtendedItem.Services.DesignPanel.Adorners.Remove(adornerPanel);
                adornerPanel = null;
                surface = null;
                horizontalMap = null;
                verticalMap = null;
            }
        }

        void DrawLine(double x1, double y1, double x2, double y2)
        {
            if (double.IsInfinity(x1) || double.IsNaN(x1) || double.IsInfinity(y1) || double.IsNaN(y1) ||
                double.IsInfinity(x2) || double.IsNaN(x2) || double.IsInfinity(y2) || double.IsNaN(y2))
                return;

            var line1 = new Line() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = 1, Stroke = new SolidColorBrush(Microsoft.UI.Colors.White) };
            surface.Children.Add(line1);

            var line2 = new Line() {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = 1,
                Stroke = new SolidColorBrush(Microsoft.UI.Colors.Orange),
                StrokeDashArray = new DoubleCollection() { 5, 2 },
                StrokeDashOffset = x1 + y1
            };
            surface.Children.Add(line2);
        }

        static double? GetBaseline(UIElement element)
        {
            var textBox = element as TextBox;
            if (textBox != null) {
                var r = textBox.GetRectFromCharacterIndex(0, false).Bottom;
                return textBox.TransformToVisual(element).TransformPoint(new Point(0, r)).Y;
            }
            var textBlock = element as TextBlock;
            if (textBlock != null)
                return textBlock.TransformToVisual(element).TransformPoint(new Point(0, textBlock.ActualHeight)).Y;
            return null;
        }

        static bool Snap(List<Snapline> input, List<Snapline> map, double accuracy, out List<Snapline> drawLines, out double delta)
        {
            delta = double.MaxValue;
            drawLines = null;

            foreach (var inputLine in input) {
                foreach (var mapLine in map) {
                    if (Math.Abs(mapLine.Offset - inputLine.Offset) <= accuracy) {
                        if (!inputLine.RequireOverlap && !mapLine.RequireOverlap ||
                            Math.Max(inputLine.Start, mapLine.Start) < Math.Min(inputLine.End, mapLine.End)) {
                            if (mapLine.Group == inputLine.Group)
                                delta = mapLine.Offset - inputLine.Offset;
                        }
                    }
                }
            }

            if (delta == double.MaxValue) return false;
            var offsetDict = new Dictionary<double, Snapline>();

            foreach (var inputLine in input) {
                inputLine.Offset += delta;
                foreach (var mapLine in map) {
                    if (inputLine.Offset == mapLine.Offset) {
                        var offset = mapLine.Offset;
                        Snapline drawLine;
                        if (!offsetDict.TryGetValue(offset, out drawLine)) {
                            drawLine = new Snapline { Start = double.MaxValue, End = double.MinValue, DrawOffset = mapLine.DrawOffset };
                            offsetDict[offset] = drawLine;
                        }
                        drawLine.Offset = offset;
                        drawLine.Start = Math.Min(drawLine.Start, Math.Min(inputLine.Start, mapLine.Start));
                        drawLine.End = Math.Max(drawLine.End, Math.Max(inputLine.End, mapLine.End));
                    }
                }
            }

            drawLines = offsetDict.Values.ToList();
            return true;
        }

        [DebuggerDisplay("Snapline: {Offset}")]
        protected class Snapline
        {
            public double Offset;
            public double Start;
            public double End;
            public bool RequireOverlap;
            public int Group;
            public double DrawOffset = 0;
        }
    }
}
