# XamlDom Element-Form Serialization (Phase C) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `XamlDocument.CreatePropertyValue` write a WinRT value's own properties into the element it emits, so a skewed control, a gradient brush and a non-path geometry survive a save and reload instead of becoming `<SkewTransform />`, `<LinearGradientBrush />` and `<RectangleGeometry />`.

**Architecture:** A new `XamlInstanceWriter` holds an allow-list of types. For a listed type it walks the properties that are writable and backed by a `DependencyProperty`, writing each set scalar through the public `XamlObject.FindOrCreateProperty` + `PropertyValue` path and each populated collection through the parser-side `ParserAddCollectionElement` path. Recursion goes back through `CreatePropertyValue`, so leaves reuse Phase B's formatter and nested listed objects come back to the writer. Nothing throws: a refusal leaves the element exactly as it is today.

**Tech Stack:** C# on `net10.0-windows10.0.19041.0`, WinUI 3 (`Microsoft.WindowsAppSDK 1.7.250606001`), NUnit 3 via NUnitLite, run from each test project's own `.exe`.

**Spec:** `docs/superpowers/specs/2026-09-07-xamldom-element-serialization-design.md`

## Global Constraints

- Target framework `net10.0-windows10.0.19041.0`, `UseWinUI=true`, `Nullable` `disable`, `RuntimeIdentifier` `win-x64`.
- `Microsoft.WindowsAppSDK` is pinned to `1.7.250606001`. Never bump it.
- Build with `dotnet build WinUIDesigner.sln`.
- **Tests MUST run via each test project's built `.exe` with `--noresult`, NEVER `dotnet test`.** `dotnet test`'s `testhost.exe` cannot activate `Microsoft.UI.Xaml` WinRT classes and silently reports every WinUI-dependent test as *ignored*, which looks like a pass.
  - `tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult`
  - `tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult`
  - `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult`
  - `tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult`
  - Single test: append `--test=Full.Namespace.Fixture.Method`.
- Every WinUI object must be created inside `WinUITestHost.Run(...)` or `RunWithWindow(...)`. A WinRT class activated off that thread throws `COMException`.
- Project dependency direction `Shell` → `Designer` → `DesignModel` → `XamlDom` must not be violated. `WinUIDesigner.XamlDom.Tests` cannot reference the Designer layer.
- Files ported from SharpDevelop keep their original MIT copyright header. **`XamlInstanceWriter.cs` is not ported code and must not carry one.**
- `src/` contains exactly one `.xaml` file, `src/WinUIDesigner.Shell.App/App.xaml`. Do not add another.
- `src/WinUIDesigner.XamlDom/*.cs` is indented with **tabs**. `tests/WinUIDesigner.XamlDom.Tests/*.cs` and `tests/WinUIDesigner.Designer.Tests/*.cs` are indented with **4 spaces**. Follow the file you are in.
- **The write path must never throw.** `PlacementOperation` never calls `Abort()` around `SetPlacement` (`PlacementOperation.cs:114`) and `src/` installs no `Application.UnhandledException` handler, so a throw from a write during a drag strands the transaction and ends the process. A refusal means returning `false`, never an exception.
- Baseline to hold: **XamlDom 144 (140 passed, 0 failed, 4 pre-existing ignores), DesignModel 77/77, Designer 245/245, Shell 47 (46 passed, 0 failed, 1 explicit).** No test may move from passing to ignored.

### Two reporting artifacts of this test host — read before believing a failure

An assertion that fails inside `WinUITestHost.Run` is listed **twice** — once against the test that actually failed and once against a different test in the same fixture. Read the stack trace, which names the real one, not the count. Before reporting an attribution, re-run that fixture alone with `--test=`.

`Console.WriteLine` from inside a `Run` body only reaches the output of the *first* test in a process to start the dispatcher thread; later tests' output vanishes. Write diagnostics to a file, or run one test per process.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/WinUIDesigner.XamlDom/XamlInstanceWriter.cs` (new) | The allow-list, the property filter, the scalar and collection writes, the depth and cycle guards, and the never-throw boundary. The whole of this phase's logic. |
| `src/WinUIDesigner.XamlDom/XamlDocument.cs` (modify) | Calls the writer from `CreatePropertyValue`'s element branch, and re-gates Phase B's `IXamlErrorSink` report onto "the writer refused". |
| `tests/WinUIDesigner.XamlDom.Tests/ElementSerializationTests.cs` (new) | Every round trip, the two gating rules, the refusal paths, the error-sink behaviour. |
| `tests/WinUIDesigner.Designer.Tests/SkewThumbExtensionTests.cs` (modify) | One end-to-end test: drag the skew thumb, save, reparse, assert the angles survive. The bug that started the phase, tested at the layer the user meets it. |

---

## Task 1: The writer, scalars only, wired in

**Files:**
- Create: `src/WinUIDesigner.XamlDom/XamlInstanceWriter.cs`
- Modify: `src/WinUIDesigner.XamlDom/XamlDocument.cs` (the `return new XamlObject(...)` at the end of `CreatePropertyValue`, currently line 370)
- Test: `tests/WinUIDesigner.XamlDom.Tests/ElementSerializationTests.cs` (created here)

**Interfaces:**
- Consumes: `XamlObject.FindOrCreateProperty(string)`, `XamlObject.XmlElement`, `XamlObject.ElementType`, `XamlObject.Instance`, `XamlObject.OwnerDocument`, `XamlProperty.PropertyValue`, `XamlDocument.CreatePropertyValue(object, XamlProperty)`, `WinUIStaticMemberLocator.FindDependencyProperty(Type, string)` (`XamlPropertyInfo.cs:100`), `CollectionSupport.IsCollectionType(Type)` (`CollectionSupport.cs:53`).
- Produces: `internal static class XamlInstanceWriter` with `public static bool HasEntry(Type)`, `public static bool TryWrite(XamlObject)` and `public static bool ShouldWarnFor(Type)`. Tasks 2, 3 and 4 call exactly these.

**Why this is its own task:** it is the whole scalar mechanism plus its wiring, and it is the smallest change that makes a measurable difference to saved XAML. A reviewer can reject the walk, the filter or the gate on their own merits before collections complicate the picture.

**Measured facts you must not re-derive:**
- `TypeDescriptor.GetProperties(typeof(SkewTransform))` returns `AngleX, AngleY, CenterX, CenterY, Inverse, Dispatcher, DispatcherQueue`. Filtering on `!pd.IsReadOnly` **and** a non-null `WinUIStaticMemberLocator.FindDependencyProperty(type, pd.Name)` keeps exactly the first four. The same filter was measured clean on `TransformGroup` (`Children`), `LinearGradientBrush` (`EndPoint, StartPoint, ColorInterpolationMode, GradientStops, MappingMode, SpreadMethod, Opacity, RelativeTransform, Transform`), `RectangleGeometry` (`Rect, Transform`) and `GeometryGroup` (`Children, FillRule, Transform`).
- `ReadLocalValue(dp) != DependencyProperty.UnsetValue` reports `AngleX` set and `AngleY`/`CenterX` unset on `new SkewTransform { AngleX = 30 }`. This is the right oracle for scalars. **It is the wrong oracle for collections** — Task 2's problem, not this one.
- Enum-valued properties need no special handling. `GeometryGroup.FillRule = Nonzero` already writes as `FillRule="Nonzero"` through Phase B's existing `hasStringConverter` branch, because `TypeDescriptor.GetConverter` returns an `EnumConverter` that answers both directions.
- A nested listed type recurses correctly: a `RectangleGeometry` with `Rect` and a `RotateTransform` writes both and reparses to `1,2,3,4` and `45`.
- `XamlProperty.PropertyValue`'s setter ends in `UpdateValueOnInstance` (`XamlProperty.cs:230`), writing the value back onto the live instance. For a scalar that is a self-assignment of the value already there, which is why this path is safe. Do not reach for it in Task 2.

- [ ] **Step 1: Write the failing test**

Create `tests/WinUIDesigner.XamlDom.Tests/ElementSerializationTests.cs` (4 spaces):

```csharp
using System;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using Windows.Foundation;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.XamlDom.Tests
{
    /// <summary>
    /// Phase C: writing a WinRT value's own properties into the element that carries it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phases A and B made a value round-trip when it has an attribute text form. Everything
    /// else was emitted as an empty element and lost - measured on seven types, one of which
    /// (<c>SkewTransform</c>) is a shipped designer feature whose output was silently discarded
    /// on every save.
    /// </para>
    /// <para>
    /// Every test runs on the WinUI dispatcher thread (see <see cref="WinUITestHost"/>) because
    /// every object involved is a thread-affine WinRT object. Assertions are written against a
    /// reparse of the saved text rather than against the text itself wherever the point is that
    /// the value survives; attribute-text assertions are used only where the point is the shape
    /// of the output.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class ElementSerializationTests
    {
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

        /// <summary>Sets the property, saves, and reparses - the whole round trip in one call.</summary>
        static Microsoft.UI.Xaml.Controls.Grid RoundTripOnAGrid(string propertyName, object value, out string saved)
        {
            var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
            SetThroughTheDocument(document, propertyName, value);
            saved = SaveToString(document);
            var reparsed = ParseDocument(saved);
            return (Microsoft.UI.Xaml.Controls.Grid)reparsed.RootInstance;
        }

        [Test]
        public void ASkewTransformKeepsBothAnglesAcrossASaveAndReload()
        {
            WinUITestHost.Run(() => {
                string saved;
                var grid = RoundTripOnAGrid(
                    "RenderTransform", new SkewTransform { AngleX = 30, AngleY = 10 }, out saved);

                var skew = grid.RenderTransform as SkewTransform;
                Assert.That(skew, Is.Not.Null,
                    "the transform did not survive the round trip at all; saved as: " + saved);
                Assert.That(skew.AngleX, Is.EqualTo(30).Within(0.001));
                Assert.That(skew.AngleY, Is.EqualTo(10).Within(0.001));
            });
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ElementSerializationTests.ASkewTransformKeepsBothAnglesAcrossASaveAndReload
```

Expected: FAIL. The message names the saved text, which will be
`<Grid ...><Grid.RenderTransform><SkewTransform /></Grid.RenderTransform></Grid>`. The
transform reparses as a `SkewTransform` with both angles at 0, so the failure is on `AngleX`,
not on the null check.

- [ ] **Step 3: Create the writer**

Create `src/WinUIDesigner.XamlDom/XamlInstanceWriter.cs` (**tabs**, no MIT header — this is not ported code):

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Writes a live WinRT value's own properties into the <see cref="XamlObject"/> that carries
	/// it, for the types on an explicit allow-list.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="XamlDocument.CreatePropertyValue"/> emits an element for any value with no
	/// attribute text form, and before this class that element was always empty: a skewed control
	/// saved as <c>&lt;SkewTransform /&gt;</c> and lost both angles on every save. WPF filled the
	/// same hole with <c>System.Windows.Markup.XamlWriter.Save</c>; WinUI 3 ships no
	/// <c>XamlWriter</c> of any kind, only <c>XamlReader</c>, so the machinery is written here.
	/// </para>
	/// <para>
	/// Two separate gates. The allow-list bounds which <b>types</b> are attempted, so a
	/// <c>Storyboard</c> or a <c>BitmapImage</c> appearing in a document keeps today's behaviour
	/// rather than meeting untested code. The property filter - writable, and backed by a
	/// <see cref="DependencyProperty"/> - bounds which <b>members</b> of a listed type are
	/// considered; measured, it excludes exactly the members XAML must not carry
	/// (<c>Inverse</c>, <c>Bounds</c>, <c>Value</c>, <c>Dispatcher</c>, <c>DispatcherQueue</c>)
	/// and nothing that is wanted.
	/// </para>
	/// <para>
	/// Nothing here throws. <c>PlacementOperation</c> never calls <c>Abort()</c> around
	/// <c>SetPlacement</c> (PlacementOperation.cs:114) and <c>src/</c> installs no
	/// <c>Application.UnhandledException</c> handler, so a throw from a write during a drag
	/// strands the transaction and ends the process. Every refusal is a <c>false</c> return.
	/// </para>
	/// </remarks>
	internal static class XamlInstanceWriter
	{
		/// <summary>
		/// How deep the walk may go. A cycle is caught by <see cref="InProgress"/> first; this is
		/// the backstop for a deep-but-acyclic graph, and for a cycle that somehow evades
		/// reference identity.
		/// </summary>
		const int MaxDepth = 12;

		/// <summary>
		/// The instances currently being written, innermost last. Recursion re-enters this class
		/// through <see cref="XamlDocument.CreatePropertyValue"/> rather than through a direct
		/// call, so the depth and the cycle set cannot be parameters - they live here.
		/// Thread-static because every WinUI object is thread-affine and each dispatcher thread
		/// walks its own graph.
		/// </summary>
		[ThreadStatic]
		static List<object> InProgress;

		static readonly HashSet<Type> Listed = new HashSet<Type> {
			typeof(Microsoft.UI.Xaml.Media.SkewTransform),
			typeof(Microsoft.UI.Xaml.Media.RotateTransform),
			typeof(Microsoft.UI.Xaml.Media.ScaleTransform),
			typeof(Microsoft.UI.Xaml.Media.TranslateTransform),
			typeof(Microsoft.UI.Xaml.Media.RectangleGeometry),
			typeof(Microsoft.UI.Xaml.Media.EllipseGeometry),
			typeof(Microsoft.UI.Xaml.Media.LineGeometry),
			typeof(Microsoft.UI.Xaml.Media.GradientStop),
		};

		/// <summary>
		/// The declared property types worth reporting about when the writer refuses. Kept
		/// separate from <see cref="Listed"/>: a nested control under a property declared
		/// <c>object</c> is not this phase's business and must not produce a diagnostic.
		/// </summary>
		static readonly Type[] WarnFor = {
			typeof(Microsoft.UI.Xaml.Media.Transform),
			typeof(Microsoft.UI.Xaml.Media.Brush),
			typeof(Microsoft.UI.Xaml.Media.Geometry),
		};

		/// <summary>Gets whether values of <paramref name="type"/> are serialized by this class.</summary>
		public static bool HasEntry(Type type)
		{
			return type != null && Listed.Contains(type);
		}

		/// <summary>
		/// Gets whether a value the writer refused, under a property declared
		/// <paramref name="declaredType"/>, is worth reporting through <see cref="IXamlErrorSink"/>.
		/// </summary>
		public static bool ShouldWarnFor(Type declaredType)
		{
			if (declaredType == null)
				return false;
			foreach (var t in WarnFor) {
				if (t.IsAssignableFrom(declaredType))
					return true;
			}
			return false;
		}

		/// <summary>
		/// Writes <paramref name="target"/>'s instance properties into its element. Returns
		/// whether anything was written; a <c>false</c> return leaves the element as it was
		/// found, which is the empty form this codebase emitted before Phase C.
		/// </summary>
		public static bool TryWrite(XamlObject target)
		{
			if (target == null || target.Instance == null || !HasEntry(target.ElementType))
				return false;

			var stack = InProgress ?? (InProgress = new List<object>());
			if (stack.Count >= MaxDepth)
				return false;
			for (int i = 0; i < stack.Count; i++) {
				if (ReferenceEquals(stack[i], target.Instance))
					return false;
			}

			stack.Add(target.Instance);
			try {
				return Write(target);
			} catch (Exception) {
				// Rule: never throw from the write path. A partially written element is a
				// degraded save; an exception here is a lost document and, mid-drag, a dead
				// process.
				return false;
			} finally {
				stack.RemoveAt(stack.Count - 1);
			}
		}

		static bool Write(XamlObject target)
		{
			var type = target.ElementType;
			var instance = target.Instance;
			var source = instance as DependencyObject;
			if (source == null)
				return false;

			var wroteSomething = false;

			foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(type)) {
				if (descriptor.IsReadOnly)
					continue;
				var dependencyProperty = WinUIStaticMemberLocator.FindDependencyProperty(type, descriptor.Name);
				if (dependencyProperty == null)
					continue;
				if (CollectionSupport.IsCollectionType(descriptor.PropertyType))
					continue;   // Task 2

				if (source.ReadLocalValue(dependencyProperty) == DependencyProperty.UnsetValue)
					continue;

				var value = descriptor.GetValue(instance);
				if (value == null)
					continue;

				var property = target.FindOrCreateProperty(descriptor.Name);
				property.PropertyValue = target.OwnerDocument.CreatePropertyValue(value, property);
				wroteSomething = true;
			}

			return wroteSomething;
		}
	}
}
```

- [ ] **Step 4: Wire it into `CreatePropertyValue`**

In `src/WinUIDesigner.XamlDom/XamlDocument.cs`, replace the final line of
`CreatePropertyValue` (currently `return new XamlObject(this, xml, elementType, instance);`,
line 370) with:

```csharp
			var result = new XamlObject(this, xml, elementType, instance);

			// Phase C: give the element its instance's own properties. A refusal - an unlisted
			// type, a cycle, a depth cap - leaves the empty element Phases A and B emitted.
			if (forProperty != null && !hasStringConverter && !isMarkupExtensionValue)
				XamlInstanceWriter.TryWrite(result);

			return result;
```

Leave the `IXamlErrorSink` block above it exactly as it is; Task 3 re-gates it.

- [ ] **Step 5: Run the test to verify it passes**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ElementSerializationTests.ASkewTransformKeepsBothAnglesAcrossASaveAndReload
```

Expected: PASS.

- [ ] **Step 6: Add the rest of the scalar round trips**

Append to `ElementSerializationTests`:

```csharp
        [Test]
        public void ARotateTransformKeepsItsAngle()
        {
            WinUITestHost.Run(() => {
                string saved;
                var grid = RoundTripOnAGrid("RenderTransform", new RotateTransform { Angle = 45 }, out saved);
                var rotate = grid.RenderTransform as RotateTransform;
                Assert.That(rotate, Is.Not.Null, "saved as: " + saved);
                Assert.That(rotate.Angle, Is.EqualTo(45).Within(0.001));
            });
        }

        [Test]
        public void AScaleTransformKeepsBothScales()
        {
            WinUITestHost.Run(() => {
                string saved;
                var grid = RoundTripOnAGrid("RenderTransform", new ScaleTransform { ScaleX = 2, ScaleY = 3 }, out saved);
                var scale = grid.RenderTransform as ScaleTransform;
                Assert.That(scale, Is.Not.Null, "saved as: " + saved);
                Assert.That(scale.ScaleX, Is.EqualTo(2).Within(0.001));
                Assert.That(scale.ScaleY, Is.EqualTo(3).Within(0.001));
            });
        }

        [Test]
        public void ATranslateTransformKeepsBothOffsets()
        {
            WinUITestHost.Run(() => {
                string saved;
                var grid = RoundTripOnAGrid("RenderTransform", new TranslateTransform { X = 5, Y = 6 }, out saved);
                var translate = grid.RenderTransform as TranslateTransform;
                Assert.That(translate, Is.Not.Null, "saved as: " + saved);
                Assert.That(translate.X, Is.EqualTo(5).Within(0.001));
                Assert.That(translate.Y, Is.EqualTo(6).Within(0.001));
            });
        }

        [Test]
        public void ARectangleGeometryKeepsItsRect()
        {
            WinUITestHost.Run(() => {
                string saved;
                var grid = RoundTripOnAGrid("Clip", new RectangleGeometry { Rect = new Rect(1, 2, 3, 4) }, out saved);
                Assert.That(grid.Clip, Is.Not.Null, "saved as: " + saved);
                Assert.That(grid.Clip.Rect, Is.EqualTo(new Rect(1, 2, 3, 4)));
            });
        }

        [Test]
        public void AnEllipseGeometryKeepsItsCentreAndRadii()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Path xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Data",
                    new EllipseGeometry { Center = new Point(5, 6), RadiusX = 7, RadiusY = 8 });
                var saved = SaveToString(document);
                var path = (Microsoft.UI.Xaml.Shapes.Path)ParseDocument(saved).RootInstance;

                var ellipse = path.Data as EllipseGeometry;
                Assert.That(ellipse, Is.Not.Null, "saved as: " + saved);
                Assert.That(ellipse.Center, Is.EqualTo(new Point(5, 6)));
                Assert.That(ellipse.RadiusX, Is.EqualTo(7).Within(0.001));
                Assert.That(ellipse.RadiusY, Is.EqualTo(8).Within(0.001));
            });
        }

        [Test]
        public void ALineGeometryKeepsBothEndpoints()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Path xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Data",
                    new LineGeometry { StartPoint = new Point(1, 2), EndPoint = new Point(3, 4) });
                var saved = SaveToString(document);
                var path = (Microsoft.UI.Xaml.Shapes.Path)ParseDocument(saved).RootInstance;

                var line = path.Data as LineGeometry;
                Assert.That(line, Is.Not.Null, "saved as: " + saved);
                Assert.That(line.StartPoint, Is.EqualTo(new Point(1, 2)));
                Assert.That(line.EndPoint, Is.EqualTo(new Point(3, 4)));
            });
        }

        [Test]
        public void AnUnsetPropertyIsNotWritten()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "RenderTransform", new SkewTransform { AngleX = 30 });
                var saved = SaveToString(document);

                StringAssert.Contains("AngleX=\"30\"", saved);
                StringAssert.DoesNotContain("CenterX", saved,
                    "an unset property must not be written, or every saved transform carries " +
                    "CenterX=\"0\" CenterY=\"0\" noise");
                StringAssert.DoesNotContain("AngleY", saved);
            });
        }

        [Test]
        public void RuntimePlumbingIsNeverWritten()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "RenderTransform", new SkewTransform { AngleX = 30, AngleY = 10 });
                var saved = SaveToString(document);

                // TypeDescriptor offers these on every WinRT type. The writable-and-backed-by-a-
                // DependencyProperty filter is what keeps them out; without it the saved XAML
                // carries members no XAML parser can read back.
                StringAssert.DoesNotContain("Inverse", saved);
                StringAssert.DoesNotContain("Dispatcher", saved);
                StringAssert.DoesNotContain("DispatcherQueue", saved);
            });
        }

        [Test]
        public void ANestedListedTypeIsWrittenByRecursion()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Path xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Data", new LineGeometry {
                    StartPoint = new Point(1, 2),
                    EndPoint = new Point(3, 4),
                    Transform = new RotateTransform { Angle = 45 }
                });
                var saved = SaveToString(document);
                var path = (Microsoft.UI.Xaml.Shapes.Path)ParseDocument(saved).RootInstance;

                // A listed type nested inside another listed type, reached by recursion back
                // through CreatePropertyValue.
                var line = (LineGeometry)path.Data;
                var rotate = line.Transform as RotateTransform;
                Assert.That(rotate, Is.Not.Null, "saved as: " + saved);
                Assert.That(rotate.Angle, Is.EqualTo(45).Within(0.001));
            });
        }
```

- [ ] **Step 7: Run the whole fixture and the whole XamlDom suite**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
```

Expected: the fixture's 10 tests pass, and the suite is 154 tests (150 passed, 0 failed, 4
pre-existing ignores). If `ValueFormattingTests.LeavesAnUnformattableInstanceOnTheElementPath`
fails, read Task 3 — it asserts on `LinearGradientBrush`, which Task 2 changes, and it must
stay green until then.

- [ ] **Step 8: Commit**

```bash
git add src/WinUIDesigner.XamlDom/XamlInstanceWriter.cs src/WinUIDesigner.XamlDom/XamlDocument.cs tests/WinUIDesigner.XamlDom.Tests/ElementSerializationTests.cs
git commit -m "fix: write a listed value's scalar properties into its element instead of an empty one"
```

---

## Task 2: Collections

**Files:**
- Modify: `src/WinUIDesigner.XamlDom/XamlInstanceWriter.cs`
- Test: `tests/WinUIDesigner.XamlDom.Tests/ElementSerializationTests.cs`

**Interfaces:**
- Consumes: everything Task 1 produced, plus `XamlProperty.IsCollection` (`XamlProperty.cs:171`) and `XamlProperty.ParserAddCollectionElement(XmlElement, XamlPropertyValue)` (`XamlProperty.cs:600`, `internal`, reachable because this code is inside `WinUIDesigner.XamlDom`).
- Produces: no new public members. `Listed` gains `TransformGroup`, `GeometryGroup` and `LinearGradientBrush`.

**Why this is its own task:** the collection path uses a different API for a measured reason and has two measured ordering traps. It deserves its own review.

**Measured facts you must not re-derive:**
- **`XamlProperty.CollectionElements.Add` throws** on this path:
  ```
  System.Runtime.InteropServices.COMException (0x800F1000): No installed components were detected.
     at Microsoft.UI.Xaml.Media.GradientStopCollection.Insert(Int32 index, GradientStop item)
     at WinUIDesigner.XamlDom.CollectionSupport.Insert(...) CollectionSupport.cs:243
  ```
  It mutates the live collection as well as the XML, and the children being written are already
  members of that collection. WinRT refuses a child that already has that parent. **Do not use
  it.**
- **`ReadLocalValue` is useless for collections.** `LinearGradientBrush.GradientStops` reads as
  *not locally set* on a fresh brush **and still reads as not locally set after two
  `GradientStop`s have been added**. Same for `TransformGroup.Children` and
  `GeometryGroup.Children`. The collection is the property's lazily-created default value,
  mutated in place. Gate collections on `Count > 0` instead. A writer that reuses Task 1's
  scalar gate here passes every other test in this plan while emitting no gradient stops.
- **Two ordering traps, both silent.** `ParserAddCollectionElement(null, value)` registers the
  value and never attaches its node. Calling it with a property element whose children have not
  been appended first leaves an empty `<LinearGradientBrush.GradientStops />`. The child's
  `XmlElement` must be appended to the property element **before** the call.
- The working sequence, measured end to end: create `<Owner.Property>` with the owner element's
  own prefix and namespace URI, append it to the owner element, then per child — build the child
  through `CreatePropertyValue`, append its `XmlElement` to the property element, then
  `ParserAddCollectionElement(propertyElement, child)`. The live collection is left untouched
  and the document reparses with all children present.

- [ ] **Step 1: Write the failing test**

Append to `ElementSerializationTests`:

```csharp
        [Test]
        public void ALinearGradientBrushKeepsItsStops()
        {
            WinUITestHost.Run(() => {
                var brush = new LinearGradientBrush();
                brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.Red, Offset = 0 });
                brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.Blue, Offset = 1 });

                string saved;
                var grid = RoundTripOnAGrid("Background", brush, out saved);

                var reloaded = grid.Background as LinearGradientBrush;
                Assert.That(reloaded, Is.Not.Null, "saved as: " + saved);
                Assert.That(reloaded.GradientStops.Count, Is.EqualTo(2),
                    "the stops did not survive; saved as: " + saved);
                Assert.That(reloaded.GradientStops[0].Color, Is.EqualTo(Microsoft.UI.Colors.Red));
                Assert.That(reloaded.GradientStops[0].Offset, Is.EqualTo(0).Within(0.001));
                Assert.That(reloaded.GradientStops[1].Color, Is.EqualTo(Microsoft.UI.Colors.Blue));
                Assert.That(reloaded.GradientStops[1].Offset, Is.EqualTo(1).Within(0.001));
            });
        }
```

- [ ] **Step 2: Run it to verify it fails**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ElementSerializationTests.ALinearGradientBrushKeepsItsStops
```

Expected: FAIL on the null check — `LinearGradientBrush` is not yet listed, so the brush saves
as an empty element and reparses to a brush with no stops. The message carries the saved text.

- [ ] **Step 3: List the three collection-bearing types**

In `XamlInstanceWriter.Listed`, add:

```csharp
			typeof(Microsoft.UI.Xaml.Media.TransformGroup),
			typeof(Microsoft.UI.Xaml.Media.GeometryGroup),
			typeof(Microsoft.UI.Xaml.Media.LinearGradientBrush),
```

- [ ] **Step 4: Replace the collection skip with the collection write**

In `XamlInstanceWriter.Write`, replace

```csharp
				if (CollectionSupport.IsCollectionType(descriptor.PropertyType))
					continue;   // Task 2
```

with

```csharp
				if (CollectionSupport.IsCollectionType(descriptor.PropertyType)) {
					if (WriteCollection(target, descriptor, descriptor.GetValue(instance)))
						wroteSomething = true;
					continue;
				}
```

and add, after `Write`:

```csharp
		/// <summary>
		/// Writes a collection property as an explicit <c>&lt;Owner.Property&gt;</c> element with
		/// one child element per item.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Not through <see cref="XamlProperty.CollectionElements"/>: that path mutates the live
		/// collection as well as the XML, and the items being written are already members of it.
		/// Measured, it throws <c>COMException 0x800F1000</c> out of
		/// <c>GradientStopCollection.Insert</c> - WinRT refuses a child that already has that
		/// parent. The parser-side entry point takes a node that is already in the XML DOM, which
		/// is what this method arranges.
		/// </para>
		/// <para>
		/// The gate is <c>Count &gt; 0</c> rather than <c>ReadLocalValue</c>, which Task 1 uses
		/// for scalars. Measured: a <c>GradientStopCollection</c> reads as not locally set even
		/// after two stops have been added to it, because the collection is the property's
		/// lazily-created default value and is mutated in place. Gating collections the way
		/// scalars are gated emits no children at all.
		/// </para>
		/// <para>
		/// The order is load-bearing and both wrong orders fail silently rather than throwing:
		/// a null property element registers the value without attaching its node, and appending
		/// the children after the call leaves an empty property element.
		/// </para>
		/// </remarks>
		static bool WriteCollection(XamlObject target, PropertyDescriptor descriptor, object collection)
		{
			var items = collection as IEnumerable;
			if (items == null)
				return false;

			var present = new List<object>();
			foreach (var item in items) {
				if (item != null)
					present.Add(item);
			}
			if (present.Count == 0)
				return false;

			var property = target.FindOrCreateProperty(descriptor.Name);
			if (!property.IsCollection)
				return false;

			var element = target.XmlElement;
			var propertyElement = element.OwnerDocument.CreateElement(
				element.Prefix, target.ElementType.Name + "." + descriptor.Name, element.NamespaceURI);
			element.AppendChild(propertyElement);

			var wroteSomething = false;
			foreach (var item in present) {
				var child = target.OwnerDocument.CreatePropertyValue(item, property) as XamlObject;
				if (child == null)
					continue;
				propertyElement.AppendChild(child.XmlElement);
				property.ParserAddCollectionElement(propertyElement, child);
				wroteSomething = true;
			}
			return wroteSomething;
		}
```

- [ ] **Step 5: Run it to verify it passes**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ElementSerializationTests.ALinearGradientBrushKeepsItsStops
```

Expected: PASS.

- [ ] **Step 6: Add the remaining collection tests and the two guard tests**

Append to `ElementSerializationTests`:

```csharp
        [Test]
        public void ATransformGroupKeepsItsChildrenInOrder()
        {
            WinUITestHost.Run(() => {
                var group = new TransformGroup();
                group.Children.Add(new RotateTransform { Angle = 45 });
                group.Children.Add(new ScaleTransform { ScaleX = 2, ScaleY = 3 });

                string saved;
                var grid = RoundTripOnAGrid("RenderTransform", group, out saved);

                var reloaded = grid.RenderTransform as TransformGroup;
                Assert.That(reloaded, Is.Not.Null, "saved as: " + saved);
                Assert.That(reloaded.Children.Count, Is.EqualTo(2), "saved as: " + saved);
                Assert.That(reloaded.Children[0], Is.InstanceOf<RotateTransform>(),
                    "order matters: a rotate then a scale is not the same transform as a scale " +
                    "then a rotate");
                Assert.That(((RotateTransform)reloaded.Children[0]).Angle, Is.EqualTo(45).Within(0.001));
                Assert.That(reloaded.Children[1], Is.InstanceOf<ScaleTransform>());
                Assert.That(((ScaleTransform)reloaded.Children[1]).ScaleX, Is.EqualTo(2).Within(0.001));
            });
        }

        [Test]
        public void AGeometryGroupKeepsItsChildrenAndFillRule()
        {
            WinUITestHost.Run(() => {
                var group = new GeometryGroup { FillRule = FillRule.Nonzero };
                group.Children.Add(new RectangleGeometry { Rect = new Rect(1, 2, 3, 4) });
                group.Children.Add(new EllipseGeometry { Center = new Point(5, 6), RadiusX = 7, RadiusY = 8 });

                var document = ParseDocument("<Path xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Data", group);
                var saved = SaveToString(document);
                var path = (Microsoft.UI.Xaml.Shapes.Path)ParseDocument(saved).RootInstance;

                var reloaded = path.Data as GeometryGroup;
                Assert.That(reloaded, Is.Not.Null, "saved as: " + saved);
                Assert.That(reloaded.FillRule, Is.EqualTo(FillRule.Nonzero));
                Assert.That(reloaded.Children.Count, Is.EqualTo(2), "saved as: " + saved);
                Assert.That(((RectangleGeometry)reloaded.Children[0]).Rect, Is.EqualTo(new Rect(1, 2, 3, 4)));
                Assert.That(((EllipseGeometry)reloaded.Children[1]).RadiusX, Is.EqualTo(7).Within(0.001));
            });
        }

        [Test]
        public void APopulatedCollectionIsWrittenEvenThoughItNeverReadsAsLocallySet()
        {
            WinUITestHost.Run(() => {
                var brush = new LinearGradientBrush();
                brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.Red, Offset = 0 });

                // The measurement this test exists to pin: the collection is the property's
                // lazily-created default and is mutated in place, so it never registers as a
                // local value. A writer that gates collections the way it gates scalars emits
                // nothing here and every other test in this fixture still passes.
                Assert.That(
                    brush.ReadLocalValue(LinearGradientBrush.GradientStopsProperty),
                    Is.EqualTo(Microsoft.UI.Xaml.DependencyProperty.UnsetValue),
                    "if this ever starts reporting the collection as locally set, the special " +
                    "case in WriteCollection can go");

                string saved;
                var grid = RoundTripOnAGrid("Background", brush, out saved);
                Assert.That(((LinearGradientBrush)grid.Background).GradientStops.Count, Is.EqualTo(1),
                    "saved as: " + saved);
            });
        }

        [Test]
        public void AnEmptyCollectionWritesNoPropertyElement()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "RenderTransform", new TransformGroup());
                var saved = SaveToString(document);

                StringAssert.DoesNotContain("TransformGroup.Children", saved,
                    "an empty collection must not produce a property element");
            });
        }

        [Test]
        public void ADeeplyNestedGraphStopsAtTheDepthCapWithoutThrowing()
        {
            WinUITestHost.Run(() => {
                // Twenty nested groups, against a MaxDepth of 12. A genuine reference cycle is
                // what MaxDepth exists to survive, but one could not be built here to test with:
                // a TransformGroup cannot be made a descendant of itself through the public
                // WinUI API, so depth is the only reachable way to drive the guard. The
                // InProgress reference check stays as the belt to this test's braces and is
                // recorded in the spec's Known gaps as unexercised.
                var outermost = new TransformGroup();
                var current = outermost;
                for (int i = 0; i < 19; i++) {
                    var next = new TransformGroup();
                    current.Children.Add(next);
                    current = next;
                }
                current.Children.Add(new RotateTransform { Angle = 45 });

                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                var property = document.RootElement.FindOrCreateProperty("RenderTransform");

                string saved = null;
                Assert.DoesNotThrow(() => {
                    property.PropertyValue = document.CreatePropertyValue(outermost, property);
                    saved = SaveToString(document);
                }, "the write path must never throw - a throw here strands a PlacementOperation " +
                   "and, with no Application.UnhandledException handler in src/, ends the process");
                Assert.That(saved, Is.Not.Null);
                Assert.That(saved, Does.Contain("TransformGroup"),
                    "the outer levels are still written; only the levels past the cap are not");
            });
        }
```

- [ ] **Step 7: Run the whole XamlDom suite**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
```

Expected: **160 tests, 156 passed, 0 failed, 4 ignored** (154 after Task 1, plus this task's
six).

`ValueFormattingTests.LeavesAnUnformattableInstanceOnTheElementPath` is the one to watch and it
must stay green: it asserts a `LinearGradientBrush` "must keep the element form", and the brush
it uses is **empty**. An empty brush has no stops to write and no locally-set scalars, so the
writer refuses it and the element stays exactly as before. Its sibling
`ValueFormattingTests.ReportsAValueItCannotWriteAsAttributeText` uses the same empty brush and
must also stay green. If either goes red, the writer is emitting something for a value with
nothing set — that is a defect in this task, not a stale test.

- [ ] **Step 8: Commit**

```bash
git add src/WinUIDesigner.XamlDom/XamlInstanceWriter.cs tests/WinUIDesigner.XamlDom.Tests/ElementSerializationTests.cs
git commit -m "fix: serialize gradient stops and transform/geometry group children"
```

---

## Task 3: Re-gate the error report onto what is still lost

**Files:**
- Modify: `src/WinUIDesigner.XamlDom/XamlDocument.cs` (the `IXamlErrorSink` block, currently lines 330-341)
- Modify: `tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs` (the two tests that assert the old behaviour)
- Test: `tests/WinUIDesigner.XamlDom.Tests/ElementSerializationTests.cs`

**Interfaces:**
- Consumes: `XamlInstanceWriter.TryWrite(XamlObject)` and `XamlInstanceWriter.ShouldWarnFor(Type)` from Task 1.
- Produces: no new members.

**Why this is its own task:** it changes a diagnostic's meaning, and the risk is a false positive on `{Binding}` that would put a spurious error in front of every user who opens a bound document. That risk is worth its own reviewer.

**Measured facts you must not re-derive:**
- Today's gate is `XamlValueFormatter.HasFormatter(forProperty.ReturnType)`. `RenderTransform` is declared `Transform`, which is not one of the formatter's sixteen types, so **the transform losses were completely silent** — no diagnostic at all.
- `Microsoft.UI.Xaml.Data.Binding` does **not** derive from `MarkupExtension` on WinUI; its base chain is `BindingBase` → `DependencyObject`. That is why the existing exclusion is written `instance is MarkupExtension || instance is BindingBase` and not `XamlObject.IsMarkupExtension`. Keep it exactly as it is.
- Widening the gate to "the writer refused" alone would report a nested control set into a property declared `object`, which is not this phase's business. `ShouldWarnFor` narrows it to properties declared `Transform`, `Brush` or `Geometry`.

- [ ] **Step 1: Write the failing tests**

Append to `ElementSerializationTests`:

```csharp
        // The registration shape is the one ValueFormattingTests.cs:588-625 already uses: the
        // sink reaches XamlDocument through XamlParserSettings.ServiceProvider, set before the
        // parse. There is no way to add a service to a document after the fact.
        sealed class RecordingSink : IXamlErrorSink
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

        /// <summary>
        /// Parses <paramref name="xaml"/> with a recording error sink attached, sets the property,
        /// and hands back both the sink and the document so the caller can also save it.
        /// </summary>
        static RecordingSink SetWithASink(string xaml, string propertyName, object value, out XamlDocument document)
        {
            var sink = new RecordingSink();
            var settings = new XamlParserSettings();
            settings.ServiceProvider = new SingleServiceProvider(typeof(IXamlErrorSink), sink);
            document = XamlParser.Parse(new System.IO.StringReader(xaml), settings);
            var property = document.RootElement.FindOrCreateProperty(propertyName);
            property.PropertyValue = document.CreatePropertyValue(value, property);
            return sink;
        }

        [Test]
        public void AnUnlistedBrushStillSavesAsAnEmptyElementAndIsReported()
        {
            WinUITestHost.Run(() => {
                XamlDocument document;
                var sink = SetWithASink("<Grid xmlns='" + PresentationNs + "'/>",
                    "Background", new ImageBrush(), out document);
                var saved = SaveToString(document);

                StringAssert.Contains("<ImageBrush", saved,
                    "an unlisted type must keep today's behaviour, not meet untested code");
                Assert.That(sink.Messages.Count, Is.EqualTo(1), "saved as: " + saved);
                StringAssert.Contains("ImageBrush", sink.Messages[0]);
            });
        }

        [Test]
        public void AnUnlistedTransformIsNowReportedWhereItUsedToBeSilent()
        {
            WinUITestHost.Run(() => {
                XamlDocument document;
                // A CompositeTransform is a Transform this phase does not list. Before Phase C
                // the gate was HasFormatter(declared type), and Transform is not one of the
                // formatter's sixteen - so a lost transform produced no diagnostic at all.
                var sink = SetWithASink("<Grid xmlns='" + PresentationNs + "'/>",
                    "RenderTransform", new CompositeTransform { Rotation = 45 }, out document);

                Assert.That(sink.Messages.Count, Is.EqualTo(1),
                    "a transform the writer cannot handle must say so");
                StringAssert.Contains("CompositeTransform", sink.Messages[0]);
            });
        }

        [Test]
        public void AWrittenValueIsNotReported()
        {
            WinUITestHost.Run(() => {
                XamlDocument document;
                var sink = SetWithASink("<Grid xmlns='" + PresentationNs + "'/>",
                    "RenderTransform", new SkewTransform { AngleX = 30 }, out document);

                Assert.That(sink.Messages, Is.Empty,
                    "the writer handled this one; reporting it would train users to ignore the " +
                    "diagnostic");
            });
        }

        [Test]
        public void ABindingOnAListedPropertyTypeIsNeitherWrittenNorReported()
        {
            WinUITestHost.Run(() => {
                XamlDocument document;
                var sink = SetWithASink("<Grid xmlns='" + PresentationNs + "'/>", "Background",
                    new Microsoft.UI.Xaml.Data.Binding { Path = new Microsoft.UI.Xaml.PropertyPath("Fill") },
                    out document);
                var saved = SaveToString(document);

                Assert.That(sink.Messages, Is.Empty,
                    "a Binding round-trips through XamlObject's own markup-extension path; " +
                    "reporting it would fire on every bound document");
                StringAssert.Contains("Binding", saved);
            });
        }
```

- [ ] **Step 2: Run them to verify they fail**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ElementSerializationTests.AnUnlistedTransformIsNowReportedWhereItUsedToBeSilent
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult --test=WinUIDesigner.XamlDom.Tests.ElementSerializationTests.AWrittenValueIsNotReported
```

Expected: `AnUnlistedTransformIsNowReportedWhereItUsedToBeSilent` FAILS with 0 messages (today's
gate is silent for `Transform`). `AWrittenValueIsNotReported` PASSES already, for the same
reason — it is the control that keeps the widened gate honest.

- [ ] **Step 3: Re-gate the report**

In `src/WinUIDesigner.XamlDom/XamlDocument.cs`, move the `IXamlErrorSink` block to after the
writer call added in Task 1, and change its condition. The tail of `CreatePropertyValue`
becomes:

```csharp
			var result = new XamlObject(this, xml, elementType, instance);

			// Phase C: give the element its instance's own properties. A refusal - an unlisted
			// type, a cycle, a depth cap - leaves the empty element Phases A and B emitted.
			bool written = forProperty != null && !hasStringConverter && !isMarkupExtensionValue
				&& XamlInstanceWriter.TryWrite(result);

			// Phase B reported a value with no attribute text form. That gate read
			// HasFormatter(declared type), so it never fired for a property declared Transform -
			// and the transform losses were silent. The accurate condition is "this value is
			// about to be written as an element that will not survive a reload", which is now
			// exactly "the writer refused". ShouldWarnFor keeps it off properties declared
			// object, where a nested control is normal and not this phase's business.
			if (forProperty != null && !hasStringConverter && !isMarkupExtensionValue && !written
			    && xml.InnerText.Length == 0
			    && (XamlValueFormatter.HasFormatter(forProperty.ReturnType)
			        || XamlInstanceWriter.ShouldWarnFor(forProperty.ReturnType))) {
				var sink = ServiceProvider == null
					? null
					: ServiceProvider.GetService(typeof(IXamlErrorSink)) as IXamlErrorSink;
				if (sink != null)
					sink.ReportError(
						"Cannot save " + forProperty.PropertyName + " as attribute text: " +
						elementType.FullName + " has no XAML attribute form, so it is written as an " +
						"element and will not survive a reload.", 0, 0);
			}

			return result;
```

Delete the old block from its former position above `string ns = GetNamespaceFor(elementType);`,
keeping the `isMarkupExtensionValue` line and its comment where they are.

- [ ] **Step 4: Update the two `ValueFormattingTests` that assert the old behaviour**

`ValueFormattingTests.LeavesAnUnformattableInstanceOnTheElementPath` (around line 515) still
describes an empty element. Replace its body's assertion so it states what is now true:

```csharp
        [Test]
        public void LeavesAnUnformattableInstanceOnTheElementPath()
        {
            WinUITestHost.Run(() => {
                var document = ParseDocument("<Grid xmlns='" + PresentationNs + "'/>");
                SetThroughTheDocument(document, "Background", new LinearGradientBrush());
                var saved = SaveToString(document);
                StringAssert.Contains("<LinearGradientBrush", saved,
                    "A gradient brush has no attribute form and must keep the element form. " +
                    "Phase C fills that element in when the brush has stops; an empty brush " +
                    "still writes an empty element.");
            });
        }
```

Then run the sink test around line 630 and read what it now expects; it asserts a message
mentioning `LinearGradientBrush` for an empty brush, which the new gate still produces (an
empty brush has nothing to write, so the writer refuses). If it fails, the message is the
authority on what changed — fix the test to the new truth, do not weaken the gate.

- [ ] **Step 5: Run the whole XamlDom suite**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
```

Expected: **164 tests, 160 passed, 0 failed, 4 ignored** (160 after Task 2, plus this task's four).

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.XamlDom/XamlDocument.cs tests/WinUIDesigner.XamlDom.Tests/ElementSerializationTests.cs tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs
git commit -m "fix: report the values the element writer still cannot save"
```

---

## Task 4: The end-to-end skew test, and the four-suite verification

**Files:**
- Modify: `tests/WinUIDesigner.Designer.Tests/SkewThumbExtensionTests.cs`

**Interfaces:**
- Consumes: `DesignSurface.LoadDesigner(XmlReader, XamlLoadSettings)`, `DesignSurface.DesignContext`, `XamlDesignContext.Save(XmlWriter)` (`XamlDesignContext.cs:89`), `SkewThumbExtension.DragListenerX` and `DragListener.SimulateDown/SimulateMove/SimulateUp` — all already used by the fixture's existing tests.
- Produces: nothing.

**Why this is its own task:** it is the only test in the plan at the layer the user meets the bug, it lives in a different test project, and it is what makes the phase's claim ("skew survives a save") true rather than inferred.

**Measured facts you must not re-derive:**
- `SkewThumbExtension.ApplySkew` (`SkewThumbExtension.cs:106-119`) sets `RenderTransform` and stops. Unlike `ModelTools.ApplyTransform` (`ModelTools.cs:148`), it never writes the angles through the model afterwards, which is why the loss reached a shipped feature.
- The fixture's existing `LoadSelected` helper builds the surface, loads `ButtonXaml`, selects the button and returns the surface. Reuse it; do not write another.
- A drag of `-20` on the X thumb produces `AngleX == 10` (the existing tests assert exactly this, with a tolerance of 0.5).

- [ ] **Step 1: Write the failing test**

Append to `SkewThumbExtensionTests` (4 spaces):

```csharp
        [Test]
        public void ASkewSurvivesASaveAndReload()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var extension = buttonItem.Extensions.OfType<SkewThumbExtension>().Single();
                var drag = extension.DragListenerX;

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(-20, 0)); // -0.5 * -20 = +10 degrees
                drag.SimulateUp(new Windows.Foundation.Point(-20, 0));
                surface.UpdateLayout();

                var text = new StringWriter();
                var settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                using (var writer = XmlWriter.Create(text, settings))
                    surface.DesignContext.Save(writer);
                var saved = text.ToString();

                // ApplySkew sets RenderTransform to a live SkewTransform and stops there - unlike
                // ModelTools.ApplyTransform, which writes the angle through the model afterwards.
                // Before Phase C the element written for that transform was empty, so dragging the
                // skew thumb and saving lost the gesture with no error anywhere.
                var reloaded = WinUIDesigner.XamlDom.XamlParser.Parse(new StringReader(saved));
                var reloadedButton = (Microsoft.UI.Xaml.Controls.Button)
                    ((Microsoft.UI.Xaml.Controls.Grid)reloaded.RootInstance).Children[0];

                var skew = reloadedButton.RenderTransform as Microsoft.UI.Xaml.Media.SkewTransform;
                Assert.IsNotNull(skew, "the skew did not survive the save; saved as: " + saved);
                Assert.AreEqual(10, skew.AngleX, 0.5, "saved as: " + saved);
            });
        }
```

- [ ] **Step 2: Run it**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.SkewThumbExtensionTests.ASkewSurvivesASaveAndReload
```

Expected: PASS, because Tasks 1-3 already fixed the underlying defect. **This is a regression
net, not a red-then-green cycle.** To prove it has teeth, temporarily remove
`typeof(Microsoft.UI.Xaml.Media.SkewTransform)` from `XamlInstanceWriter.Listed`, rebuild, and
confirm this test fails on the null check. Then put it back, rebuild, and confirm it passes
again. Record both observations in the task report.

- [ ] **Step 3: Run all four suites**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```

Expected: XamlDom 164 (160 passed, 0 failed, 4 ignored), DesignModel 77/77, Designer 246/246,
Shell 47 (46 passed, 0 failed, 1 explicit).

`ModelToolsTests.ApplyingASecondTransformKindWrapsBothInATransformGroup` is the one to watch in
the Designer suite: `ApplyTransform` now writes `Angle` on top of an element that already
carries it, and `TransformGroup` is now a listed type whose `Children` the writer populates.
If it fails, the interaction between the writer and `CollectionElementsCollection.InsertItem` is
the place to look, not the test.

- [ ] **Step 4: Update the spec's status line and Known gaps**

In `docs/superpowers/specs/2026-09-07-xamldom-element-serialization-design.md`, change

```
**Status:** designed, not implemented.
```

to name the implementing commits, and strike the Known-gaps bullet that reads "A property whose
value is a nested object *outside* the table refuses the whole parent" — the implementation
recurses uniformly instead, so an unlisted nested value keeps its own empty element while its
parent is still written. Add a bullet recording that a genuine exception mid-write leaves a
partially written element rather than rolling back, because Decision 5 forbids the alternative.

- [ ] **Step 5: Commit**

```bash
git add tests/WinUIDesigner.Designer.Tests/SkewThumbExtensionTests.cs docs/superpowers/specs/2026-09-07-xamldom-element-serialization-design.md
git commit -m "test: pin the skew round trip at the layer the user meets it"
```

---

## Plan self-review

**1. Spec coverage.**

| Spec item | Task |
|---|---|
| Decision 1 (fix in XamlDom, reached from `CreatePropertyValue`) | 1, steps 3-4 |
| Decision 2 (type allow-list + writable/DP property filter) | 1, step 3; tests in 1 step 6 (`RuntimePlumbingIsNeverWritten`) |
| Decision 3 (scalar `ReadLocalValue`, collection `Count > 0`) | 1 step 3 scalars; 2 step 4 collections; tests `AnUnsetPropertyIsNotWritten`, `APopulatedCollectionIsWrittenEvenThoughItNeverReadsAsLocallySet` |
| Decision 4 (parser-side collection path) | 2, step 4 |
| Decision 5 (never throw) | 1 step 3 (`try`/`catch` in `TryWrite`); test `ADeeplyNestedGraphStopsAtTheDepthCapWithoutThrowing` |
| Decision 6 (explicit property element) | 2, step 4 (`WriteCollection` builds `Owner.Property`) |
| Decision 7 (depth and cycle guards) | 1, step 3 (`MaxDepth`, `InProgress`) |
| Decision 8 (the eleven types) | 1 step 3 (eight scalar-only), 2 step 3 (three collection-bearing) |
| Decision 9 (re-gate the sink report) | 3 |
| DoD 1-5 (round trips) | 1 step 6, 2 step 6 |
| DoD 6 / 6b (unset absent, populated collection present) | 1 step 6, 2 step 6 |
| DoD 7 (unlisted type still empty + reported) | 3 step 1 |
| DoD 8 (`{Binding}` neither written nor reported) | 3 step 1 |
| DoD 9 (cycle refuses without throwing) | 2 step 6, as a depth-cap test - see its comment: a true reference cycle is not constructible through WinUI's public API |
| DoD 10 (`ModelToolsTests` stays green) | 4 step 3 |
| DoD 11 (end-to-end skew) | 4 |
| DoD 12 (four suites green) | 4 step 3 |

No gaps.

**2. Placeholder scan.** No "TBD", no "add appropriate error handling", no "similar to Task N".
Every code step carries the code. Two steps deliberately delegate a judgement rather than
prescribe an answer — Task 3 step 4's "read what it now expects" for an existing test whose
assertion text is stale, and Task 3 step 2's fallback if `SetWithASink` does not compile — and
both name the authority to decide with (the failure message; `XamlParser`'s own registration).

**3. Type consistency.** `HasEntry(Type)`, `TryWrite(XamlObject)`, `ShouldWarnFor(Type)`,
`Write(XamlObject)`, `WriteCollection(XamlObject, PropertyDescriptor, object)`, `Listed`,
`WarnFor`, `InProgress`, `MaxDepth` are spelled identically everywhere they appear. The test
helpers `ParseDocument`, `SaveToString`, `SetThroughTheDocument`, `RoundTripOnAGrid`,
`RecordingSink` and `SetWithASink` are each defined once, in Task 1 step 1 or Task 3 step 1, and
used with the same signatures afterwards. `PresentationNs` is defined in Task 1 step 1 and
matches the constant `ValueFormattingTests` already uses.
