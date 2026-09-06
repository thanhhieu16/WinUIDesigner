# Phase 5 — Shell App: Design

**Status:** approved, not yet implemented
**Date:** 2026-09-07
**Scope:** a new `WinUIDesigner.Shell` executable plus `WinUIDesigner.Shell.Tests`. No change to `XamlDom`, `DesignModel` or `Designer` beyond what a defect found during implementation forces.

## Context

Phases 1–4g built the engine: a XAML DOM, a design model with undo, a design surface with live visual-tree hosting, and roughly 56 concrete design-time behaviors. Phases A and B of the value-conversion fix closed the read and write sides of property serialization. Nothing in the repository launches. Every one of the 466 tests drives the engine through its API; no code has ever hosted it in an application a person can click.

Phase 5 is that application: the WinUI 3 port of the reference solution's `XamlDesigner` project. It is the first thing that proves the engine works end to end, and it is where integration defects the test suites cannot see will surface.

The reference shell is `F:\ReferencesCode\CSharp\WpfDesigner\XamlDesigner` (read-only). Its C# logic ports directly. Two of its dependencies do not: `Dirkster.AvalonDock 4.60.1`, which provides the whole docking and document-tab frame of `MainWindow.xaml`, and `AvalonEdit 6.1.3.50`, which provides the XAML text editor in `DocumentView.xaml`. Both are WPF-only packages with no WinUI equivalent. `MainWindow.xaml` also references `WpfDesign.Designer.ExpressionBlendInteractionAddon` (Behaviors and Triggers editors), a project this port does not have.

## Verified against the current codebase

Everything below was measured on this machine, against this tree, before it was written down. No claim here is inferred.

### The engine surface the shell consumes

| Member | Verified at |
|---|---|
| `DesignSurface.LoadDesigner(XmlReader, XamlLoadSettings)` | `src/WinUIDesigner.Designer/DesignSurface.cs:29` |
| `DesignSurface.SaveDesigner(XmlWriter)` | `src/WinUIDesigner.Designer/DesignSurface.cs:76` |
| `DesignSurface.UnloadDesigner()` | `src/WinUIDesigner.Designer/DesignSurface.cs:64` |
| `DesignSurface.DesignContext` | `src/WinUIDesigner.Designer/DesignSurface.cs:26` |
| `DesignItem.CreateOutlineNode()` → `IOutlineNode` | `src/WinUIDesigner.DesignModel/DesignItem.cs:361` |
| `OutlineViewControl()` and `.Root` (`IOutlineNode`) | `src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs:47,54` |
| `PropertyGridControl()` and `.SelectedItems` (`ICollection<DesignItem>`) | `src/WinUIDesigner.Designer/PropertyGrid/PropertyGridControl.cs:60,68` |
| `CreateComponentTool(Type)` | `src/WinUIDesigner.Designer/Services/CreateComponentTool.cs:40` |
| `IToolService.CurrentTool` | `src/WinUIDesigner.DesignModel/Tools.cs:39` |
| `ISelectionService.SelectionChanged` (`EventHandler<DesignItemCollectionEventArgs>`), `.SelectedItems`, `.PrimarySelection` | `src/WinUIDesigner.DesignModel/Services.cs:65,70,73` |
| `ServiceContainer.Selection`, `GetService<T>()` | `src/WinUIDesigner.DesignModel/ServiceContainer.cs:176,112` |
| `UndoService.Undo/Redo/CanUndo/CanRedo/UndoStackChanged` | `src/WinUIDesigner.DesignModel/UndoService.cs:196–305` |
| `XamlErrorService.Errors` (`ObservableCollection<XamlError>`); `XamlError.Message/Line/Column` | `src/WinUIDesigner.DesignModel/Xaml/XamlErrorService.cs:31,41–43` |
| `XamlFormatter.Format(string)` and `Format(string, out Dictionary<XamlElementLineInfo, XamlElementLineInfo>)` | `src/WinUIDesigner.XamlDom/XamlFormatter.cs:21,27` |
| `XamlXmlWriter(StringBuilder)` | `src/WinUIDesigner.XamlDom/XamlXmlWriter.cs:37` |
| `XamlTypeFinder.CreateWinUITypeFinder()`, virtual `LoadAssembly(string)`, virtual `Clone()`, `RegisterAssembly(Assembly)` | `src/WinUIDesigner.XamlDom/XamlTypeFinder.cs:409,364,372,323` |
| `XamlLoadSettings.DesignerAssemblies`, `.TypeFinder` | `src/WinUIDesigner.DesignModel/Xaml/XamlLoadSettings.cs:31,36` |

The shell adds no engine API. It is wiring.

### The launch problem, and its root cause

A code-only WinUI 3 application — no `.xaml` file anywhere, an `Application` subclass constructed inside `Application.Start` — starts, sets `Application.Current`, resolves framework theme keys (`SystemControlBackgroundBaseLowBrush` returned `True`), and constructs every control tried. It then **fail-fasts with `0xC0000409` (`STATUS_STACK_BUFFER_OVERRUN`) during the first layout pass** over a window containing a templated control. The crash is not catchable and takes the process down with no managed exception.

The cause is resource resolution, measured directly. In a project with no compiled XAML, `new XamlControlsResources()` throws:

```
COMException hr=0x80004005
Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'.
```

No `ms-appx:///` resolution means no theme resource dictionary, and every control whose default template references a theme brush dies in layout. The build produces no merged PRI for a project that contains no XAML, which is why the lookup fails.

Adding a single `App.xaml` fixes the whole chain. Measured on the fixed configuration, in one window, after ten rendered frames:

| Control | Template | Arranged size |
|---|---|---|
| `Button` | set | 33 × 30 |
| `TextBox` | set | 120 × 32 |
| `ComboBox` | set | 70 × 32 |
| `TreeView` | set | 1424 × 60 |
| `MenuBar` | set | 1424 × 40 |
| `CommandBar` | set | 1424 × 48 |
| `ListView` | set | 1424 × 60 |
| `SplitButton` | set | 71 × 28 |
| `TabView` | set | 1424 × 80 |

Process exit code 0. Reproduced four times across configuration variants.

Which knobs matter, each isolated by a single-variable run:

| Factor | Result |
|---|---|
| `App.xaml` present | **Required.** Without it there is no PRI, `ms-appx:///` fails, templated controls fail-fast at layout. |
| `XamlControlsResources` merged | **Required.** Declaring it in `App.xaml` and adding it from code both work. |
| `InitializeComponent()` in the `Application` constructor | Works. |
| Entry point | Use the XamlCompiler-generated `Main`. A hand-written `Main` missing `WinRT.ComWrappersSupport.InitializeComWrappers()` fail-fasts. |
| `EnableMsixTooling` | Either value works. `true` additionally produces a merged `resources.pri`; `false` produces `<ProjectName>.pri`. Neither is decisive once `App.xaml` exists. |
| A hand-written `app.manifest` | **Must not be added.** It overrides the generated registration-free WinRT activation manifest and the app fail-fasts. |

The reference `XamlDesigner` and the standard WinUI template both carry an `App.xaml`; this port's libraries carry none because a class library needs none.

### The toolbox source

Measured by reflecting over the live WinUI projection inside the test host:

| Fact | Value |
|---|---|
| Assembly holding `Microsoft.UI.Xaml.Controls.Button` | `Microsoft.WinUI` — **not** `Microsoft.UI.Xaml` |
| `GetExportedTypes()` | 2388 types |
| Non-abstract, non-generic `UIElement` subclasses with a public parameterless constructor | 160 |
| — in `Microsoft.UI.Xaml.Controls` | 132 |
| — in `Microsoft.UI.Xaml.Controls.Primitives` | 21 |
| — in `Microsoft.UI.Xaml.Shapes` | 6 |
| — in `Microsoft.UI.Xaml.Documents` | 1 |
| `Activator.CreateInstance` succeeds | 159 of 160 |
| Fails | `PivotHeaderItem` — `TargetInvocationException` |

The reference's `IsControl` predicate (`Toolbox.cs`) ports unchanged. The assembly-name fact matters: `MyTypeFinder.LoadAssembly(name)` compares `Assembly.GetName().Name` against the name in a XAML namespace declaration, and for WinUI that name is `Microsoft.WinUI`.

### File dialogs

`Windows.Storage.Pickers.FileOpenPicker` constructs, and `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)` succeeds against a real HWND obtained from `WinRT.Interop.WindowNative.GetWindowHandle(window)`. Measured in the test host on a live window. Unpackaged WinUI requires that HWND association; without it the picker throws at show time.

### The build already handles the Appx tooling trap

`Directory.Build.props` resolves `AppxMSBuildToolsPath` through `vswhere`, because WindowsAppSDK wires `GetPriOutputs` into the normal output chain whenever `UseWinUI=true` and its tasks live only inside a Visual Studio install. A new project in this tree inherits the fix. Verified by observing MSB4062 in a probe built outside the tree and its absence for the same project built inside.

## Decisions

**1. One `.xaml` file in `src/`, and it is `App.xaml`.**
The project convention has been zero `.xaml` files in `src/`. It is relaxed by exactly one file, in the shell only, because the launch measurements above show the application cannot run without it. Every other piece of shell UI is written in C#, matching `OutlineViewControl` and `PropertyGridControl`. `CLAUDE.md` is updated to state the exception and the reason.
*Cost if wrong:* none available — the alternative does not run.

**2. The entry point is the generated one.**
`App.xaml` gives the XamlCompiler an application definition, and it generates `Main` with `InitializeComWrappers()` and a `DispatcherQueueSynchronizationContext`. The shell does not define `Main` and does not set `DISABLE_XAML_GENERATED_MAIN`.
*Cost if wrong:* a hand-written `Main` that omits either step fail-fasts before any managed line runs, which is unusually hard to diagnose — it was the longest-lived false trail in this phase's investigation.

**3. No hand-written `app.manifest`.**
*Cost if wrong:* the generated registration-free activation manifest is replaced, WinRT activation fails, and the app dies at startup.

**4. `XamlControlsResources` is merged declaratively in `App.xaml`.**
Both forms were measured to work; the declarative form matches the platform template and keeps the code path free of a startup ordering constraint.
*Cost if wrong:* none measured; the code form remains available.

**5. Logic is split from UI so the logic is testable.**
`ShellModel` and `Document` touch no `Window` and no `MainWindow`. They are constructible on the test dispatcher thread and are what the test suite drives.
*Cost if wrong:* the shell becomes untestable and Phase 5 ships with no regression net, which is a break from every prior phase.

**6. `ShellModel` raises events instead of showing dialogs.**
The reference calls `MessageBox.Show` directly inside `Shell.Close` and `Shell.PrepareExit`. WinUI's replacement, `ContentDialog`, is asynchronous and needs an `XamlRoot`. `ShellModel` exposes what it needs answered; `MainWindow` answers it.
*Cost if wrong:* dialog calls buried in logic drag a live `XamlRoot` into every unit test, which is what Decision 5 exists to avoid.

**7. Single document, no tabs.**
One `Document` is open at a time. `ShellModel` still owns a collection so multi-document is an additive change later, but no `TabView` ships in v1.
*Cost if wrong:* adding tabs later means adding a `TabView` and a per-tab surface — the document lifecycle is already modelled.

**8. No XAML text view.**
`Document.UpdateXaml()` exists and produces formatted text, but nothing displays it in v1. The reference's text mode is an AvalonEdit surface with two-way sync and its own selection-mapping logic (`Document.UpdateXaml` maps line info back to the primary selection); porting an editor is its own phase.
*Cost if wrong:* the fastest way to eyeball what Phases A and B fixed is missing from v1. Mitigated by the round-trip tests, which assert the same thing mechanically.

**9. No docking frame.**
A `Grid` with fixed column definitions and `GridSplitter`-free borders replaces AvalonDock. Panes are not floatable, closable, or rearrangeable.
*Cost if wrong:* users cannot rearrange panes. Acceptable for a proof-of-engine.

**10. The toolbox scans assemblies, as the reference does.**
`Toolbox` loads `typeof(Button).Assembly`, applies the reference's `IsControl` predicate, and **additionally skips any type whose activation throws**, because one of the 160 does. The skip is silent and the type is simply absent from the list.
*Cost if wrong:* a control that fails to activate for an environment-specific reason disappears from the toolbox with no explanation. Accepted; the alternative is a crash while building the list.

**11. `ShellTypeFinder` derives from `XamlTypeFinder`, seeded from `CreateWinUITypeFinder()`.**
It overrides `LoadAssembly(string)` to resolve against registered assemblies and the toolbox's assemblies, and `Clone()` to return the singleton — the reference's `MyTypeFinder` shape, which the port's `XamlTypeFinder` already supports.
*Cost if wrong:* documents referencing types outside WinUI fail to load with a type-resolution error.

**12. Parse errors are reported after load, in a `ContentDialog`.**
`XamlErrorService.Errors` is read once after `LoadDesigner` returns. A non-empty collection produces a dialog listing message, line and column. There is no error list pane and no jump-to-error.
*Cost if wrong:* a document with many errors produces a long dialog. Bounded by showing the first ten and a count.

**13. File and save failures are caught at `ShellModel` and reported, never thrown.**
Phase B established that a throw escaping into the engine's transaction machinery strands a `ChangeGroup` and, with no `Application.UnhandledException` handler anywhere in `src/`, ends the process. The same rule applies to the shell's own I/O.
*Cost if wrong:* a read-only file or a locked path kills the application instead of showing a message.

**14. The soak measurement ships as a test, not as instrumentation.**
A test opens and closes the same document N times through `Document` and asserts that memory growth stays under a ceiling.
*Cost if wrong:* a per-document native leak reaches a shipping shell unnoticed. The Phase A measurement of roughly 4.7 KB of native memory retained per `XamlReader.Load` is the reason this is in scope at all.

## Architecture

```
                          ┌──────────────────────────────┐
                          │  App.xaml  (the only .xaml)  │
                          │  merges XamlControlsResources │
                          └──────────────┬───────────────┘
                                         │ generated Main
                          ┌──────────────▼───────────────┐
                          │          MainWindow          │   C#, no XAML
                          │  MenuBar / 3-column Grid     │
                          └───┬──────────┬───────────┬───┘
                              │          │           │
                  ┌───────────▼──┐  ┌────▼──────┐  ┌─▼──────────────────┐
                  │ ToolboxView  │  │DesignSurf.│  │ OutlineViewControl │
                  │  (ListView)  │  │           │  │ PropertyGridControl│
                  └───────┬──────┘  └────┬──────┘  └─▲──────────────────┘
                          │              │           │
                          │ CurrentTool  │           │ SelectionChanged
                          │              │           │
                  ┌───────▼──────────────▼───────────┴──┐
                  │            ShellModel               │   no Window,
                  │   documents, current, New/Open/…    │   no XamlRoot
                  └──────────────────┬──────────────────┘
                                     │
                          ┌──────────▼───────────┐
                          │       Document       │
                          │  Text ⇄ DesignSurface│
                          └──────────┬───────────┘
                                     │
   open:  file → Text → XmlReader → LoadDesigner → DesignContext → RootItem.View
                                                 → RootItem.CreateOutlineNode()
   save:  SaveDesigner(XamlXmlWriter) → XamlFormatter.Format → File.WriteAllText
```

### Components

| Unit | Responsibility | Ported from |
|---|---|---|
| `App` + `App.xaml` | Application object; merges `XamlControlsResources`; creates `MainWindow`. | `App.xaml`, `App.xaml.cs` |
| `MainWindow` | Layout, menu, command enable/disable, dialogs, file pickers, window title. | `MainWindow.xaml`, `MainWindow_Commands.cs` |
| `ShellModel` | Document collection, current document, `New`/`Open`/`Save`/`SaveAs`/`Close`, dirty aggregation, events asking the UI a question. | `Shell.cs` |
| `Document` | Owns one `DesignSurface`; `Text` ⇄ design; dirty flag driven by `UndoService.UndoStackChanged`; exposes `DesignContext`, `UndoService`, `SelectionService`, `XamlErrorService`, `OutlineRoot`. | `Document.cs` |
| `Toolbox` | Assembly list, control list per assembly, `IsControl` filter plus the activation filter. | `Toolbox.cs` |
| `ShellTypeFinder` | `XamlTypeFinder` override resolving assemblies the toolbox knows. | `MyTypeFinder.cs` |
| `ToolboxView` | `ListView` of control nodes; selection sets `IToolService.CurrentTool`. | `ToolboxView.xaml` |

`Document` deviates from the reference in three named ways: no `DocumentMode`, so no `InXamlMode`/`InDesignMode` and no text-mode line-info mapping; `Dispose` calls `DesignSurface.UnloadDesigner()`, which the reference never does and which the soak test depends on; and `ReloadFile` is not called from a constructor that also assigns `FilePath` through a property with side effects.

### Data flow, opening a document

1. `ShellModel.Open(path)` reads the file into `Document.Text`.
2. `Document.UpdateDesign()` builds `XamlLoadSettings`, adds every toolbox assembly to `DesignerAssemblies`, assigns `ShellTypeFinder.Instance` to `TypeFinder`, and calls `DesignSurface.LoadDesigner(XmlReader.Create(new StringReader(Text)), settings)`.
3. `DesignSurface` registers its services and constructs `XamlDesignContext`, which parses the document and builds the live visual tree; `DesignPanel.Content` becomes `RootItem.View`.
4. `Document.OutlineRoot` becomes `DesignContext.RootItem.CreateOutlineNode()`; `MainWindow` assigns it to `OutlineViewControl.Root`.
5. `MainWindow` subscribes to `ISelectionService.SelectionChanged` and pushes `SelectedItems` into `PropertyGridControl.SelectedItems`.
6. `Document` subscribes to `UndoService.UndoStackChanged` to set `IsDirty`.
7. `MainWindow` reads `XamlErrorService.Errors`; non-empty produces the error dialog.

### Data flow, saving

`Document.UpdateXaml()` writes through `XamlXmlWriter(StringBuilder)` into `DesignSurface.SaveDesigner`, passes the result through `XamlFormatter.Format(string)`, and assigns `Text`. `ShellModel.Save` then writes `Text` to disk and clears the dirty flag. This is the exact path Phases A and B repaired, exercised for the first time by a person rather than a test.

## Testing and definition of done

Tests live in `tests/WinUIDesigner.Shell.Tests`, built as an `Exe` with NUnitLite and run as
`tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult`,
matching the three existing suites. `dotnet test` is not a substitute.

1. `Toolbox` lists only non-abstract, non-generic `UIElement` subclasses with a public parameterless constructor, and every listed type activates.
2. `Toolbox` excludes `PivotHeaderItem` — the one measured activation failure — and does not throw while building the list.
3. `ShellTypeFinder.LoadAssembly("Microsoft.WinUI")` returns the WinUI assembly; an unknown name returns null rather than throwing.
4. `Document` opened from a known XAML string exposes a non-null `DesignContext`, `RootItem`, and `OutlineRoot`.
5. `Document.Text` after `UpdateXaml()` re-parses into an equivalent tree — a round trip that is stable on a second pass, so the formatter and writer agree with the parser.
6. A document containing a `Polyline` with real points round-trips with its points intact, closing the loop on Phases A and B in the shell's own save path.
7. `Document.IsDirty` is false after load, true after a change committed through `UndoService`, and false again after `Save`.
8. `ShellModel.Open` on a missing or unreadable path raises the error event and leaves the document collection unchanged — no exception escapes.
9. `ShellModel.Close` on a dirty document asks its question through the event rather than performing I/O.
10. A document whose XAML is malformed loads with a non-empty `XamlErrorService.Errors` and does not throw.
11. **Soak:** opening and disposing the same `Document` 50 times leaves process working-set growth under a stated ceiling, measured after a forced collection. The ceiling is chosen from a first measured run and written into the test with the measured number in a comment.
12. All three existing suites stay green: XamlDom 144 (140 passed, 4 pre-existing ignores), DesignModel 77/77, Designer 245/245.
13. The shell launches, opens a file, lets a control be dragged from the toolbox onto the surface, edits a property in the grid, undoes it, and saves — verified by hand once, and stated as such rather than claimed as automated.

## Known gaps

- No document tabs; one document at a time.
- No XAML text view and no text editing.
- No dockable, floatable, or resizable panes.
- No recent-files list and no settings persistence — the reference's `Settings.Default` is a WPF `ApplicationSettingsBase` facility with no direct port.
- No error list pane and no jump-to-error; errors appear once, in a dialog.
- No Behaviors or Triggers editors; `WpfDesign.Designer.ExpressionBlendInteractionAddon` is not ported.
- The toolbox lists all 159 activatable controls flat, grouped only by assembly. The reference has the same shape; its `Metadata.IsPopularControl` filter is commented out there.
- A control that fails to activate is dropped from the toolbox silently.
- The soak test measures working set, which is noisy; it asserts a ceiling, not a leak-free result.

## Out of scope

- Any change to `XamlDom`, `DesignModel`, or `Designer`, unless implementing the shell exposes a defect in them.
- The remaining Phase A and B gaps: `RectangleGeometry`/`EllipseGeometry`/`LineGeometry`/`GeometryGroup` and `LinearGradientBrush` still save as empty elements, `Vector2` has no formatter, and `IXamlErrorSink.ReportError` is still called with line and column 0.
- Multi-document support, a text editor, docking, and settings persistence — each is additive and none is required to prove the engine.
