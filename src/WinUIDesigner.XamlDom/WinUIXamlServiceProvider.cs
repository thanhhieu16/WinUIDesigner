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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Backs Microsoft.UI.Xaml.Markup.MarkupExtension.ProvideValue(IXamlServiceProvider)
	/// for markup extensions parsed out of the XAML DOM. Deliberately much smaller
	/// than WPF's System.Xaml-based service provider: WinUI's contract has no
	/// ambient-value search, no name resolver, and no schema context service.
	/// </summary>
	public class WinUIXamlServiceProvider : IXamlServiceProvider, IProvideValueTarget, IXamlTypeResolver, IRootObjectProvider
	{
		/// <summary>
		/// Creates a service provider for the specified <see cref="XamlObject"/>.
		/// </summary>
		public WinUIXamlServiceProvider(XamlObject obj)
		{
			if (obj == null)
				throw new ArgumentNullException(nameof(obj));
			XamlObject = obj;
			Resolver = new XamlTypeResolverProvider(obj);
		}

		/// <summary>
		/// Gets the <see cref="XamlObject"/> this service provider was created for.
		/// </summary>
		public XamlObject XamlObject { get; }

		internal XamlTypeResolverProvider Resolver { get; }

		/// <inheritdoc/>
		public object GetService(Type type)
		{
			if (type == typeof(IProvideValueTarget)) return this;
			if (type == typeof(IXamlTypeResolver)) return this;
			if (type == typeof(XamlTypeResolverProvider)) return Resolver;
			if (type == typeof(IRootObjectProvider)) return this;
			return null;
		}

		/// <inheritdoc/>
		public object TargetObject
		{
			get
			{
				var parentProperty = XamlObject.ParentProperty;
				if (parentProperty == null)
					return null;
				if (parentProperty.IsCollection)
					return parentProperty.ValueOnInstance;
				return parentProperty.ParentObject.Instance;
			}
		}

		/// <inheritdoc/>
		public object TargetProperty
		{
			get
			{
				var parentProperty = XamlObject.ParentProperty;
				return parentProperty?.DependencyProperty;
			}
		}

		/// <inheritdoc/>
		public Type Resolve(string qualifiedTypeName) => Resolver.Resolve(qualifiedTypeName);

		/// <inheritdoc/>
		public object RootObject => XamlObject.OwnerDocument.RootInstance;
	}

	/// <summary>
	/// Resolves <c>{x:Reference name}</c> against the owning document's flat name table
	/// (see <see cref="NameScopeHelper"/>).
	/// </summary>
	/// <remarks>
	/// WPF resolves <c>{x:Reference}</c> through <c>System.Xaml</c>'s <c>IXamlNameResolver</c>
	/// service, backed by a real <c>System.Windows.NameScope</c>. WinUI has neither
	/// (<c>NameScope</c>/<c>INameScope</c> are absent from Microsoft.UI.Xaml.winmd, and its
	/// <c>IXamlServiceProvider</c> contract has no name-resolver service), and there is also no
	/// <c>Microsoft.UI.Xaml.Markup.ReferenceExtension</c> runtime type to attach a wrapper to -
	/// verified absent by reflection over Microsoft.WinUI.dll. This wrapper is therefore driven
	/// by this codebase's own <see cref="ReferenceExtensionPlaceholder"/>, in the same way
	/// <c>{StaticResource}</c>/<c>{ThemeResource}</c>/<c>{x:Null}</c> are (see
	/// <see cref="MarkupExtensionParser"/>).
	/// </remarks>
	class ReferenceExtensionWrapper : MarkupExtensionWrapper
	{
		public ReferenceExtensionWrapper(XamlObject xamlObject) : base(xamlObject) { }

		public override object ProvideValue()
		{
			if (XamlObject.Properties.Count == 0)
				return null;
			// {x:Reference name} has exactly one positional arg, mapped to the Name property.
			var nameProperty = XamlObject.Properties[0];
			var name = (nameProperty.PropertyValue as XamlTextValue)?.Text;
			if (name == null)
				return null;
			var target = NameScopeHelper.FindNamedObject(XamlObject.OwnerDocument, name);
			return target?.Instance;
		}
	}
}
