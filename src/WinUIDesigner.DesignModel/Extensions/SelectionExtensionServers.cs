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
using System.Linq;

namespace WinUIDesigner.DesignModel.Extensions
{
	/// <summary>Applies an extension to every selected item, not only the primary one. Ported
	/// from the reference (WpfDesign/Project/Extensions/SelectionExtensionServers.cs), where it
	/// backs the per-item selection rectangle. Phase 3 dropped it for want of a consumer; Select
	/// All (Ctrl+A) is that consumer - without it a multi-selection highlighted only its primary
	/// item, so the walkthrough's two selected buttons showed one frame between them.</summary>
	public class SelectionExtensionServer : DefaultExtensionServer
	{
		protected override void OnInitialized()
		{
			base.OnInitialized();
			this.Services.Selection.SelectionChanged += OnSelectionChanged;
		}

		void OnSelectionChanged(object sender, DesignItemCollectionEventArgs e)
		{
			// e.Items holds every item whose selected state changed, in either direction - and
			// can hold null, since DefaultSelectionService adds the old and new primary selection
			// to it and either may be absent.
			ReapplyExtensions(e.Items.Where(item => item != null).ToArray());
		}

		public override bool ShouldApplyExtensions(DesignItem extendedItem)
		{
			return Services.Selection.IsComponentSelected(extendedItem);
		}
	}

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
	/// sense for a multi-selection. No WPF-specific API - mechanical port.
	///
	/// Phase 4c Task 8 fix: OnSelectionChanged originally mirrored the reference exactly
	/// (WpfDesign/Project/Extensions/SelectionExtensionServers.cs:181-185), including its
	/// `if (SelectedItems.Count > 1)` guard - reapplying only on the "grow to multi-select"
	/// transition. That guard silently drops the opposite transition: shrinking a two-item
	/// selection back down to one *without* changing which item is primary (e.g.
	/// SetSelectedComponents(new[] { primaryItem })) doesn't fire PrimarySelectionChanged
	/// either (DefaultSelectionService/the reference's DefaultSelectionService both only raise
	/// it when the primary item itself changes), so with the guard in place no code path ever
	/// re-evaluates ShouldApplyExtensions and the extension stays detached. Caught by this
	/// port's own ArrangeWrapUnwrapContextMenuExtensionTests.WrapItemExtensionOnlyAttachesWith
	/// ExactlyOneSelected exercising exactly that shrink transition - a scenario no earlier
	/// test (Task 4/5's resize/rotate/skew thumb tests, Task 7's right-click tests) exercised.
	/// Fixed by dropping the guard, matching the already-correct, already-tested sibling
	/// PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer.OnSelectionChanged below,
	/// which reapplies unconditionally on every SelectionChanged and handles both directions.
	/// Unconditional reapply is safe here too: ReapplyExtensionServer only actually attaches/
	/// detaches when ShouldApplyExtensions's result differs from the currently-applied state,
	/// so the extra calls for the "nothing changed" case are no-ops.</summary>
	public class OnlyOneItemSelectedExtensionServer : PrimarySelectionExtensionServer
	{
		protected override void OnInitialized()
		{
			base.OnInitialized();
			this.Services.Selection.SelectionChanged += OnSelectionChanged;
		}

		void OnSelectionChanged(object sender, System.EventArgs e)
		{
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

	/// <summary>Applies an extension to the primary selection only when 2+ items are
	/// selected - used by ArrangeItemsContextMenuExtension/WrapItemsContextMenuExtension
	/// (Phase 4c, Task 8), which don't make sense for a single-item selection. No
	/// WPF-specific API - mechanical port. The reference additionally overrides
	/// ShouldBeReApplied() => true (WpfDesign/Project/Extensions/SelectionExtensionServers.cs:125-128);
	/// this port skips it, matching OnlyOneItemSelectedExtensionServer's own precedent above,
	/// which achieves reapplication directly through its OnSelectionChanged handler instead.</summary>
	public class PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer : PrimarySelectionExtensionServer
	{
		protected override void OnInitialized()
		{
			base.OnInitialized();
			this.Services.Selection.SelectionChanged += OnSelectionChanged;
		}

		void OnSelectionChanged(object sender, System.EventArgs e)
		{
			ReapplyExtensions(this.Services.Selection.SelectedItems);
		}

		public override bool ShouldApplyExtensions(DesignItem extendedItem)
		{
			return Services.Selection.PrimarySelection == extendedItem && Services.Selection.SelectionCount > 1;
		}
	}
}
