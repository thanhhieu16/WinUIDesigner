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
using System.Linq;
using Microsoft.UI.Xaml;
using NUnit.Framework;
using WinUIDesigner.XamlDom;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.XamlDom.Tests
{
	/// <summary>
	/// Whole-branch review Important #3: <c>mc:Ignorable</c>/design-time (<c>d:</c>) namespace
	/// handling (<see cref="DesignTimeProperties"/>, <see cref="MarkupCompatibilityProperties"/>,
	/// and the dispatch in <see cref="XamlParser.GetXamlSpecialProperty"/>) had zero test coverage
	/// despite being reachable for virtually any real, VS/Blend-authored XAML: every VS/Blend XAML
	/// file carries <c>xmlns:d</c>/<c>xmlns:mc</c>/<c>mc:Ignorable="d"</c>, and most also carry
	/// <c>d:DesignWidth</c>/<c>d:DesignHeight</c>. The latter pair additionally exercise a write
	/// path - <see cref="XamlProperty.UpdateValueOnInstance"/>'s <c>DesignWidth</c>/
	/// <c>DesignHeight</c> -&gt; <c>FrameworkElement.Width</c>/<c>Height</c> special case, read
	/// directly (XamlProperty.cs) before writing the assertions below - that actually mutates the
	/// live parsed instance and had nothing exercising it.
	/// </summary>
	[TestFixture]
	public class DesignTimePropertiesTests
	{
		const string P = XamlConstants.PresentationNamespace;
		const string X = XamlConstants.XamlNamespace;
		const string D = XamlConstants.DesignTimeNamespace;
		const string MC = XamlConstants.MarkupCompatibilityNamespace;

		[Test]
		public void McIgnorableAndDesignTimeSizeAreHonoured()
		{
			var xaml = @"
<Grid
    xmlns=""" + P + @"""
    xmlns:x=""" + X + @"""
    xmlns:d=""" + D + @"""
    xmlns:mc=""" + MC + @"""
    mc:Ignorable=""d""
    d:DesignWidth=""400""
    d:DesignHeight=""300"">
</Grid>";

			WinUITestHost.Run(() => {
				var doc = XamlParser.Parse(new StringReader(xaml));
				Assert.IsNotNull(doc.RootInstance,
					"Document carrying xmlns:d/xmlns:mc/mc:Ignorable/d:DesignWidth/d:DesignHeight failed to parse");

				var root = doc.RootElement;

				// mc:Ignorable must resolve as MarkupCompatibilityProperties' attached "Ignorable"
				// property (GetXamlSpecialProperty in XamlParser.cs), not be silently dropped.
				var ignorable = root.Properties.SingleOrDefault(p => p.PropertyName == "Ignorable" && p.IsAttached);
				Assert.IsNotNull(ignorable, "mc:Ignorable was not resolved as an attached property at all");
				Assert.IsTrue(ignorable.IsSet, "mc:Ignorable was resolved but not recorded as set");
				Assert.AreEqual("d", MarkupCompatibilityProperties.GetIgnorable((DependencyObject)doc.RootInstance),
					"mc:Ignorable=\"d\" did not end up on the live instance via MarkupCompatibilityProperties.IgnorableProperty");

				// d:DesignWidth/d:DesignHeight must actually mutate FrameworkElement.Width/Height on
				// the live instance (XamlProperty.UpdateValueOnInstance's special case), not just
				// parse as inert DesignTimeProperties values.
				var grid = (FrameworkElement)doc.RootInstance;
				Assert.AreEqual(400d, grid.Width, "d:DesignWidth did not mutate FrameworkElement.Width");
				Assert.AreEqual(300d, grid.Height, "d:DesignHeight did not mutate FrameworkElement.Height");
			});
		}
	}
}
