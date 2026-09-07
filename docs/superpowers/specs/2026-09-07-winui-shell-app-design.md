# Phase 5 — Shell App: Design

**Status:** approved, not yet implemented
**Date:** 2026-09-07
**Scope:** a new `WinUIDesigner.Shell` executable plus `WinUIDesigner.Shell.Tests`. No change to `XamlDom`, `DesignModel` or `Designer` beyond what a defect found during implementation forces.

## Context

Phases 1–4g built the engine: a XAML DOM, a design model with undo, a design surface with live visual-tree hosting, and roughly 56 concrete design-time behaviors. Phases A and B of the value-conversion fix closed the read and write sides of property serialization. Nothing in the repository launches. Every one of the 466 tests drives the engine through its API; no code has ever hosted it in an application a person can click.

Phase 5 is that application: the WinUI 3 port of the reference solution's `XamlDesigner` project. It is the first thing that proves the engine works end to end, and it is where integration defects the test suites cannot see will surface.

The reference shell is `F:\ReferencesCode\CSharp\WpfDesigner\XamlDesigner` (read-only). Its C# logic ports directly. Two of its dependencies do not: `Dirkster.AvalonDock 4.60.1`, which provides the whole docking and document-tab frame of `MainWindow.xaml`, and `AvalonEdit 6.1.3.50`, which provides the XAML text editor in `DocumentView.xaml`. Both are WPF-only packages with no WinUI equivalent. `MainWindow.xaml` also references `WpfDesign.Designer.ExpressionBlendInteractionAddon` (Behaviors and Triggers editors), a project this port does not have.

Three earlier phases handed obligations to this one in writing, and each is discharged by a decision below: the `PropertyGridControl` cross-document cache (Phase 4f, Known gaps), `BasicMetadata`-style seeding (Phase 4e Decision 6, restated by Phase 4f Decision 14), global keyboard shortcuts (Phase 4c), toolbox-side drag initiation (Phase 4d), and supplying the outline's filter input (Phase 4g Decision 12).

## Verified against the current codebase

Everything below was measured on this machine, against this tree, before it was written down.

### The engine surface the shell consumes

Every row was opened and read; the line numbers are the ones in the tree at the time of writing.

| Member | Verified at |
|---|---|
| `DesignSurface.LoadDesigner(XmlReader, XamlLoadSettings)` | `src/WinUIDesigner.Designer/DesignSurface.cs:29` |
| `DesignSurface.SaveDesigner(XmlWriter)` | `src/WinUIDesigner.Designer/DesignSurface.cs:76` |
| `DesignSurface.UnloadDesigner()` | `src/WinUIDesigner.Designer/DesignSurface.cs:62` |
| `DesignSurface.DesignContext` | `src/WinUIDesigner.Designer/DesignSurface.cs:27` |
| `DesignItem.CreateOutlineNode()` → `IOutlineNode` | `src/WinUIDesigner.DesignModel/DesignItem.cs:361` |
| `OutlineViewControl()` ctor; `.Root` (`IOutlineNode`); `.Filter` (`string`) | `src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs:47,54,86` |
| `PropertyGridControl.SelectedItems` (`ICollection<DesignItem>`) at 60; ctor at 68 | `src/WinUIDesigner.Designer/PropertyGrid/PropertyGridControl.cs:60,68` |
| `CreateComponentTool(Type)` at 40; `CreateComponentTool(Type, object[])` at 42 | `src/WinUIDesigner.Designer/Services/CreateComponentTool.cs:40,42` |
| `IToolService.PointerTool`, `.CurrentTool`, `.CurrentToolChanged` | `src/WinUIDesigner.DesignModel/Tools.cs:38,39,40` |
| `ISelectionService.SelectionChanged` (`EventHandler<DesignItemCollectionEventArgs>`), `.SelectedItems`, `.PrimarySelection`, `.SetSelectedComponents` | `src/WinUIDesigner.DesignModel/Services.cs:65,70,73,71` |
| `ServiceContainer.Selection`, `GetService<T>()` | `src/WinUIDesigner.DesignModel/ServiceContainer.cs:176,112` |
| `UndoService` class 196; `CanUndo` 236; `UndoStackChanged` 243; `Undo` 255; `CanRedo` 300; `Redo` 305 | `src/WinUIDesigner.DesignModel/Services/UndoService.cs` |
| `ICopyPasteService.Copy/Cut/Paste/Delete` and `CanCopy/CanCut/CanPaste/CanDelete`, each taking `DesignContext` | `src/WinUIDesigner.DesignModel/Services/ICopyPasteService.cs:25–32` |
| `ModelTools.CanDeleteComponents`, `.DeleteComponents`, `.GetDefaultSize` | `src/WinUIDesigner.Designer/ModelTools.cs:184,195,57` |
| `XamlErrorService.Errors` (`ObservableCollection<XamlError>`); `XamlError.Message/Line/Column` | `src/WinUIDesigner.DesignModel/Xaml/XamlErrorService.cs:31,41–43` |
| `XamlFormatter.Format(string)` and `Format(string, out Dictionary<XamlElementLineInfo, XamlElementLineInfo>)` | `src/WinUIDesigner.XamlDom/XamlFormatter.cs:21,27` |
| `XamlXmlWriter(StringBuilder)` | `src/WinUIDesigner.XamlDom/XamlXmlWriter.cs:37` |
| `XamlTypeFinder.CreateWinUITypeFinder()` 409; virtual `LoadAssembly(string)` 364; virtual `Clone()` 372; `RegisterAssembly` 323; **`protected ImportFrom`** 384 | `src/WinUIDesigner.XamlDom/XamlTypeFinder.cs` |
| `XamlLoadSettings.DesignerAssemblies` 31; `ReportErrors` (`Action<XamlErrorService>`) 33; `TypeFinder` 36 | `src/WinUIDesigner.DesignModel/Xaml/XamlLoadSettings.cs` |
| `DesignPanel.IsTabStop = true` — the design surface can take keyboard focus | `src/WinUIDesigner.Designer/DesignPanel.cs:26` |

`ImportFrom` is `protected`, which is what makes Decision 14 implementable: `CreateWinUITypeFinder()` returns a `XamlTypeFinder`, not a `ShellTypeFinder`, so the shell's finder seeds itself by calling `ImportFrom(CreateWinUITypeFinder())` from inside its own type — exactly as the reference's `MyTypeFinder` does with `CreateWpfTypeFinder()`.

The shell adds one piece of engine-adjacent work beyond wiring: metadata seeding (Decision 22). Everything else is wiring.

### How a load failure actually behaves

This determines the whole error design, and the obvious reading of the API is wrong.

`XamlParser.Parse` catches `XmlException`, reports it to the `IXamlErrorSink`, and **returns null** (`src/WinUIDesigner.XamlDom/XamlParser.cs:99–110`). `XamlDesignContext`'s constructor then does, in this order (`src/WinUIDesigner.DesignModel/Xaml/XamlDesignContext.cs:73–84`):

```csharp
_doc = XamlParser.Parse(xamlReader, _parserSettings);
loadSettings.ReportErrors(xamlErrorService);
if (_doc == null) { ... throw new XamlLoadException(message); }
```

So for XAML that is not well-formed XML, `LoadDesigner` **throws** and never assigns `DesignSurface.DesignContext`. Since `LoadDesigner` calls `UnloadDesigner()` on entry (`DesignSurface.cs:31`), the previous context is already gone and the new one was never created: reading `DesignContext.Services.GetService<XamlErrorService>()` afterwards dereferences null. The errors are reachable only through the `XamlLoadSettings.ReportErrors` callback, which fires on the line before the throw.

Non-fatal errors behave differently. A well-formed document naming an unknown type or property is reported through `XamlParser.ReportException` and the parse still returns a document, so the context exists and its `XamlErrorService.Errors` is readable after `LoadDesigner` returns.

Decision 16 and DoD items 10 and 11 are built on this split.

### The launch problem, and its root cause

A code-only WinUI 3 application — no `.xaml` file anywhere, an `Application` subclass constructed inside `Application.Start` — starts, sets `Application.Current`, resolves framework theme keys (`SystemControlBackgroundBaseLowBrush` returned `True`), and constructs every control tried. It then **fail-fasts with `0xC0000409` (`STATUS_STACK_BUFFER_OVERRUN`, process exit code `-1073741189`) during the first layout pass** over a window containing a templated control. The crash is not catchable and takes the process down with no managed exception. Phase 4g's spec records the same fail-fast signature from the test host (`2026-09-05-winui-designer-phase4g-design.md:51`), independently.

The cause is resource resolution. In a project with no compiled XAML, `new XamlControlsResources()` throws:

```
COMException hr=0x80004005
Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'.
```

The build emits no **app-level merged `resources.pri`** for a project containing no XAML — per-project `.pri` files are still produced, including for XAML-less libraries such as `WinUIDesigner.XamlDom.pri`; it is the merged app-level one that is missing. Without it, `ms-appx:///` cannot be resolved, the theme resource dictionary never loads, and every control whose default template references a theme brush dies in layout.

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

Process exit code 0. The pixel values come from one run in a default-sized window at this machine's default `RasterizationScale` and are **not** the reproducible part — the 1424 px column is simply that window's client width, and CLAUDE.md's pixel-snapping warning applies. The reproducible assertion is `Template != null` together with a non-zero arranged size and a clean exit.

Reproduced across four configuration variants, each differing from the previous by one factor: (a) empty `App.xaml`, `InitializeComponent()` deferred into `OnLaunched`, `XamlControlsResources` merged from code; (b) the same with `InitializeComponent()` in the constructor; (c) the same with `EnableMsixTooling=false`; (d) the same with `XamlControlsResources` declared inside `App.xaml` instead of from code. All four laid out all nine controls and exited 0.

Which knobs matter, each isolated by a single-variable run:

| Factor | Result |
|---|---|
| `App.xaml` present | **Required.** Without it there is no app-level merged PRI, `ms-appx:///` fails, templated controls fail-fast at layout. |
| `XamlControlsResources` merged | **Required.** Declaring it in `App.xaml` and adding it from code both work. |
| `InitializeComponent()` in the `Application` constructor | Works. |
| Entry point | Use the XamlCompiler-generated `Main`. A hand-written `Main` missing `WinRT.ComWrappersSupport.InitializeComWrappers()` fail-fasts. |
| `EnableMsixTooling` | Either value works. `true` additionally produces the merged `resources.pri` as a build output; `false` produces `<ProjectName>.pri`. Neither is decisive once `App.xaml` exists. |
| A hand-written `app.manifest` | **Must not be added.** It overrides the generated registration-free WinRT activation manifest and the app fail-fasts. |

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

The reference's `IsControl` predicate (`Toolbox.cs`) ports unchanged. The assembly-name fact matters: `LoadAssembly(name)` compares `Assembly.GetName().Name` against the name in a XAML namespace declaration, and for WinUI that name is `Microsoft.WinUI`.

### WinUI APIs the new decisions depend on

Each was confirmed by compiling against it in this tree, not from memory:

| API | Used by |
|---|---|
| `Microsoft.UI.Xaml.Window.AppWindow`; `AppWindow.Closing` with `AppWindowClosingEventArgs.Cancel` | Decision 20 (close with unsaved changes) |
| `Microsoft.UI.Xaml.Input.KeyboardAccelerator` with `Key`, `Modifiers`, `Invoked`, `args.Handled`; `MenuFlyoutItem.KeyboardAccelerators` | Decision 21 (shortcuts) |
| `Microsoft.UI.Xaml.Application.UnhandledException` with `args.Handled` and `args.Message` | Decision 18 |
| `Microsoft.UI.Xaml.UIElement.StartDragAsync(Microsoft.UI.Input.PointerPoint)` | recorded in Known gaps as the mechanism a later phase needs for toolbox drag |
| `Windows.Storage.Pickers.FileOpenPicker`; `WinRT.Interop.WindowNative.GetWindowHandle`; `WinRT.Interop.InitializeWithWindow.Initialize` | Decision 17 (file dialogs). Measured working against a live HWND in the test host, not merely compiled. |

### The build already handles the Appx tooling trap

`Directory.Build.props` resolves `AppxMSBuildToolsPath` through `vswhere`, because WindowsAppSDK wires `GetPriOutputs` into the normal output chain whenever `UseWinUI=true` and its tasks live only inside a Visual Studio install. A new project in this tree inherits the fix. Verified by observing MSB4062 in a probe built outside the tree and its absence for the same project built inside.

## Decisions

Two kinds of note follow each decision. *Cost if wrong* is the failure mode if the decision is mistaken. *Cost of the cut* is what a deliberate scope reduction denies the user; it is not a claim of error.

**1. One `.xaml` file in `src/`, and it is `App.xaml`.**
The project convention has been zero `.xaml` files in `src/`. It is relaxed by exactly one file — `src/WinUIDesigner.Shell.App/App.xaml`, see Decision 5 for why it lives in its own project — because the launch measurements show the application cannot run without it. Every other piece of shell UI is written in C#, matching `OutlineViewControl` and `PropertyGridControl`. `CLAUDE.md` is updated to state the exception, its scope, and the reason.
*Cost if wrong:* a `.xaml` file in `src/` reads as licence for more of them, and the codebase drifts into two UI idioms. Mitigated by stating the exception narrowly in `CLAUDE.md`.

**2. The entry point is the generated one.**
`App.xaml` gives the XamlCompiler an application definition, and it generates `Main` with `InitializeComWrappers()` and a `DispatcherQueueSynchronizationContext`. The shell does not define `Main` and does not set `DISABLE_XAML_GENERATED_MAIN`.
*Cost if wrong:* a hand-written `Main` that omits either step fail-fasts before any managed line runs, which is unusually hard to diagnose — it was the longest-lived false trail in this phase's investigation.

**3. No hand-written `app.manifest`.**
The generated apphost manifest carries the registration-free WinRT activation entries that make `WindowsAppSDKSelfContained` work without an installed runtime; a project-supplied `app.manifest` replaces it.
*Cost if wrong:* WinRT activation fails and the app dies at startup with no managed error.

**4. `XamlControlsResources` is merged declaratively in `App.xaml`.**
Both forms were measured to work; the declarative form matches the platform template and keeps the code path free of a startup ordering constraint.
*Cost if wrong:* none measured; the code form remains available.

**5. The shell is two projects: a library of everything, and an executable that is only the application object.**
Found during implementation, not design: **any `ProjectReference` from a WinUI-hosting test `.exe` to a project carrying an `ApplicationDefinition` fail-fasts that test host with `0xC0000409` before a single test body runs.** Isolated by adding the same reference to the already-green `WinUIDesigner.Designer.Tests` and watching it break identically, then reverting. The failure follows the compiled-XAML application definition, not the code.

So `src/WinUIDesigner.Shell` is `OutputType=Library` and holds every class in this spec — `Document`, `ShellModel`, `Toolbox`, `ShellTypeFinder`, `ShellMetadata`, `NewDocumentTemplate`, `ToolboxView`, `MainWindow` — with no `.xaml` file at all, shaped exactly like `WinUIDesigner.Designer`, which three test projects already reference without trouble. `src/WinUIDesigner.Shell.App` is `OutputType=Exe` and holds `App.xaml` and `App.xaml.cs` and nothing else, with `RootNamespace=WinUIDesigner.Shell` so the project's default namespace matches the hand-written one in `App.xaml.cs`. (It is not what makes `x:Class="WinUIDesigner.Shell.App"` resolve — verified against the generated `App.g.i.cs`, the XamlCompiler places the generated partial class by parsing the `x:Class` string itself, independent of `RootNamespace`.) The test project references the library only.

`WinUIDesigner.Shell.App.csproj` carries: `OutputType=Exe`, `TargetFramework=net10.0-windows10.0.19041.0`, `RootNamespace=WinUIDesigner.Shell`, `Nullable=disable`, `UseWinUI=true`, `RuntimeIdentifier=win-x64`, `SelfContained=false`, `WindowsPackageType=None`, `EnableMsixTooling=true`, `GenerateAppxPackageOnBuild=false`, `WindowsAppSDKSelfContained=true`, and the two pinned `PackageReference`s. `EnableMsixTooling=true` is chosen over `false` only because it produces the merged `resources.pri` as a visible build output, which makes a future regression in the PRI pipeline obvious rather than silent. `WinUIDesigner.Shell.csproj` matches `WinUIDesigner.Designer.csproj`, adding a `ProjectReference` to it. `WinUIDesigner.Shell.Tests.csproj` matches the existing test projects, adding `GenerateProgramFile=false`, `IsPackable=false`, NUnit/NUnitLite/NUnit3TestAdapter/Microsoft.NET.Test.Sdk, and its own `Program.cs` NUnitLite entry point.

Decision 1 is unchanged in substance: there is still exactly one `.xaml` file under `src/`. Its path is `src/WinUIDesigner.Shell.App/App.xaml`.
*Cost if wrong:* omitting `WindowsPackageType=None` breaks unpackaged bootstrapping; omitting `WindowsAppSDKSelfContained=true` reproduces the `REGDB_E_CLASSNOTREG` failure the test projects document, because this machine lacks the DDLM packages. If the split itself proved insufficient, the fallback is linking the library's sources into the test project with a `Compile Include` glob — it works, but it compiles the same code twice.

**6. Logic is split from UI so the logic is testable.**
`ShellModel` and `Document` touch no `Window`, no `XamlRoot`, and no `MainWindow`. They are constructible on the test dispatcher thread and are what the test suite drives.
*Cost if wrong:* the shell ships with no regression net, breaking with every prior phase.

**7. `ShellModel` raises events instead of showing dialogs.**
The reference calls `MessageBox.Show` directly inside `Shell.Close` and `Shell.PrepareExit`. WinUI's replacement, `ContentDialog`, is asynchronous and needs an `XamlRoot`. `ShellModel` exposes `AskSaveChanges` (a document, answered Save / Discard / Cancel) and `ReportError` (a message); `MainWindow` subscribes to both.
*Cost if wrong:* dialog calls buried in logic drag a live `XamlRoot` into every unit test, which is what Decision 6 exists to avoid.

**8. Single document, no tabs.**
One `Document` is open at a time. `ShellModel` still owns a collection and a `CurrentDocument`, so multi-document is an additive change later, but no `TabView` ships.
*Cost of the cut:* one file at a time; no side-by-side comparison.

**9. No XAML text view.**
`Document.UpdateXaml()` exists and produces formatted text, but nothing displays it. The reference's text mode is an AvalonEdit surface with two-way sync and line-info mapping back to the primary selection; porting an editor is its own phase.
*Cost of the cut:* the fastest way to eyeball what Phases A and B fixed is missing. Mitigated by DoD items 6 and 7, which assert the same thing mechanically.

**10. No docking frame, and every pane scrolls.**
A `Grid` with three fixed column definitions replaces AvalonDock. Panes are not floatable, closable, or rearrangeable. `OutlineViewControl` and `PropertyGridControl` both derive from `StackPanel` and contain no `ScrollViewer` of their own, and the toolbox lists 159 items, so **each of the three side panes is wrapped in a `ScrollViewer` by `MainWindow`**, as is the design surface.
*Cost of the cut:* fixed layout. Without the `ScrollViewer` wrapping this would not be a cut but a defect — the outline and property grid would clip on any real document.

**11. The toolbox scans assemblies, as the reference does.**
`Toolbox` loads `typeof(Button).Assembly`, applies the reference's `IsControl` predicate, and **additionally skips any type whose activation throws**, because one of the 160 does. The skip is silent; the type is simply absent.
*Cost if wrong:* a control that fails to activate for an environment-specific reason disappears with no explanation. Accepted; the alternative is a crash while building the list.

**12. The toolbox arms a tool on selection and disarms itself when the engine resets.**
`CreateComponentTool` is single-use: after a placement the engine sets `IToolService.CurrentTool` back to `PointerTool`. A `ListView` whose `SelectionChanged` arms the tool would then be stuck — the placed control stays selected, so clicking it again raises no event and nothing re-arms. `ToolboxView` therefore subscribes to `IToolService.CurrentToolChanged` and clears `ListView.SelectedItem` whenever the current tool returns to `PointerTool`.
*Cost if wrong:* placing the same control twice in a row silently does nothing, which reads as a broken toolbox.

**13. Click-to-place only; no drag from the toolbox.**
Phase 4d deferred toolbox-side drag initiation to this phase. WinUI has no `DragDrop.DoDragDrop`; the equivalent is `UIElement.StartDragAsync(PointerPoint)`, which is asynchronous and needs a live `PointerPoint` captured from the pointer event that began the gesture. That is a design question of its own, and `CreateComponentTool`'s click path already places controls.
*Cost of the cut:* a control is placed by selecting it in the toolbox and then clicking or dragging out a rectangle on the surface, not by dragging the toolbox entry itself. Recorded in Known gaps with the API a later phase needs.

**14. `ShellTypeFinder` derives from `XamlTypeFinder` and seeds itself with `ImportFrom(CreateWinUITypeFinder())`.**
It overrides `LoadAssembly(string)` to resolve against its registered assemblies and the toolbox's assemblies, and `Clone()` to return the singleton — the reference's `MyTypeFinder` shape. `ImportFrom` is `protected`, so the seeding happens inside the derived type; assigning `CreateWinUITypeFinder()`'s return value to a `ShellTypeFinder` variable does not compile.
*Cost if wrong:* documents referencing types outside WinUI fail to load with a type-resolution error.

**15. The new-document template is a constant in the shell, rooted at a `Canvas`.**
The reference reads `NewFileTemplate.xaml` from disk, and its root element is `<Window>` containing a `<Canvas/>`. That does not port: `Microsoft.UI.Xaml.Window` is not a `UIElement`, and `DesignSurface.InitializeDesigner` assigns `DesignPanel.Content = context.RootItem.View`, which requires one. The shell instead holds the template as a `const string` whose root is a `Canvas` with explicit `Width="640" Height="480"`. `Canvas` rather than `Grid` because it is what the reference's template actually contains inside its window, and because `CanvasPlacementSupport` gives free placement, which is what a blank document wants.
*Cost if wrong:* a file-based template can be edited without a rebuild. Not worth a file-not-found failure mode on the app's very first action.

**16. Load errors are handled in two paths, because the engine has two.**
`Document.UpdateDesign()` assigns `settings.ReportErrors = svc => capturedErrors = svc.Errors.ToList()` before calling `LoadDesigner`, and wraps the call in `try/catch (XamlLoadException)`. On the fatal path the exception is caught, `capturedErrors` holds what the parser reported, and the document does not open. On the non-fatal path `LoadDesigner` returns, the document opens, and the same captured list is non-empty. Either way `MainWindow` shows one `ContentDialog` listing at most **ten** entries as `message (line, column)`, followed by a count of any remainder.
*Cost if wrong:* the naive design — read `XamlErrorService` from `DesignContext` after `LoadDesigner` — dereferences null on exactly the documents it exists to diagnose, because `LoadDesigner` unloads the old context before throwing.

**17. File and save failures are caught at `ShellModel` and reported, never thrown.**
Phase B established that a throw escaping into the engine's transaction machinery strands a `ChangeGroup`. The same rule applies to the shell's own I/O: `Open`, `Save`, and `SaveAs` catch `IOException`, `UnauthorizedAccessException`, and `XamlLoadException`, raise `ReportError`, and leave the document collection unchanged. File dialogs use `FileOpenPicker`/`FileSavePicker` associated with the window's HWND through `InitializeWithWindow`; unpackaged WinUI throws at show time without that association.
*Cost if wrong:* a read-only file or a locked path kills the application instead of showing a message.

**18. The shell installs an `Application.UnhandledException` handler.**
Phase B's argument for never throwing from the write path rested on the observation that no handler exists anywhere in `src/`. Phase 5 is the first phase that owns an `Application` and can install one; the reference installs three equivalents and routes them all to `Shell.ReportException`. The handler sets `args.Handled = true`, shows `args.Message` in a dialog, and marks the current document dirty-but-suspect rather than silently continuing.
*Cost if wrong:* a defect anywhere in the 56 design-time behaviors — all of which run on pointer events outside `ShellModel`, where Decision 17 does not reach — kills the process during a drag, and Phase B's documented silent-loss cases become invisible instead of reported. Setting `Handled = true` risks continuing from a corrupt state; that is why the document is marked suspect and the message names the exception rather than swallowing it.

**19. A fresh `PropertyGridControl` per document; explicit unsubscribe contracts.**
Phase 4f recorded that `PropertyGridControl`'s `nodeCache` and `editorHosts` are never invalidated across documents and handed the problem to whichever phase first keeps one alive across a document swap. This is that phase. `MainWindow` constructs a new `PropertyGridControl` when the current document changes and drops the old one, which sidesteps both caches without touching Designer code. Alongside it: `MainWindow` unsubscribes from the outgoing document's `ISelectionService.SelectionChanged` before subscribing to the incoming one — each `LoadDesigner` installs a fresh `DefaultSelectionService` — and `Document.Dispose()` unsubscribes from `UndoService.UndoStackChanged` before calling `UnloadDesigner()`. The reference does neither.
*Cost if wrong:* stale editors bound to disposed `DesignItem`s from a previous document, and a leak that the soak test would attribute to the engine rather than the shell.

**20. Closing the window with unsaved changes goes through `AppWindow.Closing`.**
`Microsoft.UI.Xaml.Window` has no synchronous cancellable close. `AppWindow.Closing` does: `AppWindowClosingEventArgs.Cancel` is set synchronously, but `ContentDialog.ShowAsync` is not. The shell therefore cancels the first close, awaits the dialog, and — on Save or Discard — closes again through a re-entry flag that lets the second close through.
*Cost if wrong:* either the dialog never appears and work is lost, or the window refuses to close at all.

**21. Undo, Redo, Cut, Copy, Paste and Delete ship as menu items with keyboard accelerators.**
Phase 4c deferred global keyboard shortcuts to this phase. `MainWindow`'s Edit menu carries `MenuFlyoutItem`s with `KeyboardAccelerator`s: Ctrl+Z, Ctrl+Y, Ctrl+X, Ctrl+C, Ctrl+V, Delete. Undo and Redo call `UndoService`; the other four call `ICopyPasteService`, which `DesignSurface.LoadDesigner` already registers, guarded by its `Can*` counterpart. Enablement is refreshed on `UndoStackChanged` and `SelectionChanged`. Accelerators on a `MenuFlyoutItem` are scoped to the window, so the design surface does not need focus for them to fire — `DesignPanel.IsTabStop` is true regardless, which matters for the behaviors' own key handling.
*Cost if wrong:* without Delete there is no way to remove a control that has been placed, which makes the app unusable for its stated purpose. Select All is **not** included: `ICopyPasteService` has no such member and "all" is ambiguous for a tree; it is listed under Known gaps.

**22. Metadata is seeded minimally, and the gap is stated.**
Phases 4e and 4f both deferred `BasicMetadata`-style seed data to this phase; no `BasicMetadata` equivalent exists anywhere in `src/`. Unseeded, `Metadata.AddPopularProperty`, `AddStandardValues`, `AddValueRange`, `AddDefaultSize`, `HideProperty` and `DisablePlacement` are all empty, which leaves the property grid's Popular category empty, `ComboBoxEditor` without standard values, `NumberEditor` without ranges, and `ModelTools.GetDefaultSize` without sizes — the last of which directly affects click-to-place. The shell adds a `ShellMetadata.Register()` covering only what the DoD exercises: default sizes for the toolbox's common controls, popular properties for `FrameworkElement` (Width, Height, Margin, HorizontalAlignment, VerticalAlignment) and `Control` (Background, Foreground, FontSize), and standard values for the enum-typed ones the grid already reaches through `TypeConverter`. Nothing else.
*Cost if wrong:* a minimal seed is a judgement call about what a user first reaches for; being wrong means a property is one category away rather than missing. Seeding nothing would leave click-to-place sizing at the engine's fallback, which Decision 22's DoD item detects.

**23. The soak test measures a slope, not a total.**
Phase A measured roughly 4.7 KB of native memory retained per `XamlReader.Load` over **5000** loads (38 MB → 61 MB). At 50 iterations the expected growth is under 250 KB, which is inside process working-set noise — a ceiling fitted to such a run passes whether the leak exists or not. The test therefore opens and disposes one named fixture document 2000 times, samples `Process.PrivateMemorySize64` after `GC.Collect()` and `WaitForPendingFinalizers()` at iteration 500 and again at 2000, and asserts that the per-iteration slope over that span is below a stated ceiling in bytes per iteration. The first measured slope is recorded in a comment beside the ceiling. Document size dominates the absolute numbers — Phase A measured roughly 7 MB for one 1500-attribute document — so the fixture is small and fixed.
*Cost if wrong:* a slope measurement over a small fixture can miss a leak proportional to document complexity. It cannot miss the constant per-load leak that Phase A actually measured, which is the one this test exists for.

## Architecture

```
                          ┌──────────────────────────────┐
                          │  App.xaml  (the only .xaml)  │
                          │ merges XamlControlsResources │
                          └──────────────┬───────────────┘
                                         │ generated Main
                          ┌──────────────▼────────────────────────────┐
                          │                MainWindow                 │  C#, no XAML
                          │  MenuBar · 3-column Grid · dialogs ·      │
                          │  pickers · KeyboardAccelerators ·         │
                          │  AppWindow.Closing gate                   │
                          └─┬──────────────┬──────────────┬───────────┘
            CurrentTool ↓   │              │              │  ↑ SelectionChanged
                            │              │              │  ↓ SelectedItems / Root
              ┌─────────────▼──┐  ┌────────▼─────┐  ┌─────┴──────────────────┐
              │  ToolboxView   │  │ DesignSurface│  │ OutlineViewControl     │
              │  (ListView in  │  │ (in a        │  │ PropertyGridControl    │
              │  a ScrollViewer)│ │ ScrollViewer)│  │ (fresh per document)   │
              └────────────────┘  └──────────────┘  └────────────────────────┘
                            ▲              ▲
                            │              │  MainWindow owns every UI wire above
              ┌─────────────┴──────────────┴───────────┐
              │               ShellModel               │  no Window, no XamlRoot
              │  documents · current · New/Open/Save   │
              │  events: AskSaveChanges, ReportError   │
              └──────────────────┬─────────────────────┘
                                 │
                      ┌──────────▼───────────┐
                      │       Document       │
                      │  Text ⇄ DesignSurface│
                      └──────────────────────┘

   open:  file → Text → XmlReader → LoadDesigner → DesignContext → RootItem.View
                                                 → RootItem.CreateOutlineNode()
          errors ← settings.ReportErrors callback  (fires before XamlLoadException)
   save:  SaveDesigner(XamlXmlWriter) → XamlFormatter.Format → File.WriteAllText
```

`ShellModel` owns document lifetime only. Every arrow into an engine service — `IToolService`, `ISelectionService`, `UndoService`, `ICopyPasteService` — is drawn and owned by `MainWindow`, because those services belong to the current document's `DesignContext` and Decision 6 keeps `ShellModel` free of UI.

### Components

| Unit | Responsibility | Ported from |
|---|---|---|
| `App` + `App.xaml` (in `WinUIDesigner.Shell.App`) | Application object; merges `XamlControlsResources`; installs the `UnhandledException` handler; creates `MainWindow`. Nothing else lives in this project. | `App.xaml`, `App.xaml.cs` |
| `MainWindow` | Layout and `ScrollViewer` wrapping, menu, accelerators, command enablement, dialogs, file pickers, window title, close gate, per-document `PropertyGridControl`, outline filter box. | `MainWindow.xaml`, `MainWindow_Commands.cs` |
| `ShellModel` | Document collection, current document, `New`/`Open`/`Save`/`SaveAs`/`Close`, dirty aggregation, `AskSaveChanges` and `ReportError` events. | `Shell.cs` |
| `Document` | Owns one `DesignSurface`; `Text` ⇄ design; dirty flag; captured load errors; exposes `DesignContext`, `UndoService`, `SelectionService`, `OutlineRoot`; `Dispose` unsubscribes then unloads. | `Document.cs` |
| `Toolbox` | Assembly list, control list per assembly, `IsControl` filter plus the activation filter. | `Toolbox.cs` |
| `ShellTypeFinder` | `XamlTypeFinder` override resolving assemblies the toolbox knows. | `MyTypeFinder.cs` |
| `ToolboxView` | `ListView` of control nodes; selection arms `CreateComponentTool`; clears selection when the engine returns to `PointerTool`. | `ToolboxView.xaml` |
| `ShellMetadata` | One `Register()` seeding default sizes and popular properties. | `BasicMetadata` (reference `WpfDesign.Designer`) |
| `NewDocumentTemplate` | `const string` — a `Canvas` root, 640 × 480. | `NewFileTemplate.xaml` |

`Document` deviates from the reference in five named ways:

1. No `DocumentMode`, so no `InXamlMode`/`InDesignMode` and no text-mode line-info mapping.
2. `Dispose()` unsubscribes from `UndoStackChanged` and calls `DesignSurface.UnloadDesigner()`. The reference does neither; the soak test depends on both.
3. `ReloadFile` is not called from a constructor that also assigns `FilePath` through a property with side effects.
4. The file write moves out of `Document.Save()`/`SaveAs()` into `ShellModel`, so that Decision 17's catch sits in one place. `Document` produces text; `ShellModel` owns the disk.
5. `IsDirty` is set only by `UndoStackChanged` and cleared by save. The reference sets it from the `Text` setter on any assignment, which makes its own `UpdateXaml()` mark the document dirty as a side effect of saving it.

### Data flow, opening a document

1. `ShellModel.Open(path)` reads the file into `Document.Text`, inside Decision 17's catch.
2. `Document.UpdateDesign()` builds `XamlLoadSettings`, adds every toolbox assembly to `DesignerAssemblies`, assigns `ShellTypeFinder.Instance` to `TypeFinder`, assigns the `ReportErrors` callback, and calls `LoadDesigner` inside `try/catch (XamlLoadException)`.
3. On success `DesignPanel.Content` becomes `RootItem.View`; `Document.OutlineRoot` becomes `DesignContext.RootItem.CreateOutlineNode()`.
4. `MainWindow` unsubscribes from the outgoing document's selection service, constructs a fresh `PropertyGridControl`, assigns `OutlineViewControl.Root`, and subscribes to the new `ISelectionService.SelectionChanged`.
5. `Document` subscribes to `UndoService.UndoStackChanged` to set `IsDirty`; `MainWindow` also listens, to refresh command enablement.
6. `MainWindow` shows the error dialog if the captured error list is non-empty, whether or not the load threw.

### Data flow, saving

`Document.UpdateXaml()` writes through `XamlXmlWriter(StringBuilder)` into `DesignSurface.SaveDesigner`, passes the result through `XamlFormatter.Format(string)`, and assigns `Text` without touching `IsDirty`. `ShellModel.Save` writes `Text` to disk and clears the dirty flag. This is the exact path Phases A and B repaired, exercised for the first time by a person rather than a test.

## Testing and definition of done

Tests live in `tests/WinUIDesigner.Shell.Tests`, built as an `Exe` with NUnitLite and run as
`tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult`,
matching the three existing suites. `dotnet test` is not a substitute.

1. `Toolbox` lists only non-abstract, non-generic `UIElement` subclasses with a public parameterless constructor, and every listed type activates.
2. `Toolbox` excludes `PivotHeaderItem` — the one measured activation failure — and does not throw while building the list.
3. `ShellTypeFinder.LoadAssembly("Microsoft.WinUI")` returns the WinUI assembly; an unknown name returns null rather than throwing.
4. `ShellModel.New()` produces a document that loads: non-null `DesignContext` and `RootItem`, and the root is a `Canvas` of 640 × 480.
5. `Document` opened from a known XAML string exposes a non-null `DesignContext`, `RootItem`, and `OutlineRoot`.
6. Round trip is a fixed point as a string: with `s1 = save(load(x))` and `s2 = save(load(s1))`, assert `s1 == s2` by ordinal string equality. Stated this way so it cannot be satisfied by a null check.
7. A document containing a `Polyline` with real points round-trips with its points intact, closing the loop on Phases A and B in the shell's own save path.
8. `Document.IsDirty` is false after load, false after `UpdateXaml()` alone, true after a change committed through `UndoService`, and false again after `ShellModel.Save`.
9. `ShellModel.Open` on a missing or unreadable path raises `ReportError` and leaves the document collection unchanged — no exception escapes.
10. **Fatal load error:** a document that is not well-formed XML (an unclosed tag) raises `ReportError` with at least one entry captured through the `ReportErrors` callback, leaves the collection unchanged, and throws nothing out of `ShellModel`.
11. **Non-fatal load error:** a well-formed document naming an unknown type opens, and the captured error list is non-empty.
12. `ShellModel.Close` on a dirty document raises `AskSaveChanges` and performs no I/O until answered; answering Cancel leaves the document open.
13. `Document.Dispose()` leaves `DesignSurface.DesignContext` null, and a second `Dispose()` does not throw.
14. `ToolboxView` re-arms: arming a tool, simulating the engine's reset to `PointerTool`, and selecting the same entry again produces a second `CreateComponentTool` — the regression Decision 12 exists to prevent.
15. `ShellMetadata.Register()` makes `ModelTools.GetDefaultSize` return the seeded size for a seeded control type, and the property grid's Popular category non-empty for a `Button`.
16. **Soak:** 2000 open/dispose cycles of one small named fixture; `Process.PrivateMemorySize64` sampled after a forced collection at iteration 500 and 2000; the per-iteration slope over that span is below a stated ceiling in bytes per iteration, with the first measured value recorded in a comment.
17. All three existing suites stay green: XamlDom 144 (140 passed, 4 pre-existing ignores), DesignModel 77/77, Designer 245/245.
18. **Manual, and labelled as manual:** `WinUIDesigner.Shell.App.exe` launches; New produces a blank canvas; a control is selected in the toolbox and placed by clicking the surface; a property is edited in the grid; Ctrl+Z undoes it; Delete removes the control; Ctrl+S saves; the saved file reopens with the same content; closing with unsaved changes prompts.

## Known gaps

- No document tabs; one document at a time.
- No XAML text view and no text editing.
- No dockable, floatable, or resizable panes.
- No drag from the toolbox onto the surface. The WinUI mechanism a later phase needs is `UIElement.StartDragAsync(PointerPoint)` — verified to exist — with the `CreateComponentTool` travelling in the `DataPackage` that `CreateComponentTool.HandleDragOver`/`HandleDrop` already expect.
- No Select All. `ICopyPasteService` has no member for it.
- No Refresh command (the reference's F5 → `Document.Refresh` = `UpdateXaml` + `UpdateDesign`).
- No thumbnail pane; no `ThumbnailView` equivalent exists in `src/` at all.
- No Run and no render-to-bitmap.
- No opening files from command-line arguments, and no drag-drop of `.xaml` or `.dll` onto the window.
- No runtime assembly loading into the toolbox. Consequently Decision 14's "resolve against the toolbox's assemblies" has exactly one assembly to resolve against; the override is written for the general case but is not exercised beyond it.
- No already-open-path dedupe in `Open` — single-document mode makes it moot, and it becomes necessary the moment tabs arrive.
- No recent-files list and no settings persistence; the reference's `Settings.Default` is a WPF `ApplicationSettingsBase` facility with no direct port.
- No error list pane and no jump-to-error; errors appear once, in a dialog capped at ten entries plus a count.
- No Behaviors or Triggers editors; `WpfDesign.Designer.ExpressionBlendInteractionAddon` is not ported.
- The toolbox lists all 159 activatable controls flat, grouped only by assembly. The reference has the same shape; its `Metadata.IsPopularControl` filter is commented out there.
- A control that fails to activate is dropped from the toolbox silently.
- `ShellMetadata` seeds only what DoD item 15 exercises. Popular properties beyond the listed ones, value ranges, hidden properties and disabled placements are all still empty.
- **The soak test is red, by design, and it found something.** Three independent 2000-iteration runs measured **79,935 / 85,251 / 79,834 bytes of process private-byte growth per document open/dispose cycle** — about 17x the ~4.7 KB per `XamlReader.Load` that Phase A measured, so the excess belongs to the `DesignSurface` load/unload machinery rather than to parsing. The ceiling was deliberately left at 4,700 bytes per iteration instead of being raised to fit, so the test fails when run and reports the regression rather than laundering it. It carries `[Explicit]`, so a normal suite run is unaffected (30 passed, 1 not run) — which also means nobody sees it unless they look. A later experiment removed one shell-side cost: `Document.UpdateDesign` had been registering every toolbox assembly into `XamlLoadSettings.DesignerAssemblies`, and `ExtensionManager`'s de-dupe set is per-`DesignContext`, so every document open re-reflected over roughly 2400 types of `Microsoft.WinUI` hunting for `[ExtensionFor]` attributes that assembly does not carry. Removing it saved **11,700 bytes per cycle, about 14%** — measured 85,066 with the loop against 73,286 without, on the same machine in the same session. It was not the cause: **roughly 73,000 bytes per cycle remain**, in the engine's own load path. The growth is native, not managed: sampling `GC.GetTotalMemory(true)` at the same two points gives a slope of only about **2,125 bytes per iteration**, under 3% of the private-byte figure — and that managed figure was *identical to the decimal* with and without the reflection loop, so the loop's cost was purely native — enough to rule out a plain rooted-managed-object leak and to rule out GC segment high-water-mark retention, which would require the two counters to move together. What remains is native/WinRT-side retention that the managed counter cannot see, which is the same shape as Phase A's finding at a much larger scale. One confound is recorded on the test for whoever picks this up: `WinUITestHost.Run` enqueues the whole loop as a single dispatcher callback, so the message queue is never pumped between iterations — probably moot, since the `DesignSurface` is never attached to a live `Window` here and the subtree likely never realizes native composition visuals. Root-causing it was out of scope for Phase 5; the likely places to start are extension and adorner teardown in `DesignSurface.UnloadDesigner()`, and state retained across `Document` instances by the toolbox or the type finder. Ruled out already: `Document`'s own `UndoStackChanged` handler, which `DocumentTests.DisposeUnloadsTheDesignerAndIsIdempotent` now proves is detached on `Dispose`.
- The soak test measures a constant per-iteration slope over a small fixture; a leak proportional to document complexity would not show up.
- `ToolboxViewTests.DetachStopsListeningSoAClosedDocumentCannotDriveTheView` is vacuous and known to be. `ToolboxView.OnCurrentToolChanged` ignores its `sender` and gates on the view's own currently-attached service, so raising the event on a detached service returns early whether or not the subscription leaked, and the test's final assertion holds unconditionally. The production `Attach`/`Detach` code was independently traced and is correct; what is missing is the regression net, not the behaviour. Its sibling `DetachClearsAnArmedButUnplacedSelection` is sound and mutation-verified, and does cover part of the same ground.
- `MainWindow.SaveAsync` calls `UpdateTitle()`, which reads the wired document rather than the document it was passed. Harmless while the shell is single-document; it would repaint the wrong title once tabs exist.
- Decision 18's handler sets `Handled = true`, so the app continues after an unhandled exception in a design-time behavior. The document is marked suspect, but the process state after such a continuation is not guaranteed.

## Out of scope

- Any change to `XamlDom`, `DesignModel`, or `Designer`, unless implementing the shell exposes a defect in them.
- The remaining Phase A and B gaps: `RectangleGeometry`/`EllipseGeometry`/`LineGeometry`/`GeometryGroup` and `LinearGradientBrush` still save as empty elements, `Vector2` has no formatter, and `IXamlErrorSink.ReportError` is still called with line and column 0.
- Multi-document support, a text editor, docking, toolbox drag, and settings persistence — each is additive and none is required to prove the engine.
