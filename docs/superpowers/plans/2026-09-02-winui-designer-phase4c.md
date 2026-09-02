# Phase 4c: Context Menus Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the designer 6 real, right-click context menus — z-order
(bring to front / send to back / forward / backward), delete/undo/redo,
align/arrange, and wrap-in-container/unwrap — driven by a new
`DesignPanel` context-flyout merge mechanism and five new
`ModelTools`/`PlacementOperation` members, so a selected item's context
menu actually mutates the design tree through the undo-tracked property
system.

**Architecture:** A `DesignPanel.AddContextFlyoutItems`/
`RemoveContextFlyoutItems` mechanism merges each attached extension's
`MenuFlyoutItem`s into one composite `MenuFlyout`, grouped by an integer
`Order` with separators between groups — the WinUI-native replacement for
the reference's per-extension `ContextMenu` + `DesignPanel.
AddContextMenu`/`UpdateContextMenu`. Six new `DefaultExtension`-derived
extensions (not `AdornerProvider` — they have no adorner visuals) each
contribute one `Order` group, gated by the selection-count-sensitive
`ExtensionServer`s already established in Phase 3/4a plus one new one.
Every menu item's `Click` handler calls straight into `ModelTools`/
`ICopyPasteService`/`UndoService` — no command-pattern indirection layer.

**Tech Stack:** .NET 10 SDK (`10.0.302`), WinUI 3 / Windows App SDK 1.7
(`Microsoft.WindowsAppSDK` `1.7.250606001` — same pin as every prior
phase), NUnit 3 + NUnitLite (the existing authoritative `.exe` test
runner).

**Spec:** `docs/superpowers/specs/2026-09-02-winui-designer-phase4c-design.md`

## Global Constraints

- No new projects. All new files land in the existing
  `WinUIDesigner.DesignModel` and `WinUIDesigner.Designer` projects (and
  their `.Tests` counterparts) — `WinUIDesigner.sln` does not change this
  phase.
- New code lives under `D:\Project\ST4I\Research\WinUIDesigner`. The
  reference repo `F:\ReferencesCode\CSharp\WpfDesigner` is read-only
  source material — never edit it.
- `Nullable` stays `disable`, matching every prior phase.
- **No `RoutedCommand`/`ApplicationCommands`/`CommandBinding`/
  `MenuItem.Command` equivalent exists in WinUI at all** (compiler/SDK-
  doc-confirmed, spec's "WinUI API surface"). Every menu item is a direct
  `MenuFlyoutItem.Click` handler calling `ModelTools`/`ICopyPasteService`/
  `UndoService` — do not build a command-pattern indirection layer to
  replace it; nothing in this phase's scope needs one. The reference's
  `DesignSurface` `RoutedCommand` hub (`AddCommandHandler(
  ApplicationCommands.*, ...)`) is explicitly **not** ported this phase —
  it exists for a toolbar/keyboard shortcuts this port doesn't have yet
  (Phase 5).
- **`Canvas.RightProperty`/`Canvas.BottomProperty` don't exist in WinUI**
  (established gap, CLAUDE.md). Every Canvas-anchored placement this
  phase writes is Left/Top-anchored only. This does **not** apply to
  Grid's `Margin.Right`/`Thickness.Right`, which exist and have no gap.
- **`DesignItemProperty`/`DesignItemPropertyCollection` are string-keyed
  only** (`Properties.GetProperty("Width")`,
  `Properties.GetAttachedProperty(typeof(Canvas), "Left")`) — there is no
  `DependencyProperty`-indexed overload anywhere in this codebase. Every
  property access in every task below uses the string form.
- **`ExtendedItem.Services.DesignPanel` is `GetRequiredService`-backed
  and throws when no `IDesignPanel` is registered** (established fact,
  Phase 4b). Any code that needs the `DesignPanel` (not just
  `IDesignPanel`) must guard with `Services.GetService<IDesignPanel>()
  != null` first, then cast, exactly like
  `src/WinUIDesigner.Designer/Extensions/RasterPlacementBehavior.cs:40,67`
  already does — never assume `Services.DesignPanel` is safe to read
  unconditionally in a bare-fixture test.
- **A `MenuFlyoutItem` instance can be safely reused across a discarded-
  and-rebuilt `MenuFlyout`** — empirically confirmed during this plan's
  writing (spec's "WinUI API surface" section), not merely assumed.
  `UpdateContextFlyout` (Task 1) rebuilds the whole composite
  `MenuFlyout.Items` from scratch on every attach/detach, reusing the
  same per-extension `MenuFlyoutItem` instances every time — this is
  known-safe, not a research risk.
- **`IComponentService.ComponentRemoved` is not fired automatically by
  removing an item from `CollectionElements`.** This port's concrete
  `XamlComponentService.RaiseComponentRemoved(DesignItem)`
  (`src/WinUIDesigner.DesignModel/Xaml/XamlComponentService.cs:235-238`,
  `internal`, reachable from `WinUIDesigner.Designer` via the existing
  `InternalsVisibleTo`) must be called explicitly, exactly like the
  reference's `DeleteComponents` does — checked directly against this
  port's code during plan-writing, not guessed.
- **Authoritative test command**, same reasoning as every prior phase
  (WinUI objects are thread-affine; `dotnet test`'s `testhost.exe` cannot
  activate them): run the built `.exe` directly —
  `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe`
  and
  `tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe`
  — not `dotnet test`.
- Phase 1-4b's suites must stay green throughout this plan. Re-run all
  `.exe`s as part of every task's verification step; treat any
  regression as a bug in that task, not something to patch around later.
- Commit after each task passes its verification step.

---

## Background: why this is nine tasks

Dependency order, backend before UI, small-and-isolated before
larger-and-composed:

1. **Task 1** — `DesignPanel`'s context-flyout merge mechanism. Zero
   dependency on anything else this phase adds; buildable and testable
   first, and every later extension task depends on it.
2. **Task 2** — `PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer`.
   Small, self-contained, needed by Tasks 8-9's `ArrangeItems`/
   `WrapItems` menus.
3. **Task 3** — `PlacementOperation.DeleteItemsAndCommit` and
   `TryStartInsertNewComponents`, re-added to `WinUIDesigner.DesignModel`.
   Both were explicitly dropped in Phase 3 for lack of a caller; this
   phase is their first real caller (Delete and Wrap-with-insert,
   respectively).
4. **Task 4** — `ModelTools.CanDeleteComponents`/`DeleteComponents` and
   the new concrete `CopyPasteService`. Depends on Task 3's
   `DeleteItemsAndCommit`.
5. **Task 5** — `ModelTools.ArrangeItems`. Large (6 directions × 2
   container types), self-contained, no dependency on Tasks 3-4.
6. **Task 6** — `ModelTools.WrapItemsNewContainer`/
   `UnwrapItemsFromContainer`. Depends on Task 3's
   `TryStartInsertNewComponents`.
7. **Task 7** — `RightClickContextMenuExtension` +
   `DefaultCommandsContextMenuExtension` (the two menus gated by
   already-existing `ExtensionServer`s, Order 20/10 — "selection
   commands"). Depends on Tasks 1, 3, 4.
8. **Task 8** — `ArrangeItemsContextMenuExtension` +
   `WrapItemContextMenuExtension` + `WrapItemsContextMenuExtension` +
   `UnwrapItemContextMenuExtension` (the four `ModelTools`-backed
   multi-item-operation menus). Depends on Tasks 1, 2, 5, 6.
9. **Task 9** — the Definition-of-Done suite from the spec's "Testing /
   Definition of Done" section.

---

### Task 1: `DesignPanel` context-flyout merge mechanism

**Files:**
- Modify: `src/WinUIDesigner.Designer/DesignPanel.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/DesignPanelContextMenuTests.cs`

**Interfaces:**
- Consumes: `WinUIDesigner.TestHost.WinUITestHost.Run(Action)` (Phase
  2/3, unchanged).
- Produces: `WinUIDesigner.Designer.DesignPanel.AddContextFlyoutItems(
  IReadOnlyList<Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase> items, int
  order)` and `.RemoveContextFlyoutItems(IReadOnlyList<MenuFlyoutItemBase>
  items)`. Every later task's context-menu extensions call these two
  methods from their own `OnInitialized`/`OnRemove`, passing the exact
  same list instance to both calls (removal is keyed by reference
  identity).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class DesignPanelContextMenuTests
    {
        [Test]
        public void MergesTwoGroupsInAscendingOrderWithOneSeparator()
        {
            WinUITestHost.Run(() => {
                var panel = new DesignPanel();
                var groupA = new List<MenuFlyoutItemBase> { new MenuFlyoutItem { Text = "A1" } };
                var groupB = new List<MenuFlyoutItemBase> {
                    new MenuFlyoutItem { Text = "B1" },
                    new MenuFlyoutItem { Text = "B2" }
                };

                // Added out of Order to prove the merge sorts, not just concatenates.
                panel.AddContextFlyoutItems(groupB, 20);
                panel.AddContextFlyoutItems(groupA, 10);

                var flyout = (MenuFlyout)panel.ContextFlyout;
                Assert.AreEqual(4, flyout.Items.Count);
                Assert.AreSame(groupA[0], flyout.Items[0]);
                Assert.IsInstanceOf<MenuFlyoutSeparator>(flyout.Items[1]);
                Assert.AreSame(groupB[0], flyout.Items[2]);
                Assert.AreSame(groupB[1], flyout.Items[3]);
            });
        }

        [Test]
        public void RemovingAGroupRebuildsWithoutItAndWithoutASeparator()
        {
            WinUITestHost.Run(() => {
                var panel = new DesignPanel();
                var groupA = new List<MenuFlyoutItemBase> { new MenuFlyoutItem { Text = "A1" } };
                var groupB = new List<MenuFlyoutItemBase> { new MenuFlyoutItem { Text = "B1" } };
                panel.AddContextFlyoutItems(groupA, 10);
                panel.AddContextFlyoutItems(groupB, 20);

                panel.RemoveContextFlyoutItems(groupA);

                var flyout = (MenuFlyout)panel.ContextFlyout;
                Assert.AreEqual(1, flyout.Items.Count);
                Assert.AreSame(groupB[0], flyout.Items[0]);
            });
        }

        [Test]
        public void RemovingTheLastGroupClearsContextFlyout()
        {
            WinUITestHost.Run(() => {
                var panel = new DesignPanel();
                var groupA = new List<MenuFlyoutItemBase> { new MenuFlyoutItem { Text = "A1" } };
                panel.AddContextFlyoutItems(groupA, 10);

                panel.RemoveContextFlyoutItems(groupA);

                Assert.IsNull(panel.ContextFlyout);
            });
        }

        [Test]
        public void SameGroupInstanceCanBeReAddedAfterRemoval()
        {
            // Exercises the same "reuse a MenuFlyoutItem across a rebuilt MenuFlyout"
            // pattern confirmed safe during spec-writing, through the real production path.
            WinUITestHost.Run(() => {
                var panel = new DesignPanel();
                var group = new List<MenuFlyoutItemBase> { new MenuFlyoutItem { Text = "A1" } };
                panel.AddContextFlyoutItems(group, 10);
                panel.RemoveContextFlyoutItems(group);

                panel.AddContextFlyoutItems(group, 10);

                var flyout = (MenuFlyout)panel.ContextFlyout;
                Assert.AreEqual(1, flyout.Items.Count);
                Assert.AreSame(group[0], flyout.Items[0]);
            });
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.DesignPanelContextMenuTests --noresult`
Expected: build FAILS with `CS1061` ("DesignPanel does not contain a
definition for 'AddContextFlyoutItems'") — `ContextFlyout` itself is a
built-in `UIElement` property (compiles fine), only the two new methods
are missing.

- [ ] **Step 3: Implement `AddContextFlyoutItems`/`RemoveContextFlyoutItems`**

In `src/WinUIDesigner.Designer/DesignPanel.cs`, add `using
System.Linq;` to the existing `using` block, then add this member group
(placed after the existing `RasterWidth` property, before
`MeasureOverride`):

```csharp
readonly Dictionary<IReadOnlyList<MenuFlyoutItemBase>, int> _contextFlyoutGroups =
    new Dictionary<IReadOnlyList<MenuFlyoutItemBase>, int>();

/// <summary>Contributes one Order-grouped set of context-menu items, merged with every
/// other attached extension's group into a single composite MenuFlyout set as
/// this.ContextFlyout - the WinUI-native replacement for the reference's per-extension
/// ContextMenu + DesignPanel.AddContextMenu/UpdateContextMenu (WpfDesign.Designer/Project/
/// DesignPanel.cs:555-613). Call with the exact same list instance from RemoveContextFlyoutItems
/// later - removal is keyed by reference identity. Reusing the same MenuFlyoutItem instances
/// across the rebuild this triggers is confirmed safe (spec's "WinUI API surface").</summary>
public void AddContextFlyoutItems(IReadOnlyList<MenuFlyoutItemBase> items, int order)
{
    _contextFlyoutGroups[items] = order;
    UpdateContextFlyout();
}

/// <summary>Removes a group previously added via AddContextFlyoutItems (same list instance).</summary>
public void RemoveContextFlyoutItems(IReadOnlyList<MenuFlyoutItemBase> items)
{
    _contextFlyoutGroups.Remove(items);
    UpdateContextFlyout();
}

void UpdateContextFlyout()
{
    if (_contextFlyoutGroups.Count == 0) {
        this.ContextFlyout = null;
        return;
    }
    var flyout = new MenuFlyout();
    foreach (var group in _contextFlyoutGroups.OrderBy(g => g.Value)) {
        if (flyout.Items.Count > 0)
            flyout.Items.Add(new MenuFlyoutSeparator());
        foreach (var item in group.Key)
            flyout.Items.Add(item);
    }
    this.ContextFlyout = flyout;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.DesignPanelContextMenuTests --noresult`
Expected: PASS, 4 tests.

- [ ] **Step 5: Re-run full suites, then commit**

Run all three `.exe`s (`WinUIDesigner.XamlDom.Tests`,
`WinUIDesigner.DesignModel.Tests`, `WinUIDesigner.Designer.Tests`) with
`--noresult`, confirm no regression.

```bash
git add src/WinUIDesigner.Designer/DesignPanel.cs tests/WinUIDesigner.Designer.Tests/DesignPanelContextMenuTests.cs
git commit -m "feat: add DesignPanel context-flyout merge mechanism"
```

---

### Task 2: `PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer`

**Files:**
- Modify: `src/WinUIDesigner.DesignModel/Extensions/SelectionExtensionServers.cs`
- Modify: `tests/WinUIDesigner.DesignModel.Tests/SelectionExtensionServersTests.cs`

**Interfaces:**
- Consumes: `PrimarySelectionExtensionServer` (existing, same file),
  `ExtensionManager.GetExtensionServer(ExtensionServerAttribute)`
  (existing).
- Produces: `WinUIDesigner.DesignModel.Extensions.
  PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer` — a public
  class with no constructor arguments, consumed by Task 8's
  `ArrangeItemsContextMenuExtension`/`WrapItemsContextMenuExtension` via
  `[ExtensionServer(typeof(PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer))]`.

- [ ] **Step 1: Write the failing test**

Add this test method to the existing `SelectionExtensionServersTests`
class in `tests/WinUIDesigner.DesignModel.Tests/SelectionExtensionServersTests.cs`
(reuses the file's existing `FakeDesignPanel`/`FakeSelectionService`
fixtures, same pattern as
`PrimarySelectionParentExtensionServerAppliesOnlyToTheSelectedItemsParent`
above it):

```csharp
        [Test]
        public void PrimarySelectionButOnlyWhenMultipleSelectedAppliesOnlyWithTwoOrMoreSelected()
        {
            WinUITestHost.Run(() => {
                const string xaml = "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Button x:Name=\"A\" /><Button x:Name=\"B\" /></Grid>";

                var loadSettings = new XamlLoadSettings();
                var fakePanel = new FakeDesignPanel();
                var fakeSelection = new FakeSelectionService();
                loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), fakePanel));
                loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(ISelectionService), fakeSelection));

                using (var reader = XmlReader.Create(new StringReader(xaml))) {
                    var context = new XamlDesignContext(reader, loadSettings);
                    fakePanel.Context = context;

                    var root = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                    var a = (Microsoft.UI.Xaml.Controls.Button)root.Children[0];
                    var b = (Microsoft.UI.Xaml.Controls.Button)root.Children[1];
                    var services = context.Services;
                    var aItem = services.Component.GetDesignItem(a);
                    var bItem = services.Component.GetDesignItem(b);

                    var server = services.ExtensionManager.GetExtensionServer(
                        new ExtensionServerAttribute(typeof(PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer)));

                    fakeSelection.SetSelectedComponents(new[] { aItem }, SelectionTypes.Primary);
                    Assert.IsFalse(server.ShouldApplyExtensions(aItem), "only one item selected - should not apply");

                    fakeSelection.SetSelectedComponents(new[] { aItem, bItem }, SelectionTypes.Replace);
                    Assert.IsTrue(server.ShouldApplyExtensions(aItem), "primary selection with 2 selected - should apply");
                    Assert.IsFalse(server.ShouldApplyExtensions(bItem), "not the primary selection - should not apply");
                }
            });
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --test=WinUIDesigner.DesignModel.Tests.SelectionExtensionServersTests --noresult`
Expected: build FAILS with `CS0246`
("PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer" not found).

- [ ] **Step 3: Implement the extension server**

Add this class to the end of `src/WinUIDesigner.DesignModel/Extensions/SelectionExtensionServers.cs`,
inside the existing namespace block, after
`PrimarySelectionParentExtensionServer`:

```csharp
	/// <summary>Applies an extension to the primary selection only when 2+ items are
	/// selected - used by ArrangeItemsContextMenuExtension/WrapItemsContextMenuExtension
	/// (Phase 4c, Task 8), which don't make sense for a single-item selection. No
	/// WPF-specific API - mechanical port. The reference additionally overrides
	/// ShouldBeReApplied() => true (WpfDesign/Project/Extensions/SelectionExtensionServers.cs:125-128);
	/// this port skips it, matching OnlyOneItemSelectedExtensionServer's own precedent above,
	/// which achieves reapplication directly through its OnSelectionChanged handler instead.</summary>
	public class PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer : PrimarySelectionExtensionServer
	{
		protected override void OnInitialized()
		{
			base.OnInitialized();
			this.Services.Selection.SelectionChanged += OnSelectionChanged;
		}

		void OnSelectionChanged(object sender, System.EventArgs e)
		{
			ReapplyExtensions(this.Services.Selection.SelectedItems);
		}

		public override bool ShouldApplyExtensions(DesignItem extendedItem)
		{
			return Services.Selection.PrimarySelection == extendedItem && Services.Selection.SelectionCount > 1;
		}
	}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --test=WinUIDesigner.DesignModel.Tests.SelectionExtensionServersTests --noresult`
Expected: PASS, 2 tests (the existing one plus this new one).

- [ ] **Step 5: Re-run full suites, then commit**

```bash
git add src/WinUIDesigner.DesignModel/Extensions/SelectionExtensionServers.cs tests/WinUIDesigner.DesignModel.Tests/SelectionExtensionServersTests.cs
git commit -m "feat: add PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer"
```

---

### Task 3: `PlacementOperation.DeleteItemsAndCommit` / `TryStartInsertNewComponents`

**Files:**
- Modify: `src/WinUIDesigner.DesignModel/PlacementOperation.cs`
- Test: `tests/WinUIDesigner.DesignModel.Tests/PlacementOperationDeleteTests.cs`

**Interfaces:**
- Consumes: `PlacementOperation.Start` (existing),
  `IPlacementBehavior.CanLeaveContainer`/`LeaveContainer`/
  `CanEnterContainer`/`EnterContainer` (existing interface members, all
  already implemented by `DefaultPlacementBehavior`),
  `PlacementInformation.OriginalBounds`/`Bounds` (existing, public
  setters).
- Produces: `PlacementOperation.DeleteItemsAndCommit()` — instance
  method, no arguments, consumed by Task 4's `ModelTools.DeleteComponents`.
  `PlacementOperation.TryStartInsertNewComponents(DesignItem container,
  IList<DesignItem> placedItems, IList<Rect> positions, PlacementType
  type)` — static method returning `PlacementOperation` (or `null` if the
  container refuses), consumed by Task 6's
  `ModelTools.WrapItemsNewContainer`.

- [ ] **Step 1: Write the failing test**

`WinUIDesigner.DesignModel.Tests` has no `ProjectReference` to
`WinUIDesigner.Designer` (checked directly — `tests/WinUIDesigner.
DesignModel.Tests/WinUIDesigner.DesignModel.Tests.csproj` only
references `WinUIDesigner.DesignModel` and `WinUIDesigner.TestHost`), so
`DefaultPlacementBehavior` (the Designer-layer placement behavior every
other phase's Designer.Tests fixture registers) is not available here.
`PlacementOperation.Start`/`TryStartInsertNewComponents` both need a
real `IPlacementBehavior` attached to the container via
`DesignItem.GetBehavior<IPlacementBehavior>()`, so this test defines its
own minimal one and registers the test assembly itself as a
`DesignerAssembly` — same `BehaviorExtension`/`AddBehavior` pattern
`DefaultPlacementBehavior` uses (`src/WinUIDesigner.Designer/Extensions/
DefaultPlacementBehavior.cs:32-43`), confirmed available at this layer:
`BehaviorExtension` (`src/WinUIDesigner.DesignModel/Extensions/
BehaviorExtension.cs`) already carries an inherited
`[ExtensionServer(typeof(DefaultExtensionServer.Permanent))]`, and
`DesignItem.AddBehavior(Type, object)` (`DesignItem.cs:281`) is public.

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class PlacementOperationDeleteTests
    {
        const string TwoButtonsInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" Canvas.Left=\"0\" Canvas.Top=\"0\" />" +
            "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" Canvas.Left=\"40\" Canvas.Top=\"0\" />" +
            "</Canvas>";

        // Minimal real IPlacementBehavior, standing in for the Designer-layer
        // DefaultPlacementBehavior this test project cannot reference. Resizes/repositions are
        // no-ops (not exercised by these tests) - only the container-membership machinery
        // (CanLeaveContainer/LeaveContainer/CanEnterContainer/EnterContainer) needs to be real.
        [ExtensionFor(typeof(object))]
        public sealed class FakePlacementBehavior : BehaviorExtension, IPlacementBehavior
        {
            protected override void OnInitialized()
            {
                base.OnInitialized();
                ExtendedItem.AddBehavior(typeof(IPlacementBehavior), this);
            }

            public bool CanPlace(IEnumerable<DesignItem> childItems, PlacementType type, PlacementAlignment position) => true;
            public void BeginPlacement(PlacementOperation operation) { }
            public void EndPlacement(PlacementOperation operation) { }
            public Windows.Foundation.Rect GetPosition(PlacementOperation operation, DesignItem child) => new Windows.Foundation.Rect(0, 0, 20, 20);
            public void BeforeSetPosition(PlacementOperation operation, Windows.System.VirtualKeyModifiers modifiers) { }
            public void SetPosition(PlacementInformation info) { }
            public bool CanLeaveContainer(PlacementOperation operation) => true;
            public void LeaveContainer(PlacementOperation operation)
            {
                foreach (var info in operation.PlacedItems)
                    info.Item.ParentProperty.CollectionElements.Remove(info.Item);
            }
            public bool CanEnterContainer(PlacementOperation operation, bool shouldAlwaysEnter) => true;
            public void EnterContainer(PlacementOperation operation)
            {
                foreach (var info in operation.PlacedItems)
                    operation.CurrentContainer.ContentProperty.CollectionElements.Add(info.Item);
            }
            public Windows.Foundation.Point PlacePoint(Windows.Foundation.Point point, Windows.System.VirtualKeyModifiers modifiers) => point;
        }

        static XamlLoadSettings Settings() => new XamlLoadSettings { DesignerAssemblies = { typeof(PlacementOperationDeleteTests).Assembly } };

        [Test]
        public void DeleteItemsAndCommitRemovesItemFromParentCollection()
        {
            WinUITestHost.Run(() => {
                using (var reader = XmlReader.Create(new StringReader(TwoButtonsInCanvasXaml))) {
                    var context = new XamlDesignContext(reader, Settings());
                    var canvas = (Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component;
                    var aItem = context.Services.Component.GetDesignItem(canvas.Children[0]);

                    var operation = PlacementOperation.Start(new[] { aItem }, PlacementType.Delete);
                    operation.DeleteItemsAndCommit();

                    Assert.AreEqual(1, canvas.Children.Count);
                    Assert.IsFalse(context.RootItem.ContentProperty.CollectionElements.Contains(aItem));
                }
            });
        }

        [Test]
        public void TryStartInsertNewComponentsAddsItemToContainerAtGivenPosition()
        {
            WinUITestHost.Run(() => {
                const string emptyCanvasXaml =
                    "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />";
                using (var reader = XmlReader.Create(new StringReader(emptyCanvasXaml))) {
                    var context = new XamlDesignContext(reader, Settings());
                    var canvasItem = context.RootItem;
                    var newButton = new Microsoft.UI.Xaml.Controls.Button { Width = 20, Height = 20 };
                    var newButtonItem = context.Services.Component.RegisterComponentForDesigner(newButton);

                    var op = PlacementOperation.TryStartInsertNewComponents(
                        canvasItem,
                        new[] { newButtonItem },
                        new[] { new Windows.Foundation.Rect(5, 5, 20, 20) },
                        PlacementType.AddItem);

                    Assert.IsNotNull(op);
                    op.Commit();

                    var canvas = (Microsoft.UI.Xaml.Controls.Canvas)canvasItem.Component;
                    Assert.AreEqual(1, canvas.Children.Count);
                    Assert.AreSame(newButton, canvas.Children[0]);
                }
            });
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --test=WinUIDesigner.DesignModel.Tests.PlacementOperationDeleteTests --noresult`
Expected: build FAILS with `CS1061` (`DeleteItemsAndCommit` and
`TryStartInsertNewComponents` don't exist on `PlacementOperation`) and
`CS0103` (`PlacementType.AddItem` may or may not already exist — if the
build fails on that symbol too, add `public static readonly PlacementType
AddItem = Register("AddItem");` to `src/WinUIDesigner.DesignModel/PlacementType.cs`
first, matching the existing `Delete`/`Move` entries' pattern; check the
file before assuming it's missing).

- [ ] **Step 3: Implement `DeleteItemsAndCommit`**

In `src/WinUIDesigner.DesignModel/PlacementOperation.cs`, add this method
directly after the existing `ChangeContainer` method (before
`TransformRectByMiddlePoint`):

```csharp
        /// <summary>Removes the placed item(s) from their container and commits, as one step.
        /// Ported from the reference (WpfDesign/Project/PlacementOperation.cs:159-168) -
        /// dropped in Phase 3 for lack of a caller (see this file's class doc comment); Phase
        /// 4c's Delete command is the first real caller.</summary>
        public void DeleteItemsAndCommit()
        {
            if (isAborted || isCommitted)
                throw new PlacementOperationException("The operation is not running anymore.");
            if (!currentContainerBehavior.CanLeaveContainer(this))
                throw new PlacementOperationException("The items cannot be removed from their parent container.");

            currentContainerBehavior.LeaveContainer(this);
            Commit();
        }
```

- [ ] **Step 4: Implement `TryStartInsertNewComponents`**

In the same file, add this static method directly after the existing
`Start` static method:

```csharp
        /// <summary>Starts a placement operation that inserts newly-created item(s) directly
        /// into an existing container at given positions, bypassing Start()'s
        /// BeginPlacement/GetPosition initialization (there is no existing position to read -
        /// the positions are supplied). Ported from the reference (WpfDesign/Project/
        /// PlacementOperation.cs:307-342) - dropped in Phase 3 for lack of a caller; Phase 4c's
        /// WrapItemsNewContainer (Task 6) is the first real caller. Returns null if the
        /// container's placement behavior refuses (matches the reference; the reference also
        /// swallows and rethrows unexpected exceptions through Debug.WriteLine, dropped here -
        /// no debug-output-only side channel anywhere else in this port).</summary>
        public static PlacementOperation TryStartInsertNewComponents(DesignItem container, IList<DesignItem> placedItems, IList<Rect> positions, PlacementType type)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (placedItems == null) throw new ArgumentNullException(nameof(placedItems));
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (placedItems.Count == 0) throw new ArgumentException("placedItems.Count must be > 0");
            if (placedItems.Count != positions.Count) throw new ArgumentException("positions.Count must be = placedItems.Count");

            DesignItem[] items = placedItems.ToArray();
            PlacementOperation op = new PlacementOperation(items, type);
            try {
                for (int i = 0; i < items.Length; i++) {
                    op.placedItems[i].OriginalBounds = op.placedItems[i].Bounds = positions[i];
                }
                op.currentContainer = container;
                op.currentContainerBehavior = container.GetBehavior<IPlacementBehavior>();
                if (op.currentContainerBehavior == null || !op.currentContainerBehavior.CanEnterContainer(op, true)) {
                    op.changeGroup.Abort();
                    return null;
                }
                op.currentContainerBehavior.EnterContainer(op);
            } catch {
                op.changeGroup.Abort();
                throw;
            }
            return op;
        }
```

Note: this calls the existing private constructor `PlacementOperation(DesignItem[] items,
PlacementType type)` — since `TryStartInsertNewComponents` is a member of
`PlacementOperation` itself, it can call that private constructor
directly, same as `Start` does. `op.placedItems[i]` indexes the
`ReadOnlyCollection<PlacementInformation>` the constructor already built
from `items` — one `PlacementInformation` per item, in the same order.

If `PlacementType.AddItem` didn't already exist (Step 2), add it to
`src/WinUIDesigner.DesignModel/PlacementType.cs` now, next to the
existing `Delete`/`Move` entries:

```csharp
        public static readonly PlacementType AddItem = Register("AddItem");
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --test=WinUIDesigner.DesignModel.Tests.PlacementOperationDeleteTests --noresult`
Expected: PASS, 2 tests.

- [ ] **Step 6: Re-run full suites, then commit**

```bash
git add src/WinUIDesigner.DesignModel/PlacementOperation.cs src/WinUIDesigner.DesignModel/PlacementType.cs tests/WinUIDesigner.DesignModel.Tests/PlacementOperationDeleteTests.cs
git commit -m "feat: re-add PlacementOperation.DeleteItemsAndCommit and TryStartInsertNewComponents"
```

---

### Task 4: `ModelTools.CanDeleteComponents`/`DeleteComponents` and `CopyPasteService`

**Files:**
- Modify: `src/WinUIDesigner.Designer/ModelTools.cs`
- Create: `src/WinUIDesigner.Designer/Services/CopyPasteService.cs`
- Modify: `src/WinUIDesigner.Designer/DesignSurface.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/ModelToolsContextMenuTests.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/CopyPasteServiceTests.cs`

**Interfaces:**
- Consumes: `PlacementOperation.Start`/`GetPlacementBehavior`/
  `DeleteItemsAndCommit` (Task 3), `IPlacementBehavior.CanPlace` (already
  returns `true` unconditionally in `DefaultPlacementBehavior`),
  `DesignItem.OpenGroup` (existing), `DesignItem.ParentProperty`/
  `DesignItem.Services.Selection`/`DesignItem.Services.Component`
  (existing), `XamlComponentService.RaiseComponentRemoved` (existing,
  `internal`).
- Produces: `ModelTools.CanDeleteComponents(ICollection<DesignItem>
  items)` returning `bool`; `ModelTools.DeleteComponents(
  ICollection<DesignItem> deleteItems)` returning `void`. Both consumed
  by `CopyPasteService` below and by Task 7's
  `DefaultCommandsContextMenuExtension`.
  `WinUIDesigner.Designer.Services.CopyPasteService : ICopyPasteService`
  — `CanDelete`/`Delete` real, `CanCopy`/`Copy`/`CanCut`/`Cut`/
  `CanPaste`/`Paste` explicitly return `false`/no-op. Registered in
  `DesignSurface.LoadDesigner` as `ICopyPasteService`, consumed by Task
  7's `DefaultCommandsContextMenuExtension` via
  `ExtendedItem.Context.Services.GetService<ICopyPasteService>()` (there
  is no `Services.CopyPasteService` shortcut property — none exists yet
  in `ServiceContainer`, and adding one is out of this task's scope).

- [ ] **Step 1: Write the failing tests**

`tests/WinUIDesigner.Designer.Tests/ModelToolsContextMenuTests.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class ModelToolsContextMenuTests
    {
        const string TwoButtonsInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" Canvas.Left=\"0\" Canvas.Top=\"0\" />" +
            "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" Canvas.Left=\"40\" Canvas.Top=\"0\" />" +
            "</Canvas>";

        static (XamlDesignContext context, DesignItem aItem, DesignItem bItem) Load(Microsoft.UI.Xaml.Window window)
        {
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new WinUIDesigner.Designer.Services.DefaultSelectionService()));
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), new WinUIDesigner.Designer.DesignPanel()));
            using var reader = XmlReader.Create(new StringReader(TwoButtonsInCanvasXaml));
            var context = new XamlDesignContext(reader, settings);
            window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
            ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component;
            var aItem = context.Services.Component.GetDesignItem(canvas.Children[0]);
            var bItem = context.Services.Component.GetDesignItem(canvas.Children[1]);
            return (context, aItem, bItem);
        }

        [Test]
        public void CanDeleteComponentsIsTrueForANonEmptySelectionAndFalseForEmpty()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, aItem, _) = Load(window);
                Assert.IsTrue(WinUIDesigner.Designer.ModelTools.CanDeleteComponents(new[] { aItem }));
                Assert.IsFalse(WinUIDesigner.Designer.ModelTools.CanDeleteComponents(new WinUIDesigner.DesignModel.DesignItem[0]));
            });
        }

        [Test]
        public void DeleteComponentsRemovesItemsAndCommitsAsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, aItem, bItem) = Load(window);
                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component;
                var undoService = context.Services.GetService<UndoService>();
                var undoCountBefore = undoService.UndoActions.Count();

                WinUIDesigner.Designer.ModelTools.DeleteComponents(new[] { aItem, bItem });

                Assert.AreEqual(0, canvas.Children.Count);
                Assert.AreEqual(undoCountBefore + 1, undoService.UndoActions.Count());

                undoService.Undo();
                Assert.AreEqual(2, canvas.Children.Count);
            });
        }

        [Test]
        public void DeleteComponentsSelectsParentWhenSelectionBecomesEmpty()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, aItem, bItem) = Load(window);
                context.Services.Selection.SetSelectedComponents(new[] { aItem, bItem });

                WinUIDesigner.Designer.ModelTools.DeleteComponents(new[] { aItem, bItem });

                Assert.AreEqual(1, context.Services.Selection.SelectionCount);
                Assert.AreSame(context.RootItem, context.Services.Selection.PrimarySelection);
            });
        }
    }
}
```

`tests/WinUIDesigner.Designer.Tests/CopyPasteServiceTests.cs`:

```csharp
using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class CopyPasteServiceTests
    {
        const string OneButtonXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" /></Canvas>";

        [Test]
        public void CanDeleteAndDeleteWorkThroughTheDesignContext()
        {
            WinUITestHost.RunWithWindow(window => {
                var settings = new XamlLoadSettings();
                settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
                settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new DefaultSelectionService()));
                settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), new WinUIDesigner.Designer.DesignPanel()));
                using var reader = XmlReader.Create(new StringReader(OneButtonXaml));
                var context = new XamlDesignContext(reader, settings);
                window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component;
                var buttonItem = context.Services.Component.GetDesignItem(canvas.Children[0]);
                context.Services.Selection.SetSelectedComponents(new[] { buttonItem });

                var service = new CopyPasteService();
                Assert.IsTrue(service.CanDelete(context));

                service.Delete(context);

                Assert.AreEqual(0, canvas.Children.Count);
            });
        }

        [Test]
        public void CopyCutPasteAreExplicitlyUnimplemented()
        {
            var service = new CopyPasteService();
            Assert.IsFalse(service.CanCopy(null));
            Assert.IsFalse(service.CanCut(null));
            Assert.IsFalse(service.CanPaste(null));
            // Copy/Cut/Paste no-op rather than throw - callers (Task 7's menu, if extended
            // later) can call them unconditionally without a null-service check.
            Assert.DoesNotThrow(() => service.Copy(null));
            Assert.DoesNotThrow(() => service.Cut(null));
            Assert.DoesNotThrow(() => service.Paste(null));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build WinUIDesigner.sln` then both `.exe`s filtered to
`ModelToolsContextMenuTests`/`CopyPasteServiceTests`.
Expected: build FAILS — `ModelTools.CanDeleteComponents`/
`DeleteComponents` don't exist (`CS0117`), `WinUIDesigner.Designer.
Services.CopyPasteService` doesn't exist (`CS0234`).

- [ ] **Step 3: Implement `ModelTools.CanDeleteComponents`/`DeleteComponents`**

Add to `src/WinUIDesigner.Designer/ModelTools.cs`, after the existing
`ApplyTransform` method, inside the `ModelTools` class. Add `using
System.Collections.Generic;` and `using WinUIDesigner.DesignModel.Xaml;`
to the file's existing `using` block first.

```csharp
        /// <summary>Gets if the specified components can be deleted. Ported unchanged from the
        /// reference (WpfDesign.Designer/Project/ModelTools.cs:84-89) - DefaultPlacementBehavior.
        /// CanPlace already returns true unconditionally for any PlacementType (Extensions/
        /// DefaultPlacementBehavior.cs:46), so this works against every placement behavior in
        /// this port with zero changes to any of them.</summary>
        public static bool CanDeleteComponents(ICollection<DesignItem> items)
        {
            IPlacementBehavior b = PlacementOperation.GetPlacementBehavior(items);
            return b != null && b.CanPlace(items, PlacementType.Delete, PlacementAlignment.Center);
        }

        /// <summary>Deletes the specified components from their parent containers, as one
        /// undo entry regardless of how many distinct parents are involved. Ported from the
        /// reference (WpfDesign.Designer/Project/ModelTools.cs:100-138) - RaiseComponentRemoved
        /// must be called explicitly (checked directly against this port's XamlComponentService,
        /// not automatic - see this plan's Global Constraints).</summary>
        public static void DeleteComponents(ICollection<DesignItem> deleteItems)
        {
            if (deleteItems.Count == 0) return;

            var changeGroup = deleteItems.First().OpenGroup("Delete Items");
            try {
                var itemsGrpParent = deleteItems.GroupBy(x => x.Parent);
                foreach (var itemsList in itemsGrpParent) {
                    var items = itemsList.ToList();
                    DesignItem parent = items.First().Parent;
                    PlacementOperation operation = PlacementOperation.Start(items, PlacementType.Delete);
                    try {
                        var selectionService = items.First().Services.Selection;
                        selectionService.SetSelectedComponents(items, SelectionTypes.Remove);
                        if (selectionService.SelectionCount == 0 && !items.Contains(parent)) {
                            selectionService.SetSelectedComponents(new[] { parent });
                        }
                        foreach (var designItem in items) {
                            designItem.Name = null;
                        }

                        var service = parent.Services.Component as XamlComponentService;
                        foreach (var item in items) {
                            service.RaiseComponentRemoved(item);
                        }

                        operation.DeleteItemsAndCommit();
                    } catch {
                        operation.Abort();
                        throw;
                    }
                }
                changeGroup.Commit();
            } catch {
                changeGroup.Abort();
                throw;
            }
        }
```

- [ ] **Step 4: Implement `CopyPasteService`**

Create `src/WinUIDesigner.Designer/Services/CopyPasteService.cs`:

```csharp
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;

namespace WinUIDesigner.Designer.Services
{
    /// <summary>Concrete ICopyPasteService. Lives in WinUIDesigner.Designer, not DesignModel
    /// (the interface's home) - it calls ModelTools, and DesignModel must not reference
    /// Designer. Only Delete/CanDelete are implemented for real; Copy/Cut/Paste stay
    /// explicitly unimplemented (Phase 4c's narrow scope - no real clipboard integration this
    /// phase, see the spec's "Decisions already made"). Registered in DesignSurface.LoadDesigner,
    /// same pattern as OptionService.</summary>
    public class CopyPasteService : ICopyPasteService
    {
        public bool CanCopy(DesignContext designContext) => false;
        public void Copy(DesignContext designContext) { }
        public bool CanCut(DesignContext designContext) => false;
        public void Cut(DesignContext designContext) { }
        public bool CanPaste(DesignContext designContext) => false;
        public void Paste(DesignContext designContext) { }

        public bool CanDelete(DesignContext designContext)
        {
            return designContext != null && ModelTools.CanDeleteComponents(designContext.Services.Selection.SelectedItems);
        }

        public void Delete(DesignContext designContext)
        {
            if (designContext != null)
                ModelTools.DeleteComponents(designContext.Services.Selection.SelectedItems);
        }
    }
}
```

Register it in `src/WinUIDesigner.Designer/DesignSurface.cs`'s
`LoadDesigner`, immediately after the existing `OptionService`
registration line:

```csharp
			loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.Services.ICopyPasteService), new Services.CopyPasteService()));
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet build WinUIDesigner.sln` then both `.exe`s filtered to
`ModelToolsContextMenuTests`/`CopyPasteServiceTests`.
Expected: PASS, 5 tests total.

- [ ] **Step 6: Re-run full suites, then commit**

```bash
git add src/WinUIDesigner.Designer/ModelTools.cs src/WinUIDesigner.Designer/Services/CopyPasteService.cs src/WinUIDesigner.Designer/DesignSurface.cs tests/WinUIDesigner.Designer.Tests/ModelToolsContextMenuTests.cs tests/WinUIDesigner.Designer.Tests/CopyPasteServiceTests.cs
git commit -m "feat: add ModelTools.DeleteComponents and a concrete CopyPasteService"
```

---

### Task 5: `ModelTools.ArrangeItems`

**Files:**
- Create: `src/WinUIDesigner.Designer/ArrangeDirection.cs`
- Modify: `src/WinUIDesigner.Designer/ModelTools.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/ModelToolsArrangeItemsTests.cs`

**Interfaces:**
- Consumes: `PlacementOperation.Start`/`GetPlacementBehavior` (existing),
  `DesignItemProperty.GetAttachedProperty(Type, string)`/`GetProperty(
  string)` (existing).
- Produces: `ModelTools.ArrangeItems(IEnumerable<DesignItem> items,
  ArrangeDirection direction)` returning `void`, and
  `WinUIDesigner.Designer.ArrangeDirection` (enum: `Top`,
  `VerticalMiddle`, `Bottom`, `Left`, `HorizontalMiddle`, `Right`).
  Consumed by Task 8's `ArrangeItemsContextMenuExtension`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class ModelToolsArrangeItemsTests
    {
        static (XamlDesignContext context, DesignItem aItem, DesignItem bItem) LoadCanvas(Microsoft.UI.Xaml.Window window)
        {
            const string xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" Canvas.Left=\"0\" Canvas.Top=\"0\" />" +
                "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" Canvas.Left=\"50\" Canvas.Top=\"30\" />" +
                "</Canvas>";
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new WinUIDesigner.Designer.Services.DefaultSelectionService()));
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), new WinUIDesigner.Designer.DesignPanel()));
            using var reader = XmlReader.Create(new StringReader(xaml));
            var context = new XamlDesignContext(reader, settings);
            window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
            ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component;
            var aItem = context.Services.Component.GetDesignItem(canvas.Children[0]);
            var bItem = context.Services.Component.GetDesignItem(canvas.Children[1]);
            return (context, aItem, bItem);
        }

        [Test]
        public void ArrangeLeftAlignsBothItemsToTheLeftmostItemsCanvasLeft()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, aItem, bItem) = LoadCanvas(window);
                WinUIDesigner.Designer.ModelTools.ArrangeItems(new[] { aItem, bItem }, ArrangeDirection.Left);

                Assert.AreEqual(0.0, aItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>());
                Assert.AreEqual(0.0, bItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>());
            });
        }

        [Test]
        public void ArrangeTopAlignsBothItemsToTheTopmostItemsCanvasTop()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, aItem, bItem) = LoadCanvas(window);
                WinUIDesigner.Designer.ModelTools.ArrangeItems(new[] { aItem, bItem }, ArrangeDirection.Top);

                Assert.AreEqual(0.0, aItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>());
                Assert.AreEqual(0.0, bItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>());
            });
        }

        [Test]
        public void ArrangeSingleItemIsANoOpDeltaAgainstItself()
        {
            // A single-item "selection" arranges against its own bounds - min == max == its
            // own position, so this exercises the min/max math without needing a second item.
            WinUITestHost.RunWithWindow(window => {
                var (context, aItem, _) = LoadCanvas(window);
                WinUIDesigner.Designer.ModelTools.ArrangeItems(new[] { aItem }, ArrangeDirection.Right);

                Assert.AreEqual(0.0, aItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>());
            });
        }

        [Test]
        public void ArrangeInGridSetsMarginLeftWhenNotRightAligned()
        {
            const string gridXaml =
                "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" HorizontalAlignment=\"Left\" Margin=\"5,5,0,0\" /></Grid>";
            WinUITestHost.RunWithWindow(window => {
                var settings = new XamlLoadSettings();
                settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
                settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new WinUIDesigner.Designer.Services.DefaultSelectionService()));
                settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), new WinUIDesigner.Designer.DesignPanel()));
                using var reader = XmlReader.Create(new StringReader(gridXaml));
                var context = new XamlDesignContext(reader, settings);
                window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
                var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                var aItem = context.Services.Component.GetDesignItem(grid.Children[0]);

                WinUIDesigner.Designer.ModelTools.ArrangeItems(new[] { aItem }, ArrangeDirection.Left);

                var margin = aItem.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Microsoft.UI.Xaml.Thickness>();
                Assert.AreEqual(0.0, margin.Left);
            });
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.ModelToolsArrangeItemsTests --noresult`
Expected: build FAILS — `ArrangeDirection`/`ModelTools.ArrangeItems`
don't exist.

- [ ] **Step 3: Create `ArrangeDirection`**

Create `src/WinUIDesigner.Designer/ArrangeDirection.cs`:

```csharp
namespace WinUIDesigner.Designer
{
    /// <summary>Ported unchanged from the reference (WpfDesign.Designer/Project/
    /// ArrangeDirection.cs) - standalone enum, same convention as PlacementType.cs.</summary>
    public enum ArrangeDirection
    {
        Top,
        VerticalMiddle,
        Bottom,
        Left,
        HorizontalMiddle,
        Right,
    }
}
```

- [ ] **Step 4: Implement `ArrangeItems`**

Add to `src/WinUIDesigner.Designer/ModelTools.cs`, after
`DeleteComponents` (Task 4). Also add a small private `ItemPos` helper
class and `GetItemPos` method used by both this task and Task 6 — add
them once here, at the bottom of the `ModelTools` class:

```csharp
        sealed class ItemPos
        {
            public DesignItem DesignItem;
            public double Xmin, Xmax, Ymin, Ymax;
        }

        static ItemPos GetItemPos(PlacementOperation operation, DesignItem designItem)
        {
            var pos = operation.CurrentContainerBehavior.GetPosition(operation, designItem);
            return new ItemPos {
                DesignItem = designItem,
                Xmin = pos.X, Xmax = pos.X + pos.Width,
                Ymin = pos.Y, Ymax = pos.Y + pos.Height,
            };
        }

        /// <summary>Aligns the given items along one edge/midline of their combined bounds.
        /// Ported from the reference (WpfDesign.Designer/Project/ModelTools.cs:628-870) with
        /// two changes: (1) the Canvas branch drops the Canvas.Right/Bottom-anchored `else`
        /// case entirely (those attached properties don't exist in WinUI - confirmed absent,
        /// CLAUDE.md) rather than reproducing an unreachable branch; (2) the reference's Top
        /// direction has a stray copy-pasted `Canvas.TopProperty.SetValue` line inside its Grid
        /// branch (a WPF attached property a Grid ignores, immediately followed by the actually-
        /// correct margin.Top assignment) - dropped, not reproduced, matching this port's
        /// established "fix a latent reference bug while porting" precedent. The Grid branch
        /// otherwise ports both alignment cases unchanged - no WinUI gap there, Margin.Right
        /// exists.</summary>
        public static void ArrangeItems(IEnumerable<DesignItem> items, ArrangeDirection arrangeDirection)
        {
            var collection = items.ToList();
            var container = collection.First().Parent;
            if (collection.Any(x => x.Parent != container)) return;

            var placement = container.GetBehavior<IPlacementBehavior>();
            if (placement == null) return;

            var operation = PlacementOperation.Start(collection, PlacementType.Move);

            var itemList = collection.Select(item => GetItemPos(operation, item)).ToList();
            var xmin = itemList.Min(x => x.Xmin);
            var xmax = itemList.Max(x => x.Xmax);
            var mpos = (xmax - xmin) / 2 + xmin;
            var ymin = itemList.Min(x => x.Ymin);
            var ymax = itemList.Max(x => x.Ymax);
            var ympos = (ymax - ymin) / 2 + ymin;

            foreach (var item in collection) {
                var fe = (Microsoft.UI.Xaml.FrameworkElement)item.Component;
                switch (arrangeDirection) {
                    case ArrangeDirection.Left:
                        if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                            item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").SetValue(xmin);
                        } else if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                            SetGridMargin(item, isRightAligned: HAlign(item) == Microsoft.UI.Xaml.HorizontalAlignment.Right,
                                leftValue: xmin, rightValue: ((Microsoft.UI.Xaml.FrameworkElement)item.Parent.Component).ActualWidth - (xmin + fe.ActualWidth));
                        }
                        break;
                    case ArrangeDirection.HorizontalMiddle:
                        if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                            item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").SetValue(mpos - fe.ActualWidth / 2);
                        } else if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                            var left = mpos - fe.ActualWidth / 2;
                            SetGridMargin(item, isRightAligned: HAlign(item) == Microsoft.UI.Xaml.HorizontalAlignment.Right,
                                leftValue: left, rightValue: ((Microsoft.UI.Xaml.FrameworkElement)item.Parent.Component).ActualWidth - left - fe.ActualWidth);
                        }
                        break;
                    case ArrangeDirection.Right:
                        if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                            item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").SetValue(xmax - fe.ActualWidth);
                        } else if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                            var left = xmax - fe.ActualWidth;
                            SetGridMargin(item, isRightAligned: HAlign(item) == Microsoft.UI.Xaml.HorizontalAlignment.Right,
                                leftValue: left, rightValue: ((Microsoft.UI.Xaml.FrameworkElement)item.Parent.Component).ActualWidth - xmax);
                        }
                        break;
                    case ArrangeDirection.Top:
                        if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                            item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").SetValue(ymin);
                        } else if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                            SetGridMarginVertical(item, isBottomAligned: VAlign(item) == Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                                topValue: ymin, bottomValue: ((Microsoft.UI.Xaml.FrameworkElement)item.Parent.Component).ActualHeight - (ymin + fe.ActualHeight));
                        }
                        break;
                    case ArrangeDirection.VerticalMiddle:
                        if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                            item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").SetValue(ympos - fe.ActualHeight / 2);
                        } else if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                            var top = ympos - fe.ActualHeight / 2;
                            SetGridMarginVertical(item, isBottomAligned: VAlign(item) == Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                                topValue: top, bottomValue: ((Microsoft.UI.Xaml.FrameworkElement)item.Parent.Component).ActualHeight - top - fe.ActualHeight);
                        }
                        break;
                    case ArrangeDirection.Bottom:
                        if (container.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                            item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").SetValue(ymax - fe.ActualHeight);
                        } else if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                            var top = ymax - fe.ActualHeight;
                            SetGridMarginVertical(item, isBottomAligned: VAlign(item) == Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                                topValue: top, bottomValue: ((Microsoft.UI.Xaml.FrameworkElement)item.Parent.Component).ActualHeight - ymax);
                        }
                        break;
                }
            }

            operation.Commit();
        }

        static Microsoft.UI.Xaml.HorizontalAlignment HAlign(DesignItem item) =>
            item.Properties.GetProperty("HorizontalAlignment").GetConvertedValueOnInstance<Microsoft.UI.Xaml.HorizontalAlignment>();

        static Microsoft.UI.Xaml.VerticalAlignment VAlign(DesignItem item) =>
            item.Properties.GetProperty("VerticalAlignment").GetConvertedValueOnInstance<Microsoft.UI.Xaml.VerticalAlignment>();

        static void SetGridMargin(DesignItem item, bool isRightAligned, double leftValue, double rightValue)
        {
            var margin = item.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Microsoft.UI.Xaml.Thickness>();
            if (isRightAligned) margin.Right = rightValue; else margin.Left = leftValue;
            item.Properties.GetProperty("Margin").SetValue(margin);
        }

        static void SetGridMarginVertical(DesignItem item, bool isBottomAligned, double topValue, double bottomValue)
        {
            var margin = item.Properties.GetProperty("Margin").GetConvertedValueOnInstance<Microsoft.UI.Xaml.Thickness>();
            if (isBottomAligned) margin.Bottom = bottomValue; else margin.Top = topValue;
            item.Properties.GetProperty("Margin").SetValue(margin);
        }
```

`SetGridMargin`/`SetGridMarginVertical` factor out the repeated
"read Margin, set one side based on alignment, write Margin back"
pattern that appears in all six reference cases' Grid branches — same
logic, deduplicated rather than copy-pasted six times, matching this
plan's "smaller, focused" preference. `HAlign`/`VAlign` read the item's
live `HorizontalAlignment`/`VerticalAlignment` property (not the dead
`ItemPos` fields — see this plan's Task 6 for that distinction).

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.ModelToolsArrangeItemsTests --noresult`
Expected: PASS, 4 tests.

- [ ] **Step 6: Re-run full suites, then commit**

```bash
git add src/WinUIDesigner.Designer/ArrangeDirection.cs src/WinUIDesigner.Designer/ModelTools.cs tests/WinUIDesigner.Designer.Tests/ModelToolsArrangeItemsTests.cs
git commit -m "feat: add ModelTools.ArrangeItems"
```

---

### Task 6: `ModelTools.WrapItemsNewContainer`/`UnwrapItemsFromContainer`

**Files:**
- Modify: `src/WinUIDesigner.Designer/ModelTools.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/ModelToolsWrapUnwrapTests.cs`

**Interfaces:**
- Consumes: `PlacementOperation.Start`/`TryStartInsertNewComponents`
  (Task 3), `ExtensionManager.CreateInstanceWithCustomInstanceFactory`
  (existing), `IComponentService.RegisterComponentForDesigner` (existing),
  this task's own `ItemPos`/`GetItemPos` helper (Task 5).
- Produces: `ModelTools.WrapItemsNewContainer(IEnumerable<DesignItem>
  items, System.Type containerType, bool doInsert = true)` returning
  `System.Tuple<DesignItem, Windows.Foundation.Rect>`;
  `ModelTools.UnwrapItemsFromContainer(DesignItem container)` returning
  `void`. Both consumed by Task 8's `WrapItemContextMenuExtension`/
  `WrapItemsContextMenuExtension`/`UnwrapItemContextMenuExtension`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class ModelToolsWrapUnwrapTests
    {
        static (XamlDesignContext context, DesignItem canvasItem, DesignItem aItem, DesignItem bItem) LoadTwoButtonsInCanvas(Microsoft.UI.Xaml.Window window)
        {
            const string xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" Canvas.Left=\"0\" Canvas.Top=\"0\" />" +
                "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" Canvas.Left=\"50\" Canvas.Top=\"30\" />" +
                "</Canvas>";
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new WinUIDesigner.Designer.Services.DefaultSelectionService()));
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), new WinUIDesigner.Designer.DesignPanel()));
            using var reader = XmlReader.Create(new StringReader(xaml));
            var context = new XamlDesignContext(reader, settings);
            window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
            ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
            var aItem = context.Services.Component.GetDesignItem(((Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component).Children[0]);
            var bItem = context.Services.Component.GetDesignItem(((Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component).Children[1]);
            return (context, context.RootItem, aItem, bItem);
        }

        [Test]
        public void WrapItemsNewContainerCreatesAndInsertsANewViewboxAsTheOnlyChild()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, canvasItem, aItem, _) = LoadTwoButtonsInCanvas(window);

                var result = WinUIDesigner.Designer.ModelTools.WrapItemsNewContainer(new[] { aItem }, typeof(Microsoft.UI.Xaml.Controls.Viewbox));

                Assert.IsNotNull(result);
                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)canvasItem.Component;
                Assert.AreEqual(2, canvas.Children.Count, "the wrapper replaces A in the canvas, B is untouched");
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Controls.Viewbox>(result.Item1.Component);
                var viewbox = (Microsoft.UI.Xaml.Controls.Viewbox)result.Item1.Component;
                Assert.AreSame(viewbox.Child, aItem.Component);
            });
        }

        [Test]
        public void WrapItemsNewContainerInCanvasSetsLeftTopOnTheWrappedItem()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, canvasItem, aItem, bItem) = LoadTwoButtonsInCanvas(window);

                WinUIDesigner.Designer.ModelTools.WrapItemsNewContainer(new[] { aItem, bItem }, typeof(Microsoft.UI.Xaml.Controls.Canvas));

                // Both items move into a new inner Canvas, re-anchored relative to their own
                // combined bounds (0,0)-(70,50) - A stays at its own Left/Top since it's already
                // the min corner.
                Assert.AreEqual(0.0, aItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>());
                Assert.AreEqual(0.0, aItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>());
            });
        }

        [Test]
        public void UnwrapItemsFromContainerMovesChildrenBackToTheOuterContainer()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, canvasItem, aItem, _) = LoadTwoButtonsInCanvas(window);
                var wrapResult = WinUIDesigner.Designer.ModelTools.WrapItemsNewContainer(new[] { aItem }, typeof(Microsoft.UI.Xaml.Controls.Canvas));
                var innerCanvasItem = wrapResult.Item1;

                WinUIDesigner.Designer.ModelTools.UnwrapItemsFromContainer(innerCanvasItem);

                var outerCanvas = (Microsoft.UI.Xaml.Controls.Canvas)canvasItem.Component;
                Assert.IsFalse(outerCanvas.Children.Contains(innerCanvasItem.Component), "the emptied wrapper is gone");
                Assert.IsTrue(outerCanvas.Children.Contains(aItem.Component), "A is back in the outer canvas");
            });
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.ModelToolsWrapUnwrapTests --noresult`
Expected: build FAILS —
`WrapItemsNewContainer`/`UnwrapItemsFromContainer` don't exist.

- [ ] **Step 3: Implement `WrapItemsNewContainer` and `UnwrapItemsFromContainer`**

Add to `src/WinUIDesigner.Designer/ModelTools.cs`, after `ArrangeItems`
(Task 5). Both use the `ItemPos`/`GetItemPos` helper Task 5 already
added — do not redeclare it.

Note on scope, from the spec's plan-writing addendum: the reference's
`ItemPos.HorizontalAlignment`/`VerticalAlignment` fields are never
assigned anywhere in the reference file, so every comparison against
them is always `false` (dead code) — this port omits those fields
entirely and always takes the "not right/bottom-aligned" branch, which
is what the reference actually does at runtime today. This is a
narrower but behavior-preserving port, not a functional cut.

```csharp
        /// <summary>Wraps the given items (all siblings under the same parent) in a new
        /// container instance, replacing them at their combined position. Ported from the
        /// reference (WpfDesign.Designer/Project/ModelTools.cs:257-380) with the reference's own
        /// dead Right/Bottom-anchored branches (ItemPos.HorizontalAlignment/VerticalAlignment
        /// are read but never assigned anywhere in the reference - always false) collapsed to
        /// their always-taken Left/Top-anchored case - see this plan's Task 6 note. Canvas.Right/
        /// BottomProperty resets also dropped (don't exist in WinUI, and were resetting
        /// properties this port never sets in the first place).</summary>
        public static System.Tuple<DesignItem, Rect> WrapItemsNewContainer(IEnumerable<DesignItem> items, System.Type containerType, bool doInsert = true)
        {
            var collection = items.ToList();
            var context = collection.First().Context;
            var container = collection.First().Parent;
            if (collection.Any(x => x.Parent != container)) return null;

            var placement = container.GetBehavior<IPlacementBehavior>();
            if (placement == null) return null;

            var operation = PlacementOperation.Start(collection, PlacementType.Move);

            var newInstance = context.Services.ExtensionManager.CreateInstanceWithCustomInstanceFactory(containerType, null);
            DesignItem newPanel = context.Services.Component.RegisterComponentForDesigner(newInstance);

            var itemList = new System.Collections.Generic.List<ItemPos>();
            int? firstIndex = null;

            foreach (var item in collection) {
                itemList.Add(GetItemPos(operation, item));
                if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                    item.Properties.GetProperty("HorizontalAlignment").Reset();
                    item.Properties.GetProperty("VerticalAlignment").Reset();
                    item.Properties.GetProperty("Margin").Reset();
                }

                if (item.ParentProperty.IsCollection) {
                    var parentCollection = item.ParentProperty.CollectionElements;
                    if (!firstIndex.HasValue) firstIndex = parentCollection.IndexOf(item);
                    parentCollection.Remove(item);
                } else {
                    item.ParentProperty.Reset();
                }
            }

            var xmin = itemList.Min(x => x.Xmin);
            var xmax = itemList.Max(x => x.Xmax);
            var ymin = itemList.Min(x => x.Ymin);
            var ymax = itemList.Max(x => x.Ymax);

            foreach (var item in itemList) {
                if (newPanel.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                    item.DesignItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").SetValue(item.Xmin - xmin);
                    item.DesignItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").SetValue(item.Ymin - ymin);
                    newPanel.ContentProperty.CollectionElements.Add(item.DesignItem);
                } else if (newPanel.Component is Microsoft.UI.Xaml.Controls.Grid) {
                    var thickness = new Microsoft.UI.Xaml.Thickness(item.Xmin - xmin, item.Ymin - ymin, 0, 0);
                    item.DesignItem.Properties.GetProperty("HorizontalAlignment").SetValue(Microsoft.UI.Xaml.HorizontalAlignment.Left);
                    item.DesignItem.Properties.GetProperty("VerticalAlignment").SetValue(Microsoft.UI.Xaml.VerticalAlignment.Top);
                    item.DesignItem.Properties.GetProperty("Margin").SetValue(thickness);
                    newPanel.ContentProperty.CollectionElements.Add(item.DesignItem);
                } else if (newPanel.Component is Microsoft.UI.Xaml.Controls.Viewbox || newPanel.Component is Microsoft.UI.Xaml.Controls.ContentControl) {
                    newPanel.ContentProperty.SetValue(item.DesignItem);
                }
            }

            if (doInsert) {
                var operation2 = PlacementOperation.TryStartInsertNewComponents(
                    container,
                    new[] { newPanel },
                    new[] { new Rect(xmin, ymin, xmax - xmin, ymax - ymin) },
                    PlacementType.AddItem);

                if (collection.Count == 1 && container.ContentProperty != null && container.ContentProperty.IsCollection) {
                    container.ContentProperty.CollectionElements.Remove(newPanel);
                    container.ContentProperty.CollectionElements.Insert(firstIndex.Value, newPanel);
                }

                operation2.Commit();
                context.Services.Selection.SetSelectedComponents(new[] { newPanel });
            }

            operation.Commit();
            return new System.Tuple<DesignItem, Rect>(newPanel, new Rect(xmin, ymin, xmax - xmin, ymax - ymin));
        }

        /// <summary>Moves a container's children back out to the container's own parent,
        /// removing the now-empty container. Ported unchanged in shape from the reference
        /// (WpfDesign.Designer/Project/ModelTools.cs:382-502) - same dead-branch collapse as
        /// WrapItemsNewContainer above.</summary>
        public static void UnwrapItemsFromContainer(DesignItem container)
        {
            var collection = container.ContentProperty.CollectionElements.ToList();
            var newPanel = container.Parent;
            if (collection.Any(x => x.Parent != container)) return;

            var placement = container.GetBehavior<IPlacementBehavior>();
            if (placement == null) return;

            var operation = PlacementOperation.Start(collection, PlacementType.Move);
            var containerPos = GetItemPos(operation, container);
            var itemList = new System.Collections.Generic.List<ItemPos>();

            foreach (var item in collection) {
                itemList.Add(GetItemPos(operation, item));
                if (container.Component is Microsoft.UI.Xaml.Controls.Grid) {
                    item.Properties.GetProperty("HorizontalAlignment").Reset();
                    item.Properties.GetProperty("VerticalAlignment").Reset();
                    item.Properties.GetProperty("Margin").Reset();
                }
                item.ParentProperty.CollectionElements.Remove(item);
            }

            newPanel.ContentProperty.CollectionElements.Remove(container);

            foreach (var item in itemList) {
                if (newPanel.Component is Microsoft.UI.Xaml.Controls.Canvas) {
                    item.DesignItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").SetValue(item.Xmin + containerPos.Xmin);
                    item.DesignItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").SetValue(item.Ymin + containerPos.Ymin);
                    newPanel.ContentProperty.CollectionElements.Add(item.DesignItem);
                } else if (newPanel.Component is Microsoft.UI.Xaml.Controls.Grid) {
                    var thickness = new Microsoft.UI.Xaml.Thickness(item.Xmin, item.Ymin, 0, 0);
                    item.DesignItem.Properties.GetProperty("HorizontalAlignment").SetValue(Microsoft.UI.Xaml.HorizontalAlignment.Left);
                    item.DesignItem.Properties.GetProperty("VerticalAlignment").SetValue(Microsoft.UI.Xaml.VerticalAlignment.Top);
                    item.DesignItem.Properties.GetProperty("Margin").SetValue(thickness);
                    newPanel.ContentProperty.CollectionElements.Add(item.DesignItem);
                } else if (newPanel.Component is Microsoft.UI.Xaml.Controls.Viewbox || newPanel.Component is Microsoft.UI.Xaml.Controls.ContentControl) {
                    newPanel.ContentProperty.SetValue(item.DesignItem);
                }
            }

            operation.Commit();
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.ModelToolsWrapUnwrapTests --noresult`
Expected: PASS, 3 tests. If `WrapItemsNewContainerCreatesAndInsertsANewViewboxAsTheOnlyChild`
fails on the `Viewbox.Child` assertion because `Viewbox` isn't a
`ContentControl` in this WinUI SDK version (its content property may
resolve differently than WPF's `Viewbox.Child`), adjust the branch in
`WrapItemsNewContainer` for `Viewbox` to use whatever this port's
`DesignItem.ContentProperty`/`[ContentProperty]` metadata resolves to
for `Viewbox` instead of assuming `Viewbox : ContentControl` — check
empirically (a quick throwaway probe, same method as the spec's Known
gaps) rather than guessing further.

- [ ] **Step 5: Re-run full suites, then commit**

```bash
git add src/WinUIDesigner.Designer/ModelTools.cs tests/WinUIDesigner.Designer.Tests/ModelToolsWrapUnwrapTests.cs
git commit -m "feat: add ModelTools.WrapItemsNewContainer and UnwrapItemsFromContainer"
```

---

### Task 7: `RightClickContextMenuExtension` and `DefaultCommandsContextMenuExtension`

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/RightClickContextMenuExtension.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/DefaultCommandsContextMenuExtension.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/RightClickAndDefaultCommandsContextMenuExtensionTests.cs`

**Interfaces:**
- Consumes: `DesignPanel.AddContextFlyoutItems`/`RemoveContextFlyoutItems`
  (Task 1), `OnlyOneItemSelectedExtensionServer`/
  `PrimarySelectionExtensionServer` (existing, Phase 3/4a), `DefaultExtension`
  (existing, `OnInitialized`/`OnRemove`/`ExtendedItem`/`Services`), `ICopyPasteService`
  (Task 4, via `Services.GetService<ICopyPasteService>()`), `UndoService`
  (existing, via `Services.GetService<UndoService>()`).
- Produces: `WinUIDesigner.Designer.Extensions.
  RightClickContextMenuExtension` (`[ExtensionServer(typeof(
  OnlyOneItemSelectedExtensionServer))]`, `[ExtensionFor(typeof(
  FrameworkElement))]`, Order 20) and `WinUIDesigner.Designer.Extensions.
  DefaultCommandsContextMenuExtension` (`[ExtensionServer(typeof(
  PrimarySelectionExtensionServer))]`, `[ExtensionFor(typeof(object))]`,
  Order 10).

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class RightClickAndDefaultCommandsContextMenuExtensionTests
    {
        static (DesignSurface surface, DesignItem canvasItem, DesignItem aItem, DesignItem bItem) Load(Microsoft.UI.Xaml.Window window)
        {
            const string xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" />" +
                "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" />" +
                "</Canvas>";
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
            var aItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[0]);
            var bItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[1]);
            return (surface, surface.DesignContext.RootItem, aItem, bItem);
        }

        [Test]
        public void BringToFrontMovesTheItemToTheEndOfTheCollection()
        {
            // MenuFlyoutItem.Click is a sealed WinRT event with no way to raise it from a test,
            // so RightClickContextMenuExtension exposes internal command methods (BringToFront
            // etc.) that its Click handlers call - the test fetches the live extension instance
            // via DesignItem.Extensions and calls the method directly, exercising the same code
            // a real click would run.
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = Load(window);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });

                var flyout = (MenuFlyout)surface.DesignPanel.ContextFlyout;
                Assert.IsTrue(flyout.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Bring to Front"), "menu construction check");

                var extension = aItem.Extensions.OfType<RightClickContextMenuExtension>().Single();
                extension.BringToFront();

                var elements = canvasItem.ContentProperty.CollectionElements.ToList();
                Assert.AreEqual(aItem, elements[1], "A should now be last (front)");
            });
        }

        [Test]
        public void DeleteMenuItemDeletesTheSelectedItem()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = Load(window);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });

                var extension = aItem.Extensions.OfType<DefaultCommandsContextMenuExtension>().Single();
                extension.Delete();

                Assert.AreEqual(1, canvasItem.ContentProperty.CollectionElements.Count);
            });
        }

        [Test]
        public void UndoMenuItemUndoesTheLastChange()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = Load(window);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });
                var groupBefore = aItem.OpenGroup("test change");
                aItem.Properties.GetProperty("Width").SetValue(99.0);
                groupBefore.Commit();

                var extension = aItem.Extensions.OfType<DefaultCommandsContextMenuExtension>().Single();
                extension.Undo();

                Assert.AreEqual(20.0, ((Microsoft.UI.Xaml.FrameworkElement)aItem.Component).Width);
            });
        }
    }
}
```

`DesignItem.Extensions` (`public IEnumerable<Extension>`,
`src/WinUIDesigner.DesignModel/DesignItem.cs:151`) is this port's real
accessor for an item's currently-attached extensions — confirmed while
writing this plan, not guessed. `GetBehavior<T>()` (same file, line 299)
is a different, narrower accessor used elsewhere in this port
specifically for `IPlacementBehavior`-style lookups; `Extensions.OfType<T>()`
is the general form these tests need.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.RightClickAndDefaultCommandsContextMenuExtensionTests --noresult`
Expected: build FAILS — the two extension classes don't exist yet.

- [ ] **Step 3: Implement `RightClickContextMenuExtension`**

Create `src/WinUIDesigner.Designer/Extensions/RightClickContextMenuExtension.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Z-order context menu (Bring to Front / Send Forward / Send Backward / Send
    /// to Back) for the primary, single selection. Ported from the reference
    /// (RightClickContextMenuExtension.cs) - subclasses DefaultExtension directly, not
    /// AdornerProvider (no adorner visuals here - see the spec's "Also verified"). Internal
    /// command methods (BringToFront etc.) exist so tests can invoke them directly - there is
    /// no way to raise a sealed MenuFlyoutItem.Click from test code.</summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.FrameworkElement))]
    public sealed class RightClickContextMenuExtension : DefaultExtension
    {
        DesignPanel panel;
        MenuFlyoutItemBase[] items;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            var bringToFront = new MenuFlyoutItem { Text = "Bring to Front" };
            bringToFront.Click += (s, e) => BringToFront();
            var sendForward = new MenuFlyoutItem { Text = "Send Forward" };
            sendForward.Click += (s, e) => SendForward();
            var sendBackward = new MenuFlyoutItem { Text = "Send Backward" };
            sendBackward.Click += (s, e) => SendBackward();
            var sendToBack = new MenuFlyoutItem { Text = "Send to Back" };
            sendToBack.Click += (s, e) => SendToBack();
            items = new MenuFlyoutItemBase[] { bringToFront, sendForward, sendBackward, sendToBack };

            panel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            panel?.AddContextFlyoutItems(items, 20);
        }

        protected override void OnRemove()
        {
            panel?.RemoveContextFlyoutItems(items);
            base.OnRemove();
        }

        internal void BringToFront()
        {
            if (ExtendedItem.ParentProperty == null || !ExtendedItem.ParentProperty.IsCollection) return;
            var collection = ExtendedItem.ParentProperty.CollectionElements;
            collection.Remove(ExtendedItem);
            collection.Add(ExtendedItem);
        }

        internal void SendToBack()
        {
            if (ExtendedItem.ParentProperty == null || !ExtendedItem.ParentProperty.IsCollection) return;
            var collection = ExtendedItem.ParentProperty.CollectionElements;
            collection.Remove(ExtendedItem);
            collection.Insert(0, ExtendedItem);
        }

        internal void SendBackward()
        {
            if (ExtendedItem.ParentProperty == null || !ExtendedItem.ParentProperty.IsCollection) return;
            var collection = ExtendedItem.ParentProperty.CollectionElements;
            var idx = collection.IndexOf(ExtendedItem);
            collection.RemoveAt(idx);
            collection.Insert(idx <= 0 ? 0 : idx - 1, ExtendedItem);
        }

        internal void SendForward()
        {
            if (ExtendedItem.ParentProperty == null || !ExtendedItem.ParentProperty.IsCollection) return;
            var collection = ExtendedItem.ParentProperty.CollectionElements;
            var idx = collection.IndexOf(ExtendedItem);
            collection.RemoveAt(idx);
            var count = collection.Count;
            collection.Insert(idx + 1 > count ? count : idx + 1, ExtendedItem);
        }
    }
}
```

Before writing this, confirm `DesignItemProperty.CollectionElements`'s
exact type supports `Insert(int, DesignItem)`/`RemoveAt(int)`/
`IndexOf(DesignItem)` (it is `IObservableList<DesignItem>` per the
spec's "Also verified" section) — if any of these three members are
missing, check `src/WinUIDesigner.DesignModel/IObservableList.cs` (or
wherever it's defined) directly rather than guessing a workaround.

- [ ] **Step 4: Implement `DefaultCommandsContextMenuExtension`**

Create `src/WinUIDesigner.Designer/Extensions/DefaultCommandsContextMenuExtension.cs`:

```csharp
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.DesignModel.Services;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Delete/Undo/Redo context menu for the primary selection. Ported from the
    /// reference's DefaultCommandsContextMenuExtension.cs, narrowed to Delete/Undo/Redo only
    /// (Cut/Copy/Paste dropped - no real clipboard support this phase, see the spec's
    /// "Decisions already made"). Unlike the reference (which routes Delete/Undo/Redo through
    /// DesignSurface's WPF RoutedCommand hub via ApplicationCommands bindings, not ported this
    /// phase), each item's Click handler resolves ICopyPasteService/UndoService directly off
    /// ExtendedItem.Context.</summary>
    [ExtensionServer(typeof(PrimarySelectionExtensionServer))]
    [ExtensionFor(typeof(object))]
    public sealed class DefaultCommandsContextMenuExtension : DefaultExtension
    {
        DesignPanel panel;
        MenuFlyoutItemBase[] items;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            var delete = new MenuFlyoutItem { Text = "Delete" };
            delete.Click += (s, e) => Delete();
            var undo = new MenuFlyoutItem { Text = "Undo" };
            undo.Click += (s, e) => Undo();
            var redo = new MenuFlyoutItem { Text = "Redo" };
            redo.Click += (s, e) => Redo();
            items = new MenuFlyoutItemBase[] { delete, undo, redo };

            panel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            panel?.AddContextFlyoutItems(items, 10);
        }

        protected override void OnRemove()
        {
            panel?.RemoveContextFlyoutItems(items);
            base.OnRemove();
        }

        internal void Delete()
        {
            var copyPasteService = ExtendedItem.Services.GetService<ICopyPasteService>();
            if (copyPasteService != null && copyPasteService.CanDelete(ExtendedItem.Context))
                copyPasteService.Delete(ExtendedItem.Context);
        }

        internal void Undo()
        {
            var undoService = ExtendedItem.Services.GetService<UndoService>();
            if (undoService == null || !undoService.CanUndo) return;
            var action = undoService.UndoActions.First();
            undoService.Undo();
            ExtendedItem.Services.Selection.SetSelectedComponents(action.AffectedElements);
        }

        internal void Redo()
        {
            var undoService = ExtendedItem.Services.GetService<UndoService>();
            if (undoService == null || !undoService.CanRedo) return;
            var action = undoService.RedoActions.First();
            undoService.Redo();
            ExtendedItem.Services.Selection.SetSelectedComponents(action.AffectedElements);
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.RightClickAndDefaultCommandsContextMenuExtensionTests --noresult`
Expected: PASS, 3 tests.

- [ ] **Step 6: Re-run full suites, then commit**

```bash
git add src/WinUIDesigner.Designer/Extensions/RightClickContextMenuExtension.cs src/WinUIDesigner.Designer/Extensions/DefaultCommandsContextMenuExtension.cs tests/WinUIDesigner.Designer.Tests/RightClickAndDefaultCommandsContextMenuExtensionTests.cs
git commit -m "feat: add RightClickContextMenuExtension and DefaultCommandsContextMenuExtension"
```

---

### Task 8: `ArrangeItemsContextMenuExtension`, `WrapItemContextMenuExtension`, `WrapItemsContextMenuExtension`, `UnwrapItemContextMenuExtension`

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/ArrangeItemsContextMenuExtension.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/WrapItemContextMenuExtension.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/WrapItemsContextMenuExtension.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/UnwrapItemContextMenuExtension.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/ArrangeWrapUnwrapContextMenuExtensionTests.cs`

**Interfaces:**
- Consumes: `DesignPanel.AddContextFlyoutItems`/`RemoveContextFlyoutItems`
  (Task 1), `PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer`
  (Task 2), `OnlyOneItemSelectedExtensionServer` (existing),
  `ModelTools.ArrangeItems` (Task 5), `ModelTools.WrapItemsNewContainer`/
  `UnwrapItemsFromContainer` (Task 6).
- Produces: the four extension classes named in Files, each `Order` per
  the spec's Architecture table (`ArrangeItemsContextMenuExtension` 30,
  `WrapItemContextMenuExtension` 50, `UnwrapItemContextMenuExtension` 51,
  `WrapItemsContextMenuExtension` 52).

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class ArrangeWrapUnwrapContextMenuExtensionTests
    {
        static (DesignSurface surface, DesignItem canvasItem, DesignItem aItem, DesignItem bItem) LoadTwoButtons(Microsoft.UI.Xaml.Window window)
        {
            const string xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" Canvas.Left=\"0\" Canvas.Top=\"0\" />" +
                "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" Canvas.Left=\"50\" Canvas.Top=\"30\" />" +
                "</Canvas>";
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
            var aItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[0]);
            var bItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[1]);
            return (surface, surface.DesignContext.RootItem, aItem, bItem);
        }

        [Test]
        public void ArrangeItemsExtensionOnlyAttachesWithTwoOrMoreSelected()
        {
            // DesignItem.Extensions (public IEnumerable<Extension>, DesignItem.cs:151) is this
            // port's real accessor for an item's currently-attached extensions - same one Task
            // 7's tests use.
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = LoadTwoButtons(window);

                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });
                Assert.IsFalse(aItem.Extensions.OfType<ArrangeItemsContextMenuExtension>().Any());

                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem, bItem });
                Assert.IsTrue(aItem.Extensions.OfType<ArrangeItemsContextMenuExtension>().Any());
            });
        }

        [Test]
        public void WrapItemExtensionOnlyAttachesWithExactlyOneSelected()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = LoadTwoButtons(window);

                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem, bItem });
                Assert.IsFalse(aItem.Extensions.OfType<WrapItemContextMenuExtension>().Any());

                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });
                Assert.IsTrue(aItem.Extensions.OfType<WrapItemContextMenuExtension>().Any());
            });
        }

        [Test]
        public void UnwrapItemExtensionAttachesToACanvasContainer()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, canvasItem, aItem, bItem) = LoadTwoButtons(window);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { canvasItem });

                Assert.IsTrue(canvasItem.Extensions.OfType<UnwrapItemContextMenuExtension>().Any());
            });
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.ArrangeWrapUnwrapContextMenuExtensionTests --noresult`
Expected: build FAILS — the four extension classes don't exist yet
(`CS0246`).

- [ ] **Step 3: Implement `ArrangeItemsContextMenuExtension`**

Create `src/WinUIDesigner.Designer/Extensions/ArrangeItemsContextMenuExtension.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Align Left/HCenter/Right/Top/VCenter/Bottom context menu for a multi-item
    /// selection. Ported from the reference's ArrangeItemsContextMenuExtension.cs.</summary>
    [ExtensionServer(typeof(PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.FrameworkElement))]
    public sealed class ArrangeItemsContextMenuExtension : DefaultExtension
    {
        DesignPanel panel;
        MenuFlyoutItemBase[] items;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            MenuFlyoutItem Make(string text, ArrangeDirection direction)
            {
                var item = new MenuFlyoutItem { Text = text };
                item.Click += (s, e) => ModelTools.ArrangeItems(ExtendedItem.Services.Selection.SelectedItems, direction);
                return item;
            }

            items = new MenuFlyoutItemBase[] {
                Make("Align Left", ArrangeDirection.Left),
                Make("Align Center", ArrangeDirection.HorizontalMiddle),
                Make("Align Right", ArrangeDirection.Right),
                Make("Align Top", ArrangeDirection.Top),
                Make("Align Middle", ArrangeDirection.VerticalMiddle),
                Make("Align Bottom", ArrangeDirection.Bottom),
            };

            panel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            panel?.AddContextFlyoutItems(items, 30);
        }

        protected override void OnRemove()
        {
            panel?.RemoveContextFlyoutItems(items);
            base.OnRemove();
        }
    }
}
```

- [ ] **Step 4: Implement `WrapItemContextMenuExtension`**

Create `src/WinUIDesigner.Designer/Extensions/WrapItemContextMenuExtension.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Wrap-in-Viewbox context menu for a single selected item. Ported from the
    /// reference's WrapItemContextMenuExtension.cs (its WindowClone exclusion check is dropped
    /// - WindowClone doesn't exist in this port, out of scope everywhere).</summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.FrameworkElement))]
    public sealed class WrapItemContextMenuExtension : DefaultExtension
    {
        DesignPanel panel;
        MenuFlyoutItemBase[] items;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            var wrapInViewbox = new MenuFlyoutItem { Text = "Wrap in Viewbox" };
            wrapInViewbox.Click += (s, e) =>
                ModelTools.WrapItemsNewContainer(ExtendedItem.Services.Selection.SelectedItems, typeof(Microsoft.UI.Xaml.Controls.Viewbox));
            items = new MenuFlyoutItemBase[] { wrapInViewbox };

            panel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            panel?.AddContextFlyoutItems(items, 50);
        }

        protected override void OnRemove()
        {
            panel?.RemoveContextFlyoutItems(items);
            base.OnRemove();
        }
    }
}
```

- [ ] **Step 5: Implement `WrapItemsContextMenuExtension`**

Create `src/WinUIDesigner.Designer/Extensions/WrapItemsContextMenuExtension.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Wrap-in-Canvas / Wrap-in-Grid context menu for a multi-item selection. Ported
    /// from the reference's WrapItemsContextMenuExtension.cs.</summary>
    [ExtensionServer(typeof(PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.FrameworkElement))]
    public sealed class WrapItemsContextMenuExtension : DefaultExtension
    {
        DesignPanel panel;
        MenuFlyoutItemBase[] items;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            var wrapInCanvas = new MenuFlyoutItem { Text = "Wrap in Canvas" };
            wrapInCanvas.Click += (s, e) =>
                ModelTools.WrapItemsNewContainer(ExtendedItem.Services.Selection.SelectedItems, typeof(Microsoft.UI.Xaml.Controls.Canvas));
            var wrapInGrid = new MenuFlyoutItem { Text = "Wrap in Grid" };
            wrapInGrid.Click += (s, e) =>
                ModelTools.WrapItemsNewContainer(ExtendedItem.Services.Selection.SelectedItems, typeof(Microsoft.UI.Xaml.Controls.Grid));
            items = new MenuFlyoutItemBase[] { wrapInCanvas, wrapInGrid };

            panel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            panel?.AddContextFlyoutItems(items, 52);
        }

        protected override void OnRemove()
        {
            panel?.RemoveContextFlyoutItems(items);
            base.OnRemove();
        }
    }
}
```

- [ ] **Step 6: Implement `UnwrapItemContextMenuExtension`**

Create `src/WinUIDesigner.Designer/Extensions/UnwrapItemContextMenuExtension.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Unwrap context menu for a single selected Grid or Canvas container. Ported
    /// from the reference's UnwrapItemContextMenuExtension.cs.</summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.Controls.Grid))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.Controls.Canvas))]
    public sealed class UnwrapItemContextMenuExtension : DefaultExtension
    {
        DesignPanel panel;
        MenuFlyoutItemBase[] items;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            var unwrap = new MenuFlyoutItem { Text = "Unwrap" };
            unwrap.Click += (s, e) => ModelTools.UnwrapItemsFromContainer(ExtendedItem.Services.Selection.PrimarySelection);
            items = new MenuFlyoutItemBase[] { unwrap };

            panel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            panel?.AddContextFlyoutItems(items, 51);
        }

        protected override void OnRemove()
        {
            panel?.RemoveContextFlyoutItems(items);
            base.OnRemove();
        }
    }
}
```

Stacking two `[ExtensionFor]` attributes on one class (applies to items
that are *either* type) is confirmed supported —
`ExtensionForAttribute` is declared `[AttributeUsage(AttributeTargets.
Class, AllowMultiple = true, ...)]`
(`src/WinUIDesigner.DesignModel/Extensions/ExtensionForAttribute.cs:26`),
and this port already has two working examples of exactly this pattern:
`DefaultPlacementBehavior.cs:32-35` (`Panel`/`Control`/`Border`/
`Viewbox`) and `PanelMove.cs:32-35` (`Panel`/`Border`/`ContentControl`/
`Viewbox`). The two-attribute form above is correct as written.

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.ArrangeWrapUnwrapContextMenuExtensionTests --noresult`
Expected: PASS, 3 tests.

- [ ] **Step 8: Re-run full suites, then commit**

```bash
git add src/WinUIDesigner.Designer/Extensions/ArrangeItemsContextMenuExtension.cs src/WinUIDesigner.Designer/Extensions/WrapItemContextMenuExtension.cs src/WinUIDesigner.Designer/Extensions/WrapItemsContextMenuExtension.cs src/WinUIDesigner.Designer/Extensions/UnwrapItemContextMenuExtension.cs tests/WinUIDesigner.Designer.Tests/ArrangeWrapUnwrapContextMenuExtensionTests.cs
git commit -m "feat: add ArrangeItems/WrapItem/WrapItems/UnwrapItem context menu extensions"
```

---

### Task 9: Definition-of-Done suite

**Files:**
- Create: `tests/WinUIDesigner.Designer.Tests/Phase4cDefinitionOfDoneTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-8.

- [ ] **Step 1: Write the Definition-of-Done tests**

One test per spec DoD bullet not already fully covered by an earlier
task's own tests. Most bullets are already covered (Tasks 1, 4, 5, 6, 7,
8 each test their own piece) — this file adds the cross-cutting
assertions the spec's DoD calls for that no single earlier task owns:

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class Phase4cDefinitionOfDoneTests
    {
        [Test]
        public void MultiParentDeleteCommitsAsExactlyOneUndoEntry()
        {
            const string xaml =
                "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Canvas x:Name=\"C1\"><Button x:Name=\"A\" Width=\"20\" Height=\"20\" /></Canvas>" +
                "<Canvas x:Name=\"C2\"><Button x:Name=\"B\" Width=\"20\" Height=\"20\" /></Canvas>" +
                "</StackPanel>";
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(xaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var root = (Microsoft.UI.Xaml.Controls.StackPanel)surface.DesignContext.RootItem.Component;
                var c1 = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
                var c2 = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[1];
                var aItem = surface.DesignContext.Services.Component.GetDesignItem(c1.Children[0]);
                var bItem = surface.DesignContext.Services.Component.GetDesignItem(c2.Children[0]);
                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                var undoCountBefore = undoService.UndoActions.Count();

                ModelTools.DeleteComponents(new[] { aItem, bItem });

                Assert.AreEqual(0, c1.Children.Count);
                Assert.AreEqual(0, c2.Children.Count);
                Assert.AreEqual(undoCountBefore + 1, undoService.UndoActions.Count(), "one delete spanning two parents must still be one undo entry");

                undoService.Undo();
                Assert.AreEqual(1, c1.Children.Count);
                Assert.AreEqual(1, c2.Children.Count);
            });
        }

        [Test]
        public void RemovingAnExtensionRemovesItsItemsFromTheMergedFlyout()
        {
            const string xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" />" +
                "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" />" +
                "</Canvas>";
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(xaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                var aItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[0]);
                var bItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[1]);

                // 2 selected - ArrangeItemsContextMenuExtension attaches, contributing its Order-30 group.
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem, bItem });
                var flyoutWithArrange = (MenuFlyout)surface.DesignPanel.ContextFlyout;
                Assert.IsTrue(flyoutWithArrange.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Align Left"));

                // Down to 1 selected - ArrangeItemsContextMenuExtension detaches.
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { aItem });
                var flyoutWithoutArrange = (MenuFlyout)surface.DesignPanel.ContextFlyout;
                Assert.IsFalse(flyoutWithoutArrange.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Align Left"));
            });
        }

        [Test]
        public void CanDeleteComponentsIsFalseForAnEmptySelection()
        {
            Assert.IsFalse(ModelTools.CanDeleteComponents(new DesignItem[0]));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails, then passes**

Run: `dotnet build WinUIDesigner.sln` then
`tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.Phase4cDefinitionOfDoneTests --noresult`
Expected: since Tasks 1-8 are already implemented by this point, these
tests should compile immediately — if `RemovingAnExtensionRemovesItsItemsFromTheMergedFlyout`
fails on the second assertion (the Order-30 group's items still present
after detach), the bug is in `UpdateContextFlyout` not being called
somewhere the selection-driven extension attach/detach path expects —
check that `OnlyOneItemSelectedExtensionServer`/
`PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer`'s
`ReapplyExtensions` call actually triggers `OnRemove` on the extension
being detached (established Phase 3 machinery, should already work) —
this is a real assertion about production behavior, not a placeholder to
skip if it initially fails.

- [ ] **Step 3: Run every `.exe` suite one final time**

```
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```

Expected: all green, zero regressions across every prior phase's tests.

- [ ] **Step 4: Commit**

```bash
git add tests/WinUIDesigner.Designer.Tests/Phase4cDefinitionOfDoneTests.cs
git commit -m "test: add Phase 4c Definition-of-Done suite"
```
