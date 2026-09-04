using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Controls.Thumbs;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Post-creation point-editing for Path. Ported from the reference's Extensions/
    /// PathHandlerExtension.cs - the largest single file in this phase (799 reference lines).
    /// This class covers geometry dispatch + drag/select/commit; the convert-segment context
    /// menu is Task 12, which completes this same partial class from a second file (PathThumb's
    /// own ContextFlyout) rather than reopening this one - hence `partial`, which is deliberate
    /// and must stay. No ZoomControl-scaling of drag.Delta (spec decision 11).
    /// No TranslatedPoint/RenderedGeometry transform (spec decision 10/Task 1's ruling) - Point
    /// values here are the Path's own local coordinate space directly.</summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(Path), OverrideExtensions = new[] { typeof(WinUIDesigner.Designer.Extensions.ResizeThumbExtension), typeof(WinUIDesigner.Designer.Extensions.RotateThumbExtension), typeof(WinUIDesigner.Designer.Extensions.SkewThumbExtension) })]
    public sealed partial class PathHandlerExtension : PointEditExtensionBase
    {
        internal readonly List<PathThumb> PointThumbs = new List<PathThumb>();

        // Deviation from the brief: exposed so tests can drive the SAME DragListener instance
        // production pointer events would (mirrors LineHandlerExtension.DragListeners (Task 7) and
        // PolyLineHandlerExtension.DragListeners (Task 9)). Without this, a test-constructed
        // `new DragListener(thumb)` is a second, fully independent state machine - Simulate* on it
        // fires only ITS OWN (unsubscribed) Started/Changed/Completed, never this extension's
        // Drag_Started/Drag_Changed/Drag_Completed. Confirmed empirically here too, as a
        // one-line controlled swap on an otherwise-passing test: with `new DragListener(thumb)`,
        // DraggingTheMiddleAnchorMovesOnlyThatLineSegmentsPointAndCommitsOneUndoEntry failed with
        // the dragged LineSegment.Point.X still 50 instead of 55 (no state change at all); with
        // `extension.DragListeners[thumb]` and nothing else altered, it passes.
        internal readonly Dictionary<PathThumb, DragListener> DragListeners = new Dictionary<PathThumb, DragListener>();

        List<PathPoint> _points = new List<PathPoint>();

        /// <summary>The extension's private, mutable copy of the Path's geometry - a deep clone of
        /// whatever Data held when the thumbs were built, and the object every PathPoint's save
        /// action mutates in place. It is deliberately NEVER assigned to Data: each SaveGeometry
        /// publishes a fresh deep clone OF it instead, so the value SetValue records as "old" is
        /// always an untouched snapshot rather than the very object the drag just mutated. See
        /// SaveGeometry's remarks for why cloning has to be deep here.</summary>
        PathGeometry _workingGeometry;

        PathThumb _draggingThumb;
        Point _dragOriginalPoint;

        protected override IEnumerable<DesignerThumb> Thumbs => PointThumbs;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            Services.Selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
            RebuildThumbs();
        }

        protected override void OnRemove()
        {
            Services.Selection.PrimarySelectionChanged -= OnPrimarySelectionChanged;
            base.OnRemove();
        }

        void OnPrimarySelectionChanged(object sender, EventArgs e)
        {
            // Reference guards an in-progress keyboard-driven move here (IKeyDown/IKeyUp - out of
            // scope this phase, spec's "Out of scope"). Nothing to guard without that feature.
        }

        internal void RebuildThumbs()
        {
            foreach (var thumb in PointThumbs) adornerPanel.Children.Remove(thumb);
            PointThumbs.Clear();
            // Kept in lockstep with PointThumbs so a discarded thumb (and its DragListener's
            // pointer-event subscriptions on it) isn't left referenced from here - the same leak
            // Task 9's ShiftCtrlClickDeletionRemovesTheThumbFromDragListenersToo regression covers
            // for PolyLineHandlerExtension.DeletePoint.
            DragListeners.Clear();
            _points = GetPoints();
            foreach (var point in _points) PointThumbs.Add(CreateThumb(point));
            UpdateAdornerVisibility();
        }

        // ---- Geometry dispatch (PathGeometry only - see class remarks) ----

        internal sealed class PathPoint
        {
            readonly Action<Point> _save;
            public Point Point { get; private set; }
            public PathPoint TargetPathPoint { get; set; } // non-null => this is a Bezier control point pointing at its anchor
            public object SegmentOrFigure { get; set; }
            public PathFigure ParentFigure { get; set; }
            public int PolyLineIndex { get; set; } = -1;

            public PathPoint(Point point, Action<Point> save)
            {
                Point = point;
                _save = save;
            }

            public void SetPoint(Point value)
            {
                Point = value;
                _save(value);
            }
        }

        List<PathPoint> GetPoints()
        {
            var path = (Path)ExtendedItem.View;
            var list = new List<PathPoint>();
            _workingGeometry = null;
            if (path.Data is PathGeometry geometry) {
                _workingGeometry = CloneGeometry(geometry);
                foreach (var figure in _workingGeometry.Figures) AddFigurePoints(list, figure, _workingGeometry);
            }
            return list;
        }

        void AddFigurePoints(List<PathPoint> list, PathFigure figure, PathGeometry geometry)
        {
            var startPoint = new PathPoint(figure.StartPoint, p => { figure.StartPoint = p; SaveGeometry(geometry); }) { SegmentOrFigure = figure, ParentFigure = figure };
            list.Add(startPoint);
            var previousAnchor = startPoint;

            foreach (var segment in figure.Segments) {
                switch (segment) {
                    case LineSegment line: {
                        var anchor = new PathPoint(line.Point, p => { line.Point = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure };
                        list.Add(anchor);
                        previousAnchor = anchor;
                        break;
                    }
                    case PolyLineSegment poly: {
                        for (int n = 0; n < poly.Points.Count; n++) {
                            int captured = n;
                            var anchor = new PathPoint(poly.Points[n], p => { SetPolyPoint(poly.Points, captured, p, pts => poly.Points = pts); SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, PolyLineIndex = n };
                            list.Add(anchor);
                            previousAnchor = anchor;
                        }
                        break;
                    }
                    case BezierSegment bezier: {
                        var anchor = new PathPoint(bezier.Point3, p => { bezier.Point3 = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure };
                        var c1 = new PathPoint(bezier.Point1, p => { bezier.Point1 = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, TargetPathPoint = previousAnchor };
                        var c2 = new PathPoint(bezier.Point2, p => { bezier.Point2 = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, TargetPathPoint = anchor };
                        list.Add(c1); list.Add(c2); list.Add(anchor);
                        previousAnchor = anchor;
                        break;
                    }
                    case PolyBezierSegment polyBezier: {
                        for (int n = 0; n + 2 < polyBezier.Points.Count + 1 && n <= polyBezier.Points.Count - 3; n += 3) {
                            int c1Index = n, c2Index = n + 1, anchorIndex = n + 2;
                            var anchor = new PathPoint(polyBezier.Points[anchorIndex], p => { SetPolyPoint(polyBezier.Points, anchorIndex, p, pts => polyBezier.Points = pts); SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, PolyLineIndex = anchorIndex };
                            var c1 = new PathPoint(polyBezier.Points[c1Index], p => { SetPolyPoint(polyBezier.Points, c1Index, p, pts => polyBezier.Points = pts); SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, PolyLineIndex = c1Index, TargetPathPoint = previousAnchor };
                            var c2 = new PathPoint(polyBezier.Points[c2Index], p => { SetPolyPoint(polyBezier.Points, c2Index, p, pts => polyBezier.Points = pts); SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, PolyLineIndex = c2Index, TargetPathPoint = anchor };
                            list.Add(c1); list.Add(c2); list.Add(anchor);
                            previousAnchor = anchor;
                        }
                        break;
                    }
                    case QuadraticBezierSegment quad: {
                        var anchor = new PathPoint(quad.Point2, p => { quad.Point2 = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure };
                        var c1 = new PathPoint(quad.Point1, p => { quad.Point1 = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, TargetPathPoint = anchor };
                        list.Add(c1); list.Add(anchor);
                        previousAnchor = anchor;
                        break;
                    }
                    case PolyQuadraticBezierSegment polyQuad: {
                        for (int n = 0; n <= polyQuad.Points.Count - 2; n += 2) {
                            int c1Index = n, anchorIndex = n + 1;
                            var anchor = new PathPoint(polyQuad.Points[anchorIndex], p => { SetPolyPoint(polyQuad.Points, anchorIndex, p, pts => polyQuad.Points = pts); SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, PolyLineIndex = anchorIndex };
                            var c1 = new PathPoint(polyQuad.Points[c1Index], p => { SetPolyPoint(polyQuad.Points, c1Index, p, pts => polyQuad.Points = pts); SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, PolyLineIndex = c1Index, TargetPathPoint = anchor };
                            list.Add(c1); list.Add(anchor);
                            previousAnchor = anchor;
                        }
                        break;
                    }
                    case ArcSegment arc: {
                        // "Virtual" size-control point - reference uses arc.Point - new Vector(arc.Size.Width, arc.Size.Height);
                        // WinUI has no Vector type (root CLAUDE.md's known gaps), so this is the
                        // equivalent Point-only arithmetic instead.
                        var anchor = new PathPoint(arc.Point, p => { arc.Point = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure };
                        var sizeHandlePoint = new Point(arc.Point.X - arc.Size.Width, arc.Point.Y - arc.Size.Height);
                        var sizeHandle = new PathPoint(sizeHandlePoint, p => {
                            arc.Size = new Size(System.Math.Abs(arc.Point.X - p.X), System.Math.Abs(arc.Point.Y - p.Y));
                            SaveGeometry(geometry);
                        }) { SegmentOrFigure = segment, ParentFigure = figure, TargetPathPoint = anchor };
                        list.Add(sizeHandle); list.Add(anchor);
                        previousAnchor = anchor;
                        break;
                    }
                    // RectangleGeometry/EllipseGeometry/LineGeometry/CombinedGeometry/GeometryGroup/
                    // StreamGeometry point-editing: out of scope (spec's "Out of scope") - nothing
                    // this phase's own DrawPathExtension ever creates uses them.
                }
            }
        }

        static void SetPolyPoint(PointCollection source, int index, Point value, Action<PointCollection> assign)
        {
            var next = new PointCollection();
            for (int i = 0; i < source.Count; i++) next.Add(i == index ? value : source[i]);
            assign(next);
        }

        /// <summary>Publishes the current working geometry to Data as a fresh DEEP clone.
        ///
        /// The brief's version built only a new top-level PathGeometry + PathFigureCollection and
        /// re-added the SAME PathFigure objects, flagging "if undo doesn't register, also clone
        /// each PathFigure". That turned out to be not merely an undo-fidelity question but a hard
        /// runtime constraint: a PathFigure that already belongs to a PathFigureCollection cannot
        /// be added to a second one - PathFigureCollection.Add throws
        /// System.Runtime.InteropServices.COMException, HRESULT 0x800F1000, message "No installed
        /// components were detected." Confirmed empirically with a throwaway probe, which also
        /// showed (a) the same throw for a PathSegment already in a PathSegmentCollection, (b) that
        /// removing the figure from its first collection first makes the add succeed, and (c) that
        /// a Transform CAN be shared across two PathGeometry instances (a plain DP set, not a
        /// collection add) - hence Transform is carried over by reference below while figures and
        /// segments are cloned. Same family as root CLAUDE.md's already-documented "DoubleCollection
        /// cannot be shared across Shape instances at runtime".
        ///
        /// Cloning also makes the undo entry meaningful rather than cosmetic: because
        /// _workingGeometry is never itself Data, the value SetValue records as "old" is a snapshot
        /// taken before this drag tick's mutation, not the same object the mutation just changed.</summary>
        void SaveGeometry(PathGeometry geometry)
        {
            ExtendedItem.Properties["Data"].SetValue(CloneGeometry(geometry));
        }

        static PathGeometry CloneGeometry(PathGeometry source)
        {
            var figures = new PathFigureCollection();
            if (source.Figures != null) {
                foreach (var figure in source.Figures) figures.Add(CloneFigure(figure));
            }
            return new PathGeometry { FillRule = source.FillRule, Transform = source.Transform, Figures = figures };
        }

        static PathFigure CloneFigure(PathFigure source)
        {
            var segments = new PathSegmentCollection();
            if (source.Segments != null) {
                foreach (var segment in source.Segments) segments.Add(CloneSegment(segment));
            }
            return new PathFigure {
                StartPoint = source.StartPoint,
                IsClosed = source.IsClosed,
                IsFilled = source.IsFilled,
                Segments = segments
            };
        }

        /// <summary>The seven cases below are exactly the PathSegment subclasses AddFigurePoints
        /// dispatches over - the set Microsoft.UI.Xaml.Media exposes. The default arm throws rather
        /// than skipping so that an unrecognised segment fails loudly instead of silently vanishing
        /// from the published geometry; nothing in this port can currently reach it.</summary>
        static PathSegment CloneSegment(PathSegment source)
        {
            switch (source) {
                case LineSegment line:
                    return new LineSegment { Point = line.Point };
                case PolyLineSegment poly:
                    return new PolyLineSegment { Points = ClonePoints(poly.Points) };
                case BezierSegment bezier:
                    return new BezierSegment { Point1 = bezier.Point1, Point2 = bezier.Point2, Point3 = bezier.Point3 };
                case PolyBezierSegment polyBezier:
                    return new PolyBezierSegment { Points = ClonePoints(polyBezier.Points) };
                case QuadraticBezierSegment quad:
                    return new QuadraticBezierSegment { Point1 = quad.Point1, Point2 = quad.Point2 };
                case PolyQuadraticBezierSegment polyQuad:
                    return new PolyQuadraticBezierSegment { Points = ClonePoints(polyQuad.Points) };
                case ArcSegment arc:
                    return new ArcSegment {
                        Point = arc.Point,
                        Size = arc.Size,
                        RotationAngle = arc.RotationAngle,
                        IsLargeArc = arc.IsLargeArc,
                        SweepDirection = arc.SweepDirection
                    };
                default:
                    throw new NotSupportedException("Unknown PathSegment type: " + source.GetType().FullName);
            }
        }

        static PointCollection ClonePoints(PointCollection source)
        {
            var copy = new PointCollection();
            if (source != null) {
                foreach (var p in source) copy.Add(p);
            }
            return copy;
        }

        // ---- Thumbs, drag, commit ----

        PathThumb CreateThumb(PathPoint point)
        {
            var thumb = new PathThumb { AssociatedPoint = point, Alignment = PlacementAlignment.BottomRight };
            thumb.SetDesignCursor(InputSystemCursorShape.Cross);
            thumb.IsEllipse = point.TargetPathPoint != null;
            thumb.Point = point.Point;
            thumb.RelativeToPoint = point.TargetPathPoint?.Point;
            // Deviation from the brief: the brief's CreateThumb never published the thumb's own
            // PointPlacementSupport to the panel, so AdornerPanel.ArrangeOverride would have used
            // the AdornerPlacement.FillContent default (AdornerPanel.cs:31) and every thumb would
            // have covered the whole Path instead of sitting on its point - and PointThumb's
            // Point-driven placement (PointThumb.OnPointChanged, which mutates exactly this
            // PointPlacementSupport instance and invalidates the parent panel's arrange) would
            // have been dead code. Task 5's own PointPlacementSupportArrangesA7By7RectCenteredOnThePoint
            // test wires it the same way. Not covered by an assertion here (this phase's DoD
            // checks point/segment values and undo entries, not pixel placement).
            AdornerPanel.SetPlacement(thumb, thumb.AdornerPlacement);
            adornerPanel.Children.Add(thumb);

            var drag = new DragListener(thumb);
            drag.Started += d => Drag_Started(thumb);
            drag.Changed += d => Drag_Changed(d, thumb);
            drag.Completed += d => Drag_Completed(d);
            DragListeners[thumb] = drag;

            return thumb;
        }

        void Drag_Started(PathThumb thumb)
        {
            _draggingThumb = thumb;
            _dragOriginalPoint = thumb.AssociatedPoint.Point;
            SetOperation();
        }

        void SetOperation()
        {
            operation = PlacementOperation.Start(extendedItemArray, PlacementType.MovePoint);
            IsResizing = true;
        }

        void Drag_Changed(DragListener drag, PathThumb thumb)
        {
            var (dx, dy) = drag.Delta;
            var newPoint = new Point(System.Math.Round(_dragOriginalPoint.X + dx), System.Math.Round(_dragOriginalPoint.Y + dy));
            thumb.AssociatedPoint.SetPoint(newPoint);
            thumb.Point = newPoint;
            // Every control-point thumb whose TargetPathPoint is the point that just moved needs
            // its own connector line re-anchored, and every anchor whose own control points moved
            // WITH it (none do here - Bezier control points move independently) does not.
            foreach (var t in PointThumbs) {
                if (t.AssociatedPoint.TargetPathPoint == thumb.AssociatedPoint) t.RelativeToPoint = newPoint;
            }
            Invalidate();
        }

        void Drag_Completed(DragListener drag)
        {
            if (drag.IsCanceled) {
                operation?.Abort();
            } else {
                operation?.Commit();
            }
            operation = null;
            IsResizing = false;
            _draggingThumb = null;
            Invalidate();
        }
    }
}
