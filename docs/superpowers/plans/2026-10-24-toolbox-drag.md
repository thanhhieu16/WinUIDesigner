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
