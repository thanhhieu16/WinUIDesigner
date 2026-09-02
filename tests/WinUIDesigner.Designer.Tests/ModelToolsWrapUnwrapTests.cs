using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class ModelToolsWrapUnwrapTests
    {
        static (XamlDesignContext context, DesignItem canvasItem, DesignItem aItem, DesignItem bItem) LoadTwoButtonsInCanvas(Microsoft.UI.Xaml.Window window)
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
            // WrapItemsNewContainer/UnwrapItemsFromContainer -> PlacementOperation.Start ->
            // SnaplinePlacementBehavior.BeginPlacement reads Services.DesignPanel.Context.RootItem
            // to enumerate sibling items for snaplines (SnaplinePlacementBehavior.cs:256) - same
            // NRE precedent documented in ModelToolsContextMenuTests.Load and
            // ModelToolsArrangeItemsTests.LoadCanvas, so designPanel.Context must be wired here too.
            var designPanel = new WinUIDesigner.Designer.DesignPanel();
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), designPanel));
            using var reader = XmlReader.Create(new StringReader(xaml));
            var context = new XamlDesignContext(reader, settings);
            designPanel.Context = context;
            window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
            ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
            var aItem = context.Services.Component.GetDesignItem(((Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component).Children[0]);
            var bItem = context.Services.Component.GetDesignItem(((Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component).Children[1]);
            return (context, context.RootItem, aItem, bItem);
        }

        [Test]
        public void WrapItemsNewContainerCreatesAndInsertsANewViewboxAsTheOnlyChild()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, canvasItem, aItem, _) = LoadTwoButtonsInCanvas(window);

                var result = WinUIDesigner.Designer.ModelTools.WrapItemsNewContainer(new[] { aItem }, typeof(Microsoft.UI.Xaml.Controls.Viewbox));

                Assert.IsNotNull(result);
                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)canvasItem.Component;
                Assert.AreEqual(2, canvas.Children.Count, "the wrapper replaces A in the canvas, B is untouched");
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Controls.Viewbox>(result.Item1.Component);
                var viewbox = (Microsoft.UI.Xaml.Controls.Viewbox)result.Item1.Component;
                Assert.AreSame(viewbox.Child, aItem.Component);
            });
        }

        [Test]
        public void WrapItemsNewContainerInCanvasSetsLeftTopOnTheWrappedItem()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, canvasItem, aItem, bItem) = LoadTwoButtonsInCanvas(window);

                WinUIDesigner.Designer.ModelTools.WrapItemsNewContainer(new[] { aItem, bItem }, typeof(Microsoft.UI.Xaml.Controls.Canvas));

                // Both items move into a new inner Canvas, re-anchored relative to their own
                // combined bounds (0,0)-(70,50) - A stays at its own Left/Top since it's already
                // the min corner.
                Assert.AreEqual(0.0, aItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>());
                Assert.AreEqual(0.0, aItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>());
            });
        }

        [Test]
        public void UnwrapItemsFromContainerMovesChildrenBackToTheOuterContainer()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, canvasItem, aItem, _) = LoadTwoButtonsInCanvas(window);
                var wrapResult = WinUIDesigner.Designer.ModelTools.WrapItemsNewContainer(new[] { aItem }, typeof(Microsoft.UI.Xaml.Controls.Canvas));
                var innerCanvasItem = wrapResult.Item1;

                WinUIDesigner.Designer.ModelTools.UnwrapItemsFromContainer(innerCanvasItem);

                var outerCanvas = (Microsoft.UI.Xaml.Controls.Canvas)canvasItem.Component;
                Assert.IsFalse(outerCanvas.Children.Contains(innerCanvasItem.Component), "the emptied wrapper is gone");
                Assert.IsTrue(outerCanvas.Children.Contains(aItem.Component), "A is back in the outer canvas");
            });
        }
    }
}
