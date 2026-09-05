using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// Generic placement behavior for any Panel/Control/Border/Viewbox that doesn't have a
    /// more specific one (Grid/Canvas/StackPanel-specific behaviors are Phase 4b). Resizes via
    /// Width/Height, not Canvas.Left/Top. Ported from the reference's
    /// Extensions/DefaultPlacementBehavior.cs with two drops: InfoTextEnterArea ("hold Alt to
    /// enter container" hint UI) is dropped, and internalCanEnterContainer's "is Decorator"
    /// branch is dropped since WinUI has no Decorator type (confirmed absent from
    /// Microsoft.UI.Xaml.winmd during spec brainstorming) - its three sibling branches
    /// (Expander/UserControl/CollectionSupport) all port unchanged.
    ///
    /// Final-review note (I2): CanEnterContainer below ignores shouldAlwaysEnter entirely -
    /// unlike the reference, which gates cross-container entry on holding Alt unless
    /// shouldAlwaysEnter is true (the reason InfoTextEnterArea existed there). This was harmless
    /// in 4a, when CanEnterContainer had no caller at all. Phase 4b's MoveLogic (Task 14) gave it
    /// its first real caller (passing shouldAlwaysEnter: false), which was the moment to either
    /// restore the Alt-gate or deliberately drop it - reviewed and ruled: dropped on purpose, so
    /// every cross-container drag in this port auto-reparents with no modifier key required.
    /// DragListener/MoveLogic.Move already thread Windows.System.VirtualKeyModifiers through, so
    /// a future phase can reintroduce the gate by checking the modifier inside
    /// CanEnterContainer/internalCanEnterContainer if desired - no plumbing is missing for it.
    /// </summary>
    [ExtensionFor(typeof(Panel))]
    [ExtensionFor(typeof(Control))]
    [ExtensionFor(typeof(Border))]
    [ExtensionFor(typeof(Viewbox))]
    public class DefaultPlacementBehavior : BehaviorExtension, IPlacementBehavior
    {
        protected override void OnInitialized()
        {
            base.OnInitialized();
            if (ExtendedItem.ContentProperty == null || Metadata.IsPlacementDisabled(ExtendedItem.ComponentType))
                return;
            ExtendedItem.AddBehavior(typeof(IPlacementBehavior), this);
        }

        public virtual bool CanPlace(IEnumerable<DesignItem> childItems, PlacementType type, PlacementAlignment position) => true;

        public virtual void BeginPlacement(PlacementOperation operation) { }

        public virtual void EndPlacement(PlacementOperation operation) { }

        public virtual Rect GetPosition(PlacementOperation operation, DesignItem item) => GetPositionRelativeToContainer(operation, item);

        public Rect GetPositionRelativeToContainer(PlacementOperation operation, DesignItem item)
        {
            if (item.View == null) return default;
            var p = item.View.TransformToVisual(operation.CurrentContainer.View).TransformPoint(new Point());
            return new Rect(p, PlacementOperation.GetRealElementSize(item.View));
        }

        public virtual void BeforeSetPosition(PlacementOperation operation, Windows.System.VirtualKeyModifiers modifiers) { }

        public virtual void SetPosition(PlacementInformation info)
        {
            if (info.Operation.Type != PlacementType.Move && info.Operation.Type != PlacementType.MovePoint && info.Operation.Type != PlacementType.MoveAndIgnoreOtherContainers)
                ModelTools.Resize(info.Item, info.Bounds.Width, info.Bounds.Height);
        }

        public virtual bool CanLeaveContainer(PlacementOperation operation) => true;

        public virtual void LeaveContainer(PlacementOperation operation)
        {
            foreach (var info in operation.PlacedItems) {
                var parentProperty = info.Item.ParentProperty;
                if (parentProperty.IsCollection)
                    parentProperty.CollectionElements.Remove(info.Item);
                else
                    parentProperty.Reset();
            }
        }

        public virtual bool CanEnterContainer(PlacementOperation operation, bool shouldAlwaysEnter) => internalCanEnterContainer(operation);

        bool internalCanEnterContainer(PlacementOperation operation)
        {
            if (ExtendedItem.Component is Expander expander) {
                if (!expander.IsExpanded) expander.IsExpanded = true;
            }

            if (ExtendedItem.Component is UserControl && ExtendedItem.ComponentType != typeof(UserControl))
                return false;

            if (ExtendedItem.ContentProperty.IsCollection)
                return WinUIDesigner.XamlDom.CollectionSupport.CanCollectionAdd(ExtendedItem.ContentProperty.ReturnType, operation.PlacedItems.Select(p => p.Item.Component));

            if (ExtendedItem.ContentProperty.ReturnType == typeof(string))
                return false;

            if (!ExtendedItem.ContentProperty.IsSet)
                return true;

            object value = ExtendedItem.ContentProperty.GetConvertedValueOnInstance<object>();
            return ExtendedItem.ContentProperty.Value == null && (value is string s && string.IsNullOrEmpty(s));
        }

        public virtual void EnterContainer(PlacementOperation operation)
        {
            if (ExtendedItem.ContentProperty.IsCollection) {
                foreach (var info in operation.PlacedItems)
                    ExtendedItem.ContentProperty.CollectionElements.Add(info.Item);
            } else {
                ExtendedItem.ContentProperty.SetValue(operation.PlacedItems[0].Item);
            }
            if (operation.Type == PlacementType.AddItem) {
                foreach (var info in operation.PlacedItems)
                    SetPosition(info);
            }
        }

        public virtual Point PlacePoint(Point point, Windows.System.VirtualKeyModifiers modifiers) => new Point(System.Math.Round(point.X), System.Math.Round(point.Y));
    }
}
