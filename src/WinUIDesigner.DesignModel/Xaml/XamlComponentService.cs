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

        public void SetDefaultPropertyValues(DesignItem designItem)
        {
            var values = Metadata.GetDefaultPropertyValues(designItem.ComponentType);
            if (values != null) {
                foreach (var value in values) {
                    designItem.Properties[value.Key].SetValue(value.Value);
                }
            }
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

            // Final-review fix: without this guard, a Type argument fell through silently to
            // Document.CreateObject(parent, typeof(SomeType)) and produced a XamlObject wrapping
            // a useless System.RuntimeType instance instead of failing loudly. Per the plan's
            // Global Constraints ("no silent fallback, no swallowed exception that hides a
            // platform difference"), refuse it explicitly instead.
            if (component is Type) {
                throw new NotSupportedException(
                    "WinUI has no {x:Type} markup extension equivalent - registering a raw System.Type as a design item is not supported.");
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
            //
            // Aliasing hazard (found in final-review pass): NameScopeHelper's table is a flat,
            // last-writer-wins map (WinUIDesigner.XamlDom/NameScopeHelper.cs). DesignItem.Clone()
            // re-parses the clone's XAML text - which still carries the *original* element's
            // x:Name - into the SAME XamlDocument (XamlParser.ParseSnippet sets its parser's
            // document to root.OwnerDocument). XamlParser.ParseObject writes straight to the
            // table the instant it sees an x:Name attribute (before this method ever runs),
            // so by the time we get here document.NamedObjects[name] may already have been
            // silently overwritten to point at `obj` itself - a "self-hit" that looks like "no
            // collision" once renamed away, even though the ORIGINAL element elsewhere in the
            // tree still carries that same x:Name in its own XML (XamlObject.Name reads that
            // attribute directly and is unaffected by the table - see XamlObject.cs). Trusting
            // NameScopeHelper.FindNamedObject for the collision check is therefore unreliable
            // here: it can never distinguish "this name is genuinely free" from "this name's
            // table slot was just stolen by the very object we're registering". So the search
            // below walks the live, already-attached document tree and compares each element's
            // own Name property directly (the same source of truth XamlObject.Name uses)
            // instead of the aliasable table, and - if a rename does happen - explicitly
            // restores the displaced original's table entry afterwards, since
            // NameScopeHelper.NameChanged's unconditional Remove(oldName) during the rename
            // would otherwise leave the original permanently unresolvable by name (breaking
            // {x:Reference}/ElementName lookups - see WinUIXamlServiceProvider.cs).
            if (_context.RootItem != null && !string.IsNullOrEmpty(site.Name)) {
                XamlObject documentRoot = ((XamlDesignItem)_context.RootItem).XamlObject;
                Func<string, XamlObject> findOtherNamedObject = name =>
                    (documentRoot != obj && documentRoot.Name == name) ? documentRoot
                        : XamlDesignItem.GetAllChildXamlObjects(documentRoot)
                            .FirstOrDefault(o => o != obj && o.Name == name);

                string newName = site.Name;
                XamlObject displacedOriginal = findOtherNamedObject(newName);
                if (displacedOriginal != null) {
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
                    while (findOtherNamedObject(newName) != null) {
                        newName = newNameTemplate + i++;
                    }

                    site.Name = newName;

                    // Restore the original's own name-table entry, in case the rename above
                    // (via NameScopeHelper.NameChanged's Remove(oldName)) just evicted it.
                    NameScopeHelper.NameChanged(displacedOriginal, null, displacedOriginal.Name);
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
