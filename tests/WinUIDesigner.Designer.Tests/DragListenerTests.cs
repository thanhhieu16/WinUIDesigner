using Windows.Foundation;
using Windows.System;
using NUnit.Framework;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class DragListenerTests
    {
        [Test]
        public void SimulatedDragBelowThresholdDoesNotFireStarted()
        {
            WinUITestHost.Run(() => {
                var target = new Grid();
                var drag = new DragListener(target);
                bool started = false;
                drag.Started += d => started = true;

                drag.SimulateDown(new Point(0, 0));
                drag.SimulateMove(new Point(1, 1)); // below the 4px/2px threshold

                Assert.IsFalse(started);
                Assert.IsFalse(drag.IsActive);
            });
        }

        [Test]
        public void SimulatedDragPastThresholdFiresStartedThenChangedThenCompleted()
        {
            WinUITestHost.Run(() => {
                var target = new Grid();
                var drag = new DragListener(target);
                int startedCount = 0, changedCount = 0, completedCount = 0;
                drag.Started += d => startedCount++;
                drag.Changed += d => changedCount++;
                drag.Completed += d => completedCount++;

                drag.SimulateDown(new Point(0, 0));
                drag.SimulateMove(new Point(10, 0)); // past MinimumHorizontalDragDistance (4)
                drag.SimulateMove(new Point(20, 0));
                drag.SimulateUp(new Point(20, 0));

                Assert.AreEqual(1, startedCount);
                Assert.AreEqual(2, changedCount, "one Changed per move past threshold, including the move that crossed it");
                Assert.AreEqual(1, completedCount);
                Assert.IsFalse(drag.IsCanceled);
                Assert.IsFalse(drag.IsActive, "IsActive resets once the drag completes");
            });
        }

        [Test]
        public void DeltaIsCurrentMinusStartWhenNoTransformIsSet()
        {
            WinUITestHost.Run(() => {
                var target = new Grid();
                var drag = new DragListener(target);
                drag.SimulateDown(new Point(5, 5));
                drag.SimulateMove(new Point(17, 9));

                var delta = drag.Delta;
                Assert.AreEqual(12, delta.X, 0.001);
                Assert.AreEqual(4, delta.Y, 0.001);
            });
        }

        [Test]
        public void DeltaIsTransformedWhenTransformIsSet()
        {
            WinUITestHost.Run(() => {
                var target = new Grid();
                var drag = new DragListener(target);
                // A pure 2x scale: a 20-unit screen delta should read back as 10 units in the
                // transform's own (pre-scale) space, matching the reference's
                // Matrix.Invert()+Matrix.Transform(Vector) behavior for an affine transform.
                drag.Transform = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 2, ScaleY = 2 };
                drag.SimulateDown(new Point(0, 0));
                drag.SimulateMove(new Point(20, 0));

                Assert.AreEqual(10, drag.Delta.X, 0.001);
                Assert.AreEqual(0, drag.Delta.Y, 0.001);
            });
        }

        [Test]
        public void CancelStopsTheDragAndSetsIsCanceled()
        {
            WinUITestHost.Run(() => {
                var target = new Grid();
                var drag = new DragListener(target);
                int completedCount = 0;
                drag.Completed += d => completedCount++;

                drag.SimulateDown(new Point(0, 0));
                drag.SimulateMove(new Point(10, 0));
                drag.Cancel();

                Assert.IsTrue(drag.IsCanceled);
                Assert.AreEqual(1, completedCount, "Cancel still fires Completed, same as the reference - callers branch on IsCanceled");
                Assert.IsFalse(drag.IsDown);
            });
        }

        [Test]
        public void GetPositionConvertsThroughTheTargetsTransformToVisual()
        {
            WinUITestHost.RunWithWindow(window => {
                var root = new Grid { Width = 400, Height = 300 };
                var child = new Grid { Width = 50, Height = 50, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(100, 60, 0, 0) };
                root.Children.Add(child);
                window.Content = root;
                root.UpdateLayout();

                var drag = new DragListener(child);
                var windowPoint = child.TransformToVisual(null).TransformPoint(new Point(10, 10));
                drag.SimulateDown(windowPoint);

                var relative = drag.GetPosition(child);
                Assert.IsNotNull(relative);
                Assert.AreEqual(10, relative.Value.X, 0.5);
                Assert.AreEqual(10, relative.Value.Y, 0.5);
            });
        }

        // Gap #1 (spec): empirically confirms a Grid-based element can actually acquire
        // keyboard focus via Focus(FocusState.Pointer) in this SDK version - the fact
        // DragListener's real (non-simulated) PointerPressed handler relies on to make
        // Escape-to-cancel reachable at all. Not a Simulate* test: this exercises the real
        // WinUI focus system, which SimulateDown deliberately bypasses.
        [Test]
        public void TargetCanAcquireKeyboardFocusAfterPointerPress()
        {
            WinUITestHost.RunWithWindow(window => {
                var target = new Grid { Width = 20, Height = 20, IsTabStop = true };
                window.Content = target;
                target.UpdateLayout();

                bool focusSucceeded = target.Focus(FocusState.Pointer);
                var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(target.XamlRoot);

                Assert.IsTrue(focusSucceeded, "Grid.IsTabStop=true should make it focus-eligible in this SDK version");
                Assert.AreSame(target, focusedElement);
            });
        }
    }
}
