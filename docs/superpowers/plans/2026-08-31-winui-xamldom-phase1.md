# WinUIDesigner.XamlDom (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port `ICSharpCode.WpfDesign.XamlDom` (SharpDevelop's WPF XAML
object-model/parser, at `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.XamlDom\Project`)
into a new `WinUIDesigner.XamlDom` library that parses and serializes
**WinUI 3** XAML — no design surface, no live rendering, no property-grid
metadata. Those are Phases 2–5.

**Architecture:** New solution at `D:\Project\ST4I\Research\WinUIDesigner`.
Most of the 27 reference source files port by systematic type substitution
(`System.Windows.*` → `Microsoft.UI.Xaml.*`) because the parser is a
hand-written `XmlDocument` + reflection engine, not a wrapper around
`System.Windows.Markup.XamlReader`. A handful of pieces have **no**
mechanical WinUI equivalent and are redesigned from scratch: markup-extension
value evaluation (`XamlObjectServiceProvider` → a new, much smaller
`WinUIXamlServiceProvider` built on WinUI's own `IXamlServiceProvider`),
`FrameworkTemplate` instantiation (`TemplateHelper`, rebuilt on
`Microsoft.UI.Xaml.Markup.XamlReader.Load`), brush serialization, named-brush
lookup, and a `DependencyProperty`-descriptor bridge that WPF gets for free
from `System.ComponentModel` and WinUI does not. Every one of these was
**verified against the real `Microsoft.UI.Xaml.winmd` metadata** with a
throwaway metadata-reader spike during planning (not guessed) — see the gap
call-outs inside each task.

**Tech Stack:** .NET 10 SDK (`10.0.302`, already installed), WinUI 3 /
Windows App SDK 1.7 (`Microsoft.WindowsAppSDK` `1.7.250606001`), NUnit 3
(matching the reference test project's framework).

**Spec:** `docs/superpowers/specs/2026-08-31-winui-xamldom-phase1-design.md`

## Global Constraints

- Target framework for all new projects: `net10.0-windows10.0.19041.0`.
- `UseWinUI=true`, `PackageReference Microsoft.WindowsAppSDK` version
  `1.7.250606001` (the version resolved during planning; do not silently
  change it — if a newer version is wanted, that's a separate decision).
- New code lives under `D:\Project\ST4I\Research\WinUIDesigner`. The
  reference repo `F:\ReferencesCode\CSharp\WpfDesigner` is read-only source
  material — never edit it.
- Test framework: NUnit 3 + NUnit3TestAdapter + Microsoft.NET.Test.Sdk
  (matching `WpfDesign.Tests.csproj`'s NUnit usage; drop RhinoMocks — nothing
  in the XamlDom test subset being ported uses it).
- Every task that touches a flagged gap (numbered below, matching the spec)
  must resolve it explicitly — no silent fallback, no swallowed exception
  that hides a platform difference.
- Commit after each task passes its verification step.

---

## Background: the tangled-core problem

`XamlObject`, `XamlProperty`, `XamlDocument`, `XamlParser`,
`MarkupExtensionParser`, and `MarkupExtensionPrinter` are mutually
referential (e.g. `XamlObject.ProvideValue()` calls into
`MarkupExtensionPrinter`, `XamlParser.ParseObject` constructs `XamlObject`
and `XamlProperty` instances, `MarkupExtensionParser.Parse` calls back into
`XamlParser.ParseObjectAttribute`). Because C# compiles a project as a
whole, none of these files can be individually compiled or behaviorally
tested in isolation — the assembly either builds complete or doesn't build
at all. Task 5 below is therefore intentionally one large task: every file
in that tangled cluster goes in together, and the first real test
(`SimpleLoadTests`) only becomes runnable once all of it is in place. This
mirrors how the pieces actually depend on each other; splitting it further
would create tasks that can't be independently verified.

---

### Task 1: Environment prerequisite + solution/project scaffolding

**Files:**
- Create: `WinUIDesigner.sln`
- Create: `src/WinUIDesigner.XamlDom/WinUIDesigner.XamlDom.csproj`
- Create: `tests/WinUIDesigner.XamlDom.Tests/WinUIDesigner.XamlDom.Tests.csproj`

**Interfaces:**
- Produces: a buildable class library project (`WinUIDesigner.XamlDom`) and
  a buildable, runnable NUnit test project referencing it. Every later task
  adds files to these two projects.

- [ ] **Step 1: Confirm the local tooling gap found during planning**

  During planning, a throwaway project with `UseWinUI=true` failed to build
  on this machine (regardless of `OutputType` or `RuntimeIdentifier`) with:
  ```
  error MSB4062: The "Microsoft.Build.Packaging.Pri.Tasks.ExpandPriContent"
  task could not be loaded from the assembly ...\AppxPackage\Microsoft.Build.Packaging.Pri.Tasks.dll.
  ```
  `vswhere` confirmed Visual Studio 2026 Professional is installed, but that
  DLL doesn't exist anywhere under the VS install or the .NET SDK. Re-run
  this check first — the environment may have changed:
  ```bash
  find "/c/Program Files/Microsoft Visual Studio" -iname "Microsoft.Build.Packaging.Pri.Tasks.dll" 2>/dev/null
  ```
  If it's still empty, this blocks every subsequent step. Tell the user:
  install the **"Universal Windows Platform development"** workload (or the
  standalone MSIX packaging tools individual component) via the Visual
  Studio Installer. This is a system-level install — do it yourself only if
  you have a non-interactive way to invoke the VS Installer with that
  workload; otherwise stop and ask the user to run it, then resume.

- [ ] **Step 2: Create the solution**

  ```bash
  cd "D:\Project\ST4I\Research\WinUIDesigner"
  dotnet new sln -n WinUIDesigner
  ```

- [ ] **Step 3: Create the library project**

  `src/WinUIDesigner.XamlDom/WinUIDesigner.XamlDom.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <OutputType>Library</OutputType>
      <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
      <RootNamespace>WinUIDesigner.XamlDom</RootNamespace>
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

  </Project>
  ```
  (`Nullable` is `disable`, not `enable` — the reference codebase predates
  nullable annotations throughout; enabling it would force annotating all
  ~7000 ported lines for no benefit in this phase.)

- [ ] **Step 4: Create the test project**

  `tests/WinUIDesigner.XamlDom.Tests/WinUIDesigner.XamlDom.Tests.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <OutputType>Library</OutputType>
      <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
      <RootNamespace>WinUIDesigner.XamlDom.Tests</RootNamespace>
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
      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
      <PackageReference Include="NUnit" Version="3.14.0" />
      <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\..\src\WinUIDesigner.XamlDom\WinUIDesigner.XamlDom.csproj" />
    </ItemGroup>

  </Project>
  ```

- [ ] **Step 5: Add both projects to the solution**

  ```bash
  dotnet sln WinUIDesigner.sln add src/WinUIDesigner.XamlDom/WinUIDesigner.XamlDom.csproj
  dotnet sln WinUIDesigner.sln add tests/WinUIDesigner.XamlDom.Tests/WinUIDesigner.XamlDom.Tests.csproj
  ```

- [ ] **Step 6: Add one placeholder class so the library builds**

  `src/WinUIDesigner.XamlDom/AssemblyInfo.cs` (mirrors the reference
  project's `Configuration/AssemblyInfo.cs`, trimmed to what still applies —
  `ComVisible`/`ThemeInfo` attributes are WPF-resource-dictionary-theming
  concepts that don't apply here):
  ```csharp
  using System.Reflection;

  [assembly: AssemblyTitle("WinUIDesigner.XamlDom")]
  [assembly: AssemblyDescription("WinUI 3 XAML object model and parser")]
  ```

- [ ] **Step 7: Build until green**

  ```bash
  dotnet build WinUIDesigner.sln
  ```
  Expected: `Build succeeded`. If the Pri-packaging error from Step 1
  resurfaces, the workload install didn't take — stop and re-check, don't
  work around it (e.g. don't strip `UseWinUI` — every later task needs the
  real `Microsoft.UI.Xaml` types).

- [ ] **Step 8: Add one smoke test**

  `tests/WinUIDesigner.XamlDom.Tests/SmokeTests.cs`:
  ```csharp
  using NUnit.Framework;

  namespace WinUIDesigner.XamlDom.Tests
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

- [ ] **Step 9: Run it**

  ```bash
  dotnet test WinUIDesigner.sln
  ```
  Expected: 1 passed.

- [ ] **Step 10: Commit**

  ```bash
  git add WinUIDesigner.sln src tests
  git commit -m "chore: scaffold WinUIDesigner.XamlDom library and test project"
  ```

---

### Task 2: Verified namespace constants + foundational leaf types

These files have zero or near-zero WPF coupling. Port each by reading the
reference file at the given path and copying it into the new namespace
`WinUIDesigner.XamlDom` (reference namespace is `ICSharpCode.WpfDesign.XamlDom`
— rename throughout), applying only the specific changes called out below.
Where no change is listed, the file is a verbatim copy (only the namespace
changes).

**Files:**
- Create: `src/WinUIDesigner.XamlDom/XamlElementLineInfo.cs` (from
  `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.XamlDom\Project\XamlElementLineInfo.cs`
  — verbatim, no WPF types at all)
- Create: `src/WinUIDesigner.XamlDom/IXamlErrorSink.cs` (from
  `...\IXamlErrorSink.cs` — verbatim)
- Create: `src/WinUIDesigner.XamlDom/MarkupExtensionWrapperAttribute.cs`
  (from `...\MarkupExtensionWrapperAttribute.cs` — verbatim; the doc-comment
  references `System.Windows.Markup.MarkupExtension` only in a `<see cref>`,
  harmless to leave or update to `Microsoft.UI.Xaml.Markup.MarkupExtension`)
- Create: `src/WinUIDesigner.XamlDom/XamlLoadException.cs` (from
  `...\XamlLoadException.cs` — verbatim)
- Create: `src/WinUIDesigner.XamlDom/XamlConstants.cs` (from
  `...\XamlConstants.cs` — see Step 2 below, values need verification)
- Create: `src/WinUIDesigner.XamlDom/XamlParserSettings.cs` (from
  `...\XamlParserSettings.cs` — one change: `XamlTypeFinder.CreateWpfTypeFinder()`
  becomes `XamlTypeFinder.CreateWinUITypeFinder()`, added in Task 3)
- Create: `src/WinUIDesigner.XamlDom/PositionXmlDocument.cs` (from
  `...\PositionXmlDocument.cs` — one change: the file has an unused
  `using System.Windows.Controls;` import; drop it, nothing in the file
  references anything from that namespace)
- Create: `src/WinUIDesigner.XamlDom/XamlXmlWriter.cs` (from
  `...\XamlXmlWriter.cs` — verbatim, it only wraps `System.Xml.XmlWriter`)
- Create: `src/WinUIDesigner.XamlDom/XamlFormatter.cs` (from
  `...\XamlFormatter.cs` — verbatim, uses only `System.Xml.Linq`)

**Interfaces:**
- Consumes: nothing from this codebase yet.
- Produces: `XamlConstants.PresentationNamespace`, `.XamlNamespace`,
  `.Xaml2009Namespace`, `.DesignTimeNamespace`, `.MarkupCompatibilityNamespace`,
  `.XmlnsNamespace`, `.ResourcesPropertyName`, `.Xmlns` (all `const string`)
  — every later task's namespace handling depends on these being correct.
  `IXamlErrorSink.ReportError(string, int, int)`. `XamlLoadException`
  (exception type). `XamlParserSettings` with settable
  `CreateInstanceCallback`, `TypeFinder`, `ServiceProvider`,
  `CurrentProjectAssemblyName`.

- [ ] **Step 1: Copy the seven verbatim/near-verbatim files**

  Read each reference file at the path listed above, write it to the new
  path with namespace `WinUIDesigner.XamlDom`, applying only the specific
  change noted (drop the unused `System.Windows.Controls` import in
  `PositionXmlDocument.cs`, update `MarkupExtensionWrapperAttribute.cs`'s
  `<see cref>` target).

- [ ] **Step 2: Verify the XAML namespace URIs before porting `XamlConstants.cs`**

  The reference `XamlConstants.cs` hardcodes:
  ```
  XmlnsNamespace = "http://www.w3.org/2000/xmlns/"
  XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml"
  Xaml2009Namespace = "http://schemas.microsoft.com/winfx/2009/xaml"
  PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation"
  DesignTimeNamespace = "http://schemas.microsoft.com/expression/blend/2008"
  MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006"
  ```
  Verify these are correct for WinUI 3 by building a real, minimal
  `MainWindow.xaml` and confirming it compiles with the XAML compiler. In
  the `WinUIDesigner.XamlDom` project directory, temporarily add:
  ```xml
  <!-- MainWindow.xaml -->
  <Window
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:local="using:WinUIDesigner.XamlDom"
      x:Class="WinUIDesigner.XamlDom.MainWindowProbe">
      <Grid />
  </Window>
  ```
  Give the library project a temporary `<ApplicationDefinition>`/`Page`
  reference to this file (or use `<Page Include="MainWindow.xaml" />` —
  since it's `UseWinUI=true`, `.xaml` files are picked up by the SDK's
  default globs) and run `dotnet build`. If the XAML compiler accepts it
  (no "namespace not recognized" / "unknown xmlns" errors), the four
  `http://schemas.microsoft.com/...` values are confirmed correct — **keep
  them unchanged**. If it fails, read the compiler error (it will name the
  expected namespace) and update `XamlConstants.cs` accordingly. Either way,
  delete the temporary `MainWindow.xaml` and any project-file changes before
  moving on — this is a verification probe, not permanent code.

- [ ] **Step 3: Build**

  ```bash
  dotnet build WinUIDesigner.sln
  ```
  Expected: succeeds (these files have no cross-file dependencies on
  anything not yet written).

- [ ] **Step 4: Commit**

  ```bash
  git add src/WinUIDesigner.XamlDom
  git commit -m "feat: port foundational XamlDom leaf types, verify WinUI xmlns constants"
  ```

---

### Task 3: `XamlTypeFinder` + design-time attached-property holder types

**Files:**
- Create: `src/WinUIDesigner.XamlDom/DesignTimeProperties.cs` (from
  `...\DesignTimeProperties.cs`)
- Create: `src/WinUIDesigner.XamlDom/MarkupCompatibilityProperties.cs`
  (from `...\MarkupCompatibilityProperties.cs`)
- Create: `src/WinUIDesigner.XamlDom/XamlNamespaceProperties.cs` (from
  `...\XamlNamespaceProperties.cs`)
- Create: `src/WinUIDesigner.XamlDom/XamlTypeFinder.cs` (from
  `...\XamlTypeFinder.cs`)
- Create: `tests/WinUIDesigner.XamlDom.Tests/XamlTypeFinderTests.cs` (from
  `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Tests\XamlDom\XamlTypeFinderTests.cs`)

**Interfaces:**
- Consumes: `XamlConstants.*` (Task 2).
- Produces: `XamlTypeFinder.CreateWinUITypeFinder()` (static factory, new
  name — see Step 3), `.GetType(string xmlNamespace, string localName)`,
  `.RegisterAssembly(Assembly)`, `.RegisterDesignerNamespaces()`,
  `.GetXmlNamespaceFor(...)`, `.GetPrefixForXmlNamespace(string)`. Later
  tasks call `XamlTypeFinder.CreateWinUITypeFinder()` wherever the reference
  code calls `XamlTypeFinder.CreateWpfTypeFinder()`.

- [ ] **Step 1: Port the three attached-property holder classes**

  `DesignTimeProperties.cs`, `MarkupCompatibilityProperties.cs`,
  `XamlNamespaceProperties.cs`: copy each from its reference path.
  Substitutions (apply to all three):
  - `using System.Windows;` → `using Microsoft.UI.Xaml;`
  - `System.Windows.DependencyObject` → `Microsoft.UI.Xaml.DependencyObject`
  - `System.Windows.DependencyProperty` → `Microsoft.UI.Xaml.DependencyProperty`
  - `DependencyProperty.RegisterAttached(...)` calls: identical signature on
    `Microsoft.UI.Xaml.DependencyProperty`, no change needed beyond the
    type.
  - `MarkupCompatibilityProperties`/`XamlNamespaceProperties` currently
    `: FrameworkElement` (WPF) purely so the type has a stable owner-type
    identity for `RegisterAttached` — change the base type to
    `Microsoft.UI.Xaml.DependencyObject` (simpler, and these classes are
    never instantiated).
  - `DesignTimeProperties.OnIsHiddenPropertyChanged` uses
    `DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, d.GetType())`
    and `dpd.AddValueChanged`/`RemoveValueChanged` — this is the
    `System.ComponentModel` DP-descriptor bridge that **does not exist in
    WinUI** (confirmed absent — see Task 5's `XamlPropertyInfo` step for the
    full explanation). For this file specifically, replace the
    `DependencyPropertyDescriptor`-based visibility-change subscription with
    WinUI's native mechanism —
    `Microsoft.UI.Xaml.DependencyObject.RegisterPropertyChangedCallback(DependencyProperty, DependencyPropertyChangedCallback)`
    (this exists on `DependencyObject` directly in WinUI, no descriptor
    bridge needed):
    ```csharp
    static void OnIsHiddenPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            EnsureHidden(d);
            d.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnVisibilityPropertyChanged);
        }
        else
        {
            d.ClearValue(UIElement.VisibilityProperty);
        }
    }

    static void OnVisibilityPropertyChanged(DependencyObject d, DependencyProperty dp)
    {
        if (GetIsHidden(d))
        {
            EnsureHidden(d);
        }
    }

    static void EnsureHidden(DependencyObject d)
    {
        if (Visibility.Visible.Equals(d.GetValue(UIElement.VisibilityProperty)))
        {
            d.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
        }
    }
    ```
    (`RegisterPropertyChangedCallback` returns a token; WinUI has no
    `SetCurrentValue`/`InvalidateProperty` equivalent either, so
    `EnsureHidden` uses plain `SetValue`/`ClearValue` — a minor behavior
    difference from WPF's "current value" semantics that doesn't matter for
    Phase 1 since nothing yet reads this property back.)

- [ ] **Step 2: Write the failing test — port `XamlTypeFinderTests.cs`**

  From the reference file, adapted:
  ```csharp
  using NUnit.Framework;
  using WinUIDesigner.XamlDom;

  namespace WinUIDesigner.XamlDom.Tests
  {
      [TestFixture]
      public class XamlTypeFinderTests
      {
          XamlTypeFinder typeFinder;

          [SetUp]
          public void FixtureSetUp()
          {
              typeFinder = XamlTypeFinder.CreateWinUITypeFinder();
          }

          [Test]
          public void FindGrid()
          {
              Assert.AreEqual(typeof(Microsoft.UI.Xaml.Controls.Grid),
                  typeFinder.GetType(XamlConstants.PresentationNamespace, "Grid"));
          }

          [Test]
          public void FindButton()
          {
              Assert.AreEqual(typeof(Microsoft.UI.Xaml.Controls.Button),
                  typeFinder.GetType(XamlConstants.PresentationNamespace, "Button"));
          }

          public const string XamlDomTestsNamespace =
              "clr-namespace:WinUIDesigner.XamlDom.Tests;assembly=WinUIDesigner.XamlDom.Tests";
      }
  }
  ```
  (No `FindStaticResourceExtension`/`FindNullExtension` here — verified
  during planning that `Microsoft.UI.Xaml.winmd` has no
  `StaticResourceExtension`/`ThemeResourceExtension`/`NullExtension`/
  `TemplateBindingExtension` type at all; see Step 5 below and the new gap
  section in Task 5. `FindBinding` is added in Step 5 once Step 4's port
  is in place, since it's what proves the hand-registered namespace list
  works. `ExampleClass` doesn't exist yet — Task 5 adds it and its own
  `FindExampleClass` test alongside `SimpleLoadTests`.)

- [ ] **Step 3: Run it, confirm it fails to compile** (nothing ported yet)

  ```bash
  dotnet test WinUIDesigner.sln --filter XamlTypeFinderTests
  ```
  Expected: build error, `XamlTypeFinder` doesn't exist.

- [ ] **Step 4: Port `XamlTypeFinder.cs`**

  From the reference file. Substitutions:
  - `using System.Windows;` and `using System.Windows.Markup;` and
    `using System.Xaml;` and `using XamlReader = System.Windows.Markup.XamlReader;`
    → drop all four (the reference file only uses these for
    `typeof(Point).Assembly`, `typeof(IAddChild).Assembly`,
    `typeof(XamlReader).Assembly`, `typeof(XamlType).Assembly` inside the
    static `WpfTypeFinder` initializer — replaced entirely, see below).
  - The one real design decision is `WpfTypeFinder`'s static constructor,
    which registers the assemblies that carry WPF's built-in XAML-visible
    types (`WindowsBase`, `PresentationCore`, `PresentationFramework`,
    `System.Xaml`, `mscorlib`) — the reference `RegisterAssembly` does this
    by scanning the assembly's `[XmlnsDefinitionAttribute]`/
    `[XmlnsPrefixAttribute]` custom attributes, a WPF-assembly-level
    convention.

    **Verified during a Task 3 implementation attempt, by direct ECMA-335
    metadata inspection of `Microsoft.UI.Xaml.winmd` (not guessed): WinUI's
    assembly carries *zero* Xmlns-related custom attributes anywhere in it**
    (checked all 12,953 type-level custom attributes in the assembly — none
    are Xmlns-related). `RegisterAssembly(typeof(Microsoft.UI.Xaml.DependencyObject).Assembly)`
    would silently register nothing and `XamlTypeFinder.GetType` would never
    resolve any WinUI control — this is not a naming slip to patch, WinUI
    genuinely does not use this convention at all. Its own XAML compiler
    resolves elements through a completely different, closed mechanism
    (`IXamlMetadataProvider`/`IXamlType`, generated per-app, not reusable
    here).

    Resolution: don't rely on attribute scanning for WinUI. Register a
    fixed, hand-maintained list of the known `Microsoft.UI.Xaml*` CLR
    namespaces directly against `XamlConstants.PresentationNamespace` (this
    is safe and correct precisely *because* Task 2 already empirically
    confirmed the WinUI XAML compiler treats a single presentation xmlns as
    covering all of these namespaces — that's why a plain `<Grid>` or
    `<Button>` element with no prefix resolves at all). This list was
    enumerated directly from `Microsoft.UI.Xaml.winmd`'s public
    namespaces (not guessed):
    ```
    Microsoft.UI.Xaml, Microsoft.UI.Xaml.Automation,
    Microsoft.UI.Xaml.Automation.Peers, Microsoft.UI.Xaml.Automation.Provider,
    Microsoft.UI.Xaml.Automation.Text, Microsoft.UI.Xaml.Controls,
    Microsoft.UI.Xaml.Controls.Primitives, Microsoft.UI.Xaml.Data,
    Microsoft.UI.Xaml.Documents, Microsoft.UI.Xaml.Input,
    Microsoft.UI.Xaml.Markup, Microsoft.UI.Xaml.Media,
    Microsoft.UI.Xaml.Media.Animation, Microsoft.UI.Xaml.Media.Imaging,
    Microsoft.UI.Xaml.Media.Media3D, Microsoft.UI.Xaml.Navigation,
    Microsoft.UI.Xaml.Shapes
    ```
    (Deliberately excluding `Microsoft.UI.Xaml.Controls.AnimatedVisuals`,
    `Microsoft.UI.Xaml.CustomAttributes`, `Microsoft.UI.Xaml.Hosting`,
    `Microsoft.UI.Xaml.Interop`, `Microsoft.UI.Xaml.Printing`,
    `Microsoft.UI.Xaml.Resources`, `Microsoft.UI.Xaml.XamlTypeInfo` — these
    are non-visual/infrastructure namespaces with no XAML-element-tag
    surface, no need to search them.)

    First, add a small internal helper to `XamlTypeFinder` itself (the outer
    class — this is new code, not in the reference, but it's a thin wrapper
    around the reference's own existing private `AddMappingToNamespace`):
    ```csharp
    // inside class XamlTypeFinder, alongside RegisterAssembly
    internal void RegisterNamespaceMapping(string xmlNamespace, Assembly assembly, string clrNamespace)
    {
        XamlNamespace ns;
        if (!namespaces.TryGetValue(xmlNamespace, out ns)) {
            ns = namespaces[xmlNamespace] = new XamlNamespace(null, xmlNamespace);
        }
        AddMappingToNamespace(ns, new AssemblyNamespaceMapping(assembly, clrNamespace));
    }
    ```
    Then rename `WpfTypeFinder` to `WinUITypeFinder`, rename the public
    factory from `CreateWpfTypeFinder()` to `CreateWinUITypeFinder()` (keep
    the old name out entirely), and use the helper above instead of
    `RegisterAssembly`:
    ```csharp
    static class WinUITypeFinder
    {
        internal static readonly XamlTypeFinder Instance;

        static readonly string[] KnownWinUINamespaces = {
            "Microsoft.UI.Xaml", "Microsoft.UI.Xaml.Automation",
            "Microsoft.UI.Xaml.Automation.Peers", "Microsoft.UI.Xaml.Automation.Provider",
            "Microsoft.UI.Xaml.Automation.Text", "Microsoft.UI.Xaml.Controls",
            "Microsoft.UI.Xaml.Controls.Primitives", "Microsoft.UI.Xaml.Data",
            "Microsoft.UI.Xaml.Documents", "Microsoft.UI.Xaml.Input",
            "Microsoft.UI.Xaml.Markup", "Microsoft.UI.Xaml.Media",
            "Microsoft.UI.Xaml.Media.Animation", "Microsoft.UI.Xaml.Media.Imaging",
            "Microsoft.UI.Xaml.Media.Media3D", "Microsoft.UI.Xaml.Navigation",
            "Microsoft.UI.Xaml.Shapes",
        };

        static WinUITypeFinder()
        {
            Instance = new XamlTypeFinder();
            Instance.RegisterDesignerNamespaces();

            var winUIAssembly = typeof(Microsoft.UI.Xaml.DependencyObject).Assembly;
            foreach (var clrNamespace in KnownWinUINamespaces) {
                Instance.RegisterNamespaceMapping(XamlConstants.PresentationNamespace, winUIAssembly, clrNamespace);
            }
        }
    }

    public static XamlTypeFinder CreateWinUITypeFinder()
    {
        return WinUITypeFinder.Instance.Clone();
    }
    ```
    (The reference's `Instance.RegisterAssembly(typeof(Type).Assembly); // mscorelib`
    line is dropped — it was a no-op in the reference too, since mscorlib
    carries no `XmlnsDefinitionAttribute` either; nothing in this codebase's
    own test/round-trip XAML references bare CLR types by unqualified tag
    name, so there's nothing lost.)
  - Everything else in the file (`AssemblyNamespaceMapping`, `XamlNamespace`,
    `GetType`, `GetXmlNamespaceFor`, `ParseNamespace`, `AddMappingToNamespace`,
    `RegisterAssembly`, `RegisterDesignerNamespaces`, `LoadAssembly`,
    `Clone`/`ImportFrom`, `ConvertUriToLocalUri`) is plain reflection/string
    logic with no WPF dependency, and stays useful for other callers (e.g.
    `RegisterAssembly` is still what registers a *user's own* project
    assembly when a `clr-namespace:` xmlns is encountered during parsing,
    Task 5's `XamlParser.ParseObject`) — copy verbatim, don't remove it just
    because `WinUITypeFinder`'s own static constructor no longer calls it.

- [ ] **Step 5: Run the test, make it pass**

  ```bash
  dotnet test WinUIDesigner.sln --filter XamlTypeFinderTests
  ```
  `FindGrid`/`FindButton` should now resolve via the hand-registered
  namespace list above. `FindStaticResourceExtension` (from Step 2's test)
  will still fail — **not a bug to chase**: verified by direct metadata
  search that `Microsoft.UI.Xaml.winmd` contains no type named
  `StaticResourceExtension` anywhere (nor `ThemeResourceExtension`,
  `NullExtension`, `TemplateBindingExtension` — checked all four). Unlike
  WPF, WinUI does not back `{StaticResource}`/`{ThemeResource}`/`{x:Null}`
  with a public, reflectable `MarkupExtension`-derived type at all — its
  XAML compiler resolves them as compiler intrinsics with no runtime object
  ever constructed for the extension itself. Delete the
  `FindStaticResourceExtension` test (it tests something WinUI doesn't
  have) and replace it with one that reflects what's actually true:
  ```csharp
  [Test]
  public void FindBinding()
  {
      Assert.AreEqual(typeof(Microsoft.UI.Xaml.Data.Binding),
          typeFinder.GetType(XamlConstants.PresentationNamespace, "Binding"));
  }
  ```
  (`Microsoft.UI.Xaml.Data.Binding` is confirmed to exist as a real type —
  unlike WPF's `Binding`, it derives from `Microsoft.UI.Xaml.Data.BindingBase`,
  **not** from `Microsoft.UI.Xaml.Markup.MarkupExtension` — this matters for
  Task 5's `MarkupExtensionParser`, see the new gap section added there.)

- [ ] **Step 6: Commit**

  ```bash
  git add src/WinUIDesigner.XamlDom tests/WinUIDesigner.XamlDom.Tests
  git commit -m "feat: port XamlTypeFinder and design-time attached properties for WinUI"
  ```

---

### Task 4: `CollectionSupport`

**Files:**
- Create: `src/WinUIDesigner.XamlDom/CollectionSupport.cs` (from
  `...\CollectionSupport.cs`)
- Create: `tests/WinUIDesigner.XamlDom.Tests/CollectionTests.cs` (from
  `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Tests\XamlDom\CollectionTests.cs`)

**Interfaces:**
- Consumes: nothing from this codebase (works on `Type`/`object` generically).
- Produces: `CollectionSupport.IsCollectionType(Type)`,
  `.CanCollectionAdd(Type, Type)`, `.AddToCollection(Type, object, XamlPropertyValue)`,
  `.Insert(...)`, `.TryInsert(...)`, `.RemoveItemAt(...)`, `.RemoveItem(...)`
  — used throughout Task 5's core object model for every collection-valued
  property.

**Gap:** the reference `IsCollectionType`/`AddToCollection` special-case
`System.Windows.Markup.IAddChild` (`RichTextBox`/`TextBlock`-style content
models that accept mixed text+element children via `AddChild`/`AddText`
rather than `IList`/`IDictionary`). Verified during planning by reading
`Microsoft.UI.Xaml.winmd`'s full type list: **no `IAddChild` interface
exists in WinUI** (searched both exact-name and substring). WinUI's
mixed-content controls (`TextBlock`, `RichTextBlock`) instead expose plain
`IList<Inline>`-shaped collections (e.g. `TextBlock.Inlines` is an
`InlineCollection` implementing standard collection interfaces) — so the
`IAddChild` branch has no purpose to replicate; WinUI's ordinary
`IList`/`IDictionary` handling already covers it.

- [ ] **Step 1: Write the failing test**

  Port `CollectionTests.cs`. The `LineBreakNoCollection` test translates
  directly (namespace swap only); `InputCollectionsPeculiarityOrBug` tests a
  WPF-specific quirk in `System.Windows.Input.InputBindingCollection`/
  `CommandBindingCollection` that has no WinUI equivalent type to test
  against — drop that test rather than inventing a fake WinUI analogue for
  a WPF-only bug-compatibility quirk:
  ```csharp
  using NUnit.Framework;
  using WinUIDesigner.XamlDom;

  namespace WinUIDesigner.XamlDom.Tests
  {
      [TestFixture]
      public class CollectionTests
      {
          [Test]
          public void LineBreakNoCollection()
          {
              var isCollection = CollectionSupport.IsCollectionType(typeof(Microsoft.UI.Xaml.Documents.LineBreak));

              Assert.IsFalse(isCollection);
          }
      }
  }
  ```

- [ ] **Step 2: Run it, confirm compile failure** (`CollectionSupport` doesn't exist yet)

- [ ] **Step 3: Port `CollectionSupport.cs`**

  Substitutions:
  - `using System.Windows;` → `using Microsoft.UI.Xaml;`
  - `using System.Windows.Documents;` → `using Microsoft.UI.Xaml.Documents;`
    (for the `typeof(LineBreak)` exclusion)
  - `using System.Windows.Markup;` → delete (only used for `IAddChild`,
    removed per the gap above)
  - `IsCollectionType`: drop the `|| typeof(IAddChild).IsAssignableFrom(type)`
    clause:
    ```csharp
    public static bool IsCollectionType(Type type)
    {
        return type != typeof(LineBreak) && (
            typeof(IList).IsAssignableFrom(type)
            || type.IsArray
            || typeof(IDictionary).IsAssignableFrom(type));
    }
    ```
  - `AddToCollection`: drop the `IAddChild addChild = collectionInstance as IAddChild; if (addChild != null) { ... }`
    branch entirely — fall through directly to the `IDictionary`/reflection-`Add`
    branches:
    ```csharp
    public static void AddToCollection(Type collectionType, object collectionInstance, XamlPropertyValue newElement)
    {
        if (collectionInstance is IDictionary) {
            object val = newElement.GetValueFor(null);
            object key = newElement is XamlObject ? ((XamlObject)newElement).GetXamlAttribute("Key") : null;
            if (key == null || (key as string) == "")
            {
                if (val is Style)
                    key = ((Style)val).TargetType;
            }
            if (key == null || (key as string) == "")
                key = val;
            ((IDictionary)collectionInstance).Add(key, val);
        } else {
            collectionType.InvokeMember(
                "Add", BindingFlags.Public | BindingFlags.InvokeMethod | BindingFlags.Instance,
                null, collectionInstance,
                new object[] { newElement.GetValueFor(null) },
                CultureInfo.InvariantCulture);
        }
    }
    ```
    (Note: this references `XamlObject`, `XamlPropertyValue`, and `Style` —
    forward references to types added in Task 5. That's fine, same
    assembly, but it means `CollectionSupport.cs` alone won't let the
    project build until Task 5 lands `XamlObject`/`XamlPropertyValue`/
    `Style`-resolution. `dotnet build` in Step 4 below will fail for that
    reason, not because of a mistake — see the note in Step 4.)
  - `Insert`, `TryInsert`, `RemoveItemAt`, `RemoveItem` (both overloads):
    copy verbatim — pure `IList`/reflection logic, no WPF types.

- [ ] **Step 4: Attempt a build, expect a specific kind of failure**

  ```bash
  dotnet build WinUIDesigner.sln
  ```
  Expected: fails with "type or namespace `XamlObject`/`XamlPropertyValue`
  could not be found" — **this is expected**, not a bug in this task.
  `CollectionSupport.cs` forward-references types Task 5 hasn't created
  yet. Confirm the *only* errors are missing-type errors for `XamlObject`,
  `XamlPropertyValue`, and `Style` (the WinUI `Microsoft.UI.Xaml.Style` type
  itself should resolve fine via `using Microsoft.UI.Xaml;`) — if there are
  any *other* errors (typos, wrong namespaces), fix those now.

- [ ] **Step 5: Commit** (with the build still red on the two forward
  references — this is a deliberate, documented interim state)

  ```bash
  git add src/WinUIDesigner.XamlDom/CollectionSupport.cs tests/WinUIDesigner.XamlDom.Tests/CollectionTests.cs
  git commit -m "feat: port CollectionSupport for WinUI (drop unavailable IAddChild path)"
  ```

  `LineBreakNoCollection` will be run and confirmed passing as part of
  Task 5's final test pass, once the project builds end-to-end.

---

### Task 5: The core object model and parser

This is the large, necessarily-monolithic task described in "Background"
above. It ports 14 reference files plus adds 1 new file
(`WinUIXamlServiceProvider.cs`), then ports the test fixtures and the two
simplest end-to-end test files. Nothing in this cluster is individually
testable until all of it exists, so the steps below are ordered for
readability (roughly leaf-to-root), not for incremental green builds.

**Files:**
- Create: `src/WinUIDesigner.XamlDom/XamlPropertyValue.cs` (from
  `...\XamlPropertyValue.cs`)
- Create: `src/WinUIDesigner.XamlDom/XamlTextValue.cs` (from
  `...\XamlTextValue.cs`)
- Create: `src/WinUIDesigner.XamlDom/XamlPropertyInfo.cs` (from
  `...\XamlPropertyInfo.cs`)
- Create: `src/WinUIDesigner.XamlDom/CollectionElementsCollection.cs` (from
  `...\CollectionElementsCollection.cs`)
- Create: `src/WinUIDesigner.XamlDom/NameScopeHelper.cs` (from
  `...\NameScopeHelper.cs` — see gap call-out, heavily rewritten)
- Create: `src/WinUIDesigner.XamlDom/MarkupExtensionWrapper.cs` (from
  `...\MarkupExtensionWrapper.cs`)
- Create: `src/WinUIDesigner.XamlDom/WinUIXamlServiceProvider.cs` (new file,
  full code below — replaces `XamlObjectServiceProvider.cs`)
- Create: `src/WinUIDesigner.XamlDom/XamlTypeResolverProvider.cs` (from
  `...\XamlTypeResolverProvider.cs`)
- Create: `src/WinUIDesigner.XamlDom/XamlObject.cs` (from `...\XamlObject.cs`)
- Create: `src/WinUIDesigner.XamlDom/XamlProperty.cs` (from `...\XamlProperty.cs`)
- Create: `src/WinUIDesigner.XamlDom/XamlDocument.cs` (from `...\XamlDocument.cs`)
- Create: `src/WinUIDesigner.XamlDom/MarkupExtensionParser.cs` (from
  `...\MarkupExtensionParser.cs`)
- Create: `src/WinUIDesigner.XamlDom/MarkupExtensionPrinter.cs` (from
  `...\MarkupExtensionPrinter.cs`)
- Create: `src/WinUIDesigner.XamlDom/XamlParser.cs` (from `...\XamlParser.cs`)
- Create: `src/WinUIDesigner.XamlDom/XamlStaticTools.cs` (from
  `...\XamlStaticTools.cs`)
- Create: `src/WinUIDesigner.XamlDom/DesignInstanceExtension.cs` (from
  `...\DesignInstanceExtension.cs`)
- Create: `src/WinUIDesigner.XamlDom/TemplateHelper.cs` (new — minimal base
  version; `XamlParser.cs` (Step 13) calls `TemplateHelper.GetFrameworkTemplate`,
  so this file must exist before Task 5's build can succeed. Task 8 later
  extends this same file with ancestor-resource splicing — it does not
  create it from scratch.)
- Create: `tests/WinUIDesigner.XamlDom.Tests/ExampleClass.cs`,
  `ExampleControl.cs`, `ExampleClassContainer.cs`, `ExampleService.cs`,
  `TestHelper.cs` (from the matching files under
  `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Tests\XamlDom`)
- Create: `tests/WinUIDesigner.XamlDom.Tests/SimpleLoadTests.cs` (from
  `...\SimpleLoadTests.cs`, subset)
- Create: `tests/WinUIDesigner.XamlDom.Tests/WhitespaceTests.cs` (from
  `...\WhitespaceTests.cs`)

**Interfaces:**
- Consumes: `XamlConstants`, `XamlTypeFinder`, `CollectionSupport`,
  `XamlLoadException`, `IXamlErrorSink`, `XamlParserSettings`,
  `PositionXmlDocument`, `XamlXmlWriter`, `XamlFormatter` (Tasks 2–4).
- Produces: `XamlParser.Parse(TextReader) : XamlDocument`,
  `XamlDocument.RootInstance : object`, `XamlDocument.RootElement : XamlObject`,
  `XamlObject.Instance`, `.Properties`, `.FindOrCreateProperty(string)`,
  `XamlProperty.ValueOnInstance`, `.PropertyValue`, `.IsSet` — the complete
  public surface every later phase (2–5) builds on.

#### Gap 5 in depth: `WinUIXamlServiceProvider.cs` (new file, replaces `XamlObjectServiceProvider.cs`)

Verified against `Microsoft.UI.Xaml.winmd` during planning:
`Microsoft.UI.Xaml.Markup.MarkupExtension` has a
`ProvideValue(IXamlServiceProvider serviceProvider)` overload (alongside a
parameterless `ProvideValue()`). `Microsoft.UI.Xaml.IXamlServiceProvider` is
a single-method interface (`GetService(Type)`). The services it's expected
to hand back —`Microsoft.UI.Xaml.Markup.IProvideValueTarget`
(`TargetObject`/`TargetProperty`), `IXamlTypeResolver` (`Resolve(string)`),
`IRootObjectProvider` (`RootObject`) — were all confirmed to exist in the
winmd, all far smaller than their `System.Xaml` counterparts. WinUI's
contract has **no** `IAmbientProvider`/`IXamlNameResolver`/`XamlSchemaContext`
equivalent, so none of the reference `XamlObjectServiceProvider`'s
ambient-value-search code (`GetAllAmbientValues`, `SystemXamlTypeForProperty`
handling) needs porting — it's simply dropped.

**Open risk, to resolve inside this task, not before:** the winmd only
proves the *shape* exists; it doesn't prove that calling
`.ProvideValue(ourProvider)` on a real, user-written custom
`Microsoft.UI.Xaml.Markup.MarkupExtension` subclass actually runs its real
logic rather than a default/stub — WinUI's `MarkupExtension` declares
*both* overloads as non-abstract `virtual` (unlike WPF where the
`IServiceProvider` overload is abstract and mandatory to override), so a
subclass might put its real logic behind the *parameterless* `ProvideValue()`
instead, with the `IXamlServiceProvider` overload falling through to an
unhelpful default. (Note this open risk is narrower than it first looked
during planning: per Gap 7 below, `Binding` doesn't derive from
`MarkupExtension` at all on WinUI — it's handled entirely by
`BindingWrapper`, never through this generic path — and
`StaticResource`/`ThemeResource`/`Null` are this codebase's own
placeholder types whose `ProvideValue()` deliberately returns `null` and
is never meant to be the real resolution path either, see Gap 7. This open
risk is about *third-party custom* markup extensions only.) Step 9 below is
a written probe test that settles this empirically before
`XamlObject.ProvideValue()` commits to one call site.

```csharp
// src/WinUIDesigner.XamlDom/WinUIXamlServiceProvider.cs
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace WinUIDesigner.XamlDom
{
    /// <summary>
    /// Backs Microsoft.UI.Xaml.Markup.MarkupExtension.ProvideValue(IXamlServiceProvider)
    /// for markup extensions parsed out of the XAML DOM. Deliberately much smaller
    /// than WPF's System.Xaml-based service provider: WinUI's contract has no
    /// ambient-value search, no name resolver, and no schema context service.
    /// </summary>
    public class WinUIXamlServiceProvider : IXamlServiceProvider, IProvideValueTarget, IXamlTypeResolver, IRootObjectProvider
    {
        public WinUIXamlServiceProvider(XamlObject obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            XamlObject = obj;
            Resolver = new XamlTypeResolverProvider(obj);
        }

        public XamlObject XamlObject { get; }
        internal XamlTypeResolverProvider Resolver { get; }

        public object GetService(Type type)
        {
            if (type == typeof(IProvideValueTarget)) return this;
            if (type == typeof(IXamlTypeResolver)) return this;
            if (type == typeof(XamlTypeResolverProvider)) return Resolver;
            if (type == typeof(IRootObjectProvider)) return this;
            return null;
        }

        public object TargetObject
        {
            get
            {
                var parentProperty = XamlObject.ParentProperty;
                if (parentProperty == null)
                    return null;
                if (parentProperty.IsCollection)
                    return parentProperty.ValueOnInstance;
                return parentProperty.ParentObject.Instance;
            }
        }

        public object TargetProperty
        {
            get
            {
                var parentProperty = XamlObject.ParentProperty;
                return parentProperty?.DependencyProperty;
            }
        }

        public Type Resolve(string qualifiedTypeName) => Resolver.Resolve(qualifiedTypeName);

        public object RootObject => XamlObject.OwnerDocument.RootInstance;
    }
}
```

#### Gap (new, found while reading `XamlPropertyInfo.cs`): no `DependencyPropertyDescriptor` bridge

WPF's `System.ComponentModel.TypeDescriptor` has a built-in bridge
(`System.Windows.DependencyPropertyDescriptor`) that lets ordinary
`PropertyDescriptor`-based reflection recover the underlying
`DependencyProperty` for a CLR wrapper property (`XamlNormalPropertyInfo`'s
constructor uses `DependencyPropertyDescriptor.FromProperty(propertyDescriptor)`
for exactly this). WinUI's `DependencyProperty` has no such
`TypeDescriptor` integration — it's a plain CLR type with no
`System.ComponentModel` bridge at all. Replace the descriptor-based lookup
with the same static-field reflection pattern `XamlParser.TryFindAttachedProperty`
already uses for attached properties, applied to instance properties: look
for a public static field named `<PropertyName>Property` of type
`Microsoft.UI.Xaml.DependencyProperty` on the declaring type (or its base
types).

#### `XamlObject.cs` gap: no `System.Windows.NameScope`

`XamlParser.ParseObject` calls
`NameScope.SetNameScope((DependencyObject)obj.Instance, new NameScope())` on
the root object, and `NameScopeHelper` walks up the tree looking for an
`INameScope` via `obj.Instance as INameScope` or `NameScope.GetNameScope(depObj)`.
Verified against `Microsoft.UI.Xaml.winmd`: **neither `NameScope` nor
`INameScope` exists anywhere in WinUI** (exact-name and substring search
both empty). `FrameworkElement` only exposes `FindName(string)` — a lookup
against the XAML-compiler-generated name table, with no public way to
create or populate a namescope object programmatically. This means
`{x:Reference}` **value resolution** (not parsing — parsing a `{x:Reference name}`
attribute into a `XamlObject` works fine, it's plain text tokenizing) has no
direct port path for a reflection-based, non-compiled parser like this one.

Resolution for Phase 1: `NameScopeHelper` is ported as a **much smaller**
helper that only tracks name→`XamlObject` mappings in a plain
`Dictionary<string, XamlObject>` per root document (not per-scope, since
WinUI gives us no scope boundaries to respect), stored on `XamlDocument`.
`NameScopeHelper.GetNameScopeFromObject` and the `INameScope`-based API are
dropped; `NameScopeHelper.NameChanged` updates the dictionary directly;
`XamlObjectServiceProvider`'s `IXamlNameResolver.Resolve` (dropped per Gap 5
above, since WinUI's `IXamlServiceProvider` contract doesn't include a name
resolver) is replaced by a resolution path used only inside a new
`ReferenceExtensionWrapper` (see Step 6). This is a real, working
replacement for *this codebase's own* `{x:Reference}` handling — it does
not attempt to replicate WPF's general-purpose `NameScope` object model,
because WinUI has nothing to replicate it against.

#### Gap 7 (new, found during a Task 3 implementation attempt): several standard `{...}` markup extensions have no backing WinUI type, and `Binding`/`RelativeSource` aren't `MarkupExtension` at all

Verified by direct ECMA-335 metadata inspection of `Microsoft.UI.Xaml.winmd`
(not guessed): there is no type named `StaticResourceExtension`,
`ThemeResourceExtension`, `NullExtension`, or `TemplateBindingExtension`
anywhere in the assembly. WPF backs `{StaticResource ...}`/
`{ThemeResource ...}`/`{x:Null}`/`{TemplateBinding ...}` with real,
reflectable `MarkupExtension`-derived classes; WinUI's XAML compiler treats
all four as compiler intrinsics with no public runtime type behind the
extension itself. Separately, `Microsoft.UI.Xaml.Data.Binding` **does**
exist as a real type, but its base type is `Microsoft.UI.Xaml.Data.BindingBase`
— **not** `Microsoft.UI.Xaml.Markup.MarkupExtension` (confirmed by reading
the type's base-type metadata directly), unlike WPF where
`Binding : BindingBase : MarkupExtension`. `Microsoft.UI.Xaml.Data.RelativeSource`
exists too, deriving from plain `DependencyObject`.

This breaks two assumptions baked into the reference `MarkupExtensionParser.Parse`
(read closely in Step 11 below):
1. It resolves `{TypeName ...}` by looking up a type named `TypeName` or
   `TypeNameExtension` and requires
   `typeof(MarkupExtension).IsAssignableFrom(extensionType)` — this throws
   for `{Binding ...}`/`{RelativeSource ...}` on WinUI, since neither
   resolved type is a `MarkupExtension`.
2. For `{StaticResource ...}`/`{ThemeResource ...}`/`{x:Null}`, there is no
   type to resolve *at all* — `typeResolver.Resolve("StaticResourceExtension")`
   returns null on WinUI, so parsing throws before construction is even
   attempted. Since `{StaticResource}` in particular is used in nearly all
   real-world WinUI XAML (theme brushes, control styles), this would break
   parsing broadly, not just as an edge case.

**Resolution, applied in Step 11 below:**
- Relax `MarkupExtensionParser.Parse`'s type-resolution gate: drop the
  `!typeof(MarkupExtension).IsAssignableFrom(extensionType)` requirement
  entirely. Any type resolved for the `{TypeName ...}` syntax is
  constructible via this codebase's own reflection-based logic regardless
  of what it derives from — `Binding`/`RelativeSource` construct and
  populate correctly through the existing ctor-args/property-mapping logic
  with no other change needed. `XamlObject.IsMarkupExtension` already
  requires no change either — `CreateWrapper` (Step 7) checks
  `Instance is BindingBase` directly, before the `IsMarkupExtension` gate,
  exactly matching the reference's own structure (the reference already
  handles `Binding` this way; WinUI just makes it necessary for `Binding`
  specifically rather than optional).
- For the four intrinsics with **no backing type at all**
  (`StaticResource`/`ThemeResource`/`Null`/`TemplateBinding`), this
  codebase defines its own small placeholder classes — owned by
  `WinUIDesigner.XamlDom`, not WinUI — that derive from
  `Microsoft.UI.Xaml.Markup.MarkupExtension` purely so `IsMarkupExtension`
  and the existing `MarkupExtensionWrapper` registry mechanism keep working
  unchanged for them. Add this to `MarkupExtensionParser.cs` (Step 11):
  ```csharp
  // in namespace WinUIDesigner.XamlDom, alongside MarkupExtensionParser

  /// <summary>
  /// WinUI has no public runtime type behind {StaticResource ...} (confirmed
  /// absent from Microsoft.UI.Xaml.winmd, unlike WPF's real
  /// StaticResourceExtension class) -- its XAML compiler resolves it as a
  /// pure compiler intrinsic. This placeholder exists only so this codebase's
  /// own MarkupExtensionParser has something constructible to represent it.
  /// </summary>
  public sealed class StaticResourceExtensionPlaceholder : Microsoft.UI.Xaml.Markup.MarkupExtension
  {
      public object ResourceKey { get; set; }
      protected override object ProvideValue() => null; // real resolution happens in StaticResourceWrapper (Step 7)
  }

  public sealed class ThemeResourceExtensionPlaceholder : Microsoft.UI.Xaml.Markup.MarkupExtension
  {
      public object ResourceKey { get; set; }
      protected override object ProvideValue() => null;
  }

  public sealed class NullExtensionPlaceholder : Microsoft.UI.Xaml.Markup.MarkupExtension
  {
      protected override object ProvideValue() => null;
  }
  ```
  (`TemplateBinding` is intentionally not given a placeholder — it's only
  legal inside a `ControlTemplate`, which Phase 1's `TemplateHelper`
  delegates entirely to WinUI's own `XamlReader.Load` (Task 5 Step 14b,
  Task 8) rather than this codebase's own object model, so
  `MarkupExtensionParser` never needs to parse a bare `{TemplateBinding ...}`
  itself in Phase 1's scope. If a future phase parses template content
  through this codebase's own parser instead of `XamlReader.Load`, add a
  placeholder for it then.)

  Then, in `MarkupExtensionParser.Parse` (Step 11), resolve these three
  names *before* falling through to the generic `typeResolver.Resolve(...)`
  path:
  ```csharp
  // at the top of MarkupExtensionParser.Parse, after tokenizing and
  // extracting typeName (see Step 11's full listing):
  Type extensionType;
  switch (typeName) {
      case "StaticResource": extensionType = typeof(StaticResourceExtensionPlaceholder); break;
      case "ThemeResource": extensionType = typeof(ThemeResourceExtensionPlaceholder); break;
      case "Null": extensionType = typeof(NullExtensionPlaceholder); break;
      default:
          var typeResolver = parent.ServiceProvider.Resolver;
          extensionType = typeResolver.Resolve(typeName + "Extension");
          if (extensionType == null) extensionType = typeResolver.Resolve(typeName);
          if (extensionType == null)
              throw new XamlMarkupExtensionParseException("Unknown markup extension " + typeName + "Extension");
          break;
  }
  ```
  (Note the `!typeof(MarkupExtension).IsAssignableFrom(extensionType)` check
  from the reference is gone entirely, per the relaxation above — not just
  skipped for the three special-cased names.)
  For the `StaticResource`/`ThemeResource` placeholders, their single
  positional argument (the resource key) maps to the `ResourceKey`
  property the same way the reference's existing "map positional args to
  properties" logic already works for any extension with a matching
  default constructor — no special-casing needed there, the placeholder
  classes just need that property to exist with a matching name.
- `XamlDocument.CreateNullValue()` (Step 10): change `new NullExtension()`
  to `new NullExtensionPlaceholder()`.
- `XamlObject.CreateWrapper()` (Step 7): change the
  `Instance is StaticResourceExtension` check to
  `Instance is StaticResourceExtensionPlaceholder`, and
  `StaticResourceWrapper.ProvideValue()`'s
  `target.ResourceKey` access still works unchanged (same property name on
  the placeholder).
- `MarkupExtensionPrinter.cs` (Step 12): change
  `obj.ElementType == typeof(StaticResourceExtension)` to
  `typeof(StaticResourceExtensionPlaceholder)`, and
  `IsStaticResourceThatReferencesLocalResource`'s
  `obj.Instance as System.Windows.StaticResourceExtension` to
  `obj.Instance as StaticResourceExtensionPlaceholder`.
- `XamlTypeResolverProvider.cs`/`WinUIXamlServiceProvider.cs` (Steps 5-6):
  no change needed — these don't reference `StaticResourceExtension` by
  name.

- [ ] **Step 1: Port the straightforward value/property-info types**

  `XamlPropertyValue.cs`: verbatim copy, zero WPF dependency
  (`abstract class XamlPropertyValue` wrapping `XmlNode`/events only).

  `XamlTextValue.cs`: copy from reference; drop the unused
  `using System.Windows.Markup;` (nothing in the file body references any
  type from it); everything else — `Text` get/set, `NormalizeWhitespace`,
  `GetValueFor`, `RemoveNodeFromParent`, `AddNodeTo`, `GetNodeForCollection`
  — copies verbatim.

  `XamlPropertyInfo.cs`: copy from reference. Substitutions:
  - `using System.Windows;` → `using Microsoft.UI.Xaml;`
  - `using System.Windows.Markup;` → `using Microsoft.UI.Xaml.Markup;` (for
    `IXamlTypeResolver` in `TypeTypeConverter.ConvertFrom`)
  - `XamlDependencyPropertyInfo`: copy verbatim — `DependencyProperty`,
    `DependencyObject.GetValue/SetValue/ClearValue` all exist on
    `Microsoft.UI.Xaml.DependencyProperty`/`DependencyObject` with the same
    shape.
  - `XamlNormalPropertyInfo`'s constructor: replace the
    `DependencyPropertyDescriptor.FromProperty` call per the gap above:
    ```csharp
    public XamlNormalPropertyInfo(PropertyDescriptor propertyDescriptor)
    {
        this._propertyDescriptor = propertyDescriptor;
        dependencyProperty = FindDependencyPropertyField(propertyDescriptor.ComponentType, propertyDescriptor.Name);
    }

    static DependencyProperty FindDependencyPropertyField(Type type, string propertyName)
    {
        for (var t = type; t != null; t = t.BaseType) {
            var field = t.GetField(propertyName + "Property", BindingFlags.Public | BindingFlags.Static);
            if (field != null && field.FieldType == typeof(DependencyProperty))
                return (DependencyProperty)field.GetValue(null);
        }
        return null;
    }
    ```
  - Everything else in `XamlNormalPropertyInfo` and all of
    `XamlEventPropertyInfo` copies verbatim.

- [ ] **Step 2: Port `CollectionElementsCollection.cs`**

  Substitutions:
  - `using System.Windows.Documents;` → `using Microsoft.UI.Xaml.Documents;`
    (for `InlineCollection`, used only in `RemoveItem`'s `is InlineCollection`
    check)
  - Everything else copies verbatim — it's `Collection<XamlPropertyValue>`
    plumbing that calls into `CollectionSupport` (Task 4) and
    `XamlPropertyInfo` (Step 1 above).

- [ ] **Step 3: Port `NameScopeHelper.cs`** (rewritten per the gap above)

  ```csharp
  using System.Collections.Generic;

  namespace WinUIDesigner.XamlDom
  {
      /// <summary>
      /// Tracks x:Name -> XamlObject for the owning document. WinUI has no public
      /// NameScope/INameScope object model (verified absent from Microsoft.UI.Xaml.winmd),
      /// so this keeps a flat per-document table instead of WPF's nested-scope model.
      /// </summary>
      public static class NameScopeHelper
      {
          public static void NameChanged(XamlObject namedObject, string oldName, string newName)
          {
              var names = namedObject.OwnerDocument.NamedObjects;
              if (oldName != null)
                  names.Remove(oldName);
              if (newName != null)
                  names[newName] = namedObject;
          }

          public static XamlObject FindNamedObject(XamlDocument document, string name)
          {
              return document.NamedObjects.TryGetValue(name, out var obj) ? obj : null;
          }
      }
  }
  ```
  This needs `XamlDocument.NamedObjects : Dictionary<string, XamlObject>` —
  add that when porting `XamlDocument.cs` in Step 8.

- [ ] **Step 4: Port `MarkupExtensionWrapper.cs`**

  Substitutions:
  - `using System.Windows.Markup;` → `using Microsoft.UI.Xaml.Markup;` (for
    `MarkupExtension` in the `IsSubclassOf` checks)
  - Everything else — the `s_MarkupExtensionWrappers` dictionary,
    `RegisterMarkupExtensionWrapper`, `CreateWrapper`, `TryCreateWrapper` —
    copies verbatim.

- [ ] **Step 5: Add `WinUIXamlServiceProvider.cs`**

  Full code given above under "Gap 5 in depth" — write it as-is.

- [ ] **Step 6: Port `XamlTypeResolverProvider.cs`**

  Substitutions:
  - `using System.Windows;` → `using Microsoft.UI.Xaml;`
  - `using System.Windows.Markup;` → `using Microsoft.UI.Xaml.Markup;` (for
    `IXamlTypeResolver`, `Style`)
  - `IXamlTypeResolver` implementation shape (`Resolve(string)`) matches the
    WinUI interface exactly — no change to method signatures.
  - `ResolveProperty`'s `Style style = obj.Instance as Style` — `Style` and
    `Style.TargetType` exist on `Microsoft.UI.Xaml.Style` with the same
    shape; no change.
  - `FindResource`/`FindLocalResource` walk `FrameworkElement.Resources` —
    exists identically on `Microsoft.UI.Xaml.FrameworkElement`; no change.
  - Everything else copies verbatim.

  While in this file, also add a small `ReferenceExtensionWrapper` (new,
  small — resolves Gap "no NameScope" above for the one case this codebase
  actually needs it for: `{x:Reference}`). Append to
  `WinUIXamlServiceProvider.cs` (not a new file — it's a `MarkupExtensionWrapper`,
  small enough to keep beside its registration):
  ```csharp
  // in WinUIXamlServiceProvider.cs, after the WinUIXamlServiceProvider class
  class ReferenceExtensionWrapper : MarkupExtensionWrapper
  {
      public ReferenceExtensionWrapper(XamlObject xamlObject) : base(xamlObject) { }

      public override object ProvideValue()
      {
          var nameProperty = XamlObject.Properties[0]; // {x:Reference name} has exactly one positional arg, mapped to a property
          var name = (nameProperty.PropertyValue as XamlTextValue)?.Text;
          if (name == null)
              return null;
          var target = NameScopeHelper.FindNamedObject(XamlObject.OwnerDocument, name);
          return target?.Instance;
      }
  }
  ```
  This needs a registration call — add
  `MarkupExtensionWrapper.RegisterMarkupExtensionWrapper(typeof(Microsoft.UI.Xaml.Markup.ReferenceExtension), typeof(ReferenceExtensionWrapper));`
  wherever `XamlObject`'s static setup runs (Step 7's `CreateWrapper`
  method already checks a registry — add this registration in a static
  constructor on `XamlObject`, mirroring how `BindingWrapper`/
  `StaticResourceWrapper` are wired via direct `is` checks in `CreateWrapper`
  rather than the registry — for `Reference`, use the same direct-`is`-check
  style instead of the registry for consistency, see Step 7).

  **Verify `Microsoft.UI.Xaml.Markup.ReferenceExtension` actually exists**
  before wiring this up — it wasn't part of the original winmd probe list.
  If the type name differs or doesn't exist, this step's `is` check in
  `XamlObject.CreateWrapper` (Step 7) simply won't match anything, and
  `{x:Reference}` parses (as any other markup extension does) but its value
  resolves to `null` — acceptable and worth a one-line comment, not a build
  break.

- [ ] **Step 7: Port `XamlObject.cs`**

  Substitutions:
  - `using System.Windows.Markup;` → `using Microsoft.UI.Xaml.Markup;`
  - `using System.Windows.Data;` → `using Microsoft.UI.Xaml.Data;` (for
    `BindingBase`/`Binding` in `CreateWrapper`/`BindingWrapper`)
  - `using System.Windows;` → `using Microsoft.UI.Xaml;`
  - Delete `using System.Xaml;` entirely, and delete the whole
    `SystemXamlTypeForProperty` property (lines 215–228 of the reference
    file) — it exists only to feed `XamlObjectServiceProvider`'s dropped
    `IAmbientProvider` implementation (Gap 5) and `XamlProperty`'s
    `SystemXamlMemberForProperty` (also being dropped, Step 8). Nothing else
    in the codebase reads it (confirmed by grepping every reference to
    `SystemXamlTypeForProperty`/`SystemXamlMemberForProperty` across the 27
    reference files during planning — the only call sites were inside
    `XamlObjectServiceProvider.GetAllAmbientValues` and `XamlProperty`'s own
    property, both being dropped).
  - `ServiceProvider = new XamlObjectServiceProvider(this);` →
    `ServiceProvider = new WinUIXamlServiceProvider(this);`, and the
    `public XamlObjectServiceProvider ServiceProvider { get; set; }`
    property's type changes to `WinUIXamlServiceProvider`.
  - `CreateWrapper()`: `BindingWrapper`/`StaticResourceWrapper`'s `is`
    checks change to `Microsoft.UI.Xaml.Data.BindingBase` and
    `StaticResourceExtensionPlaceholder` (this codebase's own placeholder
    type, added in Step 11 per Gap 7 above — WinUI has no real
    `StaticResourceExtension` type to check against). Per Gap 7,
    `Microsoft.UI.Xaml.Data.BindingBase` is **confirmed** to derive from
    plain `Microsoft.UI.Xaml.DependencyObject`, not
    `Microsoft.UI.Xaml.Markup.MarkupExtension` (unlike WPF) — so
    `IsMarkupExtension` for a `Binding` XamlObject is always `false`, and
    `CreateWrapper`'s `if (wrapper == null && IsMarkupExtension)` fallback
    path correctly never runs for it; `BindingWrapper` is what handles
    `Binding` entirely, via the unconditional `is BindingBase` check below,
    same as the reference structure already does.
    Add the `Reference` case from Step 6:
    ```csharp
    void CreateWrapper()
    {
        if (Instance is BindingBase) {
            wrapper = new BindingWrapper(this);
        } else if (Instance is StaticResourceExtensionPlaceholder) {
            wrapper = new StaticResourceWrapper(this);
        } else if (Instance is Microsoft.UI.Xaml.Markup.ReferenceExtension) {
            wrapper = new ReferenceExtensionWrapper(this);
        }

        if (wrapper == null && IsMarkupExtension) {
            var markupExtensionWrapperAttribute = Instance.GetType().GetCustomAttributes(typeof(MarkupExtensionWrapperAttribute), false).FirstOrDefault() as MarkupExtensionWrapperAttribute;
            if (markupExtensionWrapperAttribute != null) {
                wrapper = MarkupExtensionWrapper.CreateWrapper(markupExtensionWrapperAttribute.MarkupExtensionWrapperType, this);
            } else {
                wrapper = MarkupExtensionWrapper.TryCreateWrapper(Instance.GetType(), this);
            }
        }
    }
    ```
    (If `Microsoft.UI.Xaml.Markup.ReferenceExtension` doesn't exist per
    Step 6's caveat, drop that `else if` branch — `{x:Reference}` then just
    falls through to the generic `ProvideValue(ServiceProvider)` path in
    `ProvideValue()` below, which will likely throw or return null since
    there's no real logic behind it; that's the documented, accepted
    limitation, not a silent failure — it surfaces as a failing assertion in
    Task 7's `NamescopeTest`, not a swallowed exception.)
  - `ProvideValue()`: the `Setter`/`DynamicResourceExtension` special case
    (`this.ParentObject.ElementType == typeof(Setter) && this.ElementType == typeof(DynamicResourceExtension)`)
    — `Setter` and `DynamicResourceExtension` both exist on
    `Microsoft.UI.Xaml`, just change the `using`. The final line —
    `return (Instance as MarkupExtension).ProvideValue(ServiceProvider);` —
    is the exact call site the Step "Open risk" note above is about. Write
    it initially as:
    ```csharp
    return (Instance as MarkupExtension).ProvideValue(ServiceProvider);
    ```
    and resolve which overload actually runs real logic in Step 9 below,
    updating this line if needed.
  - `BindingWrapper.ProvideValue()`/`CopyBinding`: change `BindingBase b`/
    `new Binding()` to the WinUI types; `TypeDescriptor.GetProperties(target)`
    stays — it's plain `System.ComponentModel` reflection over a WinUI
    object, unaffected by the missing `DependencyPropertyDescriptor` bridge
    (that bridge only affected recovering the *owning DependencyProperty*
    from a descriptor, not basic property enumeration).
  - `StaticResourceWrapper.ProvideValue()`: `XamlObject.ServiceProvider.Resolver.FindResource(...)`
    — `Resolver` is now `internal XamlTypeResolverProvider Resolver` on
    `WinUIXamlServiceProvider` (already exposed in Step 5's code) — no
    change needed beyond the type rename.
  - Everything else (`AddProperty`, `GetValueFor`, `VirtualAttachTo`,
    `GetContentPropertyName`, `XamlSetTypeConverter`/`GetTypeConverterDelegate`,
    `AddNodeTo`/`RemoveNodeFromParent`, `OnPropertyChanged`,
    `UpdateChildMarkupExtensions`/`UpdateMarkupExtensionChain`,
    `FindOrCreateProperty`/`FindOrCreateAttachedProperty`, `GetXamlAttribute`/
    `SetXamlAttribute`, `GetNameForMarkupExtension`) copies verbatim — none
    of it references a type outside what's already been substituted above.
    Note: `GetTypeConverterDelegate` uses `XamlSetTypeConverterAttribute`/
    `XamlSetTypeConverterEventArgs` from `System.Windows.Markup` — **verify
    these exist in `Microsoft.UI.Xaml.Markup`** when you write this file; if
    they don't (this is an obscure, rarely-used WPF feature for custom
    type-converter hooks — not confirmed present or absent during planning),
    drop `XamlSetTypeConverter`/`GetTypeConverterDelegate` entirely and the
    one call site in `XamlProperty.UpdateValueOnInstance` (Step 8) that uses
    it — document the removal with a one-line comment, don't silently stub
    it.

- [ ] **Step 8: Port `XamlProperty.cs`**

  Substitutions:
  - `using System.Windows;` → `using Microsoft.UI.Xaml;`
  - `using System.Windows.Markup;` → `using Microsoft.UI.Xaml.Markup;`
  - Delete `using System.Xaml;`, delete `SystemXamlMemberForProperty` and
    `SystemXamlTypeForProperty` (lines 282–311 of the reference file) —
    same reasoning as `XamlObject`'s equivalent removal in Step 7; the only
    consumer was the (already-dropped) `XamlSetTypeConverter` call in
    `UpdateValueOnInstance` and the ambient-provider code.
  - `UpdateValueOnInstance()`: remove the
    `if (this.parentObject.XamlSetTypeConverter != null && propertyValue is XamlTextValue) { ... }`
    block (depends on the just-deleted `SystemXamlMemberForProperty` and,
    per Step 7's caveat, possibly the deleted `XamlSetTypeConverter`
    mechanism entirely). Keep the `DesignWidthProperty`/`DesignHeightProperty`
    special-casing — `DesignTimeProperties`/`FrameworkElement.WidthProperty`/
    `HeightProperty` all exist on the WinUI side with the same shape, just
    change the `using`.
  - `PropertyName == parentObject.RuntimeNameProperty` / `PossiblyNameChanged`:
    calls `NameScopeHelper.NameChanged(ParentObject, oldName, newName)` —
    matches Step 3's rewritten signature exactly, no change needed at the
    call site.
  - Everything else (`SetPropertyValue`, `Reset`/`ResetInternal`,
    `ParserSetPropertyElement`, `CreatePropertyElement`/`GetPropertyElementType`,
    `FindChildNode`, `IsNodeCollectionForThisProperty`, `AddChildNodeToProperty`,
    `InsertNodeInCollection`, `SetAttribute`, `GetNameForMarkupExtension`,
    `ParserAddCollectionElement`, `ValueOnInstance`/`DesignerValue`/
    `TextValueOnInstance`, `IsAdvanced`, `DependencyProperty`,
    `GetValueOnInstance<T>`) copies verbatim.

- [ ] **Step 9: Resolve the `ProvideValue` overload uncertainty (Gap 5's open risk)**

  Before finishing `XamlObject.cs`, write a small throwaway diagnostic
  (delete it once you have the answer — don't leave it in the final
  codebase) inside the test project:
  ```csharp
  [Test]
  public void Probe_WhichProvideValueOverloadHasRealLogic()
  {
      var binding = new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Foo") };
      object viaParameterless = null, viaProvider = null;
      try { viaParameterless = ((MarkupExtension)binding).GetType()
          .GetMethod("ProvideValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, Type.EmptyTypes, null)
          ?.Invoke(binding, null); } catch (Exception ex) { viaParameterless = ex; }
      // (repeat for the IXamlServiceProvider overload with a minimal stub provider)
      Console.WriteLine($"parameterless: {viaParameterless}");
  }
  ```
  Run it, read the console output, and use whichever overload actually
  returns something meaningful (not an exception, not a bare default) as
  the call in `XamlObject.ProvideValue()`'s final line. If **neither**
  overload does anything useful for `Binding` specifically (plausible —
  WinUI's `{Binding}` may be entirely compiler-driven with no runtime
  `ProvideValue` path at all), that's fine: `BindingWrapper.ProvideValue()`
  already has its own dedicated path that doesn't strictly need the real
  `Binding.ProvideValue` to do anything useful, since design-time code
  mostly cares about the `Binding` object's *properties* (`Path`, `Mode`,
  etc.), not an evaluated runtime value — record the finding as a comment
  above `BindingWrapper` explaining what was tried and why the current
  approach doesn't depend on it.

- [ ] **Step 10: Port `XamlDocument.cs`**

  Substitutions:
  - `using System.Windows.Markup;` → `using Microsoft.UI.Xaml.Markup;`
  - `using System.Windows.Media;` → `using Microsoft.UI.Xaml.Media;`
  - Add `public Dictionary<string, XamlObject> NamedObjects { get; } = new Dictionary<string, XamlObject>();`
    (needed by `NameScopeHelper`, Step 3).
  - **Gap 1 (brush serialization) and Gap 2 (named-brush lookup)**, both in
    the static constructor and `CreatePropertyValue`:
    ```csharp
    static XamlDocument()
    {
        _colorBrushDictionary = new Dictionary<Windows.UI.Color, string>();
        foreach (var colorProp in typeof(Microsoft.UI.Colors).GetProperties(BindingFlags.Static | BindingFlags.Public)
                     .Where(p => p.PropertyType == typeof(Windows.UI.Color)))
        {
            var color = (Windows.UI.Color)colorProp.GetValue(null, null);
            if (!_colorBrushDictionary.ContainsKey(color))
                _colorBrushDictionary.Add(color, colorProp.Name);
        }
    }

    private static Dictionary<Windows.UI.Color, string> _colorBrushDictionary;
    ```
    **Verify `Microsoft.UI.Colors` exists with this shape** before writing
    this — it was not part of the winmd probed during planning (that probe
    only covered `Microsoft.UI.Xaml.winmd`; `Colors` is very likely a plain
    managed helper class, not a WinRT type, so it may live directly in the
    `Microsoft.WinUI` assembly instead — check
    `typeof(Microsoft.UI.Colors)` resolves at all when you write this; if
    the type or namespace differs, adjust, and note what you found).
    Then, in `CreatePropertyValue`, replace the `SolidColorBrush` name-lookup
    check:
    ```csharp
    if (instance is Microsoft.UI.Xaml.Media.SolidColorBrush solidBrush && _colorBrushDictionary.ContainsKey(solidBrush.Color)) {
        var name = _colorBrushDictionary[solidBrush.Color];
        return new XamlTextValue(this, name);
    }
    ```
    And replace the `instance is Brush && forProperty != null` block (which
    called `System.Windows.Markup.XamlWriter.Save`, unavailable in WinUI)
    with a hand-written serializer covering the common case only:
    ```csharp
    else if (instance is Microsoft.UI.Xaml.Media.SolidColorBrush brush && forProperty != null) {
        var c = brush.Color;
        var colorText = _colorBrushDictionary.TryGetValue(c, out var name)
            ? name
            : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        xml.InnerText = colorText;
    } else if (instance is Microsoft.UI.Xaml.Media.Brush && forProperty != null) {
        throw new NotSupportedException(
            $"Serializing {instance.GetType().Name} to XAML is not supported in Phase 1 " +
            "(only SolidColorBrush; WinUI has no XamlWriter.Save equivalent to fall back to).");
    }
    ```
  - Everything else (`GetTypeDescriptorContext`/`DummyTypeDescriptorContext`,
    `RootElement`/`RootInstance`, `Save`, `CreateObject`,
    `CreatePropertyValue`'s non-brush parts, `GetNamespaceFor`/
    `GetNamespacesFor`, `GetPrefixForNamespace` (both overloads),
    `GetNamespaceForPrefix`, `IsNativeType`) copies verbatim. `CreateNullValue`
    is the one exception: per Gap 7 above, `Microsoft.UI.Xaml.Markup.NullExtension`
    does **not** exist (confirmed absent from the winmd, unlike the WPF
    assumption) — change `new NullExtension()` to
    `new NullExtensionPlaceholder()` (the placeholder type added in Step 11).

- [ ] **Step 11: Port `MarkupExtensionParser.cs`**

  Substitutions:
  - `using System.Windows.Markup;` → `using Microsoft.UI.Xaml.Markup;`
  - Add the three placeholder classes from Gap 7 above
    (`StaticResourceExtensionPlaceholder`, `ThemeResourceExtensionPlaceholder`,
    `NullExtensionPlaceholder`) to this file.
  - `MarkupExtensionParser.Parse`: per Gap 7, replace the type-resolution
    block (the reference's
    `Type extensionType = typeResolver.Resolve(typeName + "Extension"); if (extensionType == null) extensionType = typeResolver.Resolve(typeName); if (extensionType == null || !typeof(MarkupExtension).IsAssignableFrom(extensionType)) { throw ... }`)
    with the switch-based version given in Gap 7 — special-cased lookup for
    `StaticResource`/`ThemeResource`/`Null` first, generic type resolution
    for everything else with the `MarkupExtension`-assignability
    requirement dropped entirely (not just skipped for the three special
    cases — `Binding`/`RelativeSource` need to resolve too, and neither is
    a `MarkupExtension` on WinUI).
  - Everything else — the entire tokenizer (`MarkupExtensionTokenizer`,
    `MarkupExtensionToken`(Kind)), `XamlMarkupExtensionParseException`, and
    the rest of `MarkupExtensionParser.Parse`'s reflection-based
    construction (positional-arg-to-property mapping, constructor
    resolution) — is platform-agnostic string/reflection logic and works
    unchanged for `Binding`/`RelativeSource`/the placeholders exactly as it
    already does for any other resolved type. Copies verbatim.

- [ ] **Step 12: Port `MarkupExtensionPrinter.cs`**

  Substitutions:
  - `using System.Windows;`, `using System.Windows.Data;`,
    `using System.Windows.Markup;` → `Microsoft.UI.Xaml`,
    `Microsoft.UI.Xaml.Data`, `Microsoft.UI.Xaml.Markup`
  - `typeof(System.Windows.Data.MultiBinding)`/`PriorityBinding)` in `CanPrint`
    — **verify these exist in `Microsoft.UI.Xaml.Data`**; if WinUI has no
    multi-binding concept (plausible — MultiBinding is a less commonly
    ported WPF feature), drop that check's two type references and leave a
    comment; the rest of `CanPrint` still works.
  - `obj.ElementType == typeof(Binding)`/`typeof(Reference)`/
    `typeof(StaticResourceExtension)` special-cased property-name shortcuts
    — change to `Microsoft.UI.Xaml.Data.Binding`,
    `Microsoft.UI.Xaml.Markup.ReferenceExtension` (or drop this one case if
    Step 6 found the type doesn't exist), and — per Gap 7 —
    `StaticResourceExtensionPlaceholder` (this codebase's own type, not a
    WinUI one).
  - `IsStaticResourceThatReferencesLocalResource`: per Gap 7, change
    `obj.Instance as System.Windows.StaticResourceExtension` to
    `obj.Instance as StaticResourceExtensionPlaceholder`, and
    `staticResource.ResourceKey` stays the same property name on the
    placeholder.
  - Everything else copies verbatim.

- [ ] **Step 13: Port `XamlParser.cs`**

  Substitutions:
  - `using System.Windows;` → `using Microsoft.UI.Xaml;`
  - `using System.Windows.Markup;` → `using Microsoft.UI.Xaml.Markup;`
  - `using System.Windows.Media;` → `using Microsoft.UI.Xaml.Media;` (for
    `ImageSource` in `CreateObjectFromAttributeText`)
  - **Gap 6 (template instantiation):** `ParseObject`'s
    `if (typeof(FrameworkTemplate).IsAssignableFrom(elementType)) { ... TemplateHelper.GetFrameworkTemplate(...) ... }`
    branch — leave the branch structure as-is; `TemplateHelper` is rewritten
    in Task 8, not here. `FrameworkTemplate` exists on
    `Microsoft.UI.Xaml.FrameworkTemplate` (confirmed via winmd probe during
    planning), so this line only needs the `using` change.
  - **Gap "no NameScope":** replace
    `if (parentXamlObject == null && obj.Instance is DependencyObject) { NameScope.SetNameScope((DependencyObject)obj.Instance, new NameScope()); }`
    with nothing — delete the block. Per the "no `NameScope`" gap above,
    there's no WinUI object to create here; `XamlDocument.NamedObjects`
    (Step 10) is the document-wide table that replaces per-root namescope
    creation, and it's already initialized in `XamlDocument`'s constructor
    (as a field initializer, no explicit setup needed per-parse).
  - `ISupportInitialize` handling (`iSupportInitializeInstance.BeginInit()`/
    `EndInit()`): `System.ComponentModel.ISupportInitialize` is a plain BCL
    interface, not WPF-specific — copies verbatim, works for any WinUI
    object that happens to implement it (Task 5's own `ExampleClass` test
    fixture will implement it, proving the mechanism works even though no
    common WinUI framework type does).
  - Everything else (`Parse` overloads, `FindType`, `GetAttributeNamespace`,
    `ReportException`, `ParseObject`'s non-template/non-namescope body,
    `ParseObjectContent`, `GetNormalizedChildNodes`, `ParseValue`/
    `ParseValueCore`, `FindExistingXamlProperty`, `GetDefaultProperty`,
    `FindProperty`, `TryFindAttachedProperty`/`TryFindAttachedEvent`/
    `FindAttachedProperty`, `GetPropertyInfo` (both overloads),
    `GetXamlSpecialProperty`, `SplitQualifiedIdentifier`, `ParseObjectAttribute`
    (both overloads), `ObjectChildElementIsPropertyElement`/
    `ObjectChildElementIsCollectionInstance`/`IsElementChildACollectionForProperty`,
    `ParseObjectChildElementAsPropertyElement`, `CreateObjectFromAttributeText`
    (both overloads), `RemoveRootNamespacesFromNodeAndChildNodes`,
    `ParseSnippet` (both overloads)) copies verbatim.

- [ ] **Step 14: Port `XamlStaticTools.cs` and `DesignInstanceExtension.cs`**

  `XamlStaticTools.cs`: `using System.Windows.Documents;` → delete (only
  used for... actually check: re-reading the reference file, it doesn't use
  any `System.Windows.Documents` type in the body despite the import —
  confirm and drop the unused import). Everything else copies verbatim.

  `DesignInstanceExtension.cs`: `using System.Windows.Markup;` →
  `using Microsoft.UI.Xaml.Markup;` (for the `MarkupExtension` base class).
  `ProvideValue(IServiceProvider serviceProvider)` — WinUI's base class has
  `ProvideValue(IXamlServiceProvider serviceProvider)` instead of
  `IServiceProvider`; change the override signature to match. Everything
  else copies verbatim (it already just returns `null` unconditionally).

- [ ] **Step 14b: Add a minimal `TemplateHelper.cs`** (base version — Task 8
  extends this same file, it does not replace it)

  `XamlParser.ParseObject` (Step 13) needs `TemplateHelper.GetFrameworkTemplate`
  to exist for the project to compile at all, so a working base version goes
  in now. This version does not merge in resources from ancestor scopes
  (that's Task 8's job) — it correctly handles a template whose resource
  references are all satisfied within its own fragment, which is enough for
  Task 5's own tests (none of which need cross-scope template resources).
  ```csharp
  using System.Xml;
  using Microsoft.UI.Xaml;

  namespace WinUIDesigner.XamlDom
  {
      /// <summary>
      /// Instantiates a FrameworkTemplate from a template's XML fragment.
      /// WPF's TemplateHelper used System.Xaml's XamlXmlReader/XamlObjectWriter
      /// pipeline (no WinUI equivalent, confirmed absent). This uses WinUI's own
      /// public Microsoft.UI.Xaml.Markup.XamlReader.Load(string) instead.
      /// This base version resolves only resources declared inside the
      /// template's own fragment; Task 8 adds ancestor-scope resource splicing.
      /// </summary>
      public static class TemplateHelper
      {
          public static FrameworkTemplate GetFrameworkTemplate(XmlElement xmlElement, XamlObject parentObject)
          {
              var nav = xmlElement.CreateNavigator();
              var namespaces = new System.Collections.Generic.Dictionary<string, string>();
              while (true) {
                  foreach (var ns in nav.GetNamespacesInScope(XmlNamespaceScope.ExcludeXml)) {
                      if (!namespaces.ContainsKey(ns.Key) && ns.Key != "")
                          namespaces.Add(ns.Key, ns.Value);
                  }
                  if (!nav.MoveToParent())
                      break;
              }

              var clonedElement = (XmlElement)xmlElement.CloneNode(true);
              foreach (var entry in new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>(namespaces)) {
                  var value = entry.Value;
                  if (value.StartsWith("clr-namespace") && !value.Contains(";assembly=")
                      && !string.IsNullOrEmpty(parentObject.OwnerDocument.CurrentProjectAssemblyName)) {
                      value += ";assembly=" + parentObject.OwnerDocument.CurrentProjectAssemblyName;
                  }
                  clonedElement.SetAttribute("xmlns:" + entry.Key, value);
              }

              var wrapperXaml =
                  $"<ResourceDictionary xmlns=\"{XamlConstants.PresentationNamespace}\" xmlns:x=\"{XamlConstants.XamlNamespace}\">" +
                  clonedElement.OuterXml +
                  "</ResourceDictionary>";

              var loaded = (ResourceDictionary)Microsoft.UI.Xaml.Markup.XamlReader.Load(wrapperXaml);

              foreach (var key in loaded.Keys) {
                  if (loaded[key] is FrameworkTemplate template)
                      return template;
              }
              return null;
          }
      }
  }
  ```
  Run `dotnet build` after adding this — it's the piece that makes the
  whole assembly link. If `XamlReader.Load` rejects the wrapper XML or the
  returned dictionary doesn't contain the template under an enumerable key,
  fix it here (same iteration you'd otherwise do in Task 8 Step 4) — Task 8
  builds on a working base, not a broken one.

- [ ] **Step 15: Port the test fixtures**

  `ExampleClass.cs`: `using System.Windows.Markup;` →
  `using Microsoft.UI.Xaml.Markup;` (for `ContentPropertyAttribute`,
  `ISupportInitialize` stays `System.ComponentModel` — no change there).
  Everything else copies verbatim.

  `ExampleControl.cs`:
  ```csharp
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Controls;

  namespace WinUIDesigner.XamlDom.Tests
  {
      public class ExampleControl : Control
      {
          public object Property1
          {
              get { return (object)GetValue(Property1Property); }
              set { SetValue(Property1Property, value); }
          }

          public static readonly DependencyProperty Property1Property =
              DependencyProperty.Register(nameof(Property1), typeof(object), typeof(ExampleControl), new PropertyMetadata(null));
      }
  }
  ```

  `ExampleClassContainer.cs`: `using System.Windows.Markup;` →
  `using Microsoft.UI.Xaml.Markup;` (for `ContentPropertyAttribute`).
  Everything else copies verbatim.

  `ExampleService.cs`: `using System.Windows;` → `using Microsoft.UI.Xaml;`.
  Everything else copies verbatim (plain attached-property boilerplate).

  `TestHelper.cs`: rewritten — the reference version compares against
  `System.Windows.Markup.XamlReader.Load` + `System.Windows.Markup.XamlWriter.Save`
  text-diffing (Gap 1 means there's no `XamlWriter.Save` to call). Replace
  with an oracle that compares against `Microsoft.UI.Xaml.Markup.XamlReader.Load`
  (confirmed to exist) by type and a document self-round-trip instead of a
  text diff:
  ```csharp
  using System;
  using System.Diagnostics;
  using System.IO;
  using System.Text;
  using System.Xml;
  using WinUIDesigner.XamlDom;
  using NUnit.Framework;

  namespace WinUIDesigner.XamlDom.Tests
  {
      public class TestHelper
      {
          public static void TestLoading(string xaml)
          {
              Debug.WriteLine("Load using WinUI's built-in XamlReader:");
              ExampleClass.nextUniqueIndex = 0;
              TestHelperLog.logBuilder = new StringBuilder();
              object officialResult = Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
              string officialLog = TestHelperLog.logBuilder.ToString();
              Assert.IsNotNull(officialResult, "officialResult is null");

              Debug.WriteLine("Load using own XamlParser:");
              ExampleClass.nextUniqueIndex = 0;
              TestHelperLog.logBuilder = new StringBuilder();
              XamlDocument doc = XamlParser.Parse(new StringReader(xaml));
              Assert.IsNotNull(doc, "doc is null");
              object ownResult = doc.RootInstance;
              string ownLog = TestHelperLog.logBuilder.ToString();
              Assert.IsNotNull(ownResult, "ownResult is null");
              TestHelperLog.logBuilder = null;

              // No XamlWriter.Save equivalent exists in WinUI (Gap 1), so we
              // can't text-diff a re-serialized "official" result. Compare
              // element type instead, and verify our own document is
              // self-consistent: re-serialize it and re-parse, and check the
              // reparsed root is the same type as the first parse.
              Assert.AreEqual(officialResult.GetType(), ownResult.GetType(),
                  "Own parser produced a different root type than WinUI's XamlReader.Load");

              var sb = new StringBuilder();
              using (var writer = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true }))
                  doc.Save(writer);
              var reparsed = XamlParser.Parse(new StringReader(sb.ToString()));
              Assert.IsNotNull(reparsed, "Re-parsing our own serialized output failed");
              Assert.AreEqual(ownResult.GetType(), reparsed.RootInstance.GetType(),
                  "Round-tripped document did not reparse to the same root type");

              Debug.WriteLine("Official log:");
              Debug.WriteLine(officialLog);
              Debug.WriteLine("Own log:");
              Debug.WriteLine(ownLog);
              Assert.AreEqual(officialLog, ownLog);
          }
      }

      internal static class TestHelperLog
      {
          internal static StringBuilder logBuilder;

          internal static void Log(string text)
          {
              if (logBuilder != null) {
                  logBuilder.AppendLine(text);
                  Debug.WriteLine(text);
              }
          }
      }
  }
  ```
  Individual ported tests that need to check a specific value (not just
  "did it parse and round-trip") add their own direct
  `Assert.AreEqual(((SomeType)officialResult).SomeProp, ((SomeType)ownResult).SomeProp)`
  — both are real instances of the same concrete type, so a direct cast and
  property read works.

- [ ] **Step 16: Port `SimpleLoadTests.cs` (subset) and `WhitespaceTests.cs`**

  Port every test in `WhitespaceTests.cs` verbatim except the
  `xmlns="http://schemas.microsoft.com/netfx/2007/xaml/presentation"` string
  literal in each — **do not** blindly copy that URI; use whatever
  `XamlConstants.PresentationNamespace` was confirmed to be in Task 2 Step 2
  (if it's unchanged, i.e. still `.../2006/xaml/presentation`, note that the
  *reference tests themselves* use a *different*, older
  `.../netfx/2007/xaml/presentation` URI that WPF's parser apparently
  tolerates via namespace-compatibility fallback — for the WinUI port, use
  the confirmed-correct namespace value directly in the ported test XAML
  rather than reproducing the reference's inconsistency).

  From `SimpleLoadTests.cs`, port: `Window` (rename to use
  `Microsoft.UI.Xaml.Window` as the root — note WinUI's `Window` is not a
  `FrameworkElement`/can't nest arbitrary content the same way as WPF's; if
  it doesn't accept being a XAML root with a `Grid` child the way expected,
  substitute `Grid` as the root element for this test instead, since the
  point of the test is exercising the parser, not `Window` specifically),
  `WindowWithAttributes`/`WindowWithElementAttribute` (same caveat),
  `ExampleClassTest`, `ExampleClassWithStringPropAttribute`,
  `ExampleClassWithEscapedBraceStringPropAttribute`,
  `ExampleClassWithFilePathStringPropAttribute`,
  `ExampleClassUseDefaultProperty`, and continue through the rest of the
  file (read the remaining ~330 lines at
  `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Tests\XamlDom\SimpleLoadTests.cs`
  when you reach this step — they follow the identical
  `TestLoading(@"...")` pattern already shown above; port each one
  following the same namespace-URI and type substitutions).

- [ ] **Step 17: Build and run everything**

  ```bash
  dotnet build WinUIDesigner.sln
  dotnet test WinUIDesigner.sln
  ```
  Work through compile errors first (expected — this is the first point
  the whole graph exists), then runtime failures. Every `Assert` failure is
  either: (a) a genuine remaining substitution you missed (fix it), or (b) a
  real behavioral difference between WPF's and WinUI's XAML processing that
  the reference test wasn't written to expect (document it — either adjust
  the test's expectation with a comment explaining the platform difference,
  or mark it `[Ignore("reason")]` if it exercises something out of Phase 1's
  scope entirely — never delete a failing assertion without recording why).

- [ ] **Step 18: Commit**

  ```bash
  git add -A
  git commit -m "feat: port XamlDom core object model and parser to WinUI"
  ```

---

### Task 6: Port `MarkupExtensionTests.cs`

**Files:**
- Create: `tests/WinUIDesigner.XamlDom.Tests/MarkupExtensionTests.cs` (from
  `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Tests\XamlDom\MarkupExtensionTests.cs`)

**Interfaces:**
- Consumes: everything from Task 5.

- [ ] **Step 1: Read the reference file in full**

  `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Tests\XamlDom\MarkupExtensionTests.cs`
  (290 lines — not reproduced here; it wasn't read in full during planning).
  It will contain `TestLoading(...)` calls (same pattern as Task 5) plus
  likely some direct `MarkupExtensionParser.Parse`/`MarkupExtensionPrinter.Print`
  round-trip assertions given the file's name.

- [ ] **Step 2: Port each test**, applying the same substitutions as Task 5
  Step 16 (namespace URIs from the verified `XamlConstants`, WPF types →
  WinUI types). For any test that specifically exercises `{Binding}` value
  resolution (not just parsing), cross-check against what Task 5 Step 9's
  probe found about which `ProvideValue` overload has real logic — if the
  probe found neither overload does anything useful for `Binding`, that
  specific test's assertion about the *resolved value* (not just successful
  parsing) may need to become an assertion about the `Binding` object's
  *properties* instead (`Path`, `ElementName`, etc.) — document why with a
  comment referencing Task 5 Step 9's finding.

- [ ] **Step 3: Run, fix, commit**

  ```bash
  dotnet test WinUIDesigner.sln --filter MarkupExtensionTests
  git add tests/WinUIDesigner.XamlDom.Tests/MarkupExtensionTests.cs
  git commit -m "test: port MarkupExtensionTests for WinUI"
  ```

---

### Task 7: Port `NamescopeTest.cs` (reduced scope)

**Files:**
- Create: `tests/WinUIDesigner.XamlDom.Tests/NamescopeTest.cs` (adapted from
  `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Tests\XamlDom\NamescopeTest.cs`)

**Interfaces:**
- Consumes: `NameScopeHelper.FindNamedObject` (Task 5), `XamlObject.Properties`.

The reference test calls `((FrameworkElement)obj.RootInstance).CreateVisualTree()`
and `TryFindChild<T>(...)` — extension methods from
`ICSharpCode.WpfDesign.UIExtensions` in the **Designer** project (live
visual-tree walking), which doesn't exist until Phase 3. Rewrite the
assertions to walk the `XamlObject` tree directly instead of a live visual
tree — this still genuinely exercises name registration and `{x:Reference}`
resolution (Task 5's `NameScopeHelper`/`ReferenceExtensionWrapper`), just at
the object-model level rather than the rendered-UI level.

- [ ] **Step 1: Write a small `XamlObject` tree-walking helper in the test file**

  ```csharp
  static XamlObject FindDescendant(XamlObject root, Func<XamlObject, bool> predicate)
  {
      if (predicate(root))
          return root;
      foreach (var property in root.Properties) {
          if (property.PropertyValue is XamlObject child) {
              var found = FindDescendant(child, predicate);
              if (found != null) return found;
          }
          foreach (var element in property.CollectionElements) {
              if (element is XamlObject childElement) {
                  var found = FindDescendant(childElement, predicate);
                  if (found != null) return found;
              }
          }
      }
      return null;
  }
  ```

- [ ] **Step 2: Port `NamescopeTest1`**, replacing the visual-tree lookups:
  ```csharp
  [Test]
  public void NamescopeTest1()
  {
      var xaml = @"
  <UserControl
      xmlns=""" + XamlConstants.PresentationNamespace + @"""
      xmlns:t=""" + XamlTypeFinderTests.XamlDomTestsNamespace + @"""
      xmlns:x=""" + XamlConstants.XamlNamespace + @"""
      x:Name=""root"">
      <Grid x:Name=""rootGrid"">
          <Button x:Name=""aa"" />
          <Button x:Name=""bb"" />
          <t:ExampleControl Property1=""{x:Reference aa}"" />
      </Grid>
  </UserControl>";

      var doc = XamlParser.Parse(new StringReader(xaml));

      var example = FindDescendant(doc.RootElement, o => o.ElementType == typeof(ExampleControl));
      var buttonAa = FindDescendant(doc.RootElement, o => o.ElementType == typeof(Microsoft.UI.Xaml.Controls.Button) && o.Name == "aa");

      Assert.IsNotNull(example);
      Assert.IsNotNull(buttonAa);
      Assert.AreSame(((ExampleControl)example.Instance).Property1, buttonAa.Instance);
  }
  ```
  If this fails because `{x:Reference}` resolves to `null` (per Task 5 Step
  6's documented fallback when `Microsoft.UI.Xaml.Markup.ReferenceExtension`
  doesn't exist or isn't wired up correctly), that's the expected, flagged
  limitation — mark the test `[Ignore("x:Reference value resolution not yet wired for WinUI, see Task 5 Step 6")]`
  rather than deleting it, so Phase 2/3 has a concrete, re-enable-able
  pointer.

- [ ] **Step 3: Port `NamescopeTest2`/`NamescopeTest3`** the same way,
  comparing against `Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml)`'s
  result via the same `FindDescendant`-style walk, but using reflection
  (`Microsoft.UI.Xaml.VisualTreeHelper.FindElementsInHostCoordinates` isn't
  usable without a live window — instead walk `officialResult`'s object
  graph via `TypeDescriptor.GetProperties`/recursive descent, mirroring
  `FindDescendant`'s shape but over live CLR objects instead of `XamlObject`
  wrappers). Write this once actual test content is in front of you at
  implementation time — the exact recursive-descent shape depends on
  whether `Grid.Children`/`ContentControl.Content` are the only traversal
  points needed for this specific XAML (they are, based on the reference
  test's markup).

- [ ] **Step 4: Run, fix or `[Ignore]` with reasons, commit**

  ```bash
  dotnet test WinUIDesigner.sln --filter NamescopeTest
  git add tests/WinUIDesigner.XamlDom.Tests/NamescopeTest.cs
  git commit -m "test: port NamescopeTest for WinUI (object-model level, no live visual tree)"
  ```

---

### Task 8: Extend `TemplateHelper` with ancestor-scope resource splicing (Gap 6)

Task 5 Step 14b already added a working base `TemplateHelper.cs` (needed
there so the project compiles at all — `XamlParser.cs` calls it). That base
version only resolves resources declared inside the template's own
fragment. This task adds the missing piece: resources declared on an
*ancestor* element (e.g. a brush defined on the containing `Grid.Resources`,
referenced by a `{StaticResource}` inside the template) — which needs
manual XML splicing since `Microsoft.UI.Xaml.Markup.XamlReader.Load` has no
post-construction resource-injection hook the way WPF's
`XamlObjectWriterSettings.BeforePropertiesHandler` did.

**Files:**
- Modify: `src/WinUIDesigner.XamlDom/TemplateHelper.cs` (from Task 5 Step 14b)

**Interfaces:**
- Consumes: `Microsoft.UI.Xaml.Markup.XamlReader.Load(string)`, `XamlObject`,
  `XamlConstants`.
- Produces: no signature change — `TemplateHelper.GetFrameworkTemplate(XmlElement, XamlObject) : FrameworkTemplate`
  keeps the same signature Task 5 Step 14b already gave it.

- [ ] **Step 1: Write the failing test**

  ```csharp
  [Test]
  public void ControlTemplateWithResourceReference()
  {
      var xaml = @"
  <Grid
      xmlns=""" + XamlConstants.PresentationNamespace + @"""
      xmlns:x=""" + XamlConstants.XamlNamespace + @""">
      <Grid.Resources>
          <SolidColorBrush x:Key=""MyBrush"" Color=""Red"" />
      </Grid.Resources>
      <Grid.Resources>
          <ControlTemplate x:Key=""MyTemplate"" TargetType=""Button"">
              <Border Background=""{StaticResource MyBrush}"" />
          </ControlTemplate>
      </Grid.Resources>
  </Grid>";

      var doc = XamlParser.Parse(new StringReader(xaml));
      Assert.IsNotNull(doc.RootInstance);
      // The template resource must have parsed to a real ControlTemplate,
      // not thrown or silently produced null.
  }
  ```
  (Two separate `Grid.Resources` elements in the same XAML is invalid — 
  combine into one `<Grid.Resources><ResourceDictionary>...` when you write
  this; shown split above only for readability of the two concerns being
  tested.)

- [ ] **Step 2: Run it, confirm it fails the way this task expects**

  `TemplateHelper.cs` already exists (Task 5 Step 14b), so this won't fail
  with a missing-type error. It should fail because `MyBrush` is declared
  in a *different* `Grid.Resources` block than the one the `ControlTemplate`
  itself is nested inside — the base version only sees resources inside the
  template's own fragment, so `{StaticResource MyBrush}` resolves to nothing
  and `Border.Background` ends up unset (or `XamlReader.Load` throws a
  resource-not-found error, depending on how WinUI's loader handles a
  missing `StaticResource` — observe which one actually happens and use
  that to shape the assertion in Step 4).

- [ ] **Step 3: Add ancestor-resource splicing to the existing `TemplateHelper.cs`**

  Add a new private method, and call it from the existing
  `GetFrameworkTemplate` to inject the result into the wrapper XAML before
  the call to `XamlReader.Load`:
  ```csharp
  static string BuildMergedAncestorResourcesXaml(XamlObject parentObject)
  {
      var sb = new StringBuilder();
      var obj = parentObject;
      while (obj != null) {
          foreach (XmlNode child in obj.XmlElement.ChildNodes) {
              if (child is XmlElement el && el.LocalName.EndsWith(".Resources")) {
                  foreach (XmlNode resourceNode in el.ChildNodes) {
                      if (resourceNode is XmlElement resourceElement)
                          sb.Append(resourceElement.OuterXml);
                  }
              }
          }
          obj = obj.ParentObject;
      }
      return sb.ToString();
  }
  ```
  Update `GetFrameworkTemplate` to use it (add `using System.Text;` for
  `StringBuilder` if not already present):
  ```csharp
  // after building clonedElement, before constructing wrapperXaml:
  var mergedResourcesXaml = BuildMergedAncestorResourcesXaml(parentObject);

  var wrapperXaml =
      $"<ResourceDictionary xmlns=\"{XamlConstants.PresentationNamespace}\" xmlns:x=\"{XamlConstants.XamlNamespace}\">" +
      (mergedResourcesXaml.Length > 0
          ? $"<ResourceDictionary.MergedDictionaries><ResourceDictionary>{mergedResourcesXaml}</ResourceDictionary></ResourceDictionary.MergedDictionaries>"
          : "") +
      clonedElement.OuterXml +
      "</ResourceDictionary>";
  ```
  (Replaces the base version's plain `clonedElement.OuterXml`-only
  `wrapperXaml` assignment from Task 5 Step 14b with this
  ancestor-resource-aware version.) Note this only recovers resources
  declared *inside the same parsed XAML document* (via `XmlElement`
  traversal) — resources set outside the document (app-level or theme
  resources) are out of scope for Phase 1, same limitation the reference
  WPF implementation effectively had for a standalone parser.

- [ ] **Step 4: Run the test, iterate**

  ```bash
  dotnet test WinUIDesigner.sln --filter ControlTemplateWithResourceReference
  ```
  Likely failure modes to expect and fix: `XamlReader.Load` throwing on the
  wrapper XML (check the exact XAML it received — log `wrapperXaml` to the
  test output on failure), or the returned `ResourceDictionary` not
  containing the template under the expected key (WinUI's `XamlReader.Load`
  may require the outer element be exactly what's being loaded rather than
  wrapped — if so, load the `ResourceDictionary` wrapper as shown, but
  confirm empirically the `x:Key` on the inner `ControlTemplate` survives
  into `loaded.Keys`).

- [ ] **Step 5: Commit**

  ```bash
  git add src/WinUIDesigner.XamlDom/TemplateHelper.cs tests/WinUIDesigner.XamlDom.Tests
  git commit -m "feat: rewrite TemplateHelper on Microsoft.UI.Xaml.Markup.XamlReader.Load"
  ```

---

### Task 9: Definition-of-Done round-trip test

**Files:**
- Create: `tests/WinUIDesigner.XamlDom.Tests/RoundTripTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 5–8.

- [ ] **Step 1: Generate a real WinUI 3 project to source the test XAML from**

  ```bash
  dotnet new list | grep -i winui
  ```
  If a `winui3`-style template is installed, scaffold one in the scratch
  directory (`dotnet new winui3 -o <scratch>/winui3-sample`) and use its
  generated `MainWindow.xaml` verbatim as the test input. If no such
  template is installed, hand-write a representative `MainWindow.xaml`
  instead — a `Window` (or `Grid` root, per Task 5 Step 16's note about
  `Window` possibly not working as a XamlDom root) containing a `StackPanel`
  with a `TextBlock`, a `Button` with a `Click` event attribute, and a
  `Grid.Resources` with one named `SolidColorBrush` — enough surface to
  exercise attributes, nested elements, an event property, and a resource.

- [ ] **Step 2: Write the failing test**

  ```csharp
  [Test]
  public void RoundTripsRealWinUIWindowXaml()
  {
      var xaml = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "SampleWindow.xaml"));
      // (copy the XAML sourced in Step 1 to
      // tests/WinUIDesigner.XamlDom.Tests/SampleWindow.xaml, marked
      // CopyToOutputDirectory=PreserveNewest in the csproj)

      var doc = XamlParser.Parse(new StringReader(xaml));
      Assert.IsNotNull(doc.RootInstance);

      var sb = new StringBuilder();
      using (var writer = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true }))
          doc.Save(writer);

      var reparsedDoc = XamlParser.Parse(new StringReader(sb.ToString()));
      Assert.IsNotNull(reparsedDoc.RootInstance);
      Assert.AreEqual(doc.RootInstance.GetType(), reparsedDoc.RootInstance.GetType());

      // Structural equivalence, not byte-identical text: same element count.
      int CountElements(XamlObject o) =>
          1 + o.Properties.Sum(p =>
              (p.PropertyValue is XamlObject po ? CountElements(po) : 0) +
              p.CollectionElements.OfType<XamlObject>().Sum(CountElements));

      Assert.AreEqual(CountElements(doc.RootElement), CountElements(reparsedDoc.RootElement));
  }
  ```

- [ ] **Step 3: Add `SampleWindow.xaml` to the test project with
  `CopyToOutputDirectory`**

  ```xml
  <ItemGroup>
    <None Include="SampleWindow.xaml" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  ```

- [ ] **Step 4: Run, iterate until green**

  ```bash
  dotnet test WinUIDesigner.sln --filter RoundTripsRealWinUIWindowXaml
  ```
  This is the spec's explicit Definition-of-Done criterion — don't relax
  the assertion to make it pass; if it fails, the failure points at a real
  remaining gap in one of Tasks 5–8 (most likely a property or element type
  the earlier tasks' narrower tests didn't happen to exercise). Fix the
  root cause in the relevant earlier file, not here.

- [ ] **Step 5: Commit**

  ```bash
  git add tests/WinUIDesigner.XamlDom.Tests/RoundTripTests.cs tests/WinUIDesigner.XamlDom.Tests/SampleWindow.xaml
  git commit -m "test: add Phase 1 definition-of-done round-trip test against real WinUI3 XAML"
  ```

---

## Self-review notes

- **Spec coverage:** all six numbered gaps from the spec are addressed
  (Gaps 1–2 in Task 5 Step 10, Gap 3 in Task 2 Step 2, Gap 4 in Task 5 Step
  13, Gap 5 in Task 5 Steps 5–9, Gap 6 in Task 8). Three additional gaps
  found while writing this plan (not in the spec, since they were only
  visible once the actual line-by-line port work started) are each resolved
  concretely rather than deferred: the missing `DependencyPropertyDescriptor`
  bridge (Task 5 Step 1), the missing `IAddChild` (Task 4), and the missing
  `NameScope`/`INameScope` (Task 5 Step 3, Task 5 Step 6, Task 7). The
  environment tooling gap (missing MSIX/Appx MSBuild component) is Task 1
  Step 1, gating everything else.
- **Gaps found during execution (not planning) and fixed in the plan text
  directly, before the affected task was dispatched:** the Task 5/Task 8
  `TemplateHelper.cs` ordering defect (pre-flight scan, before any
  dispatch — see the SDD ledger). Two more surfaced once Task 3 actually
  ran against the real WinUI assembly: (1) WinUI carries no
  `XmlnsDefinitionAttribute`/`XmlnsPrefixAttribute` at all (Task 3 Step 4)
  — `WinUITypeFinder` now hand-registers a fixed, verified namespace list
  instead of scanning attributes that don't exist; (2) several standard
  `{...}` markup extensions have no public WinUI type at all
  (`StaticResource`/`ThemeResource`/`Null`/`TemplateBinding`), and
  `Binding`/`RelativeSource` aren't `MarkupExtension` subclasses on WinUI
  the way they are on WPF — Gap 7 (added to Task 5, spanning Steps 7, 10,
  11, 12) introduces this codebase's own placeholder types for the
  type-less extensions and relaxes `MarkupExtensionParser`'s
  `MarkupExtension`-assignability requirement. Both were verified against
  the real `Microsoft.UI.Xaml.winmd` metadata, not guessed, the same way
  every other gap in this plan was.
- **Known residual risk carried into later tasks, not hidden:** Task 5 Step
  9's `ProvideValue` overload uncertainty (now narrowed to third-party
  custom extensions only, per Gap 7), Task 5 Step 6's `ReferenceExtension`
  existence uncertainty, Task 5 Step 7's `XamlSetTypeConverterAttribute`
  existence uncertainty, Task 5 Step 12's `MultiBinding`/`PriorityBinding`
  existence uncertainty, Task 5 Step 10's `Microsoft.UI.Colors` shape
  uncertainty — each has a concrete verification step and a documented
  fallback, not a silent guess.
