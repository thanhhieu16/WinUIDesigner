using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls.Thumbs;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PointEditExtensionBaseTests
    {
        // Test-only concrete subclass: exposes UpdateAdornerVisibility/Thumbs so this task's own
        // base-class behavior (visibility gating per spec decision 13) can be tested without a
        // real *HandlerExtension (Tasks 7/9/11 test their own real behavior separately).
        [ExtensionServer(typeof(WinUIDesigner.DesignModel.Extensions.OnlyOneItemSelectedExtensionServer))]
        [ExtensionFor(typeof(Microsoft.UI.Xaml.Shapes.Line))]
        sealed class TestPointEditExtension : PointEditExtensionBase
        {
            public readonly DesignerThumb Thumb = new DesignerThumb();
            protected override IEnumerable<DesignerThumb> Thumbs => new[] { Thumb };
        }

        const string LineInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Line x:Name=\"L\" X1=\"10\" Y1=\"10\" X2=\"50\" Y2=\"50\" Stroke=\"Black\" StrokeThickness=\"2\" />" +
            "</Canvas>";

        [Test]
        public void ThumbIsVisibleWhenContainerHasAPlacementBehavior()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                // TestPointEditExtension is defined in this test assembly, not
                // WinUIDesigner.Designer.dll/WinUIDesigner.DesignModel.dll (DesignSurface.LoadDesigner's
                // own default DesignerAssemblies) - ExtensionManager.RegisterAssembly only scans
                // assemblies in that list for [ExtensionFor], so without this the extension is silently
                // never discovered (lineItem.Extensions.OfType<TestPointEditExtension>() empty, .Single()
                // throws). Same fix as the established precedent for a test-local [ExtensionFor] class:
                // SelectionAndAdornerIntegrationTests.cs:98.
                var loadSettings = new XamlLoadSettings();
                loadSettings.DesignerAssemblies.Add(typeof(PointEditExtensionBaseTests).Assembly);
                using (var reader = XmlReader.Create(new StringReader(LineInCanvasXaml)))
                    surface.LoadDesigner(reader, loadSettings);
                surface.UpdateLayout();

                var line = (Microsoft.UI.Xaml.Shapes.Line)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
                var lineItem = surface.DesignContext.Services.Component.GetDesignItem(line);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { lineItem }, SelectionTypes.Primary);
                surface.UpdateLayout();
                var extension = lineItem.Extensions.OfType<TestPointEditExtension>().Single();

                Assert.IsTrue(extension.Thumb.ThumbVisible, "Canvas has CanvasPlacementSupport - the thumb should be visible");
            });
        }
    }
}
