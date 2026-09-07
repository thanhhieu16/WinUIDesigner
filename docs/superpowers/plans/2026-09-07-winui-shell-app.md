# Phase 5 — Shell App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `WinUIDesigner.Shell`, the first application that hosts the designer engine — open a XAML file, place controls from a toolbox, edit properties, undo, and save.

**Architecture:** A WinUI 3 unpackaged executable with exactly one `.xaml` file (`App.xaml`, which exists so the build emits an app-level merged PRI — without it `ms-appx:///` cannot resolve and every templated control fail-fasts at layout). All other UI is written in C#. Logic (`ShellModel`, `Document`, `Toolbox`, `ShellTypeFinder`, `ShellMetadata`) touches no `Window` and no `XamlRoot`, so it is driven by an NUnitLite suite exactly like the three existing ones; `MainWindow` owns every wire into an engine service.

**Tech Stack:** .NET `net10.0-windows10.0.19041.0`, WinUI 3 via `Microsoft.WindowsAppSDK 1.7.250606001`, NUnit 3.14 + NUnitLite, `win-x64`, unpackaged and self-contained.

**Spec:** `docs/superpowers/specs/2026-09-07-winui-shell-app-design.md` (commit `f4fdc02`, revised `ba81292`). Read it alongside this plan; every decision number cited below refers to it.

## Global Constraints

- Target framework for every project: `net10.0-windows10.0.19041.0`. `UseWinUI=true`. `Nullable` is `disable`. `RuntimeIdentifier` is `win-x64`. `SelfContained=false`.
- `Microsoft.WindowsAppSDK` is pinned to `1.7.250606001` and `Microsoft.Windows.SDK.BuildTools` to `10.0.26100.4188`. Do not bump either.
- `WindowsAppSDKSelfContained=true` on both new projects. This machine lacks the DDLM packages; without it every WinRT activation fails with `REGDB_E_CLASSNOTREG`.
- `WindowsPackageType=None`. This is what makes an unpackaged WinUI 3 app initialize the Windows App Runtime bootstrapper.
- **Tests are run through each test project's built `.exe` with `--noresult`, never `dotnet test`.** `dotnet test`'s `testhost.exe` cannot activate `Microsoft.UI.Xaml` classes; every WinUI-dependent test reports as *ignored*, so a large skip count is not a green run.
- Dependency direction is `Shell → Designer → DesignModel → XamlDom`. Never reference in the other direction.
- **Exactly one `.xaml` file may exist under `src/`: `src/WinUIDesigner.Shell/App.xaml`.** No other XAML file, in any project, for any reason.
- Do **not** add a hand-written `app.manifest`. It replaces the generated registration-free WinRT activation manifest and the app fail-fasts at startup.
- Do **not** define `Main` and do **not** set `DISABLE_XAML_GENERATED_MAIN` in the shell. The XamlCompiler-generated `Main` calls `WinRT.ComWrappersSupport.InitializeComWrappers()` and installs a `DispatcherQueueSynchronizationContext`; a hand-written `Main` missing either fail-fasts before any managed line runs.
- New files carry **no** SharpDevelop MIT header (that header belongs only to files ported from `WpfDesign`). All new files in this plan are newly written.
- Indentation in every new file is **4 spaces**, matching `OutlineViewControl.cs`, `PropertyGridControl.cs`, and all test files.
- The reference source at `F:\ReferencesCode\CSharp\WpfDesigner` is **read-only**. Read from it, never write to it.
- Every WinRT object has thread affinity. Anything that constructs a `Microsoft.UI.Xaml` type — including `Toolbox`'s activation probe and every `XamlReader.Load` — must run on the WinUI dispatcher thread. In tests that means inside `WinUITestHost.Run(...)`.
- Baseline that must stay green: XamlDom 144 (140 passed, 0 failed, 4 pre-existing ignores), DesignModel 77/77, Designer 245/245.

---

## File Structure

**New project `src/WinUIDesigner.Shell/`** (references `WinUIDesigner.Designer`, `WinUIDesigner.DesignModel`, `WinUIDesigner.XamlDom`):

| File | Responsibility |
|---|---|
| `WinUIDesigner.Shell.csproj` | Project properties per Decision 5. |
| `App.xaml` | The only XAML file. Merges `XamlControlsResources`. |
| `App.xaml.cs` | `Application` subclass; installs `UnhandledException`; creates the window. |
| `AssemblyInfo.cs` | Title, description, `InternalsVisibleTo("WinUIDesigner.Shell.Tests")`. |
| `NewDocumentTemplate.cs` | The blank-document XAML as a `const string`. |
| `Toolbox.cs` | `Toolbox`, `AssemblyNode`, `ControlNode`. Assembly scan and filters. |
| `ShellTypeFinder.cs` | `XamlTypeFinder` override that resolves the toolbox's assemblies. |
| `Document.cs` | One `DesignSurface`; text ⇄ design; dirty flag; captured load errors. |
| `ShellModel.cs` | Document lifetime, file I/O, `AskSaveChanges` / `ReportError` events. |
| `ShellMetadata.cs` | `Register()` — default sizes and popular properties. |
| `ToolboxView.cs` | `ListView` of controls; arms and disarms `CreateComponentTool`. |
| `MainWindow.cs` | Layout, menu, accelerators, dialogs, pickers, close gate. |

**New project `tests/WinUIDesigner.Shell.Tests/`** (references `WinUIDesigner.Shell`, `WinUIDesigner.Designer`, `WinUIDesigner.DesignModel`, `WinUIDesigner.TestHost`):

| File | Covers |
|---|---|
| `Program.cs` | NUnitLite entry point. |
| `NewDocumentTemplateTests.cs` | DoD 4. |
| `ToolboxTests.cs` | DoD 1, 2, 3. |
| `DocumentTests.cs` | DoD 5, 6, 7, 8, 13. |
| `ShellModelTests.cs` | DoD 9, 10, 11, 12. |
| `ShellMetadataTests.cs` | DoD 15. |
| `ToolboxViewTests.cs` | DoD 14. |
| `SoakTests.cs` | DoD 16. |

**Modified:** `WinUIDesigner.sln` (both projects added), `CLAUDE.md` (the `.xaml` exception, and the shell's test command).

`MainWindow.cs` is the only file with no unit test of its own; DoD 18 covers it by hand. Everything else has a test in the same task that creates it.

---

## Task 1: Projects that build, launch, and run tests

The riskiest task, so it is first. Nothing else in this plan can be verified until an app in this tree actually starts and shows a templated control.

**Files:**
- Create: `src/WinUIDesigner.Shell/WinUIDesigner.Shell.csproj`
- Create: `src/WinUIDesigner.Shell/App.xaml`
- Create: `src/WinUIDesigner.Shell/App.xaml.cs`
- Create: `src/WinUIDesigner.Shell/AssemblyInfo.cs`
- Create: `src/WinUIDesigner.Shell/NewDocumentTemplate.cs`
- Create: `tests/WinUIDesigner.Shell.Tests/WinUIDesigner.Shell.Tests.csproj`
- Create: `tests/WinUIDesigner.Shell.Tests/Program.cs`
- Create: `tests/WinUIDesigner.Shell.Tests/NewDocumentTemplateTests.cs`
- Modify: `WinUIDesigner.sln`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `WinUIDesigner.Shell.NewDocumentTemplate.Xaml` (`public const string`); the `WinUIDesigner.Shell` and `WinUIDesigner.Shell.Tests` projects; `App` with a `protected override void OnLaunched(LaunchActivatedEventArgs)` that Task 6 replaces the body of.

- [ ] **Step 1: Create the shell project file**

`src/WinUIDesigner.Shell/WinUIDesigner.Shell.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <RootNamespace>WinUIDesigner.Shell</RootNamespace>
    <Nullable>disable</Nullable>
    <UseWinUI>true</UseWinUI>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>false</SelfContained>
    <WindowsPackageType>None</WindowsPackageType>
    <!--
      EnableMsixTooling=true is chosen over false only because it makes the build emit the
      merged resources.pri as a visible output. Both values were measured to work once
      App.xaml exists (see the spec's "Which knobs matter" table); the visible artifact
      makes a future regression in the PRI pipeline obvious instead of silent.
    -->
    <EnableMsixTooling>true</EnableMsixTooling>
    <GenerateAppxPackageOnBuild>false</GenerateAppxPackageOnBuild>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.7.250606001" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4188" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\WinUIDesigner.XamlDom\WinUIDesigner.XamlDom.csproj" />
    <ProjectReference Include="..\WinUIDesigner.DesignModel\WinUIDesigner.DesignModel.csproj" />
    <ProjectReference Include="..\WinUIDesigner.Designer\WinUIDesigner.Designer.csproj" />
  </ItemGroup>

</Project>
```

Do not add an `<ApplicationDefinition>` item and do not set `EnableDefaultXamlItems`. The default XAML items pick `App.xaml` up as the application definition automatically; adding both produces a duplicate-definition build failure.

- [ ] **Step 2: Create `App.xaml`**

`src/WinUIDesigner.Shell/App.xaml`:

```xml
<Application
    x:Class="WinUIDesigner.Shell.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 3: Create `App.xaml.cs` with a placeholder window**

The window body here exists to prove the app launches and templates a control. Task 6 replaces `OnLaunched`'s body with `new MainWindow()`.

`src/WinUIDesigner.Shell/App.xaml.cs`:

```csharp
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The shell's application object.
    /// </summary>
    /// <remarks>
    /// App.xaml is the only XAML file in src/, and it exists for a measured reason: a project
    /// containing no compiled XAML produces no app-level merged resources.pri, ms-appx:/// then
    /// cannot be resolved, XamlControlsResources fails to load themeresources.xaml, and every
    /// control whose default template references a theme brush fail-fasts the process with
    /// 0xC0000409 during the first layout pass. See the Phase 5 spec.
    /// </remarks>
    public partial class App : Application
    {
        Window window;

        public App()
        {
            InitializeComponent();
            UnhandledException += OnUnhandledException;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Replaced by MainWindow in Task 6. Until then this proves the app starts and
            // that a templated control lays out, which is the whole risk in this task.
            window = new Window();
            window.Content = new TextBox { Text = "WinUIDesigner shell", Margin = new Thickness(24) };
            window.Activate();
        }

        void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Task 6 replaces this with a dialog. Handled=true keeps a defect in a design-time
            // behavior from ending the process mid-drag; see spec Decision 18.
            e.Handled = true;
            System.Diagnostics.Debug.WriteLine("Unhandled: " + e.Message);
        }
    }
}
```

- [ ] **Step 4: Create `AssemblyInfo.cs`**

`src/WinUIDesigner.Shell/AssemblyInfo.cs`:

```csharp
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("WinUIDesigner.Shell")]
[assembly: AssemblyDescription("WinUI 3 designer shell application")]

// Toolbox's IsControl/CanActivate predicates and ShellMetadata's registration flag are internal
// seams the test suite drives directly. Same pattern as WinUIDesigner.Designer's and
// WinUIDesigner.XamlDom's own AssemblyInfo.cs.
[assembly: InternalsVisibleTo("WinUIDesigner.Shell.Tests")]
```

- [ ] **Step 5: Create the test project file**

`tests/WinUIDesigner.Shell.Tests/WinUIDesigner.Shell.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!--
      OutputType=Exe + NUnitLite: WinUI's registration-free activation manifest is embedded in
      the project's own apphost, which only an Exe gets. dotnet test's testhost.exe cannot
      activate Microsoft.UI.Xaml classes at all. See the other test projects' Program.cs.
    -->
    <OutputType>Exe</OutputType>
    <GenerateProgramFile>false</GenerateProgramFile>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <RootNamespace>WinUIDesigner.Shell.Tests</RootNamespace>
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
    <ProjectReference Include="..\..\src\WinUIDesigner.Shell\WinUIDesigner.Shell.csproj" />
    <ProjectReference Include="..\..\src\WinUIDesigner.Designer\WinUIDesigner.Designer.csproj" />
    <ProjectReference Include="..\..\src\WinUIDesigner.DesignModel\WinUIDesigner.DesignModel.csproj" />
    <ProjectReference Include="..\WinUIDesigner.TestHost\WinUIDesigner.TestHost.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 6: Create the test entry point**

`tests/WinUIDesigner.Shell.Tests/Program.cs`:

```csharp
using System.Reflection;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// NUnitLite entry point. See WinUIDesigner.Designer.Tests/Program.cs for why this project
    /// hosts NUnit itself instead of relying on dotnet test.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            return new NUnitLite.AutoRun(Assembly.GetExecutingAssembly()).Execute(args);
        }
    }
}
```

- [ ] **Step 7: Add both projects to the solution**

```bash
dotnet sln WinUIDesigner.sln add src/WinUIDesigner.Shell/WinUIDesigner.Shell.csproj
dotnet sln WinUIDesigner.sln add tests/WinUIDesigner.Shell.Tests/WinUIDesigner.Shell.Tests.csproj
```

`dotnet sln add` places both at the solution root rather than inside the existing `src` and `tests` solution folders. That nesting is cosmetic only; leave it.

- [ ] **Step 8: Write the failing test**

`tests/WinUIDesigner.Shell.Tests/NewDocumentTemplateTests.cs`:

```csharp
using System.IO;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class NewDocumentTemplateTests
    {
        [Test]
        public void TheBlankTemplateLoadsAsACanvasOf640By480()
        {
            WinUITestHost.Run(() =>
            {
                var surface = new DesignSurface();
                using (var reader = XmlReader.Create(new StringReader(NewDocumentTemplate.Xaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());

                // The reference's NewFileTemplate.xaml is rooted at <Window>, which cannot be a
                // design root here: Microsoft.UI.Xaml.Window is not a UIElement, and
                // DesignSurface assigns DesignPanel.Content = RootItem.View. Spec Decision 15.
                var canvas = surface.DesignContext.RootItem.Component as Canvas;
                Assert.That(canvas, Is.Not.Null, "the template's root must be a Canvas");
                Assert.That(canvas.Width, Is.EqualTo(640.0));
                Assert.That(canvas.Height, Is.EqualTo(480.0));

                surface.UnloadDesigner();
            });
        }
    }
}
```

- [ ] **Step 9: Run the test and watch it fail to compile**

```bash
dotnet build WinUIDesigner.sln
```

Expected: `error CS0103: The name 'NewDocumentTemplate' does not exist in the current context`.

- [ ] **Step 10: Create `NewDocumentTemplate.cs`**

`src/WinUIDesigner.Shell/NewDocumentTemplate.cs`:

```csharp
namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The XAML a new, empty document starts from.
    /// </summary>
    /// <remarks>
    /// A constant rather than the reference's on-disk NewFileTemplate.xaml, and rooted at a
    /// Canvas rather than the reference's Window: Microsoft.UI.Xaml.Window is not a UIElement,
    /// so it cannot be a design root. Canvas rather than Grid because CanvasPlacementSupport
    /// gives free placement, which is what a blank document wants. Spec Decision 15.
    /// </remarks>
    public static class NewDocumentTemplate
    {
        public const string Xaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"640\" Height=\"480\" />";
    }
}
```

- [ ] **Step 11: Build and run the new suite**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```

Expected: 1 test, 1 passed, 0 failed.

- [ ] **Step 12: Launch the shell by hand and confirm it does not fail-fast**

```bash
src/WinUIDesigner.Shell/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.exe
```

Expected: a window appears containing a `TextBox` reading "WinUIDesigner shell". Close it; the process exits 0.

If instead the process dies immediately with exit code `-1073741189` (`0xC0000409`), the PRI chain is broken — check that `App.xaml` is present, that no `app.manifest` was added, and that `resources.pri` exists in the output directory. Do not work around it by removing the `TextBox`; a shell that cannot show a `TextBox` cannot show a property grid.

- [ ] **Step 13: Confirm the three existing suites are still green**

```bash
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: XamlDom 144 (140 passed, 4 ignored), DesignModel 77/77, Designer 245/245.

- [ ] **Step 14: Record the `.xaml` exception in `CLAUDE.md`**

In the "What this is" section, after the sentence describing the five phases, add:

```markdown
`src/` contains exactly one `.xaml` file: `src/WinUIDesigner.Shell/App.xaml`. That is not a
style choice. A WinUI 3 project containing no compiled XAML produces no app-level merged
`resources.pri`, so `ms-appx:///` cannot be resolved, `XamlControlsResources` fails to load
`themeresources.xaml`, and every control whose default template references a theme brush
fail-fasts the process with `0xC0000409` during the first layout pass. All other shell UI is
written in C#, like `OutlineViewControl` and `PropertyGridControl`. Do not add a second
`.xaml` file, and do not add a hand-written `app.manifest` — it replaces the generated
registration-free WinRT activation manifest and the app will not start.
```

In the "Test" section, add the shell suite to the list of commands:

```
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```

- [ ] **Step 15: Commit**

```bash
git add src/WinUIDesigner.Shell tests/WinUIDesigner.Shell.Tests WinUIDesigner.sln CLAUDE.md
git commit -m "feat: add the shell app and test projects"
```

---

## Task 2: Toolbox and type finder

**Files:**
- Create: `src/WinUIDesigner.Shell/Toolbox.cs`
- Create: `src/WinUIDesigner.Shell/ShellTypeFinder.cs`
- Test: `tests/WinUIDesigner.Shell.Tests/ToolboxTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 beyond the projects.
- Produces:
  - `WinUIDesigner.Shell.ControlNode` — `public Type Type { get; set; }`, `public string Name { get; }`.
  - `WinUIDesigner.Shell.AssemblyNode` — `public Assembly Assembly { get; set; }`, `public string Name { get; }`, `public List<ControlNode> Controls { get; }`.
  - `WinUIDesigner.Shell.Toolbox` — `public static Toolbox Instance { get; }`, `public ObservableCollection<AssemblyNode> AssemblyNodes { get; }`, `public void AddAssembly(Assembly)`, `internal static bool IsControl(Type)`, `internal static bool CanActivate(Type)`.
  - `WinUIDesigner.Shell.ShellTypeFinder` — `public static ShellTypeFinder Instance { get; }`, `public override Assembly LoadAssembly(string)`, `public override XamlTypeFinder Clone()`.

- [ ] **Step 1: Write the failing tests**

`tests/WinUIDesigner.Shell.Tests/ToolboxTests.cs`:

```csharp
using System;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class ToolboxTests
    {
        [Test]
        public void EveryListedControlIsAConcreteUIElementWithAParameterlessConstructor()
        {
            WinUITestHost.Run(() =>
            {
                var nodes = Toolbox.Instance.AssemblyNodes;
                Assert.That(nodes.Count, Is.GreaterThanOrEqualTo(1));

                foreach (var node in nodes)
                {
                    Assert.That(node.Controls.Count, Is.GreaterThan(0), node.Name + " listed no controls");
                    foreach (var control in node.Controls)
                    {
                        var t = control.Type;
                        Assert.That(t.IsAbstract, Is.False, t.FullName + " is abstract");
                        Assert.That(t.IsGenericTypeDefinition, Is.False, t.FullName + " is a generic definition");
                        Assert.That(typeof(UIElement).IsAssignableFrom(t), Is.True, t.FullName + " is not a UIElement");
                        Assert.That(
                            t.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null),
                            Is.Not.Null,
                            t.FullName + " has no public parameterless constructor");
                    }
                }
            });
        }

        [Test]
        public void EveryListedControlActuallyActivates()
        {
            WinUITestHost.Run(() =>
            {
                foreach (var node in Toolbox.Instance.AssemblyNodes)
                    foreach (var control in node.Controls)
                        Assert.DoesNotThrow(
                            () => Activator.CreateInstance(control.Type),
                            control.Type.FullName + " is listed but does not activate");
            });
        }

        [Test]
        public void PivotHeaderItemIsExcludedBecauseItDoesNotActivate()
        {
            WinUITestHost.Run(() =>
            {
                // Measured on this SDK: of the 160 instantiable UIElement subclasses in
                // Microsoft.WinUI, exactly one throws TargetInvocationException from
                // Activator.CreateInstance. The filter exists for it; this test names it so a
                // future SDK that fixes it shows up as a failure rather than silently widening
                // the list. Spec Decision 11.
                Assert.That(Toolbox.IsControl(typeof(PivotHeaderItem)), Is.True,
                    "PivotHeaderItem still passes IsControl - only activation excludes it");
                Assert.That(Toolbox.CanActivate(typeof(PivotHeaderItem)), Is.False,
                    "PivotHeaderItem now activates; if the SDK fixed it, drop this test");

                var listed = Toolbox.Instance.AssemblyNodes
                    .SelectMany(n => n.Controls)
                    .Any(c => c.Type == typeof(PivotHeaderItem));
                Assert.That(listed, Is.False, "PivotHeaderItem must not reach the toolbox");
            });
        }

        [Test]
        public void CommonControlsAreListed()
        {
            WinUITestHost.Run(() =>
            {
                var listed = Toolbox.Instance.AssemblyNodes
                    .SelectMany(n => n.Controls)
                    .Select(c => c.Type)
                    .ToList();

                foreach (var expected in new[] {
                    typeof(Button), typeof(TextBlock), typeof(TextBox), typeof(CheckBox),
                    typeof(ComboBox), typeof(Canvas), typeof(Grid), typeof(StackPanel),
                    typeof(Border), typeof(Microsoft.UI.Xaml.Shapes.Rectangle),
                    typeof(Microsoft.UI.Xaml.Shapes.Ellipse), typeof(Microsoft.UI.Xaml.Shapes.Polyline)
                })
                    Assert.That(listed, Contains.Item(expected), expected.Name + " is missing from the toolbox");
            });
        }

        [Test]
        public void TheWinUIAssemblyResolvesByItsRealName()
        {
            WinUITestHost.Run(() =>
            {
                // Measured: the assembly holding Microsoft.UI.Xaml.Controls.Button is named
                // "Microsoft.WinUI", not "Microsoft.UI.Xaml". LoadAssembly compares against
                // Assembly.GetName().Name, so this is the name a XAML namespace declaration
                // must match.
                Assert.That(typeof(Button).Assembly.GetName().Name, Is.EqualTo("Microsoft.WinUI"));
                Assert.That(ShellTypeFinder.Instance.LoadAssembly("Microsoft.WinUI"), Is.SameAs(typeof(Button).Assembly));
            });
        }

        [Test]
        public void AnUnknownAssemblyNameReturnsNullRatherThanThrowing()
        {
            WinUITestHost.Run(() =>
            {
                Assert.That(ShellTypeFinder.Instance.LoadAssembly("No.Such.Assembly"), Is.Null);
            });
        }

        [Test]
        public void CloneReturnsTheSingletonSoTheFinderSurvivesEveryLoad()
        {
            WinUITestHost.Run(() =>
            {
                Assert.That(ShellTypeFinder.Instance.Clone(), Is.SameAs(ShellTypeFinder.Instance));
            });
        }
    }
}
```

- [ ] **Step 2: Run and watch them fail**

```bash
dotnet build WinUIDesigner.sln
```

Expected: `error CS0103: The name 'Toolbox' does not exist in the current context` and the same for `ShellTypeFinder`.

- [ ] **Step 3: Implement `Toolbox.cs`**

`src/WinUIDesigner.Shell/Toolbox.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.Shell
{
    /// <summary>One control the toolbox offers.</summary>
    public sealed class ControlNode
    {
        public Type Type { get; set; }

        public string Name
        {
            get { return Type.Name; }
        }
    }

    /// <summary>One assembly's worth of toolbox controls.</summary>
    public sealed class AssemblyNode
    {
        public AssemblyNode()
        {
            Controls = new List<ControlNode>();
        }

        public Assembly Assembly { get; set; }

        public List<ControlNode> Controls { get; private set; }

        public string Name
        {
            get { return Assembly.GetName().Name; }
        }
    }

    /// <summary>
    /// The set of control types a user can place on the design surface.
    /// </summary>
    /// <remarks>
    /// Ported from the reference's Toolbox.cs, with one addition: a type that passes
    /// <see cref="IsControl"/> but throws from Activator.CreateInstance is skipped. Measured on
    /// this SDK, exactly one of the 160 candidates does (PivotHeaderItem). Spec Decision 11.
    /// <para>
    /// Every WinUI type is a WinRT class with thread affinity, so <see cref="CanActivate"/> -
    /// and therefore <see cref="AddAssembly"/> and the first touch of <see cref="Instance"/> -
    /// must run on the WinUI dispatcher thread.
    /// </para>
    /// </remarks>
    public sealed class Toolbox
    {
        static readonly object gate = new object();
        static Toolbox instance;

        public static Toolbox Instance
        {
            get
            {
                lock (gate)
                {
                    if (instance == null)
                    {
                        instance = new Toolbox();
                        instance.AddAssembly(typeof(Microsoft.UI.Xaml.Controls.Button).Assembly);
                    }
                    return instance;
                }
            }
        }

        Toolbox()
        {
            AssemblyNodes = new ObservableCollection<AssemblyNode>();
        }

        public ObservableCollection<AssemblyNode> AssemblyNodes { get; private set; }

        public void AddAssembly(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException("assembly");

            ShellTypeFinder.Instance.RegisterAssembly(assembly);

            var node = new AssemblyNode();
            node.Assembly = assembly;

            foreach (var t in assembly.GetExportedTypes())
            {
                if (IsControl(t) && CanActivate(t))
                    node.Controls.Add(new ControlNode { Type = t });
            }

            node.Controls.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            AssemblyNodes.Add(node);
        }

        internal static bool IsControl(Type t)
        {
            return !t.IsAbstract
                && !t.IsGenericTypeDefinition
                && t.IsSubclassOf(typeof(UIElement))
                && t.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null) != null;
        }

        internal static bool CanActivate(Type t)
        {
            try
            {
                Activator.CreateInstance(t);
                return true;
            }
            catch (Exception)
            {
                // Deliberately silent, and deliberately catching everything: the alternative is
                // a crash while building the toolbox list. The type is simply absent.
                return false;
            }
        }
    }
}
```

- [ ] **Step 4: Implement `ShellTypeFinder.cs`**

`src/WinUIDesigner.Shell/ShellTypeFinder.cs`:

```csharp
using System.Reflection;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// Resolves assemblies named in a document's XAML namespace declarations against the
    /// assemblies the toolbox knows about.
    /// </summary>
    /// <remarks>
    /// Ported from the reference's MyTypeFinder.cs. XamlTypeFinder.ImportFrom is protected, so
    /// the WinUI seed is imported from inside this type - CreateWinUITypeFinder() returns a
    /// XamlTypeFinder, not a ShellTypeFinder, so it cannot simply be assigned. Spec Decision 14.
    /// </remarks>
    public sealed class ShellTypeFinder : XamlTypeFinder
    {
        static readonly object gate = new object();
        static ShellTypeFinder instance;

        public static ShellTypeFinder Instance
        {
            get
            {
                lock (gate)
                {
                    if (instance == null)
                    {
                        var created = new ShellTypeFinder();
                        created.ImportFrom(CreateWinUITypeFinder());
                        instance = created;
                    }
                    return instance;
                }
            }
        }

        public override Assembly LoadAssembly(string name)
        {
            foreach (var registered in RegisteredAssemblies)
            {
                if (registered.GetName().Name == name)
                    return registered;
            }

            foreach (var node in Toolbox.Instance.AssemblyNodes)
            {
                if (node.Name == name)
                    return node.Assembly;
            }

            return null;
        }

        public override XamlTypeFinder Clone()
        {
            return Instance;
        }
    }
}
```

- [ ] **Step 5: Run the suite**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```

Expected: 8 tests, 8 passed.

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.Shell/Toolbox.cs src/WinUIDesigner.Shell/ShellTypeFinder.cs tests/WinUIDesigner.Shell.Tests/ToolboxTests.cs
git commit -m "feat: add the shell toolbox and its type finder"
```

---

## Task 3: Document

**Files:**
- Create: `src/WinUIDesigner.Shell/Document.cs`
- Test: `tests/WinUIDesigner.Shell.Tests/DocumentTests.cs`

**Interfaces:**
- Consumes: `Toolbox.Instance.AssemblyNodes`, `ShellTypeFinder.Instance` (Task 2).
- Produces: `WinUIDesigner.Shell.Document` with
  - `public Document(string filePath, string text)`
  - `public string Text { get; }`, `public string FilePath { get; set; }`, `public string FileName { get; }`, `public string Name { get; }`, `public string Title { get; }`
  - `public bool IsDirty { get; }`, `public bool LoadFailed { get; }`, `public IReadOnlyList<XamlError> LoadErrors { get; }`
  - `public DesignSurface DesignSurface { get; }`, `public DesignContext DesignContext { get; }`, `public UndoService UndoService { get; }`, `public ISelectionService SelectionService { get; }`, `public IOutlineNode OutlineRoot { get; }`
  - `public void UpdateXaml()`, `public void MarkSaved()`, `public void Dispose()`
  - `public event PropertyChangedEventHandler PropertyChanged`

- [ ] **Step 1: Write the failing tests**

`tests/WinUIDesigner.Shell.Tests/DocumentTests.cs`:

```csharp
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class DocumentTests
    {
        const string CanvasWithButton =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"640\" Height=\"480\">" +
            "<Button x:Name=\"Ok\" Width=\"75\" Height=\"32\" Canvas.Left=\"10\" Canvas.Top=\"20\" Content=\"Ok\" />" +
            "</Canvas>";

        const string CanvasWithPolyline =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"640\" Height=\"480\">" +
            "<Polyline x:Name=\"Line\" Points=\"10,10 40,25 70,5\" Stroke=\"Red\" StrokeThickness=\"2\" />" +
            "</Canvas>";

        [Test]
        public void ALoadedDocumentExposesItsContextRootAndOutline()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    Assert.That(doc.LoadFailed, Is.False);
                    Assert.That(doc.DesignContext, Is.Not.Null);
                    Assert.That(doc.DesignContext.RootItem, Is.Not.Null);
                    Assert.That(doc.DesignContext.RootItem.Component, Is.TypeOf<Canvas>());
                    Assert.That(doc.OutlineRoot, Is.Not.Null);
                    Assert.That(doc.UndoService, Is.Not.Null);
                    Assert.That(doc.SelectionService, Is.Not.Null);
                    Assert.That(doc.LoadErrors, Is.Empty);
                }
            });
        }

        [Test]
        public void TheRoundTripIsAFixedPointAsAString()
        {
            WinUITestHost.Run(() =>
            {
                string first;
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.UpdateXaml();
                    first = doc.Text;
                }

                string second;
                using (var doc = new Document(null, first))
                {
                    doc.UpdateXaml();
                    second = doc.Text;
                }

                // Ordinal string equality, deliberately: "re-parses into an equivalent tree" can
                // be satisfied by a null check, this cannot. Save/load/save must be stable, or
                // every save churns the file.
                Assert.That(second, Is.EqualTo(first).Using(System.StringComparer.Ordinal));
            });
        }

        [Test]
        public void APolylineKeepsItsPointsThroughASave()
        {
            WinUITestHost.Run(() =>
            {
                string saved;
                using (var doc = new Document(null, CanvasWithPolyline))
                {
                    doc.UpdateXaml();
                    saved = doc.Text;
                }

                // Phases A and B exist so this holds: before them a WinRT-typed value serialized
                // as an empty element and the points were lost on save.
                Assert.That(saved, Does.Contain("Points"));

                using (var reloaded = new Document(null, saved))
                {
                    var canvas = (Canvas)reloaded.DesignContext.RootItem.Component;
                    var polyline = canvas.Children.OfType<Polyline>().Single();
                    Assert.That(polyline.Points.Count, Is.EqualTo(3));
                    Assert.That(polyline.Points[0].X, Is.EqualTo(10.0));
                    Assert.That(polyline.Points[1].Y, Is.EqualTo(25.0));
                    Assert.That(polyline.Points[2].X, Is.EqualTo(70.0));
                }
            });
        }

        [Test]
        public void DirtyTracksTheUndoStackAndNotTheTextSetter()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    Assert.That(doc.IsDirty, Is.False, "a freshly loaded document is clean");

                    doc.UpdateXaml();
                    Assert.That(doc.IsDirty, Is.False,
                        "serializing the document is not an edit - the reference's Text setter " +
                        "marks it dirty here, which is the behaviour Decision 5 of the spec drops");

                    var canvas = doc.DesignContext.RootItem;
                    var button = canvas.ContentProperty.CollectionElements[0];
                    button.Properties["Width"].SetValue(120.0);
                    Assert.That(doc.IsDirty, Is.True, "a committed change marks the document dirty");

                    doc.MarkSaved();
                    Assert.That(doc.IsDirty, Is.False);
                }
            });
        }

        [Test]
        public void DisposeUnloadsTheDesignerAndIsIdempotent()
        {
            WinUITestHost.Run(() =>
            {
                var doc = new Document(null, CanvasWithButton);
                Assert.That(doc.DesignSurface.DesignContext, Is.Not.Null);

                doc.Dispose();
                Assert.That(doc.DesignSurface.DesignContext, Is.Null);
                Assert.DoesNotThrow(() => doc.Dispose());
            });
        }

        [Test]
        public void TitleMarksUnsavedWork()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.Name_ForTest_SetTempName("New1");
                    Assert.That(doc.Title, Is.EqualTo("New1"));

                    var button = doc.DesignContext.RootItem.ContentProperty.CollectionElements[0];
                    button.Properties["Width"].SetValue(200.0);
                    Assert.That(doc.Title, Is.EqualTo("New1*"));
                }
            });
        }
    }
}
```

The last test calls a helper that does not exist yet; Step 3 adds it as an internal seam rather than making the temp name settable in the public API.

- [ ] **Step 2: Run and watch it fail**

```bash
dotnet build WinUIDesigner.sln
```

Expected: `error CS0246: The type or namespace name 'Document' could not be found`.

- [ ] **Step 3: Implement `Document.cs`**

`src/WinUIDesigner.Shell/Document.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Xml;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// One open document: the text, the design surface built from it, and the dirty flag.
    /// </summary>
    /// <remarks>
    /// Ported from the reference's Document.cs with five deliberate deviations, all recorded in
    /// the Phase 5 spec: no DocumentMode (there is no XAML text view); Dispose unsubscribes and
    /// unloads (the reference does neither, and the soak test depends on both); the constructor
    /// does not assign FilePath through a side-effecting property; the file write lives in
    /// ShellModel, not here; and IsDirty is set only by the undo stack, never by the Text setter.
    /// </remarks>
    public sealed class Document : IDisposable, INotifyPropertyChanged
    {
        readonly DesignSurface designSurface = new DesignSurface();
        readonly List<XamlError> loadErrors = new List<XamlError>();

        string text;
        string filePath;
        string tempName;
        bool isDirty;
        bool disposed;
        UndoService subscribedUndoService;

        public Document(string filePath, string text)
        {
            this.filePath = filePath;
            this.text = text;
            UpdateDesign();
            isDirty = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Text
        {
            get { return text; }
        }

        public string FilePath
        {
            get { return filePath; }
            set
            {
                filePath = value;
                RaisePropertyChanged("FilePath");
                RaisePropertyChanged("FileName");
                RaisePropertyChanged("Name");
                RaisePropertyChanged("Title");
            }
        }

        public string FileName
        {
            get { return filePath == null ? null : Path.GetFileName(filePath); }
        }

        public string Name
        {
            get { return FileName ?? tempName; }
        }

        public string Title
        {
            get { return IsDirty ? Name + "*" : Name; }
        }

        public bool IsDirty
        {
            get { return isDirty; }
            private set
            {
                if (isDirty == value)
                    return;
                isDirty = value;
                RaisePropertyChanged("IsDirty");
                RaisePropertyChanged("Title");
            }
        }

        /// <summary>True when the document could not be parsed at all - see <see cref="LoadErrors"/>.</summary>
        public bool LoadFailed { get; private set; }

        /// <summary>
        /// Errors the parser reported during the last load, fatal or not.
        /// </summary>
        /// <remarks>
        /// Captured through XamlLoadSettings.ReportErrors rather than read from
        /// DesignContext.Services afterwards. That matters: XamlDesignContext calls ReportErrors
        /// on the line before it throws XamlLoadException for a document that is not well-formed
        /// XML, and DesignSurface.LoadDesigner has already unloaded the previous context by then,
        /// so there is no context left to read the error service from. Spec Decision 16.
        /// </remarks>
        public IReadOnlyList<XamlError> LoadErrors
        {
            get { return loadErrors; }
        }

        public DesignSurface DesignSurface
        {
            get { return designSurface; }
        }

        public DesignContext DesignContext
        {
            get { return designSurface.DesignContext; }
        }

        public UndoService UndoService
        {
            get { return DesignContext == null ? null : DesignContext.Services.GetService<UndoService>(); }
        }

        public ISelectionService SelectionService
        {
            get { return DesignContext == null ? null : DesignContext.Services.Selection; }
        }

        public IOutlineNode OutlineRoot { get; private set; }

        /// <summary>Serializes the live design tree back into <see cref="Text"/>.</summary>
        public void UpdateXaml()
        {
            if (DesignContext == null)
                return;

            var sb = new StringBuilder();
            using (var writer = new XamlXmlWriter(sb))
                designSurface.SaveDesigner(writer);

            text = XamlFormatter.Format(sb.ToString());
            RaisePropertyChanged("Text");
        }

        /// <summary>Clears the dirty flag. Called by ShellModel once the text has reached disk.</summary>
        public void MarkSaved()
        {
            IsDirty = false;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            DetachUndo();
            OutlineRoot = null;
            designSurface.UnloadDesigner();
        }

        internal void Name_ForTest_SetTempName(string name)
        {
            tempName = name;
            RaisePropertyChanged("Name");
            RaisePropertyChanged("Title");
        }

        void UpdateDesign()
        {
            DetachUndo();
            OutlineRoot = null;
            loadErrors.Clear();
            LoadFailed = false;

            var settings = new XamlLoadSettings();
            foreach (var node in Toolbox.Instance.AssemblyNodes)
                settings.DesignerAssemblies.Add(node.Assembly);
            settings.TypeFinder = ShellTypeFinder.Instance;
            settings.ReportErrors = errorService =>
            {
                if (errorService != null)
                    loadErrors.AddRange(errorService.Errors);
            };

            try
            {
                using (var reader = XmlReader.Create(new StringReader(text)))
                    designSurface.LoadDesigner(reader, settings);
            }
            catch (XamlLoadException)
            {
                // The parser already reported through settings.ReportErrors, one line before the
                // throw. Swallowing the exception here is what lets ShellModel report the errors
                // instead of the process dying on a malformed file.
                LoadFailed = true;
                return;
            }

            if (DesignContext != null && DesignContext.RootItem != null)
            {
                OutlineRoot = DesignContext.RootItem.CreateOutlineNode();
                AttachUndo();
            }
        }

        void AttachUndo()
        {
            subscribedUndoService = UndoService;
            if (subscribedUndoService != null)
                subscribedUndoService.UndoStackChanged += OnUndoStackChanged;
        }

        void DetachUndo()
        {
            if (subscribedUndoService != null)
                subscribedUndoService.UndoStackChanged -= OnUndoStackChanged;
            subscribedUndoService = null;
        }

        void OnUndoStackChanged(object sender, EventArgs e)
        {
            IsDirty = true;
        }

        void RaisePropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
```

- [ ] **Step 4: Run the suite**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```

Expected: 14 tests, 14 passed.

If `TheRoundTripIsAFixedPointAsAString` fails, do **not** relax it to a substring check — a save that is not a fixed point means the writer and the parser disagree, which is a real defect worth reporting.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Shell/Document.cs tests/WinUIDesigner.Shell.Tests/DocumentTests.cs
git commit -m "feat: add the shell's Document"
```

---

## Task 4: ShellModel

**Files:**
- Create: `src/WinUIDesigner.Shell/ShellModel.cs`
- Test: `tests/WinUIDesigner.Shell.Tests/ShellModelTests.cs`

**Interfaces:**
- Consumes: `Document` (Task 3), `NewDocumentTemplate.Xaml` (Task 1).
- Produces:
  - `public enum SaveChangesResult { Save, Discard, Cancel }`
  - `public sealed class AskSaveChangesEventArgs : EventArgs` — `public Document Document { get; set; }`, `public SaveChangesResult Result { get; set; }`
  - `public sealed class ShellErrorEventArgs : EventArgs` — `public string Message { get; set; }`, `public IReadOnlyList<XamlError> Errors { get; set; }`
  - `WinUIDesigner.Shell.ShellModel` — `public ObservableCollection<Document> Documents { get; }`, `public Document CurrentDocument { get; set; }`, `public Document New()`, `public bool Open(string path)`, `public bool Save(Document doc)`, `public bool SaveAs(Document doc, string path)`, `public bool Close(Document doc)`, `public bool PrepareExit()`, `public event EventHandler<AskSaveChangesEventArgs> AskSaveChanges`, `public event EventHandler<ShellErrorEventArgs> ReportError`

- [ ] **Step 1: Write the failing tests**

`tests/WinUIDesigner.Shell.Tests/ShellModelTests.cs`:

```csharp
using System;
using System.IO;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class ShellModelTests
    {
        const string GoodXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"100\" />";

        // Not well-formed XML: the parser reports through the sink and returns null, and
        // XamlDesignContext then throws XamlLoadException. The fatal path.
        const string MalformedXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Button></Canvas>";

        // Well-formed, but names a type that does not exist. The non-fatal path: the document
        // loads and the error is reported.
        const string UnknownTypeXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"100\">" +
            "<NoSuchControl />" +
            "</Canvas>";

        static string WriteTemp(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), "shelltest-" + Guid.NewGuid().ToString("N") + ".xaml");
            File.WriteAllText(path, content);
            return path;
        }

        [Test]
        public void NewProducesALoadableBlankCanvas()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var doc = shell.New();

                Assert.That(doc, Is.Not.Null);
                Assert.That(doc.LoadFailed, Is.False);
                var canvas = doc.DesignContext.RootItem.Component as Canvas;
                Assert.That(canvas, Is.Not.Null);
                Assert.That(canvas.Width, Is.EqualTo(640.0));
                Assert.That(canvas.Height, Is.EqualTo(480.0));
                Assert.That(shell.Documents, Has.Count.EqualTo(1));
                Assert.That(shell.CurrentDocument, Is.SameAs(doc));

                shell.Close(doc);
            });
        }

        [Test]
        public void OpeningAMissingFileReportsAndChangesNothing()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                ShellErrorEventArgs reported = null;
                shell.ReportError += (s, e) => reported = e;

                var missing = Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".xaml");
                bool opened = false;
                Assert.DoesNotThrow(() => opened = shell.Open(missing));

                Assert.That(opened, Is.False);
                Assert.That(reported, Is.Not.Null, "the failure must be reported, not thrown");
                Assert.That(shell.Documents, Is.Empty);
                Assert.That(shell.CurrentDocument, Is.Null);
            });
        }

        [Test]
        public void AFatalParseErrorReportsTheParsersOwnMessageAndOpensNothing()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                ShellErrorEventArgs reported = null;
                shell.ReportError += (s, e) => reported = e;

                var path = WriteTemp(MalformedXaml);
                try
                {
                    bool opened = false;
                    Assert.DoesNotThrow(() => opened = shell.Open(path));

                    Assert.That(opened, Is.False);
                    Assert.That(reported, Is.Not.Null);
                    Assert.That(reported.Errors, Is.Not.Null.And.Not.Empty,
                        "the errors must arrive through the ReportErrors callback - after a " +
                        "XamlLoadException there is no DesignContext left to read them from");
                    Assert.That(shell.Documents, Is.Empty);
                }
                finally
                {
                    File.Delete(path);
                }
            });
        }

        [Test]
        public void ANonFatalParseErrorOpensTheDocumentAndStillReports()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                ShellErrorEventArgs reported = null;
                shell.ReportError += (s, e) => reported = e;

                var path = WriteTemp(UnknownTypeXaml);
                try
                {
                    bool opened = shell.Open(path);

                    Assert.That(opened, Is.True, "a well-formed document opens even with errors in it");
                    Assert.That(shell.Documents, Has.Count.EqualTo(1));
                    Assert.That(reported, Is.Not.Null);
                    Assert.That(reported.Errors, Is.Not.Null.And.Not.Empty);

                    shell.Close(shell.CurrentDocument);
                }
                finally
                {
                    File.Delete(path);
                }
            });
        }

        [Test]
        public void SaveWritesTheSerializedTreeAndClearsDirty()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var path = WriteTemp(GoodXaml);
                try
                {
                    Assert.That(shell.Open(path), Is.True);
                    var doc = shell.CurrentDocument;

                    doc.DesignContext.RootItem.Properties["Width"].SetValue(321.0);
                    Assert.That(doc.IsDirty, Is.True);

                    Assert.That(shell.Save(doc), Is.True);
                    Assert.That(doc.IsDirty, Is.False);
                    Assert.That(File.ReadAllText(path), Does.Contain("321"));

                    shell.Close(doc);
                }
                finally
                {
                    File.Delete(path);
                }
            });
        }

        [Test]
        public void ClosingADirtyDocumentAsksAndCancelKeepsItOpen()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var doc = shell.New();
                doc.DesignContext.RootItem.Properties["Width"].SetValue(123.0);
                Assert.That(doc.IsDirty, Is.True);

                int asked = 0;
                shell.AskSaveChanges += (s, e) => { asked++; e.Result = SaveChangesResult.Cancel; };

                Assert.That(shell.Close(doc), Is.False);
                Assert.That(asked, Is.EqualTo(1));
                Assert.That(shell.Documents, Has.Count.EqualTo(1), "Cancel keeps the document open");

                shell.AskSaveChanges += (s, e) => { e.Result = SaveChangesResult.Discard; };
            });
        }

        [Test]
        public void DiscardClosesWithoutWritingToDisk()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var path = WriteTemp(GoodXaml);
                var before = File.ReadAllText(path);
                try
                {
                    Assert.That(shell.Open(path), Is.True);
                    var doc = shell.CurrentDocument;
                    doc.DesignContext.RootItem.Properties["Width"].SetValue(999.0);

                    shell.AskSaveChanges += (s, e) => e.Result = SaveChangesResult.Discard;
                    Assert.That(shell.Close(doc), Is.True);

                    Assert.That(shell.Documents, Is.Empty);
                    Assert.That(File.ReadAllText(path), Is.EqualTo(before).Using(StringComparer.Ordinal));
                }
                finally
                {
                    File.Delete(path);
                }
            });
        }

        [Test]
        public void ClosingACleanDocumentDoesNotAsk()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var doc = shell.New();

                int asked = 0;
                shell.AskSaveChanges += (s, e) => { asked++; e.Result = SaveChangesResult.Cancel; };

                Assert.That(shell.Close(doc), Is.True);
                Assert.That(asked, Is.EqualTo(0));
                Assert.That(shell.Documents, Is.Empty);
            });
        }
    }
}
```

- [ ] **Step 2: Run and watch it fail**

```bash
dotnet build WinUIDesigner.sln
```

Expected: `error CS0246: The type or namespace name 'ShellModel' could not be found`.

- [ ] **Step 3: Implement `ShellModel.cs`**

`src/WinUIDesigner.Shell/ShellModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using WinUIDesigner.DesignModel.Xaml;

namespace WinUIDesigner.Shell
{
    public enum SaveChangesResult
    {
        Save,
        Discard,
        Cancel
    }

    /// <summary>Asks the UI what to do about a document with unsaved changes.</summary>
    public sealed class AskSaveChangesEventArgs : EventArgs
    {
        public Document Document { get; set; }

        /// <summary>Defaults to Cancel, so an unhandled event is the safe answer.</summary>
        public SaveChangesResult Result { get; set; }
    }

    /// <summary>Carries a failure to the UI. Never thrown out of ShellModel.</summary>
    public sealed class ShellErrorEventArgs : EventArgs
    {
        public string Message { get; set; }

        /// <summary>Parser errors when the failure came from loading a document; otherwise null.</summary>
        public IReadOnlyList<XamlError> Errors { get; set; }
    }

    /// <summary>
    /// Document lifetime and file I/O.
    /// </summary>
    /// <remarks>
    /// Ported from the reference's Shell.cs, with the dialogs removed. The reference calls
    /// MessageBox.Show inline; WinUI's replacement, ContentDialog, is asynchronous and needs an
    /// XamlRoot, which would drag a live window into every test. This class raises
    /// <see cref="AskSaveChanges"/> and <see cref="ReportError"/> instead, and MainWindow answers
    /// them. Spec Decisions 6 and 7.
    /// <para>
    /// No method here throws. Spec Decision 17: a throw escaping into the engine's transaction
    /// machinery strands a ChangeGroup, and there is no handler that would make that survivable.
    /// </para>
    /// </remarks>
    public sealed class ShellModel : INotifyPropertyChanged
    {
        static int nonameIndex = 1;

        Document currentDocument;

        public ShellModel()
        {
            Documents = new ObservableCollection<Document>();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<AskSaveChangesEventArgs> AskSaveChanges;
        public event EventHandler<ShellErrorEventArgs> ReportError;

        public ObservableCollection<Document> Documents { get; private set; }

        public Document CurrentDocument
        {
            get { return currentDocument; }
            set
            {
                if (currentDocument == value)
                    return;
                currentDocument = value;
                RaisePropertyChanged("CurrentDocument");
            }
        }

        public bool IsSomethingDirty
        {
            get
            {
                foreach (var doc in Documents)
                    if (doc.IsDirty)
                        return true;
                return false;
            }
        }

        public Document New()
        {
            var doc = new Document(null, NewDocumentTemplate.Xaml);
            doc.Name_ForTest_SetTempName("New" + nonameIndex++);
            if (doc.LoadFailed)
            {
                // The template is a constant in this assembly, so this means the engine broke,
                // not the user's input. Report it rather than opening a broken document.
                RaiseError("Could not create a new document.", doc.LoadErrors);
                doc.Dispose();
                return null;
            }
            Add(doc);
            return doc;
        }

        public bool Open(string path)
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                RaiseError(ex.Message, null);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                RaiseError(ex.Message, null);
                return false;
            }

            var doc = new Document(Path.GetFullPath(path), text);

            if (doc.LoadFailed)
            {
                RaiseError("Could not load " + path, doc.LoadErrors);
                doc.Dispose();
                return false;
            }

            if (doc.LoadErrors.Count > 0)
                RaiseError("The document loaded with errors.", doc.LoadErrors);

            Add(doc);
            return true;
        }

        public bool Save(Document doc)
        {
            if (doc == null)
                return false;
            if (doc.FilePath == null)
            {
                // SaveAs needs a path from the UI; the caller is expected to have asked already.
                RaiseError("This document has no file path yet.", null);
                return false;
            }
            return WriteToDisk(doc, doc.FilePath);
        }

        public bool SaveAs(Document doc, string path)
        {
            if (doc == null || path == null)
                return false;
            if (!WriteToDisk(doc, path))
                return false;
            doc.FilePath = Path.GetFullPath(path);
            return true;
        }

        public bool Close(Document doc)
        {
            if (doc == null)
                return false;

            if (doc.IsDirty)
            {
                var args = new AskSaveChangesEventArgs { Document = doc, Result = SaveChangesResult.Cancel };
                var handler = AskSaveChanges;
                if (handler != null)
                    handler(this, args);

                if (args.Result == SaveChangesResult.Cancel)
                    return false;
                if (args.Result == SaveChangesResult.Save && !Save(doc))
                    return false;
            }

            Documents.Remove(doc);
            if (CurrentDocument == doc)
                CurrentDocument = Documents.Count > 0 ? Documents[0] : null;
            doc.Dispose();
            return true;
        }

        /// <summary>Closes every document, honouring a Cancel. Returns false if the user cancelled.</summary>
        public bool PrepareExit()
        {
            var snapshot = new List<Document>(Documents);
            foreach (var doc in snapshot)
                if (!Close(doc))
                    return false;
            return true;
        }

        bool WriteToDisk(Document doc, string path)
        {
            doc.UpdateXaml();
            try
            {
                File.WriteAllText(path, doc.Text);
            }
            catch (IOException ex)
            {
                RaiseError(ex.Message, null);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                RaiseError(ex.Message, null);
                return false;
            }
            doc.MarkSaved();
            return true;
        }

        void Add(Document doc)
        {
            Documents.Add(doc);
            CurrentDocument = doc;
        }

        void RaiseError(string message, IReadOnlyList<XamlError> errors)
        {
            var handler = ReportError;
            if (handler != null)
                handler(this, new ShellErrorEventArgs { Message = message, Errors = errors });
        }

        void RaisePropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
```

`Document.Name_ForTest_SetTempName` is `internal`, so `ShellModel` in the same assembly can call it. Rename it to `SetTempName` in this step and update `DocumentTests.cs` and `Document.cs` accordingly — the `_ForTest_` name was only ever a scaffold, and `New()` is a real caller.

- [ ] **Step 4: Rename the temp-name seam**

In `src/WinUIDesigner.Shell/Document.cs`, rename `Name_ForTest_SetTempName` to `SetTempName`. In `tests/WinUIDesigner.Shell.Tests/DocumentTests.cs`, update the one call site in `TitleMarksUnsavedWork`.

- [ ] **Step 5: Run the suite**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```

Expected: 22 tests, 22 passed.

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.Shell tests/WinUIDesigner.Shell.Tests
git commit -m "feat: add ShellModel with event-based dialogs and error reporting"
```

---

## Task 5: Metadata seeding

Phases 4e and 4f both deferred this here. Unseeded, the property grid's Popular category is empty and `ModelTools.GetDefaultSize` has nothing to return, which affects click-to-place sizing directly.

**Files:**
- Create: `src/WinUIDesigner.Shell/ShellMetadata.cs`
- Test: `tests/WinUIDesigner.Shell.Tests/ShellMetadataTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `WinUIDesigner.Shell.ShellMetadata` — `public static void Register()`, idempotent.

- [ ] **Step 1: Write the failing tests**

`tests/WinUIDesigner.Shell.Tests/ShellMetadataTests.cs`:

```csharp
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using Windows.Foundation;
using WinUIDesigner.DesignModel;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class ShellMetadataTests
    {
        const string CanvasWithButton =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"640\" Height=\"480\">" +
            "<Button x:Name=\"Ok\" Content=\"Ok\" />" +
            "</Canvas>";

        [Test]
        public void RegisterSeedsDefaultSizesForCommonControls()
        {
            WinUITestHost.Run(() =>
            {
                ShellMetadata.Register();

                Assert.That(Metadata.GetDefaultSize(typeof(Button), false), Is.EqualTo(new Size(75, 32)));
                Assert.That(Metadata.GetDefaultSize(typeof(TextBox), false), Is.EqualTo(new Size(120, 32)));
                Assert.That(Metadata.GetDefaultSize(typeof(Canvas), false), Is.EqualTo(new Size(160, 120)));
            });
        }

        [Test]
        public void RegisterIsIdempotent()
        {
            WinUITestHost.Run(() =>
            {
                ShellMetadata.Register();
                Assert.DoesNotThrow(() => ShellMetadata.Register());
                Assert.That(Metadata.GetDefaultSize(typeof(Button), false), Is.EqualTo(new Size(75, 32)));
            });
        }

        [Test]
        public void SeededPropertiesShowUpAsPopularOnARealDesignItem()
        {
            WinUITestHost.Run(() =>
            {
                ShellMetadata.Register();

                using (var doc = new Document(null, CanvasWithButton))
                {
                    var button = doc.DesignContext.RootItem.ContentProperty.CollectionElements[0];

                    // Registered by DependencyProperty rather than by name: Metadata matches
                    // DesignItemProperty.DependencyProperty first, which is unambiguous, whereas
                    // the name form depends on which type FullName reports.
                    Assert.That(Metadata.IsPopularProperty(button.Properties["Width"]), Is.True);
                    Assert.That(Metadata.IsPopularProperty(button.Properties["Height"]), Is.True);
                    Assert.That(Metadata.IsPopularProperty(button.Properties["Margin"]), Is.True);
                    Assert.That(Metadata.IsPopularProperty(button.Properties["Background"]), Is.True);
                }
            });
        }
    }
}
```

- [ ] **Step 2: Run and watch it fail**

```bash
dotnet build WinUIDesigner.sln
```

Expected: `error CS0103: The name 'ShellMetadata' does not exist in the current context`.

- [ ] **Step 3: Implement `ShellMetadata.cs`**

`src/WinUIDesigner.Shell/ShellMetadata.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The seed data the engine's Metadata tables need in order to be useful.
    /// </summary>
    /// <remarks>
    /// Phases 4e and 4f both deferred this to the shell. Unseeded, Metadata's tables are empty,
    /// which leaves the property grid's Popular category blank and ModelTools.GetDefaultSize
    /// with nothing to return - the latter directly affects the size a click-placed control
    /// gets. This seeds only what the shell's own tests exercise; the rest is a stated gap in
    /// the Phase 5 spec. The reference's equivalent is BasicMetadata, which this port does not
    /// have. Spec Decision 22.
    /// <para>
    /// Registering by DependencyProperty rather than by (Type, name): Metadata.IsPopularProperty
    /// checks DesignItemProperty.DependencyProperty first, which is unambiguous, while the name
    /// form depends on which type DesignItemProperty.FullName reports.
    /// </para>
    /// </remarks>
    public static class ShellMetadata
    {
        static readonly object gate = new object();
        static bool registered;

        public static void Register()
        {
            lock (gate)
            {
                if (registered)
                    return;
                registered = true;
            }

            RegisterDefaultSizes();
            RegisterPopularProperties();
        }

        static void RegisterDefaultSizes()
        {
            Metadata.AddDefaultSize(typeof(Button), new Size(75, 32));
            Metadata.AddDefaultSize(typeof(TextBox), new Size(120, 32));
            Metadata.AddDefaultSize(typeof(TextBlock), new Size(100, 20));
            Metadata.AddDefaultSize(typeof(CheckBox), new Size(120, 32));
            Metadata.AddDefaultSize(typeof(RadioButton), new Size(120, 32));
            Metadata.AddDefaultSize(typeof(ComboBox), new Size(120, 32));
            Metadata.AddDefaultSize(typeof(Slider), new Size(160, 32));
            Metadata.AddDefaultSize(typeof(ProgressBar), new Size(160, 8));
            Metadata.AddDefaultSize(typeof(Image), new Size(120, 120));
            Metadata.AddDefaultSize(typeof(Border), new Size(120, 80));
            Metadata.AddDefaultSize(typeof(Canvas), new Size(160, 120));
            Metadata.AddDefaultSize(typeof(Grid), new Size(160, 120));
            Metadata.AddDefaultSize(typeof(StackPanel), new Size(120, 160));
            Metadata.AddDefaultSize(typeof(Microsoft.UI.Xaml.Shapes.Rectangle), new Size(80, 60));
            Metadata.AddDefaultSize(typeof(Microsoft.UI.Xaml.Shapes.Ellipse), new Size(80, 60));
        }

        static void RegisterPopularProperties()
        {
            Metadata.AddPopularProperty(FrameworkElement.WidthProperty);
            Metadata.AddPopularProperty(FrameworkElement.HeightProperty);
            Metadata.AddPopularProperty(FrameworkElement.MarginProperty);
            Metadata.AddPopularProperty(FrameworkElement.HorizontalAlignmentProperty);
            Metadata.AddPopularProperty(FrameworkElement.VerticalAlignmentProperty);
            Metadata.AddPopularProperty(Control.BackgroundProperty);
            Metadata.AddPopularProperty(Control.ForegroundProperty);
            Metadata.AddPopularProperty(Control.FontSizeProperty);
            Metadata.AddPopularProperty(ContentControl.ContentProperty);
            Metadata.AddPopularProperty(TextBlock.TextProperty);
            Metadata.AddPopularProperty(UIElement.VisibilityProperty);
            Metadata.AddPopularProperty(UIElement.OpacityProperty);
        }
    }
}
```

- [ ] **Step 4: Run the suite**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```

Expected: 25 tests, 25 passed.

If `SeededPropertiesShowUpAsPopularOnARealDesignItem` fails on `Background`, the cause is that a `Button`'s `Background` resolves to `Control.BackgroundProperty` — verify with the reported `DesignItemProperty.DependencyProperty` before changing the assertion, and do not silently drop the case.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Shell/ShellMetadata.cs tests/WinUIDesigner.Shell.Tests/ShellMetadataTests.cs
git commit -m "feat: seed the metadata the shell's property grid and placement need"
```

---

## Task 6: ToolboxView, MainWindow, and App wiring

The task that turns the pieces into an application. `ToolboxView` carries the only unit test here; `MainWindow` is verified by hand under DoD 18.

**Files:**
- Create: `src/WinUIDesigner.Shell/ToolboxView.cs`
- Create: `src/WinUIDesigner.Shell/MainWindow.cs`
- Modify: `src/WinUIDesigner.Shell/App.xaml.cs`
- Test: `tests/WinUIDesigner.Shell.Tests/ToolboxViewTests.cs`

**Interfaces:**
- Consumes: `Toolbox.Instance` (Task 2), `Document` (Task 3), `ShellModel` (Task 4), `ShellMetadata.Register()` (Task 5).
- Produces: `WinUIDesigner.Shell.ToolboxView` — `public ToolboxView()`, `public void Attach(IToolService toolService)`, `public void Detach()`; `WinUIDesigner.Shell.MainWindow` — `public MainWindow()`.

- [ ] **Step 1: Write the failing test**

`tests/WinUIDesigner.Shell.Tests/ToolboxViewTests.cs`:

```csharp
using System;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class ToolboxViewTests
    {
        /// <summary>
        /// Stands in for the design context's real IToolService so the test needs no window.
        /// </summary>
        sealed class FakeToolService : IToolService
        {
            ITool currentTool;

            public FakeToolService(ITool pointerTool)
            {
                PointerTool = pointerTool;
                currentTool = pointerTool;
            }

            public ITool PointerTool { get; private set; }

            public ITool CurrentTool
            {
                get { return currentTool; }
                set
                {
                    currentTool = value;
                    var handler = CurrentToolChanged;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                }
            }

            public event EventHandler CurrentToolChanged;
        }

        sealed class FakePointerTool : ITool
        {
            public Microsoft.UI.Input.InputCursor Cursor { get { return null; } }
            public void Activate(IDesignPanel designPanel) { }
            public void Deactivate(IDesignPanel designPanel) { }
        }

        [Test]
        public void SelectingAControlArmsACreateComponentToolForThatType()
        {
            WinUITestHost.Run(() =>
            {
                var tools = new FakeToolService(new FakePointerTool());
                var view = new ToolboxView();
                view.Attach(tools);

                view.SelectForTest(typeof(Button));

                var created = tools.CurrentTool as WinUIDesigner.Designer.Services.CreateComponentTool;
                Assert.That(created, Is.Not.Null, "selecting a toolbox entry must arm a CreateComponentTool");
                Assert.That(created.ComponentType, Is.EqualTo(typeof(Button)));
            });
        }

        [Test]
        public void TheSameControlCanBeArmedTwiceInARow()
        {
            WinUITestHost.Run(() =>
            {
                var pointer = new FakePointerTool();
                var tools = new FakeToolService(pointer);
                var view = new ToolboxView();
                view.Attach(tools);

                view.SelectForTest(typeof(Button));
                var first = tools.CurrentTool;
                Assert.That(first, Is.InstanceOf<WinUIDesigner.Designer.Services.CreateComponentTool>());

                // What the engine does after a placement completes.
                tools.CurrentTool = pointer;

                view.SelectForTest(typeof(Button));
                var second = tools.CurrentTool;

                // The regression this guards: with a ListView whose SelectionChanged arms the
                // tool, the placed control stays selected, so re-clicking it raises no event and
                // nothing re-arms. Spec Decision 12.
                Assert.That(second, Is.InstanceOf<WinUIDesigner.Designer.Services.CreateComponentTool>());
                Assert.That(second, Is.Not.SameAs(first), "a second placement needs a fresh tool");
            });
        }

        [Test]
        public void DetachStopsListeningSoAClosedDocumentCannotDriveTheView()
        {
            WinUITestHost.Run(() =>
            {
                var pointer = new FakePointerTool();
                var tools = new FakeToolService(pointer);
                var view = new ToolboxView();
                view.Attach(tools);
                view.SelectForTest(typeof(Button));

                view.Detach();
                Assert.DoesNotThrow(() => tools.CurrentTool = pointer);
            });
        }
    }
}
```

Both signatures the fakes depend on were read while writing this plan: `ITool` declares exactly `InputCursor Cursor { get; }`, `void Activate(IDesignPanel)` and `void Deactivate(IDesignPanel)` (`src/WinUIDesigner.DesignModel/Tools.cs:29-34`), and `CreateComponentTool` exposes `public Type ComponentType` (`src/WinUIDesigner.Designer/Services/CreateComponentTool.cs:48`). The fakes above are complete as written.

- [ ] **Step 2: Run and watch it fail**

```bash
dotnet build WinUIDesigner.sln
```

Expected: `error CS0246: The type or namespace name 'ToolboxView' could not be found`.

- [ ] **Step 3: Implement `ToolboxView.cs`**

`src/WinUIDesigner.Shell/ToolboxView.cs`:

```csharp
using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.Designer.Services;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The list of controls a user can place, and the arming of the tool that places them.
    /// </summary>
    /// <remarks>
    /// Written in C# rather than XAML: App.xaml is the only XAML file in this project. Ported in
    /// spirit from the reference's ToolboxView.xaml.
    /// <para>
    /// CreateComponentTool is single-use - the engine resets IToolService.CurrentTool to
    /// PointerTool once a placement finishes. A ListView whose SelectionChanged arms the tool
    /// would therefore be stuck after one placement, because the placed entry stays selected and
    /// re-clicking it raises no event. Clearing the selection when the engine resets is what
    /// keeps the same control placeable twice in a row. Spec Decision 12.
    /// </para>
    /// </remarks>
    public sealed class ToolboxView : UserControl
    {
        readonly ListView list = new ListView();
        IToolService toolService;
        bool suppressSelectionChanged;

        public ToolboxView()
        {
            list.SelectionMode = ListViewSelectionMode.Single;
            list.ItemsSource = Toolbox.Instance.AssemblyNodes
                .SelectMany(n => n.Controls)
                .Select(c => c.Type)
                .ToList();
            list.ItemTemplate = null;
            list.DisplayMemberPath = "Name";
            list.SelectionChanged += OnSelectionChanged;

            Content = new ScrollViewer
            {
                Content = list,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        public void Attach(IToolService service)
        {
            Detach();
            toolService = service;
            if (toolService != null)
                toolService.CurrentToolChanged += OnCurrentToolChanged;
        }

        public void Detach()
        {
            if (toolService != null)
                toolService.CurrentToolChanged -= OnCurrentToolChanged;
            toolService = null;
        }

        /// <summary>Selects a control type as a user click would. Test seam.</summary>
        internal void SelectForTest(Type type)
        {
            list.SelectedItem = type;
            // ListView.SelectedItem does not raise SelectionChanged without a live item
            // container, so drive the same handler the UI would.
            Arm(type);
        }

        void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressSelectionChanged)
                return;
            Arm(list.SelectedItem as Type);
        }

        void Arm(Type type)
        {
            if (type == null || toolService == null)
                return;
            toolService.CurrentTool = new CreateComponentTool(type);
        }

        void OnCurrentToolChanged(object sender, EventArgs e)
        {
            if (toolService == null)
                return;
            if (!ReferenceEquals(toolService.CurrentTool, toolService.PointerTool))
                return;

            suppressSelectionChanged = true;
            try
            {
                list.SelectedItem = null;
            }
            finally
            {
                suppressSelectionChanged = false;
            }
        }
    }
}
```

- [ ] **Step 4: Implement `MainWindow.cs`**

`src/WinUIDesigner.Shell/MainWindow.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.Designer;
using WinUIDesigner.Designer.OutlineView;
using WinUIDesigner.Designer.PropertyGrid;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The shell's only window: layout, menu, shortcuts, dialogs, file pickers, and every wire
    /// into the current document's engine services.
    /// </summary>
    /// <remarks>
    /// Written in C# - App.xaml is the only XAML file in this project. ShellModel deliberately
    /// owns none of this: it raises events and this class answers them, so the logic stays
    /// testable without an XamlRoot. Spec Decisions 6, 7, 10, 19, 20 and 21.
    /// </remarks>
    public sealed class MainWindow : Window
    {
        const string ApplicationTitle = "WinUI Designer";

        readonly ShellModel shell = new ShellModel();
        readonly ToolboxView toolboxView = new ToolboxView();
        readonly OutlineViewControl outlineView = new OutlineViewControl();
        readonly TextBox outlineFilter = new TextBox { PlaceholderText = "Filter", Margin = new Thickness(4) };
        readonly ContentControl designHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        readonly ContentControl propertyGridHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };

        PropertyGridControl propertyGrid;
        Document wiredDocument;
        ISelectionService wiredSelection;
        UndoService wiredUndo;
        bool closingConfirmed;

        MenuFlyoutItem undoItem, redoItem, cutItem, copyItem, pasteItem, deleteItem;

        public MainWindow()
        {
            ShellMetadata.Register();

            Title = ApplicationTitle;
            Content = BuildLayout();

            shell.ReportError += OnReportError;
            shell.AskSaveChanges += OnAskSaveChanges;
            shell.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "CurrentDocument")
                    WireCurrentDocument();
            };

            outlineFilter.TextChanged += (s, e) => outlineView.Filter = outlineFilter.Text;

            AppWindow.Closing += OnAppWindowClosing;

            shell.New();
        }

        UIElement BuildLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var menu = BuildMenu();
            Grid.SetRow(menu, 0);
            root.Children.Add(menu);

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

            Grid.SetColumn(toolboxView, 0);
            columns.Children.Add(toolboxView);

            // OutlineViewControl and PropertyGridControl both derive from StackPanel and contain
            // no ScrollViewer of their own, so without this wrapping they clip on any real
            // document. Spec Decision 10.
            var surfaceScroll = new ScrollViewer
            {
                Content = designHost,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetColumn(surfaceScroll, 1);
            columns.Children.Add(surfaceScroll);

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(outlineFilter, 0);
            right.Children.Add(outlineFilter);

            var outlineScroll = new ScrollViewer
            {
                Content = outlineView,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(outlineScroll, 1);
            right.Children.Add(outlineScroll);

            var gridScroll = new ScrollViewer
            {
                Content = propertyGridHost,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(gridScroll, 2);
            right.Children.Add(gridScroll);

            Grid.SetColumn(right, 2);
            columns.Children.Add(right);

            Grid.SetRow(columns, 1);
            root.Children.Add(columns);
            return root;
        }

        MenuBar BuildMenu()
        {
            var file = new MenuBarItem { Title = "File" };
            file.Items.Add(Item("New", VirtualKey.N, VirtualKeyModifiers.Control, (s, e) => shell.New()));
            file.Items.Add(Item("Open...", VirtualKey.O, VirtualKeyModifiers.Control, async (s, e) => await OpenAsync()));
            file.Items.Add(Item("Save", VirtualKey.S, VirtualKeyModifiers.Control, async (s, e) => await SaveAsync(false)));
            file.Items.Add(Item("Save As...", VirtualKey.None, VirtualKeyModifiers.None, async (s, e) => await SaveAsync(true)));
            file.Items.Add(new MenuFlyoutSeparator());
            file.Items.Add(Item("Exit", VirtualKey.None, VirtualKeyModifiers.None, (s, e) => Close()));

            undoItem = Item("Undo", VirtualKey.Z, VirtualKeyModifiers.Control, (s, e) => WithUndo(u => u.Undo()));
            redoItem = Item("Redo", VirtualKey.Y, VirtualKeyModifiers.Control, (s, e) => WithUndo(u => u.Redo()));
            cutItem = Item("Cut", VirtualKey.X, VirtualKeyModifiers.Control, (s, e) => WithClipboard((svc, ctx) => { if (svc.CanCut(ctx)) svc.Cut(ctx); }));
            copyItem = Item("Copy", VirtualKey.C, VirtualKeyModifiers.Control, (s, e) => WithClipboard((svc, ctx) => { if (svc.CanCopy(ctx)) svc.Copy(ctx); }));
            pasteItem = Item("Paste", VirtualKey.V, VirtualKeyModifiers.Control, (s, e) => WithClipboard((svc, ctx) => { if (svc.CanPaste(ctx)) svc.Paste(ctx); }));
            deleteItem = Item("Delete", VirtualKey.Delete, VirtualKeyModifiers.None, (s, e) => WithClipboard((svc, ctx) => { if (svc.CanDelete(ctx)) svc.Delete(ctx); }));

            var edit = new MenuBarItem { Title = "Edit" };
            edit.Items.Add(undoItem);
            edit.Items.Add(redoItem);
            edit.Items.Add(new MenuFlyoutSeparator());
            edit.Items.Add(cutItem);
            edit.Items.Add(copyItem);
            edit.Items.Add(pasteItem);
            edit.Items.Add(deleteItem);

            var bar = new MenuBar();
            bar.Items.Add(file);
            bar.Items.Add(edit);
            return bar;
        }

        MenuFlyoutItem Item(string text, VirtualKey key, VirtualKeyModifiers modifiers, RoutedEventHandler onClick)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += onClick;
            if (key != VirtualKey.None)
            {
                item.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = key, Modifiers = modifiers });
            }
            return item;
        }

        void WithUndo(Action<UndoService> action)
        {
            if (wiredUndo != null)
                action(wiredUndo);
            RefreshCommands();
        }

        void WithClipboard(Action<ICopyPasteService, DesignContext> action)
        {
            var doc = shell.CurrentDocument;
            if (doc == null || doc.DesignContext == null)
                return;
            var svc = doc.DesignContext.Services.GetService<ICopyPasteService>();
            if (svc == null)
                return;
            action(svc, doc.DesignContext);
            RefreshCommands();
        }

        void WireCurrentDocument()
        {
            // Unsubscribe from the outgoing document first. Each LoadDesigner installs a fresh
            // DefaultSelectionService, so a missed unsubscribe leaks a handler per document.
            if (wiredSelection != null)
                wiredSelection.SelectionChanged -= OnSelectionChanged;
            if (wiredUndo != null)
                wiredUndo.UndoStackChanged -= OnUndoStackChanged;
            toolboxView.Detach();

            wiredSelection = null;
            wiredUndo = null;
            wiredDocument = shell.CurrentDocument;

            // A fresh PropertyGridControl per document. Phase 4f recorded that its nodeCache and
            // editorHosts are never invalidated across documents and handed the problem to this
            // phase; replacing the control sidesteps both caches without touching Designer code.
            // Spec Decision 19.
            propertyGrid = new PropertyGridControl();
            propertyGridHost.Content = propertyGrid;

            if (wiredDocument == null)
            {
                designHost.Content = null;
                outlineView.Root = null;
                Title = ApplicationTitle;
                RefreshCommands();
                return;
            }

            designHost.Content = wiredDocument.DesignSurface;
            outlineView.Root = wiredDocument.OutlineRoot;

            wiredSelection = wiredDocument.SelectionService;
            if (wiredSelection != null)
                wiredSelection.SelectionChanged += OnSelectionChanged;

            wiredUndo = wiredDocument.UndoService;
            if (wiredUndo != null)
                wiredUndo.UndoStackChanged += OnUndoStackChanged;

            if (wiredDocument.DesignContext != null)
            {
                var tools = wiredDocument.DesignContext.Services.GetService<IToolService>();
                if (tools != null)
                    toolboxView.Attach(tools);
            }

            UpdateTitle();
            RefreshCommands();
        }

        void OnSelectionChanged(object sender, DesignItemCollectionEventArgs e)
        {
            if (propertyGrid != null && wiredSelection != null)
                propertyGrid.SelectedItems = wiredSelection.SelectedItems;
            RefreshCommands();
        }

        void OnUndoStackChanged(object sender, EventArgs e)
        {
            UpdateTitle();
            RefreshCommands();
        }

        void UpdateTitle()
        {
            Title = wiredDocument == null
                ? ApplicationTitle
                : wiredDocument.Title + " - " + ApplicationTitle;
        }

        void RefreshCommands()
        {
            undoItem.IsEnabled = wiredUndo != null && wiredUndo.CanUndo;
            redoItem.IsEnabled = wiredUndo != null && wiredUndo.CanRedo;

            var ctx = wiredDocument == null ? null : wiredDocument.DesignContext;
            var svc = ctx == null ? null : ctx.Services.GetService<ICopyPasteService>();
            cutItem.IsEnabled = svc != null && svc.CanCut(ctx);
            copyItem.IsEnabled = svc != null && svc.CanCopy(ctx);
            pasteItem.IsEnabled = svc != null && svc.CanPaste(ctx);
            deleteItem.IsEnabled = svc != null && svc.CanDelete(ctx);
        }

        async System.Threading.Tasks.Task OpenAsync()
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            // Unpackaged WinUI requires the picker to be associated with a window's HWND;
            // without this it throws when shown.
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".xaml");

            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            var current = shell.CurrentDocument;
            if (current != null && !shell.Close(current))
                return;

            shell.Open(file.Path);
        }

        async System.Threading.Tasks.Task SaveAsync(bool forceAsk)
        {
            var doc = shell.CurrentDocument;
            if (doc == null)
                return;

            if (!forceAsk && doc.FilePath != null)
            {
                shell.Save(doc);
                UpdateTitle();
                return;
            }

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("XAML", new List<string> { ".xaml" });
            picker.SuggestedFileName = doc.FileName ?? doc.Name;

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            shell.SaveAs(doc, file.Path);
            UpdateTitle();
        }

        void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            if (closingConfirmed || !shell.IsSomethingDirty)
                return;

            // AppWindowClosingEventArgs.Cancel is synchronous; ContentDialog.ShowAsync is not.
            // So cancel this close, ask, and close again once the answer is in. Spec Decision 20.
            args.Cancel = true;
            _ = ConfirmThenCloseAsync();
        }

        async System.Threading.Tasks.Task ConfirmThenCloseAsync()
        {
            var doc = shell.CurrentDocument;
            if (doc != null)
            {
                var answer = await AskSaveChangesAsync(doc);
                if (answer == SaveChangesResult.Cancel)
                    return;
                if (answer == SaveChangesResult.Save)
                {
                    await SaveAsync(doc.FilePath == null);
                    if (doc.IsDirty)
                        return;
                }
                doc.MarkSaved();
            }

            closingConfirmed = true;
            Close();
        }

        void OnAskSaveChanges(object sender, AskSaveChangesEventArgs e)
        {
            // ShellModel's synchronous event cannot await a dialog. Everything that can ask the
            // user does so through the async path (OpenAsync, ConfirmThenCloseAsync) before
            // calling Close, so reaching here means no one asked: answer Cancel, which is the
            // safe default and leaves the document open.
            e.Result = SaveChangesResult.Cancel;
        }

        async System.Threading.Tasks.Task<SaveChangesResult> AskSaveChangesAsync(Document doc)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Unsaved changes",
                Content = "Save changes to \"" + doc.Name + "\"?",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Discard",
                CloseButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                return SaveChangesResult.Save;
            if (result == ContentDialogResult.Secondary)
                return SaveChangesResult.Discard;
            return SaveChangesResult.Cancel;
        }

        void OnReportError(object sender, ShellErrorEventArgs e)
        {
            _ = ShowErrorAsync(e);
        }

        async System.Threading.Tasks.Task ShowErrorAsync(ShellErrorEventArgs e)
        {
            var text = new StringBuilder();
            text.AppendLine(e.Message);

            if (e.Errors != null && e.Errors.Count > 0)
            {
                text.AppendLine();
                foreach (var error in e.Errors.Take(10))
                    text.AppendLine(error.Message + " (" + error.Line + ", " + error.Column + ")");
                if (e.Errors.Count > 10)
                    text.AppendLine("... and " + (e.Errors.Count - 10) + " more.");
            }

            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Error",
                Content = new ScrollViewer { Content = new TextBlock { Text = text.ToString(), TextWrapping = TextWrapping.Wrap } },
                CloseButtonText = "Close"
            };
            await dialog.ShowAsync();
        }
    }
}
```

- [ ] **Step 5: Point `App` at `MainWindow`**

In `src/WinUIDesigner.Shell/App.xaml.cs`, replace `OnLaunched`'s body and the `OnUnhandledException` body:

```csharp
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            window = new MainWindow();
            window.Activate();
        }

        void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Phase B's rule - never throw from the write path - rested on there being no
            // handler anywhere in src/. This is the first phase that owns an Application and can
            // install one. Handled=true keeps a defect in a design-time behaviour from ending the
            // process mid-drag; the message is shown rather than swallowed. Spec Decision 18.
            e.Handled = true;
            var window = this.window;
            if (window == null || window.Content == null)
                return;

            var dialog = new ContentDialog
            {
                XamlRoot = window.Content.XamlRoot,
                Title = "Unexpected error",
                Content = new ScrollViewer
                {
                    Content = new TextBlock { Text = e.Message, TextWrapping = TextWrapping.Wrap }
                },
                CloseButtonText = "Close"
            };
            _ = dialog.ShowAsync();
        }
```

Remove the now-unused `TextBox` placeholder and any `using` it needed.

- [ ] **Step 6: Run the suite**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```

Expected: 28 tests, 28 passed.

- [ ] **Step 7: Walk DoD 18 by hand**

```bash
src/WinUIDesigner.Shell/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.exe
```

Confirm, in order, and write down what happened for each:
1. The window opens on a blank 640×480 canvas with a populated toolbox on the left.
2. Selecting `Button` in the toolbox and clicking the canvas places a button.
3. Selecting the button shows its properties on the right; changing `Width` moves the resize handles.
4. Ctrl+Z reverts the width; Ctrl+Y reapplies it.
5. Delete removes the button.
6. Placing a `Button` again immediately afterwards works — the Decision 12 regression.
7. Ctrl+S prompts for a path and writes a file; the file's text contains the placed control.
8. Reopening that file shows the same content.
9. Making a change and closing the window prompts to save.

- [ ] **Step 8: Confirm the three existing suites are still green**

```bash
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: XamlDom 144 (140 passed, 4 ignored), DesignModel 77/77, Designer 245/245.

- [ ] **Step 9: Commit**

```bash
git add src/WinUIDesigner.Shell tests/WinUIDesigner.Shell.Tests
git commit -m "feat: wire the shell window, menu, shortcuts and toolbox"
```

---

## Task 7: Soak measurement

Phase A measured roughly 4.7 KB of native memory retained per `XamlReader.Load` over 5000 loads. This test is the first thing in the project that would notice if a document open/close cycle leaks.

**Files:**
- Test: `tests/WinUIDesigner.Shell.Tests/SoakTests.cs`

**Interfaces:**
- Consumes: `Document` (Task 3).
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the measurement, ceiling deliberately absent**

`tests/WinUIDesigner.Shell.Tests/SoakTests.cs`:

```csharp
using System;
using System.Diagnostics;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class SoakTests
    {
        // Small and fixed on purpose: absolute memory is dominated by document size (Phase A
        // measured roughly 7 MB for one 1500-attribute document), and what this test looks for
        // is the constant per-load cost, not the per-attribute one.
        const string Fixture =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"150\">" +
            "<Button x:Name=\"A\" Width=\"60\" Height=\"24\" Canvas.Left=\"10\" Canvas.Top=\"10\" />" +
            "<TextBlock x:Name=\"B\" Text=\"x\" Canvas.Left=\"10\" Canvas.Top=\"50\" />" +
            "</Canvas>";

        const int TotalIterations = 2000;
        const int BaselineIteration = 500;

        static long SettledPrivateBytes()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var process = Process.GetCurrentProcess();
            process.Refresh();
            return process.PrivateMemorySize64;
        }

        [Test]
        [Explicit("Long-running: 2000 document loads. Run deliberately, not on every build.")]
        public void OpeningAndDisposingADocumentDoesNotGrowMemoryPerIteration()
        {
            WinUITestHost.Run(() =>
            {
                long baseline = 0;

                for (int i = 0; i < TotalIterations; i++)
                {
                    using (var doc = new Document(null, Fixture))
                    {
                        Assert.That(doc.LoadFailed, Is.False, "iteration " + i + " failed to load");
                    }

                    if (i == BaselineIteration)
                        baseline = SettledPrivateBytes();
                }

                long final = SettledPrivateBytes();
                long span = TotalIterations - BaselineIteration;
                double bytesPerIteration = (double)(final - baseline) / span;

                Console.WriteLine("SOAK baseline@" + BaselineIteration + " = " + baseline);
                Console.WriteLine("SOAK final@" + TotalIterations + " = " + final);
                Console.WriteLine("SOAK bytes/iteration = " + bytesPerIteration.ToString("F1"));

                // The first 500 iterations are excluded from the measurement on purpose: JIT,
                // the WinUI type cache and the toolbox's one-time assembly scan all inflate the
                // early numbers, and none of them recurs.
                Assert.That(bytesPerIteration, Is.LessThan(SoakCeilingBytesPerIteration),
                    "memory grew " + bytesPerIteration.ToString("F1") + " bytes per open/dispose cycle");
            });
        }

        // Filled in by Step 3 from the first measured run.
        const double SoakCeilingBytesPerIteration = 0;
    }
}
```

- [ ] **Step 2: Run it once to get the number**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult --test=WinUIDesigner.Shell.Tests.SoakTests.OpeningAndDisposingADocumentDoesNotGrowMemoryPerIteration
```

Expected: it FAILS on the ceiling of 0, and prints the three `SOAK` lines. Record the `bytes/iteration` figure.

`--explicit` may be required for NUnitLite to run an `[Explicit]` test when named directly; if the run reports 0 tests, add `--explicit` to the command.

- [ ] **Step 3: Set the ceiling from the measurement**

Replace the placeholder constant with the measured figure rounded up to a round number, at least double the measured value so ordinary noise does not make the test flaky, and record the raw measurement beside it:

```csharp
        /// <summary>
        /// Measured on this machine at implementation time: &lt;RAW&gt; bytes per open/dispose
        /// cycle over iterations 500-2000. The ceiling is set well above that so ordinary
        /// allocator noise does not make the test flaky, and well below the ~4.7 KB per
        /// XamlReader.Load that Phase A measured - which is the regression this exists to catch.
        /// </summary>
        const double SoakCeilingBytesPerIteration = /* rounded-up measured value */;
```

If the measured figure is at or above 4700 bytes per iteration, do **not** raise the ceiling past it. That is the leak, it is real, and it should be reported as a finding rather than accommodated.

- [ ] **Step 4: Run it again and watch it pass**

```bash
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult --test=WinUIDesigner.Shell.Tests.SoakTests.OpeningAndDisposingADocumentDoesNotGrowMemoryPerIteration
```

Expected: 1 test, 1 passed.

- [ ] **Step 5: Run the whole suite and confirm the soak test is excluded by default**

```bash
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```

Expected: 28 tests, 28 passed, 1 explicit test not run.

- [ ] **Step 6: Commit**

```bash
git add tests/WinUIDesigner.Shell.Tests/SoakTests.cs
git commit -m "test: measure per-document memory growth as a slope"
```

---

## Plan self-review

**Spec coverage.** Decisions 1–4 → Task 1. Decision 5 → Task 1 Steps 1 and 5. Decisions 6 and 7 → Tasks 3 and 4. Decisions 8 and 9 are scope cuts with nothing to build. Decision 10 → Task 6 Step 4 (`ScrollViewer` wrapping). Decision 11 → Task 2. Decision 12 → Task 6 Steps 1 and 3. Decision 13 is a cut; no toolbox drag is built. Decision 14 → Task 2 Step 4. Decision 15 → Task 1 Step 10. Decision 16 → Task 3 (`ReportErrors` plus the `XamlLoadException` catch) and Task 4 (the two error tests) and Task 6 (the ten-entry dialog). Decision 17 → Task 4. Decision 18 → Task 1 Step 3 and Task 6 Step 5. Decision 19 → Task 6 Step 4 (`WireCurrentDocument`). Decision 20 → Task 6 Step 4 (`OnAppWindowClosing`). Decision 21 → Task 6 Step 4 (`BuildMenu`). Decision 22 → Task 5. Decision 23 → Task 7.

DoD items map as: 1, 2, 3 → Task 2; 4 → Tasks 1 and 4; 5, 6, 7, 8, 13 → Task 3; 9, 10, 11, 12 → Task 4; 14 → Task 6; 15 → Task 5; 16 → Task 7; 17 → Tasks 1 and 6 Step 8; 18 → Task 6 Step 7.

**One known soft spot**, flagged in place rather than hidden. `ShellModel.Close`'s synchronous `AskSaveChanges` cannot await a dialog, so `MainWindow` answers it Cancel and does its asking on the async path before calling `Close`; the comment in `OnAskSaveChanges` says so explicitly. If a reviewer judges that too subtle, the fix is to make `Close` take the answer as a parameter rather than raising an event — a change to Task 4's interface, not a defect in Task 6. Every signature this plan's code depends on was read from the tree while writing it, including `ITool` and `CreateComponentTool.ComponentType`.

**Type consistency.** `Document.SetTempName` is introduced as `Name_ForTest_SetTempName` in Task 3 and renamed in Task 4 Step 4, with both call sites named. `ToolboxView.Attach`/`Detach`/`SelectForTest` are used identically in Task 6's test and implementation. `ShellErrorEventArgs.Errors` is `IReadOnlyList<XamlError>` in Task 4's definition and consumed as such in Task 6's `ShowErrorAsync`. `SaveChangesResult` has the same three members everywhere.

**No placeholders**, with one deliberate exception: `SoakCeilingBytesPerIteration` starts at 0 so the test fails, and Task 7 Step 3 replaces it with a measured number. That is the point of the task, not a gap.
