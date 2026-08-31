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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using WinUIDesigner.XamlDom;
using NUnit.Framework;

namespace WinUIDesigner.XamlDom.Tests
{
	public class TestHelper
	{
		public static T[] ToArray<T>(IEnumerable<T> e)
		{
			return new List<T>(e).ToArray();
		}

		/// <summary>
		/// Loads <paramref name="xaml"/> with both WinUI's own XamlReader and this codebase's
		/// XamlParser and cross-checks the results.
		/// </summary>
		/// <remarks>
		/// Runs on the WinUI dispatcher thread (see <see cref="WinUITestHost"/>) because every
		/// object either loader produces is a thread-affine WinRT object.
		/// </remarks>
		public static void TestLoading(string xaml)
		{
			WinUITestHost.Run(() => TestLoadingCore(xaml));
		}

		static void TestLoadingCore(string xaml)
		{
			Debug.WriteLine("Load using WinUI's built-in XamlReader:");
			ExampleClass.nextUniqueIndex = 0;
			TestHelperLog.logBuilder = new StringBuilder();
			object officialResult = Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
			string officialLog = TestHelperLog.logBuilder.ToString();
			Assert.IsNotNull(officialResult, "officialResult is null");

			Debug.WriteLine("Load using own XamlParser:");
			ExampleClass.nextUniqueIndex = 0;
			TestHelperLog.logBuilder = new StringBuilder();
			XamlDocument doc = XamlParser.Parse(new StringReader(xaml));
			Assert.IsNotNull(doc, "doc is null");
			object ownResult = doc.RootInstance;
			string ownLog = TestHelperLog.logBuilder.ToString();
			Assert.IsNotNull(ownResult, "ownResult is null");
			TestHelperLog.logBuilder = null;

			// No XamlWriter.Save equivalent exists in WinUI (Gap 1), so we
			// can't text-diff a re-serialized "official" result. Compare
			// element type instead, and verify our own document is
			// self-consistent: re-serialize it and re-parse, and check the
			// reparsed root is the same type as the first parse.
			Assert.AreEqual(officialResult.GetType(), ownResult.GetType(),
				"Own parser produced a different root type than WinUI's XamlReader.Load");

			var sb = new StringBuilder();
			using (var writer = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true }))
				doc.Save(writer);
			var reparsed = XamlParser.Parse(new StringReader(sb.ToString()));
			Assert.IsNotNull(reparsed, "Re-parsing our own serialized output failed");
			Assert.AreEqual(ownResult.GetType(), reparsed.RootInstance.GetType(),
				"Round-tripped document did not reparse to the same root type");

			Debug.WriteLine("Official log:");
			Debug.WriteLine(officialLog);
			Debug.WriteLine("Own log:");
			Debug.WriteLine(ownLog);
			Assert.AreEqual(StripInitializationLines(officialLog), StripInitializationLines(ownLog));

			// The one difference stripped above is genuinely expected, so assert it positively
			// rather than only ignoring it: whenever the fixture implements ISupportInitialize, our
			// parser must have bracketed the load with BeginInit/EndInit.
			if (ownLog.Contains("ctorExampleClass")) {
				Assert.IsTrue(ownLog.Contains("BeginInit ") && ownLog.Contains("EndInit "),
					"Own parser did not call ISupportInitialize.BeginInit/EndInit:\n" + ownLog);
			}
		}

		/// <summary>
		/// Parses <c>&lt;t:ExampleClass ObjectProp="markupExtension" /&gt;</c> directly (bypassing
		/// the WinUI-oracle cross-check - see <see cref="MarkupExtensionTests"/>'s remarks, point 3)
		/// and returns the resulting property value.
		/// </summary>
		/// <remarks>
		/// Shared between <see cref="MarkupExtensionTests"/> and
		/// <see cref="MarkupExtensionPlaceholderTests"/>, which each used to keep their own,
		/// cosmetically-identical copy of this method.
		/// </remarks>
		public static XamlObject ParseObjectPropValue(string markupExtension)
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

		/// <summary>
		/// Depth-first search of a parsed <see cref="XamlObject"/> tree (the root itself, then
		/// each property's <see cref="XamlProperty.PropertyValue"/> and
		/// <see cref="XamlProperty.CollectionElements"/>, in document order) for the first node
		/// matching <paramref name="predicate"/>.
		/// </summary>
		/// <remarks>
		/// Shared between <see cref="NamescopeTest"/> and <see cref="RoundTripTests"/>, which each
		/// used to keep their own, cosmetically-identical copy of this method.
		/// </remarks>
		public static XamlObject FindDescendant(XamlObject root, Func<XamlObject, bool> predicate)
		{
			if (predicate(root))
				return root;
			foreach (var property in root.Properties) {
				if (property.PropertyValue is XamlObject child) {
					var found = FindDescendant(child, predicate);
					if (found != null) return found;
				}
				foreach (var element in property.CollectionElements) {
					if (element is XamlObject childElement) {
						var found = FindDescendant(childElement, predicate);
						if (found != null) return found;
					}
				}
			}
			return null;
		}

		/// <summary>
		/// Removes <c>BeginInit</c>/<c>EndInit</c> lines before comparing the two loaders' logs.
		/// </summary>
		/// <remarks>
		/// <c>System.ComponentModel.ISupportInitialize</c> is a WPF/WinForms XAML concept. WPF's
		/// <c>XamlReader</c> honours it, which is why the reference test can compare the two logs
		/// verbatim; WinUI's XAML reader does not call it at all (verified: the official log for
		/// every <c>ExampleClass</c> fixture contains no BeginInit/EndInit line, while ours does).
		/// This codebase's parser deliberately keeps the reference's ISupportInitialize handling -
		/// the brief calls for it explicitly - so the two logs must differ by exactly these lines
		/// and by nothing else. Stripping them keeps the rest of the comparison strict instead of
		/// abandoning the log oracle altogether.
		/// </remarks>
		static string StripInitializationLines(string log)
		{
			if (string.IsNullOrEmpty(log))
				return log;
			var kept = log
				.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
				.Where(line => !line.StartsWith("BeginInit ", StringComparison.Ordinal)
				            && !line.StartsWith("EndInit ", StringComparison.Ordinal));
			return string.Join(Environment.NewLine, kept);
		}
	}

	internal static class TestHelperLog
	{
		internal static StringBuilder logBuilder;

		internal static void Log(string text)
		{
			if (logBuilder != null) {
				logBuilder.AppendLine(text);
				Debug.WriteLine(text);
			}
		}
	}
}
