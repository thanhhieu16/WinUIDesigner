# XamlDom Value Conversion, Phase A (read side) — Design Spec

## Context

`WinUIDesigner.XamlDom` cannot convert a XAML attribute string to any WinRT-typed value. Only
`double`, `enum` and `string` parse; every other attribute value silently becomes the property
default. That includes `Margin`, `Padding`, `Background`, `FontFamily`, `FontWeight`,
`CornerRadius`, `GridLength` star-sizing, `Color`, `Point`, `PointCollection`,
`DoubleCollection`, `Rect`, `Duration`, `Matrix` and `Path.Data`. The designer therefore renders
any real hand-authored XAML file wrong, and has done so since Phase 1.

The defect was first recorded during Phase 4d as a narrow "`Point`/`PointCollection`/`PathGeometry`
do not round-trip" note. It is far wider than that, and the read half is the more damaging half:
an untouched document still saves losslessly (XamlDom preserves the original XML text), so nothing
is destroyed on disk until a property is set through the model.

This spec covers **Phase A — the read side only**. The write side is deliberately deferred to a
separate Phase B spec (Decision 13), because the read fix changes rendering behaviour across the
existing suites and that blast radius should be settled on its own before write-side changes are
mixed in.

**Origin of the design.** The design below was reviewed by two independent adversarial reviewers
before being written. Both found real defects in the first draft; the decisions here are the
corrected form. Where a decision exists because a reviewer falsified an earlier assumption, that
is stated in the decision.

## Reference source

The SharpDevelop original (`F:\ReferencesCode\CSharp\WpfDesigner`, read-only) has **no equivalent
mechanism to port**, and that is the point: WPF shipped a real `TypeConverter` on every one of
these types, so `TypeDescriptor.GetConverter(ReturnType)` simply worked.

- `WpfDesign.XamlDom\Project\XamlPropertyInfo.cs:75` — the reference's
  `XamlDependencyPropertyInfo.TypeConverter` is the identical
  `TypeDescriptor.GetConverter(ReturnType)`, with no custom hook. The port's divergence is forced
  by the platform, not by taste.
- `WpfDesign.XamlDom\Project\XamlDocument.cs:227,246` — both of the reference's special cases in
  `CreatePropertyValue` are guarded by `forProperty != null`. That guard matters for Phase B
  (Decision 11).

## Verified against the current codebase

Everything in this section was measured on this machine against this tree, not assumed. Probes
were throwaway and are not committed.

### The converter inventory

Reflecting every public `DependencyObject`-derived type in the WinUI assembly yields 363 distinct
property types. Asking `TypeDescriptor.GetConverter(t)` about each:

- **0** types have no converter at all
- **0** types are from-string-only
- **115** types are **to-string only**

`CanConvertTo(string) == true` is a trap: the base `System.ComponentModel.TypeConverter` returns
true for it and implements it as `ToString()`, which yields `"Microsoft.UI.Xaml.Media.PathGeometry"`.
The only direction parsing needs — `CanConvertFrom(string)` — is the one that is missing.

### Actual behaviour through this codebase's parser

XAML text → `XamlParser.Parse(new StringReader(xaml))` → read the value back off
`doc.RootInstance`; then `doc.Save(XmlWriter)` and check the attribute survived.

```
Thickness (Border.Padding)                  READ FAIL(got '0,0,0,0' want '1,2,3,4')             SAVE ok
CornerRadius (Border)                       READ FAIL(got '0,0,0,0' want '5')                   SAVE ok
GridLength (ColumnDefinition.Width)         READ FAIL(got '1*' want '2*')                       SAVE ok
Color (SolidColorBrush.Color)               READ FAIL(got '#00000000' want '#FF102030')         SAVE ok
FontFamily (TextBlock)                      READ FAIL(got 'Segoe UI Variable' want 'Consolas')  SAVE ok
Point (UIElement.RenderTransformOrigin)     READ FAIL(got '0,0' want '0.5,0.5')                 SAVE ok
PointCollection (Polyline.Points)           READ FAIL(got '0' want '3')                         SAVE ok
DoubleCollection (Shape.StrokeDashArray)    READ FAIL(got '0' want '2')                         SAVE ok
Geometry mini-language (Path.Data)          READ FAIL(got '' want 'PathGeometry')               SAVE ok
Rect (RectangleGeometry.Rect)               READ FAIL(got '0,0,0,0' want '1,2,3,4')             SAVE ok
Duration (DoubleAnimation)                  READ FAIL(got 'Automatic' want '0:0:1')             SAVE ok
Matrix (MatrixTransform)                    READ FAIL(got 'Identity' want '1,0,0,1,5,6')        SAVE ok
FontWeight (TextBlock)                      READ FAIL(got '400' want '700')                     SAVE ok
Brush (Grid.Background='Red')               READ FAIL(got '' want '#FFFF0000')                  SAVE ok
Brush (Grid.Background='#FF102030')         READ FAIL(got '' want '#FF102030')                  SAVE ok
Margin (FrameworkElement)                   READ FAIL(got '0,0,0,0' want '4,5,6,7')             SAVE ok
CONTROL double (Grid.Width)                 READ ok                                             SAVE ok
CONTROL enum (Grid.HorizontalAlignment)     READ ok                                             SAVE ok
CONTROL string (Button.Content)             READ ok                                             SAVE ok
```

"SAVE ok" everywhere confirms that an untouched document round-trips losslessly.

### `XamlReader.Load` performs the conversion correctly

```
OK   Point via XamlReader (LineGeometry.StartPoint) -> LineGeometry
OK   PointCollection via XamlReader (Polyline.Points) -> Polyline   (Count=3; [0]=1,2 [2]=5,6)
OK   PathGeometry mini-language via XamlReader (Path.Data) -> Path  (Figures=1 Segments=1 Closed=True)
OK   Thickness via XamlReader (Border.Padding) -> Border
```

Cost: **0.093 ms** per `XamlReader.Load` of a small element (1000 iterations = 93 ms) measured on a
`Grid`; a per-host sweep by the read-side reviewer put the spread at **0.057 ms** (`Border.Padding`)
to **0.104 ms** (`Hub.BorderThickness`), and **0.263 ms** for a `Path.Data` with 200 segments.
Attached properties convert correctly through the same mechanism
(`<Grid><Button Grid.Row='2'/></Grid>` reads back `2`).

### A reflection-built host map picks hosts that cannot be activated

Building `T → (host, property)` by reflecting the WinUI assembly (concrete, parameterless-
constructible `DependencyObject` types in the default-XAML-namespace namespaces, ordered by
shortest type name) gives 238 entries, of which two of fifteen probed targets throw:

```
GridLength   host=TwoPaneView.Pane1Length          -> THREW XamlParseException
Color        host=WebView2.DefaultBackgroundColor  -> THREW XamlParseException
```

The read-side reviewer established the cause is not a "bad pick" in the aesthetic sense: the host
type could not be **resolved or activated in this process**, and the exception is identical to the
one a bad value produces. Host viability depends on the process's `IXamlMetadataProvider` and
available theme resources — a bare `<TextBox/>` also fails to load in this app host, while
`<Border/>`, `<Grid/>`, `<Path/>`, `<Polygon/>`, `<Line/>`, `<Storyboard/>`, `<MatrixTransform/>`,
`<SolidColorBrush/>`, `<ColumnDefinition/>` and `<TextBlock/>` all succeed.

### Facts that shaped specific decisions

- **`MarkupExtension` is not a `DependencyObject`.** Base chain is `MarkupExtension -> Object`.
- **Markup-extension text can reach a `XamlTextValue`.** The attribute form is protected —
  `XamlParser.cs:751-752` routes any value starting with `{` (and not `{}`) to
  `MarkupExtensionParser` — but the **property-element** form is not: `ParseValueCore`
  (`XamlParser.cs:510-523`) turns any `XmlText` into a `XamlTextValue` unconditionally, so
  `<Border.Padding>{StaticResource K}</Border.Padding>` arrives as literal text. Separately,
  `XamlTextValue.Text` strips a leading `{}` (`XamlTextValue.cs:73-77`), so `'{}{Binding}'` is
  handed on as a live `'{Binding}'`. (Read-side reviewer, probe-verified.)
- **An isolated fragment resolves `{StaticResource}`/`{ThemeResource}` against
  `Application.Current.Resources`** — the designer's own resource scope, not the document's. It is
  not an empty context, it is the *wrong* context. (Read-side reviewer, probe-verified.)
- **`XamlParseException`'s message is poisoned and sticky**: the first failure's message in a
  process is repeated for every later failure, across intervening successes. The `Line`/`Position`
  refer to the synthetic fragment, not the user's file. (Read-side reviewer, probe-verified.)
- **`UIElement.Clip` is declared `RectangleGeometry`** yet WinUI runs the Geometry mini-language on
  it: `Clip='M0,0 L10,10'` loads a `PathGeometry` into a `RectangleGeometry`-typed property and
  reading it throws `InvalidCastException`; `Clip='1,2,3,4'` is rejected. (Read-side reviewer,
  probe-verified.)
- **`ImageSource` would start network fetches.** `<Image Source='http://…/x.png'/>` through
  `XamlReader.Load` constructs a `BitmapImage` and sets `UriSource`, beginning a fetch.
  `XamlParser.cs:850-856` routes `ImageSource` through `TypeFinder.ConvertUriToLocalUri` first, but
  the base implementation is `return uri;` (`XamlTypeFinder.cs:417-420`) and nothing overrides it.
- **`object` and `Type` both report `CanConvertFrom(string) == false`**, so a naive
  "no from-string converter ⇒ use the new one" rule would displace the existing
  `StringTypeConverter`/`TypeTypeConverter` entries and break `Button.Content="hi"`, which works
  today. (Read-side reviewer, probe-verified.)
- **`Nullable<T>` property types are real**: `PointAnimation.From` is `Point?`,
  `ColorAnimation.From` is `Color?`, `ToolTip.PlacementRect` is `Rect?`. All resolve to
  `NullableConverter` with `CanConvertFrom(string) == false`. `Nullable<double>` reports `true` and
  must not be touched.
- **Enums and primitives are correctly excluded** by a `CanConvertFrom(string)` gate:
  `HorizontalAlignment → EnumConverter`, `double → DoubleConverter`, `string → StringConverter`,
  `Uri → UriTypeConverter` all report `true`.
- **`ReadLocalValue` discriminates set from unset**, including collection-typed DPs: a fresh
  `Border` reports `UnsetValue` for `Padding`, and reports a `Thickness` after assignment.
- **`PropertyNode` gates an editable text box on both directions.**
  `hasStringConverter = TypeConverter.CanConvertFrom(typeof(string)) && CanConvertTo(typeof(string))`
  (`src/WinUIDesigner.DesignModel/PropertyGrid/PropertyNode.cs:231-233`) drives `IsEnabled`
  (`:118`) and `ValueString` (`:108-110`, `ConvertToInvariantString`).

### Code facts

- `XamlPropertyInfo.cs:123-138` — `abstract class XamlPropertyInfo` with
  `public abstract TypeConverter TypeConverter { get; }`, the single choke point every attribute
  conversion passes through.
- `XamlPropertyInfo.cs:306` — `XamlNormalPropertyInfo.TypeConverter` returns
  `GetCustomTypeConverter(_propertyDescriptor.PropertyType) ?? _propertyDescriptor.Converter`.
- `XamlPropertyInfo.cs:344-354` — `GetCustomTypeConverter(Type)`, today handling only `object`,
  `Type` and `DependencyProperty`.
- `XamlPropertyInfo.cs:175` — `XamlDependencyPropertyInfo.TypeConverter`, which does **not**
  consult the hook.
- `XamlParser.cs:545-560` — `FindProperty` tries `TypeDescriptor.GetProperties(type)[name]` first
  and constructs `XamlNormalPropertyInfo` at `:557`.
- `XamlParser.cs:599` — the **only** place a `XamlDependencyPropertyInfo` is constructed during
  parsing, always with `isAttached: true`. The one other construction is
  `XamlDocument.cs:392` for the `Ignorable` markup-compatibility property.
- `XamlParser.cs:849-861` — `CreateObjectFromAttributeText` calls
  `targetProperty.TypeConverter.ConvertFromString(context, CultureInfo.InvariantCulture, text)`.
- `XamlProperty.cs:244-271` — `UpdateValueOnInstance()`; the conversion failure is swallowed at
  `:269-271` with `Debug.WriteLine`.
- `IXamlErrorSink.cs` — `void ReportError(string message, int line, int column)`, wired at
  `XamlParser.cs:97,129,235-237` and `XamlDesignContext.cs:55,75`.
- `TemplateHelper.cs:102` — existing precedent for calling `XamlReader.Load` inside this library.
- `XamlParser.cs:145-190` — `EnsureTypeDescriptorInitialized()`; the first
  `TypeDescriptor.GetConverter`/`GetProperties` call against a WinRT-backed instance in a process
  throws `FileNotFoundException: Could not load file or assembly 'System.Windows.Forms'`, one-shot
  and cached, so it is warmed deliberately.

### Measured blast radius

A throwaway spike registered a faithful read-side converter (brace rejection, `ReadLocalValue`
check, assignability assert, `ImageSource`/`Uri` exclusion, `CanConvertTo(string) => false`,
16-entry host table) through `GetCustomTypeConverter` with nothing else changed, and ran all three
suites from their `.exe` runners:

| Suite | Result | vs baseline |
|---|---|---|
| XamlDom | 70: 66 passed, 4 pre-existing ignores | unchanged |
| DesignModel | 75/75 | unchanged |
| Designer | 245: 243 passed, **1 failed** | −1 |

The full Designer run reported two failures, but both stack traces pointed at
`ModelToolsArrangeItemsTests.cs:103`; run in isolation `AdornerLayerTests` passes 3/3, so NUnit
misattributed a name under parallel workers. **Exactly one test actually fails.**

That test — `ModelToolsArrangeItemsTests.ArrangeInGridSetsMarginLeftWhenNotRightAligned`
(`tests/WinUIDesigner.Designer.Tests/ModelToolsArrangeItemsTests.cs:78-103`) — is vacuous. It loads
a `Button` with `Margin="5,5,0,0"`, arranges a **single** item left, and asserts `margin.Left == 0`.
It passes today only because `Margin` never parsed, leaving the button at `0,0,0,0`. With `Margin`
real the button sits at x=5 and a single-item arrange is a no-op against its own bounds — which the
sibling test `ArrangeSingleItemIsANoOpDeltaAgainstItself` (`:65-76`) already documents as the
correct behaviour. The spike exposes a wrong assertion, not a regression.

The spike was reverted; the working tree is clean.

## Decisions already made

1. **Convert by delegating to WinUI's own parser.** A `XamlValueConverter : TypeConverter` builds
   `<Host P='{XML-escaped text}'/>` in the presentation namespace, calls
   `Microsoft.UI.Xaml.Markup.XamlReader.Load`, and reads `P` back off the loaded object. The
   platform performs the conversion, so semantics are exact for `"Red"`, `"2*"`, `"Auto"`,
   `"M0,0 L10,10"` and every other grammar, with no hand-written parsing.
   *Cost if wrong:* every converted value is wrong in the same way; caught immediately by the
   per-type tests.

2. **Hand-list the host table; never build it by reflection.** Reflection selects `TwoPaneView` and
   `WebView2`, which cannot be activated. Prefer the lightest viable host (`Border`, not `Hub` —
   ~1.8× faster and structurally simpler).
   *Cost if wrong:* a type silently has no converter and stays broken; caught by the per-entry
   tests.

3. **Self-check host viability at runtime.** Before first use, load `<Host/>` bare once per host,
   cache the outcome, and on failure report "host unusable in this process" rather than "bad
   value". Host viability depends on the hosting process, so a host proven in
   `WinUIDesigner.XamlDom.Tests` is not thereby proven in the Phase 5 shell app.
   *Cost if wrong:* a shell-app-only failure is misreported as a document error.

4. **Register through `GetCustomTypeConverter`, with the existing entries matched first.** Order:
   `object` → `StringTypeConverter`; `Type` → `TypeTypeConverter`; `DependencyProperty` →
   `DependencyPropertyConverter`; then the host-table lookup. Both `object` and `Type` report
   `CanConvertFrom(string) == false`, so any rule that consults the host table first regresses
   `Button.Content="hi"`.
   *Cost if wrong:* three working conversions break; caught by a guard test on each.

5. **Key the table on the exact `ReturnType`, never a base type, and assert assignability.**
   `UIElement.Clip` is `RectangleGeometry` but WinUI applies the Geometry mini-language to it, so a
   base-type fallback would return a `PathGeometry` that cannot be assigned. The converter asserts
   `targetType.IsInstanceOfType(result)` before returning.
   *Cost if wrong:* an `InvalidCastException` deep inside parsing instead of a reported error.

6. **Unwrap `Nullable<T>` via `Nullable.GetUnderlyingType` rather than adding nullable table rows.**
   `NullableConverter` already delegates to the underlying type's converter; per-nullable rows
   would double the table and still miss combinations.
   *Cost if wrong:* animation `From`/`To` and `ToolTip.PlacementRect` stay broken.

7. **Reject markup-extension text at the converter boundary.** Text starting with `{` and not `{}`
   is refused before a fragment is built. The parser's attribute-form guard is not sufficient: the
   property-element form and the `{}`-stripping path both deliver live extension text. Without
   this, `'{}{Binding}'` converts to a silent default with no error — the exact failure mode this
   phase exists to remove — and `{StaticResource K}` resolves against the designer's own
   `Application.Current.Resources`.
   *Cost if wrong:* the designer renders documents using its own theme values and says nothing.

8. **Treat "the value was ignored" as a failure.** After loading the fragment, if the host
   property's `ReadLocalValue` is `UnsetValue`, the conversion failed. WinUI does not always throw
   on a value it cannot use, and a successful conversion *to* the default is otherwise
   indistinguishable from silent rejection.
   *Cost if wrong:* a legitimate value that happens to equal the default is reported as an error.

9. **Exclude `Uri`-shaped types from the table**, specifically `ImageSource` and anything assignable
   to `Uri`. Loading an untrusted document must not issue outbound requests. `Uri` already has a
   working `UriTypeConverter` and never needed an entry.
   *Cost if wrong:* opening a hostile `.xaml` file performs attacker-chosen network requests.

10. **Report read failures through `IXamlErrorSink` with a self-authored message; never throw, and
    never forward the platform's message.** The message is `Cannot convert '<text>' to <TypeName>.`
    and the coordinates are the real `XmlAttribute`'s line/column from `PositionXmlDocument`.
    `XamlParseException`'s own message is poisoned by the first failure in the process and its
    coordinates point into the synthetic fragment. Throwing on read would make a document with one
    bad attribute impossible to open — the moment the user most needs to open it.
    *Cost if wrong:* users see a misleading error, or a bad file becomes unopenable.

11. **`XamlDependencyPropertyInfo.TypeConverter` (`:175`) is left unchanged.** The first draft
    claimed `Margin`/`Points`/`Data` were dependency properties that bypassed the hook. That is
    false: `FindProperty` resolves them through `TypeDescriptor.GetProperties` to
    `XamlNormalPropertyInfo` (`XamlParser.cs:545-557`), which already consults
    `GetCustomTypeConverter`. `XamlDependencyPropertyInfo` is constructed only for **attached**
    properties (`XamlParser.cs:599`, always `isAttached: true`) plus `Ignorable`
    (`XamlDocument.cs:392`), and every attached property in play today is `int` or `double` with a
    working converter. Changing `:175` would fix nothing in the failure list and would alter
    attached-property behaviour that nobody has reasoned about.
    *Cost if wrong:* a WinRT-typed attached property stays unconvertible — a recorded gap, not a
    silent one.

12. **The converter carries both directions from the outset, and `CanConvertTo(string)` returns
    `false` while no formatter exists.** `PropertyNode` gates an editable text box on
    `CanConvertFrom && CanConvertTo` (`PropertyNode.cs:231-233`). A from-only converter flips that
    gate true and prefills the box from `ConvertToInvariantString`, which on a converter that does
    not override `ConvertTo` is `value.ToString()` — `"Microsoft.UI.Xaml.Media.FontFamily"`. For
    `Brush`/`Geometry` the commit throws and reverts; for **`FontFamily` it does not**, because
    WinUI accepts any string as a family name, so merely tabbing through the PropertyGrid would
    rewrite the user's font to that literal string and commit it through the undo stack. Phase B
    turns `CanConvertTo` on per type as each formatter lands.
    *Cost if wrong:* silent document corruption from a UI the user only looked at.

13. **Phase A ships the read side alone.** Write-side work is a separate spec. The measured blast
    radius of the read fix is one vacuous test, so Phase A can land green and independently; mixing
    the write side in would make a red suite ambiguous between "rendering changed" and "formatter
    is wrong".
    *Cost if wrong:* one extra spec/plan cycle.

## Architecture

One new file, `src/WinUIDesigner.XamlDom/XamlValueConverter.cs` (tabs, matching its siblings; new
file, so no SharpDevelop copyright header — it is not ported code), plus a one-line change to
`GetCustomTypeConverter` in `XamlPropertyInfo.cs:344-354`.

```
XamlParser.CreateObjectFromAttributeText(text, propertyInfo, scope)      XamlParser.cs:849
   └─ propertyInfo.TypeConverter                                          XamlPropertyInfo.cs:306
        └─ GetCustomTypeConverter(propertyType)                           XamlPropertyInfo.cs:344
             ├─ object / Type / DependencyProperty        (existing, matched first)
             └─ XamlValueConverter.TryCreate(propertyType)                (new)
                  ├─ reject Uri-assignable and ImageSource
                  ├─ unwrap Nullable<T>
                  └─ host-table lookup on the exact type
```

`XamlValueConverter.ConvertFrom(context, culture, value)`:

1. Non-string input → `base.ConvertFrom`.
2. Text starting `{` and not `{}` → conversion failure (Decision 7). A leading `{}` is stripped.
3. Build `<HostName xmlns='…presentation' HostProperty='{SecurityElement.Escape(text)}'/>`.
4. `XamlReader.Load`. Any throw → conversion failure with a self-authored message (Decision 10).
5. If the host property's `ReadLocalValue` is `UnsetValue` → conversion failure (Decision 8).
6. Read the property, assert `IsInstanceOfType` (Decision 5), return.

`CanConvertFrom(string)` is `true`; `CanConvertTo(string)` is `false` (Decision 12).

Host table (16 entries). Host *types* were chosen from those confirmed activatable in this
process (`Border`, `Grid`, `Path`, `Polygon`, `Line`, `TextBlock`, `Storyboard`, `MatrixTransform`,
`SolidColorBrush`, `ColumnDefinition`, `RectangleGeometry`, `ArcSegment`); per-**entry**
confirmation that each host *property* converts is DoD item 1, not a claim made here.

| Type | Host |
|---|---|
| `Thickness` | `Border.Padding` |
| `CornerRadius` | `Border.CornerRadius` |
| `GridLength` | `ColumnDefinition.Width` |
| `Windows.UI.Color` | `SolidColorBrush.Color` |
| `Brush` | `Border.Background` |
| `FontFamily` | `TextBlock.FontFamily` |
| `FontWeight` | `TextBlock.FontWeight` |
| `Windows.Foundation.Point` | `Border.RenderTransformOrigin` |
| `PointCollection` | `Polygon.Points` |
| `DoubleCollection` | `Line.StrokeDashArray` |
| `Geometry` | `Path.Data` |
| `Windows.Foundation.Rect` | `RectangleGeometry.Rect` |
| `Windows.Foundation.Size` | `ArcSegment.Size` |
| `Duration` | `Storyboard.Duration` |
| `Matrix` | `MatrixTransform.Matrix` |
| `System.Numerics.Vector3` | `Grid.Translation` |

Error reporting replaces the `Debug.WriteLine` at `XamlProperty.cs:270`. `XamlProperty` reaches the
sink through `parentObject.OwnerDocument.ServiceProvider` (`XamlDocument.cs:96`), the same service
provider `XamlParser` resolves it from (`XamlParser.cs:97,129`).

## Data flow

Load: `XamlParser` walks the XML; each attribute becomes a `XamlTextValue`; the owning
`XamlProperty`'s constructor calls `UpdateValueOnInstance` (`XamlProperty.cs:58`), which calls
`XamlTextValue.GetValueFor` → `CreateObjectFromAttributeText` → the converter → the live WinRT
object receives a real value. Failures are reported and the property keeps its default.

Edit: a PropertyGrid commit goes `PropertyNode.ValueString` setter →
`TypeConverter.ConvertFromInvariantString` → the same converter. `CanConvertTo(string) == false`
keeps every Phase A type non-editable in the PropertyGrid, exactly as today.

Save: unchanged in Phase A. XamlDom preserves the original attribute text.

## Testing / DoD

New file `tests/WinUIDesigner.XamlDom.Tests/ValueConversionTests.cs`.

1. One test per host-table entry: a representative string converts to the expected value. A bad
   host pick can then never ship silently.
2. Host-viability self-check: a table entry naming an unloadable host reports "host unusable",
   distinguishable from a value error.
3. Grammar variants, not just types — `GridLength` `Auto` / `*` / `2*` / `1.5*` / `40`,
   `Color` named (`Red`) and hex (`#FF102030`), `Duration` `0:0:1` / `Forever` / `Automatic`,
   empty `PointCollection` and `DoubleCollection` (both accepted by WinUI, `Count == 0`),
   `FontFamily` with a fragment URI (`ms-appx:///Fonts/x.ttf#Fam`).
4. Guard tests that the pre-existing entries still win: `Button.Content="hi"` stays a string,
   `Style.TargetType` still resolves through `TypeTypeConverter`, `Setter.Property` through
   `DependencyPropertyConverter`.
5. Markup-extension rejection: `<Border.Padding>{StaticResource K}</Border.Padding>` and
   `Padding="{}{Binding}"` are both reported, not silently defaulted; and a `{StaticResource X}`
   whose key exists **only** in `Application.Current.Resources` is reported rather than resolved.
6. Error-message hygiene: after a deliberate failure of one type, a failure of a different type
   reports its own message. Asserts the sink message contains neither the earlier type's name nor
   text from the platform exception.
7. `Nullable<T>`: `PointAnimation.From="1,2"` converts; `Nullable<double>` is untouched.
8. Exclusions: `Image.Source="http://…"` is not converted by this mechanism (no outbound request),
   and `Uri` still goes through `UriTypeConverter`.
9. Assignability: a type whose host returns a non-assignable value reports rather than throwing
   `InvalidCastException`.
10. Culture: the same conversions run under `de-DE` and produce identical results.
    `CultureInfo.InvariantCulture` is already what `XamlParser.cs:859` passes; this pins it.
11. A regression test on a real document: parse `SampleWindow.xaml`, assert the
    `<SolidColorBrush x:Key="AccentBrush" Color="Blue"/>` resource actually carries Blue. **Note:**
    the existing `RoundTripTests` asserts only `IsInstanceOf<SolidColorBrush>` on
    `Button.Background` (`RoundTripTests.cs:61,83`) and will therefore *not* fail today — this
    assertion has to be added, not merely re-run.
12. Fix `ModelToolsArrangeItemsTests.ArrangeInGridSetsMarginLeftWhenNotRightAligned` to assert the
    behaviour its name claims, rather than the value the parsing bug produced.
13. All three suites green from their `.exe` runners with `--noresult`. Never `dotnet test`.
14. A performance budget: opening `SampleWindow.xaml` must not regress by more than 50 ms.

## Known gaps

- **`RectangleGeometry` has no table entry.** `UIElement.Clip` declares it, but WinUI applies the
  Geometry mini-language to that property, so the only viable host returns a `PathGeometry` the
  assignability assert (Decision 5) correctly refuses. `Clip="M…"` therefore stays unconverted, and
  is now *reported* rather than silent.
- **`Vector2` has no table entry.** The only `Vector2`-typed property found is
  `UIElement.ActualSize`, which is read-only.
- **`Vector3` has no dependency property.** `UIElement.Translation`/`Scale`/`CenterPoint`/
  `RotationAxis` are plain CLR properties with no `…Property` static, so Decision 8's
  `ReadLocalValue` check cannot apply to that entry and it relies on the throw alone.
- **`FontWeight` numeric form is rejected by WinUI.** `FontWeight='450'` throws; only names
  (`SemiBold` → 600) convert. Non-standard weights are therefore unrepresentable through this
  mechanism.
- **Attached properties of WinRT-valued types remain unconverted** — a consequence of Decision 11.
  None exists in the current codebase.
- **Per-attribute cost is 5–9× a bulk parse.** 1500 conversions cost ~90 ms at the light-host rate,
  against 16.5 ms for a single whole-document `XamlReader.Load` of the same file. Memoising
  `(type, text) → value` is the obvious mitigation if the budget in DoD item 14 is missed; real
  documents repeat values like `Margin="0"` heavily.
- **Native memory growth was observed** by the read-side reviewer: ~4.7 KB per `XamlReader.Load`
  not returned to the OS (38 MB → 61 MB over 5000 loads, managed heap flat). ~7 MB per
  1500-attribute document open. A repeated open/close soak should confirm it plateaus.
- **Phase 4d's four workaround comments become false documentation.**
  `PathHandlerExtensionTests.cs:17-33`, `PolyLineHandlerExtensionTests.cs:18-30`,
  `PathHandlerExtensionConvertMenuTests.cs:18-28` and `Phase4dDefinitionOfDoneTests.cs:220-232`
  each explain a gap that Phase A closes. None asserts a wrong value as correct, so none turns red,
  but all four must be corrected in the same change.
- **Reported messages are only self-authored for the sixteen table types.**
  `XamlProperty.ReportValueFailure` prefixes the exception's own message, and only
  `XamlValueConverter` composes the `Cannot convert '<text>' to <TypeName>.` form. A property
  whose type has no table entry reports the base .NET converter's text, which names neither the
  value nor the type — e.g. `<Image Source='ms-appx:///Assets/a.png'/>` now reports
  `Cannot set Source: TypeConverter cannot convert from System.String.` on open, blaming a
  document that is not at fault: `ImageSource` is excluded from the table deliberately, by
  Decision 9, because converting it would start an outbound fetch. Composing the message from
  `propertyInfo` and the `XamlTextValue` would fix the wording; deciding whether the deliberate
  exclusions should report at all is a policy question for Phase B.
- **The performance budget is a smoke test, not a budget.** Definition-of-done item 14 asks that
  opening `SampleWindow.xaml` not regress by more than 50 ms, but the test asserts an absolute
  wall-clock under 250 ms against a parse measured at roughly 16 ms. It has no baseline, would
  pass through a fifteen-fold slowdown, and can flake under load. Timing the same file twice in
  one test — once normally, once with the converter bypassed — and asserting the delta would
  make it real.

## Out of scope

- **The write side.** `CreatePropertyValue` still emits empty elements for WinRT-typed values, so
  setting such a property through the model still destroys it on save. Phase B.
- Markup extensions, templates and bindings, beyond refusing to convert extension text.
- Any general `XamlWriter` replacement.
- Types absent from the host table: reported, not converted.

## Phase B, recorded so it is not re-derived

Not part of this spec; captured because the review that produced Phase A settled its shape.

- `CreatePropertyValue` resolves its converter through `GetCustomTypeConverter` rather than
  `TypeDescriptor.GetConverter`. The existing `hasStringConverter` branch at
  `XamlDocument.cs:290-291` then does the work and **no new branch is added**; read table, write
  table and PropertyGrid display become the same table by construction.
- **Every new behaviour must be gated on `forProperty != null`.** `CreatePropertyValue` is also the
  body of `CreateObject`, whose callers are `MarkupExtensionParser.cs:403` — which runs at parse
  time for every `{Binding}`/`{StaticResource}` — and `XamlComponentService.cs:110`, which runs for
  every newly created control. Since `MarkupExtension` is not a `DependencyObject`, an ungated
  design throws there and makes any document containing a markup extension unopenable. The
  reference guards both of its own special cases the same way (`XamlDocument.cs:227,246`).
- **Do not throw for unsupported types.** `PlacementOperation` opens its `ChangeGroup` at
  `PlacementOperation.cs:114` and calls `Abort()` only from `Start`'s catch (`:49,59,92,97`) and
  from `Abort`/`Cancel` (`:252`) — never around `SetPlacement`. A throw from
  `GridPlacementSupport`'s `Margin` write, reached from `PointerMoved` → `MoveLogic.Move` (no
  try/catch), strands the transaction: `Undo()` then fails permanently, the next unrelated group's
  commit fails, and model and view desynchronise. There is no `Application.UnhandledException`
  handler in `src/`, so the process terminates.
- **Only six formatters are actually needed**: `FontFamily`, `FontWeight`, `PointCollection`,
  `DoubleCollection`, `Vector2`, `Vector3`. Measured, `ToString()` already emits the XAML form for
  `Thickness` (`1,2,3,4`), `CornerRadius` (`5,5,5,5`), `GridLength` (`Auto`, `2*`, `1.5*`, `40`),
  `Color` (`#FFFF0000`), `Point`, `Rect`, `Size`, `Matrix` and `Duration` (`00:00:01`, `Forever`,
  `Automatic`).
- **Always `ConvertToInvariantString`, never `ToString()`.** Under `de-DE`,
  `Point.ToString()` yields `'1,5;2,5'`. `Thickness` via `ConvertToInvariantString` stayed correct
  under `de-DE`; `Point` through that path was **not** measured and must be a required test.
- **Drop the nested-element serialization branch.** Its selection rule is dead: after mutating a
  collection through its CLR getter — which is how every Phase 4d tool builds geometry —
  `ReadLocalValue` still reports `UnsetValue`:
  `PathGeometry.Figures`, `PathFigure.Segments`, `TransformGroup.Children`,
  `GradientBrush.GradientStops` and `Polyline.Points` all measured `unset=True` with `Count == 1`.
  Only assigning a whole new collection reads as set. `Style` additionally has **no** dependency
  properties at all (no `SettersProperty`/`TargetTypeProperty`/`BasedOnProperty` in
  `Microsoft.WinUI.dll` 1.7.250606001), so the branch would emit `<Style/>`.
- `Path.Data` should be written as the mini-language by walking figures and segments, validated by
  round-tripping through the Phase A read path.
- Two structural hazards the write-side reviewer identified, to be designed against explicitly:
  hand-built XML children are invisible to `XamlObject.properties` (populated only by `XamlParser`
  and `FindOrCreateProperty`), so a later edit appends a **second** `<PathGeometry.Figures>`; and
  `GetPrefixForNamespace` mutates the document (`XamlDocument.cs:345-395`), so any new branch must
  be placed before line 296 or it leaks `xmlns` declarations even when it rejects the value.
- `Rect.Empty.ToString()` is `'Empty.'`, which no XAML parser accepts; the write side must say what
  happens.
