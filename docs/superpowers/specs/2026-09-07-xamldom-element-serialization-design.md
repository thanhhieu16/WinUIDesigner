# XamlDom Element-Form Serialization — Phase C

**Status:** implemented, `bcd570d..11d1f69` plus the Task 4 regression test.
**Phase A (read side):** `docs/superpowers/specs/2026-09-06-winui-xamldom-value-conversion-design.md`, shipped as `23abc32..ab5221b`.
**Phase B (write side, attribute form):** `docs/superpowers/specs/2026-09-06-winui-xamldom-value-conversion-phase-b-design.md`, shipped as `0d5f6b4..451cac0`.

## Context

Phases A and B made a WinRT-typed value survive a save/reload **when the value has an
attribute text form**. A `Thickness` writes as `"1,2,3,4"`, a `PathGeometry` as mini-language,
a `SolidColorBrush` as `"Blue"`. Sixteen types are in that table
(`XamlValueFormatter.cs:53-177`).

Every other WinRT value still saves as an **empty element** and loses everything. That is this
phase.

`XamlDocument.CreatePropertyValue` builds the element and returns it without ever writing the
instance's own properties into it (`XamlDocument.cs:345-372`). The WPF original filled the same
hole with `System.Windows.Markup.XamlWriter.Save`
(`F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.XamlDom\Project\XamlDocument.cs:249-250`,
carrying its author's own comment `// TODO: this is a hacky fix`, and reached only for `Brush`).
**WinUI 3 has no `XamlWriter` of any kind** — only `XamlReader` — so the port has no API to
call and the machinery has to be written.

This is a port hole, not a scope cut: Phase 5's "Out of scope" excludes changes to the lower
layers "unless implementing the shell exposes a defect in them", and the shell's Save is
exactly what exposes it.

## Verified against the current codebase

Every number and every quoted output below was produced on this machine, against this tree,
by throwaway probes run through `WinUIDesigner.XamlDom.Tests.exe` and deleted afterwards.

### What is lost today

Each row: the value set through `XamlDocument.CreatePropertyValue` the way
`XamlModelProperty.cs:239` sets it, then `XamlDocument.Save`.

| Value set on a `Grid` | Saved as | Lost |
|---|---|---|
| `SkewTransform { AngleX = 30, AngleY = 10 }` | `<SkewTransform />` | both angles |
| `RotateTransform { Angle = 45 }` | `<RotateTransform />` | the angle |
| `ScaleTransform { ScaleX = 2, ScaleY = 3 }` | `<ScaleTransform />` | both scales |
| `TranslateTransform { X = 5, Y = 6 }` | `<TranslateTransform />` | both offsets |
| `LinearGradientBrush` with two `GradientStop`s | `<LinearGradientBrush />` | both stops |
| `RectangleGeometry { Rect = 1,2,3,4 }` | `<RectangleGeometry />` | the rect |
| `ImageBrush` | `<ImageBrush />` | the source |

### One of these is a shipped feature silently destroying user work

`SkewThumbExtension.ApplySkew` sets the transform and stops
(`src/WinUIDesigner.Designer/Extensions/SkewThumbExtension.cs:112-116`):

```csharp
skewTransform = new SkewTransform { AngleX = angleX, AngleY = angleY };
...
this.ExtendedItem.Properties.GetProperty("RenderTransform").SetValue(skewTransform);
```

So: drag the skew thumb, save, reopen — the skew is gone, with no error anywhere.

Rotation survives, but only by accident. `ModelTools.ApplyTransform` writes the angle a second
time **through the model** after setting the transform (`src/WinUIDesigner.Designer/ModelTools.cs:148`):

```csharp
designItem.Properties.GetProperty(transformProperty).SetValue(transform);
var angle = rotateTransform.Angle;
...
designItem.Properties.GetProperty(transformProperty).Value.Properties.GetProperty("Angle").SetValue(angle);
```

That second write is what puts `Angle="45"` in the XML. `ApplySkew` has no counterpart line.

The loss is also **silent**, not merely unreported by accident. `CreatePropertyValue`'s
`IXamlErrorSink` report added in Phase B is gated on
`XamlValueFormatter.HasFormatter(forProperty.ReturnType)` (`XamlDocument.cs:331`), and
`RenderTransform` is declared `Transform`, which is not one of the sixteen table types. So the
transform cases produce no diagnostic at all. Only the `Brush` and `Geometry` cases — whose
declared types *are* in the table — currently warn.

### The read side already understands what this phase intends to write

Parsing `<Grid.RenderTransform><SkewTransform AngleX='30' AngleY='10'/></Grid.RenderTransform>`
yields a live `SkewTransform` reading `30`/`10`, and re-saving that document reproduces the
input byte for byte. Nothing on the parse side needs to change.

### `TypeDescriptor` sees WinRT properties, and sees more than XAML should write

`TypeDescriptor.GetProperties(typeof(SkewTransform))` returns seven entries, identical for the
type and for an instance:

```
AngleX, AngleY, CenterX, CenterY, Inverse, Dispatcher, DispatcherQueue
```

`Inverse` is computed and has no setter in XAML terms; `Dispatcher` and `DispatcherQueue` are
runtime plumbing. A walker driven by `TypeDescriptor` alone would try to serialize all three.

Filtering on **writable and backed by a `DependencyProperty`** removes exactly the junk, on
every type measured. `WinUIStaticMemberLocator.FindDependencyProperty(type, name)`
(`XamlPropertyInfo.cs:100`) supplies the second half:

| Type | Kept by the filter |
|---|---|
| `SkewTransform` | `AngleX, AngleY, CenterX, CenterY` |
| `TransformGroup` | `Children` |
| `LinearGradientBrush` | `EndPoint, StartPoint, ColorInterpolationMode, GradientStops, MappingMode, SpreadMethod, Opacity, RelativeTransform, Transform` |
| `RectangleGeometry` | `Rect, Transform` |
| `GeometryGroup` | `Children, FillRule, Transform` |

So an entry does not need a hand-typed property list; it needs only to name the type. The
filter is what keeps `Inverse`, `Bounds`, `Value`, `Dispatcher` and `DispatcherQueue` out.

### Enum-valued properties already work, with no new formatter entry

`GeometryGroup.FillRule` set to `Nonzero` and written through `CreatePropertyValue` produces
`<GeometryGroup FillRule="Nonzero" />`. `TypeDescriptor.GetConverter` returns an `EnumConverter`
for an enum type, which answers both directions, so Phase B's existing `hasStringConverter`
branch claims it before this phase's code is reached. `SpreadMethod`, `MappingMode`,
`ColorInterpolationMode` and `FillRule` need nothing.

### Nested listed types recurse and round-trip

A `RectangleGeometry` carrying `Rect = 1,2,3,4` and `Transform = RotateTransform { Angle = 45 }`
writes as

```xml
<RectangleGeometry Rect="1,2,3,4">
  <RectangleGeometry.Transform><RotateTransform Angle="45" /></RectangleGeometry.Transform>
</RectangleGeometry>
```

and reparses to a rect of `1,2,3,4` and an angle of `45`.

### `ReadLocalValue` is a working oracle for scalars and useless for collections

On `new SkewTransform { AngleX = 30 }`, comparing `ReadLocalValue(dp)` against
`DependencyProperty.UnsetValue` reports `AngleX` set, `AngleY` and `CenterX` not set. The
codebase already relies on this API for the same kind of question
(`XamlValueConverter.cs:219`).

It does **not** work for collection properties. Measured: `LinearGradientBrush.GradientStops`
reads as *not* locally set on a fresh brush **and still reads as not locally set after two
`GradientStop`s have been added to it**. Same for `TransformGroup.Children` and
`GeometryGroup.Children`. The collection is the property's lazily-created default value and is
mutated in place, so nothing is ever locally set. A writer gated on `ReadLocalValue` alone
would emit no gradient stops at all — the exact bug this phase exists to fix, reintroduced.

### The scalar write path already works through the public API

Given the `XamlObject` that `CreatePropertyValue` returns for a live `SkewTransform`,

```csharp
var ax = obj.FindOrCreateProperty("AngleX");
ax.PropertyValue = document.CreatePropertyValue(30.0, ax);
```

produces `<SkewTransform AngleX="30" AngleY="10" />`. `FindOrCreateProperty` resolves WinRT
properties fine (`XamlObject.cs:459-466` goes through `TypeDescriptor`), and the recursion back
into `CreatePropertyValue` is what routes leaves through `XamlValueFormatter`.

### The collection write path does **not** work through the public API

`XamlProperty.CollectionElements.Add` throws:

```
System.Reflection.TargetInvocationException
 ---> System.Runtime.InteropServices.COMException (0x800F1000): No installed components were detected.
   at Microsoft.UI.Xaml.Media.GradientStopCollection.Insert(Int32 index, GradientStop item)
   at WinUIDesigner.XamlDom.CollectionSupport.Insert(...) CollectionSupport.cs:243
   at WinUIDesigner.XamlDom.CollectionElementsCollection.InsertItem(...) CollectionElementsCollection.cs:91
```

Root cause: that path mutates the **live** collection as well as the XML, and the
`GradientStop`s being written are already members of the live `GradientStopCollection`. WinRT
refuses to insert a child that already has that parent. The same objection applies to
`XamlProperty.PropertyValue`'s scalar path in principle — `SetPropertyValue` ends in
`UpdateValueOnInstance` (`XamlProperty.cs:230`) — but there the write is a self-assignment of
the value already present, which is why the scalar probe passes.

### The parser-side path does work, and round-trips

Building the child's XML node first and then registering it, the way `XamlParser` does — which
is what `ParserAddCollectionElement`'s own doc comment asks for ("Add a collection element that
already is part of the XML DOM", `XamlProperty.cs:597-598`) — produces:

```xml
<Grid.Background>
  <LinearGradientBrush>
    <LinearGradientBrush.GradientStops>
      <GradientStop Color="#FFFF0000" Offset="0" />
      <GradientStop Color="#FF0000FF" Offset="1" />
    </LinearGradientBrush.GradientStops>
  </LinearGradientBrush>
</Grid.Background>
```

Reparsing that document gives a `LinearGradientBrush` with two stops, the first
`#FFFF0000 @ 0`. The live `GradientStopCollection` still holds exactly its original two stops —
nothing was inserted into it.

Two failure modes were measured on the way and both are ordering traps worth recording:
`ParserAddCollectionElement(null, value)` registers the value but never attaches its node, and
calling it with a property element whose children have not been appended first leaves an empty
`<LinearGradientBrush.GradientStops />`. The node must be appended to the property element
**before** the call.

## Decisions

*Cost if wrong* is the failure mode if the decision is mistaken. *Cost of the cut* is what a
deliberate scope reduction denies the user.

**1. The fix lives in `WinUIDesigner.XamlDom`, in a new class, reached from
`CreatePropertyValue`'s element branch.**
The defect is in the bottom layer and every layer above inherits it. A patch in
`SkewThumbExtension` would fix skew alone and leave gradients, geometries and every future
type lost.
*Cost if wrong:* a bug in the bottom layer reaches all four projects. Mitigated by Decision 2's
gate and Decision 5's refusal rule — an unlisted type keeps today's exact behaviour.

**2. An explicit allow-list of *types* decides what is serialized; the *properties* of a listed
type are derived by filtering on "writable and backed by a `DependencyProperty`".**
Two separate jobs. The filter handles junk properties and was measured to do it cleanly on all
five types tried — `Inverse`, `Bounds`, `Value`, `Dispatcher` and `DispatcherQueue` all fail it,
and nothing wanted does. The allow-list handles blast radius: it is what keeps this phase from
attempting to serialize an arbitrary `Storyboard` or `BitmapImage` the first time one appears
in a document. Same shape as `XamlValueConverter`'s and `XamlValueFormatter`'s sixteen-entry
tables, which exist because WinRT metadata carries none of the attributes that would make an
unbounded walk safe.
*Cost of the cut:* a type outside the list still saves as an empty element. It keeps today's
behaviour rather than a new wrong one, and adding an entry is a one-line change.
*Cost if wrong:* if the filter lets something through on an unmeasured type, the output carries
a property XAML cannot read back. The round-trip tests in the DoD are what catch that, which is
why every listed type gets one rather than a "does not throw" assertion.

**3. A scalar property is written only when
`ReadLocalValue(dp) != DependencyProperty.UnsetValue`. A collection property is written when its
live collection is non-null and non-empty.**
Writing every property unconditionally would put `CenterX="0" CenterY="0"` into every saved
transform. But `ReadLocalValue` cannot be the rule for collections: measured, a
`GradientStopCollection` reads as not locally set even after two stops have been added to it,
because the collection is the lazily-created default value and is mutated in place. Gating
collections on `ReadLocalValue` would emit no gradient stops at all.
*Cost if wrong:* a scalar set to its own default value through a path that does not register as
a local value would be dropped. No such path is known for the listed types; the DoD covers the
round-trip for each.

**4. Collections are written through the parser-side path — append the child node to the
property element, then `ParserAddCollectionElement` — never through
`XamlProperty.CollectionElements`.**
Measured above: the public path throws `COMException 0x800F1000` because it re-inserts children
into the live collection they already belong to. Scalars keep the public
`FindOrCreateProperty` + `PropertyValue` path, whose instance write-back is a self-assignment.
*Cost if wrong:* the ordering traps recorded above produce a silently empty property element
rather than an exception, so each collection type needs its own round-trip test, not just a
"does not throw" test.

**5. The writer never throws. A type or value it cannot handle falls through to today's
behaviour.**
Phase B's rule, unchanged and for the unchanged reason: `PlacementOperation` never calls
`Abort()` around `SetPlacement` (`PlacementOperation.cs:114`) and `src/` installs no
`Application.UnhandledException` handler, so a throw from a write during a drag strands the
transaction and ends the process. Every entry point is wrapped; a refusal means the element
form is emitted exactly as it is today.
*Cost if wrong:* a crash during a drag gesture, with the document lost.

**6. Properties are always emitted as an explicit property element, never as content.**
`LinearGradientBrush.GradientStops` is that type's content property and XAML would allow the
children to sit directly inside `<LinearGradientBrush>`. The explicit form was the one measured
to round-trip, and it removes a per-type question.
*Cost of the cut:* saved XAML is more verbose than a hand-written equivalent.

**7. Recursion is depth-limited and cycle-guarded.**
A `TransformGroup` holding itself, or a chain deeper than the limit, refuses under Decision 5
rather than recursing.
*Cost if wrong:* a stack overflow is not catchable and takes the process down.

**8. The table's first version covers the measured losses.**
`SkewTransform`, `RotateTransform`, `ScaleTransform`, `TranslateTransform`, `TransformGroup`,
`LinearGradientBrush`, `GradientStop`, `RectangleGeometry`, `EllipseGeometry`, `LineGeometry`,
`GeometryGroup`.
*Cost of the cut:* `ImageBrush`, `RadialGradientBrush` and the composition brushes stay lost;
they are additive table entries once someone needs them.

**9. Phase B's `IXamlErrorSink` report is re-gated so it fires for what is still lost.**
Today it is gated on `HasFormatter(declared type)`, which is why the transform cases are silent.
After this phase it fires when the writer refuses, which is the accurate condition.
*Cost if wrong:* a false positive on every `{Binding}` if the markup-extension exclusion at
`XamlDocument.cs:328` is not carried over — that exclusion and its reasoning stay as written.

## Architecture

One new file, `src/WinUIDesigner.XamlDom/XamlInstanceWriter.cs`, and a small edit to
`XamlDocument.CreatePropertyValue`.

```
XamlDocument.CreatePropertyValue(parent, instance, forProperty)
    ├─ attribute text form available?          → XamlTextValue          (Phase B, unchanged)
    ├─ XamlInstanceWriter.TryWrite(obj)        → element with children  (this phase)
    └─ neither                                 → empty element + sink report
```

`XamlInstanceWriter` holds the type list and one recursive walk:

- `HasEntry(Type)` — is this type listed. A plain `HashSet<Type>`, since Decision 2 derives the
  properties rather than listing them.
- `TryWrite(XamlObject target, int depth)` — for each property of `target.ElementType` that is
  writable and backed by a `DependencyProperty`, and that Decision 3's rule says is set, either
  - **scalar:** `target.FindOrCreateProperty(name).PropertyValue = document.CreatePropertyValue(value, property)` — recursion lands back in `CreatePropertyValue`, so a leaf goes through `XamlValueFormatter`, an enum goes through Phase B's `hasStringConverter` branch, and a listed nested object comes back here;
  - **collection:** create the `<Owner.Property>` element, append each child's `XmlElement`, then `ParserAddCollectionElement`, then recurse into each child.

  Returns `false` on any refusal, leaving the element as it was.

Property-name-to-`DependencyProperty` resolution, for both the filter and Decision 3's oracle,
uses `WinUIStaticMemberLocator.FindDependencyProperty(Type, string)` (`XamlPropertyInfo.cs:100`).
It already degrades to `null` rather than throwing when the static accessor cannot be read.

## Testing and definition of done

New fixture `tests/WinUIDesigner.XamlDom.Tests/ElementSerializationTests.cs`, run through the
project's `.exe` as CLAUDE.md requires. Every test runs inside `WinUITestHost.Run`.

1. `SkewTransform` with both angles saves with both angles, and reparses to the same values.
2. Same for `RotateTransform`, `ScaleTransform`, `TranslateTransform`.
3. `TransformGroup` holding a rotate and a scale round-trips, children in order.
4. `LinearGradientBrush` with two stops round-trips: stop count, colours, offsets.
5. `RectangleGeometry`, `EllipseGeometry`, `LineGeometry`, `GeometryGroup` round-trip.
6. Unset properties are absent from the output — a `SkewTransform` with only `AngleX` set does
   not emit `CenterX`, and no listed type emits `Inverse`, `Bounds`, `Value`, `Dispatcher` or
   `DispatcherQueue`.
6b. A populated collection is emitted even though `ReadLocalValue` reports it unset. This is
   Decision 3's split, and a writer that gated collections on `ReadLocalValue` would pass every
   other test in this list while emitting no gradient stops.
7. A type outside the table (`ImageBrush`) still emits its empty element, and now reports
   through `IXamlErrorSink`.
8. A `{Binding}` on a property whose declared type is listed still round-trips as a markup
   extension and produces no sink report (Decision 9's regression).
9. A graph of nested `TransformGroup`s deeper than `MaxDepth` refuses to recurse past the cap
   without throwing, and the truncation is asserted, not just the absence of a throw - a
   self-referencing `TransformGroup` could not be built to test with, since no WinUI collection
   API accepts an ancestor as a child the way WPF's would; see Known gaps for the `InProgress`
   guard this leaves unexercised.
10. `ModelToolsTests.ApplyingASecondTransformKindWrapsBothInATransformGroup` and the rest of the
    Designer suite stay green — `ApplyTransform` now writes `Angle` on top of an element that
    already carries it.
11. End-to-end in `WinUIDesigner.Designer.Tests`: apply a skew through `SkewThumbExtension`'s
    own path, save the document, reparse, assert the angles survive. This is the bug that
    started the phase and it gets a test at the layer the user meets it.
12. All four suites green, run from their `.exe`s, on the final tree.

## Known gaps

- The table is the coverage. `ImageBrush`, `RadialGradientBrush`, `AcrylicBrush`,
  `XamlCompositionBrushBase`, `BitmapImage` and every `Storyboard`/animation type stay lost.
- Attached properties on a nested element are not written; no listed type has one.
- `x:Name` on a nested element is not preserved.
- The writer runs on every `SetValue` of an affected property (`XamlDocument.CreatePropertyValue`
  is reached from `XamlModelProperty`'s write path, not from `XamlDocument.Save` -
  `XamlParser` never calls `CreatePropertyValue`), walking the live object each time; no
  caching, and no measurement of its cost has been made. The phase's final review counted the
  order of thirty uncached reflection lookups per pointer move during a skew drag. Deferred
  rather than fixed - it changes no behaviour, and this codebase's rule is that performance
  claims are measured before being acted on; the review's count is not that measurement, and
  none exists yet.
- The `InProgress` reference-cycle guard (Decision 7, `XamlInstanceWriter.cs`) is unexercised by
  any test. A `TransformGroup` cannot be made its own descendant through WinUI's public API - no
  `Children.Add` accepts an ancestor as an argument the way WPF's collection would - so the only
  reachable way to drive the guard is depth, which is what
  `ADeeplyNestedGraphStopsAtTheDepthCapWithoutThrowing` exercises instead. The reference check
  stays as the belt to that test's braces.
- `XamlProperty`'s raw-`Binding`-to-`SetValue` path fails for its own, unrelated, pre-existing
  reason: WinUI's `DependencyProperty.SetValue` does not resolve a `Binding` the way real binding
  application does. `ABindingOnAListedPropertyTypeIsNeitherWrittenNorReported` and
  `ValueFormattingTests.DoesNotReportABindingMarkupExtension` are both shaped around working
  past this rather than fixing it.
- A genuine exception mid-write (not the depth/cycle guard, an actual thrown error) leaves a
  partially written element rather than rolling it back — Decision 5 forbids buffering the
  whole subtree just to discard it on failure, so `TryWrite`'s `catch` reports and stops where
  it stands.
- `SkewThumbExtension` still does not write its angles through the model the way
  `ModelTools.ApplyTransform` writes `Angle`. This phase makes that unnecessary for saving; it
  remains a difference between the two code paths.

## Out of scope

- Any change to `DesignModel`, `Designer` or the shell beyond the test in DoD item 11.
- A general WinUI XAML serializer. This writes the listed types and refuses everything else.
- The remaining Phase A/B gaps: poly segments normalising on reload, `Vector2` having no
  formatter, `IXamlErrorSink.ReportError` still being called with line and column 0.
