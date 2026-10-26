# Cut, Copy and Paste — design

**Date:** 2026-10-26
**Status:** approved in chat 2026-10-26 ("agree" to "spec and plan first, then code"); planned in
`docs/superpowers/plans/2026-10-26-clipboard.md`
**Reference:** `F:\ReferencesCode\CSharp\WpfDesigner` (read-only) — `WpfDesign.Designer`'s
`CopyPasteService` delegates to `XamlEditOperations` (Copy, Cut, Paste, AddInParent), and
`CanvasPlacementSupport.EnterContainer` carries a `PlacementType.PasteItem` branch.

## Goal

Ctrl+C, Ctrl+X and Ctrl+V on the design surface copy, cut and paste the selected controls through
the Windows clipboard. Each paste is one undo step, a cut is one undo step, and the pasted
controls end up selected.

Nothing else. Pasting XAML text from another application, drag-copying with Ctrl, and
Cut/Copy/Paste items in the surface's right-click menu are out of scope (listed at the end).

## Why this phase

The Edit menu has carried Cut, Copy and Paste since Phase 5, and all three stay disabled for good.
Phase 4c made `CopyPasteService` with only `Delete`/`CanDelete` implemented and the other six
members `false`/no-op on purpose ("real Windows clipboard integration" was out of scope). The
walkthrough run of 2026-10-25 confirmed it on the real app: Ctrl+C then Ctrl+V placed nothing. Of
the gaps the shell still has, this is the one every user reaches for first after placing a
control.

## What already exists

Read from the current tree, not assumed:

| Piece | Where | State |
|---|---|---|
| Item → XAML text | `XamlStaticTools.GetXaml(XamlObject)` | Ported. Clones the element and copies every `xmlns:` declaration from its ancestors onto it, so the snippet parses alone. |
| XAML text → item | `XamlParser.ParseSnippet(root, xaml, settings)` + `XamlComponentService.RegisterXamlComponentRecursive` | Ported, and used today by `XamlDesignItem.Clone()`. |
| Duplicate `x:Name` | `XamlComponentService` (Phase 4c gap 4) | Ported. A registered object whose name another element already uses is renamed `Name_Copy`, `Name_Copy1`, … |
| Insert into a container | `PlacementOperation.TryStartInsertNewComponents(container, items, rects, type)` | Ported. Returns null when the container's placement behaviour refuses; calls `EnterContainer` and nothing else. |
| `PlacementType.PasteItem` | `DesignModel/PlacementType.cs` | Registered, never used. |
| Canvas paste offset | `CanvasPlacementSupport.EnterContainer` | **Dropped** in Phase 4b, with a comment that names the dropped branch. |
| Commands | `MainWindow`'s Edit menu: Cut (Ctrl+X), Copy (Ctrl+C), Paste (Ctrl+V) → `WithClipboard`, enabled from `CanCut`/`CanCopy`/`CanPaste` in `RefreshCommands` | Wired, inert. |
| Text-entry guard | `MainWindow.IsTextEntryFocused`, used by Delete and Select All | Exists; not applied to Cut/Copy/Paste, which was harmless only while they did nothing. |

So the engine half is almost entirely there. What is missing is the clipboard itself, the
target-container choice, the paste offset, and the guard.

## Decisions

**1. The payload is each item's `GetXaml`, joined by a delimiter, under a private format.**
Copy writes the selected items' snippets, in selection order, joined by
`<!--WinUIDesigner.Delimiter-->`, as data of format id `WinUIDesigner.Xaml`. The reference used
WPF's `TextDataFormat.Xaml`; WinUI's `DataPackage` has no XAML format, so a custom format id
takes its place. The same text is also set as plain `Text`, so the copied XAML can be pasted into
the XAML editor or any text editor. Paste reads only the private format: plain text from the
clipboard could be anything, and treating every `<`-prefixed string as markup to insert is out of
scope (Decision 9 and the list at the end).

**2. The clipboard sits behind a small interface, implemented on the Win32 clipboard API.**
`IDesignerClipboard` in `WinUIDesigner.Designer` has three members: `void SetXaml(string)`,
`bool ContainsXaml` and `Task<string> GetXamlAsync()`. The default implementation is
`SystemDesignerClipboard`. Tests use a fake, so they never depend on the real clipboard of the
machine running them.

The first implementation used `Windows.ApplicationModel.DataTransfer.Clipboard`. On the
walkthrough runner every `SetContent` failed with `CO_E_NOTINITIALIZED` (0x800401F0), measured
with a temporary trace, and calling `OleInitialize` on the UI thread first changed nothing.
`SystemDesignerClipboard` therefore uses the Win32 API: `OpenClipboard`, `SetClipboardData` with a
registered `WinUIDesigner.Xaml` format plus `CF_UNICODETEXT`, `IsClipboardFormatAvailable` and
`GetClipboardData`. This API has no apartment or broker requirement. The read is synchronous;
`GetXamlAsync` keeps an asynchronous shape so the seam could take an asynchronous clipboard again.

**3. `Paste` starts an asynchronous read and inserts when it completes.** `ICopyPasteService.Paste`
is synchronous (ported interface, unchanged). It calls `GetXamlAsync` and awaits it. The
continuation runs back on the UI thread, which WinUI's `DispatcherQueueSynchronizationContext`
guarantees. It then calls `PasteXaml(context, xaml)`, an internal method holding the actual
insertion and the seam the tests drive. Before inserting, `PasteXaml` re-checks that the context
still has a root item, in case the document was closed or reloaded while the read was in flight.

**4. Paste target: walk up from the selection until a container accepts.** The first candidate is
the primary selection when it is a panel (its content property is a collection), the primary
selection's parent when it is any other control, or the root when nothing is selected. The
reference starts at the primary selection itself, and also accepts a `ContentControl` whose
content is empty. Copying a freshly placed Button and pasting it would then make the copy the
original's `Content`. Starting at the parent for a non-panel avoids that. Paste tries
`TryStartInsertNewComponents(candidate, items, rects, PasteItem)`; while that returns null, it moves
to the candidate's parent. So:
- with a panel selected, paste goes into the panel;
- with a button selected, it goes next to the button, in the button's container;
- with nothing selected, it goes into the root.

This keeps the reference's intent (`XamlEditOperations.Paste` walks `parent = parent.Parent` until
a content property can take the items). It asks the placement behaviour instead of re-deriving
"can this collection take these types", because the behaviour is what decides for every other
insertion in this port. If no ancestor accepts, nothing is inserted and no undo entry is left.

**5. A paste lands offset by 10px on a Canvas, and keeps its attached position elsewhere.** The
reference's `PasteItem` branch in `CanvasPlacementSupport.EnterContainer` is restored: when the
operation type is `PasteItem`, a placed item's `Canvas.Left`/`Canvas.Top` that are set get
`PlacementOperation.PasteOffset` (10) added. So a copy pasted beside its original does not cover
it exactly, and pasting the same clipboard again goes to the same offset spot, not stepping
further. That matches the reference. Every other container keeps what the snippet carries, such as
`Grid.Row`/`Grid.Column`, because `TryStartInsertNewComponents` calls only `EnterContainer`, never
`SetPosition`. The rects handed to it are `(0, 0, width, height)`, as in the reference's
`AddInParent`.

**6. One undo entry per paste and per cut.** Paste is one `PlacementOperation`, so one change group
named after `PasteItem`, committed once for all items. Cut is Copy followed by
`ModelTools.DeleteComponents`, which already commits one group. Copy changes nothing in the
document and leaves no undo entry.

**7. The pasted items become the selection**, as `CreateComponentTool` does for a placed control,
so a paste can be moved or pasted again at once.

**8. Can-execute.**
- `CanCopy`: the selection is non-empty and does not contain the root item.
- `CanCut`: `CanCopy`, and `ModelTools.CanDeleteComponents` agrees.
- `CanPaste`: the context has a root item and `IDesignerClipboard.ContainsXaml`.

`MainWindow` re-runs `RefreshCommands` whenever the window is activated. Another application can
replace the clipboard only while this window is in the background, so Paste's enabled state is
re-read the moment the user comes back. The Win32 clipboard has a change notification
(`AddClipboardFormatListener`), but it needs a window procedure hook that this shell does not
otherwise have.

**9. The text-entry guard covers Cut, Copy and Paste.** Each of the three accelerators declines,
leaving `Handled` false, while `IsTextEntryFocused()` is true, exactly as Delete and Select All do.
Ctrl+C in the XAML editor or the outline filter then copies text, not controls. Without the guard,
Ctrl+V in the outline filter would paste controls into the document.

**10. The tests drive the service through a fake clipboard.** No test touches the system clipboard.
Whether the real one round-trips is a walkthrough item: the runner drives real Ctrl+C/Ctrl+V on
the real app.

## Definition of done

1. Copy then Paste of a selected button on a Canvas adds a second button at Left/Top + 10, selected,
   as one undo entry. Undo removes only the pasted button.
2. Cut removes the selection as one undo entry and leaves it on the clipboard; Paste brings it back.
3. Paste with a panel selected goes into that panel; with a child control selected, it goes into
   that control's container; with nothing selected, into the root.
4. A pasted `x:Name` that is already taken is renamed with `_Copy`.
5. Copying several items pastes all of them in one undo entry.
6. Cut/Copy/Paste are enabled exactly when Decision 8 says, and stay inert while a text box has
   focus.
7. The walkthrough shows a real Ctrl+C / Ctrl+V / Ctrl+X round trip on the runner.
8. All four suites green from their `.exe`s on CI.

## Out of scope

- Pasting XAML text copied from another application or from the XAML editor (plain `Text` format).
- Ctrl+drag to copy on the surface.
- Cut/Copy/Paste in the surface's right-click menu. `DefaultCommandsContextMenuExtension` dropped
  those items in Phase 4c and stays as it is.
- Paste at the pointer position, or a step-offset that grows with each repeated paste.
- Pasting across documents: it works, since the payload is plain XAML, but is not separately
  tested or designed for, e.g. types the target document cannot resolve simply fail to parse and
  paste nothing.
