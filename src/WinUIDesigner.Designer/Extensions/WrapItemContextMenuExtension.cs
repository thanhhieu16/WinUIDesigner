using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Wrap-in-Viewbox context menu for a single selected item. Ported from the
    /// reference's WrapItemContextMenuExtension.cs (its WindowClone exclusion check is dropped
    /// - WindowClone doesn't exist in this port, out of scope everywhere).</summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.FrameworkElement))]
    public sealed class WrapItemContextMenuExtension : DefaultExtension
    {
        DesignPanel panel;
        MenuFlyoutItemBase[] items;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            var wrapInViewbox = new MenuFlyoutItem { Text = "Wrap in Viewbox" };
            wrapInViewbox.Click += (s, e) =>
                ModelTools.WrapItemsNewContainer(ExtendedItem.Services.Selection.SelectedItems, typeof(Microsoft.UI.Xaml.Controls.Viewbox));
            items = new MenuFlyoutItemBase[] { wrapInViewbox };

            panel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            panel?.AddContextFlyoutItems(items, 50);
        }

        protected override void OnRemove()
        {
            panel?.RemoveContextFlyoutItems(items);
            base.OnRemove();
        }
    }
}
