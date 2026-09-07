# Shell document tabs, XAML source mode, and an error list — design

**Date:** 2026-09-07
**Status:** approved, not yet planned
**Reference:** `F:\ReferencesCode\CSharp\WpfDesigner\XamlDesigner` (read-only)

## Goal

Bring three things the reference designer shell has and this port's shell does not: more
than one document open at a time, a switch between the design surface and the XAML text
that produced it, and a list of the parse errors a document reported.

Nothing else. The reference's docking manager, thumbnail view, behaviors editor, triggers
editor, recent-files list, and Find/Refresh/Select All commands are all out of scope and
listed at the end.

## Why this phase

Phase 5 shipped a shell that opens one document into a fixed three-column window. The
model underneath it was already built for more: `ShellModel` has carried an
`ObservableCollection<Document> Documents`, a `CurrentDocument` setter, and a
`Close(Document)` that runs the unsaved-changes gate since the day it was written, and no
UI has ever reached any of them. The reference's `MainWindow.xaml` binds its docking
manager's `DocumentsSource` to exactly that collection.

The design/XAML switch matters for a different reason: this port has no other way to see
what a document actually saves. Phase C found a bug — every skew angle silently discarded
on save — that a user could not have noticed, because the shell never showed them the
XAML. The reference's `DocumentView` puts that text one click away.

## The gap, measured

| Reference `XamlDesigner` | Shell today |
|---|---|
| AvalonDock: floating, dockable, resizable panes | `Grid`, columns fixed at 200 / `*` / 280 |
| Many documents, tabbed (`DocumentsSource="{Binding Documents}"`) | One document; `ShellModel.Documents` exists and is unused by the UI |
| `DocumentView`: `EnumBar` at the bottom switches Design / XAML; AvalonEdit shows the text | Design only |
| `ErrorListView` bound to `XamlErrorService.Errors` | No pane; `Document.LoadErrors` exists and is unused by the UI |
| Thumbnail, Behaviors, Triggers panes | None |
| File: New/Open/**Close**/**Close All**/Save/Save As/**Save All**/**Recent Files**/Exit | New/Open/Save/Save As/Exit |
| Edit: …/**Select All**/**Refresh**/**Find** | Undo/Redo/Cut/Copy/Paste/Delete |
| Test: Run / Render To Bitmap | None |

## Verified against the current codebase

Every claim below was measured, not assumed. Anything not listed here was not measured
and must not be relied on.

- **`Microsoft.UI.Xaml.Controls.GridSplitter` does not exist.** A probe naming it failed
  with `CS0234: The type or namespace name 'GridSplitter' does not exist in the namespace
  'Microsoft.UI.Xaml.Controls'`. WPF's resizable panes have no in-box WinUI counterpart;
  the Community Toolkit ships one, which would be a new package dependency.
- **Everything else this design names does exist**, compile-confirmed in one probe against
  `WinUIDesigner.Shell`: `TabView` (`TabItems`, `SelectedItem`, `SelectedIndex`,
  `TabCloseRequested` with `TabViewTabCloseRequestedEventArgs.Tab`/`.Item`,
  `SelectionChanged`, `IsAddTabButtonVisible`, `AddTabButtonClick`, `CanDragTabs`),
  `TabViewItem` (`Header`, `Content`, `IsClosable`), `TextBox` (`AcceptsReturn`,
  `IsSpellCheckEnabled`, `TextWrapping`, `FontFamily`, and the attached
  `ScrollViewer.HorizontalScrollBarVisibility`), `RadioButton.GroupName`, `InfoBar`,
  `Expander`, `RichEditBox`, `BreadcrumbBar`, `ScrollView`, `ListView`.
- **`ShellModel` already carries the multi-document model.** `Documents`
  (`ObservableCollection<Document>`), `CurrentDocument`, `IsSomethingDirty`,
  `New()`, `Open(path)`, `Save(doc)`, `SaveAs(doc, path)`, `Close(doc)`. `Close` removes
  the document and re-points `CurrentDocument` at `Documents[0]` when it closed the current
  one. `MainWindow` already answers the `AskSaveChanges` event that `Close` raises.
- **`Document` already carries most of what a mode switch needs.** `Text` (get-only today),
  `UpdateXaml()` (public, serializes the live tree through `XamlXmlWriter` and
  `XamlFormatter.Format`), `UpdateDesign()` (private), `LoadFailed`, `LoadErrors`,
  `OutlineRoot`, `DesignSurface`, `DesignContext`, `SelectionService`, `UndoService`,
  `MarkSaved()`, `MarkSuspect()`, `Dispose()`.
- **`Document.UpdateDesign()` already survives unparseable text.** It clears `loadErrors`,
  sets `LoadFailed = false`, installs a `settings.ReportErrors` callback, and catches
  `XamlLoadException` around `LoadDesigner`, setting `LoadFailed = true` and returning.
  Malformed XAML therefore cannot kill the process on a mode switch.
- **`MainWindow.WireCurrentDocument()` already performs the whole document swap** —
  unsubscribing the outgoing selection and undo services, detaching the toolbox, building a
  fresh `PropertyGridControl`, setting `designHost.Content`, seeding the grid from the
  incoming selection, re-attaching the toolbox to the incoming `IToolService`. Only the
  `designHost.Content` line moves in this phase.
- **`XamlError` is `{ string Message; int Line; int Column; }`**
  (`src/WinUIDesigner.DesignModel/Xaml/XamlErrorService.cs:39-44`).
- **A `TextBox` already lives in the shell's visual tree** — `MainWindow.outlineFilter` —
  and `MainWindowInteractionTests` hosts that tree in the test host's shared window today.
  The second `TextBox` this phase adds is therefore not a new class of risk, but see Known
  gaps: it is a measurement that still has to be taken, not a conclusion.

## Decisions

### 1. `TabView` hosts the documents; tabs are built by hand

`TabView.TabItems` is populated from a `ShellModel.Documents.CollectionChanged` handler
rather than through `TabItemsSource`. The rest of this shell is written the same way — no
XAML, no `DataTemplate`, no binding engine — and `TabItemsSource` would need an item
template to turn a `Document` into a header.

*Cost if wrong:* a hand-maintained collection can drift from the model's. The mitigation is
that every mutation of `Documents` goes through `ShellModel`, and `CollectionChanged` fires
for all of them.

### 2. One `DocumentView` per document; a `DesignSurface` is never re-parented

Each `TabViewItem.Content` is that document's own `DocumentView`, which owns the
`ContentControl` holding the document's `DesignSurface`. Today `MainWindow` has a single
`designHost` and re-points its `Content` on every swap; with tabs, a shared host would mean
pulling a live `DesignSurface` out of one parent and pushing it into another on every tab
click.

`MainWindow.designHost` is deleted and its one assignment in `WireCurrentDocument()` goes
with it.

*Cost of the cut:* every open document keeps its own view alive, not only its own design
tree. That is a handful of `DependencyObject`s per document on top of a tree that already
costs hundreds — see Known gaps.

### 3. `Document.Mode` mirrors the reference exactly

A `DocumentMode { Design, Xaml }` enum and a `Mode` property whose setter calls
`UpdateDesign()` when entering Design and `UpdateXaml()` when entering XAML, then raises
`Mode`, `InXamlMode`, and `InDesignMode` — the shape of
`XamlDesigner/Document.cs:51-71`. The reference's extra `PositionXmlElement` line-number
lookup inside that setter is not ported; it feeds the `XamlElementLineInfo`
selection-sync feature, which is out of scope.

`Mode` also has to reach `MainWindow`: entering Design runs `UpdateDesign()`, which builds
a **new** `DesignSurface`, `OutlineRoot`, `SelectionService`, and `UndoService`. Everything
`WireCurrentDocument()` wired is stale afterwards, so `MainWindow` subscribes to the
document's `PropertyChanged` and re-wires on `Mode`. It re-wires only when the document
that raised is the current one; a background tab changing mode must not steal the outline,
property grid, or toolbox from the visible document. A background tab cannot change mode
through the UI — only its own view offers the switch — but it can through a test or a
later caller, and the guard is what keeps that from being a defect.

*Cost if wrong:* skipping the re-wire leaves the outline, property grid, and undo menu
bound to a disposed tree — the failure would look like a dead property grid after one
round trip through XAML mode.

### 4. The editor is a plain `TextBox`

`AcceptsReturn = true`, `TextWrapping = NoWrap`, `IsSpellCheckEnabled = false`, a monospace
`FontFamily`, and `ScrollViewer.HorizontalScrollBarVisibility = Auto` attached. No syntax
highlighting, no line numbers, no folding.

WinUI has no AvalonEdit and no port of one. The two alternatives were weighed and dropped:
a `RichEditBox` with a hand-written XML colorizer means re-colouring and caret-restoring on
every keystroke through an RTF document API, none of it measured on WinUI 3; Monaco in a
WebView2 means bundling the editor's assets and an interop bridge for every operation.

*Cost of the cut:* the XAML view is readable and editable, not pleasant. Upgrading it later
touches one file.

### 5. Text flows both ways, live, guarded by inequality

`TextBox.TextChanged` assigns `Document.Text`; `Document.PropertyChanged` for `"Text"`
assigns `TextBox.Text` only when the two differ. This is the reference's exact shape
(`DocumentView.xaml.cs`), and the inequality check is what stops the two assignments from
feeding each other.

This corrects the "write back on focus loss" sketch presented in chat before the reference
code-behind was read: the reference is live, and live is both simpler and what makes
`Document.IsDirty` track typing.

`Document.Text` gains a setter that sets `IsDirty = true` on a real change.
`UpdateXaml()` keeps writing the backing field directly, because serializing the tree is
not a user edit and must not dirty the document.

*Cost if wrong:* a missing inequality guard is an infinite property-change loop, which
presents as a hung UI thread rather than an exception.

### 6. Switching to Design with unparseable text still switches

The mode change is not refused. `UpdateDesign()` reports through its error callback,
catches `XamlLoadException`, sets `LoadFailed`, and returns; the surface is empty and the
error list fills. `Text` is the source of truth while in XAML mode, so there is no design
tree being thrown away that the text does not already describe.

*Cost if wrong:* a user who mistypes lands on a blank design surface. The error list is
what tells them why, which is why it ships in the same phase.

### 7. The error list is a bottom strip, not a right-column pane

A row across the full window width under the columns, `Height = Auto`, collapsed when the
current document has no errors and collapsed when no document is open at all. The
reference stacks it in its 280px right column above
Properties; here that column already holds the outline filter, the outline, and the
property grid, and a XAML parse message wraps to several lines at 280px.

*Cost of the cut:* a deliberate departure from the reference's layout. The content and the
double-click behaviour are the same.

### 8. Errors come from `Document.LoadErrors`, not a live `XamlErrorService`

The reference binds to `CurrentDocument.XamlErrorService.Errors`, an
`ObservableCollection` that the parser appends to as it runs. This port's `Document`
instead collects into a `List<XamlError>` through `settings.ReportErrors` and exposes it as
`LoadErrors`, refreshed on each `UpdateDesign()`. The pane re-reads it on every re-wire.

*Cost of the cut:* errors raised after the load — Phase B and Phase C both report
save-path failures through `IXamlErrorSink` — do not reach this pane. They are not in
`LoadErrors` today and this phase does not change that.

### 9. Double-clicking an error jumps to it

Matching `ErrorListView.OnMouseDoubleClick` → `Shell.JumpToError` → `DocumentView.JumpToError`:
switch the document to XAML mode, then select the token at the error's line and column.

AvalonEdit gave the reference `Document.GetOffset(line, column)`; `TextBox` has only
`SelectionStart`/`SelectionLength` over a flat string, so the offset is computed by walking
the text for `line - 1` newlines and adding `column - 1`. Out-of-range line or column
selects nothing rather than throwing — the reference swallows an `ArgumentException` for
the same case.

*Cost of the cut:* the reference also widens the selection to the end of the token by
scanning forward for the next space, full stop, less-than, greater-than, or double-quote
character. That scan is ported as written, with one addition the reference did not need:
it stops at the end of the text rather than reading past it.

### 10. The menu gains Close, Close All, and Save All only

Tabs are unusable without a way to close one, and Close All / Save All are the two commands
that only make sense once more than one document is open. Recent Files, Find, Refresh,
Select All, Run, and Render To Bitmap stay out — none of them is a consequence of this
phase's three features.

### 11. The right column and the toolbox do not change

The toolbox, outline filter, outline, and property grid keep their current places, sizes,
and per-document refresh behaviour. `WireCurrentDocument()` keeps building a fresh
`PropertyGridControl` per document for the reason Phase 4f recorded.

### 12. No new package, so panes stay a fixed width

With `GridSplitter` absent from WinUI (measured above), resizable panes would mean taking a
dependency on the Community Toolkit. This project's rule is that a dependency change is its
own decision, and this phase does not ask for one.

## File structure

| File | Change | Responsibility |
|---|---|---|
| `src/WinUIDesigner.Shell/DocumentView.cs` | create | One document's centre pane: design surface, XAML editor, mode switch, `JumpToError`. |
| `src/WinUIDesigner.Shell/ErrorListView.cs` | create | `ListView` of `XamlError`, one row per error, double-click raises a jump request. |
| `src/WinUIDesigner.Shell/DocumentMode.cs` | create | The two-value enum. Its own file so `Document.cs` keeps one type. |
| `src/WinUIDesigner.Shell/Document.cs` | modify | Add `Mode`, `InXamlMode`, `InDesignMode`; give `Text` a setter. |
| `src/WinUIDesigner.Shell/MainWindow.cs` | modify | `TabView` replaces `designHost`; error row added; Close / Close All / Save All added; re-wire on `Mode`. |
| `tests/WinUIDesigner.Shell.Tests/DocumentTests.cs` | modify | Mode round trip; unparseable text. |
| `tests/WinUIDesigner.Shell.Tests/DocumentViewTests.cs` | create | Editor / document text sync, mode visibility, `JumpToError` offsets. |
| `tests/WinUIDesigner.Shell.Tests/MainWindowInteractionTests.cs` | modify | Two documents, two tabs, tab selection, tab close. |
| `tests/WinUIDesigner.Shell.Tests/DocumentObjectBudgetTests.cs` | modify | Re-check the counts now that documents stay open. |

## Testing

Every test runs from the test project's own `.exe` with `--noresult`, never `dotnet test`.

- **`Document`**: setting `Mode = Xaml` fills `Text` from the live tree; editing `Text` and
  setting `Mode = Design` rebuilds the tree with the edit in it; `Text` set to malformed
  XAML followed by `Mode = Design` leaves `LoadFailed` true, `LoadErrors` non-empty, and
  throws nothing; `UpdateXaml()` does not set `IsDirty`, a `Text` assignment does.
- **`DocumentView`**: typing into the editor reaches `Document.Text`; a programmatic
  `Document.Text` change reaches the editor; the two do not loop; design and XAML modes
  each show exactly one of the two children; `JumpToError` on line 3 column 5 of a known
  string selects the expected offset, and an out-of-range line selects nothing.
- **`MainWindow`**: opening two documents produces two tabs; selecting a tab moves
  `CurrentDocument` and re-wires the property grid; requesting a tab close runs the same
  unsaved-changes gate the File > Close path runs; the error row is collapsed for a clean
  document and visible for a failed one.
- **Budget**: `DocumentObjectBudgetTests` re-measured against the new tree.

## Definition of done

1. Two or more documents can be open at once, one tab each, headers tracking
   `Document.Title` including its dirty marker.
2. Selecting a tab makes that document current: outline, property grid, toolbox, undo, and
   window title all follow.
3. Closing a tab runs the existing unsaved-changes gate.
4. File > Close, Close All, and Save All work and are disabled with no document open.
5. Each document switches between Design and XAML independently of the others.
6. An edit made in XAML mode is present in the design tree after switching back, and an
   edit made on the surface is present in the text after switching to XAML.
7. Malformed XAML switched into Design mode produces an empty surface and a populated error
   list, and does not throw.
8. Double-clicking an error switches that document to XAML mode and selects at the reported
   line and column.
9. The error row is collapsed when the current document has no errors, and when no document
   is open.
10. `src/` still contains exactly one `.xaml` file. No new package reference. No hand-written
    `app.manifest`.
11. All four suites green, run from their `.exe`s.

## Known gaps

- **The second `TextBox` in the shell tree is a measurement not yet taken.** CLAUDE.md
  records that a lone `TextBox` in a *second* live window fail-fasts the host with
  `0xC000027B`, while the host's own shared window handles one normally, and that
  `MainWindowInteractionTests` was shaped around exactly that. The shell tree already
  contains `outlineFilter` and those tests pass, so the expectation is that a second one is
  fine — but the expectation is not the measurement, and the implementation has to take it.
- **Every open document holds a live design tree.** CLAUDE.md records that constructing any
  `DependencyObject` permanently commits native memory that is never returned, and that no
  byte budget should be written for anything in this port. Tabs multiply the number of live
  objects by the number of open documents. The response is to re-measure
  `DocumentObjectBudgetTests`' object counts, not to write a memory ceiling.
- **Errors reported after the load do not reach the pane.** Decision 8. The write-path
  reports Phases B and C added go to whatever `IXamlErrorSink` the document's service
  provider holds, not into `LoadErrors`.
- **No syntax highlighting, line numbers, or folding.** Decision 4.
- **Panes cannot be resized, floated, or docked.** Decision 12.
- **The reference's `XamlElementLineInfo` selection sync is not ported** — selecting an
  element on the surface does not move the caret in the XAML view. Decision 3.
- **`UpdateXaml()` runs the whole document through `XamlFormatter.Format` on every switch
  into XAML mode.** Its cost has not been measured on a large document.

## Out of scope

Docking, floating, and resizable panes. The thumbnail view. The behaviors and triggers
editors (a Blend interaction addon assembly that was never ported). Recent files. Find,
Refresh, and Select All. Run and Render To Bitmap. Syntax highlighting. Caret-to-selection
sync between the design surface and the XAML text. A live `XamlErrorService` on `Document`.
Any change to `WinUIDesigner.Designer`, `WinUIDesigner.DesignModel`, or
`WinUIDesigner.XamlDom`.
