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

            // Fix round 1 (test-quality gap): exposes the real, un-duplicated resizeBehavior
            // field so a test can assert *why* ThumbVisible ended up false (no IPlacementBehavior/
            // IRootPlacementBehavior found) rather than only asserting the DP's end state.
            public bool HasResizeBehavior => resizeBehavior != null;
        }

        const string LineInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Line x:Name=\"L\" X1=\"10\" Y1=\"10\" X2=\"50\" Y2=\"50\" Stroke=\"Black\" StrokeThickness=\"2\" />" +
            "</Canvas>";

        // No parent container at all - the Line here is the ROOT of the document.
        const string RootLineXaml =
            "<Line xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Name=\"L\" " +
            "X1=\"10\" Y1=\"10\" X2=\"50\" Y2=\"50\" Stroke=\"Black\" StrokeThickness=\"2\" />";

        static XamlLoadSettings NewLoadSettings()
        {
            // TestPointEditExtension is defined in this test assembly, not
            // WinUIDesigner.Designer.dll/WinUIDesigner.DesignModel.dll (DesignSurface.LoadDesigner's
            // own default DesignerAssemblies) - ExtensionManager.RegisterAssembly only scans
            // assemblies in that list for [ExtensionFor], so without this the extension is silently
            // never discovered (lineItem.Extensions.OfType<TestPointEditExtension>() empty, .Single()
            // throws). Same fix as the established precedent for a test-local [ExtensionFor] class:
            // SelectionAndAdornerIntegrationTests.cs:98.
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(PointEditExtensionBaseTests).Assembly);
            return settings;
        }

        [Test]
        public void ThumbIsVisibleWhenContainerHasAPlacementBehavior()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(LineInCanvasXaml)))
                    surface.LoadDesigner(reader, NewLoadSettings());
                surface.UpdateLayout();

                var line = (Microsoft.UI.Xaml.Shapes.Line)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
                var lineItem = surface.DesignContext.Services.Component.GetDesignItem(line);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { lineItem }, SelectionTypes.Primary);
                surface.UpdateLayout();
                var extension = lineItem.Extensions.OfType<TestPointEditExtension>().Single();

                // Fix round 1 (test-quality gap): PointEditExtensionBase.OnInitialized deliberately
                // never calls UpdateAdornerVisibility itself (subclasses call it after populating
                // Thumbs - this test's own TestPointEditExtension doesn't either), and
                // DesignerThumb.ThumbVisibleProperty's DP default is already `true` - so without
                // firing a real property change here, this assertion would pass identically even if
                // CanPlace/UpdateAdornerVisibility were deleted outright. Mutating a real model
                // property exercises the actual production wiring end-to-end: DesignItem.
                // PropertyChanged -> PointEditExtensionBase.OnPropertyChanged -> UpdateAdornerVisibility
                // -> resizeBehavior.CanPlace(..., PlacementType.MovePoint, PlacementAlignment.BottomRight).
                Assert.IsTrue(extension.HasResizeBehavior, "Canvas is a Panel - CanvasPlacementSupport/DefaultPlacementBehavior should be found");
                lineItem.Properties["X1"].SetValue(20.0);

                Assert.IsTrue(extension.Thumb.ThumbVisible, "Canvas has CanvasPlacementSupport (CanPlace always returns true) - the thumb should be visible");
            });
        }

        [Test]
        public void ThumbIsNotVisibleWhenItemHasNoPlacementBehavior()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                // The Line here is the document ROOT - it has no parent container. Per
                // PlacementOperation.GetPlacementBehavior (PlacementOperation.cs:231-233), an item
                // with no parent falls back to looking up IRootPlacementBehavior on itself; nothing
                // in this codebase implements IRootPlacementBehavior (verified: only the interface
                // declaration exists at PlacementBehavior.cs:30 - grepping the whole src tree for
                // "IRootPlacementBehavior" finds no implementing class, and grepping for
                // "AddBehavior(typeof(IPlacementBehavior)" finds exactly one registration site,
                // DefaultPlacementBehavior.cs:43, which only ever registers IPlacementBehavior - not
                // IRootPlacementBehavior - for Panel/Control/Border/Viewbox containers), so
                // GetBehavior<IRootPlacementBehavior>() returns null and resizeBehavior stays null:
                // a real, already-present "no placement behavior" case, not an artificial stub.
                using (var reader = XmlReader.Create(new StringReader(RootLineXaml)))
                    surface.LoadDesigner(reader, NewLoadSettings());
                surface.UpdateLayout();

                var lineItem = surface.DesignContext.RootItem;
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { lineItem }, SelectionTypes.Primary);
                surface.UpdateLayout();
                var extension = lineItem.Extensions.OfType<TestPointEditExtension>().Single();

                // Confirms empirically *why* ThumbVisible should end up false, rather than assuming
                // it: resizeBehavior (computed once in OnInitialized from
                // PlacementOperation.GetPlacementBehavior) is genuinely null for a parentless root
                // item, not merely coincidentally-false CanPlace.
                Assert.IsFalse(extension.HasResizeBehavior, "a root Line has no IPlacementBehavior/IRootPlacementBehavior registered anywhere");

                lineItem.Properties["X1"].SetValue(20.0);

                Assert.IsFalse(extension.Thumb.ThumbVisible, "no placement behavior was found for the root item - the thumb should be hidden");
            });
        }
    }
}
