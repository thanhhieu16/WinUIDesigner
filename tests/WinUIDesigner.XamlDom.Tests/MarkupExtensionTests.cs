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
using System.IO;
using System.Linq;
using NUnit.Framework;
using WinUIDesigner.XamlDom;
using Microsoft.UI.Xaml.Data;

namespace WinUIDesigner.XamlDom.Tests
{
	/// <remarks>
	/// <para>
	/// Substitutions against the reference file, all verified empirically (see the per-test remarks
	/// for the specific evidence) rather than assumed from the plan:
	/// </para>
	/// <para>
	/// 1. Root element/namespace: same substitution <see cref="SimpleLoadTests"/> already established
	/// - <c>Window</c> -&gt; <c>t:ExampleClass</c> (WinUI's <c>Window</c> isn't a loadable XAML root
	/// and has none of the properties the reference attaches markup extensions to), default xmlns -&gt;
	/// <see cref="XamlConstants.PresentationNamespace"/>. The reference attaches its markup extensions
	/// to whatever property happens to fit the value's type (<c>Title</c>, <c>Content</c>,
	/// <c>Background</c>); since none of those exist on <c>ExampleClass</c>, every test here targets
	/// <c>ObjectProp</c> (type <c>object</c>) instead, which accepts any of the values under test
	/// without a type-conversion side quest of its own.
	/// </para>
	/// <para>
	/// 2. <c>{Binding Some}</c>/<c>{RelativeSource Self}</c> positional shorthand: WinUI's
	/// <c>Microsoft.UI.Xaml.Data.Binding</c> and <c>...RelativeSource</c> each have <b>only a
	/// parameterless constructor</b> (verified by reflection over Microsoft.WinUI.dll - neither
	/// declares any other public constructor). <c>MarkupExtensionParser.Parse</c> requires a
	/// constructor whose parameter count matches the positional argument count, so it throws
	/// <c>"No constructor ... found that takes 1 arguments"</c> for either positional form - even
	/// though WinUI's own <c>XamlReader.Load</c> happens to accept both (verified with a throwaway
	/// probe test, since removed): its native scanner special-cases a small, fixed list of
	/// well-known extension names with a hardcoded positional grammar, which our reflection-based
	/// parser has no way to replicate for an arbitrary type. Tests that used the positional shorthand
	/// now use the equivalent named-argument form (<c>Path=Some</c>, <c>Mode=Self</c>), which both
	/// loaders accept identically (also verified with the same probe).
	/// </para>
	/// <para>
	/// 3. Custom markup extensions (<c>{t:MyExtension ...}</c>, <c>{t:String ...}</c>): the same
	/// probe found WinUI's <c>XamlReader.Load</c> rejects <i>any</i> positional argument on a markup
	/// extension it does not itself know about, independently of whether
	/// <c>IXamlMetadataProvider</c> can resolve the type or whether a matching constructor exists on
	/// it: <c>XamlParseException: Parser internal error: Markup extension scanner
	/// 'UnknownPositionalParameter'</c>. So the oracle cannot represent these forms at all - the same
	/// situation <see cref="MarkupExtensionPlaceholderTests"/> already documented for
	/// <c>{StaticResource}</c>/<c>{ThemeResource}</c>/<c>{x:Reference}</c>, for a different underlying
	/// reason. Tests using <see cref="StringExtension"/>/<see cref="MyExtension"/> therefore parse
	/// directly with <see cref="XamlParser.Parse"/> (bypassing <see cref="TestHelper.TestLoading"/>'s
	/// oracle cross-check) and assert on the parsed result instead, the same pattern
	/// <see cref="MarkupExtensionPlaceholderTests"/> uses.
	/// </para>
	/// <para>
	/// 4. <c>{x:Static ...}</c> (Test6, Test7, Test9): verified unsupported by <i>both</i> loaders,
	/// not just adapted around - <c>Microsoft.UI.Xaml.Markup.StaticExtension</c> does not exist
	/// (confirmed absent from Microsoft.WinUI.dll, consistent with Task 5's finding that the whole
	/// assembly declares no <c>MarkupExtension</c> subclasses at all), so
	/// <c>MarkupExtensionParser.ResolveExtensionType</c> - whose Gap 7 placeholder fallback only
	/// covers <c>StaticResource</c>/<c>ThemeResource</c>/<c>Null</c>/<c>Reference</c> - throws
	/// <c>"Unknown markup extension x:StaticExtension"</c>, and separately WinUI's own
	/// <c>XamlReader.Load</c> throws <c>"The type 'Static' was not found"</c> for the identical input
	/// (verified with the same probe). This is exactly the situation
	/// <see cref="SimpleLoadTests.ExampleClassObjectPropWithTypeValue"/> already documents for
	/// <c>{x:Type ...}</c> - not "needs adaptation" but "genuinely nothing to test" - so these three
	/// are <c>[Ignore]</c>d the same way, rather than invented a fake substitute for a WPF-only
	/// feature. Test7 additionally uses <c>{DynamicResource ...}</c>, independently confirmed absent
	/// from WinUI by Task 5 (finding #3).
	/// </para>
	/// <para>
	/// 5. <c>TestMultiBinding</c>/<c>TestPriorityBinding</c>: <c>Microsoft.UI.Xaml.Data.MultiBinding</c>
	/// and <c>...PriorityBinding</c> are both confirmed absent from WinUI entirely (Task 5's
	/// <see cref="MarkupExtensionPrinter.CanPrint"/> comment - verified by reflection over
	/// Microsoft.WinUI.dll), not merely unsupported by this codebase's parser. There is nothing to
	/// port a WinUI equivalent of; both tests, and their supporting <c>MyMultiConverter</c>/
	/// <c>MyBindingExtension</c> reference fixtures, are dropped rather than <c>[Ignore]</c>d, since
	/// unlike <c>x:Type</c>/<c>x:Static</c> there is no possible future WinUI runtime type that could
	/// ever make them relevant again.
	/// </para>
	/// <para>
	/// 6. <c>Binding.Path</c> cannot be set from a XAML attribute string at all in this codebase -
	/// found while writing <see cref="Test3"/>'s value-level assertion, not anticipated by the
	/// brief. <c>Binding.Path</c> is a <c>Microsoft.UI.Xaml.PropertyPath</c>, which has no
	/// <c>TypeConverter</c> capable of <c>ConvertFromString</c> (WPF's has
	/// <c>PropertyPathConverter</c>; WinUI's does not carry an equivalent
	/// <c>[TypeConverter]</c>). <c>XamlNormalPropertyInfo.TypeConverter</c> falls back to
	/// <c>PropertyDescriptor.Converter</c>, i.e. <c>TypeDescriptor</c>'s default converter for a
	/// reference type with none registered, whose <c>ConvertFromString</c> throws
	/// <c>NotSupportedException</c> - and <c>XamlProperty.UpdateValueOnInstance</c> silently
	/// swallows that (pre-existing <c>catch (Exception) { Debug.WriteLine(...) }</c>, not
	/// something this task added), so <c>Path=Some</c> parses without any visible error and
	/// leaves <c>binding.Path</c> null. Verified directly: <c>{Binding Path=Some}</c> parses
	/// clean on both loaders (so <see cref="TestHelper.TestLoading"/>-based tests are
	/// unaffected), but a direct-parse read of the resulting instance shows <c>Path</c> unset.
	/// This is a real, pre-existing gap in the ported property-conversion pipeline, not a
	/// markup-extension-parsing issue, so fixing it is out of scope for a test-porting task -
	/// flagged in the task report instead. The value-level assertions on <see cref="Test3"/>/
	/// <see cref="Test8"/> check <c>ElementName</c>/<c>Mode</c>/<c>RelativeSource.Mode</c>
	/// instead, which do convert correctly (plain string passthrough and <c>EnumConverter</c>
	/// respectively - both verified).
	/// </para>
	/// <para>
	/// The reference file's commented-out <c>Test10</c> (lines 198-211 of the WPF source) was never
	/// an active test there either; it is not ported.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class MarkupExtensionTests : TestHelper
	{
		const string P = XamlConstants.PresentationNamespace;
		const string X = XamlConstants.XamlNamespace;
		const string T = XamlTypeFinderTests.XamlDomTestsNamespace;

		// Ported verbatim from the reference, including the doubled backslashes - see
		// TestQuotedPathWithCommasAndSpaces's remarks for why that matters.
		const string PathWithSpaces = @"C:\\Folder A\\SubFolder A\\SubFolder B\\file with spaces.txt";
		const string PathWithoutSpaces = @"C:\\FolderA\\SubFolderA\\SubFolderB\\file.txt";
		const string PathWithCommasAndSpaces = @"C:\\Folder A\\Sub,Folder,A\\SubFolderB\\file,with,commas and spaces.txt";

		static void TestMarkupExtension(string attributeXaml)
		{
			TestLoading(@"<t:ExampleClass
	xmlns=""" + P + @"""
	xmlns:t=""" + T + @"""
	xmlns:x=""" + X + @"""
	" + attributeXaml + @"/>");
		}

		/// <summary>
		/// Parses <c>&lt;t:ExampleClass ObjectProp="markupExtension" /&gt;</c> directly (bypassing
		/// the WinUI-oracle cross-check - see this class's remarks, point 3) and returns the
		/// resulting property value.
		/// </summary>
		static XamlObject ParseObjectPropValue(string markupExtension)
		{
			var xaml =
				"<t:ExampleClass xmlns=\"" + P + "\"" +
				" xmlns:t=\"" + T + "\"" +
				" xmlns:x=\"" + X + "\"" +
				" ObjectProp=\"" + markupExtension + "\" />";
			var doc = XamlParser.Parse(new StringReader(xaml));
			var property = doc.RootElement.Properties.Single(p => p.PropertyName == "ObjectProp");
			return (XamlObject)property.PropertyValue;
		}

		[Test]
		public void Test1()
		{
			TestMarkupExtension("ObjectProp=\"{Binding}\"");
		}

		[Test]
		public void Test2()
		{
			// Reference: "{Binding Some}" (positional Path shorthand) - see this class's remarks,
			// point 2, for why the named-argument form is used instead.
			TestMarkupExtension("ObjectProp=\"{Binding Path=Some}\"");
		}

		/// <remarks>
		/// Goes further than the reference, which only checks that parsing doesn't throw: also
		/// parses directly and asserts on the resulting <see cref="Binding"/>'s own properties. This
		/// is deliberate, not scope creep - per the task brief, a test that touches <c>{Binding}</c>
		/// needs to be checked against Task 5 Step 9's finding that <c>Binding</c> does not derive
		/// from <c>MarkupExtension</c> on WinUI at all. One consequence of that fact which the brief
		/// didn't anticipate: <see cref="XamlObject.IsMarkupExtension"/> is <c>instance is
		/// MarkupExtension</c>, so it is <i>false</i> for a parsed <c>Binding</c>, which means
		/// <c>XamlObject.GetValueFor</c> never calls <c>ProvideValue()</c>/<c>BindingWrapper</c>
		/// for it at all during ordinary parsing - it takes the "not a markup extension" branch and
		/// returns the constructed <c>Binding</c> instance as-is. So the "resolved value" of a parsed
		/// <c>{Binding ...}</c> attribute is simply the live <c>Binding</c> object with its properties
		/// set from the named arguments - which is exactly what this test asserts.
		/// </remarks>
		[Test]
		public void Test3()
		{
			// Reference: "{ Binding  Some , ElementName = Some , Mode = TwoWay }" (positional Path
			// shorthand) - see this class's remarks, point 2.
			TestMarkupExtension("ObjectProp=\"{ Binding  Path = Some , ElementName = Some , Mode = TwoWay }\"");

			// Direct-parse value-level check (see this test's remarks above). Every Microsoft.UI.Xaml
			// object is thread-affine, so this - like every other direct XamlParser.Parse call in
			// this file - must run on the WinUI dispatcher thread (see WinUITestHost's remarks).
			WinUITestHost.Run(() => {
				var value = ParseObjectPropValue("{ Binding  Path = Some , ElementName = Some , Mode = TwoWay }");
				Assert.IsFalse(value.IsMarkupExtension, "Binding is not a MarkupExtension on WinUI (Task 5 Step 9)");
				var binding = (Binding)value.Instance;
				// Not checking binding.Path.Path here - see this class's remarks, point 6:
				// PropertyPath can't be set from a XAML attribute string in this codebase at all.
				//
				// "Some " (trailing space), not "Some": faithfully ported tokenizer behaviour, not a
				// bug here. MarkupExtensionTokenizer.MembernameOrString only trims whitespace from a
				// Membername token (the property name before "="); an unquoted *value* keeps
				// whatever's between the "=" and the next unescaped delimiter verbatim, including a
				// trailing space before a comma - confirmed identical in the WPF original
				// (WpfDesign.XamlDom/Project/MarkupExtensionParser.cs's MembernameOrString has the
				// same unconditional `default: b.Append(...)` in its unquoted branch). The reference
				// test never noticed this because it only checked for a successful parse, never a
				// resolved value.
				Assert.AreEqual("Some ", binding.ElementName);
				Assert.AreEqual(BindingMode.TwoWay, binding.Mode);
			});
		}

		// Reference Test4: Content="{x:Type Button}". Already covered - and already [Ignore]d for
		// the same reason - by SimpleLoadTests.ExampleClassObjectPropWithTypeValue. Not duplicated
		// here; see this class's remarks, point 4.

		/// <remarks>
		/// Reference: "{t:MyExtension 1, 2}", checked only for not throwing. Adapted per this class's
		/// remarks point 3: parsed directly instead of through <see cref="TestLoading"/>, and
		/// <see cref="MyExtension"/> exposes its constructor arguments (a deviation from the
		/// reference type, documented on <see cref="MyExtension"/> itself) so the assertion checks
		/// something real - that positional arguments were bound to the right constructor parameters
		/// - rather than just "didn't throw".
		/// </remarks>
		[Test]
		public void Test5()
		{
			WinUITestHost.Run(() => {
				var value = ParseObjectPropValue("{t:MyExtension 1, 2}");

				Assert.IsInstanceOf<MyExtension>(value.Instance);
				var ext = (MyExtension)value.Instance;
				Assert.AreEqual("1", ext.P1);
				Assert.AreEqual("2", ext.P2);
			});
		}

		// Reference Test6: Background="{x:Static SystemColors.ControlBrush}". Unsupported by both
		// loaders - see this class's remarks, point 4.
		[Test]
		[Ignore("x:Static has no WinUI runtime type (no StaticExtension in Microsoft.WinUI.dll) and " +
			"WinUI's own XamlReader.Load rejects it too (\"The type 'Static' was not found\"); out of Phase 1 scope.")]
		public void Test6()
		{
			TestMarkupExtension("Background=\"{x:Static SystemColors.ControlBrush}\"");
		}

		// Reference Test7: Background="{DynamicResource {x:Static SystemColors.ControlBrushKey}}".
		// Doubly unsupported - DynamicResource doesn't exist on WinUI at all (Task 5 finding #3) and
		// x:Static doesn't either (this class's remarks, point 4).
		[Test]
		[Ignore("Neither {DynamicResource} (Task 5 finding #3) nor {x:Static} (see Test6) has a WinUI runtime type; out of Phase 1 scope.")]
		public void Test7()
		{
			TestMarkupExtension("Background=\"{DynamicResource {x:Static SystemColors.ControlBrushKey}}\"");
		}

		/// <remarks>
		/// Reference: "{Binding Some, RelativeSource={RelativeSource Self}}" - adapted per this
		/// class's remarks point 2 (named-argument form for both the outer Binding and the nested
		/// RelativeSource). Also goes further than the reference (which only checks for no exception)
		/// with a direct-parse assertion on the resulting <see cref="Binding.RelativeSource"/>, for
		/// the same reason given on <see cref="Test3"/>.
		/// </remarks>
		[Test]
		public void Test8()
		{
			TestMarkupExtension("ObjectProp=\"{Binding Path=Some, RelativeSource={RelativeSource Mode=Self}}\"");

			WinUITestHost.Run(() => {
				var value = ParseObjectPropValue("{Binding Path=Some, RelativeSource={RelativeSource Mode=Self}}");
				var binding = (Binding)value.Instance;
				// Not checking binding.Path.Path here - see this class's remarks, point 6.
				Assert.IsNotNull(binding.RelativeSource);
				Assert.AreEqual(RelativeSourceMode.Self, binding.RelativeSource.Mode);
			});
		}

		// Reference Test9: Content="{x:Static t:MyStaticClass.StaticString}". Unsupported by both
		// loaders - see this class's remarks, point 4.
		[Test]
		[Ignore("x:Static has no WinUI runtime type (no StaticExtension in Microsoft.WinUI.dll) and " +
			"WinUI's own XamlReader.Load rejects it too (\"The type 'Static' was not found\"); out of Phase 1 scope.")]
		public void Test9()
		{
			TestMarkupExtension("Content=\"{x:Static t:MyStaticClass.StaticString}\"");
		}

		[Test]
		public void TestPathWithSpaces()
		{
			WinUITestHost.Run(() => {
				var value = ParseObjectPropValue("{t:String " + PathWithSpaces + "}");
				Assert.AreEqual(@"C:\Folder A\SubFolder A\SubFolder B\file with spaces.txt", ((StringExtension)value.Instance).Value);
			});
		}

		[Test]
		public void TestQuotedPathWithSpaces()
		{
			WinUITestHost.Run(() => {
				var value = ParseObjectPropValue("{t:String '" + PathWithSpaces + "'}");
				Assert.AreEqual(@"C:\Folder A\SubFolder A\SubFolder B\file with spaces.txt", ((StringExtension)value.Instance).Value);
			});
		}

		[Test]
		public void TestPathWithoutSpaces()
		{
			WinUITestHost.Run(() => {
				var value = ParseObjectPropValue("{t:String " + PathWithoutSpaces + "}");
				Assert.AreEqual(@"C:\FolderA\SubFolderA\SubFolderB\file.txt", ((StringExtension)value.Instance).Value);
			});
		}

		[Test]
		public void TestQuotedPathWithoutSpaces()
		{
			WinUITestHost.Run(() => {
				var value = ParseObjectPropValue("{t:String '" + PathWithoutSpaces + "'}");
				Assert.AreEqual(@"C:\FolderA\SubFolderA\SubFolderB\file.txt", ((StringExtension)value.Instance).Value);
			});
		}

		/// <remarks>
		/// The reference constant is declared with doubled backslashes on purpose
		/// (<c>@"C:\\Folder A\\..."</c> - a C# verbatim string, so this is literally two backslash
		/// characters per separator, not an escape). <c>MarkupExtensionTokenizer</c>'s quoted-
		/// string branch collapses each <c>\\</c> pair in the markup-extension source text to a
		/// single <c>\</c>, so the doubled form is what's needed to end up with an ordinary
		/// single-backslash Windows path once parsed - verified with a throwaway probe before relying
		/// on it here (an earlier attempt using a normal single-backslash path lost every backslash,
		/// confirming the tokenizer treats a lone <c>\</c> as "escape the next character literally").
		/// This is also why this particular case - the one with commas - can only be tested in quoted
		/// form: unquoted positional arguments stop at the first unescaped comma, and this path has
		/// three.
		/// </remarks>
		[Test]
		public void TestQuotedPathWithCommasAndSpaces()
		{
			WinUITestHost.Run(() => {
				var value = ParseObjectPropValue("{t:String '" + PathWithCommasAndSpaces + "'}");
				Assert.AreEqual(@"C:\Folder A\Sub,Folder,A\SubFolderB\file,with,commas and spaces.txt", ((StringExtension)value.Instance).Value);
			});
		}

		// Reference TestMultiBinding/TestPriorityBinding: dropped, not [Ignore]d - see this class's
		// remarks, point 5.
	}
}
