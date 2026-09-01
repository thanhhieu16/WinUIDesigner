using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class TemplateTests
    {
        const string XamlWithControlTemplate =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"MyButton\">" +
            "<Button.Template>" +
            "<ControlTemplate TargetType=\"Button\">" +
            "<Border><ContentPresenter/></Border>" +
            "</ControlTemplate>" +
            "</Button.Template>" +
            "</Button>" +
            "</Grid>";

        static XamlDesignContext Load(string xaml)
        {
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                return new XamlDesignContext(reader, new XamlLoadSettings());
        }

        [Test]
        public void ControlTemplateIsAnOpaqueLeaf()
        {
            // Gap 6: Phase 1's parser returns immediately for any FrameworkTemplate subtype -
            // no children parsed, no properties recorded - so the DesignItem for the template
            // instance has an empty Properties collection and cannot be walked into. This test
            // documents that as an explicit, tested non-goal rather than an unverified assumption.
            WinUITestHost.Run(() => {
                var context = Load(XamlWithControlTemplate);
                var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                var buttonItem = context.Services.Component.GetDesignItem(button);

                var templateProperty = buttonItem.Properties["Template"];
                Assert.IsNotNull(templateProperty.ValueOnInstance, "the template itself should still construct successfully");

                // Empirically determined (Task 4): templateItem is always non-null here, not just
                // non-null-but-empty. RegisterXamlComponentRecursive walks buttonItem's properties
                // during the initial tree registration and recurses into the Template property's
                // XamlObject - the same XamlObject that XamlParser.ParseObject built via the early
                // FrameworkTemplate branch, which never calls AddProperty. So that XamlObject is
                // registered as its own XamlDesignItem (with zero properties), keyed by its Instance
                // (the real ControlTemplate). DesignItemProperty.Value looks up exactly that
                // registration (ComponentService.GetDesignItem(xamlObject.Instance)), so it succeeds
                // whenever the template itself constructed successfully - which the ValueOnInstance
                // assertion above already confirms for any well-formed template. Confirmed
                // deterministic across repeated runs.
                var templateItem = templateProperty.Value;
                Assert.IsNotNull(templateItem, "a well-formed template's DesignItem should always be found (Phase 1 gap 6)");
                CollectionAssert.IsEmpty(templateItem.AllSetProperties,
                    "a FrameworkTemplate's DesignItem should have no parsed properties (Phase 1 gap 6)");
            });
        }

        [Test]
        public void RegisteringAComponentWithNullInstanceThrowsADesignerException()
        {
            // Gap 5: the null-Instance path (TemplateHelper.GetFrameworkTemplate returning null)
            // could not be reliably triggered through the public parsing surface during planning -
            // every well-formed <ControlTemplate>/<DataTemplate> fragment reaching that code path
            // is structurally guaranteed to produce a real FrameworkTemplate.
            //
            // Task 4 finding (deviation from the brief's original test body - see the Task 4
            // report): the public IComponentService.RegisterComponentForDesigner(parent, null)
            // entry point does NOT exercise this guard. XamlComponentService's own
            // RegisterComponentForDesigner substitutes a null `component` argument with
            // `new NullExtensionPlaceholder()` - the {x:Null} stand-in for gap 4 - *before*
            // RegisterSite's null check ever runs, so RegisterSite never actually observes a null
            // there. Confirmed empirically: Assert.Throws<DesignerException> around that call
            // fails with "But was: null" (no exception at all).
            //
            // The only other caller of RegisterSite, RegisterXamlComponentRecursive, would pass a
            // real null when XamlObject.Instance is null - but XamlObject's constructor is
            // internal to WinUIDesigner.XamlDom ("For use by XamlParser only"), so no test outside
            // that assembly can fabricate a null-Instance XamlObject to drive that path, and real
            // XAML can't produce one either (previous paragraph). So, empirically, RegisterSite's
            // null guard has no reachable trigger anywhere in the current public calling contract
            // of either assembly - it is confirmed-unreachable defensive code as things stand.
            //
            // This test instead exercises the guard directly via reflection into the private
            // RegisterSite method (same assembly, not an XamlDom internal - does not weaken the
            // cross-assembly-visibility constraint verified in Task 4 Step 1), so gap 5's actual
            // guard logic (DesignerException, not NullReferenceException/ArgumentNullException) is
            // still verified precisely, without asserting a false claim about a public repro that
            // does not exist. If a future task finds a real repro for the null-Instance case
            // reaching RegisterSite through a public path, add it here as a second, XAML-driven
            // test and this reflection-based one can be retired.
            WinUITestHost.Run(() => {
                var context = Load("<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />");

                var componentService = context.Services.Component;
                var registerSite = componentService.GetType().GetMethod("RegisterSite",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.IsNotNull(registerSite, "XamlComponentService.RegisterSite should still exist as a private guard method");

                var invocationException = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                    registerSite.Invoke(componentService, new object[] { null, context.RootItem }));

                Assert.IsInstanceOf<DesignerException>(invocationException.InnerException,
                    "the guard should throw a clear DesignerException, not an unrelated NullReferenceException/ArgumentNullException");
            });
        }
    }
}
