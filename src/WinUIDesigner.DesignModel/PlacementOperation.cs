using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.Foundation;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.DesignModel
{
    /// <summary>
    /// Stores data about a placement operation. Ported from the reference's
    /// PlacementOperation.cs with ChangeContainer, DeleteItemsAndCommit, and
    /// TryStartInsertNewComponents dropped: their only callers are container-to-container
    /// move (Phase 4b), the delete command (out of scope per Phase 3), and toolbox-drop
    /// insertion (no Phase 4a caller) - and ChangeContainer's own TransformRectByMiddlePoint
    /// helper used Vector/Rect.TopLeft/Rect.Size/GeneralTransform.Transform(Point), none of
    /// which exist in WinUI (compiler-confirmed during spec brainstorming). Same "port only
    /// the called surface" treatment already applied to ModelTools.cs/Metadata.cs.
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
