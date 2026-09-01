// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinUIDesigner.DesignModel.Adorners
{
	public sealed class AdornerPanel : Panel
	{
		public static readonly DependencyProperty PlacementProperty = DependencyProperty.RegisterAttached(
			"Placement", typeof(AdornerPlacement), typeof(AdornerPanel),
			new PropertyMetadata(AdornerPlacement.FillContent, OnPlacementChanged));

		// WinUI attached-property metadata has no FrameworkPropertyMetadataOptions.AffectsParentMeasure
		// equivalent (confirmed absent from Microsoft.UI.Xaml.winmd during spec brainstorming) -
		// invalidate the owning AdornerPanel's measure manually instead.
		static void OnPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var adorner = d as UIElement;
			var panel = adorner != null ? VisualTreeHelper.GetParent(adorner) as AdornerPanel : null;
			if (panel != null) panel.InvalidateMeasure();
		}

		public static AdornerPlacement GetPlacement(UIElement adorner)
		{
			if (adorner == null) throw new ArgumentNullException("adorner");
			return (AdornerPlacement)adorner.GetValue(PlacementProperty);
		}

		public static void SetPlacement(UIElement adorner, AdornerPlacement placement)
		{
			if (adorner == null) throw new ArgumentNullException("adorner");
			if (placement == null) throw new ArgumentNullException("placement");
			adorner.SetValue(PlacementProperty, placement);
		}

		UIElement _adornedElement;
		DesignItem _adornedDesignItem;
		AdornerOrder _order = AdornerOrder.Content;

		public UIElement AdornedElement { get { return _adornedElement; } }
		public DesignItem AdornedDesignItem { get { return _adornedDesignItem; } }

		public void SetAdornedElement(UIElement adornedElement, DesignItem adornedDesignItem)
		{
			if (adornedElement == null) throw new ArgumentNullException("adornedElement");
			if (_adornedElement == adornedElement && _adornedDesignItem == adornedDesignItem) return;
			if (_adornedElement != null) throw new InvalidOperationException("AdornedElement is already set.");
			_adornedElement = adornedElement;
			_adornedDesignItem = adornedDesignItem;
		}

		public AdornerOrder Order {
			get { return _order; }
			set { _order = value; }
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			if (this.AdornedElement != null) {
				foreach (var child in Children) {
					child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
				}
				return GetRealElementSize(this.AdornedElement);
			}
			return base.MeasureOverride(availableSize);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			foreach (UIElement element in Children) {
				GetPlacement(element).Arrange(this, element, finalSize);
			}
			return finalSize;
		}

		// Known gap #2 (spec): the reference's only use of PlacementOperation in this file is
		// this static helper - ported directly rather than pulling in the rest of
		// PlacementOperation.cs, which this phase does not otherwise use (see spec's
		// "Explicitly out of scope").
		static Size GetRealElementSize(UIElement element)
		{
			var size = element.RenderSize;
			var fe = element as FrameworkElement;
			if (fe != null && !double.IsNaN(fe.Width)) size.Width = fe.Width;
			if (fe != null && !double.IsNaN(fe.Height)) size.Height = fe.Height;
			if (fe != null && size.Width < fe.MinWidth) size.Width = fe.MinWidth;
			if (fe != null && size.Height < fe.MinHeight) size.Height = fe.MinHeight;
			return size;
		}
	}

	public struct AdornerOrder : IComparable<AdornerOrder>, IEquatable<AdornerOrder>
	{
		public static readonly AdornerOrder Background = new AdornerOrder(100);
		public static readonly AdornerOrder Content = new AdornerOrder(200);
		public static readonly AdornerOrder BehindForeground = new AdornerOrder(280);
		public static readonly AdornerOrder Foreground = new AdornerOrder(300);
		public static readonly AdornerOrder BeforeForeground = new AdornerOrder(400);

		readonly int i;

		internal AdornerOrder(int i) { this.i = i; }

		public override int GetHashCode() { return i.GetHashCode(); }
		public override bool Equals(object obj) { return obj is AdornerOrder && this == (AdornerOrder)obj; }
		public bool Equals(AdornerOrder other) { return i == other.i; }
		public int CompareTo(AdornerOrder other) { return i.CompareTo(other.i); }
		public static bool operator ==(AdornerOrder l, AdornerOrder r) { return l.i == r.i; }
		public static bool operator !=(AdornerOrder l, AdornerOrder r) { return l.i != r.i; }
		public static bool operator <(AdornerOrder l, AdornerOrder r) { return l.i < r.i; }
		public static bool operator <=(AdornerOrder l, AdornerOrder r) { return l.i <= r.i; }
		public static bool operator >(AdornerOrder l, AdornerOrder r) { return l.i > r.i; }
		public static bool operator >=(AdornerOrder l, AdornerOrder r) { return l.i >= r.i; }
	}
}
