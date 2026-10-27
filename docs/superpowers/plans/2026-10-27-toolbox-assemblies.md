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

## Results

CI: all four suites are green from their `.exe`s on `windows-latest`.
- XamlDom gained two tests:
  - `AnUnmappedNamespaceIsWrittenAsUsingAndClrNamespaceOnlyOnRequest`
  - `AControlFromAnUnmappedAssemblyIsWrittenAsAUsingNamespaceAndReloads`
- The Shell suite gained `ToolboxAssemblyTests` (6).
- Two existing Shell tests needed their expectations updated, and both are right to change:
  - `BothMenusRenderAndCarryTheirItems` now lists Add Controls Assembly....
  - The new view test filters on `TestBadge`, because "Badge" also matches WinUI's
    `InfoBadge`.

Walkthrough, steps 23–28, on the real app with `samples/WinUIDesigner.SampleControls`:
- File > Add Controls Assembly... opens the picker. Double-clicking
  `WinUIDesigner.SampleControls.dll` adds a group: the search box finds `SampleBadge` and
  `SampleCard`.
- The saved `walk.xaml` declares `xmlns:Controls0="using:WinUIDesigner.SampleControls"` and
  carries both controls.
- After a relaunch with that file as the argument, the library is loaded from the settings
  before the document opens, and both controls come back.
- An earlier run also showed the refusal path on the real app. The build output's WebView2
  native `.dll` was picked by mistake and reported as "is not a .NET assembly".

**Decision 7, measured.** `SampleBadge`, built in code, renders as itself: a green "Sample
badge". `SampleCard`, a templated `Control` whose template is in its library's
`Themes/Generic.xaml`:
- activates, so it is listed and not reported as skipped;
- places, appears in the outline, and saves and reloads;
- renders **nothing**: it has zero size, and only its adorner shows at the placement point.

So the library's `Generic.xaml` is not found when the library is loaded at runtime into this
unpackaged app, as predicted. The control is still usable, in that it can be placed, edited in
the property grid and saved, but it cannot be seen. Showing such controls would mean loading the
library's own `.pri`, which remains out of scope.

Three walkthrough-only problems were fixed on the way, none of them in the product:
- The menu item needed the documented second click.
- The workflow first copied WebView2's native `.dll`s along with the library.
- The prefix XamlDom picks for the new namespace is `Controls0`. It is valid XAML, but a
  hand-written file would say `local` or `controls`. That is cosmetic and left as is.
