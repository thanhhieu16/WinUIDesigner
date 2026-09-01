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
    }
}
