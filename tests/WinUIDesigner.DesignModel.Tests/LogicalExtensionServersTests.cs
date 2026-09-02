using NUnit.Framework;
using System;
using System.IO;
using System.Xml;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class LogicalExtensionServersTests
    {
        // A minimal ExtensionServer pair whose ShouldApplyExtensions is driven directly by
        // the test, so LogicalOrExtensionServer's OR logic can be verified without any real
        // selection/parent machinery.
        class AlwaysTrueServer : DefaultExtensionServer
        {
            public override bool ShouldApplyExtensions(DesignItem extendedItem) => true;
        }

        class AlwaysFalseServer : DefaultExtensionServer
        {
            public override bool ShouldApplyExtensions(DesignItem extendedItem) => false;
        }

        static XamlDesignContext LoadXaml(string xaml)
        {
            using (var reader = XmlReader.Create(new StringReader(xaml))) {
                return new XamlDesignContext(reader, new XamlLoadSettings());
            }
        }

        [Test]
        public void ReturnsTrueWhenEitherUnderlyingServerReturnsTrue()
        {
            WinUITestHost.Run(() => {
                var context = LoadXaml(
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />");
                var server = context.Services.ExtensionManager.GetExtensionServer(
                    new ExtensionServerAttribute(typeof(LogicalOrExtensionServer<AlwaysTrueServer, AlwaysFalseServer>)));

                Assert.IsTrue(server.ShouldApplyExtensions(context.RootItem));
            });
        }

        [Test]
        public void ReturnsFalseWhenBothUnderlyingServersReturnFalse()
        {
            WinUITestHost.Run(() => {
                var context = LoadXaml(
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />");
                var server = context.Services.ExtensionManager.GetExtensionServer(
                    new ExtensionServerAttribute(typeof(LogicalOrExtensionServer<AlwaysFalseServer, AlwaysFalseServer>)));

                Assert.IsFalse(server.ShouldApplyExtensions(context.RootItem));
            });
        }
    }
}
