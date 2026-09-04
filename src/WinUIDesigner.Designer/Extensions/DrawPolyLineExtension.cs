using System;
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
    /// <summary>Draws a new Polygon/Polyline: each click commits a vertex, DoubleTapped ends the
    /// gesture (replaces the reference's hand-rolled GetDoubleClickTime() P/Invoke outright - a
    /// native WinUI gesture recognizer, not a 1:1 API swap - spec decision 1/mechanism table).
    /// Ported from the reference's Extensions/DrawPolyLineExtension.cs; Alt-snap only (Shift-snap
    /// dropped - the reference's own Shift/Alt roles for THIS gesture are the freehand-angle vs
    /// axis-snap distinction described in the deep source read, and this port keeps the simpler,
    /// unambiguous single-modifier form: Alt held snaps the in-progress segment to the nearest
    /// horizontal/vertical axis).</summary>
    [ExtensionFor(typeof(Canvas))]
    [ExtensionFor(typeof(Grid))]
    public sealed class DrawPolyLineExtension : BehaviorExtension, IDrawItemExtension
    {
        /// <summary>Test-only seam: exposes the gesture started by the last StartDrawItem call
        /// (null before any draw, or once the gesture has finished/reset the tool). Same reason
        /// as DrawLineExtension.Gesture (Task 8): the outer "Draw ..." changeGroup only closes on
        /// a real DoubleTapped gesture - a real DoubleTappedRoutedEventArgs has no public
        /// constructor for a test to fabricate (Global Constraints) - so a test drives the
        /// gesture's own internal HandlePointerMoved/HandlePointerPressed/HandleDoubleTapped
        /// directly through this property instead.</summary>
        internal DrawPolylineMouseGesture Gesture { get; private set; }

        public bool CanItemBeDrawn(Type createItemType) => createItemType == typeof(Polyline) || createItemType == typeof(Polygon);

        public void StartDrawItem(DesignItem clickedOn, Type createItemType, IDesignPanel panel, Point point, VirtualKeyModifiers modifiers, Action<DesignItem> drawItemCallback)
        {
            var changeGroup = clickedOn.OpenGroup("Draw " + createItemType.Name);
            var createdItem = CreateComponentTool.CreateItem(clickedOn.Context, createItemType, null);

            // Task 8's report (DrawLineExtension) claimed this exact NaN-sized Rect pattern
            // silently fails to insert the item into the container's Children. Re-verified here,
            // twice, rather than assumed: (1) this task's own test (DrawPolyLineExtensionTests)
            // passes in full - including the canvas.Children.Count == 1 assertion - with the
            // literal new Rect(point.X, point.Y, double.NaN, double.NaN) kept exactly as the brief
            // wrote it; (2) temporarily reverting DrawLineExtension.cs:42 itself back to the same
            // NaN-Rect and rerunning DrawLineExtensionTests also passes in full (git history shows
            // no change to PlacementOperation.cs/CanvasPlacementSupport.cs since Task 8's commit
            // 6db80c7, so this isn't a since-fixed regression). Reading why: CanvasPlacementSupport
            // .SetPosition (CanvasPlacementSupport.cs:61-73) only ever assigns Canvas.Left/Top from
            // info.Bounds.X/Y, never Width/Height; DefaultPlacementBehavior.SetPosition's
            // ModelTools.Resize(item, NaN, NaN) call (ModelTools.cs:62-76) explicitly special-cases
            // double.IsNaN by Reset()-ing the Width/Height property instead of assigning NaN to it -
            // there is no code path where a NaN-sized insert Rect can throw or abort the insert.
            // Kept the brief's literal NaN-Rect (not Task 8's Size(0,0) fallback): it is both
            // correct and lower-deviation, and it leaves Width/Height unset (Task 8's Size(0,0)
            // would instead explicitly set Width=0/Height=0 on the created item, an unnecessary
            // side effect once the NaN path is confirmed safe).
            // Final review closed this out: DrawLineExtension.StartDrawItem was back-ported to the
            // NaN form too, so all three Draw*Extensions now agree and no Size(0,0) insert Rect
            // remains anywhere in this phase.
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

            var initialPoints = new PointCollection();
            initialPoints.Add(new Point(0, 0)); // the shape's own local origin, == the click point via Canvas.Left/Top from the insert above
            createdItem.Properties["Points"].SetValue(initialPoints);

            drawItemCallback?.Invoke(createdItem);

            var gesture = new DrawPolylineMouseGesture(createdItem, changeGroup);
            Gesture = gesture;
            gesture.Start(panel, point);
        }
    }

    sealed class DrawPolylineMouseGesture
    {
        readonly DesignItem _item;
        ChangeGroup _changeGroup;
        IDesignPanel _designPanel;
        Point _origin;

        public DrawPolylineMouseGesture(DesignItem item, ChangeGroup changeGroup)
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

        PointCollection GetPoints() => _item.View is Polygon polygon ? polygon.Points : ((Polyline)_item.View).Points;

        static PointCollection Clone(PointCollection source)
        {
            var copy = new PointCollection();
            foreach (var p in source) copy.Add(p);
            return copy;
        }

        Point ToLocal(Point panelPoint) => new Point(panelPoint.X - _origin.X, panelPoint.Y - _origin.Y);

        void OnPointerMoved(object sender, PointerRoutedEventArgs e) =>
            HandlePointerMoved(e.GetCurrentPoint((UIElement)_designPanel).Position, e.KeyModifiers);

        /// <summary>The real pointer-moved handling, extracted so a test can drive it directly -
        /// same Handle*-extraction pattern as CreateComponentMouseGesture.HandlePointerMoved
        /// (CreateComponentTool.cs) and DrawLineMouseGesture.HandlePointerReleased (Task 8),
        /// needed for the same reason (PointerRoutedEventArgs has no public constructor - Global
        /// Constraints). `panelPoint` is panel-relative, matching what e.GetCurrentPoint(panel)
        /// would have returned.</summary>
        internal void HandlePointerMoved(Point panelPoint, VirtualKeyModifiers modifiers)
        {
            var local = ApplyAltSnap(panelPoint, modifiers);
            var next = Clone(GetPoints());
            // Fix round 1 (reviewer finding): right after StartDrawItem, Count == 1 means that
            // sole point is the ALREADY-COMMITTED first vertex (placed at the click location via
            // Canvas.Left/Top in StartDrawItem), not an uncommitted preview - overwriting it here
            // would silently lose the true first-click location the moment the mouse first moves.
            // Matches the spec's own prose ("preview... added then replaced on the next move") and
            // the reference (DrawPolyLineExtension.cs:138: `if (Points.Count <= 1) Points.Add(point)`).
            // Every subsequent move (Count > 1) still overwrites the existing trailing preview in
            // place, same as before.
            if (next.Count == 1) next.Add(local); else next[next.Count - 1] = local;
            _item.Properties["Points"].SetValue(next);
        }

        // Final-review note on `e.Handled = true` here (investigated, deliberately KEPT): it does
        // NOT suppress this panel's own DoubleTapped, which is what ends the gesture. Both the UWP
        // and the WinUI 3 (Microsoft.UI.Xaml) UIElement.DoubleTapped/Tapped reference docs state it
        // outright - "Even if a control is already handling pointer events in the control logic, or
        // is handling manipulations, that doesn't prevent DoubleTapped from firing" - because
        // Tapped/DoubleTapped are higher-level gesture events synthesised alongside, not gated on,
        // the low-level pointer events' Handled flag ("otherwise unhandled" in their summary lines
        // refers to the gesture event's OWN route). What Handled does do here is stop the press
        // bubbling past the DesignPanel, which is the intent. It is NOT a defence against
        // CreateComponentTool's own still-subscribed OnPointerPressed re-processing the click: that
        // handler is registered first (ITool.Activate runs before any gesture starts) and therefore
        // always runs BEFORE this one; its own _drawing early-return is what actually guards that
        // case, and its matching "only mark Handled once we actually acted" fix is what lets this
        // handler get the click at all.
        void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
            HandlePointerPressed(e.GetCurrentPoint((UIElement)_designPanel).Position, e.KeyModifiers);
            e.Handled = true;
        }

        /// <summary>The real click-commits-a-vertex handling, extracted for the same reason as
        /// HandlePointerMoved above.</summary>
        internal void HandlePointerPressed(Point panelPoint, VirtualKeyModifiers modifiers)
        {
            var local = ApplyAltSnap(panelPoint, modifiers);
            var next = Clone(GetPoints());
            // Same Count == 1 case as HandlePointerMoved above: if only the committed first vertex
            // exists (the user clicked a second time before ever moving the mouse, so no preview
            // point was ever added), there is nothing to finalize yet - skip straight to adding the
            // new preview instead of clobbering the first vertex.
            if (next.Count > 1) next[next.Count - 1] = local; // finalize the point the preview was tracking
            next.Add(local); // start a new preview point at the same spot
            _item.Properties["Points"].SetValue(next);
        }

        Point ApplyAltSnap(Point panelPoint, VirtualKeyModifiers modifiers)
        {
            var local = ToLocal(panelPoint);
            bool alt = (modifiers & VirtualKeyModifiers.Menu) == VirtualKeyModifiers.Menu;
            if (!alt) return local;

            var points = GetPoints();
            if (points.Count < 2) return local;
            var lastCommitted = points[points.Count - 2]; // the last point BEFORE the trailing preview
            bool horizontal = System.Math.Abs(local.X - lastCommitted.X) >= System.Math.Abs(local.Y - lastCommitted.Y);
            return horizontal ? new Point(local.X, lastCommitted.Y) : new Point(lastCommitted.X, local.Y);
        }

        // Brief's literal code declared this as (object sender, TappedRoutedEventArgs e) - wrong:
        // IDesignPanel.DoubleTapped (Tools.cs:64) is typed `event DoubleTappedEventHandler
        // DoubleTapped`, whose delegate signature takes DoubleTappedRoutedEventArgs, not
        // TappedRoutedEventArgs. Confirmed via direct .winmd metadata parsing during Task 2's
        // review (not a guess) - fixed here to the correct delegate signature.
        void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            HandleDoubleTapped();
            e.Handled = true;
        }

        /// <summary>The real gesture-ending handling, extracted for the same reason as
        /// HandlePointerMoved/HandlePointerPressed above - a real DoubleTappedRoutedEventArgs has
        /// no public constructor either, so a test drives this directly.
        ///
        /// Final-review fix: TWO trailing points come off, not one. A DoubleTap is "simply two Tap
        /// interactions that occur in quick succession" (UIElement.DoubleTapped docs), and each of
        /// those taps raises its own full PointerPressed/PointerReleased pair before DoubleTapped
        /// fires on the second release - so by the time this runs, OnPointerPressed has already run
        /// twice more, and HandlePointerPressed adds exactly one entry per press (it finalises the
        /// trailing preview in place, then appends a fresh preview). Dropping only the preview
        /// therefore left the committed list ending in a duplicated final vertex (..., B, B): the
        /// vertex the double-tap's FIRST press committed, plus the redundant copy its SECOND press
        /// committed on top. Dropping both restores the intended (..., B).</summary>
        internal void HandleDoubleTapped()
        {
            var next = Clone(GetPoints());
            // Never drop the shape's own first vertex - a double-tap with no intervening single
            // click (i.e. the user double-tapped immediately after the very first click that
            // created the Polyline) has fewer than 3 points to give back.
            int drop = System.Math.Min(2, next.Count - 1);
            for (int i = 0; i < drop; i++) next.RemoveAt(next.Count - 1);
            _item.Properties["Points"].SetValue(next);
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
