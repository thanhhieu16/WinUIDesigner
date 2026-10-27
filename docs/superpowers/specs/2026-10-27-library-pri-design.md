# Resolving a library's resources from its .pri — design

**Date:** 2026-10-27
**Status:** started on "làm mục 2" (try a real library such as CommunityToolkit), 2026-10-27
**Follows:** `2026-10-27-library-resources-design.md`

## The problem

`LibraryResources` loads a library's theme from the processed `<library>/Themes/Generic.xaml`
that a project build leaves beside the `.dll`. A NuGet package does not carry that file.
Measured on `CommunityToolkit.WinUI.Controls.Segmented` 8.2.251219:
- its `lib/net8.0-windows10.0.19041` holds a `.dll`, a `.pri`, a `.pdb` and a `.xml`, and nothing
  else;
- the `.pri` embeds the compiled XAML: XAML type names appear in it as strings, so it holds
  `.xbf` data.

So the controls of the libraries people actually use would all render at zero size.

## What the platform offers (WinAppSDK 1.7.250606001 winmd)

- `Microsoft.UI.Xaml.Application.ResourceManagerRequested`, whose args carry a
  `CustomResourceManager`. With it the app hands XAML the MRT `ResourceManager` that resolves
  `ms-appx:///`.
- `Microsoft.Windows.ApplicationModel.Resources.ResourceManager`:
  - a constructor taking a `.pri` path;
  - `MainResourceMap.TryGetValue(name[, context])`, which returns a `ResourceCandidate`;
  - a `ResourceNotFound` event whose args give the `Name` and `Context`, and accept
    `SetResolvedCandidate`.

## Decisions

**1. The app hands XAML its own resource manager and routes failed lookups to library .pri
files.**
- In `App`'s constructor, before `InitializeComponent`, `ResourceManagerRequested` supplies a
  default `ResourceManager()`, which reads the app's `resources.pri` as XAML would.
- That manager's `ResourceNotFound` goes to `LibraryPriResources.OnResourceNotFound`. It tries
  each registered library `.pri` for the missing name and hands back the first match.
- The handler never throws, because it runs inside the framework's lookup.

**2. Adding an assembly registers the `.pri` files beside it.**
- When the app has the routing in place (`LibraryPriResources.IsActive`) and the `.dll` has a
  `.pri` beside it, `LibraryResources.Load` does two things: it registers the library's metadata
  provider, as before, and it registers every `.pri` in the `.dll`'s folder. Dependencies such as
  CommunityToolkit's Extensions sit beside the library and carry their own `.pri`.
- It merges nothing. The framework then finds the library's `Generic.xbf`, and anything else the
  library addresses as `ms-appx:///<library>/...`, by itself.

**3. The text-theme path stays as the fallback.** It is used when there is no `.pri`, and in the
test process, which has no routing.

**4. How a library `.pri` names its files is measured, not assumed.** The resolver tries three
forms:
- the name as asked;
- the name without its `Files/` root;
- the name under the map's `Files` subtree.

A temporary trace (`WINUIDESIGNER_RESOURCE_TRACE`) records every lookup on the runner.

## Verification

Walkthrough, on the real app:
- `SampleCard` (steps 27–28) still renders, now through its `.pri`. This is the regression check.
- After a relaunch with `CommunityToolkit.WinUI.Controls.Segmented` remembered in the settings,
  a document with a `Segmented` of three items, typed in the XAML editor, renders as a segmented
  control in Design.
- The trace shows which names were resolved, and from which `.pri`.

The in-process suites cannot exercise this: the test host has no `App` and no routing. They
cover only the unchanged text-theme path.

## Out of scope

- Resolving a library's dependencies from NuGet. The user points at a folder that already holds
  them.
- Libraries whose resources are only in a `resources.pri` merged into some other app.
