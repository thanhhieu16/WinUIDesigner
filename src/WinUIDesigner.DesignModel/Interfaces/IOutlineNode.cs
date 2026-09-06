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

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WinUIDesigner.DesignModel
{
    /// <summary>
    /// One node of the design-time outline tree. Ported from the reference's IOutlineNode.cs.
    /// Two deviations: CanInsert/Insert are added in Phase 4g Task 6 rather than declared here
    /// up front, and IsDesignTimeLocked is settable (spec Decision 15) - the reference's XAML
    /// two-way-binds its lock toggle to the concrete node class through DataContext, while this
    /// port's control holds nodes only as IOutlineNode and would otherwise have to downcast.
    /// </summary>
    public interface IOutlineNode
    {
        ISelectionService SelectionService { get; }
        ServiceContainer Services { get; }
        DesignItem DesignItem { get; set; }
        bool IsExpanded { get; set; }
        bool IsSelected { get; set; }
        bool IsDesignTimeVisible { get; set; }
        bool IsDesignTimeLocked { get; set; }
        string Name { get; }
        ObservableCollection<IOutlineNode> Children { get; }
        bool CanInsert(IEnumerable<IOutlineNode> nodes, IOutlineNode after, bool copy);
        void Insert(IEnumerable<IOutlineNode> nodes, IOutlineNode after, bool copy);
    }
}
