# Phase 4c: Context menus — WinUIDesigner.Designer

## Context

Phase 4b delivered container-specific placement (Canvas/Grid/StackPanel),
snaplines, and drag-move (including cross-container reparenting), and
merged to `main` (commit `d1a6c61`). Per the five-sub-phase decomposition
its own spec recorded
(`docs/superpowers/specs/2026-09-01-winui-designer-phase4a-design.md`,
lines 18-25), 4c is next: **context menus**.

Reference source, all under
`F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\Extensions\`
— 9 distinct context-menu extensions, each a `.xaml` (menu-item layout),
`.xaml.cs` (code-behind, click handlers), and `Extension.cs`
(`[ExtensionServer]`/`[ExtensionFor]` attachment) triple:
`RightClickContextMenu`, `TextBlockRightClickContextMenu`,
`DefaultCommandsContextMenu`, `ArrangeItemsContextMenu`,
`EditStyleContextMenu`, `PathContextMenu`, `UnwrapItemContextMenu`,
`WrapItemContextMenu`, `WrapItemsContextMenu`.

## Revision history

Brainstormed 2026-09-02. First scope question: port all 9 menus
(including real clipboard integration, style-resource extraction, a
geometry-conversion command, and a dialog-based text editor) or narrow to
the mechanically portable subset — answered **narrow**, same kind of cut
4a (single item, generic container only) and 4b (single-item move, no
clipboard/paste) already made.

A background research pass read all 27 reference files in full, plus the
reference's `DesignPanel` context-menu merge mechanism, plus this port's
current `DesignPanel`/`ModelTools`/service surface, plus verified the
relevant WinUI API against the pinned SDK (`Microsoft.WindowsAppSDK
1.7.250606001`) two ways — SDK XML docs and a throwaway compiler probe
(`src/WinUIDesigner.Designer/__Probe4c.cs`, built clean, then deleted,
`git status` confirmed no residue). It found the menu count is 9, not 8;
that 2 of the 9 need an `ExtensionServer` not yet ported; and that
`MenuFlyout`/`MenuFlyoutItem`/`MenuFlyoutSubItem`/`MenuFlyoutSeparator`/
`UIElement.ContextFlyout` are all compiler-confirmed present.

Follow-up reading (writing this document) found two things the initial
chat-level design got wrong or missed:

1. **`Delete` is not a bare `ModelTools` call — it routes through
   `ICopyPasteService.Delete()`/`CanDelete()`**, the same interface
   `Cut`/`Copy`/`Paste` use (`WpfDesign.Designer\Project\Services\
   CopyPasteService.cs:50-65`). The initial design treated Delete as
   "mechanical" and separate from the clipboard service; it isn't. Scope
   is narrowed accordingly: this phase adds one concrete
   `CopyPasteService` with only `Delete`/`CanDelete` implemented for
   real (see Decisions).
2. **The reference's `DesignSurface.cs` `RoutedCommand` hub
   (`AddCommandHandler(ApplicationCommands.Delete, Delete, CanDelete)`
   etc., plus custom `Commands.AlignTopCommand`/`RotateLeftCommand`/
   `StretchToSameWidthCommand`) is a separate, parallel wiring layer —
   not what these 9 context menus call.** Every menu's own `Click_X`
   handler (verified by reading all 9 `.xaml.cs` files) calls
   `ModelTools`/`ICopyPasteService` directly, e.g.
   `ArrangeItemsContextMenu.xaml.cs`'s `Click_ArrangeLeft` calls
   `ModelTools.ArrangeItems(...)` directly, not
   `Commands.AlignLeftCommand`. That `RoutedCommand` hub exists for
   *other* callers (a toolbar, keyboard shortcuts) this port doesn't
   have yet. **This phase does not port it** — see Decisions.

Not yet sent through adversarial review, unlike 4a (two rounds) and 4b
(two rounds). Items marked "unverified"/"plan-time detail" below should
be re-checked empirically before or during planning, not assumed correct
from this document alone.

## WinUI API surface — verified, not assumed

Checked via SDK XML docs (`Microsoft.WinUI.xml`) and a throwaway compiler
probe (`src/WinUIDesigner.Designer/__Probe4c.cs`, built with `dotnet
build` against `Microsoft.WindowsAppSDK 1.7.250606001`, then deleted).

**Confirmed present:**
- `Microsoft.UI.Xaml.Controls.MenuFlyout`, `.Items`
  (`IList<MenuFlyoutItemBase>`).
- `MenuFlyoutItem` — `.Text`, `.Click` event.
- `MenuFlyoutSubItem` — `.Items`.
- `MenuFlyoutSeparator`.
- `UIElement.ContextFlyout` / `ContextFlyoutProperty` — single flyout
  slot per element, same shape as WPF's `UIElement.ContextMenu`.

**Confirmed absent:**
- `System.Windows.Input.RoutedCommand` / `ApplicationCommands` /
  `CommandBinding` / `MenuItem.Command` — no equivalent class exists in
  WinUI at all (first time this port has hit this gap directly). Every
  menu item becomes a direct `Click` handler; see Decisions for why no
  replacement command-pattern layer is being built.
- `StreamGeometry` — zero hits in `Microsoft.WinUI.xml`. New gap, not
  previously on CLAUDE.md's list. Only load-bearing for
  `PathContextMenu`'s "Convert to Figures" (`is StreamGeometry` branch),
  which is out of scope this phase — flagged here for the record and for
  CLAUDE.md, not something this phase works around.
- `Canvas.RightProperty` / `Canvas.BottomProperty` — already-known gap
  (CLAUDE.md), re-encountered: the reference's `ModelTools.ArrangeItems`
  has `Canvas.Right`-anchored branches for right/bottom-edge alignment.
  See Decisions.

**Not yet empirically verified — flagged, not guessed:**
- Whether a live `MenuFlyoutItemBase` can be safely removed from one
  `MenuFlyout`/`MenuFlyoutSubItem.Items` collection and added to
  another's, the same way WPF's `ContextMenu.Items` (an `ItemsControl`
  collection) supports reparenting a live `MenuItem`. WPF's
  `DesignPanel.UpdateContextMenu`
  (`WpfDesign.Designer\Project\DesignPanel.cs:555-613`) depends on this:
  it snapshots each extension's private `ContextMenu.Items` into a
  dictionary keyed by `Order`, then rebuilds one composite menu by
  removing each item from its origin `ItemsControl` and adding it to the
  composite, in `Order` groups separated by a `Separator`.
  `MenuFlyoutItemBase` is a WinRT `DependencyObject`, not a WPF
  `FrameworkElement`/`Freezable`-adjacent type, and this port has
  already hit other "does a live instance survive being moved between
  collections" gaps in 4a/4b (`TransformGroup` sharing). **Must be
  verified empirically as the plan's first task**, same precedent as
  every prior phase's first Known gap.

## Also verified against the current codebase

- **`DefaultExtension`
  (`src/WinUIDesigner.DesignModel/Extensions/Extension.cs` —
  `ExtendedItem`/`Services`/`OnInitialized`/`OnRemove`) is the correct
  base for all 6 in-scope context-menu extensions, not `AdornerProvider`.**
  The reference's `SelectionAdornerProvider`/`PrimarySelectionAdornerProvider`
  (`WpfDesign\Project\Adorners\AdornerProviderClasses.cs:38-51`) are pure
  `[ExtensionServer(...)]`-typing wrappers around `AdornerProvider` with
  empty bodies — they add zero adorner-panel behavior, and every
  reference context-menu extension subclasses one of them purely for
  convenience, not necessity. 4a already established the precedent of
  skipping these thin wrapper classes and putting `[ExtensionServer(...)]`
  directly on the concrete extension instead (`ResizeThumbExtension.cs`'s
  own doc comment: "`SelectionAdornerProvider` abstract base not ported -
  subclasses `AdornerProvider` directly"). Context-menu extensions have
  zero adorner visuals, so even subclassing `AdornerProvider` directly
  would pull in unused `Adorners`-collection machinery
  (`src/WinUIDesigner.DesignModel/Adorners/AdornerProvider.cs`) — this
  phase goes one step further than 4a's precedent and uses the plain
  `DefaultExtension` base, which is exactly sized for a non-visual
  extension.
- **`PrimarySelectionExtensionServer` and `OnlyOneItemSelectedExtensionServer`
  already exist**
  (`src/WinUIDesigner.DesignModel/Extensions/SelectionExtensionServers.cs`)
  and cover 4 of 6 in-scope menus (`DefaultCommands`, `RightClick`,
  `WrapItem`, `UnwrapItem`).
  **`PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer` does
  not exist** (grep-confirmed zero hits) — needed by `ArrangeItems` and
  `WrapItems`, both multi-selection-only in the reference. Small,
  same-shape addition: reference at
  `WpfDesign.Designer\Project\Extensions\SelectionExtensionServers.cs:108-137`,
  an `ExtensionServer` whose `ShouldApplyExtensions` checks
  `SelectionCount > 1`, mirroring the two already-ported servers'
  structure.
- **`ExtendedItem.Services.DesignPanel` is `GetRequiredService`-backed
  and throws when no `IDesignPanel` is registered** (established fact,
  4b's `RasterPlacementBehavior.cs` doc comment) — every context-menu
  extension must guard with `Services.GetService<IDesignPanel>() != null`
  first (as `RasterPlacementBehavior.cs:40,67` already does), not the
  reference's soft `as DesignPanel` null-check cast on a differently-
  behaved property.
- **`DesignPanel` (`src/WinUIDesigner.Designer/DesignPanel.cs`) is a
  sealed `Panel : IDesignPanel` with no context-menu/flyout hook yet**
  (grep-confirmed) — this phase adds one (see Architecture).
- **`ModelTools.cs` (`src/WinUIDesigner.Designer/ModelTools.cs`)
  currently has only 4 members**: `GetWidth`/`GetHeight`/`Resize`/
  `ApplyTransform` (grep-confirmed) — this phase adds
  `CanDeleteComponents`/`DeleteComponents`/`ArrangeItems`/
  `WrapItemsNewContainer`/`UnwrapItemsFromContainer`.
- **`DefaultPlacementBehavior.CanPlace` already returns `true`
  unconditionally for any `PlacementType`**
  (`src/WinUIDesigner.Designer/Extensions/DefaultPlacementBehavior.cs:46`)
  — `CanDeleteComponents` (which calls `IPlacementBehavior.CanPlace(items,
  PlacementType.Delete, ...)`) works against every placement behavior in
  this port (`DefaultPlacementBehavior`, `CanvasPlacementSupport`,
  `GridPlacementSupport`, `StackPanelPlacementSupport`) with zero changes
  to any of them. `PlacementType.Delete` is already registered
  (`src/WinUIDesigner.DesignModel/PlacementType.cs:11`).
- **`PlacementOperation.DeleteItemsAndCommit` was explicitly dropped in
  Phase 3's port** (its own doc comment,
  `src/WinUIDesigner.DesignModel/PlacementOperation.cs:11-17`: "their
  only callers are the delete command (out of scope per Phase 3)... and
  toolbox-drop insertion... dropped"). Must be re-added. Small: the
  reference body
  (`WpfDesign\Project\PlacementOperation.cs:159-168`) is a
  `CanLeaveContainer` guard, `LeaveContainer`, then `Commit()` — all
  three already exist and work in this port from 4a/4b's
  `ChangeContainer` implementation.
- **`DesignItem.OpenGroup(string)` and `ChangeGroup.Commit()`/`Abort()`
  already exist** (`DesignItem.cs:123-125`) and are the established
  pattern for wrapping a multi-item operation as one undo entry (already
  used by `ModelTools.ApplyTransform`, per 4b's spec notes on why its
  own `OpenGroup` fix was needed there).
- **`DesignItemProperty.IsCollection`/`CollectionElements` and
  `DesignItem.ParentProperty` already exist** (`DesignItem.cs:82`,
  `DesignItemProperty.cs:38,40`) — `RightClickContextMenuExtension`'s
  z-order logic (Bring to Front/Send Backward/Forward/Send to Back)
  ports unchanged: direct `CollectionElements.Remove`/`Add`/`Insert`/
  `IndexOf`, no new API needed.
- **`IComponentService.ComponentRemoved` event exists**
  (`src/WinUIDesigner.DesignModel/Services.cs:29`), but whether removing
  an item from its parent's `CollectionElements` fires it automatically
  or needs an explicit raise — the way the reference's `DeleteComponents`
  calls `service.RaiseComponentRemoved(item)`, a method on the
  reference's concrete `XamlComponentService`, not part of the
  `IComponentService` interface — is **unverified**. Plan-time detail:
  check this port's concrete `IComponentService` implementation before
  assuming either way.

## Decisions already made

- **Scope: 6 of the 9 reference context menus** — `RightClick`,
  `DefaultCommands` (Delete/Undo/Redo only — its Cut/Copy/Paste items are
  dropped), `ArrangeItems`, `WrapItem`, `WrapItems`, `UnwrapItem`.
  `TextBlockRightClick` (opens a WPF dialog `Window` for rich-text
  editing), `EditStyle` (style-resource extraction plus an XAML
  round-trip), and `PathContextMenu` (Convert to Figures — also hits the
  new `StreamGeometry` gap) are out of scope. User call, made explicitly
  after being shown the narrow-vs-full-9-menu trade-off (see Revision
  history).
- **`Delete` stays in scope despite depending on `ICopyPasteService`**
  (the same interface `Cut`/`Copy`/`Paste` use). This phase adds one
  concrete `CopyPasteService : ICopyPasteService`
  (`src/WinUIDesigner.DesignModel/Services/CopyPasteService.cs`, new)
  with `CanDelete`/`Delete` implemented for real (`ModelTools`-backed)
  and `CanCopy`/`Copy`/`CanCut`/`Cut`/`CanPaste`/`Paste` explicitly
  returning `false`/no-op — documented as deliberately unimplemented,
  not silently wrong. This is not clipboard support; it's the minimum
  concrete class the interface needs to have one real member.
- **Undo/Redo menu items call the already-existing
  `UndoService.Undo()`/`Redo()`/`CanUndo`/`CanRedo` directly.** The
  reference's version of this logic lives on `DesignSurface`
  (`DesignSurface.cs:216-238`) and also reselects the affected elements
  after undo/redo (`GetLiveElements(action.AffectedElements)`). Since
  this phase does not port `DesignSurface`'s command hub (next bullet),
  the `DefaultCommandsContextMenuExtension`'s own `Undo`/`Redo` click
  handlers need their own small equivalent — resolve the `UndoService`
  off `ExtendedItem.Context`, call `Undo()`/`Redo()`, then reselect via
  `IUndoAction.AffectedElements` — not a full port of `DesignSurface`'s
  version, which also has to filter for "is this element still live in
  the tree" in ways specific to its own multi-command setup.
- **The reference's `DesignSurface`'s `RoutedCommand` hub
  (`AddCommandHandler(ApplicationCommands.*, ...)` plus custom
  `Commands.AlignTopCommand`/`RotateLeftCommand`/
  `StretchToSameWidthCommand` etc., `DesignSurface.cs:49-65`) is
  explicitly NOT ported by this phase.** Verified: every one of the 6
  in-scope menus' own `Click_X` handlers calls `ModelTools`/
  `ICopyPasteService` directly already, never through that hub — so
  nothing in this phase's scope needs it. That hub exists in the
  reference for *other* callers this port doesn't have yet (a toolbar,
  global keyboard shortcuts) — Phase 5 shell-app territory, not this
  phase's concern.
- **`ArrangeItems` drops `Canvas.Right`/`Bottom`-anchored alignment
  branches** (`Canvas.RightProperty`/`BottomProperty` confirmed absent,
  CLAUDE.md's known-gaps list) — Left/Top-anchored alignment only,
  consistent with Phase 4b's `CanvasPlacementSupport` already dropping
  `Right`/`Bottom` support for the same reason.
- **Context-menu extensions subclass `DefaultExtension` directly, not
  `AdornerProvider`** (see "Also verified" above) — each with its own
  `[ExtensionServer(...)]`/`[ExtensionFor(...)]`, extending 4a's already-
  established "skip the thin wrapper base class" precedent one step
  further since these extensions have no adorner visuals at all.

## Architecture

```
D:\Project\ST4I\Research\WinUIDesigner\
  src\
    WinUIDesigner.DesignModel\
      Extensions\
        SelectionExtensionServers.cs   (modify: add
                                         PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer)
      PlacementOperation.cs            (modify: re-add DeleteItemsAndCommit
                                         — CanLeaveContainer guard +
                                         LeaveContainer + Commit)
      Services\
        CopyPasteService.cs            (new: concrete ICopyPasteService.
                                         Delete/CanDelete real (ModelTools-
                                         backed); Copy/Cut/Paste/CanCopy/
                                         CanCut/CanPaste explicitly
                                         unimplemented)
    WinUIDesigner.Designer\
      DesignPanel.cs                    (modify: add
                                         AddContextFlyoutItems(items, order)/
                                         RemoveContextFlyoutItems — internal
                                         Order-grouped merge into one
                                         MenuFlyout set as this.ContextFlyout,
                                         separators between Order groups)
      ModelTools.cs                     (modify: add CanDeleteComponents,
                                         DeleteComponents, ArrangeItems
                                         (Left/Top-anchored only),
                                         WrapItemsNewContainer,
                                         UnwrapItemsFromContainer)
      Extensions\
        RightClickContextMenuExtension.cs      (new: [ExtensionServer(
                                                 OnlyOneItemSelectedExtensionServer)],
                                                 [ExtensionFor(FrameworkElement)],
                                                 Order 20 — Bring to
                                                 Front/Send Backward/
                                                 Forward/Send to Back)
        DefaultCommandsContextMenuExtension.cs (new: [ExtensionServer(
                                                 PrimarySelectionExtensionServer)],
                                                 [ExtensionFor(object)],
                                                 Order 10 — Delete/Undo/Redo
                                                 only)
        ArrangeItemsContextMenuExtension.cs    (new: [ExtensionServer(
                                                 PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer)],
                                                 [ExtensionFor(FrameworkElement)],
                                                 Order 30 — Align Left/
                                                 HCenter/Right/Top/VCenter/
                                                 Bottom)
        WrapItemContextMenuExtension.cs        (new: [ExtensionServer(
                                                 OnlyOneItemSelectedExtensionServer)],
                                                 [ExtensionFor(FrameworkElement)],
                                                 Order 50 — Wrap in Viewbox)
        WrapItemsContextMenuExtension.cs       (new: [ExtensionServer(
                                                 PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer)],
                                                 [ExtensionFor(FrameworkElement)],
                                                 Order 52 — Wrap in Canvas /
                                                 Wrap in Grid)
        UnwrapItemContextMenuExtension.cs      (new: [ExtensionServer(
                                                 OnlyOneItemSelectedExtensionServer)],
                                                 [ExtensionFor: Grid, Canvas],
                                                 Order 51 — Unwrap)
  tests\
    WinUIDesigner.DesignModel.Tests\
      SelectionExtensionServersTests.cs  (modify, Phase 3 file: add
                                           PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer
                                           coverage)
      CopyPasteServiceTests.cs           (new: CanDelete/Delete real;
                                           CanCopy/CanCut/CanPaste all
                                           false, Copy/Cut/Paste no-op)
      PlacementOperationDeleteTests.cs   (new: DeleteItemsAndCommit)
    WinUIDesigner.Designer.Tests\
      DesignPanelContextMenuTests.cs     (new: AddContextFlyoutItems/
                                           RemoveContextFlyoutItems merge-
                                           by-Order mechanism — the plan's
                                           first empirical task, Known gap
                                           #1)
      ModelToolsContextMenuTests.cs      (new: CanDeleteComponents/
                                           DeleteComponents/ArrangeItems/
                                           WrapItemsNewContainer/
                                           UnwrapItemsFromContainer)
      ContextMenuExtensionsTests.cs      (new: all 6 extensions attach/
                                           detach correctly per their
                                           ExtensionServer, and their menu
                                           items invoke the right
                                           ModelTools/CopyPasteService/
                                           UndoService call)
      Phase4cDefinitionOfDoneTests.cs    (new)
  docs\superpowers\specs\
    2026-09-02-winui-designer-phase4c-design.md   (this file)
```

## Mechanism table (WPF → WinUI replacements)

| WPF mechanism | WinUI replacement |
| --- | --- |
| `ContextMenu` + `DesignPanel.AddContextMenu(menu, order)`/`RemoveContextMenu`, merge-by-`Order` into one composite `ContextMenu` assigned to `DesignPanel.ContextMenu` | `MenuFlyout` + `DesignPanel.AddContextFlyoutItems(items, order)`/`RemoveContextFlyoutItems`, merge-by-`Order` into one composite `MenuFlyout` assigned to `DesignPanel.ContextFlyout`. Live-instance move between `Items` collections unverified — Known gap #1. |
| `MenuItem`, `MenuItem.Click` | `MenuFlyoutItem`, `MenuFlyoutItem.Click` |
| `RoutedCommand`/`ApplicationCommands`/`CommandBinding`/`MenuItem.Command` | Not present (compiler-confirmed, no equivalent class). Direct `Click` handler per item, calling `ModelTools`/`ICopyPasteService`/`UndoService`. No command-pattern indirection layer replaces it — nothing in this phase's scope needs one (see Decisions). |
| `Canvas.RightProperty`/`BottomProperty` (`ArrangeItems`' right/bottom-anchored branches) | Not present (known gap, CLAUDE.md) — dropped, Left/Top-anchored alignment only. |
| `StreamGeometry` (`PathContextMenu`, out of scope) | Not present (new gap, flagged for the record; irrelevant to this phase's actual scope). |
| `Properties.GetAttachedProperty(Canvas.LeftProperty)` (DP-indexed) | `Properties.GetAttachedProperty(typeof(Canvas), "Left")` (`Type, string`-indexed) — established string-keyed convention. |
| `DesignSurface.Delete()`/`CanDelete()` → `ICopyPasteService.Delete`/`CanDelete` → `ModelTools.DeleteComponents`/`CanDeleteComponents` → `PlacementOperation.Start(items, PlacementType.Delete)` + `DeleteItemsAndCommit()` | Same chain, same names, ported member-for-member — no WPF-specific API in this path once `DeleteItemsAndCommit` is re-added. |

## Data flow

**Showing the menu.** A right-click (or long-press equivalent) on a
selected item's view is received by the `DesignPanel` (it owns hit
input, same as Phase 3's selection/hit-test path). `DesignPanel`
maintains one composite `MenuFlyout`, rebuilt from every currently-
attached context-menu extension's contributed items, grouped by
ascending `Order` with a `MenuFlyoutSeparator` between groups (Order 10:
Delete/Undo/Redo; 20: z-order; 30: align; 50: wrap-in-Viewbox; 51:
unwrap; 52: wrap-in-Canvas/Grid), and assigns it to
`this.ContextFlyout`. Each extension adds/removes its own items via
`AddContextFlyoutItems(items, order)`/`RemoveContextFlyoutItems` from its
`OnInitialized`/`OnRemove` — mirroring the reference's
`AddContextMenu`/`RemoveContextMenu` calls in each `Extension.OnInitialized`/
`OnRemove`.

**Click routing.** Every item's `Click` handler calls straight into
`ModelTools`/`ICopyPasteService`/`UndoService` — no command-pattern
indirection (see Decisions). Concretely:

- **z-order** (`RightClick`): `ExtendedItem.ParentProperty.CollectionElements`
  directly reordered (`Remove`+`Add`/`Insert`/index math) — no
  `PlacementOperation`, matches the reference exactly.
- **Delete** (`DefaultCommands`): `CopyPasteService.Delete(designContext)`
  → `ModelTools.DeleteComponents(selectedItems)` → groups items by
  parent, opens one `OpenGroup("Delete Items")` `ChangeGroup` for the
  whole multi-item, multi-parent operation, and per parent-group calls
  `PlacementOperation.Start(items, PlacementType.Delete)` →
  `DeleteItemsAndCommit()` (guards `CanLeaveContainer`, calls
  `LeaveContainer`, commits) — one undo entry regardless of how many
  parents were involved.
- **Undo/Redo** (`DefaultCommands`): resolve `UndoService` off
  `ExtendedItem.Context`, call `Undo()`/`Redo()`, reselect via the
  popped/pushed `IUndoAction.AffectedElements` (see Decisions).
- **Arrange/Align** (`ArrangeItems`): `ModelTools.ArrangeItems(selectedItems,
  direction)` — computes the selection's combined bounds, then for each
  item either adjusts `Canvas.Left`/`Top` (Canvas container) or
  `Margin`+`HorizontalAlignment`/`VerticalAlignment` (Grid/other
  container), Left/Top-anchored only (`Canvas.Right`/`Bottom` branches
  dropped).
- **Wrap/Unwrap** (`WrapItem`/`WrapItems`/`UnwrapItem`):
  `ModelTools.WrapItemsNewContainer(selectedItems, typeof(Viewbox|Canvas|Grid))`
  / `ModelTools.UnwrapItemsFromContainer(primarySelection)` — each already
  wraps its own `PlacementOperation`/`ChangeGroup` internally (ported
  unchanged from the reference's `ModelTools.cs:257,382`).

## Testing / Definition of Done

Same `.exe` NUnitLite runner and live-`Window`/real-`DesignSurface`
fixture pattern every prior phase established.

Phase 4c is done when:

- A test attaches two context-menu extensions with different `Order`
  values to the same item, triggers the merge, and asserts the resulting
  `MenuFlyout.Items` contains both groups' items in ascending `Order`
  with exactly one separator between them — the plan's first task,
  resolving Known gap #1 (does moving a live `MenuFlyoutItemBase`
  between `Items` collections actually work).
- A test right-clicks a single selected item and asserts the z-order
  menu items (`Bring to Front`/`Send to Back`/`Forward`/`Backward`) move
  it to the correct index in its parent's `CollectionElements`, with
  each change a separate undo entry.
- A test selects one or more items, invokes Delete, and asserts: the
  items are removed from their parent(s)' `CollectionElements`; a
  multi-parent selection commits as exactly one undo entry
  (`DeleteComponents`'s single `OpenGroup`); the selection after delete
  matches the reference's "select the parent if nothing else is
  selected" rule; and `CanDeleteComponents` returns `false` for an
  empty selection.
- A test invokes Undo after a tracked change and asserts both the
  `UndoService` state and the reselected elements match the change's
  `AffectedElements`; a parallel test covers Redo.
- A test selects 2+ items in a `Canvas` and invokes each `Align`
  direction, asserting `Canvas.Left`/`Top` update to the expected
  aligned value (min/max/midpoint per direction) — no test targets
  `Canvas.Right`/`Bottom` since that branch is dropped.
- A test invokes Wrap-in-Viewbox on a single item and asserts a new
  `Viewbox` `DesignItem` is created as the item's new parent, with the
  item unchanged otherwise, as one undo entry; parallel tests cover
  Wrap-in-Canvas and Wrap-in-Grid on a multi-item selection.
- A test invokes Unwrap on a `Grid`/`Canvas` container and asserts its
  children are reparented to the container's own parent at the
  container's former position, and the emptied container `DesignItem`
  is removed, as one undo entry.
- A test asserts each of the 6 extensions attaches/detaches exactly when
  its `ExtensionServer`'s rule says it should (e.g.
  `ArrangeItemsContextMenuExtension` only present when 2+ items are
  selected) and that removing an extension removes its items from the
  merged `MenuFlyout`.
- Known gap #1 has an explicit, tested resolution (first bullet above).
  Known gap #2 (`ComponentRemoved` auto-fire vs. explicit raise) is
  checked directly against this port's concrete `IComponentService`
  before `DeleteComponents` is written, not guessed. Known gap #3 (exact
  `MenuFlyout` rebuild strategy on attach/detach) is a plan-time
  implementation choice, not something a test needs to "resolve" beyond
  covering whatever the plan settles on.

## Known gaps — flagged, not guessed

1. **Whether a live `MenuFlyoutItemBase` can be moved between
   `MenuFlyout`/`MenuFlyoutSubItem.Items` collections the way WPF's
   `ContextMenu.Items` supports.** The merge mechanism (`Order`-grouped,
   separator-divided, rebuilt on attach/detach) depends on this working.
   **Must be verified empirically as the plan's first task**, same
   precedent as every prior phase's first Known gap (4a's `DragListener`
   bug hunt, 4b's `ChangeContainer` sequencing check).
2. **Whether removing an item from its parent's `CollectionElements`
   fires `IComponentService.ComponentRemoved` automatically, or needs an
   explicit raise** the way the reference's `DeleteComponents` calls
   `service.RaiseComponentRemoved(item)` on its concrete
   `XamlComponentService` (not part of the `IComponentService`
   interface). Check this port's concrete `IComponentService`
   implementation directly before writing `DeleteComponents` — a small,
   quick check, not a deep research risk.
3. **Exact `MenuFlyout` rebuild strategy when an extension attaches or
   detaches at runtime** (selection changes causing
   `OnlyOneItemSelectedExtensionServer`/
   `PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer`-gated
   extensions to come and go). Rebuilding the whole composite
   `MenuFlyout.Items` from scratch on every attach/detach is the obvious
   safe default (mirrors the reference's `UpdateContextMenu`, called
   from both `AddContextMenu` and `RemoveContextMenu`); whether a
   more incremental approach is worth it is a plan-time implementation
   choice, not a research risk.

## Out of scope for Phase 4c

`TextBlockRightClickContextMenu` (opens a WPF dialog `Window` for
rich-text editing — no WinUI dialog/window design done yet, and this
looks like Phase 5 shell-app territory more than a context-menu
concern), `EditStyleContextMenu` (style-resource extraction plus an XAML
serialization round-trip), `PathContextMenu` (Convert to Figures — hits
the new `StreamGeometry` gap, plus geometry-flattening logic not needed
anywhere else in this port), `DefaultCommandsContextMenu`'s
`Cut`/`Copy`/`Paste` items (real Windows clipboard integration —
`ICopyPasteService`'s `Copy`/`Cut`/`Paste`/`CanCopy`/`CanCut`/`CanPaste`
stay explicitly unimplemented), the reference's `DesignSurface`
`RoutedCommand` command hub and its custom `Commands.*` `RoutedUICommand`s
(toolbar/keyboard-shortcut wiring — Phase 5), drawing tools (4d), and the
remaining Phase 4 sub-phase behaviors (border decorations, size display,
render-transform-origin, in-place editor, tab-item-click, quick-operation
menu, `CanvasPositionExtension` margin-handle guides — 4e).
