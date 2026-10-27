# Seeding the designer's metadata — design

**Date:** 2026-10-27
**Status:** started on "làm mục 1" (ShellMetadata), 2026-10-27
**Closes:** the shell spec's Known gap "`ShellMetadata` seeds only what DoD item 15 exercises.
Popular properties beyond the listed ones, value ranges, hidden properties and disabled placements
are all still empty."

## What already exists

The engine reads five `Metadata` tables. `ShellMetadata` seeded only two of them: default sizes
and twelve popular properties.

| Table | Read by | Effect while empty |
|---|---|---|
| `AddDefaultPropertyValue` | `CreateComponentTool`, through `XamlComponentService.SetDefaultPropertyValues`, for a control placed from the toolbox | A placed Button or CheckBox is an empty frame. A placed Rectangle or Ellipse has no `Fill` and is invisible. |
| `AddValueRange` | the property grid's `NumberEditor` | `Opacity` accepts 5. `Width` accepts -100. |
| `HideProperty` | the property grid (`PropertyGridControl`, `PropertyNode`) | Every `XYFocus*`, `FocusVisual*` and `KeyTip*` property is listed. |
| `AddPopularProperty` | the property grid's Popular category | A `TextBlock`'s `Foreground`, `FontSize` and `TextWrapping` are not popular, because `TextBlock` is not a `Control` and declares its own dependency properties. The same goes for `Border`, `Shape` and `Image`. |
| `DisablePlacement` | `DefaultPlacementBehavior` | Nothing measured is wrong with it, so it stays empty. |

## Decisions

**1. Placed controls get visible defaults.**
- Button, ToggleButton, RepeatButton, HyperlinkButton, CheckBox and RadioButton get their type
  name as `Content`.
- TextBlock gets "TextBlock" as `Text`.
- TextBox and ComboBox get their type name as `PlaceholderText`, so they stay empty but labelled.
- ToggleSwitch gets its type name as `Header`.
- Rectangle and Ellipse get a light gray `Fill`.
- Border gets a gray one-pixel border.
- Panels stay transparent, because a background on a panel changes how it hit-tests in the user's
  own app.

Keys are exact types, as `Metadata` looks them up.

**2. Number editors are bounded.**
- `Opacity`: 0–1.
- `FontSize` (Control's and TextBlock's): 1–1000.
- `StrokeThickness`: 0–1000.
- `Width`, `Height`, `Min*` and `Max*`: 0–100000.

**3. Focus-navigation plumbing is hidden from the property grid.** That is the nine `XYFocus*`,
the five `FocusVisual*` and the four `KeyTip*` properties. They still round-trip through the XAML.
They are only not listed.

**4. Popular grows to the look-and-text properties of the common non-`Control` types,** plus a
few common `Control` ones:
- `Control`: Padding, BorderBrush, BorderThickness, CornerRadius, FontWeight and FontFamily.
- `TextBlock`: its own Foreground, FontSize, FontWeight, TextWrapping and TextAlignment.
- `TextBox`: Text, PlaceholderText and Header.
- `Border`: Background, BorderBrush, BorderThickness, CornerRadius and Padding.
- `Panel`: Background.
- `Shape`: Fill, Stroke and StrokeThickness.
- `Image`: Source and Stretch.
- The checked, on, minimum, maximum and value properties of ToggleButton, ToggleSwitch and
  RangeBase.

Default sizes are added for nine more common controls.

## Verification

Shell suite (`ShellMetadataTests`):
- Registering and defaulting a Button gives `Content` "Button", a TextBlock gives "TextBlock",
  and a Rectangle gets a light gray `Fill`.
- `Opacity` is bounded 0–1 and `Width` starts at 0.
- `XYFocusUp` and `FocusVisualPrimaryBrush` are not browsable, and `Width` is.
- A TextBlock's Foreground, FontSize and Text are popular.

Walkthrough: step 02 already places a Button by click, so its screenshot shows the label.

## Results

- CI is green on `windows-latest`, with all four suites run from their `.exe`s. `ShellMetadataTests`
  has 3 new tests.
- Walkthrough, on the real app:
  - **Step 02:** the click-placed Button reads "Button".
  - **Step 06:** both placed Buttons do too.
  - **Popular category:** the selected Button's now lists BorderBrush, BorderThickness, Content,
    CornerRadius, FontFamily, FontSize and FontWeight.
