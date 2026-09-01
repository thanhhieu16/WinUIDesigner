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
using System.ComponentModel;
using System.Collections.Generic;

namespace WinUIDesigner.DesignModel
{
    /// <summary>Supports adding and removing components</summary>
    public interface IComponentService
    {
        DesignItem GetDesignItem(object component);
        DesignItem GetDesignItem(object component, bool findByView);
        DesignItem RegisterComponentForDesigner(object component);
        DesignItem RegisterComponentForDesigner(DesignItem parent, object component);

        event EventHandler<DesignItemEventArgs> ComponentRegisteredAndAddedToContainer;
        event EventHandler<DesignItemEventArgs> ComponentRegistered;
        event EventHandler<DesignItemEventArgs> ComponentRemoved;
        event EventHandler<DesignItemPropertyChangedEventArgs> PropertyChanged;
    }

    /// <summary>
    /// Used to get properties for a Design Item.
    /// </summary>
    public interface IComponentPropertyService
    {
        IEnumerable<MemberDescriptor> GetAvailableProperties(DesignItem designItem);
        IEnumerable<MemberDescriptor> GetAvailableEvents(DesignItem designItem);
        IEnumerable<MemberDescriptor> GetCommonAvailableProperties(IEnumerable<DesignItem> designItems);
    }

    [Flags]
    public enum SelectionTypes
    {
        None = 0,
        Auto = 1,
        Primary = 2,
        Toggle = 4,
        Add = 8,
        Remove = 0x10,
        Replace = 0x20
    }

    public interface ISelectionService
    {
        event EventHandler SelectionChanging;
        event EventHandler<DesignItemCollectionEventArgs> SelectionChanged;
        event EventHandler PrimarySelectionChanging;
        event EventHandler PrimarySelectionChanged;

        bool IsComponentSelected(DesignItem component);
        ICollection<DesignItem> SelectedItems { get; }
        void SetSelectedComponents(ICollection<DesignItem> components);
        void SetSelectedComponents(ICollection<DesignItem> components, SelectionTypes selectionType);
        DesignItem PrimarySelection { get; }
        int SelectionCount { get; }
    }

    public interface IErrorService
    {
        void ShowErrorTooltip(Microsoft.UI.Xaml.FrameworkElement attachTo, Microsoft.UI.Xaml.UIElement errorElement);
    }

    public interface ITopLevelWindow
    {
        void SetOwner(Microsoft.UI.Xaml.Window child);
        void Activate();
    }

    public interface ITopLevelWindowService
    {
        ITopLevelWindow GetTopLevelWindow(Microsoft.UI.Xaml.UIElement element);
    }

    public interface IKeyBindingService
    {
        object Owner { get; }
        void RegisterBinding(Microsoft.UI.Xaml.Input.KeyboardAccelerator binding);
        void DeregisterBinding(Microsoft.UI.Xaml.Input.KeyboardAccelerator binding);
        Microsoft.UI.Xaml.Input.KeyboardAccelerator GetBinding(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers);
    }
}
