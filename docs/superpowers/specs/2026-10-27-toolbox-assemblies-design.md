# Adding control assemblies to the toolbox — design

**Date:** 2026-10-27
**Status:** approved in chat 2026-10-27 ("đồng ý"); planned in
`docs/superpowers/plans/2026-10-27-toolbox-assemblies.md`
**Closes:** the shell spec's Known gap "No runtime assembly loading into the toolbox", and the
`.dll` half of "no drag-drop of `.xaml` or `.dll` onto the window". It also narrows "A control
that fails to activate is dropped from the toolbox silently" for user assemblies.

## Goal

File > Add Controls Assembly... lets the user pick a `.dll`. Its public controls appear in the
toolbox under the assembly's name, can be placed like WinUI's own, and the document saves and
reloads with them. Added assemblies are remembered and loaded again on the next start, so a
document using them opens.

## What already exists

| Piece | Where | State |
|---|---|---|
| Toolbox model | `Toolbox.AddAssembly(Assembly)` | Works for any assembly: lists public `UIElement` types with a public parameterless constructor that activate, and registers the assembly with `ShellTypeFinder`. Only ever called for WinUI's own assembly. |
| Toolbox view | `ToolboxView` | Copies `Toolbox.Instance.AssemblyNodes` once, in its constructor, so a later `AddAssembly` never shows. |
| Reading `using:` | `XamlTypeFinder.ParseUsingNamespace` | Resolves `xmlns:x="using:Ns"` against registered assemblies, the entry assembly, and every loaded one. |
| Writing a namespace | `XamlTypeFinder.GetXmlNamespaceFor` | For any type without a mapped XML namespace (every non-WinUI type) it writes `clr-namespace:Ns;assembly=Asm`. WinUI's own `XamlReader` rejects that form ("unknown namespace", measured in Phase 1), and real WinUI XAML never uses it. |
| Settings | `ShellSettings` | JSON file with `RecentFiles`. |

## Decisions

**1. An assembly is loaded with `Assembly.LoadFrom` into the default context.** `LoadFrom`
finds the library's own dependencies in its folder. A collectible `AssemblyLoadContext` would
allow unloading, but WinUI objects created from its types are referenced by the native XAML
core, so it would never actually unload. Loading runs the library's code: static constructors,
and each control's constructor during the activation check. That is inherent in a designer that
shows real controls, and only happens on the user's explicit choice.

**2. `Toolbox.AddAssembly` reports what it did instead of failing silently.** It returns an
`AddAssemblyResult` with:
- the controls added;
- the names of the types it skipped because activation threw.

A file that is not a .NET assembly (`BadImageFormatException`), cannot be loaded, or contains
no controls is reported through `ShellModel.ReportError`'s dialog, as a failed open is. Skipped
types are listed in the same dialog, at most ten names plus a count. Adding an assembly that is
already in the toolbox (same assembly name) changes nothing and says so.

**3. Non-WinUI types are written as `using:Ns`.** `GetXmlNamespaceFor` returns
`using:` + the CLR namespace when no XML namespace is mapped for the type. `clr-namespace:` is
still produced when a caller asks for it (`getClrNamespace: true`). The parser already reads
both forms, so documents saved before this change still load.

This changes XamlDom output for every test fixture type too, which is the point. Tests that
assert the old form are updated, not kept.

**4. The toolbox view follows `AssemblyNodes`.** `ToolboxView` subscribes to
`AssemblyNodes.CollectionChanged`, rebuilds its list, and re-applies the current filter. Each
assembly's controls stay grouped and sorted as today: WinUI first, then each added assembly in
the order added.

**5. Added assemblies are remembered by path.** `ShellSettings` gains `ToolboxAssemblies`, a
list of full paths. A successful add appends to it. At startup, `MainWindow` loads each
remembered path before opening any document, so `using:` namespaces in a recent or
command-line file resolve. A remembered path that no longer exists, or fails to load, is
skipped and reported once, in one dialog, and stays in the list. A library can be on a removable
drive or mid-rebuild, and dropping it silently would be worse.

Removing an assembly from the toolbox is out of scope: it cannot be unloaded (Decision 1), so
removal would only take effect after a restart. The settings file can be edited by hand
meanwhile.

**6. A `.dll` dropped on the window is added to the toolbox.** This uses the same path as the
menu command: `XamlPathsIn`'s counterpart `AssemblyPathsIn` picks out `.dll` files. Like the
`.xaml` drop, it is verified only through its seams (see CLAUDE.md's runner finding).

**7. What a control from a library can look like is measured, not assumed.** Two kinds of
control behave differently.
- **A control built in code** (a `UserControl`, `Panel` or `Button` subclass that sets its own
  content in code; WinUI's `Border` is sealed)
  needs nothing from the library but its types. This phase supports it, and it is the case the
  tests prove.
- **A control whose look comes from XAML in its library** (a templated control with
  `Themes/Generic.xaml`, or a `UserControl` with its own `.xaml`) loads that XAML from
  `ms-appx:///Library/...`. In an unpackaged app those resources resolve through the app's
  merged `resources.pri`, and a library loaded at runtime is not in it.

  So the second kind is expected to show without its template, or to fail to activate and be
  listed as skipped. The sample library includes one of each, and the walkthrough records what
  actually happens. Making the second kind work would mean loading the library's own `.pri`,
  which is its own phase if the measurement says it is needed.

**8. A sample control library is part of the repository, referenced by no test project.**
`samples/WinUIDesigner.SampleControls` is a WinUI class library with:
- `SampleBadge`: a `UserControl` whose content, a `Border` holding a `TextBlock`, is built in
  code, with a `Text` dependency property.
- `SampleCard`: a templated `Control` with `Themes/Generic.xaml`. This is the measurement case
  for Decision 7.

It sits outside `src/`, so `src/` keeps exactly one `.xaml` file, and outside `tests/`, because no
test host may reference a project carrying XAML pages without risking CLAUDE.md's measured
fail-fast. The solution builds it, and nothing references it.

The Shell suite cannot load it either, since a test project referencing it would be the case
above. So the in-process tests use the test assembly's own code-built control types (Decision 9),
and the sample library is exercised only by the walkthrough, on the real app.

**9. The in-process tests use a control type the test assembly defines itself.**
`Toolbox.AddAssembly(typeof(SomeTestControl).Assembly)` is the same code path a loaded DLL
takes, short of `LoadFrom`. The tests cover:
- the result and the skipped-type list;
- that the view updates;
- placing the control;
- the saved `using:` namespace;
- reloading the saved text;
- the remembered-path list;
- startup loading, through a seam that takes the paths.

## Verification

XamlDom suite:
- A type with no mapped namespace is written as `using:Ns`, and that output parses back to the
  same type.
- `clr-namespace:` on request is unchanged.

Shell suite:
- `AddAssembly` on the test assembly lists its controls and reports a type that throws on
  construction as skipped.
- A second add of the same assembly changes nothing.
- `ToolboxView` shows the new controls without being rebuilt, and the filter still applies.
- A placed test control saves as `using:` and the saved text reloads to the same type.
- Adding records the path in `ShellSettings`.
- The startup seam loads remembered paths before documents. A missing path is reported once and
  kept.
- `AssemblyPathsIn` keeps only `.dll`.

Walkthrough, on the real app:
1. Copy the sample library's build output into Documents.
2. File > Add Controls Assembly..., then double-click the DLL.
3. The toolbox shows `SampleControls`, with `SampleBadge`, and `SampleCard` or its skipped
   report.
4. Place `SampleBadge` and save as `walk2.xaml`. The saved file is printed, showing `using:`.
5. Relaunch with `walk2.xaml`: the assembly is loaded from settings and the badge is on the
   canvas.
6. Record what `SampleCard` did.

## Out of scope

- Removing an assembly from the toolbox, and unloading.
- Loading a library's `.pri` so XAML-styled controls get their templates (pending Decision 7's
  measurement).
- Toolbox categories, icons and popular-control filtering.
- NuGet packages or project references as a source of controls.
