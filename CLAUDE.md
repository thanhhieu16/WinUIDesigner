# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A port of SharpDevelop's `WpfDesign` (a runtime WYSIWYG XAML designer engine) from WPF to **WinUI 3**. Read-only reference source for the port lives at `F:\ReferencesCode\CSharp\WpfDesigner` — never edit it, only read from it. The port is being built in five sequential phases (XamlDom → DesignModel core engine → DesignPanel/DesignSurface → concrete design-time behaviors → shell app); phases 1–4b are done. Each phase has a spec and a plan under `docs/superpowers/specs/` and `docs/superpowers/plans/` — read the relevant one for exact rationale, verified WinUI API facts, and file:line history before making non-trivial changes in an area you haven't touched before.

`src/` contains exactly one `.xaml` file: `src/WinUIDesigner.Shell.App/App.xaml`. That is not a
style choice. A WinUI 3 project containing no compiled XAML produces no app-level merged
`resources.pri`, so `ms-appx:///` cannot be resolved, `XamlControlsResources` fails to load
`themeresources.xaml`, and every control whose default template references a theme brush
fail-fasts the process with `0xC0000409` during the first layout pass. All other shell UI is
written in C#, like `OutlineViewControl` and `PropertyGridControl`. Do not add a second
`.xaml` file, and do not add a hand-written `app.manifest` — it replaces the generated
registration-free WinRT activation manifest and the app will not start.

## Build

```
dotnet build WinUIDesigner.sln
```

Target framework for every project: `net10.0-windows10.0.19041.0`, `UseWinUI=true`, `Microsoft.WindowsAppSDK` pinned to `1.7.250606001` (do not bump silently — treat a version change as its own decision). `Nullable` is `disable` throughout, matching every prior phase. `RuntimeIdentifier` is `win-x64`, `SelfContained=false` for the src projects.

## Test

**Tests must be run via each test project's built `.exe`, not `dotnet test`.** `Microsoft.UI.Xaml` objects are WinRT classes with thread affinity that can only activate on a thread running inside `Microsoft.UI.Xaml.Application.Start`, inside a process carrying WinUI's registration-free activation manifest. That manifest is embedded into each test project's own apphost at build time (`OutputType=Exe` + NUnitLite, see each project's `Program.cs`) — `dotnet test`'s `testhost.exe` ships with its own pre-built manifest and cannot activate `Microsoft.UI.Xaml` classes at all. Under `dotnet test`, every test needing a live WinUI object reports **ignored** (not failed, not passed) — large "Skipped" counts from `dotnet test` are not a green run.

```
dotnet build WinUIDesigner.sln

tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```

Run a single test with NUnitLite's own filter flag, e.g. `... .exe --test=WinUIDesigner.Designer.Tests.MoveTests.SomeMethod`. `dotnet test WinUIDesigner.sln --filter <Name>` still works for IDE discovery and for the subset of tests that touch no live WinUI object, but treat it as a supplement, never a replacement for the `.exe` run.

`WinUITestHost.RunOnWindow(...)` is `RunWithWindow` without the trailing `Content = null`, for a fixture that builds one tree in its setup and drives that same live tree across several tests; its caller owns the cleanup.

`tests/WinUIDesigner.TestHost` is shared test infrastructure (not a test project itself): `WinUITestHost.Run(...)`/`RunWithWindow(...)` marshal a test body onto the process-wide WinUI dispatcher thread (started lazily, one shared `Window` reused and never closed — closing the last window ends the dispatcher's message loop and kills every later test). `ReflectionXamlMetadataProvider` backs the `TestApplication`'s `IXamlMetadataProvider` so `XamlReader.Load` can resolve test fixture types.

## Architecture

Three layered library projects, each with a `.Tests` counterpart of the same name, plus the shell application that hosts them:

```
WinUIDesigner.XamlDom         — WPF→WinUI XAML DOM: parse/serialize XAML into an object model, no UI.
        ↑
WinUIDesigner.DesignModel     — DesignItem/DesignContext/PlacementOperation/extension framework,
                                 plus a WinUI Adorner replacement (WinUI has no Adorner/AdornerLayer).
        ↑
WinUIDesigner.Designer        — DesignPanel/DesignSurface: live visual-tree hosting, hit-testing,
                                 selection, and the ~56 concrete design-time behaviors (resize/rotate/
                                 skew thumbs, snaplines, per-container placement, drag-move/reparent,
                                 Grid row/column editing).
        ↑
WinUIDesigner.Shell           — the shell app's logic and UI, all in C#: Document, ShellModel,
                                 Toolbox, ShellTypeFinder, ShellMetadata, ToolboxView, MainWindow.
                                 A class library, and deliberately free of XAML.
        ↑
WinUIDesigner.Shell.App       — the executable. Holds App.xaml and App.xaml.cs and nothing else.
```

`Shell` and `Shell.App` are two projects rather than one for a measured reason: any `ProjectReference`
from a WinUI-hosting test `.exe` to a project carrying an `ApplicationDefinition` fail-fasts that test
host with `0xC0000409` before a single test body runs. Keeping the classes in a XAML-free library lets
`WinUIDesigner.Shell.Tests` reference them the same way the other three test projects reference
`WinUIDesigner.Designer`.

Each project only depends on the ones above it in this list (`ProjectReference`s in the `.csproj`s make this explicit). `tests/WinUIDesigner.TestHost` is referenced by all three `.Tests` projects.

**Extension system** (`WinUIDesigner.DesignModel/Extensions/`): every `DesignItem` gets a set of `Extension`s attached by an `ExtensionServer`, matched by type via `[ExtensionFor]`/`[ExtensionServer]` attributes and resolved through `ExtensionManager`. This is how per-control-type design-time behavior (a resize thumb, a Canvas-specific placement rule, a Grid adorner) gets attached without the core engine knowing about concrete controls.

**Placement chain** (`WinUIDesigner.Designer/Extensions/`): container-specific placement composes as a short `IPlacementBehavior` chain — `DefaultPlacementBehavior` (generic Width/Height resize) → `RasterPlacementBehavior` (snap-to-grid) → `SnaplinePlacementBehavior` (snap-to-sibling-edge) → `CanvasPlacementSupport` / `GridPlacementSupport` (container-specific coordinate systems: `Canvas.Left/Top`, `Grid.Row/Column`). `StackPanelPlacementSupport` bases directly on `DefaultPlacementBehavior` since reordering isn't a coordinate-snapping concern. `PlacementOperation.ChangeContainer` plus `DefaultPlacementBehavior.LeaveContainer`/`EnterContainer` are the reparenting mechanism a `MoveLogic` class drives for cross-container drag-move, triggered by `AdornerProvider` extensions built on the `DragListener` primitive (`PointerPressed`/`PointerMoved`/`PointerCaptureLost`).

**Undo**: every mutation goes through `PlacementOperation`/`ChangeGroup`/`UndoService` so a whole gesture (resize, move, reparent) commits as one undo entry.

## Known WinUI platform gaps (compiler-confirmed absent — do not assume otherwise)

`Rect.Inflate`, the static `Brushes` class, `UIElement.TranslatePoint`, `RowDefinition.Offset`/`ColumnDefinition.Offset`, `CombinedGeometry`/`GeometryCombineMode`, `Animatable`/`Geometry.BeginAnimation`/`RectAnimation`/the 3-arg `DoubleAnimation` constructor, `Visibility.Hidden` (only `Visible`/`Collapsed`), `DispatcherPriority.Loaded` (`DispatcherQueuePriority` has only `Low`/`Normal`/`High`), `InvalidateVisual`, `SolidColorBrush.Freeze()`, `DoubleCollection(IEnumerable<double>)`, single-arg `TextBox.GetRectFromCharacterIndex`, `Canvas.RightProperty`/`Canvas.BottomProperty`, `Microsoft.UI.Xaml.Media.PathSegment.IsStroked`/`.IsSmoothJoin` (WPF-only — `PathSegment` in this WinUI SDK declares no properties of its own at all beyond what it inherits from `DependencyObject`, confirmed by both a `CS1061` compile error and reflecting over the live WinRT type; there is no per-segment stroke/join equivalent anywhere in WinUI's `Path` model), `Microsoft.UI.Xaml.Media.LayoutInformation` (the WPF `LayoutInformation.GetLayoutSlot` static class has no projected equivalent in this WinUI 3 SDK — confirmed by `CS0234`; use `FrameworkElement.ActualOffset`/`.ActualSize` against a live layout pass instead, which reflects post-arrange, pixel-snapped values). `DoubleCollection` also cannot be shared across `Shape` instances at runtime (not a compile error, an empirically-found runtime constraint). `Microsoft.UI.Xaml.Media.PointCollection`'s projected indexer does not bounds-check: `points[i]` for `i >= points.Count` (or negative) returns `Point(0,0)` rather than throwing `ArgumentOutOfRangeException` the way WPF's `PointCollection` would (confirmed empirically on a live object: `count=3` probe at index `99` returned `Point(0,0)`, no exception) — code relying on WPF's throw-on-out-of-range behavior as an implicit assertion will instead silently read a phantom `(0,0)` point. A `FrameworkElement`'s arranged position/size can round to the nearest pixel at the default `RasterizationScale` (e.g. an arrange rect of `(16.5, 26.5, 7, 7)` reads back via `ActualOffset`/`ActualSize` as `(17, 27, 7, 7)`) — symmetric across both axes, not a bug; give coordinate assertions against real layout output a tolerance (0.5px at 1x scale) rather than asserting exact fractional values. Three more found while building the shell (Phase 5), each after a wrong assumption cost real time. **A `ListView` bound through `DisplayMemberPath` against `System.Type` renders every row blank**: `typeof(X)` is an instance of the non-public `System.RuntimeType`, and WinUI's binding engine reflects against an object's actual type, so `"Name"` never resolves — the containers realize with correct sizes and no text, which looks like a layout bug and is not. Bind to a public class of your own (`ControlNode` here) instead. **Never nest a `ListView` inside a `ScrollViewer`**: the list contains its own, and the outer one hands it an infinite available height — measured here as a 200x6360 `ListView` inside a 200x680 parent, with all 159 containers realized against an unbounded viewport. **`System.Windows.Automation` crashes a WinUI 3 app**: walking the tree with `FindAll(TreeScope.Descendants, ...)` killed the process with `0xc0000005` inside `Microsoft.UI.Xaml.dll`, so it is not usable for driving or inspecting this app; `SendKeys` does not reach a XAML island either, which is why the shell's interactive walkthrough still needs a human. Three more were measured while making the shell's `MainWindow` testable in-process, and they bound what that is able to prove. **A test process cannot load `XamlControlsResources`**: with no compiled XAML there is no merged `resources.pri`, `ms-appx:///` does not resolve, and constructing it throws `COMException` 0x80004005. Controls whose default style lives only in that dictionary are then unusable - `MenuBar` measures to zero, `TextBox` and `NumberBox` fail their first layout pass with a bare `E_FAIL`. `MenuBar` at least names the keys it wants (`TabViewButtonBackground`, `TabViewScrollButtonBackground`) in the exception's `Data["RestrictedDescription"]`, never in its `Message`, so seeding them by hand works; `TextBox` names nothing and cannot be seeded around. **A second live `Window` in a test process is fine in general but not with a `TextBox` in it**: `ListView`, `ToolboxView`, `OutlineViewControl`, `DesignSurface` and `ScrollViewer` were each measured going live in one, while a lone `TextBox` fail-fasts the host with `0xC000027B` (STOWED_EXCEPTION) during the first layout pass. Since the host's own shared window handles a `TextBox` normally, a window-owning test and a `TextBox`-using test are mutually exclusive in one process - which is why `MainWindowInteractionTests` hosts the shell tree in the shared window and marks its focus-guard test `[Explicit]`. **Ruling 6 holds for a project's own `ApplicationDefinition`, not just a referenced one**: adding an `App.xaml` to a test project (with `DISABLE_XAML_GENERATED_MAIN`) does generate the merged PRI, and still fail-fasts the host before a single test body runs.

Before assuming a WPF API exists in WinUI, check the spec's "WinUI API surface" section for the phase you're touching, or verify empirically (`dotnet build` against a throwaway probe, or direct `Microsoft.UI.Xaml.winmd` metadata inspection) rather than guessing — this codebase has been burned by wrong assumptions here more than once.

## Working with SharpDevelop-derived files

Files ported from `WpfDesign` (e.g. `ExtensionManager.cs`) carry the original SharpDevelop MIT copyright header — keep it when editing those files. Follow each file's existing brace/indentation style rather than imposing a different one across the codebase; ported files and newly-written files differ slightly in convention.
