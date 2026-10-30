# Thumbnail pane — design

**Date:** 2026-10-30
**Status:** approved in chat ("làm mục 1")

## Goal

A small picture of the whole design, with a frame around the part the surface is showing, so a
design larger than the window can be seen at once and moved around in.

## Decisions

**1. The pane sits at the bottom of the right column, 170px high.** It is below the property
grid, so the outline and the grid keep their places (and the walkthrough's coordinates hold).
The toolbox and the surface lose no room.

**2. It draws the root element, like Export as Image.** `ThumbnailView` renders
`DesignContext.RootItem.View` with `RenderTargetBitmap`, so adorners are not in it. The picture is
scaled to fit the pane, never enlarged, and centred. A root with no size of its own is drawn at
its content's bounds (export-image plan Results), so the picture's own size stands in for the
root's.

**3. It is redrawn when the design changes, 300ms after changes settle.** The triggers are:
- `Document.DesignChanged`, a new event raised on every undo stack change (an edit, an undo, a
  redo);
- `PropertyChanged("Mode")` and `("DesignContext")`, for a rebuilt tree;
- switching documents.

Layout events are not used: `LayoutUpdated` fires for any layout in the window, the redraw
itself included. A control that changes how it looks on its own (an animation) is therefore not
followed until the next edit.

**4. The frame follows the surface's scroll position.** It is the `ScrollViewer`'s viewport,
divided by its zoom factor, moved into the root's coordinates through
`TransformToVisual`, and clipped to the picture. It updates on `ViewChanged` and `SizeChanged`,
without a redraw.

**5. Pressing or dragging on the pane scrolls there.** The point under the pointer is centred in
the surface's view, as far as the scroll range allows (`ChangeView`, no animation).

**6. In XAML mode, and with no document, the pane is empty.**

## Verification

Shell suite (`ThumbnailViewTests`):
- The layout arithmetic: fitting, centring, never enlarging; the frame scaled and clipped; a
  point mapped back and clamped.
- An 800×600 document in a 300×200 `ScrollViewer` beside a 160×120 pane: the picture fills the
  pane, the frame is the viewport at scale 0.2, a press near the bottom right scrolls the
  surface right and down, and XAML mode empties the pane.
- `ThumbnailView.Attach` takes a document, its `ScrollViewer` and the scrolled content, so the
  test needs no `DocumentView`, whose `TextBox` is unsafe in a test window (CLAUDE.md).

Walkthrough: the pane appears in every screenshot. Right after start, a press near the pane's
bottom right scrolls the new 640×480 canvas (`01c-thumbnail-scrolled`), and one near its top
left scrolls back (`01d-thumbnail-back`).

## Out of scope

- Zooming from the pane, and a resizable or collapsible pane.
- Following changes that do not go through the undo stack.
