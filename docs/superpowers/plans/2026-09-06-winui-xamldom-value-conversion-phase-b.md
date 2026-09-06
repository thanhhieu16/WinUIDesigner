# XamlDom Value Conversion, Phase B (write side) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `XamlDocument.CreatePropertyValue` serialize WinRT-typed property values as XAML attribute text instead of empty `XmlElement`s, so that a `Path`, `Polyline` or `Polygon` built by the designer survives a save and reload.

**Architecture:** Phase A already converts attribute text into WinRT values through a sixteen-entry host table (`XamlValueConverter`). Phase B adds the other direction: a formatter table keyed by the same sixteen types, a mini-language writer for `PathGeometry`, and a `CanConvertTo` that answers about the *instance* when the caller supplies one and about the *type* when it does not. The write path never throws — an instance the formatter cannot handle answers `false` and falls through to today's element form, plus an error-sink report.

**Tech Stack:** C# on `net10.0-windows10.0.19041.0`, WinUI 3 (`Microsoft.WindowsAppSDK 1.7.250606001`), NUnit 3 via NUnitLite, run from each test project's own `.exe`.

**Spec:** `docs/superpowers/specs/2026-09-06-winui-xamldom-value-conversion-phase-b-design.md`

## Global Constraints

- Target framework `net10.0-windows10.0.19041.0`, `UseWinUI=true`, `Nullable` `disable`, `RuntimeIdentifier` `win-x64`.
- `Microsoft.WindowsAppSDK` is pinned to `1.7.250606001`. Never bump it.
- Build with `dotnet build WinUIDesigner.sln`.
- **Tests MUST run via each test project's built `.exe` with `--noresult`, NEVER `dotnet test`.** `dotnet test` cannot activate `Microsoft.UI.Xaml` WinRT classes and silently reports every WinUI-dependent test as *ignored*, which looks like a pass.
  - `tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult`
  - `tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult`
  - `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult`
  - Single test: append `--test=Full.Namespace.Fixture.Method`.
- Every WinUI object must be created inside `WinUITestHost.Run(...)` or `RunWithWindow(...)`. A WinRT class activated off that thread throws `COMException`.
- Project dependency direction `Designer` → `DesignModel` → `XamlDom` must not be violated. `WinUIDesigner.XamlDom.Tests` cannot reference the Designer layer.
- Files ported from SharpDevelop keep their original MIT copyright header. **The two new files in this plan are not ported code and must not carry one.**
- `src/` contains zero `.xaml` files.
- `src/WinUIDesigner.XamlDom/*.cs` is indented with **tabs**. `tests/WinUIDesigner.XamlDom.Tests/*.cs` is indented with **4 spaces**. Follow the file you are in.
- Baseline to hold: XamlDom 116 tests (112 passed, 0 failed, 4 pre-existing ignores), DesignModel 75/75, Designer 245/245. No test may move from passing to ignored.

### NUnit misattribution in this test host — read before believing a failure

Under eight parallel workers, a failure's **header name can name a different test than its stack trace**, and the first failure group is numbered `1-1)`, `1-2)`, which inflates naive counts. Only the stack trace identifies the real test. Before reporting an attribution, re-run that fixture alone with `--test=`.

`XamlParseException`'s message is also poisoned and sticky: WinUI caches the first failure's message for the process's lifetime and repeats it for every later failure, across intervening successes. Never assert on it.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/WinUIDesigner.XamlDom/GeometryMiniLanguageWriter.cs` (new) | Turns a `PathGeometry` into path mini-language, and decides whether it can. The only entry that is an algorithm rather than a format string. |
| `src/WinUIDesigner.XamlDom/XamlValueFormatter.cs` (new) | The sixteen-entry format table: `HasFormatter(Type)`, `CanFormat(Type, object)`, `Format(Type, object)`. Delegates geometry to the writer above. |
| `src/WinUIDesigner.XamlDom/XamlValueConverter.cs` (modify) | `CanConvertTo` gains the two-answer rule; `ConvertTo` delegates to the formatter. |
| `src/WinUIDesigner.XamlDom/XamlDocument.cs` (modify) | Resolves the write-side converter from the declared property type; exposes `ColorBrushDictionary` to the formatter; reports unformattable values; drops the throwing `TypeDescriptor.GetConverter` overload. |
| `tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs` (new) | Every formatter round trip, the geometry writer, the culture sweep, the serialization regressions. |
| `tests/WinUIDesigner.DesignModel.Tests/PropertyGridStringConverterTests.cs` (new) | The one test that needs `PropertyNode`, which lives in the DesignModel layer. |

---

## Task 1: The geometry mini-language writer

**Files:**
- Create: `src/WinUIDesigner.XamlDom/GeometryMiniLanguageWriter.cs`
- Test: `tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs` (created here)

**Interfaces:**
- Consumes: `XamlValueConverter.TryCreate(Type)` from Phase A, for the round-trip assertions only.
- Produces: `internal static class GeometryMiniLanguageWriter` with
  `public static bool CanWrite(Microsoft.UI.Xaml.Media.Geometry geometry)` and
  `public static string Write(Microsoft.UI.Xaml.Media.PathGeometry geometry)`.
  Task 2 calls exactly these two.

**Why this is its own task:** it is the only formatter with branching logic, it is testable with no wiring at all, and a reviewer can reject it on its own merits.

**Measured facts you must not re-derive:**
- `PathFigure.IsFilled` defaults to `True`, `IsClosed` to `False`, `Segments` is non-null on a fresh figure. `PathGeometry.Figures` is non-null, `FillRule` defaults to `EvenOdd`, `Transform` defaults to `null`. So the geometry `DrawPathExtension` builds passes `CanWrite` unchanged.
- WinUI's parser **expands coordinate runs**: `L20,5 30,10` reads back as two `LineSegment`s, not one `PolyLineSegment`. Emit one command per resulting segment or the text will not be stable across saves.

- [ ] **Step 1: Write the failing test**

Create `tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs` (4 spaces):

```csharp
using System;
using System.Globalization;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using Windows.Foundation;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.XamlDom.Tests
{
    /// <summary>
    /// Phase B of the XamlDom value-conversion fix: turning a WinRT-typed value back into the
    /// XAML attribute text that Phase A's converter can read.
    /// </summary>
    /// <remarks>
    /// Every test runs on the WinUI dispatcher thread (see <see cref="WinUITestHost"/>) because
    /// every object involved is a thread-affine WinRT object.
    /// </remarks>
    [TestFixture]
    public class ValueFormattingTests
    {
        /// <summary>A figure carrying one of every segment type this codebase can produce.</summary>
        static PathGeometry BuildAllSegmentGeometry()
        {
            var figure = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
            figure.Segments.Add(new LineSegment { Point = new Point(10, 0) });

            var polyLine = new PointCollection();
            polyLine.Add(new Point(20, 5));
            polyLine.Add(new Point(30, 10));
            figure.Segments.Add(new PolyLineSegment { Points = polyLine });

            figure.Segments.Add(new BezierSegment {
                Point1 = new Point(40, 0), Point2 = new Point(50, 20), Point3 = new Point(60, 10)
            });

            var polyBezier = new PointCollection();
            polyBezier.Add(new Point(70, 0));
            polyBezier.Add(new Point(80, 20));
            polyBezier.Add(new Point(90, 10));
            figure.Segments.Add(new PolyBezierSegment { Points = polyBezier });

            figure.Segments.Add(new QuadraticBezierSegment {
                Point1 = new Point(100, 0), Point2 = new Point(110, 10)
            });

            var polyQuad = new PointCollection();
            polyQuad.Add(new Point(120, 0));
            polyQuad.Add(new Point(130, 10));
            figure.Segments.Add(new PolyQuadraticBezierSegment { Points = polyQuad });

            figure.Segments.Add(new ArcSegment {
                Point = new Point(140, 10), Size = new Size(5, 7.5), RotationAngle = 30,
                IsLargeArc = true, SweepDirection = SweepDirection.Clockwise
            });

            var geometry = new PathGeometry { FillRule = FillRule.Nonzero };
            geometry.Figures.Add(figure);
            return geometry;
        }

        [Test]
        public void WritesEverySegmentTypeInTheCanonicalForm()
        {
            WinUITestHost.Run(() => {
                var geometry = BuildAllSegmentGeometry();
                Assert.IsTrue(GeometryMiniLanguageWriter.CanWrite(geometry));
                Assert.AreEqual(
                    "F1 M0,0 L10,0 L20,5 L30,10 C40,0 50,20 60,10 C70,0 80,20 90,10 " +
                    "Q100,0 110,10 Q120,0 130,10 A5,7.5 30 1 1 140,10 Z",
                    GeometryMiniLanguageWriter.Write(geometry));
            });
        }

        [Test]
        public void TheWrittenGeometryTextIsAFixedPoint()
        {
            WinUITestHost.Run(() => {
                // WinUI's parser expands coordinate runs into individual segments, so the written
                // text must survive being read back and rewritten unchanged - otherwise every save
                // rewrites the file even when nothing was edited.
                var text = GeometryMiniLanguageWriter.Write(BuildAllSegmentGeometry());
                var reloaded = (PathGeometry)XamlValueConverter.TryCreate(typeof(Geometry))
                    .ConvertFrom(null, CultureInfo.InvariantCulture, text);
                Assert.AreEqual(text, GeometryMiniLanguageWriter.Write(reloaded));
            });
        }

        [Test]
        public void WritesTheEvenOddFillRuleAndOpenFigures()
        {
            WinUITestHost.Run(() => {
                var figure = new PathFigure { StartPoint = new Point(1.5, 2.5) };
                figure.Segments.Add(new LineSegment { Point = new Point(3, 4) });
                var geometry = new PathGeometry();      // FillRule defaults to EvenOdd
                geometry.Figures.Add(figure);
                Assert.AreEqual("F0 M1.5,2.5 L3,4", GeometryMiniLanguageWriter.Write(geometry));
            });
        }

        [Test]
        public void WritesEveryFigureOfAMultiFigureGeometry()
        {
            WinUITestHost.Run(() => {
                var geometry = new PathGeometry();
                var first = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
                first.Segments.Add(new LineSegment { Point = new Point(10, 0) });
                var second = new PathFigure { StartPoint = new Point(20, 20) };
                second.Segments.Add(new LineSegment { Point = new Point(30, 20) });
                geometry.Figures.Add(first);
                geometry.Figures.Add(second);
                Assert.AreEqual("F0 M0,0 L10,0 Z M20,20 L30,20",
                    GeometryMiniLanguageWriter.Write(geometry));
            });
        }

        [Test]
        public void RefusesGeometryTheMiniLanguageCannotExpress()
        {
            WinUITestHost.Run(() => {
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(new RectangleGeometry()),
                    "RectangleGeometry has no mini-language form and must not be rewritten as a path");
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(new EllipseGeometry()));
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(new LineGeometry()));
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(new GeometryGroup()));

                var transformed = BuildAllSegmentGeometry();
                transformed.Transform = new TranslateTransform { X = 5 };
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(transformed),
                    "A Transform cannot be expressed in the mini-language");

                var unfilled = new PathGeometry();
                unfilled.Figures.Add(new PathFigure { StartPoint = new Point(0, 0), IsFilled = false });
                Assert.IsFalse(GeometryMiniLanguageWriter.CanWrite(unfilled),
                    "IsFilled=false has no mini-language form");
            });
        }
    }
}
```

- [ ] **Step 2: Run the tests and capture the red run**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueFormattingTests
```

Expected: the **build fails** with `CS0103: The name 'GeometryMiniLanguageWriter' does not exist in the current context`. That compile error is this task's red run — paste it into the report. Do not proceed until you have seen it.

- [ ] **Step 3: Create the writer**

Create `src/WinUIDesigner.XamlDom/GeometryMiniLanguageWriter.cs` — **tabs**, no SharpDevelop header:

```csharp
using System;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Writes a <see cref="PathGeometry"/> as XAML path mini-language, the form
	/// <c>Path.Data</c> takes as an attribute.
	/// </summary>
	/// <remarks>
	/// <para>
	/// WinUI has no <c>XamlWriter</c>, so there is nothing to fall back to: the mini-language is
	/// written by hand here. Only <see cref="PathGeometry"/> is written.
	/// <c>RectangleGeometry</c>, <c>EllipseGeometry</c>, <c>LineGeometry</c> and
	/// <c>GeometryGroup</c> have no mini-language form, and rewriting one into an equivalent path
	/// string would change the type recorded in the user's document - fixing a data-loss bug by
	/// mutating data.
	/// </para>
	/// <para>
	/// One command is emitted per resulting segment, never a run of coordinates. WinUI's parser
	/// expands a run: <c>L20,5 30,10</c> reads back as two <c>LineSegment</c>s rather than one
	/// <c>PolyLineSegment</c>, so the run form would produce text that changes on the next save
	/// even when nothing was edited. Emitting one command per segment makes this writer a fixed
	/// point - measured over a figure carrying all seven segment types.
	/// </para>
	/// </remarks>
	internal static class GeometryMiniLanguageWriter
	{
		/// <summary>
		/// Gets whether <paramref name="geometry"/> has a faithful mini-language form.
		/// </summary>
		public static bool CanWrite(Geometry geometry)
		{
			var path = geometry as PathGeometry;
			if (path == null || path.Transform != null)
				return false;

			foreach (var figure in path.Figures) {
				// IsFilled=false has no mini-language representation, so writing such a figure
				// would silently turn it into a filled one.
				if (!figure.IsFilled)
					return false;

				foreach (var segment in figure.Segments) {
					if (!(segment is LineSegment || segment is PolyLineSegment ||
					      segment is BezierSegment || segment is PolyBezierSegment ||
					      segment is QuadraticBezierSegment || segment is PolyQuadraticBezierSegment ||
					      segment is ArcSegment))
						// Refusing the whole geometry beats dropping one segment: a silently
						// shortened path is exactly the failure mode this phase exists to remove.
						return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Writes <paramref name="geometry"/> as mini-language. Only call this when
		/// <see cref="CanWrite"/> returned <c>true</c> for it.
		/// </summary>
		public static string Write(PathGeometry geometry)
		{
			if (geometry == null)
				throw new ArgumentNullException("geometry");

			var text = new StringBuilder();
			text.Append(geometry.FillRule == FillRule.Nonzero ? "F1" : "F0");

			foreach (var figure in geometry.Figures) {
				text.Append(" M").Append(Pt(figure.StartPoint));

				foreach (var segment in figure.Segments) {
					var line = segment as LineSegment;
					if (line != null) {
						text.Append(" L").Append(Pt(line.Point));
						continue;
					}

					var polyLine = segment as PolyLineSegment;
					if (polyLine != null) {
						foreach (var point in polyLine.Points)
							text.Append(" L").Append(Pt(point));
						continue;
					}

					var bezier = segment as BezierSegment;
					if (bezier != null) {
						text.Append(" C").Append(Pt(bezier.Point1))
						    .Append(' ').Append(Pt(bezier.Point2))
						    .Append(' ').Append(Pt(bezier.Point3));
						continue;
					}

					var polyBezier = segment as PolyBezierSegment;
					if (polyBezier != null) {
						for (int i = 0; i + 2 < polyBezier.Points.Count; i += 3)
							text.Append(" C").Append(Pt(polyBezier.Points[i]))
							    .Append(' ').Append(Pt(polyBezier.Points[i + 1]))
							    .Append(' ').Append(Pt(polyBezier.Points[i + 2]));
						continue;
					}

					var quadratic = segment as QuadraticBezierSegment;
					if (quadratic != null) {
						text.Append(" Q").Append(Pt(quadratic.Point1))
						    .Append(' ').Append(Pt(quadratic.Point2));
						continue;
					}

					var polyQuadratic = segment as PolyQuadraticBezierSegment;
					if (polyQuadratic != null) {
						for (int i = 0; i + 1 < polyQuadratic.Points.Count; i += 2)
							text.Append(" Q").Append(Pt(polyQuadratic.Points[i]))
							    .Append(' ').Append(Pt(polyQuadratic.Points[i + 1]));
						continue;
					}

					var arc = segment as ArcSegment;
					if (arc != null) {
						text.Append(" A").Append(Num(arc.Size.Width)).Append(',').Append(Num(arc.Size.Height))
						    .Append(' ').Append(Num(arc.RotationAngle))
						    .Append(' ').Append(arc.IsLargeArc ? "1" : "0")
						    .Append(' ').Append(arc.SweepDirection == SweepDirection.Clockwise ? "1" : "0")
						    .Append(' ').Append(Pt(arc.Point));
						continue;
					}

					throw new NotSupportedException(
						"CanWrite admitted an unsupported segment type: " + segment.GetType().FullName);
				}

				if (figure.IsClosed)
					text.Append(" Z");
			}

			return text.ToString();
		}

		internal static string Num(double value)
		{
			// "R" round-trips the double exactly, and the invariant culture keeps the decimal
			// separator a period under every user locale. Measured: under de-DE this still yields
			// "2.5", while Point.ToString() yields "1,5;2,5".
			return value.ToString("R", CultureInfo.InvariantCulture);
		}

		internal static string Pt(Point point)
		{
			return Num(point.X) + "," + Num(point.Y);
		}
	}
}
```

- [ ] **Step 4: Run the tests**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueFormattingTests
```

Expected: 5 tests, all passing.

- [ ] **Step 5: Run the whole XamlDom suite**

```
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
```

Expected: 121 tests, 117 passed, 0 failed, 4 ignored. Nothing is wired yet, so nothing else can move.

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.XamlDom/GeometryMiniLanguageWriter.cs tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs
git commit -m "feat: write PathGeometry as XAML path mini-language"
```

---

## Task 2: The formatter table

**Files:**
- Create: `src/WinUIDesigner.XamlDom/XamlValueFormatter.cs`
- Modify: `src/WinUIDesigner.XamlDom/XamlDocument.cs:48` (make `ColorBrushDictionary` internal)
- Test: `tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs`

**Interfaces:**
- Consumes: `GeometryMiniLanguageWriter.CanWrite(Geometry)` and `.Write(PathGeometry)` from Task 1; `XamlValueConverter.TryCreate(Type)` from Phase A for round-trip assertions.
- Produces: `internal static class XamlValueFormatter` with
  `public static bool HasFormatter(Type targetType)`,
  `public static bool CanFormat(Type targetType, object value)` and
  `public static string Format(Type targetType, object value)`.
  Task 3 calls exactly these three. `targetType` is always the **declared property type** — the same key Phase A's host table uses — never `value.GetType()`.

**Measured values you must not re-derive.** Every string below was produced by these expressions and read back through Phase A's converter:

| Value | Text |
|---|---|
| `Thickness(1, 2.5, 3, 4)` | `1,2.5,3,4` |
| `CornerRadius(1, 2, 3, 4)` | `1,2,3,4` |
| `GridLength(1, GridUnitType.Auto)` | `Auto` |
| `GridLength(40)` | `40` |
| `GridLength(2, GridUnitType.Star)` | `2*` |
| `Colors.Blue` | `#FF0000FF` |
| `FontWeights.Bold` | `Bold` |
| `Point(1.5, 2.5)` | `1.5,2.5` |
| `PointCollection` of `(1,2)`, `(3.5,4)` | `1,2 3.5,4` |
| `DoubleCollection` of `1`, `2.5`, `3` | `1,2.5,3` |
| `Rect(1, 2, 3.5, 4)` | `1,2,3.5,4` |
| `Size(3.5, 4)` | `3.5,4` |
| `Duration(TimeSpan.FromSeconds(1))` | `00:00:01` |
| `Duration.Forever` / `Duration.Automatic` | `Forever` / `Automatic` |
| `Matrix(1, 2, 3, 4, 5.5, 6)` | `1,2,3,4,5.5,6` |
| `Vector3(1, 2.5f, 3)` | `1,2.5,3` |
| `SolidColorBrush(Colors.Blue)` | `Blue` |

The eleven named weights on `Microsoft.UI.Text.FontWeights`: `Thin=100 ExtraLight=200 Light=300 SemiLight=350 Normal=400 Medium=500 SemiBold=600 Bold=700 ExtraBold=800 Black=900 ExtraBlack=950`.

**The trap in this task.** `FontFamily`, `PointCollection`, `DoubleCollection`, `SolidColorBrush` and `PathGeometry` **do not implement value equality** — measured, a correct round trip still returns `Equals == false` for all five. Assert on their content (`Source`, the point sequence, the double sequence, `Color`, the written text). A test written the obvious way fails against a correct implementation.

- [ ] **Step 1: Write the failing tests**

Append to `tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs`, inside the fixture:

```csharp
        static string Format(Type declaredType, object value)
        {
            Assert.IsTrue(XamlValueFormatter.HasFormatter(declaredType),
                "No formatter for " + declaredType.FullName);
            Assert.IsTrue(XamlValueFormatter.CanFormat(declaredType, value),
                "The formatter refused a value it should accept: " + value);
            return XamlValueFormatter.Format(declaredType, value);
        }

        static object ReadBack(Type declaredType, string text)
        {
            return XamlValueConverter.TryCreate(declaredType)
                .ConvertFrom(null, CultureInfo.InvariantCulture, text);
        }

        [Test]
        public void FormatsEveryValueTypeInTheTableAndReadsItBackEqual()
        {
            WinUITestHost.Run(() => {
                var cases = new object[,] {
                    { typeof(Microsoft.UI.Xaml.Thickness), new Microsoft.UI.Xaml.Thickness(1, 2.5, 3, 4), "1,2.5,3,4" },
                    { typeof(Microsoft.UI.Xaml.CornerRadius), new Microsoft.UI.Xaml.CornerRadius(1, 2, 3, 4), "1,2,3,4" },
                    { typeof(Microsoft.UI.Xaml.GridLength), new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Auto), "Auto" },
                    { typeof(Microsoft.UI.Xaml.GridLength), new Microsoft.UI.Xaml.GridLength(40), "40" },
                    { typeof(Microsoft.UI.Xaml.GridLength), new Microsoft.UI.Xaml.GridLength(2, Microsoft.UI.Xaml.GridUnitType.Star), "2*" },
                    { typeof(Windows.UI.Color), Microsoft.UI.Colors.Blue, "#FF0000FF" },
                    { typeof(Windows.UI.Text.FontWeight), Microsoft.UI.Text.FontWeights.Bold, "Bold" },
                    { typeof(Point), new Point(1.5, 2.5), "1.5,2.5" },
                    { typeof(Rect), new Rect(1, 2, 3.5, 4), "1,2,3.5,4" },
                    { typeof(Size), new Size(3.5, 4), "3.5,4" },
                    { typeof(Microsoft.UI.Xaml.Duration), new Microsoft.UI.Xaml.Duration(TimeSpan.FromSeconds(1)), "00:00:01" },
                    { typeof(Microsoft.UI.Xaml.Duration), Microsoft.UI.Xaml.Duration.Forever, "Forever" },
                    { typeof(Microsoft.UI.Xaml.Duration), Microsoft.UI.Xaml.Duration.Automatic, "Automatic" },
                    { typeof(Matrix), new Matrix(1, 2, 3, 4, 5.5, 6), "1,2,3,4,5.5,6" },
                    { typeof(System.Numerics.Vector3), new System.Numerics.Vector3(1, 2.5f, 3), "1,2.5,3" },
                };

                for (int i = 0; i < cases.GetLength(0); i++) {
                    var declared = (Type)cases[i, 0];
                    var value = cases[i, 1];
                    var expected = (string)cases[i, 2];
                    var text = Format(declared, value);
                    Assert.AreEqual(expected, text, "Wrong text for " + declared.Name + " " + value);
                    Assert.AreEqual(value, ReadBack(declared, text),
                        "Round trip changed the value for " + declared.Name);
                }
            });
        }

        [Test]
        public void FormatsEveryReferenceTypeInTheTableAndReadsItBackWithTheSameContent()
        {
            WinUITestHost.Run(() => {
                // None of these five implements value equality, so a correct round trip still
                // compares unequal under Equals. Assert on content.
                var family = new FontFamily("Segoe UI");
                Assert.AreEqual("Segoe UI", Format(typeof(FontFamily), family));
                Assert.AreEqual("Segoe UI", ((FontFamily)ReadBack(typeof(FontFamily), "Segoe UI")).Source);

                var points = new PointCollection();
                points.Add(new Point(1, 2));
                points.Add(new Point(3.5, 4));
                Assert.AreEqual("1,2 3.5,4", Format(typeof(PointCollection), points));
                var readPoints = (PointCollection)ReadBack(typeof(PointCollection), "1,2 3.5,4");
                Assert.AreEqual(2, readPoints.Count);
                Assert.AreEqual(3.5, readPoints[1].X);

                var doubles = new DoubleCollection();
                doubles.Add(1);
                doubles.Add(2.5);
                doubles.Add(3);
                Assert.AreEqual("1,2.5,3", Format(typeof(DoubleCollection), doubles));
                var readDoubles = (DoubleCollection)ReadBack(typeof(DoubleCollection), "1,2.5,3");
                Assert.AreEqual(3, readDoubles.Count);
                Assert.AreEqual(2.5, readDoubles[1]);

                var brush = new SolidColorBrush(Microsoft.UI.Colors.Blue);
                Assert.AreEqual("Blue", Format(typeof(Brush), brush),
                    "A named colour must stay named, as XamlDocument's own dictionary emits it");
                Assert.AreEqual(Microsoft.UI.Colors.Blue,
                    ((SolidColorBrush)ReadBack(typeof(Brush), "Blue")).Color);

                var unnamed = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 1, 2, 3));
                Assert.AreEqual("#FF010203", Format(typeof(Brush), unnamed));

                var geometry = BuildAllSegmentGeometry();
                Assert.AreEqual(GeometryMiniLanguageWriter.Write(geometry),
                    Format(typeof(Geometry), geometry));
            });
        }

        [Test]
        public void FormatsTheSameUnderACommaDecimalCulture()
        {
            WinUITestHost.Run(() => {
                var previous = CultureInfo.CurrentCulture;
                try {
                    CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                    // Point.ToString() yields '1,5;2,5' under this culture, which is why no
                    // formatter is allowed to reach ToString().
                    Assert.AreEqual("1.5,2.5", Format(typeof(Point), new Point(1.5, 2.5)));
                    Assert.AreEqual("1,2.5,3,4",
                        Format(typeof(Microsoft.UI.Xaml.Thickness), new Microsoft.UI.Xaml.Thickness(1, 2.5, 3, 4)));
                    Assert.AreEqual("1,2,3.5,4", Format(typeof(Rect), new Rect(1, 2, 3.5, 4)));
                    Assert.AreEqual("00:00:01",
                        Format(typeof(Microsoft.UI.Xaml.Duration), new Microsoft.UI.Xaml.Duration(TimeSpan.FromSeconds(1))));
                    Assert.AreEqual("1,2.5,3",
                        Format(typeof(System.Numerics.Vector3), new System.Numerics.Vector3(1, 2.5f, 3)));

                    var doubles = new DoubleCollection();
                    doubles.Add(2.5);
                    Assert.AreEqual("2.5", Format(typeof(DoubleCollection), doubles));
                } finally {
                    CultureInfo.CurrentCulture = previous;
                }
            });
        }

        [Test]
        public void RefusesInstancesItCannotFormatFaithfully()
        {
            WinUITestHost.Run(() => {
                Assert.IsTrue(XamlValueFormatter.HasFormatter(typeof(Brush)),
                    "The type-level answer stays true so the PropertyGrid text box is enabled");
                Assert.IsFalse(XamlValueFormatter.CanFormat(typeof(Brush), new LinearGradientBrush()),
                    "A gradient brush has no attribute form");
                Assert.IsFalse(XamlValueFormatter.CanFormat(typeof(Geometry), new RectangleGeometry()));
                Assert.IsFalse(XamlValueFormatter.CanFormat(typeof(FontFamily), new FontFamily("")),
                    "An empty font family would read back as a different thing");
                Assert.IsFalse(
                    XamlValueFormatter.CanFormat(typeof(Windows.UI.Text.FontWeight),
                        new Windows.UI.Text.FontWeight { Weight = 137 }),
                    "137 has no name, and Phase A cannot read the numeric form back");
                Assert.IsFalse(XamlValueFormatter.CanFormat(typeof(Point), null),
                    "A null value has no text form");
            });
        }

        [Test]
        public void HasNoFormatterForTypesOutsideTheTable()
        {
            WinUITestHost.Run(() => {
                Assert.IsFalse(XamlValueFormatter.HasFormatter(typeof(object)));
                Assert.IsFalse(XamlValueFormatter.HasFormatter(typeof(System.Numerics.Vector2)),
                    "Vector2 has no host entry either, so it could not be round-trip tested");
                Assert.IsFalse(XamlValueFormatter.HasFormatter(typeof(Microsoft.UI.Xaml.Controls.Button)));
            });
        }
```

Add `using Microsoft.UI.Text;` is **not** needed — the tests above spell out `Microsoft.UI.Text.FontWeights` in full.

- [ ] **Step 2: Run the tests and capture the red run**

```
dotnet build WinUIDesigner.sln
```

Expected: `CS0103: The name 'XamlValueFormatter' does not exist in the current context`. Paste it into the report.

- [ ] **Step 3: Make the colour dictionary visible to the formatter**

In `src/WinUIDesigner.XamlDom/XamlDocument.cs:48`, change the accessibility of the existing property only. Before:

```csharp
		static Dictionary<Windows.UI.Color, string> ColorBrushDictionary
```

After:

```csharp
		internal static Dictionary<Windows.UI.Color, string> ColorBrushDictionary
```

Change nothing else about it: the lazy build, the reflection over `Microsoft.UI.Colors` and the `try`/`catch` fallback all stay exactly as they are.

- [ ] **Step 4: Create the formatter**

Create `src/WinUIDesigner.XamlDom/XamlValueFormatter.cs` — **tabs**, no SharpDevelop header:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Turns a WinRT-typed property value back into the XAML attribute text that
	/// <see cref="XamlValueConverter"/> reads.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The table is keyed by the same sixteen declared property types as
	/// <see cref="XamlValueConverter"/>'s host table, so no type gains a write path without a read
	/// path to verify it against. The key is always the DECLARED property type, never
	/// <c>value.GetType()</c>: the table matches exactly, and the instance under a
	/// <c>Path.Data</c> property is a <c>PathGeometry</c> while the entry is <c>Geometry</c>.
	/// </para>
	/// <para>
	/// Nothing here reaches <c>ToString()</c>. <c>TypeConverter.ConvertTo</c> ignores the culture
	/// it is handed and calls <c>value.ToString()</c>, so <c>ConvertToInvariantString</c> does not
	/// make a culture-dependent <c>ToString()</c> safe - measured, <c>Point.ToString()</c> yields
	/// <c>'1,5;2,5'</c> under <c>de-DE</c>.
	/// </para>
	/// </remarks>
	internal static class XamlValueFormatter
	{
		delegate bool Predicate(object value);
		delegate string Writer(object value);

		sealed class Entry
		{
			public readonly Predicate CanFormat;
			public readonly Writer Format;

			public Entry(Predicate canFormat, Writer format)
			{
				this.CanFormat = canFormat;
				this.Format = format;
			}
		}

		static readonly Dictionary<Type, Entry> Table = BuildTable();

		static Dictionary<Type, Entry> BuildTable()
		{
			var table = new Dictionary<Type, Entry>();

			Add(table, typeof(Thickness), v => v is Thickness, v => {
				var t = (Thickness)v;
				return Num(t.Left) + "," + Num(t.Top) + "," + Num(t.Right) + "," + Num(t.Bottom);
			});

			Add(table, typeof(CornerRadius), v => v is CornerRadius, v => {
				var c = (CornerRadius)v;
				return Num(c.TopLeft) + "," + Num(c.TopRight) + "," + Num(c.BottomRight) + "," + Num(c.BottomLeft);
			});

			Add(table, typeof(GridLength), v => v is GridLength, v => {
				var g = (GridLength)v;
				if (g.IsAuto)
					return "Auto";
				if (g.IsStar)
					return g.Value == 1.0 ? "*" : Num(g.Value) + "*";
				return Num(g.Value);
			});

			Add(table, typeof(Windows.UI.Color), v => v is Windows.UI.Color,
				v => Hex((Windows.UI.Color)v));

			// Only SolidColorBrush has an attribute form. The named-colour output reproduces what
			// XamlDocument.cs:287-290 already emits, which RoundTripTests asserts.
			Add(table, typeof(Brush), v => v is SolidColorBrush, v => {
				var color = ((SolidColorBrush)v).Color;
				string name;
				return XamlDocument.ColorBrushDictionary.TryGetValue(color, out name) ? name : Hex(color);
			});

			Add(table, typeof(FontFamily),
				v => v is FontFamily && !string.IsNullOrEmpty(((FontFamily)v).Source),
				v => ((FontFamily)v).Source);

			Add(table, typeof(Windows.UI.Text.FontWeight),
				v => v is Windows.UI.Text.FontWeight && FontWeightName(((Windows.UI.Text.FontWeight)v).Weight) != null,
				v => FontWeightName(((Windows.UI.Text.FontWeight)v).Weight));

			Add(table, typeof(Point), v => v is Point, v => Pt((Point)v));

			Add(table, typeof(PointCollection), v => v is PointCollection, v => {
				var text = new StringBuilder();
				foreach (var point in (PointCollection)v) {
					if (text.Length > 0)
						text.Append(' ');
					text.Append(Pt(point));
				}
				return text.ToString();
			});

			Add(table, typeof(DoubleCollection), v => v is DoubleCollection, v => {
				var text = new StringBuilder();
				foreach (var value in (DoubleCollection)v) {
					if (text.Length > 0)
						text.Append(',');
					text.Append(Num(value));
				}
				return text.ToString();
			});

			Add(table, typeof(Geometry),
				v => v is Geometry && GeometryMiniLanguageWriter.CanWrite((Geometry)v),
				v => GeometryMiniLanguageWriter.Write((PathGeometry)v));

			Add(table, typeof(Rect), v => v is Rect, v => {
				var r = (Rect)v;
				return Num(r.X) + "," + Num(r.Y) + "," + Num(r.Width) + "," + Num(r.Height);
			});

			Add(table, typeof(Size), v => v is Size, v => {
				var s = (Size)v;
				return Num(s.Width) + "," + Num(s.Height);
			});

			Add(table, typeof(Duration), v => v is Duration, v => {
				var d = (Duration)v;
				if (d.HasTimeSpan)
					return d.TimeSpan.ToString("c", CultureInfo.InvariantCulture);
				return d == Duration.Forever ? "Forever" : "Automatic";
			});

			Add(table, typeof(Matrix), v => v is Matrix, v => {
				var m = (Matrix)v;
				return Num(m.M11) + "," + Num(m.M12) + "," + Num(m.M21) + "," + Num(m.M22) + "," +
				       Num(m.OffsetX) + "," + Num(m.OffsetY);
			});

			Add(table, typeof(System.Numerics.Vector3), v => v is System.Numerics.Vector3, v => {
				var vec = (System.Numerics.Vector3)v;
				return Num(vec.X) + "," + Num(vec.Y) + "," + Num(vec.Z);
			});

			return table;
		}

		static void Add(Dictionary<Type, Entry> table, Type targetType, Predicate canFormat, Writer format)
		{
			table[targetType] = new Entry(canFormat, format);
		}

		/// <summary>
		/// Gets whether values of <paramref name="targetType"/> have a text form at all. This is
		/// the answer for a caller that has no instance to ask about - the PropertyGrid, through
		/// the one-argument <c>CanConvertTo</c> overload.
		/// </summary>
		public static bool HasFormatter(Type targetType)
		{
			return targetType != null && Table.ContainsKey(targetType);
		}

		/// <summary>
		/// Gets whether this particular <paramref name="value"/> has a faithful text form.
		/// </summary>
		public static bool CanFormat(Type targetType, object value)
		{
			Entry entry;
			if (targetType == null || value == null || !Table.TryGetValue(targetType, out entry))
				return false;
			return entry.CanFormat(value);
		}

		/// <summary>
		/// Writes <paramref name="value"/> as XAML attribute text. Only call this when
		/// <see cref="CanFormat"/> returned <c>true</c> for the same pair.
		/// </summary>
		public static string Format(Type targetType, object value)
		{
			Entry entry;
			if (targetType == null || !Table.TryGetValue(targetType, out entry))
				throw new NotSupportedException("No formatter for " + (targetType == null ? "null" : targetType.FullName));
			if (value == null || !entry.CanFormat(value))
				throw new NotSupportedException(
					"Values of this shape have no text form: " + (value == null ? "null" : value.GetType().FullName));
			return entry.Format(value);
		}

		#region Primitives

		static string Num(double value)
		{
			return value.ToString("R", CultureInfo.InvariantCulture);
		}

		static string Num(float value)
		{
			return value.ToString("R", CultureInfo.InvariantCulture);
		}

		static string Pt(Point point)
		{
			return Num(point.X) + "," + Num(point.Y);
		}

		static string Hex(Windows.UI.Color color)
		{
			return "#" + color.A.ToString("X2", CultureInfo.InvariantCulture)
			           + color.R.ToString("X2", CultureInfo.InvariantCulture)
			           + color.G.ToString("X2", CultureInfo.InvariantCulture)
			           + color.B.ToString("X2", CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Maps a numeric weight to the XAML keyword for it, or <c>null</c> when there is none.
		/// The numeric form is not written: Phase A cannot read it back.
		/// </summary>
		static string FontWeightName(ushort weight)
		{
			switch (weight) {
				case 100: return "Thin";
				case 200: return "ExtraLight";
				case 300: return "Light";
				case 350: return "SemiLight";
				case 400: return "Normal";
				case 500: return "Medium";
				case 600: return "SemiBold";
				case 700: return "Bold";
				case 800: return "ExtraBold";
				case 900: return "Black";
				case 950: return "ExtraBlack";
				default: return null;
			}
		}

		#endregion
	}
}
```

- [ ] **Step 5: Run the tests**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueFormattingTests
```

Expected: 10 tests, all passing.

- [ ] **Step 6: Run all three suites**

```
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: XamlDom 126 tests (122 passed, 4 ignored), DesignModel 75/75, Designer 245/245. The formatter is still unwired, so behaviour cannot have changed.

- [ ] **Step 7: Commit**

```bash
git add src/WinUIDesigner.XamlDom/XamlValueFormatter.cs src/WinUIDesigner.XamlDom/XamlDocument.cs tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs
git commit -m "feat: add the sixteen-entry XAML value formatter table"
```

---

## Task 3: Wire the write side into serialization

**Files:**
- Modify: `src/WinUIDesigner.XamlDom/XamlValueConverter.cs:128-145` (`CanConvertTo`, plus a new `ConvertTo`)
- Modify: `src/WinUIDesigner.XamlDom/XamlDocument.cs:281`
- Test: `tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs`

**Interfaces:**
- Consumes: `XamlValueFormatter.HasFormatter/CanFormat/Format` from Task 2.
- Produces: nothing new by name. After this task, `XamlProperty.PropertyValue = document.CreatePropertyValue(value, property)` writes attribute text for the sixteen types.

**This is the task where behaviour changes.** Three rules govern it:

1. **Resolve from the declared property type, and only through `XamlValueConverter.TryCreate`.** Not through `GetCustomTypeConverter`: measured, that returns `StringConverter` for a property declared `object`, whose `ConvertToInvariantString(new Button())` is the literal string `'Microsoft.UI.Xaml.Controls.Button'`. Routing `ContentControl.Content` through it would replace whole subtrees with a type name.
2. **Gate on `forProperty != null`.** `CreatePropertyValue` is also `CreateObject`'s body, called from `MarkupExtensionParser.cs:403` for every `{Binding}` at parse time and from `XamlComponentService.cs:110` for every new control. Without the gate a document containing a markup extension becomes unopenable.
3. **Never throw.** `PlacementOperation` opens its `ChangeGroup` at `PlacementOperation.cs:114` and never calls `Abort()` around `SetPlacement`, and there is no `Application.UnhandledException` handler in `src/`. A throw from a `Margin` write kills the process. The design achieves this structurally: `ConvertTo` is only reached for an instance `CanConvertTo` already approved.

- [ ] **Step 1: Write the failing tests**

Append to `tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs`, inside the fixture:

```csharp
        const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        static XamlDocument ParseDocument(string xaml)
        {
            return XamlParser.Parse(new System.IO.StringReader(xaml));
        }

        static string SaveToString(XamlDocument document)
        {
            var text = new System.IO.StringWriter();
            var settings = new System.Xml.XmlWriterSettings();
            settings.OmitXmlDeclaration = true;
            using (var writer = System.Xml.XmlWriter.Create(text, settings))
                document.Save(writer);
            return text.ToString();
        }

        /// <summary>Sets a property the way XamlModelProperty.cs:239 sets it.</summary>
        static void SetThroughTheDocument(XamlDocument document, string propertyName, object value)
        {
            var property = document.RootElement.FindOrCreateProperty(propertyName);
            property.PropertyValue = document.CreatePropertyValue(value, property);
        }

        [Test]
        public void SavesAGeometryAsAttributeTextInsteadOfAnEmptyElement()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Path xmlns='" + PresentationNs + "'/>");
                var geometry = BuildAllSegmentGeometry();
                SetThroughTheDocument(document, "Data", geometry);

                var saved = SaveToString(document);
                StringAssert.DoesNotContain("<PathGeometry", saved,
                    "The geometry was still serialized as an empty element");
                StringAssert.Contains("A5,7.5 30 1 1 140,10", saved);

                var reloaded = ParseDocument(saved);
                var reloadedGeometry = reloaded.RootElement.FindOrCreateProperty("Data").ValueOnInstance as PathGeometry;
                Assert.IsNotNull(reloadedGeometry, "The saved document did not reload a PathGeometry");
                Assert.AreEqual(GeometryMiniLanguageWriter.Write(geometry),
                    GeometryMiniLanguageWriter.Write(reloadedGeometry));
            });
        }

        [Test]
        public void SavesAPointCollectionAsAttributeTextInsteadOfAnEmptyElement()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Polyline xmlns='" + PresentationNs + "'/>");
                var points = new PointCollection();
                points.Add(new Point(10, 10));
                points.Add(new Point(50, 10));
                points.Add(new Point(50, 40));
                SetThroughTheDocument(document, "Points", points);

                var saved = SaveToString(document);
                StringAssert.Contains("10,10 50,10 50,40", saved);

                var reloaded = ParseDocument(saved);
                var reloadedPoints = reloaded.RootElement.FindOrCreateProperty("Points").ValueOnInstance as PointCollection;
                Assert.IsNotNull(reloadedPoints);
                Assert.AreEqual(3, reloadedPoints.Count);
                Assert.AreEqual(50.0, reloadedPoints[2].X);
                Assert.AreEqual(40.0, reloadedPoints[2].Y);
            });
        }

        [Test]
        public void SavesAThicknessAsAttributeText()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Margin", new Microsoft.UI.Xaml.Thickness(1, 2.5, 3, 4));
                StringAssert.Contains("Margin=\"1,2.5,3,4\"", SaveToString(document));
            });
        }

        [Test]
        public void KeepsSerializingANestedControlAsAnElement()
        {
            WinUITestHost.Run(() => {
                // ContentControl.Content is declared `object`. Resolving through
                // GetCustomTypeConverter would hand back StringConverter, whose
                // ConvertToInvariantString(new Button()) is the literal text
                // 'Microsoft.UI.Xaml.Controls.Button' - the whole subtree, replaced by a type name.
                var document = ParseDocument("<Button xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Content", new Microsoft.UI.Xaml.Controls.TextBlock());

                var saved = SaveToString(document);
                StringAssert.Contains("<TextBlock", saved, "The nested control was not serialized as an element");
                StringAssert.DoesNotContain("Microsoft.UI.Xaml.Controls.TextBlock", saved,
                    "The nested control was serialized as its type name");
            });
        }

        [Test]
        public void StillParsesADocumentContainingMarkupExtensions()
        {
            WinUITestHost.Run(() => {
                // CreatePropertyValue is also CreateObject's body, and MarkupExtensionParser.cs:403
                // runs it for every markup extension at parse time.
                var document = ParseDocument(
                    "<Grid xmlns='" + PresentationNs + "' " +
                    "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                    "Tag='{Binding Path=Whatever}'/>");
                Assert.IsNotNull(document.RootElement);
            });
        }

        [Test]
        public void StillSavesANamedColourByName()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Background", new SolidColorBrush(Microsoft.UI.Colors.Blue));
                var saved = SaveToString(document);
                StringAssert.Contains("Background=\"Blue\"", saved);
            });
        }

        [Test]
        public void LeavesAnUnformattableInstanceOnTheElementPath()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Background", new LinearGradientBrush());
                var saved = SaveToString(document);
                StringAssert.Contains("<LinearGradientBrush", saved,
                    "A gradient brush has no attribute form and must keep the element form");
            });
        }
```

- [ ] **Step 2: Run the tests and capture the red run**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueFormattingTests
```

Expected: 3 failures — `SavesAGeometryAsAttributeTextInsteadOfAnEmptyElement`, `SavesAPointCollectionAsAttributeTextInsteadOfAnEmptyElement` and `SavesAThicknessAsAttributeText`. The other four already pass, because they assert behaviour this task must **preserve**. Paste the failure text into the report; remember that only the stack trace identifies a test.

- [ ] **Step 3: Teach the converter to write**

In `src/WinUIDesigner.XamlDom/XamlValueConverter.cs`, replace the whole `CanConvertTo` member — its remark block at `:128-139` and the method at `:140-145` — with:

```csharp
		/// <remarks>
		/// Two callers ask this question and they need different answers.
		/// <c>XamlDocument.CreatePropertyValue</c> assigns <c>ctx.Instance</c> before asking
		/// (<c>XamlDocument.cs:283</c>) and wants to know whether THAT value has a text form;
		/// <c>PropertyNode</c> uses the one-argument overload, which passes a null context
		/// (measured), and wants to know whether the TYPE has one at all, because that is what
		/// gates its editable text box (<c>PropertyNode.cs:231-233</c>, <c>:108-118</c>).
		/// Answering per-instance is also what keeps the write path from throwing: an instance the
		/// formatter cannot handle is refused here, so <see cref="ConvertTo"/> is never reached
		/// for it and serialization simply falls through to the element form.
		/// </remarks>
		public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
		{
			if (destinationType == typeof(string)) {
				var instance = context != null ? context.Instance : null;
				return instance == null
					? XamlValueFormatter.HasFormatter(targetType)
					: XamlValueFormatter.CanFormat(targetType, instance);
			}
			return base.CanConvertTo(context, destinationType);
		}

		public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture,
		                                 object value, Type destinationType)
		{
			if (destinationType == typeof(string) && XamlValueFormatter.CanFormat(targetType, value))
				return XamlValueFormatter.Format(targetType, value);
			return base.ConvertTo(context, culture, value, destinationType);
		}
```

- [ ] **Step 4: Resolve the write-side converter from the declared type**

In `src/WinUIDesigner.XamlDom/XamlDocument.cs`, replace line 281. Before:

```csharp
			TypeConverter c = TypeDescriptor.GetConverter(instance);
```

After:

```csharp
			// The converter is resolved from the DECLARED property type, not from the instance:
			// the host and format tables match exactly, so a Path.Data value resolves under
			// Geometry while its instance is a PathGeometry. Resolving through
			// GetCustomTypeConverter instead would be wrong in a different way - measured, it
			// returns StringConverter for a property declared `object`, and that converter turns a
			// nested Button into the literal text 'Microsoft.UI.Xaml.Controls.Button'.
			// The null forProperty case is CreateObject's, which must keep its current behaviour:
			// MarkupExtensionParser.cs:403 runs it for every {Binding} at parse time.
			//
			// TypeDescriptor's Type overload is used rather than its instance overload: measured,
			// the instance overload throws FileNotFoundException for System.Windows.Forms on its
			// first call in a process and succeeds on every call afterwards, while the Type
			// overload never throws.
			TypeConverter c = (forProperty != null ? XamlValueConverter.TryCreate(forProperty.ReturnType) : null)
				?? TypeDescriptor.GetConverter(elementType);
```

`elementType` is already in scope, assigned on the line above (`XamlDocument.cs:280`).

- [ ] **Step 5: Run the tests**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueFormattingTests
```

Expected: 17 tests, all passing.

- [ ] **Step 6: Run all three suites**

```
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: XamlDom 133 tests (129 passed, 4 ignored), DesignModel 75/75, Designer 245/245.

**If a Designer or DesignModel test fails here, do not "fix" it by weakening it.** This task changes what the designer writes; a failure means either a real regression or a test that asserted the empty-element form as correct. Read the failing assertion, decide which it is, and report it in your report file either way.

- [ ] **Step 7: Commit**

```bash
git add src/WinUIDesigner.XamlDom/XamlValueConverter.cs src/WinUIDesigner.XamlDom/XamlDocument.cs tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs
git commit -m "fix: serialize WinRT-typed property values as XAML attribute text"
```

---

## Task 4: Report the values that still cannot be written

**Files:**
- Modify: `src/WinUIDesigner.XamlDom/XamlDocument.cs` (inside `CreatePropertyValue`, after the `hasStringConverter` branch)
- Test: `tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs`
- Create: `tests/WinUIDesigner.DesignModel.Tests/PropertyGridStringConverterTests.cs`

**Interfaces:**
- Consumes: `XamlValueConverter.TryCreate` and `XamlValueFormatter.HasFormatter` from Tasks 2-3; `IXamlErrorSink.ReportError(string message, int line, int column)`.
- Produces: nothing new by name.

**Why this is its own task:** it changes a shared code path in a way that is visible to every consumer of the error sink, and it carries the one test that lives in another test project.

- [ ] **Step 1: Write the failing tests**

Append to `tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs`, inside the fixture:

```csharp
        sealed class CollectingErrorSink : IXamlErrorSink
        {
            public readonly System.Collections.Generic.List<string> Messages =
                new System.Collections.Generic.List<string>();

            public void ReportError(string message, int line, int column)
            {
                Messages.Add(message);
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

        static CollectingErrorSink SetCollectingErrors(string xaml, string propertyName, object value)
        {
            var sink = new CollectingErrorSink();
            var settings = new XamlParserSettings();
            settings.ServiceProvider = new SingleServiceProvider(typeof(IXamlErrorSink), sink);
            var document = XamlParser.Parse(new System.IO.StringReader(xaml), settings);
            var property = document.RootElement.FindOrCreateProperty(propertyName);
            property.PropertyValue = document.CreatePropertyValue(value, property);
            return sink;
        }

        [Test]
        public void ReportsAValueItCannotWriteAsAttributeText()
        {
            WinUITestHost.Run(() => {
                var sink = SetCollectingErrors(
                    "<Grid xmlns='" + PresentationNs + "'/>", "Background", new LinearGradientBrush());
                Assert.AreEqual(1, sink.Messages.Count, "The unwritable value was not reported");
                StringAssert.Contains("Background", sink.Messages[0]);
                StringAssert.Contains("LinearGradientBrush", sink.Messages[0]);
            });
        }

        [Test]
        public void ReportsAGeometryTheMiniLanguageCannotExpress()
        {
            WinUITestHost.Run(() => {
                var sink = SetCollectingErrors(
                    "<Path xmlns='" + PresentationNs + "'/>", "Data", new RectangleGeometry());
                Assert.AreEqual(1, sink.Messages.Count);
                StringAssert.Contains("RectangleGeometry", sink.Messages[0]);
            });
        }

        [Test]
        public void DoesNotReportAValueItWroteSuccessfully()
        {
            WinUITestHost.Run(() => {
                var sink = SetCollectingErrors(
                    "<Grid xmlns='" + PresentationNs + "'/>", "Margin",
                    new Microsoft.UI.Xaml.Thickness(1, 2, 3, 4));
                Assert.AreEqual(0, sink.Messages.Count,
                    "A value that serialized fine must not report anything");
            });
        }

        [Test]
        public void DoesNotReportAValueThatWasNeverInTheTable()
        {
            WinUITestHost.Run(() => {
                // A nested control has always been serialized as an element and always will be.
                // Reporting it would blame a document that is not at fault.
                var sink = SetCollectingErrors(
                    "<Button xmlns='" + PresentationNs + "'/>", "Content",
                    new Microsoft.UI.Xaml.Controls.TextBlock());
                Assert.AreEqual(0, sink.Messages.Count);
            });
        }
```

Create `tests/WinUIDesigner.DesignModel.Tests/PropertyGridStringConverterTests.cs` (4 spaces):

```csharp
using System.IO;
using System.Xml;
using Microsoft.UI.Xaml;
using NUnit.Framework;
using WinUIDesigner.DesignModel.PropertyGrid;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    /// <summary>
    /// PropertyNode gates its editable text box on a converter reporting BOTH directions
    /// (PropertyNode.cs:231-233), so Phase B's write side is what turns the box on for the
    /// sixteen WinRT types.
    /// </summary>
    [TestFixture]
    public class PropertyGridStringConverterTests
    {
        [Test]
        public void AThicknessPropertyIsEditableInTheGrid()
        {
            WinUITestHost.Run(() => {
                using var reader = XmlReader.Create(new StringReader(
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />"));
                var context = new XamlDesignContext(reader, new XamlLoadSettings());
                var margin = context.RootItem.Properties["Margin"];

                var node = new PropertyNode();
                node.Load(new[] { margin });
                Assert.IsTrue(node.IsEnabled, "The Thickness text box stayed disabled");

                node.ValueString = "1,2,3,4";
                var written = (Thickness)margin.ValueOnInstance;
                Assert.AreEqual(1.0, written.Left);
                Assert.AreEqual(4.0, written.Bottom);
                Assert.AreEqual("1,2,3,4", node.ValueString);
            });
        }
    }
}
```

The three names this file leans on were checked against the tree while the plan was written:
`PropertyNode.Load(DesignItemProperty[] properties)` (`PropertyNode.cs:210`),
`new XamlDesignContext(reader, new XamlLoadSettings())` (the form every existing DesignModel test
uses, e.g. `DesignItemPropertyTests.cs:23`), and `DesignItemProperty.ValueOnInstance`
(`DesignItemProperty.cs:45`, get-only — this test only reads it).

- [ ] **Step 2: Run the tests and capture the red run**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueFormattingTests
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult --test=WinUIDesigner.DesignModel.Tests.PropertyGridStringConverterTests
```

Expected: `ReportsAValueItCannotWriteAsAttributeText` and `ReportsAGeometryTheMiniLanguageCannotExpress` fail with `Expected: 1 But was: 0`. The two "does not report" tests already pass. The DesignModel test passes already if Task 3 landed correctly — if it fails, that is a real finding about Task 3, not about this task; report it.

- [ ] **Step 3: Raise the report**

In `src/WinUIDesigner.XamlDom/XamlDocument.cs`, immediately after the closing brace of the `if (forProperty != null && hasStringConverter) { ... }` block (which ends at `:292`) and before the `string ns = GetNamespaceFor(elementType);` line, insert:

```csharp
			// A value whose type HAS a text form but whose instance does not - a gradient brush
			// under a Brush property, a RectangleGeometry under Path.Data - is about to be written
			// as an element, which is the shape that does not survive a reload. Phase A taught
			// that failing silently here is how this class of bug lives for four phases, so say
			// so. Throwing is not an option: PlacementOperation never calls Abort() around
			// SetPlacement (PlacementOperation.cs:114), so a throw from a Margin write strands the
			// transaction and, with no Application.UnhandledException handler in src/, ends the
			// process.
			if (forProperty != null && !hasStringConverter &&
			    XamlValueFormatter.HasFormatter(forProperty.ReturnType)) {
				var sink = ServiceProvider == null
					? null
					: ServiceProvider.GetService(typeof(IXamlErrorSink)) as IXamlErrorSink;
				if (sink != null)
					sink.ReportError(
						"Cannot save " + forProperty.PropertyName + " as attribute text: " +
						elementType.FullName + " has no XAML attribute form, so it is written as an " +
						"element and will not survive a reload.", 0, 0);
			}
```

`XamlProperty.PropertyName` (`XamlProperty.cs:107`) and `XamlDocument.ServiceProvider`
(`XamlDocument.cs:96`, public) were both checked against the tree while the plan was written.

- [ ] **Step 4: Run the tests**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ValueFormattingTests
```

Expected: 21 tests, all passing.

- [ ] **Step 5: Run all three suites**

```
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: XamlDom 137 tests (133 passed, 4 ignored), DesignModel 76/76, Designer 245/245.

- [ ] **Step 6: Correct the memory of the gap**

`docs/superpowers/specs/2026-09-06-winui-xamldom-value-conversion-design.md` describes the write side as out of scope and Phase B as unwritten. Add one line under its "Out of scope" heading pointing at this phase's spec, so a reader of the Phase A spec is not left believing the gap is still open:

```markdown
- **The write side.** Closed by Phase B —
  `docs/superpowers/specs/2026-09-06-winui-xamldom-value-conversion-phase-b-design.md`.
```

Replace only the first sentence of the existing "The write side" bullet; leave the rest of the file alone.

- [ ] **Step 7: Commit**

```bash
git add src/WinUIDesigner.XamlDom/XamlDocument.cs tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs tests/WinUIDesigner.DesignModel.Tests/PropertyGridStringConverterTests.cs docs/superpowers/specs/2026-09-06-winui-xamldom-value-conversion-design.md
git commit -m "fix: report property values that cannot be saved as attribute text"
```

---

## Plan self-review

**Spec coverage.** Decisions 1, 2, 5 and 11 land in Task 3 Step 4; Decisions 3 and 4 in Task 3 Step 3; Decisions 6, 7, 9, 10 and 14 in Task 2 Step 4; Decision 8 in Task 1 Step 3; Decision 12 in Task 4 Step 3; Decision 13 in Task 4's DesignModel test. Definition-of-done items 1-3 are Task 2's tests, 4-5 Task 1's, 6 is split (the predicate half in Task 2, the reporting half in Task 4), 7-10 are Task 3's, 11 is Task 4's DesignModel test, 12 is every task's final step.

**Type consistency.** `GeometryMiniLanguageWriter.CanWrite(Geometry)`/`.Write(PathGeometry)` are declared in Task 1 and called with those exact signatures in Task 2's table and Task 1's tests. `XamlValueFormatter.HasFormatter(Type)`, `.CanFormat(Type, object)` and `.Format(Type, object)` are declared in Task 2 and called with those exact signatures in Tasks 3 and 4. `XamlDocument.ColorBrushDictionary` becomes internal in Task 2 Step 3 and is read in Task 2 Step 4.

**Every name in this plan was read out of the tree or compiled during design**, including the ones an earlier draft left for the implementer to check: `PropertyNode.Load(DesignItemProperty[])` at `PropertyNode.cs:210`, the `XamlDesignContext(reader, settings)` constructor used by `DesignItemPropertyTests.cs:23`, `DesignItemProperty.ValueOnInstance` at `DesignItemProperty.cs:45`, `XamlProperty.PropertyName` at `XamlProperty.cs:107`, and the public `XamlDocument.ServiceProvider` at `XamlDocument.cs:96`. The formatter expressions in Task 2 and the writer in Task 1 were both executed against live WinUI objects and round-tripped through Phase A's converter before being written down.

**Vacuous-test check.** Task 3's seven tests split into three that must go from red to green and four that assert preserved behaviour and pass throughout — the plan says which are which and requires the red run to show exactly three failures. Task 4's four tests split two and two the same way. Task 1's and Task 2's tests cannot pass before their task, because the type they name does not compile.
