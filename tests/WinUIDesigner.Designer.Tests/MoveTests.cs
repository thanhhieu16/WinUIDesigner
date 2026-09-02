using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class MoveTests
    {
        // Side-by-side (not stacked/overlapping) so a window-coordinate point unambiguously
        // hit-tests into one container or the other - the whole point of these tests is
        // exercising real coordinate-based hit-testing during a drag (Known gap #1's first
        // real exercise of LeaveContainer/EnterContainer via ChangeContainer).
        const string SideBySideCanvasesXaml =
            "<StackPanel Orientation=\"Horizontal\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Canvas x:Name=\"SourceCanvas\" Width=\"150\" Height=\"150\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" Canvas.Left=\"10\" Canvas.Top=\"10\" />" +
            "</Canvas>" +
            "<Canvas x:Name=\"TargetCanvas\" Width=\"150\" Height=\"150\" />" +
            "</StackPanel>";

        static (DesignSurface surface, DesignItem buttonItem, DesignItem sourceItem, DesignItem targetItem)
            LoadSelected(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(SideBySideCanvasesXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            // Brief-defect correction: UseSnaplinePlacement defaults to true (DesignPanel.cs),
            // and SnaplinePlacementBehavior.BuildMaps treats the move's own CurrentContainer
            // (SourceCanvas) as a snap-to sibling in its own right (only the moved item itself is
            // excluded via .Except) - its own edge/center guides land close enough to these
            // fixture's exact +20/+15 deltas to nudge the result by SnaplineAccuracy (5px),
            // confirmed empirically (disabling this flag makes the exact-math assertions below
            // pass exactly). This suite verifies MoveLogic's raw delta math, not snapping (which
            // has its own dedicated suite, SnaplinePlacementBehaviorTests) - same call already
            // made by RasterPlacementBehaviorTests.cs for the same reason.
            surface.DesignPanel.UseSnaplinePlacement = false;
            surface.UpdateLayout();

            var root = (Microsoft.UI.Xaml.Controls.StackPanel)surface.DesignContext.RootItem.Component;
            var sourceCanvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
            var targetCanvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[1];
            var button = (Microsoft.UI.Xaml.Controls.Button)sourceCanvas.Children[0];

            // Brief-defect correction: a Panel/Canvas with Background == null is not hit-test
            // visible over its own empty interior (same WPF/UWP/WinUI convention) - confirmed
            // empirically that this loader's XAML parsing does not populate Background from a
            // plain "Background=\"Transparent\"" attribute on the live view (TargetCanvas.
            // Background measured null after load even with that attribute present), so without
            // this, DesignPanel.HitTest never finds TargetCanvas at all and
            // DraggingTheHandleAcrossContainersReparentsTheItemWithOneUndoRevertingBoth's
            // ChangeContainerIfPossible hit-test can never land on it. Set directly on the live
            // view (not through a DesignItemProperty) since this is test-fixture plumbing, not
            // model state under test.
            targetCanvas.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

            var services = surface.DesignContext.Services;
            var buttonItem = services.Component.GetDesignItem(button);
            var sourceItem = services.Component.GetDesignItem(sourceCanvas);
            var targetItem = services.Component.GetDesignItem(targetCanvas);
            services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            return (surface, buttonItem, sourceItem, targetItem);
        }

        static TopLeftContainerDragHandle GetExtension(DesignItem buttonItem)
        {
            return buttonItem.Extensions.OfType<TopLeftContainerDragHandle>().Single();
        }

        // Brief-defect correction: the brief's original test bodies used window-coordinate
        // literals under the assumption that the loaded StackPanel renders flush with the test
        // window's top-left corner (matching e.g. ResizeThumbExtensionTests' single-Button
        // fixture). Measured empirically (Known gap #1's own "must be verified empirically"
        // framing): WinUITestHost's shared window is much larger than this fixture's 300x150
        // content, and DesignPanel.ArrangeOverride arranges root content within its own full
        // ActualSize, so the StackPanel's default alignment centers it - observed at window
        // offset (0, 285) in one run, not (0, 0). Hardcoding that offset would just trade one
        // fragile assumption for another (it depends on the shared window's size), so instead
        // every simulated point below is derived from the *live* TransformToVisual(null) of the
        // container the point is meant to land in, converting a local canvas-space offset into
        // real window coordinates on every run.
        static Windows.Foundation.Point WindowPoint(DesignItem containerItem, double localX, double localY)
        {
            var element = (Microsoft.UI.Xaml.FrameworkElement)containerItem.View;
            return element.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(localX, localY));
        }

        [Test]
        public void SelectingAnElementShowsATopLeftDragHandle()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, buttonItem, _, _) = LoadSelected(window);
                Assert.DoesNotThrow(() => GetExtension(buttonItem));
            });
        }

        [Test]
        public void DraggingTheHandleWithinTheSameContainerMovesTheItemAndCommitsOneChangeGroup()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, _) = LoadSelected(window);
                var extension = GetExtension(buttonItem);
                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                bool canUndoBefore = undoService.CanUndo;

                // Points are local to SourceCanvas, converted to real window coordinates (see
                // WindowPoint) - DragListener's SimulateDown/Move bypass real pointer
                // hit-testing entirely, but Move()'s own hit-testing math still runs against
                // these as real window coordinates (see MoveLogic).
                extension.DragListener.SimulateDown(WindowPoint(sourceItem, 20, 20));
                extension.DragListener.SimulateMove(WindowPoint(sourceItem, 40, 35)); // +20,+15, stays inside SourceCanvas (width 150)
                extension.DragListener.SimulateUp(WindowPoint(sourceItem, 40, 35));
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, buttonItem.Parent, "a same-container move must not reparent");
                var left = buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>();
                var top = buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>();
                Assert.AreEqual(30, left, 0.5);
                Assert.AreEqual(25, top, 0.5);
                Assert.IsTrue(undoService.CanUndo);
                Assert.AreNotEqual(canUndoBefore, undoService.CanUndo);
            });
        }

        [Test]
        public void DraggingTheHandleAcrossContainersReparentsTheItemWithOneUndoRevertingBoth()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, targetItem) = LoadSelected(window);
                var extension = GetExtension(buttonItem);

                // (180, 20) in SourceCanvas-local space is past its own width (150), landing
                // inside TargetCanvas, which sits immediately to its right (Horizontal
                // StackPanel) - see WindowPoint.
                extension.DragListener.SimulateDown(WindowPoint(sourceItem, 20, 20));
                extension.DragListener.SimulateMove(WindowPoint(sourceItem, 180, 20)); // past x=150, into TargetCanvas
                extension.DragListener.SimulateUp(WindowPoint(sourceItem, 180, 20));
                surface.UpdateLayout();

                Assert.AreSame(targetItem, buttonItem.Parent, "the item should have reparented into TargetCanvas");
                Assert.IsFalse(sourceItem.ContentProperty.CollectionElements.Contains(buttonItem));
                Assert.IsTrue(targetItem.ContentProperty.CollectionElements.Contains(buttonItem));

                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                Assert.IsTrue(undoService.CanUndo);
                undoService.Undo();
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, buttonItem.Parent,
                    "a single undo must revert the whole gesture - reparent and position together");
                Assert.IsTrue(sourceItem.ContentProperty.CollectionElements.Contains(buttonItem));
                Assert.IsFalse(targetItem.ContentProperty.CollectionElements.Contains(buttonItem));
            });
        }

        [Test]
        public void CancelingAMoveRevertsPositionWithNoCommittedChange()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, _) = LoadSelected(window);
                var extension = GetExtension(buttonItem);
                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                bool canUndoBefore = undoService.CanUndo;

                extension.DragListener.SimulateDown(WindowPoint(sourceItem, 20, 20));
                extension.DragListener.SimulateMove(WindowPoint(sourceItem, 50, 50));
                extension.DragListener.Cancel();
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, buttonItem.Parent);
                var left = buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>();
                Assert.AreEqual(10, left, 0.5, "canceled move should revert to the original position");
                Assert.AreEqual(canUndoBefore, undoService.CanUndo,
                    "an aborted PlacementOperation must not push anything onto the undo stack");
            });
        }

        [Test]
        public void PointerCaptureLostMidMoveCancelsCleanly()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, _) = LoadSelected(window);
                var extension = GetExtension(buttonItem);
                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                bool canUndoBefore = undoService.CanUndo;

                extension.DragListener.SimulateDown(WindowPoint(sourceItem, 20, 20));
                extension.DragListener.SimulateMove(WindowPoint(sourceItem, 180, 20)); // would have crossed into TargetCanvas
                extension.DragListener.RaisePointerCaptureLostForTest();
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, buttonItem.Parent,
                    "a capture loss mid-drag must abort, not commit a half-finished cross-container move");
                Assert.AreEqual(canUndoBefore, undoService.CanUndo);
            });
        }

        const string NestedCanvasXaml =
            "<Canvas x:Name=\"RootCanvas\" Width=\"300\" Height=\"300\" " +
            "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Canvas x:Name=\"InnerCanvas\" Width=\"100\" Height=\"100\" Canvas.Left=\"10\" Canvas.Top=\"10\">" +
            "<Button x:Name=\"Target\" Width=\"20\" Height=\"20\" />" +
            "</Canvas></Canvas>";

        [Test]
        public void ClickingAndDraggingAnUnselectedContainersBodyDoesNothing()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(NestedCanvasXaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                // Brief-defect correction (same call as LoadSelected above): UseSnaplinePlacement
                // defaults to true and would nudge a real move by SnaplineAccuracy (5px); this
                // test doesn't expect any move to happen at all, but disabling it keeps the
                // fixture consistent with the sibling test below and immune to that noise either
                // way.
                surface.DesignPanel.UseSnaplinePlacement = false;
                surface.UpdateLayout();

                var rootCanvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                var innerCanvas = (Microsoft.UI.Xaml.Controls.Canvas)rootCanvas.Children[0];
                var services = surface.DesignContext.Services;
                var innerItem = services.Component.GetDesignItem(innerCanvas);
                // Nothing selected - InnerCanvas is not the primary selection.
                var extension = innerItem.Extensions.OfType<PanelMove>().Single();

                extension.DragListener.SimulateDown(new Windows.Foundation.Point(150, 150));
                extension.DragListener.SimulateMove(new Windows.Foundation.Point(170, 165));
                extension.DragListener.SimulateUp(new Windows.Foundation.Point(170, 165));
                surface.UpdateLayout();

                var left = innerItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>();
                Assert.AreEqual(10, left, 0.5, "dragging an unselected container's body must not move it");
            });
        }

        [Test]
        public void ClickingAndDraggingASelectedContainersOwnBodyMovesTheContainerItself()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(NestedCanvasXaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                // Brief-defect correction: UseSnaplinePlacement defaults to true (DesignPanel.cs),
                // and SnaplinePlacementBehavior.BuildMaps treats the move's own CurrentContainer
                // (RootCanvas) as a snap-to sibling in its own right - its own edge/center guides
                // land close enough to this fixture's exact +20/+15 delta to nudge the result by
                // SnaplineAccuracy (5px), confirmed empirically (Top measured 20 instead of 25
                // with the flag left on). Same root cause and same fix as LoadSelected above.
                surface.DesignPanel.UseSnaplinePlacement = false;
                surface.UpdateLayout();

                var rootCanvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                var innerCanvas = (Microsoft.UI.Xaml.Controls.Canvas)rootCanvas.Children[0];
                var services = surface.DesignContext.Services;
                var innerItem = services.Component.GetDesignItem(innerCanvas);
                services.Selection.SetSelectedComponents(new[] { innerItem }, SelectionTypes.Primary);
                surface.UpdateLayout();
                var extension = innerItem.Extensions.OfType<PanelMove>().Single();

                extension.DragListener.SimulateDown(new Windows.Foundation.Point(150, 150));
                extension.DragListener.SimulateMove(new Windows.Foundation.Point(170, 165)); // +20,+15
                extension.DragListener.SimulateUp(new Windows.Foundation.Point(170, 165));
                surface.UpdateLayout();

                var left = innerItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>();
                var top = innerItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>();
                Assert.AreEqual(30, left, 0.5);
                Assert.AreEqual(25, top, 0.5);
            });
        }

        // Design-intent regression (see task context): PanelMove moves the CONTAINER, not
        // whatever child happens to be selected inside it. If the child, not the container, is
        // the primary selection, clicking-and-dragging the container's own body must be a no-op -
        // it must not silently move the container out from under its actually-selected child.
        [Test]
        public void ClickingAndDraggingAContainersBodyWhileAChildInsideItIsSelectedDoesNotMoveTheContainer()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(NestedCanvasXaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.DesignPanel.UseSnaplinePlacement = false;
                surface.UpdateLayout();

                var rootCanvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                var innerCanvas = (Microsoft.UI.Xaml.Controls.Canvas)rootCanvas.Children[0];
                var targetButton = (Microsoft.UI.Xaml.Controls.Button)innerCanvas.Children[0];
                var services = surface.DesignContext.Services;
                var innerItem = services.Component.GetDesignItem(innerCanvas);
                var targetItem = services.Component.GetDesignItem(targetButton);
                // The child (Target), not the container (InnerCanvas), is the primary selection.
                services.Selection.SetSelectedComponents(new[] { targetItem }, SelectionTypes.Primary);
                surface.UpdateLayout();
                var extension = innerItem.Extensions.OfType<PanelMove>().Single();

                extension.DragListener.SimulateDown(new Windows.Foundation.Point(150, 150));
                extension.DragListener.SimulateMove(new Windows.Foundation.Point(170, 165));
                extension.DragListener.SimulateUp(new Windows.Foundation.Point(170, 165));
                surface.UpdateLayout();

                var left = innerItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>();
                var top = innerItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>();
                Assert.AreEqual(10, left, 0.5, "dragging the container's body must not move it when a child inside it is selected instead");
                Assert.AreEqual(10, top, 0.5, "dragging the container's body must not move it when a child inside it is selected instead");
            });
        }

        // Final-review regression (I3): every PanelMove test above drives DragListener.SimulateDown
        // directly on the extension's own overlay, bypassing DesignPanel.OnPointerPressed entirely -
        // so none of them exercise the REAL pointer path, where the press bubbles up to
        // DesignPanel.OnPointerPressed (DragListener.OnPointerPressed never sets e.Handled) and
        // re-hit-tests/re-selects whatever is under the pointer before PanelMove's own Drag_Started
        // gate ever runs. Target here has the same Width/Height as InnerCanvas (and sits at its
        // origin), so it fully covers InnerCanvas's body - through the real pointer path this
        // must resolve the press to Target, not InnerCanvas, even though InnerCanvas starts out
        // selected.
        const string ContainerFullyCoveredByChildXaml =
            "<Canvas x:Name=\"RootCanvas\" Width=\"300\" Height=\"300\" " +
            "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Canvas x:Name=\"InnerCanvas\" Width=\"100\" Height=\"100\" Canvas.Left=\"10\" Canvas.Top=\"10\">" +
            "<Button x:Name=\"Target\" Width=\"100\" Height=\"100\" Canvas.Left=\"0\" Canvas.Top=\"0\" />" +
            "</Canvas></Canvas>";

        [Test]
        public void ClickingAContainerFullyCoveredByAChildThroughTheRealPointerPathSelectsTheChildNotTheContainer()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(ContainerFullyCoveredByChildXaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.DesignPanel.UseSnaplinePlacement = false;
                surface.UpdateLayout();

                var rootCanvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                var innerCanvas = (Microsoft.UI.Xaml.Controls.Canvas)rootCanvas.Children[0];
                var targetButton = (Microsoft.UI.Xaml.Controls.Button)innerCanvas.Children[0];
                var services = surface.DesignContext.Services;
                var innerItem = services.Component.GetDesignItem(innerCanvas);
                var targetItem = services.Component.GetDesignItem(targetButton);

                // InnerCanvas starts as the primary selection - through DragListener.SimulateDown
                // (every other test above), a body drag from here would move the container, per
                // ClickingAndDraggingASelectedContainersOwnBodyMovesTheContainerItself. The real
                // pointer path differs (I3): Target fully covers InnerCanvas, so the real
                // OnPointerPressed hit-test-and-select logic - DesignPanel.HandlePointerPressed,
                // the internal seam this test drives directly since PointerRoutedEventArgs has no
                // public constructor (Global Constraints) - resolves the press to Target instead.
                services.Selection.SetSelectedComponents(new[] { innerItem }, SelectionTypes.Primary);
                surface.UpdateLayout();
                var extension = innerItem.Extensions.OfType<PanelMove>().Single();

                var pressPoint = WindowPoint(innerItem, 50, 50); // lands on Target, which fills InnerCanvas

                surface.DesignPanel.HandlePointerPressed(pressPoint, Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();

                Assert.AreSame(targetItem, services.Selection.PrimarySelection,
                    "the real pointer path must re-select the covering child, not the container");

                // PanelMove's Drag_Started gate (PrimarySelection == ExtendedItem) now sees the
                // child selected, not InnerCanvas, so a drag starting from the same point must not
                // move the container. Drag_Started/Changed/Completed are still driven through
                // DragListener.SimulateDown/Move/Up (same seam as every other test in this file) -
                // only the selection state preceding the drag now reflects what the real pointer
                // path would actually have produced.
                extension.DragListener.SimulateDown(pressPoint);
                extension.DragListener.SimulateMove(WindowPoint(innerItem, 70, 65));
                extension.DragListener.SimulateUp(WindowPoint(innerItem, 70, 65));
                surface.UpdateLayout();

                var left = innerItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>();
                var top = innerItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>();
                Assert.AreEqual(10, left, 0.5,
                    "a container fully covered by a content-filling child has no real-pointer-path body-move surface (I3) - only the corner drag handle still reparents it");
                Assert.AreEqual(10, top, 0.5);
            });
        }
    }
}
