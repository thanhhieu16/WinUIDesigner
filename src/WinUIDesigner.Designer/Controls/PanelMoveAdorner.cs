using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinUIDesigner.Designer.Controls
{
    /// <summary>Transparent full-fill overlay making a container's own body draggable to move
    /// its selected child, without needing to hit TopLeftContainerDragHandle's small corner
    /// handle. Background must be a real (if transparent) brush, not null, for WinUI hit-
    /// testing to reach this element at all. Ported from the reference's
    /// Controls/PanelMoveAdorner.cs with its ZoomControl-driven ScaleTransform/LayoutTransform
    /// binding dropped (LayoutTransform doesn't exist in WinUI - confirmed absent since Phase
    /// 3 - and no ZoomControl exists anywhere in this port) and its IsVisible-binding dropped
    /// (a visibility nicety with no consumer here) - both out of scope, not stubbed.</summary>
    public sealed class PanelMoveAdorner : Grid
    {
        public PanelMoveAdorner()
        {
            Background = new SolidColorBrush(Colors.Transparent);
        }
    }
}
