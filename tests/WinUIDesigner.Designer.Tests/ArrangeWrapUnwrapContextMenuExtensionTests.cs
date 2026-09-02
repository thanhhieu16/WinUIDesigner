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
    public class ArrangeWrapUnwrapContextMenuExtensionTests
    {
        static (DesignSurface surface, DesignItem canvasItem, DesignItem aItem, DesignItem bItem) LoadTwoButtons(Microsoft.UI.Xaml.Window window)
        {
            const string xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" Canvas.Left=\"0\" Canvas.Top=\"0\" />" +
                "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" Canvas.Left=\"50\" Canvas.Top=\"30\" />" +
                "</Canvas>";
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
            var aItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[0]);
            var bItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[1]);
            return (surface, surface.DesignContext.RootItem, aItem, bItem);
        }

        [Test]
        public void ArrangeItemsExtensionOnlyAttachesWithTwoOrMoreSelected()
        {
            // DesignItem.Extensions (public IEnumerable<Extension>, DesignItem.cs:151) is this
            // port's real accessor for an item's currently-attached extensions - same one Task
            // 7's tests use.
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = LoadTwoButtons(window);

                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });
                Assert.IsFalse(aItem.Extensions.OfType<ArrangeItemsContextMenuExtension>().Any());

                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem, bItem });
                Assert.IsTrue(aItem.Extensions.OfType<ArrangeItemsContextMenuExtension>().Any());
            });
        }

        [Test]
        public void WrapItemExtensionOnlyAttachesWithExactlyOneSelected()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = LoadTwoButtons(window);

                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem, bItem });
                Assert.IsFalse(aItem.Extensions.OfType<WrapItemContextMenuExtension>().Any());

                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });
                Assert.IsTrue(aItem.Extensions.OfType<WrapItemContextMenuExtension>().Any());
            });
        }

        [Test]
        public void UnwrapItemExtensionAttachesToACanvasContainer()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = LoadTwoButtons(window);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { canvasItem });

                Assert.IsTrue(canvasItem.Extensions.OfType<UnwrapItemContextMenuExtension>().Any());
            });
        }
    }
}
