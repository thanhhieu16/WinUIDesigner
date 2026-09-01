using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer
{
    /// <summary>
    /// Static helper methods for working with the designer DOM. Ported from the reference's
    /// ModelTools.cs (890 lines) with only the four members Phase 4a actually calls -
    /// WrapItemsNewContainer/UnwrapItemsFromContainer/ArrangeItems/StretchItems/
    /// CanDeleteComponents/IsInDocument/ComparePositionInModelFile are all multi-selection/
    /// context-menu helpers with no 4a consumer (4c/4e territory).
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
                // XamlProperty.InsertNodeInCollection when a subsequent transform of a third kind
                // triggered a second CollectionElements.Add. Root cause (traced through
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
    }
}
