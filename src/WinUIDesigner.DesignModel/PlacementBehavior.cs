using System.Collections.Generic;
using Windows.Foundation;
using Windows.System;

namespace WinUIDesigner.DesignModel
{
    /// <summary>Behavior interface implemented by container elements to support resizing child
    /// elements. No WPF-specific API - mechanical port, except that PlacePoint and
    /// BeforeSetPosition carry an explicit VirtualKeyModifiers argument: the reference reads
    /// Keyboard.IsKeyDown(Key.LeftCtrl) ambiently inside RasterPlacementBehavior/
    /// SnaplinePlacementBehavior, and WinUI has no ambient keyboard-state query - modifiers are
    /// only available from the originating pointer event (same choice Phase 3's DesignPanel
    /// selection code and Phase 4a's ResizeThumbExtension aspect-lock already made).</summary>
    public interface IPlacementBehavior
    {
        bool CanPlace(IEnumerable<DesignItem> childItems, PlacementType type, PlacementAlignment position);
        void BeginPlacement(PlacementOperation operation);
        void EndPlacement(PlacementOperation operation);
        Rect GetPosition(PlacementOperation operation, DesignItem child);
        void BeforeSetPosition(PlacementOperation operation, VirtualKeyModifiers modifiers);
        void SetPosition(PlacementInformation info);
        bool CanLeaveContainer(PlacementOperation operation);
        void LeaveContainer(PlacementOperation operation);
        bool CanEnterContainer(PlacementOperation operation, bool shouldAlwaysEnter);
        void EnterContainer(PlacementOperation operation);
        Point PlacePoint(Point point, VirtualKeyModifiers modifiers);
    }

    /// <summary>Behavior interface for root elements (Parent == null).</summary>
    public interface IRootPlacementBehavior : IPlacementBehavior
    {
    }
}
