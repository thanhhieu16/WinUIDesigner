# Exporting the design as an image — design

**Date:** 2026-10-30
**Status:** approved in chat ("làm mục 1")

## Goal

File > Export as Image... saves what the current document's design looks like as a PNG file, for
a mock-up, a bug report or documentation.

## Decisions

**1. What is drawn is the document's root element, without the designer's chrome.**
`DesignImageExport` renders `DesignContext.RootItem.View` with `RenderTargetBitmap`. The
selection frames, resize thumbs and snaplines are in `DesignPanel`'s adorner layer, a sibling of
the root rather than a descendant, so they are not in the picture. The surface's scroll position
does not matter either: the element is drawn whole.
`RenderTargetBitmap` draws only what is painted, so a root with a null `Background` is given a
`Transparent` one on the live object for the render, then cleared. The XAML is serialized from
the model and never sees it (plan Results).

**2. The PNG is at the display's scale and keeps transparency.** The bitmap's pixel size is the
element's size times `XamlRoot.RasterizationScale`, and the file records the matching DPI
(96 × scale). A root without a `Background` (a new document's `Canvas` has none) stays
transparent, as it would be in the app it is designed for.

**3. Export needs the Design view and a document that loaded.** In XAML mode the surface is
collapsed, and `RenderTargetBitmap` draws nothing that is not visible. Switching views for the
user could fail on the text being edited, so the command says "Switch to the Design view to
export an image." instead. A document whose XAML did not load has no design to draw, and says
so. Both checks run before the save picker opens.

**4. Failures are reported, and leave no file.** A root with no size on screen, an encoder
failure or a write failure is reported in the usual error dialog. The save picker can create the
chosen file empty before the export runs; a failed export deletes it if it is still empty.

**5. The encoding is `Windows.Graphics.Imaging`.** `BitmapEncoder` with `PngEncoderId`, fed the
BGRA8 premultiplied pixels `RenderTargetBitmap.GetPixelsAsync` returns, into an
`InMemoryRandomAccessStream`; the bytes are written with `File.WriteAllBytes`. No new package.

## Verification

Shell suite (`DesignImageExportTests`), with a new `WinUITestHost.RunAsync` /
`RunWithWindowAsync` for bodies that await WinRT operations on the dispatcher thread:
- A document whose root is an 80×60 red `Canvas`, live in the shared window, exports a PNG that
  decodes to 80×60 times the scale, with an opaque red middle pixel.
- XAML mode reports and writes nothing.
- A document that did not load reports and writes nothing.
- A root of zero size reports and writes nothing.
- The File menu lists the new item (`MainWindowInteractionTests`).

Walkthrough, on the real app: after the CommunityToolkit `Segmented` is placed (step 35),
File > Export as Image..., save as `walk-export` in Documents. The workflow publishes the file
as `35d-exported-file.png` beside the screenshots, so the exported image itself can be looked at.

## Out of scope

- Other formats (JPEG, SVG, clipboard copy).
- Exporting the selected element alone, or at a chosen scale.
- A background colour option.
