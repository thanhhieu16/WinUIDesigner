# XamlDom Element-Form Serialization — Phase C

**Status:** designed, not implemented.
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

### `ReadLocalValue` is a working "was this actually set" oracle

On `new SkewTransform { AngleX = 30 }`, comparing `ReadLocalValue(dp)` against
`DependencyProperty.UnsetValue` reports `AngleX` set, `AngleY` and `CenterX` not set. The
codebase already relies on this API for the same kind of question
(`XamlValueConverter.cs:219`).

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

**2. An explicit allow-list table decides which types are serialized, not reflection over
whatever `TypeDescriptor` returns.**
Same shape as `XamlValueConverter`'s and `XamlValueFormatter`'s sixteen-entry tables, and for
the same reason those exist: WinRT metadata does not carry the attributes that would make a
generic walk safe. The measurement above is the concrete case — `Inverse`, `Dispatcher` and
`DispatcherQueue` are returned for `SkewTransform` and none of them belongs in XAML. Each entry
names the type and the properties to consider.
*Cost of the cut:* a type outside the table still saves as an empty element. It keeps today's
behaviour rather than a new wrong one, and adding an entry is a one-line change.

**3. A property is written only when `ReadLocalValue(dp) != DependencyProperty.UnsetValue`.**
Writing every listed property unconditionally would put `CenterX="0" CenterY="0"` into every
saved transform, and the same noise into every gradient stop.
*Cost if wrong:* a property set to its own default value through a path that does not register
as a local value would be dropped. No such path is known for the listed types; the DoD covers
the round-trip for each.

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

`XamlInstanceWriter` holds the table and one recursive walk:

- `HasEntry(Type)` — is this type listed.
- `TryWrite(XamlObject target, int depth)` — for each listed property of `target.ElementType`
  that reads as locally set on `target.Instance`, either
  - **scalar:** `target.FindOrCreateProperty(name).PropertyValue = document.CreatePropertyValue(value, property)` — recursion lands back in `CreatePropertyValue`, so a leaf goes through `XamlValueFormatter` and a listed nested object comes back here;
  - **collection:** create the `<Owner.Property>` element, append each child's `XmlElement`, then `ParserAddCollectionElement`, then recurse into each child.

  Returns `false` on any refusal, leaving the element as it was.

Table entry shape, mirroring `XamlValueFormatter.Add`:

```csharp
Add(table, typeof(SkewTransform),
    "AngleX", "AngleY", "CenterX", "CenterY");
Add(table, typeof(LinearGradientBrush),
    "StartPoint", "EndPoint", "SpreadMethod", "MappingMode", "Opacity", "GradientStops");
```

Property-name-to-`DependencyProperty` resolution for Decision 3's oracle uses the existing
`XamlProperty.DependencyProperty` (`XamlProperty.cs:690`); a listed property that resolves to
no `DependencyProperty` is written unconditionally if it reads non-null and non-default, or
refused if that cannot be determined.

## Testing and definition of done

New fixture `tests/WinUIDesigner.XamlDom.Tests/ElementSerializationTests.cs`, run through the
project's `.exe` as CLAUDE.md requires. Every test runs inside `WinUITestHost.Run`.

1. `SkewTransform` with both angles saves with both angles, and reparses to the same values.
2. Same for `RotateTransform`, `ScaleTransform`, `TranslateTransform`.
3. `TransformGroup` holding a rotate and a scale round-trips, children in order.
4. `LinearGradientBrush` with two stops round-trips: stop count, colours, offsets.
5. `RectangleGeometry`, `EllipseGeometry`, `LineGeometry`, `GeometryGroup` round-trip.
6. Unset properties are absent from the output — a `SkewTransform` with only `AngleX` set does
   not emit `CenterX`.
7. A type outside the table (`ImageBrush`) still emits its empty element, and now reports
   through `IXamlErrorSink`.
8. A `{Binding}` on a property whose declared type is listed still round-trips as a markup
   extension and produces no sink report (Decision 9's regression).
9. A self-referencing `TransformGroup` refuses without throwing and without recursing.
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
- A property whose value is a nested object *outside* the table refuses the whole parent, so a
  `TransformGroup` containing an unlisted transform saves as an empty `<TransformGroup />`
  rather than partially.
- The writer runs on every save of an affected property, walking the live object each time; no
  caching, and no measurement of its cost has been made.
- `SkewThumbExtension` still does not write its angles through the model the way
  `ModelTools.ApplyTransform` writes `Angle`. This phase makes that unnecessary for saving; it
  remains a difference between the two code paths.

## Out of scope

- Any change to `DesignModel`, `Designer` or the shell beyond the test in DoD item 11.
- A general WinUI XAML serializer. This writes the listed types and refuses everything else.
- The remaining Phase A/B gaps: poly segments normalising on reload, `Vector2` having no
  formatter, `IXamlErrorSink.ReportError` still being called with line and column 0.
