using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;

namespace WinUIDesigner.Designer
{
    /// <summary>
    /// Static helper methods for working with the designer DOM. Ported from the reference's
    /// ModelTools.cs (890 lines) with only the members actually called by a phase so far -
    /// Phase 4a added GetWidth/GetHeight/Resize/ApplyTransform; Phase 4c Task 4 added
    /// CanDeleteComponents/DeleteComponents; Phase 4c Task 5 added ArrangeItems; Phase 4c
    /// Task 6 added WrapItemsNewContainer/UnwrapItemsFromContainer (below).
    /// StretchItems/IsInDocument/ComparePositionInModelFile remain unported multi-selection/
    /// context-menu helpers with no consumer yet (4e territory).
    /// </summary>
    public static class ModelTools
    {
        public static double GetWidth(UIElement element)
        {
            var fe = (FrameworkElement)element;
            double v = fe.Width;
            return double.IsNaN(v) ? element.RenderSize.Width : v;
        }

        public static double GetHeight(UIElement element)
        {
            var fe = (FrameworkElement)element;
            double v = fe.Height;
            return double.IsNaN(v) ? element.RenderSize.Height : v;
        }

        public static void Resize(DesignItem item, double newWidth, double newHeight)
        {
            if (newWidth != GetWidth(item.View)) {
                if (double.IsNaN(newWidth))
                    item.Properties.GetProperty("Width").Reset();
                else
                    item.Properties.GetProperty("Width").SetValue(newWidth);
            }
            if (newHeight != GetHeight(item.View)) {
                if (double.IsNaN(newHeight))
                    item.Properties.GetProperty("Height").Reset();
                else
                    item.Properties.GetProperty("Height").SetValue(newHeight);
            }
        }

        /// <summary>
        /// Applies (or replaces) a render transform on the design item. transformProperty is a
        /// string (not a DependencyProperty, unlike the reference) - see the spec's
        /// "DesignItemProperty are string-keyed only" finding - defaulting to "RenderTransform".
        /// Gap #4 (spec): the TransformGroup-wrapping branch below (when an existing transform
        /// of a different type needs to become part of a group) has not been exercised by any
        /// prior phase against a non-FrameworkElement WinRT type - see
        /// ModelToolsTests.ApplyingASecondTransformKindWrapsBothInATransformGroup.
        /// </summary>
        public static void ApplyTransform(DesignItem designItem, Transform transform, bool relative = true, string transformProperty = null)
        {
            var changeGroup = designItem.OpenGroup("Apply Transform");
            transformProperty = transformProperty ?? "RenderTransform";

            Transform oldTransform = null;
            if (designItem.Properties.GetProperty(transformProperty).IsSet)
                oldTransform = designItem.Properties.GetProperty(transformProperty).GetConvertedValueOnInstance<Transform>();

            if (oldTransform != null && oldTransform.GetType() != transform.GetType() && !(oldTransform is TransformGroup)) {
                var tg = new TransformGroup();
                var tgDes = designItem.Services.Component.RegisterComponentForDesigner(tg);
                var oldTransformItem = designItem.Services.Component.GetDesignItem(oldTransform);
                // Gap #4 (spec) empirical finding: the reference's order here - Add the old
                // transform into the new (still-unattached) TransformGroup's children, THEN
                // SetValue the property to the group - reproducibly threw
                // ArgumentException("The reference node is not a child of this node.") from
                // XamlProperty.InsertNodeInCollection when the very next CollectionElements.Add
                // in this same call (the RotateTransform/TransformGroup branch below, adding the
                // new transform into the group) ran against the now-desynced collection. Root
                // cause (traced through
                // WinUIDesigner.XamlDom.XamlProperty/XamlObject, both unchanged Phase 1 code, so
                // not fixable inside this file's scope): CollectionElementsCollection.InsertItem
                // reparents the old transform's XmlElement into the TransformGroup via a plain
                // XmlNode.AppendChild *without* updating designItem's own RenderTransform
                // XamlProperty, whose propertyValue field still points at the old transform. The
                // subsequent SetValue(tg) call resets that stale propertyValue, and
                // XamlObject.RemoveNodeFromParent walks the (now wrong) live element.ParentNode -
                // the TransformGroup, not the original RenderTransform property element - silently
                // yanking the just-moved child back out. Swapping the order (SetValue first, so
                // the old transform's node is cleanly detached from RenderTransform's own property
                // element while everyone's bookkeeping still agrees where it lives, before it gets
                // re-parented into the new, already-attached TransformGroup) avoids the stale
                // reference entirely - confirmed by ModelToolsTests.ApplyingASecondTransformKindWrapsBothInATransformGroup.
                designItem.Properties.GetProperty(transformProperty).SetValue(tg);
                tgDes.ContentProperty.CollectionElements.Add(oldTransformItem);
                oldTransform = tg;
            }

            if (transform is RotateTransform rotateTransform) {
                if (oldTransform is RotateTransform || oldTransform == null) {
                    if (rotateTransform.Angle != 0) {
                        designItem.Properties.GetProperty(transformProperty).SetValue(transform);
                        var angle = rotateTransform.Angle;
                        if (relative && oldTransform != null)
                            angle = rotateTransform.Angle + ((RotateTransform)oldTransform).Angle;
                        designItem.Properties.GetProperty(transformProperty).Value.Properties.GetProperty("Angle").SetValue(angle);
                        if (oldTransform == null)
                            designItem.Properties.GetProperty("RenderTransformOrigin").SetValue(new Windows.Foundation.Point(0.5, 0.5));
                    } else {
                        designItem.Properties.GetProperty(transformProperty).Reset();
                        designItem.Properties.GetProperty("RenderTransformOrigin").Reset();
                    }
                } else if (oldTransform is TransformGroup tg) {
                    var tgItem = designItem.Services.Component.GetDesignItem(tg);
                    var existingRotate = tg.Children.OfType<RotateTransform>().FirstOrDefault();
                    if (existingRotate != null)
                        tgItem.ContentProperty.CollectionElements.Remove(designItem.Services.Component.GetDesignItem(existingRotate));
                    if (rotateTransform.Angle != 0) {
                        var des = designItem.Services.Component.GetDesignItem(transform) ?? designItem.Services.Component.RegisterComponentForDesigner(transform);
                        tgItem.ContentProperty.CollectionElements.Add(des);
                    }
                } else if (rotateTransform.Angle != 0) {
                    designItem.Properties.GetProperty(transformProperty).SetValue(transform);
                    if (oldTransform == null)
                        designItem.Properties.GetProperty("RenderTransformOrigin").SetValue(new Windows.Foundation.Point(0.5, 0.5));
                }
            } else if (transform is SkewTransform) {
                designItem.Properties.GetProperty(transformProperty).SetValue(transform);
            }

            ((DesignPanel)designItem.Services.DesignPanel).AdornerLayer.UpdateAdornersForElement(designItem.View, true);
            changeGroup.Commit();
        }

        /// <summary>Gets if the specified components can be deleted. Ported unchanged from the
        /// reference (WpfDesign.Designer/Project/ModelTools.cs:84-89) - DefaultPlacementBehavior.
        /// CanPlace already returns true unconditionally for any PlacementType (Extensions/
        /// DefaultPlacementBehavior.cs:46), so this works against every placement behavior in
        /// this port with zero changes to any of them.</summary>
        public static bool CanDeleteComponents(ICollection<DesignItem> items)
        {
            IPlacementBehavior b = PlacementOperation.GetPlacementBehavior(items);
            return b != null && b.CanPlace(items, PlacementType.Delete, PlacementAlignment.Center);
        }

        /// <summary>Deletes the specified components from their parent containers, as one
        /// undo entry regardless of how many distinct parents are involved. Ported from the
        /// reference (WpfDesign.Designer/Project/ModelTools.cs:100-138) - RaiseComponentRemoved
        /// must be called explicitly (checked directly against this port's XamlComponentService,
        /// not automatic - see this plan's Global Constraints).</summary>
        public static void DeleteComponents(ICollection<DesignItem> deleteItems)
        {
            if (deleteItems.Count == 0) return;

            var changeGroup = deleteItems.First().OpenGroup("Delete Items");
            try {
                var itemsGrpParent = deleteItems.GroupBy(x => x.Parent);
                foreach (var itemsList in itemsGrpParent) {
                    var items = itemsList.ToList();
                    DesignItem parent = items.First().Parent;
                    PlacementOperation operation = PlacementOperation.Start(items, PlacementType.Delete);
                    try {
                        var selectionService = items.First().Services.Selection;
                        selectionService.SetSelectedComponents(items, SelectionTypes.Remove);
                        if (selectionService.SelectionCount == 0 && !items.Contains(parent)) {
                            selectionService.SetSelectedComponents(new[] { parent });
                        }
                        foreach (var designItem in items) {
                            designItem.Name = null;
                        }

                        var service = parent.Services.Component as XamlComponentService;
                        foreach (var item in items) {
                            service.RaiseComponentRemoved(item);
                        }

                        operation.DeleteItemsAndCommit();
                    } catch {
                        operation.Abort();
                        throw;
                    }
                }
                changeGroup.Commit();
            } catch {
                changeGroup.Abort();
                throw;
            }
        }

        sealed class ItemPos
        {
            public DesignItem DesignItem;
            public double Xmin, Xmax, Ymin, Ymax;
        }

        static ItemPos GetItemPos(PlacementOperation operation, DesignItem designItem)
        {
            var pos = operation.CurrentContainerBehavior.GetPosition(operation, designItem);
            return new ItemPos {
                DesignItem = designItem,
                Xmin = pos.X, Xmax = pos.X + pos.Width,
                Ymin = pos.Y, Ymax = pos.Y + pos.Height,
            };
        }

        /// <summary>Aligns the given items along one edge/midline of their combined bounds.
        /// Ported from the reference (WpfDesign.Designer/Project/ModelTools.cs:628-870) with
        /// two changes: (1) the Canvas branch drops the Canvas.Right/Bottom-anchored `else`
        /// case entirely (those attached properties don't exist in WinUI - confirmed absent,
        /// CLAUDE.md) rather than reproducing an unreachable branch; (2) the reference's Top
        /// direction has a stray copy-pasted `Canvas.TopProperty.SetValue` line inside its Grid
        /// branch (a WPF attached property a Grid ignores, immediately followed by the actually-
        /// correct margin.Top assignment) - dropped, not reproduced, matching this port's
        /// established "fix a latent reference bug while porting" precedent. The Grid branch
        /// otherwise ports both alignment cases unchanged - no WinUI gap there, Margin.Right
        /// exists.</summary>
        public static void ArrangeItems(IEnumerable<DesignItem> items, ArrangeDirection arrangeDirection)
        {
            var collection = items.ToList();
            var container = collection.First().Parent;
            if (collection.Any(x => x.Parent != container)) return;

            var placement = container.GetBehavior<IPlacementBehavior>();
            if (placement == null) return;

            var operation = PlacementOperation.Start(collection, PlacementType.Move);
            try {
                var itemList = collection.Select(item => GetItemPos(operation, item)).ToList();
                var xmin = itemList.Min(x => x.Xmin);
                var xmax = itemList.Max(x => x.Xmax);
                var mpos = (xmax - xmin) / 2 + xmin;
                var ymin = itemList.Min(x => x.Ymin);
                var ymax = itemList.Max(x => x.Ymax);
                var ympos = (ymax - ymin) / 2 + ymin;

                foreach (var item in collection) {
                    var fe = (FrameworkElement)item.Component;
                    switch (arrangeDirection) {
                        case ArrangeDirection.Left:
                            if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                                item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").SetValue(xmin);
                            } else if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                                SetGridMargin(item, isRightAligned: HAlign(item) == HorizontalAlignment.Right,
                                    leftValue: xmin, rightValue: ((FrameworkElement)item.Parent.Component).ActualWidth - (xmin + fe.ActualWidth));
                            }
                            break;
                        case ArrangeDirection.HorizontalMiddle:
                            if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                                item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").SetValue(mpos - fe.ActualWidth / 2);
                            } else if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                                var left = mpos - fe.ActualWidth / 2;
                                SetGridMargin(item, isRightAligned: HAlign(item) == HorizontalAlignment.Right,
                                    leftValue: left, rightValue: ((FrameworkElement)item.Parent.Component).ActualWidth - left - fe.ActualWidth);
                            }
                            break;
                        case ArrangeDirection.Right:
                            if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                                item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").SetValue(xmax - fe.ActualWidth);
                            } else if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                                var left = xmax - fe.ActualWidth;
                                SetGridMargin(item, isRightAligned: HAlign(item) == HorizontalAlignment.Right,
                                    leftValue: left, rightValue: ((FrameworkElement)item.Parent.Component).ActualWidth - xmax);
                            }
                            break;
                        case ArrangeDirection.Top:
                            if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                                item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").SetValue(ymin);
                            } else if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                                SetGridMarginVertical(item, isBottomAligned: VAlign(item) == VerticalAlignment.Bottom,
                                    topValue: ymin, bottomValue: ((FrameworkElement)item.Parent.Component).ActualHeight - (ymin + fe.ActualHeight));
                            }
                            break;
                        case ArrangeDirection.VerticalMiddle:
                            if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                                item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").SetValue(ympos - fe.ActualHeight / 2);
                            } else if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                                var top = ympos - fe.ActualHeight / 2;
                                SetGridMarginVertical(item, isBottomAligned: VAlign(item) == VerticalAlignment.Bottom,
                                    topValue: top, bottomValue: ((FrameworkElement)item.Parent.Component).ActualHeight - top - fe.ActualHeight);
                            }
                            break;
                        case ArrangeDirection.Bottom:
                            if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                                item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").SetValue(ymax - fe.ActualHeight);
                            } else if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                                var top = ymax - fe.ActualHeight;
                                SetGridMarginVertical(item, isBottomAligned: VAlign(item) == VerticalAlignment.Bottom,
                                    topValue: top, bottomValue: ((FrameworkElement)item.Parent.Component).ActualHeight - ymax);
                            }
                            break;
                    }
                }

                operation.Commit();
            } catch {
                operation.Abort();
                throw;
            }
        }

        static HorizontalAlignment HAlign(DesignItem item) =>
            item.Properties.GetProperty("HorizontalAlignment").GetConvertedValueOnInstance<HorizontalAlignment>();

        static VerticalAlignment VAlign(DesignItem item) =>
            item.Properties.GetProperty("VerticalAlignment").GetConvertedValueOnInstance<VerticalAlignment>();

        static void SetGridMargin(DesignItem item, bool isRightAligned, double leftValue, double rightValue)
        {
            var margin = item.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Thickness>();
            if (isRightAligned) margin.Right = rightValue; else margin.Left = leftValue;
            item.Properties.GetProperty("Margin").SetValue(margin);
        }

        static void SetGridMarginVertical(DesignItem item, bool isBottomAligned, double topValue, double bottomValue)
        {
            var margin = item.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Thickness>();
            if (isBottomAligned) margin.Bottom = bottomValue; else margin.Top = topValue;
            item.Properties.GetProperty("Margin").SetValue(margin);
        }

        /// <summary>Wraps the given items (all siblings under the same parent) in a new
        /// container instance, replacing them at their combined position. Ported from the
        /// reference (WpfDesign.Designer/Project/ModelTools.cs:257-380) with the reference's own
        /// dead Right/Bottom-anchored branches (ItemPos.HorizontalAlignment/VerticalAlignment
        /// are read but never assigned anywhere in the reference - always false) collapsed to
        /// their always-taken Left/Top-anchored case - see this plan's Task 6 note. When the old
        /// container is a Canvas, the wrapped items' Canvas.Left/Top ARE reset here (final-review
        /// fix I3 - stale values would otherwise persist in the serialized XAML on an item no
        /// longer inside a Canvas); only Canvas.Right/BottomProperty resets are dropped, because
        /// those attached properties don't exist in WinUI (confirmed absent, CLAUDE.md) and this
        /// port never sets them in the first place.</summary>
        public static System.Tuple<DesignItem, Rect> WrapItemsNewContainer(IEnumerable<DesignItem> items, System.Type containerType, bool doInsert = true)
        {
            var collection = items.ToList();
            var context = collection.First().Context;
            var container = collection.First().Parent;
            if (container == null) return null;
            if (collection.Any(x => x.Parent != container)) return null;

            var placement = container.GetBehavior<IPlacementBehavior>();
            if (placement == null) return null;

            var operation = PlacementOperation.Start(collection, PlacementType.Move);
            try {
                var newInstance = context.Services.ExtensionManager.CreateInstanceWithCustomInstanceFactory(containerType, null);
                DesignItem newPanel = context.Services.Component.RegisterComponentForDesigner(newInstance);

                var itemList = new List<ItemPos>();
                int? firstIndex = null;

                foreach (var item in collection) {
                    itemList.Add(GetItemPos(operation, item));
                    if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                        item.Properties.GetProperty("HorizontalAlignment").Reset();
                        item.Properties.GetProperty("VerticalAlignment").Reset();
                        item.Properties.GetProperty("Margin").Reset();
                    } else if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                        item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").Reset();
                        item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").Reset();
                    }

                    if (item.ParentProperty.IsCollection) {
                        var parentCollection = item.ParentProperty.CollectionElements;
                        if (!firstIndex.HasValue) firstIndex = parentCollection.IndexOf(item);
                        parentCollection.Remove(item);
                    } else {
                        item.ParentProperty.Reset();
                    }
                }

                var xmin = itemList.Min(x => x.Xmin);
                var xmax = itemList.Max(x => x.Xmax);
                var ymin = itemList.Min(x => x.Ymin);
                var ymax = itemList.Max(x => x.Ymax);

                foreach (var item in itemList) {
                    if (newPanel.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                        item.DesignItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").SetValue(item.Xmin - xmin);
                        item.DesignItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").SetValue(item.Ymin - ymin);
                        newPanel.ContentProperty.CollectionElements.Add(item.DesignItem);
                    } else if (newPanel.Component is Microsoft.UI.Xaml.Controls.Grid) {
                        var thickness = new Thickness(item.Xmin - xmin, item.Ymin - ymin, 0, 0);
                        item.DesignItem.Properties.GetProperty("HorizontalAlignment").SetValue(HorizontalAlignment.Left);
                        item.DesignItem.Properties.GetProperty("VerticalAlignment").SetValue(VerticalAlignment.Top);
                        item.DesignItem.Properties.GetProperty("Margin").SetValue(thickness);
                        newPanel.ContentProperty.CollectionElements.Add(item.DesignItem);
                    } else if (newPanel.Component is Microsoft.UI.Xaml.Controls.Viewbox || newPanel.Component is Microsoft.UI.Xaml.Controls.ContentControl) {
                        newPanel.ContentProperty.SetValue(item.DesignItem);
                    }
                }

                if (doInsert) {
                    var operation2 = PlacementOperation.TryStartInsertNewComponents(
                        container,
                        new[] { newPanel },
                        new[] { new Rect(xmin, ymin, xmax - xmin, ymax - ymin) },
                        PlacementType.AddItem);

                    if (operation2 == null) {
                        operation.Abort();
                        return null;
                    }

                    try {
                        if (collection.Count == 1 && firstIndex.HasValue && container.ContentProperty != null && container.ContentProperty.IsCollection) {
                            container.ContentProperty.CollectionElements.Remove(newPanel);
                            container.ContentProperty.CollectionElements.Insert(firstIndex.Value, newPanel);
                        }

                        operation2.Commit();
                        context.Services.Selection.SetSelectedComponents(new[] { newPanel });
                    } catch {
                        operation2.Abort();
                        throw;
                    }
                }

                operation.Commit();
                return new System.Tuple<DesignItem, Rect>(newPanel, new Rect(xmin, ymin, xmax - xmin, ymax - ymin));
            } catch {
                operation.Abort();
                throw;
            }
        }

        /// <summary>Moves a container's children back out to the container's own parent,
        /// removing the now-empty container. Ported unchanged in shape from the reference
        /// (WpfDesign.Designer/Project/ModelTools.cs:382-502) - same dead-branch collapse as
        /// WrapItemsNewContainer above. When the container being unwrapped is itself a Canvas,
        /// each child's Canvas.Left/Top ARE reset here (final-review fix I3) before it's re-
        /// parented; only Canvas.Right/BottomProperty resets are dropped (don't exist in WinUI -
        /// confirmed absent, CLAUDE.md - and this port never sets them). Guards against a null
        /// root Parent and an empty container (final-review fixes C1/C2) return before
        /// PlacementOperation.Start is ever called - otherwise the null Parent dereference used
        /// to happen only after the transaction had already removed every child from the tree,
        /// and an empty collection made PlacementOperation.Start itself throw.</summary>
        public static void UnwrapItemsFromContainer(DesignItem container)
        {
            var collection = container.ContentProperty.CollectionElements.ToList();
            if (collection.Count == 0) return;
            var newPanel = container.Parent;
            if (newPanel == null) return;
            if (collection.Any(x => x.Parent != container)) return;

            var placement = container.GetBehavior<IPlacementBehavior>();
            if (placement == null) return;

            var operation = PlacementOperation.Start(collection, PlacementType.Move);
            try {
                var containerPos = GetItemPos(operation, container);
                var itemList = new List<ItemPos>();

                foreach (var item in collection) {
                    itemList.Add(GetItemPos(operation, item));
                    if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                        item.Properties.GetProperty("HorizontalAlignment").Reset();
                        item.Properties.GetProperty("VerticalAlignment").Reset();
                        item.Properties.GetProperty("Margin").Reset();
                    } else if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                        item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").Reset();
                        item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").Reset();
                    }
                    item.ParentProperty.CollectionElements.Remove(item);
                }

                newPanel.ContentProperty.CollectionElements.Remove(container);

                foreach (var item in itemList) {
                    if (newPanel.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                        item.DesignItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").SetValue(item.Xmin + containerPos.Xmin);
                        item.DesignItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").SetValue(item.Ymin + containerPos.Ymin);
                        newPanel.ContentProperty.CollectionElements.Add(item.DesignItem);
                    } else if (newPanel.Component is Microsoft.UI.Xaml.Controls.Grid) {
                        var thickness = new Thickness(item.Xmin, item.Ymin, 0, 0);
                        item.DesignItem.Properties.GetProperty("HorizontalAlignment").SetValue(HorizontalAlignment.Left);
                        item.DesignItem.Properties.GetProperty("VerticalAlignment").SetValue(VerticalAlignment.Top);
                        item.DesignItem.Properties.GetProperty("Margin").SetValue(thickness);
                        newPanel.ContentProperty.CollectionElements.Add(item.DesignItem);
                    } else if (newPanel.Component is Microsoft.UI.Xaml.Controls.Viewbox || newPanel.Component is Microsoft.UI.Xaml.Controls.ContentControl) {
                        newPanel.ContentProperty.SetValue(item.DesignItem);
                    }
                }

                operation.Commit();
            } catch {
                operation.Abort();
                throw;
            }
        }
    }
}
