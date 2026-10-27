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
