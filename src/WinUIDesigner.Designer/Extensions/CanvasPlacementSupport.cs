using System;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// IPlacementBehavior for Canvas - positions via Canvas.Left/Top, instead of
    /// DefaultPlacementBehavior's Width/Height-only resize. Ported from the reference's
    /// CanvasPlacementSupport.cs, with one deliberate algorithm change: the reference's
    /// Canvas.Right/Canvas.Bottom fallback branches (used when a child has Right/Bottom set
    /// instead of Left/Top) are dropped rather than ported. WPF's Canvas.ArrangeOverride
    /// reads Right/Bottom as a positioning fallback when Left/Top aren't set, which is why
    /// the reference mirrors that with RightProperty/BottomProperty. WinUI's
    /// Microsoft.UI.Xaml.Controls.Canvas has no such fallback and no such properties at all -
    /// confirmed by inspecting the shipped Microsoft.UI.Xaml.winmd's Canvas type directly
    /// (its metadata declares only LeftProperty/TopProperty/ZIndexProperty; RightProperty and
    /// BottomProperty don't exist, which is also why they don't compile as C# member
    /// references here). With no Right/Bottom to fall back to, GetPosition/SetPosition
    /// always operate on Left/Top directly, and the reference's IsPropertySet helper (which
    /// existed solely to choose between Left-vs-Right and Top-vs-Bottom) is removed as dead
    /// code. GetCanvasProperty operates on the live UIElement's real WinUI
    /// Canvas.LeftProperty/TopProperty DependencyProperty directly (not the string-keyed
    /// DesignItemProperty convention, which only applies to the undo-tracked SetValue side).
    /// EnterContainer's PasteItem branch, dropped in Phase 4b while nothing pasted, was restored
    /// with Cut/Copy/Paste (docs/superpowers/specs/2026-10-26-clipboard-design.md, Decision 5).
    /// </summary>
    [ExtensionFor(typeof(Canvas), OverrideExtension = typeof(DefaultPlacementBehavior))]
    public class CanvasPlacementSupport : SnaplinePlacementBehavior
    {
        GrayOutDesignerExceptActiveArea grayOut;
        FrameworkElement extendedView;

        static double GetCanvasProperty(UIElement element, DependencyProperty d)
        {
            double v = (double)element.GetValue(d);
            return double.IsNaN(v) ? 0 : v;
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            extendedView = (FrameworkElement)ExtendedItem.View;
        }

        public override Rect GetPosition(PlacementOperation operation, DesignItem item)
        {
            UIElement child = item.View;
            if (child == null) return default;

            double x = GetCanvasProperty(child, Canvas.LeftProperty);
            double y = GetCanvasProperty(child, Canvas.TopProperty);

            return new Rect(new Point(x, y), PlacementOperation.GetRealElementSize(item.View));
        }

        public override void SetPosition(PlacementInformation info)
        {
            base.SetPosition(info);
            info.Item.Properties["Margin"].Reset();

            UIElement child = info.Item.View;
            Rect newPosition = info.Bounds;

            if (newPosition.X != GetCanvasProperty(child, Canvas.LeftProperty))
                info.Item.Properties.GetAttachedProperty(typeof(Canvas), "Left").SetValue(newPosition.X);

            if (newPosition.Y != GetCanvasProperty(child, Canvas.TopProperty))
                info.Item.Properties.GetAttachedProperty(typeof(Canvas), "Top").SetValue(newPosition.Y);

            if (info.Item == Services.Selection.PrimarySelection) {
                var b = new Rect(0, 0, extendedView.ActualWidth, extendedView.ActualHeight);
                if (grayOut != null)
                    grayOut.AnimateActiveAreaRectTo(b);
                else
                    GrayOutDesignerExceptActiveArea.Start(ref grayOut, Services, ExtendedItem.View, b);
            }
        }

        public override void LeaveContainer(PlacementOperation operation)
        {
            GrayOutDesignerExceptActiveArea.Stop(ref grayOut);
            base.LeaveContainer(operation);
            foreach (var info in operation.PlacedItems) {
                info.Item.Properties.GetAttachedProperty(typeof(Canvas), "Left").Reset();
                info.Item.Properties.GetAttachedProperty(typeof(Canvas), "Top").Reset();
            }
        }

        public override void EnterContainer(PlacementOperation operation)
        {
            base.EnterContainer(operation);
            foreach (var info in operation.PlacedItems) {
                info.Item.Properties["HorizontalAlignment"].Reset();
                info.Item.Properties["VerticalAlignment"].Reset();
                info.Item.Properties["Margin"].Reset();

                // A paste lands PasteOffset to the right of and below where the copy stood, so a
                // copy pasted into the same Canvas does not exactly cover its original.
                if (operation.Type == PlacementType.PasteItem) {
                    OffsetIfSet(info.Item.Properties.GetAttachedProperty(typeof(Canvas), "Left"));
                    OffsetIfSet(info.Item.Properties.GetAttachedProperty(typeof(Canvas), "Top"));
                }
            }
        }

        static void OffsetIfSet(DesignItemProperty property)
        {
            if (!property.IsSet)
                return;
            var value = property.ValueOnInstance;
            if (value is double d && !double.IsNaN(d))
                property.SetValue(d + PlacementOperation.PasteOffset);
        }

        public override void EndPlacement(PlacementOperation operation)
        {
            GrayOutDesignerExceptActiveArea.Stop(ref grayOut);
            base.EndPlacement(operation);
        }
    }
}
