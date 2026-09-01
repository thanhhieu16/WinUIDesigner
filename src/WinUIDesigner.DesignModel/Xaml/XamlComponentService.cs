using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.DesignModel.Xaml
{
    public sealed class XamlComponentService : IComponentService
    {
        public event EventHandler<DesignItemPropertyChangedEventArgs> PropertyChanged;

        sealed class IdentityEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly IdentityEqualityComparer Instance = new IdentityEqualityComparer();
            int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
            bool IEqualityComparer<object>.Equals(object x, object y) => x == y;
        }

        readonly XamlDesignContext _context;

        public XamlComponentService(XamlDesignContext context)
        {
            this._context = context;
        }

        public event EventHandler<DesignItemEventArgs> ComponentRegisteredAndAddedToContainer;
        public event EventHandler<DesignItemEventArgs> ComponentRegistered;
        public event EventHandler<DesignItemEventArgs> ComponentRemoved;

        Dictionary<object, XamlDesignItem> _sites = new Dictionary<object, XamlDesignItem>(IdentityEqualityComparer.Instance);

        public DesignItem GetDesignItem(object component)
        {
            return GetDesignItem(component, false);
        }

        public DesignItem GetDesignItem(object component, bool findByView)
        {
            if (component == null)
                throw new ArgumentNullException("component");
            XamlDesignItem site;
            _sites.TryGetValue(component, out site);

            if (findByView) {
                site = site ?? _sites.Values.FirstOrDefault(x => Equals(x.View, component));
            }
            return site;
        }

        public DesignItem RegisterComponentForDesigner(object component)
        {
            return RegisterComponentForDesigner(null, component);
        }

        public DesignItem RegisterComponentForDesigner(DesignItem parent, object component)
        {
            // The reference substitutes System.Windows.Markup.NullExtension/TypeExtension here
            // for a null/Type argument. WinUI has no {x:Type} markup extension at all (confirmed
            // absent from Microsoft.UI.Xaml.winmd during Phase 1 planning), so there is nothing to
            // substitute for a Type argument - that branch is dropped, not ported. For a null
            // argument, this codebase's own NullExtensionPlaceholder (Phase 1's real stand-in for
            // {x:Null}) is the direct equivalent of the reference's NullExtension.
            if (component == null) {
                component = new NullExtensionPlaceholder();
            }

            XamlObject parentXamlObject = null;
            if (parent != null)
                parentXamlObject = ((XamlDesignItem)parent).XamlObject;

            XamlDesignItem item = new XamlDesignItem(_context.Document.CreateObject(parentXamlObject, component), _context);
            _context.Services.ExtensionManager.ApplyDesignItemInitializers(item);

            if (!(component is string))
                RegisterSite(component, item);
            if (ComponentRegistered != null) {
                ComponentRegistered(this, new DesignItemEventArgs(item));
            }
            return item;
        }

        internal void RaiseComponentRegisteredAndAddedToContainer(DesignItem obj)
        {
            if (ComponentRegisteredAndAddedToContainer != null) {
                ComponentRegisteredAndAddedToContainer(this, new DesignItemEventArgs(obj));
            }
        }

        /// <summary>
        /// Registers components from an existing XAML tree.
        /// </summary>
        public XamlDesignItem RegisterXamlComponentRecursive(XamlObject obj)
        {
            if (obj == null) return null;

            foreach (XamlProperty prop in obj.Properties) {
                RegisterXamlComponentRecursive(prop.PropertyValue as XamlObject);
                foreach (XamlPropertyValue val in prop.CollectionElements) {
                    RegisterXamlComponentRecursive(val as XamlObject);
                }
            }

            XamlDesignItem site = new XamlDesignItem(obj, _context);
            _context.Services.ExtensionManager.ApplyDesignItemInitializers(site);

            RegisterSite(site.Component, site);
            if (ComponentRegistered != null) {
                ComponentRegistered(this, new DesignItemEventArgs(site));
            }

            // Gap 4: the reference resolves duplicate-name-on-paste via WPF's real INameScope
            // object (nameScope.FindName/.RegisterName). Phase 1 has no INameScope - only a flat
            // per-document name table (WinUIDesigner.XamlDom.NameScopeHelper) - so this queries
            // that table directly instead. Setting site.Name below already routes through
            // XamlObject.SetXamlAttribute, which calls NameScopeHelper.NameChanged itself
            // (verified in src/WinUIDesigner.XamlDom/XamlObject.cs) - no separate "register" step
            // is needed here the way the reference needed nameScope.RegisterName.
            if (_context.RootItem != null && !string.IsNullOrEmpty(site.Name)) {
                var document = _context.Document;
                string newName = site.Name;
                if (NameScopeHelper.FindNamedObject(document, newName) != null) {
                    int copyIndex = newName.LastIndexOf("_Copy", StringComparison.Ordinal);
                    if (copyIndex < 0) {
                        newName += "_Copy";
                    } else if (!newName.EndsWith("_Copy", StringComparison.Ordinal)) {
                        string copyEnd = newName.Substring(copyIndex + "_Copy".Length);
                        int copyEndValue;
                        if (Int32.TryParse(copyEnd, out copyEndValue))
                            newName = newName.Remove(copyIndex + "_Copy".Length);
                        else
                            newName += "_Copy";
                    }

                    int i = 1;
                    string newNameTemplate = newName;
                    while (NameScopeHelper.FindNamedObject(document, newName) != null) {
                        newName = newNameTemplate + i++;
                    }

                    site.Name = newName;
                }
            }
            return site;
        }

        /// <summary>
        /// Gap 5: XamlObject.Instance can be null when TemplateHelper.GetFrameworkTemplate
        /// fails to find a FrameworkTemplate in a wrapped template fragment (see TemplateHelper.cs).
        /// The reference's _sites.Add(site.Component, site) would throw ArgumentNullException on
        /// this - a Dictionary key can't be null. Refuse registration with a clear, specific
        /// exception instead of letting an unrelated NullReferenceException/ArgumentNullException
        /// surface deeper in the dictionary. See Task 4 for whether this path is reachable in
        /// practice under the current calling contract - it's kept defensively either way.
        /// </summary>
        void RegisterSite(object component, XamlDesignItem site)
        {
            if (component == null) {
                throw new DesignerException(
                    "Cannot register a design item whose Component is null " +
                    "(the underlying XamlObject.Instance was null - typically a template " +
                    "fragment that failed to produce a FrameworkTemplate).");
            }
            _sites.Add(component, site);
        }

        internal void RaisePropertyChanged(XamlModelProperty property, object oldValue, object newValue)
        {
            var ev = this.PropertyChanged;
            if (ev != null) {
                ev(this, new DesignItemPropertyChangedEventArgs(property.DesignItem, property, oldValue, newValue));
            }
        }

        internal void RaiseComponentRemoved(DesignItem item)
        {
            var ev = this.ComponentRemoved;
            if (ev != null) {
                ev(this, new DesignItemEventArgs(item));
            }
        }
    }
}
