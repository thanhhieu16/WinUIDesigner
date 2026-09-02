using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class SnaplinePlacementBehaviorTests
    {
        // Sibling "A" is positioned from code rather than a XAML Margin literal: this port's
        // XAML parser converts a Thickness attribute string to the default 0,0,0,0 (verified -
        // Margin="150,150,0,0" loads as 0,0,0,0), a pre-existing Phase 1 XamlDom limitation
        // unrelated to placement. The snapline map is built from live layout
        // (TransformToVisual + real size), so setting the live Margin is exactly what these
        // tests need.
        const string PanelXaml =
            "<SwapChainPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Button x:Name=\"A\" Width=\"40\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "<Button x:Name=\"B\" Width=\"40\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</SwapChainPanel>";

        /// <summary>Loads the fixture and leaves sibling A occupying (150,150,40,20) and the
        /// item under test, B, at (0,0,40,20).</summary>
        static DesignSurface Load(Microsoft.UI.Xaml.Window window, out DesignItem movedItem)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(SnaplinePlacementBehaviorTests).Assembly);
            using (var reader = XmlReader.Create(new StringReader(PanelXaml)))
                surface.LoadDesigner(reader, settings);
            surface.UpdateLayout();

            var panel = (SwapChainPanel)surface.DesignContext.RootItem.Component;
            ((FrameworkElement)panel.Children[0]).Margin = new Thickness(150, 150, 0, 0);
            surface.UpdateLayout();

            movedItem = surface.DesignContext.Services.Component.GetDesignItem(panel.Children[1]);
            surface.DesignPanel.UseSnaplinePlacement = true;
            surface.DesignPanel.UseRasterPlacement = false;
            return surface;
        }

        static int SnaplineCount(DesignSurface surface)
        {
            return surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<Canvas>()
                .Sum(c => c.Children.OfType<Line>().Count());
        }

        [Test]
        public void PlacePointSnapsToASiblingsLeftAndTopEdges()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Move);
                try {
                    var behavior = operation.CurrentContainerBehavior;
                    Assert.AreEqual(new Point(150, 300), behavior.PlacePoint(new Point(152, 300), VirtualKeyModifiers.None),
                        "X snaps to sibling A's left edge, Y has nothing within SnaplineAccuracy");
                    Assert.AreEqual(new Point(300, 150), behavior.PlacePoint(new Point(300, 152), VirtualKeyModifiers.None),
                        "Y snaps to sibling A's top edge");
                    Assert.AreEqual(new Point(300, 300), behavior.PlacePoint(new Point(300, 300), VirtualKeyModifiers.None),
                        "a point with no snapline within range is left where it is");
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void HoldingControlSuppressesSnappingForBothEntryPoints()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Move);
                try {
                    Assert.AreEqual(new Point(152, 300),
                        operation.CurrentContainerBehavior.PlacePoint(new Point(152, 300), VirtualKeyModifiers.Control));

                    operation.PlacedItems[0].Bounds = new Rect(152, 300, 40, 20);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.Control);
                    Assert.AreEqual(new Rect(152, 300, 40, 20), operation.PlacedItems[0].Bounds);
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void BeforeSetPositionSnapsAMovesBoundsToTheSibling()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Move);
                try {
                    operation.PlacedItems[0].Bounds = new Rect(152, 300, 40, 20);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                    Assert.AreEqual(new Rect(150, 300, 40, 20), operation.PlacedItems[0].Bounds);

                    operation.PlacedItems[0].Bounds = new Rect(300, 300, 40, 20);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                    Assert.AreEqual(new Rect(300, 300, 40, 20), operation.PlacedItems[0].Bounds,
                        "nothing in range means nothing moves");
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void AResizeSnapsOnlyTheEdgeTheThumbIsDragging()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Resize);
                try {
                    var info = operation.PlacedItems[0];
                    info.ResizeThumbAlignment = PlacementAlignment.BottomRight;
                    info.Bounds = new Rect(0, 0, 152, 20);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                    Assert.AreEqual(new Rect(0, 0, 150, 20), info.Bounds,
                        "the dragged right edge snaps to sibling A's left edge; X and Height stay put");
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void SnaplinePlacementRoundValuesRoundsTheSnappedResizeBounds()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                var options = surface.DesignContext.Services.GetService<OptionService>();

                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Resize);
                try {
                    var info = operation.PlacedItems[0];
                    info.ResizeThumbAlignment = PlacementAlignment.Top;

                    options.SnaplinePlacementRoundValues = false;
                    info.Bounds = new Rect(0, 152, 40, 148.6);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                    Assert.AreEqual(new Rect(0, 150, 40, 150.6), info.Bounds,
                        "top-thumb resize moves Y to the snapline and grows Height by the same delta");

                    options.SnaplinePlacementRoundValues = true;
                    info.Bounds = new Rect(0, 152, 40, 148.6);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                    Assert.AreEqual(new Rect(0, 150, 40, 151), info.Bounds,
                        "with rounding on, the fractional Height is rounded");
                } finally {
                    options.SnaplinePlacementRoundValues = false;
                    operation.Abort();
                }
            });
        }

        [Test]
        public void SnaplineVisualsAreDrawnOnlyWhileAnEdgeIsActuallySnapped()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                Assert.AreEqual(0, surface.DesignPanel.Adorners.Count);

                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Move);
                // Two surfaces: RasterPlacementBehavior's and SnaplinePlacementBehavior's - both
                // BeginPlacement overrides run, one per level of the chain.
                Assert.AreEqual(2, surface.DesignPanel.Adorners.Count);
                Assert.AreEqual(0, SnaplineCount(surface));

                operation.PlacedItems[0].Bounds = new Rect(152, 300, 40, 20);
                operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                Assert.Greater(SnaplineCount(surface), 0, "a snapped edge draws its guide line");

                operation.PlacedItems[0].Bounds = new Rect(300, 300, 40, 20);
                operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                Assert.AreEqual(0, SnaplineCount(surface), "moving out of range clears the guides");

                operation.Abort();
                Assert.AreEqual(0, surface.DesignPanel.Adorners.Count, "EndPlacement tears both surfaces down");
            });
        }

        [Test]
        public void DisableSnaplinesOnASiblingRemovesItFromTheSnaplineMap()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                var panel = (SwapChainPanel)surface.DesignContext.RootItem.Component;
                SnaplinePlacementBehavior.SetDisableSnaplines(panel.Children[0], true);
                Assert.IsTrue(SnaplinePlacementBehavior.GetDisableSnaplines(panel.Children[0]));

                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Move);
                try {
                    Assert.AreEqual(new Point(152, 300),
                        operation.CurrentContainerBehavior.PlacePoint(new Point(152, 300), VirtualKeyModifiers.None),
                        "a sibling with DisableSnaplines set contributes no snaplines");
                } finally {
                    operation.Abort();
                }
            });
        }
    }
}
