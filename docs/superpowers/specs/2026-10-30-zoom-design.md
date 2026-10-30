# Zooming the design surface — design

**Date:** 2026-10-30
**Status:** approved in chat ("1")

## Goal

The design can be seen larger, for detail, or smaller, to see all of a big layout.

## Decisions

**1. The zoom is the surface's own `ScrollViewer` zoom.** `DocumentView` sets
`ZoomMode = Enabled` with a range of 25% to 400%, so Ctrl+wheel and touchpad pinch work with no
code of their own. The designer's code already maps points through `TransformToVisual`, which
includes the zoom, and the thumbnail's frame already divides by `ZoomFactor`. No transform is
added to the design tree, so the saved XAML is unaffected.

**2. A zoom bar on the mode bar's right.** It holds minus, the percentage, plus, 100% and Fit.
- Minus and plus step through 25, 33, 50, 67, 75, 100, 125, 150, 200, 300 and 400%. From a
  factor between steps, such as one Ctrl+wheel left, they go to the nearest step in that
  direction.
- Fit picks the factor that shows the whole surface in the view, within the range.
- The percentage follows `ViewChanged`, so it is right after a Ctrl+wheel too.
- The bar is hidden in XAML mode.

**3. Each document keeps its own zoom.** The zoom belongs to the document's `ScrollViewer`, so
switching tabs keeps each document's zoom. It is not saved.

## Verification

Shell suite (`ZoomTests`) covers the stepping (from a step, from between steps, at the ends) and
Fit.

The zoom itself is the platform's and needs a real layout, so it is verified on the real app.
The walkthrough clicks plus twice (150%), then Fit, then turns Ctrl+wheel two notches, then
clicks 100%. Each step is screenshotted with the canvas, the percentage and the thumbnail frame
in view. The walkthrough learns a `ctrlwheel x y notches` step. The steps after it place and
drag controls at 100%, so they also show the surface still works after a zoom round trip.

## Out of scope

- Zooming around the pointer from the buttons; they keep the scroll offsets.
- Saving the zoom per document.
