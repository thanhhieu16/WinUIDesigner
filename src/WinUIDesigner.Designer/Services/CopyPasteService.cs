using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.Designer.Services
{
    /// <summary>Concrete ICopyPasteService. Lives in WinUIDesigner.Designer, not DesignModel
    /// (the interface's home) - it calls ModelTools, and DesignModel must not reference
    /// Designer. Registered in DesignSurface.LoadDesigner, same pattern as OptionService.
    /// Phase 4c implemented only Delete/CanDelete; Copy/Cut/Paste follow the reference's
    /// XamlEditOperations and are specified in docs/superpowers/specs/2026-10-26-clipboard-design.md.</summary>
    public class CopyPasteService : ICopyPasteService
    {
        readonly IDesignerClipboard clipboard;

        public CopyPasteService()
            : this(SystemDesignerClipboard.Instance)
        {
        }

        public CopyPasteService(IDesignerClipboard clipboard)
        {
            if (clipboard == null)
                throw new ArgumentNullException(nameof(clipboard));
            this.clipboard = clipboard;
        }

        /// <summary>A non-empty selection without the root, which has no container to copy out of.</summary>
        public bool CanCopy(DesignContext designContext)
        {
            if (designContext == null || designContext.RootItem == null)
                return false;
            var selected = designContext.Services.Selection.SelectedItems;
            return selected.Count > 0 && !selected.Contains(designContext.RootItem);
        }

        /// <summary>Puts each selected item's XAML on the clipboard, in selection order. Changes
        /// nothing in the document.</summary>
        public void Copy(DesignContext designContext)
        {
            if (!CanCopy(designContext))
                return;
            var xamls = designContext.Services.Selection.SelectedItems
                .OfType<XamlDesignItem>()
                .Select(item => XamlStaticTools.GetXaml(item.XamlObject));
            clipboard.SetXaml(ClipboardPayload.Join(xamls));
        }

        public bool CanCut(DesignContext designContext)
        {
            return CanCopy(designContext) && CanDelete(designContext);
        }

        /// <summary>Copy, then Delete. Delete commits its own single undo entry.</summary>
        public void Cut(DesignContext designContext)
        {
            if (!CanCut(designContext))
                return;
            Copy(designContext);
            Delete(designContext);
        }

        public bool CanPaste(DesignContext designContext)
        {
            return designContext != null && designContext.RootItem != null && clipboard.ContainsXaml;
        }

        /// <summary>
        /// Reads the clipboard and pastes what it holds. ICopyPasteService.Paste is synchronous
        /// and WinUI's clipboard read is not, so this starts the read and inserts once it
        /// completes; the await resumes on the UI thread. Spec Decision 3.
        /// </summary>
        public async void Paste(DesignContext designContext)
        {
            if (!CanPaste(designContext))
                return;
            string payload;
            try
            {
                payload = await clipboard.GetXamlAsync();
            }
            catch (Exception)
            {
                // The clipboard can be held open by another process; there is nothing to paste
                // then, and an exception escaping an async void would take the process down.
                return;
            }
            PasteXaml(designContext, payload);
        }

        /// <summary>
        /// Parses <paramref name="payload"/> and inserts the items into the first container that
        /// accepts them, walking up from the selected panel, the selected control's container,
        /// or the root when nothing is selected. One undo entry; the pasted items end up selected. Returns false, leaving no
        /// undo entry, when nothing parsed or no container accepted. Spec Decisions 4-7.
        /// </summary>
        internal bool PasteXaml(DesignContext designContext, string payload)
        {
            // The document may have been closed or reloaded while the clipboard read was in flight.
            var xamlContext = designContext as XamlDesignContext;
            var root = xamlContext == null ? null : xamlContext.RootItem as XamlDesignItem;
            if (root == null)
                return false;

            var items = new List<DesignItem>();
            foreach (var part in ClipboardPayload.Split(payload))
            {
                XamlObject parsed;
                try
                {
                    parsed = XamlParser.ParseSnippet(root.XamlObject, part, xamlContext.ParserSettings);
                }
                catch (Exception)
                {
                    continue;
                }
                if (parsed == null)
                    continue;
                var item = xamlContext._componentService.RegisterXamlComponentRecursive(parsed);
                if (item != null && item.View != null)
                    items.Add(item);
            }
            if (items.Count == 0)
                return false;

            // As the reference's AddInParent: each item at the container's origin with its own
            // size. TryStartInsertNewComponents calls only EnterContainer, never SetPosition, so
            // these rects position nothing themselves; a Canvas offsets a paste in its PasteItem
            // branch and every other container keeps what the XAML carries (Grid.Row, ...).
            var rects = items.Select(item => new Rect(0, 0, ModelTools.GetWidth(item.View), ModelTools.GetHeight(item.View))).ToList();

            // A selected control that is not a panel is pasted beside, never into: the reference
            // would make the paste the Content of an empty Button, so copying a freshly placed
            // Button and pasting it nested the copy inside the original.
            var selection = designContext.Services.Selection;
            var start = selection.PrimarySelection ?? root;
            if (start != root && (start.ContentProperty == null || !start.ContentProperty.IsCollection))
                start = start.Parent;
            for (var container = start; container != null; container = container.Parent)
            {
                var operation = PlacementOperation.TryStartInsertNewComponents(container, items, rects, PlacementType.PasteItem);
                if (operation == null)
                    continue;
                selection.SetSelectedComponents(items);
                operation.Commit();
                return true;
            }
            return false;
        }

        public bool CanDelete(DesignContext designContext)
        {
            return designContext != null && ModelTools.CanDeleteComponents(designContext.Services.Selection.SelectedItems);
        }

        public void Delete(DesignContext designContext)
        {
            if (designContext != null)
                ModelTools.DeleteComponents(designContext.Services.Selection.SelectedItems);
        }
    }
}
