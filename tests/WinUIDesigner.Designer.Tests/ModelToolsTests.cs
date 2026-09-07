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

        const string NestedGridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Grid x:Name=\"Target\" />" +
            "</Grid>";

        const string SizelessButtonXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\" />" +
            "</Grid>";

        const string GroupTransformXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\">" +
            "<Button.RenderTransform><TransformGroup>" +
            "<SkewTransform AngleX=\"10\" /><RotateTransform Angle=\"20\" />" +
            "</TransformGroup></Button.RenderTransform>" +
            "</Button>" +
            "</Grid>";

        // Width-only Grid (no Height) - empirically probed (see
        // AGetDefaultSizeOverrideAppliesIndependentlyPerAxis) to measure DesiredSize (40, 0): the
        // explicit Width comes out above the method's >5 threshold while the unset Height comes
        // out at the Grid's natural (childless) 0 - a genuine one-axis-small/one-axis-large split.
        // A same-shaped Button (Width=40, no Height) measures (40, 13) instead - both axes above 5
        // - so it can't exercise the two independently-checked branches in GetDefaultSize; only
        // the childless Grid does.
        const string WidthOnlyGridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Grid x:Name=\"Target\" Width=\"40\" />" +
            "</Grid>";

        static (XamlDesignContext context, Microsoft.UI.Xaml.Controls.Button button, WinUIDesigner.DesignModel.DesignItem buttonItem) Load(Microsoft.UI.Xaml.Window window, string xaml = null)
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
            using var reader = XmlReader.Create(new StringReader(xaml ?? ButtonXaml));
            var context = new XamlDesignContext(reader, settings);
            window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
            ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
            var button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component).Children[0];
            var buttonItem = context.Services.Component.GetDesignItem(button);
            return (context, button, buttonItem);
        }

        static WinUIDesigner.DesignModel.DesignItem LoadTarget(Microsoft.UI.Xaml.Window window, string xaml)
        {
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new WinUIDesigner.Designer.Services.DefaultSelectionService()));
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), new WinUIDesigner.Designer.DesignPanel()));
            using var reader = XmlReader.Create(new StringReader(xaml));
            var context = new XamlDesignContext(reader, settings);
            window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
            ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
            var target = ((Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component).Children[0];
            return context.Services.Component.GetDesignItem(target);
        }

        // Metadata's registries are process-wide static state (matches the reference's own
        // design - see spec Decision 6). AddDefaultSize is being registered here against real,
        // widely-shared WinUI types (Grid/Panel/Control) that other tests elsewhere in this same
        // process may also create - leaving a registration behind would make GetDefaultSize's
        // result for THOSE tests depend on whether this file happened to run first. Every test
        // below that calls AddDefaultSize resets the registry in a finally block; there is no
        // Metadata.RemoveDefaultSize (the reference has none either), so resetting means
        // clearing the whole private dictionary via reflection (cannot replace a readonly field
        // after type initialization, so we clear it instead).
        static void ResetDefaultSizeRegistry()
        {
            var field = typeof(WinUIDesigner.DesignModel.Metadata)
                .GetField("defaultSizes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var dict = (System.Collections.Generic.Dictionary<System.Type, Windows.Foundation.Size>)field.GetValue(null);
            dict.Clear();
        }

        [Test]
        public void AnExactTypeRegistryEntryShortCircuitsMeasureEntirely()
        {
            WinUITestHost.RunWithWindow(window => {
                var item = LoadTarget(window, NestedGridXaml);
                WinUIDesigner.DesignModel.Metadata.AddDefaultSize(typeof(Microsoft.UI.Xaml.Controls.Grid), new Windows.Foundation.Size(300, 200));
                try {
                    var size = WinUIDesigner.Designer.ModelTools.GetDefaultSize(item);
                    Assert.AreEqual(300, size.Width);
                    Assert.AreEqual(200, size.Height);
                } finally {
                    ResetDefaultSizeRegistry();
                }
            });
        }

        [Test]
        public void ABaseTypeRegistryEntryOverridesASmallMeasuredDimension()
        {
            WinUITestHost.RunWithWindow(window => {
                var item = LoadTarget(window, NestedGridXaml); // measures (0,0) - see Empirically-verified facts
                WinUIDesigner.DesignModel.Metadata.AddDefaultSize(typeof(Microsoft.UI.Xaml.Controls.Panel), new Windows.Foundation.Size(150, 120));
                try {
                    var size = WinUIDesigner.Designer.ModelTools.GetDefaultSize(item);
                    Assert.AreEqual(150, size.Width);
                    Assert.AreEqual(120, size.Height);
                } finally {
                    ResetDefaultSizeRegistry();
                }
            });
        }

        [Test]
        public void ARegistryEntryIsIgnoredWhenTheMeasuredDimensionIsAlreadyLargerThanFive()
        {
            WinUITestHost.RunWithWindow(window => {
                var item = LoadTarget(window, SizelessButtonXaml); // measures (20,13) - see Empirically-verified facts
                WinUIDesigner.DesignModel.Metadata.AddDefaultSize(typeof(Microsoft.UI.Xaml.Controls.Control), new Windows.Foundation.Size(999, 888));
                try {
                    var size = WinUIDesigner.Designer.ModelTools.GetDefaultSize(item);
                    Assert.AreEqual(20, size.Width);
                    Assert.AreEqual(13, size.Height);
                } finally {
                    ResetDefaultSizeRegistry();
                }
            });
        }

        // Both prior registry tests above are uniform - either both axes measure small (0,0) and
        // both get overridden, or both measure above 5 (20,13) and neither does - so they can't
        // tell GetDefaultSize's two independent `if (!(s.Width > 5) ...)` / `if (!(s.Height > 5)
        // ...)` checks apart from one combined check. WidthOnlyGridXaml measures (40,0) (see its
        // own comment) - Width already above 5 stays put, Height at 0 gets overridden - so this
        // is the one test where getting either axis's condition wrong would be visible.
        [Test]
        public void AGetDefaultSizeOverrideAppliesIndependentlyPerAxis()
        {
            WinUITestHost.RunWithWindow(window => {
                var item = LoadTarget(window, WidthOnlyGridXaml); // measures (40,0) - see WidthOnlyGridXaml's comment
                WinUIDesigner.DesignModel.Metadata.AddDefaultSize(typeof(Microsoft.UI.Xaml.Controls.Panel), new Windows.Foundation.Size(500, 500));
                try {
                    var size = WinUIDesigner.Designer.ModelTools.GetDefaultSize(item);
                    Assert.AreEqual(40, size.Width, "the already-large measured Width must NOT be replaced by the registered value");
                    Assert.AreEqual(500, size.Height, "the small measured Height must be overridden by the registered value");
                } finally {
                    ResetDefaultSizeRegistry();
                }
            });
        }

        [Test]
        public void WithNoRegistrationAtAllTheExistingMeasureBehaviorIsUnchanged()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, button, buttonItem) = Load(window); // existing fixture: Width=40, Height=20 explicit
                var size = WinUIDesigner.Designer.ModelTools.GetDefaultSize(buttonItem);
                Assert.AreEqual(40, size.Width);
                Assert.AreEqual(20, size.Height);
            });
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

                // Finding 1: the RotateTransform-into-TransformGroup branch above registers the
                // new child through RegisterComponentForDesigner, which goes through
                // XamlDocument.CreateObject - CreatePropertyValue with forProperty == null. Before
                // this phase's fix that meant XamlInstanceWriter never ran on this path, so the
                // child saved as an empty <RotateTransform /> and the angle was gone on reload.
                // The assertions above only ever look at the live object tree, which is correct
                // either way (the model write happens whether or not the child gets its own
                // DesignItem) - a save-and-reparse is the only way this test can see the bug.
                var text = new StringWriter();
                var xmlSettings = new XmlWriterSettings();
                xmlSettings.OmitXmlDeclaration = true;
                using (var writer = XmlWriter.Create(text, xmlSettings))
                    context.Save(writer);
                var saved = text.ToString();

                var reloaded = WinUIDesigner.XamlDom.XamlParser.Parse(new StringReader(saved));
                var reloadedButton = (Microsoft.UI.Xaml.Controls.Button)
                    ((Microsoft.UI.Xaml.Controls.Grid)reloaded.RootInstance).Children[0];
                var reloadedGroup = reloadedButton.RenderTransform as Microsoft.UI.Xaml.Media.TransformGroup;
                Assert.IsNotNull(reloadedGroup,
                    "the TransformGroup itself did not survive the save; saved as: " + saved);
                var reloadedRotate = reloadedGroup.Children.OfType<Microsoft.UI.Xaml.Media.RotateTransform>().FirstOrDefault();
                Assert.IsNotNull(reloadedRotate,
                    "the RotateTransform child did not survive the save; saved as: " + saved);
                Assert.AreEqual(20, reloadedRotate.Angle, 0.5, "saved as: " + saved);
            });
        }

        // A third application, when the RenderTransform is already a TransformGroup that already
        // holds a RotateTransform, is the one branch of ApplyTransform no test reached: it takes
        // the group's existing rotate back out through
        // Services.Component.GetDesignItem(existingRotate) before adding the new one. Nothing
        // guarantees that lookup finds a DesignItem - GetDesignItem returns null for any instance
        // that was never registered - so this pins the sequence end to end.
        [Test]
        public void AThirdTransformApplicationReplacesTheRotateAlreadyInTheGroup()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, button, buttonItem) = Load(window);
                WinUIDesigner.Designer.ModelTools.ApplyTransform(buttonItem, new Microsoft.UI.Xaml.Media.SkewTransform { AngleX = 10 }, false);
                WinUIDesigner.Designer.ModelTools.ApplyTransform(buttonItem, new Microsoft.UI.Xaml.Media.RotateTransform { Angle = 20 }, false);

                Assert.DoesNotThrow(() =>
                    WinUIDesigner.Designer.ModelTools.ApplyTransform(buttonItem, new Microsoft.UI.Xaml.Media.RotateTransform { Angle = 35 }, false));

                var group = button.RenderTransform as Microsoft.UI.Xaml.Media.TransformGroup;
                Assert.IsNotNull(group, "the TransformGroup should still be the RenderTransform");
                Assert.AreEqual(1, group.Children.OfType<Microsoft.UI.Xaml.Media.RotateTransform>().Count(),
                    "the old rotate should have been replaced, not accumulated");
                Assert.AreEqual(35, group.Children.OfType<Microsoft.UI.Xaml.Media.RotateTransform>().First().Angle, 0.5);

                var text = new StringWriter();
                var xmlSettings = new XmlWriterSettings();
                xmlSettings.OmitXmlDeclaration = true;
                using (var writer = XmlWriter.Create(text, xmlSettings))
                    context.Save(writer);
                var saved = text.ToString();

                var reloaded = WinUIDesigner.XamlDom.XamlParser.Parse(new StringReader(saved));
                var reloadedButton = (Microsoft.UI.Xaml.Controls.Button)
                    ((Microsoft.UI.Xaml.Controls.Grid)reloaded.RootInstance).Children[0];
                var reloadedGroup = reloadedButton.RenderTransform as Microsoft.UI.Xaml.Media.TransformGroup;
                Assert.IsNotNull(reloadedGroup, "saved as: " + saved);
                var rotates = reloadedGroup.Children.OfType<Microsoft.UI.Xaml.Media.RotateTransform>().ToList();
                Assert.AreEqual(1, rotates.Count, "saved as: " + saved);
                Assert.AreEqual(35, rotates[0].Angle, 0.5, "saved as: " + saved);
            });
        }


        // The same branch reached the other way: a document that already carries a
        // <TransformGroup> with a <RotateTransform> in it, opened from disk rather than built by
        // ApplyTransform. This is the path a user actually meets - the group's children come from
        // the parse, not from RegisterComponentForDesigner - so it is the one that decides whether
        // GetDesignItem(existingRotate) can be null in practice.
        [Test]
        public void ARotateAppliedToAGroupLoadedFromXamlReplacesTheExistingRotate()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, button, buttonItem) = Load(window, GroupTransformXaml);

                Assert.DoesNotThrow(() =>
                    WinUIDesigner.Designer.ModelTools.ApplyTransform(buttonItem, new Microsoft.UI.Xaml.Media.RotateTransform { Angle = 35 }, false));

                var group = button.RenderTransform as Microsoft.UI.Xaml.Media.TransformGroup;
                Assert.IsNotNull(group, "the parsed TransformGroup should still be the RenderTransform");
                var rotates = group.Children.OfType<Microsoft.UI.Xaml.Media.RotateTransform>().ToList();
                Assert.AreEqual(1, rotates.Count,
                    "the parsed rotate should have been replaced, not accumulated alongside the new one");
                Assert.AreEqual(35, rotates[0].Angle, 0.5);
                Assert.AreEqual(1, group.Children.OfType<Microsoft.UI.Xaml.Media.SkewTransform>().Count(),
                    "the sibling skew should be untouched");
            });
        }

    }
}
