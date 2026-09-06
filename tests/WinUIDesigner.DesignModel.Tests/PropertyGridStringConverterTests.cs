using System.IO;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using WinUIDesigner.DesignModel.PropertyGrid;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    /// <summary>
    /// Guards the PropertyGrid ToString() hazard Phase A's original
    /// <c>XamlValueConverter.CanConvertTo => false</c> existed to avoid, and Task 3's write-side
    /// wiring reopened: <c>CanConvertTo</c> answers per-instance once a formatter exists, so a
    /// declared type that sometimes has a text form (e.g. <c>Brush</c>) and sometimes doesn't
    /// (e.g. a <c>LinearGradientBrush</c> instance of that same declared type) needs
    /// <see cref="PropertyNode"/> to ask about the CURRENT value, not cache a type-level answer at
    /// load time.
    /// </summary>
    [TestFixture]
    public class PropertyGridStringConverterTests
    {
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Name=\"Target\" />";

        static DesignItem LoadGrid()
        {
            using var reader = XmlReader.Create(new StringReader(GridXaml));
            var context = new XamlDesignContext(reader, new XamlLoadSettings());
            return context.Services.Component.GetDesignItem(context.RootItem.Component);
        }

        [SetUp]
        public void RegisterFallbackEditor()
        {
            // PropertyNode.Load() calls EditorManager.CreateEditor - these tests don't exercise
            // editor SELECTION logic, they just need it to not throw. Re-registering every test is
            // idempotent, matching PropertyNodeTests' own [SetUp].
            EditorManager.SetDefaultTextBoxEditorFactory(p => new TextBox());
            EditorManager.SetDefaultComboBoxEditorFactory(p => new ComboBox());
        }

        [Test]
        public void ALinearGradientBrushDisablesTheEditorAndDoesNotLeakItsTypeNameIntoValueString()
        {
            WinUITestHost.Run(() => {
                var target = LoadGrid();
                var backgroundProperty = target.Properties.GetProperty("Background");

                // Set the LIVE component property directly, bypassing DesignItemProperty.SetValue
                // (and the XamlModelProperty/XamlComponentService.RegisterXamlComponentRecursive
                // registration it does) entirely. This is what happens whenever a value reaches
                // the control some way other than an explicit designer commit - a Style default, a
                // ControlTemplate, an inherited value - so DesignerValue must still be correct for
                // it: the underlying XamlProperty.PropertyValue stays unset (null), which is what
                // makes ValueItem null and DesignerValue fall back to the live CLR value (see
                // XamlProperty.DesignerValue/Value, XamlModelProperty.Value). Going through
                // SetValue instead would register the LinearGradientBrush as its own DesignItem,
                // which makes ValueItem non-null and masks the hazard through a DIFFERENT gate -
                // not what this test is pinning.
                ((Microsoft.UI.Xaml.Controls.Grid)target.Component).Background = new LinearGradientBrush();

                var node = new PropertyNode(new[] { backgroundProperty }, null);

                Assert.IsNull(node.ValueItem, "sanity: this value must NOT be DOM-registered, or it would mask the hazard through a different gate");
                Assert.IsFalse(node.IsEnabled,
                    "A LinearGradientBrush has no text form - the editor must stay read-only, " +
                    "matching the pre-Task-3 (LinearGradientBrush) label instead of an editable box");
                StringAssert.DoesNotContain("Microsoft.UI.Xaml.Media", node.ValueString ?? string.Empty,
                    "The value must not fall back to ToString() and leak the CLR type name into the grid");
            });
        }

        [Test]
        public void AThicknessPropertyIsEditableInTheGrid()
        {
            WinUITestHost.Run(() => {
                using var reader = XmlReader.Create(new StringReader(
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />"));
                var context = new XamlDesignContext(reader, new XamlLoadSettings());
                var margin = context.RootItem.Properties["Margin"];

                var node = new PropertyNode();
                node.Load(new[] { margin });
                Assert.IsTrue(node.IsEnabled, "The Thickness text box stayed disabled");

                node.ValueString = "1,2,3,4";
                var written = (Microsoft.UI.Xaml.Thickness)margin.ValueOnInstance;
                Assert.AreEqual(1.0, written.Left);
                Assert.AreEqual(4.0, written.Bottom);
                Assert.AreEqual("1,2,3,4", node.ValueString);
            });
        }
    }
}
