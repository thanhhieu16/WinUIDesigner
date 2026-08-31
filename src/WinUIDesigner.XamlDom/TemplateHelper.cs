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
using System.Xml;
using System.Xml.XPath;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Instantiates a FrameworkTemplate from a template's XML fragment.
	/// WPF's TemplateHelper used System.Xaml's XamlXmlReader/XamlObjectWriter
	/// pipeline (no WinUI equivalent, confirmed absent). This uses WinUI's own
	/// public Microsoft.UI.Xaml.Markup.XamlReader.Load(string) instead.
	/// This base version resolves only resources declared inside the
	/// template's own fragment; Task 8 adds ancestor-scope resource splicing.
	/// </summary>
	public static class TemplateHelper
	{
		/// <summary>
		/// Instantiates the <see cref="FrameworkTemplate"/> described by <paramref name="xmlElement"/>.
		/// </summary>
		public static FrameworkTemplate GetFrameworkTemplate(XmlElement xmlElement, XamlObject parentObject)
		{
			var nav = xmlElement.CreateNavigator();
			var namespaces = new Dictionary<string, string>();
			while (true) {
				foreach (var ns in nav.GetNamespacesInScope(XmlNamespaceScope.ExcludeXml)) {
					if (!namespaces.ContainsKey(ns.Key) && ns.Key != "")
						namespaces.Add(ns.Key, ns.Value);
				}
				if (!nav.MoveToParent())
					break;
			}

			var clonedElement = (XmlElement)xmlElement.CloneNode(true);
			foreach (var entry in new List<KeyValuePair<string, string>>(namespaces)) {
				var value = entry.Value;
				if (value.StartsWith("clr-namespace") && !value.Contains(";assembly=")
				    && parentObject != null
				    && !string.IsNullOrEmpty(parentObject.OwnerDocument.CurrentProjectAssemblyName)) {
					value += ";assembly=" + parentObject.OwnerDocument.CurrentProjectAssemblyName;
				}
				clonedElement.SetAttribute("xmlns:" + entry.Key, value);
			}

			var wrapperXaml =
				$"<ResourceDictionary xmlns=\"{XamlConstants.PresentationNamespace}\" xmlns:x=\"{XamlConstants.XamlNamespace}\">" +
				clonedElement.OuterXml +
				"</ResourceDictionary>";

			var loaded = (ResourceDictionary)Microsoft.UI.Xaml.Markup.XamlReader.Load(wrapperXaml);

			foreach (var key in loaded.Keys) {
				if (loaded[key] is FrameworkTemplate template)
					return template;
			}
			return null;
		}
	}
}
