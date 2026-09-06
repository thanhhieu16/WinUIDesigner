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
using Microsoft.UI.Xaml.Markup;
using System.Xml;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.Reflection;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Represents a .xaml document.
	/// </summary>
	public sealed class XamlDocument
	{
		/// <remarks>
		/// WPF builds this table from <c>System.Windows.Media.Brushes</c>' static
		/// <c>SolidColorBrush</c> properties. WinUI has no <c>Brushes</c> class; the equivalent
		/// named-colour table is <c>Microsoft.UI.Colors</c>, whose 141 public static properties are
		/// of type <see cref="Windows.UI.Color"/> (verified by reflection - note it lives in
		/// <c>Microsoft.InteractiveExperiences.Projection.dll</c>, not <c>Microsoft.WinUI.dll</c>).
		/// The dictionary is therefore keyed on the colour directly rather than on a brush's colour.
		/// It is built lazily because reading a <c>Microsoft.UI.Colors</c> property activates a
		/// WinRT class, which is unavailable until the Windows App SDK runtime is up.
		/// </remarks>
		private static Dictionary<Windows.UI.Color, string> _colorBrushDictionary;

		internal static Dictionary<Windows.UI.Color, string> ColorBrushDictionary
		{
			get
			{
				if (_colorBrushDictionary == null) {
					var dict = new Dictionary<Windows.UI.Color, string>();
					try {
						foreach (var colorProp in typeof(Microsoft.UI.Colors)
						             .GetProperties(BindingFlags.Static | BindingFlags.Public)
						             .Where(p => p.PropertyType == typeof(Windows.UI.Color)))
						{
							var color = (Windows.UI.Color)colorProp.GetValue(null, null);
							if (!dict.ContainsKey(color))
								dict.Add(color, colorProp.Name);
						}
					} catch (Exception) {
						// Windows App SDK runtime not available - fall back to an empty table, which
						// simply means colours serialize as #AARRGGBB rather than by name.
					}
					_colorBrushDictionary = dict;
				}
				return _colorBrushDictionary;
			}
		}

		XmlDocument _xmlDoc;
		XamlObject _rootElement;
		IServiceProvider _serviceProvider;
		private string _currentProjectAssemblyName;

		XamlTypeFinder _typeFinder;

		int namespacePrefixCounter;

		internal XmlDocument XmlDocument {
			get { return _xmlDoc; }
		}

		/// <summary>
		/// Gets the type finder used for this XAML document.
		/// </summary>
		public XamlTypeFinder TypeFinder {
			get { return _typeFinder; }
		}

		/// <summary>
		/// Gets the service provider used for markup extensions in this document.
		/// </summary>
		public IServiceProvider ServiceProvider {
			get { return _serviceProvider; }
		}

		/// <summary>
		/// Maps <c>x:Name</c> to the <see cref="XamlObject"/> declaring it, for the whole document.
		/// Replaces WPF's per-scope <c>NameScope</c> object model, which WinUI has no counterpart
		/// for (see <see cref="NameScopeHelper"/>).
		/// </summary>
		public Dictionary<string, XamlObject> NamedObjects { get; } = new Dictionary<string, XamlObject>();

		/// <summary>
		/// Gets the Current Projects Assembly Name (if it has any).
		/// </summary>
		public string CurrentProjectAssemblyName
		{
			get { return _currentProjectAssemblyName; }
		}

		/// <summary>
		/// Gets the type descriptor context used for type conversions.
		/// </summary>
		/// <param name="containingObject">The containing object, used when the
		/// type descriptor context needs to resolve an XML namespace.</param>
		internal ITypeDescriptorContext GetTypeDescriptorContext(XamlObject containingObject)
		{
			IServiceProvider serviceProvider;
			if (containingObject != null) {
				if (containingObject.OwnerDocument != this)
					throw new ArgumentException("Containing object must belong to the document!");
				serviceProvider = new XamlObjectServiceProviderAdapter(containingObject.ServiceProvider);
			} else {
				serviceProvider = this.ServiceProvider;
			}
			return new DummyTypeDescriptorContext(serviceProvider);
		}

		/// <summary>
		/// Adapts <see cref="WinUIXamlServiceProvider"/> (which implements WinUI's
		/// <c>IXamlServiceProvider</c>) to <see cref="IServiceProvider"/>, which is what
		/// <see cref="ITypeDescriptorContext"/> requires. WPF's <c>XamlObjectServiceProvider</c>
		/// implemented <c>System.IServiceProvider</c> directly; WinUI's service-provider contract
		/// is its own single-method <c>Microsoft.UI.Xaml.IXamlServiceProvider</c> interface, which
		/// is not related to <c>System.IServiceProvider</c> by inheritance.
		/// </summary>
		sealed class XamlObjectServiceProviderAdapter : IServiceProvider
		{
			readonly WinUIXamlServiceProvider inner;

			public XamlObjectServiceProviderAdapter(WinUIXamlServiceProvider inner)
			{
				this.inner = inner;
			}

			public object GetService(Type serviceType)
			{
				return inner.GetService(serviceType);
			}
		}

		sealed class DummyTypeDescriptorContext : ITypeDescriptorContext
		{
			readonly IServiceProvider baseServiceProvider;

			public DummyTypeDescriptorContext(IServiceProvider serviceProvider)
			{
				this.baseServiceProvider = serviceProvider;
			}

			public IContainer Container {
				get { return null; }
			}

			public object Instance {
				get; set;
			}

			public PropertyDescriptor PropertyDescriptor {
				get { return null; }
			}

			public bool OnComponentChanging()
			{
				return false;
			}

			public void OnComponentChanged()
			{
			}

			public object GetService(Type serviceType)
			{
				return baseServiceProvider.GetService(serviceType);
			}
		}

		/// <summary>
		/// Gets the root xaml object.
		/// </summary>
		public XamlObject RootElement {
			get { return _rootElement; }
		}

		/// <summary>
		/// Gets the object instance created by the root xaml object.
		/// </summary>
		public object RootInstance {
			get { return (_rootElement != null) ? _rootElement.Instance : null; }
		}

		/// <summary>
		/// Saves the xaml document into the <paramref name="writer"/>.
		/// </summary>
		public void Save(XmlWriter writer)
		{
			if (writer == null)
				throw new ArgumentNullException("writer");
			_xmlDoc.Save(writer);
		}

		/// <summary>
		/// Internal constructor, used by XamlParser.
		/// </summary>
		internal XamlDocument(XmlDocument xmlDoc, XamlParserSettings settings)
		{
			this._xmlDoc = xmlDoc;
			this._typeFinder = settings.TypeFinder;
			this._serviceProvider = settings.ServiceProvider;
			this._currentProjectAssemblyName = settings.CurrentProjectAssemblyName;
		}

		/// <summary>
		/// Called by XamlParser to finish initializing the document.
		/// </summary>
		internal void ParseComplete(XamlObject rootElement)
		{
			this._rootElement = rootElement;
		}

		/// <summary>
		/// Create an XamlObject from the instance.
		/// </summary>
		public XamlObject CreateObject(object instance)
		{
			return (XamlObject)CreatePropertyValue(instance, null);
		}

		/// <summary>
		/// Create an XamlObject from the instance.
		/// </summary>
		public XamlObject CreateObject(XamlObject parent, object instance)
		{
			return (XamlObject)CreatePropertyValue(parent, instance, null);
		}

		/// <summary>
		/// Creates a value that represents {x:Null}
		/// </summary>
		/// <remarks>
		/// WPF's <c>System.Windows.Markup.NullExtension</c> has no WinUI counterpart (verified
		/// absent), so this codebase's own <see cref="NullExtensionPlaceholder"/> stands in - see
		/// <see cref="MarkupExtensionParser"/>.
		/// </remarks>
		public XamlPropertyValue CreateNullValue()
		{
			return CreateObject(new NullExtensionPlaceholder());
		}

		/// <summary>
		/// Create a XamlPropertyValue for the specified value instance.
		/// </summary>
		public XamlPropertyValue CreatePropertyValue(object instance, XamlProperty forProperty)
		{
			return CreatePropertyValue(null, instance, forProperty);
		}

		/// <summary>
		/// Create a XamlPropertyValue for the specified value instance.
		/// </summary>
		public XamlPropertyValue CreatePropertyValue(XamlObject parent, object instance, XamlProperty forProperty)
		{
			if (instance == null)
				throw new ArgumentNullException("instance");

			Type elementType = instance.GetType();
			// The converter is resolved from the DECLARED property type, not from the instance:
			// the host and format tables match exactly, so a Path.Data value resolves under
			// Geometry while its instance is a PathGeometry. Resolving through
			// GetCustomTypeConverter instead would be wrong in a different way - measured, it
			// returns StringConverter for a property declared `object`, and that converter turns a
			// nested Button into the literal text 'Microsoft.UI.Xaml.Controls.Button'.
			// The null forProperty case is CreateObject's, which must keep its current behaviour:
			// MarkupExtensionParser.cs:403 runs it for every {Binding} at parse time.
			//
			// TypeDescriptor's Type overload is used rather than its instance overload: measured,
			// the instance overload throws FileNotFoundException for System.Windows.Forms on its
			// first call in a process and succeeds on every call afterwards, while the Type
			// overload never throws.
			TypeConverter c = (forProperty != null ? XamlValueConverter.TryCreate(forProperty.ReturnType) : null)
				?? TypeDescriptor.GetConverter(elementType);
			var ctx = new DummyTypeDescriptorContext(this.ServiceProvider);
			ctx.Instance = instance;
			bool hasStringConverter = c.CanConvertTo(ctx, typeof(string)) && c.CanConvertFrom(typeof(string));
			if (forProperty != null && hasStringConverter) {

				if (instance is SolidColorBrush solidBrush && ColorBrushDictionary.ContainsKey(solidBrush.Color)) {
					var name = ColorBrushDictionary[solidBrush.Color];
					return new XamlTextValue(this, name, instance);
				}

				return new XamlTextValue(this, c.ConvertToInvariantString(ctx, instance), instance);
			}

			string ns = GetNamespaceFor(elementType);
			string prefix = GetPrefixForNamespace(ns);
			if (parent != null)
				prefix = GetPrefixForNamespace(parent.XmlElement, ns);

			XmlElement xml = _xmlDoc.CreateElement(prefix, elementType.Name, ns);

			if (hasStringConverter && (XamlObject.GetContentPropertyName(elementType) != null || IsNativeType(instance))) {
				xml.InnerText = c.ConvertToInvariantString(instance);
			} else if (instance is SolidColorBrush brush && forProperty != null) {
				// WPF fell back to System.Windows.Markup.XamlWriter.Save here; WinUI has no
				// XamlWriter at all (only XamlReader), so the common case is serialized by hand.
				// Reachable only when XamlValueFormatter refuses this instance despite it being a
				// SolidColorBrush (forProperty's declared type is not Brush-shaped, so the early
				// return above never claimed it) - the formatted branch above handles every other
				// SolidColorBrush.
				var col = brush.Color;
				var colorText = ColorBrushDictionary.TryGetValue(col, out var name)
					? name
					: $"#{col.A:X2}{col.R:X2}{col.G:X2}{col.B:X2}";
				xml.InnerText = colorText;
			}
			// Any other Brush (e.g. LinearGradientBrush) that XamlValueFormatter cannot format
			// falls through here and keeps its element form (Rule 3: never throw from this path -
			// the design lets an unformattable instance serialize as its own empty/child-bearing
			// element instead, exactly like every other type this method doesn't special-case).

			return new XamlObject(this, xml, elementType, instance);
		}

		internal string GetNamespaceFor(Type type, bool getClrNamespace = false)
		{
			if (type == typeof (DesignTimeProperties))
				return XamlConstants.DesignTimeNamespace;
			if (type == typeof (MarkupCompatibilityProperties))
				return XamlConstants.MarkupCompatibilityNamespace;
			if (type == typeof(XamlNamespaceProperties))
				return XamlConstants.Xaml2009Namespace;

			return _typeFinder.GetXmlNamespaceFor(type.Assembly, type.Namespace, getClrNamespace);
		}

		internal List<string> GetNamespacesFor(Type type, bool getClrNamespace = false)
		{
			if (type == typeof (DesignTimeProperties))
				return new List<string>(){XamlConstants.DesignTimeNamespace};
			if (type == typeof (MarkupCompatibilityProperties))
				return new List<string>(){XamlConstants.MarkupCompatibilityNamespace};
			if (type == typeof(XamlNamespaceProperties))
				return new List<string>() { XamlConstants.Xaml2009Namespace, XamlConstants.XamlNamespace };

			return _typeFinder.GetXmlNamespacesFor(type.Assembly, type.Namespace, getClrNamespace);
		}

		internal string GetPrefixForNamespace(string @namespace)
		{
			return GetPrefixForNamespace(_xmlDoc.DocumentElement, @namespace);
		}

		internal string GetPrefixForNamespace(XmlElement xmlElement, string @namespace)
		{
			//if (@namespace == XamlConstants.PresentationNamespace)
			//{
			//	return null;
			//}

			string prefix = xmlElement.GetPrefixOfNamespace(@namespace);

			if (xmlElement.NamespaceURI == @namespace && xmlElement.Prefix == String.Empty)
			{
				return string.Empty;
			}

			if (String.IsNullOrEmpty(prefix))
			{
				prefix = _typeFinder.GetPrefixForXmlNamespace(@namespace);

				string existingNamespaceForPrefix = null;
				if (!String.IsNullOrEmpty(prefix))
				{
					existingNamespaceForPrefix = xmlElement.GetNamespaceOfPrefix(prefix);
				}

				if (String.IsNullOrEmpty(prefix) ||
				    !String.IsNullOrEmpty(existingNamespaceForPrefix) &&
				    existingNamespaceForPrefix != @namespace)
				{
					do
					{
						prefix = "Controls" + namespacePrefixCounter++;
					} while (!String.IsNullOrEmpty(xmlElement.GetNamespaceOfPrefix(prefix)));
				}

				string xmlnsPrefix = xmlElement.GetPrefixOfNamespace(XamlConstants.XmlnsNamespace);
				System.Diagnostics.Debug.Assert(!String.IsNullOrEmpty(xmlnsPrefix));

				xmlElement.SetAttribute(xmlnsPrefix + ":" + prefix, @namespace);

				if (@namespace == XamlConstants.DesignTimeNamespace)
				{
					var ignorableProp = new XamlProperty(this._rootElement,
						new XamlDependencyPropertyInfo(MarkupCompatibilityProperties.IgnorableProperty, true, "Ignorable",
							typeof(MarkupCompatibilityProperties), typeof(string)));
					ignorableProp.SetAttribute(prefix);
				}
			}

			return prefix;
		}

		internal string GetNamespaceForPrefix(string prefix)
		{
			return _xmlDoc.DocumentElement.GetNamespaceOfPrefix(prefix);
		}

		bool IsNativeType(object instance)
		{
			return instance.GetType().Assembly == typeof(String).Assembly || instance.GetType().IsEnum;
		}
	}
}
