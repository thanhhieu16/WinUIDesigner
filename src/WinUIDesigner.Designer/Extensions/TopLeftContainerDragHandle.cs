using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// The move-drag handle shown at the top-left corner of any selected FrameworkElement.
    /// Ported from the reference's TopLeftContainerDragHandle.cs, rebuilt on DragListener
    /// (already extended with PointerCaptureLost handling, Task 2) instead of the reference's
    /// ContainerDragHandle+DragMoveMouseGesture stack - same choice Phase 4a made for
    /// DragListener itself (a from-scratch WinUI replacement, not a port of anything
    /// WPF-mouse-specific).
    /// </summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(FrameworkElement))]
    public class TopLeftContainerDragHandle : AdornerProvider
    {
        internal readonly DragListener DragListener;
        MoveLogic moveLogic;

        public TopLeftContainerDragHandle()
        {
            var handle = new ContainerDragHandle();
            DragListener = new DragListener(handle);
            DragListener.Started += Drag_Started;
            DragListener.Changed += Drag_Changed;
            DragListener.Completed += Drag_Completed;

            var placement = new RelativePlacement(HorizontalAlignment.Left, VerticalAlignment.Top) {
                XOffset = -7,
                YOffset = -7
            };
            AddAdorner(placement, AdornerOrder.Background, handle);
        }

        void Drag_Started(DragListener drag)
        {
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
