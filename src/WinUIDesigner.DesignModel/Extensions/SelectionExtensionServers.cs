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

namespace WinUIDesigner.DesignModel.Extensions
{
	public class PrimarySelectionExtensionServer : DefaultExtensionServer
	{
		DesignItem oldPrimarySelection;

		protected override void OnInitialized()
		{
			base.OnInitialized();
			this.Services.Selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
		}

		void OnPrimarySelectionChanged(object sender, EventArgs e)
		{
			DesignItem newPrimarySelection = this.Services.Selection.PrimarySelection;
			if (oldPrimarySelection != newPrimarySelection) {
				if (oldPrimarySelection == null) {
					ReapplyExtensions(new DesignItem[] { newPrimarySelection });
				} else if (newPrimarySelection == null) {
					ReapplyExtensions(new DesignItem[] { oldPrimarySelection });
				} else {
					ReapplyExtensions(new DesignItem[] { oldPrimarySelection, newPrimarySelection });
				}
				oldPrimarySelection = newPrimarySelection;
			}
		}

		public override bool ShouldApplyExtensions(DesignItem extendedItem)
		{
			return Services.Selection.PrimarySelection == extendedItem;
		}
	}

	/// <summary>Applies an extension to the primary selection only when exactly one item is
	/// selected - used by the resize/rotate/skew thumb extensions (Task 4/5), which don't make
	/// sense for a multi-selection. No WPF-specific API - mechanical port.</summary>
	public class OnlyOneItemSelectedExtensionServer : PrimarySelectionExtensionServer
	{
		protected override void OnInitialized()
		{
			base.OnInitialized();
			this.Services.Selection.SelectionChanged += OnSelectionChanged;
		}

		void OnSelectionChanged(object sender, System.EventArgs e)
		{
			if (this.Services.Selection.SelectedItems.Count > 1)
				ReapplyExtensions(this.Services.Selection.SelectedItems);
		}

		public override bool ShouldApplyExtensions(DesignItem extendedItem)
		{
			return Services.Selection.PrimarySelection == extendedItem && Services.Selection.SelectionCount < 2;
		}
	}

	/// <summary>Applies an extension to whichever DesignItem is the primary selection's
	/// immediate parent - used by GridAdornerProvider (Task 11), so its rails show whether the
	/// Grid or one of its direct children is selected. Ported from the reference unchanged;
	/// DesignItem.ParentChanged already exists (Phase 2), no WPF-specific API.</summary>
	public class PrimarySelectionParentExtensionServer : DefaultExtensionServer
	{
		DesignItem primarySelection;
		DesignItem primarySelectionParent;

		protected override void OnInitialized()
		{
			base.OnInitialized();
			this.Services.Selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
		}

		void OnPrimarySelectionChanged(object sender, EventArgs e)
		{
			DesignItem newPrimarySelection = this.Services.Selection.PrimarySelection;
			if (primarySelection != newPrimarySelection) {
				if (primarySelection != null) primarySelection.ParentChanged -= OnParentChanged;
				if (newPrimarySelection != null) newPrimarySelection.ParentChanged += OnParentChanged;
				primarySelection = newPrimarySelection;
				OnParentChanged(sender, e);
			}
		}

		void OnParentChanged(object sender, EventArgs e)
		{
			DesignItem newPrimarySelectionParent = primarySelection != null ? primarySelection.Parent : null;
			if (primarySelectionParent != newPrimarySelectionParent) {
				DesignItem oldPrimarySelectionParent = primarySelectionParent;
				primarySelectionParent = newPrimarySelectionParent;
				ReapplyExtensions(new DesignItem[] { oldPrimarySelectionParent, newPrimarySelectionParent });
			}
		}

		public override bool ShouldApplyExtensions(DesignItem extendedItem)
		{
			return primarySelectionParent == extendedItem;
		}
	}
}
