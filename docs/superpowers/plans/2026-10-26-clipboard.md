# Cut, Copy and Paste — plan

**Spec:** `docs/superpowers/specs/2026-10-26-clipboard-design.md`

Every task ends with all four suites green from their `.exe`s. This work is written in a Linux
container, so "green" means `.github/workflows/build-and-test.yml` on `windows-latest`. The C# is
compiled locally first against the real WindowsAppSDK 1.7.250606001 reference assemblies, with
the Windows-only packaging targets stubbed out (the stubs are never committed).

## Task 1 — the clipboard seam

New `src/WinUIDesigner.Designer/Services/DesignerClipboard.cs`:

- `public interface IDesignerClipboard`:
  - `void SetXaml(string xaml)`
  - `bool ContainsXaml { get; }`
  - `Task<string> GetXamlAsync()`
  - `event EventHandler ContentChanged`
- `public sealed class SystemDesignerClipboard : IDesignerClipboard`, a singleton `Instance` over
  `Windows.ApplicationModel.DataTransfer.Clipboard`:
  - `SetXaml` builds a `DataPackage` with `SetData("WinUIDesigner.Xaml", xaml)` and `SetText(xaml)`,
    then calls `Clipboard.SetContent`.
  - `ContainsXaml` is `GetContent().Contains(format)`.
  - `GetXamlAsync` awaits `GetDataAsync(format)` and returns null when the format is missing.
  - `ContentChanged` forwards `Clipboard.ContentChanged`, subscribed on first `+=`.
- `internal static class ClipboardPayload`, with `Join(IEnumerable<string>)` and
  `Split(string)` around the delimiter `<!--WinUIDesigner.Delimiter-->`. `Split` drops empty and
  whitespace-only parts.

## Task 2 — `CopyPasteService` implements Copy, Cut and Paste

`src/WinUIDesigner.Designer/Services/CopyPasteService.cs`:

- Constructors: the existing parameterless one uses `SystemDesignerClipboard.Instance`. A new
  `CopyPasteService(IDesignerClipboard)` takes the clipboard to use.
- `CanCopy`: the selection is non-empty and does not contain `RootItem`.
- `Copy`: `ClipboardPayload.Join` of `XamlStaticTools.GetXaml` over the selected items' XamlObjects,
  in `SelectedItems` order, then `SetXaml`.
- `CanCut`: `CanCopy` and `ModelTools.CanDeleteComponents`.
- `Cut`: `Copy`, then `ModelTools.DeleteComponents`.
- `CanPaste`: `RootItem != null` and `clipboard.ContainsXaml`.
- `Paste`: `async void` over `GetXamlAsync` → `PasteXaml`.
- `internal bool PasteXaml(DesignContext, string)`:
  1. Parse each part with `XamlParser.ParseSnippet(root.XamlObject, part, context.ParserSettings)`
     and `RegisterXamlComponentRecursive`. A part that fails to parse is skipped.
  2. Walk candidate containers from the primary selection, or the root, upwards, calling
     `TryStartInsertNewComponents(candidate, items, rects, PlacementType.PasteItem)`, with rects
     `(0, 0, ModelTools.GetWidth, ModelTools.GetHeight)` of each view.
  3. On the first non-null operation, select the items, then `Commit`.
  4. Return whether anything was pasted.

  `XamlDesignContext.ParserSettings` and `_componentService` are reached the way
  `XamlDesignItem.Clone()` reaches them. Both are `internal`, so this task adds `InternalsVisibleTo`
  for `WinUIDesigner.Designer` if DesignModel does not already grant it.

`src/WinUIDesigner.DesignModel/PlacementOperation.cs`: `public const double PasteOffset = 10;`.

`src/WinUIDesigner.Designer/Extensions/CanvasPlacementSupport.cs`: restore the `PasteItem` branch in
`EnterContainer`. For each placed item whose `Canvas.Left` (respectively `Top`) `IsSet`, add
`PasteOffset` to its value. Replace the class comment that recorded the drop.

Tests, `tests/WinUIDesigner.Designer.Tests/CopyPasteServiceTests.cs` (new), with a
`FakeClipboard`:
- Copy writes a payload that `ParseSnippet` accepts alone, carrying the `xmlns`.
- Copy and paste of a Canvas child adds a selected copy at +10/+10 as one undo entry, and Undo
  removes only the copy.
- Cut removes the selection as one undo entry, and Paste brings it back.
- The target walks up: a child control selected pastes into its container; a panel selected pastes
  into the panel; nothing selected pastes into the root.
- A taken `x:Name` gets `_Copy`.
- Two items copied together paste together, in one undo entry.
- `CanCopy`/`CanCut` are false for an empty selection and for the root. `CanPaste` follows the
  clipboard.
- The asynchronous `Paste` path: a completed fake read inserts synchronously.

## Task 3 — the shell

`src/WinUIDesigner.Shell/MainWindow.cs`:

- Give Cut, Copy and Paste the same declining accelerator handler shape Delete and Select All use,
  so they do nothing while `IsTextEntryFocused()`. The menu click paths route through the same
  guarded methods.
- Subscribe to `SystemDesignerClipboard.Instance.ContentChanged` → `RefreshCommands`, marshalled to
  the window's `DispatcherQueue`, and unsubscribe on close.

`tests/WinUIDesigner.Shell.Tests`: a Copy/Paste through the menu seam on a real document, using the
window's own service, would hit the system clipboard. So the shell test covers only enablement:
- Copy is enabled after selecting a child.
- Copy is disabled with nothing selected.

## Task 4 — walkthrough

`.github/walkthrough/steps.txt`: after the selection-sync steps, with the typed button selected:

1. Ctrl+C, then Ctrl+V: a second button at +10/+10, selected.
2. Ctrl+Z: gone.
3. Ctrl+X: the original is gone too.
4. Ctrl+V: back.

Screenshots after each step.

## Results

Recorded below once run.
