using System;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class ExtensionServerAttributeTests
    {
        sealed class FakeExtensionServer : ExtensionServer
        {
            public override bool ShouldApplyExtensions(DesignItem extendedItem) => true;
            public override Extension CreateExtension(Type extensionType, DesignItem extendedItem) => null;
            public override void RemoveExtension(Extension extension) { }
            public override event EventHandler<DesignItemCollectionEventArgs> ShouldApplyExtensionsInvalidated { add { } remove { } }
        }

        [Test]
        public void AcceptsValidExtensionServerType()
        {
            var attr = new ExtensionServerAttribute(typeof(FakeExtensionServer));
            Assert.AreEqual(typeof(FakeExtensionServer), attr.ExtensionServerType);
        }

        [Test]
        public void RejectsTypeThatIsNotAnExtensionServer()
        {
            Assert.Throws<ArgumentException>(() => new ExtensionServerAttribute(typeof(string)));
        }

        [Test]
        public void RejectsNullType()
        {
            Assert.Throws<ArgumentNullException>(() => new ExtensionServerAttribute(null));
        }
    }
}
