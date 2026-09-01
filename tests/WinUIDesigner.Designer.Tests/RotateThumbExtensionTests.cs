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
                var drag = extension.DragListener;

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
    }
}
