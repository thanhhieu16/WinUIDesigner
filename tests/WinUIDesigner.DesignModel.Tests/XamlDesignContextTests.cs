using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class XamlDesignContextTests
    {
        const string SimpleXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"MyButton\" Content=\"Hello\" />" +
            "</Grid>";

        static XamlDesignContext Load(string xaml)
        {
            using (var reader = XmlReader.Create(new StringReader(xaml))) {
                return new XamlDesignContext(reader, new XamlLoadSettings());
            }
        }

        [Test]
        public void RootItemHasCorrectComponentType()
        {
            WinUITestHost.Run(() => {
                var context = Load(SimpleXaml);
                Assert.AreEqual(typeof(Microsoft.UI.Xaml.Controls.Grid), context.RootItem.ComponentType);
            });
        }

        [Test]
        public void ChildIsRegisteredAndFindableByName()
        {
            WinUITestHost.Run(() => {
                var context = Load(SimpleXaml);
                var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];

                var buttonItem = context.Services.Component.GetDesignItem(button);

                Assert.IsNotNull(buttonItem);
                Assert.AreEqual("MyButton", buttonItem.Name);
                Assert.AreEqual(context.RootItem, buttonItem.Parent);
            });
        }

        [Test]
        public void ContentPropertyReadsThroughToLiveInstance()
        {
            WinUITestHost.Run(() => {
                var context = Load(SimpleXaml);
                var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                var buttonItem = context.Services.Component.GetDesignItem(button);

                Assert.AreEqual("Hello", buttonItem.Properties["Content"].ValueOnInstance);
            });
        }
    }
}
