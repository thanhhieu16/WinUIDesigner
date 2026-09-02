using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Align Left/HCenter/Right/Top/VCenter/Bottom context menu for a multi-item
    /// selection. Ported from the reference's ArrangeItemsContextMenuExtension.cs.</summary>
    [ExtensionServer(typeof(PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.FrameworkElement))]
    public sealed class ArrangeItemsContextMenuExtension : DefaultExtension
    {
        DesignPanel panel;
        MenuFlyoutItemBase[] items;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            MenuFlyoutItem Make(string text, ArrangeDirection direction)
            {
                var item = new MenuFlyoutItem { Text = text };
                item.Click += (s, e) => ModelTools.ArrangeItems(ExtendedItem.Services.Selection.SelectedItems, direction);
                return item;
            }

            items = new MenuFlyoutItemBase[] {
                Make("Align Left", ArrangeDirection.Left),
                Make("Align Center", ArrangeDirection.HorizontalMiddle),
                Make("Align Right", ArrangeDirection.Right),
                Make("Align Top", ArrangeDirection.Top),
                Make("Align Middle", ArrangeDirection.VerticalMiddle),
                Make("Align Bottom", ArrangeDirection.Bottom),
            };

            panel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            panel?.AddContextFlyoutItems(items, 30);
        }

        protected override void OnRemove()
        {
            panel?.RemoveContextFlyoutItems(items);
            base.OnRemove();
        }
    }
}
