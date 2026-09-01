using System.Linq;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class GetCompleteAppliedTransformationToViewTests
    {
        const string RotatedButtonXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\">" +
            "<Button.RenderTransform><RotateTransform Angle=\"30\" /></Button.RenderTransform>" +
            "</Button></Grid>";

        // Gap #3 (spec): confirms re-adding a live, already-parented RenderTransform instance
        // into a new TransformGroup.Children collection does not throw in this SDK version -
        // WPF allows this because Transform is a shareable Freezable, WinUI's is a plain
        // DependencyObject with generally stricter parenting rules.
        [Test]
        public void ReparentingALiveRenderTransformIntoANewTransformGroupDoesNotThrow()
        {
            WinUITestHost.RunWithWindow(window => {
                var settings = new XamlLoadSettings();
                using (var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(RotatedButtonXaml))) {
                    var context = new XamlDesignContext(reader, settings);
                    var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                    var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                    var buttonItem = context.Services.Component.GetDesignItem(button);
                    window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                    ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();

                    Microsoft.UI.Xaml.Media.Transform result = null;
                    Assert.DoesNotThrow(() => result = buttonItem.GetCompleteAppliedTransformationToView());

                    var group = (Microsoft.UI.Xaml.Media.TransformGroup)result;
                    var rotate = group.Children.OfType<Microsoft.UI.Xaml.Media.RotateTransform>().SingleOrDefault();
                    Assert.IsNotNull(rotate, "the button's own RenderTransform should be included");
                    Assert.AreEqual(30, rotate.Angle, 0.01);
                }
            });
        }
    }
}
