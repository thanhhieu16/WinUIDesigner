using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml.Markup;

namespace WinUIDesigner.XamlDom.Tests
{
	/// <summary>
	/// A reflection-driven <see cref="IXamlMetadataProvider"/> that lets WinUI's own
	/// <c>XamlReader.Load</c> resolve ordinary CLR types from this test assembly.
	/// </summary>
	/// <remarks>
	/// <para>
	/// WPF's <c>XamlReader</c> resolves any type by reflection, which is what makes the reference
	/// <c>TestHelper</c>'s "load it both ways and compare" oracle work for its <c>ExampleClass</c>
	/// fixtures. WinUI's runtime XAML reader does not: it resolves non-framework types only through
	/// an <see cref="IXamlMetadataProvider"/> that the XAML compiler normally generates into the
	/// <c>App</c> class. Without one, every fixture-based test fails inside the oracle with
	/// <c>XamlParseException: The type 'ExampleClass' was not found because '...' is an unknown
	/// namespace</c> - verified: 29 of the ported tests failed this way, all before reaching this
	/// codebase's parser.
	/// </para>
	/// <para>
	/// This supplies that metadata by reflection instead of code generation, so the oracle keeps
	/// working. Note it only feeds WinUI type information; the XAML parsing, whitespace handling,
	/// content-property and collection semantics being compared against are still entirely WinUI's
	/// own, so the comparison remains meaningful.
	/// </para>
	/// </remarks>
	public sealed class ReflectionXamlMetadataProvider : IXamlMetadataProvider
	{
		readonly Assembly[] assemblies;
		readonly ConcurrentDictionary<Type, IXamlType> byType = new ConcurrentDictionary<Type, IXamlType>();

		public ReflectionXamlMetadataProvider(params Assembly[] assemblies)
		{
			this.assemblies = assemblies;
		}

		public IXamlType GetXamlType(Type type)
		{
			if (type == null)
				return null;
			return byType.GetOrAdd(type, t => new ReflectionXamlType(this, t));
		}

		public IXamlType GetXamlType(string fullName)
		{
			if (string.IsNullOrEmpty(fullName))
				return null;
			foreach (var asm in assemblies) {
				var t = asm.GetType(fullName);
				if (t != null)
					return GetXamlType(t);
			}
			var direct = Type.GetType(fullName);
			return direct != null ? GetXamlType(direct) : null;
		}

		public XmlnsDefinition[] GetXmlnsDefinitions() => Array.Empty<XmlnsDefinition>();
	}

	sealed class ReflectionXamlType : IXamlType
	{
		readonly ReflectionXamlMetadataProvider provider;
		readonly Type type;

		public ReflectionXamlType(ReflectionXamlMetadataProvider provider, Type type)
		{
			this.provider = provider;
			this.type = type;
		}

		public Type UnderlyingType => type;
		public string FullName => type.FullName;
		public IXamlType BaseType => type.BaseType == null || type.BaseType == typeof(object)
			? null
			: provider.GetXamlType(type.BaseType);
		public IXamlType BoxedType => null;
		public bool IsArray => type.IsArray;
		public bool IsBindable => true;
		public bool IsMarkupExtension => typeof(MarkupExtension).IsAssignableFrom(type);

		public bool IsCollection => !IsDictionary && (
			typeof(IList).IsAssignableFrom(type) || type.IsArray || GenericInterface(typeof(IList<>)) != null);

		public bool IsDictionary =>
			typeof(IDictionary).IsAssignableFrom(type) || GenericInterface(typeof(IDictionary<,>)) != null;

		public bool IsConstructible => !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null;

		public IXamlType ItemType
		{
			get
			{
				if (type.IsArray)
					return provider.GetXamlType(type.GetElementType());
				var dict = GenericInterface(typeof(IDictionary<,>));
				if (dict != null)
					return provider.GetXamlType(dict.GetGenericArguments()[1]);
				var list = GenericInterface(typeof(IList<>));
				if (list != null)
					return provider.GetXamlType(list.GetGenericArguments()[0]);
				return IsCollection || IsDictionary ? provider.GetXamlType(typeof(object)) : null;
			}
		}

		public IXamlType KeyType
		{
			get
			{
				var dict = GenericInterface(typeof(IDictionary<,>));
				if (dict != null)
					return provider.GetXamlType(dict.GetGenericArguments()[0]);
				return IsDictionary ? provider.GetXamlType(typeof(object)) : null;
			}
		}

		public IXamlMember ContentProperty
		{
			get
			{
				var attrs = type.GetCustomAttributes(typeof(ContentPropertyAttribute), true) as ContentPropertyAttribute[];
				if (attrs != null && attrs.Length > 0 && !string.IsNullOrEmpty(attrs[0].Name))
					return GetMember(attrs[0].Name);
				return null;
			}
		}

		public object ActivateInstance() => Activator.CreateInstance(type);

		public object CreateFromString(string value)
		{
			if (type == typeof(string))
				return value;
			if (type.IsEnum)
				return Enum.Parse(type, value, true);
			var converter = TypeDescriptor.GetConverter(type);
			if (converter != null && converter.CanConvertFrom(typeof(string)))
				return converter.ConvertFromString(null, CultureInfo.InvariantCulture, value);
			return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
		}

		public IXamlMember GetMember(string name)
		{
			if (string.IsNullOrEmpty(name))
				return null;

			// Attachable members arrive either as "Owner.Name" or as a plain name on the owner type.
			var localName = name;
			var ownerType = type;
			int dot = name.LastIndexOf('.');
			if (dot > 0) {
				localName = name.Substring(dot + 1);
				var resolved = provider.GetXamlType(name.Substring(0, dot));
				if (resolved != null)
					ownerType = resolved.UnderlyingType;
			}

			var property = ownerType.GetProperty(localName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
			if (property != null)
				return new ReflectionXamlMember(provider, this, localName, property, null, null);

			var getter = ownerType.GetMethod("Get" + localName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
			var setter = ownerType.GetMethod("Set" + localName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
			if (getter != null || setter != null)
				return new ReflectionXamlMember(provider, this, localName, null, getter, setter);

			return null;
		}

		public void AddToVector(object instance, object value)
		{
			var list = instance as IList;
			if (list != null) {
				list.Add(value);
				return;
			}
			instance.GetType().InvokeMember("Add",
				BindingFlags.Public | BindingFlags.InvokeMethod | BindingFlags.Instance,
				null, instance, new[] { value }, CultureInfo.InvariantCulture);
		}

		public void AddToMap(object instance, object key, object value)
		{
			var dict = instance as IDictionary;
			if (dict != null) {
				dict.Add(key, value);
				return;
			}
			instance.GetType().InvokeMember("Add",
				BindingFlags.Public | BindingFlags.InvokeMethod | BindingFlags.Instance,
				null, instance, new[] { key, value }, CultureInfo.InvariantCulture);
		}

		public void RunInitializer() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);

		Type GenericInterface(Type openGeneric)
		{
			if (type.IsGenericType && type.GetGenericTypeDefinition() == openGeneric)
				return type;
			return type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGeneric);
		}
	}

	sealed class ReflectionXamlMember : IXamlMember
	{
		readonly ReflectionXamlMetadataProvider provider;
		readonly IXamlType declaringType;
		readonly PropertyInfo property;
		readonly MethodInfo getter;
		readonly MethodInfo setter;
		readonly Microsoft.UI.Xaml.DependencyProperty dependencyProperty;

		public ReflectionXamlMember(ReflectionXamlMetadataProvider provider, IXamlType declaringType, string name,
			PropertyInfo property, MethodInfo getter, MethodInfo setter)
		{
			this.provider = provider;
			this.declaringType = declaringType;
			this.property = property;
			this.getter = getter;
			this.setter = setter;
			Name = name;

			if (property == null) {
				// Attachable member: prefer its DependencyProperty over the static Get/Set wrappers.
				// This is deliberate harness alignment, not a behaviour claim: WPF's XamlReader -
				// the oracle the reference tests were written against - assigns attached properties
				// straight through DependencyObject.SetValue, which is why the reference's
				// log-equality assertion holds there and why XamlDependencyPropertyInfo (ported
				// verbatim) does the same. Routing this oracle through the static wrappers instead
				// would make every attached-property test differ purely in which accessor was used,
				// hiding the parsing behaviour the test is actually about.
				var owner = declaringType.UnderlyingType;
				var field = owner.GetField(name + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
				if (field != null && field.FieldType == typeof(Microsoft.UI.Xaml.DependencyProperty))
					dependencyProperty = (Microsoft.UI.Xaml.DependencyProperty)field.GetValue(null);
				else {
					var prop = owner.GetProperty(name + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
					if (prop != null && prop.PropertyType == typeof(Microsoft.UI.Xaml.DependencyProperty))
						dependencyProperty = (Microsoft.UI.Xaml.DependencyProperty)prop.GetValue(null);
				}
			}
		}

		public string Name { get; }
		public bool IsAttachable => property == null;
		public bool IsDependencyProperty => dependencyProperty != null;
		public bool IsReadOnly => property != null ? !property.CanWrite : setter == null;
		public IXamlType TargetType => declaringType;

		public IXamlType Type => provider.GetXamlType(
			property != null ? property.PropertyType
			: getter != null ? getter.ReturnType
			: setter.GetParameters()[1].ParameterType);

		public object GetValue(object instance)
		{
			if (property != null)
				return property.GetValue(instance);
			if (dependencyProperty != null && instance is Microsoft.UI.Xaml.DependencyObject depObj)
				return depObj.GetValue(dependencyProperty);
			return getter?.Invoke(null, new[] { instance });
		}

		public void SetValue(object instance, object value)
		{
			if (property != null) {
				property.SetValue(instance, value);
				return;
			}
			if (dependencyProperty != null && instance is Microsoft.UI.Xaml.DependencyObject depObj) {
				depObj.SetValue(dependencyProperty, value);
				return;
			}
			setter?.Invoke(null, new[] { instance, value });
		}
	}
}
