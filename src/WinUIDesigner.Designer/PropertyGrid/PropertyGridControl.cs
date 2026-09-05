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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.PropertyGrid;
using WinUIDesigner.Designer.PropertyGrid.Editors;

namespace WinUIDesigner.Designer.PropertyGrid
{
    /// <summary>
    /// Design-time property-editing panel (spec Decision 6 folds the reference's separate
    /// PropertyGrid.cs controller directly into this control - no XAML/DataTemplate view layer
    /// exists in this codebase to justify the split). No shell app exists yet (Phase 5) - a host
    /// sets SelectedItems directly, the same host-less-control-testing pattern DesignSurfaceTests
    /// and other Designer.Tests already use.
    /// </summary>
    public class PropertyGridControl : StackPanel
    {
        static PropertyGridControl()
        {
            TextBoxEditor.Register();
        }

        readonly Category specialCategory = new Category("Special");
        readonly Category popularCategory = new Category("Popular");
        readonly Category otherCategory = new Category("Other");
        readonly Category attachedCategory = new Category("Attached");

        readonly Dictionary<string, PropertyNode> nodeCache = new Dictionary<string, PropertyNode>();
        readonly Dictionary<PropertyNode, Panel> editorHosts = new Dictionary<PropertyNode, Panel>();

        public IReadOnlyList<Category> Categories { get; }

        IList<DesignItem> selectedItems;

        public ICollection<DesignItem> SelectedItems {
            get { return (ICollection<DesignItem>)selectedItems; }
            set {
                selectedItems = value == null ? null : value.ToList();
                Reload();
            }
        }

        public PropertyGridControl()
        {
            Categories = new[] { popularCategory, otherCategory, attachedCategory, specialCategory };
        }

        void Reload()
        {
            foreach (var category in Categories) {
                category.IsVisible = false;
                foreach (var node in category.Properties) node.IsVisible = false;
            }

            Children.Clear();

            if (selectedItems == null || selectedItems.Count == 0) return;

            IEnumerable<System.ComponentModel.MemberDescriptor> descriptors;
            if (selectedItems.Count == 1) {
                var service = selectedItems[0].Services.GetService<IComponentPropertyService>();
                descriptors = service.GetAvailableProperties(selectedItems[0]);
            } else {
                var service = selectedItems[0].Services.GetService<IComponentPropertyService>();
                descriptors = service.GetCommonAvailableProperties(selectedItems);
            }

            foreach (var descriptor in descriptors) {
                AddNode(descriptor.Name);
            }

            // GetAvailableProperties/TypeDescriptor.GetProperties (IComponentPropertyService, Phase 2) only
            // reflects a component's own CLR properties - it has no mechanism to discover attached
            // properties set on it (e.g. Grid.Row), since that's a XAML/DesignItem-level concept, not a
            // plain-reflection one. AllSetProperties DOES include attached properties actually set in the
            // source, so surface those directly here. Single-select only (matches the reference's own
            // precedent of reduced fidelity for multi-select beyond the plain descriptor list).
            if (selectedItems.Count == 1) {
                foreach (var property in selectedItems[0].AllSetProperties) {
                    if (property.IsAttachedProperty) AddNodeCore(new[] { property });
                }
            }

            foreach (var category in Categories) {
                if (category.IsVisible) Children.Add(BuildCategoryRow(category));
            }
        }

        void AddNode(string propertyName)
        {
            var designProperties = selectedItems.Select(item => item.Properties.GetProperty(propertyName)).ToArray();
            AddNodeCore(designProperties);
        }

        void AddNodeCore(DesignItemProperty[] designProperties)
        {
            if (!Metadata.IsBrowsable(designProperties[0])) return;

            PropertyNode node;
            if (nodeCache.TryGetValue(designProperties[0].FullName, out node)) {
                node.Load(designProperties);
            } else {
                node = new PropertyNode(designProperties, null);
                var category = PickCategory(node);
                InsertSorted(category.Properties, node);
                node.Category = category;
                nodeCache[designProperties[0].FullName] = node;
            }
            node.IsVisible = true;
            node.Category.IsVisible = true;
        }

        Category PickCategory(PropertyNode node)
        {
            if (Metadata.IsPopularProperty(node.FirstProperty)) return popularCategory;
            if (node.FirstProperty.IsAttachedProperty) return attachedCategory;
            var typeName = node.FirstProperty.DeclaringType.FullName;
            if (typeName != null && typeName.StartsWith("Microsoft.UI.Xaml.", StringComparison.Ordinal)) return otherCategory;
            return specialCategory;
        }

        static void InsertSorted(List<PropertyNode> list, PropertyNode node)
        {
            int index = 0;
            while (index < list.Count && string.Compare(list[index].Name, node.Name, StringComparison.OrdinalIgnoreCase) < 0) {
                index++;
            }
            list.Insert(index, node);
        }

        FrameworkElement BuildCategoryRow(Category category)
        {
            var header = new TextBlock { Text = category.Name, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
            var toggle = new ToggleButton { Content = header, IsChecked = category.IsExpanded };
            var body = new StackPanel { Visibility = category.IsExpanded ? Visibility.Visible : Visibility.Collapsed };
            foreach (var node in category.Properties) {
                if (node.IsVisible) body.Children.Add(BuildPropertyRow(node));
            }
            toggle.Checked += (s, e) => { category.IsExpanded = true; body.Visibility = Visibility.Visible; };
            toggle.Unchecked += (s, e) => { category.IsExpanded = false; body.Visibility = Visibility.Collapsed; };

            var panel = new StackPanel();
            panel.Children.Add(toggle);
            panel.Children.Add(body);
            return panel;
        }

        FrameworkElement BuildPropertyRow(PropertyNode node)
        {
            var row = new Grid { Margin = new Thickness(node.Level * 12, 0, 0, 0) };
            // 3 columns always, whether or not this node has children - a toggle-or-empty
            // spacer (0), the name (1), the editor (2). Sharing one column between the toggle
            // and the name (as an earlier draft of this method did) makes them overlap.
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Pixel) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            ToggleButton toggle = null;
            if (node.HasChildren) {
                toggle = new ToggleButton { Content = "▶", IsChecked = node.IsExpanded };
                Grid.SetColumn(toggle, 0);
                row.Children.Add(toggle);
            }

            var nameText = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameText, 1);
            row.Children.Add(nameText);

            // node.Editor is cached on the PropertyNode (Task 3's Load() only creates it once) -
            // on a second Reload() of an already-seen node it is still parented to the PREVIOUS
            // call's now-orphaned row Grid. WinUI (like WPF/UWP) throws if a FrameworkElement is
            // added as a child while still parented elsewhere, so it must be detached first.
            // FrameworkElement.Parent is NOT reliable for this: it reads back null for an element
            // added to a Panel that is itself never connected to a live Window (confirmed
            // empirically - exactly this control's own host-less-control-testing situation, since
            // nothing here ever assigns a PropertyGridControl into a Window's content), so track
            // each node's current host Panel explicitly instead of trusting .Parent.
            // editorHosts tracking is confined to top-level nodes (node.Parent == null - the plain
            // PropertyNode.Parent field, unrelated to the FrameworkElement.Parent quirk above): a
            // recursive CHILD node (PropertyNode.Parent != null) is always freshly constructed by
            // PropertyNode.UpdateChildren() on every expand/value-change, with a freshly-created
            // Editor each time (Load()'s `if (Editor == null)` check on a brand-new instance), so
            // there is nothing to detach for it - and the discarded old child instances would
            // otherwise sit in editorHosts forever (never reachable via nodeCache, which only
            // caches top-level nodes), a growing memory leak.
            if (node.Parent == null) {
                Panel oldHost;
                if (editorHosts.TryGetValue(node, out oldHost)) {
                    oldHost.Children.Remove(node.Editor);
                }
            }
            Grid.SetColumn(node.Editor, 2);
            row.Children.Add(node.Editor);
            if (node.Parent == null) {
                editorHosts[node] = row;
            }

            var stack = new StackPanel();
            stack.Children.Add(row);

            if (node.HasChildren) {
                var childrenStack = new StackPanel { Visibility = node.IsExpanded ? Visibility.Visible : Visibility.Collapsed };
                RebuildChildRows(node, childrenStack);
                toggle.Checked += (s, e) => {
                    node.IsExpanded = true;
                    childrenStack.Visibility = Visibility.Visible;
                    RebuildChildRows(node, childrenStack);
                };
                toggle.Unchecked += (s, e) => {
                    node.IsExpanded = false;
                    childrenStack.Visibility = Visibility.Collapsed;
                };
                stack.Children.Add(childrenStack);
            }

            return stack;
        }

        void RebuildChildRows(PropertyNode node, StackPanel childrenStack)
        {
            childrenStack.Children.Clear();
            foreach (var child in node.Children) {
                childrenStack.Children.Add(BuildPropertyRow(child));
            }
            if (node.MoreChildren.Count > 0) {
                foreach (var child in node.MoreChildren) {
                    childrenStack.Children.Add(BuildPropertyRow(child));
                }
            }
        }
    }
}
