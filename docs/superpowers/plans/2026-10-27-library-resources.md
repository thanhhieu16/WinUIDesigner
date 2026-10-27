# Showing templated controls from an added library — plan

**Spec:** `docs/superpowers/specs/2026-10-27-library-resources-design.md`

Every task ends with all four suites green from their `.exe`s on `windows-latest`. `Shell.App`
and the sample library carry XAML, so they build only on the runner.

## Task 1 — measure first

Change `walkthrough.yml`'s copy step:
- Print the sample library's whole build output, recursively.
- Copy the `WinUIDesigner.SampleControls` folder as well as the library's own top-level files.

Then shift the picker step's double-click down one row, because the folder now sorts above the
`.dll`. Push this alone, and record R1 before writing code.

## Task 2 — the host interface and `App`

- `src/WinUIDesigner.Shell/IXamlMetadataProviderHost.cs`.
- `src/WinUIDesigner.Shell.App/App.xaml.cs`: `App : IXamlMetadataProviderHost`, whose
  `AddProvider` calls `_AddOtherProvider`. This is R2: CI's build of `Shell.App` proves the
  generated method exists.

## Task 3 — `LibraryResources`

New `src/WinUIDesigner.Shell/LibraryResources.cs`, with
`internal static string Load(Assembly assembly, string assemblyPath)`, which returns an error or
null:
1. Find the layout file. If it is absent, return null.
2. Register the providers through the host, if `Application.Current` is one.
3. Call `XamlReader.Load(File.ReadAllText(...))` as a `ResourceDictionary`. On any failure,
   return the message.
4. Add the result to `Application.Current.Resources.MergedDictionaries`.

`ShellModel.AddAssembly` calls it after a successful add that is not `AlreadyPresent`. It
reports a returned error as "<name>'s styles could not be loaded: ...".

Tests in `LibraryResourcesTests`:
- `TestCard` gets its template from a temporary layout.
- No layout means no error.
- A malformed file is reported and merges nothing.

Each test removes the dictionaries it merged.

## Task 4 — walkthrough

`SampleCard` is already placed at step 27 and relaunched at step 28. Its screenshots now show
whether R3 and R4 hold.

## Task 5 — docs

- Record the results.
- Update the toolbox-assemblies plan's Decision 7 note and CLAUDE.md's runner finding.

## Results

On the real app (walkthrough steps 27 and 28), `SampleCard` renders its blue "Sample card"
template, both right after the library is added and after a relaunch that loads the library from
the settings. CI is green on `windows-latest`, with all four suites run from their `.exe`s. New
tests are in `LibraryResourcesTests` (3).

The spec's four risks:
- **R1 holds.** The sample library's build output carries
  `WinUIDesigner.SampleControls/Themes/Generic.xaml` and `Generic.xbf` beside the `.dll` and
  `.pri`, as listed by the workflow.
- **R2 failed as written, and was worked around.** This project's generated `App` has no
  `_AddOtherProvider` (CS0103), and its `XamlMetaDataProvider` has no `AddOtherProvider`, though
  the compiler carries templates with both. A temporary workflow step printed the generated
  `XamlTypeInfo.g.cs`. It shows `App._AppProvider`, whose private `Provider`
  (`XamlTypeInfoProvider`) has a private `OtherProviders` list. That list is lazily seeded with
  WinUI's `XamlControlsXamlMetaDataProvider`, and every failed type lookup falls through to it.
  `App.AddProvider` adds the library's provider to that list by reflection. This relies on
  generated internals: an SDK that renames them makes the add dialog say so, and nothing else
  breaks.
- **R3 holds.** With the provider registered, `XamlReader.Load` resolves `local:SampleCard` in
  the library's theme.
- **R4 holds.** The merged implicit style gives the control its template.

Two findings from the test host, both bisected on the runner with temporary workflow steps:

1. **A failed `XamlReader.Load` leaves a stowed error behind, even when its exception is
   caught.** The Shell suite's host died with `0xC000027B` a few tests after the malformed-theme
   test. With only that test left out, the suite passed (125/125).
   - `LibraryResources` now checks a theme is well-formed XML with `XmlReader` before
     `XamlReader` sees it, so the likeliest broken theme never reaches WinUI's parser.
   - A theme that is well-formed but semantically wrong still goes to `XamlReader`. Examples are
     a type the provider cannot name and `Source="ms-appx:///..."`. Whether the real app survives
     that stowed error was not verified here. It was measured afterwards: it does
     (`2026-10-27-broken-theme-measurement.md`).
2. **Rendering a templated test control in the shared window is not possible here.** It needs
   resources the test process does not have. So the in-process test asserts the merged
   dictionary's `Style` and its `TargetType`. That `TargetType` is the library's type resolved
   through the provider path. The rendering itself is the walkthrough's to show.

The Shell test step now runs with `--labels=Before`, so a host that dies names the test it died
in. That is how both findings were located, and it stays.
