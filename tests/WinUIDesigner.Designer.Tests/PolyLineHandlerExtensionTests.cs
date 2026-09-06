using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PolyLineHandlerExtensionTests
    {
        // NOTE: no Points="..." attribute here. Historically this was a workaround:
        // Microsoft.UI.Xaml.Media.PointCollection carries no TypeConverterAttribute in this WinUI
        // SDK (unlike WPF's, which had PointCollectionConverter), so a Points attribute silently
        // left Polyline.Points empty. That gap is closed - XamlValueConverter converts
        // PointCollection through WinUI's own XamlReader - but this fixture keeps building its
        // points in code and handing them to SetValue, the same path PolyLineHandlerExtension
        // itself uses.
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

        [Test]
        public void AnExternalPointsShrinkRebuildsTheThumbsInsteadOfLeavingStaleOnesBehind()
        {
            // Final-review regression test (Fix 2). PolyLineHandlerExtension had no
            // ExtendedItem.PropertyChanged -> rebuild path of its own (the only PropertyChanged
            // handling it inherited from PointEditExtensionBase just calls UpdateAdornerVisibility),
            // so any EXTERNAL Points change - an Undo of the Shift+Alt point-insert below above all,
            // but equally a property-grid edit - left the thumb list frozen at its old length. Each
            // surviving stale thumb kept a PointTrackerPlacementSupport whose Index now pointed past
            // the end of the collection, and AdornerPanel.ArrangeOverride calls
            // GetPlacement(child).Arrange(...) for EVERY child unconditionally, so the next layout
            // pass fed that out-of-range Index straight into polyline.Points[Index].
            // PathHandlerExtension already carried this exact fix for Path/Data; this is the
            // Polygon/Polyline/Points half.
            //
            // NB the final review expected an ArgumentOutOfRangeException crash there. This SDK's
            // Microsoft.UI.Xaml.Media.PointCollection does not throw for an out-of-range index - it
            // returns Point(0,0) (probed directly; see PointTrackerPlacementSupport.Arrange's
            // remarks) - so the real symptom was a silently wrong 7x7 handle parked on the shape's
            // local origin rather than a crash. The assertions below pin both halves of the fix.
            WinUITestHost.RunWithWindow(window => {
                var (surface, polylineItem, polyline, extension) = Load(window);
                Assert.AreEqual(3, extension.PointThumbs.Count, "sanity: the fixture's 3 points produced 3 thumbs");

                // Grow Points via the real Shift+Alt insert-during-drag path (Drag_Changed ->
                // InsertPointDuringDrag). Driven through the extension's OWN DragListener for the
                // thumb, for the reason DraggingTheMiddlePointMovesOnlyThatPointAndCommitsOneUndoEntry
                // documents.
                var drag = extension.DragListeners[extension.PointThumbs[1]];
                var shiftAlt = Windows.System.VirtualKeyModifiers.Shift | Windows.System.VirtualKeyModifiers.Menu;
                drag.SimulateDown(new Windows.Foundation.Point(0, 0), shiftAlt);
                drag.SimulateMove(new Windows.Foundation.Point(10, 5), shiftAlt);
                drag.SimulateUp(new Windows.Foundation.Point(10, 5));
                surface.UpdateLayout();

                Assert.AreEqual(4, polyline.Points.Count, "Shift+Alt during the drag inserts a duplicate of the dragged point");
                Assert.AreEqual(4, extension.PointThumbs.Count, "the insert keeps its own thumb bookkeeping in sync");

                // Now shrink Points back externally, exactly as an Undo of that insert would - a
                // plain SetValue from outside this extension, with none of its own bookkeeping.
                var restored = new Microsoft.UI.Xaml.Media.PointCollection();
                restored.Add(new Windows.Foundation.Point(10, 10));
                restored.Add(new Windows.Foundation.Point(50, 10));
                restored.Add(new Windows.Foundation.Point(50, 50));
                polylineItem.Properties["Points"].SetValue(restored);

                Assert.AreEqual(3, extension.PointThumbs.Count, "an external Points change must rebuild the thumbs - a stale 4th thumb indexes past the end of the 3-point collection");
                CollectionAssert.AreEqual(new[] { 0, 1, 2 }, extension.PointThumbs.Select(t => t.Index), "rebuilt thumbs are re-indexed with no gap");
                Assert.AreEqual(3, extension.DragListeners.Count, "DragListeners is rebuilt in lockstep, leaving no reference to a discarded thumb");
                Assert.DoesNotThrow(() => surface.UpdateLayout(), "the layout pass after an external Points shrink must not throw");

                // Defense in depth (same fix round): even if some transient staleness ever survived
                // the rebuild above, PointTrackerPlacementSupport.Arrange must bounds-check the
                // Index. Forced here by publishing a deliberately out-of-range placement for a live
                // thumb - AdornerPanel.SetPlacement invalidates the owning panel's measure, so the
                // UpdateLayout below really does re-run ArrangeOverride, which calls
                // GetPlacement(child).Arrange(...) for every child unconditionally. Asserted on the
                // arranged size rather than on "doesn't throw", because an out-of-range index never
                // throws on this SDK in the first place (see the note at the top of this test): the
                // observable difference is a 7x7 handle parked on the shape's local origin (before)
                // versus nothing at all (after).
                var staleThumb = extension.PointThumbs[2];
                Assert.AreEqual(47f, staleThumb.ActualOffset.X, 0.5, "sanity: while its Index is valid the thumb really is arranged on its own point (50,50) less the 3.5 half-thumb");
                AdornerPanel.SetPlacement(staleThumb, new PointTrackerPlacementSupport(polyline, PlacementAlignment.BottomRight, 99));
                Assert.DoesNotThrow(() => surface.UpdateLayout(), "an out-of-range point index must never throw out of a layout pass");
                // Asserted on the arranged OFFSET, not the arranged size: a MultiPointThumb has a
                // fixed 7x7 DesiredSize, and WinUI clamps an element's rendered size up to its
                // DesiredSize, so ActualSize reads 7x7 either way. The offset does distinguish -
                // Rect(0,0,0,0) arranges at (0,0), whereas reading the out-of-range Points[99]
                // yields Point(0,0) and therefore an arrange at (-3.5,-3.5).
                Assert.AreEqual(0f, staleThumb.ActualOffset.X, 0.5, "an out-of-range point index must arrange to an empty rect at the origin, not to a misleading handle offset by the half-thumb margin");
                Assert.AreEqual(0f, staleThumb.ActualOffset.Y, 0.5);
            });
        }
    }
}
