using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class RasterPlacementBehaviorTests
    {
        const string PanelXaml =
            "<SwapChainPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</SwapChainPanel>";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window, out DesignItem buttonItem)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(RasterPlacementBehaviorTests).Assembly);
            using (var reader = XmlReader.Create(new StringReader(PanelXaml)))
                surface.LoadDesigner(reader, settings);
            surface.UpdateLayout();

            var panel = (SwapChainPanel)surface.DesignContext.RootItem.Component;
            buttonItem = surface.DesignContext.Services.Component.GetDesignItem(panel.Children[0]);

            // Isolate the raster path: this same test-only extension is rebased onto
            // SnaplinePlacementBehavior by Task 6, and snapline placement (on by default) would
            // otherwise take precedence over raster in PlacePoint.
            surface.DesignPanel.UseSnaplinePlacement = false;
            surface.DesignPanel.UseRasterPlacement = true;
            return surface;
        }

        static int RasterLineCount(DesignSurface surface)
        {
            return surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<Canvas>()
                .Sum(c => c.Children.OfType<Line>().Count());
        }

        [Test]
        public void PlacePointRoundsDownToTheRasterCellWhileRasterPlacementIsOn()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var buttonItem);
                var operation = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                try {
                    Assert.AreEqual(new Point(10, 25),
                        operation.CurrentContainerBehavior.PlacePoint(new Point(13.4, 27.9), VirtualKeyModifiers.None));
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void PlacePointFallsBackToPlainRoundingWhenRasterPlacementIsOff()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var buttonItem);
                surface.DesignPanel.UseRasterPlacement = false;
                var operation = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                try {
                    Assert.AreEqual(new Point(13, 28),
                        operation.CurrentContainerBehavior.PlacePoint(new Point(13.4, 27.9), VirtualKeyModifiers.None));
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void HoldingControlSuppressesRasterSnappingForBothEntryPoints()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var buttonItem);
                var operation = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                try {
                    Assert.AreEqual(new Point(13, 28),
                        operation.CurrentContainerBehavior.PlacePoint(new Point(13.4, 27.9), VirtualKeyModifiers.Control),
                        "Ctrl must fall through to the base (plain-rounding) PlacePoint");

                    operation.PlacedItems[0].Bounds = new Rect(13, 27, 41, 22);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.Control);
                    Assert.AreEqual(new Rect(13, 27, 41, 22), operation.PlacedItems[0].Bounds,
                        "Ctrl must leave the operation bounds untouched");
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void BeforeSetPositionSnapsTheOperationBoundsToTheRaster()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var buttonItem);
                var operation = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                try {
                    operation.PlacedItems[0].Bounds = new Rect(13, 27, 41, 22);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                    Assert.AreEqual(new Rect(10, 25, 40, 20), operation.PlacedItems[0].Bounds);
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void TheRasterSurfaceIsCreatedOnBeginPlacementDrawnOnceAndTornDownOnEndPlacement()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var buttonItem);
                Assert.AreEqual(0, surface.DesignPanel.Adorners.Count, "no placement operation, no raster surface");

                var operation = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                Assert.GreaterOrEqual(surface.DesignPanel.Adorners.Count, 1, "BeginPlacement adds the raster surface");
                Assert.AreEqual(0, RasterLineCount(surface), "the raster is not drawn until it is actually used");

                operation.CurrentContainerBehavior.PlacePoint(new Point(13.4, 27.9), VirtualKeyModifiers.None);
                Assert.AreEqual(81, RasterLineCount(surface), "400px tall, 5px raster, inclusive loop => 81 horizontal lines");

                operation.CurrentContainerBehavior.PlacePoint(new Point(21.4, 33.9), VirtualKeyModifiers.None);
                Assert.AreEqual(81, RasterLineCount(surface), "rasterDrawn must stop a second draw pass");

                operation.CurrentContainerBehavior.PlacePoint(new Point(13.4, 27.9), VirtualKeyModifiers.Control);
                Assert.AreEqual(0, RasterLineCount(surface), "Ctrl clears the raster visuals");

                operation.Abort();
                Assert.AreEqual(0, surface.DesignPanel.Adorners.Count, "EndPlacement removes the raster surface");
            });
        }
    }
}
