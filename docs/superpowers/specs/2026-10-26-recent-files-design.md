# Recent files — design

**Date:** 2026-10-26
**Status:** approved in chat 2026-10-26 ("ok"); planned in
`docs/superpowers/plans/2026-10-26-recent-files.md`
**Closes:** the shell spec's Known gap "No recent-files list and no settings persistence; the
reference's `Settings.Default` is a WPF `ApplicationSettingsBase` facility with no direct port".

## Goal

File > Open Recent lists the files this user last opened or saved, most recent first, and
choosing one opens it. The list survives closing the app. That needs somewhere to keep settings
between runs, so this phase adds the smallest store that serves it.

Nothing else is persisted: no window size or position, no pane widths, no toolbox state. Each
is a separate decision that can use the same store later.

## What already exists

| Piece | Where | State |
|---|---|---|
| Open | `ShellModel.Open(path)` | Resolves the full path, dedupes against open tabs, never throws. |
| Save As | `ShellModel.SaveAs(doc, path)` | Sets `FilePath` to the full path on success. |
| Batch open | `MainWindow.OpenFiles(paths)` | One dialog for every failure in the batch. |
| File menu | `MainWindow.BuildMenu` | New, Open..., Close, Close All, Save, Save As..., Save All, Exit. |
| Settings | — | Nothing. `ApplicationData.Current` is not an option: it throws for an unpackaged app, and this one is unpackaged. |

## Decisions

**1. Settings live in one JSON file under the user's local app data.** The path is
`%LOCALAPPDATA%\WinUIDesigner\settings.json`, read and written with `System.Text.Json`.
`ShellSettings` is a small class with one property for now, `RecentFiles` (a list of strings).
It has `Load(path)` and `Save(path)`.
- `Load` returns empty settings for a missing, unreadable or malformed file, and never throws.
  A settings file must never stop the app from starting.
- `Save` writes to a temporary file in the same folder and then replaces the target, so a crash
  mid-write cannot leave half a file. A failed save is written to `Debug` and otherwise ignored:
  losing a recent-files entry is not worth an error dialog.

The reference keeps its settings in `Settings.Default`, a WPF `ApplicationSettingsBase`. Its
nearest .NET 10 equivalent writes `user.config` under a path derived from the assembly's
evidence hash, which is hard to find and to reset by hand. A named JSON file is neither.

**2. The list is a `RecentFileList`, independent of any UI.** It has `Items` (most recent
first), `Add(path)`, `Remove(path)`, `Clear()` and a `Changed` event.
- `Add` moves an existing entry to the top rather than duplicating it, comparing full paths
  case-insensitively, as `ShellModel.Open`'s dedupe does.
- The list keeps at most **10** entries and drops the oldest.
- Every change saves the store at once.

So two instances of the app both write the file, and the last change wins. Merging two
windows' lists is out of scope.

**3. Opening a file and saving one under a new name both add it.** `ShellModel` takes an
optional `RecentFileList`, and calls `Add` after a successful `Open` (including an `Open` that
only switched to an existing tab) and after a successful `SaveAs`. A plain `Save` does not add
the file: it already has the path it was opened or first saved with, so it is already in the
list. A failed open adds nothing. The parameterless `ShellModel()` has no list. Every existing
`ShellModelTests` test keeps using it, so none of them touches a settings file.

**4. Choosing a recent file that no longer exists removes it.** A recent entry is opened through
`OpenFiles`, so a missing file reports through the batch dialog like any other open failure. If
the open failed because the file does not exist, the entry is also removed from the list. A file
that exists but fails to parse stays in the list, because the user may fix it and try again.

**5. File > Open Recent is a `MenuFlyoutSubItem` below Open...** Its items are the full paths,
most recent first. Full paths, because two `MainPage.xaml` files in different folders must be
told apart. A separator and "Clear Recent Files" follow.
- The submenu is rebuilt on `RecentFileList.Changed`.
- With an empty list the submenu shows one disabled "(none)" item.

No accelerators: the reference's `Ctrl+1`..`Ctrl+9` style is not used anywhere in this shell.

**6. The window's store path is replaceable for tests.** `MainWindow` loads the list from
`ShellSettings.DefaultPath`, an `internal static` property that starts out as the
`%LOCALAPPDATA%` path. The Shell test assembly gets a `[SetUpFixture]` that points it at a
temporary file before any test runs, and deletes that file afterwards. Without it, every
`new MainWindow()` in the suite would read and rewrite the settings of whoever runs the tests.

## Verification

Shell suite:
- `ShellSettings`:
  - Save then Load round-trips.
  - A missing file loads empty.
  - A file of garbage loads empty and does not throw.
- `RecentFileList`:
  - `Add` puts an entry first.
  - `Add` of an existing entry moves it up, case-insensitively.
  - The 11th entry drops the oldest.
  - `Remove` and `Clear` work.
  - Each change raises `Changed` and is in the file on the next `Load`.
- `ShellModel` with a list:
  - A successful `Open` adds the file; a failed `Open` adds nothing.
  - `SaveAs` adds the file; a plain `Save` does not change the order.
- `MainWindow`:
  - Opening two files lists them in the submenu, most recent first.
  - Opening a recent entry whose file is missing removes it and reports once.
  - "Clear Recent Files" leaves the "(none)" item.

Walkthrough on the real app:
- After the save and open steps, File > Open Recent lists `walk.xaml`. The submenu needs a
  whole-screen capture.
- After the step-22 relaunch, the list is still there, which shows it was persisted across
  runs.

## Out of scope

- Persisting the window's size, position or pane layout.
- Pinned entries, a jump list, or the taskbar's recent list.
- Showing recent files on a start page.
- Merging the lists of two running instances.
