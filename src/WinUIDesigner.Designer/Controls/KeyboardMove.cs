using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Controls
{
    /// <summary>
    /// Moves the selected items with the arrow keys. One press, held down and repeating or not,
    /// is one PlacementOperation and so one undo entry: the operation starts on the first
    /// KeyDown and is committed on the KeyUp (or when the panel loses focus). Raster and
    /// snapline snapping are not applied - BeforeSetPosition is not called - since a 1px step
    /// that snapped back to the grid would not move at all. Keyboard-move spec.
    /// </summary>
    internal sealed class KeyboardMove
    {
        PlacementOperation operation;
        double totalX, totalY;

        public bool IsActive => operation != null;

        /// <summary>
        /// Moves <paramref name="selection"/> a further (<paramref name="dx"/>,
        /// <paramref name="dy"/>). Returns false when there is nothing that can be moved: no
        /// selection, only the root, or a container that refuses a move.
        /// </summary>
        public bool Nudge(IEnumerable<DesignItem> selection, double dx, double dy)
        {
            if (operation == null && !Begin(selection))
                return false;

            totalX += dx;
            totalY += dy;
            foreach (var info in operation.PlacedItems) {
                info.Bounds = new Rect(
                    info.OriginalBounds.X + totalX,
                    info.OriginalBounds.Y + totalY,
                    info.OriginalBounds.Width,
                    info.OriginalBounds.Height);
                operation.CurrentContainerBehavior.SetPosition(info);
            }
            return true;
        }

        public void Commit()
        {
            if (operation == null) return;
            var op = operation;
            operation = null;
            op.Commit();
        }

        public void Abort()
        {
            if (operation == null) return;
            var op = operation;
            operation = null;
            op.Abort();
        }

        bool Begin(IEnumerable<DesignItem> selection)
        {
            if (selection == null) return false;
            // The root has no container to move within. Items in different containers cannot
            // share one operation, so a mixed selection moves those sharing the primary's.
            var items = selection.Where(i => i != null && i.Parent != null).ToList();
            if (items.Count == 0) return false;
            var parent = items[0].Parent;
            items = items.Where(i => i.Parent == parent).ToList();

            var behavior = PlacementOperation.GetPlacementBehavior(items);
            if (behavior == null || !behavior.CanPlace(items, PlacementType.Move, PlacementAlignment.TopLeft))
                return false;
            try {
                operation = PlacementOperation.Start(items, PlacementType.Move);
            } catch (PlacementOperation.PlacementOperationException) {
                return false;
            }
            totalX = 0;
            totalY = 0;
            return true;
        }
    }
}
