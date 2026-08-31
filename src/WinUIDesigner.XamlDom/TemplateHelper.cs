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
using System.Text;
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
	/// This version also splices in resources declared on an ancestor element
	/// (e.g. a brush defined on the containing Grid.Resources) as a merged
	/// dictionary, since XamlReader.Load has no post-construction
	/// resource-injection hook the way WPF's XamlObjectWriterSettings.
	/// BeforePropertiesHandler did (Task 8 / Gap 6). This only recovers
	/// resources declared inside the same parsed XAML document - resources
	/// set outside the document (app-level or theme resources) are out of
	/// scope for Phase 1, same limitation the reference WPF implementation
	/// effectively had for a standalone parser. It also stamps a temporary
	/// x:Key onto the cloned template element when the source XAML had none
	/// (the property-element-syntax case, e.g. &lt;Control.Template&gt;
	/// &lt;ControlTemplate ...&gt;), since the fragment is always wrapped in a
	/// synthetic ResourceDictionary and WinUI's loader rejects a keyless
	/// dictionary item - see GetFrameworkTemplate's remarks (this was Task 5's
	/// review Minor #11, addressed here rather than deferred).
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

			// A template authored via property-element syntax (e.g. <Control.Template><ControlTemplate
			// ...>...</ControlTemplate></Control.Template>) carries no x:Key in the original document -
			// none is needed there, since at that point the template isn't a dictionary item. But
			// GetFrameworkTemplate always wraps the fragment in a synthetic ResourceDictionary (below)
			// so ancestor-resource splicing has somewhere to attach MergedDictionaries, and
			// Microsoft.UI.Xaml.Markup.XamlReader.Load rejects a keyless dictionary item ("Items added
			// to a dictionary must have a key" - confirmed empirically by probing this exact scenario).
			// Ported from the WPF reference (TemplateHelper.cs, ~lines 76-79), which stamps the same
			// temporary key for the same reason; the lookup loop below already returns whichever entry
			// is a FrameworkTemplate regardless of its key, so the temporary value never needs reading
			// back or stripping.
			if (string.IsNullOrEmpty(clonedElement.GetAttribute("Key", XamlConstants.XamlNamespace))) {
				clonedElement.SetAttribute("Key", XamlConstants.XamlNamespace, "$$temp&&§§%%__");
			}

			var mergedResourcesXaml = BuildMergedAncestorResourcesXaml(parentObject);

			var wrapperXaml =
				$"<ResourceDictionary xmlns=\"{XamlConstants.PresentationNamespace}\" xmlns:x=\"{XamlConstants.XamlNamespace}\">" +
				(mergedResourcesXaml.Length > 0
					? $"<ResourceDictionary.MergedDictionaries><ResourceDictionary>{mergedResourcesXaml}</ResourceDictionary></ResourceDictionary.MergedDictionaries>"
					: "") +
				clonedElement.OuterXml +
				"</ResourceDictionary>";

			var loaded = (ResourceDictionary)Microsoft.UI.Xaml.Markup.XamlReader.Load(wrapperXaml);

			foreach (var key in loaded.Keys) {
				if (loaded[key] is FrameworkTemplate template)
					return template;
			}
			return null;
		}

		/// <summary>
		/// Collects the XAML of every resource declared in a <c>*.Resources</c> property element
		/// (e.g. <c>Grid.Resources</c>) on <paramref name="parentObject"/> or any of its ancestors,
		/// so it can be merged into the synthetic wrapper <see cref="ResourceDictionary"/> that
		/// hosts the template fragment being loaded. Without this, a <c>{StaticResource}</c> inside
		/// the template can only see resources declared inside the template's own fragment.
		/// </summary>
		static string BuildMergedAncestorResourcesXaml(XamlObject parentObject)
		{
			var sb = new StringBuilder();
			var obj = parentObject;
			while (obj != null) {
				foreach (XmlNode child in obj.XmlElement.ChildNodes) {
					if (child is XmlElement el && el.LocalName.EndsWith(".Resources"))
						AppendResourceItems(sb, el);
				}
				obj = obj.ParentObject;
			}
			return sb.ToString();
		}

		/// <summary>
		/// Appends the XAML of each actual resource item found under a <c>*.Resources</c> property
		/// element. Handles both the implicit form (resource items are direct children of the
		/// property element, e.g. <c>&lt;Grid.Resources&gt;&lt;SolidColorBrush .../&gt;...</c>) and
		/// the explicit form (a single <c>&lt;ResourceDictionary&gt;</c> wrapper child, whose own
		/// children are the actual resources) - both are valid, commonly-used XAML shapes and only
		/// the second one needs unwrapping: splicing the wrapper element itself into
		/// <c>ResourceDictionary.MergedDictionaries</c> would nest a keyless <c>ResourceDictionary</c>
		/// directly inside another dictionary's item collection, which
		/// <c>Microsoft.UI.Xaml.Markup.XamlReader.Load</c> rejects with "Items added to a dictionary
		/// must have a key" (confirmed empirically against the explicit form).
		/// </summary>
		static void AppendResourceItems(StringBuilder sb, XmlElement resourcesElement)
		{
			foreach (XmlNode resourceNode in resourcesElement.ChildNodes) {
				if (!(resourceNode is XmlElement resourceElement))
					continue;
				if (resourceElement.LocalName == "ResourceDictionary") {
					foreach (XmlNode innerNode in resourceElement.ChildNodes) {
						if (innerNode is XmlElement innerElement)
							sb.Append(innerElement.OuterXml);
					}
				} else {
					sb.Append(resourceElement.OuterXml);
				}
			}
		}
	}
}
