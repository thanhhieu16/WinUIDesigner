using System;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Controls
{
    /// <summary>
    /// Drives a single-item move gesture: opens a PlacementOperation, switches container mid-
    /// drag via hit-testing when the pointer crosses into a different container's bounds, and
    /// updates PlacementInformation.Bounds from the drag delta every tick. Ported from the
    /// reference's Services/MoveLogic.cs, narrowed to a single item (no multi-selection sort
    /// via ModelTools.ComparePositionInModelFile - not ported, no 4b consumer) and with no
    /// double-click-to-create-event-handler behavior (IEventHandlerService doesn't exist in
    /// this port). Driven by DragListener (window-coordinate points) instead of the
    /// reference's DesignPanel-relative MouseGestureBase - every point this class receives is
    /// converted from window space directly via TransformToVisual(null).Inverse, the same
    /// pattern DragListener.GetPosition already uses, rather than routing through an
    /// intermediate "DesignPanel-relative" space the way the reference does.
    /// </summary>
    internal sealed class MoveLogic
    {
        readonly DesignItem clickedOn;
        PlacementOperation operation;
        Point startWindowPoint;

        public MoveLogic(DesignItem clickedOn)
        {
            this.clickedOn = clickedOn ?? throw new ArgumentNullException(nameof(clickedOn));
        }

        public void Start(Point windowPoint)
        {
            startWindowPoint = windowPoint;
            var items = new[] { clickedOn };
            var behavior = PlacementOperation.GetPlacementBehavior(items);
            if (behavior != null && behavior.CanPlace(items, PlacementType.Move, PlacementAlignment.TopLeft))
                operation = PlacementOperation.Start(items, PlacementType.Move);
        }

        public void Move(Point windowPoint, VirtualKeyModifiers modifiers)
        {
            if (operation == null) return;

            if (operation.CurrentContainerBehavior.CanLeaveContainer(operation))
                ChangeContainerIfPossible(windowPoint);

            double dx, dy;
            var container = operation.CurrentContainer.View;
            var inverse = container != null ? container.TransformToVisual(null).Inverse : null;
            if (inverse != null) {
                var p = inverse.TransformPoint(windowPoint);
                var s = inverse.TransformPoint(startWindowPoint);
                dx = p.X - s.X;
                dy = p.Y - s.Y;
            } else {
                dx = windowPoint.X - startWindowPoint.X;
                dy = windowPoint.Y - startWindowPoint.Y;
            }

            foreach (var info in operation.PlacedItems) {
                info.Bounds = new Rect(
                    info.OriginalBounds.X + Math.Round(dx, PlacementInformation.BoundsPrecision),
                    info.OriginalBounds.Y + Math.Round(dy, PlacementInformation.BoundsPrecision),
                    info.OriginalBounds.Width,
                    info.OriginalBounds.Height);
            }
            operation.CurrentContainerBehavior.BeforeSetPosition(operation, modifiers);
            foreach (var info in operation.PlacedItems)
                operation.CurrentContainerBehavior.SetPosition(info);
        }

        public void Stop()
        {
            if (operation == null) return;
            operation.Commit();
            operation = null;
        }

        public void Cancel()
        {
            if (operation == null) return;
            operation.Abort();
            operation = null;
        }

        bool ChangeContainerIfPossible(Point windowPoint)
        {
            var result = HitTestUnselectedModel(windowPoint);
            if (result.ModelHit == null) return false;
            if (result.ModelHit == operation.CurrentContainer) return false;

            var tmp = result.ModelHit;
            while (tmp != null) {
                if (tmp == clickedOn) return false;
                tmp = tmp.Parent;
            }

            var behavior = result.ModelHit.GetBehavior<IPlacementBehavior>();
            if (behavior != null && behavior.CanEnterContainer(operation, false)) {
                operation.ChangeContainer(result.ModelHit);
                return true;
            }
            return false;
        }

        DesignPanelHitTestResult HitTestUnselectedModel(Point windowPoint)
        {
            var result = DesignPanelHitTestResult.NoHit;
            var services = clickedOn.Services;
            var selection = services.Selection;
            var designPanel = services.DesignPanel;
            var designPanelElement = designPanel as UIElement;

            var panelPoint = windowPoint;
            if (designPanelElement != null) {
                var inverse = designPanelElement.TransformToVisual(null).Inverse;
                if (inverse != null) panelPoint = inverse.TransformPoint(windowPoint);
            }

            designPanel.HitTest(panelPoint, false, true, r => {
                if (r.ModelHit == null) return true;
                if (selection.IsComponentSelected(r.ModelHit)) return true;
                result = r;
                return false;
            }, HitTestType.Default);

            return result;
        }
    }
}
