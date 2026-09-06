using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class ModelToolsArrangeItemsTests
    {
        static (XamlDesignContext context, DesignItem aItem, DesignItem bItem) LoadCanvas(Microsoft.UI.Xaml.Window window)
        {
            const string xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" Canvas.Left=\"0\" Canvas.Top=\"0\" />" +
                "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" Canvas.Left=\"50\" Canvas.Top=\"30\" />" +
                "</Canvas>";
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new WinUIDesigner.Designer.Services.DefaultSelectionService()));
            // ArrangeItems -> PlacementOperation.Start -> SnaplinePlacementBehavior.BeginPlacement
            // reads Services.DesignPanel.Context.RootItem to enumerate sibling items for snaplines
            // (SnaplinePlacementBehavior.cs:256) - same NRE precedent documented in
            // ModelToolsContextMenuTests.Load, so designPanel.Context must be wired here too.
            var designPanel = new WinUIDesigner.Designer.DesignPanel();
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), designPanel));
            using var reader = XmlReader.Create(new StringReader(xaml));
            var context = new XamlDesignContext(reader, settings);
            designPanel.Context = context;
            window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
            ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component;
            var aItem = context.Services.Component.GetDesignItem(canvas.Children[0]);
            var bItem = context.Services.Component.GetDesignItem(canvas.Children[1]);
            return (context, aItem, bItem);
        }

        [Test]
        public void ArrangeLeftAlignsBothItemsToTheLeftmostItemsCanvasLeft()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, aItem, bItem) = LoadCanvas(window);
                WinUIDesigner.Designer.ModelTools.ArrangeItems(new[] { aItem, bItem }, ArrangeDirection.Left);

                Assert.AreEqual(0.0, aItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>());
                Assert.AreEqual(0.0, bItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>());
            });
        }

        [Test]
        public void ArrangeTopAlignsBothItemsToTheTopmostItemsCanvasTop()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, aItem, bItem) = LoadCanvas(window);
                WinUIDesigner.Designer.ModelTools.ArrangeItems(new[] { aItem, bItem }, ArrangeDirection.Top);

                Assert.AreEqual(0.0, aItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>());
                Assert.AreEqual(0.0, bItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>());
            });
        }

        [Test]
        public void ArrangeSingleItemIsANoOpDeltaAgainstItself()
        {
            // A single-item "selection" arranges against its own bounds - min == max == its
            // own position, so this exercises the min/max math without needing a second item.
            WinUITestHost.RunWithWindow(window => {
                var (context, aItem, _) = LoadCanvas(window);
                WinUIDesigner.Designer.ModelTools.ArrangeItems(new[] { aItem }, ArrangeDirection.Right);

                Assert.AreEqual(0.0, aItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>());
            });
        }

        [Test]
        public void ArrangeInGridSetsMarginLeftWhenNotRightAligned()
        {
            // Two items, so arranging left actually moves one: both align to the leftmost edge
            // (5), which is a no-op for A and a real move for B. With a single item this asserted
            // nothing - and passed only while Margin failed to parse and both edges were 0.
            const string gridXaml =
                "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Margin=\"5,5,0,0\" />" +
                "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Margin=\"40,5,0,0\" /></Grid>";
            WinUITestHost.RunWithWindow(window => {
                var settings = new XamlLoadSettings();
                settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
                settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new WinUIDesigner.Designer.Services.DefaultSelectionService()));
                var designPanel = new WinUIDesigner.Designer.DesignPanel();
                settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), designPanel));
                using var reader = XmlReader.Create(new StringReader(gridXaml));
                var context = new XamlDesignContext(reader, settings);
                designPanel.Context = context;
                window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
                var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                var aItem = context.Services.Component.GetDesignItem(grid.Children[0]);
                var bItem = context.Services.Component.GetDesignItem(grid.Children[1]);

                // Precondition: the margins the XAML asked for actually reached the elements.
                Assert.AreEqual(5.0, aItem.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Microsoft.UI.Xaml.Thickness>().Left);
                Assert.AreEqual(40.0, bItem.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Microsoft.UI.Xaml.Thickness>().Left);

                WinUIDesigner.Designer.ModelTools.ArrangeItems(new[] { aItem, bItem }, ArrangeDirection.Left);

                Assert.AreEqual(5.0, bItem.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Microsoft.UI.Xaml.Thickness>().Left,
                    "B did not move to the leftmost edge");
                Assert.AreEqual(5.0, aItem.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Microsoft.UI.Xaml.Thickness>().Left,
                    "A, already leftmost, should not have moved");
            });
        }
    }
}
