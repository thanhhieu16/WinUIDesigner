using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using WinUIDesigner.XamlDom;
using WinUIDesigner.TestHost;

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
		// ParseObjectPropValue moved to TestHelper (shared with MarkupExtensionTests, which had a
		// cosmetically-identical copy); this class doesn't inherit TestHelper, so calls below are
		// qualified.

		[Test]
		public void StaticResourceMapsPositionalArgumentToResourceKey()
		{
			WinUITestHost.Run(() => {
				var value = TestHelper.ParseObjectPropValue("{StaticResource someKey}");

				Assert.IsInstanceOf<StaticResourceExtensionPlaceholder>(value.Instance);
				Assert.IsTrue(value.IsMarkupExtension);
				Assert.AreEqual("someKey", ((StaticResourceExtensionPlaceholder)value.Instance).ResourceKey);
			});
		}

		[Test]
		public void ThemeResourceMapsPositionalArgumentToResourceKey()
		{
			WinUITestHost.Run(() => {
				var value = TestHelper.ParseObjectPropValue("{ThemeResource someKey}");

				Assert.IsInstanceOf<ThemeResourceExtensionPlaceholder>(value.Instance);
				Assert.AreEqual("someKey", ((ThemeResourceExtensionPlaceholder)value.Instance).ResourceKey);
			});
		}

		/// <summary>
		/// Whole-branch review Important #1: <c>ThemeResourceExtensionPlaceholder.ProvideValue()</c>
		/// unconditionally returned null, and nothing else resolved it, so every
		/// <c>{ThemeResource ...}</c> silently evaluated to null - unlike <c>{StaticResource}</c>,
		/// which <see cref="TemplateHelperTests.ControlTemplateWithResourceReference"/> already
		/// proves resolves for real. Fixed by giving <c>ThemeResourceExtensionPlaceholder</c> the
		/// same <see cref="ThemeResourceWrapper"/> treatment <c>StaticResourceExtensionPlaceholder</c>
		/// already had (see <see cref="XamlObject.CreateWrapper"/>). This test follows
		/// <c>ControlTemplateWithResourceReference</c>'s pattern directly (a <c>Grid.Resources</c>
		/// brush referenced from a child element) rather than only checking the placeholder's own
		/// properties, so it fails if <c>ProvideValue()</c> ever again returns null instead of the
		/// real resource.
		/// </summary>
		/// <remarks>
		/// Asserts reference identity to the <see cref="Grid.Resources"/> entry rather than reading
		/// <see cref="SolidColorBrush.Color"/> (the way <c>ControlTemplateWithResourceReference</c>
		/// does) deliberately: that test's Color check passes through WinUI's own native
		/// <c>XamlReader.Load</c> (see <see cref="TemplateHelper.GetFrameworkTemplate"/>, which hands
		/// the whole template fragment to it), not this codebase's own attribute-parsing pipeline.
		/// Parsing <c>Color="Red"</c> through this library's own <c>XamlProperty</c>/TypeConverter
		/// path was found, while writing this test, to silently leave <c>Color</c> at its default
		/// (no <c>TypeConverter</c> recognizes WinUI's <c>Windows.UI.Color</c> the way WPF's
		/// <c>System.Windows.Media.Color</c> carries <c>ColorConverter</c>) - a real, pre-existing
		/// gap, but a separate one from Important #1 (ThemeResource resolution) and out of scope for
		/// this fix pass. Reference identity to the actual registered resource object sidesteps it
		/// while still proving exactly what Important #1 asks: that <c>{ThemeResource MyBrush}</c>
		/// resolves to the real resource, not null.
		/// </remarks>
		[Test]
		public void ThemeResourceResolvesToRealResource()
		{
			var xaml = @"
<Grid
    xmlns=""" + XamlConstants.PresentationNamespace + @"""
    xmlns:x=""" + XamlConstants.XamlNamespace + @""">
    <Grid.Resources>
        <SolidColorBrush x:Key=""MyBrush"" Color=""Red"" />
    </Grid.Resources>
    <Border Background=""{ThemeResource MyBrush}"" />
</Grid>";

			WinUITestHost.Run(() => {
				var doc = XamlParser.Parse(new StringReader(xaml));
				Assert.IsNotNull(doc.RootInstance);

				var grid = (Grid)doc.RootInstance;
				Assert.AreEqual(1, grid.Children.Count, "Grid did not parse its Border child");
				var border = (Border)grid.Children[0];
				Assert.IsNotNull(border.Background,
					"{ThemeResource MyBrush} did not resolve at all - Border.Background is null");
				Assert.IsTrue(grid.Resources.ContainsKey("MyBrush"), "Grid.Resources did not contain 'MyBrush'");
				Assert.AreSame(grid.Resources["MyBrush"], border.Background,
					"{ThemeResource MyBrush} did not resolve to the actual registered resource object");
			});
		}

		[Test]
		public void PrefixedNullResolvesToPlaceholder()
		{
			WinUITestHost.Run(() => {
				// The tokenizer keeps the prefix, so the type name here is literally "x:Null".
				var value = TestHelper.ParseObjectPropValue("{x:Null}");

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
