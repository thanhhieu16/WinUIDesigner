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

using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using WinUIDesigner.XamlDom;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.XamlDom.Tests
{
	/// <summary>
	/// Exercises <see cref="WinUIDesigner.XamlDom.TemplateHelper"/> - the only place a
	/// <c>FrameworkTemplate</c> (e.g. <c>ControlTemplate</c>) is instantiated at runtime, via
	/// <c>Microsoft.UI.Xaml.Markup.XamlReader.Load</c> (see Task 8 brief / TemplateHelper.cs remarks).
	/// </summary>
	[TestFixture]
	public class TemplateHelperTests
	{
		const string P = XamlConstants.PresentationNamespace;
		const string X = XamlConstants.XamlNamespace;

		/// <summary>
		/// Step 1 (task-8-brief.md): base-case smoke test, added because Task 5's report flagged
		/// (Concern 5) that <c>TemplateHelper.cs</c> compiled but no Task 5 test ever exercised a
		/// <c>FrameworkTemplate</c> at runtime. A template with no cross-scope resource reference -
		/// entirely self-contained - must parse to a real <c>ControlTemplate</c> through the base
		/// (pre-splicing) logic.
		/// </summary>
		[Test]
		public void SelfContainedControlTemplateParses()
		{
			var xaml = @"
<Grid
    xmlns=""" + P + @"""
    xmlns:x=""" + X + @""">
    <Grid.Resources>
        <ControlTemplate x:Key=""MyTemplate"" TargetType=""Button"">
            <Border Background=""Red"" />
        </ControlTemplate>
    </Grid.Resources>
</Grid>";

			WinUITestHost.Run(() => {
				var doc = XamlParser.Parse(new StringReader(xaml));
				Assert.IsNotNull(doc.RootInstance);

				var grid = (Grid)doc.RootInstance;
				Assert.IsTrue(grid.Resources.ContainsKey("MyTemplate"),
					"Grid.Resources did not contain the 'MyTemplate' key");
				var template = grid.Resources["MyTemplate"] as ControlTemplate;
				Assert.IsNotNull(template,
					"TemplateHelper.GetFrameworkTemplate did not produce a real ControlTemplate for a self-contained template");
			});
		}

		/// <summary>
		/// Step 2 (task-8-brief.md): a <c>{StaticResource}</c> inside a <c>ControlTemplate</c> must
		/// resolve a resource declared in a different, ancestor <c>Grid.Resources</c> entry - not
		/// just resources inside the template's own fragment. Both resources are combined into a
		/// single <c>Grid.Resources</c>/<c>ResourceDictionary</c> block, as required (two sibling
		/// <c>Grid.Resources</c> property elements on the same object is invalid XAML).
		/// </summary>
		[Test]
		public void ControlTemplateWithResourceReference()
		{
			var xaml = @"
<Grid
    xmlns=""" + P + @"""
    xmlns:x=""" + X + @""">
    <Grid.Resources>
        <ResourceDictionary>
            <SolidColorBrush x:Key=""MyBrush"" Color=""Red"" />
            <ControlTemplate x:Key=""MyTemplate"" TargetType=""Button"">
                <Border Background=""{StaticResource MyBrush}"" />
            </ControlTemplate>
        </ResourceDictionary>
    </Grid.Resources>
</Grid>";

			WinUITestHost.Run(() => {
				var doc = XamlParser.Parse(new StringReader(xaml));
				Assert.IsNotNull(doc.RootInstance);

				var grid = (Grid)doc.RootInstance;
				Assert.IsTrue(grid.Resources.ContainsKey("MyTemplate"),
					"Grid.Resources did not contain the 'MyTemplate' key");
				var template = grid.Resources["MyTemplate"] as ControlTemplate;
				Assert.IsNotNull(template,
					"TemplateHelper.GetFrameworkTemplate did not produce a real ControlTemplate when the template references an ancestor-scope resource");

				// Apply the template to a real Button and walk the resulting visual tree to confirm
				// {StaticResource MyBrush} actually resolved to the ancestor-declared brush, not
				// merely that parsing didn't throw: an unresolved StaticResource that WinUI silently
				// drops would still leave `template` non-null with Border.Background unset.
				// (FrameworkTemplate has no public LoadContent() in this WinUI projection - verified
				// by reflection, unlike WPF - so Control.ApplyTemplate()/VisualTreeHelper is the only
				// available way to materialize the template's content.)
				var button = new Button { Template = template };
				bool applied = button.ApplyTemplate();
				Assert.IsTrue(applied, "Control.ApplyTemplate() reported the template did not apply");
				Assert.AreEqual(1, VisualTreeHelper.GetChildrenCount(button),
					"Applied template did not produce the expected single Border child");
				var border = (Border)VisualTreeHelper.GetChild(button, 0);
				var brush = border.Background as SolidColorBrush;
				Assert.IsNotNull(brush,
					"Border.Background did not resolve to a SolidColorBrush - {StaticResource MyBrush} did not splice in the ancestor Grid.Resources entry");
				Assert.AreEqual(Microsoft.UI.Colors.Red, brush.Color);
			});
		}

		/// <summary>
		/// Task 5's review flagged (Minor #11) that the WPF reference stamps a temporary x:Key onto
		/// a cloned template element with none in the source (see reference TemplateHelper.cs,
		/// ~lines 76-79), to handle the property-element-syntax case where no x:Key is needed or
		/// present in the original document - and that the ported base version omitted this
		/// entirely. Confirmed empirically while implementing Task 8 (splicing always wraps the
		/// fragment in a synthetic ResourceDictionary, and WinUI's loader rejects a keyless
		/// dictionary item with "Items added to a dictionary must have a key") and fixed in the same
		/// change rather than deferred; this test is the regression guard for that fix.
		/// </summary>
		[Test]
		public void PropertyElementSyntaxTemplateWithNoExplicitKeyParses()
		{
			var xaml = @"
<Button
    xmlns=""" + P + @"""
    xmlns:x=""" + X + @""">
    <Button.Template>
        <ControlTemplate TargetType=""Button"">
            <Border Background=""Red"" />
        </ControlTemplate>
    </Button.Template>
</Button>";

			WinUITestHost.Run(() => {
				var doc = XamlParser.Parse(new StringReader(xaml));
				Assert.IsNotNull(doc.RootInstance);
				var button = (Button)doc.RootInstance;
				Assert.IsNotNull(button.Template,
					"Button.Template was not set - property-element-syntax ControlTemplate with no explicit x:Key failed to parse");

				button.ApplyTemplate();
				Assert.AreEqual(1, VisualTreeHelper.GetChildrenCount(button),
					"Applied template did not produce the expected single Border child");
				Assert.IsInstanceOf<Border>(VisualTreeHelper.GetChild(button, 0));
			});
		}
	}
}
