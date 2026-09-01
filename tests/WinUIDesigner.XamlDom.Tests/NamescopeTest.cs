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
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.XamlDom;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.XamlDom.Tests
{
	/// <remarks>
	/// <para>
	/// Adapted from the reference's <c>NamescopeTest.cs</c>. All three of its tests call
	/// <c>((FrameworkElement)obj.RootInstance).CreateVisualTree()</c> and
	/// <c>TryFindChild&lt;T&gt;(...)</c> - extension methods from
	/// <c>ICSharpCode.WpfDesign.UIExtensions</c> in the <b>Designer</b> project (live-visual-tree
	/// walking), which doesn't exist until Phase 3. Rewritten per the brief to walk the parsed
	/// <see cref="XamlObject"/> tree directly instead, via <see cref="FindDescendant"/> below -
	/// this still genuinely exercises name registration and <c>{x:Reference}</c> resolution
	/// (<see cref="NameScopeHelper"/> / <c>ReferenceExtensionWrapper</c>, both from Task 5), just
	/// at the object-model level rather than the rendered-UI level.
	/// </para>
	/// <para>
	/// The reference's NamescopeTest2/NamescopeTest3 additionally instantiate a
	/// <c>NamscopeTestUsercontrol</c> fixture - a UserControl whose own compiled XAML declares its
	/// own "aa"/"bb" Buttons, in what is, in WPF, a separate NameScope from the outer document's -
	/// specifically to demonstrate that <c>TryFindChild&lt;Button&gt;("bb")</c> (a naive by-name
	/// search over the whole live visual tree) can be fooled into finding <i>that</i> control's own
	/// nested "bb" Button (found first because the control is declared, and so visited, before the
	/// outer "bb"), while <c>{x:Reference bb}</c> - resolved against the correct NameScope - is
	/// not. That specific trap has no equivalent to port: <see cref="NameScopeHelper"/> is a single
	/// flat per-document table (Task 5, by design - WinUI has no NameScope/INameScope object model
	/// at all), and <see cref="XamlParser.Parse"/> never looks inside a constructed instance's own
	/// runtime content in the first place (there is no live visual tree standing in for
	/// <c>TryFindChild</c> to search), so there is no comparable "which same-named object did the
	/// naive search actually find" hazard here to reproduce. NamescopeTest2/NamescopeTest3 are
	/// therefore ported without that fixture, keeping the part of the original tests that
	/// <i>is</i> portable: multiple named elements and multiple <c>{x:Reference}</c> users in the
	/// same document, each independently resolving to its own distinct target rather than, say,
	/// always the first named object in the document.
	/// </para>
	/// <para>
	/// Also deliberately not cross-checked against <c>Microsoft.UI.Xaml.Markup.XamlReader.Load</c>
	/// (unlike most of this test suite, via <c>TestHelper.TestLoading</c>): <c>{x:Reference}</c> has
	/// no WinUI runtime type at all (no <c>Microsoft.UI.Xaml.Markup.ReferenceExtension</c> - verified
	/// absent, see <c>WinUIXamlServiceProvider.cs</c>'s <c>ReferenceExtensionWrapper</c> remarks), so
	/// WinUI's own XamlReader cannot represent it outside its compiler - the same situation
	/// <see cref="MarkupExtensionPlaceholderTests"/> already documents for
	/// <c>{StaticResource}</c>/<c>{ThemeResource}</c>/<c>{x:Null}</c>/<c>{x:Reference}</c> alike.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class NamescopeTest
	{
		const string P = XamlConstants.PresentationNamespace;
		const string X = XamlConstants.XamlNamespace;
		const string T = XamlTypeFinderTests.XamlDomTestsNamespace;

		// FindDescendant moved to TestHelper (shared with RoundTripTests, which had a
		// cosmetically-identical copy); this class doesn't inherit TestHelper, so calls below are
		// qualified.

		/// <summary>
		/// NamescopeTest 1
		/// </summary>
		[Test]
		public void NamescopeTest1()
		{
			var xaml = @"
<UserControl
    xmlns=""" + P + @"""
    xmlns:t=""" + T + @"""
    xmlns:x=""" + X + @"""
    x:Name=""root"">
    <Grid x:Name=""rootGrid"">
        <Button x:Name=""aa"" />
        <Button x:Name=""bb"" />
        <t:ExampleControl Property1=""{x:Reference aa}"" />
    </Grid>
</UserControl>";

			WinUITestHost.Run(() => {
				var doc = XamlParser.Parse(new StringReader(xaml));

				var example = TestHelper.FindDescendant(doc.RootElement, o => o.ElementType == typeof(ExampleControl));
				var buttonAa = TestHelper.FindDescendant(doc.RootElement, o => o.ElementType == typeof(Button) && o.Name == "aa");

				Assert.IsNotNull(example, "ExampleControl not found in the parsed tree");
				Assert.IsNotNull(buttonAa, "Button 'aa' not found in the parsed tree");
				Assert.AreSame(((ExampleControl)example.Instance).Property1, buttonAa.Instance,
					"{x:Reference aa} did not resolve to the 'aa' Button's live instance");
			});
		}

		/// <summary>
		/// NamescopeTest 2
		/// </summary>
		/// <remarks>
		/// Extends NamescopeTest1 with a second named Button and a second
		/// <c>{x:Reference}</c>-using <c>ExampleControl</c>, so the assertions actually check that
		/// each reference resolves to <i>its own</i> named target - not merely that some reference
		/// resolved to something non-null (which a bug that always bound to "the first named object
		/// in the document" would still pass).
		/// </remarks>
		[Test]
		public void NamescopeTest2()
		{
			var xaml = @"
<UserControl
    xmlns=""" + P + @"""
    xmlns:t=""" + T + @"""
    xmlns:x=""" + X + @"""
    x:Name=""root"">
    <Grid x:Name=""grid"">
        <Button Content=""level0"" x:Name=""aa"" />
        <Button Content=""level0"" x:Name=""bb"" />
        <t:ExampleControl Property1=""{x:Reference aa}"" />
        <t:ExampleControl x:Name=""exampleb"" Property1=""{x:Reference bb}"" />
    </Grid>
</UserControl>";

			WinUITestHost.Run(() => {
				var doc = XamlParser.Parse(new StringReader(xaml));

				var example = TestHelper.FindDescendant(doc.RootElement, o => o.ElementType == typeof(ExampleControl));
				var exampleb = TestHelper.FindDescendant(doc.RootElement, o => o.ElementType == typeof(ExampleControl) && o.Name == "exampleb");
				var buttonAa = TestHelper.FindDescendant(doc.RootElement, o => o.ElementType == typeof(Button) && o.Name == "aa");
				var buttonBb = TestHelper.FindDescendant(doc.RootElement, o => o.ElementType == typeof(Button) && o.Name == "bb");

				Assert.IsNotNull(example, "unnamed ExampleControl not found in the parsed tree");
				Assert.IsNotNull(exampleb, "'exampleb' ExampleControl not found in the parsed tree");
				Assert.IsNotNull(buttonAa, "Button 'aa' not found in the parsed tree");
				Assert.IsNotNull(buttonBb, "Button 'bb' not found in the parsed tree");
				Assert.AreNotSame(example, exampleb, "predicates matched the same ExampleControl twice");
				Assert.AreNotSame(buttonAa.Instance, buttonBb.Instance, "'aa' and 'bb' resolved to the same Button instance");

				Assert.AreSame(((ExampleControl)example.Instance).Property1, buttonAa.Instance,
					"the unnamed ExampleControl's {x:Reference aa} did not resolve to 'aa'");
				Assert.AreSame(((ExampleControl)exampleb.Instance).Property1, buttonBb.Instance,
					"'exampleb''s {x:Reference bb} did not resolve to 'bb'");
			});
		}

		/// <summary>
		/// NamescopeTest 3
		/// </summary>
		/// <remarks>
		/// Same as NamescopeTest2, except the document root is the <c>Grid</c> itself (matching the
		/// reference, which drops the wrapping <c>UserControl</c> here) - checks that
		/// <c>{x:Reference}</c> resolution doesn't depend on a <c>UserControl</c> root being present.
		/// </remarks>
		[Test]
		public void NamescopeTest3()
		{
			var xaml = @"
<Grid
    xmlns=""" + P + @"""
    xmlns:t=""" + T + @"""
    xmlns:x=""" + X + @"""
    x:Name=""root"">
        <Button Content=""level0"" x:Name=""aa"" />
        <Button Content=""level0"" x:Name=""bb"" />
        <t:ExampleControl Property1=""{x:Reference aa}"" />
        <t:ExampleControl x:Name=""exampleb"" Property1=""{x:Reference bb}"" />
</Grid>";

			WinUITestHost.Run(() => {
				var doc = XamlParser.Parse(new StringReader(xaml));

				var example = TestHelper.FindDescendant(doc.RootElement, o => o.ElementType == typeof(ExampleControl));
				var exampleb = TestHelper.FindDescendant(doc.RootElement, o => o.ElementType == typeof(ExampleControl) && o.Name == "exampleb");
				var buttonAa = TestHelper.FindDescendant(doc.RootElement, o => o.ElementType == typeof(Button) && o.Name == "aa");
				var buttonBb = TestHelper.FindDescendant(doc.RootElement, o => o.ElementType == typeof(Button) && o.Name == "bb");

				Assert.IsNotNull(example, "unnamed ExampleControl not found in the parsed tree");
				Assert.IsNotNull(exampleb, "'exampleb' ExampleControl not found in the parsed tree");
				Assert.IsNotNull(buttonAa, "Button 'aa' not found in the parsed tree");
				Assert.IsNotNull(buttonBb, "Button 'bb' not found in the parsed tree");
				Assert.AreNotSame(example, exampleb, "predicates matched the same ExampleControl twice");
				Assert.AreNotSame(buttonAa.Instance, buttonBb.Instance, "'aa' and 'bb' resolved to the same Button instance");

				Assert.AreSame(((ExampleControl)example.Instance).Property1, buttonAa.Instance,
					"the unnamed ExampleControl's {x:Reference aa} did not resolve to 'aa'");
				Assert.AreSame(((ExampleControl)exampleb.Instance).Property1, buttonBb.Instance,
					"'exampleb''s {x:Reference bb} did not resolve to 'bb'");
			});
		}
	}
}
