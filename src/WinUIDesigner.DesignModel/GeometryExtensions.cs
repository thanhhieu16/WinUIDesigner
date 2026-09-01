using System;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WinUIDesigner.DesignModel
{
    /// <summary>
    /// Small WPF-reference extension methods with no other home in this port. Ported alone
    /// rather than pulling in their reference source files wholesale - see
    /// UIExtensions/UIHelpers.cs and ExtensionMethods.cs in the reference for the originals.
    /// </summary>
    public static class GeometryExtensions
    {
        /// <summary>
        /// Rounds position and size of a Rect to PlacementInformation.BoundsPrecision digits.
        /// </summary>
        public static Rect Round(this Rect rect)
        {
            return new Rect(
                Math.Round(rect.X, PlacementInformation.BoundsPrecision),
                Math.Round(rect.Y, PlacementInformation.BoundsPrecision),
                Math.Round(rect.Width, PlacementInformation.BoundsPrecision),
                Math.Round(rect.Height, PlacementInformation.BoundsPrecision)
            );
        }

        /// <summary>
        /// Walks up from <paramref name="child"/> looking for the first ancestor of type
        /// <typeparamref name="T"/>. WinUI has no ContentElement/logical-tree split the
        /// reference's version handles - FrameworkElement.Parent (when set) falls back to
        /// VisualTreeHelper.GetParent, matching this port's only two call sites.
        /// </summary>
        public static T TryFindParent<T>(this DependencyObject child) where T : DependencyObject
        {
            if (child == null) return null;
            var fe = child as FrameworkElement;
            DependencyObject parentObject = fe != null && fe.Parent != null ? fe.Parent : VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            var parent = parentObject as T;
            if (parent != null) return parent;
            return TryFindParent<T>(parentObject);
        }
    }
}
