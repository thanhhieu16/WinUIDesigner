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
}
