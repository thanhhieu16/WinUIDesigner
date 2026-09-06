# XamlDom Value Conversion, Phase A (read side) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `WinUIDesigner.XamlDom` convert XAML attribute text to WinRT-typed values — `Margin`, `Background`, `FontFamily`, `GridLength`, `Color`, `Point`, `PointCollection`, `Path.Data` and the rest — instead of silently leaving every one of them at its default.

**Architecture:** A single new `TypeConverter` delegates the conversion to WinUI's own `XamlReader`: it builds a one-element fragment `<Host P='{text}'/>`, loads it, and reads `P` back. Hosts come from a hand-listed 16-entry table keyed on the exact property type. The converter is registered behind the three existing entries in `XamlPropertyInfo.GetCustomTypeConverter`, which `XamlNormalPropertyInfo.TypeConverter` already consults — so no call site changes. Read failures stop being swallowed and are reported through the existing `IXamlErrorSink`.

**Tech Stack:** C# / .NET 10 / WinUI 3 (Windows App SDK 1.7.250606001) / NUnit + NUnitLite.

**Spec:** `docs/superpowers/specs/2026-09-06-winui-xamldom-value-conversion-design.md`

## Global Constraints

- Target framework for every project: `net10.0-windows10.0.19041.0`, `UseWinUI=true`, `RuntimeIdentifier` `win-x64`, `Nullable` is `disable`.
- `Microsoft.WindowsAppSDK` is pinned to `1.7.250606001` — never bump it.
- Build: `dotnet build WinUIDesigner.sln`.
- **Tests MUST be run via each test project's built `.exe` with `--noresult`. NEVER `dotnet test`** — it cannot activate `Microsoft.UI.Xaml` WinRT classes and silently reports every WinUI-dependent test as *ignored* rather than run. The three executables:
  - `tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe`
  - `tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe`
  - `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe`
  - Single fixture or test: append `--test=<FullyQualifiedName>`.
- **Every WinUI object must be created inside `WinUITestHost.Run(...)` or `WinUITestHost.RunWithWindow(...)`.** WinRT objects are thread-affine to the process-wide dispatcher thread those helpers marshal onto.
- Project dependency direction `Designer` → `DesignModel` → `XamlDom` must not be violated.
- Files ported from SharpDevelop keep their original MIT copyright header. **`XamlValueConverter.cs` is NOT ported code — it gets no SharpDevelop header.**
- **Zero `.xaml` files in `src/`.**
- CLAUDE.md's "Known WinUI platform gaps" list applies: before assuming a WPF API exists in WinUI, verify it.
- **Indentation is per-file — match the file you are editing.** Surveyed for this plan: **tabs** in every file under `src/WinUIDesigner.XamlDom/` (`XamlPropertyInfo.cs`, `XamlProperty.cs`, `XamlTextValue.cs`, `AssemblyInfo.cs`) — the new `src/WinUIDesigner.XamlDom/XamlValueConverter.cs` therefore uses tabs too; **4 spaces** in `tests/WinUIDesigner.XamlDom.Tests/RoundTripTests.cs` and in `tests/WinUIDesigner.Designer.Tests/ModelToolsArrangeItemsTests.cs`, so the new `tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs` uses 4 spaces. (`tests/WinUIDesigner.XamlDom.Tests/TestHelper.cs` uses tabs — do not take it as the convention for new test files.) Code blocks below are written in the destination file's own indentation; paste them as-is.

## Baseline

Measured on the current tree, and the number every task must return to:

| Suite | Baseline |
|---|---|
| XamlDom | 70 tests: 66 passed, 4 pre-existing ignores |
| DesignModel | 75 passed / 75 |
| Designer | 245 passed / 245 |

The 4 XamlDom ignores are pre-existing and unrelated; they stay ignored.

## Plan-level rulings

These resolve wording the spec leaves open to an implementer:

1. **`XamlValueConverter` is `internal`, and `WinUIDesigner.XamlDom` gains an `InternalsVisibleTo` for its test project.** `GetCustomTypeConverter` is declared `public static TypeConverter`, so an internal concrete type is returned as `TypeConverter` and nothing outside the assembly needs the name. `src/WinUIDesigner.Designer/AssemblyInfo.cs:12` and `src/WinUIDesigner.DesignModel/AssemblyInfo.cs:13` already establish this pattern in this codebase; `src/WinUIDesigner.XamlDom/AssemblyInfo.cs` has none yet and gains one in Task 1.

2. **Two distinct exception types, deliberately.** `NotSupportedException` means *this mechanism refuses to try* (markup-extension text; host cannot be activated in this process). `FormatException` means *it tried and the value is wrong* (XamlReader threw; the value was ignored; the result is not assignable). Task 4's error reporting and several tests discriminate on the type, so do not collapse them.

3. **Decision 8's failing branch has no known triggering input, and the plan says so rather than faking a test for it.** The `ReadLocalValue == UnsetValue` check exists because WinUI does not always throw on a value it cannot use — the read-side reviewer measured `Hub.BorderThickness='{Binding}'` loading with no exception and leaving the default. Ruling 5's brace rejection now closes that specific path before the fragment is built, and no other accept-and-ignore input has been found. Task 1 therefore pins only the no-false-positive direction (a value that legitimately equals the property default must still convert). The failing direction is uncovered insurance costing one comparison. Do not invent a test that asserts it by mocking; do not delete the check.

4. **The host table is filled in two tasks on purpose.** Task 1 lands the algorithm with four entries chosen to cover the three shapes (value type, `DependencyObject`-derived, collection) plus a nullable case. Task 2 fills the remaining twelve. The two tasks fail in different ways — "the algorithm is wrong" versus "this entry's host is wrong" — and a reflection-built table is exactly what got rejected for picking hosts that cannot be activated, so the per-entry surface deserves its own reviewer gate.

5. **Brace rejection happens inside the converter even though the parser already guards the attribute form.** `XamlParser.cs:751-752` routes attribute values starting with `{` to `MarkupExtensionParser`, but the property-element form does not go through that route, and `XamlTextValue.Text` strips a leading `{}` (`XamlTextValue.cs:73-77`) so `'{}{Binding}'` arrives as a live `'{Binding}'`. The converter is the boundary that must hold.

6. **Tasks 1 and 2 are behaviourally dark.** Nothing calls the converter until Task 3 registers it, so all three suites stay at baseline through the end of Task 2. That is expected, not a sign the tests are vacuous — every Task 1 and Task 2 test calls the converter directly and fails without it.

7. **Task 3 changes behaviour and fixes its own fallout in the same commit.** The measured fallout is exactly one test. Splitting the registration from that fix would leave the tree red at a task boundary.

---

### Task 1: XamlValueConverter — the conversion algorithm

**Files:**
- Create: `src/WinUIDesigner.XamlDom/XamlValueConverter.cs` (tabs)
- Modify: `src/WinUIDesigner.XamlDom/AssemblyInfo.cs`
- Create: `tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs` (4 spaces)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `internal sealed class WinUIDesigner.XamlDom.XamlValueConverter : TypeConverter`
  - `internal XamlValueConverter(Type targetType, Type hostType, string hostPropertyName)` — direct construction, used by tests to exercise hosts that are not in the table.
  - `public static TypeConverter XamlValueConverter.TryCreate(Type propertyType)` — returns a converter, or `null` when this mechanism does not apply. Task 3 calls this from `GetCustomTypeConverter`.
  - `CanConvertFrom(context, typeof(string)) == true`; `CanConvertTo(context, typeof(string)) == false`.

**Context:** `src/WinUIDesigner.XamlDom/TemplateHelper.cs:102` is the existing precedent for calling `Microsoft.UI.Xaml.Markup.XamlReader.Load` inside this library — this task is not introducing a new dependency.

- [ ] **Step 1: Add the `InternalsVisibleTo`**

`src/WinUIDesigner.XamlDom/AssemblyInfo.cs` currently reads:

```csharp
using System.Reflection;

[assembly: AssemblyTitle("WinUIDesigner.XamlDom")]
[assembly: AssemblyDescription("WinUI 3 XAML object model and parser")]
```

Replace the whole file with:

```csharp
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("WinUIDesigner.XamlDom")]
[assembly: AssemblyDescription("WinUI 3 XAML object model and parser")]

// XamlValueConverter is internal - GetCustomTypeConverter hands it out as a plain TypeConverter,
// so no consumer outside this assembly needs the name - but its tests construct it directly to
// exercise hosts that are deliberately not in the table. Same pattern as
// WinUIDesigner.Designer's and WinUIDesigner.DesignModel's own AssemblyInfo.cs.
[assembly: InternalsVisibleTo("WinUIDesigner.XamlDom.Tests")]
```

- [ ] **Step 2: Write the failing tests**

Create `tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs`:

```csharp
using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using Microsoft.UI.Xaml;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.XamlDom.Tests
{
    /// <summary>
    /// Phase A of the XamlDom value-conversion fix: the converter that turns XAML attribute text
    /// into a WinRT-typed value by letting WinUI's own XamlReader do the conversion.
    /// </summary>
    /// <remarks>
    /// Every test runs on the WinUI dispatcher thread (see <see cref="WinUITestHost"/>) because
    /// XamlReader.Load and every object it produces are thread-affine WinRT objects.
    /// </remarks>
    [TestFixture]
    public class ValueConversionTests
    {
        static object Convert(Type propertyType, string text)
        {
            var converter = XamlValueConverter.TryCreate(propertyType);
            Assert.IsNotNull(converter, "No converter was created for " + propertyType.FullName);
            return converter.ConvertFrom(null, CultureInfo.InvariantCulture, text);
        }

        [Test]
        public void ConvertsAValueTypeFromAttributeText()
        {
            WinUITestHost.Run(() => {
                var value = Convert(typeof(Thickness), "1,2,3,4");
                Assert.IsInstanceOf<Thickness>(value);
                var thickness = (Thickness)value;
                Assert.AreEqual(1.0, thickness.Left);
                Assert.AreEqual(2.0, thickness.Top);
                Assert.AreEqual(3.0, thickness.Right);
                Assert.AreEqual(4.0, thickness.Bottom);
            });
        }

        [Test]
        public void ConvertsADependencyObjectValueFromTheGeometryMiniLanguage()
        {
            WinUITestHost.Run(() => {
                var value = Convert(typeof(Microsoft.UI.Xaml.Media.Geometry), "M0,0 L10,10 Z");
                var geometry = value as Microsoft.UI.Xaml.Media.PathGeometry;
                Assert.IsNotNull(geometry, "Path mini-language did not produce a PathGeometry");
                Assert.AreEqual(1, geometry.Figures.Count);
                Assert.AreEqual(1, geometry.Figures[0].Segments.Count);
                Assert.IsTrue(geometry.Figures[0].IsClosed, "The trailing Z did not close the figure");
            });
        }

        [Test]
        public void ConvertsACollectionValueFromAttributeText()
        {
            WinUITestHost.Run(() => {
                var value = Convert(typeof(Microsoft.UI.Xaml.Media.PointCollection), "1,2 3,4 5,6");
                var points = value as Microsoft.UI.Xaml.Media.PointCollection;
                Assert.IsNotNull(points);
                Assert.AreEqual(3, points.Count);
                Assert.AreEqual(1.0, points[0].X);
                Assert.AreEqual(6.0, points[2].Y);
            });
        }

        [Test]
        public void UnwrapsNullableSoAnimationFromAndToConvert()
        {
            // PointAnimation.From is Nullable<Point>; its NullableConverter reports
            // CanConvertFrom(string) == false, so without unwrapping it stays broken.
            WinUITestHost.Run(() => {
                var value = Convert(typeof(Windows.Foundation.Point?), "0.5,0.25");
                Assert.IsInstanceOf<Windows.Foundation.Point>(value);
                var point = (Windows.Foundation.Point)value;
                Assert.AreEqual(0.5, point.X);
                Assert.AreEqual(0.25, point.Y);
            });
        }

        [Test]
        public void ProducesNoConverterForATypeWithNoHostEntry()
        {
            Assert.IsNull(XamlValueConverter.TryCreate(typeof(Microsoft.UI.Xaml.Controls.Grid)));
        }

        [Test]
        public void ProducesNoConverterForUriShapedTypes()
        {
            // Loading an untrusted document must not issue outbound requests: XamlReader would
            // construct a BitmapImage and set UriSource, which begins a fetch. Uri itself already
            // has a working UriTypeConverter and never needed an entry.
            Assert.IsNull(XamlValueConverter.TryCreate(typeof(Microsoft.UI.Xaml.Media.ImageSource)));
            Assert.IsNull(XamlValueConverter.TryCreate(typeof(Uri)));
        }

        [Test]
        public void RefusesMarkupExtensionText()
        {
            WinUITestHost.Run(() => {
                var converter = XamlValueConverter.TryCreate(typeof(Thickness));
                Assert.Throws<NotSupportedException>(
                    () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "{StaticResource SomeKey}"));
                // XamlTextValue.Text strips a leading "{}", so an escaped literal arrives here as a
                // live extension. This is the boundary that has to hold.
                Assert.Throws<NotSupportedException>(
                    () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "{Binding}"));
            });
        }

        [Test]
        public void AcceptsEscapedLiteralBraces()
        {
            WinUITestHost.Run(() => {
                var value = Convert(typeof(Thickness), "{}1,2,3,4");
                Assert.AreEqual(1.0, ((Thickness)value).Left);
            });
        }

        [Test]
        public void ReportsAHostThatCannotBeActivatedAsUnsupportedRatherThanAsABadValue()
        {
            // TwoPaneView is the host a reflection-built table picks for GridLength, and it cannot
            // be activated in this process. Its failure must not look like "your value is wrong".
            WinUITestHost.Run(() => {
                var converter = new XamlValueConverter(
                    typeof(GridLength), typeof(Microsoft.UI.Xaml.Controls.TwoPaneView), "Pane1Length");
                var ex = Assert.Throws<NotSupportedException>(
                    () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "2*"));
                StringAssert.Contains("TwoPaneView", ex.Message);
            });
        }

        [Test]
        public void RefusesAResultThatIsNotAssignableToTheTargetType()
        {
            // UIElement.Clip is declared RectangleGeometry, but WinUI runs the Geometry
            // mini-language on it, so the only viable host returns a PathGeometry. Assigning that
            // would throw InvalidCastException deep inside the parser.
            WinUITestHost.Run(() => {
                var converter = new XamlValueConverter(
                    typeof(Microsoft.UI.Xaml.Media.RectangleGeometry),
                    typeof(Microsoft.UI.Xaml.Shapes.Path), "Data");
                var ex = Assert.Throws<FormatException>(
                    () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "M0,0 L10,10"));
                StringAssert.Contains("RectangleGeometry", ex.Message);
            });
        }

        [Test]
        public void RejectsTextTheHostCannotParse()
        {
            WinUITestHost.Run(() => {
                var converter = XamlValueConverter.TryCreate(typeof(Thickness));
                Assert.Throws<FormatException>(
                    () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "not-a-thickness"));
            });
        }

        [Test]
        public void ConvertsAValueThatHappensToEqualTheHostPropertyDefault()
        {
            // The converter treats "WinUI ignored the value" (ReadLocalValue == UnsetValue) as a
            // failure. This pins that the check does not fire on a legitimate value that merely
            // equals the default.
            WinUITestHost.Run(() => {
                var value = Convert(typeof(Thickness), "0,0,0,0");
                Assert.IsInstanceOf<Thickness>(value);
                Assert.AreEqual(0.0, ((Thickness)value).Left);
            });
        }

        [Test]
        public void DoesNotClaimAToStringConversion()
        {
            // PropertyNode gates an editable PropertyGrid text box on
            // CanConvertFrom(string) && CanConvertTo(string). Claiming the to-string direction
            // without a formatter would prefill that box from value.ToString() - a type name - and
            // for FontFamily WinUI accepts any string as a family name, so tabbing through the
            // grid would commit "Microsoft.UI.Xaml.Media.FontFamily" as the user's font.
            var converter = XamlValueConverter.TryCreate(typeof(Thickness));
            Assert.IsTrue(converter.CanConvertFrom(null, typeof(string)));
            Assert.IsFalse(converter.CanConvertTo(null, typeof(string)));
        }

        [Test]
        public void ConvertsIdenticallyUnderANonInvariantCulture()
        {
            WinUITestHost.Run(() => {
                var previous = Thread.CurrentThread.CurrentCulture;
                try {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                    CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                    var thickness = (Thickness)Convert(typeof(Thickness), "1.5,0,0,0");
                    Assert.AreEqual(1.5, thickness.Left, "Invariant decimal point was not honoured under de-DE");

                    var point = (Windows.Foundation.Point)Convert(typeof(Windows.Foundation.Point?), "1.5,2.5");
                    Assert.AreEqual(1.5, point.X);
                    Assert.AreEqual(2.5, point.Y);
                } finally {
                    Thread.CurrentThread.CurrentCulture = previous;
                    CultureInfo.CurrentCulture = previous;
                }
            });
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```
dotnet build WinUIDesigner.sln
```

Expected: **build errors**, `CS0103`/`CS0246` — `XamlValueConverter` does not exist. That is the failing state for this step; there is nothing to run yet.

- [ ] **Step 4: Write the converter**

Create `src/WinUIDesigner.XamlDom/XamlValueConverter.cs`. **This file uses tabs** (every sibling in `src/WinUIDesigner.XamlDom/` does), and it is new code, not a SharpDevelop port, so it carries **no** MIT copyright header:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Converts XAML attribute text to a WinRT-typed value by letting WinUI's own
	/// <c>XamlReader</c> perform the conversion on a synthetic one-element fragment.
	/// </summary>
	/// <remarks>
	/// <para>
	/// WinRT types projected into .NET carry no <see cref="TypeConverterAttribute"/>, so
	/// <c>TypeDescriptor.GetConverter</c> falls back to the base <see cref="TypeConverter"/>,
	/// whose <c>CanConvertFrom(string)</c> is false. Measured on this SDK: of 363 distinct
	/// property types on WinUI's <see cref="DependencyObject"/> surface, 115 report
	/// <c>CanConvertTo(string)</c> but not <c>CanConvertFrom(string)</c> - and the to-string claim
	/// is the base implementation's <c>ToString()</c>, which is why the gap never looked like a
	/// missing converter. The result was that only <c>double</c>, <c>enum</c> and <c>string</c>
	/// attribute values parsed at all.
	/// </para>
	/// <para>
	/// Rather than hand-write a converter per type, this class delegates to the platform:
	/// <c>&lt;Host P='text'/&gt;</c> is loaded through
	/// <c>Microsoft.UI.Xaml.Markup.XamlReader.Load</c> and <c>P</c> is read back. WinUI's own
	/// converters then handle every grammar exactly - <c>"Red"</c>, <c>"2*"</c>, <c>"Auto"</c>,
	/// <c>"M0,0 L10,10"</c>. <see cref="TemplateHelper"/> already uses <c>XamlReader.Load</c>
	/// inside this library for the same reason.
	/// </para>
	/// <para>
	/// Hosts are hand-listed. A reflection-built map was tried and rejected: ordering candidates
	/// by shortest type name selects <c>TwoPaneView.Pane1Length</c> for <see cref="GridLength"/>
	/// and <c>WebView2.DefaultBackgroundColor</c> for <c>Windows.UI.Color</c>, neither of which
	/// can be activated - and the resulting <c>XamlParseException</c> is indistinguishable from
	/// the one a bad value produces.
	/// </para>
	/// </remarks>
	internal sealed class XamlValueConverter : TypeConverter
	{
		const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

		readonly Type targetType;
		readonly Type hostType;
		readonly string hostPropertyName;

		internal XamlValueConverter(Type targetType, Type hostType, string hostPropertyName)
		{
			this.targetType = targetType;
			this.hostType = hostType;
			this.hostPropertyName = hostPropertyName;
		}

		#region Host table

		static readonly Dictionary<Type, KeyValuePair<Type, string>> HostTable = BuildHostTable();

		static Dictionary<Type, KeyValuePair<Type, string>> BuildHostTable()
		{
			var table = new Dictionary<Type, KeyValuePair<Type, string>>();
			Add(table, typeof(Thickness), typeof(Microsoft.UI.Xaml.Controls.Border), "Padding");
			Add(table, typeof(Windows.Foundation.Point), typeof(Microsoft.UI.Xaml.Controls.Border), "RenderTransformOrigin");
			Add(table, typeof(Microsoft.UI.Xaml.Media.PointCollection), typeof(Microsoft.UI.Xaml.Shapes.Polygon), "Points");
			Add(table, typeof(Microsoft.UI.Xaml.Media.Geometry), typeof(Microsoft.UI.Xaml.Shapes.Path), "Data");
			return table;
		}

		static void Add(Dictionary<Type, KeyValuePair<Type, string>> table, Type target, Type host, string property)
		{
			table[target] = new KeyValuePair<Type, string>(host, property);
		}

		#endregion

		/// <summary>
		/// Returns a converter for <paramref name="propertyType"/>, or <c>null</c> when this
		/// mechanism does not apply to it.
		/// </summary>
		public static TypeConverter TryCreate(Type propertyType)
		{
			if (propertyType == null)
				return null;

			// Uri-shaped types are excluded deliberately. XamlReader would construct a
			// BitmapImage and set UriSource for an ImageSource attribute, beginning an outbound
			// fetch - so merely opening an untrusted document would issue attacker-chosen
			// requests. XamlParser routes ImageSource through TypeFinder.ConvertUriToLocalUri
			// first, but the base implementation is a pass-through (XamlTypeFinder.cs:417-420).
			// Uri itself already has a working UriTypeConverter.
			if (typeof(Uri).IsAssignableFrom(propertyType))
				return null;
			if (typeof(Microsoft.UI.Xaml.Media.ImageSource).IsAssignableFrom(propertyType))
				return null;

			// NullableConverter delegates to the underlying type's converter, so unwrapping here
			// covers PointAnimation.From (Point?), ColorAnimation.From (Color?) and
			// ToolTip.PlacementRect (Rect?) without doubling the table. Nullable<double> and
			// friends never reach here: their converters report CanConvertFrom(string) already,
			// so GetCustomTypeConverter is not consulted for them.
			var lookupType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

			KeyValuePair<Type, string> host;
			if (!HostTable.TryGetValue(lookupType, out host))
				return null;

			return new XamlValueConverter(lookupType, host.Key, host.Value);
		}

		public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
		{
			return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
		}

		/// <remarks>
		/// Deliberately false for <see cref="string"/> while no value-to-string formatter exists.
		/// <c>PropertyNode</c> gates an editable PropertyGrid text box on
		/// <c>CanConvertFrom(string) &amp;&amp; CanConvertTo(string)</c>
		/// (<c>PropertyNode.cs:231-233</c>) and fills it from
		/// <c>ConvertToInvariantString</c>, which on a converter that does not override
		/// <c>ConvertTo</c> is <c>value.ToString()</c> - a type name. For most types the commit
		/// throws and reverts, but WinUI accepts any string as a font family name, so a user
		/// merely tabbing through the grid would silently rewrite their font to
		/// "Microsoft.UI.Xaml.Media.FontFamily". Phase B turns this on per type as each formatter
		/// lands.
		/// </remarks>
		public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
		{
			if (destinationType == typeof(string))
				return false;
			return base.CanConvertTo(context, destinationType);
		}

		public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
		{
			var text = value as string;
			if (text == null)
				return base.ConvertFrom(context, culture, value);

			// Markup extensions are out of scope, and letting one through is worse than failing:
			// an isolated fragment resolves {StaticResource}/{ThemeResource} against
			// Application.Current.Resources - the designer's own resource scope, not the
			// document's - so the designer would render the user's document with its own theme
			// values and say nothing. XamlParser guards the attribute form
			// (XamlParser.cs:751-752), but the property-element form is not routed through
			// MarkupExtensionParser, and XamlTextValue.Text strips a leading "{}"
			// (XamlTextValue.cs:73-77), so "{}{Binding}" arrives here as a live "{Binding}".
			if (text.StartsWith("{", StringComparison.Ordinal) && !text.StartsWith("{}", StringComparison.Ordinal))
				throw new NotSupportedException(
					"Markup extension text cannot be converted to " + targetType.FullName + ": " + text);

			var attributeText = text.StartsWith("{}", StringComparison.Ordinal) ? text.Substring(2) : text;

			// Host viability depends on the hosting process's IXamlMetadataProvider and available
			// theme resources - a bare <TextBox/> does not load in this test host, for instance -
			// so a host proven here is not proven in every consumer. Checking it separately keeps
			// "this host is unusable" from being reported as "your value is wrong".
			if (!IsHostUsable(hostType))
				throw new NotSupportedException(
					"Conversion host <" + hostType.Name + "/> cannot be activated in this process, so " +
					targetType.FullName + " values cannot be converted here.");

			var xaml = "<" + hostType.Name + " xmlns='" + PresentationNamespace + "' " +
			           hostPropertyName + "='" + System.Security.SecurityElement.Escape(attributeText) + "'/>";

			object host;
			try {
				host = Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
			} catch (Exception) {
				// The XamlParseException's own message is unusable: WinUI caches the first
				// failure's message for the lifetime of the process and repeats it for every
				// later failure, across intervening successes, and its Line/Position refer to
				// this synthetic fragment rather than the user's file.
				throw new FormatException(FailureMessage(text));
			}

			var hostProperty = hostType.GetProperty(hostPropertyName, BindingFlags.Public | BindingFlags.Instance);
			if (hostProperty == null)
				throw new NotSupportedException(
					"Conversion host property " + hostType.Name + "." + hostPropertyName + " does not exist.");

			// WinUI does not always throw on a value it cannot use; it can leave the property
			// untouched, which is indistinguishable from a successful conversion to the same
			// default. The local value says which happened.
			var dependencyProperty = TryGetHostDependencyProperty();
			var hostObject = host as DependencyObject;
			if (dependencyProperty != null && hostObject != null &&
			    hostObject.ReadLocalValue(dependencyProperty) == DependencyProperty.UnsetValue)
				throw new FormatException(FailureMessage(text));

			var result = hostProperty.GetValue(host);
			if (result != null && !targetType.IsInstanceOfType(result))
				throw new FormatException(
					"Converting '" + text + "' produced a " + result.GetType().FullName +
					", which is not assignable to " + targetType.FullName + ".");

			return result;
		}

		string FailureMessage(string text)
		{
			return "Cannot convert '" + text + "' to " + targetType.FullName + ".";
		}

		#region Host viability

		static readonly Dictionary<Type, bool> hostViability = new Dictionary<Type, bool>();

		static bool IsHostUsable(Type hostType)
		{
			lock (hostViability) {
				bool usable;
				if (hostViability.TryGetValue(hostType, out usable))
					return usable;

				try {
					Microsoft.UI.Xaml.Markup.XamlReader.Load(
						"<" + hostType.Name + " xmlns='" + PresentationNamespace + "'/>");
					usable = true;
				} catch (Exception) {
					usable = false;
				}

				hostViability[hostType] = usable;
				return usable;
			}
		}

		#endregion

		DependencyProperty TryGetHostDependencyProperty()
		{
			try {
				return (DependencyProperty)WinUIStaticMemberLocator.GetStaticValue(
					hostType, hostPropertyName + "Property", typeof(DependencyProperty),
					includeNonPublic: true, walkBaseTypes: true);
			} catch (Exception) {
				return null;
			}
		}
	}
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueConversionTests
```

Expected: 14 passed, 0 failed.

If `ReportsAHostThatCannotBeActivatedAsUnsupportedRatherThanAsABadValue` fails because `TwoPaneView` *does* load in your process, that is a genuine environment difference — report it as a BLOCKED finding rather than deleting the test; the plan's Ruling 4 depends on that host being unusable here.

- [ ] **Step 6: Run all three suites to confirm nothing else moved**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: XamlDom 84 tests (80 passed, 4 ignored) — baseline 66 passed plus this task's 14; DesignModel 75/75; Designer 245/245. Nothing calls the converter yet, so the other two suites are unchanged by construction.

- [ ] **Step 7: Commit**

```bash
git add src/WinUIDesigner.XamlDom/XamlValueConverter.cs src/WinUIDesigner.XamlDom/AssemblyInfo.cs tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs
git commit -m "feat: add XamlValueConverter, converting XAML attribute text via WinUI's own XamlReader

WinRT types projected into .NET carry no TypeConverterAttribute, so
TypeDescriptor.GetConverter falls back to the base TypeConverter and
CanConvertFrom(string) is false for 115 of the 363 property types on WinUI's
DependencyObject surface. Only double, enum and string ever parsed.

Rather than hand-writing a converter per type, delegate to the platform: load
<Host P='text'/> through XamlReader and read P back, so WinUI's own converters
handle every grammar exactly.

Refuses markup-extension text (an isolated fragment would resolve
{StaticResource} against the designer's own Application.Current.Resources),
excludes Uri-shaped types (an ImageSource attribute would start an outbound
fetch on document open), treats a value WinUI silently ignored as a failure,
and asserts the result is assignable to the target type.

CanConvertTo(string) is deliberately false while no formatter exists: PropertyNode
gates an editable PropertyGrid box on both directions, and WinUI accepts any
string as a font family name, so claiming the to-string direction would let a
user overwrite their font by tabbing through the grid.

Not yet registered - nothing calls it until the next commit.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Fill the host table

**Files:**
- Modify: `src/WinUIDesigner.XamlDom/XamlValueConverter.cs` (the `BuildHostTable` method only)
- Modify: `tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs` (append tests)

**Interfaces:**
- Consumes: `XamlValueConverter.TryCreate(Type)` from Task 1.
- Produces: a 16-entry host table. Task 3 relies on `TryCreate` returning a converter for `Thickness`, `CornerRadius`, `GridLength`, `Windows.UI.Color`, `Brush`, `FontFamily`, `FontWeight`, `Point`, `PointCollection`, `DoubleCollection`, `Geometry`, `Rect`, `Size`, `Duration`, `Matrix` and `Vector3`.

**Context:** Host *types* were chosen from those confirmed activatable in this process — `Border`, `Grid`, `Path`, `Polygon`, `Line`, `TextBlock`, `Storyboard`, `MatrixTransform`, `SolidColorBrush`, `ColumnDefinition`, `RectangleGeometry`, `ArcSegment`. `Hub` also works but is ~1.8× slower than `Border` and is not used. Two known-absent entries are deliberate and are recorded in the spec's Known gaps: `RectangleGeometry` (its only viable host returns a `PathGeometry`, which Task 1's assignability check correctly refuses) and `Vector2` (its only `Vector2`-typed property, `UIElement.ActualSize`, is read-only).

- [ ] **Step 1: Write the failing tests**

Append to `tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs`, inside the class:

```csharp
        // ---- One test per host-table entry. A host that cannot be activated, or a host property
        // whose grammar differs from the target type's, can then never ship silently.

        [Test]
        public void ConvertsCornerRadius()
        {
            WinUITestHost.Run(() => {
                var radius = (CornerRadius)Convert(typeof(CornerRadius), "5");
                Assert.AreEqual(5.0, radius.TopLeft);
                Assert.AreEqual(5.0, radius.BottomRight);
            });
        }

        [Test]
        public void ConvertsGridLengthInEveryUnit()
        {
            WinUITestHost.Run(() => {
                var star = (GridLength)Convert(typeof(GridLength), "2*");
                Assert.AreEqual(GridUnitType.Star, star.GridUnitType);
                Assert.AreEqual(2.0, star.Value);

                var fractionalStar = (GridLength)Convert(typeof(GridLength), "1.5*");
                Assert.AreEqual(GridUnitType.Star, fractionalStar.GridUnitType);
                Assert.AreEqual(1.5, fractionalStar.Value);

                var auto = (GridLength)Convert(typeof(GridLength), "Auto");
                Assert.AreEqual(GridUnitType.Auto, auto.GridUnitType);

                var pixel = (GridLength)Convert(typeof(GridLength), "40");
                Assert.AreEqual(GridUnitType.Pixel, pixel.GridUnitType);
                Assert.AreEqual(40.0, pixel.Value);
            });
        }

        [Test]
        public void ConvertsColorFromBothNamedAndHexForms()
        {
            WinUITestHost.Run(() => {
                var named = (Windows.UI.Color)Convert(typeof(Windows.UI.Color), "Red");
                Assert.AreEqual(255, named.R);
                Assert.AreEqual(0, named.G);
                Assert.AreEqual(255, named.A);

                var hex = (Windows.UI.Color)Convert(typeof(Windows.UI.Color), "#FF102030");
                Assert.AreEqual(0x10, hex.R);
                Assert.AreEqual(0x20, hex.G);
                Assert.AreEqual(0x30, hex.B);
            });
        }

        [Test]
        public void ConvertsBrushFromBothNamedAndHexForms()
        {
            WinUITestHost.Run(() => {
                var named = Convert(typeof(Microsoft.UI.Xaml.Media.Brush), "Red") as Microsoft.UI.Xaml.Media.SolidColorBrush;
                Assert.IsNotNull(named);
                Assert.AreEqual(255, named.Color.R);

                var hex = Convert(typeof(Microsoft.UI.Xaml.Media.Brush), "#FF102030") as Microsoft.UI.Xaml.Media.SolidColorBrush;
                Assert.IsNotNull(hex);
                Assert.AreEqual(0x30, hex.Color.B);
            });
        }

        [Test]
        public void ConvertsFontFamilyIncludingAFragmentUri()
        {
            WinUITestHost.Run(() => {
                var plain = (Microsoft.UI.Xaml.Media.FontFamily)Convert(
                    typeof(Microsoft.UI.Xaml.Media.FontFamily), "Consolas");
                Assert.AreEqual("Consolas", plain.Source);

                var fragment = (Microsoft.UI.Xaml.Media.FontFamily)Convert(
                    typeof(Microsoft.UI.Xaml.Media.FontFamily), "ms-appx:///Fonts/Custom.ttf#Custom");
                Assert.AreEqual("ms-appx:///Fonts/Custom.ttf#Custom", fragment.Source);
            });
        }

        [Test]
        public void ConvertsFontWeightFromItsNamedForm()
        {
            // WinUI rejects the numeric form: FontWeight='450' throws. Only names convert, so a
            // non-standard weight is not representable through this mechanism - a recorded gap.
            WinUITestHost.Run(() => {
                var bold = (Windows.UI.Text.FontWeight)Convert(typeof(Windows.UI.Text.FontWeight), "Bold");
                Assert.AreEqual(700, bold.Weight);

                var semiBold = (Windows.UI.Text.FontWeight)Convert(typeof(Windows.UI.Text.FontWeight), "SemiBold");
                Assert.AreEqual(600, semiBold.Weight);

                var converter = XamlValueConverter.TryCreate(typeof(Windows.UI.Text.FontWeight));
                Assert.Throws<FormatException>(
                    () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "450"),
                    "WinUI is expected to reject the numeric FontWeight form");
            });
        }

        [Test]
        public void ConvertsDoubleCollectionIncludingTheEmptyForm()
        {
            WinUITestHost.Run(() => {
                var dashes = (Microsoft.UI.Xaml.Media.DoubleCollection)Convert(
                    typeof(Microsoft.UI.Xaml.Media.DoubleCollection), "2 3");
                Assert.AreEqual(2, dashes.Count);
                Assert.AreEqual(2.0, dashes[0]);
                Assert.AreEqual(3.0, dashes[1]);
            });
        }

        [Test]
        public void ConvertsRect()
        {
            WinUITestHost.Run(() => {
                var rect = (Windows.Foundation.Rect)Convert(typeof(Windows.Foundation.Rect), "1,2,3,4");
                Assert.AreEqual(1.0, rect.X);
                Assert.AreEqual(2.0, rect.Y);
                Assert.AreEqual(3.0, rect.Width);
                Assert.AreEqual(4.0, rect.Height);
            });
        }

        [Test]
        public void ConvertsSize()
        {
            WinUITestHost.Run(() => {
                var size = (Windows.Foundation.Size)Convert(typeof(Windows.Foundation.Size), "3,4");
                Assert.AreEqual(3.0, size.Width);
                Assert.AreEqual(4.0, size.Height);
            });
        }

        [Test]
        public void ConvertsDurationInAllThreeForms()
        {
            WinUITestHost.Run(() => {
                // Asserted through ToString() because those are the exact forms measured on this
                // SDK; Duration's own member names were not verified and are not worth guessing.
                Assert.AreEqual("00:00:01", Convert(typeof(Duration), "0:0:1").ToString());
                Assert.AreEqual("Forever", Convert(typeof(Duration), "Forever").ToString());
                Assert.AreEqual("Automatic", Convert(typeof(Duration), "Automatic").ToString());
            });
        }

        [Test]
        public void ConvertsMatrix()
        {
            WinUITestHost.Run(() => {
                var matrix = Convert(typeof(Microsoft.UI.Xaml.Media.Matrix), "1,0,0,1,5,6");
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Media.Matrix>(matrix);
                Assert.AreEqual("1,0,0,1,5,6", matrix.ToString());
            });
        }

        [Test]
        public void ConvertsVector3()
        {
            // UIElement.Translation is a plain CLR property with no TranslationProperty static, so
            // the ReadLocalValue guard cannot apply to this entry; it relies on the throw alone.
            WinUITestHost.Run(() => {
                var vector = (System.Numerics.Vector3)Convert(typeof(System.Numerics.Vector3), "1,2,3");
                Assert.AreEqual(1f, vector.X);
                Assert.AreEqual(2f, vector.Y);
                Assert.AreEqual(3f, vector.Z);
            });
        }

        [Test]
        public void ConvertsAnEmptyPointCollection()
        {
            WinUITestHost.Run(() => {
                var points = (Microsoft.UI.Xaml.Media.PointCollection)Convert(
                    typeof(Microsoft.UI.Xaml.Media.PointCollection), "");
                Assert.AreEqual(0, points.Count);
            });
        }

        [Test]
        public void HasNoEntryForTheTwoTypesWithNoViableHost()
        {
            // Recorded gaps, asserted so they are a decision rather than an oversight.
            // RectangleGeometry: UIElement.Clip declares it, but WinUI runs the Geometry
            // mini-language on that property, so the only host returns a PathGeometry.
            // Vector2: the only Vector2-typed property, UIElement.ActualSize, is read-only.
            Assert.IsNull(XamlValueConverter.TryCreate(typeof(Microsoft.UI.Xaml.Media.RectangleGeometry)));
            Assert.IsNull(XamlValueConverter.TryCreate(typeof(System.Numerics.Vector2)));
        }

        [Test]
        public void EveryHostInTheTableCanBeActivatedInThisProcess()
        {
            // The failure mode this guards is the reason the table is hand-listed at all: a
            // reflection-built map picked TwoPaneView and WebView2, which cannot be activated, and
            // their XamlParseException is indistinguishable from a bad value.
            var targets = new[] {
                typeof(Thickness), typeof(CornerRadius), typeof(GridLength), typeof(Windows.UI.Color),
                typeof(Microsoft.UI.Xaml.Media.Brush), typeof(Microsoft.UI.Xaml.Media.FontFamily),
                typeof(Windows.UI.Text.FontWeight), typeof(Windows.Foundation.Point),
                typeof(Microsoft.UI.Xaml.Media.PointCollection), typeof(Microsoft.UI.Xaml.Media.DoubleCollection),
                typeof(Microsoft.UI.Xaml.Media.Geometry), typeof(Windows.Foundation.Rect),
                typeof(Windows.Foundation.Size), typeof(Duration),
                typeof(Microsoft.UI.Xaml.Media.Matrix), typeof(System.Numerics.Vector3),
            };

            WinUITestHost.Run(() => {
                foreach (var target in targets) {
                    var converter = XamlValueConverter.TryCreate(target);
                    Assert.IsNotNull(converter, "No host-table entry for " + target.FullName);
                    // A NotSupportedException here means the host itself could not be activated.
                    // A FormatException would mean the host is fine and this filler text is not a
                    // valid value, which is expected and not what this test is about.
                    try {
                        converter.ConvertFrom(null, CultureInfo.InvariantCulture, "0");
                    } catch (NotSupportedException ex) {
                        Assert.Fail("Host for " + target.FullName + " is unusable: " + ex.Message);
                    } catch (FormatException) {
                        // Fine - the host activated and rejected the filler value.
                    }
                }
            });
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueConversionTests
```

Expected: 13 failures — the 12 new conversion tests fail with `No converter was created for <type>` (Task 1's table has only four entries), and `EveryHostInTheTableCanBeActivatedInThisProcess` fails on the first missing entry. `HasNoEntryForTheTwoTypesWithNoViableHost` passes already — that is correct, it pins a deliberate absence.

- [ ] **Step 3: Fill the table**

In `src/WinUIDesigner.XamlDom/XamlValueConverter.cs`, replace the body of `BuildHostTable` with all sixteen entries:

```csharp
		static Dictionary<Type, KeyValuePair<Type, string>> BuildHostTable()
		{
			var table = new Dictionary<Type, KeyValuePair<Type, string>>();
			Add(table, typeof(Thickness), typeof(Microsoft.UI.Xaml.Controls.Border), "Padding");
			Add(table, typeof(CornerRadius), typeof(Microsoft.UI.Xaml.Controls.Border), "CornerRadius");
			Add(table, typeof(GridLength), typeof(Microsoft.UI.Xaml.Controls.ColumnDefinition), "Width");
			Add(table, typeof(Windows.UI.Color), typeof(Microsoft.UI.Xaml.Media.SolidColorBrush), "Color");
			Add(table, typeof(Microsoft.UI.Xaml.Media.Brush), typeof(Microsoft.UI.Xaml.Controls.Border), "Background");
			Add(table, typeof(Microsoft.UI.Xaml.Media.FontFamily), typeof(Microsoft.UI.Xaml.Controls.TextBlock), "FontFamily");
			Add(table, typeof(Windows.UI.Text.FontWeight), typeof(Microsoft.UI.Xaml.Controls.TextBlock), "FontWeight");
			Add(table, typeof(Windows.Foundation.Point), typeof(Microsoft.UI.Xaml.Controls.Border), "RenderTransformOrigin");
			Add(table, typeof(Microsoft.UI.Xaml.Media.PointCollection), typeof(Microsoft.UI.Xaml.Shapes.Polygon), "Points");
			Add(table, typeof(Microsoft.UI.Xaml.Media.DoubleCollection), typeof(Microsoft.UI.Xaml.Shapes.Line), "StrokeDashArray");
			Add(table, typeof(Microsoft.UI.Xaml.Media.Geometry), typeof(Microsoft.UI.Xaml.Shapes.Path), "Data");
			Add(table, typeof(Windows.Foundation.Rect), typeof(Microsoft.UI.Xaml.Media.RectangleGeometry), "Rect");
			Add(table, typeof(Windows.Foundation.Size), typeof(Microsoft.UI.Xaml.Media.ArcSegment), "Size");
			Add(table, typeof(Duration), typeof(Microsoft.UI.Xaml.Media.Animation.Storyboard), "Duration");
			Add(table, typeof(Microsoft.UI.Xaml.Media.Matrix), typeof(Microsoft.UI.Xaml.Media.MatrixTransform), "Matrix");
			Add(table, typeof(System.Numerics.Vector3), typeof(Microsoft.UI.Xaml.Controls.Grid), "Translation");
			return table;
		}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueConversionTests
```

Expected: 29 passed, 0 failed.

If `ConvertsAnEmptyPointCollection` or `ConvertsDoubleCollectionIncludingTheEmptyForm` fails, do not weaken the assertion — an empty collection attribute was measured as accepted (`Points=''` → `Count == 0`), so a failure is a real regression in the mechanism.

- [ ] **Step 5: Run all three suites**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: XamlDom 99 tests (95 passed, 4 ignored); DesignModel 75/75; Designer 245/245. Still nothing calls the converter.

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.XamlDom/XamlValueConverter.cs tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs
git commit -m "feat: fill the XamlValueConverter host table to sixteen entries

Adds CornerRadius, GridLength, Color, Brush, FontFamily, FontWeight,
DoubleCollection, Rect, Size, Duration, Matrix and Vector3 alongside the four
entries the algorithm landed with, each with its own test so a host that cannot
be activated in the hosting process can never ship silently.

Grammar variants are covered rather than just types: GridLength Auto/star/
fractional-star/pixel, Color named and hex, Duration timespan/Forever/Automatic,
FontFamily with a fragment URI, and the empty collection form.

Two absences are asserted as deliberate: RectangleGeometry, whose only viable
host returns a PathGeometry that the assignability check correctly refuses, and
Vector2, whose only property of that type is read-only.

FontWeight's numeric form is rejected by WinUI; only names convert, so a
non-standard weight is not representable through this mechanism.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Register the converter, and correct what it exposes

**Files:**
- Modify: `src/WinUIDesigner.XamlDom/XamlPropertyInfo.cs:344-354` (`GetCustomTypeConverter`)
- Modify: `tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs` (append document-level and guard tests)
- Modify: `tests/WinUIDesigner.XamlDom.Tests/RoundTripTests.cs` (add the colour assertion)
- Modify: `tests/WinUIDesigner.Designer.Tests/ModelToolsArrangeItemsTests.cs:78-103`
- Modify: `tests/WinUIDesigner.Designer.Tests/PathHandlerExtensionTests.cs:17-33`
- Modify: `tests/WinUIDesigner.Designer.Tests/PolyLineHandlerExtensionTests.cs:18-30`
- Modify: `tests/WinUIDesigner.Designer.Tests/PathHandlerExtensionConvertMenuTests.cs:18-28`
- Modify: `tests/WinUIDesigner.Designer.Tests/Phase4dDefinitionOfDoneTests.cs:220-232`

**Interfaces:**
- Consumes: `XamlValueConverter.TryCreate(Type)` from Tasks 1-2.
- Produces: attribute conversion now works document-wide. No new public API.

**This is the behaviour flip.** `XamlNormalPropertyInfo.TypeConverter` (`XamlPropertyInfo.cs:306`) already calls `GetCustomTypeConverter`, and `XamlParser.FindProperty` resolves every normal property to `XamlNormalPropertyInfo` (`XamlParser.cs:557`), so this one line reaches every attribute in every document. **Do not touch `XamlDependencyPropertyInfo.TypeConverter` at `XamlPropertyInfo.cs:175`** — spec Decision 11: that class is only ever constructed for attached properties (`XamlParser.cs:599`, always `isAttached: true`) and for `Ignorable` (`XamlDocument.cs:392`), every attached property in play today is `int` or `double` with a working converter, and changing it would alter behaviour nobody has reasoned about.

- [ ] **Step 1: Write the failing document-level and guard tests**

Append to `tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs`, inside the class:

```csharp
        // ---- Document level: the converter is only useful if XamlParser actually reaches it.

        const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        [Test]
        public void ParsesMarginFromARealDocument()
        {
            WinUITestHost.Run(() => {
                var doc = XamlParser.Parse(new System.IO.StringReader(
                    "<Grid xmlns='" + PresentationNs + "' Margin='4,5,6,7'/>"));
                var margin = ((Microsoft.UI.Xaml.Controls.Grid)doc.RootInstance).Margin;
                Assert.AreEqual(4.0, margin.Left);
                Assert.AreEqual(5.0, margin.Top);
                Assert.AreEqual(6.0, margin.Right);
                Assert.AreEqual(7.0, margin.Bottom);
            });
        }

        [Test]
        public void ParsesBackgroundFromARealDocument()
        {
            WinUITestHost.Run(() => {
                var doc = XamlParser.Parse(new System.IO.StringReader(
                    "<Grid xmlns='" + PresentationNs + "' Background='#FF102030'/>"));
                var brush = ((Microsoft.UI.Xaml.Controls.Grid)doc.RootInstance).Background
                    as Microsoft.UI.Xaml.Media.SolidColorBrush;
                Assert.IsNotNull(brush, "Background did not parse to a SolidColorBrush");
                Assert.AreEqual(0x10, brush.Color.R);
                Assert.AreEqual(0x30, brush.Color.B);
            });
        }

        [Test]
        public void ParsesGridStarSizingFromARealDocument()
        {
            WinUITestHost.Run(() => {
                var doc = XamlParser.Parse(new System.IO.StringReader(
                    "<Grid xmlns='" + PresentationNs + "'><Grid.ColumnDefinitions>" +
                    "<ColumnDefinition Width='2*'/><ColumnDefinition Width='Auto'/>" +
                    "</Grid.ColumnDefinitions></Grid>"));
                var grid = (Microsoft.UI.Xaml.Controls.Grid)doc.RootInstance;
                Assert.AreEqual(GridUnitType.Star, grid.ColumnDefinitions[0].Width.GridUnitType);
                Assert.AreEqual(2.0, grid.ColumnDefinitions[0].Width.Value);
                Assert.AreEqual(GridUnitType.Auto, grid.ColumnDefinitions[1].Width.GridUnitType);
            });
        }

        [Test]
        public void ParsesShapeGeometryFromARealDocument()
        {
            WinUITestHost.Run(() => {
                var polyline = XamlParser.Parse(new System.IO.StringReader(
                    "<Polyline xmlns='" + PresentationNs + "' Points='1,2 3,4 5,6'/>"));
                Assert.AreEqual(3, ((Microsoft.UI.Xaml.Shapes.Polyline)polyline.RootInstance).Points.Count);

                var path = XamlParser.Parse(new System.IO.StringReader(
                    "<Path xmlns='" + PresentationNs + "' Data='M0,0 L10,10 Z'/>"));
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Media.PathGeometry>(
                    ((Microsoft.UI.Xaml.Shapes.Path)path.RootInstance).Data);
            });
        }

        // ---- Guards: the three pre-existing GetCustomTypeConverter entries must still win.
        // Both object and Type report CanConvertFrom(string) == false, so a rule that consulted
        // the host table first would displace them and break conversions that work today.

        [Test]
        public void StillTreatsAnObjectTypedPropertyAsAString()
        {
            WinUITestHost.Run(() => {
                var doc = XamlParser.Parse(new System.IO.StringReader(
                    "<Button xmlns='" + PresentationNs + "' Content='hi'/>"));
                Assert.AreEqual("hi", ((Microsoft.UI.Xaml.Controls.Button)doc.RootInstance).Content);
            });
        }

        [Test]
        public void KeepsThePreExistingCustomConverterEntriesAheadOfTheHostTable()
        {
            // Asserted against GetCustomTypeConverter directly rather than through a document,
            // because this is exactly what the registration changes and the assertion cannot then
            // fail for an unrelated parsing reason.
            Assert.AreSame(XamlNormalPropertyInfo.StringTypeConverter,
                XamlNormalPropertyInfo.GetCustomTypeConverter(typeof(object)));
            Assert.AreEqual("TypeTypeConverter",
                XamlNormalPropertyInfo.GetCustomTypeConverter(typeof(Type)).GetType().Name);
            Assert.AreEqual("DependencyPropertyConverter",
                XamlNormalPropertyInfo.GetCustomTypeConverter(typeof(DependencyProperty)).GetType().Name);
        }

        [Test]
        public void LeavesEnumAndPrimitiveConversionsAlone()
        {
            WinUITestHost.Run(() => {
                var doc = XamlParser.Parse(new System.IO.StringReader(
                    "<Grid xmlns='" + PresentationNs + "' Width='123' HorizontalAlignment='Center'/>"));
                var grid = (Microsoft.UI.Xaml.Controls.Grid)doc.RootInstance;
                Assert.AreEqual(123.0, grid.Width);
                Assert.AreEqual(HorizontalAlignment.Center, grid.HorizontalAlignment);
            });
        }

        [Test]
        public void OpeningARealDocumentStaysWithinItsTimeBudget()
        {
            // Per-attribute conversion costs ~0.06-0.10 ms, roughly 5-9x a single bulk parse of
            // the same file. This pins that the cost stays a fixed overhead rather than growing
            // into something a user would notice on open.
            WinUITestHost.Run(() => {
                var xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
                    TestContext.CurrentContext.TestDirectory, "SampleWindow.xaml"));

                XamlParser.Parse(new System.IO.StringReader(xaml)); // warm up

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                XamlParser.Parse(new System.IO.StringReader(xaml));
                stopwatch.Stop();

                Assert.Less(stopwatch.ElapsedMilliseconds, 250,
                    "Parsing SampleWindow.xaml took " + stopwatch.ElapsedMilliseconds + " ms");
            });
        }
```

- [ ] **Step 2: Add the SampleWindow colour assertion**

`tests/WinUIDesigner.XamlDom.Tests/RoundTripTests.cs` asserts only `IsInstanceOf<SolidColorBrush>` on `Button.Background` (`:61,83`), so it does **not** fail today — the assertion has to be added. `tests/WinUIDesigner.XamlDom.Tests/SampleWindow.xaml:17` declares `<SolidColorBrush x:Key="AccentBrush" Color="Blue" />`.

Find this line in `RoundTripTests.RoundTripsRealWinUIWindowXaml`:

```csharp
				Assert.IsInstanceOf<SolidColorBrush>(((Button)button.Instance).Background,
					"{StaticResource AccentBrush} on Button.Background did not resolve against the ancestor Grid.Resources entry");
```

and add immediately after it:

```csharp
				// The resource's own Color="Blue" is a Windows.UI.Color-typed attribute. Before
				// Phase A's value conversion it parsed to transparent, so the brush resolved but
				// carried no colour - the assertion above passed while the document rendered wrong.
				Assert.AreEqual(Microsoft.UI.Colors.Blue, ((SolidColorBrush)((Button)button.Instance).Background).Color,
					"SolidColorBrush.Color did not parse from the sample document");
```

- [ ] **Step 3: Correct the vacuous arrange test**

`tests/WinUIDesigner.Designer.Tests/ModelToolsArrangeItemsTests.cs:78-103` currently loads one `Button` with `Margin="5,5,0,0"`, arranges a **single** item left, and asserts `margin.Left == 0`. It passes today only because `Margin` never parsed, so the button sat at `0,0,0,0`. Arranging one item is a no-op against its own bounds — the sibling test `ArrangeSingleItemIsANoOpDeltaAgainstItself` (`:65-76`) documents exactly that — so with `Margin` real the correct result is `5`, and asserting `0` is asserting the bug.

Replace the whole test method with a two-item version, which is what the name claims to test:

```csharp
        [Test]
        public void ArrangeInGridSetsMarginLeftWhenNotRightAligned()
        {
            // Two items, so arranging left actually moves one: both align to the leftmost edge
            // (5), which is a no-op for A and a real move for B. With a single item this asserted
            // nothing - and passed only while Margin failed to parse and both edges were 0.
            const string gridXaml =
                "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Margin=\"5,5,0,0\" />" +
                "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" Margin=\"40,5,0,0\" /></Grid>";
            WinUITestHost.RunWithWindow(window => {
                var settings = new XamlLoadSettings();
                settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
                settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new WinUIDesigner.Designer.Services.DefaultSelectionService()));
                var designPanel = new WinUIDesigner.Designer.DesignPanel();
                settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), designPanel));
                using var reader = XmlReader.Create(new StringReader(gridXaml));
                var context = new XamlDesignContext(reader, settings);
                designPanel.Context = context;
                window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
                var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                var aItem = context.Services.Component.GetDesignItem(grid.Children[0]);
                var bItem = context.Services.Component.GetDesignItem(grid.Children[1]);

                // Precondition: the margins the XAML asked for actually reached the elements.
                Assert.AreEqual(5.0, aItem.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Microsoft.UI.Xaml.Thickness>().Left);
                Assert.AreEqual(40.0, bItem.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Microsoft.UI.Xaml.Thickness>().Left);

                WinUIDesigner.Designer.ModelTools.ArrangeItems(new[] { aItem, bItem }, ArrangeDirection.Left);

                Assert.AreEqual(5.0, bItem.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Microsoft.UI.Xaml.Thickness>().Left,
                    "B did not move to the leftmost edge");
                Assert.AreEqual(5.0, aItem.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Microsoft.UI.Xaml.Thickness>().Left,
                    "A, already leftmost, should not have moved");
            });
        }
```

- [ ] **Step 4: Run the new tests to verify they fail**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueConversionTests
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.RoundTripTests
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.ModelToolsArrangeItemsTests
```

Expected failures, and only these:
- `ParsesMarginFromARealDocument` — `Expected: 4.0d But was: 0.0d`
- `ParsesBackgroundFromARealDocument` — `Background did not parse to a SolidColorBrush`
- `ParsesGridStarSizingFromARealDocument` — `Expected: Star But was: Pixel`
- `ParsesShapeGeometryFromARealDocument` — `Expected: 3 But was: 0`
- `RoundTripsRealWinUIWindowXaml` — colour is transparent, not Blue
- `ArrangeInGridSetsMarginLeftWhenNotRightAligned` — fails on the **precondition**, `Expected: 5.0d But was: 0.0d`

The three guard tests (`StillTreatsAnObjectTypedPropertyAsAString`, `KeepsThePreExistingCustomConverterEntriesAheadOfTheHostTable`, `LeavesEnumAndPrimitiveConversionsAlone`) and `OpeningARealDocumentStaysWithinItsTimeBudget` must **pass** here — they pin behaviour that already works and that the next step must not break.

- [ ] **Step 5: Register the converter**

`src/WinUIDesigner.XamlDom/XamlPropertyInfo.cs:344-354` currently reads:

```csharp
		public static TypeConverter GetCustomTypeConverter(Type propertyType)
		{
			if (propertyType == typeof(object))
				return StringTypeConverter;
			else if (propertyType == typeof(Type))
				return TypeTypeConverter.Instance;
			else if (propertyType == typeof(DependencyProperty))
				return DependencyPropertyConverter.Instance;
			else
				return null;
		}
```

Change the final `else` branch only, leaving the three existing entries ahead of it:

```csharp
		public static TypeConverter GetCustomTypeConverter(Type propertyType)
		{
			if (propertyType == typeof(object))
				return StringTypeConverter;
			else if (propertyType == typeof(Type))
				return TypeTypeConverter.Instance;
			else if (propertyType == typeof(DependencyProperty))
				return DependencyPropertyConverter.Instance;
			else
				// Order matters: object and Type both report CanConvertFrom(string) == false, so
				// consulting the host table first would displace the two entries above and break
				// conversions that already work.
				return XamlValueConverter.TryCreate(propertyType);
		}
```

Note the file has no byte-order mark — do not add one.

- [ ] **Step 6: Run all three suites**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: XamlDom 107 tests (103 passed, 4 ignored); DesignModel 75/75; Designer 245/245.

The measured fallout of this flip is exactly one test, and Step 3 already corrected it. **If any other test fails, do not adjust it to match the new behaviour without first establishing which of the two behaviours is correct** — a test that was passing on the un-parsed default is vacuous and should be strengthened; a test that genuinely depended on the old semantics is a finding worth reporting.

Note the full-run output may attribute a failure to a neighbouring fixture name under parallel workers — the earlier measurement saw `ArrangeInGridSetsMarginLeftWhenNotRightAligned`'s failure reported once under `AdornerLayerTests`. Always re-run the named fixture alone before believing an attribution.

- [ ] **Step 7: Correct the four stale workaround comments**

These comment blocks each explain a gap this task just closed. None of their tests turns red — they all avoid the broken path by building object graphs in code — but they are now false documentation, and the next reader would re-learn a limitation that no longer exists. Correct each in place; keep the surrounding test code unchanged.

In `tests/WinUIDesigner.Designer.Tests/PathHandlerExtensionTests.cs`, replace the comment block at `:17-33` (the one beginning `// NOTE: no <Path.Data> here (deliberately, see Load() below).`) with:

```csharp
        // NOTE: no <Path.Data> here. Historically this was a workaround: Windows.Foundation.Point
        // carries no TypeConverterAttribute in this WinUI SDK, so XamlDom's converter lookup fell
        // back to the base TypeConverter, whose ConvertFromString throws - and
        // XamlProperty.UpdateValueOnInstance swallowed it, leaving every Point-typed attribute at
        // (0,0). That gap is closed: XamlValueConverter now converts Point and Geometry through
        // WinUI's own XamlReader (see the XamlDom value-conversion Phase A spec). The fixture
        // still builds its geometry as a CLR object graph and hands it to SetValue, because that
        // is the same typed-object path SaveGeometry itself uses and the test is about the
        // extension, not about parsing.
```

In `tests/WinUIDesigner.Designer.Tests/PolyLineHandlerExtensionTests.cs`, replace the comment block at `:18-30` (beginning `// NOTE: no Points="..." attribute here`) with:

```csharp
        // NOTE: no Points="..." attribute here. Historically this was a workaround:
        // Microsoft.UI.Xaml.Media.PointCollection carries no TypeConverterAttribute in this WinUI
        // SDK (unlike WPF's, which had PointCollectionConverter), so a Points attribute silently
        // left Polyline.Points empty. That gap is closed - XamlValueConverter converts
        // PointCollection through WinUI's own XamlReader - but this fixture keeps building its
        // points in code and handing them to SetValue, the same path PolyLineHandlerExtension
        // itself uses.
```

In `tests/WinUIDesigner.Designer.Tests/PathHandlerExtensionConvertMenuTests.cs`, replace the comment block at `:18-28` (beginning `// NOTE: no <Path.Data> in the XAML, deliberately`) with:

```csharp
        // NOTE: no <Path.Data> in the XAML. Historically this was a workaround for
        // Windows.Foundation.Point-typed attributes silently parsing to (0,0) - see
        // PathHandlerExtensionTests for the full history. That gap is closed by
        // XamlValueConverter; the fixtures still build their PathGeometry as a real CLR object
        // graph and hand it to Properties["Data"].SetValue, the same typed-object path
        // SaveGeometry uses.
```

In `tests/WinUIDesigner.Designer.Tests/Phase4dDefinitionOfDoneTests.cs`, replace the comment block at `:220-232` (beginning `// Corrected from the brief's version`) with:

```csharp
            // Historically the brief's inline XAML used a Points="10,10 50,10 50,50 10,50" string
            // attribute, which hit the PointCollection-has-no-TypeConverterAttribute gap: the
            // conversion failed silently, polyline.Points ended up empty, and
            // extension.PointThumbs[1] threw ArgumentOutOfRangeException before the real
            // assertions ran. That gap is closed by XamlValueConverter. This test still sets
            // Points through the DesignItem property API - the same SetValue(object) path
            // PolyLineHandlerExtension itself uses - BEFORE selection, so
```

- [ ] **Step 8: Re-run all three suites after the comment edits**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: unchanged from Step 6 — comments do not affect behaviour, but a mis-paste can break compilation.

- [ ] **Step 9: Commit**

```bash
git add src/WinUIDesigner.XamlDom/XamlPropertyInfo.cs tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs tests/WinUIDesigner.XamlDom.Tests/RoundTripTests.cs tests/WinUIDesigner.Designer.Tests/ModelToolsArrangeItemsTests.cs tests/WinUIDesigner.Designer.Tests/PathHandlerExtensionTests.cs tests/WinUIDesigner.Designer.Tests/PolyLineHandlerExtensionTests.cs tests/WinUIDesigner.Designer.Tests/PathHandlerExtensionConvertMenuTests.cs tests/WinUIDesigner.Designer.Tests/Phase4dDefinitionOfDoneTests.cs
git commit -m "fix: parse WinRT-typed XAML attribute values instead of silently defaulting them

Registers XamlValueConverter as the fallback in GetCustomTypeConverter, behind
the three existing object/Type/DependencyProperty entries - both object and Type
report CanConvertFrom(string) == false, so consulting the host table first would
displace them.

XamlNormalPropertyInfo.TypeConverter already consults that hook and
XamlParser.FindProperty resolves every normal property to it, so this one line
reaches every attribute in every document: Margin, Background, FontFamily,
GridLength star-sizing, Color, Point, PointCollection and Path.Data now parse.
XamlDependencyPropertyInfo.TypeConverter is deliberately untouched - it is only
ever constructed for attached properties, all of which are int or double today.

Corrects ArrangeInGridSetsMarginLeftWhenNotRightAligned, which arranged a single
item and asserted a margin of 0. It passed only because Margin never parsed and
the button sat at the origin; arranging one item is a no-op against its own
bounds. It now uses two items, so arranging left is a real move, and asserts its
own precondition.

Adds the colour assertion RoundTripTests was missing: it checked only that
Button.Background resolved to a SolidColorBrush, which passed while the brush's
own Color="Blue" parsed to transparent.

Updates four Phase 4d comment blocks that documented the closed gap as current.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Report conversion failures instead of swallowing them

**Files:**
- Modify: `src/WinUIDesigner.XamlDom/XamlProperty.cs:244-271`
- Modify: `src/WinUIDesigner.XamlDom/XamlTextValue.cs`
- Modify: `tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs` (append)

**Interfaces:**
- Consumes: the registered converter from Task 3; `IXamlErrorSink.ReportError(string message, int line, int column)`.
- Produces:
  - `internal System.Xml.IXmlLineInfo XamlTextValue.LineInfo { get; }` — the position of the attribute this value came from, or `null`.
  - No new public API.

**Context:** `XamlProperty.cs:269-271` currently swallows every conversion failure into `Debug.WriteLine`. That is the mechanism that hid this entire defect through four phases. The error sink is already wired for parsing (`XamlParser.cs:97,129,235-237`, `XamlDesignContext.cs:55,75`) and is reachable from `XamlProperty` through `parentObject.OwnerDocument.ServiceProvider` (`XamlDocument.cs:96`). Only `XmlElement` and `XmlAttribute` carry position information — `PositionXmlDocument` overrides `CreateElement` and `CreateAttribute` only — so a text node falls back to its owning element's position.

This task runs **after** the registration, deliberately: turning reporting on before Task 3 would surface every one of today's failures at once, drowning the signal.

- [ ] **Step 1: Write the failing tests**

Append to `tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs`, inside the class:

```csharp
        sealed class CollectingErrorSink : IXamlErrorSink
        {
            public readonly System.Collections.Generic.List<string> Messages = new System.Collections.Generic.List<string>();
            public readonly System.Collections.Generic.List<int> Lines = new System.Collections.Generic.List<int>();

            public void ReportError(string message, int line, int column)
            {
                Messages.Add(message);
                Lines.Add(line);
            }
        }

        sealed class SingleServiceProvider : IServiceProvider
        {
            readonly Type serviceType;
            readonly object service;

            public SingleServiceProvider(Type serviceType, object service)
            {
                this.serviceType = serviceType;
                this.service = service;
            }

            public object GetService(Type type)
            {
                return type == serviceType ? service : null;
            }
        }

        static CollectingErrorSink ParseCollectingErrors(string xaml)
        {
            // XamlParserSettings.ServiceProvider is a plain IServiceProvider, defaulting to a
            // dummy that resolves nothing; XamlParser reads the error sink out of it
            // (XamlParser.cs:97,129). XamlDesignContext does the same thing with its own
            // ServiceContainer (XamlDesignContext.cs:71). Every other setting has a working
            // default (XamlParserSettings.cs:33-35).
            var sink = new CollectingErrorSink();
            var settings = new XamlParserSettings();
            settings.ServiceProvider = new SingleServiceProvider(typeof(IXamlErrorSink), sink);
            XamlParser.Parse(new System.IO.StringReader(xaml), settings);
            return sink;
        }

        [Test]
        public void ReportsAnUnconvertibleAttributeInsteadOfSwallowingIt()
        {
            WinUITestHost.Run(() => {
                var sink = ParseCollectingErrors(
                    "<Grid xmlns='" + PresentationNs + "' Margin='not-a-thickness'/>");
                Assert.AreEqual(1, sink.Messages.Count, "The failure was not reported");
                StringAssert.Contains("not-a-thickness", sink.Messages[0]);
                StringAssert.Contains("Thickness", sink.Messages[0]);
            });
        }

        [Test]
        public void ReportsMarkupExtensionTextInAPropertyElement()
        {
            // The attribute form is routed to MarkupExtensionParser, but the property-element form
            // is not - it arrives as plain text and must be reported, not silently defaulted.
            WinUITestHost.Run(() => {
                var sink = ParseCollectingErrors(
                    "<Border xmlns='" + PresentationNs + "'><Border.Padding>{StaticResource K}</Border.Padding></Border>");
                Assert.AreEqual(1, sink.Messages.Count, "Markup extension text in a property element was not reported");
                StringAssert.Contains("Markup extension", sink.Messages[0]);
            });
        }

        [Test]
        public void ReportsAnEscapedBraceThatHidesALiveExtension()
        {
            // XamlTextValue.Text strips the leading "{}", so this arrives at the converter as a
            // live "{Binding}". Before Phase A it converted to a silent default with no error.
            WinUITestHost.Run(() => {
                var sink = ParseCollectingErrors(
                    "<Border xmlns='" + PresentationNs + "' Padding='{}{Binding}'/>");
                Assert.AreEqual(1, sink.Messages.Count);
                StringAssert.Contains("Markup extension", sink.Messages[0]);
            });
        }

        [Test]
        public void DoesNotReportAResourceKeyResolvedFromTheDesignerOwnApplicationResources()
        {
            // An isolated fragment resolves {StaticResource} against Application.Current.Resources -
            // the designer's own scope, not the document's - so a key that exists only there must
            // be refused rather than silently used.
            WinUITestHost.Run(() => {
                Application.Current.Resources["PhaseAProbeThickness"] = new Thickness(9);
                try {
                    var sink = ParseCollectingErrors(
                        "<Border xmlns='" + PresentationNs + "'><Border.Padding>{StaticResource PhaseAProbeThickness}</Border.Padding></Border>");
                    Assert.AreEqual(1, sink.Messages.Count, "A designer-scoped resource key was silently used");
                    StringAssert.Contains("Markup extension", sink.Messages[0]);
                } finally {
                    Application.Current.Resources.Remove("PhaseAProbeThickness");
                }
            });
        }

        [Test]
        public void ReportsEachFailureWithItsOwnMessage()
        {
            // WinUI caches the first XamlParseException's message for the life of the process and
            // repeats it for every later failure, across intervening successes. The reported text
            // must be self-authored, never forwarded from the platform.
            WinUITestHost.Run(() => {
                ParseCollectingErrors("<Grid xmlns='" + PresentationNs + "' Margin='first-bad-value'/>");
                ParseCollectingErrors("<Grid xmlns='" + PresentationNs + "' Width='120'/>");
                var sink = ParseCollectingErrors(
                    "<Border xmlns='" + PresentationNs + "' CornerRadius='second-bad-value'/>");

                Assert.AreEqual(1, sink.Messages.Count);
                StringAssert.Contains("second-bad-value", sink.Messages[0]);
                StringAssert.DoesNotContain("first-bad-value", sink.Messages[0]);
                StringAssert.Contains("CornerRadius", sink.Messages[0]);
                StringAssert.DoesNotContain("Thickness", sink.Messages[0]);
            });
        }

        [Test]
        public void ReportsTheAttributeOwnPositionNotTheSyntheticFragmentPosition()
        {
            WinUITestHost.Run(() => {
                var sink = ParseCollectingErrors(
                    "<Grid xmlns='" + PresentationNs + "'>\r\n  <Border Padding='not-a-thickness'/>\r\n</Grid>");
                Assert.AreEqual(1, sink.Messages.Count);
                Assert.AreEqual(2, sink.Lines[0], "The reported line is not the attribute's own line");
            });
        }

        [Test]
        public void ReportsNothingForADocumentThatConvertsCleanly()
        {
            WinUITestHost.Run(() => {
                var sink = ParseCollectingErrors(
                    "<Grid xmlns='" + PresentationNs + "' Margin='4,5,6,7' Background='Red' Width='120'/>");
                Assert.AreEqual(0, sink.Messages.Count,
                    "A clean document reported: " + string.Join(" | ", sink.Messages));
            });
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueConversionTests
```

Expected: the six reporting tests fail with `Expected: 1 But was: 0` — the failures are still going to `Debug.WriteLine`. `ReportsNothingForADocumentThatConvertsCleanly` passes already, which is correct: it guards against the next step over-reporting.

- [ ] **Step 3: Expose the text value's position**

In `src/WinUIDesigner.XamlDom/XamlTextValue.cs` (tabs), add this member to the class, after the `Text` property:

```csharp
		/// <summary>
		/// Position information for the node this value came from, or <c>null</c> when none is
		/// available.
		/// </summary>
		/// <remarks>
		/// <see cref="PositionXmlDocument"/> overrides only <c>CreateElement</c> and
		/// <c>CreateAttribute</c>, so an attribute carries its own line and column but a text node
		/// does not. A text value therefore falls back to its owning element's position, which is
		/// still far better than the coordinates of a synthetic conversion fragment.
		/// </remarks>
		internal System.Xml.IXmlLineInfo LineInfo {
			get {
				var attributeLineInfo = attribute as System.Xml.IXmlLineInfo;
				if (attributeLineInfo != null)
					return attributeLineInfo;
				if (ParentProperty != null && ParentProperty.ParentObject != null)
					return ParentProperty.ParentObject.XmlElement as System.Xml.IXmlLineInfo;
				return null;
			}
		}
```

- [ ] **Step 4: Report instead of swallowing**

In `src/WinUIDesigner.XamlDom/XamlProperty.cs`, replace the `catch` block at `:269-271`:

```csharp
				catch (Exception ex) {
					Debug.WriteLine("UpdateValueOnInstance() failed - Exception:" + ex.Message);
				}
```

with:

```csharp
				catch (Exception ex) {
					ReportValueFailure(ex);
				}
```

and add these two members to the class, immediately after `UpdateValueOnInstance`:

```csharp
		/// <summary>
		/// Reports a failure to apply this property's value to the live instance.
		/// </summary>
		/// <remarks>
		/// This used to be a bare <c>Debug.WriteLine</c>, and that is how the XamlDom
		/// value-conversion gap stayed invisible for four phases: every WinRT-typed attribute
		/// failed to convert and the property silently kept its default. The failure is reported
		/// rather than rethrown on purpose - throwing here would make a document with one bad
		/// attribute impossible to open at all, which is the moment the user most needs to open it.
		/// </remarks>
		void ReportValueFailure(Exception ex)
		{
			Debug.WriteLine("UpdateValueOnInstance() failed - Exception:" + ex.Message);

			var serviceProvider = parentObject != null && parentObject.OwnerDocument != null
				? parentObject.OwnerDocument.ServiceProvider
				: null;
			if (serviceProvider == null)
				return;

			var errorSink = serviceProvider.GetService(typeof(IXamlErrorSink)) as IXamlErrorSink;
			if (errorSink == null)
				return;

			var lineInfo = GetValueLineInfo();
			var message = "Cannot set " + propertyInfo.Name + ": " + ex.Message;
			if (lineInfo != null && lineInfo.HasLineInfo())
				errorSink.ReportError(message, lineInfo.LineNumber, lineInfo.LinePosition);
			else
				errorSink.ReportError(message, 0, 0);
		}

		System.Xml.IXmlLineInfo GetValueLineInfo()
		{
			var textValue = propertyValue as XamlTextValue;
			if (textValue != null)
				return textValue.LineInfo;
			return parentObject != null ? parentObject.XmlElement as System.Xml.IXmlLineInfo : null;
		}
```

The exception message already carries the value and the target type — `XamlValueConverter` composes `Cannot convert '<text>' to <TypeName>.` and never forwards the platform's own message, which WinUI poisons with the first failure in the process.

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueConversionTests
```

Expected: 44 passed, 0 failed.

- [ ] **Step 6: Run all three suites**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: XamlDom 114 tests (110 passed, 4 ignored); DesignModel 75/75; Designer 245/245.

A test that now fails because a previously-silent failure became a reported error is a real finding, not noise: it means that document was rendering wrong. Investigate before changing anything.

- [ ] **Step 7: Commit**

```bash
git add src/WinUIDesigner.XamlDom/XamlProperty.cs src/WinUIDesigner.XamlDom/XamlTextValue.cs tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs
git commit -m "fix: report XAML value-conversion failures through IXamlErrorSink

XamlProperty.UpdateValueOnInstance swallowed every failure into a
Debug.WriteLine. That is how the value-conversion gap stayed invisible for four
phases: each WinRT-typed attribute failed to convert and the property silently
kept its default, so a document rendered wrong with no indication anywhere.

Failures now reach the IXamlErrorSink that is already wired for parsing, with a
self-authored message and the offending attribute's own line and column. The
platform's XamlParseException message is never forwarded: WinUI caches the first
failure's message for the life of the process and repeats it for every later
one, across intervening successes.

Reported rather than rethrown deliberately - throwing would make a document with
one bad attribute impossible to open, which is when the user most needs it.

Adds XamlTextValue.LineInfo, falling back to the owning element's position
because PositionXmlDocument gives coordinates to elements and attributes but not
to text nodes.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Definition of Done

Phase A is complete when all of the following hold on one tree:

- [ ] `dotnet build WinUIDesigner.sln` reports 0 errors.
- [ ] XamlDom suite: 114 tests, 110 passed, 4 pre-existing ignores.
- [ ] DesignModel suite: 75/75.
- [ ] Designer suite: 245/245.
- [ ] All three run from their `.exe` runners with `--noresult`. No result relied on `dotnet test`.
- [ ] `git status --porcelain` is empty.

Spec DoD coverage, item by item:

| Spec DoD item | Where |
|---|---|
| 1. One test per host-table entry | Task 2, Step 1 |
| 2. Host-viability self-check | Task 1 (`IsHostUsable`), tested in Task 1 Step 2 and Task 2 Step 1 |
| 3. Grammar variants | Task 2, Step 1 |
| 4. Pre-existing converter entries still win | Task 3, Step 1 |
| 5. Markup-extension rejection | Task 1 Step 2 (converter refuses), Task 4 Step 1 (refusal is reported) |
| 6. Error-message hygiene | Task 4, Step 1 |
| 7. `Nullable<T>` | Task 1, Step 2 |
| 8. `ImageSource`/`Uri` exclusions | Task 1, Step 2 |
| 9. Assignability | Task 1, Step 2 |
| 10. Culture | Task 1, Step 2 |
| 11. `SampleWindow.xaml` regression | Task 3, Step 2 |
| 12. Vacuous arrange test corrected | Task 3, Step 3 |
| 13. All three suites green from the `.exe` runners | every task's final steps |
| 14. Performance budget | Task 3, Step 1 (`OpeningARealDocumentStaysWithinItsTimeBudget`) |

Out of scope for Phase A, and expected to remain broken: setting a WinRT-typed property through the model still serializes to an empty element on save. That is Phase B, whose shape is recorded at the end of the spec.
