# Phase 4g: OutlineView Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the design-time document-tree panel — a control that renders the loaded document's `DesignItem` hierarchy as a tree, syncs selection with `ISelectionService` both ways, lets the user hide/lock/filter elements, and reparents elements by drag.

**Architecture:** `IOutlineNode` and its two supporting service interfaces live in `WinUIDesigner.DesignModel` (engine layer); every implementation lives in `WinUIDesigner.Designer/OutlineView/`. The node model (`OutlineNodeBase` → `OutlineNode` → `PropertyOutlineNode`) is a near-verbatim port of the reference's, mirroring the `DesignItem` tree and pushing changes out through `INotifyPropertyChanged`/`ObservableCollection`. The view is a WinUI 3 `Microsoft.UI.Xaml.Controls.TreeView` whose `TreeViewNode.Content` is a row `Grid` constructed in C# — no `.xaml`, no `DataTemplate`, no `{Binding}` — with a `Dictionary` pair mapping the two trees.

**Tech Stack:** C# / .NET 10 / WinUI 3 (Windows App SDK 1.7.250606001) / NUnit + NUnitLite.

**Spec:** `docs/superpowers/specs/2026-09-05-winui-designer-phase4g-design.md`

## Global Constraints

- Target framework for every project: `net10.0-windows10.0.19041.0`, `UseWinUI=true`, `RuntimeIdentifier` `win-x64`, `Nullable` is `disable`.
- `Microsoft.WindowsAppSDK` is pinned to `1.7.250606001` — never bump it.
- Build: `dotnet build WinUIDesigner.sln`.
- **Tests MUST be run via each test project's built `.exe` with `--noresult`. NEVER `dotnet test`** — it cannot activate `Microsoft.UI.Xaml` WinRT classes and silently reports every WinUI-dependent test as *ignored* rather than run. The three executables:
  - `tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe`
  - `tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe`
  - `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe`
  - Single test: append `--test=<FullyQualifiedName>`.
- Project dependency direction `Designer` → `DesignModel` → `XamlDom` must not be violated. **`WinUIDesigner.DesignModel` must never reference `WinUIDesigner.Designer`.**
- Files ported from SharpDevelop keep their original MIT copyright header (copy the 17-line header verbatim from the reference file being ported).
- **Zero `.xaml` files in `src/`** — every visual is built by C# object construction.
- CLAUDE.md's "Known WinUI platform gaps" list applies: before assuming a WPF API exists in WinUI, verify it.
- Indentation: `src/WinUIDesigner.DesignModel/DesignItem.cs` uses **tabs**; `Services.cs`, `ModelTools.cs` and everything under `PropertyGrid/` use **4 spaces**. Match the file you are editing. New files in `Designer/OutlineView/` and `DesignModel/Interfaces/` use 4 spaces.

## Plan-level rulings

These resolve wording in the spec that would otherwise be ambiguous to an implementer:

1. **`OutlineViewControl` derives from `Microsoft.UI.Xaml.Controls.StackPanel`, not `Control`.** The spec's Architecture block says "Control"; that names the role, not the base type. A bare `Control` subclass renders nothing without a `ControlTemplate`, and this codebase has zero `.xaml` in which to declare one. `PropertyGridControl` derives from `StackPanel` for exactly this reason (`src/WinUIDesigner.Designer/PropertyGrid/PropertyGridControl.cs:38`); follow it.
2. **`IOutlineNode` lives in namespace `WinUIDesigner.DesignModel`** even though its file sits in `src/WinUIDesigner.DesignModel/Interfaces/`. The reference puts it in its root engine namespace, `DesignItem.cs` (same namespace) consumes it directly, and this avoids adding a `using` to `DesignItem.cs`. Note this differs from the neighbouring `IObservableList.cs`, which uses `WinUIDesigner.DesignModel.Interfaces`.
3. **`TreeViewNode`'s parent property is `Parent`, not `ParentNode`.** A byte-scan of `Microsoft.UI.Xaml.winmd` finds zero occurrences of `ParentNode` and confirms `ContainerFromNode`, `NodeFromContainer`, `TreeViewDragItemsCompletedEventArgs`, `TreeViewSelectionChangedEventArgs`, `TreeViewItemInvokedEventArgs`, `HasUnrealizedChildren`, `IsExpanded` and `Depth` are all present.
4. **`CanInsert`/`Insert` are added to `IOutlineNode` in Task 6, not Task 2.** Every task must compile and pass on its own; declaring interface members in Task 2 that nothing implements until Task 6 would break Tasks 4 and 5.

---

### Task 1: WinUI TreeView platform probe

The spec's Decision 5 leaves one thing genuinely unknown: whether WinUI's built-in `CanReorderItems` reorder reports enough information afterwards to apply the same move to the model. Decision 4 likewise assumes, without proof, that a `TreeViewItem` renders a `UIElement`-valued `Content`. Both are settled here, by characterization tests that stay in the suite as living documentation of the platform's behaviour — the same role `RenderedGeometryInvestigationTests.cs` already plays in this project.

**Files:**
- Create: `tests/WinUIDesigner.Designer.Tests/TreeViewPlatformProbeTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: a recorded ruling — **drag branch A (primary)** or **drag branch B (fallback)** — written into this task's commit message and consumed by Task 10. Nothing else in the codebase depends on this task.

- [ ] **Step 1: Write the probe tests**

Create `tests/WinUIDesigner.Designer.Tests/TreeViewPlatformProbeTests.cs`:

```csharp
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    /// <summary>
    /// Characterization tests for the two WinUI TreeView behaviours Phase 4g's design depends on
    /// (spec Decisions 4 and 5). These assert what the platform actually does, not what this
    /// codebase does - they exist so a future SDK bump that changes either behaviour fails loudly
    /// here instead of silently breaking OutlineViewControl. Same role as
    /// RenderedGeometryInvestigationTests.cs.
    /// </summary>
    [TestFixture]
    public class TreeViewPlatformProbeTests
    {
        [Test]
        public void TreeViewItemRendersAUIElementValuedContent()
        {
            WinUITestHost.RunWithWindow(window => {
                var tree = new TreeView();
                window.Content = tree;

                var row = new TextBlock { Text = "probe" };
                var node = new TreeViewNode { Content = row };
                tree.RootNodes.Add(node);

                tree.UpdateLayout();

                var container = tree.ContainerFromNode(node) as TreeViewItem;
                Assert.IsNotNull(container, "TreeView did not realize a container for a root node");
                Assert.AreSame(row, container.Content,
                    "TreeViewItem.Content is not the UIElement we put on TreeViewNode.Content");
            });
        }

        [Test]
        public void MovingATreeViewNodeReportsItsNewParentAndIndex()
        {
            WinUITestHost.RunWithWindow(window => {
                var tree = new TreeView();
                window.Content = tree;

                var root = new TreeViewNode { Content = new TextBlock { Text = "root" }, IsExpanded = true };
                var a = new TreeViewNode { Content = new TextBlock { Text = "a" } };
                var b = new TreeViewNode { Content = new TextBlock { Text = "b" }, IsExpanded = true };
                root.Children.Add(a);
                root.Children.Add(b);
                tree.RootNodes.Add(root);
                tree.UpdateLayout();

                // Exactly the mutation WinUI's own built-in reorder performs on drop: detach the
                // node from its old parent's Children and insert it into the new parent's. If the
                // node reports its new parent and index correctly afterwards, DragItemsCompleted
                // can drive IOutlineNode.Insert; if it does not, branch B is forced.
                root.Children.Remove(a);
                b.Children.Add(a);
                tree.UpdateLayout();

                Assert.AreSame(b, a.Parent, "TreeViewNode.Parent does not track a reparent");
                Assert.AreEqual(0, b.Children.IndexOf(a), "new parent does not report the moved node's index");
                Assert.AreEqual(-1, root.Children.IndexOf(a), "old parent still contains the moved node");
            });
        }
    }
}
```

- [ ] **Step 2: Build and run the probe**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.TreeViewPlatformProbeTests
```

Expected: both tests pass. If `TreeViewItemRendersAUIElementValuedContent` fails, **stop and report** — Decision 4's entire row-rendering approach is invalid and the spec needs revisiting, which is a plan defect, not something to work around.

- [ ] **Step 3: Record the drag branch**

Decide from the second test's result:
- **Both assertions in `MovingATreeViewNodeReportsItsNewParentAndIndex` pass → branch A (primary).** Task 10 uses `CanReorderItems = true` and drives the model from `DragItemsCompleted`.
- **Either assertion fails → branch B (fallback).** Task 10 uses `CanReorderItems = false` and handles `DragOver`/`Drop` directly, inserting at the end of the drop target's children.

If a probe assertion fails, keep the test but invert it to assert the behaviour actually observed, with a comment naming which branch that forces. Do not delete the test.

- [ ] **Step 4: Commit**

```bash
git add tests/WinUIDesigner.Designer.Tests/TreeViewPlatformProbeTests.cs
git commit -m "test: characterize WinUI TreeView content rendering and node reparenting

Records Phase 4g drag branch: <A|B>."
```

The commit message MUST name the branch — Task 10's implementer reads it.

---

### Task 2: Engine-layer seams

`IOutlineNode` (minus `CanInsert`/`Insert`, which arrive in Task 6 — see plan ruling 4), the two service interfaces, `DesignItem.CreateOutlineNode()`, and `ModelTools.CanSelectComponent`.

**Files:**
- Create: `src/WinUIDesigner.DesignModel/Interfaces/IOutlineNode.cs`
- Modify: `src/WinUIDesigner.DesignModel/Services.cs` (append two interfaces before the closing brace)
- Modify: `src/WinUIDesigner.DesignModel/DesignItem.cs:355` (add `CreateOutlineNode()` immediately after `Clone()`)
- Modify: `src/WinUIDesigner.Designer/ModelTools.cs` (add `CanSelectComponent` after `GetHeight`, which ends at :33)
- Test: `tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs` (new file)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `interface WinUIDesigner.DesignModel.IOutlineNode` with `ISelectionService SelectionService { get; }`, `ServiceContainer Services { get; }`, `DesignItem DesignItem { get; set; }`, `bool IsExpanded { get; set; }`, `bool IsSelected { get; set; }`, `bool IsDesignTimeVisible { get; set; }`, `bool IsDesignTimeLocked { get; set; }`, `string Name { get; }`, `ObservableCollection<IOutlineNode> Children { get; }`
  - `interface WinUIDesigner.DesignModel.IOutlineNodeService` with `IOutlineNode Create(DesignItem designItem)`
  - `interface WinUIDesigner.DesignModel.IOutlineNodeNameService` with `string GetOutlineNodeName(DesignItem designItem)`
  - `public IOutlineNode DesignItem.CreateOutlineNode()`
  - `public static bool ModelTools.CanSelectComponent(DesignItem item)`

- [ ] **Step 1: Write the failing test**

Create `tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class OutlineViewTests
    {
        // The reference's own outline fixture, verbatim: HierarchyTests, SelectionTests and
        // InsertTests all use "<Button/><StackPanel><Button/></StackPanel>" inside a Grid.
        // It deliberately sets NO non-content property on the Grid: from Task 5 onwards every set
        // non-content property becomes a leading PropertyOutlineNode, which would shift every
        // index-based assertion in Tasks 4-10. Any test needing a non-visual collection element
        // declares its own XAML instead of adding one here.
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button />" +
            "<StackPanel><Button /></StackPanel>" +
            "</Grid>";

        static DesignSurface LoadSurface(Window window, string xaml)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            return surface;
        }

        [Test]
        public void CanSelectComponentIsTrueForAVisualItemAndFalseForANonVisualOne()
        {
            WinUITestHost.RunWithWindow(window => {
                // Own fixture: RowDefinitions gives a non-visual collection element, and keeping
                // it out of GridXaml keeps every other test's child indices stable once Task 5
                // starts emitting PropertyOutlineNodes for set non-content properties.
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
                    "<Button />" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var button = grid.ContentProperty.CollectionElements[0];

                Assert.IsNotNull(button.View);
                Assert.IsTrue(ModelTools.CanSelectComponent(button));

                var rowDefinition = grid.Properties["RowDefinitions"].CollectionElements[0];
                Assert.IsNull(rowDefinition.View,
                    "a RowDefinition is not a UIElement, so its DesignItem must have no View");
                Assert.IsFalse(ModelTools.CanSelectComponent(rowDefinition));
            });
        }
    }
}
```

If `rowDefinition.View` turns out non-null, replace those three lines with a `DesignItem` built directly from a non-visual component instead — `var rowDefinition = surface.DesignContext.Services.Component.RegisterComponentForDesigner(new RowDefinition());` — and keep the same two assertions. Do not weaken the assertion to make it pass.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet build WinUIDesigner.sln
```

Expected: FAIL to compile with `CS0117`/`CS1061` — `ModelTools` has no `CanSelectComponent`.

- [ ] **Step 3: Create `IOutlineNode`**

Create `src/WinUIDesigner.DesignModel/Interfaces/IOutlineNode.cs`. Copy the 17-line SharpDevelop MIT header verbatim from `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign\Project\Interfaces\IOutlineNode.cs`, then:

```csharp
using System.Collections.ObjectModel;

namespace WinUIDesigner.DesignModel
{
    /// <summary>
    /// One node of the design-time outline tree. Ported from the reference's IOutlineNode.cs.
    /// Two deviations: CanInsert/Insert are added in Phase 4g Task 6 rather than declared here
    /// up front, and IsDesignTimeLocked is settable (spec Decision 15) - the reference's XAML
    /// two-way-binds its lock toggle to the concrete node class through DataContext, while this
    /// port's control holds nodes only as IOutlineNode and would otherwise have to downcast.
    /// </summary>
    public interface IOutlineNode
    {
        ISelectionService SelectionService { get; }
        ServiceContainer Services { get; }
        DesignItem DesignItem { get; set; }
        bool IsExpanded { get; set; }
        bool IsSelected { get; set; }
        bool IsDesignTimeVisible { get; set; }
        bool IsDesignTimeLocked { get; set; }
        string Name { get; }
        ObservableCollection<IOutlineNode> Children { get; }
    }
}
```

- [ ] **Step 4: Append the two service interfaces to `Services.cs`**

In `src/WinUIDesigner.DesignModel/Services.cs`, immediately before the file's final closing `}` (after `IKeyBindingService`), add:

```csharp
    /// <summary>Creates outline nodes, returning a cached node for an already-seen DesignItem.</summary>
    public interface IOutlineNodeService
    {
        IOutlineNode Create(DesignItem designItem);
    }

    /// <summary>Produces the display name for an outline node.</summary>
    public interface IOutlineNodeNameService
    {
        string GetOutlineNodeName(DesignItem designItem);
    }
```

- [ ] **Step 5: Add `CreateOutlineNode()` to `DesignItem`**

In `src/WinUIDesigner.DesignModel/DesignItem.cs`, immediately after `public abstract DesignItem Clone();` (line 355), add — **using tabs, matching this file**:

```csharp

		/// <summary>
		/// Gets the <see cref="IOutlineNode"/> for this design item, creating it on first use.
		/// Requires IOutlineNodeService, which DesignSurface.LoadDesigner registers.
		/// </summary>
		public IOutlineNode CreateOutlineNode()
		{
			return Services.GetRequiredService<IOutlineNodeService>().Create(this);
		}
```

- [ ] **Step 6: Add `CanSelectComponent` to `ModelTools`**

In `src/WinUIDesigner.Designer/ModelTools.cs`, after `GetHeight` (which ends at line 33), add:

```csharp

        /// <summary>Whether this item can appear as a selectable node in its own right. Ported
        /// verbatim from the reference's ModelTools.CanSelectComponent (WpfDesign.Designer/
        /// Project/ModelTools.cs:91-94). An item without a View is a non-visual collection
        /// element (a RowDefinition, say); OutlineNode.UpdateChildrenCore skips over it and
        /// promotes its own content children in its place instead of showing it as a node.</summary>
        public static bool CanSelectComponent(DesignItem item)
        {
            return item.View != null;
        }
```

- [ ] **Step 7: Run the test to verify it passes**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/WinUIDesigner.DesignModel/Interfaces/IOutlineNode.cs src/WinUIDesigner.DesignModel/Services.cs src/WinUIDesigner.DesignModel/DesignItem.cs src/WinUIDesigner.Designer/ModelTools.cs tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs
git commit -m "feat: add IOutlineNode, outline services, CreateOutlineNode and CanSelectComponent"
```

---

### Task 3: OutlineNodeNameService

**Files:**
- Create: `src/WinUIDesigner.Designer/OutlineView/OutlineNodeNameService.cs`
- Modify: `src/WinUIDesigner.Designer/DesignSurface.cs:29-45` (add one registration line)
- Test: `tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs`

**Interfaces:**
- Consumes: `IOutlineNodeNameService` (Task 2).
- Produces: `public class WinUIDesigner.Designer.OutlineView.OutlineNodeNameService : IOutlineNodeNameService`, registered into every `DesignContext` by `DesignSurface.LoadDesigner`, resolvable via `context.Services.GetService<IOutlineNodeNameService>()`.

- [ ] **Step 1: Write the failing test**

Add to `OutlineViewTests.cs`:

```csharp
        [Test]
        public void OutlineNodeNameIsTheTypeNameAloneOrWithTheXName()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Button /><Button x:Name=\"A\" />" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var names = surface.DesignContext.Services.GetService<IOutlineNodeNameService>();
                Assert.IsNotNull(names, "IOutlineNodeNameService was not registered");

                Assert.AreEqual("Button", names.GetOutlineNodeName(grid.ContentProperty.CollectionElements[0]));
                Assert.AreEqual("Button (A)", names.GetOutlineNodeName(grid.ContentProperty.CollectionElements[1]));
                Assert.AreEqual("", names.GetOutlineNodeName(null));
            });
        }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests.OutlineNodeNameIsTheTypeNameAloneOrWithTheXName
```

Expected: FAIL — `IOutlineNodeNameService was not registered`.

- [ ] **Step 3: Create the service**

Create `src/WinUIDesigner.Designer/OutlineView/OutlineNodeNameService.cs`. Copy the 17-line SharpDevelop MIT header verbatim from `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\OutlineView\OutlineNodeNameService.cs`, then:

```csharp
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.OutlineView
{
    /// <summary>
    /// Produces an outline node's display name. Ported from the reference's
    /// OutlineNodeNameService.cs with no behavioural change.
    /// </summary>
    public class OutlineNodeNameService : IOutlineNodeNameService
    {
        public string GetOutlineNodeName(DesignItem designItem)
        {
            if (designItem == null)
                return "";
            if (string.IsNullOrEmpty(designItem.Name))
                return designItem.ComponentType.Name;
            return designItem.ComponentType.Name + " (" + designItem.Name + ")";
        }
    }
}
```

- [ ] **Step 4: Register it**

In `src/WinUIDesigner.Designer/DesignSurface.cs`, immediately after the `IToolService` registration line (the last `CustomServiceRegisterFunctions.Add` before `InitializeDesigner`), add:

```csharp
            // The reference registers both outline services inside XamlDesignContext's own
            // constructor (Xaml/XamlDesignContext.cs:77-78). Same constraint as OptionService
            // above: both implementations live in WinUIDesigner.Designer, and XamlDesignContext
            // lives in WinUIDesigner.DesignModel, which does not (and must not) reference it.
            loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(IOutlineNodeNameService), new OutlineView.OutlineNodeNameService()));
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests
```

Expected: PASS (both tests in the fixture).

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.Designer/OutlineView/OutlineNodeNameService.cs src/WinUIDesigner.Designer/DesignSurface.cs tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs
git commit -m "feat: add OutlineNodeNameService and register it into every design context"
```

---

### Task 4: Node model — hierarchy and selection

`OutlineNodeBase` (abstract) + `OutlineNode` handling **only** the collection-valued content property + `OutlineNodeService` + its registration. Property nodes, non-collection content and incremental collection updates arrive in Task 5; `CanInsert`/`Insert` in Task 6.

**Files:**
- Create: `src/WinUIDesigner.Designer/OutlineView/OutlineNodeBase.cs`
- Create: `src/WinUIDesigner.Designer/OutlineView/OutlineNode.cs`
- Modify: `src/WinUIDesigner.Designer/DesignSurface.cs` (add the `IOutlineNodeService` registration)
- Test: `tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs`

**Interfaces:**
- Consumes: `IOutlineNode`, `IOutlineNodeService`, `IOutlineNodeNameService`, `DesignItem.CreateOutlineNode()`, `ModelTools.CanSelectComponent` (Tasks 2-3).
- Produces:
  - `public abstract class WinUIDesigner.Designer.OutlineView.OutlineNodeBase : INotifyPropertyChanged, IOutlineNode` with `protected abstract void UpdateChildren()`, `protected abstract void UpdateChildrenCollectionChanged(NotifyCollectionChangedEventArgs e)`, `protected static PlacementType DummyPlacementType`, `public void RaisePropertyChanged(string name)`, and protected constructors `OutlineNodeBase(DesignItem)` / `OutlineNodeBase(string name)`.
  - `public class WinUIDesigner.Designer.OutlineView.OutlineNode : OutlineNodeBase` with `protected OutlineNode(DesignItem)` / `protected OutlineNode(string name)` and nested `internal class OutlineNodeService : IOutlineNodeService, IDisposable`.

- [ ] **Step 1: Write the failing tests**

Add to `OutlineViewTests.cs`:

```csharp
        [Test]
        public void TheOutlineMirrorsTheDesignItemHierarchy()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();
                Assert.IsNotNull(outline);

                Assert.AreEqual(2, outline.Children.Count);
                Assert.AreEqual(1, outline.Children[1].Children.Count);

                var button = grid.ContentProperty.CollectionElements[0];
                var stackPanel = grid.ContentProperty.CollectionElements[1];
                var stackPanelButton = stackPanel.ContentProperty.CollectionElements[0];

                Assert.AreEqual(button, outline.Children[0].DesignItem);
                Assert.AreEqual(stackPanel, outline.Children[1].DesignItem);
                Assert.AreEqual(stackPanelButton, outline.Children[1].Children[0].DesignItem);
                Assert.AreEqual("Button", outline.Children[0].Name);
            });
        }

        [Test]
        public void CreateOutlineNodeReturnsTheSameNodeForTheSameDesignItem()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                Assert.AreSame(grid.CreateOutlineNode(), grid.CreateOutlineNode());
            });
        }

        [Test]
        public void SelectingInTheDesignerSelectsTheMatchingOutlineNodes()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();

                var stackPanel = grid.ContentProperty.CollectionElements[1];
                var stackPanelButton = stackPanel.ContentProperty.CollectionElements[0];
                surface.DesignContext.Services.Selection.SetSelectedComponents(
                    new[] { stackPanel, stackPanelButton });

                Assert.IsFalse(outline.IsSelected);
                Assert.IsFalse(outline.Children[0].IsSelected);
                Assert.IsTrue(outline.Children[1].IsSelected);
                Assert.IsTrue(outline.Children[1].Children[0].IsSelected);
            });
        }

        [Test]
        public void SelectingAnOutlineNodeSelectsItInTheDesigner()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();
                var selection = surface.DesignContext.Services.Selection;

                selection.SetSelectedComponents(new DesignItem[0]);
                outline.IsSelected = true;
                outline.Children[0].IsSelected = true;

                Assert.IsTrue(selection.SelectedItems.Contains(grid));
                Assert.IsTrue(selection.SelectedItems.Contains(grid.ContentProperty.CollectionElements[0]));
            });
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests
```

Expected: the four new tests FAIL with a `ServiceRequiredException` for `IOutlineNodeService`.

- [ ] **Step 3: Create `OutlineNodeBase`**

Create `src/WinUIDesigner.Designer/OutlineView/OutlineNodeBase.cs`. Copy the 17-line SharpDevelop MIT header verbatim from the reference's `OutlineNodeBase.cs`, then:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.Designer.OutlineView
{
    /// <summary>
    /// Base for every outline node. Ported from the reference's OutlineNodeBase.cs. Deviations:
    /// DummyPlacementType is registered exactly once, here, rather than in two derived types'
    /// static constructors (spec Decision 11); the reference's IsVisualNode (a WPF Visibility
    /// helper for its XAML) is dropped since this port builds rows in C#; CanInsert/Insert land
    /// in Phase 4g Task 6; GetAttachedProperty takes (ownerType, name) in this port rather than
    /// the reference's (DependencyProperty).
    /// </summary>
    public abstract class OutlineNodeBase : INotifyPropertyChanged, IOutlineNode
    {
        protected abstract void UpdateChildren();
        protected abstract void UpdateChildrenCollectionChanged(NotifyCollectionChangedEventArgs e);

        /// <summary>Used only to ask a container whether an item could enter it.</summary>
        protected static readonly PlacementType DummyPlacementType = PlacementType.Register("DummyPlacement");

        bool _collectionWasChanged;
        readonly string _name;

        protected OutlineNodeBase(DesignItem designItem)
        {
            DesignItem = designItem;

            bool hidden = false;
            try {
                hidden = object.Equals(
                    designItem.Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsHidden")
                        .GetConvertedValueOnInstance<bool>(), true);
            } catch (Exception) { }
            if (hidden)
                _isDesignTimeVisible = false;

            bool locked = false;
            try {
                locked = object.Equals(
                    designItem.Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsLocked")
                        .GetConvertedValueOnInstance<bool>(), true);
            } catch (Exception) { }
            if (locked)
                _isDesignTimeLocked = true;

            DesignItem.NameChanged += DesignItem_NameChanged;

            if (DesignItem.ContentProperty != null && DesignItem.ContentProperty.IsCollection)
                DesignItem.ContentProperty.CollectionElements.CollectionChanged += CollectionElements_CollectionChanged;

            DesignItem.PropertyChanged += DesignItem_PropertyChanged;
        }

        protected OutlineNodeBase(string name)
        {
            _name = name;
        }

        public DesignItem DesignItem { get; set; }

        public virtual ServiceContainer Services {
            get { return this.DesignItem.Services; }
        }

        public ISelectionService SelectionService {
            get { return DesignItem != null ? DesignItem.Services.Selection : null; }
        }

        bool isExpanded = true;

        public bool IsExpanded {
            get { return isExpanded; }
            set {
                isExpanded = value;
                RaisePropertyChanged("IsExpanded");
            }
        }

        bool isSelected;

        public bool IsSelected {
            get { return isSelected; }
            set {
                if (isSelected != value && SelectionService != null) {
                    isSelected = value;
                    SelectionService.SetSelectedComponents(new[] { DesignItem },
                        value ? SelectionTypes.Add : SelectionTypes.Remove);
                    RaisePropertyChanged("IsSelected");
                }
            }
        }

        bool _isDesignTimeVisible = true;

        public bool IsDesignTimeVisible {
            get { return _isDesignTimeVisible; }
            set {
                _isDesignTimeVisible = value;
                RaisePropertyChanged("IsDesignTimeVisible");

                if (!value)
                    DesignItem.Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsHidden").SetValue(true);
                else
                    DesignItem.Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsHidden").Reset();
            }
        }

        bool _isDesignTimeLocked;

        public bool IsDesignTimeLocked {
            get { return _isDesignTimeLocked; }
            set {
                _isDesignTimeLocked = value;
                ((XamlDesignItem)DesignItem).IsDesignTimeLocked = _isDesignTimeLocked;
                RaisePropertyChanged("IsDesignTimeLocked");
            }
        }

        readonly ObservableCollection<IOutlineNode> children = new ObservableCollection<IOutlineNode>();

        public ObservableCollection<IOutlineNode> Children {
            get { return children; }
        }

        public string Name {
            get {
                if (!string.IsNullOrEmpty(_name))
                    return _name;
                return DesignItem.Services.GetService<IOutlineNodeNameService>().GetOutlineNodeName(DesignItem);
            }
        }

        void DesignItem_NameChanged(object sender, EventArgs e)
        {
            RaisePropertyChanged("Name");
        }

        void DesignItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // A collection change raises BOTH CollectionChanged and PropertyChanged; the flag
            // stops the second one triggering a redundant full rebuild that would throw away the
            // incremental update CollectionElements_CollectionChanged just did.
            if (!_collectionWasChanged)
                UpdateChildren();
            _collectionWasChanged = false;
        }

        void CollectionElements_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            _collectionWasChanged = true;
            UpdateChildrenCollectionChanged(e);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void RaisePropertyChanged(string name)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}
```

- [ ] **Step 4: Create `OutlineNode`**

Create `src/WinUIDesigner.Designer/OutlineView/OutlineNode.cs`. Copy the 17-line SharpDevelop MIT header verbatim from the reference's `OutlineNode.cs`, then:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.OutlineView
{
    /// <summary>
    /// An outline node standing for one DesignItem. Ported from the reference's OutlineNode.cs.
    /// Phase 4g Task 4 implements only the collection-valued content property; Task 5 adds
    /// property nodes, non-collection content and the incremental collection-changed path.
    /// </summary>
    public class OutlineNode : OutlineNodeBase
    {
        protected OutlineNode(DesignItem designItem) : base(designItem)
        {
            UpdateChildren();
            SelectionService.SelectionChanged += Selection_SelectionChanged;
        }

        protected OutlineNode(string name) : base(name)
        {
        }

        void Selection_SelectionChanged(object sender, DesignItemCollectionEventArgs e)
        {
            IsSelected = DesignItem.Services.Selection.IsComponentSelected(DesignItem);
        }

        protected override void UpdateChildren()
        {
            Children.Clear();

            if (DesignItem.ContentPropertyName != null) {
                var content = DesignItem.ContentProperty;
                if (content.IsCollection)
                    UpdateChildrenCore(content.CollectionElements);
            }
        }

        protected override void UpdateChildrenCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            UpdateChildren();
        }

        bool UpdateChildrenCore(IEnumerable<DesignItem> items, int index = -1)
        {
            var retVal = false;
            foreach (var item in items) {
                if (ModelTools.CanSelectComponent(item)) {
                    if (Children.All(x => x.DesignItem != item)) {
                        var node = item.CreateOutlineNode();
                        if (index > -1)
                            Children.Insert(index++, node);
                        else
                            Children.Add(node);
                        retVal = true;
                    }
                } else {
                    var content = item.ContentProperty;
                    if (content != null) {
                        if (content.IsCollection) {
                            UpdateChildrenCore(content.CollectionElements);
                            retVal = true;
                        } else if (content.Value != null) {
                            UpdateChildrenCore(new[] { content.Value });
                            retVal = true;
                        }
                    }
                }
            }

            return retVal;
        }

        internal class OutlineNodeService : IOutlineNodeService, IDisposable
        {
            readonly Dictionary<DesignItem, IOutlineNode> outlineNodes = new Dictionary<DesignItem, IOutlineNode>();

            public IOutlineNode Create(DesignItem designItem)
            {
                IOutlineNode node = null;
                if (designItem != null && !outlineNodes.TryGetValue(designItem, out node)) {
                    node = new OutlineNode(designItem);
                    outlineNodes[designItem] = node;
                }

                return node;
            }

            public void Dispose()
            {
                outlineNodes.Clear();
            }
        }
    }
}
```

Note the recursion guard the reference relies on and this port keeps: `OutlineNodeService.Create` inserts into `outlineNodes` **after** the `OutlineNode` constructor (and therefore `UpdateChildren`) has run. A cyclic `DesignItem` graph would recurse forever; the document tree is acyclic, so this is the reference's behaviour preserved, not a new risk.

- [ ] **Step 5: Register `IOutlineNodeService`**

In `src/WinUIDesigner.Designer/DesignSurface.cs`, immediately after the `IOutlineNodeNameService` registration added in Task 3, add:

```csharp
            loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(IOutlineNodeService), new OutlineView.OutlineNode.OutlineNodeService()));
```

`OutlineNodeService` is `internal` to `WinUIDesigner.Designer`, and `DesignSurface` is in the same assembly, so this compiles without widening its accessibility.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests
```

Expected: PASS (six tests).

- [ ] **Step 7: Run the full suite**

```bash
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: all green. `WinUIDesigner.XamlDom.Tests` has 4 pre-existing unrelated ignores; that is the known baseline, not a regression.

- [ ] **Step 8: Commit**

```bash
git add src/WinUIDesigner.Designer/OutlineView/OutlineNodeBase.cs src/WinUIDesigner.Designer/OutlineView/OutlineNode.cs src/WinUIDesigner.Designer/DesignSurface.cs tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs
git commit -m "feat: add outline node model with hierarchy mirroring and two-way selection sync"
```

---

### Task 5: Property nodes, non-collection content, incremental collection updates

Completes `UpdateChildren` to the reference's full behaviour and adds `PropertyOutlineNode`.

**Files:**
- Create: `src/WinUIDesigner.Designer/OutlineView/PropertyOutlineNode.cs`
- Modify: `src/WinUIDesigner.Designer/OutlineView/OutlineNode.cs` (replace `UpdateChildren` and `UpdateChildrenCollectionChanged`)
- Test: `tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs`

**Interfaces:**
- Consumes: `OutlineNode`, `OutlineNodeBase` (Task 4).
- Produces: `public class WinUIDesigner.Designer.OutlineView.PropertyOutlineNode : OutlineNode` with `public static IOutlineNode Create(DesignItemProperty property)`.

- [ ] **Step 1: Write the failing tests**

Add to `OutlineViewTests.cs`:

```csharp
        [Test]
        public void ASetNonContentPropertyBecomesAPropertyNodeWrappingItsValue()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Button><Button.RenderTransform><RotateTransform Angle=\"10\" /></Button.RenderTransform></Button>" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();

                var buttonNode = outline.Children[0];
                var transformNode = buttonNode.Children.FirstOrDefault(n => n.Name == "RenderTransform");
                Assert.IsNotNull(transformNode, "no PropertyOutlineNode was created for RenderTransform");
                Assert.AreEqual(1, transformNode.Children.Count);
                Assert.AreEqual(typeof(Microsoft.UI.Xaml.Media.RotateTransform),
                    transformNode.Children[0].DesignItem.ComponentType);
            });
        }

        [Test]
        public void AddingAndRemovingACollectionElementUpdatesTheChildNodes()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();
                Assert.AreEqual(2, outline.Children.Count);

                var added = surface.DesignContext.Services.Component
                    .RegisterComponentForDesigner(new Button());
                grid.ContentProperty.CollectionElements.Add(added);
                Assert.AreEqual(3, outline.Children.Count);
                Assert.AreEqual(added, outline.Children[2].DesignItem);

                grid.ContentProperty.CollectionElements.Remove(added);
                Assert.AreEqual(2, outline.Children.Count);
                Assert.IsTrue(outline.Children.All(n => n.DesignItem != added));
            });
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests
```

Expected: `ASetNonContentPropertyBecomesAPropertyNodeWrappingItsValue` FAILS ("no PropertyOutlineNode was created for RenderTransform").

- [ ] **Step 3: Create `PropertyOutlineNode`**

Create `src/WinUIDesigner.Designer/OutlineView/PropertyOutlineNode.cs`. Copy the 17-line SharpDevelop MIT header verbatim from the reference's `PropertyOutlineNode.cs`, then:

```csharp
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.OutlineView
{
    /// <summary>
    /// A name-only node standing for one DesignItemProperty rather than an element; its single
    /// child is the property's value. Ported from the reference's PropertyOutlineNode.cs, minus
    /// its redundant second DummyPlacementType registration (spec Decision 11).
    /// </summary>
    public class PropertyOutlineNode : OutlineNode
    {
        readonly DesignItemProperty _property;

        protected PropertyOutlineNode(DesignItemProperty property) : base(property.Name)
        {
            _property = property;
        }

        public override ServiceContainer Services {
            get { return this._property.DesignItem.Services; }
        }

        public static IOutlineNode Create(DesignItemProperty property)
        {
            return new PropertyOutlineNode(property);
        }
    }
}
```

`PropertyOutlineNode` uses the `string`-name constructor chain, so `DesignItem` stays null on it and the base's `Name` short-circuits to `_name` before ever touching `DesignItem`.

- [ ] **Step 4: Complete `UpdateChildren`**

In `src/WinUIDesigner.Designer/OutlineView/OutlineNode.cs`, replace the whole `UpdateChildren` method and the whole `UpdateChildrenCollectionChanged` method with:

```csharp
        protected override void UpdateChildren()
        {
            Children.Clear();

            foreach (var prp in DesignItem.AllSetProperties) {
                if (prp.Name != DesignItem.ContentPropertyName && prp.Value != null) {
                    var propertyNode = PropertyOutlineNode.Create(prp);
                    propertyNode.Children.Add(prp.Value.CreateOutlineNode());
                    Children.Add(propertyNode);
                }
            }

            if (DesignItem.ContentPropertyName != null) {
                var content = DesignItem.ContentProperty;
                if (content.IsCollection) {
                    UpdateChildrenCore(content.CollectionElements);
                } else if (content.Value != null) {
                    // A content value that is itself selectable becomes a plain child node;
                    // anything else (a non-visual value) gets a PropertyOutlineNode wrapper so it
                    // is still reachable in the tree.
                    if (!UpdateChildrenCore(new[] { content.Value })) {
                        var propertyNode = PropertyOutlineNode.Create(content);
                        propertyNode.Children.Add(content.Value.CreateOutlineNode());
                        Children.Add(propertyNode);
                    }
                }
            }
        }

        protected override void UpdateChildrenCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove) {
                foreach (var oldItem in e.OldItems) {
                    var item = Children.FirstOrDefault(x => x.DesignItem == oldItem);
                    if (item != null)
                        Children.Remove(item);
                }
            } else if (e.Action == NotifyCollectionChangedAction.Add) {
                UpdateChildrenCore(e.NewItems.Cast<DesignItem>(), e.NewStartingIndex);
            }
        }
```

`e.NewStartingIndex` is the index within the *content collection*, while `Children` may also hold leading property nodes. This is the reference's own behaviour, ported as-is; the mismatch only shows on an element that has both set non-content properties and a collection insert, which no test in this phase exercises. Record it in the phase's Known gaps rather than diverging from the reference here.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests
```

Expected: PASS (eight tests). `GridXaml` deliberately sets no non-content property on the `Grid`, so no leading `PropertyOutlineNode` appears and Task 4's index-based assertions still hold. If some other set property does surface one, locate children by `DesignItem` rather than by fixed index — do not weaken the new behaviour to keep the old indices.

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.Designer/OutlineView/PropertyOutlineNode.cs src/WinUIDesigner.Designer/OutlineView/OutlineNode.cs tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs
git commit -m "feat: add property outline nodes and incremental collection updates"
```

---

### Task 6: Reparenting — CanInsert and Insert

**Files:**
- Modify: `src/WinUIDesigner.DesignModel/Interfaces/IOutlineNode.cs` (add two members)
- Modify: `src/WinUIDesigner.Designer/OutlineView/OutlineNodeBase.cs` (implement them)
- Test: `tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs`

**Interfaces:**
- Consumes: `OutlineNodeBase`, `DummyPlacementType` (Task 4).
- Produces, on `IOutlineNode` and `OutlineNodeBase`:
  - `bool CanInsert(IEnumerable<IOutlineNode> nodes, IOutlineNode after, bool copy)`
  - `void Insert(IEnumerable<IOutlineNode> nodes, IOutlineNode after, bool copy)`

- [ ] **Step 1: Write the failing tests**

Add to `OutlineViewTests.cs`:

```csharp
        [Test]
        public void CanInsertReflectsWhetherTheTargetCanAcceptTheItems()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Button /><StackPanel><Image /></StackPanel>" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();

                var buttonNode = outline.Children[0];
                var stackPanelNode = outline.Children[1];
                var imageNode = stackPanelNode.Children[0];

                // A Grid takes anything.
                Assert.IsTrue(outline.CanInsert(new[] { imageNode }, buttonNode, false));
                // A ContentControl with an unset Content takes a single item
                // (DefaultPlacementBehavior.internalCanEnterContainer, src/WinUIDesigner.Designer/
                // Extensions/DefaultPlacementBehavior.cs:99-100).
                Assert.IsTrue(buttonNode.CanInsert(new[] { imageNode }, null, false));
                // An Image has no ContentProperty, so DefaultPlacementBehavior.OnInitialized
                // never attaches an IPlacementBehavior to it and CanInsert must be false.
                Assert.IsFalse(imageNode.CanInsert(new[] { buttonNode }, null, false));
                // Ported deliberately without a cycle guard (spec Decision 14): the reference
                // reports true for inserting an ancestor into its own descendant.
                Assert.IsTrue(stackPanelNode.CanInsert(new[] { outline }, outline, false));
            });
        }

        [Test]
        public void InsertMovesAnItemBetweenContainersAsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int before = undo.UndoActions.Count();

                var stackPanel = grid.ContentProperty.CollectionElements[1];
                var stackPanelButton = stackPanel.ContentProperty.CollectionElements[0];
                outline.Insert(new[] { outline.Children[1].Children[0] }, outline.Children[0], false);

                Assert.AreEqual(3, grid.ContentProperty.CollectionElements.Count);
                Assert.AreEqual(0, stackPanel.ContentProperty.CollectionElements.Count);
                Assert.AreEqual(stackPanelButton, grid.ContentProperty.CollectionElements[1]);
                Assert.AreEqual(before + 1, undo.UndoActions.Count(),
                    "the whole move must commit as a single undo entry");
            });
        }

        [Test]
        public void InsertInCopyModeLeavesTheSourceInPlace()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();

                var stackPanel = grid.ContentProperty.CollectionElements[1];
                outline.Insert(new[] { outline.Children[1].Children[0] }, outline.Children[0], true);

                Assert.AreEqual(1, stackPanel.ContentProperty.CollectionElements.Count,
                    "copy mode must not remove the source");
                Assert.AreEqual(3, grid.ContentProperty.CollectionElements.Count);
                Assert.AreEqual(typeof(Button), grid.ContentProperty.CollectionElements[1].ComponentType);
            });
        }
```

Add `using WinUIDesigner.DesignModel.Services;` to the test file's usings for `UndoService`.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build WinUIDesigner.sln
```

Expected: FAIL to compile — `IOutlineNode` has no `CanInsert`/`Insert`.

- [ ] **Step 3: Add the interface members**

In `src/WinUIDesigner.DesignModel/Interfaces/IOutlineNode.cs`, add `using System.Collections.Generic;` and, after `ObservableCollection<IOutlineNode> Children { get; }`:

```csharp
        bool CanInsert(IEnumerable<IOutlineNode> nodes, IOutlineNode after, bool copy);
        void Insert(IEnumerable<IOutlineNode> nodes, IOutlineNode after, bool copy);
```

- [ ] **Step 4: Implement them on `OutlineNodeBase`**

In `src/WinUIDesigner.Designer/OutlineView/OutlineNodeBase.cs`, add `using System.Collections.Generic;` and `using System.Linq;`, then add before the `INotifyPropertyChanged` members:

```csharp
        public bool CanInsert(IEnumerable<IOutlineNode> nodes, IOutlineNode after, bool copy)
        {
            var placementBehavior = DesignItem.GetBehavior<IPlacementBehavior>();
            if (placementBehavior == null)
                return false;
            var operation = PlacementOperation.Start(nodes.Select(node => node.DesignItem).ToArray(), DummyPlacementType);
            if (operation != null) {
                bool canEnter = placementBehavior.CanEnterContainer(operation, true);
                operation.Abort();
                return canEnter;
            }
            return false;
        }

        public virtual void Insert(IEnumerable<IOutlineNode> nodes, IOutlineNode after, bool copy)
        {
            using (var moveTransaction = DesignItem.Context.OpenGroup("Item moved in outline view",
                       nodes.Select(n => n.DesignItem).ToList())) {
                if (copy) {
                    nodes = nodes.Select(n => n.DesignItem.Clone().CreateOutlineNode()).ToList();
                } else {
                    foreach (var node in nodes) {
                        node.DesignItem.Remove();
                    }
                }

                var index = after == null ? 0 : Children.IndexOf(after) + 1;

                var content = DesignItem.ContentProperty;
                if (content.IsCollection) {
                    foreach (var node in nodes) {
                        content.CollectionElements.Insert(index++, node.DesignItem);
                    }
                } else {
                    content.SetValue(nodes.First().DesignItem);
                }
                moveTransaction.Commit();
            }
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests
```

Expected: PASS (eleven tests).

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.DesignModel/Interfaces/IOutlineNode.cs src/WinUIDesigner.Designer/OutlineView/OutlineNodeBase.cs tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs
git commit -m "feat: add outline CanInsert/Insert reparenting with single-undo-entry moves"
```

---

### Task 7: OutlineViewControl core

The control: a `TreeView` mirroring the node tree, rows built in C#, selection synced to the tree's own selection.

**Files:**
- Create: `src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs`

**Interfaces:**
- Consumes: `IOutlineNode` and everything from Tasks 2-6.
- Produces: `public class WinUIDesigner.Designer.OutlineView.OutlineViewControl : StackPanel` with `public IOutlineNode Root { get; set; }` (setting it rebuilds the tree), `public TreeView Tree { get; }`, `internal IOutlineNode NodeFor(TreeViewNode treeNode)`, and `protected void Rebuild()`. `WinUIDesigner.Designer` already carries `[assembly: InternalsVisibleTo("WinUIDesigner.Designer.Tests")]` (`src/WinUIDesigner.Designer/AssemblyInfo.cs:12`), so the tests can call the `internal` members.

- [ ] **Step 1: Write the failing tests**

Add to `OutlineViewTests.cs` (add `using WinUIDesigner.Designer.OutlineView;` to the usings):

```csharp
        [Test]
        public void TheControlMirrorsTheOutlineTreeIntoTreeViewNodes()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                Assert.AreEqual(1, control.Tree.RootNodes.Count);
                var rootTreeNode = control.Tree.RootNodes[0];
                Assert.AreEqual(2, rootTreeNode.Children.Count);
                Assert.AreEqual(1, rootTreeNode.Children[1].Children.Count);
                Assert.IsInstanceOf<FrameworkElement>(rootTreeNode.Content);
                Assert.AreSame(control.Root, control.NodeFor(rootTreeNode));
            });
        }

        [Test]
        public void SelectingATreeViewNodeSelectsTheDesignItem()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var button = grid.ContentProperty.CollectionElements[0];
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[0]);
                control.Tree.SelectedNodes.Add(control.Tree.RootNodes[0].Children[0]);

                Assert.IsTrue(surface.DesignContext.Services.Selection.SelectedItems.Contains(button));
            });
        }

        [Test]
        public void SelectingInTheDesignerSelectsTheTreeViewNode()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var button = grid.ContentProperty.CollectionElements[0];
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { button });

                Assert.IsTrue(control.Tree.SelectedNodes.Contains(control.Tree.RootNodes[0].Children[0]));
            });
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build WinUIDesigner.sln
```

Expected: FAIL to compile — `OutlineViewControl` does not exist.

- [ ] **Step 3: Create the control**

Create `src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs`. This file is newly written for this port (its reference counterpart is XAML), so it carries **no** SharpDevelop header:

```csharp
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.OutlineView
{
    /// <summary>
    /// The design-time document-tree panel. Derives from StackPanel, not Control: this codebase
    /// has zero .xaml files in which to declare a ControlTemplate, and a templateless Control
    /// renders nothing - PropertyGridControl derives from StackPanel for the same reason.
    ///
    /// Rows are built in C# and set as TreeViewNode.Content (spec Decision 4); a
    /// TreeViewItem renders a UIElement-valued Content directly, which
    /// TreeViewPlatformProbeTests pins. No shell app exists yet (Phase 5), so a host sets Root
    /// directly, the same host-less-control-testing pattern PropertyGridControl uses.
    /// </summary>
    public class OutlineViewControl : StackPanel
    {
        readonly TreeView tree = new TreeView { SelectionMode = TreeViewSelectionMode.Multiple };
        readonly Dictionary<TreeViewNode, IOutlineNode> nodeFromTreeNode = new Dictionary<TreeViewNode, IOutlineNode>();
        readonly Dictionary<IOutlineNode, TreeViewNode> treeNodeFromNode = new Dictionary<IOutlineNode, TreeViewNode>();

        // Guards the selection round trip: outline -> ISelectionService -> outline, and the same
        // loop in the other direction. Same guard-flag pattern the PropertyGrid editors use.
        bool syncingSelection;

        public OutlineViewControl()
        {
            tree.SelectionChanged += Tree_SelectionChanged;
            Children.Add(tree);
        }

        public TreeView Tree {
            get { return tree; }
        }

        IOutlineNode root;

        public IOutlineNode Root {
            get { return root; }
            set {
                root = value;
                Rebuild();
            }
        }

        internal IOutlineNode NodeFor(TreeViewNode treeNode)
        {
            IOutlineNode node;
            return nodeFromTreeNode.TryGetValue(treeNode, out node) ? node : null;
        }

        // IOutlineNode does not declare INotifyPropertyChanged, but every implementation in this
        // assembly derives from OutlineNodeBase, which does. These two keep the cast in one place.
        void Subscribe(IOutlineNode node)
        {
            var source = node as INotifyPropertyChanged;
            if (source != null)
                source.PropertyChanged += Node_PropertyChanged;
            node.Children.CollectionChanged += Node_ChildrenChanged;
        }

        void Unsubscribe(IOutlineNode node)
        {
            var source = node as INotifyPropertyChanged;
            if (source != null)
                source.PropertyChanged -= Node_PropertyChanged;
            node.Children.CollectionChanged -= Node_ChildrenChanged;
        }

        /// <summary>Discards and rebuilds the whole TreeViewNode tree from Root.</summary>
        protected void Rebuild()
        {
            foreach (var node in nodeFromTreeNode.Values) {
                Unsubscribe(node);
            }
            nodeFromTreeNode.Clear();
            treeNodeFromNode.Clear();
            tree.RootNodes.Clear();

            if (root == null)
                return;

            tree.RootNodes.Add(BuildTreeNode(root));
        }

        TreeViewNode BuildTreeNode(IOutlineNode node)
        {
            var treeNode = new TreeViewNode {
                Content = BuildRow(node),
                IsExpanded = node.IsExpanded,
            };
            nodeFromTreeNode[treeNode] = node;
            treeNodeFromNode[node] = treeNode;

            Subscribe(node);

            foreach (var child in node.Children) {
                treeNode.Children.Add(BuildTreeNode(child));
            }

            if (node.IsSelected && !tree.SelectedNodes.Contains(treeNode))
                tree.SelectedNodes.Add(treeNode);

            return treeNode;
        }

        /// <summary>The row visual for one node. Task 8 adds the hide and lock toggles into
        /// columns 0 and 1; this task fills column 2 with the name only.</summary>
        protected virtual FrameworkElement BuildRow(IOutlineNode node)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nameText = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameText, 2);
            row.Children.Add(nameText);
            return row;
        }

        void Node_ChildrenChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // A node's Children collection is rebuilt wholesale by OutlineNode.UpdateChildren, so
            // resynchronising the whole tree is both correct and the simplest thing that is.
            Rebuild();
        }

        void Node_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var node = (IOutlineNode)sender;
            TreeViewNode treeNode;
            if (!treeNodeFromNode.TryGetValue(node, out treeNode))
                return;

            if (e.PropertyName == "Name") {
                treeNode.Content = BuildRow(node);
            } else if (e.PropertyName == "IsSelected") {
                if (syncingSelection)
                    return;
                syncingSelection = true;
                try {
                    if (node.IsSelected) {
                        if (!tree.SelectedNodes.Contains(treeNode))
                            tree.SelectedNodes.Add(treeNode);
                    } else {
                        tree.SelectedNodes.Remove(treeNode);
                    }
                } finally {
                    syncingSelection = false;
                }
            }
        }

        void Tree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
        {
            if (syncingSelection)
                return;
            syncingSelection = true;
            try {
                foreach (TreeViewNode treeNode in args.AddedItems.OfType<TreeViewNode>()) {
                    var node = NodeFor(treeNode);
                    if (node != null)
                        node.IsSelected = true;
                }
                foreach (TreeViewNode treeNode in args.RemovedItems.OfType<TreeViewNode>()) {
                    var node = NodeFor(treeNode);
                    if (node != null)
                        node.IsSelected = false;
                }
            } finally {
                syncingSelection = false;
            }
        }
    }
}
```

If `TreeViewSelectionChangedEventArgs` does not expose `AddedItems`/`RemovedItems` under those names, read the real member names off the type in the same byte-scan way Task 1 did, and use them — do not fall back to rescanning `tree.SelectedNodes` wholesale, which would lose the distinction between added and removed.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests
```

Expected: PASS (fourteen tests).

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs
git commit -m "feat: add OutlineViewControl mirroring the outline tree into a WinUI TreeView"
```

---

### Task 8: Hide and lock toggles, and lock enforcement in DesignPanel

**Files:**
- Modify: `src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs` (`BuildRow`)
- Modify: `src/WinUIDesigner.Designer/DesignPanel.cs:214-232` (content hit-test pass)
- Test: `tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs`

**Interfaces:**
- Consumes: `IOutlineNode.IsDesignTimeVisible` / `IsDesignTimeLocked` (Tasks 2, 4), `OutlineViewControl.BuildRow` (Task 7).
- Produces: no new public API. `BuildRow` gains two `ToggleButton`s; `DesignPanel.HitTest` gains lock filtering.

- [ ] **Step 1: Write the failing tests**

Add to `OutlineViewTests.cs`:

```csharp
        [Test]
        public void HidingANodeSetsAndResettingItClearsTheIsHiddenProperty()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();
                var buttonNode = outline.Children[0];
                var button = (Button)buttonNode.DesignItem.Component;

                buttonNode.IsDesignTimeVisible = false;
                Assert.IsTrue(buttonNode.DesignItem.Properties
                    .GetAttachedProperty(typeof(WinUIDesigner.XamlDom.DesignTimeProperties), "IsHidden").IsSet);
                Assert.AreEqual(Visibility.Collapsed, button.Visibility);

                buttonNode.IsDesignTimeVisible = true;
                Assert.IsFalse(buttonNode.DesignItem.Properties
                    .GetAttachedProperty(typeof(WinUIDesigner.XamlDom.DesignTimeProperties), "IsHidden").IsSet);
            });
        }

        [Test]
        public void LockingANodeWritesTheIsLockedProperty()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();
                var buttonNode = outline.Children[0];

                buttonNode.IsDesignTimeLocked = true;
                Assert.IsTrue(((XamlDesignItem)buttonNode.DesignItem).IsDesignTimeLocked);
            });
        }

        [Test]
        public void TheRowCarriesAHideToggleAndALockToggle()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var row = (Grid)control.Tree.RootNodes[0].Children[0].Content;
                var toggles = row.Children
                    .OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>()
                    .ToList();
                Assert.AreEqual(2, toggles.Count, "row must carry a hide toggle and a lock toggle");

                var node = control.NodeFor(control.Tree.RootNodes[0].Children[0]);
                toggles[0].IsChecked = true;   // hide
                Assert.IsFalse(node.IsDesignTimeVisible);
                toggles[1].IsChecked = true;   // lock
                Assert.IsTrue(node.IsDesignTimeLocked);
            });
        }
```

- [ ] **Step 2: Write the lock-enforcement test**

Also add:

```csharp
        [Test]
        public void HitTestSkipsALockedItemAndItsChildren()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"200\">" +
                    "<StackPanel Width=\"200\" Height=\"200\"><Button Width=\"200\" Height=\"200\" /></StackPanel>" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                surface.UpdateLayout();
                var grid = surface.DesignContext.RootItem;
                var stackPanel = grid.ContentProperty.CollectionElements[0];
                var button = stackPanel.ContentProperty.CollectionElements[0];

                var point = new Windows.Foundation.Point(100, 100);
                var before = surface.DesignPanel.HitTest(point, false, true, HitTestType.ElementSelection);
                Assert.AreEqual(button, before.ModelHit, "baseline: the Button is what is under the point");

                ((XamlDesignItem)stackPanel).IsDesignTimeLocked = true;
                var after = surface.DesignPanel.HitTest(point, false, true, HitTestType.ElementSelection);
                Assert.AreNotEqual(button, after.ModelHit,
                    "a child of a locked container must not be hit");
                Assert.AreNotEqual(stackPanel, after.ModelHit, "the locked container itself must not be hit");
            });
        }
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests
```

Expected: `TheRowCarriesAHideToggleAndALockToggle` FAILS (0 toggles) and `HitTestSkipsALockedItemAndItsChildren` FAILS (still hits the Button).

- [ ] **Step 4: Add the two toggles to `BuildRow`**

In `src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs`, replace the body of `BuildRow` with:

```csharp
        protected virtual FrameworkElement BuildRow(IOutlineNode node)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // The reference uses Eye.png/EyeClose.png through an IconItem control; this port ships
            // no image resources, so Segoe Fluent Icons glyphs stand in (spec Decision 3).
            // E7B3 = View, E72E = Lock.
            var hideToggle = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton {
                Content = new FontIcon { Glyph = "\uE7B3" },
                IsChecked = !node.IsDesignTimeVisible,
            };
            hideToggle.Checked += (s, e) => node.IsDesignTimeVisible = false;
            hideToggle.Unchecked += (s, e) => node.IsDesignTimeVisible = true;
            Grid.SetColumn(hideToggle, 0);
            row.Children.Add(hideToggle);

            var lockToggle = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton {
                Content = new FontIcon { Glyph = "\uE72E" },
                IsChecked = node.IsDesignTimeLocked,
            };
            lockToggle.Checked += (s, e) => node.IsDesignTimeLocked = true;
            lockToggle.Unchecked += (s, e) => node.IsDesignTimeLocked = false;
            Grid.SetColumn(lockToggle, 1);
            row.Children.Add(lockToggle);

            var nameText = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameText, 2);
            row.Children.Add(nameText);
            return row;
        }
```

A `PropertyOutlineNode` has a null `DesignItem`, so its toggles would throw. Guard by adding, right after the three `ColumnDefinition`s:

```csharp
            if (node.DesignItem == null) {
                var propertyName = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(propertyName, 2);
                row.Children.Add(propertyName);
                return row;
            }
```

- [ ] **Step 5: Enforce the lock in `DesignPanel`**

In `src/WinUIDesigner.Designer/DesignPanel.cs`, inside the content pass's `foreach` (lines 214-232), after `customResult.ModelHit = modelHit ?? (Context != null ? Context.RootItem : null);` and before `continueHitTest = callback(customResult);`, insert:

```csharp
                    // Design-time lock (Phase 4g): a locked item and everything inside it are
                    // unhittable, reproducing the reference's ContinueSkipSelfAndChildren filter
                    // (WpfDesign.Designer/Project/DesignPanel.cs:103). Testing only the resolved
                    // ModelHit would leave a locked container's children selectable, so walk the
                    // whole ancestor chain. DesignItem's base class carries no lock member - the
                    // cast is the mechanism, exactly as in the reference's own OutlineNodeBase.
                    bool lockedInChain = false;
                    for (var ancestor = customResult.ModelHit; ancestor != null; ancestor = ancestor.Parent) {
                        var xamlAncestor = ancestor as WinUIDesigner.DesignModel.Xaml.XamlDesignItem;
                        if (xamlAncestor != null && xamlAncestor.IsDesignTimeLocked) {
                            lockedInChain = true;
                            break;
                        }
                    }
                    if (lockedInChain)
                        continue;
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests
```

Expected: PASS (eighteen tests).

- [ ] **Step 7: Run the full suite**

```bash
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: all green. `DesignPanel.HitTest` is exercised by many existing Designer tests — a regression there means the lock filter is too aggressive, not that the old tests are wrong.

- [ ] **Step 8: Commit**

```bash
git add src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs src/WinUIDesigner.Designer/DesignPanel.cs tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs
git commit -m "feat: add outline hide/lock toggles and enforce the design-time lock in hit testing"
```

---

### Task 9: Name filter

**Files:**
- Modify: `src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs`

**Interfaces:**
- Consumes: `OutlineViewControl.Rebuild`, `BuildTreeNode` (Task 7).
- Produces: `public string Filter { get; set; }` on `OutlineViewControl` (setting it rebuilds the tree filtered), and `public AutoSuggestBox FilterBox { get; }`.

- [ ] **Step 1: Write the failing test**

Add to `OutlineViewTests.cs`:

```csharp
        [Test]
        public void FilterKeepsMatchingNodesAndTheirAncestorsAndAnEmptyFilterRestoresTheTree()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Button /><StackPanel><Image /></StackPanel>" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();
                Assert.AreEqual(2, control.Tree.RootNodes[0].Children.Count);

                control.Filter = "image";
                control.UpdateLayout();
                var rootTreeNode = control.Tree.RootNodes[0];
                Assert.AreEqual(1, rootTreeNode.Children.Count, "only the StackPanel branch survives");
                Assert.AreEqual("StackPanel", control.NodeFor(rootTreeNode.Children[0]).Name);
                Assert.AreEqual(1, rootTreeNode.Children[0].Children.Count);
                Assert.AreEqual("Image", control.NodeFor(rootTreeNode.Children[0].Children[0]).Name);

                control.Filter = "";
                control.UpdateLayout();
                Assert.AreEqual(2, control.Tree.RootNodes[0].Children.Count);
            });
        }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet build WinUIDesigner.sln
```

Expected: FAIL to compile — `OutlineViewControl` has no `Filter`.

- [ ] **Step 3: Add the filter**

In `src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs`:

Add a field and put the box above the tree in the constructor (replace `Children.Add(tree);` with the two lines below):

```csharp
        readonly AutoSuggestBox filterBox = new AutoSuggestBox { PlaceholderText = "Filter" };
```

```csharp
            filterBox.TextChanged += (s, e) => Filter = filterBox.Text;
            Children.Add(filterBox);
            Children.Add(tree);
```

Add the property:

```csharp
        public AutoSuggestBox FilterBox {
            get { return filterBox; }
        }

        string filter;

        /// <summary>Case-insensitive name filter. A node survives if its own name matches or any
        /// descendant's does. WinUI's TreeView has no per-node visibility hook (the reference
        /// uses WPF's container generator for this), so filtering rebuilds the node tree - which
        /// discards TreeViewNode expand state for filtered-out branches (spec Known gaps).</summary>
        public string Filter {
            get { return filter; }
            set {
                filter = value;
                Rebuild();
            }
        }

        bool Matches(IOutlineNode node)
        {
            if (string.IsNullOrEmpty(filter))
                return true;
            if (node.Name != null && node.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return node.Children.Any(Matches);
        }
```

In `BuildTreeNode`, replace the child loop with a filtered one:

```csharp
            foreach (var child in node.Children) {
                if (Matches(child))
                    treeNode.Children.Add(BuildTreeNode(child));
            }
```

`Rebuild` already clears both dictionaries and `tree.RootNodes`, so no other change is needed. A root that does not itself match is still shown — the filter narrows branches, it never empties the tree of its root.

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests
```

Expected: PASS (nineteen tests).

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs
git commit -m "feat: add name filtering to the outline view"
```

---

### Task 10: Drag wiring

Read Task 1's commit message first — it names branch **A** or **B**. Implement that branch only.

**Files:**
- Modify: `src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs`

**Interfaces:**
- Consumes: `IOutlineNode.CanInsert`/`Insert` (Task 6), `OutlineViewControl.NodeFor`/`TreeNodeFor`/`Rebuild` (Task 7).
- Produces: `internal bool TryApplyDrop(TreeViewNode targetTreeNode, IList<TreeViewNode> draggedTreeNodes, TreeViewNode afterTreeNode, bool copy)` on `OutlineViewControl` — the seam the tests drive and both branches funnel through.

- [ ] **Step 1: Write the failing test**

Add to `OutlineViewTests.cs`:

```csharp
        [Test]
        public void DroppingANodeOntoAContainerReparentsTheDesignItem()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var stackPanel = grid.ContentProperty.CollectionElements[1];
                var stackPanelButton = stackPanel.ContentProperty.CollectionElements[0];

                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var rootTreeNode = control.Tree.RootNodes[0];
                var stackPanelTreeNode = rootTreeNode.Children[1];
                var draggedTreeNode = stackPanelTreeNode.Children[0];

                bool applied = control.TryApplyDrop(rootTreeNode,
                    new List<TreeViewNode> { draggedTreeNode }, rootTreeNode.Children[0], false);

                Assert.IsTrue(applied);
                Assert.AreEqual(3, grid.ContentProperty.CollectionElements.Count);
                Assert.AreEqual(stackPanelButton, grid.ContentProperty.CollectionElements[1]);
                Assert.AreEqual(0, stackPanel.ContentProperty.CollectionElements.Count);
            });
        }

        [Test]
        public void DroppingOntoATargetThatCannotAcceptTheItemDoesNothing()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Image /><Button />" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var rootTreeNode = control.Tree.RootNodes[0];
                var imageTreeNode = rootTreeNode.Children[0];
                var buttonTreeNode = rootTreeNode.Children[1];

                bool applied = control.TryApplyDrop(imageTreeNode,
                    new List<TreeViewNode> { buttonTreeNode }, null, false);

                Assert.IsFalse(applied, "an Image has no IPlacementBehavior and must reject the drop");
                Assert.AreEqual(2, grid.ContentProperty.CollectionElements.Count);
            });
        }
```

Add `using System.Collections.Generic;` and `using Microsoft.UI.Xaml.Controls;` to the test file if not already present.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build WinUIDesigner.sln
```

Expected: FAIL to compile — `OutlineViewControl` has no `TryApplyDrop`.

- [ ] **Step 3: Add the shared drop seam**

In `src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs`, add:

```csharp
        /// <summary>Applies a drop to the model. Both drag branches funnel through here, and the
        /// tests drive it directly rather than synthesising a pointer gesture - the same approach
        /// DragListenerTests/MoveTests already take for drag-driven behaviour in this suite.
        /// Returns false, changing nothing, when the target cannot accept the items.</summary>
        internal bool TryApplyDrop(TreeViewNode targetTreeNode, IList<TreeViewNode> draggedTreeNodes,
            TreeViewNode afterTreeNode, bool copy)
        {
            var target = NodeFor(targetTreeNode);
            if (target == null || target.DesignItem == null)
                return false;

            var dragged = draggedTreeNodes.Select(NodeFor).Where(n => n != null && n.DesignItem != null).ToList();
            if (dragged.Count == 0)
                return false;

            var after = afterTreeNode == null ? null : NodeFor(afterTreeNode);

            if (!target.CanInsert(dragged, after, copy))
                return false;

            suppressRebuild = true;
            try {
                target.Insert(dragged, after, copy);
            } finally {
                suppressRebuild = false;
            }
            Rebuild();
            return true;
        }
```

Add the guard field beside `syncingSelection`:

```csharp
        // Insert fires a burst of Children.CollectionChanged events (one per remove, one per
        // insert). Rebuilding on each would discard and recreate the very TreeViewNodes the drop
        // is still walking, so coalesce them into the single Rebuild that TryApplyDrop does at
        // the end. Same guard-flag pattern the PropertyGrid editors use (commit b5eb9c1).
        bool suppressRebuild;
```

and make `Node_ChildrenChanged` respect it:

```csharp
        void Node_ChildrenChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (suppressRebuild)
                return;
            Rebuild();
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.OutlineViewTests
```

Expected: PASS (twenty-one tests).

- [ ] **Step 5: Wire the platform drag — branch A (only if Task 1 recorded A)**

In the `OutlineViewControl` constructor, after `tree.SelectionChanged += Tree_SelectionChanged;`:

```csharp
            tree.CanDragItems = true;
            tree.CanReorderItems = true;
            tree.AllowDrop = true;
            tree.DragItemsStarting += Tree_DragItemsStarting;
            tree.DragItemsCompleted += Tree_DragItemsCompleted;
```

and add:

```csharp
        List<TreeViewNode> draggingTreeNodes = new List<TreeViewNode>();
        readonly Dictionary<TreeViewNode, TreeViewNode> parentBeforeDrag = new Dictionary<TreeViewNode, TreeViewNode>();

        void Tree_DragItemsStarting(TreeView sender, TreeViewDragItemsStartingEventArgs args)
        {
            draggingTreeNodes = args.Items.OfType<TreeViewNode>().ToList();
            parentBeforeDrag.Clear();
            foreach (var treeNode in draggingTreeNodes) {
                parentBeforeDrag[treeNode] = treeNode.Parent;
            }
        }

        void Tree_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
        {
            if (draggingTreeNodes.Count == 0)
                return;

            var moved = draggingTreeNodes[0];
            var newParent = moved.Parent;
            TreeViewNode oldParent;
            parentBeforeDrag.TryGetValue(moved, out oldParent);
            draggingTreeNodes = new List<TreeViewNode>();

            if (newParent == null)
                return;

            int index = newParent.Children.IndexOf(moved);
            var after = index > 0 ? newParent.Children[index - 1] : null;

            // WinUI has already performed the visual move. Put the TreeViewNode tree back the way
            // it was, then let TryApplyDrop change the MODEL and rebuild from it - otherwise the
            // visual move and the model move would both apply and double-count.
            newParent.Children.Remove(moved);
            if (oldParent != null)
                oldParent.Children.Add(moved);

            if (!TryApplyDrop(newParent, new List<TreeViewNode> { moved }, after, false))
                Rebuild();
        }
```

- [ ] **Step 6: Wire the platform drag — branch B (only if Task 1 recorded B)**

Instead of Step 5, in the constructor:

```csharp
            tree.CanDragItems = true;
            tree.CanReorderItems = false;
            tree.AllowDrop = true;
            tree.DragItemsStarting += Tree_DragItemsStarting;
            tree.DragOver += Tree_DragOver;
            tree.Drop += Tree_Drop;
```

and add:

```csharp
        List<TreeViewNode> draggingTreeNodes = new List<TreeViewNode>();

        void Tree_DragItemsStarting(TreeView sender, TreeViewDragItemsStartingEventArgs args)
        {
            draggingTreeNodes = args.Items.OfType<TreeViewNode>().ToList();
        }

        TreeViewNode TargetUnder(DragEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null) {
                var item = source as TreeViewItem;
                if (item != null)
                    return tree.NodeFromContainer(item);
                source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        void Tree_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;

            var targetTreeNode = TargetUnder(e);
            if (targetTreeNode == null || draggingTreeNodes.Count == 0)
                return;

            var target = NodeFor(targetTreeNode);
            var dragged = draggingTreeNodes.Select(NodeFor).Where(n => n != null && n.DesignItem != null).ToList();
            if (target == null || target.DesignItem == null || dragged.Count == 0)
                return;

            var after = targetTreeNode.Children.Count > 0
                ? targetTreeNode.Children[targetTreeNode.Children.Count - 1]
                : null;
            if (target.CanInsert(dragged, after == null ? null : NodeFor(after), false))
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        }

        void Tree_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            var targetTreeNode = TargetUnder(e);
            if (targetTreeNode == null || draggingTreeNodes.Count == 0)
                return;

            var after = targetTreeNode.Children.Count > 0
                ? targetTreeNode.Children[targetTreeNode.Children.Count - 1]
                : null;
            var dragged = draggingTreeNodes;
            draggingTreeNodes = new List<TreeViewNode>();
            TryApplyDrop(targetTreeNode, dragged, after, false);
        }
```

- [ ] **Step 7: Run the full suite**

```bash
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: all green, with the XamlDom suite's 4 pre-existing unrelated ignores as the known baseline.

- [ ] **Step 8: Commit**

```bash
git add src/WinUIDesigner.Designer/OutlineView/OutlineViewControl.cs tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs
git commit -m "feat: wire outline drag-and-drop reparenting through CanInsert/Insert"
```

---

## Coverage map

Every item in the spec's Testing/DoD list, and the task that delivers it:

| Spec coverage item | Task | Test |
|---|---|---|
| Hierarchy (2 root children, StackPanel has 1, DesignItem identity) | 4 | `TheOutlineMirrorsTheDesignItemHierarchy` |
| Property nodes | 5 | `ASetNonContentPropertyBecomesAPropertyNodeWrappingItsValue` |
| Name service formats | 3 | `OutlineNodeNameIsTheTypeNameAloneOrWithTheXName` |
| Selection, canvas→outline | 4 | `SelectingInTheDesignerSelectsTheMatchingOutlineNodes` |
| Selection, outline→canvas | 4 | `SelectingAnOutlineNodeSelectsItInTheDesigner` |
| Hide sets/resets IsHidden, Visibility follows | 8 | `HidingANodeSetsAndResettingItClearsTheIsHiddenProperty` |
| Lock writes IsLocked; hit-test skips it and its children | 8 | `LockingANodeWritesTheIsLockedProperty`, `HitTestSkipsALockedItemAndItsChildren` |
| CanInsert: Grid true, no-behavior false, cycle true | 6 | `CanInsertReflectsWhetherTheTargetCanAcceptTheItems` |
| Insert cut mode + single undo entry | 6 | `InsertMovesAnItemBetweenContainersAsOneUndoEntry` |
| Insert copy mode | 6 | `InsertInCopyModeLeavesTheSourceInPlace` |
| Collection change adds/removes a child node | 5 | `AddingAndRemovingACollectionElementUpdatesTheChildNodes` |
| Filter narrows and restores | 9 | `FilterKeepsMatchingNodesAndTheirAncestorsAndAnEmptyFilterRestoresTheTree` |
| Drag: model-level drop outcome | 10 | `DroppingANodeOntoAContainerReparentsTheDesignItem`, `DroppingOntoATargetThatCannotAcceptTheItemDoesNothing` |
| Decision 4 assumption (UIElement Content) | 1 | `TreeViewItemRendersAUIElementValuedContent` |
| Decision 5 branch selection | 1 | `MovingATreeViewNodeReportsItsNewParentAndIndex` |
| `CanSelectComponent` | 2 | `CanSelectComponentIsTrueForAVisualItemAndFalseForANonVisualOne` |
| Control mirrors the tree; row is a FrameworkElement | 7 | `TheControlMirrorsTheOutlineTreeIntoTreeViewNodes` |
| Control selection sync both ways | 7 | `SelectingATreeViewNodeSelectsTheDesignItem`, `SelectingInTheDesignerSelectsTheTreeViewNode` |
| Row carries hide + lock toggles | 8 | `TheRowCarriesAHideToggleAndALockToggle` |

## Known gaps to record at the end of the phase

Carry these into the spec's Known gaps section (or a phase-completion commit) if implementation confirms them:

- `UpdateChildrenCollectionChanged` passes `e.NewStartingIndex` — an index into the content collection — straight into `Children`, which may also hold leading property nodes. Ported from the reference as-is; only observable on an element that has both set non-content properties and a collection insert.
- The filter rebuild discards `TreeViewNode` expand state.
- `OutlineNodeBase` never unsubscribes from `NameChanged`/`PropertyChanged`/`CollectionChanged`/`SelectionChanged`.
- Whichever drag branch Task 1 did **not** select, and why.
