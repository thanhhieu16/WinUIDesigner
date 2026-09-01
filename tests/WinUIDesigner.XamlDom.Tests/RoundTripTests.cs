using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using WinUIDesigner.XamlDom;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.XamlDom.Tests
{
	/// <summary>
	/// Phase 1's Definition-of-Done round-trip test (Task 9): parse a real, representative WinUI 3
	/// XAML file, re-serialize it through this codebase's own <see cref="XamlDocument.Save"/>, and
	/// re-parse the result, checking that the object model round-trips structurally.
	/// </summary>
	/// <remarks>
	/// <see cref="SampleWindow.xaml"/> is the hand-written stand-in described there (no
	/// <c>winui3</c> `dotnet new` template is installed on this machine - verified via
	/// <c>dotnet new list</c>). It exercises plain attributes, nested elements, an event property
	/// (<c>Click</c>) and a named resource (<c>Grid.Resources</c> &gt; <c>SolidColorBrush</c>)
	/// referenced via <c>{StaticResource}</c> - deliberately not byte-identical-after-round-trip
	/// (the brief only asks for structural equivalence), and deliberately not cross-checked against
	/// WinUI's own <c>XamlReader.Load</c> the way <see cref="TestHelper.TestLoading"/> does for the
	/// narrower per-feature tests: this test's whole point is to be this codebase's own parser
	/// talking to itself, twice, on a document nobody hand-picked to dodge a known gap.
	/// </remarks>
	[TestFixture]
	public class RoundTripTests
	{
		// FindDescendant moved to TestHelper (shared with NamescopeTest, which had a
		// cosmetically-identical copy); this class doesn't inherit TestHelper, so calls below are
		// qualified.

		static int CountElements(XamlObject o) =>
			1 + o.Properties.Sum(p =>
				(p.PropertyValue is XamlObject po ? CountElements(po) : 0) +
				p.CollectionElements.OfType<XamlObject>().Sum(CountElements));

		[Test]
		public void RoundTripsRealWinUIWindowXaml()
		{
			var xaml = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "SampleWindow.xaml"));

			WinUITestHost.Run(() => {
				var doc = XamlParser.Parse(new StringReader(xaml));
				Assert.IsNotNull(doc.RootInstance);
				Assert.IsInstanceOf<Grid>(doc.RootInstance, "Root element did not parse to a real Grid instance");

				// Sanity: the sample XAML actually has the non-trivial shape this test claims to
				// exercise, before round-tripping it - a nested Button two levels down, with its
				// Click event property and its StaticResource-bound Background both present.
				var button = TestHelper.FindDescendant(doc.RootElement, o => o.ElementType == typeof(Button));
				Assert.IsNotNull(button, "Sample XAML's Button was not found in the first parse");
				var clickProperty = button.Properties.SingleOrDefault(p => p.PropertyName == "Click");
				Assert.IsNotNull(clickProperty, "Button.Click was not parsed as a property at all");
				Assert.IsTrue(clickProperty.IsEvent, "Click was not resolved as an event property");
				Assert.AreEqual("GreetButton_Click", (clickProperty.PropertyValue as XamlTextValue)?.Text,
					"Click event handler name did not round-trip through the first parse");
				Assert.IsInstanceOf<SolidColorBrush>(((Button)button.Instance).Background,
					"{StaticResource AccentBrush} on Button.Background did not resolve against the ancestor Grid.Resources entry");

				var sb = new StringBuilder();
				using (var writer = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true }))
					doc.Save(writer);

				var reparsedDoc = XamlParser.Parse(new StringReader(sb.ToString()));
				Assert.IsNotNull(reparsedDoc.RootInstance);
				Assert.AreEqual(doc.RootInstance.GetType(), reparsedDoc.RootInstance.GetType());

				// Structural equivalence, not byte-identical text: same element count.
				Assert.AreEqual(CountElements(doc.RootElement), CountElements(reparsedDoc.RootElement));

				// A few targeted checks beyond the raw count, so a bug that swaps one element for
				// another same-sized subtree (which the count alone wouldn't catch) still fails.
				var reparsedButton = TestHelper.FindDescendant(reparsedDoc.RootElement, o => o.ElementType == typeof(Button));
				Assert.IsNotNull(reparsedButton, "Button did not survive the round-trip");
				var reparsedClick = reparsedButton.Properties.SingleOrDefault(p => p.PropertyName == "Click");
				Assert.IsNotNull(reparsedClick, "Button.Click did not survive the round-trip");
				Assert.AreEqual("GreetButton_Click", (reparsedClick.PropertyValue as XamlTextValue)?.Text,
					"Click event handler name did not survive the round-trip");
				Assert.IsInstanceOf<SolidColorBrush>(((Button)reparsedButton.Instance).Background,
					"{StaticResource AccentBrush} did not still resolve after the round-trip");

				var grid = (Grid)reparsedDoc.RootInstance;
				Assert.IsTrue(grid.Resources.ContainsKey("AccentBrush"),
					"Named resource 'AccentBrush' did not survive the round-trip");
			});
		}
	}
}
