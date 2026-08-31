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
using NUnit.Framework;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.XamlDom.Tests
{
	/// <remarks>
	/// The reference tests declare the default xmlns as
	/// <c>http://schemas.microsoft.com/netfx/2007/xaml/presentation</c>, an older URI WPF's parser
	/// tolerates through namespace-compatibility fallback. Per the brief, the ported tests use the
	/// value Task 2 confirmed against the real WinUI XAML compiler,
	/// <see cref="XamlConstants.PresentationNamespace"/>, instead of reproducing that
	/// inconsistency.
	/// </remarks>
	[TestFixture]
	public class WhitespaceTests : TestHelper
	{
		const string P = XamlConstants.PresentationNamespace;
		const string X = XamlConstants.XamlNamespace;
		const string T = XamlTypeFinderTests.XamlDomTestsNamespace;

		[Test]
		public void TrimSurroundingWhitespace()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">

   a test string

</t:ExampleClass>
			");
		}

		[Test]
		public void TrimConsecutiveWhitespace()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">
   a test               string
</t:ExampleClass>
			");
		}

		[Test]
		public void ConvertLineFeedToSpace()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">
   a test
   string
</t:ExampleClass>
			");
		}

		[Test]
		public void PreserveSurroundingWhitespace()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""" xml:space=""preserve"">

   a test string

</t:ExampleClass>
			");
		}

		[Test]
		public void PreserveConsecutiveWhitespace()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""" xml:space=""preserve"">
   a test               string
</t:ExampleClass>
			");
		}

		[Test]
		public void PreserveLineFeed()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""" xml:space=""preserve"">
   a test
   string
</t:ExampleClass>
			");
		}

		[Test]
		public void CDataTest()
		{
			TestLoading(@"
		          <t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""" xml:space=""preserve""> <![CDATA[
	This is text inside the CData section.
This is another line of text.
	This one is indented again.

And that was an empty line.
		]]>
 </t:ExampleClass>");
		}

		[Test]
		public void CDataAndContentMixTest()
		{
			TestLoading(@"
		          <t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">
	some text
	<![CDATA[ text in <CDATA> ]]>
	more text
 </t:ExampleClass>");
		}
	}
}
