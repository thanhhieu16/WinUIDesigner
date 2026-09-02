using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Unwrap context menu for a single selected Grid or Canvas container. Ported
    /// from the reference's UnwrapItemContextMenuExtension.cs.</summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.Controls.Grid))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.Controls.Canvas))]
    public sealed class UnwrapItemContextMenuExtension : DefaultExtension
    {
        DesignPanel panel;
        MenuFlyoutItemBase[] items;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            var unwrap = new MenuFlyoutItem { Text = "Unwrap" };
            unwrap.Click += (s, e) => ModelTools.UnwrapItemsFromContainer(ExtendedItem.Services.Selection.PrimarySelection);
            items = new MenuFlyoutItemBase[] { unwrap };

            panel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            panel?.AddContextFlyoutItems(items, 51);
        }

        protected override void OnRemove()
        {
            panel?.RemoveContextFlyoutItems(items);
            base.OnRemove();
        }
    }
}
