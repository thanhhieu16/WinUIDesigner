using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
	[TestFixture]
	public class LiveLayoutTests
	{
		[Test]
		public void ActivatedWindowProducesRealLayoutAndHitTestableContent()
		{
			// Known gap #1 (spec): does an activated-but-not-necessarily-visible-on-screen
			// Window produce a real layout pass and a working XamlRoot, or does this need
			// something more (explicit positioning, an extra dispatcher tick, a visible
			// desktop session)? This test is the empirical answer, not an assumption.
			WinUITestHost.RunWithWindow(window => {
				var button = new Button { Content = "X", Width = 80, Height = 30 };
				var grid = new Grid { Width = 300, Height = 200 };
				grid.Children.Add(button);
				window.Content = grid;
				grid.UpdateLayout();

				Assert.IsNotNull(button.XamlRoot,
					"button should have a XamlRoot once its Window is activated");
				Assert.Greater(button.ActualWidth, 0,
					"layout should have run: ActualWidth should be nonzero");
				Assert.Greater(button.ActualHeight, 0,
					"layout should have run: ActualHeight should be nonzero");

				// "Host coordinates" means the XAML island's coordinate space, which is what
				// TransformToVisual(null) produces. TransformToVisual(XamlRoot.Content) is NOT
				// the same thing: the root Grid has an explicit Width/Height, so it is centred
				// in the window rather than sitting at the island origin, and hit-testing a
				// root-relative point silently finds nothing. Measured on this host: the grid's
				// ActualOffset is <562,260> inside a 1424x720 island.
				var centerInButton = new Windows.Foundation.Point(button.ActualWidth / 2, button.ActualHeight / 2);
				var hostPoint = button.TransformToVisual(null).TransformPoint(centerInButton);
				var hits = VisualTreeHelper.FindElementsInHostCoordinates(hostPoint, button.XamlRoot.Content);

				CollectionAssert.Contains(hits, button,
					"FindElementsInHostCoordinates should find the button at its own center " +
					"point after an Activate()+UpdateLayout() pass");
			});
		}

		[Test]
		public void WindowIsReusableAcrossCalls()
		{
			// Regression guard: closing the last live WinUI Window ends Application.Start's
			// message loop and kills the dispatcher, and NUnitLite then exits 0 with no output
			// at all. If RunWithWindow ever starts closing its window again, this fails loudly.
			WinUITestHost.RunWithWindow(w => w.Content = new Grid { Width = 10, Height = 10 });
			WinUITestHost.RunWithWindow(w => Assert.IsNull(w.Content, "Content should be reset between calls"));
			WinUITestHost.Run(() => Assert.IsNotNull(new Grid()));
		}
	}
}
