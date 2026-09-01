using System.Collections.Generic;
using Windows.Foundation;

namespace WinUIDesigner.DesignModel
{
    /// <summary>Behavior interface implemented by container elements to support resizing
    /// child elements. No WPF-specific API - mechanical port.</summary>
    public interface IPlacementBehavior
    {
        bool CanPlace(IEnumerable<DesignItem> childItems, PlacementType type, PlacementAlignment position);
        void BeginPlacement(PlacementOperation operation);
        void EndPlacement(PlacementOperation operation);
        Rect GetPosition(PlacementOperation operation, DesignItem child);
        void BeforeSetPosition(PlacementOperation operation);
        void SetPosition(PlacementInformation info);
        bool CanLeaveContainer(PlacementOperation operation);
        void LeaveContainer(PlacementOperation operation);
        bool CanEnterContainer(PlacementOperation operation, bool shouldAlwaysEnter);
        void EnterContainer(PlacementOperation operation);
        Point PlacePoint(Point point);
    }

    /// <summary>Behavior interface for root elements (Parent == null).</summary>
    public interface IRootPlacementBehavior : IPlacementBehavior
    {
    }
}
