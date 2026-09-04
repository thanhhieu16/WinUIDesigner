using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Services;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Draws a new Path: each click appends a LineSegment, DoubleTapped ends the
    /// gesture. Ported from the reference's Extensions/DrawPathExtension.cs; the figure's
    /// PathFigure is never separately RegisterComponentForDesigner'd (matches the reference's own
    /// commented-out CollectionElements.Add line - it lives only inside Path.Data's object
    /// graph, not as its own tracked DesignItem - spec's Data flow #5).</summary>
    [ExtensionFor(typeof(Canvas))]
    [ExtensionFor(typeof(Grid))]
    public sealed class DrawPathExtension : BehaviorExtension, IDrawItemExtension
    {
        /// <summary>Test-only seam: exposes the gesture started by the last StartDrawItem call
        /// (null before any draw, or once the gesture has finished/reset the tool). Same reason as
        /// DrawPolyLineExtension.Gesture (Task 10) / DrawLineExtension.Gesture (Task 8): the outer
        /// "Draw Path" changeGroup only closes on a real DoubleTapped gesture - a real
        /// DoubleTappedRoutedEventArgs has no public constructor for a test to fabricate (Global
        /// Constraints) - so a test drives the gesture's own internal HandlePointerMoved/
        /// HandlePointerPressed/HandleDoubleTapped directly through this property instead.</summary>
        internal DrawPathMouseGesture Gesture { get; private set; }

        public bool CanItemBeDrawn(Type createItemType) => createItemType == typeof(Path);

        public void StartDrawItem(DesignItem clickedOn, Type createItemType, IDesignPanel panel, Point point, VirtualKeyModifiers modifiers, Action<DesignItem> drawItemCallback)
        {
            var changeGroup = clickedOn.OpenGroup("Draw Path");
            var createdItem = CreateComponentTool.CreateItem(clickedOn.Context, typeof(Path), null);

            var operation = PlacementOperation.TryStartInsertNewComponents(clickedOn, new[] { createdItem }, new[] { new Rect(point.X, point.Y, double.NaN, double.NaN) }, PlacementType.AddItem);
            if (operation == null) {
                changeGroup.Abort();
                return;
            }
            clickedOn.Context.Services.Selection.SetSelectedComponents(new[] { createdItem });
            operation.Commit();

            createdItem.Properties["Stroke"].SetValue(new SolidColorBrush(Colors.Black));
            createdItem.Properties["StrokeThickness"].SetValue(2.0);
            createdItem.Properties["Stretch"].SetValue(Stretch.None);

            var figure = new PathFigure { StartPoint = new Point(0, 0) };
            var figures = new PathFigureCollection();
            figures.Add(figure);
            var geometry = new PathGeometry { Figures = figures };
            createdItem.Properties["Data"].SetValue(geometry);

            drawItemCallback?.Invoke(createdItem);

            var gesture = new DrawPathMouseGesture(createdItem, changeGroup);
            Gesture = gesture;
            gesture.Start(panel, point);
        }
    }

    sealed class DrawPathMouseGesture
    {
        readonly DesignItem _item;
        ChangeGroup _changeGroup;
        IDesignPanel _designPanel;
        Point _origin;

        public DrawPathMouseGesture(DesignItem item, ChangeGroup changeGroup)
        {
            _item = item;
            _changeGroup = changeGroup;
        }

        public void Start(IDesignPanel designPanel, Point origin)
        {
            _designPanel = designPanel;
            _origin = origin;
            designPanel.PointerMoved += OnPointerMoved;
            designPanel.PointerPressed += OnPointerPressed;
            designPanel.DoubleTapped += OnDoubleTapped;
            designPanel.PointerCaptureLost += OnPointerCaptureLost;
            designPanel.KeyDown += OnKeyDown;
        }

        Point ToLocal(Point panelPoint) => new Point(panelPoint.X - _origin.X, panelPoint.Y - _origin.Y);

        /// <summary>Rebuilds the figure's whole Segments collection through `transform`, then
        /// pushes a NEW top-level PathGeometry/PathFigureCollection/PathSegmentCollection through
        /// SetValue - same "genuinely new top-level reference" reasoning as Task 11's
        /// SaveGeometry (PathFigure/PathSegment aren't separately-tracked DesignItems, so only
        /// the Data property itself is observable for undo).
        ///
        /// Deviation from the brief's literal code (found empirically, not one of the two
        /// pre-identified corrections): the brief's version built `current` by re-adding the
        /// SAME PathSegment object references straight from the old figure.Segments, and any
        /// element `transform` leaves untouched (e.g. every already-committed segment before the
        /// trailing preview, once there are 3+ segments) flows unchanged into `updated` and then
        /// into `newSegments.Add(s)` below. That throws
        /// System.Runtime.InteropServices.COMException (HRESULT 0x800F1000, "No installed
        /// components were detected") - the exact "a PathSegment already in a PathSegmentCollection
        /// cannot be added to a second one" constraint PathHandlerExtension.cs's own
        /// CloneSegment/SaveGeometry doc comment already discovered and documented for its
        /// larger geometry-rewrite path (confirmed there empirically with a throwaway probe).
        /// Reproduced here the same way: this task's own
        /// DrawPathExtensionTests.MultiClickSequenceProducesOnlyLineSegmentsAndDoubleTappedEndsTheGesture
        /// threw exactly this COMException on its third click (2 pre-existing segments, one
        /// left untouched by the transform) before this fix, and passes after it. Fixed by
        /// cloning every segment read off the OLD figure into a brand-new LineSegment up front,
        /// so `current`/`updated` never carries an already-collection-owned reference regardless
        /// of what `transform` does or doesn't touch.</summary>
        void ReplaceSegments(Func<List<PathSegment>, List<PathSegment>> transform)
        {
            var geometry = (PathGeometry)((Path)_item.View).Data;
            var figure = geometry.Figures[0];
            var current = new List<PathSegment>();
            foreach (var s in figure.Segments) current.Add(CloneSegment(s));
            var updated = transform(current);

            var newFigure = new PathFigure { StartPoint = figure.StartPoint, IsClosed = figure.IsClosed, IsFilled = figure.IsFilled };
            var newSegments = new PathSegmentCollection();
            foreach (var s in updated) newSegments.Add(s);
            newFigure.Segments = newSegments;

            var newFigures = new PathFigureCollection();
            newFigures.Add(newFigure);
            var newGeometry = new PathGeometry { FillRule = geometry.FillRule, Figures = newFigures };
            _item.Properties["Data"].SetValue(newGeometry);
        }

        /// <summary>DrawPathExtension only ever creates LineSegments (spec decision 5 - Bezier/Arc
        /// segments are only ever introduced later via Task 12's convert-menu, never during initial
        /// drawing), so a LineSegment-only clone is sufficient for ReplaceSegments' needs. See
        /// ReplaceSegments' own remarks for why cloning (rather than reusing the source reference)
        /// is required at all. The default arm throws rather than silently reusing the un-clonable
        /// reference, matching PathHandlerExtension.CloneSegment's own "fail loudly" convention.</summary>
        static PathSegment CloneSegment(PathSegment source) => source is LineSegment line
            ? new LineSegment { Point = line.Point }
            : throw new NotSupportedException("DrawPathExtension only ever creates LineSegments; unexpected PathSegment type: " + source.GetType().FullName);

        void OnPointerMoved(object sender, PointerRoutedEventArgs e) =>
            HandlePointerMoved(e.GetCurrentPoint((UIElement)sender).Position);

        /// <summary>The real pointer-moved handling, extracted so a test can drive it directly -
        /// same Handle*-extraction pattern as CreateComponentMouseGesture.HandlePointerMoved
        /// (CreateComponentTool.cs) and DrawPolylineMouseGesture.HandlePointerMoved (Task 10),
        /// needed for the same reason (PointerRoutedEventArgs has no public constructor - Global
        /// Constraints). `panelPoint` is panel-relative, matching what
        /// e.GetCurrentPoint(designPanel) would have returned.</summary>
        internal void HandlePointerMoved(Point panelPoint)
        {
            var local = ToLocal(panelPoint);
            ReplaceSegments(segs => {
                if (segs.Count == 0) segs.Add(new LineSegment { Point = local });
                else segs[segs.Count - 1] = new LineSegment { Point = local }; // "preview" trailing segment
                return segs;
            });
        }

        // Final-review note on `e.Handled = true` here (investigated, deliberately KEPT) - see
        // DrawPolyLineExtension.OnPointerPressed's own note for the full reasoning: marking the
        // press Handled does NOT suppress this panel's DoubleTapped (both the UWP and the WinUI 3
        // UIElement.DoubleTapped docs state that handling pointer events "doesn't prevent
        // DoubleTapped from firing" - gesture events are synthesised alongside, not gated on, the
        // pointer events' Handled flag), and it is not what guards against CreateComponentTool
        // re-processing the click either (that handler runs first and returns early on _drawing).
        // It only stops the press bubbling past the DesignPanel, which is the intent.
        void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
            HandlePointerPressed(e.GetCurrentPoint((UIElement)sender).Position);
            e.Handled = true;
        }

        /// <summary>The real click-appends-a-LineSegment handling, extracted for the same reason
        /// as HandlePointerMoved above.</summary>
        internal void HandlePointerPressed(Point panelPoint)
        {
            var local = ToLocal(panelPoint);
            ReplaceSegments(segs => {
                if (segs.Count == 0) segs.Add(new LineSegment { Point = local });
                else segs[segs.Count - 1] = new LineSegment { Point = local }; // finalize the point the preview was tracking
                segs.Add(new LineSegment { Point = local }); // start a new preview segment at the same spot
                return segs;
            });
        }

        // Brief's literal code declared this as (object sender, TappedRoutedEventArgs e) - wrong:
        // IDesignPanel.DoubleTapped (Tools.cs:64) is typed `event DoubleTappedEventHandler
        // DoubleTapped`, whose delegate signature takes DoubleTappedRoutedEventArgs, not
        // TappedRoutedEventArgs. Confirmed via direct read of Tools.cs during Task 13's dispatch
        // (not a guess, and the identical fix was independently made in Task 10's
        // DrawPolyLineExtension and the Task 12 convert-menu work) - fixed here to the correct
        // delegate signature.
        void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            HandleDoubleTapped();
            e.Handled = true;
        }

        /// <summary>The real gesture-ending handling, extracted for the same reason as
        /// HandlePointerMoved/HandlePointerPressed above - a real DoubleTappedRoutedEventArgs has
        /// no public constructor either, so a test drives this directly.
        ///
        /// Final-review fix: TWO trailing segments come off, not one - identical reasoning to
        /// DrawPolylineMouseGesture.HandleDoubleTapped (see its remarks). A DoubleTap is two Tap
        /// interactions, each raising its own PointerPressed, and HandlePointerPressed appends
        /// exactly one segment per press - so the segment list arrives here carrying the trailing
        /// preview AND the redundant duplicate the double-tap's second press committed on top of
        /// the first's. Dropping only one left the committed figure ending in two coincident
        /// LineSegments (a zero-length final segment).</summary>
        internal void HandleDoubleTapped()
        {
            ReplaceSegments(segs => {
                int drop = System.Math.Min(2, segs.Count);
                for (int i = 0; i < drop; i++) segs.RemoveAt(segs.Count - 1);
                return segs;
            });
            Commit();
        }

        void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => Cancel();

        void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape) { Cancel(); e.Handled = true; }
        }

        void Commit()
        {
            _changeGroup?.Commit();
            _changeGroup = null;
            Stop(reapply: true);
        }

        /// <summary>The gesture's abort path - what Escape (OnKeyDown) and PointerCaptureLost both
        /// drive. Internal rather than private so a test can exercise the cancellation path
        /// directly, the same seam CreateComponentMouseGesture.Cancel() already provides (neither
        /// KeyRoutedEventArgs nor PointerRoutedEventArgs has a public constructor - Global
        /// Constraints - so the real events can't be raised from a test).</summary>
        internal void Cancel()
        {
            _changeGroup?.Abort();
            _changeGroup = null;
            Stop(reapply: false);
        }

        void Stop(bool reapply)
        {
            _designPanel.PointerMoved -= OnPointerMoved;
            _designPanel.PointerPressed -= OnPointerPressed;
            _designPanel.DoubleTapped -= OnDoubleTapped;
            _designPanel.PointerCaptureLost -= OnPointerCaptureLost;
            _designPanel.KeyDown -= OnKeyDown;
            _designPanel.ReleasePointerCaptures();
            if (reapply) _item.ReapplyAllExtensions();
            var toolService = _item.Services.GetService<IToolService>();
            if (toolService.CurrentTool is CreateComponentTool) toolService.CurrentTool = toolService.PointerTool;
        }
    }
}
