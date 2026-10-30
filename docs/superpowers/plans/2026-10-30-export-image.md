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
