using NUnit.Framework;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class RelativePlacementTests
    {
        // RelativePlacement.Arrange() only produces observable ActualOffset/ActualSize when
        // driven through the real WinUI layout pipeline on a connected, composited element -
        // empirically verified (Task 2) that calling it directly on a disconnected UIElement
        // (as the plan originally specified) leaves ActualOffset stuck at (0,0) and ActualSize
        // pinned to the adorner's own explicit Width/Height rather than the arranged rect. Two
        // additional wrinkles, also verified empirically: (1) even when connected, an isolated
        // out-of-band Arrange() call on a child of a live Panel gets silently overwritten by
        // the next system layout pass unless the placement is registered via
        // AdornerPanel.SetPlacement so the panel's own ArrangeOverride is what invokes it; (2)
        // the adorner needs a real DesiredSize before that arrange runs, which only happens
        // once AdornerPanel.SetAdornedElement has been called (AdornerPanel.MeasureOverride
        // only measures its children when AdornedElement is non-null). This helper reproduces
        // AdornerPanel's real production usage so the placement math is exercised faithfully.
        static void ArrangeThroughAdornerPanel(Microsoft.UI.Xaml.Window window, Microsoft.UI.Xaml.UIElement adorner, RelativePlacement placement, double adornedWidth, double adornedHeight)
        {
            var adornedElement = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = adornedWidth, Height = adornedHeight };
            var panel = new AdornerPanel {
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top
            };
            panel.SetAdornedElement(adornedElement, null);
            AdornerPanel.SetPlacement(adorner, placement);
            panel.Children.Add(adorner);
            window.Content = panel;
            ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
        }

        [Test]
        public void TopLeftAlignmentArrangesAtTheOriginWithDesiredSize()
        {
            WinUITestHost.RunWithWindow(window => {
                var adorner = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 7, Height = 7 };
                var placement = new RelativePlacement(Microsoft.UI.Xaml.HorizontalAlignment.Left, Microsoft.UI.Xaml.VerticalAlignment.Top);
                ArrangeThroughAdornerPanel(window, adorner, placement, 100, 50);

                Assert.AreEqual(-7, adorner.ActualOffset.X, 0.01);
                Assert.AreEqual(-7, adorner.ActualOffset.Y, 0.01);
                Assert.AreEqual(7, adorner.ActualSize.X, 0.01);
                Assert.AreEqual(7, adorner.ActualSize.Y, 0.01);
            });
        }

        [Test]
        public void CenterStretchArrangesAcrossTheFullContentWidth()
        {
            WinUITestHost.RunWithWindow(window => {
                var adorner = new Microsoft.UI.Xaml.Shapes.Rectangle();
                var placement = new RelativePlacement(Microsoft.UI.Xaml.HorizontalAlignment.Stretch, Microsoft.UI.Xaml.VerticalAlignment.Top) { HeightOffset = 6 };
                ArrangeThroughAdornerPanel(window, adorner, placement, 100, 50);

                Assert.AreEqual(100, adorner.ActualSize.X, 0.01);
                Assert.AreEqual(6, adorner.ActualSize.Y, 0.01);
            });
        }
    }
}
