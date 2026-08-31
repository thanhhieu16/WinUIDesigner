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
using System.Diagnostics;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Locates the static "<c>&lt;Name&gt;Property</c>"-style accessor that exposes a
	/// <see cref="DependencyProperty"/> (or a similar static singleton such as a
	/// <c>RoutedEvent</c>) on a type.
	/// </summary>
	/// <remarks>
	/// <para>
	/// WPF declares dependency-property accessors as <c>public static readonly</c> <b>fields</b>
	/// (<c>FrameworkElement.WidthProperty</c>), which is why the reference implementation looks
	/// them up with <see cref="Type.GetField(string, BindingFlags)"/> throughout
	/// (<c>XamlParser.TryFindAttachedProperty</c>, <c>DependencyPropertyConverter</c>), and why
	/// it can additionally rely on <c>System.Windows.DependencyPropertyDescriptor</c>.
	/// </para>
	/// <para>
	/// WinUI's C#/WinRT projection declares them as <b>static properties</b> instead - verified
	/// directly by reflection over <c>Microsoft.WinUI.dll</c>: for
	/// <c>Grid.RowProperty</c>, <c>Grid.ColumnProperty</c>, <c>FrameworkElement.WidthProperty</c>,
	/// <c>FrameworkElement.HeightProperty</c>, <c>Control.BackgroundProperty</c> and
	/// <c>TextBlock.TextProperty</c>, <c>GetField(name)</c> returns <c>null</c> in every case
	/// while <c>GetProperty(name)</c> returns a <c>DependencyProperty</c>-typed static property.
	/// A field-only lookup therefore finds nothing at all on any WinUI framework type.
	/// </para>
	/// <para>
	/// Plain C# types that this codebase or a user project declares (for example the test
	/// fixtures' <c>ExampleControl.Property1Property</c>) still use the ordinary field form, so
	/// both shapes have to be supported. Property is checked first, then field, walking up the
	/// base-type chain.
	/// </para>
	/// </remarks>
	internal static class WinUIStaticMemberLocator
	{
		/// <summary>
		/// Finds and reads a public static member named <paramref name="memberName"/> whose type is
		/// <paramref name="expectedType"/>, searching <paramref name="type"/> and its base types.
		/// Returns null when no such member exists, or when reading it fails.
		/// </summary>
		public static object GetStaticValue(Type type, string memberName, Type expectedType, bool includeNonPublic = false, bool walkBaseTypes = true)
		{
			var flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
			if (includeNonPublic)
				flags |= BindingFlags.NonPublic;

			for (var t = type; t != null; t = walkBaseTypes ? t.BaseType : null) {
				// WinUI shape: static property.
				var property = t.GetProperty(memberName, flags);
				if (property != null && property.PropertyType == expectedType && property.CanRead) {
					var value = TryRead(() => property.GetValue(null, null));
					if (value != null)
						return value;
				}

				// Plain-CLR shape (user code, and this codebase's own attached-property holders):
				// static readonly field.
				var field = t.GetField(memberName, flags);
				if (field != null && field.FieldType == expectedType) {
					var value = TryRead(() => field.GetValue(null));
					if (value != null)
						return value;
				}
			}
			return null;
		}

		/// <summary>
		/// Finds the <see cref="DependencyProperty"/> backing the CLR property
		/// <paramref name="propertyName"/> on <paramref name="type"/>, if any.
		/// Replaces WPF's <c>DependencyPropertyDescriptor.FromProperty</c>, which has no WinUI
		/// counterpart (WinUI's <see cref="DependencyProperty"/> has no
		/// <c>System.ComponentModel</c> integration at all).
		/// </summary>
		public static DependencyProperty FindDependencyProperty(Type type, string propertyName)
		{
			return (DependencyProperty)GetStaticValue(type, propertyName + "Property", typeof(DependencyProperty));
		}

		static object TryRead(Func<object> read)
		{
			try {
				return read();
			} catch (Exception) {
				// Reading a WinUI static DependencyProperty accessor activates a WinRT class and can
				// throw when the Windows App SDK runtime is not available on the current thread.
				// "Not a dependency property" is the same answer the reference gets from a null
				// DependencyPropertyDescriptor, so degrade to null rather than failing the parse.
				return null;
			}
		}
	}

	/// <summary>
	/// Represents a property assignable in XAML.
	/// This can be a normal .NET property or an attached property.
	/// </summary>
	internal abstract class XamlPropertyInfo
	{
		public abstract object GetValue(object instance);
		public abstract void SetValue(object instance, object value);
		public abstract void ResetValue(object instance);
		public abstract TypeConverter TypeConverter { get; }
		public abstract Type TargetType { get; }
		public abstract Type ReturnType { get; }
		public abstract string Name { get; }
		public abstract string FullyQualifiedName { get; }
		public abstract bool IsAttached { get; }
		public abstract bool IsCollection { get; }
		public virtual bool IsEvent { get { return false; } }
		public virtual bool IsAdvanced { get { return false; } }
		public virtual DependencyProperty DependencyProperty { get { return null; } }
		public abstract string Category { get; }
	}

	#region XamlDependencyPropertyInfo
	internal class XamlDependencyPropertyInfo : XamlPropertyInfo
	{
		readonly DependencyProperty property;
		readonly bool isAttached;
		readonly bool isCollection;
		readonly string dependencyPropertyGetterName;
		readonly Type targetType;
		readonly Type returnType;
		Func<object, object> attachedGetter;

		public override DependencyProperty DependencyProperty {
			get { return property; }
		}

		/// <remarks>
		/// WPF's <c>DependencyProperty</c> exposes <c>OwnerType</c> and <c>PropertyType</c>;
		/// WinUI's does not (verified by reflection - its only public members are
		/// <c>Register</c>, <c>RegisterAttached</c>, <c>GetMetadata</c> and <c>UnsetValue</c>).
		/// The owner and return types are therefore passed in by the caller, which already knows
		/// them from the declaring type and the Get-accessor's return type.
		/// </remarks>
		public XamlDependencyPropertyInfo(DependencyProperty property, bool isAttached, string dependencyPropertyGetterName, Type targetType, Type returnType, Func<object, object> attachedGetter = null)
		{
			Debug.Assert(property != null);
			this.property = property;
			this.isAttached = isAttached;
			this.targetType = targetType;
			this.returnType = returnType;
			this.isCollection = CollectionSupport.IsCollectionType(returnType);
			this.attachedGetter = attachedGetter;
			this.dependencyPropertyGetterName = dependencyPropertyGetterName;
		}

		public override TypeConverter TypeConverter {
			get {
				return TypeDescriptor.GetConverter(this.ReturnType);
			}
		}

		public override string FullyQualifiedName {
			get {
				return this.TargetType.FullName + "." + this.Name;
			}
		}

		public override Type TargetType {
			get { return targetType; }
		}

		public override Type ReturnType {
			get { return returnType; }
		}

		public override string Name {
			get { return dependencyPropertyGetterName; }
		}

		public override string Category {
			get { return "Misc"; }
		}

		public override bool IsAttached {
			get { return isAttached; }
		}

		public override bool IsCollection {
			get {
				return isCollection;
			}
		}

		public override object GetValue(object instance)
		{
			try {
				if (attachedGetter != null) {
					return attachedGetter(instance);
				}
			} catch (Exception) {

			}

			var dependencyObject = instance as DependencyObject;
			if (dependencyObject != null) {
				return dependencyObject.GetValue(property);
			}

			return null;
		}

		public override void SetValue(object instance, object value)
		{
			((DependencyObject)instance).SetValue(property, value);
		}

		public override void ResetValue(object instance)
		{
			((DependencyObject)instance).ClearValue(property);
		}
	}
	#endregion

	#region XamlNormalPropertyInfo
	internal sealed class XamlNormalPropertyInfo : XamlPropertyInfo
	{
		PropertyDescriptor _propertyDescriptor;
		DependencyProperty dependencyProperty;

		public XamlNormalPropertyInfo(PropertyDescriptor propertyDescriptor)
		{
			this._propertyDescriptor = propertyDescriptor;
			// WPF: DependencyPropertyDescriptor.FromProperty(propertyDescriptor). WinUI has no
			// TypeDescriptor bridge for DependencyProperty at all, so recover the underlying
			// DependencyProperty by locating the static "<Name>Property" accessor instead.
			dependencyProperty = WinUIStaticMemberLocator.FindDependencyProperty(propertyDescriptor.ComponentType, propertyDescriptor.Name);
		}

		public override DependencyProperty DependencyProperty {
			get {
				return dependencyProperty;
			}
		}

		public override object GetValue(object instance)
		{
			return _propertyDescriptor.GetValue(instance);
		}

		public override void SetValue(object instance, object value)
		{
			_propertyDescriptor.SetValue(instance, value);
		}

		public override void ResetValue(object instance)
		{
			try {
				_propertyDescriptor.ResetValue(instance);
			} catch (Exception) {
				//For Example "UndoRedoSimpleBinding" will raise a exception here => look if it has Side Effects if we generally catch here?
			}
		}

		public override Type ReturnType {
			get { return _propertyDescriptor.PropertyType; }
		}

		public override Type TargetType {
			get { return _propertyDescriptor.ComponentType; }
		}

		public override string Category {
			get { return _propertyDescriptor.Category; }
		}

		public override TypeConverter TypeConverter {
			get {
				return GetCustomTypeConverter(_propertyDescriptor.PropertyType) ?? _propertyDescriptor.Converter;
			}
		}

		public override string FullyQualifiedName {
			get {
				return _propertyDescriptor.ComponentType.FullName + "." + _propertyDescriptor.Name;
			}
		}

		public override string Name {
			get { return _propertyDescriptor.Name; }
		}

		public override bool IsAttached {
			get { return false; }
		}

		public override bool IsCollection {
			get {
				return CollectionSupport.IsCollectionType(_propertyDescriptor.PropertyType);
			}
		}

		public override bool IsAdvanced	{
			get	{
				var a = _propertyDescriptor.Attributes[typeof(EditorBrowsableAttribute)] as EditorBrowsableAttribute;
				if (a != null) {
					return a.State == EditorBrowsableState.Advanced;
				}
				return false;
			}
		}

		public static readonly TypeConverter StringTypeConverter = TypeDescriptor.GetConverter(typeof(string));

		public static TypeConverter GetCustomTypeConverter(Type propertyType)
		{
			if (propertyType == typeof(object))
				return StringTypeConverter;
			else if (propertyType == typeof(Type))
				return TypeTypeConverter.Instance;
			else if (propertyType == typeof(DependencyProperty))
				return DependencyPropertyConverter.Instance;
			else
				return null;
		}

		sealed class TypeTypeConverter : TypeConverter
		{
			public readonly static TypeTypeConverter Instance = new TypeTypeConverter();

			public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
			{
				if (sourceType == typeof(string))
					return true;
				else
					return base.CanConvertFrom(context, sourceType);
			}

			public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
			{
				if (value == null)
					return null;
				if (value is string) {
					IXamlTypeResolver xamlTypeResolver = (IXamlTypeResolver)context.GetService(typeof(IXamlTypeResolver));
					if (xamlTypeResolver == null)
						throw new XamlLoadException("IXamlTypeResolver not found in type descriptor context.");
					return xamlTypeResolver.Resolve((string)value);
				} else {
					return base.ConvertFrom(context, culture, value);
				}
			}
		}

		sealed class DependencyPropertyConverter : TypeConverter
		{
			public readonly static DependencyPropertyConverter Instance = new DependencyPropertyConverter();

			public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
			{
				if (sourceType == typeof(string))
					return true;
				else
					return base.CanConvertFrom(context, sourceType);
			}

			public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
			{
				if (value == null)
					return null;
				if (value is string) {
					XamlTypeResolverProvider xamlTypeResolver = (XamlTypeResolverProvider)context.GetService(typeof(XamlTypeResolverProvider));
					if (xamlTypeResolver == null)
						throw new XamlLoadException("XamlTypeResolverProvider not found in type descriptor context.");
					XamlPropertyInfo prop = xamlTypeResolver.ResolveProperty((string)value);
					if (prop == null)
						throw new XamlLoadException("Could not find property " + value + ".");
					XamlDependencyPropertyInfo depProp = prop as XamlDependencyPropertyInfo;
					if (depProp != null)
						return depProp.DependencyProperty;
					// Reference used GetField(prop.Name + "Property"); WinUI exposes these as static
					// properties instead - see WinUIStaticMemberLocator.
					var dp = WinUIStaticMemberLocator.FindDependencyProperty(prop.TargetType, prop.Name);
					if (dp != null) {
						return dp;
					}
					throw new XamlLoadException("Property " + value + " is not a dependency property.");
				} else {
					return base.ConvertFrom(context, culture, value);
				}
			}
		}
	}
	#endregion

	#region XamlEventPropertyInfo
	sealed class XamlEventPropertyInfo : XamlPropertyInfo
	{
		readonly EventDescriptor _eventDescriptor;

		public XamlEventPropertyInfo(EventDescriptor eventDescriptor)
		{
			this._eventDescriptor = eventDescriptor;
		}

		public override object GetValue(object instance)
		{
			throw new NotSupportedException();
		}

		public override void SetValue(object instance, object value)
		{

		}

		public override void ResetValue(object instance)
		{

		}

		public override Type ReturnType {
			get { return _eventDescriptor.EventType; }
		}

		public override Type TargetType {
			get { return _eventDescriptor.ComponentType; }
		}

		public override string Category {
			get { return _eventDescriptor.Category; }
		}

		public override TypeConverter TypeConverter {
			get { return XamlNormalPropertyInfo.StringTypeConverter; }
		}

		public override string FullyQualifiedName {
			get {
				return _eventDescriptor.ComponentType.FullName + "." + _eventDescriptor.Name;
			}
		}

		public override string Name {
			get { return _eventDescriptor.Name; }
		}

		public override bool IsEvent {
			get { return true; }
		}

		public override bool IsAttached {
			get { return false; }
		}

		public override bool IsCollection {
			get { return false; }
		}
	}
	#endregion
}
