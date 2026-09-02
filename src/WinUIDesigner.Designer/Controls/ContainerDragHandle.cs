using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinUIDesigner.Designer.Controls
{
    /// <summary>Small square move-handle visual for TopLeftContainerDragHandle, positioned at
    /// a selected element's top-left corner. Grid-based (not Control), same idiom as
    /// DesignerThumb - code-only appearance, no generic.xaml.</summary>
    public sealed class ContainerDragHandle : Grid
    {
        public ContainerDragHandle()
        {
            Width = 7;
            Height = 7;
            Background = new SolidColorBrush(Colors.White);
            BorderBrush = new SolidColorBrush(Colors.Black);
            BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
        }
    }
}
