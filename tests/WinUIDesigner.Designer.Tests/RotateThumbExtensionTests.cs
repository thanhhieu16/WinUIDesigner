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
    public class RotateThumbExtensionTests
    {
        const string ButtonXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\" Width=\"60\" Height=\"30\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</Grid>";

        static DesignSurface LoadSelected(Microsoft.UI.Xaml.Window window, out DesignItem buttonItem, out Microsoft.UI.Xaml.Controls.Button button)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(ButtonXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();

            button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component).Children[0];
            buttonItem = surface.DesignContext.Services.Component.GetDesignItem(button);
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void DraggingTheRotateThumbAppliesARotateTransform()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var extension = buttonItem.Extensions.OfType<RotateThumbExtension>().Single();
                var drag = extension.RotateDragListener;

                var centerScreen = button.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(30, 15));
                var startAbove = new Windows.Foundation.Point(centerScreen.X, centerScreen.Y - 40); // straight up from center
                var afterQuarterTurn = new Windows.Foundation.Point(centerScreen.X + 40, centerScreen.Y); // straight right - 90 deg clockwise

                drag.SimulateDown(startAbove);
                drag.SimulateMove(afterQuarterTurn);
                drag.SimulateUp(afterQuarterTurn);
                surface.UpdateLayout();

                var rotate = button.RenderTransform as Microsoft.UI.Xaml.Media.RotateTransform;
                Assert.IsNotNull(rotate, "expected RenderTransform to be a RotateTransform after dragging the rotate thumb");
                Assert.AreEqual(90, rotate.Angle, 1, "a quarter turn clockwise around the center should read back as ~90 degrees");
            });
        }

        // Final-review finding #4: ModelTools.ApplyTransform replaces RenderTransform with a
        // brand-new instance on every call, but RotateThumbExtension's own rotateTransform field
        // was only ever captured once in OnInitialized and never refreshed - so a second rotate
        // gesture computed its start angle from a stale (null/0) baseline and snapped back to 0
        // instead of continuing from the first gesture's angle. Two separate down/move/up
        // gestures (not two ticks of one drag) are what actually exercises the bug, since the
        // stale field was only ever read in Drag_Started.
        [Test]
        public void DraggingTheRotateThumbTwiceContinuesFromTheFirstGesturesAngleInsteadOfResettingToZero()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var extension = buttonItem.Extensions.OfType<RotateThumbExtension>().Single();
                var drag = extension.RotateDragListener;

                var centerScreen = button.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(30, 15));
                var startAbove = new Windows.Foundation.Point(centerScreen.X, centerScreen.Y - 40); // north of center
                var afterQuarterTurn = new Windows.Foundation.Point(centerScreen.X + 40, centerScreen.Y); // east of center - 90 deg cw from north

                drag.SimulateDown(startAbove);
                drag.SimulateMove(afterQuarterTurn);
                drag.SimulateUp(afterQuarterTurn);
                surface.UpdateLayout();

                var angleAfterFirstGesture = ((Microsoft.UI.Xaml.Media.RotateTransform)button.RenderTransform).Angle;
                Assert.AreEqual(90, angleAfterFirstGesture, 1, "sanity check on the first gesture's committed angle");

                // Second gesture: start where the first one ended (east of center) and move to
                // south of center - another 90 deg clockwise. Correct behavior continues from 90
                // to 180; the finding #4 bug would compute this delta against a stale initialAngle
                // of 0, landing on ~90 instead (i.e. behaving as if the first gesture never
                // happened).
                var afterHalfTurn = new Windows.Foundation.Point(centerScreen.X, centerScreen.Y + 40); // south of center
                drag.SimulateDown(afterQuarterTurn);
                drag.SimulateMove(afterHalfTurn);
                drag.SimulateUp(afterHalfTurn);
                surface.UpdateLayout();

                var angleAfterSecondGesture = ((Microsoft.UI.Xaml.Media.RotateTransform)button.RenderTransform).Angle;
                Assert.AreEqual(180, angleAfterSecondGesture, 1,
                    "the second rotate gesture should continue from the first gesture's committed angle (90 -> 180), not reset to ~90 as if starting from 0");
            });
        }

        // Final-review findings #1/#2: without an outer ChangeGroup bracketing the whole drag,
        // each Drag_Changed tick's ModelTools.ApplyTransform call committed its own separate
        // undo-stack entry - a 2-second rotate drag at typical mouse polling rates would need
        // ~250 Ctrl+Z presses to fully undo. Fixed by opening one ChangeGroup in Drag_Started and
        // committing/aborting it in Drag_Completed, so every tick nests inside it instead.
        [Test]
        public void MultiTickRotateDragCommitsExactlyOneUndoEntryForTheWholeGesture()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var extension = buttonItem.Extensions.OfType<RotateThumbExtension>().Single();
                var drag = extension.RotateDragListener;
                var undoService = surface.DesignContext.Services.GetService<WinUIDesigner.DesignModel.Services.UndoService>();
                Assert.IsNotNull(undoService, "XamlDesignContext always registers UndoService");

                var centerScreen = button.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(30, 15));
                var startAbove = new Windows.Foundation.Point(centerScreen.X, centerScreen.Y - 40);
                var quarterTurn = new Windows.Foundation.Point(centerScreen.X + 40, centerScreen.Y); // 90 deg cw
                var halfTurn = new Windows.Foundation.Point(centerScreen.X, centerScreen.Y + 40); // 180 deg cw

                drag.SimulateDown(startAbove);
                drag.SimulateMove(quarterTurn); // tick 1
                surface.UpdateLayout();
                drag.SimulateMove(halfTurn); // tick 2
                drag.SimulateUp(halfTurn);
                surface.UpdateLayout();

                Assert.AreEqual(180, ((Microsoft.UI.Xaml.Media.RotateTransform)button.RenderTransform).Angle, 1,
                    "sanity check on the drag's final committed angle");
                Assert.AreEqual(1, undoService.UndoActions.Count(),
                    "a multi-tick rotate drag should collapse into exactly one undo-stack entry, not one per tick");

                undoService.Undo();
                surface.UpdateLayout();
                Assert.IsFalse(buttonItem.Properties.GetProperty("RenderTransform").IsSet,
                    "a single Undo should revert the whole rotate drag back to its pre-drag state (no RenderTransform), not just the last tick");
            });
        }
    }
}
