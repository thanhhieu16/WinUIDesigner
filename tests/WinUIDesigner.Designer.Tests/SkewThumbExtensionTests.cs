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
    public class SkewThumbExtensionTests
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
        public void DraggingTheHorizontalSkewThumbAppliesASkewTransform()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var extension = buttonItem.Extensions.OfType<SkewThumbExtension>().Single();
                var drag = extension.DragListenerX;

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(-20, 0)); // -0.5 * -20 = +10 degrees AngleX
                drag.SimulateUp(new Windows.Foundation.Point(-20, 0));
                surface.UpdateLayout();

                var skew = button.RenderTransform as Microsoft.UI.Xaml.Media.SkewTransform;
                Assert.IsNotNull(skew, "expected RenderTransform to be a SkewTransform after dragging the X skew thumb");
                Assert.AreEqual(10, skew.AngleX, 0.5);
                Assert.AreEqual(0, skew.AngleY, 0.5);
            });
        }

        [Test]
        public void DraggingBackToZeroResetsTheRenderTransform()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var extension = buttonItem.Extensions.OfType<SkewThumbExtension>().Single();
                var drag = extension.DragListenerX;

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(-20, 0));
                drag.SimulateUp(new Windows.Foundation.Point(-20, 0));
                surface.UpdateLayout();
                Assert.IsNotNull(button.RenderTransform);

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(20, 0)); // back to 0
                drag.SimulateUp(new Windows.Foundation.Point(20, 0));
                surface.UpdateLayout();

                Assert.IsFalse(buttonItem.Properties.GetProperty("RenderTransform").IsSet);
            });
        }

        // Final-review findings #1/#2 (skew's equivalent of RotateThumbExtensionTests.
        // MultiTickRotateDragCommitsExactlyOneUndoEntryForTheWholeGesture): without the
        // per-handle ChangeGroup this test drives against, each Drag_Changed tick's ApplySkew
        // call committed its own separate undo-stack entry instead of the whole drag collapsing
        // into one.
        [Test]
        public void MultiTickSkewDragCommitsExactlyOneUndoEntryForTheWholeGesture()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var extension = buttonItem.Extensions.OfType<SkewThumbExtension>().Single();
                var drag = extension.DragListenerX;
                var undoService = surface.DesignContext.Services.GetService<WinUIDesigner.DesignModel.Services.UndoService>();
                Assert.IsNotNull(undoService, "XamlDesignContext always registers UndoService");

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(-10, 0)); // tick 1: +5 degrees
                surface.UpdateLayout();
                drag.SimulateMove(new Windows.Foundation.Point(-20, 0)); // tick 2: +10 degrees
                drag.SimulateUp(new Windows.Foundation.Point(-20, 0));
                surface.UpdateLayout();

                var skew = button.RenderTransform as Microsoft.UI.Xaml.Media.SkewTransform;
                Assert.IsNotNull(skew);
                Assert.AreEqual(10, skew.AngleX, 0.5, "sanity check on the drag's final committed AngleX");
                Assert.AreEqual(1, undoService.UndoActions.Count(),
                    "a multi-tick skew drag on one handle should collapse into exactly one undo-stack entry, not one per tick");

                undoService.Undo();
                surface.UpdateLayout();
                Assert.IsFalse(buttonItem.Properties.GetProperty("RenderTransform").IsSet,
                    "a single Undo should revert the whole skew drag back to its pre-drag state (no RenderTransform), not just the last tick");
            });
        }

        [Test]
        public void ASkewSurvivesASaveAndReload()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var extension = buttonItem.Extensions.OfType<SkewThumbExtension>().Single();
                var drag = extension.DragListenerX;

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(-20, 0)); // -0.5 * -20 = +10 degrees
                drag.SimulateUp(new Windows.Foundation.Point(-20, 0));
                surface.UpdateLayout();

                var text = new StringWriter();
                var settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                using (var writer = XmlWriter.Create(text, settings))
                    surface.DesignContext.Save(writer);
                var saved = text.ToString();

                // ApplySkew sets RenderTransform to a live SkewTransform and stops there - unlike
                // ModelTools.ApplyTransform, which writes the angle through the model afterwards.
                // Before Phase C the element written for that transform was empty, so dragging the
                // skew thumb and saving lost the gesture with no error anywhere.
                var reloaded = WinUIDesigner.XamlDom.XamlParser.Parse(new StringReader(saved));
                var reloadedButton = (Microsoft.UI.Xaml.Controls.Button)
                    ((Microsoft.UI.Xaml.Controls.Grid)reloaded.RootInstance).Children[0];

                var skew = reloadedButton.RenderTransform as Microsoft.UI.Xaml.Media.SkewTransform;
                Assert.IsNotNull(skew, "the skew did not survive the save; saved as: " + saved);
                Assert.AreEqual(10, skew.AngleX, 0.5, "saved as: " + saved);
                Assert.AreEqual(0, skew.AngleY, 0.5, "saved as: " + saved);
            });
        }
    }
}
