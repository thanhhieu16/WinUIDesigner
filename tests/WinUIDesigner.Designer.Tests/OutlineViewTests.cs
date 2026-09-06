using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class OutlineViewTests
    {
        // The reference's own outline fixture, verbatim: HierarchyTests, SelectionTests and
        // InsertTests all use "<Button/><StackPanel><Button/></StackPanel>" inside a Grid.
        // It deliberately sets NO non-content property on the Grid: from Task 5 onwards every set
        // non-content property becomes a leading PropertyOutlineNode, which would shift every
        // index-based assertion in Tasks 4-10. Any test needing a non-visual collection element
        // declares its own XAML instead of adding one here.
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button />" +
            "<StackPanel><Button /></StackPanel>" +
            "</Grid>";

        static DesignSurface LoadSurface(Window window, string xaml)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            return surface;
        }

        [Test]
        public void CanSelectComponentIsTrueForAVisualItemAndFalseForANonVisualOne()
        {
            WinUITestHost.RunWithWindow(window => {
                // Own fixture: RowDefinitions gives a non-visual collection element, and keeping
                // it out of GridXaml keeps every other test's child indices stable once Task 5
                // starts emitting PropertyOutlineNodes for set non-content properties.
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
                    "<Button />" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var button = grid.ContentProperty.CollectionElements[0];

                Assert.IsNotNull(button.View);
                Assert.IsTrue(ModelTools.CanSelectComponent(button));

                var rowDefinition = grid.Properties["RowDefinitions"].CollectionElements[0];
                Assert.IsNull(rowDefinition.View,
                    "a RowDefinition is not a UIElement, so its DesignItem must have no View");
                Assert.IsFalse(ModelTools.CanSelectComponent(rowDefinition));
            });
        }
    }
}
