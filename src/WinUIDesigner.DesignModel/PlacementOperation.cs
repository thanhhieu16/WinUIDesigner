using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WinUIDesigner.DesignModel
{
    /// <summary>
    /// Stores data about a placement operation. Ported from the reference's
    /// PlacementOperation.cs with DeleteItemsAndCommit and TryStartInsertNewComponents
    /// dropped: their only callers are the delete command (out of scope per Phase 3) and
    /// toolbox-drop insertion (no Phase 4a/4b caller). Same "port only the called surface"
    /// treatment already applied to ModelTools.cs/Metadata.cs.
    /// </summary>
    public sealed class PlacementOperation
    {
        public sealed class PlacementOperationException : InvalidOperationException
        {
            public PlacementOperationException(string message) : base(message) { }
        }

        readonly ChangeGroup changeGroup;
        readonly ReadOnlyCollection<PlacementInformation> placedItems;
        readonly PlacementType type;
        DesignItem currentContainer;
        IPlacementBehavior currentContainerBehavior;
        bool isAborted, isCommitted;

        public ReadOnlyCollection<PlacementInformation> PlacedItems => placedItems;
        public PlacementType Type => type;
        public bool IsAborted => isAborted;
        public bool IsCommitted => isCommitted;
        public DesignItem CurrentContainer => currentContainer;
        public IPlacementBehavior CurrentContainerBehavior => currentContainerBehavior;

        public static PlacementOperation Start(ICollection<DesignItem> placedItems, PlacementType type)
        {
            if (placedItems == null) throw new ArgumentNullException(nameof(placedItems));
            if (type == null) throw new ArgumentNullException(nameof(type));
            DesignItem[] items = placedItems.ToArray();
            if (items.Length == 0) throw new ArgumentException("placedItems.Length must be > 0");

            var op = new PlacementOperation(items, type);
            if (op.currentContainerBehavior == null) {
                op.changeGroup.Abort();
                throw new PlacementOperationException("Starting the operation is not supported");
            }
            try {
                op.currentContainerBehavior.BeginPlacement(op);
                foreach (var info in op.placedItems) {
                    info.OriginalBounds = op.currentContainerBehavior.GetPosition(op, info.Item);
                    info.Bounds = info.OriginalBounds;
                }
            } catch {
                op.changeGroup.Abort();
                throw;
            }
            return op;
        }

        PlacementOperation(DesignItem[] items, PlacementType type)
        {
            List<DesignItem> moveableItems;
            currentContainerBehavior = GetPlacementBehavior(items, out moveableItems, type);

            var information = new PlacementInformation[moveableItems.Count];
            for (int i = 0; i < information.Length; i++)
                information[i] = new PlacementInformation(moveableItems[i], this);
            placedItems = new ReadOnlyCollection<PlacementInformation>(information);
            this.type = type;
            currentContainer = moveableItems[0].Parent;
            changeGroup = moveableItems[0].Context.OpenGroup(type.ToString(), moveableItems);
        }

        /// <summary>
        /// Makes the placed item(s) switch container. Ported from the reference's
        /// ChangeContainer - the only two changes from the reference are the coordinate math
        /// (GeneralTransform.Transform(Point)/Vector/Rect.TopLeft/Rect.Size don't exist in
        /// WinUI - TransformPoint plus component-wise double arithmetic replaces them) and a
        /// latent NullReferenceException the reference has (it Debug.Asserts
        /// currentContainerBehavior non-null after reassigning it, then dereferences
        /// unconditionally - Debug.Assert is a no-op in Release builds). This port validates
        /// the new behavior before it ever replaces the field, so a null new-container
        /// behavior throws a clear PlacementOperationException instead, and the catch below
        /// still finds the OLD (already-left) behavior on currentContainerBehavior to run
        /// Abort()'s EndPlacement against, instead of null-derefing there too.
        /// </summary>
        public void ChangeContainer(DesignItem newContainer)
        {
            if (newContainer == null) throw new ArgumentNullException(nameof(newContainer));
            if (isAborted || isCommitted) throw new PlacementOperationException("The operation is not running anymore.");
            if (currentContainer == newContainer) return;

            if (!currentContainerBehavior.CanLeaveContainer(this))
                throw new PlacementOperationException("The items cannot be removed from their parent container.");

            try {
                currentContainerBehavior.LeaveContainer(this);

                var transform = currentContainer.View.TransformToVisual(newContainer.View);
                foreach (var info in placedItems) {
                    info.OriginalBounds = TransformRectByMiddlePoint(transform, info.OriginalBounds);
                    info.Bounds = TransformRectByMiddlePoint(transform, info.Bounds).Round();
                }

                var newBehavior = newContainer.GetBehavior<IPlacementBehavior>();
                if (newBehavior == null)
                    throw new PlacementOperationException("The new container has no placement behavior.");

                currentContainer = newContainer;
                currentContainerBehavior = newBehavior;
                currentContainerBehavior.EnterContainer(this);
            } catch {
                Abort();
                throw;
            }
        }

        static Rect TransformRectByMiddlePoint(GeneralTransform transform, Rect r)
        {
            // We don't want to adjust the size of the control when moving it out of a scaled
            // container - just move it correctly. Transforming the middle point and taking the
            // component-wise delta is the Vector-free equivalent of the reference's
            // "transform.Transform(p) - p" Vector subtraction (Vector doesn't exist in WinUI).
            var p = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            var transformedP = transform.TransformPoint(p);
            double dx = transformedP.X - p.X;
            double dy = transformedP.Y - p.Y;
            return new Rect(r.X + dx, r.Y + dy, r.Width, r.Height);
        }

        /// <summary>The size an element really should have (even if smaller-rendered, like an
        /// empty Image).</summary>
        public static Size GetRealElementSize(UIElement element)
        {
            var size = element.RenderSize;
            var fe = element as FrameworkElement;
            if (fe != null && !double.IsNaN(fe.Width)) size.Width = fe.Width;
            if (fe != null && !double.IsNaN(fe.Height)) size.Height = fe.Height;
            if (fe != null && size.Width < fe.MinWidth) size.Width = fe.MinWidth;
            if (fe != null && size.Height < fe.MinHeight) size.Height = fe.MinHeight;
            return size;
        }

        public static IPlacementBehavior GetPlacementBehavior(ICollection<DesignItem> items)
        {
            List<DesignItem> moveableItems;
            return GetPlacementBehavior(items, out moveableItems, PlacementType.Move);
        }

        public static IPlacementBehavior GetPlacementBehavior(ICollection<DesignItem> items, out List<DesignItem> moveableItems, PlacementType placementType)
        {
            moveableItems = new List<DesignItem>();
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (items.Count == 0) return null;

            var possibleItems = items;
            if (!items.Any(x => x.Parent == null)) {
                var itemsParentGroup = items.GroupBy(x => x.Parent);
                var parents = itemsParentGroup.Select(x => x.Key).OrderBy(x => x.DepthLevel).First();
                possibleItems = itemsParentGroup.Where(x => x.Key.DepthLevel == parents.DepthLevel).SelectMany(x => x).ToList();
            }

            var first = possibleItems.First();
            DesignItem parent = first.Parent;
            moveableItems.Add(first);
            foreach (var item in possibleItems.Skip(1)) {
                if (item.Parent != parent) {
                    if (placementType != PlacementType.MoveAndIgnoreOtherContainers) return null;
                } else {
                    moveableItems.Add(item);
                }
            }
            if (parent != null) return parent.GetBehavior<IPlacementBehavior>();
            if (possibleItems.Count() == 1) return first.GetBehavior<IRootPlacementBehavior>();
            return null;
        }

        public string Description {
            get => changeGroup.Title;
            set => changeGroup.Title = value;
        }

        // Abort/Commit dereference currentContainerBehavior unguarded (no null check). Safe
        // because Start() only ever returns a PlacementOperation once currentContainerBehavior
        // is non-null, and ChangeContainer validates the new behavior before ever assigning it
        // to this field (see ChangeContainer above) - currentContainerBehavior can never become
        // null over this object's lifetime.
        public void Abort()
        {
            if (isAborted) return;
            if (isCommitted) throw new PlacementOperationException("PlacementOperation is committed.");
            isAborted = true;
            currentContainerBehavior.EndPlacement(this);
            changeGroup.Abort();
        }

        public void Commit()
        {
            if (isAborted || isCommitted) throw new PlacementOperationException("PlacementOperation is already aborted/committed.");
            isCommitted = true;
            currentContainerBehavior.EndPlacement(this);
            changeGroup.Commit();
        }
    }
}
