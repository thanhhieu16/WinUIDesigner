# Phase 4g: OutlineView — Design Spec

## Context

Phase 2's spec (`docs/superpowers/specs/2026-09-01-winui-designmodel-phase2-design.md:346`) named "property grid, metadata, and outline view" as one deferred Phase 4 item. That item was decomposed into 4e (Metadata, done) → 4f (PropertyGrid, done) → **4g (OutlineView, this spec)**, the last piece of Phase 4. Phase 4f's own "Out of scope" section (`docs/superpowers/specs/2026-09-05-winui-designer-phase4f-design.md:109`) names OutlineView explicitly as 4g.

Phase 4g builds the design-time document-tree panel: a control that renders the `DesignItem` hierarchy of the loaded document as a tree, syncs selection with `ISelectionService` in both directions, lets the user hide/lock individual elements, filter the tree by name, and reparent/reorder elements by dragging.

Repo state: confirmed greenfield. Zero matches for `IOutlineNode`/`OutlineNode`/`OutlineView` anywhere in `src/` or `tests/` (the only `src/` hit for the substring "outline" is an unrelated visual-styling comment in `src/WinUIDesigner.Designer/Controls/SelectionHighlight.cs`).

## Reference source

Engine layer (maps to `WinUIDesigner.DesignModel`):
- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign\Project\Interfaces\IOutlineNode.cs` (38 lines) — the interface.
- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign\Project\Services.cs:226-250` — `IOutlineNodeService` (`Create(DesignItem) : IOutlineNode`, returns cached instance) and `IOutlineNodeNameService` (`GetOutlineNodeName(DesignItem) : string`).
- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign\Project\DesignItem.cs:361` — `public IOutlineNode CreateOutlineNode() => Services.GetRequiredService<IOutlineNodeService>().Create(this);`

UI layer (maps to `WinUIDesigner.Designer`), all under `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\OutlineView\`:
- `OutlineNodeBase.cs` (271 lines) — abstract base implementing `IOutlineNode` + `INotifyPropertyChanged`.
- `OutlineNode.cs` (154 lines) — concrete node + the nested `internal class OutlineNodeService : IOutlineNodeService, IDisposable`.
- `PropertyOutlineNode.cs` (45 lines) — node standing in for a property rather than an element.
- `OutlineNodeNameService.cs` (44 lines).
- `DragTreeView.cs` (357) + `DragTreeViewItem.cs` (159) + `OutlineTreeView.cs` (67) + `IconItem.cs` (49) + `Outline.xaml` (20) + `OutlineView.xaml` (229) + `Outline.xaml.cs` (82) — the WPF drag-drop tree UI. Mostly **not ported**, see Decision 3.
- `DragListener.cs` (72) — already ported in an earlier phase as `src/WinUIDesigner.Designer/Controls/DragListener.cs`.

Reference tests (the basis for this phase's tests) at `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Tests\Designer\OutlineView\`: `HierarchyTests.cs`, `SelectionTests.cs`, `InsertTests.cs`.

Reference registers both services in `WpfDesign.Designer\Project\Xaml\XamlDesignContext.cs:77-78`. Reference enforces the design-time lock in `WpfDesign.Designer\Project\DesignPanel.cs:103`. Reference's `ModelTools.CanSelectComponent` is `WpfDesign.Designer\Project\ModelTools.cs:91-94`, whose whole body is `return item.View != null;`.

All file:line citations in this spec were read directly from the named files during this spec's own writing.

## Verified against the current codebase

Every API the port needs already exists unless noted:

- **`DesignItem`** (`src/WinUIDesigner.DesignModel/DesignItem.cs`): `ComponentType` (:51), `View` (:56), `Parent` (:72), `Properties` (:87), `AllSetProperties` (:92), `Name` (:97), `NameChanged` (:107), `Services` (:112), `OpenGroup(string)` (:123), `GetBehavior<T>()` (:299), `PropertyChanged` (:310, `DesignItem : INotifyPropertyChanged` at :34), `ContentPropertyName` (:325), `ContentProperty` (:330), `Remove()` (:340), `Clone()` (:355). **Missing**: `CreateOutlineNode()` — this phase adds it (Decision 1).
- **`DesignItemProperty`** (`src/WinUIDesigner.DesignModel/DesignItemProperty.cs`): `Name` (:29), `IsCollection` (:38), `CollectionElements` (:40, typed `IObservableList<DesignItem>`), `Value` (:41, typed `DesignItem` — exactly what `OutlineNode.UpdateChildren` needs), `DesignItem` back-reference (:52), `GetConvertedValueOnInstance<T>()` (:51). `IObservableList<T> : IList<T>, INotifyCollectionChanged` (`src/WinUIDesigner.DesignModel/Interfaces/IObservableList.cs:30`) — so the reference's `CollectionElements.CollectionChanged` subscription ports unchanged.
- **`DesignItemPropertyCollection.GetAttachedProperty(Type ownerType, string name)`** (`DesignItemProperty.cs:95`) — note the signature differs from the reference's `GetAttachedProperty(DependencyProperty)`; the port takes owner type + name. Existing callers already use the port form (`src/WinUIDesigner.DesignModel/Xaml/XamlDesignItem.cs:219`).
- **`ServiceContainer`** (`src/WinUIDesigner.DesignModel/ServiceContainer.cs`): `AddService` (:52), `GetService<T>()` (:112), `GetRequiredService<T>()` (:143), `Selection` (:176).
- **`ISelectionService`** (`src/WinUIDesigner.DesignModel/Services.cs:62-76`): `SelectionChanged` (event, `DesignItemCollectionEventArgs`), `IsComponentSelected`, `SetSelectedComponents(ICollection<DesignItem>, SelectionTypes)`. `SelectionTypes` (`Services.cs:51-60`) has `Add = 8` and `Remove = 0x10` — the two values `OutlineNodeBase.IsSelected`'s setter uses.
- **`DesignContext.OpenGroup(string changeGroupTitle, ICollection<DesignItem> affectedItems)`** (`src/WinUIDesigner.DesignModel/DesignContext.cs:64`) — the reference's `Insert` passes a `List<DesignItem>`, which satisfies `ICollection<DesignItem>`.
- **`PlacementOperation.Start(ICollection<DesignItem>, PlacementType)`** (`src/WinUIDesigner.DesignModel/PlacementOperation.cs:40`), `Abort()` (:246), **`IPlacementBehavior.CanEnterContainer(PlacementOperation, bool shouldAlwaysEnter)`** (`src/WinUIDesigner.DesignModel/PlacementBehavior.cs:24`), **`PlacementType.Register(string)`** (`src/WinUIDesigner.DesignModel/PlacementType.cs:19`) — the whole `CanInsert` mechanism ports unchanged.
- **`DesignTimeProperties`** (`src/WinUIDesigner.XamlDom/DesignTimeProperties.cs`): `IsHiddenProperty` (:51) with an `OnIsHiddenPropertyChanged` callback (:54-62) that already forces the element's `Visibility` when set and clears it when unset — **hide enforcement therefore needs no new code in this phase**. `IsLocked` is parsed by `src/WinUIDesigner.XamlDom/XamlParser.cs:668`.
- **`XamlDesignItem.IsDesignTimeLocked`** (`src/WinUIDesigner.DesignModel/Xaml/XamlDesignItem.cs:217-227`) already reads/writes the `IsLocked` attached property. Repo-wide grep confirms it currently has **exactly one occurrence — its own definition**: nothing in the port reads it. Lock enforcement is new work this phase (Decision 8).
- **`XamlDesignItem.Clone()`** (`src/WinUIDesigner.DesignModel/Xaml/XamlDesignItem.cs:230-240`) is a real implementation (XAML round-trip via `XamlStaticTools.GetXaml` + `XamlParser.ParseSnippet` + `RegisterXamlComponentRecursive`). The reference's own copy-mode `Insert` tests are commented out with "Cloning DesignItem is not yet supported" (`InsertTests.cs:113`); in this port copy-mode **is** testable.
- **`ModelTools`** (`src/WinUIDesigner.Designer/ModelTools.cs:20-471`) has no `CanSelectComponent`. This phase adds it (Decision 2).
- **`DesignSurface.LoadDesigner`** (`src/WinUIDesigner.Designer/DesignSurface.cs:29-45`) is where `IDesignPanel`, `ISelectionService`, `OptionService`, `ICopyPasteService` and `IToolService` are registered via `loadSettings.CustomServiceRegisterFunctions`, with an existing in-file comment explaining exactly why services owned by `WinUIDesigner.Designer` cannot be registered inside `XamlDesignContext`'s constructor the way the reference does. The two new outline services follow that same established pattern (Decision 9).
- **`DesignPanel`'s content hit-test pass** (`src/WinUIDesigner.Designer/DesignPanel.cs:214-232`): for each element from `VisualTreeHelper.FindElementsInHostCoordinates`, it walks up the visual tree until `Context.Services.Component.GetDesignItem(el, true)` returns non-null, then reports that as `ModelHit`. This is the loop Decision 8 modifies.
- **Zero `.xaml` files exist in `src/`**, as established and re-verified in Phase 4f. Every visual in this codebase is built by C# object construction. `src/WinUIDesigner.Designer/PropertyGrid/PropertyGridControl.cs:158-190` is the concrete precedent this phase's rows follow: `new TextBlock{...}`, `new ToggleButton{...}`, `new Grid{...}` with `ColumnDefinitions` added in code and indentation applied via `Margin = new Thickness(level * 12, 0, 0, 0)`.
- **`Microsoft.UI.Xaml.Markup.XamlReader.Load(string)` is already used in `src/`** (`src/WinUIDesigner.XamlDom/TemplateHelper.cs:102`), so an inline-XAML `DataTemplate` would not be unprecedented — it is nonetheless rejected here in favour of C#-built rows (Decision 4).
- **WinUI TreeView surface, probed empirically** against `Microsoft.UI.Xaml.winmd` (the copy at `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Microsoft.UI.Xaml.winmd`, byte-scanned for each name): `TreeView`, `TreeViewNode`, `TreeViewItem`, `TreeViewList`, `RootNodes`, `SelectedNode`, `SelectedNodes`, `SelectionMode`/`TreeViewSelectionMode`, `ItemTemplateSelector`, `ItemContainerStyle`, `ContainerFromItem`/`ItemFromContainer`, `CanReorderItems`, `CanDragItems`, `AllowDrop`, `DragItemsStarting`, `DragItemsCompleted`, `TreeViewDragItemsStartingEventArgs`, `FontIcon`, `SymbolIcon`, `ToggleButton`, `AutoSuggestBox` **all present**. This is the single biggest divergence from the reference: WPF's `TreeView` has none of the drag-reorder surface, which is the entire reason the reference wrote `DragTreeView`/`DragTreeViewItem` (516 lines). A name-presence probe proves the members exist in metadata; it does **not** prove runtime behaviour — Task 1 must confirm behaviour empirically (Decision 5).
- **Test fixture pattern** for a live design surface is established at `tests/WinUIDesigner.Designer.Tests/PropertyGridControlTests.cs:28-37`: `WinUITestHost.RunWithWindow(window => ...)`, `new DesignSurface()`, `window.Content = surface`, `surface.LoadDesigner(XmlReader.Create(new StringReader(xaml)), new XamlLoadSettings())`, then `surface.DesignContext.RootItem` / `Services.Component.GetDesignItem(...)`. This phase's tests reuse it verbatim; the port has no `ModelTestHelper` equivalent to the reference's, and this phase does not add one.

## Decisions already made

1. **`IOutlineNode` lives in `WinUIDesigner.DesignModel`, implementations in `WinUIDesigner.Designer`** — mirroring the reference's own split (interface in `WpfDesign`, classes in `WpfDesign.Designer`) and Phase 4f's two-project split. Concretely: `src/WinUIDesigner.DesignModel/Interfaces/IOutlineNode.cs` (new), the two service interfaces appended to `src/WinUIDesigner.DesignModel/Services.cs`, and `DesignItem.CreateOutlineNode()` added to `src/WinUIDesigner.DesignModel/DesignItem.cs` as a concrete (non-abstract) one-liner delegating to `Services.GetRequiredService<IOutlineNodeService>()`, exactly as the reference does at `DesignItem.cs:361`. `WinUIDesigner.DesignModel` must not reference `WinUIDesigner.Designer` (existing `ProjectReference` direction, unchanged).

2. **`ModelTools.CanSelectComponent(DesignItem item) => item.View != null`** added to `src/WinUIDesigner.Designer/ModelTools.cs`, ported verbatim from the reference (`ModelTools.cs:91-94`). `OutlineNode.UpdateChildrenCore` is its only caller this phase; it is what makes a non-visual collection element (which has no `View`) transparently contribute its own content children to the tree instead of appearing as a node.

3. **`DragTreeView`, `DragTreeViewItem`, `OutlineTreeView`, `IconItem`, `Outline.xaml(.cs)`, `OutlineView.xaml` are not ported.** WinUI 3's own `TreeView` supplies expand/collapse, virtualization, multi-select and drag-reorder — the reference wrote 516 lines of `TreeView` subclassing precisely because WPF's does not. Concretely dropped along with them: the 3-zone (top/middle/bottom-third of the header) drop hit-testing, the `PART_InsertLine` `Border` insert marker, `IconItem` and its `Eye.png`/`EyeClose.png` image resources (this port ships no image resources at all — `FontIcon` glyphs are used instead), and `Outline.xaml.cs`'s Undo/Redo/Cut/Copy/Paste/Delete/SelectAll command handlers (this port's `DesignSurface` exposes none of those commands — see Out of scope).

4. **Rows are built in C#, not from a `DataTemplate`.** The tree is a `Microsoft.UI.Xaml.Controls.TreeView` populated through `RootNodes`, and each `TreeViewNode.Content` is set to a fully-constructed `Grid` row (`FontIcon`-bearing eye `ToggleButton` | lock `ToggleButton` | name `TextBlock`) built the same way `PropertyGridControl.BuildPropertyRow` builds its rows (`PropertyGridControl.cs:175-190`). No `.xaml` file, no `XamlReader.Load`-ed `DataTemplate`, no `{Binding}` — consistent with the codebase-wide "zero `.xaml` in `src/`" convention. A `Dictionary<TreeViewNode, IOutlineNode>` (plus its inverse `Dictionary<IOutlineNode, TreeViewNode>`) is the mapping between the two trees; the control owns both. **`TreeViewItem` rendering a `UIElement`-valued `Content` correctly is an assumption to confirm in Task 1**, not a verified fact — `TreeViewItem` derives from `ListViewItem` → `ContentControl`, so a `ContentPresenter` should render it directly, but this codebase's discipline is to confirm rather than assume.

5. **Drag-drop: primary design plus a named fallback, chosen by an empirical probe in Task 1.**
   - **Primary**: `CanDragItems = true`, `CanReorderItems = true`, `AllowDrop = true`. `DragOver` vetoes (`e.AcceptedOperation = DataPackageOperation.None`) when the resolved target node's `IOutlineNode.CanInsert(draggedNodes, after, copy)` is false. `DragItemsCompleted` reads the moved `TreeViewNode`'s new parent and index, maps both back to `IOutlineNode`s, and applies the model change via `IOutlineNode.Insert(nodes, after, copy)`, with a `bool` re-entrancy guard suppressing the control's own model-changed rebuild for the duration (the guard-flag pattern already used by this codebase's PropertyGrid editors, commit `b5eb9c1`).
   - **Fallback, used only if the Task 1 probe shows `DragItemsCompleted` cannot reliably report the new parent and index**: `CanReorderItems = false`, with `DragOver`/`Drop` handled directly on the `TreeView`; dropping onto a row inserts the dragged items into that row's node at the end of its children (`after` = its current last child). This keeps reparenting and loses precise ordering within a parent.
   - The decision criterion is exactly that: whether the new parent and index are readable after the built-in reorder. Task 1 records which branch it took and why; every later task builds on that recorded outcome.
   - Copy-mode (`copy: true`, the reference's Ctrl-drag) is wired through in both branches — the port's `Clone()` is real (see Verified section), so it is testable here even though the reference's own copy tests are commented out.

6. **Multi-select**: `TreeView.SelectionMode = TreeViewSelectionMode.Multiple`. Selection syncs both ways with a setter guard, ported from the reference: `OutlineNodeBase.IsSelected`'s setter no-ops unless the value actually changed (`OutlineNodeBase.cs:134`), which is what stops the canvas→outline→canvas feedback loop. Outline→canvas goes through `SelectionService.SetSelectedComponents(new[]{DesignItem}, value ? SelectionTypes.Add : SelectionTypes.Remove)`; canvas→outline goes through `OutlineNode`'s `SelectionService.SelectionChanged` handler setting `IsSelected = Selection.IsComponentSelected(DesignItem)` (`OutlineNode.cs:46-49`), which the control observes via `INotifyPropertyChanged` to update `TreeView.SelectedNodes`.

7. **All four per-node features are in scope**: hide toggle, lock toggle, name filter, and `PropertyOutlineNode` for complex (non-content) properties.
   - Hide: `IsDesignTimeVisible = false` → `Properties.GetAttachedProperty(typeof(DesignTimeProperties), "IsHidden").SetValue(true)`; `true` → `.Reset()`. Enforcement is already automatic (`DesignTimeProperties.cs:54-62`).
   - `PropertyOutlineNode`: for every `DesignItem.AllSetProperties` entry that is not the content property and whose `Value` is non-null, a name-only intermediate node is inserted whose single child is the value's own outline node (`OutlineNode.cs:55-65`).

8. **Lock enforcement is wired into `DesignPanel`'s hit test this phase** — without it the lock toggle would write a property nothing reads. In `DesignPanel.cs`'s content pass (:214-232), after `modelHit` is resolved, walk `modelHit`'s `DesignItem.Parent` chain (including `modelHit` itself); if any item in that chain is a `XamlDesignItem` whose `IsDesignTimeLocked` is true, skip this element entirely and continue with the next candidate from `FindElementsInHostCoordinates`. Walking the whole ancestor chain (rather than testing only `modelHit`) is what reproduces the reference's `ContinueSkipSelfAndChildren` semantics — a locked container's children must be unselectable too, not just the container. The check casts (`modelHit as XamlDesignItem`) because `DesignItem`'s abstract base carries no lock member; the reference casts identically in `OutlineNodeBase.cs:175`. **No lock-related member is added to the `DesignItem` base class** — the cast is the whole mechanism. (`CreateOutlineNode()` from Decision 1 is the only member this phase adds to that base.)

9. **Both new services are registered in `DesignSurface.LoadDesigner`** via `loadSettings.CustomServiceRegisterFunctions`, alongside `IDesignPanel`/`ISelectionService`/`OptionService`/`ICopyPasteService`/`IToolService` (`DesignSurface.cs:33-42`) — not in `XamlDesignContext`'s constructor where the reference puts them (`XamlDesignContext.cs:77-78`), because both implementations live in `WinUIDesigner.Designer` and `XamlDesignContext` lives in `WinUIDesigner.DesignModel`, which must not reference it. The existing in-file comment at `DesignSurface.cs:35-40` already documents this exact constraint for `OptionService`.

10. **`OutlineNodeService`'s `Dictionary<DesignItem, IOutlineNode>` cache is per-`DesignContext`**, since registration is per-`LoadDesigner` call. It therefore dies with the document and needs no cross-document invalidation — unlike `PropertyGridControl`'s node cache, which Phase 4f flagged as a leak across document swaps. `IDisposable` is implemented (clearing the dictionary) to match the reference, but nothing in this phase calls `Dispose`.

11. **`DummyPlacementType` is registered exactly once**, in `OutlineNodeBase`'s static constructor. The reference calls `PlacementType.Register("DummyPlacement")` in the static constructors of **both** `OutlineNode` (`OutlineNode.cs:40`) and `PropertyOutlineNode` (`PropertyOutlineNode.cs:37`), which — since `Register` just constructs a new instance (`PlacementType.cs:19`) — produces two distinct `PlacementType` objects with the same name and lets whichever type initialises last win the shared `protected static` field. Harmless in the reference but pointless; the port registers it once at the base.

12. **Filter rebuilds the node tree rather than hiding rows.** `OutlineViewControl.Filter` (a settable `string`) triggers a rebuild of `TreeView.RootNodes` including only nodes whose `Name` contains the filter text (case-insensitive) **or** which have a descendant that does. The reference instead sets per-item `Visibility` through WPF's container generator (`DragTreeView.ShouldItemBeVisible` + `OutlineTreeView`'s override); WinUI's `TreeView` has no equivalent per-node visibility hook, and a `TreeViewNode` cannot be hidden in place. Empty or null filter shows the whole tree.

13. **The control exposes `Root` as a plain settable property**, not bound to any document/selection service internally — the same host-less design Phase 4f chose for `PropertyGridControl.SelectedItems` (Phase 4f Decision 16), for the same reason: no shell app exists until Phase 5, so tests set `Root` directly. `OutlineViewControl` is named with the `Control` suffix to match `PropertyGridControl` and to leave the reference's bare `Outline`/`OutlineView` names free.

14. **`CanInsert` is ported without a cycle guard**, matching the reference. Reference `InsertTests.CanInsertIntoStackPanel` (`InsertTests.cs:63-67`) asserts that inserting the root `Grid` into its own descendant `StackPanel` returns **true** — the reference has no ancestor check, and reproducing that faithfully keeps the ported tests meaningful. Recorded as a known gap rather than silently fixed.

15. **`IOutlineNode.IsDesignTimeLocked` is declared `{ get; set; }`, deviating from the reference's get-only `{ get; }`** (`IOutlineNode.cs:32`). The reference gets away with get-only because its XAML two-way-binds the lock toggle to the *concrete* `OutlineNodeBase` through `DataContext`; this port's control holds nodes only as `IOutlineNode` (Decision 4's dictionaries), so a get-only member would force a downcast to `OutlineNodeBase` on every toggle. `IsDesignTimeVisible` is already `{ get; set; }` in the reference, so this makes the two lock/hide members symmetric.

## Architecture

```
src/WinUIDesigner.DesignModel/
    Interfaces/IOutlineNode.cs   NEW    — SelectionService, Services, DesignItem, IsExpanded, IsSelected,
                                          IsDesignTimeVisible, IsDesignTimeLocked (both settable —
                                          Decision 15), Name, Children (ObservableCollection<IOutlineNode>),
                                          CanInsert(...), Insert(...).
    Services.cs                  MODIFY — + IOutlineNodeService, IOutlineNodeNameService.
    DesignItem.cs                MODIFY — + CreateOutlineNode().

src/WinUIDesigner.Designer/OutlineView/
    OutlineNodeBase.cs           NEW    — abstract; INotifyPropertyChanged + IOutlineNode. Owns the
                                          hidden/locked initial read, NameChanged/PropertyChanged/
                                          CollectionChanged subscriptions, IsSelected's guarded setter,
                                          CanInsert/Insert, and the single DummyPlacementType registration.
    OutlineNode.cs               NEW    — UpdateChildren (property nodes + content children),
                                          UpdateChildrenCollectionChanged (incremental add/remove),
                                          UpdateChildrenCore (CanSelectComponent-based transparency),
                                          + internal OutlineNodeService : IOutlineNodeService, IDisposable.
    PropertyOutlineNode.cs       NEW    — name-only node for one DesignItemProperty.
    OutlineNodeNameService.cs    NEW    — "Button" or "Button (myName)".
    OutlineViewControl.cs        NEW    — Control. Owns the TreeView, the filter box, both node maps,
                                          row construction, selection sync and drag wiring.

src/WinUIDesigner.Designer/ModelTools.cs    MODIFY — + CanSelectComponent.
src/WinUIDesigner.Designer/DesignPanel.cs   MODIFY — lock-aware content hit test (Decision 8).
src/WinUIDesigner.Designer/DesignSurface.cs MODIFY — register the two new services (Decision 9).
```

Dependency direction is unchanged: `Designer` → `DesignModel` → `XamlDom`.

## Data flow

1. Host (a test, or Phase 5's shell) sets `outlineViewControl.Root = designContext.RootItem.CreateOutlineNode()`.
2. `IOutlineNodeService.Create` returns a cached `OutlineNode` per `DesignItem`; constructing one runs `UpdateChildren`, which recursively creates child nodes through the same service, so the whole tree materialises eagerly from the root.
3. `UpdateChildren` builds children in two groups: first, one `PropertyOutlineNode` per non-content set property with a non-null `Value` (its single child being that value's node); then the content property — its `CollectionElements` if it is a collection, otherwise its single `Value`.
4. `UpdateChildrenCore` filters each candidate through `ModelTools.CanSelectComponent`: an item with a `View` becomes a child node; an item without one contributes *its* content children directly instead, so non-visual wrappers do not appear in the tree.
5. `OutlineViewControl` walks `Root` and mirrors it into `TreeViewNode`s, each with a C#-built row as `Content` (Decision 4), recording both directions in its dictionaries.
6. Model changes flow in through `DesignItem.PropertyChanged` (full `UpdateChildren`) and `ContentProperty.CollectionElements.CollectionChanged` (incremental add/remove). `OutlineNodeBase`'s `_collectionWasChanged` flag suppresses the redundant full rebuild that the property-changed event fires immediately after a collection change (`OutlineNodeBase.cs:203-215`). The control observes `Children.CollectionChanged` and `INotifyPropertyChanged` on each node and updates the affected `TreeViewNode` subtree.
7. Selection, hide, lock, filter and drag behave as described in Decisions 5-8 and 12.
8. Every model mutation (`Insert`, hide, lock) routes through the existing undo machinery — `Insert` opens one `ChangeGroup` via `DesignItem.Context.OpenGroup("Item moved in outline view", ...)` so a whole drag commits as a single undo entry; hide/lock go through `DesignItemProperty.SetValue`/`Reset`, which already route through `UndoService` (established in Phase 4f).

## Testing / DoD

New: `tests/WinUIDesigner.Designer.Tests/OutlineViewTests.cs`, using the `WinUITestHost.RunWithWindow` + `DesignSurface.LoadDesigner` fixture pattern from `PropertyGridControlTests.cs:28-37`. The reference's three test files are the content basis; the fixture XAML they use (`<Grid><Button/><StackPanel><Button/></StackPanel></Grid>`) ports directly.

Coverage required:
- **Hierarchy** (from `HierarchyTests.cs`): a root `Grid` containing a `Button` and a `StackPanel` containing a `Button` yields 2 root children; the `StackPanel` node has 1 child; each node's `DesignItem` is the expected `ContentProperty.CollectionElements[i]`.
- **Property nodes**: an element with a set non-content property whose value is a tracked sub-object produces a `PropertyOutlineNode` named after the property, with the value's node as its single child.
- **Name service**: `"Button"` without `x:Name`, `"Button (A)"` with `x:Name="A"`.
- **Selection, canvas→outline** (from `SelectionTests.cs`): selecting the `StackPanel` and its `Button` through `ISelectionService` leaves the root node and the first `Button`'s node unselected and both of the others selected.
- **Selection, outline→canvas** (from `SelectionTests.cs`): clearing the selection then setting `IsSelected = true` on two nodes puts both corresponding `DesignItem`s into `SelectionService.SelectedItems`.
- **Hide**: toggling `IsDesignTimeVisible` false then true sets and then resets the `IsHidden` attached property, and the element's live `Visibility` follows.
- **Lock**: toggling `IsDesignTimeLocked` true writes the `IsLocked` attached property, and `DesignPanel.HitTest` at a point over the locked element no longer resolves to it — nor to a child of it (the ancestor-chain requirement of Decision 8 gets its own case).
- **CanInsert** (from `InsertTests.cs`): true for a `Grid` target; false for a target with no `IPlacementBehavior`; and the reference's `CanInsert(root into its own descendant) == true` case, pinning Decision 14's deliberate absence of a cycle guard.
- **Insert, cut mode** (from `InsertTests.cs`): moving the `StackPanel`'s `Button` after the `Grid`'s first `Button` leaves the `Grid` with 3 children in the expected order, the `StackPanel` with 0, both in the outline and in `ContentProperty.CollectionElements`; `UndoService` shows exactly one new undoable entry for the whole operation.
- **Insert, copy mode**: same move with `copy: true` leaves the source in place and adds a clone (testable here, unlike in the reference — see Verified section).
- **Collection change**: adding and removing an element from `ContentProperty.CollectionElements` adds/removes exactly one child node without a full rebuild losing node identity.
- **Filter**: a filter matching one leaf keeps that leaf and its ancestors and drops the rest; an empty filter restores the full tree.
- **Drag**: whichever branch of Decision 5 Task 1 selects, a test drives the model-level outcome of a drop (target/after/copy → `Insert`) rather than a synthesised pointer gesture, matching how `DragListenerTests`/`MoveTests` already test drag-driven behaviour in this suite.

All three suites must stay green, each run via its built `.exe` with `--noresult` — never `dotnet test`, which reports WinUI-dependent tests as ignored rather than run.

Roughly 6 tasks fit this scope: (1) the WinUI `TreeView` behaviour probe of Decision 5 plus the `UIElement`-as-`Content` confirmation of Decision 4, recording the chosen branch; (2) `IOutlineNode` + the two service interfaces + `DesignItem.CreateOutlineNode` + `ModelTools.CanSelectComponent`; (3) `OutlineNodeBase` + `OutlineNodeNameService` + `OutlineNodeService` registration; (4) `OutlineNode` + `PropertyOutlineNode` (tree building, collection changes, selection sync); (5) `OutlineViewControl` (TreeView mirroring, C# rows, hide/lock toggles, filter); (6) drag wiring per Task 1's branch + `DesignPanel` lock enforcement. `writing-plans` should confirm or adjust this decomposition against the coverage list above rather than treat it as final.

## Known gaps

- **The WinUI `TreeView` probe is metadata-only so far.** Names were confirmed present in `Microsoft.UI.Xaml.winmd`; runtime behaviour of `CanReorderItems`, `DragItemsCompleted`'s reported parent/index, and `TreeViewItem` rendering a `UIElement` `Content` are all confirmed only by Task 1. If Task 1 takes Decision 5's fallback branch, precise ordering within a parent is lost and that becomes a real gap for a later phase.
- **No cycle guard in `CanInsert`** (Decision 14): the outline will report that a container can be inserted into its own descendant. Whether the subsequent `Insert` corrupts the tree or is caught downstream by `PlacementOperation` is not established by this phase's tests.
- **No per-control-type icons.** `IconItem` and the reference's icon resources are dropped (Decision 3); rows carry only the eye and lock glyphs plus the name.
- **No `ScrollIntoView` on selection.** The reference scrolls the design surface to the newly selected element (`OutlineTreeView.SelectOnly`); this port's `DesignSurface` (`src/WinUIDesigner.Designer/DesignSurface.cs`) exposes no `ScrollIntoView`, so selecting a node scrolls nothing.
- **No editing commands.** `Outline.xaml.cs` binds Undo/Redo/Cut/Copy/Paste/Delete/SelectAll to `DesignSurface` methods that do not exist in this port (`DesignSurface`'s entire public surface is `DesignPanel`, `DesignContext`, `LoadDesigner`, `UnloadDesigner`, `SaveDesigner`). Deferred to Phase 5 along with the shell.
- **Filter rebuilds the whole node tree** (Decision 12). Correct but O(n) per keystroke, and it discards `TreeViewNode` expand state for filtered-out branches. Acceptable at document sizes this designer handles; a real cost if a later phase targets large documents.
- **`OutlineNodeBase` never unsubscribes.** Nodes subscribe to `DesignItem.NameChanged`, `DesignItem.PropertyChanged`, `CollectionElements.CollectionChanged` and `ISelectionService.SelectionChanged` and never detach — the reference has the identical omission. Bounded here by Decision 10 (the cache and every node in it die with the `DesignContext`), but a node for a `DesignItem` that is removed from the document mid-session stays alive and subscribed until the document is unloaded.
- **`UpdateChildren` clears and rebuilds `Children` wholesale** on any non-collection property change, discarding child node objects that the control's `TreeViewNode` maps still reference until the control processes the collection-changed event. Same shape as the Phase 4f gap recorded for `PropertyNode.UpdateChildren`.

## Out of scope

Per-control-type icons and any image resources; the insert-line/3-zone drop UX; `DragTreeView`/`DragTreeViewItem`/`OutlineTreeView`/`IconItem`; editing commands (Undo/Redo/Cut/Copy/Paste/Delete/SelectAll) and their `DesignSurface` backing; `ScrollIntoView`; shell-app wiring (Phase 5); inline rename of a node; the XamlDom serialization gap recorded as a blocking prerequisite for Phase 5's Save feature (unrelated to this phase, must not be picked up here).
