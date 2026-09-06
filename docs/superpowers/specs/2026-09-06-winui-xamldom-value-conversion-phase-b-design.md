# XamlDom Value Conversion, Phase B — the write side

**Status:** implemented. Shipped as commits `0d5f6b4..451cac0` on `main`.
**Phase A:** `docs/superpowers/specs/2026-09-06-winui-xamldom-value-conversion-design.md`,
shipped as commits `23abc32..ab5221b` on `main`.

## Context

Phase A fixed the read side: XAML attribute text now converts to WinRT-typed values by
round-tripping through WinUI's own `XamlReader.Load` against a sixteen-entry host table
(`XamlValueConverter.cs`), registered through
`XamlNormalPropertyInfo.GetCustomTypeConverter` (`XamlPropertyInfo.cs:344-354`).

The write side is untouched and still destroys data. `XamlDocument.CreatePropertyValue`
(`XamlDocument.cs:275-320`) emits attribute text only for values whose converter reports
**both** directions (`XamlDocument.cs:284`), and Phase A deliberately ships
`CanConvertTo(string) => false` (Phase A Decision 12). So every WinRT-typed value still
serializes as an empty `XmlElement`: a `Path` loaded with real geometry, after a `SetValue`
through the model, saves as `<PathGeometry />`. Every Polyline, Polygon and Path that Phase 4d's
drawing tools create is unsaveable today.

Phase 5 is the first phase where Save is a real user operation. This is its blocking prerequisite.

## Verified against the current codebase

Every claim below was measured this session against the tree at `ab5221b`, on the WinUI
dispatcher thread, through a throwaway NUnit fixture run via
`WinUIDesigner.XamlDom.Tests.exe --noresult` and deleted afterwards.

| Probe | Result |
|---|---|
| `XamlNormalPropertyInfo.GetCustomTypeConverter(typeof(object))` | `System.ComponentModel.StringConverter`, `CanConvertFrom(string)=True`, `CanConvertTo(string)=True` |
| That converter's `ConvertToInvariantString(new Button())` | `'Microsoft.UI.Xaml.Controls.Button'` |
| `XamlValueConverter.TryCreate(typeof(Geometry))` | non-null |
| `XamlValueConverter.TryCreate(typeof(PathGeometry))` | **null** — the host table matches exactly, with no base-type walk |
| `TypeConverter.CanConvertTo(Type)`, the one-argument overload | passes `context == null` through to the two-argument override |
| `TypeDescriptor.GetConverter(typeof(Button))` as the first call in a process | `TypeConverter`, `from=False`, `to=True` |
| `TypeDescriptor.GetConverter(<Button instance>)` as the first *instance*-overload call in a process | throws `TargetInvocationException` → `FileNotFoundException: Could not load file or assembly 'System.Windows.Forms'` |
| The same call, second time in the same process | succeeds, returns `TypeConverter` |
| `TypeDescriptor.GetConverter(<boxed Thickness>)` | `TypeConverter`, `from=False`, `to=True` |
| `new Thickness(1,2,3,4).ToString()` | `'1,2,3,4'` |

A second probe wrote each of the sixteen types with the formatter expressions this spec
prescribes and read the text back through Phase A's `ConvertFrom`:

| Type | Text written | Read back |
|---|---|---|
| `Thickness(1,2.5,3,4)` | `1,2.5,3,4` | equal |
| `CornerRadius(1,2,3,4)` | `1,2,3,4` | equal |
| `GridLength` Auto / 40 / 2\* | `Auto` / `40` / `2*` | equal |
| `Colors.Blue` | `#FF0000FF` | equal |
| `FontWeights.Bold` | `Bold` | equal |
| `Point(1.5,2.5)` | `1.5,2.5` | equal |
| `Rect(1,2,3.5,4)` | `1,2,3.5,4` | equal |
| `Size(3.5,4)` | `3.5,4` | equal |
| `Duration` 1s / Forever / Automatic | `00:00:01` / `Forever` / `Automatic` | equal |
| `Matrix(1,2,3,4,5.5,6)` | `1,2,3,4,5.5,6` | equal |
| `Vector3(1,2.5,3)` | `1,2.5,3` | equal |
| `FontFamily("Segoe UI")` | `Segoe UI` | `Source` matches |
| `PointCollection` | `1,2 3.5,4` | 2 points, matching |
| `DoubleCollection` | `1,2.5,3` | 3 doubles, matching |
| `SolidColorBrush(Blue)` | `Blue` | `Color == Colors.Blue` |
| `PathGeometry`, all 7 segment types | see the writer section | shape matches, text stable |

**The five reference types compare unequal under `Equals`** — `FontFamily`, `PointCollection`,
`DoubleCollection`, `SolidColorBrush` and `PathGeometry` all measured `equal=False` while
converting correctly, because none implements value equality. Their round-trip tests must compare
content (`Source`, the point sequence, the double sequence, `Color`, the written text), never
`Equals`. A test written the obvious way would fail against a correct implementation.

The eleven named font weights, read off `Microsoft.UI.Text.FontWeights` by reflection:
`Thin=100 ExtraLight=200 Light=300 SemiLight=350 Normal=400 Medium=500 SemiBold=600 Bold=700
ExtraBold=800 Black=900 ExtraBlack=950`.

Under `de-DE`, `double.ToString("R", CultureInfo.InvariantCulture)` still yields `2.5`, and
`TimeSpan.ToString("c", CultureInfo.InvariantCulture)` still yields `00:00:01`, while
`Point.ToString()` yields `1,5;2,5` — which is why Decision 6 forbids the `ToString()` path.

Facts read out of the tree rather than measured:

- The only write entry point a designer tool reaches is `XamlModelProperty.cs:239`
  (`SetValue` → `CreatePropertyValue`). `XamlDesignItem.cs:92,102` uses it for the name only.
- The two `CreateObject` callers that share `CreatePropertyValue`'s body are
  `MarkupExtensionParser.cs:403` (every `{Binding}`/`{StaticResource}`, at parse time) and
  `XamlComponentService.cs:110` (every newly created control).
- `ctx.Instance` is assigned at `XamlDocument.cs:283`, one line before `hasStringConverter` is
  computed at `:284`, and the context is passed to `CanConvertTo` there.
- `hasStringConverter` has a second use at `XamlDocument.cs:302-303`, ungated by `forProperty`.
- `PropertyNode` computes its own `hasStringConverter` at `PropertyNode.cs:231-233` with the
  one-argument overloads, and it gates `IsEnabled` (`:118`) and `ValueString` (`:108-110`).
- Phase A's failure-reporting path exists: `XamlProperty.ReportValueFailure` (`:303`), resolving
  the sink through `parentObject.OwnerDocument.ServiceProvider`, with line info from
  `GetValueLineInfo()` (`:325`).
- The seven `PathSegment` types this codebase constructs, with their exact members, from
  `PathHandlerExtension.cs:330-349`: `LineSegment{Point}`, `PolyLineSegment{Points}`,
  `BezierSegment{Point1,Point2,Point3}`, `PolyBezierSegment{Points}`,
  `QuadraticBezierSegment{Point1,Point2}`, `PolyQuadraticBezierSegment{Points}`,
  `ArcSegment{Point,Size,RotationAngle,IsLargeArc,SweepDirection}`. Containers:
  `PathGeometry{FillRule,Figures}`, `PathFigure{StartPoint,IsClosed,IsFilled,Segments}`
  (`GrayOutDesignerExceptActiveArea.cs:94-109`, `PathHandlerExtension.cs:126-133`).

## Decisions already made

**1. Resolve the write-side converter through `XamlValueConverter.TryCreate(forProperty.ReturnType)`,
not through `GetCustomTypeConverter`.**
Phase A's spec recorded the opposite, and it is wrong. `GetCustomTypeConverter(typeof(object))`
returns `StringConverter`, which reports both directions, so any property declared `object` —
`ContentControl.Content` above all — would take the attribute-text branch and serialize a nested
control as the literal string `Microsoft.UI.Xaml.Controls.Button`. Both values are measured above.
*Cost if wrong:* saving a document silently replaces whole subtrees with a type name — strictly
worse than the bug being fixed.

**2. Key the lookup on the declared property type, never the instance type.**
`TryCreate` matches the host table exactly: `Geometry` resolves, `PathGeometry` does not
(measured). The instance under a `Data` property is always a `PathGeometry`, so an
instance-keyed lookup misses the exact case that destroys data today. This also makes the read
table and the write table the same table by construction, which is what Phase A's note asked for.
*Cost if wrong:* the headline defect survives the fix that was written to remove it.

**3. Answer `CanConvertTo(string)` from the instance when there is one, from the type when there
is not.**
`XamlDocument.cs:283` assigns `ctx.Instance` before asking, and the one-argument overload
`PropertyNode` uses passes a null context (both measured). So: null context → answer for the
target type, which is what enables the PropertyGrid text box; non-null context with an `Instance`
→ answer for that instance. A `LinearGradientBrush` under a `Brush`-declared property answers
`false`, `hasStringConverter` stays false, and serialization falls through to today's element
path.
*Cost if wrong:* either the PropertyGrid stays disabled for all sixteen types, or an
unformattable instance reaches `ConvertTo`.

**4. Never throw from the write path, and achieve that structurally rather than with a catch.**
Decision 3 means `ConvertTo` is only ever reached for an instance `CanConvertTo` already
approved, so there is no failure left to throw. This matters because `PlacementOperation` opens
its `ChangeGroup` at `PlacementOperation.cs:114` and calls `Abort()` only from `Start`'s catch
(`:49,59,92,97`) and from `Abort`/`Cancel` (`:252`) — never around `SetPlacement`. A throw from a
`Margin` write, reached through `PointerMoved` → `MoveLogic.Move` with no try/catch, strands the
transaction: `Undo()` then fails permanently, the next unrelated group's commit fails, and model
and view desynchronise. There is no `Application.UnhandledException` handler in `src/`, so the
process terminates.
*Cost if wrong:* a drag gesture over an unsupported property type kills the designer.

**5. Gate every new behaviour on `forProperty != null`.**
`CreatePropertyValue` is also `CreateObject`'s body. `MarkupExtension` is not a
`DependencyObject` and no markup extension type is in the host table, so the gate is what keeps
`MarkupExtensionParser.cs:403` on exactly its current path. The reference guards both of its own
special cases the same way (`XamlDocument.cs:285,306`).
*Cost if wrong:* a document containing any `{Binding}` becomes unopenable.

**6. Write a real formatter for all sixteen table types. Never rely on the base
`TypeConverter`'s `ToString()` fallback.**
`TypeConverter.ConvertTo` ignores the culture it is handed and calls `value.ToString()`, so
`ConvertToInvariantString` does not make a culture-dependent `ToString()` safe. Phase A measured
`Point.ToString()` under `de-DE` as `'1,5;2,5'` and recorded that `Point` through
`ConvertToInvariantString` was never measured. Writing every formatter removes the unmeasured
dependency instead of documenting it.
*Cost if wrong:* documents saved under a comma-decimal locale are corrupt and the corruption is
invisible until reopened.

**7. The format table is the host table.** Sixteen entries, the same sixteen types, keyed the same
way. No type gains a write path without a read path to verify it against, and every formatter's
primary test is a round trip through Phase A's `ConvertFrom`.
*Cost if wrong:* the two tables drift and a value can be written that cannot be read back.

**8. `Geometry` formats `PathGeometry` only; other geometry types keep the element form.**
`RectangleGeometry`, `EllipseGeometry`, `LineGeometry` and `GeometryGroup` have no representation
in the path mini-language, and rewriting a `<RectangleGeometry/>` the user authored into an
equivalent path string would change the type recorded in their document. Fixing a data-loss bug
by mutating data is not a fix. A `PathGeometry` carrying a non-null `Transform` also answers
`false`, for the same reason: the mini-language cannot express it.
*Cost if wrong:* saves rewrite documents into a shape the user never authored.

**9. `Brush` formats `SolidColorBrush` only, preserving the existing named-colour output.**
`ColorBrushDictionary` behaviour at `XamlDocument.cs:287-290` is what `RoundTripTests` asserts
today; the formatter reproduces it rather than replacing it. Gradient brushes answer `false`.
*Cost if wrong:* a passing round-trip test starts emitting hex where it emitted `Blue`.

**10. `FontWeight` formats only the eleven named weights.**
Phase A recorded that the numeric form is unrepresentable on the read side. A weight whose
`Weight` value matches no `FontWeights` name answers `false` rather than emitting a number the
reader cannot parse back.
*Cost if wrong:* a saved document fails to reload the value it just wrote.

**11. Replace `TypeDescriptor.GetConverter(instance)` with the `Type` overload at
`XamlDocument.cs:281`.**
Measured: the instance overload throws `FileNotFoundException` for `System.Windows.Forms` on its
first call in a process and succeeds on every call after; the `Type` overload never throws, even
as the process's first call. Test processes survive it only because something trips the one-time
initialization before `CreatePropertyValue` runs. In Phase 5's shell — a fresh process whose
first action may be a drop or a save — it throws. This is unrelated to value conversion but it
lives on the exact line being changed.
*Cost if wrong:* the first `CreateObject` in a fresh process throws
`TargetInvocationException`, and the caller has no handler.

**12. Report a value that cannot be formatted through `IXamlErrorSink`, and still serialize it
the way it is serialized today.**
Phase A's `ReportValueFailure` path is reused. The element form is left exactly as it is: this
decision adds a report, it does not change any byte that is written.
*Cost if wrong:* noise in the error sink for types nobody was going to save anyway.

**13. Accept that the PropertyGrid becomes editable for the sixteen types.**
`PropertyNode.cs:231-233` gates `IsEnabled` and `ValueString` on both directions, so Decision 3
turns the text box on. Phase A's stated hazard — a user tabbing through the grid rewriting a font
to `Microsoft.UI.Xaml.Media.FontFamily` — is removed by Decision 6, because every formatter's
output is required to round-trip through `ConvertFrom`.
*Cost if wrong:* a grid edit commits a value the reader parses differently from what the user
typed.

**14. `Vector2` stays out.** It has no host-table entry (Phase A known gap), so a formatter for it
could not be round-trip tested. Phase A's note listed it among the formatters to write; that note
predates the table being finalised.
*Cost if wrong:* a `Vector2`-typed property still saves as an empty element, and now also reports.

## Architecture

Three files change and two are added. The dependency direction
`Designer → DesignModel → XamlDom` is untouched; every change is inside `WinUIDesigner.XamlDom`.

```
XamlModelProperty.SetValue (:239)
        |
        v
XamlDocument.CreatePropertyValue (:275)
        |  forProperty != null ?  XamlValueConverter.TryCreate(forProperty.ReturnType)
        |                      ?? TypeDescriptor.GetConverter(elementType)      <- Decision 11
        v
XamlValueConverter.CanConvertTo(ctx, string)
        |  ctx?.Instance == null -> XamlValueFormatter.HasFormatter(targetType)
        |  otherwise             -> XamlValueFormatter.CanFormat(ctx.Instance)
        v
XamlValueConverter.ConvertTo -> XamlValueFormatter.Format(instance)
        |                            +- Geometry -> GeometryMiniLanguageWriter.Write(PathGeometry)
        v
XamlTextValue                       (attribute text; the value survives the save)

... CanConvertTo false -> today's element path, plus XamlProperty.ReportValueFailure
```

**New: `src/WinUIDesigner.XamlDom/XamlValueFormatter.cs`** (tabs, no SharpDevelop header — new
code). `internal static class` holding the sixteen-entry format table keyed by the same types as
the host table, plus `HasFormatter(Type)`, `CanFormat(object)` and `Format(object)`. Each entry
is a pair: a predicate over the instance and a formatter producing invariant text.

**New: `src/WinUIDesigner.XamlDom/GeometryMiniLanguageWriter.cs`** (tabs). Writes a
`PathGeometry` as path mini-language. It is its own file because it is the only entry that is an
algorithm rather than a `string.Format`, and `XamlValueConverter.cs` is already 278 lines.

**Modified: `XamlValueConverter.cs`.** `CanConvertTo` implements Decision 3 and `ConvertTo`
delegates to the formatter. The remark at `:128-139` explaining why `CanConvertTo` is false is
replaced by one explaining the two-answer rule.

**Modified: `XamlDocument.cs`.** Line 281 becomes the resolution above; the report from Decision
12 is raised where `hasStringConverter` is false and `forProperty != null`. `:302-303` is not
touched: it is reachable only when the first branch did not return, which for a non-null
`forProperty` means `hasStringConverter` was false, so the new converter cannot influence it.

### The format table

Sixteen entries. Text column is the canonical output; every one is required to satisfy
`ConvertFrom(Format(v)).Equals(v)`.

| Type | Instance predicate | Text |
|---|---|---|
| `Thickness` | always | `L,T,R,B` |
| `CornerRadius` | always | `TL,TR,BR,BL` |
| `GridLength` | always | `Auto`, `40`, `2*`, `*` |
| `Windows.UI.Color` | always | `#AARRGGBB` |
| `Brush` | `is SolidColorBrush` | named colour when `ColorBrushDictionary` has it, else `#AARRGGBB` |
| `FontFamily` | `Source` non-empty | `Source` |
| `FontWeight` | `Weight` matches a `FontWeights` name | that name |
| `Point` | always | `X,Y` |
| `PointCollection` | always | `x1,y1 x2,y2` |
| `DoubleCollection` | always | `d1,d2,d3` |
| `Geometry` | `is PathGeometry` with null `Transform` | mini-language |
| `Rect` | always | `X,Y,Width,Height` |
| `Size` | always | `Width,Height` |
| `Duration` | always | `00:00:01`, `Forever`, `Automatic` |
| `Matrix` | always | `M11,M12,M21,M22,OffsetX,OffsetY` |
| `Vector3` | always | `X,Y,Z` |

Every number is written with `CultureInfo.InvariantCulture`. No entry calls `ToString()` without
an explicit invariant format.

### The mini-language writer

`FillRule` first, always explicit: `F0` for `EvenOdd`, `F1` for `Nonzero`. Then each figure:
`M startPoint`, its segments, and `Z` when `IsClosed`. `IsFilled == false` has no mini-language
representation, so a figure with `IsFilled == false` makes the whole geometry answer `false` from
the predicate, the same as a non-null `Transform`.

| Segment | Output |
|---|---|
| `LineSegment` | `L x,y` |
| `PolyLineSegment` | one `L x,y` per point |
| `BezierSegment` | `C p1 p2 p3` |
| `PolyBezierSegment` | one `C p p p` per group of three points |
| `QuadraticBezierSegment` | `Q p1 p2` |
| `PolyQuadraticBezierSegment` | one `Q p p` per pair of points |
| `ArcSegment` | `A w,h rotationAngle isLargeArc sweepDirection x,y` |

`isLargeArc` is written `1`/`0`; `sweepDirection` is `1` for `Clockwise` and `0` for
`Counterclockwise`. An unknown `PathSegment` subtype makes the predicate answer `false` rather
than dropping the segment — a silently shortened path is the failure mode this whole phase exists
to remove.

**One command per resulting segment, never a run of coordinates.** WinUI's mini-language parser
expands a coordinate run into individual segments: measured, `L20,5 30,10` reads back as two
`LineSegment`s, not one `PolyLineSegment`, and the same holds for `C` and `Q`. Emitting the run
form therefore produces text that changes on the next save even when nothing was edited. Emitting
one command per segment makes the writer a fixed point — measured over a figure carrying all
seven segment types: the text written from the reloaded geometry is byte-identical to the text
that produced it.

A worked example, measured end to end, is the canonical-form assertion this writer owes its
tests:

```
F1 M0,0 L10,0 L20,5 L30,10 C40,0 50,20 60,10 C70,0 80,20 90,10 Q100,0 110,10 Q120,0 130,10 A5,7.5 30 1 1 140,10 Z
```

## Testing / definition of done

Tests live in `tests/WinUIDesigner.XamlDom.Tests/ValueFormattingTests.cs` (4 spaces, matching its
siblings). Every WinUI object is created inside `WinUITestHost.Run(...)`.

1. Sixteen round-trip tests, one per table entry: `ConvertFrom(Format(v))` equals `v` for the
   eleven value types, and matches on content for the five reference types named above.
2. Sixteen canonical-text assertions, one per entry, against the exact strings above.
3. The whole round-trip set repeated under `de-DE` (`CultureInfo.CurrentCulture` swapped and
   restored in a `finally`), proving Decision 6.
4. A `PathGeometry` covering all seven segment types in one figure: its written text equals the
   canonical string in the writer section, and writing the reloaded geometry reproduces that same
   text byte for byte.
5. `PathGeometry` with `FillRule.Nonzero`, with `IsClosed`, and with two figures.
6. Predicate rejections, each asserting that the element form is still produced and that the sink
   received a report: `LinearGradientBrush`, `RectangleGeometry`, a `PathGeometry` with a
   `Transform`, a `PathFigure` with `IsFilled == false`, a `FontWeight` with an unnamed weight.
7. The Phase 4d case end to end: build a `PathGeometry` the way `DrawPathExtension.cs:54-58`
   builds one, set it through `XamlProperty.SetValue`, save the document, reload it, and assert
   the geometry survives. Same for `Polyline.Points`. These are the tests that would have failed
   before this phase. They live in `WinUIDesigner.XamlDom.Tests`, which cannot reference the
   Designer layer, so the geometry is constructed directly rather than driven through the
   extension.
8. `ContentControl.Content` holding a nested `Button` still serializes as a child element, not as
   text — the regression Decision 1 exists to prevent.
9. A document containing `{Binding}` and `{StaticResource}` still parses — the regression
   Decision 5 exists to prevent.
10. A `SolidColorBrush` set to `Colors.Blue` still saves as `Blue`, not `#FF0000FF`.
11. `PropertyNode.IsEnabled` is true for a `Thickness`-typed property and its `ValueString`
    round-trips through the grid's own setter. `PropertyNode` lives in `WinUIDesigner.DesignModel`,
    so this one test goes in `tests/WinUIDesigner.DesignModel.Tests` rather than the new file.
12. All three suites green via the `.exe` runners with `--noresult`, against the Phase A baseline:
    XamlDom 116 tests (112 passed, 4 pre-existing ignores), DesignModel 75/75, Designer 245/245 —
    plus the new tests, with no test moving from passing to ignored.

## Known gaps this phase accepts

- **A save/reload normalizes poly segments away.** WinUI's mini-language parser expands
  coordinate runs, so a `PolyLineSegment` written by this phase reads back as individual
  `LineSegment`s (measured). The drawn shape is identical and the text is stable, but
  `PathHandlerExtension`'s `PolyLineSegment` handling — its `PolyLineIndex` splitting at
  `PathHandlerExtension.ConvertMenu.cs:169-173` — sees the normalized form after a reload. There
  is no way around it: the mini-language has no poly-segment concept, and the alternative is the
  element form, which is what this phase exists to stop emitting.
- `Vector2` gets neither a host entry nor a formatter (Decision 14).
- The numeric `FontWeight` form stays unrepresentable in both directions.
- `RectangleGeometry`, `EllipseGeometry`, `LineGeometry` and `GeometryGroup` still save as empty
  elements. They now report, which is the whole change for them.
- **A non-`SolidColorBrush` `Brush` (e.g. `LinearGradientBrush`) now serializes as an empty
  element, losing its content — `GradientStops` included — instead of throwing.** Task 3 removed
  `XamlDocument.CreatePropertyValue`'s `throw new NotSupportedException(...)` for this case: the
  write path must never throw (an uncaught exception from a property commit is fatal — no
  `Application.UnhandledException` handler exists in `src/`, and `PlacementOperation` never wraps
  `SetPlacement` in a try/catch), so the only alternative left is to fall through to the generic
  element form, same as every other unhandled type. `WinUIDesigner.XamlDom` has no `XamlWriter`
  equivalent to fall back to for a gradient brush's children (Decision 1's Out of scope still
  holds), so `<LinearGradientBrush />` genuinely has nothing else to write. This is a new
  silent-loss case in the save path — before this task it was a loud, unrecoverable crash instead
  of a silent one, which is worse, but "silent" is still worth naming rather than leaving in a
  code comment.
- Decision 11 fixes a first-call-in-process throw that cannot be regression-tested from inside a
  test process: NUnit's eight parallel workers share one process, and whichever test runs first
  has already tripped the initialization. The measurement is recorded above in place of a test.
- Phase A's remaining gaps are untouched: the self-authored message covers only table types, the
  performance test is an absolute smoke test rather than a regression delta, and the ~4.7 KB of
  native memory per `XamlReader.Load` still wants an open/close soak before Phase 5.

## Out of scope

- Markup extensions, templates and bindings, beyond leaving their path exactly as it is.
- Any general `XamlWriter` replacement. WinUI has no `XamlWriter`; this phase writes sixteen
  types by hand and says so.
- Nested-element serialization for collection-valued properties. Phase A measured the selection
  rule dead: after mutating a collection through its CLR getter — which is how every Phase 4d
  tool builds geometry — `ReadLocalValue` still reports `UnsetValue` for `PathGeometry.Figures`,
  `PathFigure.Segments`, `TransformGroup.Children`, `GradientBrush.GradientStops` and
  `Polyline.Points`. `Style` additionally declares no dependency properties at all in
  `Microsoft.WinUI.dll 1.7.250606001`, so the branch would emit `<Style/>`.
