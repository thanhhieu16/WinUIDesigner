using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class Phase4dDefinitionOfDoneTests
    {
        const string EmptyCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\" />";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(EmptyCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());

            // Brief-defect correction discovered empirically while implementing this task (not
            // one of the brief's own two flagged corrections, but blocking both tests below the
            // same way): the same fix CreateComponentToolTests.cs/MoveTests.cs already document
            // for their own Canvas fixtures. A Panel/Canvas with Background == null isn't
            // hit-test visible over its own empty interior (WPF/UWP/WinUI convention), so without
            // this, DesignPanel.HitTest never finds the Canvas at all and every
            // tool.HandlePointerPressed call below reads as "hit nothing" - confirmed empirically:
            // both FullGestureConnectsEndToEndToolboxSelectionCursorChangeDrawCommitPointEdit
            // (CreateComponentTool.Gesture stayed null, so its later
            // .HandlePointerReleased()/GetDesignItem call NullReferenceException'd/
            // ArgumentNullException'd) and EveryDrawGestureLeavesNoDanglingChangeGroupOnEscape
            // failed this way before this line was added.
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
            canvas.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

            surface.UpdateLayout();
            return surface;
        }

        // Brief-defect correction (see Load()'s own comment): the Canvas has an explicit
        // Width/Height, so DesignPanel.ArrangeOverride's Stretch-alignment arrange centers it
        // within the shared test window's (much larger) DesignPanel instead of placing it flush
        // at panel-local (0,0) - a hardcoded panel-local point can land outside the Canvas
        // entirely. Deriving the point from the Canvas's own live
        // TransformToVisual(surface.DesignPanel), the same pattern CreateComponentToolTests.cs/
        // MoveTests.cs/HitTestTests.cs already use, is robust to wherever the Canvas actually got
        // arranged.
        static Windows.Foundation.Point CanvasPointToPanel(DesignSurface surface, Windows.Foundation.Point canvasLocalPoint)
        {
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
            return canvas.TransformToVisual(surface.DesignPanel).TransformPoint(canvasLocalPoint);
        }

        [Test]
        public void FullGestureConnectsEndToEndToolboxSelectionCursorChangeDrawCommitPointEdit()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var toolService = surface.DesignContext.Services.GetService<IToolService>();
                Assert.AreSame(toolService.PointerTool, toolService.CurrentTool, "starts on PointerTool");

                // "toolbox selection" (no real toolbox yet - spec Known gap #3): user selects
                // Line from a toolbox, which sets CurrentTool.
                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Shapes.Line));
                toolService.CurrentTool = tool;
                Assert.AreSame(tool, toolService.CurrentTool);

                // "cursor change": ITool.Cursor was applied (can't assert the OS cursor directly,
                // but SetCursor was reachable and didn't throw - see Task 2).

                // "draw": click-drag creates and sizes a Line.
                //
                // Not a bug (traced, no fix needed - see task-14-report.md): the following
                // SimulateDown re-runs BeginAt on the very same DragListener that
                // DrawLineExtension.StartDrawItem already primed via DragListener.ExternalStart
                // (DrawLineExtension.cs:85) when tool.HandlePointerPressed dispatched to it above.
                // DragListener.BeginAt (DragListener.cs:122-129) unconditionally overwrites
                // StartPoint/CurrentPoint/IsDown/IsCanceled with no guard, and since no
                // ExternalMove ran between the two BeginAt calls, IsActive never flipped true, so
                // nothing fired prematurely from the discarded first call. The test's arithmetic
                // (final line.X2 == 25 after two separate drag deltas of 20 then 5) is
                // self-consistent under this mechanic.
                tool.HandlePointerPressed(surface.DesignPanel, CanvasPointToPanel(surface, new Windows.Foundation.Point(50, 50)), Windows.System.VirtualKeyModifiers.None);
                var lineItem = surface.DesignContext.Services.Component.GetDesignItem(
                    ((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0]);
                var lineHandler = lineItem.Extensions.OfType<LineHandlerExtension>().Single();
                lineHandler.DragListener.SimulateDown(new Windows.Foundation.Point(0, 0));
                lineHandler.DragListener.SimulateMove(new Windows.Foundation.Point(20, 20));
                lineHandler.DragListener.SimulateUp(new Windows.Foundation.Point(20, 20));

                // Addition beyond the brief's literal code (a genuine gap found while verifying
                // this test empirically - see task-14-report.md): the brief's version stopped
                // here and asserted the tool had reset to PointerTool, but LineHandlerExtension.
                // Drag_Completed (LineHandlerExtension.cs:92-107) only commits ITS OWN inner
                // resize operation/changeGroup - it never touches toolService.CurrentTool. The
                // reset back to PointerTool only happens in DrawLineMouseGesture.Stop()
                // (DrawLineExtension.cs:130-142), which is reached only via the OUTER "Draw Line"
                // gesture's own HandlePointerReleased/Cancel - driven by real panel-level
                // PointerReleased/PointerCaptureLost/KeyDown events in production, or by this test
                // seam. Without this call the assertion below would fail (confirmed empirically:
                // it did, with CurrentTool still the CreateComponentTool). Matches the already-
                // established pattern in DrawLineExtensionTests.
                // DrawingALineEndToEndProducesTheExpectedEndpointsAndOneUndoEntry, which drives
                // the exact same two-step sequence (inner DragListener.SimulateUp, then the outer
                // DrawLineExtension.Gesture.HandlePointerReleased) for the same reason.
                var drawLineExtension = surface.DesignContext.RootItem.Extensions.OfType<DrawLineExtension>().Single();
                drawLineExtension.Gesture.HandlePointerReleased(new Windows.Foundation.Point(20, 20));
                surface.UpdateLayout();

                // "commit": tool reset back to PointerTool once the draw gesture completed.
                Assert.AreSame(toolService.PointerTool, toolService.CurrentTool);

                // "point-edit": the same LineHandlerExtension instance (still attached, no
                // ReapplyAllExtensions needed for Line - only Polyline/Path structural edits
                // trigger that) accepts a further point-drag.
                lineHandler.DragListener.SimulateDown(new Windows.Foundation.Point(0, 0));
                lineHandler.DragListener.SimulateMove(new Windows.Foundation.Point(5, 0));
                lineHandler.DragListener.SimulateUp(new Windows.Foundation.Point(5, 0));
                surface.UpdateLayout();

                var line = (Microsoft.UI.Xaml.Shapes.Line)lineItem.View;
                Assert.AreEqual(25, line.X2, 0.5, "post-creation point-edit moved the same endpoint the draw gesture created");
            });
        }

        [Test]
        public void AllThreePointEditExtensionsUseTheSameOverrideExtensionsList()
        {
            // Spec decision 7: Line/Polygon/Polyline/Path's OverrideExtensions must all list
            // exactly ResizeThumbExtension/RotateThumbExtension/SkewThumbExtension - drift between
            // the three *HandlerExtension classes would silently reintroduce the double-adorner
            // bug (both a generic resize thumb AND a point-edit thumb showing at once) for
            // whichever type's list fell out of sync.
            var expected = new[] {
                typeof(WinUIDesigner.Designer.Extensions.ResizeThumbExtension),
                typeof(WinUIDesigner.Designer.Extensions.RotateThumbExtension),
                typeof(WinUIDesigner.Designer.Extensions.SkewThumbExtension)
            };
            foreach (var extensionType in new[] { typeof(LineHandlerExtension), typeof(PolyLineHandlerExtension), typeof(PathHandlerExtension) }) {
                var attrs = extensionType.GetCustomAttributes(typeof(ExtensionForAttribute), false).Cast<ExtensionForAttribute>();
                foreach (var attr in attrs) {
                    CollectionAssert.AreEquivalent(expected, attr.OverrideExtensions, $"{extensionType.Name}'s [ExtensionFor({attr.DesignedItemType.Name})] OverrideExtensions drifted from spec decision 7");
                }
            }
        }

        [Test]
        public void EveryDrawGestureLeavesNoDanglingChangeGroupOnEscape()
        {
            // The exact defect class 4c's final review caught three times (spec Testing/DoD's
            // closing bullet): Escape mid-gesture must abort cleanly enough that Undo/Redo still
            // work afterwards, not throw "Cannot Undo while transaction is running".
            //
            // Corrected from the brief's version (see task-14-report.md, "Required correction 1"):
            // the brief's test called tool.HandlePointerPressed(...) - which opens a ChangeGroup
            // via context.RootItem.OpenGroup("Add Control") (CreateComponentTool.cs:110), pushing
            // onto UndoService._transactionStack (UndoService.cs:202-205) - and never committed,
            // aborted, or otherwise released it, then asserted Undo() didn't throw. But
            // UndoService.Undo() (UndoService.cs:255-260) checks `if (!CanUndo) throw ...` FIRST,
            // before ever checking `_transactionStack.Count != 0`. Since nothing was ever
            // committed, _undoStack stays empty, CanUndo is false, and Undo() throws "Cannot Undo:
            // undo stack is empty" unconditionally - regardless of whether the dangling-transaction
            // defect this test claims to guard against exists or not. That version would "pass" for
            // the wrong reason (or rather, throws for a reason the assertion's name doesn't intend
            // and Assert.DoesNotThrow would just fail outright).
            //
            // Redesigned to actually exercise the intended defect class: first commit ONE real
            // action (so CanUndo is genuinely true), then start and abort a SECOND, independent
            // gesture (simulating Escape/PointerCaptureLost), then assert the real prior action can
            // still be undone - i.e. an aborted gesture's transaction pop doesn't leave anything on
            // _transactionStack that would block a legitimate later Undo().
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var toolService = surface.DesignContext.Services.GetService<IToolService>();
                var undo = surface.DesignContext.Services.GetService<UndoService>();

                // Step 1: one real, fully-committed action, so _undoStack.Count >= 1 and CanUndo
                // is genuinely true. CreateComponentTool.Gesture exposes the internal
                // CreateComponentMouseGesture started by HandlePointerPressed; calling its own
                // HandlePointerReleased test seam directly (no drag in between) takes the
                // click-without-drag path, which commits immediately via AddItemsWithDefaultSize
                // (CreateComponentTool.cs:279-290).
                var commitTool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Controls.Button));
                toolService.CurrentTool = commitTool;
                commitTool.HandlePointerPressed(surface.DesignPanel, CanvasPointToPanel(surface, new Windows.Foundation.Point(50, 50)), Windows.System.VirtualKeyModifiers.None);
                commitTool.Gesture.HandlePointerReleased();
                surface.UpdateLayout();
                Assert.IsTrue(undo.CanUndo, "one real action must be committed before the abort scenario below is a meaningful test");

                // Step 2: a fresh, independent CreateComponentTool instance (single-use by design -
                // it has its own _drawing flag) starts a SECOND gesture at a different point, then
                // aborts it via the gesture's own Cancel() seam - the same unwind path
                // PointerCaptureLost/Escape drive in production (CreateComponentMouseGesture.cs:
                // OnPointerCaptureLost/OnKeyDown both call Cancel()). Cancel() aborts the
                // ChangeGroup, which pops it off UndoService._transactionStack without pushing
                // anything onto _undoStack.
                var abortTool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Controls.Button));
                toolService.CurrentTool = abortTool;
                abortTool.HandlePointerPressed(surface.DesignPanel, CanvasPointToPanel(surface, new Windows.Foundation.Point(150, 150)), Windows.System.VirtualKeyModifiers.None);
                abortTool.Gesture.Cancel();
                surface.UpdateLayout();

                // Step 3: the dangling transaction, once properly aborted, must not block undoing
                // the real prior change from Step 1.
                Assert.DoesNotThrow(() => undo.Undo(), "no dangling transaction should block Undo even mid-gesture, once the gesture properly aborts");
            });
        }

        [Test]
        public void PolyLineHandlerExtensionsInternalIndexStaysInSyncWithAdornerPanelChildren()
        {
            // Corrected from the brief's version (see task-14-report.md, "Required correction 2"):
            // the brief's inline XAML used a Points="10,10 50,10 50,50 10,50" string attribute,
            // which hits the exact same PointCollection-has-no-TypeConverterAttribute bug an
            // earlier task in this plan already found and fixed for Polyline.Points fixtures
            // (confirmed via direct WinUI SDK reflection in that task's review -
            // Microsoft.UI.Xaml.Media.PointCollection carries no TypeConverterAttribute in this
            // SDK, so XamlProperty.UpdateValueOnInstance's string-to-object conversion silently
            // fails and swallows the failure - see PolyLineHandlerExtensionTests.cs's own fixture
            // comment for the full trace). Loaded via surface.LoadDesigner(...) with the brief's
            // literal XAML, polyline.Points would end up empty and extension.PointThumbs[1] would
            // throw ArgumentOutOfRangeException before this test's real assertions ever ran.
            //
            // Fixed by loading the XAML without a Points attribute (still parses structurally
            // fine), then setting Points via the DesignItem property API - the same SetValue(object)
            // path PolyLineHandlerExtension itself uses - BEFORE selection, so
            // PolyLineHandlerExtension.OnInitialized (which runs when
            // OnlyOneItemSelectedExtensionServer attaches the extension on selection) sees the 4
            // points already in place.
            const string xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
                "<Polyline x:Name=\"P\" Stroke=\"Black\" StrokeThickness=\"2\" />" +
                "</Canvas>";
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(xaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var polyline = (Microsoft.UI.Xaml.Shapes.Polyline)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
                var polylineItem = surface.DesignContext.Services.Component.GetDesignItem(polyline);

                var points = new Microsoft.UI.Xaml.Media.PointCollection();
                points.Add(new Windows.Foundation.Point(10, 10));
                points.Add(new Windows.Foundation.Point(50, 10));
                points.Add(new Windows.Foundation.Point(50, 50));
                points.Add(new Windows.Foundation.Point(10, 50));
                polylineItem.Properties["Points"].SetValue(points);
                surface.UpdateLayout();

                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { polylineItem }, SelectionTypes.Primary);
                surface.UpdateLayout();
                var extension = polylineItem.Extensions.OfType<PolyLineHandlerExtension>().Single();

                // Verifies the fixture fix actually worked (not vacuously testing a 0-count
                // collection) before proceeding with the delete that's the point of the test.
                Assert.AreEqual(4, extension.PointThumbs.Count, "fixture must produce 4 real point thumbs before the delete-click below is meaningful");

                extension.HandleThumbClick(extension.PointThumbs[1], Windows.System.VirtualKeyModifiers.Shift | Windows.System.VirtualKeyModifiers.Control);
                surface.UpdateLayout();

                var thumbsInAdornerPanel = surface.DesignPanel.Adorners
                    .SelectMany(a => a.Children)
                    .OfType<WinUIDesigner.Designer.Controls.Thumbs.MultiPointThumb>()
                    .ToList();

                Assert.AreEqual(polyline.Points.Count, extension.PointThumbs.Count, "internal index count matches the real Points count after a delete");
                Assert.AreEqual(extension.PointThumbs.Count, thumbsInAdornerPanel.Count, "internal index and the real visual tree (adornerPanel.Children) agree on count - decision 12's lockstep requirement");
                CollectionAssert.AreEquivalent(extension.PointThumbs, thumbsInAdornerPanel, "internal index holds exactly the thumbs actually in the visual tree, no stale entries");
            });
        }
    }
}
