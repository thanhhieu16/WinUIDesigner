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
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Allows finding types in a set of assemblies.
	/// </summary>
	public class XamlTypeFinder : ICloneable
	{
		public XamlTypeFinder()
		{
			_registeredAssemblies = new List<Assembly>();
		}

		sealed class AssemblyNamespaceMapping : IEquatable<AssemblyNamespaceMapping>
		{
			internal readonly Assembly Assembly;
			internal readonly string Namespace;

			internal AssemblyNamespaceMapping(Assembly assembly, string @namespace)
			{
				this.Assembly = assembly;
				this.Namespace = @namespace;
			}

			public override int GetHashCode()
			{
				return Assembly.GetHashCode() ^ Namespace.GetHashCode();
			}

			public override bool Equals(object obj)
			{
				return Equals(obj as AssemblyNamespaceMapping);
			}

			public bool Equals(AssemblyNamespaceMapping other)
			{
				return other != null && other.Assembly == this.Assembly && other.Namespace == this.Namespace;
			}
		}

		sealed class XamlNamespace
		{
			internal readonly string XmlNamespacePrefix;
			internal readonly string XmlNamespace;

			internal XamlNamespace(string xmlNamespacePrefix, string xmlNamespace)
			{
				this.XmlNamespacePrefix = xmlNamespacePrefix;
				this.XmlNamespace = xmlNamespace;
			}

			internal List<AssemblyNamespaceMapping> ClrNamespaces = new List<AssemblyNamespaceMapping>();

			internal XamlNamespace Clone()
			{
				XamlNamespace copy = new XamlNamespace(this.XmlNamespacePrefix, this.XmlNamespace);
				// AssemblyNamespaceMapping is immutable
				copy.ClrNamespaces.AddRange(this.ClrNamespaces);
				return copy;
			}
		}

		Dictionary<string, XamlNamespace> namespaces = new Dictionary<string, XamlNamespace>();
		Dictionary<AssemblyNamespaceMapping, string> reverseDict = new Dictionary<AssemblyNamespaceMapping, string>();
		Dictionary<AssemblyNamespaceMapping, List<string>> reverseDictList = new Dictionary<AssemblyNamespaceMapping, List<string>>();

		/// <summary>
		/// Gets a type referenced in XAML.
		/// </summary>
		/// <param name="xmlNamespace">The XML namespace to use to look up the type.
		/// This can be a registered namespace or a 'clr-namespace' value.</param>
		/// <param name="localName">The local name of the type to find.</param>
		/// <returns>
		/// The requested type, or null if it could not be found.
		/// </returns>
		public Type GetType(string xmlNamespace, string localName)
		{
			if (xmlNamespace == null)
				throw new ArgumentNullException("xmlNamespace");
			if (localName == null)
				throw new ArgumentNullException("localName");
			XamlNamespace ns;
			if (!namespaces.TryGetValue(xmlNamespace, out ns)) {
				if (xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal)) {
					ns = namespaces[xmlNamespace] = ParseNamespace(xmlNamespace);
				} else if (xmlNamespace.StartsWith(UsingPrefix, StringComparison.Ordinal)) {
					ns = ParseUsingNamespace(xmlNamespace);
					// Only cache a resolution that found something: a "using:" namespace carries no
					// assembly name, so an assembly loaded later could still supply it.
					if (ns.ClrNamespaces.Count > 0)
						namespaces[xmlNamespace] = ns;
				} else {
					return null;
				}
			}
			foreach (AssemblyNamespaceMapping mapping in ns.ClrNamespaces) {
				Type type = mapping.Assembly.GetType(mapping.Namespace + "." + localName);
				if (type != null)
					return type;
			}
			return null;
		}

		/// <summary>
		/// Gets the XML namespace that can be used for the specified assembly/namespace combination.
		/// </summary>
		public string GetXmlNamespaceFor(Assembly assembly, string @namespace, bool getClrNamespace = false)
		{
			AssemblyNamespaceMapping mapping = new AssemblyNamespaceMapping(assembly, @namespace);
			string xmlNamespace;
			if (!getClrNamespace && reverseDict.TryGetValue(mapping, out xmlNamespace)) {
				return xmlNamespace;
			} else {
				return "clr-namespace:" + mapping.Namespace + ";assembly=" + mapping.Assembly.GetName().Name;
			}
		}

		/// <summary>
		/// Gets the XML namespaces that can be used for the specified assembly/namespace combination.
		/// </summary>
		public List<string> GetXmlNamespacesFor(Assembly assembly, string @namespace, bool getClrNamespace = false)
		{
			AssemblyNamespaceMapping mapping = new AssemblyNamespaceMapping(assembly, @namespace);
			List<string> xmlNamespaces;
			if (!getClrNamespace && reverseDictList.TryGetValue(mapping, out xmlNamespaces)) {
				return xmlNamespaces;
			} else {
				return new List<string>() { "clr-namespace:" + mapping.Namespace + ";assembly=" + mapping.Assembly.GetName().Name };
			}
		}

		/// <summary>
		/// Gets the prefix to use for the specified XML namespace,
		/// or null if no suitable prefix could be found.
		/// </summary>
		public string GetPrefixForXmlNamespace(string xmlNamespace)
		{
			XamlNamespace ns;

			if (namespaces.TryGetValue(xmlNamespace, out ns)) {
				return ns.XmlNamespacePrefix;
			} else {
				return null;
			}
		}

		internal const string UsingPrefix = "using:";

		/// <summary>
		/// Resolves a WinUI <c>using:SomeClrNamespace</c> XML namespace.
		/// </summary>
		/// <remarks>
		/// <para>
		/// WPF spells a CLR-namespace mapping <c>clr-namespace:Ns;assembly=Asm</c>, which is what
		/// the reference <see cref="ParseNamespace"/> handles and what the reference tests use.
		/// WinUI's XAML dialect uses <c>using:Ns</c> instead, with no assembly part - verified
		/// empirically against WinUI's own <c>XamlReader.Load</c>: loading
		/// <c>&lt;t:ExampleClass xmlns:t="using:WinUIDesigner.XamlDom.Tests" .../&gt;</c> succeeds,
		/// while the identical document with
		/// <c>xmlns:t="clr-namespace:WinUIDesigner.XamlDom.Tests;assembly=WinUIDesigner.XamlDom.Tests"</c>
		/// fails with "... is an unknown namespace". <c>clr-namespace:</c> support is kept as-is
		/// (it costs nothing and remains useful for tooling), but <c>using:</c> has to work for any
		/// real WinUI XAML to be parseable at all.
		/// </para>
		/// <para>
		/// Because <c>using:</c> carries no assembly name, candidate assemblies are the explicitly
		/// registered ones, the entry assembly, and everything already loaded - mirroring how
		/// WinUI itself resolves such a namespace through the app's XAML metadata provider, which
		/// covers the app and its referenced components.
		/// </para>
		/// </remarks>
		XamlNamespace ParseUsingNamespace(string xmlNamespace)
		{
			string namespaceName = xmlNamespace.Substring(UsingPrefix.Length).Trim();
			XamlNamespace ns = new XamlNamespace(null, xmlNamespace);

			foreach (Assembly asm in GetUsingCandidateAssemblies()) {
				if (AssemblyDeclaresNamespace(asm, namespaceName)) {
					var mapping = new AssemblyNamespaceMapping(asm, namespaceName);
					if (!ns.ClrNamespaces.Contains(mapping))
						AddMappingToNamespace(ns, mapping);
				}
			}
			return ns;
		}

		IEnumerable<Assembly> GetUsingCandidateAssemblies()
		{
			var seen = new HashSet<Assembly>();
			foreach (Assembly asm in _registeredAssemblies) {
				if (seen.Add(asm))
					yield return asm;
			}
			Assembly entry = Assembly.GetEntryAssembly();
			if (entry != null && seen.Add(entry))
				yield return entry;
			foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
				if (!asm.IsDynamic && seen.Add(asm))
					yield return asm;
			}
		}

		static bool AssemblyDeclaresNamespace(Assembly assembly, string namespaceName)
		{
			try {
				foreach (Type t in assembly.GetExportedTypes()) {
					if (t.Namespace == namespaceName)
						return true;
				}
			} catch (Exception) {
				// A partially-loadable assembly simply isn't a candidate.
			}
			return false;
		}

		XamlNamespace ParseNamespace(string xmlNamespace)
		{
			string name = xmlNamespace;
			Debug.Assert(name.StartsWith("clr-namespace:", StringComparison.Ordinal));
			name = name.Substring("clr-namespace:".Length);
			string namespaceName, assembly;
			int pos = name.IndexOf(';');
			if (pos < 0) {
				namespaceName = name;
				assembly = "";
			} else {
				namespaceName = name.Substring(0, pos);
				name = name.Substring(pos + 1).Trim();
				if (!name.StartsWith("assembly=", StringComparison.Ordinal)) {
					throw new XamlLoadException("Expected: 'assembly='");
				}
				assembly = name.Substring("assembly=".Length);
			}
			XamlNamespace ns = new XamlNamespace(null, xmlNamespace);

			Assembly asm = LoadAssembly(assembly);

			if (asm == null && assembly == "mscorlib")
				asm = typeof (Boolean).Assembly;

			if (asm != null) {
				AddMappingToNamespace(ns, new AssemblyNamespaceMapping(asm, namespaceName));
			}
			return ns;
		}

		void AddMappingToNamespace(XamlNamespace ns, AssemblyNamespaceMapping mapping)
		{
			ns.ClrNamespaces.Add(mapping);

			List<string> xmlNamespaceList;
			if (reverseDictList.TryGetValue(mapping, out xmlNamespaceList)) {
				if (!xmlNamespaceList.Contains(ns.XmlNamespace))
					xmlNamespaceList.Add(ns.XmlNamespace);
			}
			else
				reverseDictList.Add(mapping, new List<string>(){ ns.XmlNamespace });

			string xmlNamespace;
			if (reverseDict.TryGetValue(mapping, out xmlNamespace)) {
				if (xmlNamespace == XamlConstants.PresentationNamespace) {
					return;
				}
			}
			reverseDict[mapping] = ns.XmlNamespace;
		}

		private List<Assembly> _registeredAssemblies;

		public ReadOnlyCollection<Assembly> RegisteredAssemblies
		{
			get
			{
				return new ReadOnlyCollection<Assembly>(_registeredAssemblies);
			}
		}

		/// <summary>
		/// Registers <paramref name="assembly"/> for later lookup (see <see cref="RegisteredAssemblies"/>
		/// and <see cref="LoadAssembly"/>'s callers). Does not itself discover any XML-namespace
		/// mappings - see remarks.
		/// </summary>
		/// <remarks>
		/// WPF's reference implementation additionally scans
		/// <c>System.Windows.Markup.XmlnsPrefixAttribute</c>/<c>XmlnsDefinitionAttribute</c> custom
		/// attributes on <paramref name="assembly"/> to discover namespace mappings. WinUI carries no
		/// equivalent of either attribute (verified absent from the assembly - see
		/// <see cref="WinUITypeFinder"/>'s remarks), so attribute-based discovery does not apply on
		/// this platform and there is nothing to port here. This is not a gap in practice:
		/// <c>clr-namespace:</c> xmlns resolution (<see cref="ParseNamespace"/>) is entirely
		/// self-contained - the CLR namespace and assembly name are spelled out literally in the URI
		/// itself - and was never dependent on attribute scanning on either platform. This method is
		/// kept for its assembly-registration bookkeeping role (backing <see cref="RegisteredAssemblies"/>
		/// and the <c>using:</c> namespace resolution in <see cref="ParseUsingNamespace"/>); explicit
		/// namespace mappings are registered directly via <see cref="RegisterNamespaceMapping"/>
		/// instead (see <see cref="WinUITypeFinder"/> for how the built-in WinUI namespaces are set up
		/// that way).
		/// </remarks>
		public void RegisterAssembly(Assembly assembly)
		{
			if (assembly == null)
				throw new ArgumentNullException("assembly");

			_registeredAssemblies.Add(assembly);

			// Attribute-based xmlns discovery removed: WinUI has no XmlnsPrefixAttribute/
			// XmlnsDefinitionAttribute equivalent (verified — see remarks above). Callers
			// currently get no namespace mappings out of this method; use
			// RegisterNamespaceMapping for explicit registration instead.
		}

		/// <summary>
		/// Registers a direct CLR-namespace-to-XML-namespace mapping without relying on assembly-level
		/// Xmlns custom attributes (which WinUI does not carry — see <see cref="RegisterAssembly"/>).
		/// </summary>
		internal void RegisterNamespaceMapping(string xmlNamespace, Assembly assembly, string clrNamespace)
		{
			XamlNamespace ns;
			if (!namespaces.TryGetValue(xmlNamespace, out ns)) {
				ns = namespaces[xmlNamespace] = new XamlNamespace(null, xmlNamespace);
			}
			AddMappingToNamespace(ns, new AssemblyNamespaceMapping(assembly, clrNamespace));
		}

		/// <summary>
		/// Register the Namspaces not found in any Assembly, but used by VS and Expression Blend
		/// </summary>
		public void RegisterDesignerNamespaces()
		{
			var ns = namespaces[XamlConstants.DesignTimeNamespace] = new XamlNamespace("d", XamlConstants.DesignTimeNamespace);
			AddMappingToNamespace(ns, new AssemblyNamespaceMapping(typeof(DesignTimeProperties).Assembly, typeof(DesignTimeProperties).Namespace));
			ns = namespaces[XamlConstants.MarkupCompatibilityNamespace] = new XamlNamespace("mc", XamlConstants.MarkupCompatibilityNamespace);
			AddMappingToNamespace(ns, new AssemblyNamespaceMapping(typeof(MarkupCompatibilityProperties).Assembly, typeof(MarkupCompatibilityProperties).Namespace));
		}

		/// <summary>
		/// Load the assembly with the specified name.
		/// You can override this method to implement custom assembly lookup.
		/// </summary>
		public virtual Assembly LoadAssembly(string name)
		{
			return Assembly.Load(name);
		}

		/// <summary>
		/// Clones this XamlTypeFinder.
		/// </summary>
		public virtual XamlTypeFinder Clone()
		{
			XamlTypeFinder copy = new XamlTypeFinder();
			copy.ImportFrom(this);
			return copy;
		}

		/// <summary>
		/// Import information from another XamlTypeFinder.
		/// Use this if you override Clone().
		/// </summary>
		protected void ImportFrom(XamlTypeFinder source)
		{
			if (source == null)
				throw new ArgumentNullException("source");

			_registeredAssemblies.AddRange(source.RegisteredAssemblies);

			foreach (KeyValuePair<string, XamlNamespace> pair in source.namespaces) {
				this.namespaces.Add(pair.Key, pair.Value.Clone());
			}
			foreach (KeyValuePair<AssemblyNamespaceMapping, string> pair in source.reverseDict) {
				this.reverseDict.Add(pair.Key, pair.Value);
			}
			foreach (KeyValuePair<AssemblyNamespaceMapping, List<string>> pair in source.reverseDictList) {
				this.reverseDictList.Add(pair.Key, pair.Value.ToList());
			}
		}

		object ICloneable.Clone()
		{
			return this.Clone();
		}

		/// <summary>
		/// Creates a new XamlTypeFinder where the WinUI namespaces are registered.
		/// </summary>
		public static XamlTypeFinder CreateWinUITypeFinder()
		{
			return WinUITypeFinder.Instance.Clone();
		}

		/// <summary>
		/// Converts the specified <see cref="Uri"/> to local.
		/// </summary>
		public virtual Uri ConvertUriToLocalUri(Uri uri)
		{
			return uri;
		}

		static class WinUITypeFinder
		{
			internal static readonly XamlTypeFinder Instance;

			// Hand-maintained list of WinUI's public, XAML-element-tag-bearing CLR namespaces.
			// Enumerated directly from Microsoft.UI.Xaml.winmd's public namespaces (not guessed).
			// WinUI carries no XmlnsDefinitionAttribute/XmlnsPrefixAttribute custom attributes at
			// all, so attribute-scanning (RegisterAssembly) cannot discover these; they are
			// registered directly against XamlConstants.PresentationNamespace, which was
			// empirically confirmed against WinUI 3's real XAML compiler: a minimal Window/Grid
			// probe using this namespace as the default xmlns compiled successfully, with a plain
			// <Grid> or <Button> (no prefix) resolving - i.e. the WinUI XAML compiler treats this
			// one namespace as covering all of them.
			static readonly string[] KnownWinUINamespaces = {
				"Microsoft.UI.Xaml", "Microsoft.UI.Xaml.Automation",
				"Microsoft.UI.Xaml.Automation.Peers", "Microsoft.UI.Xaml.Automation.Provider",
				"Microsoft.UI.Xaml.Automation.Text", "Microsoft.UI.Xaml.Controls",
				"Microsoft.UI.Xaml.Controls.Primitives", "Microsoft.UI.Xaml.Data",
				"Microsoft.UI.Xaml.Documents", "Microsoft.UI.Xaml.Input",
				"Microsoft.UI.Xaml.Markup", "Microsoft.UI.Xaml.Media",
				"Microsoft.UI.Xaml.Media.Animation", "Microsoft.UI.Xaml.Media.Imaging",
				"Microsoft.UI.Xaml.Media.Media3D", "Microsoft.UI.Xaml.Navigation",
				"Microsoft.UI.Xaml.Shapes",
			};

			[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline",
			                                                 Justification = "We're using an explicit constructor to get it's lazy-loading semantics.")]
			static WinUITypeFinder()
			{
				Instance = new XamlTypeFinder();
				Instance.RegisterDesignerNamespaces();

				var winUIAssembly = typeof(Microsoft.UI.Xaml.DependencyObject).Assembly;
				foreach (var clrNamespace in KnownWinUINamespaces) {
					Instance.RegisterNamespaceMapping(XamlConstants.PresentationNamespace, winUIAssembly, clrNamespace);
				}
			}
		}
	}
}
