using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class CopyPasteServiceTests
    {
        const string OneButtonXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" /></Canvas>";

        [Test]
        public void CanDeleteAndDeleteWorkThroughTheDesignContext()
        {
            WinUITestHost.RunWithWindow(window => {
                var settings = new XamlLoadSettings();
                settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
                settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new DefaultSelectionService()));
                // See ModelToolsContextMenuTests.Load for why Context must be wired up explicitly:
                // CopyPasteService.Delete -> ModelTools.DeleteComponents -> PlacementOperation.Start
                // walks into SnaplinePlacementBehavior.BeginPlacement, which reads
                // Services.DesignPanel.Context.RootItem.
                var designPanel = new WinUIDesigner.Designer.DesignPanel();
                settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), designPanel));
                using var reader = XmlReader.Create(new StringReader(OneButtonXaml));
                var context = new XamlDesignContext(reader, settings);
                designPanel.Context = context;
                window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component;
                var buttonItem = context.Services.Component.GetDesignItem(canvas.Children[0]);
                context.Services.Selection.SetSelectedComponents(new[] { buttonItem });

                var service = new CopyPasteService();
                Assert.IsTrue(service.CanDelete(context));

                service.Delete(context);

                Assert.AreEqual(0, canvas.Children.Count);
            });
        }

        [Test]
        public void CopyCutPasteAreExplicitlyUnimplemented()
        {
            var service = new CopyPasteService();
            Assert.IsFalse(service.CanCopy(null));
            Assert.IsFalse(service.CanCut(null));
            Assert.IsFalse(service.CanPaste(null));
            // Copy/Cut/Paste no-op rather than throw - callers (Task 7's menu, if extended
            // later) can call them unconditionally without a null-service check.
            Assert.DoesNotThrow(() => service.Copy(null));
            Assert.DoesNotThrow(() => service.Cut(null));
            Assert.DoesNotThrow(() => service.Paste(null));
        }
    }
}
