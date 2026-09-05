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
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.DesignModel.PropertyGrid
{
    /// <summary>
    /// View-model for one property row, ported from the reference's PropertyNode.cs
    /// (WpfDesign/Project/PropertyGrid/PropertyNode.cs, 456 lines). Trimmed for Phase 4f MVP
    /// scope (spec Decisions 12-13): no Description/IPropertyDescriptionService (no such service
    /// exists in this port), no CreateBinding()/NameForeground binding-value coloring (no
    /// binding/resource-value editing this phase - ValueString's MarkupExtension special case is
    /// simplified to a flat "has a complex sub-object => show its type name" check), no
    /// TextBlock.Text/Inlines special case (Known gap, not resolved this phase). Name uses
    /// DesignItemProperty.IsAttachedProperty (Task 1) instead of the reference's
    /// DependencyPropertyDescriptor (no WinUI equivalent).
    /// </summary>
    public class PropertyNode : INotifyPropertyChanged
    {
        static readonly object Unset = new object();

        public IReadOnlyList<DesignItemProperty> Properties { get; private set; }

        bool raiseEvents = true;
        bool hasStringConverter;

        public string Name {
            get {
                var first = FirstProperty;
                if (first.IsAttachedProperty)
                    return first.DeclaringType.Name + "." + first.Name;
                return first.Name;
            }
        }

        public FrameworkElement Editor { get; private set; }

        public DesignItemProperty FirstProperty { get { return Properties[0]; } }

        public PropertyNode Parent { get; private set; }

        public int Level { get; private set; }

        public Category Category { get; set; }

        public ObservableCollection<PropertyNode> Children { get; private set; }

        public ObservableCollection<PropertyNode> MoreChildren { get; private set; }

        bool isExpanded;

        public bool IsExpanded {
            get { return isExpanded; }
            set {
                isExpanded = value;
                UpdateChildren();
                RaisePropertyChanged("IsExpanded");
            }
        }

        public bool HasChildren {
            get { return Children.Count > 0 || MoreChildren.Count > 0; }
        }

        public object DesignerValue {
            get {
                if (IsAmbiguous) return null;
                return FirstProperty.DesignerValue;
            }
            set { SetValueCore(value); }
        }

        public object Value {
            get {
                if (IsAmbiguous) return null;
                return FirstProperty.ValueOnInstance;
            }
            set { SetValueCore(value); }
        }

        public string ValueString {
            get {
                var item = ValueItem;
                if (item != null) return "(" + item.ComponentType.Name + ")";
                var value = DesignerValue;
                if (value == null) return null;
                if (hasStringConverter)
                    return FirstProperty.TypeConverter.ConvertToInvariantString(value);
                return "(" + value.GetType().Name + ")";
            }
            set {
                DesignerValue = FirstProperty.TypeConverter.ConvertFromInvariantString(value);
            }
        }

        public bool IsEnabled {
            get { return ValueItem == null && hasStringConverter; }
        }

        public bool IsSet {
            get {
                foreach (var p in Properties) {
                    if (p.IsSet) return true;
                }
                return false;
            }
        }

        public DesignItem ValueItem {
            get {
                if (Properties.Count == 1) return FirstProperty.Value;
                return null;
            }
        }

        public bool IsAmbiguous {
            get {
                foreach (var p in Properties) {
                    if (p != FirstProperty && !object.Equals(p.ValueOnInstance, FirstProperty.ValueOnInstance))
                        return true;
                }
                return false;
            }
        }

        bool isVisible;

        public bool IsVisible {
            get { return isVisible; }
            set {
                isVisible = value;
                RaisePropertyChanged("IsVisible");
            }
        }

        public bool CanReset { get { return IsSet; } }

        public void Reset()
        {
            SetValueCore(Unset);
        }

        void SetValueCore(object value)
        {
            raiseEvents = false;
            if (value == Unset) {
                foreach (var p in Properties) p.Reset();
            } else {
                foreach (var p in Properties) p.SetValue(value);
            }
            raiseEvents = true;
            OnValueChanged();
        }

        void OnValueChanged()
        {
            RaisePropertyChanged("IsSet");
            RaisePropertyChanged("Value");
            RaisePropertyChanged("DesignerValue");
            RaisePropertyChanged("ValueString");
            RaisePropertyChanged("IsAmbiguous");
            RaisePropertyChanged("IsEnabled");
            UpdateChildren();
        }

        void OnValueOnInstanceChanged()
        {
            RaisePropertyChanged("Value");
            RaisePropertyChanged("DesignerValue");
            RaisePropertyChanged("ValueString");
        }

        public PropertyNode()
        {
            Children = new ObservableCollection<PropertyNode>();
            MoreChildren = new ObservableCollection<PropertyNode>();
        }

        public PropertyNode(DesignItemProperty[] properties, PropertyNode parent) : this()
        {
            this.Parent = parent;
            this.Level = parent == null ? 0 : parent.Level + 1;
            Load(properties);
        }

        public void Load(DesignItemProperty[] properties)
        {
            if (this.Properties != null) {
                foreach (var property in this.Properties) {
                    property.ValueChanged -= Property_ValueChanged;
                    property.ValueOnInstanceChanged -= Property_ValueOnInstanceChanged;
                }
            }

            this.Properties = new List<DesignItemProperty>(properties);

            if (Editor == null) {
                Editor = EditorManager.CreateEditor(FirstProperty);
                Editor.DataContext = this;
            }

            foreach (var property in properties) {
                property.ValueChanged += Property_ValueChanged;
                property.ValueOnInstanceChanged += Property_ValueOnInstanceChanged;
            }

            hasStringConverter =
                FirstProperty.TypeConverter.CanConvertFrom(typeof(string)) &&
                FirstProperty.TypeConverter.CanConvertTo(typeof(string));

            OnValueChanged();
        }

        void Property_ValueOnInstanceChanged(object sender, EventArgs e)
        {
            if (raiseEvents) OnValueOnInstanceChanged();
        }

        void Property_ValueChanged(object sender, EventArgs e)
        {
            if (raiseEvents) OnValueChanged();
        }

        void UpdateChildren()
        {
            Children.Clear();
            MoreChildren.Clear();

            if (Parent == null || Parent.IsExpanded) {
                var item = ValueItem;
                if (item != null) {
                    var service = item.Services.GetService<IComponentPropertyService>();
                    var list = service.GetAvailableProperties(item)
                        .OrderBy(d => d.Name)
                        .Select(d => new PropertyNode(new[] { item.Properties.GetProperty(d.Name) }, this));

                    foreach (var node in list) {
                        if (Metadata.IsBrowsable(node.FirstProperty)) {
                            node.IsVisible = true;
                            if (Metadata.IsPopularProperty(node.FirstProperty))
                                Children.Add(node);
                            else
                                MoreChildren.Add(node);
                        }
                    }
                }
            }

            RaisePropertyChanged("HasChildren");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void RaisePropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
