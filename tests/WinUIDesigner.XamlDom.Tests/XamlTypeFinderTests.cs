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

        /// <summary>
        /// The XML namespace the test fixture types live in.
        /// </summary>
        /// <remarks>
        /// WinUI's XAML dialect spells a CLR-namespace mapping <c>using:Ns</c>, not WPF's
        /// <c>clr-namespace:Ns;assembly=Asm</c>. Verified against WinUI's own
        /// <c>XamlReader.Load</c>: the <c>using:</c> form resolves the fixture types, the
        /// <c>clr-namespace:</c> form fails with "... is an unknown namespace". The tests use the
        /// WinUI-native form so that both loaders in <see cref="TestHelper.TestLoading"/> see the
        /// same document.
        /// </remarks>
        public const string XamlDomTestsNamespace = "using:WinUIDesigner.XamlDom.Tests";

        [Test]
        public void FindExampleClassViaUsingNamespace()
        {
            Assert.AreEqual(typeof(ExampleClass),
                typeFinder.GetType(XamlDomTestsNamespace, "ExampleClass"));
        }

        [Test]
        public void ClrNamespaceSchemeStillResolves()
        {
            Assert.AreEqual(typeof(ExampleClass),
                typeFinder.GetType("clr-namespace:WinUIDesigner.XamlDom.Tests;assembly=WinUIDesigner.XamlDom.Tests",
                    "ExampleClass"));
        }
    }
}
