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

using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.OutlineView
{
    /// <summary>
    /// A name-only node standing for one DesignItemProperty rather than an element; its single
    /// child is the property's value. Ported from the reference's PropertyOutlineNode.cs, minus
    /// its redundant second DummyPlacementType registration (spec Decision 11).
    /// </summary>
    public class PropertyOutlineNode : OutlineNode
    {
        readonly DesignItemProperty _property;

        protected PropertyOutlineNode(DesignItemProperty property) : base(property.Name)
        {
            _property = property;
        }

        public override ServiceContainer Services {
            get { return this._property.DesignItem.Services; }
        }

        public static IOutlineNode Create(DesignItemProperty property)
        {
            return new PropertyOutlineNode(property);
        }
    }
}
