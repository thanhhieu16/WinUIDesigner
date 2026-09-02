using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    /// <summary>
    /// Records the VirtualKeyModifiers value each IPlacementBehavior entry point is handed, so a
    /// test can prove the modifier really travels from the originating pointer event down into
    /// the behavior instead of being read ambiently (which WinUI cannot do). RelativePanel is
    /// the container type on purpose: nothing else in this port's Phase 4b scope registers a
    /// placement behavior for it (Canvas/Grid/StackPanel only), so this extension is the one and
    /// only IPlacementBehavior on that item and no later task can collide with it.
    /// </summary>
    [ExtensionFor(typeof(RelativePanel), OverrideExtension = typeof(DefaultPlacementBehavior))]
    public sealed class RecordingModifierPlacementBehavior : DefaultPlacementBehavior
    {
        public VirtualKeyModifiers? LastBeforeSetPositionModifiers { get; private set; }
        public VirtualKeyModifiers? LastPlacePointModifiers { get; private set; }

        public override void BeforeSetPosition(PlacementOperation operation, VirtualKeyModifiers modifiers)
        {
            LastBeforeSetPositionModifiers = modifiers;
            base.BeforeSetPosition(operation, modifiers);
        }

        public override Point PlacePoint(Point point, VirtualKeyModifiers modifiers)
        {
            LastPlacePointModifiers = modifiers;
            return base.PlacePoint(point, modifiers);
        }
    }

    [TestFixture]
    public class PlacementModifierThreadingTests
    {
        const string PanelXaml =
            "<RelativePanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</RelativePanel>";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window, out DesignItem panelItem, out DesignItem buttonItem)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            var settings = new XamlLoadSettings();
            // The test assembly is not one of DesignSurface.LoadDesigner's own DesignerAssemblies,
            // so its [ExtensionFor] types are only discovered when it is added explicitly.
            settings.DesignerAssemblies.Add(typeof(PlacementModifierThreadingTests).Assembly);
            using (var reader = XmlReader.Create(new StringReader(PanelXaml)))
                surface.LoadDesigner(reader, settings);
            surface.UpdateLayout();

            panelItem = surface.DesignContext.RootItem;
            var button = (Button)((RelativePanel)panelItem.Component).Children[0];
            buttonItem = surface.DesignContext.Services.Component.GetDesignItem(button);
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void DesignPanelExposesTheRasterAndSnaplineOptionsWithTheReferenceDefaults()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out _, out _);
                Assert.IsTrue(surface.DesignPanel.UseSnaplinePlacement, "UseSnaplinePlacement defaults to true in the reference");
                Assert.IsFalse(surface.DesignPanel.UseRasterPlacement, "UseRasterPlacement defaults to false in the reference");
                Assert.AreEqual(5, surface.DesignPanel.RasterWidth, "RasterWidth defaults to 5 in the reference");
            });
        }

        [Test]
        public void OptionServiceIsRegisteredOnALoadedDesignerWithRoundingOff()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out _, out _);
                var options = surface.DesignContext.Services.GetService<OptionService>();
                Assert.IsNotNull(options, "DesignSurface.LoadDesigner must register OptionService");
                Assert.IsFalse(options.SnaplinePlacementRoundValues, "SnaplinePlacementRoundValues defaults to false in the reference");
                Assert.IsTrue(options.GrayOutDesignSurfaceExceptParentContainerWhenDragging,
                    "GrayOutDesignSurfaceExceptParentContainerWhenDragging defaults to true in the reference");
            });
        }

        [Test]
        public void PlacePointAndBeforeSetPositionReceiveTheModifiersTheyArePassed()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var panelItem, out var buttonItem);
                var behavior = panelItem.Extensions.OfType<RecordingModifierPlacementBehavior>().Single();
                Assert.AreSame(behavior, panelItem.GetBehavior<IPlacementBehavior>(),
                    "OverrideExtension must make the recording behavior win over DefaultPlacementBehavior");

                var operation = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                try {
                    operation.CurrentContainerBehavior.PlacePoint(new Point(1, 2), VirtualKeyModifiers.Control);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.Shift);
                } finally {
                    operation.Abort();
                }

                Assert.AreEqual(VirtualKeyModifiers.Control, behavior.LastPlacePointModifiers);
                Assert.AreEqual(VirtualKeyModifiers.Shift, behavior.LastBeforeSetPositionModifiers);
            });
        }

        [Test]
        public void ResizingWithControlHeldThreadsThatModifierAllTheWayIntoBeforeSetPosition()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var panelItem, out var buttonItem);
                var behavior = panelItem.Extensions.OfType<RecordingModifierPlacementBehavior>().Single();
                var extension = buttonItem.Extensions.OfType<ResizeThumbExtension>().Single();
                var drag = extension.DragListeners[PlacementAlignment.BottomRight];

                drag.SimulateDown(new Point(0, 0));
                drag.SimulateMove(new Point(20, 15), VirtualKeyModifiers.Control);
                Assert.AreEqual(VirtualKeyModifiers.Control, behavior.LastBeforeSetPositionModifiers,
                    "ResizeThumbExtension must pass the drag's live KeyModifiers, not a default");

                drag.SimulateMove(new Point(25, 20), VirtualKeyModifiers.None);
                Assert.AreEqual(VirtualKeyModifiers.None, behavior.LastBeforeSetPositionModifiers,
                    "releasing Ctrl mid-drag must be visible to the behavior on the very next tick");

                drag.SimulateUp(new Point(25, 20));
            });
        }
    }
}
