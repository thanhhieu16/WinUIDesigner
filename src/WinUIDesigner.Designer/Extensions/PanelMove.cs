using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// Makes a container's own body draggable to move its selected child - a bigger, alternate
    /// trigger surface for the exact same MoveLogic-driven move TopLeftContainerDragHandle
    /// (Task 14) provides via its corner handle. Ported from the reference's PanelMove.cs. The
    /// reference bases on the abstract PermanentAdornerProvider - not ported, same precedent
    /// as Phase 4a's SelectionAdornerProvider abstraction (skipping an empty intermediate
    /// class that exists only to carry one attribute changes nothing at runtime): this class
    /// subclasses AdornerProvider directly and carries the ExtensionServer attribute itself.
    /// </summary>
    [ExtensionServer(typeof(DefaultExtensionServer.PermanentWithDesignPanel))]
    [ExtensionFor(typeof(Panel))]
    [ExtensionFor(typeof(Border))]
    [ExtensionFor(typeof(ContentControl))]
    [ExtensionFor(typeof(Viewbox))]
    public class PanelMove : AdornerProvider
    {
        internal readonly DragListener DragListener;
        MoveLogic moveLogic;

        public PanelMove()
        {
            var overlay = new PanelMoveAdorner();
            DragListener = new DragListener(overlay);
            DragListener.Started += Drag_Started;
            DragListener.Changed += Drag_Changed;
            DragListener.Completed += Drag_Completed;

            AddAdorner(AdornerPlacement.FillContent, AdornerOrder.Background, overlay);
        }

        void Drag_Started(DragListener drag)
        {
            // The reference's MoveLogic constructor filters its working set down to "clickedOn,
            // if clickedOn is currently selected" (multi-selection machinery this port doesn't
            // reconstruct - see MoveLogic's doc comment, Task 14) - otherwise the gesture is a
            // no-op. This is the one piece of that filtering that still matters for a
            // single-item MoveLogic: PanelMove moves the container itself, only when the
            // container is the thing currently selected (not whatever child happens to be
            // selected inside it).
            if (!ReferenceEquals(Services.Selection.PrimarySelection, ExtendedItem)) return;
            moveLogic = new MoveLogic(ExtendedItem);
            moveLogic.Start(drag.StartPoint);
        }

        void Drag_Changed(DragListener drag)
        {
            moveLogic?.Move(drag.CurrentPoint, drag.KeyModifiers);
        }

        void Drag_Completed(DragListener drag)
        {
            if (moveLogic == null) return;
            if (drag.IsCanceled) moveLogic.Cancel();
            else moveLogic.Stop();
            moveLogic = null;
        }
    }
}
