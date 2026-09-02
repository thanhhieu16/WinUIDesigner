using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Z-order context menu (Bring to Front / Send Forward / Send Backward / Send
    /// to Back) for the primary, single selection. Ported from the reference
    /// (RightClickContextMenuExtension.cs) - subclasses DefaultExtension directly, not
    /// AdornerProvider (no adorner visuals here - see the spec's "Also verified"). Internal
    /// command methods (BringToFront etc.) exist so tests can invoke them directly - there is
    /// no way to raise a sealed MenuFlyoutItem.Click from test code.</summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.FrameworkElement))]
    public sealed class RightClickContextMenuExtension : DefaultExtension
    {
        DesignPanel panel;
        MenuFlyoutItemBase[] items;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            var bringToFront = new MenuFlyoutItem { Text = "Bring to Front" };
            bringToFront.Click += (s, e) => BringToFront();
            var sendForward = new MenuFlyoutItem { Text = "Send Forward" };
            sendForward.Click += (s, e) => SendForward();
            var sendBackward = new MenuFlyoutItem { Text = "Send Backward" };
            sendBackward.Click += (s, e) => SendBackward();
            var sendToBack = new MenuFlyoutItem { Text = "Send to Back" };
            sendToBack.Click += (s, e) => SendToBack();
            items = new MenuFlyoutItemBase[] { bringToFront, sendForward, sendBackward, sendToBack };

            panel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            panel?.AddContextFlyoutItems(items, 20);
        }

        protected override void OnRemove()
        {
            panel?.RemoveContextFlyoutItems(items);
            base.OnRemove();
        }

        internal void BringToFront()
        {
            if (ExtendedItem.ParentProperty == null || !ExtendedItem.ParentProperty.IsCollection) return;
            var changeGroup = ExtendedItem.OpenGroup("Bring to Front");
            try {
                var collection = ExtendedItem.ParentProperty.CollectionElements;
                collection.Remove(ExtendedItem);
                collection.Add(ExtendedItem);
                changeGroup.Commit();
            } catch {
                changeGroup.Abort();
                throw;
            }
        }

        internal void SendToBack()
        {
            if (ExtendedItem.ParentProperty == null || !ExtendedItem.ParentProperty.IsCollection) return;
            var changeGroup = ExtendedItem.OpenGroup("Send to Back");
            try {
                var collection = ExtendedItem.ParentProperty.CollectionElements;
                collection.Remove(ExtendedItem);
                collection.Insert(0, ExtendedItem);
                changeGroup.Commit();
            } catch {
                changeGroup.Abort();
                throw;
            }
        }

        internal void SendBackward()
        {
            if (ExtendedItem.ParentProperty == null || !ExtendedItem.ParentProperty.IsCollection) return;
            var changeGroup = ExtendedItem.OpenGroup("Send Backward");
            try {
                var collection = ExtendedItem.ParentProperty.CollectionElements;
                var idx = collection.IndexOf(ExtendedItem);
                collection.RemoveAt(idx);
                collection.Insert(idx <= 0 ? 0 : idx - 1, ExtendedItem);
                changeGroup.Commit();
            } catch {
                changeGroup.Abort();
                throw;
            }
        }

        internal void SendForward()
        {
            if (ExtendedItem.ParentProperty == null || !ExtendedItem.ParentProperty.IsCollection) return;
            var changeGroup = ExtendedItem.OpenGroup("Send Forward");
            try {
                var collection = ExtendedItem.ParentProperty.CollectionElements;
                var idx = collection.IndexOf(ExtendedItem);
                collection.RemoveAt(idx);
                var count = collection.Count;
                collection.Insert(idx + 1 > count ? count : idx + 1, ExtendedItem);
                changeGroup.Commit();
            } catch {
                changeGroup.Abort();
                throw;
            }
        }
    }
}
