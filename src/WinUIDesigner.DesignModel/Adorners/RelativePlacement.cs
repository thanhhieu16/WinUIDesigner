using System;
using Windows.Foundation;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.DesignModel.Adorners
{
    /// <summary>Placement class providing properties for different kinds of relative
    /// placements. No WPF-specific API - mechanical port; operates only on doubles plus
    /// HorizontalAlignment/VerticalAlignment/AdornerPlacement/UIElement.DesiredSize/Arrange
    /// (all already ported, Phase 3).</summary>
    public sealed class RelativePlacement : AdornerPlacement
    {
        public RelativePlacement() { }

        public RelativePlacement(HorizontalAlignment horizontal, VerticalAlignment vertical)
        {
            switch (horizontal) {
                case HorizontalAlignment.Left:
                    widthRelativeToDesiredWidth = 1;
                    xRelativeToAdornerWidth = -1;
                    break;
                case HorizontalAlignment.Right:
                    widthRelativeToDesiredWidth = 1;
                    xRelativeToContentWidth = 1;
                    break;
                case HorizontalAlignment.Center:
                    widthRelativeToDesiredWidth = 1;
                    xRelativeToContentWidth = 0.5;
                    xRelativeToAdornerWidth = -0.5;
                    break;
                case HorizontalAlignment.Stretch:
                    widthRelativeToContentWidth = 1;
                    break;
            }
            switch (vertical) {
                case VerticalAlignment.Top:
                    heightRelativeToDesiredHeight = 1;
                    yRelativeToAdornerHeight = -1;
                    break;
                case VerticalAlignment.Bottom:
                    heightRelativeToDesiredHeight = 1;
                    yRelativeToContentHeight = 1;
                    break;
                case VerticalAlignment.Center:
                    heightRelativeToDesiredHeight = 1;
                    yRelativeToContentHeight = 0.5;
                    yRelativeToAdornerHeight = -0.5;
                    break;
                case VerticalAlignment.Stretch:
                    heightRelativeToContentHeight = 1;
                    break;
            }
        }

        double widthRelativeToDesiredWidth, heightRelativeToDesiredHeight;
        public double WidthRelativeToDesiredWidth { get => widthRelativeToDesiredWidth; set => widthRelativeToDesiredWidth = value; }
        public double HeightRelativeToDesiredHeight { get => heightRelativeToDesiredHeight; set => heightRelativeToDesiredHeight = value; }

        double widthRelativeToContentWidth, heightRelativeToContentHeight;
        public double WidthRelativeToContentWidth { get => widthRelativeToContentWidth; set => widthRelativeToContentWidth = value; }
        public double HeightRelativeToContentHeight { get => heightRelativeToContentHeight; set => heightRelativeToContentHeight = value; }

        double widthOffset, heightOffset;
        public double WidthOffset { get => widthOffset; set => widthOffset = value; }
        public double HeightOffset { get => heightOffset; set => heightOffset = value; }

        Size CalculateSize(UIElement adorner, Size adornedElementSize)
        {
            return new Size(
                Math.Max(widthOffset + widthRelativeToDesiredWidth * adorner.DesiredSize.Width + widthRelativeToContentWidth * adornedElementSize.Width, 0),
                Math.Max(heightOffset + heightRelativeToDesiredHeight * adorner.DesiredSize.Height + heightRelativeToContentHeight * adornedElementSize.Height, 0));
        }

        double xOffset, yOffset;
        public double XOffset { get => xOffset; set => xOffset = value; }
        public double YOffset { get => yOffset; set => yOffset = value; }

        double xRelativeToAdornerWidth, yRelativeToAdornerHeight;
        public double XRelativeToAdornerWidth { get => xRelativeToAdornerWidth; set => xRelativeToAdornerWidth = value; }
        public double YRelativeToAdornerHeight { get => yRelativeToAdornerHeight; set => yRelativeToAdornerHeight = value; }

        double xRelativeToContentWidth, yRelativeToContentHeight;
        public double XRelativeToContentWidth { get => xRelativeToContentWidth; set => xRelativeToContentWidth = value; }
        public double YRelativeToContentHeight { get => yRelativeToContentHeight; set => yRelativeToContentHeight = value; }

        Point CalculatePosition(Size adornedElementSize, Size adornerSize)
        {
            return new Point(
                xOffset + xRelativeToAdornerWidth * adornerSize.Width + xRelativeToContentWidth * adornedElementSize.Width,
                yOffset + yRelativeToAdornerHeight * adornerSize.Height + yRelativeToContentHeight * adornedElementSize.Height);
        }

        public override void Arrange(AdornerPanel panel, UIElement adorner, Size adornedElementSize)
        {
            Size adornerSize = CalculateSize(adorner, adornedElementSize);
            adorner.Arrange(new Rect(CalculatePosition(adornedElementSize, adornerSize), adornerSize));
        }
    }
}
