# WinUIDesigner.Designer (Phase 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the design-time overlay layer (Adorner replacement), hit-testing,
and a minimal `DesignSurface`/`DesignPanel` — enough to host a live
`DesignItem.View` in a real WinUI 3 window, click an element to select it,
and see one highlight adorner track it. No resize thumbs, no drag-drop
placement, no property grid — those are Phase 4/5.

**Architecture:** New project `src/WinUIDesigner.Designer/` (mirrors the
reference's `WpfDesign.Designer` split), referencing the already-merged
`WinUIDesigner.XamlDom` (Phase 1) and `WinUIDesigner.DesignModel` (Phase 2).
The overlay/hit-testing/selection *abstractions* (`AdornerPanel`,
`AdornerProvider`, `IDesignPanel`, `ISelectionService`, etc.) are added to
the existing `WinUIDesigner.DesignModel` project — where the reference
itself keeps them — while the *concrete*, UI-framework-heavy pieces
(`AdornerLayer`, `DesignPanel`, `DesignSurface`, `DefaultSelectionService`)
go into the new project. Every WPF→WinUI mechanism swap below was verified
against `Microsoft.UI.Xaml.winmd`/`Microsoft.UI.winmd` during spec
brainstorming — see the spec's "WinUI API surface" section; this plan does
not re-derive those findings, it applies them.

**Tech Stack:** .NET 10 SDK (`10.0.302`), WinUI 3 / Windows App SDK 1.7
(`Microsoft.WindowsAppSDK` `1.7.250606001` — same pin as Phase 1/2), NUnit 3
+ NUnitLite (matching the existing authoritative `.exe` test runner).

**Spec:** `docs/superpowers/specs/2026-09-01-winui-designer-phase3-design.md`

## Global Constraints

- Target framework for the new project: `net10.0-windows10.0.19041.0`,
  `UseWinUI=true`, `PackageReference Microsoft.WindowsAppSDK` version
  `1.7.250606001` — same values as Phase 1/2, do not silently change.
- New code lives under `D:\Project\ST4I\Research\WinUIDesigner`. The
  reference repo `F:\ReferencesCode\CSharp\WpfDesigner` is read-only source
  material — never edit it.
- `Nullable` stays `disable`, matching Phase 1/2 and the reference codebase.
- Every task that touches a flagged gap (numbered in each task, matching the
  spec's "Known gaps" section) must resolve it explicitly and empirically —
  no silent fallback, no swallowed exception that hides a platform
  difference, no assumption presented as a verified fact.
- Commit after each task passes its verification step.
- **Authoritative test command**, same reasoning as Phase 1/2 (WinUI objects
  are thread-affine; `dotnet test`'s `testhost.exe` cannot activate them):
  run the built `.exe` directly —
  `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe`
  — not `dotnet test`. `dotnet test` stays useful for IDE discovery and
  non-WinUI-object checks.
- Phase 1's suite (`WinUIDesigner.XamlDom.Tests`, baseline
  `70 total / 66 passed / 0 failed / 4 ignored`) and Phase 2's suite
  (`WinUIDesigner.DesignModel.Tests`) must both stay green throughout this
  plan. Re-run both `.exe`s as part of every task's verification step and
  treat any regression as a bug in that task, not something to patch around
  later.
- `IDesignPanel.CustomHitTestFilterBehavior`, `ITool`/`IToolService`,
  `IErrorService`, `ITopLevelWindow`/`ITopLevelWindowService`,
  `IKeyBindingService`, `ICopyPasteService`, `ISelectionFilterService` are
  declared (mechanical interface ports) but never implemented or registered
  in this plan — per the spec's "Service scope, picked deliberately narrow"
  decision. Do not add concrete implementations "while we're at it."

---

## Background: why this is five tasks

The spec identifies two hard technical risks — the overlay/Adorner-
replacement geometry and the hit-testing mechanism — plus a foundational
unknown (whether `WinUIDesigner.TestHost`'s dispatcher-only thread can
support real layout at all, spec's Known gap #1). Splitting along those
seams:

1. **Task 1** — resolve Known gap #1 empirically and scaffold the new
   project pair. Nothing else in this plan can be tested for real until
   this is settled: every later task's tests need a live layout pass.
2. **Task 2** — the abstract additions to `WinUIDesigner.DesignModel`
   (overlay types, hit-test types, service interfaces,
   `PrimarySelectionExtensionServer`). Zero dependency on the new project;
   testable with the dispatcher thread alone (no `Window` needed), so it
   can proceed in parallel with Task 1 conceptually, but is sequenced after
   it here because Task 1's `RunWithWindow` low-risk validation informs
   whether any of Task 2's own tests should also exercise real layout.
3. **Task 3** — the concrete overlay host (`AdornerLayer`) and `DesignPanel`
   (hit-testing, pointer-driven selection resolution). This is where Task
   1's live-window infrastructure and Task 2's abstractions first combine
   into working geometry — the riskiest task, isolated on its own.
4. **Task 4** — `DesignSurface`, `DefaultSelectionService`, and the
   selection-driven highlight adorner. Wires Tasks 2/3 into an actual
   click-to-select pipeline.
5. **Task 5** — the Definition-of-Done test suite from the spec, plus final
   verification that the three flagged gaps all have tested resolutions.

---

### Task 1: Resolve Known gap #1 (live `Window`/`XamlRoot` layout) + scaffold `WinUIDesigner.Designer`

**Files:**
- Modify: `tests/WinUIDesigner.TestHost/WinUITestHost.cs` (add
  `RunWithWindow`)
- Create: `src/WinUIDesigner.Designer/WinUIDesigner.Designer.csproj`
- Create: `src/WinUIDesigner.Designer/AssemblyInfo.cs`
- Create: `tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
- Create: `tests/WinUIDesigner.Designer.Tests/Program.cs`
- Create: `tests/WinUIDesigner.Designer.Tests/LiveLayoutTests.cs`
- Modify: `WinUIDesigner.sln`

**Interfaces:**
- Consumes: `WinUIDesigner.TestHost.WinUITestHost.Run(Action)` (Phase 2,
  unchanged).
- Produces: `WinUIDesigner.TestHost.WinUITestHost.RunWithWindow(Action<Microsoft.UI.Xaml.Window>)`
  — every later task's live-layout tests use this. A buildable, empty
  `WinUIDesigner.Designer` library and a buildable
  `WinUIDesigner.Designer.Tests` executable.

- [ ] **Step 1: Add `WinUITestHost.RunWithWindow`**

  `WinUITestHost.Run` already marshals onto the dispatcher thread and
  handles the "no WinUI thread available" ignore-path (Phase 1/2). Add a
  new method to the same `WinUIDesigner.TestHost.WinUITestHost` class in
  `tests/WinUIDesigner.TestHost/WinUITestHost.cs` (insert after the
  existing `Run` method, before its closing brace's containing class ends):

  ```csharp
  /// <summary>
  /// Runs <paramref name="action"/> on the WinUI dispatcher thread with a real,
  /// activated <see cref="Microsoft.UI.Xaml.Window"/> available - needed for any test that
  /// depends on a live layout pass (ActualWidth/ActualHeight, TransformToVisual,
  /// VisualTreeHelper.FindElementsInHostCoordinates all require an element to be
  /// connected to an activated Window's XamlRoot). The window is closed after
  /// <paramref name="action"/> returns, even if it throws.
  /// </summary>
  public static void RunWithWindow(Action<Microsoft.UI.Xaml.Window> action)
  {
      Run(() => {
          var window = new Microsoft.UI.Xaml.Window();
          try {
              window.Activate();
              action(window);
          } finally {
              window.Close();
          }
      });
  }
  ```

- [ ] **Step 2: Scaffold `WinUIDesigner.Designer`**

  `src/WinUIDesigner.Designer/WinUIDesigner.Designer.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <OutputType>Library</OutputType>
      <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
      <RootNamespace>WinUIDesigner.Designer</RootNamespace>
      <Nullable>disable</Nullable>
      <UseWinUI>true</UseWinUI>
      <RuntimeIdentifier>win-x64</RuntimeIdentifier>
      <SelfContained>false</SelfContained>
      <WindowsPackageType>None</WindowsPackageType>
      <EnableMsixTooling>false</EnableMsixTooling>
      <GenerateAppxPackageOnBuild>false</GenerateAppxPackageOnBuild>
      <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.7.250606001" />
      <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4188" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\WinUIDesigner.XamlDom\WinUIDesigner.XamlDom.csproj" />
      <ProjectReference Include="..\WinUIDesigner.DesignModel\WinUIDesigner.DesignModel.csproj" />
    </ItemGroup>

  </Project>
  ```
  `src/WinUIDesigner.Designer/AssemblyInfo.cs`:
  ```csharp
  using System.Reflection;

  [assembly: AssemblyTitle("WinUIDesigner.Designer")]
  [assembly: AssemblyDescription("DesignSurface, overlay layer, and hit-testing for WinUI 3")]
  ```

- [ ] **Step 3: Scaffold `WinUIDesigner.Designer.Tests`**

  `tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  (same shape as `WinUIDesigner.DesignModel.Tests.csproj`):
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <GenerateProgramFile>false</GenerateProgramFile>
      <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
      <RootNamespace>WinUIDesigner.Designer.Tests</RootNamespace>
      <Nullable>disable</Nullable>
      <UseWinUI>true</UseWinUI>
      <RuntimeIdentifier>win-x64</RuntimeIdentifier>
      <SelfContained>false</SelfContained>
      <WindowsPackageType>None</WindowsPackageType>
      <EnableMsixTooling>false</EnableMsixTooling>
      <GenerateAppxPackageOnBuild>false</GenerateAppxPackageOnBuild>
      <IsPackable>false</IsPackable>
      <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.7.250606001" />
      <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4188" />
      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
      <PackageReference Include="NUnit" Version="3.14.0" />
      <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
      <PackageReference Include="NUnitLite" Version="3.14.0" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\..\src\WinUIDesigner.Designer\WinUIDesigner.Designer.csproj" />
      <ProjectReference Include="..\..\src\WinUIDesigner.DesignModel\WinUIDesigner.DesignModel.csproj" />
      <ProjectReference Include="..\WinUIDesigner.TestHost\WinUIDesigner.TestHost.csproj" />
    </ItemGroup>

  </Project>
  ```
  `tests/WinUIDesigner.Designer.Tests/Program.cs` (identical pattern to
  Phase 1/2's):
  ```csharp
  using System;
  using System.Reflection;

  namespace WinUIDesigner.Designer.Tests
  {
      public static class Program
      {
          public static int Main(string[] args)
          {
              return new NUnitLite.AutoRun(Assembly.GetExecutingAssembly()).Execute(args);
          }
      }
  }
  ```

- [ ] **Step 4: Add both projects to the solution**

  ```bash
  dotnet sln WinUIDesigner.sln add src/WinUIDesigner.Designer/WinUIDesigner.Designer.csproj
  dotnet sln WinUIDesigner.sln add tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj
  dotnet build WinUIDesigner.sln
  ```
  Expected: builds clean (the new projects have no source files yet besides
  `AssemblyInfo.cs`/`Program.cs`, so this only proves the scaffolding is
  wired correctly).

- [ ] **Step 5: Write and run the Known gap #1 empirical test**

  `tests/WinUIDesigner.Designer.Tests/LiveLayoutTests.cs`:
  ```csharp
  using Microsoft.UI.Xaml.Controls;
  using Microsoft.UI.Xaml.Media;
  using NUnit.Framework;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.Designer.Tests
  {
      [TestFixture]
      public class LiveLayoutTests
      {
          [Test]
          public void ActivatedWindowProducesRealLayoutAndHitTestableContent()
          {
              // Known gap #1 (spec): does an activated-but-not-necessarily-visible-on-screen
              // Window produce a real layout pass and a working XamlRoot, or does this need
              // something more (explicit positioning, an extra dispatcher tick, a visible
              // desktop session)? This test is the empirical answer, not an assumption.
              WinUITestHost.RunWithWindow(window => {
                  var button = new Button { Content = "X", Width = 80, Height = 30 };
                  var grid = new Grid { Width = 300, Height = 200 };
                  grid.Children.Add(button);
                  window.Content = grid;
                  grid.UpdateLayout();

                  Assert.IsNotNull(button.XamlRoot,
                      "button should have a XamlRoot once its Window is activated");
                  Assert.Greater(button.ActualWidth, 0,
                      "layout should have run: ActualWidth should be nonzero");
                  Assert.Greater(button.ActualHeight, 0,
                      "layout should have run: ActualHeight should be nonzero");

                  var centerInButton = new Windows.Foundation.Point(button.ActualWidth / 2, button.ActualHeight / 2);
                  var hostPoint = button.TransformToVisual(button.XamlRoot.Content).TransformPoint(centerInButton);
                  var hits = VisualTreeHelper.FindElementsInHostCoordinates(hostPoint, button.XamlRoot.Content);

                  CollectionAssert.Contains(hits, button,
                      "FindElementsInHostCoordinates should find the button at its own center " +
                      "point after an Activate()+UpdateLayout() pass");
              });
          }
      }
  }
  ```
  Run:
  ```bash
  dotnet build WinUIDesigner.sln
  tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
  ```

  **If this passes** (expected on the interactive desktop session this repo
  is developed in): gap #1 is resolved for interactive use —
  `RunWithWindow` is the mechanism every later task's live-layout tests use.
  Add a one-line comment to `RunWithWindow` recording this:
  `// Verified (Phase 3, Task 1): Activate() + UpdateLayout() alone is
  sufficient for real layout and hit-testing on an interactive desktop
  session. Headless/disconnected-session (CI) behavior is unconfirmed -
  out of scope for this plan; whoever wires up CI should re-run this test
  there first.` Proceed to Step 6.

  **If this fails** (e.g. `Activate()` throws, or `ActualWidth`/`XamlRoot`
  stay `0`/`null`): do not guess a fix. Try, in order, and record which one
  (if any) resolves it, with the empirical reason why:
  1. An extra dispatcher round-trip before asserting — `Activate()`'s
     effects may not be synchronous with the first layout pass:
     ```csharp
     window.Activate();
     var ready = new System.Threading.ManualResetEventSlim();
     window.DispatcherQueue.TryEnqueue(() => ready.Set());
     ready.Wait();
     ```
  2. Explicit off-screen positioning via `Microsoft.UI.Windowing.AppWindow`
     (`window.AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000))`
     before the layout assertions) — some window managers only run a
     compositor pass for windows placed within visible desktop bounds.
  If neither resolves it, stop and report the exact failure to the user
  rather than shipping a plan that assumes a broken mechanism works —
  every later task depends on this.

- [ ] **Step 6: Verify Phase 1/2 are still green, commit**

  ```bash
  tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
  tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
  ```
  Expected: both unchanged from their pre-Task-1 baselines.
  ```bash
  git add tests/WinUIDesigner.TestHost/WinUITestHost.cs src/WinUIDesigner.Designer tests/WinUIDesigner.Designer.Tests WinUIDesigner.sln
  git commit -m "chore: scaffold WinUIDesigner.Designer, resolve live-layout gap (Phase 3 Task 1)"
  ```

---

### Task 2: `WinUIDesigner.DesignModel` additions — overlay abstractions, hit-test types, service interfaces

Ports everything the spec keeps in `WinUIDesigner.DesignModel` (mirroring
where the reference itself keeps these types — `WpfDesign` core, not
`WpfDesign.Designer`): the overlay/Adorner-replacement abstractions, the
hit-test result/type, `IDesignPanel`/`ITool`/`IToolService`, the six
service interfaces, `PrimarySelectionExtensionServer`, and the
`ServiceContainer.Selection` convenience property. None of this depends on
the new `WinUIDesigner.Designer` project — testable with the existing
dispatcher-only `WinUITestHost.Run` (no live window needed; nothing here
does real layout yet).

**Files:**
- Create: `src/WinUIDesigner.DesignModel/Adorners/AdornerPlacement.cs`
- Create: `src/WinUIDesigner.DesignModel/Adorners/AdornerPanel.cs` (includes
  `AdornerOrder`, matching the reference — that struct is nested in the
  same file there too)
- Create: `src/WinUIDesigner.DesignModel/Adorners/IAdornerLayer.cs`
- Create: `src/WinUIDesigner.DesignModel/Adorners/AdornerProvider.cs`
- Create: `src/WinUIDesigner.DesignModel/HitTestType.cs`
- Create: `src/WinUIDesigner.DesignModel/DesignPanelHitTestResult.cs`
- Create: `src/WinUIDesigner.DesignModel/Tools.cs`
- Modify: `src/WinUIDesigner.DesignModel/Services.cs` (add
  `SelectionTypes`, `ISelectionService`, `IErrorService`,
  `ITopLevelWindow`/`ITopLevelWindowService`, `IKeyBindingService`)
- Create: `src/WinUIDesigner.DesignModel/Services/ICopyPasteService.cs`
- Create: `src/WinUIDesigner.DesignModel/Services/ISelectionFilterService.cs`
- Create: `src/WinUIDesigner.DesignModel/Extensions/SelectionExtensionServers.cs`
- Modify: `src/WinUIDesigner.DesignModel/ServiceContainer.cs` (add
  `.Selection`)
- Create: `tests/WinUIDesigner.DesignModel.Tests/AdornerPanelTests.cs`
- Create: `tests/WinUIDesigner.DesignModel.Tests/SelectionAndAdornerIntegrationTests.cs`

**Interfaces:**
- Consumes: `WinUIDesigner.DesignModel.Extensions.DefaultExtension`/
  `DefaultExtensionServer`/`ExtensionServer`/`ExtensionForAttribute`/
  `ExtensionServerAttribute` (Phase 2), `DesignItem`/`DesignContext`/
  `ServiceContainer`/`DesignItemCollectionEventArgs` (Phase 2).
- Produces: `Adorners.AdornerPanel`/`AdornerOrder`/`AdornerPlacement`/
  `AdornerProvider`/`IAdornerLayer`, `HitTestType`,
  `DesignPanelHitTestResult`, `ITool`/`IToolService`/`IDesignPanel`,
  `ISelectionService`/`SelectionTypes` and the other five service
  interfaces, `Extensions.PrimarySelectionExtensionServer`,
  `ServiceContainer.Selection`. Task 3's `AdornerLayer`/`DesignPanel`
  implement `IDesignPanel` and consume `AdornerPanel`/`DesignPanelHitTestResult`
  directly.

#### Known drops from this task's reference files (verified, not guessed)

- **`IDesignPanel`'s `IInputElement` base and its `MouseDown`/`MouseUp`/
  `DragEnter`/`DragOver`/`DragLeave`/`Drop` event redeclarations** are
  dropped (spec decision) — `DesignPanel` (Task 3) is a real
  `Microsoft.UI.Xaml.UIElement` and already has `PointerPressed`/
  `PointerReleased`/drag events natively.
- **`IDesignPanel.CustomHitTestFilterBehavior`** is dropped (spec
  decision) — typed in WPF's `HitTestFilterCallback`, which this phase's
  hit-testing approach (verified during spec brainstorming:
  `VisualTreeHelper.FindElementsInHostCoordinates` has no filter-callback
  parameter) doesn't use.
- **`IToolService.DesignPanelHitTestFilterCallback`** is dropped for the
  same reason — same `HitTestFilterCallback` type, and `IToolService` has
  no concrete implementation in this phase anyway.
- **`AdornerPanel.AbsoluteToRelative`/`RelativeToAbsolute`** are dropped
  (spec decision) — typed in `System.Windows.Vector`, no
  `Windows.Foundation` counterpart, zero consumers in this phase's scope
  (`RenderTransformOriginExtension.cs`, their only reference-repo consumer,
  is Phase 4).
- **`AdornerLayer.ArrangeOverride`'s `GeneralTransformGroup` 3D-fallback
  branch** — not this task (that's Task 3's file), noted here so the
  drop isn't missed: confirmed absent from the winmd during spec
  brainstorming.

- [ ] **Step 1: Port `AdornerPlacement.cs`**

  ```csharp
  // src/WinUIDesigner.DesignModel/Adorners/AdornerPlacement.cs
  using Windows.Foundation;
  using Microsoft.UI.Xaml;

  namespace WinUIDesigner.DesignModel.Adorners
  {
      public abstract class AdornerPlacement
      {
          public static readonly AdornerPlacement FillContent = new FillContentPlacement();

          public abstract void Arrange(AdornerPanel panel, UIElement adorner, Size adornedElementSize);

          sealed class FillContentPlacement : AdornerPlacement
          {
              public override void Arrange(AdornerPanel panel, UIElement adorner, Size adornedElementSize)
              {
                  adorner.Arrange(new Rect(new Point(0, 0), adornedElementSize));
              }
          }
      }
  }
  ```
  (Reference used `new Rect(adornedElementSize)`, a `Size`-only constructor
  WPF's `Rect` has. `Windows.Foundation.Rect` has no such overload — use
  the explicit `Rect(Point, Size)` constructor instead, same result since
  the origin is always `(0,0)`.)

- [ ] **Step 2: Port `AdornerPanel.cs` — attached-property metadata gap, `InternalChildren` gap, `GetRealElementSize` gap 2**

  ```csharp
  // src/WinUIDesigner.DesignModel/Adorners/AdornerPanel.cs
  using System;
  using Windows.Foundation;
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Controls;
  using Microsoft.UI.Xaml.Media;

  namespace WinUIDesigner.DesignModel.Adorners
  {
      public sealed class AdornerPanel : Panel
      {
          public static readonly DependencyProperty PlacementProperty = DependencyProperty.RegisterAttached(
              "Placement", typeof(AdornerPlacement), typeof(AdornerPanel),
              new PropertyMetadata(AdornerPlacement.FillContent, OnPlacementChanged));

          // WinUI attached-property metadata has no FrameworkPropertyMetadataOptions.AffectsParentMeasure
          // equivalent (confirmed absent from Microsoft.UI.Xaml.winmd during spec brainstorming) -
          // invalidate the owning AdornerPanel's measure manually instead.
          static void OnPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
          {
              var adorner = d as UIElement;
              var panel = adorner != null ? VisualTreeHelper.GetParent(adorner) as AdornerPanel : null;
              if (panel != null) panel.InvalidateMeasure();
          }

          public static AdornerPlacement GetPlacement(UIElement adorner)
          {
              if (adorner == null) throw new ArgumentNullException("adorner");
              return (AdornerPlacement)adorner.GetValue(PlacementProperty);
          }

          public static void SetPlacement(UIElement adorner, AdornerPlacement placement)
          {
              if (adorner == null) throw new ArgumentNullException("adorner");
              if (placement == null) throw new ArgumentNullException("placement");
              adorner.SetValue(PlacementProperty, placement);
          }

          UIElement _adornedElement;
          DesignItem _adornedDesignItem;
          AdornerOrder _order = AdornerOrder.Content;

          public UIElement AdornedElement { get { return _adornedElement; } }
          public DesignItem AdornedDesignItem { get { return _adornedDesignItem; } }

          public void SetAdornedElement(UIElement adornedElement, DesignItem adornedDesignItem)
          {
              if (adornedElement == null) throw new ArgumentNullException("adornedElement");
              if (_adornedElement == adornedElement && _adornedDesignItem == adornedDesignItem) return;
              if (_adornedElement != null) throw new InvalidOperationException("AdornedElement is already set.");
              _adornedElement = adornedElement;
              _adornedDesignItem = adornedDesignItem;
          }

          public AdornerOrder Order {
              get { return _order; }
              set { _order = value; }
          }

          protected override Size MeasureOverride(Size availableSize)
          {
              if (this.AdornedElement != null) {
                  foreach (var child in Children) {
                      child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                  }
                  return GetRealElementSize(this.AdornedElement);
              }
              return base.MeasureOverride(availableSize);
          }

          protected override Size ArrangeOverride(Size finalSize)
          {
              foreach (UIElement element in Children) {
                  GetPlacement(element).Arrange(this, element, finalSize);
              }
              return finalSize;
          }

          // Known gap #2 (spec): the reference's only use of PlacementOperation in this file is
          // this static helper - ported directly rather than pulling in the rest of
          // PlacementOperation.cs, which this phase does not otherwise use (see spec's
          // "Explicitly out of scope").
          static Size GetRealElementSize(UIElement element)
          {
              var size = element.RenderSize;
              var fe = element as FrameworkElement;
              if (fe != null && !double.IsNaN(fe.Width)) size.Width = fe.Width;
              if (fe != null && !double.IsNaN(fe.Height)) size.Height = fe.Height;
              if (fe != null && size.Width < fe.MinWidth) size.Width = fe.MinWidth;
              if (fe != null && size.Height < fe.MinHeight) size.Height = fe.MinHeight;
              return size;
          }
      }

      public struct AdornerOrder : IComparable<AdornerOrder>, IEquatable<AdornerOrder>
      {
          public static readonly AdornerOrder Background = new AdornerOrder(100);
          public static readonly AdornerOrder Content = new AdornerOrder(200);
          public static readonly AdornerOrder BehindForeground = new AdornerOrder(280);
          public static readonly AdornerOrder Foreground = new AdornerOrder(300);
          public static readonly AdornerOrder BeforeForeground = new AdornerOrder(400);

          readonly int i;

          internal AdornerOrder(int i) { this.i = i; }

          public override int GetHashCode() { return i.GetHashCode(); }
          public override bool Equals(object obj) { return obj is AdornerOrder && this == (AdornerOrder)obj; }
          public bool Equals(AdornerOrder other) { return i == other.i; }
          public int CompareTo(AdornerOrder other) { return i.CompareTo(other.i); }
          public static bool operator ==(AdornerOrder l, AdornerOrder r) { return l.i == r.i; }
          public static bool operator !=(AdornerOrder l, AdornerOrder r) { return l.i != r.i; }
          public static bool operator <(AdornerOrder l, AdornerOrder r) { return l.i < r.i; }
          public static bool operator <=(AdornerOrder l, AdornerOrder r) { return l.i <= r.i; }
          public static bool operator >(AdornerOrder l, AdornerOrder r) { return l.i > r.i; }
          public static bool operator >=(AdornerOrder l, AdornerOrder r) { return l.i >= r.i; }
      }
  }
  ```
  (`base.InternalChildren` in the reference's `MeasureOverride` becomes
  plain `Children` — WinUI `Panel` has no protected child-collection
  member, confirmed absent from the winmd.)

- [ ] **Step 3: Port `IAdornerLayer.cs`, `AdornerProvider.cs`**

  `IAdornerLayer.cs` — verbatim marker interface, namespace only:
  ```csharp
  // src/WinUIDesigner.DesignModel/Adorners/IAdornerLayer.cs
  namespace WinUIDesigner.DesignModel.Adorners
  {
      public interface IAdornerLayer
      {
      }
  }
  ```
  `AdornerProvider.cs`:
  ```csharp
  // src/WinUIDesigner.DesignModel/Adorners/AdornerProvider.cs
  using System.Collections.ObjectModel;
  using Microsoft.UI.Xaml;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Extensions;

  namespace WinUIDesigner.DesignModel.Adorners
  {
      public abstract class AdornerProvider : DefaultExtension
      {
          sealed class AdornerPanelCollection : Collection<AdornerPanel>
          {
              readonly AdornerProvider _provider;
              internal AdornerPanelCollection(AdornerProvider provider) { _provider = provider; }
              protected override void InsertItem(int index, AdornerPanel item) { base.InsertItem(index, item); _provider.OnAdornerAdd(item); }
              protected override void RemoveItem(int index) { _provider.OnAdornerRemove(base[index]); base.RemoveItem(index); }
              protected override void SetItem(int index, AdornerPanel item) { _provider.OnAdornerRemove(base[index]); base.SetItem(index, item); _provider.OnAdornerAdd(item); }
              protected override void ClearItems() { foreach (var v in this) _provider.OnAdornerRemove(v); base.ClearItems(); }
          }

          readonly AdornerPanelCollection _adorners;
          bool isVisible;

          protected AdornerProvider() { _adorners = new AdornerPanelCollection(this); }

          protected override void OnInitialized()
          {
              base.OnInitialized();
              isVisible = true;
              foreach (var v in _adorners) OnAdornerAdd(v);
          }

          protected override void OnRemove()
          {
              base.OnRemove();
              foreach (var v in _adorners) OnAdornerRemove(v);
              isVisible = false;
          }

          public Collection<AdornerPanel> Adorners { get { return _adorners; } }

          protected void AddAdorner(AdornerPlacement placement, AdornerOrder order, UIElement adorner)
          {
              var p = new AdornerPanel { Order = order };
              AdornerPanel.SetPlacement(adorner, placement);
              p.Children.Add(adorner);
              this.Adorners.Add(p);
          }

          protected void AddAdorners(AdornerPlacement placement, params UIElement[] adorners)
          {
              var p = new AdornerPanel();
              foreach (var adorner in adorners) {
                  AdornerPanel.SetPlacement(adorner, placement);
                  p.Children.Add(adorner);
              }
              this.Adorners.Add(p);
          }

          internal void OnAdornerAdd(AdornerPanel item)
          {
              if (!isVisible) return;
              item.SetAdornedElement(this.ExtendedItem.View, this.ExtendedItem);
              this.Services.GetService<IDesignPanel>().Adorners.Add(item);
          }

          internal void OnAdornerRemove(AdornerPanel item)
          {
              if (!isVisible) return;
              this.Services.GetService<IDesignPanel>().Adorners.Remove(item);
          }
      }
  }
  ```

- [ ] **Step 4: Port `HitTestType.cs`, `DesignPanelHitTestResult.cs`**

  ```csharp
  // src/WinUIDesigner.DesignModel/HitTestType.cs
  namespace WinUIDesigner.DesignModel
  {
      public enum HitTestType
      {
          Default,
          ElementSelection,
      }
  }
  ```
  `DesignPanelHitTestResult.cs` — `VisualHit` retyped `Visual` → `UIElement`
  (`FindElementsInHostCoordinates` returns `UIElement`, not `Visual` —
  verified during spec brainstorming):
  ```csharp
  // src/WinUIDesigner.DesignModel/DesignPanelHitTestResult.cs
  using System;
  using Microsoft.UI.Xaml;
  using WinUIDesigner.DesignModel.Adorners;

  namespace WinUIDesigner.DesignModel
  {
      public struct DesignPanelHitTestResult : IEquatable<DesignPanelHitTestResult>
      {
          public static readonly DesignPanelHitTestResult NoHit = new DesignPanelHitTestResult();

          readonly UIElement _visualHit;
          AdornerPanel _adornerHit;
          DesignItem _modelHit;

          public UIElement VisualHit { get { return _visualHit; } }
          public AdornerPanel AdornerHit { get { return _adornerHit; } set { _adornerHit = value; } }
          public DesignItem ModelHit { get { return _modelHit; } set { _modelHit = value; } }

          public DesignPanelHitTestResult(UIElement visualHit)
          {
              _visualHit = visualHit;
              _adornerHit = null;
              _modelHit = null;
          }

          public override bool Equals(object obj) { return obj is DesignPanelHitTestResult && Equals((DesignPanelHitTestResult)obj); }
          public bool Equals(DesignPanelHitTestResult other) { return _visualHit == other._visualHit && _adornerHit == other._adornerHit && _modelHit == other._modelHit; }
          public override int GetHashCode() { return (_visualHit != null ? _visualHit.GetHashCode() : 0) ^ (_adornerHit != null ? _adornerHit.GetHashCode() : 0) ^ (_modelHit != null ? _modelHit.GetHashCode() : 0); }
          public static bool operator ==(DesignPanelHitTestResult l, DesignPanelHitTestResult r) { return l.Equals(r); }
          public static bool operator !=(DesignPanelHitTestResult l, DesignPanelHitTestResult r) { return !l.Equals(r); }
      }
  }
  ```

- [ ] **Step 5: Port `Tools.cs` — `IInputElement` base and event redeclarations dropped, `CustomHitTestFilterBehavior`/`DesignPanelHitTestFilterCallback` dropped**

  `Cursor` (WPF `System.Windows.Input.Cursor`) →
  `Microsoft.UI.Input.InputCursor` (confirmed present in
  `Microsoft.UI.Xaml.winmd` during spec brainstorming).
  ```csharp
  // src/WinUIDesigner.DesignModel/Tools.cs
  using System;
  using System.Collections.Generic;
  using Windows.Foundation;
  using Microsoft.UI.Input;
  using WinUIDesigner.DesignModel.Adorners;

  namespace WinUIDesigner.DesignModel
  {
      public interface ITool
      {
          InputCursor Cursor { get; }
          void Activate(IDesignPanel designPanel);
          void Deactivate(IDesignPanel designPanel);
      }

      public interface IToolService
      {
          ITool PointerTool { get; }
          ITool CurrentTool { get; set; }
          event EventHandler CurrentToolChanged;
      }

      public interface IDesignPanel
      {
          DesignContext Context { get; }
          bool IsContentHitTestVisible { get; set; }
          bool IsAdornerLayerHitTestVisible { get; set; }
          ICollection<AdornerPanel> Adorners { get; }
          DesignPanelHitTestResult HitTest(Point mousePosition, bool testAdorners, bool testDesignSurface, HitTestType hitTestType);
          void HitTest(Point mousePosition, bool testAdorners, bool testDesignSurface, Predicate<DesignPanelHitTestResult> callback, HitTestType hitTestType);
      }
  }
  ```

- [ ] **Step 6: Modify `Services.cs` — add `SelectionTypes`, `ISelectionService`, `IErrorService`, `ITopLevelWindow`/`ITopLevelWindowService`, `IKeyBindingService`**

  Append to `src/WinUIDesigner.DesignModel/Services.cs`, inside the
  existing `namespace WinUIDesigner.DesignModel { ... }` block (after
  `IComponentPropertyService`):
  ```csharp
  [Flags]
  public enum SelectionTypes
  {
      None = 0,
      Auto = 1,
      Primary = 2,
      Toggle = 4,
      Add = 8,
      Remove = 0x10,
      Replace = 0x20
  }

  public interface ISelectionService
  {
      event EventHandler SelectionChanging;
      event EventHandler<DesignItemCollectionEventArgs> SelectionChanged;
      event EventHandler PrimarySelectionChanging;
      event EventHandler PrimarySelectionChanged;

      bool IsComponentSelected(DesignItem component);
      ICollection<DesignItem> SelectedItems { get; }
      void SetSelectedComponents(ICollection<DesignItem> components);
      void SetSelectedComponents(ICollection<DesignItem> components, SelectionTypes selectionType);
      DesignItem PrimarySelection { get; }
      int SelectionCount { get; }
  }

  public interface IErrorService
  {
      void ShowErrorTooltip(Microsoft.UI.Xaml.FrameworkElement attachTo, Microsoft.UI.Xaml.UIElement errorElement);
  }

  public interface ITopLevelWindow
  {
      void SetOwner(Microsoft.UI.Xaml.Window child);
      void Activate();
  }

  public interface ITopLevelWindowService
  {
      ITopLevelWindow GetTopLevelWindow(Microsoft.UI.Xaml.UIElement element);
  }

  public interface IKeyBindingService
  {
      object Owner { get; }
      void RegisterBinding(Microsoft.UI.Xaml.Input.KeyboardAccelerator binding);
      void DeregisterBinding(Microsoft.UI.Xaml.Input.KeyboardAccelerator binding);
      Microsoft.UI.Xaml.Input.KeyboardAccelerator GetBinding(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers);
  }
  ```
  (`IKeyBindingService`'s WPF `KeyBinding`/`KeyGesture` params become
  `Microsoft.UI.Xaml.Input.KeyboardAccelerator` — confirmed present in the
  winmd — plus an explicit `(VirtualKey, VirtualKeyModifiers)` pair for
  `GetBinding`, since WinUI has no separate gesture-only type the way WPF's
  `KeyGesture` is; `KeyboardAccelerator` itself already carries `Key` +
  `Modifiers`. This interface has zero consumers in this phase — a
  reasonable mechanical mapping, not load-bearing.)

- [ ] **Step 7: Port `Services/ICopyPasteService.cs`, `Services/ISelectionFilterService.cs`**

  ```csharp
  // src/WinUIDesigner.DesignModel/Services/ICopyPasteService.cs
  using WinUIDesigner.DesignModel;

  namespace WinUIDesigner.DesignModel.Services
  {
      public interface ICopyPasteService
      {
          bool CanCopy(DesignContext designContext);
          void Copy(DesignContext designContext);
          bool CanPaste(DesignContext designContext);
          void Paste(DesignContext designContext);
          bool CanCut(DesignContext designContext);
          void Cut(DesignContext designContext);
          bool CanDelete(DesignContext designContext);
          void Delete(DesignContext designContext);
      }
  }
  ```
  ```csharp
  // src/WinUIDesigner.DesignModel/Services/ISelectionFilterService.cs
  using System.Collections.Generic;
  using WinUIDesigner.DesignModel;

  namespace WinUIDesigner.DesignModel.Services
  {
      public interface ISelectionFilterService
      {
          ICollection<DesignItem> FilterSelectedElements(ICollection<DesignItem> items);
      }
  }
  ```

- [ ] **Step 8: Port `Extensions/SelectionExtensionServers.cs` — `PrimarySelectionExtensionServer` only**

  ```csharp
  // src/WinUIDesigner.DesignModel/Extensions/SelectionExtensionServers.cs
  using System;

  namespace WinUIDesigner.DesignModel.Extensions
  {
      public class PrimarySelectionExtensionServer : DefaultExtensionServer
      {
          DesignItem oldPrimarySelection;

          protected override void OnInitialized()
          {
              base.OnInitialized();
              this.Services.Selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
          }

          void OnPrimarySelectionChanged(object sender, EventArgs e)
          {
              DesignItem newPrimarySelection = this.Services.Selection.PrimarySelection;
              if (oldPrimarySelection != newPrimarySelection) {
                  if (oldPrimarySelection == null) {
                      ReapplyExtensions(new DesignItem[] { newPrimarySelection });
                  } else if (newPrimarySelection == null) {
                      ReapplyExtensions(new DesignItem[] { oldPrimarySelection });
                  } else {
                      ReapplyExtensions(new DesignItem[] { oldPrimarySelection, newPrimarySelection });
                  }
                  oldPrimarySelection = newPrimarySelection;
              }
          }

          public override bool ShouldApplyExtensions(DesignItem extendedItem)
          {
              return Services.Selection.PrimarySelection == extendedItem;
          }
      }
  }
  ```
  (`DesignItem`/`ServiceContainer.Selection` are in the parent
  `WinUIDesigner.DesignModel` namespace — already visible here without an
  extra `using` because `WinUIDesigner.DesignModel.Extensions`'s sibling
  files, e.g. `ExtensionServer.cs`, already reference `DesignContext`/
  `DesignItem` the same way; confirm this still compiles in Step 10 and add
  `using WinUIDesigner.DesignModel;` if it doesn't — C#'s nested-namespace
  visibility for unqualified names depends on the file's own `using`
  directives, not automatically on namespace nesting.)

- [ ] **Step 9: Modify `ServiceContainer.cs` — add `.Selection`**

  In `src/WinUIDesigner.DesignModel/ServiceContainer.cs`, add after the
  existing `ExtensionManager` property (same file read in this task's
  planning — currently ends at the `ExtensionManager` property before the
  closing braces):
  ```csharp
  /// <summary>
  /// Gets the <see cref="ISelectionService"/>.
  /// Throws an exception if the service is not found.
  /// </summary>
  public ISelectionService Selection {
      get {
          return GetRequiredService<ISelectionService>();
      }
  }
  ```

- [ ] **Step 10: Write and run the framework tests**

  `tests/WinUIDesigner.DesignModel.Tests/AdornerPanelTests.cs`:
  ```csharp
  using NUnit.Framework;
  using WinUIDesigner.DesignModel.Adorners;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.DesignModel.Tests
  {
      [TestFixture]
      public class AdornerPanelTests
      {
          [Test]
          public void PlacementDefaultsToFillContent()
          {
              WinUITestHost.Run(() => {
                  var element = new Microsoft.UI.Xaml.Shapes.Rectangle();
                  Assert.AreSame(AdornerPlacement.FillContent, AdornerPanel.GetPlacement(element));
              });
          }

          [Test]
          public void SetPlacementRoundTrips()
          {
              WinUITestHost.Run(() => {
                  var element = new Microsoft.UI.Xaml.Shapes.Rectangle();
                  AdornerPanel.SetPlacement(element, AdornerPlacement.FillContent);
                  Assert.AreSame(AdornerPlacement.FillContent, AdornerPanel.GetPlacement(element));
              });
          }

          [Test]
          public void AdornerOrderComparesByDeclaredSeverity()
          {
              Assert.Less(AdornerOrder.Background, AdornerOrder.Content);
              Assert.Less(AdornerOrder.Content, AdornerOrder.BehindForeground);
              Assert.Less(AdornerOrder.BehindForeground, AdornerOrder.Foreground);
              Assert.Less(AdornerOrder.Foreground, AdornerOrder.BeforeForeground);
          }

          [Test]
          public void SetAdornedElementCannotBeCalledTwice()
          {
              WinUITestHost.Run(() => {
                  var panel = new AdornerPanel();
                  panel.SetAdornedElement(new Microsoft.UI.Xaml.Shapes.Rectangle(), null);
                  Assert.Throws<System.InvalidOperationException>(() =>
                      panel.SetAdornedElement(new Microsoft.UI.Xaml.Shapes.Rectangle(), null));
              });
          }
      }
  }
  ```
  `tests/WinUIDesigner.DesignModel.Tests/SelectionAndAdornerIntegrationTests.cs`
  — exercises `AdornerProvider` and `PrimarySelectionExtensionServer`
  together through the real `ExtensionManager`/`RegisterAssembly` pipeline
  (not reflection into `internal` members), using a minimal fake
  `ISelectionService` since the real `DefaultSelectionService` doesn't
  exist until Task 4:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Xml;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel.Adorners;
  using WinUIDesigner.DesignModel.Extensions;
  using WinUIDesigner.DesignModel.Xaml;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.DesignModel.Tests
  {
      [TestFixture]
      public class SelectionAndAdornerIntegrationTests
      {
          sealed class FakeSelectionService : ISelectionService
          {
              public event EventHandler SelectionChanging;
              public event EventHandler<DesignItemCollectionEventArgs> SelectionChanged;
              public event EventHandler PrimarySelectionChanging;
              public event EventHandler PrimarySelectionChanged;

              List<DesignItem> _selected = new List<DesignItem>();
              public DesignItem PrimarySelection { get; private set; }
              public ICollection<DesignItem> SelectedItems { get { return _selected; } }
              public int SelectionCount { get { return _selected.Count; } }
              public bool IsComponentSelected(DesignItem component) { return _selected.Contains(component); }
              public void SetSelectedComponents(ICollection<DesignItem> components) { SetSelectedComponents(components, SelectionTypes.Replace); }

              public void SetSelectedComponents(ICollection<DesignItem> components, SelectionTypes selectionType)
              {
                  _selected = components != null ? components.ToList() : new List<DesignItem>();
                  var newPrimary = _selected.FirstOrDefault();
                  if (newPrimary != PrimarySelection) {
                      PrimarySelection = newPrimary;
                      if (PrimarySelectionChanged != null) PrimarySelectionChanged(this, EventArgs.Empty);
                  }
                  if (SelectionChanged != null) SelectionChanged(this, new DesignItemCollectionEventArgs(_selected));
              }
          }

          [ExtensionFor(typeof(Microsoft.UI.Xaml.UIElement))]
          [ExtensionServer(typeof(PrimarySelectionExtensionServer))]
          public sealed class TestSelectionAdorner : AdornerProvider
          {
              protected override void OnInitialized()
              {
                  base.OnInitialized();
                  AddAdorner(AdornerPlacement.FillContent, AdornerOrder.Foreground, new Microsoft.UI.Xaml.Shapes.Rectangle());
              }
          }

          sealed class FakeDesignPanel : WinUIDesigner.DesignModel.IDesignPanel
          {
              public DesignContext Context { get; set; }
              public bool IsContentHitTestVisible { get; set; }
              public bool IsAdornerLayerHitTestVisible { get; set; }
              public ICollection<AdornerPanel> Adorners { get; } = new List<AdornerPanel>();
              public DesignPanelHitTestResult HitTest(Windows.Foundation.Point p, bool a, bool b, HitTestType t) { return DesignPanelHitTestResult.NoHit; }
              public void HitTest(Windows.Foundation.Point p, bool a, bool b, Predicate<DesignPanelHitTestResult> c, HitTestType t) { }
          }

          [Test]
          public void SelectingAnItemAppliesTheAdornerAndDeselectingRemovesIt()
          {
              WinUITestHost.Run(() => {
                  const string xaml = "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                      "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                      "<Button x:Name=\"A\" />" +
                      "<Button x:Name=\"B\" />" +
                      "</Grid>";

                  var loadSettings = new XamlLoadSettings();
                  loadSettings.DesignerAssemblies.Add(typeof(SelectionAndAdornerIntegrationTests).Assembly);
                  var fakePanel = new FakeDesignPanel();
                  var fakeSelection = new FakeSelectionService();
                  loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), fakePanel));
                  loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(ISelectionService), fakeSelection));

                  using (var reader = XmlReader.Create(new StringReader(xaml)))
                  {
                      var context = new XamlDesignContext(reader, loadSettings);
                      fakePanel.Context = context;

                      var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                      var buttonA = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                      var buttonB = (Microsoft.UI.Xaml.Controls.Button)grid.Children[1];
                      var itemA = context.Services.Component.GetDesignItem(buttonA);
                      var itemB = context.Services.Component.GetDesignItem(buttonB);

                      Assert.AreEqual(0, fakePanel.Adorners.Count, "nothing selected yet");

                      fakeSelection.SetSelectedComponents(new[] { itemA }, SelectionTypes.Primary);
                      Assert.AreEqual(1, fakePanel.Adorners.Count, "selecting A should apply one adorner");
                      Assert.AreSame(buttonA, fakePanel.Adorners.First().AdornedElement);

                      fakeSelection.SetSelectedComponents(new[] { itemB }, SelectionTypes.Primary);
                      Assert.AreEqual(1, fakePanel.Adorners.Count, "selecting B should remove A's adorner and apply one for B");
                      Assert.AreSame(buttonB, fakePanel.Adorners.First().AdornedElement);

                      fakeSelection.SetSelectedComponents(null, SelectionTypes.Replace);
                      Assert.AreEqual(0, fakePanel.Adorners.Count, "clearing the selection should remove the adorner");
                  }
              });
          }
      }
  }
  ```
  Run:
  ```bash
  dotnet build WinUIDesigner.sln
  tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
  ```
  Expected: builds clean, all new tests pass. If Step 8's namespace-
  visibility assumption was wrong, fix the `using` directives now — this is
  exactly the kind of thing a clean build either confirms or refutes, not
  something to leave as a guess.

- [ ] **Step 11: Commit**

  ```bash
  git add src/WinUIDesigner.DesignModel tests/WinUIDesigner.DesignModel.Tests
  git commit -m "feat: add overlay/hit-test/selection abstractions to DesignModel (Phase 3 Task 2)"
  ```

---

### Task 3: Concrete overlay host (`AdornerLayer`) + `DesignPanel` + hit-testing

The riskiest task: the concrete `AdornerLayer` (`LayoutUpdated`-driven
repositioning) and `DesignPanel` (hit-testing, coordinate-space
conversion, pointer-driven selection resolution) — the two mechanisms the
spec flagged as this phase's hardest technical risks. Uses Task 1's
`RunWithWindow` for every test that needs real layout (all of them).

**Files:**
- Create: `src/WinUIDesigner.Designer/Controls/AdornerLayer.cs`
- Create: `src/WinUIDesigner.Designer/DesignPanel.cs`
- Create: `tests/WinUIDesigner.Designer.Tests/AdornerLayerTests.cs`
- Create: `tests/WinUIDesigner.Designer.Tests/HitTestTests.cs`

**Interfaces:**
- Consumes: Task 2's `AdornerPanel`/`AdornerOrder`/`IAdornerLayer`/
  `IDesignPanel`/`HitTestType`/`DesignPanelHitTestResult`/`SelectionTypes`,
  Phase 2's `DesignContext`/`DesignItem`/`IComponentService`, Task 1's
  `WinUITestHost.RunWithWindow`.
- Produces: `WinUIDesigner.Designer.Controls.AdornerLayer` (internal
  constructor — only `DesignPanel` creates one, matching the reference's
  own access restriction), `WinUIDesigner.Designer.DesignPanel : Panel,
  IDesignPanel` with a public `Content` property, `HitTest` (both
  overloads), `Adorners`, `IsContentHitTestVisible`/
  `IsAdornerLayerHitTestVisible`. Task 4's `DesignSurface` hosts a
  `DesignPanel` and sets its `Content`/`Context`.

#### Known drops from this task's reference files (verified, not guessed)

- **`DesignPanel.DesignSurface`** (a back-reference property the reference
  sets from its `DesignSurface` constructor) is dropped — nothing in this
  task's own scope reads it, and adding it here would forward-reference
  `WinUIDesigner.Designer.DesignSurface`, a type Task 4 creates. If Task 4
  needs it, add it there.
- **`DesignPanel`'s `_eatAllHitTestRequests` trick, arrow-key nudge, Alt-
  cycle hit-test bookkeeping (`hitTestElements`/`skippedHitTestElements`,
  driven by `Keyboard.IsKeyDown(Key.LeftAlt)`)** — the spec's "Explicitly
  out of scope" already drops the first two. The Alt-cycle bookkeeping is
  an **additional drop found while writing this task**, not anticipated by
  the spec: it depends on `Keyboard.IsKeyDown`, a global keyboard-state
  read in the same unavailable-in-WinUI category as `Keyboard.Modifiers`
  (spec's "Decisions already made"), but unlike the modifier read there is
  no single call site to thread it from (`HitTest` is a general-purpose
  method, not only called from a click handler) and this phase's own
  Definition of Done never exercises cycling through overlapping elements.
  Dropped, not stubbed: no `hitTestElements`/`skippedHitTestElements`
  fields, no Alt check, `HitTest` runs a plain single pass per call.
- **`FilterHitTestInvisibleElements`'s `designItem.IsDesignTimeLocked`
  check** is dropped — `DesignItem` (Phase 2) has no such property (Phase 2
  never ported it; it exists in the reference to support
  `ISelectionFilterService`, which this phase declares but never
  implements). No enforcement mechanism exists in this phase's scope to
  make the check meaningful.
- **`Content` (this task's own, custom property on `DesignPanel`) is not a
  `Border`-wrapped `_sceneContainer`** the way the reference's `DesignPanel.Child`
  is — `_sceneContainer`'s only job in the reference is `TextFormattingMode`/
  `UseLayoutRounding` rendering hints, both WPF-specific concerns absent
  from WinUI's rendering model. `DesignPanel.Content` is set directly to
  `DesignItem.View`.

- [ ] **Step 1: Port `Controls/AdornerLayer.cs` — `IsDescendantOf`, `TransformToAncestor`, `GeneralTransformGroup` fixes**

  ```csharp
  // src/WinUIDesigner.Designer/Controls/AdornerLayer.cs
  using System;
  using System.Collections.Generic;
  using Windows.Foundation;
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Controls;
  using Microsoft.UI.Xaml.Media;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Adorners;

  namespace WinUIDesigner.Designer.Controls
  {
      public sealed class AdornerLayer : Panel, IAdornerLayer
      {
          internal sealed class AdornerPanelCollection : ICollection<AdornerPanel>
          {
              readonly AdornerLayer _layer;
              public AdornerPanelCollection(AdornerLayer layer) { _layer = layer; }
              public int Count { get { return _layer.Children.Count; } }
              public bool IsReadOnly { get { return false; } }
              public void Add(AdornerPanel item) { if (item == null) throw new ArgumentNullException("item"); _layer.AddAdorner(item); }
              public void Clear() { _layer.ClearAdorners(); }
              public bool Contains(AdornerPanel item) { if (item == null) throw new ArgumentNullException("item"); return VisualTreeHelper.GetParent(item) == _layer; }
              public void CopyTo(AdornerPanel[] array, int arrayIndex) { foreach (var panel in this) array[arrayIndex++] = panel; }
              public bool Remove(AdornerPanel item) { if (item == null) throw new ArgumentNullException("item"); return _layer.RemoveAdorner(item); }
              public IEnumerator<AdornerPanel> GetEnumerator() { foreach (AdornerPanel panel in _layer.Children) yield return panel; }
              System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
          }

          readonly AdornerPanelCollection _adorners;
          readonly UIElement _designPanel;

          internal AdornerLayer(UIElement designPanel)
          {
              _designPanel = designPanel;
              // FrameworkElement.LayoutUpdated's delegate type in WinUI 3 is EventHandler<object>,
              // not EventHandler (WPF) - confirm against the actual signature at build time; this is
              // a copy-compile-fix detail, not a design decision.
              LayoutUpdated += OnLayoutUpdated;
              _adorners = new AdornerPanelCollection(this);
          }

          void OnLayoutUpdated(object sender, object e) { UpdateAllAdorners(false); }

          internal AdornerPanelCollection Adorners { get { return _adorners; } }

          sealed class AdornerInfo
          {
              internal readonly List<AdornerPanel> adorners = new List<AdornerPanel>();
              internal bool isVisible;
              internal Rect position;
          }

          Dictionary<UIElement, AdornerInfo> _dict = new Dictionary<UIElement, AdornerInfo>();

          void ClearAdorners()
          {
              if (_dict.Count == 0) return;
              this.Children.Clear();
              _dict = new Dictionary<UIElement, AdornerInfo>();
          }

          AdornerInfo GetOrCreateAdornerInfo(UIElement adornedElement)
          {
              AdornerInfo info;
              if (!_dict.TryGetValue(adornedElement, out info)) {
                  info = _dict[adornedElement] = new AdornerInfo();
                  info.isVisible = IsDescendantOf(adornedElement, _designPanel);
              }
              return info;
          }

          AdornerInfo GetExistingAdornerInfo(UIElement adornedElement)
          {
              AdornerInfo info;
              _dict.TryGetValue(adornedElement, out info);
              return info;
          }

          void AddAdorner(AdornerPanel adornerPanel)
          {
              if (adornerPanel.AdornedElement == null)
                  throw new DesignerException("adornerPanel.AdornedElement must be set");

              AdornerInfo info = GetOrCreateAdornerInfo(adornerPanel.AdornedElement);
              info.adorners.Add(adornerPanel);
              if (info.isVisible) AddAdornerToChildren(adornerPanel);
          }

          void AddAdornerToChildren(AdornerPanel adornerPanel)
          {
              int i;
              for (i = 0; i < Children.Count; i++) {
                  var p = (AdornerPanel)Children[i];
                  if (p.Order > adornerPanel.Order) break;
              }
              Children.Insert(i, adornerPanel);
          }

          protected override Size MeasureOverride(Size availableSize)
          {
              var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
              foreach (AdornerPanel adorner in Children) adorner.Measure(infinite);
              return new Size(0, 0);
          }

          protected override Size ArrangeOverride(Size finalSize)
          {
              foreach (AdornerPanel adorner in Children) {
                  if (IsDescendantOf(adorner.AdornedElement, _designPanel)) {
                      // Reference casts the ancestor-relative transform to MatrixTransform and
                      // assigns it directly, preserving rotation/scale as well as translation - the
                      // spec's own "Explicitly out of scope" already established WinUI's
                      // TransformToVisual only ever needs this MatrixTransform path (no
                      // GeneralTransformGroup exists to fall back to). Kept as a defensive
                      // translation-only fallback in case that assumption is wrong for some element.
                      var transform = adorner.AdornedElement.TransformToVisual(_designPanel);
                      var matrixTransform = transform as MatrixTransform;
                      if (matrixTransform != null) {
                          adorner.RenderTransform = matrixTransform;
                      } else {
                          var origin = transform.TransformPoint(new Point(0, 0));
                          adorner.RenderTransform = new TranslateTransform { X = origin.X, Y = origin.Y };
                      }
                  }
                  adorner.Arrange(new Rect(new Point(0, 0), adorner.DesiredSize));
              }
              return finalSize;
          }

          bool RemoveAdorner(AdornerPanel adornerPanel)
          {
              if (adornerPanel.AdornedElement == null) return false;
              AdornerInfo info = GetExistingAdornerInfo(adornerPanel.AdornedElement);
              if (info == null) return false;
              if (info.adorners.Remove(adornerPanel)) {
                  if (info.isVisible) this.Children.Remove(adornerPanel);
                  if (info.adorners.Count == 0) _dict.Remove(adornerPanel.AdornedElement);
                  return true;
              }
              return false;
          }

          public void UpdateAdornersForElement(UIElement element, bool forceInvalidate)
          {
              AdornerInfo info = GetExistingAdornerInfo(element);
              if (info != null) UpdateAdornersForElement(element, info, forceInvalidate);
          }

          Rect GetPositionCache(UIElement element)
          {
              var t = element.TransformToVisual(_designPanel);
              var p = t.TransformPoint(new Point(0, 0));
              return new Rect(p, element.RenderSize);
          }

          void UpdateAdornersForElement(UIElement element, AdornerInfo info, bool forceInvalidate)
          {
              if (IsDescendantOf(element, _designPanel)) {
                  if (!info.isVisible) {
                      info.isVisible = true;
                      info.adorners.ForEach(AddAdornerToChildren);
                  }
                  Rect c = GetPositionCache(element);
                  if (forceInvalidate || !info.position.Equals(c)) {
                      info.position = c;
                      foreach (var p in info.adorners) p.InvalidateMeasure();
                      this.InvalidateArrange();
                  }
              } else {
                  if (info.isVisible) {
                      info.isVisible = false;
                      foreach (var p in info.adorners) this.Children.Remove(p);
                  }
              }
          }

          void UpdateAllAdorners(bool forceInvalidate)
          {
              foreach (var pair in _dict) UpdateAdornersForElement(pair.Key, pair.Value, forceInvalidate);
          }

          // Known absent (spec, verified against Microsoft.UI.Xaml.winmd): IsDescendantOf /
          // GetIsAncestorOf. Manual ancestor walk instead.
          static bool IsDescendantOf(UIElement element, UIElement ancestor)
          {
              DependencyObject current = element;
              while (current != null) {
                  if (current == ancestor) return true;
                  current = VisualTreeHelper.GetParent(current);
              }
              return false;
          }
      }
  }
  ```

- [ ] **Step 2: Write and run `AdornerLayerTests.cs`**

  `tests/WinUIDesigner.Designer.Tests/AdornerLayerTests.cs` — tests go
  through `DesignPanel`'s public surface, not `AdornerLayer` directly: its
  constructor is `internal` (matching the reference's own restriction —
  only `DesignPanel` should create one), unreachable from this separate
  test assembly.
  ```csharp
  using Microsoft.UI.Xaml.Controls;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel.Adorners;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.Designer.Tests
  {
      [TestFixture]
      public class AdornerLayerTests
      {
          [Test]
          public void AdornerBecomesVisibleAndPositionedWhenItsElementIsInTheDesignTree()
          {
              WinUITestHost.RunWithWindow(window => {
                  var panel = new DesignPanel();
                  var button = new Button { Width = 40, Height = 20 };
                  var grid = new Grid { Width = 300, Height = 300 };
                  grid.Children.Add(button);
                  panel.Content = grid;
                  window.Content = panel;
                  panel.UpdateLayout();

                  var adorner = new AdornerPanel();
                  adorner.SetAdornedElement(button, null);
                  panel.Adorners.Add(adorner);
                  panel.UpdateLayout();

                  Assert.IsTrue(panel.Adorners.Contains(adorner),
                      "adorner should be visible: its adorned element is in the design tree");
                  Assert.IsNotNull(adorner.RenderTransform,
                      "AdornerLayer should have positioned the adorner via RenderTransform");
              });
          }

          [Test]
          public void AdornerDisappearsWhenItsElementLeavesTheDesignTree()
          {
              WinUITestHost.RunWithWindow(window => {
                  var panel = new DesignPanel();
                  var button = new Button { Width = 40, Height = 20 };
                  var grid = new Grid();
                  grid.Children.Add(button);
                  panel.Content = grid;
                  window.Content = panel;
                  panel.UpdateLayout();

                  var adorner = new AdornerPanel();
                  adorner.SetAdornedElement(button, null);
                  panel.Adorners.Add(adorner);
                  panel.UpdateLayout();
                  Assert.IsTrue(panel.Adorners.Contains(adorner));

                  grid.Children.Remove(button);
                  panel.UpdateLayout();

                  Assert.IsFalse(panel.Adorners.Contains(adorner),
                      "adorner should become invisible once its adorned element leaves the design " +
                      "tree - exercises the IsDescendantOf replacement (mechanism table, spec)");
              });
          }

          [Test]
          public void UIElementUsesReferenceIdentityEqualityByDefault()
          {
              // Known gap #3 (spec): AdornerLayer's Dictionary<UIElement, AdornerInfo> relies on
              // UIElement not overriding Equals/GetHashCode, so the dictionary's default comparer
              // already uses reference identity - no custom IEqualityComparer needed. This test is
              // the empirical check, not an assumption: two distinct instances of the same UIElement
              // subclass must compare unequal, and an instance must equal itself.
              WinUITestHost.Run(() => {
                  var a = new Microsoft.UI.Xaml.Shapes.Rectangle();
                  var b = new Microsoft.UI.Xaml.Shapes.Rectangle();
                  Assert.IsFalse(a.Equals(b), "two distinct UIElement instances should not be equal");
                  Assert.IsTrue(a.Equals(a), "an element should equal itself");
              });
          }
      }
  }
  ```
  Run:
  ```bash
  dotnet build WinUIDesigner.sln
  tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
  ```
  Expected: build fails until Step 3 below adds `DesignPanel` — that's
  expected at this point in the task; continue to Step 3, then re-run both
  Step 2's and Step 4's tests together.

- [ ] **Step 3: Port `DesignPanel.cs` — coordinate-space conversion, `IsContentHitTestVisible` semantics, pointer-driven selection**

  ```csharp
  // src/WinUIDesigner.Designer/DesignPanel.cs
  using System;
  using System.Collections.Generic;
  using Windows.Foundation;
  using Windows.System;
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Controls;
  using Microsoft.UI.Xaml.Input;
  using Microsoft.UI.Xaml.Media;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Adorners;
  using WinUIDesigner.Designer.Controls;

  namespace WinUIDesigner.Designer
  {
      public sealed class DesignPanel : Panel, IDesignPanel
      {
          readonly AdornerLayer _adornerLayer;
          UIElement _content;

          public DesignPanel()
          {
              _adornerLayer = new AdornerLayer(this);
              Children.Add(_adornerLayer);
              this.PointerPressed += OnPointerPressed;
              IsContentHitTestVisible = true;
          }

          public UIElement Content {
              get { return _content; }
              set {
                  if (_content == value) return;
                  if (_content != null) Children.Remove(_content);
                  _content = value;
                  if (_content != null) Children.Insert(0, _content);
              }
          }

          public DesignContext Context { get; set; }
          public bool IsContentHitTestVisible { get; set; }

          public bool IsAdornerLayerHitTestVisible {
              get { return _adornerLayer.IsHitTestVisible; }
              set { _adornerLayer.IsHitTestVisible = value; }
          }

          public ICollection<AdornerPanel> Adorners { get { return _adornerLayer.Adorners; } }

          protected override Size MeasureOverride(Size availableSize)
          {
              if (_content != null) _content.Measure(availableSize);
              _adornerLayer.Measure(availableSize);
              return _content != null ? _content.DesiredSize : new Size(0, 0);
          }

          protected override Size ArrangeOverride(Size finalSize)
          {
              var r = new Rect(new Point(0, 0), finalSize);
              if (_content != null) _content.Arrange(r);
              _adornerLayer.Arrange(r);
              return finalSize;
          }

          public DesignPanelHitTestResult HitTest(Point mousePosition, bool testAdorners, bool testDesignSurface, HitTestType hitTestType)
          {
              DesignPanelHitTestResult result = DesignPanelHitTestResult.NoHit;
              HitTest(mousePosition, testAdorners, testDesignSurface, r => { result = r; return false; }, hitTestType);
              return result;
          }

          public void HitTest(Point mousePosition, bool testAdorners, bool testDesignSurface, Predicate<DesignPanelHitTestResult> callback, HitTestType hitTestType)
          {
              if (mousePosition.X < 0 || mousePosition.Y < 0 || mousePosition.X > this.ActualWidth || mousePosition.Y > this.ActualHeight)
                  return;
              if (this.XamlRoot == null)
                  return;

              // FindElementsInHostCoordinates needs a point in host/XamlRoot coordinates, not the
              // panel-local point the reference's HitTest takes (verified during spec brainstorming).
              var hostPoint = this.TransformToVisual(this.XamlRoot.Content).TransformPoint(mousePosition);
              bool continueHitTest = true;

              if (testAdorners) {
                  foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(hostPoint, _adornerLayer)) {
                      if (!continueHitTest) break;
                      var customResult = new DesignPanelHitTestResult(element);
                      DependencyObject obj = element;
                      while (obj != null && obj != _adornerLayer) {
                          var adorner = obj as AdornerPanel;
                          if (adorner != null) customResult.AdornerHit = adorner;
                          obj = VisualTreeHelper.GetParent(obj);
                      }
                      continueHitTest = callback(customResult);
                  }
              }

              // IsContentHitTestVisible: skip the content pass entirely instead of the reference's
              // _eatAllHitTestRequests element-swap trick (dropped - see "Known drops").
              if (continueHitTest && testDesignSurface && IsContentHitTestVisible && _content != null) {
                  foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(hostPoint, _content)) {
                      if (!continueHitTest) break;
                      var customResult = new DesignPanelHitTestResult(element);
                      DependencyObject obj = element;
                      DesignItem modelHit = null;
                      while (obj != null) {
                          var el = obj as UIElement;
                          if (el != null && Context != null) {
                              modelHit = Context.Services.Component.GetDesignItem(el, true);
                              if (modelHit != null) break;
                          }
                          obj = VisualTreeHelper.GetParent(obj);
                      }
                      customResult.ModelHit = modelHit ?? (Context != null ? Context.RootItem : null);
                      continueHitTest = callback(customResult);
                  }
              }
          }

          void OnPointerPressed(object sender, PointerRoutedEventArgs e)
          {
              if (Context == null) return;
              var point = e.GetCurrentPoint(this).Position;
              var result = HitTest(point, true, true, HitTestType.ElementSelection);
              if (result.ModelHit == null) return;

              // Mirrors the reference's SelectionTypes.Auto resolution (Control -> Toggle,
              // Shift -> Add, else -> Primary) - read from the triggering pointer event instead of
              // a global keyboard-state query (spec's "Decisions already made").
              var modifiers = e.KeyModifiers;
              SelectionTypes selectionType;
              if ((modifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control)
                  selectionType = SelectionTypes.Toggle;
              else if ((modifiers & VirtualKeyModifiers.Shift) == VirtualKeyModifiers.Shift)
                  selectionType = SelectionTypes.Add;
              else
                  selectionType = SelectionTypes.Primary;

              Context.Services.Selection.SetSelectedComponents(new DesignItem[] { result.ModelHit }, selectionType);
          }
      }
  }
  ```

- [ ] **Step 4: Write and run `HitTestTests.cs`**

  `tests/WinUIDesigner.Designer.Tests/HitTestTests.cs`:
  ```csharp
  using System.IO;
  using System.Xml;
  using Microsoft.UI.Xaml.Controls;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Xaml;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.Designer.Tests
  {
      [TestFixture]
      public class HitTestTests
      {
          const string TwoButtonsXaml =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"200\">" +
              "<Button x:Name=\"Left\" Width=\"80\" Height=\"80\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
              "<Button x:Name=\"Right\" Width=\"80\" Height=\"80\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Top\" />" +
              "</Grid>";

          [Test]
          public void HitTestReturnsTheDesignItemAtAGivenPoint()
          {
              WinUITestHost.RunWithWindow(window => {
                  using (var reader = XmlReader.Create(new StringReader(TwoButtonsXaml))) {
                      var context = new XamlDesignContext(reader, new XamlLoadSettings());
                      var panel = new DesignPanel { Context = context, Content = context.RootItem.View };
                      window.Content = panel;
                      panel.UpdateLayout();

                      var grid = (Grid)context.RootItem.Component;
                      var leftButton = (Button)grid.Children[0];
                      var rightButton = (Button)grid.Children[1];
                      var leftItem = context.Services.Component.GetDesignItem(leftButton);
                      var rightItem = context.Services.Component.GetDesignItem(rightButton);

                      var pointInLeftButton = new Windows.Foundation.Point(40, 40);
                      var result = panel.HitTest(pointInLeftButton, true, true, HitTestType.ElementSelection);

                      Assert.AreSame(leftItem, result.ModelHit);
                      Assert.AreNotSame(rightItem, result.ModelHit);
                  }
              });
          }

          [Test]
          public void HitTestOutsidePanelBoundsReturnsNoHit()
          {
              WinUITestHost.RunWithWindow(window => {
                  using (var reader = XmlReader.Create(new StringReader(TwoButtonsXaml))) {
                      var context = new XamlDesignContext(reader, new XamlLoadSettings());
                      var panel = new DesignPanel { Context = context, Content = context.RootItem.View };
                      window.Content = panel;
                      panel.UpdateLayout();

                      var result = panel.HitTest(new Windows.Foundation.Point(-10, -10), true, true, HitTestType.ElementSelection);
                      Assert.AreEqual(DesignPanelHitTestResult.NoHit, result);
                  }
              });
          }

          [Test]
          public void IsContentHitTestVisibleFalseSkipsTheContentPass()
          {
              WinUITestHost.RunWithWindow(window => {
                  using (var reader = XmlReader.Create(new StringReader(TwoButtonsXaml))) {
                      var context = new XamlDesignContext(reader, new XamlLoadSettings());
                      var panel = new DesignPanel { Context = context, Content = context.RootItem.View, IsContentHitTestVisible = false };
                      window.Content = panel;
                      panel.UpdateLayout();

                      var result = panel.HitTest(new Windows.Foundation.Point(40, 40), true, true, HitTestType.ElementSelection);
                      Assert.AreEqual(DesignPanelHitTestResult.NoHit, result);
                  }
              });
          }
      }
  }
  ```
  (No dedicated test for `OnPointerPressed` itself: `PointerRoutedEventArgs`
  has no public constructor to fabricate one, and the spec's own
  Definition of Done deliberately tests selection "with a direct method
  call with a resolved `SelectionTypes`, not a synthesized OS input event"
  — that's Task 5. These tests already cover everything `OnPointerPressed`
  depends on: `HitTest` itself.)

  Run:
  ```bash
  dotnet build WinUIDesigner.sln
  tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
  ```
  Expected: builds clean, all of Step 2's and this step's tests pass.

- [ ] **Step 5: Verify Phase 1/2 are still green, commit**

  ```bash
  tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
  tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
  ```
  ```bash
  git add src/WinUIDesigner.Designer/Controls src/WinUIDesigner.Designer/DesignPanel.cs tests/WinUIDesigner.Designer.Tests
  git commit -m "feat: port concrete AdornerLayer and DesignPanel hit-testing (Phase 3 Task 3)"
  ```

---

### Task 4: `DesignSurface` + selection wiring

Wires Tasks 2/3 into an actual click-to-select pipeline: `DesignSurface`
hosts a `DesignPanel`, registers `IDesignPanel`/`ISelectionService` on the
`DesignContext`, and a `SelectionAdornerProvider` (driven by Task 2's
`PrimarySelectionExtensionServer`) shows one `SelectionHighlight` adorner
on whichever `DesignItem` is currently primary-selected.

**Files:**
- Create: `src/WinUIDesigner.Designer/DesignSurface.cs`
- Create: `src/WinUIDesigner.Designer/Services/SelectionService.cs`
- Create: `src/WinUIDesigner.Designer/Services/SelectionAdornerProvider.cs`
- Create: `src/WinUIDesigner.Designer/Controls/SelectionHighlight.cs`
- Create: `tests/WinUIDesigner.Designer.Tests/DesignSurfaceTests.cs`

**Interfaces:**
- Consumes: Task 3's `DesignPanel`, Task 2's `ISelectionService`/
  `SelectionTypes`/`PrimarySelectionExtensionServer`/`AdornerProvider`/
  `AdornerPlacement`/`AdornerOrder`, Phase 2's `XamlDesignContext`/
  `XamlLoadSettings`/`DesignItem`.
- Produces: `WinUIDesigner.Designer.DesignSurface` (`LoadDesigner`/
  `UnloadDesigner`/`SaveDesigner`/`DesignContext`/`DesignPanel`),
  `WinUIDesigner.Designer.Services.DefaultSelectionService`,
  `SelectionAdornerProvider`, `Controls.SelectionHighlight`. Task 5's DoD
  suite drives the whole pipeline through this surface.

- [ ] **Step 1: Port `DesignSurface.cs` — untemplated, no `ZoomControl`/commands**

  ```csharp
  // src/WinUIDesigner.Designer/DesignSurface.cs
  using System;
  using System.Xml;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Xaml;
  using WinUIDesigner.Designer.Services;

  namespace WinUIDesigner.Designer
  {
      public sealed class DesignSurface : Microsoft.UI.Xaml.Controls.ContentControl
      {
          readonly DesignPanel _designPanel;
          DesignContext _designContext;

          public DesignSurface()
          {
              _designPanel = new DesignPanel();
              Content = _designPanel;
          }

          public DesignPanel DesignPanel { get { return _designPanel; } }
          public DesignContext DesignContext { get { return _designContext; } }

          public void LoadDesigner(XmlReader xamlReader, XamlLoadSettings loadSettings)
          {
              UnloadDesigner();
              loadSettings = loadSettings ?? new XamlLoadSettings();
              loadSettings.DesignerAssemblies.Add(typeof(DesignSurface).Assembly);
              loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(IDesignPanel), _designPanel));
              loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(ISelectionService), new DefaultSelectionService()));
              InitializeDesigner(new XamlDesignContext(xamlReader, loadSettings));
          }

          void InitializeDesigner(DesignContext context)
          {
              _designContext = context;
              _designPanel.Context = context;
              if (context.RootItem != null) {
                  _designPanel.Content = context.RootItem.View;
              }
          }

          public void UnloadDesigner()
          {
              if (_designContext != null) {
                  foreach (object o in _designContext.Services.AllServices) {
                      var d = o as IDisposable;
                      if (d != null) d.Dispose();
                  }
              }
              _designContext = null;
              _designPanel.Context = null;
              _designPanel.Content = null;
              _designPanel.Adorners.Clear();
          }

          public void SaveDesigner(XmlWriter writer)
          {
              _designContext.Save(writer);
          }
      }
  }
  ```
  (`typeof(DesignSurface).Assembly` in `loadSettings.DesignerAssemblies` is
  what makes `ExtensionManager.RegisterAssembly` — Phase 2, called from
  `XamlDesignContext`'s constructor — discover `SelectionAdornerProvider`
  below via its `[ExtensionFor]`/`[ExtensionServer]` attributes.)

- [ ] **Step 2: Port `Services/SelectionService.cs` — `Keyboard.Modifiers` gap resolved via the verified alternative**

  ```csharp
  // src/WinUIDesigner.Designer/Services/SelectionService.cs
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using WinUIDesigner.DesignModel;

  namespace WinUIDesigner.Designer.Services
  {
      public class DefaultSelectionService : ISelectionService
      {
          HashSet<DesignItem> _selectedComponents = new HashSet<DesignItem>();
          DesignItem _primarySelection;

          public bool IsComponentSelected(DesignItem component) { return _selectedComponents.Contains(component); }
          public ICollection<DesignItem> SelectedItems { get { return _selectedComponents.ToArray(); } }
          public DesignItem PrimarySelection { get { return _primarySelection; } }
          public int SelectionCount { get { return _selectedComponents.Count; } }

          public event EventHandler SelectionChanging;
          public event EventHandler<DesignItemCollectionEventArgs> SelectionChanged;
          public event EventHandler PrimarySelectionChanging;
          public event EventHandler PrimarySelectionChanged;

          public void SetSelectedComponents(ICollection<DesignItem> components)
          {
              SetSelectedComponents(components, SelectionTypes.Replace);
          }

          public virtual void SetSelectedComponents(ICollection<DesignItem> components, SelectionTypes selectionType)
          {
              if (components == null) components = new DesignItem[0];
              if (components.Contains(null)) throw new ArgumentException("Cannot select 'null'.");

              var prevSelectedItems = _selectedComponents.ToArray();
              if (SelectionChanging != null) SelectionChanging(this, EventArgs.Empty);

              DesignItem newPrimarySelection = _primarySelection;

              if (selectionType == SelectionTypes.Auto) {
                  selectionType = ResolveAutoSelectionType();
              }

              if ((selectionType & SelectionTypes.Primary) == SelectionTypes.Primary) {
                  newPrimarySelection = null;
                  foreach (var obj in components) { newPrimarySelection = obj; break; }

                  selectionType &= ~SelectionTypes.Primary;
                  if (selectionType == 0) {
                      if (components.Count == 1 && IsComponentSelected(newPrimarySelection) && prevSelectedItems.Length == 1) {
                          // keep selectionType = 0 -> don't change the selection
                      } else {
                          selectionType = SelectionTypes.Replace;
                      }
                  }
              }

              var componentsToNotify = new HashSet<DesignItem>();
              switch (selectionType) {
                  case SelectionTypes.Add:
                      foreach (var obj in components) if (_selectedComponents.Add(obj)) componentsToNotify.Add(obj);
                      break;
                  case SelectionTypes.Remove:
                      foreach (var obj in components) if (_selectedComponents.Remove(obj)) componentsToNotify.Add(obj);
                      break;
                  case SelectionTypes.Replace:
                      componentsToNotify.UnionWith(_selectedComponents);
                      _selectedComponents.Clear();
                      foreach (var obj in components) { _selectedComponents.Add(obj); componentsToNotify.Add(obj); }
                      break;
                  case SelectionTypes.Toggle:
                      foreach (var obj in components) {
                          if (_selectedComponents.Contains(obj)) _selectedComponents.Remove(obj);
                          else _selectedComponents.Add(obj);
                          componentsToNotify.Add(obj);
                      }
                      break;
                  case 0:
                      break;
                  default:
                      throw new NotSupportedException("The selection type " + selectionType + " is not supported");
              }

              if (!IsComponentSelected(newPrimarySelection)) {
                  newPrimarySelection = null;
                  foreach (var obj in _selectedComponents) { newPrimarySelection = obj; break; }
              }

              if (newPrimarySelection != _primarySelection) {
                  componentsToNotify.Add(_primarySelection);
                  componentsToNotify.Add(newPrimarySelection);
                  if (PrimarySelectionChanging != null) PrimarySelectionChanging(this, EventArgs.Empty);
                  _primarySelection = newPrimarySelection;
                  if (PrimarySelectionChanged != null) PrimarySelectionChanged(this, EventArgs.Empty);
              }

              if (!_selectedComponents.SequenceEqual(prevSelectedItems)) {
                  if (SelectionChanged != null) SelectionChanged(this, new DesignItemCollectionEventArgs(componentsToNotify));
              }
          }

          // Interface completeness for SelectionTypes.Auto (spec: DesignPanel's own call path never
          // passes Auto - it resolves modifiers from the triggering pointer event instead, see
          // Task 3 Step 3). Uses the verified global alternative
          // (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread, confirmed present
          // in Microsoft.UI.winmd during spec brainstorming) rather than leaving Auto unimplemented.
          // No try/catch here deliberately (Global Constraints: no swallowed exception that hides a
          // platform difference) - Step 3 below verifies empirically whether this call works in this
          // project's unpackaged-desktop configuration, and if not, this method's body is replaced
          // with a plain `return SelectionTypes.Primary;` plus a comment recording that finding.
          static SelectionTypes ResolveAutoSelectionType()
          {
              var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
              var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
              bool ctrlDown = (ctrl & Microsoft.UI.Input.CoreVirtualKeyStates.Down) == Microsoft.UI.Input.CoreVirtualKeyStates.Down;
              bool shiftDown = (shift & Microsoft.UI.Input.CoreVirtualKeyStates.Down) == Microsoft.UI.Input.CoreVirtualKeyStates.Down;
              if (ctrlDown) return SelectionTypes.Toggle;
              if (shiftDown) return SelectionTypes.Add;
              return SelectionTypes.Primary;
          }
      }
  }
  ```

- [ ] **Step 3: Write and run `DefaultSelectionServiceTests.cs`, empirically resolve `ResolveAutoSelectionType`**

  `tests/WinUIDesigner.Designer.Tests/DefaultSelectionServiceTests.cs`:
  ```csharp
  using System.Linq;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.Designer.Services;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.Designer.Tests
  {
      [TestFixture]
      public class DefaultSelectionServiceTests
      {
          // No DesignItem instance is needed here - HashSet<DesignItem> only needs distinct
          // references, and DesignItem is abstract - a bare object cast is enough to exercise the
          // selection bookkeeping in isolation from Phase 2's concrete XamlDesignItem.
          static DesignItem[] Items(int count)
          {
              var items = new DesignItem[count];
              for (int i = 0; i < count; i++) items[i] = null; // replaced by real DesignItems in Task 5's DoD suite
              return items;
          }

          [Test]
          public void AutoWithNoModifiersHeldResolvesToPrimarySelectionBehavior()
          {
              WinUITestHost.RunWithWindow(window => {
                  // This is the one thing this test CAN verify deterministically: in a test run with
                  // no physical modifier key actually held, Auto should behave like Primary (select
                  // the one item, make it primary) rather than throwing.
                  var service = new DefaultSelectionService();
                  Assert.DoesNotThrow(() => service.SetSelectedComponents(new DesignItem[0], SelectionTypes.Auto));
              });
          }
      }
  }
  ```
  Run:
  ```bash
  dotnet build WinUIDesigner.sln
  tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
  ```

  **If it passes:** `InputKeyboardSource.GetKeyStateForCurrentThread` works
  in this project's unpackaged-desktop configuration. Leave
  `ResolveAutoSelectionType` as written; add a one-line comment recording
  the empirical confirmation.

  **If it throws** (e.g. an API requiring packaged identity): replace
  `ResolveAutoSelectionType`'s body with:
  ```csharp
  static SelectionTypes ResolveAutoSelectionType()
  {
      // Empirically confirmed (Phase 3, Task 4): InputKeyboardSource.GetKeyStateForCurrentThread
      // throws in this project's unpackaged-desktop (WindowsPackageType=None) configuration.
      // DesignPanel's own call path never passes Auto anyway (see Task 3) - this is dead-except-
      // for-interface-completeness, so default to the common case rather than leave it broken.
      return SelectionTypes.Primary;
  }
  ```
  and remove the now-unused `Windows.System`/`Microsoft.UI.Input` usings.
  Re-run the test either way — it must pass before continuing.

- [ ] **Step 4: Port `Services/SelectionAdornerProvider.cs`, `Controls/SelectionHighlight.cs`**

  ```csharp
  // src/WinUIDesigner.Designer/Controls/SelectionHighlight.cs
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Controls;
  using Microsoft.UI.Xaml.Media;

  namespace WinUIDesigner.Designer.Controls
  {
      /// <summary>
      /// Minimal highlight adorner for the current primary selection - not a port of the
      /// reference's Controls/SelectionFrame.cs, which carries resize-thumb layout Phase 4 doesn't
      /// need yet (spec).
      /// </summary>
      public sealed class SelectionHighlight : Border
      {
          public SelectionHighlight()
          {
              BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Blue);
              BorderThickness = new Thickness(2);
              IsHitTestVisible = false;
          }
      }
  }
  ```
  ```csharp
  // src/WinUIDesigner.Designer/Services/SelectionAdornerProvider.cs
  using WinUIDesigner.DesignModel.Adorners;
  using WinUIDesigner.DesignModel.Extensions;
  using WinUIDesigner.Designer.Controls;

  namespace WinUIDesigner.Designer.Services
  {
      [ExtensionFor(typeof(Microsoft.UI.Xaml.UIElement))]
      [ExtensionServer(typeof(PrimarySelectionExtensionServer))]
      public sealed class SelectionAdornerProvider : AdornerProvider
      {
          protected override void OnInitialized()
          {
              base.OnInitialized();
              AddAdorner(AdornerPlacement.FillContent, AdornerOrder.Foreground, new SelectionHighlight());
          }
      }
  }
  ```

- [ ] **Step 5: Write and run `DesignSurfaceTests.cs`**

  ```csharp
  // tests/WinUIDesigner.Designer.Tests/DesignSurfaceTests.cs
  using System.IO;
  using System.Linq;
  using System.Xml;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Xaml;
  using WinUIDesigner.Designer.Controls;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.Designer.Tests
  {
      [TestFixture]
      public class DesignSurfaceTests
      {
          const string Xaml =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
              "<Button x:Name=\"MyButton\" />" +
              "</Grid>";

          [Test]
          public void LoadDesignerRegistersIDesignPanelAndISelectionService()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = new DesignSurface();
                  window.Content = surface;

                  using (var reader = XmlReader.Create(new StringReader(Xaml)))
                      surface.LoadDesigner(reader, new XamlLoadSettings());

                  Assert.AreSame(surface.DesignPanel, surface.DesignContext.Services.GetService<IDesignPanel>());
                  Assert.IsNotNull(surface.DesignContext.Services.GetService<ISelectionService>());
                  Assert.AreSame(surface.DesignContext.RootItem.View, surface.DesignPanel.Content);
              });
          }

          [Test]
          public void SelectingTheRootItemAppliesExactlyOneSelectionHighlight()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = new DesignSurface();
                  window.Content = surface;

                  using (var reader = XmlReader.Create(new StringReader(Xaml)))
                      surface.LoadDesigner(reader, new XamlLoadSettings());
                  surface.UpdateLayout();

                  surface.DesignContext.Services.Selection.SetSelectedComponents(
                      new DesignItem[] { surface.DesignContext.RootItem }, SelectionTypes.Primary);
                  surface.UpdateLayout();

                  var highlights = surface.DesignPanel.Adorners
                      .SelectMany(a => a.Children)
                      .OfType<SelectionHighlight>()
                      .ToList();
                  Assert.AreEqual(1, highlights.Count);
              });
          }
      }
  }
  ```
  Run:
  ```bash
  dotnet build WinUIDesigner.sln
  tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
  ```
  Expected: builds clean, all tests pass.

- [ ] **Step 6: Verify Phase 1/2 are still green, commit**

  ```bash
  tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
  tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
  ```
  ```bash
  git add src/WinUIDesigner.Designer/DesignSurface.cs src/WinUIDesigner.Designer/Services src/WinUIDesigner.Designer/Controls/SelectionHighlight.cs tests/WinUIDesigner.Designer.Tests
  git commit -m "feat: wire DesignSurface, DefaultSelectionService, selection highlight adorner (Phase 3 Task 4)"
  ```

---

### Task 5: Definition-of-Done test suite

Covers the spec's Definition of Done directly, end to end through
`DesignSurface` (not the narrower per-component tests earlier tasks used),
plus a final check that all three flagged gaps have tested resolutions.

**Files:**
- Create: `tests/WinUIDesigner.Designer.Tests/DefinitionOfDoneTests.cs`

**Interfaces:**
- Consumes: `DesignSurface`/`DesignPanel`/`DefaultSelectionService`/
  `SelectionHighlight` (Task 4), everything below them.
- Produces: nothing new — this task only adds tests.

- [ ] **Step 1: Write `DefinitionOfDoneTests.cs`**

  ```csharp
  // tests/WinUIDesigner.Designer.Tests/DefinitionOfDoneTests.cs
  using System.IO;
  using System.Linq;
  using System.Xml;
  using Microsoft.UI.Xaml.Controls;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Xaml;
  using WinUIDesigner.Designer.Controls;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.Designer.Tests
  {
      [TestFixture]
      public class DefinitionOfDoneTests
      {
          const string TwoButtonsXaml =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
              "<Button x:Name=\"Left\" Width=\"60\" Height=\"30\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
              "<Button x:Name=\"Right\" Width=\"60\" Height=\"30\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Top\" />" +
              "</Grid>";

          static DesignSurface LoadInto(Microsoft.UI.Xaml.Window window, string xaml)
          {
              var surface = new DesignSurface();
              window.Content = surface;
              using (var reader = XmlReader.Create(new StringReader(xaml)))
                  surface.LoadDesigner(reader, new XamlLoadSettings());
              surface.UpdateLayout();
              return surface;
          }

          // Spec DoD bullet 1: hit-test at a point known to fall inside a specific child element
          // returns the correct DesignItem as ModelHit.
          [Test]
          public void HitTestingThroughTheDesignSurfaceReturnsTheCorrectDesignItem()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadInto(window, TwoButtonsXaml);
                  var grid = (Grid)surface.DesignContext.RootItem.Component;
                  var leftItem = surface.DesignContext.Services.Component.GetDesignItem(grid.Children[0]);

                  var result = surface.DesignPanel.HitTest(new Windows.Foundation.Point(30, 15), true, true, HitTestType.ElementSelection);

                  Assert.AreSame(leftItem, result.ModelHit);
              });
          }

          // Spec DoD bullet 2: selecting via a direct, already-resolved SelectionTypes call updates
          // PrimarySelection/SelectedItems, fires SelectionChanged/PrimarySelectionChanged, and shows
          // exactly one SelectionHighlight adorner.
          [Test]
          public void SelectingAnElementUpdatesSelectionStateFiresEventsAndShowsOneHighlight()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadInto(window, TwoButtonsXaml);
                  var grid = (Grid)surface.DesignContext.RootItem.Component;
                  var leftItem = surface.DesignContext.Services.Component.GetDesignItem(grid.Children[0]);

                  bool selectionChanged = false, primaryChanged = false;
                  surface.DesignContext.Services.Selection.SelectionChanged += (s, e) => selectionChanged = true;
                  surface.DesignContext.Services.Selection.PrimarySelectionChanged += (s, e) => primaryChanged = true;

                  surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[] { leftItem }, SelectionTypes.Primary);
                  surface.UpdateLayout();

                  Assert.IsTrue(selectionChanged, "SelectionChanged should fire");
                  Assert.IsTrue(primaryChanged, "PrimarySelectionChanged should fire");
                  Assert.AreSame(leftItem, surface.DesignContext.Services.Selection.PrimarySelection);
                  CollectionAssert.AreEquivalent(new[] { leftItem }, surface.DesignContext.Services.Selection.SelectedItems);

                  var highlights = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<SelectionHighlight>().ToList();
                  Assert.AreEqual(1, highlights.Count);
              });
          }

          // Spec DoD bullet 3: mutating a layout property through DesignItemProperty.SetValue moves
          // the highlight adorner to track the adorned element after the next LayoutUpdated pass.
          [Test]
          public void ResizingTheSelectedElementMovesTheHighlightAfterLayoutUpdated()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadInto(window, TwoButtonsXaml);
                  var grid = (Grid)surface.DesignContext.RootItem.Component;
                  var leftButton = (Button)grid.Children[0];
                  var leftItem = surface.DesignContext.Services.Component.GetDesignItem(leftButton);

                  surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[] { leftItem }, SelectionTypes.Primary);
                  surface.UpdateLayout();

                  var highlight = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<SelectionHighlight>().Single();
                  double widthBefore = highlight.ActualWidth;

                  using (var group = surface.DesignContext.OpenGroup("resize", new DesignItem[] { leftItem })) {
                      leftItem.Properties["Width"].SetValue(150.0);
                      group.Commit();
                  }
                  surface.UpdateLayout();

                  Assert.AreNotEqual(widthBefore, highlight.ActualWidth,
                      "the highlight adorner should track the adorned element's new size after LayoutUpdated");
              });
          }

          // Spec DoD bullet 4: selecting a second element removes the first element's adorner
          // (single active selection at a time, per this phase's minimal scope).
          [Test]
          public void SelectingASecondElementRemovesTheFirstElementsHighlight()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadInto(window, TwoButtonsXaml);
                  var grid = (Grid)surface.DesignContext.RootItem.Component;
                  var leftButton = (Button)grid.Children[0];
                  var rightButton = (Button)grid.Children[1];
                  var leftItem = surface.DesignContext.Services.Component.GetDesignItem(leftButton);
                  var rightItem = surface.DesignContext.Services.Component.GetDesignItem(rightButton);

                  surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[] { leftItem }, SelectionTypes.Primary);
                  surface.UpdateLayout();
                  var afterFirst = surface.DesignPanel.Adorners.Where(a => a.AdornedElement == leftButton).ToList();
                  Assert.AreEqual(1, afterFirst.Count, "selecting Left should apply one adorner panel to it");

                  surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[] { rightItem }, SelectionTypes.Primary);
                  surface.UpdateLayout();

                  var stillOnLeft = surface.DesignPanel.Adorners.Where(a => a.AdornedElement == leftButton).ToList();
                  var onRight = surface.DesignPanel.Adorners.Where(a => a.AdornedElement == rightButton).ToList();
                  Assert.AreEqual(0, stillOnLeft.Count, "Left's adorner should be removed once selection moves to Right");
                  Assert.AreEqual(1, onRight.Count, "Right should now have exactly one adorner");
              });
          }
      }
  }
  ```

- [ ] **Step 2: Verify all three flagged gaps have tested resolutions**

  - **Gap #1** (no live `Window`/`XamlRoot` in the test host): resolved
    Task 1, Step 5 (`LiveLayoutTests.ActivatedWindowProducesRealLayoutAndHitTestableContent`),
    and exercised by every `RunWithWindow`-based test since.
  - **Gap #2** (`PlacementOperation.GetRealElementSize`, the one member
    ported from an otherwise-excluded file): resolved Task 2, Step 2
    (`AdornerPanel`'s inlined `GetRealElementSize`), exercised indirectly
    by every `AdornerPanel`-hosting test (its `MeasureOverride` calls it on
    every layout pass) — no dedicated unit test needed beyond that, since
    it has no independently observable behavior apart from the sizes those
    tests already assert on.
  - **Gap #3** (`AdornerLayer`'s dictionary keyed by `UIElement` reference
    identity): resolved Task 3, Step 2
    (`AdornerLayerTests.UIElementUsesReferenceIdentityEqualityByDefault`
    empirically confirms `UIElement` doesn't override `Equals`/
    `GetHashCode`, so `Dictionary<UIElement, AdornerInfo>`'s default
    comparer already uses reference identity — no custom comparer needed),
    exercised further by Task 3's other `AdornerLayerTests` (two different
    `Button` instances tracked correctly as distinct keys) and this task's
    `SelectingASecondElementRemovesTheFirstElementsHighlight`.

  If any of these turns out not to actually be covered when you check the
  real test output (not just this plan's description of intent), add the
  missing coverage now rather than checking this box on faith.

- [ ] **Step 3: Full build and full authoritative test run**

  ```bash
  dotnet build WinUIDesigner.sln
  ```
  Expected: 0 errors.
  ```bash
  tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
  tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
  tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
  ```
  Expected: every `WinUIDesigner.Designer.Tests` test (this task's plus
  Tasks 1/3/4's) passes; `WinUIDesigner.DesignModel.Tests` and
  `WinUIDesigner.XamlDom.Tests` unchanged from their pre-Phase-3 baselines.

- [ ] **Step 4: Commit**

  ```bash
  git add tests/WinUIDesigner.Designer.Tests/DefinitionOfDoneTests.cs
  git commit -m "test: add Phase 3 Definition-of-Done suite (hit-test, select, track, reselect)"
  ```

---

## Definition of Done (matches the spec)

- [ ] A test hosts a real `DesignItem.View` inside a live
  `DesignPanel`/`DesignSurface`, hit-tests a known point, and asserts the
  correct `DesignItem` as `ModelHit`. (Task 3, Step 4; Task 5, Step 1)
- [ ] A test selects an element via a direct, already-resolved
  `SelectionTypes` call and asserts `PrimarySelection`/`SelectedItems`
  update, `SelectionChanged`/`PrimarySelectionChanged` fire, and exactly
  one `SelectionHighlight` adorner becomes visible. (Task 4, Step 5; Task
  5, Step 1)
- [ ] A test mutates a layout property via `DesignItemProperty.SetValue`
  and asserts the highlight adorner's position/size tracks it after the
  next `LayoutUpdated`. (Task 5, Step 1)
- [ ] A test selects a second element and asserts the first element's
  adorner is removed. (Task 2, Step 10 — via a fake selection service;
  Task 5, Step 1 — via the real pipeline)
- [ ] Each of the three flagged gaps has an explicit, tested resolution.
  (Task 5, Step 2)
- [ ] Phase 1's and Phase 2's test suites are unaffected throughout. (every
  task's final verification step)
