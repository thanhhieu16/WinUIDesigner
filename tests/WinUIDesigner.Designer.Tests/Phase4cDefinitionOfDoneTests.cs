using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class Phase4cDefinitionOfDoneTests
    {
        [Test]
        public void MultiParentDeleteCommitsAsExactlyOneUndoEntry()
        {
            const string xaml =
                "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Canvas x:Name=\"C1\"><Button x:Name=\"A\" Width=\"20\" Height=\"20\" /></Canvas>" +
                "<Canvas x:Name=\"C2\"><Button x:Name=\"B\" Width=\"20\" Height=\"20\" /></Canvas>" +
                "</StackPanel>";
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(xaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var root = (Microsoft.UI.Xaml.Controls.StackPanel)surface.DesignContext.RootItem.Component;
                var c1 = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
                var c2 = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[1];
                var aItem = surface.DesignContext.Services.Component.GetDesignItem(c1.Children[0]);
                var bItem = surface.DesignContext.Services.Component.GetDesignItem(c2.Children[0]);
                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                var undoCountBefore = undoService.UndoActions.Count();

                ModelTools.DeleteComponents(new[] { aItem, bItem });

                Assert.AreEqual(0, c1.Children.Count);
                Assert.AreEqual(0, c2.Children.Count);
                Assert.AreEqual(undoCountBefore + 1, undoService.UndoActions.Count(), "one delete spanning two parents must still be one undo entry");

                undoService.Undo();
                Assert.AreEqual(1, c1.Children.Count);
                Assert.AreEqual(1, c2.Children.Count);
            });
        }

        [Test]
        public void RemovingAnExtensionRemovesItsItemsFromTheMergedFlyout()
        {
            const string xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" />" +
                "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" />" +
                "</Canvas>";
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(xaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                var aItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[0]);
                var bItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[1]);

                // 2 selected - ArrangeItemsContextMenuExtension attaches, contributing its Order-30 group.
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem, bItem });
                var flyoutWithArrange = (MenuFlyout)surface.DesignPanel.ContextFlyout;
                Assert.IsTrue(flyoutWithArrange.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Align Left"));

                // Down to 1 selected - ArrangeItemsContextMenuExtension detaches.
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });
                var flyoutWithoutArrange = (MenuFlyout)surface.DesignPanel.ContextFlyout;
                Assert.IsFalse(flyoutWithoutArrange.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Align Left"));
            });
        }

        [Test]
        public void CanDeleteComponentsIsFalseForAnEmptySelection()
        {
            Assert.IsFalse(ModelTools.CanDeleteComponents(new DesignItem[0]));
        }
    }
}
