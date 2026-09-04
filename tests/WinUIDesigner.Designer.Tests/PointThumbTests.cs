using NUnit.Framework;
using WinUIDesigner.Designer.Controls.Thumbs;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PointThumbTests
    {
        [Test]
        public void DefaultThumbShowsTheSquareAnchorNotTheEllipse()
        {
            WinUITestHost.Run(() => {
                var thumb = new PointThumb(new Windows.Foundation.Point(5, 5));
                Assert.IsFalse(thumb.IsEllipse);
                Assert.IsNotNull(thumb.Background, "anchor square uses the Grid's own Background");
            });
        }

        [Test]
        public void SettingIsEllipseTrueHidesTheSquareBackground()
        {
            WinUITestHost.Run(() => {
                var thumb = new PointThumb(new Windows.Foundation.Point(5, 5));
                thumb.IsEllipse = true;
                Assert.IsNull(thumb.Background);
            });
        }

        [Test]
        public void RelativeToPointNullMeansNoConnectorLine()
        {
            WinUITestHost.Run(() => {
                var thumb = new PointThumb(new Windows.Foundation.Point(5, 5));
                Assert.IsNull(thumb.RelativeToPoint);
            });
        }

        [Test]
        public void PointPlacementSupportArrangesA7By7RectCenteredOnThePoint()
        {
            WinUITestHost.RunWithWindow(window => {
                var adornedElement = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 100, Height = 100 };
                var adornerPanel = new WinUIDesigner.DesignModel.Adorners.AdornerPanel {
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top
                };
                adornerPanel.SetAdornedElement(adornedElement, null);
                var thumb = new PointThumb(new Windows.Foundation.Point(20, 30));
                WinUIDesigner.DesignModel.Adorners.AdornerPanel.SetPlacement(thumb, thumb.AdornerPlacement);
                adornerPanel.Children.Add(thumb);
                window.Content = adornerPanel;
                window.Activate();
                adornerPanel.UpdateLayout();

                Assert.AreEqual(20 - 3.5, thumb.ActualOffset.X, 0.5);
                Assert.AreEqual(30 - 3.5, thumb.ActualOffset.Y, 0.5);
                Assert.AreEqual(7, thumb.ActualSize.X, 0.5);
                Assert.AreEqual(7, thumb.ActualSize.Y, 0.5);
            });
        }
    }
}
