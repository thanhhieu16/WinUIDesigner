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
