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

using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.DesignModel.Adorners
{
	public abstract class AdornerProvider : DefaultExtension
	{
		sealed class AdornerPanelCollection : Collection<AdornerPanel>
		{
			readonly AdornerProvider _provider;
			internal AdornerPanelCollection(AdornerProvider provider) { _provider = provider; }
			protected override void InsertItem(int index, AdornerPanel item) { base.InsertItem(index, item); _provider.OnAdornerAdd(item); }
			protected override void RemoveItem(int index) { _provider.OnAdornerRemove(base[index]); base.RemoveItem(index); }
			protected override void SetItem(int index, AdornerPanel item) { _provider.OnAdornerRemove(base[index]); base.SetItem(index, item); _provider.OnAdornerAdd(item); }
			protected override void ClearItems() { foreach (var v in this) _provider.OnAdornerRemove(v); base.ClearItems(); }
		}

		readonly AdornerPanelCollection _adorners;
		bool isVisible;

		protected AdornerProvider() { _adorners = new AdornerPanelCollection(this); }

		protected override void OnInitialized()
		{
			base.OnInitialized();
			isVisible = true;
			foreach (var v in _adorners) OnAdornerAdd(v);
		}

		protected override void OnRemove()
		{
			base.OnRemove();
			foreach (var v in _adorners) OnAdornerRemove(v);
			isVisible = false;
		}

		public Collection<AdornerPanel> Adorners { get { return _adorners; } }

		protected void AddAdorner(AdornerPlacement placement, AdornerOrder order, UIElement adorner)
		{
			var p = new AdornerPanel { Order = order };
			AdornerPanel.SetPlacement(adorner, placement);
			p.Children.Add(adorner);
			this.Adorners.Add(p);
		}

		protected void AddAdorners(AdornerPlacement placement, params UIElement[] adorners)
		{
			var p = new AdornerPanel();
			foreach (var adorner in adorners) {
				AdornerPanel.SetPlacement(adorner, placement);
				p.Children.Add(adorner);
			}
			this.Adorners.Add(p);
		}

		internal void OnAdornerAdd(AdornerPanel item)
		{
			if (!isVisible) return;
			item.SetAdornedElement(this.ExtendedItem.View, this.ExtendedItem);
			this.Services.GetService<IDesignPanel>().Adorners.Add(item);
		}

		internal void OnAdornerRemove(AdornerPanel item)
		{
			if (!isVisible) return;
			this.Services.GetService<IDesignPanel>().Adorners.Remove(item);
		}
	}
}
