using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
	[TestFixture]
	public class AdornerLayerTests
	{
		[Test]
		public void AdornerBecomesVisibleAndPositionedWhenItsElementIsInTheDesignTree()
		{
			WinUITestHost.RunWithWindow(window => {
				var panel = new DesignPanel();
				var button = new Button { Width = 40, Height = 20 };
				var grid = new Grid { Width = 300, Height = 300 };
				grid.Children.Add(button);
				panel.Content = grid;
				window.Content = panel;
				panel.UpdateLayout();

				var adorner = new AdornerPanel();
				adorner.SetAdornedElement(button, null);
				panel.Adorners.Add(adorner);
				panel.UpdateLayout();

				Assert.IsTrue(panel.Adorners.Contains(adorner),
					"adorner should be visible: its adorned element is in the design tree");
				Assert.IsNotNull(adorner.RenderTransform,
					"AdornerLayer should have positioned the adorner via RenderTransform");
			});
		}

		[Test]
		public void AdornerDisappearsWhenItsElementLeavesTheDesignTree()
		{
			WinUITestHost.RunWithWindow(window => {
				var panel = new DesignPanel();
				var button = new Button { Width = 40, Height = 20 };
				var grid = new Grid();
				grid.Children.Add(button);
				panel.Content = grid;
				window.Content = panel;
				panel.UpdateLayout();

				var adorner = new AdornerPanel();
				adorner.SetAdornedElement(button, null);
				panel.Adorners.Add(adorner);
				panel.UpdateLayout();
				Assert.IsTrue(panel.Adorners.Contains(adorner));

				grid.Children.Remove(button);
				panel.UpdateLayout();

				Assert.IsFalse(panel.Adorners.Contains(adorner),
					"adorner should become invisible once its adorned element leaves the design " +
					"tree - exercises the IsDescendantOf replacement (mechanism table, spec)");
			});
		}

		[Test]
		public void UIElementUsesReferenceIdentityEqualityByDefault()
		{
			// Known gap #3 (spec): AdornerLayer's Dictionary<UIElement, AdornerInfo> relies on
			// UIElement not overriding Equals/GetHashCode, so the dictionary's default comparer
			// already uses reference identity - no custom IEqualityComparer needed. This test is
			// the empirical check, not an assumption: two distinct instances of the same UIElement
			// subclass must compare unequal, and an instance must equal itself.
			WinUITestHost.Run(() => {
				var a = new Microsoft.UI.Xaml.Shapes.Rectangle();
				var b = new Microsoft.UI.Xaml.Shapes.Rectangle();
				Assert.IsFalse(a.Equals(b), "two distinct UIElement instances should not be equal");
				Assert.IsTrue(a.Equals(a), "an element should equal itself");
			});
		}
	}
}
