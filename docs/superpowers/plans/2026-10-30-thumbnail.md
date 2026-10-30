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
