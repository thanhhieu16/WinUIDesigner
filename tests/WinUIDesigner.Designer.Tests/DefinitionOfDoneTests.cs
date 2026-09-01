using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
	[TestFixture]
	public class DefinitionOfDoneTests
	{
		const string TwoButtonsXaml =
			"<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
			"xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
			"<Button x:Name=\"Left\" Width=\"60\" Height=\"30\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
			"<Button x:Name=\"Right\" Width=\"60\" Height=\"30\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Top\" />" +
			"</Grid>";

		static DesignSurface LoadInto(Microsoft.UI.Xaml.Window window, string xaml)
		{
			var surface = new DesignSurface();
			// Measured directly (Phase 3, Task 5): a bare Microsoft.UI.Xaml.Controls.ContentControl's
			// HorizontalContentAlignment/VerticalContentAlignment default to Left/Top, not Stretch -
			// so without this, DesignSurface's ContentPresenter arranges the unsized DesignPanel at
			// exactly its DesiredSize (here, the root Grid's Auto-sized single implicit cell, which
			// equals one button's 60x30 - both buttons the same size). That leaves the Grid with no
			// extra room for HorizontalAlignment="Left" vs "Right" to differentiate: both buttons end
			// up stacked on the exact same rect, and hit-testing at any point can no longer tell them
			// apart. Stretching the surface's content is what a real design host does anyway (show the
			// design at the surface's actual size), and it is what gives the Left/Right-aligned buttons
			// room to land at different points - confirmed by direct measurement: with this set, Left
			// lands at panel-local (0,0) and Right at (1364,0) in a 1424-wide panel.
			surface.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
			surface.VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
			window.Content = surface;
			using (var reader = XmlReader.Create(new StringReader(xaml)))
				surface.LoadDesigner(reader, new XamlLoadSettings());
			surface.UpdateLayout();
			return surface;
		}

		// Spec DoD bullet 1: hit-test at a point known to fall inside a specific child element
		// returns the correct DesignItem as ModelHit.
		[Test]
		public void HitTestingThroughTheDesignSurfaceReturnsTheCorrectDesignItem()
		{
			WinUITestHost.RunWithWindow(window => {
				var surface = LoadInto(window, TwoButtonsXaml);
				var grid = (Grid)surface.DesignContext.RootItem.Component;
				var leftItem = surface.DesignContext.Services.Component.GetDesignItem(grid.Children[0]);

				var result = surface.DesignPanel.HitTest(new Windows.Foundation.Point(30, 15), true, true, HitTestType.ElementSelection);

				Assert.AreSame(leftItem, result.ModelHit);
			});
		}

		// Spec DoD bullet 2: selecting via a direct, already-resolved SelectionTypes call updates
		// PrimarySelection/SelectedItems, fires SelectionChanged/PrimarySelectionChanged, and shows
		// exactly one SelectionHighlight adorner.
		[Test]
		public void SelectingAnElementUpdatesSelectionStateFiresEventsAndShowsOneHighlight()
		{
			WinUITestHost.RunWithWindow(window => {
				var surface = LoadInto(window, TwoButtonsXaml);
				var grid = (Grid)surface.DesignContext.RootItem.Component;
				var leftItem = surface.DesignContext.Services.Component.GetDesignItem(grid.Children[0]);

				bool selectionChanged = false, primaryChanged = false;
				surface.DesignContext.Services.Selection.SelectionChanged += (s, e) => selectionChanged = true;
				surface.DesignContext.Services.Selection.PrimarySelectionChanged += (s, e) => primaryChanged = true;

				surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[] { leftItem }, SelectionTypes.Primary);
				surface.UpdateLayout();

				Assert.IsTrue(selectionChanged, "SelectionChanged should fire");
				Assert.IsTrue(primaryChanged, "PrimarySelectionChanged should fire");
				Assert.AreSame(leftItem, surface.DesignContext.Services.Selection.PrimarySelection);
				CollectionAssert.AreEquivalent(new[] { leftItem }, surface.DesignContext.Services.Selection.SelectedItems);

				var highlights = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<SelectionHighlight>().ToList();
				Assert.AreEqual(1, highlights.Count);
			});
		}

		// Spec DoD bullet 3: mutating a layout property through DesignItemProperty.SetValue moves
		// the highlight adorner to track the adorned element after the next LayoutUpdated pass.
		[Test]
		public void ResizingTheSelectedElementMovesTheHighlightAfterLayoutUpdated()
		{
			WinUITestHost.RunWithWindow(window => {
				var surface = LoadInto(window, TwoButtonsXaml);
				var grid = (Grid)surface.DesignContext.RootItem.Component;
				var leftButton = (Button)grid.Children[0];
				var leftItem = surface.DesignContext.Services.Component.GetDesignItem(leftButton);

				surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[] { leftItem }, SelectionTypes.Primary);
				surface.UpdateLayout();

				var highlight = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<SelectionHighlight>().Single();
				double widthBefore = highlight.ActualWidth;

				using (var group = surface.DesignContext.OpenGroup("resize", new DesignItem[] { leftItem })) {
					leftItem.Properties["Width"].SetValue(150.0);
					group.Commit();
				}
				surface.UpdateLayout();

				Assert.AreNotEqual(widthBefore, highlight.ActualWidth,
					"the highlight adorner should track the adorned element's new size after LayoutUpdated");
			});
		}

		// Spec DoD bullet 4: selecting a second element removes the first element's adorner
		// (single active selection at a time, per this phase's minimal scope).
		[Test]
		public void SelectingASecondElementRemovesTheFirstElementsHighlight()
		{
			WinUITestHost.RunWithWindow(window => {
				var surface = LoadInto(window, TwoButtonsXaml);
				var grid = (Grid)surface.DesignContext.RootItem.Component;
				var leftButton = (Button)grid.Children[0];
				var rightButton = (Button)grid.Children[1];
				var leftItem = surface.DesignContext.Services.Component.GetDesignItem(leftButton);
				var rightItem = surface.DesignContext.Services.Component.GetDesignItem(rightButton);

				surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[] { leftItem }, SelectionTypes.Primary);
				surface.UpdateLayout();
				var afterFirst = surface.DesignPanel.Adorners.Where(a => a.AdornedElement == leftButton).ToList();
				Assert.AreEqual(1, afterFirst.Count, "selecting Left should apply one adorner panel to it");

				surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[] { rightItem }, SelectionTypes.Primary);
				surface.UpdateLayout();

				var stillOnLeft = surface.DesignPanel.Adorners.Where(a => a.AdornedElement == leftButton).ToList();
				var onRight = surface.DesignPanel.Adorners.Where(a => a.AdornedElement == rightButton).ToList();
				Assert.AreEqual(0, stillOnLeft.Count, "Left's adorner should be removed once selection moves to Right");
				Assert.AreEqual(1, onRight.Count, "Right should now have exactly one adorner");
			});
		}
	}
}
