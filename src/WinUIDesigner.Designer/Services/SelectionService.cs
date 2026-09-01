using System;
using System.Collections.Generic;
using System.Linq;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Services
{
	public class DefaultSelectionService : ISelectionService
	{
		HashSet<DesignItem> _selectedComponents = new HashSet<DesignItem>();
		DesignItem _primarySelection;

		public bool IsComponentSelected(DesignItem component) { return _selectedComponents.Contains(component); }
		public ICollection<DesignItem> SelectedItems { get { return _selectedComponents.ToArray(); } }
		public DesignItem PrimarySelection { get { return _primarySelection; } }
		public int SelectionCount { get { return _selectedComponents.Count; } }

		public event EventHandler SelectionChanging;
		public event EventHandler<DesignItemCollectionEventArgs> SelectionChanged;
		public event EventHandler PrimarySelectionChanging;
		public event EventHandler PrimarySelectionChanged;

		public void SetSelectedComponents(ICollection<DesignItem> components)
		{
			SetSelectedComponents(components, SelectionTypes.Replace);
		}

		public virtual void SetSelectedComponents(ICollection<DesignItem> components, SelectionTypes selectionType)
		{
			if (components == null) components = new DesignItem[0];
			if (components.Contains(null)) throw new ArgumentException("Cannot select 'null'.");

			var prevSelectedItems = _selectedComponents.ToArray();
			if (SelectionChanging != null) SelectionChanging(this, EventArgs.Empty);

			DesignItem newPrimarySelection = _primarySelection;

			if (selectionType == SelectionTypes.Auto) {
				selectionType = ResolveAutoSelectionType();
			}

			if ((selectionType & SelectionTypes.Primary) == SelectionTypes.Primary) {
				newPrimarySelection = null;
				foreach (var obj in components) { newPrimarySelection = obj; break; }

				selectionType &= ~SelectionTypes.Primary;
				if (selectionType == 0) {
					if (components.Count == 1 && IsComponentSelected(newPrimarySelection) && prevSelectedItems.Length == 1) {
						// keep selectionType = 0 -> don't change the selection
					} else {
						selectionType = SelectionTypes.Replace;
					}
				}
			}

			var componentsToNotify = new HashSet<DesignItem>();
			switch (selectionType) {
				case SelectionTypes.Add:
					foreach (var obj in components) if (_selectedComponents.Add(obj)) componentsToNotify.Add(obj);
					break;
				case SelectionTypes.Remove:
					foreach (var obj in components) if (_selectedComponents.Remove(obj)) componentsToNotify.Add(obj);
					break;
				case SelectionTypes.Replace:
					componentsToNotify.UnionWith(_selectedComponents);
					_selectedComponents.Clear();
					foreach (var obj in components) { _selectedComponents.Add(obj); componentsToNotify.Add(obj); }
					break;
				case SelectionTypes.Toggle:
					foreach (var obj in components) {
						if (_selectedComponents.Contains(obj)) _selectedComponents.Remove(obj);
						else _selectedComponents.Add(obj);
						componentsToNotify.Add(obj);
					}
					break;
				case 0:
					break;
				default:
					throw new NotSupportedException("The selection type " + selectionType + " is not supported");
			}

			if (!IsComponentSelected(newPrimarySelection)) {
				newPrimarySelection = null;
				foreach (var obj in _selectedComponents) { newPrimarySelection = obj; break; }
			}

			if (newPrimarySelection != _primarySelection) {
				componentsToNotify.Add(_primarySelection);
				componentsToNotify.Add(newPrimarySelection);
				if (PrimarySelectionChanging != null) PrimarySelectionChanging(this, EventArgs.Empty);
				_primarySelection = newPrimarySelection;
				if (PrimarySelectionChanged != null) PrimarySelectionChanged(this, EventArgs.Empty);
			}

			if (!_selectedComponents.SequenceEqual(prevSelectedItems)) {
				if (SelectionChanged != null) SelectionChanged(this, new DesignItemCollectionEventArgs(componentsToNotify));
			}
		}

		// Interface completeness for SelectionTypes.Auto (spec: DesignPanel's own call path never
		// passes Auto - it resolves modifiers from the triggering pointer event instead, see
		// Task 3 Step 3). Uses the verified global alternative
		// (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread, confirmed present
		// in Microsoft.UI.winmd during spec brainstorming) rather than leaving Auto unimplemented.
		// No try/catch here deliberately (Global Constraints: no swallowed exception that hides a
		// platform difference) - Step 3 below verifies empirically whether this call works in this
		// project's unpackaged-desktop configuration, and if not, this method's body is replaced
		// with a plain `return SelectionTypes.Primary;` plus a comment recording that finding.
		//
		// Empirically confirmed (Phase 3, Task 4): InputKeyboardSource.GetKeyStateForCurrentThread
		// does NOT throw in this project's unpackaged-desktop (WindowsPackageType=None)
		// configuration - DefaultSelectionServiceTests.AutoWithNoModifiersHeldResolvesToPrimarySelectionBehavior
		// exercises this exact call on the live WinUI dispatcher thread and passes. The one build-time
		// fix needed was the return type's namespace: GetKeyStateForCurrentThread returns
		// Windows.UI.Core.CoreVirtualKeyStates, not Microsoft.UI.Input.CoreVirtualKeyStates (the
		// latter does not exist - CS0234 at build) - a copy-compile-fix detail, not a platform gap.
		static SelectionTypes ResolveAutoSelectionType()
		{
			var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
			var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
			bool ctrlDown = (ctrl & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
			bool shiftDown = (shift & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
			if (ctrlDown) return SelectionTypes.Toggle;
			if (shiftDown) return SelectionTypes.Add;
			return SelectionTypes.Primary;
		}
	}
}
