using System.IO;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
	[TestFixture]
	public class HitTestTests
	{
		const string TwoButtonsXaml =
			"<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
			"xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"200\">" +
			"<Button x:Name=\"Left\" Width=\"80\" Height=\"80\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
			"<Button x:Name=\"Right\" Width=\"80\" Height=\"80\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Top\" />" +
			"</Grid>";

		[Test]
		public void HitTestReturnsTheDesignItemAtAGivenPoint()
		{
			WinUITestHost.RunWithWindow(window => {
				using (var reader = XmlReader.Create(new StringReader(TwoButtonsXaml))) {
					var context = new XamlDesignContext(reader, new XamlLoadSettings());
					var panel = new DesignPanel { Context = context, Content = context.RootItem.View };
					window.Content = panel;
					panel.UpdateLayout();

					var grid = (Grid)context.RootItem.Component;
					var leftButton = (Button)grid.Children[0];
					var rightButton = (Button)grid.Children[1];
					var leftItem = context.Services.Component.GetDesignItem(leftButton);
					var rightItem = context.Services.Component.GetDesignItem(rightButton);

					// A bare DesignPanel with no explicit Width/Height (sizing it to its design
					// content is Task 4's DesignSurface job - out of this task's scope, see "Known
					// drops") stretches to fill the whole window when set as Window.Content
					// (measured: panel ActualWidth/Height 1424x720 on this host), and the 200x200
					// Grid content then gets centred within that much bigger panel rather than
					// sitting at the panel's local origin (measured: leftButton's panel-local
					// offset is <612,260>, not <0,0>) - the same centring effect LiveLayoutTests
					// documents for Window.Content directly. So the hit point is computed from the
					// button's actual measured panel-local position instead of assuming it sits at
					// the panel origin.
					var leftButtonOrigin = leftButton.TransformToVisual(panel).TransformPoint(new Windows.Foundation.Point(0, 0));
					var pointInLeftButton = new Windows.Foundation.Point(leftButtonOrigin.X + 40, leftButtonOrigin.Y + 40);
					var result = panel.HitTest(pointInLeftButton, true, true, HitTestType.ElementSelection);

					Assert.AreSame(leftItem, result.ModelHit);
					Assert.AreNotSame(rightItem, result.ModelHit);
				}
			});
		}

		[Test]
		public void HitTestOutsidePanelBoundsReturnsNoHit()
		{
			WinUITestHost.RunWithWindow(window => {
				using (var reader = XmlReader.Create(new StringReader(TwoButtonsXaml))) {
					var context = new XamlDesignContext(reader, new XamlLoadSettings());
					var panel = new DesignPanel { Context = context, Content = context.RootItem.View };
					window.Content = panel;
					panel.UpdateLayout();

					var result = panel.HitTest(new Windows.Foundation.Point(-10, -10), true, true, HitTestType.ElementSelection);
					Assert.AreEqual(DesignPanelHitTestResult.NoHit, result);
				}
			});
		}

		[Test]
		public void IsContentHitTestVisibleFalseSkipsTheContentPass()
		{
			WinUITestHost.RunWithWindow(window => {
				using (var reader = XmlReader.Create(new StringReader(TwoButtonsXaml))) {
					var context = new XamlDesignContext(reader, new XamlLoadSettings());
					var panel = new DesignPanel { Context = context, Content = context.RootItem.View, IsContentHitTestVisible = false };
					window.Content = panel;
					panel.UpdateLayout();

					// Same panel-stretch/content-centring caveat as HitTestReturnsTheDesignItemAtAGivenPoint:
					// compute a point that is genuinely inside the left button, so this test proves
					// IsContentHitTestVisible=false is what suppresses the hit - not that the fixed
					// point simply missed the button regardless.
					var grid = (Grid)context.RootItem.Component;
					var leftButton = (Button)grid.Children[0];
					var leftButtonOrigin = leftButton.TransformToVisual(panel).TransformPoint(new Windows.Foundation.Point(0, 0));
					var pointInLeftButton = new Windows.Foundation.Point(leftButtonOrigin.X + 40, leftButtonOrigin.Y + 40);

					var result = panel.HitTest(pointInLeftButton, true, true, HitTestType.ElementSelection);
					Assert.AreEqual(DesignPanelHitTestResult.NoHit, result);
				}
			});
		}
	}
}
