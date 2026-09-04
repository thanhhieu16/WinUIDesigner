using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class LineHandlerExtensionTests
    {
        const string LineInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Line x:Name=\"L\" X1=\"10\" Y1=\"10\" X2=\"50\" Y2=\"50\" Stroke=\"Black\" StrokeThickness=\"2\" />" +
            "</Canvas>";

        static (DesignSurface surface, DesignItem lineItem, Microsoft.UI.Xaml.Shapes.Line line) Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(LineInCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            var line = (Microsoft.UI.Xaml.Shapes.Line)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
            var lineItem = surface.DesignContext.Services.Component.GetDesignItem(line);
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { lineItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            return (surface, lineItem, line);
        }

        [Test]
        public void DraggingTheBottomRightThumbMovesOnlyX2Y2()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, lineItem, line) = Load(window);
                var extension = lineItem.Extensions.OfType<LineHandlerExtension>().Single();
                var drag = extension.DragListeners[WinUIDesigner.DesignModel.PlacementAlignment.BottomRight];

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(10, 5));
                drag.SimulateUp(new Windows.Foundation.Point(10, 5));
                surface.UpdateLayout();

                Assert.AreEqual(10, line.X1, 0.5, "X1/Y1 untouched");
                Assert.AreEqual(10, line.Y1, 0.5);
                Assert.AreEqual(60, line.X2, 0.5);
                Assert.AreEqual(55, line.Y2, 0.5);
            });
        }

        [Test]
        public void DraggingTheTopLeftThumbMovesOnlyX1Y1AndCommitsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, lineItem, line) = Load(window);
                var extension = lineItem.Extensions.OfType<LineHandlerExtension>().Single();
                var drag = extension.DragListeners[WinUIDesigner.DesignModel.PlacementAlignment.TopLeft];
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int undoCountBefore = undo.UndoActions.Count();

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(-5, -5));
                drag.SimulateUp(new Windows.Foundation.Point(-5, -5));
                surface.UpdateLayout();

                Assert.AreEqual(5, line.X1, 0.5);
                Assert.AreEqual(5, line.Y1, 0.5);
                Assert.AreEqual(50, line.X2, 0.5, "X2/Y2 untouched");
                Assert.AreEqual(50, line.Y2, 0.5);
                Assert.AreEqual(undoCountBefore + 1, undo.UndoActions.Count(), "one drag = one undo entry");
            });
        }
    }
}
