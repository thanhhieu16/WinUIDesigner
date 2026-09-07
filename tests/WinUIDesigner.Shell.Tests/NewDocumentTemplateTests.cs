using System.IO;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class NewDocumentTemplateTests
    {
        [Test]
        public void TheBlankTemplateLoadsAsACanvasOf640By480()
        {
            WinUITestHost.Run(() =>
            {
                var surface = new DesignSurface();
                using (var reader = XmlReader.Create(new StringReader(NewDocumentTemplate.Xaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());

                // The reference's NewFileTemplate.xaml is rooted at <Window>, which cannot be a
                // design root here: Microsoft.UI.Xaml.Window is not a UIElement, and
                // DesignSurface assigns DesignPanel.Content = RootItem.View. Spec Decision 15.
                var canvas = surface.DesignContext.RootItem.Component as Canvas;
                Assert.That(canvas, Is.Not.Null, "the template's root must be a Canvas");
                Assert.That(canvas.Width, Is.EqualTo(640.0));
                Assert.That(canvas.Height, Is.EqualTo(480.0));

                surface.UnloadDesigner();
            });
        }
    }
}
