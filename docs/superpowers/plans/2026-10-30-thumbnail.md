# Thumbnail pane — plan

**Spec:** `docs/superpowers/specs/2026-10-30-thumbnail-design.md`

## Task 1 — `Document.DesignChanged`

Raise it from the undo stack handler.

## Task 2 — `ThumbnailView`

`src/WinUIDesigner.Shell/ThumbnailView.cs`:
- `Attach(DocumentView)` and the seam `Attach(Document, ScrollViewer, FrameworkElement)`.
- Debounced `RenderNowAsync`.
- The frame, and scrolling on press and drag.
- Static `ImageRect`, `FrameRect` and `ContentPoint`.

`DocumentView` exposes `SurfaceScroll` and `SurfaceContent`. Tests in `ThumbnailViewTests`.

## Task 3 — `MainWindow`

A fourth row in the right column holds the pane. It is attached wherever the current document
changes.

## Task 4 — walkthrough and results

## Results

- **First run (9834151).** The pane appeared at the bottom of the right column. For the new,
  empty document it showed nothing, and a press on it did nothing. After a Button was placed, it
  showed the Button stretched over the whole picture (`02-click-placed.png`).
  `RenderTargetBitmap` draws only the bounds of what is painted, and the root `Canvas` has no
  `Background`. The frame was right, at about 173 of 213px across: the 528px viewport over a 640px
  canvas. This also corrects the export plan's reading of its 179×32 image.
- **Fix.** `DesignImageExport.RenderWholeAsync` sets a `Transparent` background on such a root
  for the render and clears it afterwards. Both the export and the thumbnail use it. The pane
  also draws a white page under the picture.
- **Second run (91bb7ee), CI green.**
  - The empty new document shows as a white page in the pane.
  - A press near the pane's bottom right moved the frame to the right edge
    (`01c-thumbnail-scrolled.png`), and a press near its top left moved it back.
  - The placed Button appears small, at its place on the 640×480 page (`02-click-placed.png`).
  - The exported Segmented document is now 528×613: its unsized root `Canvas` at the size it is
    arranged to on the surface, with the `Segmented` at its offset of 40.
