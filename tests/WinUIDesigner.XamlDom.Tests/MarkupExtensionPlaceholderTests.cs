using System.IO;
using System.Linq;
using NUnit.Framework;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.XamlDom.Tests
{
	/// <summary>
	/// Covers the stand-in markup-extension types this codebase declares for WinUI's compiler
	/// intrinsics (see <see cref="StaticResourceExtensionPlaceholder"/> and friends).
	/// </summary>
	/// <remarks>
	/// WinUI has no runtime type behind <c>{StaticResource}</c>, <c>{ThemeResource}</c>,
	/// <c>{x:Null}</c> or <c>{x:Reference}</c> - verified: its whole assembly declares exactly two
	/// public "*Extension"-named types, <c>IDataTemplateExtension</c> and <c>MarkupExtension</c>.
	/// These tests pin the placeholder mechanism end to end: name resolution, construction through
	/// the parser's positional-argument handling, and the resulting property value. They cannot go
	/// through <see cref="TestHelper.TestLoading"/> because WinUI's own XamlReader is the very
	/// thing that cannot represent these forms outside its compiler.
	/// </remarks>
	[TestFixture]
	public class MarkupExtensionPlaceholderTests
	{
		static XamlObject ParseObjectPropValue(string markupExtension)
		{
			var xaml =
				"<t:ExampleClass xmlns=\"" + XamlConstants.PresentationNamespace + "\"" +
				" xmlns:t=\"" + XamlTypeFinderTests.XamlDomTestsNamespace + "\"" +
				" xmlns:x=\"" + XamlConstants.XamlNamespace + "\"" +
				" ObjectProp=\"" + markupExtension + "\" />";
			var doc = XamlParser.Parse(new StringReader(xaml));
			var property = doc.RootElement.Properties.Single(p => p.PropertyName == "ObjectProp");
			return (XamlObject)property.PropertyValue;
		}

		[Test]
		public void StaticResourceMapsPositionalArgumentToResourceKey()
		{
			WinUITestHost.Run(() => {
				var value = ParseObjectPropValue("{StaticResource someKey}");

				Assert.IsInstanceOf<StaticResourceExtensionPlaceholder>(value.Instance);
				Assert.IsTrue(value.IsMarkupExtension);
				Assert.AreEqual("someKey", ((StaticResourceExtensionPlaceholder)value.Instance).ResourceKey);
			});
		}

		[Test]
		public void ThemeResourceMapsPositionalArgumentToResourceKey()
		{
			WinUITestHost.Run(() => {
				var value = ParseObjectPropValue("{ThemeResource someKey}");

				Assert.IsInstanceOf<ThemeResourceExtensionPlaceholder>(value.Instance);
				Assert.AreEqual("someKey", ((ThemeResourceExtensionPlaceholder)value.Instance).ResourceKey);
			});
		}

		[Test]
		public void PrefixedNullResolvesToPlaceholder()
		{
			WinUITestHost.Run(() => {
				// The tokenizer keeps the prefix, so the type name here is literally "x:Null".
				var value = ParseObjectPropValue("{x:Null}");

				Assert.IsInstanceOf<NullExtensionPlaceholder>(value.Instance);
			});
		}

		[Test]
		public void ReferenceResolvesNamedObjectThroughTheDocumentNameTable()
		{
			WinUITestHost.Run(() => {
				var xaml =
					"<ExampleClassContainer xmlns=\"" + XamlTypeFinderTests.XamlDomTestsNamespace + "\"" +
					" xmlns:x=\"" + XamlConstants.XamlNamespace + "\">" +
					"<ExampleClass x:Name=\"target\" OtherProp=\"a\" />" +
					"<ExampleClass ObjectProp=\"{x:Reference target}\" />" +
					"</ExampleClassContainer>";
				var doc = XamlParser.Parse(new StringReader(xaml));

				var named = NameScopeHelper.FindNamedObject(doc, "target");
				Assert.IsNotNull(named, "x:Name was not recorded in the document's name table");

				var example = doc.RootElement.Properties
					.Single(p => p.PropertyName == "List").CollectionElements
					.OfType<XamlObject>()
					.Single(o => o.Properties.Any(p => p.PropertyName == "ObjectProp"));
				var reference = (XamlObject)example.Properties.Single(p => p.PropertyName == "ObjectProp").PropertyValue;

				Assert.IsInstanceOf<ReferenceExtensionPlaceholder>(reference.Instance);
				Assert.AreEqual("target", ((ReferenceExtensionPlaceholder)reference.Instance).Name);
				Assert.AreSame(named.Instance, ((ExampleClass)example.Instance).ObjectProp,
					"{x:Reference} did not resolve to the named object's instance");
			});
		}
	}
}
