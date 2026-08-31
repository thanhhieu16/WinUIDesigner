using NUnit.Framework;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.XamlDom.Tests
{
    [TestFixture]
    public class XamlTypeFinderTests
    {
        XamlTypeFinder typeFinder;

        [SetUp]
        public void FixtureSetUp()
        {
            typeFinder = XamlTypeFinder.CreateWinUITypeFinder();
        }

        [Test]
        public void FindGrid()
        {
            Assert.AreEqual(typeof(Microsoft.UI.Xaml.Controls.Grid),
                typeFinder.GetType(XamlConstants.PresentationNamespace, "Grid"));
        }

        [Test]
        public void FindButton()
        {
            Assert.AreEqual(typeof(Microsoft.UI.Xaml.Controls.Button),
                typeFinder.GetType(XamlConstants.PresentationNamespace, "Button"));
        }

        [Test]
        public void FindBinding()
        {
            Assert.AreEqual(typeof(Microsoft.UI.Xaml.Data.Binding),
                typeFinder.GetType(XamlConstants.PresentationNamespace, "Binding"));
        }

        public const string XamlDomTestsNamespace =
            "clr-namespace:WinUIDesigner.XamlDom.Tests;assembly=WinUIDesigner.XamlDom.Tests";
    }
}
