# Shell document tabs, XAML source mode, and an error list — design

**Date:** 2026-09-07 (revised 2026-09-08 after two adversarial reviews)
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
`Close(Document)` since the day it was written, and no UI has ever reached any of them.

The design/XAML switch matters for a different reason: this port has no other way to see
what a document actually saves. Phase C found a bug — every skew angle silently discarded
on save — that a user could not have noticed, because the shell never showed them the
XAML. The reference's `DocumentView` puts that text one click away.

## The gap, measured

| Reference `XamlDesigner` | Shell today |
|---|---|
| AvalonDock: floating, dockable, resizable panes | `Grid`, columns fixed at 200 / `*` / 280 |
| Many documents, tabbed (`DocumentsSource="{Binding Documents}"`) | One document; `ShellModel.Documents` exists and is unreachable |
| `DocumentView`: `EnumBar` at the bottom switches Design / XAML; AvalonEdit shows the text | Design only |
| `ErrorListView` bound to `XamlErrorService.Errors` | No pane; `Document.LoadErrors` exists and is unused |
| Thumbnail, Behaviors, Triggers panes | None |
| File: New/Open/**Close**/**Close All**/Save/Save As/**Save All**/**Recent Files**/Exit | New/Open/Save/Save As/Exit |
| Edit: …/**Select All**/**Refresh**/**Find** | Undo/Redo/Cut/Copy/Paste/Delete |
| Test: Run / Render To Bitmap | None |

## Verified against the current codebase and the live test host

Every claim below was measured. Anything not listed here was not measured and must not be
relied on. The measurements marked **live** were taken by running a throwaway NUnit fixture
inside the real test host, not by compiling.

### Platform

- **`Microsoft.UI.Xaml.Controls.GridSplitter` does not exist.** A fully-qualified reference
  fails with `CS0234`; an unqualified one under `using Microsoft.UI.Xaml.Controls;` fails
  with `CS0246`. Both mean absent. The Community Toolkit ships one, which would be a new
  package dependency.
- **Every other type and member this design names compiles**: `TabView` (`TabItems`,
  `SelectedItem`, `SelectedIndex`, `TabCloseRequested` with
  `TabViewTabCloseRequestedEventArgs.Tab`/`.Item`, `SelectionChanged`,
  `IsAddTabButtonVisible`, `AddTabButtonClick`, `CanDragTabs`), `TabViewItem` (`Header`,
  `Content`, `IsClosable`), `TextBox` (`AcceptsReturn`, `IsSpellCheckEnabled`,
  `TextWrapping`, `FontFamily`, attached `ScrollViewer.HorizontalScrollBarVisibility`),
  `RadioButton.GroupName`, `InfoBar`, `Expander`, `RichEditBox`, `BreadcrumbBar`,
  `ScrollView`, `ListView`.
- **live — a `TabView` in the test host is inert.** Hosted in the shared test window it
  measures (1424x720) after one theme key (`TabViewScrollButtonBackground`) is hand-seeded,
  but its template never applies: a `VisualTreeHelper` walk finds **zero children**, a
  selected item's `Content` never enters the visual tree (`ActualWidth` stays 0 through five
  layout passes), `SelectedIndex` and `SelectedItem` disagree with each other, and
  **`SelectionChanged` and `TabCloseRequested` never fire at all** (0 invocations across
  add, select-by-item, select-by-index, and remove). This is the same class of failure
  CLAUDE.md records for `MenuBar` and `TextBox`: no compiled XAML in the test process means
  no merged `resources.pri`, so `XamlControlsResources` cannot load and a Fluent control's
  default template is unreachable. Decisions 1 and 3 exist because of this measurement.
- **live — a bare `TabViewItem` hosted outside a `TabView` fail-fasts the host** with
  `0xC000027B` (STOWED_EXCEPTION). A `TabViewItem` inside a `TabView` does not. No test may
  host one directly.
- **live — a `TabView` used as a strip with the real content in a sibling `ContentControl`
  works.** In a two-row `Grid` (strip on top, `ContentControl` below), the content's payload
  laid out at its natural size and was reachable by a visual-tree walk.
- **live — `TextBox` normalises assigned line endings.** Assigning `a\r\nb\r\nc` (7 chars)
  reads back as `a\rb\rc` (5 chars); the two strings are not equal. Re-assigning the same
  CRLF string raises `TextChanged` zero times, so WinUI compares against its own normalised
  value. Decision 14 exists because of this measurement.
- **live — a bare `TextBox` as the root content of the shared test window throws a
  `COMException` during layout whose `Message` and every `Data` entry are empty**, so
  nothing can be seeded around it — matching CLAUDE.md's note that `TextBox` "names nothing
  and cannot be seeded around". A `TextBox` *inside* the laid-out shell tree does work:
  `MainWindowInteractionTests` finds and drives `outlineFilter` today. The editor must
  therefore be tested at the property level, or inside a parent that lays out, never as bare
  root content.

### Current codebase

- **`ShellModel`**: `Documents`, `CurrentDocument`, `IsSomethingDirty`, `New()`,
  `Open(path)`, `Save(doc)`, `SaveAs(doc, path)`, `Close(doc)`. `Close` refuses a dirty
  document unless its `AskSaveChanges` handler answers something other than `Cancel`, then
  removes it and, if it closed the current one, sets `CurrentDocument` to `Documents[0]` —
  or to `null` when the collection is now empty (`ShellModel.cs:179-181`).
- **`ShellModel.WriteToDisk` calls `doc.UpdateXaml()` unconditionally**
  (`ShellModel.cs:196`) before writing. The reference guards the same call with
  `if (InDesignMode)` (`XamlDesigner/Document.cs:196-203`). Decision 12.
- **`MainWindow.OnAskSaveChanges` always answers `Cancel`** (`MainWindow.cs:628-636`). It is
  a safety net, not a prompt: every real save prompt happens earlier through the async
  `PrepareForCloseAsync` / `AskSaveChangesAsync` pair (`MainWindow.cs:546-565, 638-659`),
  before `ShellModel.Close` is reached. Decision 7.
- **`MainWindow.ReplaceCurrentDocumentAsync` closes the outgoing document**
  (`MainWindow.cs:465-509`), and both `NewAsync` and `OpenAsync` go through it. Its own
  remark says why: "The shell is single-document and has no tab strip, so a document left in
  the collection would be unreachable." Decision 6.
- **`Document`**: `Text` is get-only today (`Document.cs:47-50`); `UpdateXaml()` is public
  and runs `DesignSurface.SaveDesigner` through `XamlXmlWriter` then
  `XamlFormatter.Format` (`Document.cs:142-153`); `UpdateDesign()` is private
  (`Document.cs:193`); `LoadFailed`, `LoadErrors`, `OutlineRoot`, `DesignSurface`,
  `DesignContext`, `SelectionService`, `UndoService`, `MarkSaved()`, `MarkSuspect()`,
  `Dispose()` all exist as this design uses them. `Dispose` is idempotent.
- **`Document` holds one `readonly DesignSurface` for its whole life**
  (`Document.cs:27`); `UpdateDesign()` reloads *into* it rather than replacing it. It does
  build a new `DesignContext`, `SelectionService` and `UndoService` each time, because
  `DesignSurface.LoadDesigner` unloads and disposes the previous ones.
- **`Document.UpdateDesign()` survives unparseable text**: clears `loadErrors`, sets
  `LoadFailed = false`, installs `settings.ReportErrors`, and catches `XamlLoadException`
  around `LoadDesigner`, setting `LoadFailed = true` and returning (`Document.cs:193-230`).
  `UpdateXaml()` early-returns on a null `DesignContext` (`Document.cs:144`), so malformed
  text is not clobbered on the way back into XAML mode.
- **`MainWindow.WireCurrentDocument()`** performs the whole document swap
  (`MainWindow.cs:314-374`), and assigns `designHost.Content` in **two** places — `:337`
  (null, no-document branch) and `:344` (the surface). Both move in this phase.
- **`Document`'s class remark claims "IsDirty is set only by the undo stack, never by the
  Text setter"** (`Document.cs:23`), and `DocumentTests.DirtyTracksTheUndoStackAndNotThe`
  `TextSetter` (`DocumentTests.cs:118`) names it. Decision 14 reverses that; both must be
  rewritten, not left contradicting the code.
- **`XamlError` is `{ string Message; int Line; int Column; }`**
  (`XamlErrorService.cs:39-44`).
- **`XamlFormatter` emits `Environment.NewLine`** (`XamlFormatter.cs:128`), so
  `Document.Text` carries CRLF. It also re-indents and re-sorts attributes
  (`XamlFormatter.cs:85`).
- **`MainWindowInteractionTests`** hosts the shell tree in the test host's *shared* window
  (not `MainWindow`'s own), hand-seeds `TabViewButtonBackground` and
  `TabViewScrollButtonBackground` for `MenuBar`, detaches the property-grid pane before
  every body, and asserts in `OneTimeSetUp` that `MenuBar`, `outlineFilter`, `DesignSurface`
  and `ToolboxView` are all findable in the live tree.

### Reference

- `Document.Mode`'s setter (`XamlDesigner/Document.cs:53-75`) calls `UpdateDesign()` or
  `UpdateXaml()` then raises `Mode`, `InXamlMode`, `InDesignMode`. It has **no equality
  guard**. The `PositionXmlElement` lookup in its else-branch computes a local that is never
  used — dead code; the real `XamlElementLineInfo` assignment lives in `UpdateXaml()`.
- `DocumentMode` is declared `{ Xaml, Design }` — `Xaml` is the zero value.
- `DocumentView` wires `TextChanged` in its `Loaded` handler, *after* setting
  `Document.Mode = DocumentMode.Design`, and syncs text both ways guarded by inequality.
- `Document.SelectionService` returns `null` while in XAML mode
  (`XamlDesigner/Document.cs:159-166`), and the undo handler re-runs `UpdateXaml()` while in
  XAML mode (`XamlDesigner/Document.cs:257-263`).
- `Document.Save()` is `if (InDesignMode) UpdateXaml();` then write.
- `Shell.Open` re-points `CurrentDocument` at an already-open document with the same path
  rather than opening a second copy (`Shell.cs:141-146`).
- `Shell.Save(doc)` routes a document with a null `FilePath` to `SaveAs`
  (`Shell.cs:153-162`), and `SaveAll` is a loop over `Save`.
- `ErrorListView.OnMouseDoubleClick` → `Shell.JumpToError` → `DocumentView.JumpToError`,
  which switches to XAML mode, scrolls, sets the caret, then widens the selection forward to
  the next space, `.`, `<`, `>` or `"`, swallowing `ArgumentException` for an invalid line.

## Decisions

### 1. `TabView` is a tab strip; the document content lives in a sibling host

A two-row centre column: the `TabView` on top carrying one `TabViewItem` per document with
`Content` left null, and a single `ContentControl` below it holding the current document's
`DocumentView`.

The measurement above forces this. If the `DesignSurface` lived inside a `TabViewItem`, it
would be invisible in the test process — `MainWindowInteractionTests`' `OneTimeSetUp`
asserts `FindDescendant<DesignSurface>` is non-null, and every test in that fixture would
die. As a strip, the `TabView` contributes only headers, and everything the tests reach
stays in an ordinary, template-free part of the tree.

*Cost of the cut:* the tab strip and its content are visually separate controls, so the
"page" look of a real `TabView` is lost. Behaviour is unchanged.

### 2. Each `Document` owns one `DocumentView`, and it is only ever in the one host

`MainWindow` keeps a `Document` → `DocumentView` map. Switching documents assigns
`contentHost.Content = view`. A `DocumentView` is in the tree while its document is current
and detached otherwise; a `DesignSurface` is never re-parented, because it stays inside its
own `DocumentView` for that view's whole life.

`MainWindow.designHost` and both of its assignments are deleted; the no-document case sets
`contentHost.Content = null`.

*Cost if wrong:* holding one view per document keeps every open document's tree alive. That
is the point of tabs, and Known gaps records what it costs.

### 3. Every tab interaction runs through an internal method; the events are one-line delegates

`TabView.SelectionChanged` and `TabCloseRequested` **never fire in the test process**
(measured). So the logic lives in `internal void ActivateDocument(Document)` and
`internal Task RequestCloseAsync(Document)`, which tests call directly, and the two event
handlers do nothing but look up the `Document` for the tab and call them.

This is the seam `ReplaceCurrentDocumentAsync(createReplacement, decideClose)` already
established for the close dialog, applied to the same problem one level out.

*Cost if wrong:* logic left inside an event handler is untestable in this repo, and would
ship unverified.

### 4. `TabItems` is mutated only by the `Documents.CollectionChanged` handler

`RequestCloseAsync` never removes a `TabViewItem`. It calls the async close gate; if the
gate proceeds and `ShellModel.Close` removes the document, `CollectionChanged` removes the
tab. A cancelled gate therefore leaves the strip and `Documents` in agreement by
construction.

`MainWindow` subscribes to `Documents.CollectionChanged` **before** its constructor calls
`shell.New()` (`MainWindow.cs:84`), or the first document never gets a tab.

*Cost if wrong:* the strip and the model drift apart, and the drift is invisible until a
user clicks a tab whose document is gone.

### 5. `ShellModel.CurrentDocument` is the only source of truth, and the sync is one-way at a time

The strip reflects `CurrentDocument`; it never defines it. `ActivateDocument` sets
`CurrentDocument` and re-wires; a `CurrentDocument` change pushes `TabView.SelectedItem`.
Both directions run behind one re-entrancy flag, because `ShellModel.Close` re-points
`CurrentDocument` while the `TabView` is reacting to the same removal.

Closing the current tab selects whatever `ShellModel.Close` chose (`Documents[0]`, or none),
not the neighbour. Matching the model is worth more than matching a tab-strip convention.

*Cost if wrong:* an unguarded two-way sync is an infinite recursion on the UI thread.

### 6. `New` and `Open` add a document instead of replacing one

`ReplaceCurrentDocumentAsync` exists only because the shell had no tab strip. With one, both
callers simply create: `NewAsync` calls `shell.New()`, `OpenAsync` calls
`shell.Open(path)`, and neither closes anything.

`ReplaceCurrentDocumentAsync` and its injected-decision overload are deleted, and
`MainWindowCloseOrderingTests` — four tests written entirely around that ordering — are
deleted with them. The ordering they pinned (decide, create, discharge, close) no longer
exists as a sequence; what replaces it is Decision 9's Close All ordering, which gets its
own tests.

`Open` also gains the reference's de-dupe: a path already open makes that document current
instead of opening a second live tree over the same file.

*Cost if wrong:* deleting a test fixture is only safe because the code it covers is gone.
The plan must delete the code and the fixture in the same commit, so no revision of the tree
has an orphaned test.

### 7. Close, Close All and Save All go through the async gate, not `ShellModel.Close`'s event

`ShellModel.Close` raises a synchronous `AskSaveChanges` that `MainWindow` always answers
`Cancel`, because a synchronous handler cannot await a `ContentDialog`. Every new command
therefore asks through `PrepareForCloseAsync` first and only then calls `ShellModel.Close`,
exactly as `OpenAsync` does today.

*Cost if wrong:* a dirty document silently refuses to close and the user is shown nothing.

### 8. Save All prompts Save As for each pathless document, one at a time

`ShellModel.Save` refuses a null `FilePath` and raises an error; `MainWindow.ShowDialogAsync`
returns `null` for a second concurrent dialog, so a naive loop over three unsaved documents
would swallow two failures. Save All awaits each document in turn and routes a pathless one
through the Save As picker, as `Shell.Save(doc)` does in the reference. A cancelled picker
stops the run and reports which documents were not saved.

### 9. Close All decides everything first, then discharges

Ask about every dirty document, collect the answers, and only then close — the ordering
`ConfirmThenCloseAsync` already implements for window close (`MainWindow.cs:603-626`).
Cancel at any prompt abandons the whole operation with nothing closed.

*Cost if wrong:* a Cancel halfway through leaves half the documents closed and no way back.

### 10. `Document.Mode` has an equality guard

The reference's setter is unconditional; this one returns early when the value is unchanged.
The reference can afford it because nothing re-asserts the current mode. This design does:
`JumpToError` sets `Mode = Xaml` on a document that may already be in XAML mode with
unsaved typing in it, and an unguarded setter would run `UpdateXaml()` and overwrite that
typing with a serialization of the pre-edit tree — then select a line and column that refer
to text no longer present.

The same guard is what makes Decision 20's mode-button re-sync safe.

*Cost if wrong:* silent loss of whatever the user typed, at the exact moment they asked to
be shown an error.

### 11. `Design` is `DocumentMode`'s zero value

The reference declares `{ Xaml, Design }`. Here it is `{ Design, Xaml }`, so a freshly
constructed `Document` — whose constructor already runs `UpdateDesign()` — reports
`InDesignMode` without anyone assigning `Mode`. The reference needs
`Document.Mode = DocumentMode.Design` in `DocumentView`'s `Loaded` handler to correct for
its own zero value, which costs a second full `UpdateDesign()` per document opened. No
`DocumentView` may re-load its document on construction.

### 12. Saving while in XAML mode must not re-serialize the tree

`ShellModel.WriteToDisk` guards its `UpdateXaml()` call with the document's mode, matching
the reference's `Document.Save()`. Without it, Ctrl+S in XAML mode overwrites everything the
user typed with a serialization of the design tree they typed it *instead of*.

This is the one change this phase makes to `ShellModel` beyond the additive New/Open path,
and it is the phase's highest-value line: it turns the new feature from data-losing into
correct.

### 13. The editor is a plain `TextBox`

`AcceptsReturn = true`, `TextWrapping = NoWrap`, `IsSpellCheckEnabled = false`, a monospace
`FontFamily`, and `ScrollViewer.HorizontalScrollBarVisibility = Auto` attached. No syntax
highlighting, no line numbers, no folding.

WinUI has no AvalonEdit and no port of one. A `RichEditBox` with a hand-written XML
colorizer means re-colouring and caret-restoring on every keystroke through an RTF document
API, none of it measured on WinUI 3; Monaco in a WebView2 means bundling the editor's assets
and an interop bridge for every operation.

### 14. Text flows both ways, live, compared on normalised line endings

`TextBox.TextChanged` assigns `Document.Text`; `Document.PropertyChanged` for `"Text"`
assigns `TextBox.Text` when the two differ. That is the reference's shape — but the
reference's plain `!=` is not enough here, because **`TextBox` normalises CRLF to CR**
(measured: 7 chars in, 5 chars out, not equal) while `XamlFormatter` emits CRLF. With a raw
comparison, merely opening the XAML view marks a clean document dirty and writes CR-only
line endings to disk.

So: the comparison normalises both sides before deciding, and `Document.Text`'s setter
normalises what it stores to `Environment.NewLine`. `Document.Text` gains a setter that sets
`IsDirty = true` on a change that survives normalisation. `UpdateXaml()` keeps writing the
backing field directly, because serializing the tree is not a user edit.

`TextChanged` is subscribed **after** the editor's initial text is assigned, as the
reference does in its `Loaded` handler. Subscribing first makes the seeding assignment echo
back and open every document dirty.

This reverses the Phase 5 rule recorded in `Document.cs:23` and named by
`DocumentTests.DirtyTracksTheUndoStackAndNotTheTextSetter`. Both must be rewritten in the
same change.

### 15. In XAML mode the text is the truth, and the model side is closed off

Two ports from the reference, neither optional:

- `Document.SelectionService` returns `null` while in XAML mode, so the property grid and
  outline have nothing to edit through.
- `Document`'s undo handler re-runs `UpdateXaml()` when a change lands while in XAML mode,
  so a mutation that reaches the tree anyway is reflected into the text instead of being
  silently reverted on the next switch to Design.

Without both, a user in XAML mode can still change a property through the right column: the
tree mutates, `IsDirty` goes true, `Text` stays stale, and the next switch to Design reloads
from `Text` and throws the edit away.

### 16. Switching to Design with unparseable text still switches

The mode change is not refused. `UpdateDesign()` reports through its error callback, catches
`XamlLoadException`, sets `LoadFailed`, and returns; the surface is empty and the error list
fills. `UpdateXaml()`'s null-context early return means the malformed text is preserved on
the way back to XAML mode.

### 17. Undo history does not survive a mode round trip, and that is accepted

`UpdateDesign()` goes through `DesignSurface.LoadDesigner`, which unloads and disposes every
service including the `UndoService`. A user who looks at the XAML and comes back cannot undo
edits made before the trip. The reference behaves the same way. Recorded here so it is a
decision rather than a surprise, and stated in Known gaps.

`MainWindow`'s re-wire happens after that teardown, so its `OnSelectionChanged` and
`OnUndoStackChanged` handlers must tolerate being called while the old context is being
disposed — they already null-check, and the plan must not remove those checks.

### 18. The error list is a height-capped bottom strip

A row across the full window width under the columns, `Height = Auto` with an explicit
`MaxHeight = 160`, collapsed when the current document has no errors and when no document is
open.

The cap is not cosmetic. CLAUDE.md records a measured case of a `ListView` in an
unconstrained parent realising every container and growing to 6360px; an `Auto` row hands it
exactly that unbounded height, and a document with two hundred parse errors would push the
whole three-column area off screen.

The reference stacks this pane in its 280px right column above Properties; here that column
already holds three things and a parse message wraps several times at 280px.

### 19. Errors come from `Document.LoadErrors`, and each row is a display wrapper

`LoadErrors` is refreshed by every `UpdateDesign()`; the pane re-reads it on each re-wire and
on each `Mode` change. With no XAML there is no `DataTemplate`, so the `ListView` binds
through `DisplayMemberPath` to a small public class of this shell's own holding the
formatted `"Line, Column — Message"` string and the `XamlError` it came from — the pattern
`ToolboxView` already uses with `ControlNode`. The double-click handler reads the wrapper
from `ListView.SelectedItem`.

*Cost of the cut:* errors raised after the load — the save-path reports Phases B and C added
through `IXamlErrorSink` — never reach this pane.

### 20. Double-clicking an error jumps to it

Switch that document to XAML mode, then select at the error's line and column.

The offset is computed **against `TextBox.Text`**, not `Document.Text`: the two differ by
one character per preceding line (Decision 14), so computing against the document's text
puts a line-40 error 39 characters early. Walk for `line - 1` line breaks, add `column - 1`,
then widen forward to the next space, `.`, `<`, `>` or `"`, stopping at the end of the text.
An out-of-range line or column selects nothing rather than throwing.

The mode switch and the selection happen in the same tick. The reference needed an
`await Task.Delay(70)` here because AvalonEdit was not ready; whether a plain `TextBox`
needs the same has not been measured, and the plan must measure it rather than copy the
delay or assume it away.

### 21. Mode buttons are per-document, with unique group names and a suppression flag

The mode switch is two `RadioButton`s in a bottom bar, mirroring the reference's `EnumBar`.
Radio groups resolve by name across the whole visual root, so every `DocumentView` uses a
group name unique to its instance — otherwise checking XAML in one tab unchecks Design in
another. Pushing `Mode` back into `IsChecked` runs behind a suppression flag so the
resulting `Checked` event does not re-enter the `Mode` setter.

`DocumentView`'s own layout is a two-row `Grid`: row 0 is a `Grid` holding the design
surface inside its own `ScrollViewer` and the editor `TextBox` beside it, exactly one of the
two visible; row 1 is the mode bar. The surface's `ScrollViewer` is the one `MainWindow`
has today (`MainWindow.cs:114-121`) moved down with it. The `TextBox` must **not** be inside
that `ScrollViewer` — it has its own, and nesting is the unbounded-height trap again.

### 22. Subscriptions are taken per document, on add, and released on remove

`MainWindow` subscribes to every `Document`'s `PropertyChanged` when
`CollectionChanged` adds it and unsubscribes when it removes it — not only for the current
document. A background document's `Title` changes through `MarkSaved()` (Save All),
`FilePath` (Save As) and `SetTempName`, and its tab header has to follow.

The handler filters: `Title` and `Name` update that document's tab header; `Mode` re-wires
**only if the raising document is the current one**, so a background tab changing mode
cannot steal the outline, property grid or toolbox from the visible document; `Text` is not
handled here (the `DocumentView` handles its own).

`WireCurrentDocument()`'s existing comment records that a missed unsubscribe leaks a handler
per document. This decision multiplies that risk by the number of open documents, which is
why the rule is written down rather than left to the implementer.

### 23. The right column, the toolbox, and the package set do not change

The toolbox, outline filter, outline and property grid keep their places, sizes and
per-document refresh behaviour, including the fresh `PropertyGridControl` per document that
Phase 4f's finding requires. With `GridSplitter` absent, resizable panes would mean a new
Community Toolkit dependency, and a dependency change is its own decision.

## File structure

| File | Change | Responsibility |
|---|---|---|
| `src/WinUIDesigner.Shell/DocumentMode.cs` | create | The two-value enum, `Design` first. |
| `src/WinUIDesigner.Shell/DocumentView.cs` | create | One document's centre pane: surface in its `ScrollViewer`, XAML editor, mode bar, `JumpToError`. |
| `src/WinUIDesigner.Shell/ErrorListView.cs` | create | Height-capped `ListView` over error display wrappers; raises a jump request on double-click. |
| `src/WinUIDesigner.Shell/Document.cs` | modify | `Mode` with an equality guard, `InXamlMode`, `InDesignMode`; `Text` setter with CRLF normalisation; `SelectionService` null in XAML mode; undo handler re-serializes in XAML mode; class remark rewritten. |
| `src/WinUIDesigner.Shell/ShellModel.cs` | modify | `WriteToDisk` guards `UpdateXaml()` on mode; `Open` de-dupes by path; `SaveAll`/`CloseAll` support. |
| `src/WinUIDesigner.Shell/MainWindow.cs` | modify | Tab strip + content host replace `designHost`; `ActivateDocument`/`RequestCloseAsync` seams; per-document subscriptions; error row; Close / Close All / Save All; `ReplaceCurrentDocumentAsync` deleted. |
| `tests/WinUIDesigner.Shell.Tests/DocumentTests.cs` | modify | Mode round trip, malformed text, dirty-on-text-set (replacing the rule it reverses). |
| `tests/WinUIDesigner.Shell.Tests/DocumentViewTests.cs` | create | Text sync incl. line endings, mode visibility, `JumpToError` offsets. |
| `tests/WinUIDesigner.Shell.Tests/ShellModelTests.cs` | modify | Save-in-XAML-mode does not re-serialize; `Open` de-dupe. |
| `tests/WinUIDesigner.Shell.Tests/MainWindowInteractionTests.cs` | modify | Two documents, `ActivateDocument`, `RequestCloseAsync`, error row visibility. |
| `tests/WinUIDesigner.Shell.Tests/MainWindowCloseOrderingTests.cs` | delete | Covers `ReplaceCurrentDocumentAsync`, which this phase removes. Same commit as the removal. |

## Testing

Every test runs from the test project's own `.exe` with `--noresult`, never `dotnet test`.

- **`Document`**: `Mode = Xaml` fills `Text` from the live tree; editing `Text` then
  `Mode = Design` rebuilds the tree with the edit; malformed text then `Mode = Design`
  leaves `LoadFailed` true, `LoadErrors` non-empty, throws nothing, and does not clobber
  `Text`; re-asserting the current `Mode` is a no-op (Decision 10); `UpdateXaml()` does not
  dirty, a `Text` assignment does, and a CRLF-vs-CR-only difference does **not**;
  `SelectionService` is null in XAML mode; an undo-stack change in XAML mode re-runs
  `UpdateXaml`.
- **`DocumentView`**: typing reaches `Document.Text`; a programmatic `Document.Text` change
  reaches the editor; the pair does not loop and does not dirty a clean document; exactly one
  of surface and editor is visible per mode; `JumpToError` on a known string selects the
  expected offset, an out-of-range line selects nothing, and the token-widening stops at the
  end of the text. Driven at the property level, not through layout — a bare `TextBox` cannot
  be root content in this process (measured).
- **`ShellModel`**: saving a document in XAML mode writes what the user typed, not a
  re-serialization; `Open` on an already-open path makes it current instead of adding;
  `SaveAll` on a pathless document asks once and stops on cancel.
- **`MainWindow`**: opening two documents produces two tabs and leaves both alive;
  `ActivateDocument` moves `CurrentDocument` and re-wires; `RequestCloseAsync` with a
  Cancel decision removes neither the tab nor the document; with a Discard decision removes
  both; a background document's `Title` change updates its own tab header; the error row is
  collapsed for a clean document, for no document, and visible for a failed one; Close /
  Close All / Save All are disabled with no document open. All driven through the internal
  seams, because the `TabView`'s own events do not fire in this process (measured).

**Not provable in-process, and stated as such rather than left implied:** anything requiring
the `TabView` to render (header text, click-to-select, the close button), and anything
requiring the property grid to be live — `MainWindowInteractionTests` detaches that pane for
the whole fixture. Those stay manual-walkthrough items, the way the `MenuBar` test already
is.

## Definition of done

1. Two or more documents open at once, one tab each, headers following `Document.Title`
   including its dirty marker — including for the document that is not current.
2. `ActivateDocument` makes a document current: outline, property grid, toolbox, undo state
   and window title all follow.
3. Closing a tab runs the async unsaved-changes gate; a cancelled gate leaves the tab and the
   document both in place.
4. File > Close, Close All and Save All work, ask through the async gate, and are disabled
   with no document open.
5. Each document switches Design ↔ XAML independently of the others.
6. An edit made in XAML mode is in the design tree after switching back; an edit made on the
   surface is in the text after switching to XAML.
7. Saving while in XAML mode writes the user's text, not a re-serialization of the tree.
8. Malformed XAML switched into Design mode produces an empty surface and a populated error
   list, throws nothing, and does not destroy the text.
9. Double-clicking an error switches that document to XAML mode and selects at the reported
   line and column, and does not overwrite unsaved typing on the way.
10. The error row is collapsed with no errors and with no document, and never taller than its
    cap.
11. `src/` still contains exactly one `.xaml` file. No new package reference. No hand-written
    `app.manifest`.
12. All four suites green, run from their `.exe`s. `MainWindowCloseOrderingTests` is gone in
    the same commit that removes the code it covered.

## Known gaps

- **`TabView` renders nothing in the test process.** Measured above. Every assertion about
  tabs is made through the internal seams or against `ShellModel`; nothing proves the strip
  actually draws. That is a manual walkthrough item.
- **Every open document holds a live design tree and a live view.** CLAUDE.md records that
  constructing any `DependencyObject` permanently commits native memory that is never
  returned, and that no byte budget should be written for anything in this port.
  `DocumentObjectBudgetTests` cannot see this: it constructs a bare `Document` with no
  window, tab or view, so tabs cannot change its numbers. If the per-document view cost is
  worth pinning, it needs its own count of the objects a `DocumentView` builds — otherwise
  the honest position is that this is unmeasured.
- **Undo history does not survive a mode round trip.** Decision 17.
- **Entering XAML mode replaces the file's text with a re-serialization.** `UpdateXaml()`
  round-trips through `XamlXmlWriter` and `XamlFormatter`, which re-indents, re-sorts
  attributes, and drops anything the serializer does not model — comments included. Merely
  looking at a hand-authored file's XAML therefore changes it in memory, and the next save
  writes the reformatted version. True today through `WriteToDisk`; this phase is the first
  time a user can see it happen.
- **Errors reported after the load do not reach the pane.** Decision 19.
- **Whether the editor needs a delay before `JumpToError`'s selection lands is unmeasured.**
  Decision 20 requires the plan to measure it.
- **No syntax highlighting, line numbers or folding.** Decision 13.
- **Panes cannot be resized, floated or docked.** Decision 23.
- **The reference's `XamlElementLineInfo` selection sync is not ported** — selecting an
  element on the surface does not move the caret in the XAML view.
- **Every mode toggle on the current document allocates a fresh `PropertyGridControl`.**
  Decision 22 routes a `Mode` change on the current document through the same
  `WireCurrentDocument()` that Decision 23's per-document grid rebuild lives in, so each
  Design ↔ XAML switch throws the old grid away and builds another. CLAUDE.md records that
  constructing any `DependencyObject` permanently commits native memory that is never
  returned, so a user toggling the two views repeatedly commits memory nothing gives back.
  Both decisions are implemented as written and this is what they compose to; recorded
  rather than fixed, because separating the two re-wires is a design change, not a defect
  fix.
- **Whether the editor's font is actually monospaced is unverified.** `DocumentView` sets
  `FontFamily` from the string `"Cascadia Mono, Consolas, Courier New"`, and WinUI's
  `FontFamily` accepts any string without validating it, so if none of the three resolves the
  editor falls back to the proportional UI font in silence and Decision 13's "monospace"
  quietly does not hold. One look during the manual walkthrough settles it.
- **`JumpToError`'s own body is only half covered.** The refusal branch — selecting nothing
  once the buffer is no longer the text the coordinates were reported against — has a test,
  as do the `OffsetOf` and `TokenLength` helpers. The path that matters to the user, a mode
  switch followed by a selection landing in a live editor, is proven by no automated test:
  a `TextBox` cannot be root content in this test process, so nothing here lays one out.
  Manual walkthrough item, with the two — refusal and selection — worth checking together.

## Out of scope

Docking, floating and resizable panes. The thumbnail view. The behaviors and triggers
editors (a Blend interaction addon assembly that was never ported). Recent files. Find,
Refresh and Select All. Run and Render To Bitmap. Syntax highlighting. Caret-to-selection
sync from the design surface into the XAML text. A live `XamlErrorService` on `Document`.
Any change to `WinUIDesigner.Designer`, `WinUIDesigner.DesignModel` or
`WinUIDesigner.XamlDom`.
