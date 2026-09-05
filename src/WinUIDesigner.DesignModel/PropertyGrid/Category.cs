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
using System.ComponentModel;

namespace WinUIDesigner.DesignModel.PropertyGrid
{
    /// <summary>
    /// View-model for one property-grid category bucket (Special/Popular/Other/Attached),
    /// ported from the reference's Category.cs (WpfDesign/Project/PropertyGrid/Category.cs). No
    /// SortedObservableCollection/PropertyNodeCollection port (spec Decision 5) - Properties is a
    /// plain list PropertyGridControl inserts into in sorted order.
    /// </summary>
    public class Category : INotifyPropertyChanged
    {
        public Category(string name)
        {
            Name = name;
            Properties = new List<PropertyNode>();
        }

        public string Name { get; private set; }

        public List<PropertyNode> Properties { get; private set; }

        bool isExpanded = true;

        public bool IsExpanded {
            get { return isExpanded; }
            set {
                isExpanded = value;
                RaisePropertyChanged("IsExpanded");
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

        public event PropertyChangedEventHandler PropertyChanged;

        void RaisePropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
