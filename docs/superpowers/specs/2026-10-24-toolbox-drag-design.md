# Toolbox drag-to-place — design

**Date:** 2026-10-24
**Status:** draft, awaiting review
**Reference:** `F:\ReferencesCode\CSharp\WpfDesigner\XamlDesigner` (read-only) — `ToolboxView`
starts `DragDrop.DoDragDrop` with the prepared tool; `WpfDesign.Designer`'s
`CreateComponentTool` handles the drop.

## Goal

Let a user drag a control from the toolbox onto the design surface and have it placed where
they release it, as one undo step. Today a control is placed only by selecting it in the
toolbox and then clicking or dragging out a rectangle on the surface.

Nothing else. Live preview of the control following the cursor, dragging `.xaml`/`.dll` files
from Explorer onto the window, and dragging between two documents are out of scope and listed
at the end.

## Why this phase

Phase 5 deferred it in writing (shell spec, Decision on toolbox drag, and Known gaps: "No drag
from the toolbox onto the surface"), naming `UIElement.StartDragAsync(PointerPoint)` as the
mechanism a later phase needs. Of the gaps the shell still carries, it is the one a user
reaches for first: dragging is how every designer they have used places a control.

The engine half was written in Phase 4d — `CreateComponentTool` subscribes to
`IDesignPanel.DragOver`/`Drop`/`DragLeave` — but it has never run. Nothing in `src/` starts a
drag, and, measured below, nothing makes the design panel a drop target either.

## The gap, measured

Read from the current tree (`180a5ba`), not assumed:

| | Today |
|---|---|
| Starting a drag | Nothing. `ToolboxView`'s `ListView` has `CanDragItems` at its default, false. |
| Drop target | `DesignPanel` never sets `AllowDrop`; no line in `src/` does. WinUI raises `DragEnter`/`DragOver`/`Drop` only on an element with `AllowDrop = true`, so `CreateComponentTool`'s three handlers are unreachable. |
| Engine drop path | `CreateComponentTool.HandleDragOver` **inserts** the control on the *first* `DragOver` that hits a container, at that point, and later `DragOver`s only keep accepting. So a working drag would place the control where the cursor *entered* the surface, not where it was released. Its own doc comment records this as a deliberate Phase 4d narrowing. |
| Tests | No test calls `HandleDragOver`, `HandleDrop` or `HandleDragLeave` (grep over `tests/`). `HandleDragOver` takes a `DragEventArgs`, which has no public constructor, so as written it cannot be tested. |

## Verified

Compiled against `Microsoft.WindowsAppSDK` 1.7.250606001 in a throwaway probe (`CS` errors
would have shown an absent member; none did):

- `ListViewBase.CanDragItems`, `DragItemsStarting` (`DragItemsStartingEventArgs.Items`,
  `.Data` as a `DataPackage`, `.Cancel`), `DragItemsCompleted`
  (`DragItemsCompletedEventArgs.DropResult` as a `DataPackageOperation`, `.Items`).
- `UIElement.AllowDrop`, `DragEnter`; `DragEventArgs.AcceptedOperation`, `.GetPosition(UIElement)`,
  `.DataView`, `.DragUIOverride.Caption`/`.IsCaptionVisible`.
- `DataPackage.RequestedOperation`, `DataPackage.Properties[string] = string`, and reading it
  back synchronously through `DataPackageView.Properties.TryGetValue`.

**Not measured, and the plan must measure it before relying on it:**

- Whether a drag over an empty area of the surface reaches `DesignPanel` at all. WinUI only
  hit-tests an element whose `Background` is non-null for both pointer and drag input, and
  neither `DesignPanel` nor the new-document `Canvas` sets one. Click-to-place on an empty
  canvas is known to work (the Phase 5 and tabs walkthroughs both placed controls), so
  something in the tree is hit-testable there — but which element, and whether drag input
  takes the same route, is unmeasured. If drag events do not arrive, the fix is
  `DesignPanel.Background = Transparent`, and the plan records which it was.
- Whether `new DataPackage()` constructs in the test process. If it does not, the Shell seams
  in Decision 7 take the type alone and the marker write is covered by the walkthrough.

## Decisions

### 1. The drag starts from the `ListView`'s own item drag, not `StartDragAsync`

`ToolboxView` sets `list.CanDragItems = true` and handles `DragItemsStarting`. The list
already owns the pointer press, the drag threshold and the pointer capture for its items;
`StartDragAsync` would need a live `PointerPoint` from a `PointerPressed` the `ListView`
itself handles first, re-implementing all three. `CanReorderItems` stays false and the list's
`AllowDrop` stays false, so the toolbox cannot be reordered or dropped onto.

*Cost if wrong:* `StartDragAsync` remains available as a fallback; nothing below depends on
which call started the drag.

### 2. The tool is armed in the tool service; the data package carries only a marker

`DragItemsStarting` arms a fresh `CreateComponentTool` for the dragged entry through the same
`Arm` the click path uses, and writes the type's assembly-qualified name into
`e.Data.Properties["WinUIDesigner.ToolboxType"]`, with `RequestedOperation = Copy`.

The reference puts the tool itself in the drag data. That does not port: a `DataPackage`
crosses into WinRT, and a managed `CreateComponentTool` is not a WinRT type. A string is.
The tool does not need to travel anyway — it is already the current tool of the one surface
that can receive the drop, and only the current tool is subscribed to that surface's drag
events (`CreateComponentTool.Activate`).

### 3. The marker gates acceptance

`CreateComponentTool` accepts a drag only when `DataView.Properties` carries the marker and
it names the tool's own `ComponentType`. Without that check, any text or file dragged in from
another app while a control is armed by click would drop that control.

### 4. `DragOver` only decides; `Drop` inserts, at the release point

`HandleDragOver` hit-tests and returns `Copy` when the point lands on a model item that
carries an `IPlacementBehavior`, and `None` otherwise. It no longer creates or inserts
anything. This is a cheaper and looser check than the drop's: the real test,
`CanEnterContainer`, needs a `PlacementOperation` built around an already-created item, which
is exactly the per-`DragOver` work this decision removes. So the cursor can read `Copy` over a
container that then refuses the drop; the drop then places nothing (Known gaps). `HandleDrop`
does what the click path's click-without-drag does, at the drop point: open an "Add Control"
change group, create the item, `AddItemsWithDefaultSize` into the hit container, commit, select
it, and reset the current tool to `PointerTool`. `HandleDragLeave` has nothing left to undo
and becomes a no-op; it is kept so `Activate`/`Deactivate` stay symmetric.

This reverses Phase 4d's insert-on-first-`DragOver`. That narrowing placed the control where
the cursor entered the surface, which is wrong for any drag that does not end where it
entered, and its stated reason — reconciling `MoveLogic`'s window coordinates with
`DragEventArgs.GetPosition`'s panel-relative ones for a live preview — goes away once there is
no preview. Both the click path and the new drop path take panel-relative points
(`GetCurrentPoint(designPanel)` / `GetPosition(designPanel)`), so they place identically.

A dragged `Polyline`/`Path` is placed at its default size, not handed to its
`IDrawItemExtension`: a multi-click draw gesture cannot start from a drop.

*Cost:* no visual preview while dragging — only the system drag cursor and `Copy`/`None`.
Recorded in Known gaps.

### 5. `DesignPanel` becomes a drop target

`DesignPanel`'s constructor sets `AllowDrop = true`, plus `Background = Transparent` if the
measurement above shows it is needed. This is the one change to `WinUIDesigner.Designer`
outside `CreateComponentTool`. It has no effect while the current tool is `PointerTool`, which
does not subscribe to drag events: an unaccepted drag is refused by the system.

### 6. A cancelled drag disarms the toolbox

`DragItemsCompleted` with `DropResult == None` — Escape, or a release anywhere that refused
it — resets the current tool to `PointerTool` if it is still the tool this drag armed. That
reset already clears the toolbox selection (Phase 5 Decision 12). Without it, an abandoned
drag leaves the control armed and the user's next click on the surface places it, which is
not what they asked for. A successful drop has already reset the tool, so `Copy` needs no
handling.

`DragItemsStarting` sets `e.Cancel = true` when no tool service is attached — no document, or
the current document is in XAML mode — so a drag that could never land does not start.

### 7. Seams follow the existing `Handle*` pattern

`DragEventArgs` and `DragItemsStartingEventArgs` cannot be built in a test with the data a
real drag carries. As the rest of `CreateComponentTool` already does, every handler becomes a
thin wrapper over an `internal` method taking plain values:

- `CreateComponentTool.HandleDragOver(IDesignPanel, Point, IReadOnlyDictionary<string, object>)`
  returning a `DataPackageOperation`;
- `CreateComponentTool.HandleDrop(IDesignPanel, Point, IReadOnlyDictionary<string, object>)`
  returning whether it placed anything;
- `ToolboxView.BeginDragForTest(Type, DataPackage)` and `CompleteDragForTest(DataPackageOperation)`,
  next to the existing `SelectForTest`.

## File structure

| File | Change |
|---|---|
| `src/WinUIDesigner.Designer/Services/CreateComponentTool.cs` | Decisions 3, 4, 7; the marker key as a public constant. |
| `src/WinUIDesigner.Designer/DesignPanel.cs` | Decision 5. |
| `src/WinUIDesigner.Shell/ToolboxView.cs` | Decisions 1, 2, 6, 7. |
| `tests/WinUIDesigner.Designer.Tests/CreateComponentToolDropTests.cs` | New. |
| `tests/WinUIDesigner.Shell.Tests/ToolboxViewTests.cs` | Extended. |

No new package. No `.xaml` file. No change to `XamlDom` or `DesignModel`.

## Testing

All four suites run from their `.exe`s, locally or through `.github/workflows/build-and-test.yml`.

- **`CreateComponentTool`** (Designer.Tests, against a loaded Canvas document):
  `HandleDragOver` over the canvas with a matching marker returns `Copy` and adds nothing to
  the tree; without the marker, with a marker naming another type, or over a point with no
  model hit, returns `None`. `HandleDrop` with a matching marker adds exactly one child, at
  the drop point (`Canvas.Left`/`Top`, within the 0.5px tolerance CLAUDE.md records), at the
  default size, selected, as one undo entry that a single `Undo` removes, and leaves
  `PointerTool` current. `HandleDrop` with no container hit adds nothing and opens no undo
  entry. Two drops at different points place at those two points — the regression test for
  Decision 4.
- **`ToolboxView`** (Shell.Tests): `CanDragItems` is true and `CanReorderItems` false;
  `BeginDragForTest` arms a `CreateComponentTool` of the dragged type and writes the marker;
  with no attached service it cancels and arms nothing; `CompleteDragForTest(None)` after a
  begin resets to `PointerTool` and clears the selection; `CompleteDragForTest(None)` after
  the tool has already changed leaves the new tool alone.
- **`DesignPanel`**: `AllowDrop` is true.

**Not provable in-process:** that a real OS drag starts from the list, reaches the surface,
and drops. `DragEventArgs` cannot be raised from a test. That is a walkthrough item, and it is
drivable without a human: CLAUDE.md records that `SetCursorPos` + `mouse_event` reach the XAML
island.

## Definition of done

1. Dragging a toolbox entry onto the surface and releasing places that control at the release
   point, at its default size, selected.
2. One `Ctrl+Z` removes it.
3. Releasing outside the surface, or pressing Escape mid-drag, places nothing and leaves the
   toolbox unarmed: a following click on the surface places nothing.
4. A text drag from another application onto the surface places nothing, even with a control
   armed by click.
5. No drag starts with no document open or with the current document in XAML mode.
6. Click-to-place and drag-a-rectangle-to-place behave exactly as before.
7. `src/` still contains exactly one `.xaml` file. No new package reference.
8. All four suites green from their `.exe`s, and the walkthrough below recorded in the plan.

Walkthrough: items 1–5, plus dragging onto a `Grid`-rooted document to confirm the drop lands
in the cell under the cursor.

## Known gaps

- **No live preview.** The control appears on release, not while dragging. The reference
  moves a real instance under the cursor through `MoveLogic`; porting that needs the window-
  versus-panel coordinate reconciliation Phase 4d deferred.
- **Only the hit container is tried.** `AddItemsWithCustomSize` does not retry on ancestors,
  because `UIElement.TranslatePoint` does not exist (CLAUDE.md). A drop onto a control that
  cannot take children (a `Button`) is refused rather than placed in that control's parent.
  Same limit the click path has today.
- **Drawn shapes are placed at default size** (Decision 4).
- **The cursor can promise a drop the container then refuses** (Decision 4): `DragOver`
  checks for an `IPlacementBehavior`, `Drop` asks `CanEnterContainer`.

## Out of scope

Live drag preview. Dropping `.xaml` or `.dll` files from Explorer. Dragging a control from one
document to another, or out of the surface into the toolbox. Reordering the toolbox. Any
change to `XamlDom` or `DesignModel`.
