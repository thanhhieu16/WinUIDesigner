using NUnit.Framework;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.XamlDom.Tests
{
    [TestFixture]
    public class CollectionTests
    {
        [Test]
        public void LineBreakNoCollection()
        {
            var isCollection = CollectionSupport.IsCollectionType(typeof(Microsoft.UI.Xaml.Documents.LineBreak));

            Assert.IsFalse(isCollection);
        }
    }
}
