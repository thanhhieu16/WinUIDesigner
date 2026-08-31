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

namespace WinUIDesigner.XamlDom.Tests
{
	/// <summary>
	/// Custom markup extensions used by <see cref="MarkupExtensionTests"/> to exercise
	/// <c>MarkupExtensionParser</c>'s positional-argument handling (constructor selection, the
	/// tokenizer's quoted/unquoted string rules).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The reference types (<c>ICSharpCode.WpfDesigner.WpfDesign.Designer.Tests.XamlDom</c>) declare
	/// <c>public override object ProvideValue(IServiceProvider serviceProvider)</c> - WPF's
	/// <c>MarkupExtension.ProvideValue</c> is public abstract, taking <c>System.IServiceProvider</c>.
	/// Neither shape exists on WinUI: <c>Microsoft.UI.Xaml.Markup.MarkupExtension.ProvideValue</c> is
	/// declared <c>protected virtual</c> (both the parameterless overload and the one taking
	/// <c>Microsoft.UI.Xaml.IXamlServiceProvider</c> - see <c>XamlObject.InvokeProvideValue</c>'s
	/// remarks, Task 5 Step 9). These fixtures override the parameterless <c>protected</c> overload,
	/// which a subclass in a different assembly may still do (protected access, not public); verified
	/// end-to-end with a throwaway probe type before writing these: <c>XamlObject</c>'s
	/// reflection-based invoker successfully reaches this override via virtual dispatch.
	/// </para>
	/// <para>
	/// Verified separately (also with a throwaway probe, since it changes which test-helper these
	/// tests can use): WinUI's own <c>Microsoft.UI.Xaml.Markup.XamlReader.Load</c> - the oracle
	/// <see cref="TestHelper.TestLoading"/> cross-checks against - rejects *any* positional argument
	/// on a markup extension it does not itself special-case, regardless of whether
	/// <c>IXamlMetadataProvider</c> can resolve the type and regardless of whether a matching
	/// constructor exists on the CLR type:
	/// <c>XamlParseException: Parser internal error: Markup extension scanner
	/// 'UnknownPositionalParameter'</c>. Its scanner has a fixed, hardcoded grammar per
	/// known/whitelisted extension name (<c>Binding</c>, <c>StaticResource</c>, <c>RelativeSource</c>,
	/// <c>x:Null</c>, ...); arbitrary custom types are not part of that grammar for the positional
	/// form. So none of the tests using these two types can go through
	/// <see cref="TestHelper.TestLoading"/> - see <see cref="MarkupExtensionTests"/> for how they are
	/// verified instead (direct <c>XamlParser.Parse</c>, the same pattern
	/// <see cref="MarkupExtensionPlaceholderTests"/> already established for a different reason).
	/// </para>
	/// </remarks>
	public class StringExtension : Microsoft.UI.Xaml.Markup.MarkupExtension
	{
		readonly string s;

		public StringExtension(string s)
		{
			TestHelperLog.Log(this.GetType().Name + " " + s);
			this.s = s;
		}

		/// <summary>The exact string this extension was constructed with, for test assertions.</summary>
		public string Value => s;

		protected override object ProvideValue()
		{
			return s;
		}
	}

	/// <remarks>
	/// The reference's <c>MyExtension</c> exposes no way to observe the constructor arguments it
	/// received (its <c>ProvideValue</c> always returns null and the test only checks that parsing
	/// does not throw). Since these tests can't go through the WinUI-oracle cross-check at all (see
	/// <see cref="StringExtension"/>'s remarks) - the strongest available "did this actually work"
	/// check is entirely on our own parser's side - <c>P1</c>/<c>P2</c> are added here so
	/// <see cref="MarkupExtensionTests.Test5"/> can assert the positional arguments were bound to the
	/// right constructor parameters, not just that construction didn't throw.
	/// </remarks>
	public class MyExtension : Microsoft.UI.Xaml.Markup.MarkupExtension
	{
		public MyExtension(object p1, object p2)
		{
			P1 = p1;
			P2 = p2;
		}

		public object P1 { get; }
		public object P2 { get; }

		protected override object ProvideValue()
		{
			return null;
		}
	}
}
