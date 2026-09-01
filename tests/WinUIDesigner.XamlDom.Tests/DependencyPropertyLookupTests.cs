using System.IO;
using System.Linq;
using NUnit.Framework;
using WinUIDesigner.XamlDom;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.XamlDom.Tests
{
	/// <summary>
	/// Guards the WinUI-specific dependency-property discovery in
	/// <c>WinUIStaticMemberLocator</c>.
	/// </summary>
	/// <remarks>
	/// WPF declares dependency-property accessors as <c>public static readonly</c> <b>fields</b>,
	/// and the reference implementation looks them up with <c>Type.GetField</c> throughout (plus
	/// <c>DependencyPropertyDescriptor</c>, which WinUI has no counterpart for). WinUI's C#/WinRT
	/// projection declares them as <b>static properties</b> instead, so a field-only lookup finds
	/// nothing on any WinUI framework type. Both shapes must work: framework types use the
	/// property form, ordinary C# types written by hand (this codebase's own attached-property
	/// holders, a user's project) still use the field form. These tests pin down one of each -
	/// without them a regression to field-only lookup would leave every
	/// <c>XamlProperty.DependencyProperty</c> silently null while the load tests still passed.
	/// </remarks>
	[TestFixture]
	public class DependencyPropertyLookupTests
	{
		[Test]
		public void NormalPropertyResolvesWinUIStaticPropertyAccessor()
		{
			WinUITestHost.Run(() => {
				var doc = XamlParser.Parse(new StringReader(
					"<Grid xmlns=\"" + XamlConstants.PresentationNamespace + "\" Width=\"300\" />"));
				var width = doc.RootElement.Properties.Single(p => p.PropertyName == "Width");

				Assert.AreEqual(Microsoft.UI.Xaml.FrameworkElement.WidthProperty, width.DependencyProperty,
					"Width's DependencyProperty was not recovered from FrameworkElement's static " +
					"WidthProperty accessor (WinUI declares it as a property, not a field).");
				Assert.AreEqual(300.0, ((Microsoft.UI.Xaml.Controls.Grid)doc.RootInstance).Width);
			});
		}

		[Test]
		public void AttachedPropertyResolvesPlainStaticFieldAccessor()
		{
			WinUITestHost.Run(() => {
				var doc = XamlParser.Parse(new StringReader(
					"<Grid xmlns=\"" + XamlConstants.PresentationNamespace + "\"" +
					" xmlns:t=\"" + XamlTypeFinderTests.XamlDomTestsNamespace + "\"" +
					" t:ExampleService.Example=\"attached value\" />"));
				var attached = doc.RootElement.Properties.Single(p => p.PropertyName == "Example");

				Assert.IsTrue(attached.IsAttached);
				Assert.AreEqual(ExampleService.ExampleProperty, attached.DependencyProperty);
				Assert.AreEqual(typeof(ExampleService), attached.PropertyTargetType,
					"TargetType must be the declaring type, standing in for WPF's " +
					"DependencyProperty.OwnerType which WinUI does not expose.");
				Assert.AreEqual("attached value",
					ExampleService.GetExample((Microsoft.UI.Xaml.Controls.Grid)doc.RootInstance));
			});
		}
	}
}
