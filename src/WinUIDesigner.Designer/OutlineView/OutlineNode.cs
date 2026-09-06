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
using System.Collections.Specialized;
using System.Linq;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.OutlineView
{
    /// <summary>
    /// An outline node standing for one DesignItem. Ported from the reference's OutlineNode.cs.
    /// Phase 4g Task 4 implements only the collection-valued content property; Task 5 adds
    /// property nodes, non-collection content and the incremental collection-changed path.
    /// </summary>
    public class OutlineNode : OutlineNodeBase
    {
        protected OutlineNode(DesignItem designItem) : base(designItem)
        {
            UpdateChildren();
            SelectionService.SelectionChanged += Selection_SelectionChanged;
        }

        protected OutlineNode(string name) : base(name)
        {
        }

        void Selection_SelectionChanged(object sender, DesignItemCollectionEventArgs e)
        {
            IsSelected = DesignItem.Services.Selection.IsComponentSelected(DesignItem);
        }

        protected override void UpdateChildren()
        {
            Children.Clear();

            foreach (var prp in DesignItem.AllSetProperties) {
                if (prp.Name != DesignItem.ContentPropertyName && prp.Value != null) {
                    var propertyNode = PropertyOutlineNode.Create(prp);
                    propertyNode.Children.Add(prp.Value.CreateOutlineNode());
                    Children.Add(propertyNode);
                }
            }

            if (DesignItem.ContentPropertyName != null) {
                var content = DesignItem.ContentProperty;
                if (content.IsCollection) {
                    UpdateChildrenCore(content.CollectionElements);
                } else if (content.Value != null) {
                    // A content value that is itself selectable becomes a plain child node;
                    // anything else (a non-visual value) gets a PropertyOutlineNode wrapper so it
                    // is still reachable in the tree.
                    if (!UpdateChildrenCore(new[] { content.Value })) {
                        var propertyNode = PropertyOutlineNode.Create(content);
                        propertyNode.Children.Add(content.Value.CreateOutlineNode());
                        Children.Add(propertyNode);
                    }
                }
            }
        }

        protected override void UpdateChildrenCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove) {
                foreach (var oldItem in e.OldItems) {
                    var item = Children.FirstOrDefault(x => x.DesignItem == oldItem);
                    if (item != null)
                        Children.Remove(item);
                }
            } else if (e.Action == NotifyCollectionChangedAction.Add) {
                UpdateChildrenCore(e.NewItems.Cast<DesignItem>(), e.NewStartingIndex);
            }
        }

        bool UpdateChildrenCore(IEnumerable<DesignItem> items, int index = -1)
        {
            var retVal = false;
            foreach (var item in items) {
                if (ModelTools.CanSelectComponent(item)) {
                    if (Children.All(x => x.DesignItem != item)) {
                        var node = item.CreateOutlineNode();
                        if (index > -1)
                            Children.Insert(index++, node);
                        else
                            Children.Add(node);
                        retVal = true;
                    }
                } else {
                    var content = item.ContentProperty;
                    if (content != null) {
                        if (content.IsCollection) {
                            UpdateChildrenCore(content.CollectionElements);
                            retVal = true;
                        } else if (content.Value != null) {
                            UpdateChildrenCore(new[] { content.Value });
                            retVal = true;
                        }
                    }
                }
            }

            return retVal;
        }

        internal class OutlineNodeService : IOutlineNodeService, IDisposable
        {
            readonly Dictionary<DesignItem, IOutlineNode> outlineNodes = new Dictionary<DesignItem, IOutlineNode>();

            public IOutlineNode Create(DesignItem designItem)
            {
                IOutlineNode node = null;
                if (designItem != null && !outlineNodes.TryGetValue(designItem, out node)) {
                    node = new OutlineNode(designItem);
                    outlineNodes[designItem] = node;
                }

                return node;
            }

            public void Dispose()
            {
                outlineNodes.Clear();
            }
        }
    }
}
