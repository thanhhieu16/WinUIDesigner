# Exporting the design as an image — plan

**Spec:** `docs/superpowers/specs/2026-10-30-export-image-design.md`

Every task ends with all four suites green from their `.exe`s on `windows-latest`.

## Task 1 — test host

`WinUITestHost.RunAsync(Func<Task>)` and `RunWithWindowAsync(Func<Window, Task>)`: start the body
on the dispatcher thread with a `DispatcherQueueSynchronizationContext`, block the caller until
the task completes, rethrow its exception.

## Task 2 — `DesignImageExport`

`src/WinUIDesigner.Shell/DesignImageExport.cs`: `Problem(Document)`, `ExportAsync(Document, path)`
(returns the error or null), `RenderPngAsync(UIElement)`. Tests in `DesignImageExportTests`.

## Task 3 — the command

`MainWindow`: File > Export as Image... below Save All, `ExportImageAsync` with a
`FileSavePicker` filtered to `.png` and the document's name suggested. The menu test lists it.

## Task 4 — walkthrough

`steps.txt` after step 35: open the File menu, click the item, save as `walk-export`.
`walkthrough.yml` copies `Documents\walk-export.png` into the published screenshots.

## Task 5 — docs

Record the results here, and close the gap in the shell spec if it lists one.

## Results

- **Real app (walkthrough, fe0f082).** File > Export as Image... opened the save picker in Documents
  with "PNG image" as the type and `New1` suggested (`35a-export-menu.png`, taken after the first
  click, which already opened it). Saving as `walk-export` wrote a 965-byte PNG, published as
  `35d-exported-file.png`: the CommunityToolkit `Segmented` with "One", "Two", "Three", on a
  transparent background, with no selection frame or thumbs.
- **Measured: a root without a size is drawn at its content's bounds.** The walkthrough's
  document is a `Canvas` typed without `Width`/`Height`, so its own size is 0×0. `RenderTargetBitmap`
  still drew it, at 179×32: the bounds of what it contains, so the `Segmented`'s
  `Canvas.Left`/`Top` of 40 is not in the picture. A root with a size (every new document's
  640×480 `Canvas`) is drawn at that size, as the suite's 80×60 case shows. The zero-size error
  path therefore applies only to a root with nothing visible in it.
- **Test host.** The first CI run failed `ExportAsync_WritesTheRootElementAsAPng` with
  `RPC_E_WRONG_THREAD` from `Document.Dispose`, after its image assertions had passed.
  `RunAsync` had set the `SynchronizationContext` inside its async lambda. The async method
  builder restores the previous (null) context when the body first yields, and
  `DispatcherQueueSynchronizationContext.Post` does not reinstate itself. So every await after
  the first resumed on the thread pool. It is now set in the synchronous dispatcher callback.
