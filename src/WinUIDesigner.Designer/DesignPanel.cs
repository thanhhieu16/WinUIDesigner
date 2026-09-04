using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.Designer.Controls;

namespace WinUIDesigner.Designer
{
	public sealed class DesignPanel : Panel, IDesignPanel
	{
		readonly AdornerLayer _adornerLayer;
		UIElement _content;

		public DesignPanel()
		{
			_adornerLayer = new AdornerLayer(this);
			Children.Add(_adornerLayer);
			IsContentHitTestVisible = true;
			IsTabStop = true; // makes Focus()/KeyDown reachable - same reasoning as DesignerThumb's own IsTabStop=true (DragListener.cs's Escape-to-cancel comment)
		}

		public UIElement Content {
			get { return _content; }
			set {
				if (_content == value) return;
				if (_content != null) Children.Remove(_content);
				_content = value;
				if (_content != null) Children.Insert(0, _content);
			}
		}

		public DesignContext Context { get; set; }
		public bool IsContentHitTestVisible { get; set; }

		public bool IsAdornerLayerHitTestVisible {
			get { return _adornerLayer.IsHitTestVisible; }
			set { _adornerLayer.IsHitTestVisible = value; }
		}

		/// <summary>Enables/disables snapline placement. Same default (true) as the reference's
		/// DesignPanel.UseSnaplinePlacement. Plain auto-property, not the reference's
		/// INotifyPropertyChanged-backed one: the reference raises OnPropertyChanged purely so
		/// DesignSurface.xaml's toolbar can two-way bind to it, and this port's DesignSurface is
		/// code-only with no such toolbar.</summary>
		public bool UseSnaplinePlacement { get; set; } = true;

		/// <summary>Enables/disables raster placement. Same default (false) as the reference's
		/// DesignPanel.UseRasterPlacement - see UseSnaplinePlacement for why this is a plain
		/// auto-property.</summary>
		public bool UseRasterPlacement { get; set; } = false;

		/// <summary>The raster cell size used when UseRasterPlacement is on. Same default (5)
		/// and same int type as the reference's DesignPanel.RasterWidth. Final-review fix (I4):
		/// clamped to a minimum of 1 - RasterPlacementBehavior.drawRaster's gridline loop
		/// (`for (int i = 0; i &lt;= h; i += raster)`) runs before any division, so a value &lt;= 0
		/// with UseRasterPlacement on would otherwise be an infinite loop, not a clean
		/// exception.</summary>
		public int RasterWidth {
			get { return _rasterWidth; }
			set { _rasterWidth = value < 1 ? 1 : value; }
		}
		int _rasterWidth = 5;

		readonly Dictionary<IReadOnlyList<MenuFlyoutItemBase>, int> _contextFlyoutGroups =
			new Dictionary<IReadOnlyList<MenuFlyoutItemBase>, int>();

		/// <summary>Contributes one Order-grouped set of context-menu items, merged with every
		/// other attached extension's group into a single composite MenuFlyout set as
		/// this.ContextFlyout - the WinUI-native replacement for the reference's per-extension
		/// ContextMenu + DesignPanel.AddContextMenu/UpdateContextMenu (WpfDesign.Designer/Project/
		/// DesignPanel.cs:555-613). Call with the exact same list instance from RemoveContextFlyoutItems
		/// later - removal is keyed by reference identity. Reusing the same MenuFlyoutItem instances
		/// across the rebuild this triggers is confirmed safe (spec's "WinUI API surface").</summary>
		public void AddContextFlyoutItems(IReadOnlyList<MenuFlyoutItemBase> items, int order)
		{
			_contextFlyoutGroups[items] = order;
			UpdateContextFlyout();
		}

		/// <summary>Removes a group previously added via AddContextFlyoutItems (same list instance).</summary>
		public void RemoveContextFlyoutItems(IReadOnlyList<MenuFlyoutItemBase> items)
		{
			_contextFlyoutGroups.Remove(items);
			UpdateContextFlyout();
		}

		void UpdateContextFlyout()
		{
			if (_contextFlyoutGroups.Count == 0) {
				this.ContextFlyout = null;
				return;
			}
			var flyout = new MenuFlyout();
			foreach (var group in _contextFlyoutGroups.OrderBy(g => g.Value)) {
				if (group.Key.Count == 0) continue;
				if (flyout.Items.Count > 0)
					flyout.Items.Add(new MenuFlyoutSeparator());
				foreach (var item in group.Key)
					flyout.Items.Add(item);
			}
			this.ContextFlyout = flyout;
		}

		public ICollection<AdornerPanel> Adorners { get { return _adornerLayer.Adorners; } }

		public Controls.AdornerLayer AdornerLayer { get { return _adornerLayer; } }

		protected override Size MeasureOverride(Size availableSize)
		{
			if (_content != null) _content.Measure(availableSize);
			_adornerLayer.Measure(availableSize);
			return _content != null ? _content.DesiredSize : new Size(0, 0);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			var r = new Rect(new Point(0, 0), finalSize);
			if (_content != null) _content.Arrange(r);
			_adornerLayer.Arrange(r);
			return finalSize;
		}

		// hitTestType (both overloads below): currently unused - the reference's only consumer was
		// the Alt-cycle hit-test filter callback, dropped this phase (see plan's "Known drops").
		// Retained for signature/interface compatibility.
		public DesignPanelHitTestResult HitTest(Point mousePosition, bool testAdorners, bool testDesignSurface, HitTestType hitTestType)
		{
			DesignPanelHitTestResult result = DesignPanelHitTestResult.NoHit;
			HitTest(mousePosition, testAdorners, testDesignSurface, r => { result = r; return false; }, hitTestType);
			return result;
		}

		public void HitTest(Point mousePosition, bool testAdorners, bool testDesignSurface, Predicate<DesignPanelHitTestResult> callback, HitTestType hitTestType)
		{
			if (mousePosition.X < 0 || mousePosition.Y < 0 || mousePosition.X > this.ActualWidth || mousePosition.Y > this.ActualHeight)
				return;
			if (this.XamlRoot == null)
				return;

			// FindElementsInHostCoordinates needs a point in island coordinates, not the
			// panel-local point the reference's HitTest takes. This is TransformToVisual(null),
			// not TransformToVisual(this.XamlRoot.Content) - empirically confirmed (Phase 3,
			// Task 1 investigation) that the latter silently returns zero hits whenever the root
			// content isn't itself sitting at the island origin (e.g. a sized root Grid ends up
			// centred in the island instead).
			var hostPoint = this.TransformToVisual(null).TransformPoint(mousePosition);
			bool continueHitTest = true;

			if (testAdorners) {
				foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(hostPoint, _adornerLayer)) {
					if (!continueHitTest) break;

					DependencyObject obj = element;
					AdornerPanel adorner = null;
					while (obj != null && obj != _adornerLayer) {
						var candidate = obj as AdornerPanel;
						if (candidate != null) adorner = candidate;
						obj = VisualTreeHelper.GetParent(obj);
					}

					// No specific AdornerPanel was found along this hit's own ancestor chain - the
					// hit resolved only to _adornerLayer itself (or some other non-AdornerPanel
					// descendant of it). _adornerLayer is always Arrange()'d to the DesignPanel's
					// full size regardless of DesiredSize (DesignPanel.ArrangeOverride) - empirically
					// confirmed (Task 15) that once it has at least one real child anywhere,
					// FindElementsInHostCoordinates can report _adornerLayer itself as "hit" at ANY
					// point within that full-size rect, even far from where any actual adorner is
					// positioned. That is not a real adorner hit, so it must not block
					// ElementSelection hit-testing (this method's own single-result convenience
					// overload, and DesignPanel.OnPointerPressed's real click-to-select path) from
					// reaching the design-surface content underneath.
					if (adorner == null) continue;

					// PanelMove (Task 15)'s PanelMoveAdorner is a full-fill overlay permanently
					// present over every Panel/Border/ContentControl/Viewbox (regardless of
					// selection), unlike every other adorner (resize/rotate/skew thumbs,
					// TopLeftContainerDragHandle), which only exists once something is already
					// selected and is meant to swallow the click rather than fall through. If a
					// PanelMoveAdorner hit stopped this loop like any other adorner would, it
					// would permanently shadow ElementSelection hit-testing for every element it
					// covers - including nested children, since it fills its whole container's
					// bounds. Skipping it here lets the point fall through to the real
					// design-surface content beneath it, so normal click-to-select still resolves
					// the actually-clicked element; PanelMove's own move gesture is driven
					// separately, directly off the overlay's own routed pointer events
					// (DragListener), not through this hit-test path.
					//
					// Final-review note (I3, see PanelMove's own class doc comment for the fuller
					// story): this same fall-through is *why* clicking a container's body over a
					// content-filling child re-selects the child instead of starting a container
					// move through the real pointer path below (OnPointerPressed/
					// HandlePointerPressed) - the press reaches this method's content pass and
					// resolves to the child underneath, same as any other click on that child
					// would, before PanelMove's Drag_Started ever gets a chance to check what's
					// selected.
					if (adorner.Children.Count == 1 && adorner.Children[0] is PanelMoveAdorner)
						continue;

					var customResult = new DesignPanelHitTestResult(element);
					customResult.AdornerHit = adorner;
					continueHitTest = callback(customResult);
				}
			}

			// IsContentHitTestVisible: skip the content pass entirely instead of the reference's
			// _eatAllHitTestRequests element-swap trick (dropped - see "Known drops").
			if (continueHitTest && testDesignSurface && IsContentHitTestVisible && _content != null) {
				foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(hostPoint, _content)) {
					if (!continueHitTest) break;
					var customResult = new DesignPanelHitTestResult(element);
					DependencyObject obj = element;
					DesignItem modelHit = null;
					while (obj != null) {
						var el = obj as UIElement;
						if (el != null && Context != null) {
							modelHit = Context.Services.Component.GetDesignItem(el, true);
							if (modelHit != null) break;
						}
						obj = VisualTreeHelper.GetParent(obj);
					}
					customResult.ModelHit = modelHit ?? (Context != null ? Context.RootItem : null);
					continueHitTest = callback(customResult);
				}
			}
		}

		/// <summary>The real OnPointerPressed hit-test-and-select logic, extracted so a test can
		/// drive it directly with a Point and modifiers instead of a real pointer event.
		/// PointerRoutedEventArgs has no public constructor (Global Constraints), so this
		/// internal seam - reachable from WinUIDesigner.Designer.Tests via InternalsVisibleTo -
		/// is what makes it possible to exercise the real click-to-select path (as opposed to
		/// DragListener's SimulateDown, which bypasses it entirely) from a test. See PanelMove's
		/// class doc comment and the PanelMoveAdorner fall-through note above (final-review
		/// I3).</summary>
		internal void HandlePointerPressed(Point point, VirtualKeyModifiers modifiers)
		{
			if (Context == null) return;
			var result = HitTest(point, true, true, HitTestType.ElementSelection);
			if (result.ModelHit == null) return;

			// Mirrors the reference's SelectionTypes.Auto resolution (Control -> Toggle,
			// Shift -> Add, else -> Primary) - read from the triggering pointer event instead of
			// a global keyboard-state query (spec's "Decisions already made").
			SelectionTypes selectionType;
			if ((modifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control)
				selectionType = SelectionTypes.Toggle;
			else if ((modifiers & VirtualKeyModifiers.Shift) == VirtualKeyModifiers.Shift)
				selectionType = SelectionTypes.Add;
			else
				selectionType = SelectionTypes.Primary;

			Context.Services.Selection.SetSelectedComponents(new DesignItem[] { result.ModelHit }, selectionType);
		}

		public void SetCursor(Microsoft.UI.Input.InputCursor cursor)
		{
			ProtectedCursor = cursor;
		}
	}
}
