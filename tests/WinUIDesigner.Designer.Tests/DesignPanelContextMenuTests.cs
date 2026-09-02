using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class DesignPanelContextMenuTests
    {
        [Test]
        public void MergesTwoGroupsInAscendingOrderWithOneSeparator()
        {
            WinUITestHost.Run(() => {
                var panel = new DesignPanel();
                var groupA = new List<MenuFlyoutItemBase> { new MenuFlyoutItem { Text = "A1" } };
                var groupB = new List<MenuFlyoutItemBase> {
                    new MenuFlyoutItem { Text = "B1" },
                    new MenuFlyoutItem { Text = "B2" }
                };

                // Added out of Order to prove the merge sorts, not just concatenates.
                panel.AddContextFlyoutItems(groupB, 20);
                panel.AddContextFlyoutItems(groupA, 10);

                var flyout = (MenuFlyout)panel.ContextFlyout;
                Assert.AreEqual(4, flyout.Items.Count);
                Assert.AreSame(groupA[0], flyout.Items[0]);
                Assert.IsInstanceOf<MenuFlyoutSeparator>(flyout.Items[1]);
                Assert.AreSame(groupB[0], flyout.Items[2]);
                Assert.AreSame(groupB[1], flyout.Items[3]);
            });
        }

        [Test]
        public void RemovingAGroupRebuildsWithoutItAndWithoutASeparator()
        {
            WinUITestHost.Run(() => {
                var panel = new DesignPanel();
                var groupA = new List<MenuFlyoutItemBase> { new MenuFlyoutItem { Text = "A1" } };
                var groupB = new List<MenuFlyoutItemBase> { new MenuFlyoutItem { Text = "B1" } };
                panel.AddContextFlyoutItems(groupA, 10);
                panel.AddContextFlyoutItems(groupB, 20);

                panel.RemoveContextFlyoutItems(groupA);

                var flyout = (MenuFlyout)panel.ContextFlyout;
                Assert.AreEqual(1, flyout.Items.Count);
                Assert.AreSame(groupB[0], flyout.Items[0]);
            });
        }

        [Test]
        public void RemovingTheLastGroupClearsContextFlyout()
        {
            WinUITestHost.Run(() => {
                var panel = new DesignPanel();
                var groupA = new List<MenuFlyoutItemBase> { new MenuFlyoutItem { Text = "A1" } };
                panel.AddContextFlyoutItems(groupA, 10);

                panel.RemoveContextFlyoutItems(groupA);

                Assert.IsNull(panel.ContextFlyout);
            });
        }

        [Test]
        public void SameGroupInstanceCanBeReAddedAfterRemoval()
        {
            // Exercises the same "reuse a MenuFlyoutItem across a rebuilt MenuFlyout"
            // pattern confirmed safe during spec-writing, through the real production path.
            WinUITestHost.Run(() => {
                var panel = new DesignPanel();
                var group = new List<MenuFlyoutItemBase> { new MenuFlyoutItem { Text = "A1" } };
                panel.AddContextFlyoutItems(group, 10);
                panel.RemoveContextFlyoutItems(group);

                panel.AddContextFlyoutItems(group, 10);

                var flyout = (MenuFlyout)panel.ContextFlyout;
                Assert.AreEqual(1, flyout.Items.Count);
                Assert.AreSame(group[0], flyout.Items[0]);
            });
        }
    }
}
