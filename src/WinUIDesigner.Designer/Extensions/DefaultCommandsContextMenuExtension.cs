using System.Linq;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.DesignModel.Services;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Delete/Undo/Redo context menu for the primary selection. Ported from the
    /// reference's DefaultCommandsContextMenuExtension.cs, narrowed to Delete/Undo/Redo only
    /// (Cut/Copy/Paste dropped - no real clipboard support this phase, see the spec's
    /// "Decisions already made"). Unlike the reference (which routes Delete/Undo/Redo through
    /// DesignSurface's WPF RoutedCommand hub via ApplicationCommands bindings, not ported this
    /// phase), each item's Click handler resolves ICopyPasteService/UndoService directly off
    /// ExtendedItem.Context.</summary>
    [ExtensionServer(typeof(PrimarySelectionExtensionServer))]
    [ExtensionFor(typeof(object))]
    public sealed class DefaultCommandsContextMenuExtension : DefaultExtension
    {
        DesignPanel panel;
        MenuFlyoutItemBase[] items;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            var delete = new MenuFlyoutItem { Text = "Delete" };
            delete.Click += (s, e) => Delete();
            var undo = new MenuFlyoutItem { Text = "Undo" };
            undo.Click += (s, e) => Undo();
            var redo = new MenuFlyoutItem { Text = "Redo" };
            redo.Click += (s, e) => Redo();
            items = new MenuFlyoutItemBase[] { delete, undo, redo };

            panel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            panel?.AddContextFlyoutItems(items, 10);
        }

        protected override void OnRemove()
        {
            panel?.RemoveContextFlyoutItems(items);
            base.OnRemove();
        }

        internal void Delete()
        {
            var copyPasteService = ExtendedItem.Services.GetService<ICopyPasteService>();
            if (copyPasteService != null && copyPasteService.CanDelete(ExtendedItem.Context))
                copyPasteService.Delete(ExtendedItem.Context);
        }

        internal void Undo()
        {
            var undoService = ExtendedItem.Services.GetService<UndoService>();
            if (undoService == null || !undoService.CanUndo) return;
            var action = undoService.UndoActions.First();
            undoService.Undo();
            ExtendedItem.Services.Selection.SetSelectedComponents(action.AffectedElements);
        }

        internal void Redo()
        {
            var undoService = ExtendedItem.Services.GetService<UndoService>();
            if (undoService == null || !undoService.CanRedo) return;
            var action = undoService.RedoActions.First();
            undoService.Redo();
            ExtendedItem.Services.Selection.SetSelectedComponents(action.AffectedElements);
        }
    }
}
