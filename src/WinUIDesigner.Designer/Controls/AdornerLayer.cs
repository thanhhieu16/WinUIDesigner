using System;
using System.Collections.Generic;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;

namespace WinUIDesigner.Designer.Controls
{
	public sealed class AdornerLayer : Panel, IAdornerLayer
	{
		internal sealed class AdornerPanelCollection : ICollection<AdornerPanel>
		{
			readonly AdornerLayer _layer;
			public AdornerPanelCollection(AdornerLayer layer) { _layer = layer; }
			public int Count { get { return _layer.Children.Count; } }
			public bool IsReadOnly { get { return false; } }
			public void Add(AdornerPanel item) { if (item == null) throw new ArgumentNullException("item"); _layer.AddAdorner(item); }
			public void Clear() { _layer.ClearAdorners(); }
			public bool Contains(AdornerPanel item) { if (item == null) throw new ArgumentNullException("item"); return VisualTreeHelper.GetParent(item) == _layer; }
			public void CopyTo(AdornerPanel[] array, int arrayIndex) { foreach (var panel in this) array[arrayIndex++] = panel; }
			public bool Remove(AdornerPanel item) { if (item == null) throw new ArgumentNullException("item"); return _layer.RemoveAdorner(item); }
			public IEnumerator<AdornerPanel> GetEnumerator() { foreach (AdornerPanel panel in _layer.Children) yield return panel; }
			System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
		}

		readonly AdornerPanelCollection _adorners;
		readonly UIElement _designPanel;

		internal AdornerLayer(UIElement designPanel)
		{
			_designPanel = designPanel;
			// FrameworkElement.LayoutUpdated's delegate type in WinUI 3 is EventHandler<object>,
			// not EventHandler (WPF).
			LayoutUpdated += OnLayoutUpdated;
			_adorners = new AdornerPanelCollection(this);
		}

		void OnLayoutUpdated(object sender, object e) { UpdateAllAdorners(false); }

		internal AdornerPanelCollection Adorners { get { return _adorners; } }

		sealed class AdornerInfo
		{
			internal readonly List<AdornerPanel> adorners = new List<AdornerPanel>();
			internal bool isVisible;
			internal Rect position;
		}

		Dictionary<UIElement, AdornerInfo> _dict = new Dictionary<UIElement, AdornerInfo>();

		void ClearAdorners()
		{
			if (_dict.Count == 0) return;
			this.Children.Clear();
			_dict = new Dictionary<UIElement, AdornerInfo>();
		}

		AdornerInfo GetOrCreateAdornerInfo(UIElement adornedElement)
		{
			AdornerInfo info;
			if (!_dict.TryGetValue(adornedElement, out info)) {
				info = _dict[adornedElement] = new AdornerInfo();
				info.isVisible = IsDescendantOf(adornedElement, _designPanel);
			}
			return info;
		}

		AdornerInfo GetExistingAdornerInfo(UIElement adornedElement)
		{
			AdornerInfo info;
			_dict.TryGetValue(adornedElement, out info);
			return info;
		}

		void AddAdorner(AdornerPanel adornerPanel)
		{
			if (adornerPanel.AdornedElement == null)
				throw new DesignerException("adornerPanel.AdornedElement must be set");

			AdornerInfo info = GetOrCreateAdornerInfo(adornerPanel.AdornedElement);
			info.adorners.Add(adornerPanel);
			if (info.isVisible) AddAdornerToChildren(adornerPanel);
		}

		void AddAdornerToChildren(AdornerPanel adornerPanel)
		{
			int i;
			for (i = 0; i < Children.Count; i++) {
				var p = (AdornerPanel)Children[i];
				if (p.Order > adornerPanel.Order) break;
			}
			Children.Insert(i, adornerPanel);
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
			foreach (AdornerPanel adorner in Children) adorner.Measure(infinite);
			return new Size(0, 0);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			foreach (AdornerPanel adorner in Children) {
				if (IsDescendantOf(adorner.AdornedElement, _designPanel)) {
					// Empirically confirmed (Phase 3 final review, I2 -
					// DefinitionOfDoneTests.SelectedElementsAdornerPanelIsArrangedAtTheElementsTransformedPosition):
					// TransformToVisual always returns a MatrixTransform in this environment, matching
					// the spec's "Explicitly out of scope" finding that no GeneralTransformGroup case
					// exists here to fall back to. The previous TranslateTransform (position-only)
					// fallback branch was therefore dead, untested code that would have silently
					// dropped rotation/scale on some future element instead of failing loudly - removed.
					// A direct cast is used instead, so an element that ever breaks this assumption
					// throws an InvalidCastException here rather than silently degrading.
					adorner.RenderTransform = (MatrixTransform)adorner.AdornedElement.TransformToVisual(_designPanel);
				}
				adorner.Arrange(new Rect(new Point(0, 0), adorner.DesiredSize));
			}
			return finalSize;
		}

		bool RemoveAdorner(AdornerPanel adornerPanel)
		{
			if (adornerPanel.AdornedElement == null) return false;
			AdornerInfo info = GetExistingAdornerInfo(adornerPanel.AdornedElement);
			if (info == null) return false;
			if (info.adorners.Remove(adornerPanel)) {
				if (info.isVisible) this.Children.Remove(adornerPanel);
				if (info.adorners.Count == 0) _dict.Remove(adornerPanel.AdornedElement);
				return true;
			}
			return false;
		}

		public void UpdateAdornersForElement(UIElement element, bool forceInvalidate)
		{
			AdornerInfo info = GetExistingAdornerInfo(element);
			if (info != null) UpdateAdornersForElement(element, info, forceInvalidate);
		}

		Rect GetPositionCache(UIElement element)
		{
			var t = element.TransformToVisual(_designPanel);
			var p = t.TransformPoint(new Point(0, 0));
			return new Rect(p, element.RenderSize);
		}

		void UpdateAdornersForElement(UIElement element, AdornerInfo info, bool forceInvalidate)
		{
			if (IsDescendantOf(element, _designPanel)) {
				if (!info.isVisible) {
					info.isVisible = true;
					info.adorners.ForEach(AddAdornerToChildren);
				}
				Rect c = GetPositionCache(element);
				if (forceInvalidate || !info.position.Equals(c)) {
					info.position = c;
					foreach (var p in info.adorners) p.InvalidateMeasure();
					this.InvalidateArrange();
				}
			} else {
				if (info.isVisible) {
					info.isVisible = false;
					foreach (var p in info.adorners) this.Children.Remove(p);
				}
			}
		}

		void UpdateAllAdorners(bool forceInvalidate)
		{
			foreach (var pair in _dict) UpdateAdornersForElement(pair.Key, pair.Value, forceInvalidate);
		}

		// Known absent (spec, verified against Microsoft.UI.Xaml.winmd): IsDescendantOf /
		// GetIsAncestorOf. Manual ancestor walk instead.
		static bool IsDescendantOf(UIElement element, UIElement ancestor)
		{
			DependencyObject current = element;
			while (current != null) {
				if (current == ancestor) return true;
				current = VisualTreeHelper.GetParent(current);
			}
			return false;
		}
	}
}
