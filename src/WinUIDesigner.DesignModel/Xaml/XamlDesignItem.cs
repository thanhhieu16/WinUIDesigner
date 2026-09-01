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
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using WinUIDesigner.XamlDom;
using WinUIDesigner.DesignModel.Services;

namespace WinUIDesigner.DesignModel.Xaml
{
    [DebuggerDisplay("XamlDesignItem: {ComponentType.Name}")]
    public sealed class XamlDesignItem : DesignItem
    {
        readonly XamlObject _xamlObject;
        readonly XamlDesignContext _designContext;
        readonly XamlModelPropertyCollection _properties;
        UIElement _view;

        public XamlDesignItem(XamlObject xamlObject, XamlDesignContext designContext)
        {
            this._xamlObject = xamlObject;
            this._designContext = designContext;
            this._properties = new XamlModelPropertyCollection(this);
        }

        internal XamlComponentService ComponentService {
            get { return _designContext._componentService; }
        }

        public XamlObject XamlObject {
            get { return _xamlObject; }
        }

        public override object Component {
            get { return _xamlObject.Instance; }
        }

        public override Type ComponentType {
            get { return _xamlObject.ElementType; }
        }

        void SetNameInternal(string newName)
        {
            var oldName = Name;
            _xamlObject.Name = newName;
            FixDesignItemReferencesOnNameChange(oldName, Name);
        }

        public override string Name {
            get { return _xamlObject.Name; }
            set {
                UndoService undoService = this.Services.GetService<UndoService>();
                if (undoService != null)
                    undoService.Execute(new SetNameAction(this, value));
                else
                    SetNameInternal(value);
            }
        }

        /// <summary>
        /// Fixes {x:Reference} and {Binding ElementName=...} references to this element
        /// elsewhere in the document when its name changes.
        /// </summary>
        public void FixDesignItemReferencesOnNameChange(string oldName, string newName)
        {
            if (!string.IsNullOrEmpty(oldName) && !string.IsNullOrEmpty(newName)) {
                var root = GetRootXamlObject(this.XamlObject);

                var references = GetAllChildXamlObjects(root).Where(x =>
                    x.ElementType == typeof(ReferenceExtensionPlaceholder) &&
                    Equals(x.FindOrCreateProperty("Name").GetValueOnInstance<string>(), oldName));
                foreach (var designItem in references) {
                    var property = designItem.FindOrCreateProperty("Name");
                    var propertyValue = designItem.OwnerDocument.CreatePropertyValue(newName, property);
                    this.ComponentService.RegisterXamlComponentRecursive(propertyValue as XamlObject);
                    property.PropertyValue = propertyValue;
                }

                var bindings = GetAllChildXamlObjects(root).Where(x =>
                    x.ElementType == typeof(Microsoft.UI.Xaml.Data.Binding) &&
                    Equals(x.FindOrCreateProperty("ElementName").GetValueOnInstance<string>(), oldName));
                foreach (var designItem in bindings) {
                    var property = designItem.FindOrCreateProperty("ElementName");
                    var propertyValue = designItem.OwnerDocument.CreatePropertyValue(newName, property);
                    this.ComponentService.RegisterXamlComponentRecursive(propertyValue as XamlObject);
                    property.PropertyValue = propertyValue;
                }
            }
        }

        internal static XamlObject GetRootXamlObject(XamlObject item)
        {
            var root = item;
            while (root.ParentObject != null)
                root = root.ParentObject;
            return root;
        }

        internal static IEnumerable<XamlObject> GetAllChildXamlObjects(XamlObject item)
        {
            foreach (var prop in item.Properties) {
                var propValue = prop.PropertyValue as XamlObject;
                if (propValue != null) {
                    yield return propValue;
                    foreach (var i in GetAllChildXamlObjects(propValue))
                        yield return i;
                }

                if (prop.IsCollection) {
                    foreach (var collectionElement in prop.CollectionElements) {
                        var ce = collectionElement as XamlObject;
                        if (ce != null) {
                            yield return ce;
                            foreach (var i in GetAllChildXamlObjects(ce))
                                yield return i;
                        }
                    }
                }
            }
        }

        public override string Key {
            get { return XamlObject.GetXamlAttribute("Key"); }
            set { XamlObject.SetXamlAttribute("Key", value); }
        }

        public override event EventHandler NameChanged {
            add { _xamlObject.NameChanged += value; }
            remove { _xamlObject.NameChanged -= value; }
        }

        public override DesignItem Parent {
            get {
                if (_xamlObject.ParentProperty == null)
                    return null;
                else
                    return ComponentService.GetDesignItem(_xamlObject.ParentProperty.ParentObject.Instance);
            }
        }

        public override DesignItemProperty ParentProperty {
            get {
                DesignItem parent = this.Parent;
                if (parent == null)
                    return null;
                XamlProperty prop = _xamlObject.ParentProperty;
                if (prop.IsAttached) {
                    return parent.Properties.GetAttachedProperty(prop.PropertyTargetType, prop.PropertyName);
                } else {
                    return parent.Properties.GetProperty(prop.PropertyName);
                }
            }
        }

        public override event EventHandler ParentChanged {
            add { _xamlObject.ParentPropertyChanged += value; }
            remove { _xamlObject.ParentPropertyChanged -= value; }
        }

        public override UIElement View {
            get {
                if (_view != null)
                    return _view;
                else
                    return this.Component as UIElement;
            }
        }

        public override void SetView(UIElement newView)
        {
            _view = newView;
        }

        public override DesignContext Context {
            get { return _designContext; }
        }

        public override DesignItemPropertyCollection Properties {
            get { return _properties; }
        }

        public override IEnumerable<DesignItemProperty> AllSetProperties {
            get { return _xamlObject.Properties.Select(x => new XamlModelProperty(this, x)); }
        }

        internal void NotifyPropertyChanged(XamlModelProperty property, object oldValue, object newValue)
        {
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(property.Name));
            ((XamlComponentService)this.Services.Component).RaisePropertyChanged(property, oldValue, newValue);
        }

        public override string ContentPropertyName {
            get { return XamlObject.ContentPropertyName; }
        }

        /// <summary>
        /// Item is Locked at Design Time.
        /// </summary>
        public bool IsDesignTimeLocked {
            get {
                var locked = Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsLocked").GetConvertedValueOnInstance<object>();
                return (locked != null && (bool)locked == true);
            }
            set {
                if (value)
                    Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsLocked").SetValue(true);
                else
                    Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsLocked").Reset();
            }
        }

        public override DesignItem Clone()
        {
            DesignItem item = null;
            var xaml = XamlStaticTools.GetXaml(this.XamlObject);
            XamlDesignItem rootItem = Context.RootItem as XamlDesignItem;
            var obj = XamlParser.ParseSnippet(rootItem.XamlObject, xaml, ((XamlDesignContext)Context).ParserSettings);
            if (obj != null) {
                item = ((XamlDesignContext)Context)._componentService.RegisterXamlComponentRecursive(obj);
            }
            return item;
        }

        sealed class SetNameAction : ITransactionItem
        {
            XamlDesignItem designItem;
            string oldName;
            string newName;

            public SetNameAction(XamlDesignItem designItem, string newName)
            {
                this.designItem = designItem;
                this.newName = newName;
                oldName = designItem.Name;
            }

            public string Title { get { return "Set name"; } }

            public void Do() { designItem.SetNameInternal(newName); }
            public void Undo() { designItem.SetNameInternal(oldName); }

            public ICollection<DesignItem> AffectedElements {
                get { return new DesignItem[] { designItem }; }
            }

            public bool MergeWith(ITransactionItem other)
            {
                SetNameAction o = other as SetNameAction;
                if (o != null && designItem == o.designItem) {
                    newName = o.newName;
                    return true;
                }
                return false;
            }
        }
    }
}
