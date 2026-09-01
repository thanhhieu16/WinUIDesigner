using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class DefaultPlacementBehaviorTests
    {
        const string ButtonInGridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" />" +
            "</Grid>";

        static XamlDesignContext Load(string xaml)
        {
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
            // Registering the whole WinUIDesigner.Designer assembly as a designer assembly (needed
            // so DefaultPlacementBehavior itself gets picked up) also registers
            // Services.SelectionAdornerProvider, whose [ExtensionServer(typeof(PrimarySelectionExtensionServer))]
            // reads Services.Selection during ExtensionManager.RegisterAssembly - before the XAML
            // is even parsed. Production code only ever reaches that path through
            // DesignSurface.LoadDesigner, which registers ISelectionService up front for exactly
            // this reason; a bare XamlDesignContext needs the same registration or construction
            // throws ServiceRequiredException(ISelectionService) - confirmed empirically here.
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(ISelectionService), new WinUIDesigner.Designer.Services.DefaultSelectionService()));
            using var reader = XmlReader.Create(new StringReader(xaml));
            return new XamlDesignContext(reader, settings);
        }

        [Test]
        public void GridChildResolvesDefaultPlacementBehavior()
        {
            WinUITestHost.RunWithWindow(window => {
                var context = Load(ButtonInGridXaml);
                window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();

                var button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component).Children[0];
                var buttonItem = context.Services.Component.GetDesignItem(button);

                var behavior = PlacementOperation.GetPlacementBehavior(new[] { buttonItem });
                Assert.IsInstanceOf<DefaultPlacementBehavior>(behavior);
            });
        }

        [Test]
        public void ResizeThroughAPlacementOperationSetsWidthAndHeight()
        {
            WinUITestHost.RunWithWindow(window => {
                var context = Load(ButtonInGridXaml);
                window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();

                var button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component).Children[0];
                var buttonItem = context.Services.Component.GetDesignItem(button);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Resize);
                var info = op.PlacedItems[0];
                info.Bounds = new Windows.Foundation.Rect(info.Bounds.X, info.Bounds.Y, 80, 50);
                op.CurrentContainerBehavior.SetPosition(info);
                op.Commit();

                Assert.AreEqual(80, button.Width);
                Assert.AreEqual(50, button.Height);
            });
        }
    }
}
