# Adding control assemblies to the toolbox — plan

**Spec:** `docs/superpowers/specs/2026-10-27-toolbox-assemblies-design.md`

Every task ends with all four suites green from their `.exe`s on `windows-latest`. The C# is
compiled locally first, with the Windows-only packaging targets stubbed out (never committed).
The sample library carries XAML, so it builds only on the runner.

## Task 1 — `using:` on write (XamlDom)

- In `XamlTypeFinder.GetXmlNamespaceFor` and `GetXmlNamespacesFor`, use `using:Ns` in place of the
  `clr-namespace:` fallback when `getClrNamespace` is false.
- Update the tests that assert the old form. Add a test that writes a fixture type and parses the
  output back to the same type.
- Run the XamlDom, DesignModel and Designer suites, since all three serialize fixture types.

## Task 2 — `Toolbox.AddAssembly` reports

- `AddAssemblyResult`: `Added` (an `AssemblyNode`, or null if it was already present) and
  `Skipped` (type names).
- The existing WinUI seed call ignores the result.
- `Toolbox.LoadAssembly(string path, out string error)`: `Assembly.LoadFrom`, with a broad catch
  that turns every failure into `error`.

Test controls in `tests/WinUIDesigner.Shell.Tests/TestControls.cs`:
- `TestBadge : UserControl`, its content built in code (WinUI's `Border` is sealed).
- `ThrowingControl : UserControl`, whose constructor throws.

## Task 3 — the view follows the toolbox

`ToolboxView` subscribes to `AssemblyNodes.CollectionChanged`, rebuilds `nodes`, and re-applies
`Filter`. Test: after `AddAssembly`, `VisibleTypesForTest` contains `TestBadge`, and with a filter
set it still narrows.

The shared `Toolbox.Instance` keeps the test assembly for the rest of the process. Tests that
count the toolbox's entries are checked for that.

## Task 4 — the command, the drop, the settings

- `ShellSettings.ToolboxAssemblies`.
- `MainWindow`:
  - `internal void AddAssemblies(IEnumerable<string> paths)`: shares `OpenFiles`' batch
    reporting.
  - File > Add Controls Assembly... (a `FileOpenPicker` filtered to `.dll`).
  - The drop handler also takes `AssemblyPathsIn`.
  - `internal void LoadRememberedAssemblies()` runs in the constructor before `shell.New()`. Its
    failures are collected and shown on the first `Loaded`, with the startup files.
- Tests: the recording, the startup seam with a missing path, and the filter.

## Task 5 — the sample library and the walkthrough

- `samples/WinUIDesigner.SampleControls`: a csproj, `SampleBadge.cs`, `SampleCard.cs` and
  `Themes/Generic.xaml`. Added to the solution.
- `walkthrough.yml`: a step before Walk copies the library's build output into Documents.
- `steps.txt`:
  1. File > Add Controls Assembly..., then double-click the DLL in the picker (coordinates from
     the first run).
  2. Screenshot the toolbox scrolled to the end, and use the search box to show `Sample`.
  3. Place `SampleBadge` and Save As `walk2`.
  4. `launch {docs}\walk2.xaml`.
- The workflow prints `walk2.xaml`.

## Task 6 — docs

- Close the shell spec's gaps.
- Record `SampleCard`'s measured behaviour in the plan's Results and in CLAUDE.md.
