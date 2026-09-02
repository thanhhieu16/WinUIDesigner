using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Wrap-in-Canvas / Wrap-in-Grid context menu for a multi-item selection. Ported
    /// from the reference's WrapItemsContextMenuExtension.cs.</summary>
    [ExtensionServer(typeof(PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.FrameworkElement))]
    public sealed class WrapItemsContextMenuExtension : DefaultExtension
    {
        DesignPanel panel;
        MenuFlyoutItemBase[] items;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            var wrapInCanvas = new MenuFlyoutItem { Text = "Wrap in Canvas" };
            wrapInCanvas.Click += (s, e) =>
                ModelTools.WrapItemsNewContainer(ExtendedItem.Services.Selection.SelectedItems, typeof(Microsoft.UI.Xaml.Controls.Canvas));
            var wrapInGrid = new MenuFlyoutItem { Text = "Wrap in Grid" };
            wrapInGrid.Click += (s, e) =>
                ModelTools.WrapItemsNewContainer(ExtendedItem.Services.Selection.SelectedItems, typeof(Microsoft.UI.Xaml.Controls.Grid));
            items = new MenuFlyoutItemBase[] { wrapInCanvas, wrapInGrid };

            panel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            panel?.AddContextFlyoutItems(items, 52);
        }

        protected override void OnRemove()
        {
            panel?.RemoveContextFlyoutItems(items);
            base.OnRemove();
        }
    }
}
