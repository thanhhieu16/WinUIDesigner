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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.Designer.OutlineView
{
    /// <summary>
    /// Base for every outline node. Ported from the reference's OutlineNodeBase.cs. Deviations:
    /// DummyPlacementType is registered exactly once, here, rather than in two derived types'
    /// static constructors (spec Decision 11); the reference's IsVisualNode (a WPF Visibility
    /// helper for its XAML) is dropped since this port builds rows in C#; CanInsert/Insert land
    /// in Phase 4g Task 6; GetAttachedProperty takes (ownerType, name) in this port rather than
    /// the reference's (DependencyProperty).
    /// </summary>
    public abstract class OutlineNodeBase : INotifyPropertyChanged, IOutlineNode
    {
        protected abstract void UpdateChildren();
        protected abstract void UpdateChildrenCollectionChanged(NotifyCollectionChangedEventArgs e);

        /// <summary>Used only to ask a container whether an item could enter it.</summary>
        protected static readonly PlacementType DummyPlacementType = PlacementType.Register("DummyPlacement");

        bool _collectionWasChanged;
        readonly string _name;

        protected OutlineNodeBase(DesignItem designItem)
        {
            DesignItem = designItem;

            bool hidden = false;
            try {
                hidden = object.Equals(
                    designItem.Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsHidden")
                        .GetConvertedValueOnInstance<bool>(), true);
            } catch (Exception) { }
            if (hidden)
                _isDesignTimeVisible = false;

            bool locked = false;
            try {
                locked = object.Equals(
                    designItem.Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsLocked")
                        .GetConvertedValueOnInstance<bool>(), true);
            } catch (Exception) { }
            if (locked)
                _isDesignTimeLocked = true;

            DesignItem.NameChanged += DesignItem_NameChanged;

            if (DesignItem.ContentProperty != null && DesignItem.ContentProperty.IsCollection)
                DesignItem.ContentProperty.CollectionElements.CollectionChanged += CollectionElements_CollectionChanged;

            DesignItem.PropertyChanged += DesignItem_PropertyChanged;
        }

        protected OutlineNodeBase(string name)
        {
            _name = name;
        }

        public DesignItem DesignItem { get; set; }

        public virtual ServiceContainer Services {
            get { return this.DesignItem.Services; }
        }

        public ISelectionService SelectionService {
            get { return DesignItem != null ? DesignItem.Services.Selection : null; }
        }

        bool isExpanded = true;

        public bool IsExpanded {
            get { return isExpanded; }
            set {
                isExpanded = value;
                RaisePropertyChanged("IsExpanded");
            }
        }

        bool isSelected;

        public bool IsSelected {
            get { return isSelected; }
            set {
                if (isSelected != value && SelectionService != null) {
                    isSelected = value;
                    SelectionService.SetSelectedComponents(new[] { DesignItem },
                        value ? SelectionTypes.Add : SelectionTypes.Remove);
                    RaisePropertyChanged("IsSelected");
                }
            }
        }

        bool _isDesignTimeVisible = true;

        public bool IsDesignTimeVisible {
            get { return _isDesignTimeVisible; }
            set {
                _isDesignTimeVisible = value;
                RaisePropertyChanged("IsDesignTimeVisible");

                if (!value)
                    DesignItem.Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsHidden").SetValue(true);
                else
                    DesignItem.Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsHidden").Reset();
            }
        }

        bool _isDesignTimeLocked;

        public bool IsDesignTimeLocked {
            get { return _isDesignTimeLocked; }
            set {
                _isDesignTimeLocked = value;
                ((XamlDesignItem)DesignItem).IsDesignTimeLocked = _isDesignTimeLocked;
                RaisePropertyChanged("IsDesignTimeLocked");
            }
        }

        readonly ObservableCollection<IOutlineNode> children = new ObservableCollection<IOutlineNode>();

        public ObservableCollection<IOutlineNode> Children {
            get { return children; }
        }

        public string Name {
            get {
                if (!string.IsNullOrEmpty(_name))
                    return _name;
                return DesignItem.Services.GetService<IOutlineNodeNameService>().GetOutlineNodeName(DesignItem);
            }
        }

        void DesignItem_NameChanged(object sender, EventArgs e)
        {
            RaisePropertyChanged("Name");
        }

        void DesignItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // A collection change raises BOTH CollectionChanged and PropertyChanged; the flag
            // stops the second one triggering a redundant full rebuild that would throw away the
            // incremental update CollectionElements_CollectionChanged just did.
            if (!_collectionWasChanged)
                UpdateChildren();
            _collectionWasChanged = false;
        }

        void CollectionElements_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            _collectionWasChanged = true;
            UpdateChildrenCollectionChanged(e);
        }

        public bool CanInsert(IEnumerable<IOutlineNode> nodes, IOutlineNode after, bool copy)
        {
            var placementBehavior = DesignItem.GetBehavior<IPlacementBehavior>();
            if (placementBehavior == null)
                return false;
            // Nothing without a DesignItem (a PropertyOutlineNode) and nothing without a parent
            // (the document root) can be placed. PlacementOperation.Start resolves an
            // IRootPlacementBehavior for a parentless item (PlacementOperation.cs:232), and this
            // port has never implemented that interface - deferred in Phase 2's plan, never
            // revisited - so Start would throw PlacementOperationException. Returning false is
            // both the honest answer (a root has no parent to be reparented from) and what keeps
            // the exception out of Task 10's drop-veto path. The reference has no such guard
            // because its RootItemBehavior supplies the missing behavior. Spec Decision 16.
            if (nodes.Any(node => node.DesignItem == null || node.DesignItem.Parent == null))
                return false;
            var operation = PlacementOperation.Start(nodes.Select(node => node.DesignItem).ToArray(), DummyPlacementType);
            if (operation != null) {
                bool canEnter = placementBehavior.CanEnterContainer(operation, true);
                operation.Abort();
                return canEnter;
            }
            return false;
        }

        public virtual void Insert(IEnumerable<IOutlineNode> nodes, IOutlineNode after, bool copy)
        {
            using (var moveTransaction = DesignItem.Context.OpenGroup("Item moved in outline view",
                       nodes.Select(n => n.DesignItem).ToList())) {
                if (copy) {
                    nodes = nodes.Select(n => n.DesignItem.Clone().CreateOutlineNode()).ToList();
                } else {
                    foreach (var node in nodes) {
                        node.DesignItem.Remove();
                    }
                }

                var content = DesignItem.ContentProperty;
                if (content.IsCollection) {
                    // Final-review fix (F1): the insert index has to be computed in
                    // CollectionElements' own index space. It used to come from
                    // Children.IndexOf(after), but OutlineNode.UpdateChildren prefixes Children
                    // with one PropertyOutlineNode per set non-content object-valued property and
                    // CollectionElements has none of those, so the two spaces diverge by exactly
                    // that many positions: for a container with one such property and two
                    // children, an `after` naming the last child produced index 3 against a
                    // collection of 2 and Collection<T>.Insert threw ArgumentOutOfRangeException
                    // out of a pointer callback; with a non-last `after` the item silently landed
                    // N positions early.
                    //
                    // An `after` that is not in this content collection at all - a
                    // PropertyOutlineNode (no DesignItem), or a node belonging to some other
                    // container - APPENDS. That is a deliberate choice, not IndexOf's -1 falling
                    // through: `after` names an item to insert behind, so naming something this
                    // container does not hold is a caller error, and appending keeps the drop
                    // where the gesture pointed (the drag wiring aims at the end of the target's
                    // children) instead of silently PREPENDING, which is what the -1 used to do.
                    // `after == null` still means index 0, unchanged from the reference - that is
                    // the one case where the caller has said "no position" explicitly rather than
                    // by accident, and no caller in this port passes it for a non-empty target.
                    int index;
                    if (after == null) {
                        index = 0;
                    } else {
                        var afterIndex = after.DesignItem != null
                            ? content.CollectionElements.IndexOf(after.DesignItem)
                            : -1;
                        index = afterIndex < 0 ? content.CollectionElements.Count : afterIndex + 1;
                    }
                    // Defensive clamp: Insert throws outside [0, Count], and this runs inside a
                    // drag gesture's callback where an exception is not recoverable.
                    if (index < 0)
                        index = 0;
                    if (index > content.CollectionElements.Count)
                        index = content.CollectionElements.Count;

                    foreach (var node in nodes) {
                        content.CollectionElements.Insert(index++, node.DesignItem);
                    }
                } else {
                    content.SetValue(nodes.First().DesignItem);
                }
                moveTransaction.Commit();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void RaisePropertyChanged(string name)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}
