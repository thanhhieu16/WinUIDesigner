# Recent files — plan

**Spec:** `docs/superpowers/specs/2026-10-26-recent-files-design.md`

Every task ends with all four suites green from their `.exe`s on `windows-latest`. The C# is
compiled locally first, with the Windows-only packaging targets stubbed out (never committed).

## Task 1 — the store

New `src/WinUIDesigner.Shell/ShellSettings.cs`:
- `public List<string> RecentFiles`.
- `static ShellSettings Load(string path)`: broad catch, so it never throws.
- `void Save(string path)`: create the directory, write to `path + ".tmp"`, then `File.Move(..., overwrite: true)`.
- `internal static string DefaultPath`: starts as `%LOCALAPPDATA%\WinUIDesigner\settings.json`.

New `src/WinUIDesigner.Shell/RecentFileList.cs`:
- `RecentFileList(string storePath)` loads the store at that path.
- Members: `IReadOnlyList<string> Items`, `Add`, `Remove`, `Clear`, `event EventHandler Changed`, and `const int Capacity = 10`.
- Every mutation saves the store, then raises `Changed`.

Tests: `ShellSettingsTests`, `RecentFileListTests`. Each uses its own temp file, and neither touches WinUI.

## Task 2 — `ShellModel` records opens and Save As

- `ShellModel(RecentFileList recentFiles)`. The parameterless constructor passes null.
- `Open`: `Add(fullPath)` on both success paths (switching to an existing tab, and a new load).
- `SaveAs`: `Add(fullPath)` after a successful write.

Tests in `ShellModelTests`, using a list over a temp file.

## Task 3 — the menu

`MainWindow`:
- Builds `shell` from `new RecentFileList(ShellSettings.DefaultPath)`. The field initializer
  becomes constructor code.
- `MenuFlyoutSubItem openRecentItem` sits after Open.... `RebuildRecentMenu()` is called once
  and again on `Changed`.
- `internal void OpenRecent(string path)`:
  1. Calls `OpenFiles(new[] { path })`.
  2. If that batch reported a failure and `!File.Exists(path)`, calls `Remove(path)`.
- Test seam: `OpenRecentItemForTest`.

Test assembly: `tests/WinUIDesigner.Shell.Tests/ShellSettingsFixture.cs`, a `[SetUpFixture]`
that sets `ShellSettings.DefaultPath` to a temp file and deletes it afterwards.

Tests in `MainWindowDocumentTests`:
- Two files open in order and are listed most recent first.
- A missing recent entry is removed.
- "Clear Recent Files" leaves "(none)".

## Task 4 — walkthrough

`steps.txt`:
1. After step 17 (the file opened by double-click), click File, hover Open Recent, and take a whole-screen shot.
2. After the step-22 relaunch, do the same again.

Coordinates for Open Recent come from the first run's screenshot.

## Task 5 — docs

- Mark the shell spec's gap closed.
- Record the results here.
