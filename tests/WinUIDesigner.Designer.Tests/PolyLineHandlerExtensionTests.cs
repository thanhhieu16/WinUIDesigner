using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PolyLineHandlerExtensionTests
    {
        // NOTE: no Points="..." attribute here (deliberately, see Load() below) - this port's
        // XamlDom layer resolves a property's TypeConverter via
        // System.ComponentModel.TypeDescriptor.GetConverter(propertyType) (XamlPropertyInfo.cs),
        // and WinUI's Microsoft.UI.Xaml.Media.PointCollection carries no
        // System.ComponentModel.TypeConverterAttribute (unlike WPF's, which had
        // PointCollectionConverter) - so TypeDescriptor.GetConverter falls back to the base
        // TypeConverter, whose ConvertFromString throws NotSupportedException. That exception is
        // caught and merely Debug.WriteLine-logged by XamlProperty.UpdateValueOnInstance (not
        // rethrown), so a Points="..." attribute silently leaves Polyline.Points at its default
        // empty collection - confirmed empirically (all 3 tests initially failed with
        // PointThumbs.Count == 0 before this fixture change). Matches this phase's own documented
        // gap (spec's WinUI API surface table: "PointCollectionConverter... this port's XamlDom
        // layer serializes the object graph itself; no string mini-language conversion needed") -
        // that note covers serialization; this is the same limitation on the parse side.
        const string PolylineInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Polyline x:Name=\"P\" Stroke=\"Black\" StrokeThickness=\"2\" />" +
            "</Canvas>";

        static (DesignSurface surface, DesignItem polylineItem, Microsoft.UI.Xaml.Shapes.Polyline polyline, PolyLineHandlerExtension extension) Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(PolylineInCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            var polyline = (Microsoft.UI.Xaml.Shapes.Polyline)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
            var polylineItem = surface.DesignContext.Services.Component.GetDesignItem(polyline);

            // Set the initial Points via the DesignItem property API (a real PointCollection
            // object, not a string) - the same SetValue(object) path PolyLineHandlerExtension
            // itself uses for every point mutation - BEFORE selection, so PolyLineHandlerExtension.
            // OnInitialized (which runs when OnlyOneItemSelectedExtensionServer attaches the
            // extension on selection) sees the 3 points already in place.
            var initialPoints = new Microsoft.UI.Xaml.Media.PointCollection();
            initialPoints.Add(new Windows.Foundation.Point(10, 10));
            initialPoints.Add(new Windows.Foundation.Point(50, 10));
            initialPoints.Add(new Windows.Foundation.Point(50, 50));
            polylineItem.Properties["Points"].SetValue(initialPoints);

            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { polylineItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            var extension = polylineItem.Extensions.OfType<PolyLineHandlerExtension>().Single();
            return (surface, polylineItem, polyline, extension);
        }

        [Test]
        public void ThreeInitialPointsProduceThreeThumbsInOrder()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, _, _, extension) = Load(window);
                Assert.AreEqual(3, extension.PointThumbs.Count);
                CollectionAssert.AreEqual(new[] { 0, 1, 2 }, extension.PointThumbs.Select(t => t.Index));
            });
        }

        [Test]
        public void DraggingTheMiddlePointMovesOnlyThatPointAndCommitsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, _, polyline, extension) = Load(window);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int before = undo.UndoActions.Count();
                // Must be the extension's OWN DragListener for this thumb (not a freshly
                // constructed one) - Simulate* drives whichever instance's internal state machine
                // it's called on, and only the extension's own instance is wired to
                // Drag_Started/Drag_Changed/Drag_Completed. See PolyLineHandlerExtension's
                // DragListeners field remarks.
                var drag = extension.DragListeners[extension.PointThumbs[1]];

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(10, 5));
                drag.SimulateUp(new Windows.Foundation.Point(10, 5));
                surface.UpdateLayout();

                Assert.AreEqual(new Windows.Foundation.Point(10, 10), polyline.Points[0]);
                Assert.AreEqual(new Windows.Foundation.Point(60, 15), polyline.Points[1]);
                Assert.AreEqual(new Windows.Foundation.Point(50, 50), polyline.Points[2]);
                Assert.AreEqual(before + 1, undo.UndoActions.Count());
            });
        }

        [Test]
        public void ShiftCtrlClickDeletesAPointAndReindexesRemainingThumbs()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, _, polyline, extension) = Load(window);
                extension.HandleThumbClick(extension.PointThumbs[1], Windows.System.VirtualKeyModifiers.Shift | Windows.System.VirtualKeyModifiers.Control);
                surface.UpdateLayout();

                Assert.AreEqual(2, polyline.Points.Count);
                Assert.AreEqual(new Windows.Foundation.Point(10, 10), polyline.Points[0]);
                Assert.AreEqual(new Windows.Foundation.Point(50, 50), polyline.Points[1]);
                Assert.AreEqual(2, extension.PointThumbs.Count, "the deleted thumb's own entry is gone");
                CollectionAssert.AreEqual(new[] { 0, 1 }, extension.PointThumbs.Select(t => t.Index), "remaining thumbs re-indexed with no gap");
            });
        }

        [Test]
        public void ShiftCtrlClickDeletionRemovesTheThumbFromDragListenersToo()
        {
            // Regression test (fix round 1): DeletePoint used to remove the thumb from
            // adornerPanel.Children and PointThumbs but not from DragListeners, leaking a second
            // live reference to the deleted thumb (and its DragListener's pointer-event
            // subscriptions on it) for as long as the extension persists - breaking spec decision
            // 12's "adornerPanel.Children is the only place a thumb is referenced from" invariant.
            WinUITestHost.RunWithWindow(window => {
                var (surface, _, _, extension) = Load(window);
                var deletedThumb = extension.PointThumbs[1];
                Assert.IsTrue(extension.DragListeners.ContainsKey(deletedThumb), "sanity check: the thumb is registered before deletion");

                extension.HandleThumbClick(deletedThumb, Windows.System.VirtualKeyModifiers.Shift | Windows.System.VirtualKeyModifiers.Control);
                surface.UpdateLayout();

                Assert.IsFalse(extension.DragListeners.ContainsKey(deletedThumb), "the deleted thumb must not remain referenced from DragListeners");
            });
        }
    }
}
