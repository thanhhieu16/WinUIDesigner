using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    /// <summary>
    /// Characterization tests for the platform constraint that shaped Phase 4g's view layer
    /// (spec Decisions 4 and 5). They assert what this host actually does, not what this codebase
    /// does. Same role as RenderedGeometryInvestigationTests.cs.
    /// </summary>
    [TestFixture]
    public class TreeViewPlatformProbeTests
    {
        [Test]
        public void TreeViewCannotBeRealizedInThisTestHost()
        {
            WinUITestHost.RunWithWindow(window => {
                var tree = new TreeView();
                window.Content = tree;
                tree.RootNodes.Add(new TreeViewNode { Content = new TextBlock { Text = "probe" } });

                // TreeView's default style chain reaches a theme resource this unpackaged,
                // registration-free-activation host cannot resolve. Merging XamlControlsResources
                // into TestApplication does not fix it - it fail-fast crashes the whole test
                // process (STATUS_STACK_BUFFER_OVERRUN), because there is no package identity or
                // MRT for it to load from. Hence spec Decision 4: the outline builds its own rows.
                var ex = Assert.Throws<COMException>(() => tree.UpdateLayout());
                Assert.That(ex.Data["RestrictedDescription"], Is.Not.Null,
                    "expected a WinRT restricted-error description on the COMException");
                StringAssert.Contains("Cannot find a Resource",
                    ex.Data["RestrictedDescription"].ToString());

                // Leave the shared window in a good state for every later test in this process.
                window.Content = null;
            });
        }

        [Test]
        public void AHandBuiltRowTreeRendersInThisTestHost()
        {
            WinUITestHost.RunWithWindow(window => {
                // The shape OutlineViewControl uses instead: a StackPanel of Grid rows, with each
                // node's children in a nested StackPanel. Exactly what PropertyGridControl already
                // does, and it lays out here without any merged resource dictionary.
                var outer = new StackPanel();
                window.Content = outer;

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var toggle = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Content = "▶" };
                Grid.SetColumn(toggle, 0);
                row.Children.Add(toggle);
                var text = new TextBlock { Text = "Button" };
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                outer.Children.Add(row);

                var childStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
                childStack.Children.Add(new TextBlock { Text = "child" });
                outer.Children.Add(childStack);

                outer.UpdateLayout();

                Assert.Greater(row.ActualWidth, 0, "the row did not lay out");
                Assert.Greater(text.ActualHeight, 0, "the name text did not lay out");

                window.Content = null;
            });
        }
    }
}
