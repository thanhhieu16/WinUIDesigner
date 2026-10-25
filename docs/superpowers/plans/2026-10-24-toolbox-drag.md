# Toolbox drag-to-place — plan

**Spec:** `docs/superpowers/specs/2026-10-24-toolbox-drag-design.md` (approved 2026-10-24)

Every task ends with all four suites green from their `.exe`s. This plan's work was written in
a Linux container, so "green" here means `.github/workflows/build-and-test.yml` on
`windows-latest`, which builds the solution and runs each test `.exe` in turn. The C# was
compiled locally first against the real WindowsAppSDK 1.7.250606001 reference assemblies, with
its Windows-only packaging targets stubbed out (the stubs are never committed).

## Two measurements the spec asked for, and how each was settled

- **Does a drag over empty surface reach `DesignPanel`?** Not measurable here: it needs a real
  OS drag, which no test can raise and the CI runner is not driven to perform. Settled by
  construction instead of by measurement: `DesignPanel` gets `Background = Transparent`
  alongside `AllowDrop`, so the whole panel is hit-testable whichever answer the measurement
  would have given. The cost of being wrong in this direction is that empty stretches of the
  panel now also receive pointer input; the existing suites are the check that nothing relied
  on them not doing so. Walkthrough step 1 confirms a drop over empty canvas lands.
- **Does `new DataPackage()` construct in the test process?** Answered by the Shell tests in
  Task 3, which construct one; if CI shows otherwise, those seams fall back to taking the type
  alone, per the spec.

## Task 1 — `CreateComponentTool`: decide on DragOver, insert on Drop

`src/WinUIDesigner.Designer/Services/CreateComponentTool.cs`

- Add `public const string ToolboxTypeKey = "WinUIDesigner.ToolboxType"`.
- `HandleDragOver(IDesignPanel, Point, IReadOnlyDictionary<string, object>)` returns
  `DataPackageOperation`: `None` unless the properties carry the key naming this tool's
  `ComponentType` and the hit model item has an `IPlacementBehavior`; `Copy` otherwise. Creates
  nothing.
- `HandleDrop(IDesignPanel, Point, IReadOnlyDictionary<string, object>)` returns `bool`: same
  marker check, then open "Add Control", `CreateItem`, `Position = point`,
  `AddItemsWithDefaultSize`, commit (or abort and return false), reset to `PointerTool` if this
  tool is still current.
- `OnDragLeave` becomes a no-op; `_dropItem`/`_dropChangeGroup` are deleted.
- Subscribe `DragEnter` to the same handler as `DragOver`, so the first frame of a drag already
  carries the right `AcceptedOperation`.

Tests: `tests/WinUIDesigner.Designer.Tests/CreateComponentToolDropTests.cs` (new) —
DragOver accepts a matching drag and inserts nothing; refuses no marker, a null property set,
another type's marker, and a point with nothing under it; Drop places one selected control as
one undo entry and disarms the tool; two drops 100px apart land 100px apart (the Decision 4
regression test, asserted as a difference so it does not depend on the panel-to-canvas
mapping); a refused drop places nothing, opens no undo entry and leaves the tool armed.

## Task 2 — `DesignPanel` is a drop target

`src/WinUIDesigner.Designer/DesignPanel.cs`: `AllowDrop = true` and
`Background = new SolidColorBrush(Colors.Transparent)` in the constructor.

Test: `TheDesignPanelIsADropTargetOverItsWholeArea` in the same new fixture.

## Task 3 — `ToolboxView` starts the drag

`src/WinUIDesigner.Shell/ToolboxView.cs`

- `list.CanDragItems = true`; `CanReorderItems` and the list's `AllowDrop` left false.
- `DragItemsStarting` → `BeginDrag(type, e.Data)`; `e.Cancel = true` when it returns false.
  `BeginDrag` arms through the same `Arm` the click path uses (now returning the tool), remembers
  it as `draggedTool`, writes the marker into `e.Data.Properties`, sets `RequestedOperation =
  Copy`. Returns false with no tool service attached.
- `DragItemsCompleted` → `CompleteDrag(e.DropResult)`: on `None`, reset to `PointerTool` only if
  the current tool is still `draggedTool`.
- `Detach` forgets `draggedTool`.
- Seams: `ListForTest`, `BeginDragForTest(Type, DataPackage)`,
  `CompleteDragForTest(DataPackageOperation)`.

Tests, appended to `tests/WinUIDesigner.Shell.Tests/ToolboxViewTests.cs`: drag allowed and
reorder/drop-onto not; a begin arms the dragged type and writes the marker and `Copy`; with no
service it refuses and writes nothing; `None` disarms and clears the selection; `None` after the
user picked another tool leaves that tool; `Copy` leaves the tool as the drop left it.

## Task 4 — Walkthrough

Run `WinUIDesigner.Shell.App.exe` and check, by hand or driven by `SetCursorPos` +
`mouse_event` as CLAUDE.md records:

1. Drag `Button` from the toolbox onto empty canvas and release: a button appears at the
   release point, selected.
2. `Ctrl+Z` removes it in one step.
3. Start a drag and press Escape, or release over the toolbox: nothing placed; a click on the
   canvas afterwards places nothing.
4. Drag text from another application (e.g. a browser selection) onto the canvas with a
   control armed by click: nothing placed.
5. With the document in XAML mode, a toolbox drag does not start.
6. On a `Grid`-rooted document, a drop lands in the cell under the cursor.
7. Click-to-place and drag-a-rectangle-to-place still work.

Results are recorded below once walked.

## CI result

`f6c9219`, [run 3](https://github.com/thanhhieu16/WinUIDesigner/actions/runs/40121449154):
all four suites green from their `.exe`s. Designer.Tests 255/255 (248 before, the seven new
`CreateComponentToolDropTests`), Shell.Tests 82 passed + 1 `[Explicit]` (76 before, the six new
`ToolboxViewTests`). This settles both open questions as far as a test process can: `new
DataPackage()` constructs in the test host and its `Properties` round-trip a string, and no
existing suite depended on empty stretches of `DesignPanel` being unhittable. The walkthrough
above has not been run yet - see the next section.

## Self-review

**Spec coverage.** Decision 1 → Task 3 (`CanDragItems`, `DragItemsStarting`). 2 → Task 3
`BeginDrag`. 3 → Task 1 `CarriesThisTool`. 4 → Task 1 `HandleDragOver`/`HandleDrop`. 5 → Task 2.
6 → Task 3 `CompleteDrag` and `BeginDrag`'s refusal. 7 → the `Handle*` and `*ForTest` seams.
DoD 1–2 → Task 1 tests + walkthrough 1–2. 3 → Task 3 tests + walkthrough 3. 4 → Task 1 marker
tests + walkthrough 4. 5 → Task 3 refusal test + walkthrough 5. 6 → the existing
`CreateComponentToolTests`, unchanged and still green, + walkthrough 7. 7 → no `.xaml` or
package added. 8 → CI + this section.

**Risk stated rather than hidden.** The Transparent background is the one change with reach
beyond the drag: any pointer press on an empty part of the panel now reaches `DesignPanel`'s
handlers where before it reached nothing. If a suite turns red on it, the fallback is to drop
the brush and rely on `AllowDrop` alone, at the cost of drags over empty canvas going
undelivered.

## Walkthrough result

Driven on 2026-10-25 by `.github/workflows/walkthrough.yml`, which runs
`.github/walkthrough/steps.txt` against the real `WinUIDesigner.Shell.App.exe` on the
`windows-latest` runner with `SetCursorPos` + `mouse_event` + `keybd_event`, and publishes the
screenshots to `refs/walkthrough/latest`. Final state at `8127081`.

| # | Result |
|---|--------|
| 1 | **Not verified.** On the runner, any `ListView` item drag fail-fasts the app with `0xC0000409` right after `DragItemsStarting` returns - including a bare variant that only calls `e.Data.SetText` and arms no tool, so it is not this phase's handler code. Not root-caused; the runner runs the app elevated, which is the leading suspect. Needs a person at a real, non-elevated desktop. |
| 2 | Click-place a Button, then Ctrl+Z: removed in one step; Ctrl+Y puts it back. |
| 3-6 | Not verified: each needs a real drag (see 1). |
| 7 | Click-to-place works and lands at the click point (after the fixes below). |

Also confirmed on screen: the property grid's enum rows read `Stretch`, `Left`, `Center`,
`Visible` and stay inside the 280px column (the `180a5ba` fix), and clicking the empty interior
of the root Canvas selects it.

**Three defects the walkthrough found, none visible to the suites, all fixed:**

1. *A background-less panel's empty interior hit nothing.* `DesignPanel.HitTest` uses
   `FindElementsInHostCoordinates`, which skips a `Panel` with a null `Background`, and a new
   document's root `Canvas` has none - so a click or drop on a blank document found no
   container. The test fixtures had been setting a `Background` on the live view to get past
   exactly this. Fixed with a bounds-based second pass that accepts only background-less panels.
2. *Click-to-place never placed anything over a panel.* `PanelMove`'s overlay `DragListener`
   captures on every press before the press bubbles to `DesignPanel`, so the gesture's
   `CapturePointer` returns false; on release the overlay's `PointerCaptureLost` - a routed
   event - bubbled up and the gesture took it as its own, cancelling ~5ms before
   `PointerReleased`. Fixed by ignoring a capture loss whose `OriginalSource` is not the panel.
   Measured with a temporary trace (since removed); a first fix aimed at `DragListener` was
   disproved by the same trace and reverted.
3. *Placed controls landed offset by the container's position in the panel.* The panel-relative
   point went straight into `Canvas.Left/Top`: a click at y=420 put the button at y=488. Fixed by
   `CreateComponentTool.ToContainer`; the click and drop tests now assert absolute positions.

Still open and seen in passing: after Ctrl+Z removes the selected button, the property grid
keeps showing the removed button's properties until the next selection change.
