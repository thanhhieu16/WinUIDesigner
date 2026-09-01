# WinUIDesigner.DesignModel (Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the design-item object model — `ICSharpCode.WpfDesign`'s core
abstractions (`DesignContext`/`DesignItem`/`DesignItemProperty`/`ChangeGroup`/
`ServiceContainer`/extension framework) plus the only concrete XAML-backed
implementation of them, which in the reference lives inside the much larger
`WpfDesign.Designer` project — into a new `WinUIDesigner.DesignModel`
library that sits on top of the already-merged `WinUIDesigner.XamlDom`
(Phase 1). No design surface, no rendering, no `Adorner`s. Those are Phase 3+.

**Architecture:** New project `src/WinUIDesigner.DesignModel/`, referencing
`WinUIDesigner.XamlDom` and talking only to its **public** surface
(`XamlProperty`, `XamlObject`, `XamlDocument`, `XamlParser`,
`CollectionSupport`, `NameScopeHelper`) — `XamlPropertyInfo` is `internal` to
that assembly and stays untouched. Most files port by systematic type
substitution (`System.Windows.*` → `Microsoft.UI.Xaml.*` /
`Windows.Foundation.*`) because the object model is a thin wrapper around
Phase 1's already-live `XamlObject.Instance`, not a new parser. A handful of
reference files reference out-of-scope concretes (selection, tool, copy/paste,
outline, property-grid, placement) and are either dropped entirely or ported
with those specific members removed — see each task's file list. Two real
behavioral gaps have no mechanical fix and are redesigned: the unique-name-
on-paste logic in `XamlComponentService` (WPF's real `INameScope` object vs.
Phase 1's flat per-document name table) and the `DependencyProperty`-keyed
property lookups (WinUI's `DependencyProperty` has no `.Name`/`.OwnerType`,
confirmed in Phase 1).

**Tech Stack:** .NET 10 SDK (`10.0.302`), WinUI 3 / Windows App SDK 1.7
(`Microsoft.WindowsAppSDK` `1.7.250606001` — same pin as Phase 1), NUnit 3 +
NUnitLite (matching Phase 1's authoritative `.exe` test runner).

**Spec:** `docs/superpowers/specs/2026-09-01-winui-designmodel-phase2-design.md`

## Global Constraints

- Target framework for all new projects: `net10.0-windows10.0.19041.0`,
  `UseWinUI=true`, `PackageReference Microsoft.WindowsAppSDK` version
  `1.7.250606001` — same values as Phase 1, do not silently change.
- New code lives under `D:\Project\ST4I\Research\WinUIDesigner`. The
  reference repo `F:\ReferencesCode\CSharp\WpfDesigner` is read-only source
  material — never edit it.
- `WinUIDesigner.DesignModel` may reference only **public** members of
  `WinUIDesigner.XamlDom`. No `InternalsVisibleTo` is added to the Phase 1
  assembly, and no file inside `src/WinUIDesigner.XamlDom/` is modified by
  this plan. Task 3, Step 1 verifies this holds.
- `Nullable` stays `disable`, matching Phase 1 and the reference codebase.
- Every task that touches a flagged gap (numbered in each task, matching the
  spec) must resolve it explicitly — no silent fallback, no swallowed
  exception that hides a platform difference.
- Commit after each task passes its verification step.
- **Authoritative test command**, same reasoning as Phase 1 (WinUI objects
  are thread-affine; `dotnet test`'s `testhost.exe` cannot activate them):
  run the built `.exe` directly —
  `tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe`
  — not `dotnet test`. `dotnet test` stays useful for IDE discovery and
  non-WinUI-object checks, but under `testhost.exe` every WinUI-object test
  reports **ignored**, not failed.
- Phase 1's existing test suite (`WinUIDesigner.XamlDom.Tests`) must stay
  green (70 total / 66 passed / 0 failed / 4 ignored) after Task 1's
  refactor and for the rest of this plan — re-run its `.exe` as part of
  Task 1's verification and treat any regression there as a Task 1 bug, not
  something to patch around later.

---

## Background: why this is four tasks, not nine

Phase 1's plan had a "tangled core" problem: `XamlObject`/`XamlProperty`/
`XamlParser` are mutually referential and only compile as a whole. Phase 2's
core has the same property at a larger scale — `DesignItem` needs
`Extensions.ExtensionManager`, `ExtensionManager` needs `DesignItem` and
`ServiceContainer`, `ServiceContainer` needs `Extensions.ExtensionManager`,
and so on through `ChangeGroup`/`UndoService`/`DesignItemProperty`. Splitting
this into many small tasks (the way Phase 1 split `XamlTypeFinder` and
`CollectionSupport` out from its own tangled core) isn't possible here
because there is no leaf subset of these files that means anything on its
own — a `DesignItem` with no `ExtensionManager` to call, or an
`ExtensionManager` with no `DesignItem` to extend, doesn't compile.

The one real seam in the reference material is the abstract/concrete split
described in the spec: `ICSharpCode.WpfDesign`'s abstract classes and
extension framework (Task 2) have **zero** dependency on the concrete
XAML-backed classes (Task 3) — they only reference each other and
`Microsoft.UI.Xaml`/BCL types. So:

1. **Task 1** — scaffolding + extracting the shared test host (mechanical,
   independently verifiable).
2. **Task 2** — the abstract core + extension framework + `UndoService`
   (one tangled cluster, verifiable by compilation plus a few genuinely
   standalone unit tests — `ExtensionServerAttribute`'s validation,
   `ServiceContainer`'s service registry, `UndoService`'s empty-stack
   behavior — none of which need a concrete `DesignItem`).
3. **Task 3** — the concrete XAML-backed implementation (a second tangled
   cluster, but one that *produces* a working system: this is where the
   first real "parse XAML, get a `DesignItem` tree" test lives).
4. **Task 4** — the Definition-of-Done test suite: round-trip mutation
   through a `ChangeGroup`, undo, and the remaining flagged-gap tests.

---

### Task 1: Extract shared `WinUIDesigner.TestHost` + scaffold `WinUIDesigner.DesignModel`

Phase 1's test project hosts its own NUnitLite entry point and WinUI
dispatcher-thread bootstrap (`Program.cs`, `WinUITestHost.cs`,
`ReflectionXamlMetadataProvider.cs`) because a plain `dotnet test`
`testhost.exe` cannot activate `Microsoft.UI.Xaml` classes at all (see
Phase 1's plan, Task 5). `WinUIDesigner.DesignModel.Tests` needs the exact
same bootstrap. Rather than copy those three files a second time, this task
extracts them into a new shared project both test projects reference.

**Files:**
- Create: `tests/WinUIDesigner.TestHost/WinUIDesigner.TestHost.csproj`
- Create: `tests/WinUIDesigner.TestHost/WinUITestHost.cs` (moved from
  `tests/WinUIDesigner.XamlDom.Tests/WinUITestHost.cs`)
- Create: `tests/WinUIDesigner.TestHost/ReflectionXamlMetadataProvider.cs`
  (moved from `tests/WinUIDesigner.XamlDom.Tests/ReflectionXamlMetadataProvider.cs`)
- Delete: `tests/WinUIDesigner.XamlDom.Tests/WinUITestHost.cs`,
  `tests/WinUIDesigner.XamlDom.Tests/ReflectionXamlMetadataProvider.cs`
- Modify: `tests/WinUIDesigner.XamlDom.Tests/WinUIDesigner.XamlDom.Tests.csproj`
  (add `ProjectReference` to the new `WinUIDesigner.TestHost` project)
- Modify (add `using WinUIDesigner.TestHost;`): every file under
  `tests/WinUIDesigner.XamlDom.Tests/` that references `WinUITestHost`,
  `TestApplication`, or `ReflectionXamlMetadataProvider` — confirmed by grep
  to be exactly: `TemplateHelperTests.cs`, `TestHelper.cs`,
  `RoundTripTests.cs`, `NamescopeTest.cs`, `MarkupExtensionTests.cs`,
  `MarkupExtensionPlaceholderTests.cs`, `DesignTimePropertiesTests.cs`,
  `DependencyPropertyLookupTests.cs`. (Re-run the same grep in Step 3 below
  in case the file list has changed since this plan was written — don't
  trust this list blindly.)
- Create: `src/WinUIDesigner.DesignModel/WinUIDesigner.DesignModel.csproj`
- Create: `tests/WinUIDesigner.DesignModel.Tests/WinUIDesigner.DesignModel.Tests.csproj`
- Modify: `WinUIDesigner.sln` (add the two new projects)

**Interfaces:**
- Produces: `WinUIDesigner.TestHost.WinUITestHost.Run(Action)` (unchanged
  signature, new namespace `WinUIDesigner.TestHost`), a buildable
  `WinUIDesigner.DesignModel` library referencing `WinUIDesigner.XamlDom`,
  and a buildable `WinUIDesigner.DesignModel.Tests` executable referencing
  both `WinUIDesigner.DesignModel` and `WinUIDesigner.TestHost`.

- [ ] **Step 1: Create the `WinUIDesigner.TestHost` project**

  `tests/WinUIDesigner.TestHost/WinUIDesigner.TestHost.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <OutputType>Library</OutputType>
      <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
      <RootNamespace>WinUIDesigner.TestHost</RootNamespace>
      <Nullable>disable</Nullable>
      <UseWinUI>true</UseWinUI>
      <RuntimeIdentifier>win-x64</RuntimeIdentifier>
      <SelfContained>false</SelfContained>
      <WindowsPackageType>None</WindowsPackageType>
      <EnableMsixTooling>false</EnableMsixTooling>
      <GenerateAppxPackageOnBuild>false</GenerateAppxPackageOnBuild>
      <IsPackable>false</IsPackable>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.7.250606001" />
      <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4188" />
      <PackageReference Include="NUnit" Version="3.14.0" />
    </ItemGroup>

  </Project>
  ```
  (`NUnit` is referenced because `WinUITestHost.Run` calls
  `NUnit.Framework.Assert.Ignore` when no WinUI dispatcher thread is
  available — this project is a *test-infrastructure* library, not a
  general-purpose one; it's fine for it to depend on the test framework.
  It deliberately does **not** set `OutputType=Exe` — it's a library that
  the two `.exe` test projects link against, not an entry point itself.)

- [ ] **Step 2: Move `WinUITestHost.cs` and `ReflectionXamlMetadataProvider.cs`**

  Read both files from `tests/WinUIDesigner.XamlDom.Tests/`, write them to
  `tests/WinUIDesigner.TestHost/`, changing only the namespace declaration
  from `WinUIDesigner.XamlDom.Tests` to `WinUIDesigner.TestHost` in both
  files. One additional change, in `WinUITestHost.cs`'s `TestApplication`
  class:

  The reference-derived original built its `IXamlMetadataProvider` from
  only `typeof(TestApplication).Assembly` — correct when `TestApplication`
  lived in the same project as the test fixtures it needed to resolve
  (`ExampleClass`, `ExampleControl`, etc.), wrong now that `TestApplication`
  lives in the shared `WinUIDesigner.TestHost` project while the fixtures
  live in whichever `.exe` project is actually running. Fix:
  ```csharp
  public sealed class TestApplication : Microsoft.UI.Xaml.Application, Microsoft.UI.Xaml.Markup.IXamlMetadataProvider
  {
      readonly ReflectionXamlMetadataProvider provider = new ReflectionXamlMetadataProvider(
          Assembly.GetEntryAssembly() ?? typeof(TestApplication).Assembly,
          typeof(TestApplication).Assembly);

      public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(Type type) => provider.GetXamlType(type);

      public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(string fullName) => provider.GetXamlType(fullName);

      public Microsoft.UI.Xaml.Markup.XmlnsDefinition[] GetXmlnsDefinitions() => provider.GetXmlnsDefinitions();
  }
  ```
  (`Assembly.GetEntryAssembly()` returns the running `.exe`'s own assembly
  — `WinUIDesigner.XamlDom.Tests.exe` or `WinUIDesigner.DesignModel.Tests.exe`
  depending on which one is running — which is exactly where each project's
  test fixture types live. `ReflectionXamlMetadataProvider` already accepts
  `params Assembly[]`, so this needs no change to that file beyond the
  namespace.)

- [ ] **Step 3: Re-point `WinUIDesigner.XamlDom.Tests`**

  Delete the two original files from `tests/WinUIDesigner.XamlDom.Tests/`.
  Add the project reference:
  ```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\WinUIDesigner.XamlDom\WinUIDesigner.XamlDom.csproj" />
    <ProjectReference Include="..\WinUIDesigner.TestHost\WinUIDesigner.TestHost.csproj" />
  </ItemGroup>
  ```
  Re-run the grep from this task's file list (`WinUITestHost|TestApplication|ReflectionXamlMetadataProvider`
  over `tests/WinUIDesigner.XamlDom.Tests/*.cs`) and add
  `using WinUIDesigner.TestHost;` to every matching file that doesn't
  already have it (they're all currently in the same namespace as the
  moved types, so none of them have an explicit `using` for it yet).

- [ ] **Step 4: Add `WinUIDesigner.TestHost` to the solution, build, verify Phase 1 is still green**

  ```bash
  dotnet sln WinUIDesigner.sln add tests/WinUIDesigner.TestHost/WinUIDesigner.TestHost.csproj
  dotnet build WinUIDesigner.sln
  ```
  Expected: builds clean. Then run the **authoritative** Phase 1 test
  command (the built `.exe`, not `dotnet test`) and confirm the result
  matches the pre-refactor baseline exactly:
  ```bash
  tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
  ```
  Expected: `70 total / 66 passed / 0 failed / 4 ignored`. If this
  regresses, the extraction broke something — fix it here, don't carry a
  broken Phase 1 suite forward into Task 2.

- [ ] **Step 5: Create the `WinUIDesigner.DesignModel` library project**

  `src/WinUIDesigner.DesignModel/WinUIDesigner.DesignModel.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <OutputType>Library</OutputType>
      <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
      <RootNamespace>WinUIDesigner.DesignModel</RootNamespace>
      <Nullable>disable</Nullable>
      <UseWinUI>true</UseWinUI>
      <RuntimeIdentifier>win-x64</RuntimeIdentifier>
      <SelfContained>false</SelfContained>
      <WindowsPackageType>None</WindowsPackageType>
      <EnableMsixTooling>false</EnableMsixTooling>
      <GenerateAppxPackageOnBuild>false</GenerateAppxPackageOnBuild>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.7.250606001" />
      <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4188" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\WinUIDesigner.XamlDom\WinUIDesigner.XamlDom.csproj" />
    </ItemGroup>

  </Project>
  ```

- [ ] **Step 6: Create the `WinUIDesigner.DesignModel.Tests` project**

  `tests/WinUIDesigner.DesignModel.Tests/WinUIDesigner.DesignModel.Tests.csproj`
  — same shape as `WinUIDesigner.XamlDom.Tests.csproj` (`OutputType=Exe`,
  NUnitLite, `WindowsAppSDKSelfContained=true`, same reasoning as Phase 1:
  the registration-free activation manifest only gets embedded into an
  `.exe`'s own apphost):
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <GenerateProgramFile>false</GenerateProgramFile>
      <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
      <RootNamespace>WinUIDesigner.DesignModel.Tests</RootNamespace>
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
      <ProjectReference Include="..\..\src\WinUIDesigner.DesignModel\WinUIDesigner.DesignModel.csproj" />
      <ProjectReference Include="..\WinUIDesigner.TestHost\WinUIDesigner.TestHost.csproj" />
    </ItemGroup>

  </Project>
  ```
  `Program.cs` (identical pattern to Phase 1's):
  ```csharp
  using System;
  using System.Reflection;

  namespace WinUIDesigner.DesignModel.Tests
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

- [ ] **Step 7: Add both new projects to the solution**

  ```bash
  dotnet sln WinUIDesigner.sln add src/WinUIDesigner.DesignModel/WinUIDesigner.DesignModel.csproj
  dotnet sln WinUIDesigner.sln add tests/WinUIDesigner.DesignModel.Tests/WinUIDesigner.DesignModel.Tests.csproj
  ```

- [ ] **Step 8: Add one placeholder class so the library builds, plus a smoke test**

  `src/WinUIDesigner.DesignModel/AssemblyInfo.cs`:
  ```csharp
  using System.Reflection;

  [assembly: AssemblyTitle("WinUIDesigner.DesignModel")]
  [assembly: AssemblyDescription("Design-item object model for WinUI 3 XAML")]
  ```
  `tests/WinUIDesigner.DesignModel.Tests/SmokeTests.cs`:
  ```csharp
  using NUnit.Framework;

  namespace WinUIDesigner.DesignModel.Tests
  {
      [TestFixture]
      public class SmokeTests
      {
          [Test]
          public void AssemblyLoads()
          {
              Assert.Pass();
          }
      }
  }
  ```

- [ ] **Step 9: Build and run**

  ```bash
  dotnet build WinUIDesigner.sln
  dotnet test WinUIDesigner.sln
  ```
  Expected: builds clean; the new smoke test passes (alongside Phase 1's
  suite, which will show its usual `dotnet test`-mode ignored count for
  WinUI-object tests — not a regression, see Global Constraints).

- [ ] **Step 10: Commit**

  ```bash
  git add tests/WinUIDesigner.TestHost tests/WinUIDesigner.XamlDom.Tests src/WinUIDesigner.DesignModel tests/WinUIDesigner.DesignModel.Tests WinUIDesigner.sln
  git commit -m "chore: extract shared WinUIDesigner.TestHost, scaffold WinUIDesigner.DesignModel"
  ```

---

### Task 2: Core abstractions + extension framework + undo engine

Ports `ICSharpCode.WpfDesign`'s abstract model (`DesignContext`/`DesignItem`/
`DesignItemProperty`/`ChangeGroup`/`ServiceContainer`) and its extension
framework, plus the one concrete `ChangeGroup` implementation
(`UndoService`/`UndoTransaction`). None of these files depend on the
concrete XAML-backed classes (Task 3) — they only reference each other and
`Microsoft.UI.Xaml`/BCL types, so unlike Phase 1's tangled-core task, this
one **does** end in a fully green build.

**Files:**
- Create: `src/WinUIDesigner.DesignModel/DesignerException.cs` (from
  `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign\Project\DesignerException.cs`
  — see Step 1, simplified)
- Create: `src/WinUIDesigner.DesignModel/ServiceRequiredException.cs` (from
  `...\ServiceRequiredException.cs` — see Step 1, simplified)
- Create: `src/WinUIDesigner.DesignModel/Interfaces/IObservableList.cs`
  (from `...\Interfaces\IObservableList.cs` — verbatim, namespace only)
- Create: `src/WinUIDesigner.DesignModel/EventArgs.cs` (from
  `...\EventArgs.cs` — verbatim, namespace only)
- Create: `src/WinUIDesigner.DesignModel/ServiceContainer.cs` (from
  `...\ServiceContainer.cs` — see Step 2, trimmed)
- Create: `src/WinUIDesigner.DesignModel/Services.cs` (from
  `...\Services.cs` — see Step 3, heavily trimmed)
- Create: `src/WinUIDesigner.DesignModel/DesignContext.cs` (from
  `...\DesignContext.cs` — verbatim, namespace only)
- Create: `src/WinUIDesigner.DesignModel/DesignItem.cs` (from
  `...\DesignItem.cs` — see Step 4, trimmed + type swaps)
- Create: `src/WinUIDesigner.DesignModel/DesignItemProperty.cs` (from
  `...\DesignItemProperty.cs` — see Step 5, DP-keyed overloads dropped)
- Create: `src/WinUIDesigner.DesignModel/ChangeGroup.cs` (from
  `...\ChangeGroup.cs` — verbatim, namespace only)
- Create: `src/WinUIDesigner.DesignModel/Extensions/ExtensionServerAttribute.cs`,
  `ExtensionForAttribute.cs`, `ExtensionAttribute.cs` (from
  `...\Extensions\*.cs` — verbatim, namespace only)
- Create: `src/WinUIDesigner.DesignModel/Extensions/Extension.cs` (from
  `...\Extensions\Extension.cs` — see Step 6, type swap)
- Create: `src/WinUIDesigner.DesignModel/Extensions/ExtensionServer.cs`
  (from `...\Extensions\ExtensionServer.cs` — verbatim, namespace only)
- Create: `src/WinUIDesigner.DesignModel/Extensions/DefaultExtension.cs`
  (from `...\Extensions\DefaultExtension.cs` — see Step 7, one nested class
  dropped)
- Create: `src/WinUIDesigner.DesignModel/Extensions/NeverApplyExtensionsExtensionServer.cs`
  (from `...\Extensions\NeverApplyExtensionsExtensionServer.cs` — verbatim,
  namespace only)
- Create: `src/WinUIDesigner.DesignModel/Extensions/CustomInstanceFactory.cs`
  (from `...\Extensions\CustomInstanceFactory.cs` — see Step 8, gap 2)
- Create: `src/WinUIDesigner.DesignModel/Extensions/DefaultInitializer.cs`,
  `DesignItemInitializer.cs` (from `...\Extensions\*.cs` — verbatim,
  namespace only)
- Create: `src/WinUIDesigner.DesignModel/Extensions/ExtensionManager.cs`
  (from `...\Extensions\ExtensionManager.cs` — verbatim, namespace only)
- Create: `src/WinUIDesigner.DesignModel/Services/UndoService.cs` (from
  `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\Services\UndoService.cs`
  — verbatim, namespace only; this file has zero WPF-specific types)
- Create: `tests/WinUIDesigner.DesignModel.Tests/ExtensionServerAttributeTests.cs`
- Create: `tests/WinUIDesigner.DesignModel.Tests/ServiceContainerTests.cs`
- Create: `tests/WinUIDesigner.DesignModel.Tests/UndoServiceTests.cs`

**Interfaces:**
- Consumes: nothing from `WinUIDesigner.XamlDom` yet — this task is pure
  framework.
- Produces: `DesignContext`, `DesignItem` (abstract, trimmed member set —
  see Step 4), `DesignItemProperty`/`DesignItemPropertyCollection` (abstract,
  string+Type-keyed lookups only), `ChangeGroup` (abstract:
  `Commit()`/`Abort()`), `ServiceContainer` (`AddService`/`GetService<T>`/
  `GetRequiredService<T>`/`RunWhenAvailable<T>`), `IComponentService`/
  `IComponentPropertyService` (trimmed interfaces), the `Extensions.*`
  framework (`Extension`, `ExtensionServer`, `ExtensionManager`,
  `DefaultExtension`/`DefaultExtensionServer`, `CustomInstanceFactory`,
  `DefaultInitializer`, `DesignItemInitializer`), and
  `WinUIDesigner.DesignModel.Services.UndoService`/`UndoTransaction` (the
  concrete `ChangeGroup`). Task 3 builds `XamlDesignContext`/`XamlDesignItem`
  on top of all of this.

#### Known drops from this task's reference files (verified, not guessed)

Found while reading the reference source for this task — each verified by
reading the actual file, not assumed from the spec's higher-level scope
list:

- **`ExtensionInterfaces.cs`** (`IKeyDown`/`IKeyUp`) is dropped entirely,
  not ported. Both interfaces are built on `System.Windows.Input.KeyEventArgs`
  and describe "alerted of KeyDown/KeyUp on the DesignPanel" — a live-surface
  concept. Checked: neither is referenced anywhere in `ExtensionManager.cs`
  or any other file this task ports.
- **`LogicalExtensionServers.cs`** (`LogicalOrExtensionServer<A,B>`/
  `LogicalAndExtensionServer<A,B>`) is dropped. It's WPF-type-free generic
  infrastructure for combining `ExtensionServer`s, but has zero consumers
  anywhere in this task's scope — nothing here registers a concrete
  behavior extension yet. Add back in the phase that adds concrete
  behaviors, if still needed then.
- **`DefaultExtension.cs`'s nested `PermanentWithDesignPanel` class** is
  dropped (`Permanent` is kept). `PermanentWithDesignPanel.ShouldApplyExtensions`
  checks `Services.GetService(typeof(IDesignPanel)) != null` —
  `IDesignPanel` is `Tools.cs`, explicitly out of scope per the spec.
- **`ExtensionMethods.cs`** (Gap 8, from the spec) is dropped entirely, not
  ported. Its `Round()` helper is built on `PlacementInformation.BoundsPrecision`
  (out of scope) and its `GetProperty(this DesignItemPropertyCollection, MemberDescriptor)`/
  `IsAttachedDependencyProperty` are both built on
  `DependencyPropertyDescriptor.FromProperty`, which Phase 1 already
  established has no WinUI counterpart. The spec's investigation (checking
  every reference file for callers) found only `WpfDesign.Designer/Project/PropertyGrid/PropertyGrid.cs`
  (Phase 4) and placement files (out of scope) ever call these three
  methods — nothing in this task's scope needs them.

- [ ] **Step 1: Port `DesignerException.cs` and `ServiceRequiredException.cs`, simplified**

  The reference versions carry `[Serializable]` + a `SerializationInfo`
  constructor + `GetObjectData` override (binary-serialization support,
  including a `[SecurityPermission(...)]` attribute on `ServiceRequiredException`).
  .NET's binary formatter/exception-serialization ceremony is legacy even
  on WPF's own current target frameworks and nothing in this codebase ever
  serializes an exception across an `AppDomain`/process boundary — simplify
  to plain exception subclasses:
  ```csharp
  // src/WinUIDesigner.DesignModel/DesignerException.cs
  using System;

  namespace WinUIDesigner.DesignModel
  {
      public class DesignerException : Exception
      {
          public DesignerException() { }
          public DesignerException(string message) : base(message) { }
          public DesignerException(string message, Exception innerException) : base(message, innerException) { }
      }
  }
  ```
  ```csharp
  // src/WinUIDesigner.DesignModel/ServiceRequiredException.cs
  using System;

  namespace WinUIDesigner.DesignModel
  {
      public class ServiceRequiredException : DesignerException
      {
          public Type ServiceType { get; private set; }

          public ServiceRequiredException(Type serviceType)
              : base("Service " + serviceType.FullName + " is required.")
          {
              this.ServiceType = serviceType;
          }

          public ServiceRequiredException() { }
          public ServiceRequiredException(string message) : base(message) { }
          public ServiceRequiredException(string message, Exception innerException) : base(message, innerException) { }
      }
  }
  ```

- [ ] **Step 2: Port `IObservableList.cs`, `EventArgs.cs`, `ServiceContainer.cs`**

  `Interfaces/IObservableList.cs` and `EventArgs.cs`: verbatim copy, only
  the namespace changes (`ICSharpCode.WpfDesign(.Interfaces)` →
  `WinUIDesigner.DesignModel(.Interfaces)`). `EventArgs.cs` references
  `DesignItem`/`DesignItemProperty`, which don't exist until Step 4/5 below
  — fine, same assembly, whole-assembly compilation (same pattern Phase 1's
  plan used for `CollectionSupport.cs`'s forward references).

  `ServiceContainer.cs`: copy verbatim through `GetService<T>`/
  `RunWhenAvailable<T>` (lines ~1-137 of the reference — no WPF-specific
  type anywhere in that part). Drop the `using ICSharpCode.WpfDesign.Services;`
  import. Of the reference's seven hard-typed convenience properties, keep
  only two:
  ```csharp
  /// <summary>
  /// Gets the <see cref="IComponentService"/>.
  /// Throws an exception if the service is not found.
  /// </summary>
  public IComponentService Component {
      get {
          return GetRequiredService<IComponentService>();
      }
  }

  /// <summary>
  /// Gets the <see cref="ExtensionManager"/>.
  /// Throws an exception if the service is not found.
  /// </summary>
  public Extensions.ExtensionManager ExtensionManager {
      get {
          return GetRequiredService<Extensions.ExtensionManager>();
      }
  }
  ```
  Drop `Selection` (`ISelectionService`), `Tool` (`IToolService`),
  `CopyPasteService` (`ICopyPasteService`), `View` (`ViewService`), and
  `DesignPanel` (`IDesignPanel`) — each points at an out-of-scope service
  interface not ported in this plan.

- [ ] **Step 3: Port `Services.cs`, trimmed to `IComponentService` + `IComponentPropertyService`**

  ```csharp
  // src/WinUIDesigner.DesignModel/Services.cs
  using System;
  using System.ComponentModel;
  using System.Collections.Generic;

  namespace WinUIDesigner.DesignModel
  {
      /// <summary>Supports adding and removing components</summary>
      public interface IComponentService
      {
          DesignItem GetDesignItem(object component);
          DesignItem GetDesignItem(object component, bool findByView);
          DesignItem RegisterComponentForDesigner(object component);
          DesignItem RegisterComponentForDesigner(DesignItem parent, object component);

          event EventHandler<DesignItemEventArgs> ComponentRegisteredAndAddedToContainer;
          event EventHandler<DesignItemEventArgs> ComponentRegistered;
          event EventHandler<DesignItemEventArgs> ComponentRemoved;
          event EventHandler<DesignItemPropertyChangedEventArgs> PropertyChanged;
      }

      /// <summary>
      /// Used to get properties for a Design Item.
      /// </summary>
      public interface IComponentPropertyService
      {
          IEnumerable<MemberDescriptor> GetAvailableProperties(DesignItem designItem);
          IEnumerable<MemberDescriptor> GetAvailableEvents(DesignItem designItem);
          IEnumerable<MemberDescriptor> GetCommonAvailableProperties(IEnumerable<DesignItem> designItems);
      }
  }
  ```
  Dropped from the reference's `IComponentService`: `RegisterComponentForDesignerRecursiveUsingXaml`
  (built on `System.Windows.Markup.XamlWriter.Save`, no WinUI equivalent —
  same finding as Phase 1's gap 1) and `SetDefaultPropertyValues` (built on
  `Metadata.GetDefaultPropertyValues`, and `Metadata.cs` is out of scope,
  deferred to Phase 4). Dropped entirely: `ISelectionService`/
  `SelectionTypes`, `ViewService`, `IPropertyDescriptionService`,
  `IOutlineNodeService`/`IOutlineNodeNameService`, `IErrorService`,
  `ITopLevelWindow`/`ITopLevelWindowService`, `IKeyBindingService` — all
  out of scope per the spec.

- [ ] **Step 4: Port `DesignContext.cs` verbatim, `DesignItem.cs` trimmed**

  `DesignContext.cs`: verbatim copy, namespace only — no WPF types in this
  file at all beyond `System.Xml.XmlWriter` (BCL, unchanged).

  `DesignItem.cs`: copy the reference, applying:
  - `using System.Windows;` / `System.Windows.Media;` →
    `using Microsoft.UI.Xaml;` / `using Microsoft.UI.Xaml.Media;`
  - `Point Position { get; set; }` → `Windows.Foundation.Point Position { get; set; }`
    (kept — inert data, no rendering dependency, cheaper to keep than to
    re-add later)
  - **Drop** `CreateOutlineNode()` — depends on `IOutlineNodeService`
    (Phase 4/5, not ported).
  - **Drop** `GetCompleteAppliedTransformationToView()` and its `Transform`
    return type — depends on WPF `Visual`/`ContainerVisual`/
    `FrameworkElement.LayoutTransform`, none of which exist in WinUI; this
    is a placement/rendering concern, Phase 3.
  - `abstract UIElement View { get; }` → `Microsoft.UI.Xaml.UIElement View { get; }`
    (unchanged shape otherwise).
  - Everything else (`Component`/`ComponentType`/`Context`/`Parent`/
    `ParentProperty`/`Properties`/`AllSetProperties`/`Name`/`Key`/
    extension-management region/`AddBehavior`/`GetBehavior<T>`/`Remove()`/
    `Clone()`/`ContentProperty`/`DepthLevel`) copies verbatim.

- [ ] **Step 5: Port `DesignItemProperty.cs`, dropping the `DependencyProperty`-keyed overloads**

  **Gap 1**, verified while reading this file: WinUI's `DependencyProperty`
  has no `.Name`/`.OwnerType` (established in Phase 1,
  `src/WinUIDesigner.XamlDom/XamlPropertyInfo.cs:157-160`). The reference's
  `DesignItemPropertyCollection.GetProperty(DependencyProperty)` and
  `GetAttachedProperty(DependencyProperty)` both forward to the string-keyed
  overload using `dependencyProperty.Name`/`.OwnerType` — impossible to
  port as-is, and there is no cross-assembly reverse lookup available
  (Phase 1's own `WinUIStaticMemberLocator`, which does forward
  `(Type, name) → DependencyProperty` lookups, is `internal` — not usable
  from this project, and the *reverse* direction it would need,
  `DependencyProperty → (Type, name)`, isn't something it does anyway).

  Resolution: drop both `DependencyProperty`-keyed overloads and the
  `this[DependencyProperty]` indexer. Every caller in this plan (Task 3's
  `XamlDesignItem.IsDesignTimeLocked`, ported in Task 3) already knows the
  declaring type and name statically at the call site — there is no
  situation in this plan's scope where only a `DependencyProperty` instance
  is available with no known owner type.

  ```csharp
  // src/WinUIDesigner.DesignModel/DesignItemProperty.cs
  using System;
  using System.Collections.Generic;
  using System.ComponentModel;
  using WinUIDesigner.DesignModel.Interfaces;
  using Microsoft.UI.Xaml;

  namespace WinUIDesigner.DesignModel
  {
      public abstract class DesignItemProperty : INotifyPropertyChanged
      {
          public abstract string Name { get; }
          public abstract Type ReturnType { get; }
          public abstract Type DeclaringType { get; }
          public abstract string Category { get; }

          public virtual TypeConverter TypeConverter {
              get { return TypeDescriptor.GetConverter(this.ReturnType); }
          }

          public abstract bool IsCollection { get; }
          public abstract bool IsEvent { get; }
          public abstract IObservableList<DesignItem> CollectionElements { get; }
          public abstract DesignItem Value { get; }
          public abstract string TextValue { get; }
          public abstract event EventHandler ValueChanged;
          public abstract event EventHandler ValueOnInstanceChanged;
          public abstract object ValueOnInstance { get; }
          public abstract object DesignerValue { get; }
          public abstract void SetValue(object value);
          public abstract bool IsSet { get; }
          public abstract event EventHandler IsSetChanged;
          public abstract void Reset();
          public abstract T GetConvertedValueOnInstance<T>();
          public abstract DesignItem DesignItem { get; }
          public abstract DependencyProperty DependencyProperty { get; }
          public virtual bool IsAdvanced { get { return false; } }

          public string FullName {
              get { return DeclaringType.FullName + "." + Name; }
          }

          public object ValueOnInstanceOrView {
              get { return Value == null ? ValueOnInstance : Value.View; }
          }

          public string DependencyFullName {
              get {
                  if (DependencyProperty != null) {
                      return DependencyProperty.GetFullName();
                  }
                  return FullName;
              }
          }

          public event PropertyChangedEventHandler PropertyChanged;

          protected virtual void OnPropertyChanged(string propertyName)
          {
              var handler = PropertyChanged;
              if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
          }
      }

      public abstract class DesignItemPropertyCollection : IEnumerable<DesignItemProperty>
      {
          public DesignItemProperty this[string name] {
              get { return GetProperty(name); }
          }

          public abstract DesignItemProperty GetProperty(string name);
          public abstract DesignItemProperty GetAttachedProperty(Type ownerType, string name);
          public abstract IEnumerator<DesignItemProperty> GetEnumerator();

          System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
          {
              return this.GetEnumerator();
          }
      }
  }
  ```
  `DependencyProperty.GetFullName()` (used by `DependencyFullName` above)
  is an extension method — port it as a one-off in this same file rather
  than reviving the reference's `Metadata.cs` (out of scope) just for this:
  ```csharp
  public static class DependencyPropertyExtensions
  {
      public static string GetFullName(this DependencyProperty p)
      {
          return p.ToString();
      }
  }
  ```
  (WinUI's `DependencyProperty` has no `.OwnerType`/`.Name` to compose
  `"OwnerType.Name"` the way the reference did — `ToString()` is the only
  built-in textual identity it exposes. This is a real, permanent behavior
  difference from the reference, not a placeholder; document it with a
  one-line comment above the method.)

- [ ] **Step 6: Port `ChangeGroup.cs` verbatim, `Extension.cs` with type swap**

  `ChangeGroup.cs`: verbatim copy, namespace only.

  `Extension.cs`: `using System.Windows;` → `using Microsoft.UI.Xaml;`, both
  `DependencyProperty.RegisterAttached` calls are otherwise identical on
  `Microsoft.UI.Xaml.DependencyProperty`.

- [ ] **Step 7: Port the `ExtensionServerAttribute`/`ExtensionForAttribute`/`ExtensionAttribute`/`ExtensionServer` files verbatim, `DefaultExtension.cs` minus one nested class**

  The three attribute files and `ExtensionServer.cs`: verbatim copy,
  namespace only — zero WPF-specific types in any of them.

  `DefaultExtension.cs`: copy `DefaultExtension` and `DefaultExtensionServer`
  (including its nested `Permanent` class) verbatim. Drop the nested
  `PermanentWithDesignPanel` class (see "Known drops" above).

- [ ] **Step 8: Port `CustomInstanceFactory.cs` — Gap 2, no `DesignerProperties.SetIsInDesignMode` equivalent**

  **Gap 2**: WPF's `System.Windows.DesignerProperties.SetIsInDesignMode(UIElement, bool)`
  lets designer code mark an individual instance as "in design mode" so the
  control can adjust its own behavior (skip animations, etc.). WinUI's
  equivalent, `Windows.ApplicationModel.DesignMode.DesignModeEnabled`, is a
  process-wide, read-only, auto-detected flag — there is no per-instance
  setter, and nothing in `Microsoft.UI.Xaml.winmd`'s public surface
  provides one. This is a genuine, permanent capability loss in this
  architecture, not a naming difference to paper over: drop the call
  entirely rather than inventing a substitute that doesn't actually tell
  the instance anything.
  ```csharp
  // src/WinUIDesigner.DesignModel/Extensions/CustomInstanceFactory.cs
  using System;

  namespace WinUIDesigner.DesignModel.Extensions
  {
      [ExtensionServer(typeof(NeverApplyExtensionsExtensionServer))]
      public class CustomInstanceFactory : Extension
      {
          public static readonly CustomInstanceFactory DefaultInstanceFactory = new CustomInstanceFactory();

          protected CustomInstanceFactory() { }

          /// <summary>
          /// Creates an instance of the specified type, passing the specified arguments to its constructor.
          /// Unlike the WPF reference, this does not mark the instance as design-time via
          /// DesignerProperties.SetIsInDesignMode - WinUI has no per-instance equivalent
          /// (Windows.ApplicationModel.DesignMode.DesignModeEnabled is process-wide and read-only).
          /// </summary>
          public virtual object CreateInstance(Type type, params object[] arguments)
          {
              return Activator.CreateInstance(type, arguments);
          }
      }
  }
  ```

- [ ] **Step 9: Port `DefaultInitializer.cs`, `DesignItemInitializer.cs`, `NeverApplyExtensionsExtensionServer.cs`, `ExtensionManager.cs` verbatim**

  All four: namespace only. Verified while reading each: none reference any
  `System.Windows.*` type. `ExtensionManager.cs` in particular calls
  `Extension.GetDisabledExtensions(item.View)` where `item.View` is already
  `Microsoft.UI.Xaml.UIElement` per Step 4 above — consistent, no further
  change needed.

- [ ] **Step 10: Port `UndoService.cs` verbatim**

  From `WpfDesign.Designer/Project/Services/UndoService.cs` — namespace
  `ICSharpCode.WpfDesign.Designer.Services` → `WinUIDesigner.DesignModel.Services`.
  Verified: zero `System.Windows.*` types anywhere in this file (`Stack<T>`,
  `List<T>`, plain C# events only). `UndoTransaction`/`ITransactionItem`/
  `IUndoAction` stay exactly as in the reference — `ITransactionItem` stays
  file-internal (`interface ITransactionItem : IUndoAction` with no access
  modifier, i.e. `internal` by default), `IUndoAction` and `UndoService`
  stay `public`.

- [ ] **Step 11: Write and run the standalone framework tests**

  These are the only tests possible before Task 3 exists — no concrete
  `DesignItem`/`DesignContext` yet, so they exercise only what's
  independently testable: attribute validation, the service registry, and
  `UndoService`'s public (non-`internal`) surface.

  `tests/WinUIDesigner.DesignModel.Tests/ExtensionServerAttributeTests.cs`:
  ```csharp
  using System;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel.Extensions;

  namespace WinUIDesigner.DesignModel.Tests
  {
      [TestFixture]
      public class ExtensionServerAttributeTests
      {
          sealed class FakeExtensionServer : ExtensionServer
          {
              public override bool ShouldApplyExtensions(DesignItem extendedItem) => true;
              public override Extension CreateExtension(Type extensionType, DesignItem extendedItem) => null;
              public override void RemoveExtension(Extension extension) { }
              public override event EventHandler<DesignItemCollectionEventArgs> ShouldApplyExtensionsInvalidated { add { } remove { } }
          }

          [Test]
          public void AcceptsValidExtensionServerType()
          {
              var attr = new ExtensionServerAttribute(typeof(FakeExtensionServer));
              Assert.AreEqual(typeof(FakeExtensionServer), attr.ExtensionServerType);
          }

          [Test]
          public void RejectsTypeThatIsNotAnExtensionServer()
          {
              Assert.Throws<ArgumentException>(() => new ExtensionServerAttribute(typeof(string)));
          }

          [Test]
          public void RejectsNullType()
          {
              Assert.Throws<ArgumentNullException>(() => new ExtensionServerAttribute(null));
          }
      }
  }
  ```
  `tests/WinUIDesigner.DesignModel.Tests/ServiceContainerTests.cs`:
  ```csharp
  using NUnit.Framework;
  using WinUIDesigner.DesignModel;

  namespace WinUIDesigner.DesignModel.Tests
  {
      [TestFixture]
      public class ServiceContainerTests
      {
          interface IFakeService { }
          class FakeService : IFakeService { }

          [Test]
          public void GetServiceReturnsNullWhenNotRegistered()
          {
              var services = new ServiceContainer();
              Assert.IsNull(services.GetService<IFakeService>());
          }

          [Test]
          public void AddThenGetReturnsSameInstance()
          {
              var services = new ServiceContainer();
              var instance = new FakeService();
              services.AddService(typeof(IFakeService), instance);
              Assert.AreSame(instance, services.GetService<IFakeService>());
          }

          [Test]
          public void GetRequiredServiceThrowsWhenMissing()
          {
              var services = new ServiceContainer();
              Assert.Throws<ServiceRequiredException>(() => services.GetRequiredService<IFakeService>());
          }

          [Test]
          public void RunWhenAvailableFiresImmediatelyIfAlreadyRegistered()
          {
              var services = new ServiceContainer();
              var instance = new FakeService();
              services.AddService(typeof(IFakeService), instance);

              IFakeService seen = null;
              services.RunWhenAvailable<IFakeService>(s => seen = s);

              Assert.AreSame(instance, seen);
          }

          [Test]
          public void RunWhenAvailableFiresLaterWhenRegisteredAfterSubscribing()
          {
              var services = new ServiceContainer();
              IFakeService seen = null;
              services.RunWhenAvailable<IFakeService>(s => seen = s);

              Assert.IsNull(seen);

              var instance = new FakeService();
              services.AddService(typeof(IFakeService), instance);

              Assert.AreSame(instance, seen);
          }
      }
  }
  ```
  `tests/WinUIDesigner.DesignModel.Tests/UndoServiceTests.cs`:
  ```csharp
  using System;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel.Services;

  namespace WinUIDesigner.DesignModel.Tests
  {
      [TestFixture]
      public class UndoServiceTests
      {
          [Test]
          public void NewServiceCannotUndoOrRedo()
          {
              var undoService = new UndoService();
              Assert.IsFalse(undoService.CanUndo);
              Assert.IsFalse(undoService.CanRedo);
          }

          [Test]
          public void UndoOnEmptyStackThrows()
          {
              var undoService = new UndoService();
              Assert.Throws<InvalidOperationException>(() => undoService.Undo());
          }

          [Test]
          public void RedoOnEmptyStackThrows()
          {
              var undoService = new UndoService();
              Assert.Throws<InvalidOperationException>(() => undoService.Redo());
          }

          [Test]
          public void ClearOnEmptyServiceDoesNotThrow()
          {
              var undoService = new UndoService();
              Assert.DoesNotThrow(() => undoService.Clear());
          }
      }
  }
  ```
  (`StartTransaction`/`Execute` are `internal` to `WinUIDesigner.DesignModel`
  — not reachable from the test project, by design, same as the reference.
  The real transaction path is exercised end-to-end in Task 4 through the
  public `DesignContext.OpenGroup`/`ChangeGroup.Commit`/`Abort` surface,
  once Task 3 provides a concrete `DesignContext`.)

  Run:
  ```bash
  dotnet build WinUIDesigner.sln
  dotnet test WinUIDesigner.sln --filter "FullyQualifiedName~WinUIDesigner.DesignModel.Tests"
  ```
  Expected: builds clean (this task has no forward references left
  unresolved, unlike Phase 1's `CollectionSupport` task), all new tests
  pass.

- [ ] **Step 12: Commit**

  ```bash
  git add src/WinUIDesigner.DesignModel tests/WinUIDesigner.DesignModel.Tests
  git commit -m "feat: port DesignModel core abstractions, extension framework, undo engine"
  ```

---

### Task 3: Concrete XAML-backed implementation

Ports the classes that in the reference live in `WpfDesign.Designer/Project/Xaml/`
— the only concrete implementation of Task 2's abstractions, built directly
on Phase 1's `XamlDocument`/`XamlObject`/`XamlProperty`. This is where a
`XamlDesignContext.RootItem` first becomes a real, walkable `DesignItem`
tree.

**Files:**
- Create: `src/WinUIDesigner.DesignModel/Xaml/XamlLoadSettings.cs` (from
  `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\Xaml\XamlLoadSettings.cs`
  — see Step 1)
- Create: `src/WinUIDesigner.DesignModel/Xaml/XamlErrorService.cs` (from
  `...\WpfDesign.Designer\Project\Services\XamlErrorService.cs` — verbatim,
  namespace only)
- Create: `src/WinUIDesigner.DesignModel/Xaml/XamlDesignContext.cs` (from
  `...\WpfDesign.Designer\Project\Xaml\XamlDesignContext.cs` — see Step 2,
  heavily trimmed)
- Create: `src/WinUIDesigner.DesignModel/Xaml/XamlDesignItem.cs` (from
  `...\WpfDesign.Designer\Project\Xaml\XamlDesignItem.cs` — see Step 3,
  gaps 1 and 4)
- Create: `src/WinUIDesigner.DesignModel/Xaml/XamlModelProperty.cs` (from
  `...\WpfDesign.Designer\Project\Xaml\XamlModelProperty.cs` — see Step 4,
  verbatim)
- Create: `src/WinUIDesigner.DesignModel/Xaml/XamlModelPropertyCollection.cs`
  (from `...\XamlModelPropertyCollection.cs` — see Step 5, verbatim)
- Create: `src/WinUIDesigner.DesignModel/Xaml/XamlModelCollectionElementsCollection.cs`
  (from `...\XamlModelCollectionElementsCollection.cs` — see Step 6, verbatim)
- Create: `src/WinUIDesigner.DesignModel/Xaml/XamlComponentService.cs` (from
  `...\XamlComponentService.cs` — see Step 7, gaps 3 and 4)
- Create: `tests/WinUIDesigner.DesignModel.Tests/XamlDesignContextTests.cs`

**Interfaces:**
- Consumes: Phase 1's public surface (`XamlParser.Parse`, `XamlDocument`,
  `XamlObject`, `XamlProperty`, `NameScopeHelper.FindNamedObject`,
  `MarkupExtensionParser`'s `ReferenceExtensionPlaceholder`), Task 2's
  abstractions.
- Produces: `new XamlDesignContext(XmlReader, XamlLoadSettings).RootItem`
  as a real, walkable `DesignItem` — every later task builds directly on
  this.

- [ ] **Step 1: Port `XamlLoadSettings.cs`**

  One change: `XamlTypeFinder typeFinder = XamlTypeFinder.CreateWpfTypeFinder();`
  → `XamlTypeFinder.CreateWinUITypeFinder()` (Phase 1's factory method).
  Everything else — `DesignerAssemblies`, `CustomServiceRegisterFunctions`,
  `ReportErrors`, `CurrentProjectAssemblyName` — copies verbatim, namespace
  only.

- [ ] **Step 2: Port `XamlDesignContext.cs`, trimmed to in-scope services**

  Verified while reading the reference constructor: it registers 11
  services and does 3 other setup steps that reference out-of-scope types
  (`ISelectionService`/`DefaultSelectionService`, `IToolService`/
  `DefaultToolService`, `ICopyPasteService`/`CopyPasteService`,
  `IErrorService`/`DefaultErrorService`, `IOutlineNodeService`/
  `IOutlineNodeNameService`, `ViewService`/`DefaultViewService`,
  `OptionService`, `EditorManager.SetDefault*EditorType`,
  `ITopLevelWindowService`/`WpfTopLevelWindowService` fallback,
  `RootItemBehavior`, `XamlEditOperations`) — none of those concrete
  service implementations exist in this plan's scope, so none of those
  registrations are ported.
  ```csharp
  // src/WinUIDesigner.DesignModel/Xaml/XamlDesignContext.cs
  using System;
  using System.Collections.Generic;
  using System.Reflection;
  using System.Xml;
  using WinUIDesigner.XamlDom;
  using WinUIDesigner.DesignModel.Services;

  namespace WinUIDesigner.DesignModel.Xaml
  {
      public sealed class XamlDesignContext : DesignContext
      {
          readonly XamlDocument _doc;
          readonly XamlDesignItem _rootItem;
          readonly XamlParserSettings _parserSettings;
          internal readonly XamlComponentService _componentService;

          internal XamlDocument Document {
              get { return _doc; }
          }

          public string ClassName {
              get { return _doc.RootElement.GetXamlAttribute("Class"); }
          }

          public XamlDesignContext(XmlReader xamlReader, XamlLoadSettings loadSettings)
          {
              if (xamlReader == null)
                  throw new ArgumentNullException("xamlReader");
              if (loadSettings == null)
                  throw new ArgumentNullException("loadSettings");

              this.Services.AddService(typeof(IComponentPropertyService), new ComponentPropertyService());
              this.Services.AddService(typeof(UndoService), new UndoService());

              var xamlErrorService = new XamlErrorService();
              this.Services.AddService(typeof(XamlErrorService), xamlErrorService);
              this.Services.AddService(typeof(IXamlErrorSink), xamlErrorService);

              _componentService = new XamlComponentService(this);
              this.Services.AddService(typeof(IComponentService), _componentService);

              foreach (Action<XamlDesignContext> action in loadSettings.CustomServiceRegisterFunctions) {
                  action(this);
              }

              foreach (Assembly designerAssembly in loadSettings.DesignerAssemblies) {
                  this.Services.ExtensionManager.RegisterAssembly(designerAssembly);
              }

              _parserSettings = new XamlParserSettings();
              _parserSettings.TypeFinder = loadSettings.TypeFinder;
              _parserSettings.CurrentProjectAssemblyName = loadSettings.CurrentProjectAssemblyName;
              _parserSettings.CreateInstanceCallback = this.Services.ExtensionManager.CreateInstanceWithCustomInstanceFactory;
              _parserSettings.ServiceProvider = this.Services;
              _doc = XamlParser.Parse(xamlReader, _parserSettings);

              loadSettings.ReportErrors(xamlErrorService);

              if (_doc == null) {
                  string message;
                  if (xamlErrorService != null && xamlErrorService.Errors.Count > 0)
                      message = xamlErrorService.Errors[0].Message;
                  else
                      message = "Could not load document.";
                  throw new XamlLoadException(message);
              }

              _rootItem = _componentService.RegisterXamlComponentRecursive(_doc.RootElement);
          }

          public override void Save(XmlWriter writer)
          {
              _doc.Save(writer);
          }

          public override DesignItem RootItem {
              get { return _rootItem; }
          }

          public XamlParserSettings ParserSettings {
              get { return _parserSettings; }
          }

          public override ChangeGroup OpenGroup(string changeGroupTitle, ICollection<DesignItem> affectedItems)
          {
              if (affectedItems == null)
                  throw new ArgumentNullException("affectedItems");

              UndoService undoService = this.Services.GetRequiredService<UndoService>();
              var g = undoService.StartTransaction(affectedItems);
              g.Title = changeGroupTitle;
              return g;
          }
      }

      /// <summary>
      /// Minimal IComponentPropertyService: reflects System.ComponentModel.TypeDescriptor
      /// properties/events for the design item's component type. No WPF-specific dependency
      /// in the reference implementation this is modelled on beyond TypeDescriptor itself.
      /// </summary>
      sealed class ComponentPropertyService : IComponentPropertyService
      {
          public IEnumerable<System.ComponentModel.MemberDescriptor> GetAvailableProperties(DesignItem designItem)
          {
              foreach (System.ComponentModel.PropertyDescriptor p in System.ComponentModel.TypeDescriptor.GetProperties(designItem.Component))
                  yield return p;
          }

          public IEnumerable<System.ComponentModel.MemberDescriptor> GetAvailableEvents(DesignItem designItem)
          {
              foreach (System.ComponentModel.EventDescriptor e in System.ComponentModel.TypeDescriptor.GetEvents(designItem.Component))
                  yield return e;
          }

          public IEnumerable<System.ComponentModel.MemberDescriptor> GetCommonAvailableProperties(IEnumerable<DesignItem> designItems)
          {
              System.Collections.Generic.HashSet<string> commonNames = null;
              var all = new List<System.ComponentModel.PropertyDescriptor>();
              foreach (var item in designItems) {
                  var props = System.ComponentModel.TypeDescriptor.GetProperties(item.Component);
                  var names = new System.Collections.Generic.HashSet<string>();
                  foreach (System.ComponentModel.PropertyDescriptor p in props) {
                      names.Add(p.Name);
                      all.Add(p);
                  }
                  commonNames = commonNames == null ? names : new System.Collections.Generic.HashSet<string>(System.Linq.Enumerable.Where(commonNames, n => names.Contains(n)));
              }
              if (commonNames == null)
                  yield break;
              var seen = new System.Collections.Generic.HashSet<string>();
              foreach (var p in all) {
                  if (commonNames.Contains(p.Name) && seen.Add(p.Name))
                      yield return p;
              }
          }
      }
  }
  ```
  (`ComponentPropertyService` has no reference-source equivalent to port —
  the reference's own `ComponentPropertyService` lives in
  `WpfDesign.Designer/Project/PropertyGrid/`, out of scope, and is
  significantly more elaborate than what Phase 2 needs. This minimal
  version satisfies `IComponentPropertyService` using only
  `System.ComponentModel.TypeDescriptor`, which Phase 1's own parser
  already depends on — no new gap introduced. It exists so
  `XamlDesignContext`'s service registrations aren't silently short one
  entry; nothing in this plan's tests currently exercises it beyond "it
  doesn't throw," which Step 8 below covers.)

- [ ] **Step 3: Port `XamlDesignItem.cs` — gaps 1, 4, 5, 6, 7**

  ```csharp
  // src/WinUIDesigner.DesignModel/Xaml/XamlDesignItem.cs
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.Linq;
  using Microsoft.UI.Xaml;
  using WinUIDesigner.XamlDom;
  using WinUIDesigner.DesignModel.Services;

  namespace WinUIDesigner.DesignModel.Xaml
  {
      [DebuggerDisplay("XamlDesignItem: {ComponentType.Name}")]
      public sealed class XamlDesignItem : DesignItem
      {
          readonly XamlObject _xamlObject;
          readonly XamlDesignContext _designContext;
          readonly XamlModelPropertyCollection _properties;
          UIElement _view;

          public XamlDesignItem(XamlObject xamlObject, XamlDesignContext designContext)
          {
              this._xamlObject = xamlObject;
              this._designContext = designContext;
              this._properties = new XamlModelPropertyCollection(this);
          }

          internal XamlComponentService ComponentService {
              get { return _designContext._componentService; }
          }

          public XamlObject XamlObject {
              get { return _xamlObject; }
          }

          public override object Component {
              get { return _xamlObject.Instance; }
          }

          public override Type ComponentType {
              get { return _xamlObject.ElementType; }
          }

          void SetNameInternal(string newName)
          {
              var oldName = Name;
              _xamlObject.Name = newName;
              FixDesignItemReferencesOnNameChange(oldName, Name);
          }

          public override string Name {
              get { return _xamlObject.Name; }
              set {
                  UndoService undoService = this.Services.GetService<UndoService>();
                  if (undoService != null)
                      undoService.Execute(new SetNameAction(this, value));
                  else
                      SetNameInternal(value);
              }
          }

          /// <summary>
          /// Fixes {x:Reference} and {Binding ElementName=...} references to this element
          /// elsewhere in the document when its name changes.
          /// </summary>
          public void FixDesignItemReferencesOnNameChange(string oldName, string newName)
          {
              if (!string.IsNullOrEmpty(oldName) && !string.IsNullOrEmpty(newName)) {
                  var root = GetRootXamlObject(this.XamlObject);

                  var references = GetAllChildXamlObjects(root).Where(x =>
                      x.ElementType == typeof(ReferenceExtensionPlaceholder) &&
                      Equals(x.FindOrCreateProperty("Name").GetValueOnInstance<string>(), oldName));
                  foreach (var designItem in references) {
                      var property = designItem.FindOrCreateProperty("Name");
                      var propertyValue = designItem.OwnerDocument.CreatePropertyValue(newName, property);
                      this.ComponentService.RegisterXamlComponentRecursive(propertyValue as XamlObject);
                      property.PropertyValue = propertyValue;
                  }

                  var bindings = GetAllChildXamlObjects(root).Where(x =>
                      x.ElementType == typeof(Microsoft.UI.Xaml.Data.Binding) &&
                      Equals(x.FindOrCreateProperty("ElementName").GetValueOnInstance<string>(), oldName));
                  foreach (var designItem in bindings) {
                      var property = designItem.FindOrCreateProperty("ElementName");
                      var propertyValue = designItem.OwnerDocument.CreatePropertyValue(newName, property);
                      this.ComponentService.RegisterXamlComponentRecursive(propertyValue as XamlObject);
                      property.PropertyValue = propertyValue;
                  }
              }
          }

          internal static XamlObject GetRootXamlObject(XamlObject item)
          {
              var root = item;
              while (root.ParentObject != null)
                  root = root.ParentObject;
              return root;
          }

          internal static IEnumerable<XamlObject> GetAllChildXamlObjects(XamlObject item)
          {
              foreach (var prop in item.Properties) {
                  var propValue = prop.PropertyValue as XamlObject;
                  if (propValue != null) {
                      yield return propValue;
                      foreach (var i in GetAllChildXamlObjects(propValue))
                          yield return i;
                  }

                  if (prop.IsCollection) {
                      foreach (var collectionElement in prop.CollectionElements) {
                          var ce = collectionElement as XamlObject;
                          if (ce != null) {
                              yield return ce;
                              foreach (var i in GetAllChildXamlObjects(ce))
                                  yield return i;
                          }
                      }
                  }
              }
          }

          public override string Key {
              get { return XamlObject.GetXamlAttribute("Key"); }
              set { XamlObject.SetXamlAttribute("Key", value); }
          }

          public override event EventHandler NameChanged {
              add { _xamlObject.NameChanged += value; }
              remove { _xamlObject.NameChanged -= value; }
          }

          public override DesignItem Parent {
              get {
                  if (_xamlObject.ParentProperty == null)
                      return null;
                  else
                      return ComponentService.GetDesignItem(_xamlObject.ParentProperty.ParentObject.Instance);
              }
          }

          public override DesignItemProperty ParentProperty {
              get {
                  DesignItem parent = this.Parent;
                  if (parent == null)
                      return null;
                  XamlProperty prop = _xamlObject.ParentProperty;
                  if (prop.IsAttached) {
                      return parent.Properties.GetAttachedProperty(prop.PropertyTargetType, prop.PropertyName);
                  } else {
                      return parent.Properties.GetProperty(prop.PropertyName);
                  }
              }
          }

          public override event EventHandler ParentChanged {
              add { _xamlObject.ParentPropertyChanged += value; }
              remove { _xamlObject.ParentPropertyChanged -= value; }
          }

          public override UIElement View {
              get {
                  if (_view != null)
                      return _view;
                  else
                      return this.Component as UIElement;
              }
          }

          public override void SetView(UIElement newView)
          {
              _view = newView;
          }

          public override DesignContext Context {
              get { return _designContext; }
          }

          public override DesignItemPropertyCollection Properties {
              get { return _properties; }
          }

          public override IEnumerable<DesignItemProperty> AllSetProperties {
              get { return _xamlObject.Properties.Select(x => new XamlModelProperty(this, x)); }
          }

          internal void NotifyPropertyChanged(XamlModelProperty property, object oldValue, object newValue)
          {
              OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(property.Name));
              ((XamlComponentService)this.Services.Component).RaisePropertyChanged(property, oldValue, newValue);
          }

          public override string ContentPropertyName {
              get { return XamlObject.ContentPropertyName; }
          }

          /// <summary>
          /// Item is Locked at Design Time.
          /// </summary>
          public bool IsDesignTimeLocked {
              get {
                  var locked = Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsLocked").GetConvertedValueOnInstance<object>();
                  return (locked != null && (bool)locked == true);
              }
              set {
                  if (value)
                      Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsLocked").SetValue(true);
                  else
                      Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsLocked").Reset();
              }
          }

          public override DesignItem Clone()
          {
              DesignItem item = null;
              var xaml = XamlStaticTools.GetXaml(this.XamlObject);
              XamlDesignItem rootItem = Context.RootItem as XamlDesignItem;
              var obj = XamlParser.ParseSnippet(rootItem.XamlObject, xaml, ((XamlDesignContext)Context).ParserSettings);
              if (obj != null) {
                  item = ((XamlDesignContext)Context)._componentService.RegisterXamlComponentRecursive(obj);
              }
              return item;
          }

          sealed class SetNameAction : ITransactionItem
          {
              XamlDesignItem designItem;
              string oldName;
              string newName;

              public SetNameAction(XamlDesignItem designItem, string newName)
              {
                  this.designItem = designItem;
                  this.newName = newName;
                  oldName = designItem.Name;
              }

              public string Title { get { return "Set name"; } }

              public void Do() { designItem.SetNameInternal(newName); }
              public void Undo() { designItem.SetNameInternal(oldName); }

              public ICollection<DesignItem> AffectedElements {
                  get { return new DesignItem[] { designItem }; }
              }

              public bool MergeWith(ITransactionItem other)
              {
                  SetNameAction o = other as SetNameAction;
                  if (o != null && designItem == o.designItem) {
                      newName = o.newName;
                      return true;
                  }
                  return false;
              }
          }
      }
  }
  ```
  Three deliberate deviations from the reference, each a verified gap from
  the spec:
  - **Gap 4** (`FixDesignItemReferencesOnNameChange`/`GetRootXamlObject`/
    `GetAllChildXamlObjects`): the reference's `onlyFromSameNamescope`
    parameter and its `NameScopeHelper.GetNameScopeFromObject` comparisons
    are dropped entirely — Phase 1's `NameScopeHelper` has only one flat
    table per `XamlDocument`, not per-namescope, so "same namescope" and
    "same document" collapse to the same thing within a single document;
    the parameter had nothing left to distinguish. `typeof(System.Windows.Markup.Reference)`
    → `typeof(ReferenceExtensionPlaceholder)` (Phase 1's real placeholder
    type for `{x:Reference}` — verified it has a settable `Name` property
    via `ReferenceExtensionPlaceholder(string name)`, matching the
    `FindOrCreateProperty("Name")` usage below it). `typeof(System.Windows.Data.Binding)`
    → `typeof(Microsoft.UI.Xaml.Data.Binding)` (a real, direct type swap —
    `Binding` not being a `MarkupExtension` on WinUI, per Phase 1's Gap 7,
    doesn't matter here since this code only compares `ElementType`, never
    calls `ProvideValue`).
  - **Gap 1** (`IsDesignTimeLocked`): uses the string+`Type` overload of
    `GetAttachedProperty` (`typeof(DesignTimeProperties)`, `"IsLocked"`)
    rather than the dropped `DependencyProperty`-keyed overload — verified
    against Phase 1's `DesignTimeProperties.cs`, which registers exactly
    `DependencyProperty.RegisterAttached("IsLocked", typeof(bool), typeof(DesignTimeProperties), ...)`.
  - **Gap 5/6** are not code changes in this file — see Step 7's note on
    `XamlComponentService`'s null-`Component` guard, and Task 4's template
    tests, for how the opaque-template-leaf and null-`Instance` findings
    are actually handled.

- [ ] **Step 4: Port `XamlModelProperty.cs` verbatim**

  Verified while reading the reference: every member this file touches
  (`XamlProperty.PropertyName`/`.IsCollection`/`.IsEvent`/`.ReturnType`/
  `.PropertyTargetType`/`.Category`/`.TypeConverter`/`.CollectionElements`/
  `.PropertyValue`/`.ValueOnInstance`/`.DesignerValue`/`.IsSet`/
  `.ValueChanged`/`.ValueOnInstanceChanged`/`.IsSetChanged`/`.Reset()`/
  `.GetValueOnInstance<T>()`/`.DependencyProperty`/`.IsAdvanced`,
  `XamlObject.ParentObject`/`.OwnerDocument`, `XamlDocument.CreateNullValue()`/
  `.CreatePropertyValue(object, XamlProperty)`) is public on Phase 1's
  `XamlProperty`/`XamlObject`/`XamlDocument`. Copy the reference file with
  only: namespace change, `using System.Windows;` dropped (only used for
  the `DependencyProperty` type, already imported via `WinUIDesigner.XamlDom`
  → actually add `using Microsoft.UI.Xaml;` for `DependencyProperty`), and
  `using ICSharpCode.WpfDesign.Designer.Services;` →
  `using WinUIDesigner.DesignModel.Services;` (for `UndoService`,
  `PropertyChangeAction : ITransactionItem`).

- [ ] **Step 5: Port `XamlModelPropertyCollection.cs` verbatim**

  Namespace only. Uses `_item.XamlObject.FindOrCreateProperty(name)`/
  `.FindOrCreateAttachedProperty(ownerType, name)` — both confirmed public
  on Phase 1's `XamlObject`.

- [ ] **Step 6: Port `XamlModelCollectionElementsCollection.cs` verbatim**

  Namespace only. Uses `NameScopeHelper.NameChanged(designItem.XamlObject, designItem.Name, null)` /
  `(..., null, designItem.Name)` directly in `RemoveFromNamescopeRecursive`/
  `AddToNamescopeRecursive` — confirmed this is exactly Phase 1's public
  `NameScopeHelper.NameChanged(XamlObject, string oldName, string newName)`
  signature, no rewrite needed (unlike `XamlDesignItem`'s
  `onlyFromSameNamescope` handling in Step 3, this file never touched the
  WPF-only per-object namescope-lookup concept to begin with).

- [ ] **Step 7: Port `XamlComponentService.cs` — gaps 3 and 4, plus a null-`Component` guard**

  ```csharp
  // src/WinUIDesigner.DesignModel/Xaml/XamlComponentService.cs
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Runtime.CompilerServices;
  using WinUIDesigner.XamlDom;

  namespace WinUIDesigner.DesignModel.Xaml
  {
      public sealed class XamlComponentService : IComponentService
      {
          public event EventHandler<DesignItemPropertyChangedEventArgs> PropertyChanged;

          sealed class IdentityEqualityComparer : IEqualityComparer<object>
          {
              internal static readonly IdentityEqualityComparer Instance = new IdentityEqualityComparer();
              int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
              bool IEqualityComparer<object>.Equals(object x, object y) => x == y;
          }

          readonly XamlDesignContext _context;

          public XamlComponentService(XamlDesignContext context)
          {
              this._context = context;
          }

          public event EventHandler<DesignItemEventArgs> ComponentRegisteredAndAddedToContainer;
          public event EventHandler<DesignItemEventArgs> ComponentRegistered;
          public event EventHandler<DesignItemEventArgs> ComponentRemoved;

          Dictionary<object, XamlDesignItem> _sites = new Dictionary<object, XamlDesignItem>(IdentityEqualityComparer.Instance);

          public DesignItem GetDesignItem(object component)
          {
              return GetDesignItem(component, false);
          }

          public DesignItem GetDesignItem(object component, bool findByView)
          {
              if (component == null)
                  throw new ArgumentNullException("component");
              XamlDesignItem site;
              _sites.TryGetValue(component, out site);

              if (findByView) {
                  site = site ?? _sites.Values.FirstOrDefault(x => Equals(x.View, component));
              }
              return site;
          }

          public DesignItem RegisterComponentForDesigner(object component)
          {
              return RegisterComponentForDesigner(null, component);
          }

          public DesignItem RegisterComponentForDesigner(DesignItem parent, object component)
          {
              // The reference substitutes System.Windows.Markup.NullExtension/TypeExtension here
              // for a null/Type argument. WinUI has no {x:Type} markup extension at all (confirmed
              // absent from Microsoft.UI.Xaml.winmd during Phase 1 planning), so there is nothing to
              // substitute for a Type argument - that branch is dropped, not ported. For a null
              // argument, this codebase's own NullExtensionPlaceholder (Phase 1's real stand-in for
              // {x:Null}) is the direct equivalent of the reference's NullExtension.
              if (component == null) {
                  component = new NullExtensionPlaceholder();
              }

              XamlObject parentXamlObject = null;
              if (parent != null)
                  parentXamlObject = ((XamlDesignItem)parent).XamlObject;

              XamlDesignItem item = new XamlDesignItem(_context.Document.CreateObject(parentXamlObject, component), _context);
              _context.Services.ExtensionManager.ApplyDesignItemInitializers(item);

              if (!(component is string))
                  RegisterSite(component, item);
              if (ComponentRegistered != null) {
                  ComponentRegistered(this, new DesignItemEventArgs(item));
              }
              return item;
          }

          internal void RaiseComponentRegisteredAndAddedToContainer(DesignItem obj)
          {
              if (ComponentRegisteredAndAddedToContainer != null) {
                  ComponentRegisteredAndAddedToContainer(this, new DesignItemEventArgs(obj));
              }
          }

          /// <summary>
          /// Registers components from an existing XAML tree.
          /// </summary>
          public XamlDesignItem RegisterXamlComponentRecursive(XamlObject obj)
          {
              if (obj == null) return null;

              foreach (XamlProperty prop in obj.Properties) {
                  RegisterXamlComponentRecursive(prop.PropertyValue as XamlObject);
                  foreach (XamlPropertyValue val in prop.CollectionElements) {
                      RegisterXamlComponentRecursive(val as XamlObject);
                  }
              }

              XamlDesignItem site = new XamlDesignItem(obj, _context);
              _context.Services.ExtensionManager.ApplyDesignItemInitializers(site);

              RegisterSite(site.Component, site);
              if (ComponentRegistered != null) {
                  ComponentRegistered(this, new DesignItemEventArgs(site));
              }

              // Gap 4: the reference resolves duplicate-name-on-paste via WPF's real INameScope
              // object (nameScope.FindName/.RegisterName). Phase 1 has no INameScope - only a flat
              // per-document name table (WinUIDesigner.XamlDom.NameScopeHelper) - so this queries
              // that table directly instead. Setting site.Name below already routes through
              // XamlObject.SetXamlAttribute, which calls NameScopeHelper.NameChanged itself
              // (verified in src/WinUIDesigner.XamlDom/XamlObject.cs) - no separate "register" step
              // is needed here the way the reference needed nameScope.RegisterName.
              if (_context.RootItem != null && !string.IsNullOrEmpty(site.Name)) {
                  var document = _context.Document;
                  string newName = site.Name;
                  if (NameScopeHelper.FindNamedObject(document, newName) != null) {
                      int copyIndex = newName.LastIndexOf("_Copy", StringComparison.Ordinal);
                      if (copyIndex < 0) {
                          newName += "_Copy";
                      } else if (!newName.EndsWith("_Copy", StringComparison.Ordinal)) {
                          string copyEnd = newName.Substring(copyIndex + "_Copy".Length);
                          int copyEndValue;
                          if (Int32.TryParse(copyEnd, out copyEndValue))
                              newName = newName.Remove(copyIndex + "_Copy".Length);
                          else
                              newName += "_Copy";
                      }

                      int i = 1;
                      string newNameTemplate = newName;
                      while (NameScopeHelper.FindNamedObject(document, newName) != null) {
                          newName = newNameTemplate + i++;
                      }

                      site.Name = newName;
                  }
              }
              return site;
          }

          /// <summary>
          /// Gap 5: XamlObject.Instance can be null when TemplateHelper.GetFrameworkTemplate
          /// fails to find a FrameworkTemplate in a wrapped template fragment (see TemplateHelper.cs).
          /// The reference's _sites.Add(site.Component, site) would throw ArgumentNullException on
          /// this - a Dictionary key can't be null. Refuse registration with a clear, specific
          /// exception instead of letting an unrelated NullReferenceException/ArgumentNullException
          /// surface deeper in the dictionary. See Task 4 for whether this path is reachable in
          /// practice under the current calling contract - it's kept defensively either way.
          /// </summary>
          void RegisterSite(object component, XamlDesignItem site)
          {
              if (component == null) {
                  throw new DesignerException(
                      "Cannot register a design item whose Component is null " +
                      "(the underlying XamlObject.Instance was null - typically a template " +
                      "fragment that failed to produce a FrameworkTemplate).");
              }
              _sites.Add(component, site);
          }

          internal void RaisePropertyChanged(XamlModelProperty property, object oldValue, object newValue)
          {
              var ev = this.PropertyChanged;
              if (ev != null) {
                  ev(this, new DesignItemPropertyChangedEventArgs(property.DesignItem, property, oldValue, newValue));
              }
          }

          internal void RaiseComponentRemoved(DesignItem item)
          {
              var ev = this.ComponentRemoved;
              if (ev != null) {
                  ev(this, new DesignItemEventArgs(item));
              }
          }
      }
  }
  ```
  **Gap 3**: `component is Type` → `new TypeExtension((Type)component)`
  branch dropped entirely — confirmed no `{x:Type}` markup extension exists
  anywhere in `Microsoft.UI.Xaml.winmd` (Phase 1 finding). Nothing in this
  plan's scope ever calls `RegisterComponentForDesigner` with a `Type`
  argument, so this isn't a loss of tested behavior, only of an unreachable
  branch.

  Two methods from the reference are **not present at all**: `SetDefaultPropertyValues`
  (built on `Metadata.GetDefaultPropertyValues`, out of scope — and no
  longer part of the trimmed `IComponentService` interface from Task 2, so
  there's nothing to override) and `RegisterComponentForDesignerRecursiveUsingXaml`
  (built on `System.Windows.Markup.XamlWriter.Save`, no WinUI equivalent,
  also removed from the interface).

- [ ] **Step 8: Write the first end-to-end test**

  `tests/WinUIDesigner.DesignModel.Tests/XamlDesignContextTests.cs`:
  ```csharp
  using System.IO;
  using System.Xml;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Xaml;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.DesignModel.Tests
  {
      [TestFixture]
      public class XamlDesignContextTests
      {
          const string SimpleXaml =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
              "<Button x:Name=\"MyButton\" Content=\"Hello\" />" +
              "</Grid>";

          static XamlDesignContext Load(string xaml)
          {
              using (var reader = XmlReader.Create(new StringReader(xaml))) {
                  return new XamlDesignContext(reader, new XamlLoadSettings());
              }
          }

          [Test]
          public void RootItemHasCorrectComponentType()
          {
              WinUITestHost.Run(() => {
                  var context = Load(SimpleXaml);
                  Assert.AreEqual(typeof(Microsoft.UI.Xaml.Controls.Grid), context.RootItem.ComponentType);
              });
          }

          [Test]
          public void ChildIsRegisteredAndFindableByName()
          {
              WinUITestHost.Run(() => {
                  var context = Load(SimpleXaml);
                  var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                  var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];

                  var buttonItem = context.Services.Component.GetDesignItem(button);

                  Assert.IsNotNull(buttonItem);
                  Assert.AreEqual("MyButton", buttonItem.Name);
                  Assert.AreEqual(context.RootItem, buttonItem.Parent);
              });
          }

          [Test]
          public void ContentPropertyReadsThroughToLiveInstance()
          {
              WinUITestHost.Run(() => {
                  var context = Load(SimpleXaml);
                  var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                  var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                  var buttonItem = context.Services.Component.GetDesignItem(button);

                  Assert.AreEqual("Hello", buttonItem.Properties["Content"].ValueOnInstance);
              });
          }
      }
  }
  ```

- [ ] **Step 9: Build and run**

  ```bash
  dotnet build WinUIDesigner.sln
  ```
  Then run the authoritative `.exe` (these tests create real WinUI objects,
  so `dotnet test`'s `testhost.exe` will report them ignored, not passed —
  that's expected, not a failure):
  ```bash
  tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
  ```
  Expected: all tests pass, including Task 2's framework tests and this
  task's three new ones.

- [ ] **Step 10: Commit**

  ```bash
  git add src/WinUIDesigner.DesignModel/Xaml tests/WinUIDesigner.DesignModel.Tests/XamlDesignContextTests.cs
  git commit -m "feat: port concrete XAML-backed DesignItem implementation"
  ```

---

### Task 4: Definition-of-Done test suite

Covers the spec's Definition of Done directly: round-trip mutation through
a real `ChangeGroup`, undo, the cross-assembly-visibility claim, and the two
remaining flagged gaps (5 and 6 — the template opaque-leaf behavior and the
null-`Instance` registration policy from Task 3, Step 7).

**Files:**
- Create: `tests/WinUIDesigner.DesignModel.Tests/RoundTripMutationTests.cs`
- Create: `tests/WinUIDesigner.DesignModel.Tests/TemplateTests.cs`

**Interfaces:**
- Consumes: `XamlDesignContext`/`XamlDesignItem`/`DesignItemProperty` (Task 3).
- Produces: nothing new — this task only adds tests and one grep-based
  verification step.

- [ ] **Step 1: Verify the cross-assembly-visibility claim**

  The spec and this plan's Global Constraints both assert
  `WinUIDesigner.DesignModel` uses only public members of
  `WinUIDesigner.XamlDom`. Confirm this by grepping the new project for any
  reference to the one internal type that would matter
  (`XamlPropertyInfo`, and its nested `WinUIStaticMemberLocator`):
  ```bash
  grep -rn "XamlPropertyInfo\|WinUIStaticMemberLocator" src/WinUIDesigner.DesignModel/
  ```
  Expected: no matches. A clean `dotnet build` of the whole solution (Step 4
  below) is the actual proof — this grep is a fast pre-check, not a
  substitute for it. If this ever needs to change, that's a real API
  surface decision to make explicitly (add specific public members to
  `WinUIDesigner.XamlDom`), not something to route around with
  `InternalsVisibleTo`.

- [ ] **Step 2: Round-trip mutation test — parse, mutate inside a `ChangeGroup`, commit, save, re-verify against the real WinUI oracle**

  `tests/WinUIDesigner.DesignModel.Tests/RoundTripMutationTests.cs`:
  ```csharp
  using System.IO;
  using System.Text;
  using System.Xml;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Xaml;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.DesignModel.Tests
  {
      [TestFixture]
      public class RoundTripMutationTests
      {
          const string SimpleXaml =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
              "<Button x:Name=\"MyButton\" Content=\"Hello\" />" +
              "</Grid>";

          static XamlDesignContext Load(string xaml)
          {
              using (var reader = XmlReader.Create(new StringReader(xaml)))
                  return new XamlDesignContext(reader, new XamlLoadSettings());
          }

          static string Save(XamlDesignContext context)
          {
              var sb = new StringBuilder();
              using (var writer = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true }))
                  context.Save(writer);
              return sb.ToString();
          }

          [Test]
          public void CommittedMutationSurvivesRoundTripThroughTheRealWinUIOracle()
          {
              WinUITestHost.Run(() => {
                  var context = Load(SimpleXaml);
                  var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                  var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                  var buttonItem = context.Services.Component.GetDesignItem(button);

                  using (var group = context.OpenGroup("change content", new DesignItem[] { buttonItem })) {
                      buttonItem.Properties["Content"].SetValue("World");
                      group.Commit();
                  }

                  Assert.AreEqual("World", button.Content, "live instance should reflect the committed mutation immediately");

                  var savedXaml = Save(context);
                  var reparsed = (Microsoft.UI.Xaml.Controls.Grid)Microsoft.UI.Xaml.Markup.XamlReader.Load(savedXaml);
                  var reparsedButton = (Microsoft.UI.Xaml.Controls.Button)reparsed.Children[0];

                  Assert.AreEqual("World", reparsedButton.Content, "the real WinUI XamlReader oracle should agree after re-parsing the saved XAML");
              });
          }

          [Test]
          public void AbortedMutationRevertsTheLiveInstance()
          {
              WinUITestHost.Run(() => {
                  var context = Load(SimpleXaml);
                  var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                  var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                  var buttonItem = context.Services.Component.GetDesignItem(button);

                  using (var group = context.OpenGroup("change content", new DesignItem[] { buttonItem })) {
                      buttonItem.Properties["Content"].SetValue("World");
                      Assert.AreEqual("World", button.Content, "the mutation should be visible immediately, before Abort");
                      group.Abort();
                  }

                  Assert.AreEqual("Hello", button.Content, "Abort should revert the live instance to its pre-transaction value");
              });
          }

          [Test]
          public void UndoServiceUndoRevertsACommittedMutation()
          {
              WinUITestHost.Run(() => {
                  var context = Load(SimpleXaml);
                  var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                  var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                  var buttonItem = context.Services.Component.GetDesignItem(button);
                  var undoService = context.Services.GetRequiredService<Services.UndoService>();

                  using (var group = context.OpenGroup("change content", new DesignItem[] { buttonItem })) {
                      buttonItem.Properties["Content"].SetValue("World");
                      group.Commit();
                  }

                  Assert.IsTrue(undoService.CanUndo);
                  undoService.Undo();

                  Assert.AreEqual("Hello", button.Content, "UndoService.Undo() should revert the committed mutation");
                  Assert.IsTrue(undoService.CanRedo);
              });
          }
      }
  }
  ```
  (`ChangeGroup g = context.OpenGroup(...)` returns the concrete
  `UndoTransaction`, which is `IDisposable` — `Dispose()` aborts if the
  transaction is still open, matching the reference's documented contract;
  using `using` blocks here mirrors that intent even though every path
  above explicitly calls `Commit()`/`Abort()` before the block ends.)

- [ ] **Step 3: Template tests — gap 6 (opaque leaf) and gap 5 (null-`Instance` guard)**

  `tests/WinUIDesigner.DesignModel.Tests/TemplateTests.cs`:
  ```csharp
  using System.IO;
  using System.Xml;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Xaml;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.DesignModel.Tests
  {
      [TestFixture]
      public class TemplateTests
      {
          const string XamlWithControlTemplate =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
              "<Button x:Name=\"MyButton\">" +
              "<Button.Template>" +
              "<ControlTemplate TargetType=\"Button\">" +
              "<Border><ContentPresenter/></Border>" +
              "</ControlTemplate>" +
              "</Button.Template>" +
              "</Button>" +
              "</Grid>";

          static XamlDesignContext Load(string xaml)
          {
              using (var reader = XmlReader.Create(new StringReader(xaml)))
                  return new XamlDesignContext(reader, new XamlLoadSettings());
          }

          [Test]
          public void ControlTemplateIsAnOpaqueLeaf()
          {
              // Gap 6: Phase 1's parser returns immediately for any FrameworkTemplate subtype -
              // no children parsed, no properties recorded - so the DesignItem for the template
              // instance has an empty Properties collection and cannot be walked into. This test
              // documents that as an explicit, tested non-goal rather than an unverified assumption.
              WinUITestHost.Run(() => {
                  var context = Load(XamlWithControlTemplate);
                  var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                  var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                  var buttonItem = context.Services.Component.GetDesignItem(button);

                  var templateProperty = buttonItem.Properties["Template"];
                  Assert.IsNotNull(templateProperty.ValueOnInstance, "the template itself should still construct successfully");

                  var templateItem = templateProperty.Value;
                  if (templateItem != null) {
                      CollectionAssert.IsEmpty(templateItem.AllSetProperties,
                          "a FrameworkTemplate's DesignItem should have no parsed properties (Phase 1 gap 6)");
                  }
                  // templateItem may also legitimately be null here: Properties.Value only returns
                  // non-null for a PropertyValue that is itself a registered XamlObject, and whether
                  // a FrameworkTemplate value is exposed that way is exactly what this test is
                  // pinning down empirically rather than assuming either way.
              });
          }

          [Test]
          public void RegisteringAComponentWithNullInstanceThrowsADesignerException()
          {
              // Gap 5: the null-Instance path (TemplateHelper.GetFrameworkTemplate returning null)
              // could not be reliably triggered through the public parsing surface during planning -
              // every well-formed <ControlTemplate>/<DataTemplate> fragment reaching that code path
              // is structurally guaranteed to produce a real FrameworkTemplate. This test instead
              // verifies the guard directly: XamlComponentService refuses a null Component with a
              // clear DesignerException rather than an unrelated NullReferenceException /
              // ArgumentNullException from the underlying dictionary. If a future task finds a real
              // repro for the null-Instance case, add it here as a second, XAML-driven test.
              WinUITestHost.Run(() => {
                  var context = Load("<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                      "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />");

                  Assert.Throws<DesignerException>(() =>
                      context.Services.Component.RegisterComponentForDesigner(context.RootItem, null));
              });
          }
      }
  }
  ```
  Run both new files, confirm results empirically rather than assuming —
  in particular, `ControlTemplateIsAnOpaqueLeaf` is written to pass under
  either outcome for `templateItem` (null or non-null-but-empty) precisely
  because which one is true wasn't settled during planning; if the test run
  shows `templateItem` is non-null, tighten the assertion to
  `Assert.IsNotNull(templateItem)` before moving on, so the test actually
  pins down the real behavior instead of merely tolerating both.

- [ ] **Step 4: Full build and full authoritative test run**

  ```bash
  dotnet build WinUIDesigner.sln
  ```
  Expected: 0 errors.
  ```bash
  tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
  tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
  ```
  Expected: every `WinUIDesigner.DesignModel.Tests` test passes (adjust the
  `ControlTemplateIsAnOpaqueLeaf` assertion per Step 3's note if needed,
  then re-run); `WinUIDesigner.XamlDom.Tests` still reports
  `70 total / 66 passed / 0 failed / 4 ignored`, unchanged from Task 1's
  baseline.

- [ ] **Step 5: Commit**

  ```bash
  git add tests/WinUIDesigner.DesignModel.Tests/RoundTripMutationTests.cs tests/WinUIDesigner.DesignModel.Tests/TemplateTests.cs
  git commit -m "test: add DesignModel round-trip mutation, undo, and template-gap coverage"
  ```

---

## Definition of Done (matches the spec)

- [ ] A round-trip test parses real WinUI 3 XAML into a `XamlDesignContext`,
  mutates a property via `DesignItemProperty.SetValue` inside a
  `ChangeGroup`, commits, calls `XamlDocument.Save`, and the result
  re-parses via the real `Microsoft.UI.Xaml.Markup.XamlReader.Load` oracle
  to the mutated value. (Task 4, Step 2)
- [ ] An undo test performs the same mutation and reverts it, both via
  `ChangeGroup.Abort()` and via `UndoService.Undo()` after `Commit()`.
  (Task 4, Step 2)
- [ ] Each of the eight flagged gaps from the spec has an explicit, tested
  resolution: gap 1 (Task 2 Step 5 drops the `DependencyProperty`-keyed
  overloads; Task 3 Step 3 uses the string+`Type` overload instead), gap 2
  (Task 2 Step 8), gap 3 (Task 3 Step 7), gap 4 (Task 3 Steps 3 and 7),
  gap 5 (Task 3 Step 7's guard, tested Task 4 Step 3), gap 6 (Task 4 Step 3),
  gap 7 (Task 3 Step 3), gap 8 (Task 2's "Known drops" — `ExtensionMethods.cs`
  is not ported at all, so its `DependencyPropertyDescriptor.FromProperty`
  calls never need a WinUI substitute).
- [ ] A test confirms `WinUIDesigner.DesignModel` compiles against
  `WinUIDesigner.XamlDom` using only public members. (Task 4, Step 1)
- [ ] A test confirms the template non-goal explicitly. (Task 4, Step 3)
- [ ] Phase 1's test suite is unaffected. (Task 1, Step 4; re-verified Task 4, Step 4)
