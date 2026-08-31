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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml.Markup;
using System.Xml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Represents a xaml object element.
	/// </summary>
	[DebuggerDisplay("XamlObject: {Instance}")]
	public sealed class XamlObject : XamlPropertyValue
	{
		XamlDocument document;
		XmlElement element;
		Type elementType;
		object instance;
		List<XamlProperty> properties = new List<XamlProperty>();
		string contentPropertyName;
		XamlProperty nameProperty;
		string runtimeNameProperty;

		/// <summary>For use by XamlParser only.</summary>
		internal XamlObject(XamlDocument document, XmlElement element, Type elementType, object instance)
		{
			this.document = document;
			this.element = element;
			this.elementType = elementType;
			this.instance = instance;

			this.contentPropertyName = GetContentPropertyName(elementType);

			ServiceProvider = new WinUIXamlServiceProvider(this);
			CreateWrapper();

			runtimeNameProperty = GetRuntimeNamePropertyName(elementType);
		}

		/// <summary>
		/// Determines which CLR property of <paramref name="elementType"/> the XAML <c>x:Name</c>
		/// directive maps to.
		/// </summary>
		/// <remarks>
		/// WPF reads <c>System.Windows.Markup.RuntimeNamePropertyAttribute</c> off the type
		/// (<c>FrameworkElement</c> carries <c>[RuntimeNameProperty("Name")]</c>). WinUI has no
		/// such attribute anywhere - verified by reflection: no type whose name contains
		/// "RuntimeName" exists in Microsoft.WinUI.dll, and
		/// <c>Microsoft.UI.Xaml.Markup.RuntimeNamePropertyAttribute</c> does not resolve;
		/// <c>FrameworkElement</c>'s only custom attributes are C#/WinRT projection plumbing
		/// (<c>WindowsRuntimeType</c>, <c>ProjectedRuntimeClass</c>, RCW factories, contract
		/// versions). The attribute scan is therefore replaced by the one mapping WinUI actually
		/// has: <c>FrameworkElement</c> exposes a real <c>Name</c> property (verified present)
		/// and that is what <c>x:Name</c> binds to, exactly as WPF's attribute declares.
		/// </remarks>
		static string GetRuntimeNamePropertyName(Type elementType)
		{
			if (typeof(FrameworkElement).IsAssignableFrom(elementType))
				return "Name";
			return null;
		}

		/// <summary>For use by XamlParser only.</summary>
		internal void AddProperty(XamlProperty property)
		{
			#if DEBUG
			if (property.IsAttached == false) {
				foreach (XamlProperty p in properties) {
					if (p.IsAttached == false && p.PropertyName == property.PropertyName)
						throw new XamlLoadException("duplicate property:" + property.PropertyName);
				}
			}
			#endif
			properties.Add(property);
		}

		#region XamlPropertyValue implementation
		internal override object GetValueFor(XamlPropertyInfo targetProperty)
		{
			if (IsMarkupExtension) {
				var value = ProvideValue();
				if (value is string && targetProperty != null && targetProperty.ReturnType != typeof(string)) {
					return XamlParser.CreateObjectFromAttributeText((string)value, targetProperty, this);
				}
				return value;
			} else {
				return instance;
			}
		}

		internal override XmlNode GetNodeForCollection()
		{
			return element;
		}
		#endregion

		XamlObject parentObject;

		/// <summary>
		/// Gets the parent object.
		/// </summary>
		public XamlObject ParentObject {
			get {
				return parentObject;
			}
			internal set { parentObject = value; }
		}

		internal override void OnParentPropertyChanged()
		{
			parentObject = (ParentProperty != null) ? ParentProperty.ParentObject : null;
			base.OnParentPropertyChanged();
		}

		/// <summary>
		/// Gets the XML element backing this object.
		/// </summary>
		public XmlElement XmlElement {
			get { return element; }
		}

		/// <summary>
		/// Gets the XML element backing this object as a <see cref="PositionXmlElement"/>, if it is one.
		/// </summary>
		public PositionXmlElement PositionXmlElement
		{
			get { return element as PositionXmlElement; }
		}

		XmlAttribute xmlAttribute;

		internal XmlAttribute XmlAttribute {
			get { return xmlAttribute; }
			set {
				xmlAttribute = value;
				element = VirtualAttachTo(XmlElement, value.OwnerElement);
			}
		}

		string GetPrefixOfNamespace(string ns, XmlElement target)
		{
			if (target.NamespaceURI == ns)
				return null;
			var prefix = target.GetPrefixOfNamespace(ns);
			if (!string.IsNullOrEmpty(prefix))
				return prefix;
			var obj = this;
			while (obj != null)
			{
				prefix = obj.XmlElement.GetPrefixOfNamespace(ns);
				if (!string.IsNullOrEmpty(prefix))
					return prefix;
				obj = obj.ParentObject;
			}
			return null;
		}

		XmlElement VirtualAttachTo(XmlElement e, XmlElement target)
		{
			var prefix = GetPrefixOfNamespace(e.NamespaceURI, target);

			XmlElement newElement = e.OwnerDocument.CreateElement(prefix, e.LocalName, e.NamespaceURI);

			foreach (XmlAttribute a in target.Attributes) {
				if (a.Prefix == "xmlns" || a.Name == "xmlns") {
					newElement.Attributes.Append(a.Clone() as XmlAttribute);
				}
			}

			while (e.HasChildNodes) {
				newElement.AppendChild(e.FirstChild);
			}

			XmlAttributeCollection ac = e.Attributes;
			while (ac.Count > 0) {
				newElement.Attributes.Append(ac[0]);
			}

			return newElement;
		}

		/// <summary>
		/// Gets the name of the content property for the specified element type, or null if not available.
		/// </summary>
		/// <param name="elementType">The element type to get the content property name for.</param>
		/// <returns>The name of the content property for the specified element type, or null if not available.</returns>
		internal static string GetContentPropertyName(Type elementType)
		{
			var contentAttrs = elementType.GetCustomAttributes(typeof(ContentPropertyAttribute), true) as ContentPropertyAttribute[];
			if (contentAttrs != null && contentAttrs.Length > 0) {
				return contentAttrs[0].Name;
			}

			return null;
		}

		// NOTE (WinUI port): the reference declares a TypeConverterDelegate here plus
		// GetTypeConverterDelegate(), driven by System.Windows.Markup.XamlSetTypeConverterAttribute /
		// XamlSetTypeConverterEventArgs. Both types are absent from WinUI - verified by reflection:
		// neither Microsoft.UI.Xaml.Markup.XamlSetTypeConverterAttribute nor
		// ...XamlSetTypeConverterEventArgs resolves in Microsoft.WinUI.dll. The whole mechanism
		// (an obscure WPF hook that lets a type intercept type-converter assignment) is therefore
		// dropped, along with its single call site in XamlProperty.UpdateValueOnInstance.

		internal override void AddNodeTo(XamlProperty property)
		{
			XamlObject holder;
			if (!UpdateXmlAttribute(true, out holder)) {
				property.AddChildNodeToProperty(element);
			}
			UpdateMarkupExtensionChain();
		}

		internal override void RemoveNodeFromParent()
		{
			if (XmlAttribute != null) {
				XmlAttribute.OwnerElement.RemoveAttribute(XmlAttribute.Name);
				xmlAttribute = null;
			} else {
				XamlObject holder;
				if (!UpdateXmlAttribute(false, out holder)) {
					element.ParentNode.RemoveChild(element);
				}
			}
			//TODO: PropertyValue still there
			//UpdateMarkupExtensionChain();
		}

		//TODO: reseting path property for binding doesn't work in XamlProperty
		//use CanResetValue()
		internal void OnPropertyChanged(XamlProperty property)
		{
			XamlObject holder;
			if (!UpdateXmlAttribute(false, out holder)) {
				if (holder != null &&
				    holder.XmlAttribute != null) {
					holder.XmlAttribute.OwnerElement.RemoveAttributeNode(holder.XmlAttribute);
					holder.xmlAttribute = null;
					holder.ParentProperty.AddChildNodeToProperty(holder.element);

					bool isThisUpdated = false;
					foreach(XamlObject propXamlObject in holder.Properties.Where((prop) => prop.IsSet).Select((prop) => prop.PropertyValue).OfType<XamlObject>()) {
						XamlObject innerHolder;
						bool updateResult = propXamlObject.UpdateXmlAttribute(true, out innerHolder);
						Debug.Assert(updateResult || innerHolder == null);

						if (propXamlObject == this)
							isThisUpdated = true;
					}
					if (!isThisUpdated)
						this.UpdateXmlAttribute(true, out holder);
				}
			}
			UpdateMarkupExtensionChain();

			if (!element.HasChildNodes && !element.IsEmpty) {
				element.IsEmpty = true;
			}

			if (property == NameProperty) {
				if (NameChanged != null)
					NameChanged(this, EventArgs.Empty);
			}
		}

		void UpdateChildMarkupExtensions(XamlObject obj)
		{
			foreach (var prop in obj.Properties) {
				if (prop.IsSet) {
					var propXamlObject = prop.PropertyValue as XamlObject;
					if (propXamlObject != null) {
						UpdateChildMarkupExtensions(propXamlObject);
					}
				} else if (prop.IsCollection) {
					foreach (var propXamlObject in prop.CollectionElements.OfType<XamlObject>()) {
						UpdateChildMarkupExtensions(propXamlObject);
					}
				}
			}

			if (obj.IsMarkupExtension && obj.ParentProperty != null) {
				obj.ParentProperty.UpdateValueOnInstance();
			}
		}

		void UpdateMarkupExtensionChain()
		{
			UpdateChildMarkupExtensions(this);

			var obj = this.ParentObject;
			while (obj != null && obj.IsMarkupExtension && obj.ParentProperty != null) {
				obj.ParentProperty.UpdateValueOnInstance();
				obj = obj.ParentObject;
			}
		}

		bool UpdateXmlAttribute(bool force, out XamlObject holder)
		{
			holder = FindXmlAttributeHolder();
			if (holder == null && force && IsMarkupExtension) {
				holder = this;
			}
			if (holder != null && MarkupExtensionPrinter.CanPrint(holder)) {
				var s = MarkupExtensionPrinter.Print(holder);
				holder.XmlAttribute = holder.ParentProperty.SetAttribute(s);
				return true;
			}
			return false;
		}

		XamlObject FindXmlAttributeHolder()
		{
			var obj = this;
			while (obj != null && obj.IsMarkupExtension) {
				if (obj.XmlAttribute != null) {
					return obj;
				}
				obj = obj.ParentObject;
			}
			return null;
		}

		/// <summary>
		/// Gets the XamlDocument where this XamlObject is declared in.
		/// </summary>
		public XamlDocument OwnerDocument {
			get { return document; }
		}

		/// <summary>
		/// Gets the instance created by this object element.
		/// </summary>
		public object Instance {
			get { return instance; }
		}

		/// <summary>
		/// Gets whether this instance represents a MarkupExtension.
		/// </summary>
		public bool IsMarkupExtension {
			get { return instance is MarkupExtension; }
		}

		/// <summary>
		/// Gets whether there were load errors for this object.
		/// </summary>
		public bool HasErrors { get; internal set; }

		/// <summary>
		/// Gets the type of this object element.
		/// </summary>
		public Type ElementType {
			get { return elementType; }
		}

		/// <summary>
		/// Gets a read-only collection of properties set on this XamlObject.
		/// This includes both attribute and element properties.
		/// </summary>
		public IList<XamlProperty> Properties {
			get {
				return properties.AsReadOnly();
			}
		}

		/// <summary>
		/// Gets the name of the content property.
		/// </summary>
		public string ContentPropertyName {
			get {
				return contentPropertyName;
			}
		}

		/// <summary>
		/// Gets which property name of the type maps to the XAML x:Name attribute.
		/// </summary>
		public string RuntimeNameProperty {
			get {
				return runtimeNameProperty;
			}
		}

		/// <summary>
		/// Gets which property of the type maps to the XAML x:Name attribute.
		/// </summary>
		public XamlProperty NameProperty {
			get {
				if(nameProperty == null && runtimeNameProperty != null)
					nameProperty = FindOrCreateProperty(runtimeNameProperty);

				return nameProperty;
			}
		}

		/// <summary>
		/// Gets/Sets the name of this XamlObject.
		/// </summary>
		public string Name {
			get
			{
				string name = GetXamlAttribute("Name");

				if (String.IsNullOrEmpty(name)) {
					if (NameProperty != null && NameProperty.IsSet)
						name = (string)NameProperty.ValueOnInstance;
				}

				if (name == String.Empty)
					name = null;

				return name;
			}
			set
			{
				if (String.IsNullOrEmpty(value))
					this.SetXamlAttribute("Name", null);
				else
					this.SetXamlAttribute("Name", value);
			}
		}

		/// <summary>
		/// Finds the specified property, or creates it if it doesn't exist.
		/// </summary>
		public XamlProperty FindOrCreateProperty(string propertyName)
		{
			if (propertyName == null)
				throw new ArgumentNullException("propertyName");

			foreach (XamlProperty p in properties) {
				if (!p.IsAttached && p.PropertyName == propertyName)
					return p;
			}
			PropertyDescriptorCollection propertyDescriptors = TypeDescriptor.GetProperties(instance);
			PropertyDescriptor propertyInfo = propertyDescriptors[propertyName];
			XamlProperty newProperty;

			if (propertyInfo == null) {
				propertyDescriptors = TypeDescriptor.GetProperties(this.elementType);
				propertyInfo = propertyDescriptors[propertyName];
			}

			if (propertyInfo != null) {
				newProperty = new XamlProperty(this, new XamlNormalPropertyInfo(propertyInfo));
			} else {
				EventDescriptorCollection events = TypeDescriptor.GetEvents(instance);
				EventDescriptor eventInfo = events[propertyName];

				if (eventInfo == null) {
					events = TypeDescriptor.GetEvents(this.elementType);
					eventInfo = events[propertyName];
				}

				if (eventInfo != null) {
					newProperty = new XamlProperty(this, new XamlEventPropertyInfo(eventInfo));
				} else {
					throw new ArgumentException("The property '" + propertyName + "' doesn't exist on " + elementType.FullName, "propertyName");
				}
			}
			properties.Add(newProperty);
			return newProperty;
		}

		/// <summary>
		/// Finds the specified property, or creates it if it doesn't exist.
		/// </summary>
		public XamlProperty FindOrCreateAttachedProperty(Type ownerType, string propertyName)
		{
			if (ownerType == null)
				throw new ArgumentNullException("ownerType");
			if (propertyName == null)
				throw new ArgumentNullException("propertyName");

			foreach (XamlProperty p in properties) {
				if (p.IsAttached && p.PropertyTargetType == ownerType && p.PropertyName == propertyName)
					return p;
			}
			XamlPropertyInfo info = XamlParser.TryFindAttachedProperty(ownerType, propertyName);
			if (info == null) {
				throw new ArgumentException("The attached property '" + propertyName + "' doesn't exist on " + ownerType.FullName, "propertyName");
			}
			XamlProperty newProperty = new XamlProperty(this, info);
			properties.Add(newProperty);
			return newProperty;
		}

		/// <summary>
		/// Gets an attribute in the x:-namespace.
		/// </summary>
		public string GetXamlAttribute(string name)
		{
			var value = element.GetAttribute(name, XamlConstants.XamlNamespace);

			if (string.IsNullOrEmpty(value)) {
				value = element.GetAttribute(name, XamlConstants.Xaml2009Namespace);
			}

			return value;
		}

		/// <summary>
		/// Sets an attribute in the x:-namespace.
		/// </summary>
		public void SetXamlAttribute(string name, string value)
		{
			XamlProperty runtimeNameProperty = null;
			bool isNameChange = false;

			if (name == "Name") {
				isNameChange = true;
				string oldName = GetXamlAttribute("Name");

				if (String.IsNullOrEmpty(oldName)) {
					runtimeNameProperty = this.NameProperty;
					if (runtimeNameProperty != null) {
						if (runtimeNameProperty.IsSet)
							oldName = (string)runtimeNameProperty.ValueOnInstance;
						else
							runtimeNameProperty = null;
					}
				}

				if (String.IsNullOrEmpty(oldName))
					oldName = null;

				NameScopeHelper.NameChanged(this, oldName, value);
			}

			if (value == null)
				element.RemoveAttribute(name, XamlConstants.XamlNamespace);
			else
			{
				var prefix = element.GetPrefixOfNamespace(XamlConstants.XamlNamespace);
				var prefix2009 = element.GetPrefixOfNamespace(XamlConstants.Xaml2009Namespace);

				if (!string.IsNullOrEmpty(prefix))
				{
					var attribute = element.OwnerDocument.CreateAttribute(prefix, name, XamlConstants.XamlNamespace);
					attribute.InnerText = value;
					element.SetAttributeNode(attribute);
				}
				else if (!string.IsNullOrEmpty(prefix2009))
				{
					var attribute = element.OwnerDocument.CreateAttribute(prefix, name, XamlConstants.Xaml2009Namespace);
					attribute.InnerText = value;
					element.SetAttributeNode(attribute);
				}
				else
					element.SetAttribute(name, XamlConstants.XamlNamespace, value);
			}

			if (isNameChange) {
				bool nameChangedAlreadyRaised = false;
				if (runtimeNameProperty != null) {
					var handler = new EventHandler((sender, e) => nameChangedAlreadyRaised = true);
					this.NameChanged += handler;

					try {
						runtimeNameProperty.Reset();
					}
					finally {
						this.NameChanged -= handler;
					}
				}

				if (NameChanged != null && !nameChangedAlreadyRaised)
					NameChanged(this, EventArgs.Empty);
			}
		}

		/// <summary>
		/// Gets/Sets the <see cref="WinUIXamlServiceProvider"/> associated with this XamlObject.
		/// </summary>
		public WinUIXamlServiceProvider ServiceProvider { get; set; }

		MarkupExtensionWrapper wrapper;

		void CreateWrapper()
		{
			// Microsoft.UI.Xaml.Data.BindingBase derives from DependencyObject, NOT from
			// MarkupExtension (verified) - unlike WPF. So IsMarkupExtension is always false for a
			// Binding on WinUI, and BindingWrapper (selected by this unconditional 'is' check) is
			// the only thing that ever handles it.
			if (Instance is BindingBase) {
				wrapper = new BindingWrapper(this);
			} else if (Instance is StaticResourceExtensionPlaceholder) {
				wrapper = new StaticResourceWrapper(this);
			} else if (Instance is ThemeResourceExtensionPlaceholder) {
				wrapper = new ThemeResourceWrapper(this);
			} else if (Instance is ReferenceExtensionPlaceholder) {
				wrapper = new ReferenceExtensionWrapper(this);
			}

			if (wrapper == null && IsMarkupExtension) {
				var markupExtensionWrapperAttribute = Instance.GetType().GetCustomAttributes(typeof(MarkupExtensionWrapperAttribute), false).FirstOrDefault() as MarkupExtensionWrapperAttribute;
				if(markupExtensionWrapperAttribute != null) {
					wrapper = MarkupExtensionWrapper.CreateWrapper(markupExtensionWrapperAttribute.MarkupExtensionWrapperType, this);
				}
				else {
					wrapper = MarkupExtensionWrapper.TryCreateWrapper(Instance.GetType(), this);
				}
			}
		}

		object ProvideValue()
		{
			if (wrapper != null) {
				return wrapper.ProvideValue();
			}
			// NOTE (WinUI port): the reference has a special case here returning the extension
			// instance itself for a DynamicResourceExtension used as a Setter's value. WinUI has no
			// DynamicResource concept at all - Microsoft.UI.Xaml.DynamicResourceExtension is absent
			// (verified by reflection; the whole assembly contains exactly two "*Extension"-named
			// public types, IDataTemplateExtension and MarkupExtension). WinUI's nearest analogue,
			// {ThemeResource}, is represented here by ThemeResourceExtensionPlaceholder, resolved
			// through ThemeResourceWrapper (see CreateWrapper) exactly like {StaticResource} is
			// through StaticResourceWrapper - this method's reflection-based fallback path below is
			// never actually reached for either of them.
			return InvokeProvideValue(Instance as MarkupExtension, ServiceProvider);
		}

		static MethodInfo _provideValueWithProvider;
		static MethodInfo _provideValueParameterless;

		/// <summary>
		/// Invokes <c>MarkupExtension.ProvideValue</c> on <paramref name="markupExtension"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The reference writes this as a direct call:
		/// <c>(Instance as MarkupExtension).ProvideValue(ServiceProvider)</c>. That does not compile
		/// against WinUI: both overloads of <c>Microsoft.UI.Xaml.Markup.MarkupExtension.ProvideValue</c>
		/// are declared <b>protected</b> virtual (verified by reflection - the public surface is
		/// only <c>Equals</c>/<c>GetHashCode</c>), and the interface that does expose them publicly,
		/// <c>Microsoft.UI.Xaml.Markup.IMarkupExtensionOverrides</c>, is <b>internal</b> to
		/// Microsoft.WinUI.dll, so it cannot be cast to either. WPF by contrast declares
		/// <c>ProvideValue(IServiceProvider)</c> public abstract. Reflection over the protected
		/// virtual method is therefore the only available way to reach a subclass's override, and
		/// virtual dispatch means the subclass's implementation is what actually runs.
		/// </para>
		/// <para>
		/// The <c>IXamlServiceProvider</c> overload is preferred, matching the reference; if a
		/// subclass overrode only the parameterless overload (WinUI declares both as non-abstract
		/// virtual, so either may carry the real logic) the base implementation returns null and
		/// the parameterless overload is tried as a fallback.
		/// </para>
		/// </remarks>
		static object InvokeProvideValue(MarkupExtension markupExtension, IXamlServiceProvider serviceProvider)
		{
			if (markupExtension == null)
				return null;

			if (_provideValueWithProvider == null) {
				_provideValueWithProvider = typeof(MarkupExtension).GetMethod(
					"ProvideValue", BindingFlags.NonPublic | BindingFlags.Instance, null,
					new[] { typeof(IXamlServiceProvider) }, null);
				_provideValueParameterless = typeof(MarkupExtension).GetMethod(
					"ProvideValue", BindingFlags.NonPublic | BindingFlags.Instance, null,
					Type.EmptyTypes, null);
			}

			object result = null;
			if (_provideValueWithProvider != null) {
				result = Invoke(_provideValueWithProvider, markupExtension, new object[] { serviceProvider });
			}
			if (result == null && _provideValueParameterless != null) {
				result = Invoke(_provideValueParameterless, markupExtension, null);
			}
			return result;
		}

		static object Invoke(MethodInfo method, object target, object[] args)
		{
			try {
				return method.Invoke(target, args);
			} catch (TargetInvocationException ex) {
				throw ex.InnerException ?? ex;
			}
		}

		internal string GetNameForMarkupExtension()
		{
			string markupExtensionName = XmlElement.Name;

			// By convention a markup extension class name typically includes an "Extension" suffix.
			// When you reference the markup extension in XAML the "Extension" suffix is optional.
			// If present remove it to avoid bloating the XAML.
			if (markupExtensionName.EndsWith("Extension", StringComparison.Ordinal)) {
				markupExtensionName = markupExtensionName.Substring(0, markupExtensionName.Length - 9);
			}

			return markupExtensionName;
		}

		/// <summary>
		/// Is raised when the name of this XamlObject changes.
		/// </summary>
		public event EventHandler NameChanged;
	}

	/// <remarks>
	/// Step 9 probe result (Gap 5's open risk), recorded here as the brief asks: WinUI's
	/// <c>MarkupExtension</c> declares <b>both</b> <c>ProvideValue()</c> and
	/// <c>ProvideValue(IXamlServiceProvider)</c> as <c>protected virtual</c>, with no public
	/// entry point at all (<c>IMarkupExtensionOverrides</c>, which exposes them, is internal to
	/// Microsoft.WinUI.dll). More importantly for <c>Binding</c> specifically:
	/// <c>Microsoft.UI.Xaml.Data.Binding</c> does not derive from <c>MarkupExtension</c> on WinUI
	/// at all (its base chain is <c>BindingBase</c> -&gt; <c>DependencyObject</c>), so neither
	/// overload exists on it and the question is moot for bindings - there is no runtime
	/// <c>Binding.ProvideValue</c> path to call. This wrapper therefore does not depend on one:
	/// it copies the <c>Binding</c> and returns the copy, which is what design-time code actually
	/// needs (the binding's <c>Path</c>/<c>Mode</c>/... properties), rather than an evaluated
	/// runtime value. For third-party custom <c>MarkupExtension</c> subclasses,
	/// <see cref="XamlObject"/>'s reflection-based invoker prefers the <c>IXamlServiceProvider</c>
	/// overload and falls back to the parameterless one.
	/// </remarks>
	class BindingWrapper : MarkupExtensionWrapper
	{
		public BindingWrapper(XamlObject xamlObject)
			: base(xamlObject)
		{
		}

		public override object ProvideValue()
		{
			var target = XamlObject.Instance as BindingBase;
			Debug.Assert(target != null);
			//TODO: XamlObject.Clone()
			return CopyBinding(target);
		}

		BindingBase CopyBinding(BindingBase target)
		{
			BindingBase b;
			if (target != null) {
				b = (BindingBase)Activator.CreateInstance(target.GetType());
			} else {
				b = new Binding();
			}

			foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(target)) {
				if (pd.IsReadOnly) {
					if (pd.Name.Equals("Bindings", StringComparison.Ordinal)) {
						var bindings = (Collection<BindingBase>)pd.GetValue(target);
						var newBindings = (Collection<BindingBase>)pd.GetValue(b);

						foreach (var binding in bindings) {
							newBindings.Add(CopyBinding(binding));
						}
					}

					continue;
				}
				try {
					var val1 = pd.GetValue(b);
					var val2 = pd.GetValue(target);
					if (object.Equals(val1, val2)) continue;
					pd.SetValue(b, val2);
				} catch {}
			}

			return b;
		}
	}

	class StaticResourceWrapper : MarkupExtensionWrapper
	{
		public StaticResourceWrapper(XamlObject xamlObject)
			: base(xamlObject)
		{
		}

		public override object ProvideValue()
		{
			var target = XamlObject.Instance as StaticResourceExtensionPlaceholder;
			return XamlObject.ServiceProvider.Resolver.FindResource(target.ResourceKey);
		}
	}

	/// <summary>
	/// Resolves <c>{ThemeResource ...}</c> the same way <see cref="StaticResourceWrapper"/>
	/// resolves <c>{StaticResource ...}</c>. WinUI's real <c>ThemeResource</c> additionally tracks
	/// the active theme and re-resolves on a theme change; this design-time-only wrapper does a
	/// single ancestor-scope resource lookup, same as StaticResource, which is the value that
	/// matters for design-time consumers (e.g. the initial render).
	/// </summary>
	class ThemeResourceWrapper : MarkupExtensionWrapper
	{
		public ThemeResourceWrapper(XamlObject xamlObject)
			: base(xamlObject)
		{
		}

		public override object ProvideValue()
		{
			var target = XamlObject.Instance as ThemeResourceExtensionPlaceholder;
			return XamlObject.ServiceProvider.Resolver.FindResource(target.ResourceKey);
		}
	}
}
