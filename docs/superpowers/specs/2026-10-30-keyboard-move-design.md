# Moving the selection with the arrow keys — design

**Date:** 2026-10-30
**Status:** approved in chat ("làm mục 2")

## Goal

An arrow key moves the selected controls by 1px, and by 10px with Shift, for the fine
adjustment a mouse drag cannot make.

## Decisions

**1. The keys belong to the design surface.** `DesignPanel` handles `KeyDown`/`KeyUp` for the
four arrows, through events, since `Panel` is not a `Control` and has no `OnKeyDown` to
override. The arrows act only while the panel has focus. A list, the property grid or the XAML
editor keeps its own arrow behaviour, and no window-level accelerator takes the arrows from
them. `PointerTool` now focuses the panel on a press, as `CreateComponentTool` already did.

**2. One press is one undo entry.** `KeyboardMove` starts a `PlacementOperation` (`Move`) on the
first `KeyDown`. It adds each auto-repeated `KeyDown` to a running offset, and commits on the
arrow's `KeyUp`. It also commits when the panel loses focus, so a release that lands elsewhere
cannot leave the operation open. Holding a key and letting go is therefore one Ctrl+Z.

**3. No snapping.** `BeforeSetPosition` is not called, so the raster and the snaplines do not
apply. A 1px step that snapped back to the grid would not move at all. The container's own
`SetPosition` still decides what a move means: `Canvas.Left`/`Top` on a `Canvas`, margins and
cells in a `Grid`.

**4. What moves.** The selected items, without the root, which has no container to move in.
Items in different containers cannot share one operation, so a mixed selection moves those
sharing the first item's container. With nothing movable the key is not handled, and it bubbles
on as before.

## Verification

Designer suite (`KeyboardMoveTests`, through `DesignPanel.HandleArrowKeyDown`/`HandleArrowKeyUp`,
since a test cannot raise a real `KeyDown`):
- Right is +1px and Shift+Down is +10px.
- Five repeats and one release move 5px as one undo entry, which Undo reverts.
- Two selected Buttons move together.
- The root alone, an empty selection, or a non-arrow key is not handled.

Walkthrough: after the first Button is placed, Shift+Right twice and Down once
(`02a-arrow-moved`), then three Ctrl+Z (`02b-arrow-undone`). The walkthrough's `key` step learns
the arrows, sent as extended keys.
