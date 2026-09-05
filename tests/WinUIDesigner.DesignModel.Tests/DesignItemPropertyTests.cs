using System.IO;
using System.Xml;
using NUnit.Framework;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class DesignItemPropertyTests
    {
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
            "<Button x:Name=\"Target\" Grid.Row=\"1\" Width=\"40\" Height=\"20\" />" +
            "</Grid>";

        static DesignItem LoadButton()
        {
            using var reader = XmlReader.Create(new StringReader(GridXaml));
            var context = new XamlDesignContext(reader, new XamlLoadSettings());
            var grid = (Grid)context.RootItem.Component;
            var button = (Button)grid.Children[0];
            return context.Services.Component.GetDesignItem(button);
        }

        [Test]
        public void AnAttachedPropertyReportsIsAttachedPropertyTrue()
        {
            WinUITestHost.Run(() => {
                var buttonItem = LoadButton();
                var rowProperty = buttonItem.Properties.GetAttachedProperty(typeof(Grid), "Row");
                Assert.IsTrue(rowProperty.IsAttachedProperty);
            });
        }

        [Test]
        public void AnOrdinaryPropertyReportsIsAttachedPropertyFalse()
        {
            WinUITestHost.Run(() => {
                var buttonItem = LoadButton();
                var widthProperty = buttonItem.Properties.GetProperty("Width");
                Assert.IsFalse(widthProperty.IsAttachedProperty);
            });
        }
    }
}
