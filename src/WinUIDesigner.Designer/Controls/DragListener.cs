using System;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WinUIDesigner.Designer.Controls
{
    public delegate void DragHandler(DragListener drag);

    /// <summary>
    /// Tracks a click-drag gesture on a UIElement without relying on Thumb's own drag events
    /// (the reference doesn't either - see the spec's "Decisions already made"). Ported from
    /// the reference's Controls/DragListener.cs with three WinUI-specific changes: pointer
    /// events replace WPF's Preview* tunneling events (WinUI has no tunneling, so
    /// AddHandler(..., handledEventsToo: true) is used instead - see the spec's mechanism
    /// table); Delta uses GeneralTransform.Inverse/TransformPoint instead of WPF's
    /// Matrix.Invert()/Matrix.Transform(Vector) (neither exists in WinUI - compiler-confirmed
    /// during spec brainstorming); and drags can be driven programmatically via
    /// SimulateDown/SimulateMove/SimulateUp/Cancel, since PointerRoutedEventArgs has no public
    /// constructor for tests to fabricate one.
    /// </summary>
    public sealed class DragListener
    {
        /// <summary>WPF's SystemParameters.MinimumHorizontalDragDistance default - no WinUI
        /// equivalent exists to read at runtime (confirmed absent from the SDK), so this is a
        /// hardcoded constant matching WPF's own default (spec Known gap #2).</summary>
        public const double MinimumHorizontalDragDistance = 4;

        /// <summary>WPF's SystemParameters.MinimumVerticalDragDistance default - see
        /// MinimumHorizontalDragDistance.</summary>
        public const double MinimumVerticalDragDistance = 2;

        public UIElement Target { get; }

        /// <summary>Optional transform used to convert Delta out of screen/window space and
        /// into the dragged element's own pre-transform space - set by callers whose thumb
        /// sits inside a rotated/scaled adorner.</summary>
        public GeneralTransform Transform { get; set; }

        public Point StartPoint { get; private set; }
        public Point CurrentPoint { get; private set; }
        public bool IsActive { get; private set; }
        public bool IsDown { get; private set; }
        public bool IsCanceled { get; private set; }
        public VirtualKeyModifiers KeyModifiers { get; private set; }

        public event DragHandler Started;
        public event DragHandler Changed;
        public event DragHandler Completed;

        Pointer _capturedPointer;

        public DragListener(UIElement target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            Target = target;
            Target.AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)OnPointerPressed, true);
            Target.AddHandler(UIElement.PointerMovedEvent, (PointerEventHandler)OnPointerMoved, true);
            Target.AddHandler(UIElement.PointerReleasedEvent, (PointerEventHandler)OnPointerReleased, true);
            Target.PointerCaptureLost += OnPointerCaptureLost;
            Target.KeyDown += OnKeyDown;
        }

        void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Final-review fix (finding #5): only the left mouse button (or the primary action
            // mode of another pointer device) starts a drag - a right-click or pen-barrel press
            // on a thumb must not begin a resize/rotate/skew (it should open a context menu, or
            // do nothing). Confirmed via Microsoft.UI.xml:
            // Microsoft.UI.Input.PointerPointProperties.IsLeftButtonPressed. Simulate* (the test
            // seam) bypasses this handler entirely, so no existing test is affected.
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

            _capturedPointer = e.Pointer;
            Target.CapturePointer(e.Pointer);
            // Best-effort: makes Escape-to-cancel reachable while this element holds the
            // pointer capture. Confirmed reachable for a Grid with IsTabStop=true - see
            // DragListenerTests.TargetCanAcquireKeyboardFocusAfterPointerPress. Callers whose
            // Target isn't focus-eligible simply won't get KeyDown; the drag itself still
            // works via pointer events regardless.
            Target.Focus(FocusState.Pointer);
            BeginAt(e.GetCurrentPoint(null).Position, e.KeyModifiers);
        }

        void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            // Final-review fix (finding #5, "ideally"): ignore Moved/Released from a pointer
            // other than the one that started this drag (e.g. a second finger/pen touching down
            // mid-drag) - cheap given _capturedPointer already exists for CapturePointer/
            // ReleasePointerCapture.
            if (!IsDown || e.Pointer.PointerId != _capturedPointer?.PointerId) return;
            MoveTo(e.GetCurrentPoint(null).Position, e.KeyModifiers);
        }

        void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!IsDown || e.Pointer.PointerId != _capturedPointer?.PointerId) return;
            EndAt();
        }

        void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            // The OS can revoke capture without a matching PointerReleased (e.g. a system
            // interruption). Without this handler IsDown would stay true forever and Completed
            // would never fire - stranding an open ChangeGroup, and for a move gesture that had
            // already called PlacementOperation.ChangeContainer mid-drag, a partially-reparented
            // item too. Cancel() is the same safe unwind Escape already uses.
            if (!IsDown || e.Pointer.PointerId != _capturedPointer?.PointerId) return;
            Cancel();
        }

        void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (IsDown && e.Key == VirtualKey.Escape) {
                Cancel();
                e.Handled = true;
            }
        }

        void BeginAt(Point point, VirtualKeyModifiers modifiers)
        {
            StartPoint = point;
            CurrentPoint = point;
            KeyModifiers = modifiers;
            IsDown = true;
            IsCanceled = false;
        }

        void MoveTo(Point point, VirtualKeyModifiers modifiers)
        {
            CurrentPoint = point;
            KeyModifiers = modifiers;
            if (!IsActive) {
                var d = Delta;
                if (Math.Abs(d.X) >= MinimumHorizontalDragDistance || Math.Abs(d.Y) >= MinimumVerticalDragDistance) {
                    IsActive = true;
                    Started?.Invoke(this);
                }
            }
            if (IsActive) Changed?.Invoke(this);
        }

        void EndAt()
        {
            IsDown = false;
            ReleaseCapture();
            if (IsActive) Complete();
        }

        /// <summary>Cancels the in-progress drag (Escape, or any other caller). Still fires
        /// Completed, same as the reference - callers distinguish success from cancellation via
        /// IsCanceled inside their Completed handler.</summary>
        public void Cancel()
        {
            if (!IsDown) return;
            IsCanceled = true;
            IsDown = false;
            ReleaseCapture();
            if (IsActive) Complete();
        }

        void ReleaseCapture()
        {
            if (_capturedPointer != null) {
                Target.ReleasePointerCapture(_capturedPointer);
                _capturedPointer = null;
            }
        }

        void Complete()
        {
            IsActive = false;
            Completed?.Invoke(this);
        }

        /// <summary>Start-to-current offset, converted through Transform's inverse when set.
        /// Transforming both points individually and subtracting is algebraically equivalent to
        /// WPF's Matrix.Invert()+Matrix.Transform(Vector) for an affine transform - translation
        /// cancels out of the difference - without needing a Vector type, which WinUI doesn't
        /// have.</summary>
        public (double X, double Y) Delta {
            get {
                if (Transform != null) {
                    var inverse = Transform.Inverse;
                    if (inverse != null) {
                        var s = inverse.TransformPoint(StartPoint);
                        var c = inverse.TransformPoint(CurrentPoint);
                        return (c.X - s.X, c.Y - s.Y);
                    }
                }
                return (CurrentPoint.X - StartPoint.X, CurrentPoint.Y - StartPoint.Y);
            }
        }

        /// <summary>Converts the last-known pointer position (window coordinates, per
        /// PointerRoutedEventArgs.GetCurrentPoint(null)'s documented frame) into
        /// <paramref name="relativeTo"/>'s own coordinate space - the replacement for WPF's
        /// ambient Mouse.GetPosition(element), which has no WinUI equivalent (position is only
        /// available from a live pointer event). Returns null if relativeTo's transform isn't
        /// invertible (GeneralTransform.Inverse can return null - unlike WPF's Matrix.Invert(),
        /// which throws instead).</summary>
        public Point? GetPosition(UIElement relativeTo)
        {
            if (relativeTo == null) return CurrentPoint;
            var inverse = relativeTo.TransformToVisual(null).Inverse;
            if (inverse == null) return null;
            return inverse.TransformPoint(CurrentPoint);
        }

        /// <summary>Test-only entry point: drives the same internal state machine
        /// OnPointerPressed does, without a real PointerRoutedEventArgs (which has no public
        /// constructor).</summary>
        public void SimulateDown(Point point, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None) => BeginAt(point, modifiers);

        /// <summary>Test-only entry point - see SimulateDown.</summary>
        public void SimulateMove(Point point, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None) => MoveTo(point, modifiers);

        /// <summary>Test-only entry point - see SimulateDown.</summary>
        public void SimulateUp(Point point)
        {
            CurrentPoint = point;
            EndAt();
        }

        /// <summary>Production entry point: re-drives this DragListener's state machine from a
        /// pointer press that landed somewhere other than Target (e.g. a Draw*Extension creating a
        /// new Line and immediately handing the drag to its endpoint thumb's own DragListener). Same
        /// underlying call as SimulateDown - kept as a separate, distinctly-named method (not a
        /// rename) so a reader can tell "test drives this artificially" from "a real production code
        /// path re-enters this listener's state machine" without reading call sites. Does NOT capture
        /// the pointer on Target - the caller is responsible for its own capture (see spec decision
        /// 4's "Capture responsibility").</summary>
        public void ExternalStart(Point point, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None) => BeginAt(point, modifiers);

        /// <summary>Production entry point - see ExternalStart.</summary>
        public void ExternalMove(Point point, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None) => MoveTo(point, modifiers);

        /// <summary>Production entry point - see ExternalStart.</summary>
        public void ExternalStop(Point point)
        {
            CurrentPoint = point;
            EndAt();
        }

        /// <summary>Test-only entry point: drives the same PointerCaptureLost path
        /// OnPointerCaptureLost does, without a real PointerRoutedEventArgs (which has no
        /// public constructor). See SimulateDown.</summary>
        public void RaisePointerCaptureLostForTest()
        {
            if (!IsDown) return;
            Cancel();
        }
    }
}
