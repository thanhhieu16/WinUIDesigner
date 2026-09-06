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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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

        public event PropertyChangedEventHandler PropertyChanged;

        public void RaisePropertyChanged(string name)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}
