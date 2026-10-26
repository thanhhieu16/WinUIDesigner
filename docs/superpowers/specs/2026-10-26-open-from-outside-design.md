# Opening files from outside the app — design

**Date:** 2026-10-26
**Status:** approved in chat 2026-10-26 ("agree"); planned in
`docs/superpowers/plans/2026-10-26-open-from-outside.md`
**Closes:** the shell spec's Known gap "No opening files from command-line arguments, and no
drag-drop of `.xaml` or `.dll` onto the window" — the `.xaml` half. The `.dll` half belongs to
runtime assembly loading into the toolbox, which is its own gap and stays open.

## Goal

1. `WinUIDesigner.Shell.App.exe a.xaml b.xaml` starts with those files open, one tab each.
   This is what "Open with" in Explorer and dropping a file onto the `.exe` both pass.
2. Dragging one or more `.xaml` files from Explorer onto the window opens them.

Nothing else: no single-instance handoff (a second launch is a second window), no file
association registration, no recent-files list.

## What already exists

| Piece | Where | State |
|---|---|---|
| Open one path | `ShellModel.Open(path)` | Full path, dedupe against open tabs, `ReportError` on failure, never throws. |
| Error reporting | `MainWindow.OnReportError` → `ShowErrorAsync` → `ShowDialogAsync` | Shows **one** dialog. A second report while one is up, or any report before the window has a `XamlRoot`, is only written to `Debug` and is lost. |
| Startup | `Program.Main(args)` → `App.OnLaunched` → `new MainWindow()` | The constructor calls `shell.New()`; args are ignored. |
| Drop target | `DesignPanel` (`AllowDrop`) + `CreateComponentTool.OnDragOver`/`OnDrop` | Subscribed only while a toolbox tool is current, and they set `Handled = true` for every drag, including one that is not theirs. |

## Decisions

**1. One entry point for a batch of paths: `MainWindow.OpenFiles(IEnumerable<string>)`.**
Both the command line and a drop call it. It calls `shell.Open` for each path in order, and the
last one opened becomes the current tab (which `Open` already does). It is `internal`, so the
Shell suite drives it directly, as it drives `ActivateDocument` instead of `TabView` events.

**2. A batch reports its errors once, in one dialog.** Today a second `ReportError` while the
first dialog is up is lost, so dropping three broken files would show one message. While
`OpenFiles` runs, `OnReportError` appends to a list instead of showing a dialog. After the batch,
the list is shown as one "Some files could not be opened" dialog: each failure's message, plus
the first parse errors as `ShowErrorAsync` lists them today. Outside a batch nothing changes.

**3. Command-line paths are opened once the window has a `XamlRoot`.** `ShowDialogAsync` cannot
show anything before then (it returns null and the message goes to `Debug`), and the constructor
runs before activation. So `App.OnLaunched` passes the paths to `MainWindow`, which opens them
from its root element's first `Loaded`. The paths come from `Environment.GetCommandLineArgs()`
minus the first entry. `LaunchActivatedEventArgs.Arguments` is not used: for an unpackaged app it
is one unsplit string, and splitting it would mean re-implementing Windows' quoting rules.

**4. A launch that opens a file closes the blank document it started with**, if nothing has
touched it: no file path, not dirty, and the undo stack empty. The window always starts with a
blank document (the constructor's wiring depends on `shell.New()`), and launching with a file
and finding two tabs, the first an empty `New1`, is noise. If no file opened, the blank
document stays, so the window is never empty. This applies only to the command line. A drop
adds tabs the way File > Open does.

**5. Command-line arguments are opened whatever their extension; a drop accepts only `.xaml`.**
A path on the command line is an explicit request, and `Open` reports a file that is not XAML.
A drop can bring anything Explorer can drag, so it filters by extension, case-insensitively.
`.dll` is filtered out on purpose: it would mean "add to the toolbox" in the reference, which
this shell cannot do yet. `DragOver` cannot read the dragged paths (Decision 6), so a drag of
only non-XAML files still shows the copy cursor and opens nothing on release.

**6. The drop target is the window's root `Grid`.** `AllowDrop = true`, plus `DragOver` and
`Drop` handlers. `DragOver` accepts `Copy` when `e.DataView.Contains(StandardDataFormats.StorageItems)`.
It cannot read the items synchronously, so the extension filter runs on `Drop`. `Drop` takes a
deferral, awaits `GetStorageItemsAsync`, keeps the `StorageFile`s whose `Path` ends in `.xaml`,
and calls `OpenFiles`. The handlers are thin; their logic is a `static` filter, `XamlPathsIn`,
that the suite tests. The toolbox drag never carries `StorageItems`, so it never reaches this
path.

**7. `CreateComponentTool` leaves a drag that is not its own unhandled.** While a toolbox item is
armed, its `OnDragOver`/`OnDrop` set `Handled = true` with `AcceptedOperation = None` for every
drag. That stops a file drag over the surface from bubbling to the window. They will set
`Handled` only when `CarriesThisTool`. This is a one-line behaviour change in `WinUIDesigner.Designer`.
It is the defect the shell exposed, which the shell spec's Out of scope allows.

**8. A drop on the XAML editor is the editor's.** The `TextBox` handles drops itself. Measured
during the work rather than assumed: if it swallows a file drag, a file dropped on the editor
does nothing, and that is recorded as a known gap rather than worked around.

## Verification

Shell suite (in-process, through `OpenFiles` and `XamlPathsIn`):
- Two paths open as two tabs, and the second is current.
- A batch with one missing file and one good file opens the good one and reports once, naming
  the missing one.
- An untouched blank document is closed by a startup open, and kept when it is dirty or when
  nothing opened.
- `XamlPathsIn` keeps `.xaml`/`.XAML` and drops `.dll`, `.txt` and paths with no extension.

Designer suite: `HandleDragOver`'s callers. A unit test that a non-tool drag leaves `Handled` false
needs a `DragEventArgs`, which a test cannot construct, so this one is covered by the walkthrough.

Walkthrough (real app on the runner):
- **Command line:** a new `launch <args>` step ends the running app and starts it again with
  arguments. After the save steps, the app is relaunched with `Documents\walk.xaml`. The
  screenshot should show one tab, `walk.xaml`, with the saved design.
- **Drop:** a `dropfile <path> <x> <y>` step. A helper `powershell.exe -STA` process presses the
  left button and starts an OLE `DoDragDrop` with a `FileDrop` data object; a timer moves the
  cursor onto the target and releases the button. This is untried. If it cannot be made to
  work on the runner, the drop is recorded as verified only through its seams, not claimed.

## Out of scope

- Single-instance handoff, file association, jump lists.
- Dropping a `.dll` onto the window to add its controls to the toolbox.
- Dropping a file onto the XAML editor to insert its text.
- Recent files.
