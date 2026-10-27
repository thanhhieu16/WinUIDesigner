# Showing templated controls from an added library — design

**Date:** 2026-10-27
**Status:** approved in chat 2026-10-27 ("đồng ý"); planned in
`docs/superpowers/plans/2026-10-27-library-resources.md`
**Follows:** `2026-10-27-toolbox-assemblies-design.md`, Decision 7. Its measurement showed that a
templated control from a library added at runtime (`SampleCard`) renders nothing.

## The problem, precisely

A templated control finds its look through its `DefaultStyleKey`. The XAML framework resolves
that key in the library's `Themes/Generic.xaml`, which it locates as
`ms-appx:///<Library>/Themes/Generic.xaml` through the app's merged `resources.pri`. A library
loaded at runtime was never merged into that file, so the lookup finds nothing. The control
gets no template and measures to zero size.

Most real control libraries, such as CommunityToolkit's, are templated controls. Until this is
solved, the toolbox can add them but not show them.

## What the platform offers (read from the WinAppSDK 1.7.250606001 build files)

1. **A library's build output carries its XAML as text.** For any project that is not an
   `.exe`, `GenerateLibraryLayout` defaults to `true`
   (`buildTransitive/Microsoft.Build.Msix.Pri.targets:25`). With it,
   `AddProcessedXamlFilesToCopyLocal`
   (`buildTransitive/Microsoft.WinUI.NET.Markup.Compiler.targets:23`) copies the processed `.xaml`
   files into the output, under a folder named after the library, beside the `.xbf`:
   `<output>/WinUIDesigner.SampleControls/Themes/Generic.xaml`. The previous phase's walkthrough
   copied only top-level files, so this folder was never looked at.
   - "Processed" means the markup compiler has already removed what only it can handle.
   - NuGet packages of WinUI libraries ship the same layout.
2. **The app can register another XAML metadata provider at runtime.** The code the XAML
   compiler generates for `App` contains `partial void _AddOtherProvider(IXamlMetadataProvider
   otherProvider)`, which forwards to the app provider's `AddOtherProvider` (a string template
   in `Microsoft.UI.Xaml.Markup.Compiler.dll`).
   - A WinUI class library generates its own public `XamlMetaDataProvider`, describing its
     types.
   - Registering it is what lets `XamlReader.Load` resolve `local:SampleCard` in that library's
     XAML. Without it, the reader cannot name the library's types at all.
3. **An implicit style in `Application.Resources` applies to every instance of its target
   type**, a templated control included. This is ordinary XAML resource lookup, not a new
   mechanism.

Both 1 and 2 are read from build files, not yet observed in this app. The first walkthrough run
measures them (see Verification).

## Decisions

**1. After an assembly is added, its theme XAML is loaded and merged into the application's
resources.** `LibraryResources.Load(assembly, assemblyPath)` does three things:
- It looks for `<folder of the .dll>/<assembly name>/Themes/Generic.xaml`.
- It registers the library's `IXamlMetadataProvider` with the application (Decision 2).
- It loads the file with `XamlReader.Load` into a `ResourceDictionary`, and adds that to
  `Application.Current.Resources.MergedDictionaries`.

Its styles then apply to the library's controls on the design surface. A library with no such
file is normal (a code-built library like the test assembly) and nothing is reported.

**2. `App` exposes provider registration through a Shell interface.**
`WinUIDesigner.Shell` declares `IXamlMetadataProviderHost` with one method,
`AddProvider(IXamlMetadataProvider)`. `App` implements it by calling the generated
`_AddOtherProvider`.

The provider is found as the library's public, non-abstract types implementing
`IXamlMetadataProvider` that have a parameterless constructor. One is created per library.

When `Application.Current` is not a host, nothing is registered. This is the case in the test
process, whose `TestApplication` already resolves every loaded type by reflection.

**3. A theme that fails to load is reported, and the controls stay.** If `XamlReader.Load` throws,
the failure is reported in the add dialog, and the toolbox keeps the library's controls. They
work as they did before this phase: they can be placed, edited and saved, just without their
look.

Typical causes are a resource the library pulls in with
`<ResourceDictionary Source="ms-appx:///...">` and an image under `ms-appx:///`. Both need the
library's `.pri`, which stays out of scope.

**4. The theme dictionary is merged once per library, and only for libraries added
successfully.** That covers adding through the menu, through a drop, and from the remembered
list at startup. An assembly already present does not merge again.

## Verification

Shell suite:
- A test assembly control, `TestCard`, is a templated `Control` with no template of its own.
- The test writes a `<temp>/WinUIDesigner.Shell.Tests/Themes/Generic.xaml` layout and calls
  `LibraryResources.Load` with a temporary path beside it. A `TestCard` placed in the shared
  window then has a template: its visual tree has a child.
- A missing layout folder returns no error and merges nothing.
- A malformed `Generic.xaml` returns an error and merges nothing.

Walkthrough, on the real app:
- **R1:** the workflow lists the sample library's whole build output. This confirms
  `WinUIDesigner.SampleControls/Themes/Generic.xaml` is there, and copies that folder into
  Documents along with the `.dll`.
- **R2:** the app compiles against `_AddOtherProvider`. If the generated method is absent, CI
  fails at build time and the decision is revisited.
- **R3 and R4:** `SampleCard` placed from the toolbox shows its blue card with "Sample card",
  both after the add and after a relaunch that loads the library from the settings.

## Out of scope

- Loading a library's `.pri`, and with it `ms-appx:///` images and resources inside the library.
- XAML files in a library other than `Themes/Generic.xaml`, such as a `UserControl` with its own
  `.xaml`. Their `InitializeComponent` resolves through `ms-appx:///` too, so they stay
  unsupported, and the plan's Results says so.
- Theme dictionaries keyed by light/dark (`ThemeDictionaries`) beyond what `XamlReader.Load`
  already handles.
