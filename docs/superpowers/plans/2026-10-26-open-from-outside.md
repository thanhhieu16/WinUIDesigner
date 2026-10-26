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
  of a second saved copy, which should add a tab. (The `dropfile` half was tried and removed; see
  Results.)

## Task 5 — docs

Mark the shell spec's gap closed for `.xaml` and record the results here.

## Results

CI: all four suites are green from their `.exe`s on `windows-latest`. New in the Shell suite:
- `OpenFilesOpensEachPathAsATabAndTheLastBecomesCurrent`
- `ABatchOpensWhatItCanAndCollectsEveryFailureWithItsPath`
- `AStartupOpenClosesTheUntouchedBlankDocument`
- `AStartupOpenKeepsABlankDocumentThatHasChanges`
- `AStartupOpenThatOpensNothingKeepsTheBlankDocument`
- `ADropOpensOnlyXamlFiles`

Walkthrough, step 22, on the real app: launched with `Documents\walk.xaml` as its argument, the
window shows one tab, `walk.xaml`, with the saved design (the typed button). The blank `New1` was
closed.

**The file drop is not verified on the real app.** Four walkthrough runs tried to simulate a drag
from Explorer with an OLE `DoDragDrop` from a helper `powershell.exe -STA` process, using the same
`mouse_event` input that drives the in-app toolbox drag. They measured, with a temporary file
trace in `MainWindow`'s `DragOver`/`Drop` and the helper's own `GiveFeedback`/`QueryContinueDrag`:

1. Calling `DoDragDrop` right after injecting the button press ended the drag on the spot: the
   source thread had not yet processed the press. Starting the drag from the helper window's own
   `MouseDown` fixed that.
2. After that, the drag ran its whole path and ended by the button's release over the target, at
   the expected screen points on the canvas and on the tab strip (`keyState=0`, no Escape). The
   result was `None` every time, and the app's trace recorded **no** `DragOver` or `Drop` at all,
   over the design surface or outside it. The same trace did record the in-app toolbox drag.

So on the runner, an OLE drag from another process never reaches this WinUI window as a drop
target. Why is not established. Both processes run under the runner's same account, so a UIPI
integrity mismatch is not the obvious cause. The helper and its walkthrough step were removed.
The drop handlers stay, covered by `XamlPathsIn` and `OpenFiles` (the logic) and by the
`CreateComponentTool` change (a drag that is not the tool's is no longer marked handled). A
manual check is the remaining verification: drag a `.xaml` from Explorer onto the window.
