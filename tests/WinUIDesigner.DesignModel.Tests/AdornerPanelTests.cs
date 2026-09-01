using NUnit.Framework;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class AdornerPanelTests
    {
        [Test]
        public void PlacementDefaultsToFillContent()
        {
            WinUITestHost.Run(() => {
                var element = new Microsoft.UI.Xaml.Shapes.Rectangle();
                Assert.AreSame(AdornerPlacement.FillContent, AdornerPanel.GetPlacement(element));
            });
        }

        [Test]
        public void SetPlacementRoundTrips()
        {
            WinUITestHost.Run(() => {
                var element = new Microsoft.UI.Xaml.Shapes.Rectangle();
                AdornerPanel.SetPlacement(element, AdornerPlacement.FillContent);
                Assert.AreSame(AdornerPlacement.FillContent, AdornerPanel.GetPlacement(element));
            });
        }

        [Test]
        public void AdornerOrderComparesByDeclaredSeverity()
        {
            // AdornerOrder only implements the generic IComparable<AdornerOrder>, not the
            // non-generic IComparable that Assert.Less(IComparable, IComparable) requires -
            // Is.LessThan supports IComparable<T> via NUnit's ComparisonAdapter, Assert.Less does not.
            Assert.That(AdornerOrder.Background, Is.LessThan(AdornerOrder.Content));
            Assert.That(AdornerOrder.Content, Is.LessThan(AdornerOrder.BehindForeground));
            Assert.That(AdornerOrder.BehindForeground, Is.LessThan(AdornerOrder.Foreground));
            Assert.That(AdornerOrder.Foreground, Is.LessThan(AdornerOrder.BeforeForeground));
        }

        [Test]
        public void SetAdornedElementCannotBeCalledTwice()
        {
            WinUITestHost.Run(() => {
                var panel = new AdornerPanel();
                panel.SetAdornedElement(new Microsoft.UI.Xaml.Shapes.Rectangle(), null);
                Assert.Throws<System.InvalidOperationException>(() =>
                    panel.SetAdornedElement(new Microsoft.UI.Xaml.Shapes.Rectangle(), null));
            });
        }
    }
}
