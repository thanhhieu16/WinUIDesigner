using System;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class ToolboxTests
    {
        [Test]
        public void EveryListedControlIsAConcreteUIElementWithAParameterlessConstructor()
        {
            WinUITestHost.Run(() =>
            {
                var nodes = Toolbox.Instance.AssemblyNodes;
                Assert.That(nodes.Count, Is.GreaterThanOrEqualTo(1));

                foreach (var node in nodes)
                {
                    Assert.That(node.Controls.Count, Is.GreaterThan(0), node.Name + " listed no controls");
                    foreach (var control in node.Controls)
                    {
                        var t = control.Type;
                        Assert.That(t.IsAbstract, Is.False, t.FullName + " is abstract");
                        Assert.That(t.IsGenericTypeDefinition, Is.False, t.FullName + " is a generic definition");
                        Assert.That(typeof(UIElement).IsAssignableFrom(t), Is.True, t.FullName + " is not a UIElement");
                        Assert.That(
                            t.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null),
                            Is.Not.Null,
                            t.FullName + " has no public parameterless constructor");
                    }
                }
            });
        }

        [Test]
        public void EveryListedControlActuallyActivates()
        {
            WinUITestHost.Run(() =>
            {
                foreach (var node in Toolbox.Instance.AssemblyNodes)
                    foreach (var control in node.Controls)
                        Assert.DoesNotThrow(
                            () => Activator.CreateInstance(control.Type),
                            control.Type.FullName + " is listed but does not activate");
            });
        }

        [Test]
        public void PivotHeaderItemIsExcludedBecauseItDoesNotActivate()
        {
            WinUITestHost.Run(() =>
            {
                // Measured on this SDK: of the 160 instantiable UIElement subclasses in
                // Microsoft.WinUI, exactly one throws TargetInvocationException from
                // Activator.CreateInstance. The filter exists for it; this test names it so a
                // future SDK that fixes it shows up as a failure rather than silently widening
                // the list. Spec Decision 11.
                Assert.That(Toolbox.IsControl(typeof(PivotHeaderItem)), Is.True,
                    "PivotHeaderItem still passes IsControl - only activation excludes it");
                Assert.That(Toolbox.CanActivate(typeof(PivotHeaderItem)), Is.False,
                    "PivotHeaderItem now activates; if the SDK fixed it, drop this test");

                var listed = Toolbox.Instance.AssemblyNodes
                    .SelectMany(n => n.Controls)
                    .Any(c => c.Type == typeof(PivotHeaderItem));
                Assert.That(listed, Is.False, "PivotHeaderItem must not reach the toolbox");
            });
        }

        [Test]
        public void CommonControlsAreListed()
        {
            WinUITestHost.Run(() =>
            {
                var listed = Toolbox.Instance.AssemblyNodes
                    .SelectMany(n => n.Controls)
                    .Select(c => c.Type)
                    .ToList();

                foreach (var expected in new[] {
                    typeof(Button), typeof(TextBlock), typeof(TextBox), typeof(CheckBox),
                    typeof(ComboBox), typeof(Canvas), typeof(Grid), typeof(StackPanel),
                    typeof(Border), typeof(Microsoft.UI.Xaml.Shapes.Rectangle),
                    typeof(Microsoft.UI.Xaml.Shapes.Ellipse), typeof(Microsoft.UI.Xaml.Shapes.Polyline)
                })
                    Assert.That(listed, Contains.Item(expected), expected.Name + " is missing from the toolbox");
            });
        }

        [Test]
        public void TheWinUIAssemblyResolvesByItsRealName()
        {
            WinUITestHost.Run(() =>
            {
                // Measured: the assembly holding Microsoft.UI.Xaml.Controls.Button is named
                // "Microsoft.WinUI", not "Microsoft.UI.Xaml". LoadAssembly compares against
                // Assembly.GetName().Name, so this is the name a XAML namespace declaration
                // must match.
                Assert.That(typeof(Button).Assembly.GetName().Name, Is.EqualTo("Microsoft.WinUI"));
                Assert.That(ShellTypeFinder.Instance.LoadAssembly("Microsoft.WinUI"), Is.SameAs(typeof(Button).Assembly));
            });
        }

        [Test]
        public void AnUnknownAssemblyNameReturnsNullRatherThanThrowing()
        {
            WinUITestHost.Run(() =>
            {
                Assert.That(ShellTypeFinder.Instance.LoadAssembly("No.Such.Assembly"), Is.Null);
            });
        }

        [Test]
        public void CloneReturnsTheSingletonSoTheFinderSurvivesEveryLoad()
        {
            WinUITestHost.Run(() =>
            {
                Assert.That(ShellTypeFinder.Instance.Clone(), Is.SameAs(ShellTypeFinder.Instance));
            });
        }
    }
}
