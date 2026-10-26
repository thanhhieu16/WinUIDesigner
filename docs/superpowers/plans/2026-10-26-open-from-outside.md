# Opening files from outside the app — plan

**Spec:** `docs/superpowers/specs/2026-10-26-open-from-outside-design.md`

Every task ends with all four suites green from their `.exe`s on `windows-latest`. The C# is
compiled locally first, with the Windows-only packaging targets stubbed out (never committed).

## Task 1 — `OpenFiles` and batch error reporting

`src/WinUIDesigner.Shell/MainWindow.cs`:
- `internal void OpenFiles(IEnumerable<string> paths)`. A `List<ShellErrorEventArgs> batchErrors`
  field is non-null while the batch runs, and `OnReportError` appends to it instead of showing.
  After the loop, if anything was collected, `Fire(() => ShowBatchErrorsAsync(collected))`.
- `ShowBatchErrorsAsync` builds the text the way `ShowErrorAsync` does, for each report in turn,
  under the title "Some files could not be opened". `ShowErrorAsync`'s text building is factored
  into a shared `AppendReport(StringBuilder, ShellErrorEventArgs)`.
- Test seam: `BatchErrorsForTest` records the last batch's reports, because the dialog cannot be
  shown in the test process.

Tests (`MainWindowDocumentTests`): two paths open as two tabs with the second current; one missing
and one good file open the good one and record exactly one report naming the missing path.

## Task 2 — startup

- `src/WinUIDesigner.Shell.App/App.xaml.cs`: `OnLaunched` passes
  `Environment.GetCommandLineArgs().Skip(1)` to `MainWindow`.
- `MainWindow(IEnumerable<string> startupPaths)`. The parameterless constructor stays and passes
  none. With paths, a one-shot root `Loaded` handler calls `OpenStartupFiles`.
- `internal void OpenStartupFiles(IList<string> paths)`: remember the initial document, call
  `OpenFiles`, then close the initial document through `shell.Close` if a different document is
  now current and the initial one is untouched: `FilePath == null`, `!IsDirty`, and its undo
  service has nothing to undo.

Tests: an untouched blank document is closed by a startup open; a dirty one is kept; with only a
missing path, the blank document is kept.

## Task 3 — drop

- `MainWindow.BuildLayout`: root `AllowDrop = true`, `DragOver` → `Copy` when the data view
  contains `StandardDataFormats.StorageItems`, and `Drop` → deferral, `GetStorageItemsAsync`,
  `XamlPathsIn`, `OpenFiles`.
- `internal static IList<string> XamlPathsIn(IEnumerable<string> paths)`.
- `src/WinUIDesigner.Designer/Services/CreateComponentTool.cs`: `OnDragOver`/`OnDrop` set
  `Handled` only when `CarriesThisTool(e.DataView.Properties)`.

Tests: `XamlPathsIn` with `.xaml`, `.XAML`, `.dll`, `.txt`, no extension and null entries.

## Task 4 — walkthrough

- `walkthrough.ps1`: a `launch <args...>` step: stop the app, start it again with the arguments,
  wait for its window, and re-run the window placement. A `dropfile <path> <x> <y>` step through
  a `powershell.exe -STA` helper doing `DoDragDrop` with a `FileDrop` `DataObject`.
- `steps.txt`: after the save steps, `launch` with `Documents\walk.xaml`, then a screenshot. Then
  a `dropfile` of the same file onto the canvas, which should only switch to its tab (dedupe), and
  of a second saved copy, which should add a tab.

## Task 5 — docs

Mark the shell spec's gap closed for `.xaml` and record the results here.
