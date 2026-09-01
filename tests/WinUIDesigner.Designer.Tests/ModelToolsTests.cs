using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class ModelToolsTests
    {
        const string ButtonXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" />" +
            "</Grid>";

        static (XamlDesignContext context, Microsoft.UI.Xaml.Controls.Button button, WinUIDesigner.DesignModel.DesignItem buttonItem) Load(Microsoft.UI.Xaml.Window window)
        {
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
            // Registering the whole WinUIDesigner.Designer assembly (needed so DefaultPlacementBehavior
            // gets picked up) also registers Services.SelectionAdornerProvider, whose
            // [ExtensionServer(typeof(PrimarySelectionExtensionServer))] reads Services.Selection during
            // ExtensionManager.RegisterAssembly - before the XAML is even parsed. Production code only
            // reaches that path through DesignSurface.LoadDesigner, which registers both services up
            // front; a bare XamlDesignContext needs the same registrations or construction throws
            // ServiceRequiredException - confirmed empirically here. IDesignPanel is additionally needed
            // because ModelTools.ApplyTransform reads Services.DesignPanel; the DesignPanel instance
            // below is never attached to the window, which is fine - AdornerLayer.UpdateAdornersForElement
            // no-ops for an element with no registered adorners (see AdornerLayer.GetExistingAdornerInfo).
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new WinUIDesigner.Designer.Services.DefaultSelectionService()));
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), new WinUIDesigner.Designer.DesignPanel()));
            using var reader = XmlReader.Create(new StringReader(ButtonXaml));
            var context = new XamlDesignContext(reader, settings);
            window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
            ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
            var button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component).Children[0];
            var buttonItem = context.Services.Component.GetDesignItem(button);
            return (context, button, buttonItem);
        }

        [Test]
        public void ResizeSetsWidthAndHeightProperties()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, button, buttonItem) = Load(window);
                using (var group = context.OpenGroup("resize", new[] { buttonItem })) {
                    WinUIDesigner.Designer.ModelTools.Resize(buttonItem, 100, 60);
                    group.Commit();
                }
                Assert.AreEqual(100, button.Width);
                Assert.AreEqual(60, button.Height);
            });
        }

        // Gap #4 (spec): confirms the TransformGroup-wrapping path in ApplyTransform - swapping
        // an existing SkewTransform for a RotateTransform, which must wrap both in a
        // TransformGroup - actually resolves through WinRT's [ContentProperty] metadata via
        // this port's XamlDom component registration, not assumed to work.
        [Test]
        public void ApplyingASecondTransformKindWrapsBothInATransformGroup()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, button, buttonItem) = Load(window);
                WinUIDesigner.Designer.ModelTools.ApplyTransform(buttonItem, new Microsoft.UI.Xaml.Media.SkewTransform { AngleX = 10 }, false);

                Microsoft.UI.Xaml.Media.TransformGroup group = null;
                Assert.DoesNotThrow(() =>
                    WinUIDesigner.Designer.ModelTools.ApplyTransform(buttonItem, new Microsoft.UI.Xaml.Media.RotateTransform { Angle = 20 }, false));

                group = button.RenderTransform as Microsoft.UI.Xaml.Media.TransformGroup;
                Assert.IsNotNull(group, "a second transform kind should wrap both in a TransformGroup");
                Assert.AreEqual(1, group.Children.OfType<Microsoft.UI.Xaml.Media.SkewTransform>().Count());
                Assert.AreEqual(1, group.Children.OfType<Microsoft.UI.Xaml.Media.RotateTransform>().Count());
            });
        }
    }
}
