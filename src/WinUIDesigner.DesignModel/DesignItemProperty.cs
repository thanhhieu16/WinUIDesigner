using System;
using System.Collections.Generic;
using System.ComponentModel;
using WinUIDesigner.DesignModel.Interfaces;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.DesignModel
{
    public abstract class DesignItemProperty : INotifyPropertyChanged
    {
        public abstract string Name { get; }
        public abstract Type ReturnType { get; }
        public abstract Type DeclaringType { get; }
        public abstract string Category { get; }

        public virtual TypeConverter TypeConverter {
            get { return TypeDescriptor.GetConverter(this.ReturnType); }
        }

        public abstract bool IsCollection { get; }
        public abstract bool IsEvent { get; }
        public abstract IObservableList<DesignItem> CollectionElements { get; }
        public abstract DesignItem Value { get; }
        public abstract string TextValue { get; }
        public abstract event EventHandler ValueChanged;
        public abstract event EventHandler ValueOnInstanceChanged;
        public abstract object ValueOnInstance { get; }
        public abstract object DesignerValue { get; }
        public abstract void SetValue(object value);
        public abstract bool IsSet { get; }
        public abstract event EventHandler IsSetChanged;
        public abstract void Reset();
        public abstract T GetConvertedValueOnInstance<T>();
        public abstract DesignItem DesignItem { get; }
        public abstract DependencyProperty DependencyProperty { get; }
        public virtual bool IsAdvanced { get { return false; } }

        public string FullName {
            get { return DeclaringType.FullName + "." + Name; }
        }

        public object ValueOnInstanceOrView {
            get { return Value == null ? ValueOnInstance : Value.View; }
        }

        public string DependencyFullName {
            get {
                if (DependencyProperty != null) {
                    return DependencyProperty.GetFullName();
                }
                return FullName;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public abstract class DesignItemPropertyCollection : IEnumerable<DesignItemProperty>
    {
        public DesignItemProperty this[string name] {
            get { return GetProperty(name); }
        }

        public abstract DesignItemProperty GetProperty(string name);
        public abstract DesignItemProperty GetAttachedProperty(Type ownerType, string name);
        public abstract IEnumerator<DesignItemProperty> GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }

    public static class DependencyPropertyExtensions
    {
        // WinUI's DependencyProperty has no .OwnerType/.Name to compose "OwnerType.Name" the way
        // the WPF reference did - ToString() is the only built-in textual identity it exposes.
        // This is a real, permanent behavior difference from the reference, not a placeholder.
        public static string GetFullName(this DependencyProperty p)
        {
            return p.ToString();
        }
    }
}
