using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
			// DesignSurface's constructor now sets HorizontalContentAlignment/VerticalContentAlignment
			// to Stretch itself (Phase 3 final review fix, I1) - verified below by NOT overriding it
			// here. Without that default, a bare ContentControl's generic.xaml would arrange the
			// unsized DesignPanel at exactly its DesiredSize (here, the root Grid's Auto-sized single
			// implicit cell, which equals one button's 60x30 - both buttons the same size), leaving no
			// extra room for HorizontalAlignment="Left" vs "Right" to differentiate: both buttons would
			// end up stacked on the exact same rect, and hit-testing at any point could no longer tell
			// them apart. Confirmed by direct measurement: with the constructor's Stretch default in
			// effect, Left lands at panel-local (0,0) and Right at (1364,0) in a 1424-wide panel.
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
				var leftButton = (Button)grid.Children[0];
				var leftItem = surface.DesignContext.Services.Component.GetDesignItem(leftButton);

				// Derived from the button's actual measured position (not a raw panel-local literal,
				// per HitTestTests.cs's pattern), so this fails loudly - rather than silently hitting
				// something else - if TwoButtonsXaml's button dimensions ever change.
				var pointInLeftButton = leftButton.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(30, 15));
				var result = surface.DesignPanel.HitTest(pointInLeftButton, true, true, HitTestType.ElementSelection);

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
				// Filtered to the SelectionHighlight-bearing panel specifically (not "all adorner
				// panels on this element") since Task 4's ResizeThumbExtension now also adorns the
				// primary selection with its own, separate AdornerPanel (the eight resize thumbs) -
				// both legitimately coexist. This test's own concern, per its comment above, is only
				// the highlight's single-active-selection lifecycle.
				var afterFirst = surface.DesignPanel.Adorners.Where(a => a.AdornedElement == leftButton && a.Children.OfType<SelectionHighlight>().Any()).ToList();
				Assert.AreEqual(1, afterFirst.Count, "selecting Left should apply one highlight adorner panel to it");

				surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[] { rightItem }, SelectionTypes.Primary);
				surface.UpdateLayout();

				var stillOnLeft = surface.DesignPanel.Adorners.Where(a => a.AdornedElement == leftButton && a.Children.OfType<SelectionHighlight>().Any()).ToList();
				var onRight = surface.DesignPanel.Adorners.Where(a => a.AdornedElement == rightButton && a.Children.OfType<SelectionHighlight>().Any()).ToList();
				Assert.AreEqual(0, stillOnLeft.Count, "Left's highlight adorner should be removed once selection moves to Right");
				Assert.AreEqual(1, onRight.Count, "Right should now have exactly one highlight adorner");
			});
		}

		// Final whole-branch review finding I2: no existing test asserted the adorner's arranged
		// *position* numerically (AdornerLayerTests.cs only checks RenderTransform is non-null for
		// any value), and nothing determined which of ArrangeOverride's two branches - the
		// MatrixTransform cast or the TranslateTransform position-only fallback - actually runs in
		// this environment. Asserts both: the branch taken, and the numeric position it produces.
		[Test]
		public void SelectedElementsAdornerPanelIsArrangedAtTheElementsTransformedPosition()
		{
			WinUITestHost.RunWithWindow(window => {
				var surface = LoadInto(window, TwoButtonsXaml);
				var grid = (Grid)surface.DesignContext.RootItem.Component;
				var rightButton = (Button)grid.Children[1];
				var rightItem = surface.DesignContext.Services.Component.GetDesignItem(rightButton);

				surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[] { rightItem }, SelectionTypes.Primary);
				surface.UpdateLayout();

				// Filtered to the SelectionHighlight-bearing panel specifically - see the comment in
				// SelectingASecondElementRemovesTheFirstElementsHighlight above; Task 4's
				// ResizeThumbExtension now also legitimately adorns the primary selection with its
				// own separate AdornerPanel, so a plain AdornedElement match is no longer unique.
				var panel = surface.DesignPanel.Adorners.Single(a => a.AdornedElement == rightButton && a.Children.OfType<SelectionHighlight>().Any());
				var matrixTransform = panel.RenderTransform as MatrixTransform;
				Assert.IsNotNull(matrixTransform, "expected AdornerLayer.ArrangeOverride to take the MatrixTransform branch, not the TranslateTransform fallback");

				var expected = rightButton.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(0, 0));
				Assert.AreEqual(expected.X, matrixTransform.Matrix.OffsetX, 0.5, "adorner X position should match the adorned element's transformed position");
				Assert.AreEqual(expected.Y, matrixTransform.Matrix.OffsetY, 0.5, "adorner Y position should match the adorned element's transformed position");
			});
		}
	}
}
