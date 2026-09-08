# Shell Document Tabs, XAML Source Mode, and Error List — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the shell hold several documents open at once as tabs, switch each between the design surface and its XAML text, and list the parse errors a document reported.

**Architecture:** A `TabView` is used as a header strip only — measured to be inert in the test process — with each document's `DocumentView` shown in a sibling `ContentControl` below it. `Document` gains a `Mode` that reloads the tree or re-serializes the text, `ShellModel` stops re-serializing over a user's typing on save, and every tab interaction goes through an internal method so it can be driven without the `TabView`'s own events, which never fire in tests.

**Tech Stack:** C# on `net10.0-windows10.0.19041.0`, WinUI 3 via `Microsoft.WindowsAppSDK` 1.7.250606001, NUnitLite run from each test project's own `.exe`.

**Spec:** `docs/superpowers/specs/2026-09-07-shell-document-tabs-design.md` (committed `695f644`)

## Global Constraints

- Target framework `net10.0-windows10.0.19041.0`, `UseWinUI=true`, `Microsoft.WindowsAppSDK` pinned to `1.7.250606001`. **Do not bump it.** No new `PackageReference` of any kind.
- `Nullable` is `disable`. `RuntimeIdentifier` is `win-x64`.
- `src/` must still contain exactly one `.xaml` file, `src/WinUIDesigner.Shell.App/App.xaml`. Do not add a second. Do not add a hand-written `app.manifest`.
- **Tests run from each test project's built `.exe` with `--noresult`, never `dotnet test`.** `dotnet test` reports every WinUI-dependent test as *ignored*, so a large skip count is not a green run.
  `tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult`
  Single test: append `--test=WinUIDesigner.Shell.Tests.<Fixture>.<Method>`.
- Build with `dotnet build WinUIDesigner.sln`.
- Indentation in `src/WinUIDesigner.Shell/**` and `tests/**` is **4 spaces** (only `src/WinUIDesigner.XamlDom/*.cs` uses tabs). No file in this plan is SharpDevelop-derived, so no MIT header is added.
- Dependency direction `Shell.App → Shell → Designer → DesignModel → XamlDom` must not be violated. **No change to `WinUIDesigner.Designer`, `WinUIDesigner.DesignModel` or `WinUIDesigner.XamlDom` in this plan.**
- Every method on `ShellModel` must remain non-throwing (Phase 5 Decision 17).
- Two NUnit reporting artifacts, both measured, neither a real failure: an assertion failing inside `WinUITestHost.Run` is listed **twice**, once against a different test in the same fixture — read the stack trace, not the count. And `Console.WriteLine` from inside a `Run` body only reaches the output of the *first* test in a process to start the dispatcher thread.

## Measured platform facts this plan is built on

Do not re-litigate these; they were measured live in the test host and are why the design is shaped as it is.

- A `TabView` in the test process **lays out but never applies its template**: zero visual children, a selected item's `Content` never enters the visual tree, `SelectedIndex` and `SelectedItem` disagree, and `SelectionChanged` / `TabCloseRequested` **never fire**. It needs `TabViewScrollButtonBackground` hand-seeded to lay out at all.
- A bare `TabViewItem` hosted **outside** a `TabView` fail-fasts the process with `0xC000027B`. Never host one directly in a test.
- `TextBox` normalises assigned `\r\n` to lone `\r` (7 chars in, 5 out; the strings are not equal).
- A bare `TextBox` as the **root** content of the shared test window throws a `COMException` whose `Message` and every `Data` entry are empty, so nothing can be seeded around it. A `TextBox` inside the laid-out shell tree works — `MainWindowInteractionTests` drives `outlineFilter` today.

## File structure

| File | Change | Responsibility |
|---|---|---|
| `src/WinUIDesigner.Shell/DocumentMode.cs` | create | Two-value enum, `Design` first. |
| `src/WinUIDesigner.Shell/Document.cs` | modify | `Mode` with equality guard, `Text` setter with line-ending normalisation, `SelectionService` null in XAML mode, undo re-serializes in XAML mode. |
| `src/WinUIDesigner.Shell/ShellModel.cs` | modify | `WriteToDisk` guards `UpdateXaml()`; `Open` de-dupes by path. |
| `src/WinUIDesigner.Shell/DocumentView.cs` | create | One document's centre pane: surface in a `ScrollViewer`, XAML editor, mode bar, `JumpToError`. |
| `src/WinUIDesigner.Shell/ErrorListView.cs` | create | Height-capped `ListView` over `XamlErrorNode`; raises a jump request. |
| `src/WinUIDesigner.Shell/MainWindow.cs` | modify | Tab strip + content host, `ActivateDocument` / `RequestCloseAsync` seams, per-document subscriptions, additive New/Open, Close/Close All/Save All, error row. |
| `tests/WinUIDesigner.Shell.Tests/DocumentTests.cs` | modify | +6 tests; one existing test rewritten. |
| `tests/WinUIDesigner.Shell.Tests/ShellModelTests.cs` | modify | +3 tests. |
| `tests/WinUIDesigner.Shell.Tests/DocumentViewTests.cs` | create | 6 tests. |
| `tests/WinUIDesigner.Shell.Tests/ErrorListViewTests.cs` | create | 3 tests. |
| `tests/WinUIDesigner.Shell.Tests/MainWindowInteractionTests.cs` | modify | +9 tests across Tasks 5–7. |
| `tests/WinUIDesigner.Shell.Tests/MainWindowCloseOrderingTests.cs` | delete | Covers `ReplaceCurrentDocumentAsync`, removed in Task 5. |

**Test count ledger.** Baseline is **47** (`DocumentObjectBudget` 3, `Document` 7, `MainWindowCloseOrdering` 4, `MainWindowInteraction` 7, `NewDocumentTemplate` 1, `ShellMetadata` 3, `ShellModel` 10, `Toolbox` 7, `ToolboxView` 5). After Task 1: **53**. Task 2: **56**. Task 3: **62**. Task 4: **65**. Task 5: 65 − 4 (fixture deleted) + 4 = **65**. Task 6: **68**. Task 7: **70**. Final run: `Test Count: 70, Passed: 69, Failed: 0, Skipped: 1` — the one skip is the pre-existing `[Explicit]` focus-guard test in `MainWindowInteractionTests`.

---

### Task 1: Document mode, and text that can be written to

**Files:**
- Create: `src/WinUIDesigner.Shell/DocumentMode.cs`
- Modify: `src/WinUIDesigner.Shell/Document.cs` (class remark at `:15-24`, `Text` at `:47-50`, `SelectionService` at `:131-134`, `OnUndoStackChanged` at `:251-255`)
- Test: `tests/WinUIDesigner.Shell.Tests/DocumentTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `public enum DocumentMode { Design, Xaml }`; `Document.Mode` (get/set), `Document.InDesignMode`, `Document.InXamlMode` (get bool); `Document.Text` now has a setter; `internal static string Document.NormaliseLineEndings(string)`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/WinUIDesigner.Shell.Tests/DocumentTests.cs`, inside the fixture. Note `MalformedXaml` is a new constant — put it beside the two existing ones at the top of the class.

```csharp
        const string MalformedXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Button Width=\"75\"";
```

```csharp
        [Test]
        public void SwitchingToXamlModeFillsTextFromTheLiveTree()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    var button = doc.DesignContext.RootItem.ContentProperty.CollectionElements[0];
                    button.Properties["Width"].SetValue(123.0);

                    doc.Mode = DocumentMode.Xaml;

                    Assert.That(doc.InXamlMode, Is.True);
                    Assert.That(doc.InDesignMode, Is.False);
                    StringAssert.Contains("123", doc.Text, "the live edit should be in the text");
                }
            });
        }

        [Test]
        public void AnEditMadeInXamlModeIsInTheTreeAfterSwitchingBack()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.Mode = DocumentMode.Xaml;
                    doc.Text = doc.Text.Replace("Width=\"75\"", "Width=\"200\"");
                    doc.Mode = DocumentMode.Design;

                    Assert.That(doc.LoadFailed, Is.False, "the edited text should still parse");
                    var button = (Button)doc.DesignContext.RootItem.ContentProperty
                        .CollectionElements[0].Component;
                    Assert.That(button.Width, Is.EqualTo(200.0));
                }
            });
        }

        [Test]
        public void ReAssertingTheCurrentModeIsANoOpAndKeepsUnsavedText()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.Mode = DocumentMode.Xaml;
                    var typed = doc.Text + "<!-- typed -->";
                    doc.Text = typed;

                    // Decision 10: without the equality guard this re-runs UpdateXaml and
                    // silently replaces the typing with a serialization of the untouched tree.
                    doc.Mode = DocumentMode.Xaml;

                    Assert.That(doc.Text, Is.EqualTo(typed));
                }
            });
        }

        [Test]
        public void MalformedTextSwitchedToDesignModeReportsAndKeepsTheText()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.Mode = DocumentMode.Xaml;
                    doc.Text = MalformedXaml;

                    Assert.DoesNotThrow(() => doc.Mode = DocumentMode.Design,
                        "the write path must never throw - a throw here kills the app mid-edit");
                    Assert.That(doc.LoadFailed, Is.True);
                    Assert.That(doc.LoadErrors, Is.Not.Empty);
                    Assert.That(doc.Text, Is.EqualTo(Document.NormaliseLineEndings(MalformedXaml)),
                        "UpdateXaml early-returns on a null context, so the bad text survives");
                }
            });
        }

        [Test]
        public void TheSelectionServiceIsClosedOffInXamlMode()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    Assert.That(doc.SelectionService, Is.Not.Null, "design mode exposes it");
                    doc.Mode = DocumentMode.Xaml;
                    Assert.That(doc.SelectionService, Is.Null,
                        "Decision 15: the right column must have nothing to edit through while " +
                        "the text is the source of truth");
                    doc.Mode = DocumentMode.Design;
                    Assert.That(doc.SelectionService, Is.Not.Null);
                }
            });
        }

        [Test]
        public void AChangeThatLandsWhileInXamlModeIsReflectedIntoTheText()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    doc.Mode = DocumentMode.Xaml;
                    var before = doc.Text;

                    var button = doc.DesignContext.RootItem.ContentProperty.CollectionElements[0];
                    button.Properties["Width"].SetValue(321.0);

                    Assert.That(doc.Text, Is.Not.EqualTo(before));
                    StringAssert.Contains("321", doc.Text,
                        "Decision 15: a mutation that reaches the tree in XAML mode must be " +
                        "serialized into the text, not silently reverted on the next switch");
                }
            });
        }
```

Then **rewrite** the existing `DirtyTracksTheUndoStackAndNotTheTextSetter` (currently at `DocumentTests.cs:107-129`) — its whole point is reversed by Decision 14. Replace the method entirely with:

```csharp
        [Test]
        public void DirtyTracksTheUndoStackAndARealTextEditButNotSerializationOrLineEndings()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    Assert.That(doc.IsDirty, Is.False, "a freshly loaded document is clean");

                    doc.UpdateXaml();
                    Assert.That(doc.IsDirty, Is.False,
                        "serializing the tree is not a user edit and writes the backing field");

                    // Decision 14: TextBox hands back lone CR where XamlFormatter emitted CRLF
                    // (measured). Assigning that back must not dirty a document nobody touched.
                    doc.Text = doc.Text.Replace("\r\n", "\r");
                    Assert.That(doc.IsDirty, Is.False,
                        "a line-ending-only difference is not an edit");

                    doc.Text = doc.Text + "<!-- real edit -->";
                    Assert.That(doc.IsDirty, Is.True, "a real text change marks the document dirty");

                    doc.MarkSaved();
                    Assert.That(doc.IsDirty, Is.False);

                    var canvas = doc.DesignContext.RootItem;
                    var button = canvas.ContentProperty.CollectionElements[0];
                    button.Properties["Width"].SetValue(120.0);
                    Assert.That(doc.IsDirty, Is.True, "a committed change marks the document dirty");
                }
            });
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet build WinUIDesigner.sln
```
Expected: **build errors**, not test failures — `DocumentMode` does not exist, `Document.Text` has no setter, `Document.Mode` does not exist, `Document.NormaliseLineEndings` does not exist. That is the correct "red" for this task.

- [ ] **Step 3: Create the enum**

`src/WinUIDesigner.Shell/DocumentMode.cs`:

```csharp
namespace WinUIDesigner.Shell
{
    /// <summary>Which view of a document is showing.</summary>
    /// <remarks>
    /// <c>Design</c> is deliberately the zero value, unlike the reference's
    /// <c>{ Xaml, Design }</c>. A <see cref="Document"/>'s constructor already runs
    /// <c>UpdateDesign()</c>, so with Design first a fresh document reports
    /// <see cref="Document.InDesignMode"/> without anyone assigning <see cref="Document.Mode"/>.
    /// The reference has to correct for its own zero value by assigning
    /// <c>Mode = DocumentMode.Design</c> when its view loads, which costs a second full
    /// <c>UpdateDesign()</c> per document opened. Spec Decision 11.
    /// </remarks>
    public enum DocumentMode
    {
        Design,
        Xaml
    }
}
```

- [ ] **Step 4: Change `Document`**

Replace the class remark (`Document.cs:15-24`) — its last clause is now false:

```csharp
    /// <summary>
    /// One open document: the text, the design surface built from it, the mode the user is
    /// viewing it in, and the dirty flag.
    /// </summary>
    /// <remarks>
    /// Ported from the reference's Document.cs with four deliberate deviations: Dispose
    /// unsubscribes and unloads (the reference does neither, and the soak test depends on both);
    /// the constructor does not assign FilePath through a side-effecting property; the file write
    /// lives in ShellModel, not here; and <see cref="Mode"/>'s setter has an equality guard the
    /// reference's does not, because this shell re-asserts the current mode from JumpToError and
    /// from its mode buttons, and an unguarded setter would re-serialize over unsaved typing.
    /// <para>
    /// IsDirty is raised by the undo stack and by a real assignment to <see cref="Text"/>. It is
    /// NOT raised by <see cref="UpdateXaml"/>, which writes the backing field directly because
    /// serializing the tree is not a user edit, nor by a change that only differs in line
    /// endings - WinUI's TextBox hands back lone CR where XamlFormatter emitted CRLF, measured.
    /// </para>
    /// </remarks>
```

Add the field beside the other private fields (`Document.cs:30-34`):

```csharp
        DocumentMode mode;
```

Replace the `Text` property (`Document.cs:47-50`):

```csharp
        /// <summary>
        /// The document's XAML text. Assigning a value that differs by more than line endings is
        /// a user edit and marks the document dirty; <see cref="UpdateXaml"/> bypasses this
        /// setter deliberately.
        /// </summary>
        public string Text
        {
            get { return text; }
            set
            {
                var normalised = NormaliseLineEndings(value);
                if (text == normalised)
                    return;
                text = normalised;
                IsDirty = true;
                RaisePropertyChanged("Text");
            }
        }

        /// <summary>
        /// Rewrites every CR, LF and CRLF in <paramref name="value"/> as
        /// <see cref="Environment.NewLine"/>.
        /// </summary>
        /// <remarks>
        /// Measured: assigning "a\r\nb\r\nc" to a WinUI TextBox reads back as "a\rb\rc" - seven
        /// characters in, five out, and the two strings are not equal. XamlFormatter emits
        /// Environment.NewLine. Without this, merely opening a document's XAML view would mark it
        /// dirty and put lone-CR line endings on disk. Spec Decision 14.
        /// </remarks>
        internal static string NormaliseLineEndings(string value)
        {
            if (value == null)
                return null;
            if (value.IndexOf('\r') < 0 && value.IndexOf('\n') < 0)
                return value;

            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\r')
                {
                    sb.Append(Environment.NewLine);
                    if (i + 1 < value.Length && value[i + 1] == '\n')
                        i++;
                }
                else if (c == '\n')
                {
                    sb.Append(Environment.NewLine);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>Which view the user is looking at. Spec Decision 10.</summary>
        public DocumentMode Mode
        {
            get { return mode; }
            set
            {
                if (mode == value)
                    return;
                mode = value;

                if (mode == DocumentMode.Design)
                    UpdateDesign();
                else
                    UpdateXaml();

                RaisePropertyChanged("Mode");
                RaisePropertyChanged("InXamlMode");
                RaisePropertyChanged("InDesignMode");
                RaisePropertyChanged("SelectionService");
            }
        }

        public bool InXamlMode
        {
            get { return mode == DocumentMode.Xaml; }
        }

        public bool InDesignMode
        {
            get { return mode == DocumentMode.Design; }
        }
```

Replace `SelectionService` (`Document.cs:131-134`):

```csharp
        /// <summary>
        /// The live selection service, or null while in XAML mode.
        /// </summary>
        /// <remarks>
        /// Null in XAML mode on purpose, ported from the reference (XamlDesigner/Document.cs:159-166).
        /// The text is the source of truth there, so a property-grid or outline edit that reached
        /// the tree would be thrown away by the next switch back to Design. Spec Decision 15.
        /// </remarks>
        public ISelectionService SelectionService
        {
            get
            {
                if (InXamlMode)
                    return null;
                return DesignContext == null ? null : DesignContext.Services.Selection;
            }
        }
```

Replace `OnUndoStackChanged` (`Document.cs:251-255`):

```csharp
        void OnUndoStackChanged(object sender, EventArgs e)
        {
            IsDirty = true;

            // A change that reaches the tree while the user is looking at the text must be
            // serialized into that text, or the next switch to Design reloads from stale text and
            // discards it. Ported from the reference (XamlDesigner/Document.cs:257-263).
            // Spec Decision 15.
            if (InXamlMode)
                UpdateXaml();
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```
Expected: `Test Count: 53, Passed: 52, Failed: 0, Skipped: 1`.

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.Shell/DocumentMode.cs src/WinUIDesigner.Shell/Document.cs tests/WinUIDesigner.Shell.Tests/DocumentTests.cs
git commit -m "feat: give Document a Design/XAML mode and a writable Text"
```

---

### Task 2: Stop the save path from overwriting what the user typed

**Files:**
- Modify: `src/WinUIDesigner.Shell/ShellModel.cs` (`Open` at `:105-135`, `WriteToDisk` at `:186-205`)
- Test: `tests/WinUIDesigner.Shell.Tests/ShellModelTests.cs`

**Interfaces:**
- Consumes: `Document.Mode`, `Document.InDesignMode`, `Document.Text` setter (Task 1).
- Produces: no new public members. `ShellModel.Open` now returns `true` without adding when the path is already open.

- [ ] **Step 1: Write the failing tests**

Append to `tests/WinUIDesigner.Shell.Tests/ShellModelTests.cs`. Match the fixture's existing temp-file style.

```csharp
        [Test]
        public void SavingInXamlModeWritesWhatTheUserTypedNotAReSerialization()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "wd-xamlmode-" + System.Guid.NewGuid().ToString("N") + ".xaml");
                try
                {
                    var doc = shell.New();
                    Assert.That(doc, Is.Not.Null);

                    doc.Mode = DocumentMode.Xaml;
                    doc.Text = doc.Text + System.Environment.NewLine + "<!-- typed by hand -->";

                    Assert.That(shell.SaveAs(doc, path), Is.True);

                    var onDisk = System.IO.File.ReadAllText(path);
                    StringAssert.Contains("typed by hand", onDisk,
                        "WriteToDisk used to call UpdateXaml unconditionally, which replaced the " +
                        "user's typing with a serialization of the tree they typed it instead of");
                }
                finally
                {
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                }
            });
        }

        [Test]
        public void SavingInDesignModeStillSerializesTheLiveTree()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "wd-designmode-" + System.Guid.NewGuid().ToString("N") + ".xaml");
                try
                {
                    var doc = shell.New();
                    var root = doc.DesignContext.RootItem;
                    root.Properties["Width"].SetValue(777.0);

                    Assert.That(shell.SaveAs(doc, path), Is.True);

                    StringAssert.Contains("777", System.IO.File.ReadAllText(path),
                        "in design mode the tree is still the source of truth");
                }
                finally
                {
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                }
            });
        }

        [Test]
        public void OpeningAPathThatIsAlreadyOpenMakesItCurrentInsteadOfAddingASecondCopy()
        {
            WinUITestHost.Run(() =>
            {
                var shell = new ShellModel();
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "wd-dedupe-" + System.Guid.NewGuid().ToString("N") + ".xaml");
                try
                {
                    System.IO.File.WriteAllText(path, NewDocumentTemplate.Xaml);

                    Assert.That(shell.Open(path), Is.True);
                    Assert.That(shell.Documents.Count, Is.EqualTo(1));
                    var first = shell.Documents[0];

                    var other = shell.New();
                    Assert.That(shell.CurrentDocument, Is.SameAs(other));

                    Assert.That(shell.Open(path), Is.True);
                    Assert.That(shell.Documents.Count, Is.EqualTo(2),
                        "two live trees over one file means saving from either clobbers the other");
                    Assert.That(shell.CurrentDocument, Is.SameAs(first));
                }
                finally
                {
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                }
            });
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult --test=WinUIDesigner.Shell.Tests.ShellModelTests.SavingInXamlModeWritesWhatTheUserTypedNotAReSerialization
```
Expected: FAIL — the saved file contains the serialized tree, not the comment.
`OpeningAPathThatIsAlreadyOpen...` fails on `Documents.Count` being 3.

- [ ] **Step 3: Guard the save path**

In `ShellModel.WriteToDisk`, replace the unconditional call (`ShellModel.cs:196`):

```csharp
                // Only when the tree is the source of truth. In XAML mode the user's typing is,
                // and re-serializing here would overwrite it with the tree they typed it instead
                // of. The reference guards the same call the same way
                // (XamlDesigner/Document.cs:196-203). Spec Decision 12.
                if (doc.InDesignMode)
                    doc.UpdateXaml();
                File.WriteAllText(path, doc.Text);
```

- [ ] **Step 4: De-dupe in `Open`**

Replace the head of `ShellModel.Open` (`ShellModel.cs:105-122`) up to and including the `new Document(...)` line:

```csharp
        public bool Open(string path)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                // Path.GetFullPath throws for a null, empty, or malformed path. No method on
                // ShellModel may throw - Phase 5 Decision 17 - so report and refuse.
                RaiseError(ex.Message, null);
                return false;
            }

            // A path already open becomes current rather than opening a second live tree over the
            // same file, which is what the reference does (XamlDesigner/Shell.cs:141-146). Without
            // tabs this could not arise; with them, two copies means saving from either silently
            // clobbers the other. Spec Decision 6.
            foreach (var alreadyOpen in Documents)
            {
                if (alreadyOpen.FilePath != null
                    && string.Equals(alreadyOpen.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentDocument = alreadyOpen;
                    return true;
                }
            }

            string text;
            try
            {
                text = File.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                // Broad on purpose: File.ReadAllText can throw ArgumentNullException,
                // ArgumentException, NotSupportedException, PathTooLongException and more,
                // none of which derive from a common I/O base.
                RaiseError(ex.Message, null);
                return false;
            }

            var doc = new Document(fullPath, text);
```

Leave the rest of `Open` (the `LoadFailed` branch, the `LoadErrors` report, `Add(doc)`, `return true`) exactly as it is.

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```
Expected: `Test Count: 56, Passed: 55, Failed: 0, Skipped: 1`.

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.Shell/ShellModel.cs tests/WinUIDesigner.Shell.Tests/ShellModelTests.cs
git commit -m "fix: do not re-serialize over the user's XAML on save, and de-dupe Open by path"
```

---

### Task 3: DocumentView

**Files:**
- Create: `src/WinUIDesigner.Shell/DocumentView.cs`
- Test: `tests/WinUIDesigner.Shell.Tests/DocumentViewTests.cs`

**Interfaces:**
- Consumes: `Document.Mode`, `InDesignMode`, `InXamlMode`, `Text` setter, `Document.PropertyChanged` (Task 1).
- Produces: `public sealed class DocumentView : UserControl` with `public DocumentView(Document document)`, `public Document Document { get; }`, `public void JumpToError(XamlError error)`, `public void Detach()`, `internal TextBox Editor { get; }`, `internal static int OffsetOf(string text, int line, int column)`, `internal static int TokenLength(string text, int start)`.

- [ ] **Step 1: Write the failing tests**

`tests/WinUIDesigner.Shell.Tests/DocumentViewTests.cs`:

```csharp
using System;
using Microsoft.UI.Xaml;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// Drives <see cref="DocumentView"/> at the property level rather than through layout.
    /// </summary>
    /// <remarks>
    /// Measured: a bare TextBox as the root content of the shared test window throws a
    /// COMException whose Message and every Data entry are empty, so nothing can be seeded
    /// around it. A TextBox inside the laid-out shell tree does work - MainWindowInteractionTests
    /// drives the outline filter today - but nothing here needs layout to be meaningful.
    /// </remarks>
    [TestFixture]
    public class DocumentViewTests
    {
        const string CanvasWithButton =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"640\" Height=\"480\">" +
            "<Button x:Name=\"Ok\" Width=\"75\" Height=\"32\" Content=\"Ok\" />" +
            "</Canvas>";

        [Test]
        public void TypingInTheEditorReachesTheDocument()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    var view = new DocumentView(doc);
                    doc.Mode = DocumentMode.Xaml;

                    view.Editor.Text = view.Editor.Text + "<!-- typed -->";

                    StringAssert.Contains("typed", doc.Text);
                    Assert.That(doc.IsDirty, Is.True);
                    view.Detach();
                }
            });
        }

        [Test]
        public void OpeningTheXamlViewDoesNotDirtyACleanDocument()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    var view = new DocumentView(doc);
                    Assert.That(doc.IsDirty, Is.False, "constructing the view is not an edit");

                    doc.Mode = DocumentMode.Xaml;

                    Assert.That(doc.IsDirty, Is.False,
                        "TextBox hands back lone CR where XamlFormatter emitted CRLF (measured); " +
                        "the round trip through the editor must not read as a user edit");
                    Assert.That(view.Editor.Text, Is.Not.Empty);
                    view.Detach();
                }
            });
        }

        [Test]
        public void AProgrammaticTextChangeReachesTheEditorWithoutLooping()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    var view = new DocumentView(doc);
                    doc.Mode = DocumentMode.Xaml;

                    doc.Text = "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />";

                    StringAssert.Contains("Canvas", view.Editor.Text);
                    Assert.That(view.Editor.Text.Contains("Button"), Is.False,
                        "the editor should be showing the new text, not the old");
                    view.Detach();
                }
            });
        }

        [Test]
        public void ExactlyOneOfTheSurfaceAndTheEditorIsVisiblePerMode()
        {
            WinUITestHost.Run(() =>
            {
                using (var doc = new Document(null, CanvasWithButton))
                {
                    var view = new DocumentView(doc);

                    Assert.That(view.SurfaceVisibility, Is.EqualTo(Visibility.Visible));
                    Assert.That(view.Editor.Visibility, Is.EqualTo(Visibility.Collapsed));

                    doc.Mode = DocumentMode.Xaml;
                    Assert.That(view.SurfaceVisibility, Is.EqualTo(Visibility.Collapsed));
                    Assert.That(view.Editor.Visibility, Is.EqualTo(Visibility.Visible));

                    doc.Mode = DocumentMode.Design;
                    Assert.That(view.SurfaceVisibility, Is.EqualTo(Visibility.Visible));
                    Assert.That(view.Editor.Visibility, Is.EqualTo(Visibility.Collapsed));
                    view.Detach();
                }
            });
        }

        [Test]
        public void OffsetOfCountsLinesAndColumnsAndRefusesWhatIsOutOfRange()
        {
            // Pure string arithmetic - no dispatcher needed.
            const string crlf = "one\r\ntwo\r\nthree";
            const string cr = "one\rtwo\rthree";

            Assert.That(DocumentView.OffsetOf(crlf, 1, 1), Is.EqualTo(0));
            Assert.That(DocumentView.OffsetOf(crlf, 2, 1), Is.EqualTo(5));
            Assert.That(DocumentView.OffsetOf(crlf, 3, 3), Is.EqualTo(12));

            // The same coordinates against the normalised form the editor actually holds.
            Assert.That(DocumentView.OffsetOf(cr, 2, 1), Is.EqualTo(4));
            Assert.That(DocumentView.OffsetOf(cr, 3, 3), Is.EqualTo(10));

            Assert.That(DocumentView.OffsetOf(crlf, 9, 1), Is.EqualTo(-1), "line past the end");
            Assert.That(DocumentView.OffsetOf(crlf, 1, 99), Is.EqualTo(-1), "column past the end");
            Assert.That(DocumentView.OffsetOf(crlf, 0, 1), Is.EqualTo(-1));
            Assert.That(DocumentView.OffsetOf(null, 1, 1), Is.EqualTo(-1));
        }

        [Test]
        public void TokenLengthStopsAtADelimiterOrTheEndOfTheText()
        {
            Assert.That(DocumentView.TokenLength("<Button Width=\"1\"", 1), Is.EqualTo(6), "Button");
            Assert.That(DocumentView.TokenLength("abc", 0), Is.EqualTo(3), "runs off the end");
            Assert.That(DocumentView.TokenLength("a.b", 0), Is.EqualTo(1));
            Assert.That(DocumentView.TokenLength("ab", 2), Is.EqualTo(0), "start at the end");
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```
dotnet build WinUIDesigner.sln
```
Expected: build errors — `DocumentView` does not exist.

- [ ] **Step 3: Write `DocumentView`**

`src/WinUIDesigner.Shell/DocumentView.cs`:

```csharp
using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUIDesigner.DesignModel.Xaml;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// One document's centre pane: its design surface, its XAML text, and the bar that switches
    /// between them.
    /// </summary>
    /// <remarks>
    /// One instance per <see cref="Document"/>, held by MainWindow and shown in a single
    /// ContentControl while its document is current. It is never a TabViewItem's Content: a
    /// TabView's template does not apply in the test process (measured - zero visual children,
    /// content never realized), so anything inside a tab is invisible to every test.
    /// A DesignSurface is therefore never re-parented; it stays inside this view for the view's
    /// whole life. Spec Decisions 1 and 2.
    /// </remarks>
    public sealed class DocumentView : UserControl
    {
        readonly Document document;
        readonly ContentControl surfaceHost;
        readonly ScrollViewer surfaceScroll;
        readonly TextBox editor;
        readonly RadioButton designButton;
        readonly RadioButton xamlButton;

        bool suppressModeButtons;
        bool suppressTextSync;
        bool detached;

        public DocumentView(Document document)
        {
            if (document == null)
                throw new ArgumentNullException("document");

            this.document = document;

            surfaceHost = new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = document.DesignSurface
            };

            // The surface's own ScrollViewer, moved down from MainWindow. The editor must NOT be
            // inside it: a TextBox brings its own, and nesting hands the inner one an infinite
            // available height - the trap CLAUDE.md measured on ListView.
            surfaceScroll = new ScrollViewer
            {
                Content = surfaceHost,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            editor = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                IsSpellCheckEnabled = false,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New")
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);

            // Radio groups resolve by name across the whole visual root, not per parent, so two
            // open documents sharing a group name would uncheck each other's buttons. Spec
            // Decision 21.
            var group = "DocumentMode_" + Guid.NewGuid().ToString("N");
            designButton = new RadioButton { GroupName = group, Content = "Design", Margin = new Thickness(4, 2, 4, 2) };
            xamlButton = new RadioButton { GroupName = group, Content = "XAML", Margin = new Thickness(4, 2, 4, 2) };

            var modeBar = new StackPanel { Orientation = Orientation.Horizontal };
            modeBar.Children.Add(designButton);
            modeBar.Children.Add(xamlButton);

            var body = new Grid();
            body.Children.Add(surfaceScroll);
            body.Children.Add(editor);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(body, 0);
            Grid.SetRow(modeBar, 1);
            root.Children.Add(body);
            root.Children.Add(modeBar);
            Content = root;

            // Seed BEFORE subscribing. The other order makes this assignment echo back through
            // TextChanged into Document.Text's setter and opens every document dirty. The
            // reference wires TextChanged in its Loaded handler for the same reason.
            editor.Text = document.Text ?? string.Empty;
            editor.TextChanged += OnEditorTextChanged;

            designButton.Checked += OnModeButtonChecked;
            xamlButton.Checked += OnModeButtonChecked;

            document.PropertyChanged += OnDocumentPropertyChanged;
            ApplyMode();
        }

        public Document Document
        {
            get { return document; }
        }

        /// <summary>Test seam: the XAML editor.</summary>
        internal TextBox Editor
        {
            get { return editor; }
        }

        /// <summary>Test seam: the design surface's visibility, without walking the tree.</summary>
        internal Visibility SurfaceVisibility
        {
            get { return surfaceScroll.Visibility; }
        }

        /// <summary>
        /// Releases the document subscription. Called when the document closes; without it the
        /// view keeps listening to a document nobody can reach.
        /// </summary>
        public void Detach()
        {
            if (detached)
                return;
            detached = true;
            document.PropertyChanged -= OnDocumentPropertyChanged;
            editor.TextChanged -= OnEditorTextChanged;
            designButton.Checked -= OnModeButtonChecked;
            xamlButton.Checked -= OnModeButtonChecked;
            surfaceHost.Content = null;
        }

        /// <summary>
        /// Switches to XAML mode and selects the token at <paramref name="error"/>'s position.
        /// </summary>
        /// <remarks>
        /// The offset is computed against the editor's text, not the document's: TextBox
        /// normalises CRLF to lone CR (measured), so the two differ by one character per
        /// preceding line and a line-40 error computed against Document.Text lands 39 characters
        /// early. Spec Decision 20.
        /// </remarks>
        public void JumpToError(XamlError error)
        {
            if (error == null)
                return;

            document.Mode = DocumentMode.Xaml;

            var text = editor.Text ?? string.Empty;
            int start = OffsetOf(text, error.Line, error.Column);
            if (start < 0)
            {
                editor.SelectionStart = 0;
                editor.SelectionLength = 0;
                return;
            }

            editor.SelectionStart = start;
            editor.SelectionLength = TokenLength(text, start);
            editor.Focus(FocusState.Programmatic);
        }

        /// <summary>
        /// The offset of 1-based <paramref name="line"/>/<paramref name="column"/> in
        /// <paramref name="text"/>, or -1 when either is out of range. CR, LF and CRLF each count
        /// as one break.
        /// </summary>
        internal static int OffsetOf(string text, int line, int column)
        {
            if (text == null || line < 1 || column < 1)
                return -1;

            int i = 0;
            int currentLine = 1;
            while (currentLine < line && i < text.Length)
            {
                char c = text[i];
                if (c == '\r')
                {
                    i++;
                    if (i < text.Length && text[i] == '\n')
                        i++;
                    currentLine++;
                }
                else if (c == '\n')
                {
                    i++;
                    currentLine++;
                }
                else
                {
                    i++;
                }
            }

            if (currentLine < line)
                return -1;

            int offset = i + (column - 1);
            return offset > text.Length ? -1 : offset;
        }

        /// <summary>
        /// How far forward from <paramref name="start"/> the token runs. Ported from the
        /// reference's scan (space, '.', '&lt;', '&gt;', '"'), with one addition it did not need:
        /// this one stops at the end of the text rather than reading past it.
        /// </summary>
        internal static int TokenLength(string text, int start)
        {
            if (text == null || start < 0 || start >= text.Length)
                return 0;

            int n = 0;
            while (start + n < text.Length)
            {
                char c = text[start + n];
                if (c == ' ' || c == '.' || c == '<' || c == '>' || c == '"')
                    break;
                n++;
            }
            return n;
        }

        void OnEditorTextChanged(object sender, TextChangedEventArgs e)
        {
            if (suppressTextSync)
                return;
            document.Text = editor.Text;
        }

        void OnDocumentPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Text")
            {
                var wanted = document.Text ?? string.Empty;
                // Compare normalised: the editor holds lone CR where the document holds CRLF, so
                // a raw comparison would reassign on every raise and stamp on the caret.
                if (Document.NormaliseLineEndings(editor.Text) == Document.NormaliseLineEndings(wanted))
                    return;

                suppressTextSync = true;
                try
                {
                    editor.Text = wanted;
                }
                finally
                {
                    suppressTextSync = false;
                }
            }
            else if (e.PropertyName == "Mode")
            {
                ApplyMode();
            }
        }

        void ApplyMode()
        {
            surfaceScroll.Visibility = document.InDesignMode ? Visibility.Visible : Visibility.Collapsed;
            editor.Visibility = document.InXamlMode ? Visibility.Visible : Visibility.Collapsed;

            // Pushing Mode back into IsChecked raises Checked, which would re-enter the Mode
            // setter. The setter's equality guard makes that harmless, but the flag keeps the
            // two from depending on each other. Spec Decision 21.
            suppressModeButtons = true;
            try
            {
                designButton.IsChecked = document.InDesignMode;
                xamlButton.IsChecked = document.InXamlMode;
            }
            finally
            {
                suppressModeButtons = false;
            }
        }

        void OnModeButtonChecked(object sender, RoutedEventArgs e)
        {
            if (suppressModeButtons)
                return;
            document.Mode = ReferenceEquals(sender, xamlButton) ? DocumentMode.Xaml : DocumentMode.Design;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```
Expected: `Test Count: 62, Passed: 61, Failed: 0, Skipped: 1`.

- [ ] **Step 5: Measure whether the selection needs a delay**

The spec (Decision 20, Known gaps) requires this to be measured, not assumed. The reference needed `await Task.Delay(70)` before touching AvalonEdit's selection; whether a plain `TextBox` does is unknown.

Write a throwaway test that constructs a `DocumentView` inside `MainWindow`'s live tree in the shared window (the way `MainWindowInteractionTests` hosts the shell), calls `JumpToError(new XamlError { Line = 3, Column = 5, Message = "x" })` and asserts `Editor.SelectionStart` and `Editor.SelectionLength` are what `OffsetOf`/`TokenLength` predict. Run it from the `.exe`.

- If it passes, delete the throwaway and record in the commit message that no delay is needed.
- If it fails, keep the test, add the smallest delay that makes it pass to `JumpToError`, and say so in the commit message.

Do not add a delay without this measurement, and do not skip the measurement.

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.Shell/DocumentView.cs tests/WinUIDesigner.Shell.Tests/DocumentViewTests.cs
git commit -m "feat: add DocumentView with a Design/XAML switch and jump-to-error"
```

---

### Task 4: The error list

**Files:**
- Create: `src/WinUIDesigner.Shell/ErrorListView.cs`
- Test: `tests/WinUIDesigner.Shell.Tests/ErrorListViewTests.cs`

**Interfaces:**
- Consumes: `WinUIDesigner.DesignModel.Xaml.XamlError` (`{ string Message; int Line; int Column; }`).
- Produces: `public sealed class XamlErrorNode` with `public XamlError Error { get; }` and `public string Description { get; }`; `public sealed class ErrorListView : UserControl` with `public const double MaxListHeight = 160`, `public void Show(IReadOnlyList<XamlError> errors)`, `public event EventHandler<XamlError> JumpRequested`, `internal ListView List { get; }`.

- [ ] **Step 1: Write the failing tests**

`tests/WinUIDesigner.Shell.Tests/ErrorListViewTests.cs`:

```csharp
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class ErrorListViewTests
    {
        static IReadOnlyList<XamlError> Two()
        {
            return new List<XamlError>
            {
                new XamlError { Line = 3, Column = 5, Message = "first problem" },
                new XamlError { Line = 9, Column = 1, Message = "second problem" }
            };
        }

        [Test]
        public void ShowingErrorsMakesTheListVisibleAndOneRowPerError()
        {
            WinUITestHost.Run(() =>
            {
                var view = new ErrorListView();
                Assert.That(view.Visibility, Is.EqualTo(Visibility.Collapsed),
                    "an empty error list must not take space");

                view.Show(Two());

                Assert.That(view.Visibility, Is.EqualTo(Visibility.Visible));
                var rows = (IReadOnlyList<XamlErrorNode>)view.List.ItemsSource;
                Assert.That(rows.Count, Is.EqualTo(2));
                StringAssert.Contains("first problem", rows[0].Description);
                StringAssert.Contains("3", rows[0].Description);
                StringAssert.Contains("5", rows[0].Description);
            });
        }

        [Test]
        public void ShowingNothingCollapsesTheList()
        {
            WinUITestHost.Run(() =>
            {
                var view = new ErrorListView();
                view.Show(Two());
                Assert.That(view.Visibility, Is.EqualTo(Visibility.Visible));

                view.Show(new List<XamlError>());
                Assert.That(view.Visibility, Is.EqualTo(Visibility.Collapsed));

                view.Show(null);
                Assert.That(view.Visibility, Is.EqualTo(Visibility.Collapsed));
            });
        }

        [Test]
        public void TheListIsHeightCapped()
        {
            WinUITestHost.Run(() =>
            {
                var view = new ErrorListView();
                // CLAUDE.md records a measured ListView growing to 6360px inside an unbounded
                // parent. The error row's Height is Auto, which is exactly that parent, so the
                // cap has to live on the list itself.
                Assert.That(view.List.MaxHeight, Is.EqualTo(ErrorListView.MaxListHeight));
            });
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```
dotnet build WinUIDesigner.sln
```
Expected: build errors — `ErrorListView` and `XamlErrorNode` do not exist.

- [ ] **Step 3: Write the control**

`src/WinUIDesigner.Shell/ErrorListView.cs`:

```csharp
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel.Xaml;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// One row of the error list.
    /// </summary>
    /// <remarks>
    /// A wrapper rather than binding XamlError directly, for the reason ToolboxView's ControlNode
    /// exists: with no XAML in this project there is no DataTemplate, so a ListView shows exactly
    /// one property through DisplayMemberPath, and an error needs its line, column and message
    /// together. Spec Decision 19.
    /// </remarks>
    public sealed class XamlErrorNode
    {
        public XamlErrorNode(XamlError error)
        {
            Error = error;
        }

        public XamlError Error { get; private set; }

        public string Description
        {
            get
            {
                if (Error == null)
                    return string.Empty;
                return Error.Line + ", " + Error.Column + " - " + Error.Message;
            }
        }
    }

    /// <summary>
    /// The errors the current document reported while parsing. Collapsed when there are none.
    /// </summary>
    public sealed class ErrorListView : UserControl
    {
        /// <summary>
        /// The list's height ceiling.
        /// </summary>
        /// <remarks>
        /// The row that hosts this control is Auto-height, which hands a ListView an infinite
        /// available height: CLAUDE.md records a measured 200x6360 ListView realizing every
        /// container inside a 200x680 parent. A document with two hundred parse errors would push
        /// the whole three-column area off screen. Spec Decision 18.
        /// </remarks>
        public const double MaxListHeight = 160;

        readonly ListView list;

        public ErrorListView()
        {
            list = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                DisplayMemberPath = "Description",
                MaxHeight = MaxListHeight
            };
            list.DoubleTapped += OnDoubleTapped;

            Content = list;
            Visibility = Visibility.Collapsed;
        }

        /// <summary>Raised when the user double-taps a row. Carries the error to jump to.</summary>
        public event EventHandler<XamlError> JumpRequested;

        /// <summary>Test seam: the list itself.</summary>
        internal ListView List
        {
            get { return list; }
        }

        /// <summary>
        /// Shows <paramref name="errors"/>, or collapses the control when there are none.
        /// </summary>
        public void Show(IReadOnlyList<XamlError> errors)
        {
            var rows = new List<XamlErrorNode>();
            if (errors != null)
            {
                foreach (var error in errors)
                    rows.Add(new XamlErrorNode(error));
            }

            list.ItemsSource = rows;
            Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void OnDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            var node = list.SelectedItem as XamlErrorNode;
            if (node == null || node.Error == null)
                return;

            var handler = JumpRequested;
            if (handler != null)
                handler(this, node.Error);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```
Expected: `Test Count: 65, Passed: 64, Failed: 0, Skipped: 1`.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Shell/ErrorListView.cs tests/WinUIDesigner.Shell.Tests/ErrorListViewTests.cs
git commit -m "feat: add a height-capped error list with jump-on-double-tap"
```

---

### Task 5: Tabs in MainWindow

This is the structural task. It removes `ReplaceCurrentDocumentAsync` and the fixture that covers it, so the deletion and its test removal land in one commit and no revision has an orphaned test.

**Files:**
- Modify: `src/WinUIDesigner.Shell/MainWindow.cs` (fields `:36-49`, constructor `:59-85`, `BuildLayout` `:87-157`, `WireCurrentDocument` `:314-374`, `ReplaceCurrentDocumentAsync`/`NewAsync`/`OpenAsync` `:429-539`)
- Modify: `tests/WinUIDesigner.Shell.Tests/MainWindowInteractionTests.cs`
- Delete: `tests/WinUIDesigner.Shell.Tests/MainWindowCloseOrderingTests.cs`

**Interfaces:**
- Consumes: `DocumentView(Document)`, `DocumentView.Detach()` (Task 3); `Document.Mode`, `Document.Title` (Task 1).
- Produces: `internal void MainWindow.ActivateDocument(Document doc)`; `internal Task MainWindow.RequestCloseAsync(Document doc, Func<Document, Task<CloseDecision>> decideClose)`; `internal TabView MainWindow.TabStripForTest { get; }`; `internal DocumentView MainWindow.ViewFor(Document doc)`. `MainWindow.CloseDecision` keeps its existing shape.

- [ ] **Step 1: Write the failing tests**

Append to `tests/WinUIDesigner.Shell.Tests/MainWindowInteractionTests.cs`. These drive the seams, not the `TabView`'s events — measured: those never fire in this process.

```csharp
        [Test]
        public void OpeningASecondDocumentLeavesBothOpenWithATabEach()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                var first = shell.CurrentDocument;
                Assert.That(first, Is.Not.Null);

                var second = shell.New();

                Assert.That(shell.Documents.Count, Is.EqualTo(2),
                    "New must add, not replace - the tab strip is what makes both reachable");
                Assert.That(shell.CurrentDocument, Is.SameAs(second));
                Assert.That(window.TabStripForTest.TabItems.Count, Is.EqualTo(2));
                Assert.That(window.ViewFor(first), Is.Not.Null);
                Assert.That(window.ViewFor(second), Is.Not.Null);
                Assert.That(window.ViewFor(first), Is.Not.SameAs(window.ViewFor(second)));
            });
        }

        [Test]
        public void ActivatingADocumentMakesItCurrentAndShowsItsView()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                var first = shell.CurrentDocument;
                var second = shell.New();

                window.ActivateDocument(first);

                Assert.That(shell.CurrentDocument, Is.SameAs(first));
                Assert.That(window.ContentHostForTest.Content, Is.SameAs(window.ViewFor(first)));

                window.ActivateDocument(second);
                Assert.That(window.ContentHostForTest.Content, Is.SameAs(window.ViewFor(second)));
            });
        }

        [Test]
        public void ACancelledTabCloseLeavesTheTabAndTheDocumentInPlace()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                var doc = shell.CurrentDocument;
                var before = shell.Documents.Count;

                window.RequestCloseAsync(doc,
                    d => Task.FromResult(new MainWindow.CloseDecision { Proceed = false })).Wait();

                Assert.That(shell.Documents.Count, Is.EqualTo(before));
                Assert.That(window.TabStripForTest.TabItems.Count, Is.EqualTo(before));
                Assert.That(window.ViewFor(doc), Is.Not.Null);
            });
        }

        [Test]
        public void ADiscardedTabCloseRemovesBothTheTabAndTheDocument()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                var doomed = shell.New();
                var before = shell.Documents.Count;

                window.RequestCloseAsync(doomed,
                    d => Task.FromResult(new MainWindow.CloseDecision
                    {
                        Proceed = true,
                        ClearDirtyBeforeClose = true
                    })).Wait();

                Assert.That(shell.Documents.Count, Is.EqualTo(before - 1));
                Assert.That(shell.Documents.Contains(doomed), Is.False);
                Assert.That(window.TabStripForTest.TabItems.Count, Is.EqualTo(before - 1));
                Assert.That(window.ViewFor(doomed), Is.Null,
                    "the view must be detached and forgotten with its document");
            });
        }
```

Add `using System.Threading.Tasks;` to the fixture's usings if it is not already there.

- [ ] **Step 2: Run to verify it fails**

```
dotnet build WinUIDesigner.sln
```
Expected: build errors — `TabStripForTest`, `ContentHostForTest`, `ViewFor`, `ActivateDocument`, `RequestCloseAsync` do not exist.

- [ ] **Step 3: Replace the fields and the centre column**

In `MainWindow`'s field block, delete `designHost` and add:

```csharp
        readonly TabView tabStrip = new TabView
        {
            IsAddTabButtonVisible = false,
            CanDragTabs = false
        };
        readonly ContentControl contentHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        readonly Dictionary<Document, DocumentView> views = new Dictionary<Document, DocumentView>();
        readonly Dictionary<Document, TabViewItem> tabs = new Dictionary<Document, TabViewItem>();
        bool syncingSelection;
```

Add `using System.Collections.Generic;` and `using System.Collections.Specialized;` if absent.

In `BuildLayout`, replace the centre column (`MainWindow.cs:114-121`, the `surfaceScroll` block) with a two-row grid. The old `surfaceScroll` is gone — its `ScrollViewer` now lives inside `DocumentView`:

```csharp
            // TabView is a header strip only. Measured: its template never applies in the test
            // process - zero visual children, a selected item's Content never enters the visual
            // tree - so anything hosted inside a tab is invisible to every test, including the
            // DesignSurface that MainWindowInteractionTests asserts it can find. Spec Decision 1.
            var centre = new Grid();
            centre.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            centre.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(tabStrip, 0);
            Grid.SetRow(contentHost, 1);
            centre.Children.Add(tabStrip);
            centre.Children.Add(contentHost);
            Grid.SetColumn(centre, 1);
            columns.Children.Add(centre);
```

- [ ] **Step 4: Wire the strip in the constructor, before `shell.New()`**

In the constructor, between `Content = BuildLayout();` and `shell.New();`, add:

```csharp
            // Before shell.New() below: New does Documents.Add then sets CurrentDocument, so
            // subscribing after it means the first document never gets a tab. Spec Decision 4.
            shell.Documents.CollectionChanged += OnDocumentsChanged;
            tabStrip.SelectionChanged += OnTabSelectionChanged;
            tabStrip.TabCloseRequested += OnTabCloseRequested;
```

- [ ] **Step 5: Add the collection, selection, close and subscription plumbing**

Add these members to `MainWindow`:

```csharp
        /// <summary>Test seam: the tab strip.</summary>
        internal TabView TabStripForTest { get { return tabStrip; } }

        /// <summary>Test seam: the host that shows the current document's view.</summary>
        internal ContentControl ContentHostForTest { get { return contentHost; } }

        /// <summary>The view built for <paramref name="doc"/>, or null once it has closed.</summary>
        internal DocumentView ViewFor(Document doc)
        {
            DocumentView view;
            return doc != null && views.TryGetValue(doc, out view) ? view : null;
        }

        void OnDocumentsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (Document doc in e.OldItems)
                    ForgetDocument(doc);
            }

            if (e.NewItems != null)
            {
                foreach (Document doc in e.NewItems)
                    RememberDocument(doc);
            }

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var doc in new List<Document>(views.Keys))
                    ForgetDocument(doc);
            }

            SyncSelectedTab();
            RefreshCommands();
        }

        void RememberDocument(Document doc)
        {
            if (doc == null || views.ContainsKey(doc))
                return;

            var view = new DocumentView(doc);
            views.Add(doc, view);

            var tab = new TabViewItem { Header = doc.Title, IsClosable = true, Content = null };
            tabs.Add(doc, tab);
            tabStrip.TabItems.Add(tab);

            // Every document, not only the current one: a background document's Title changes
            // through MarkSaved (Save All), FilePath (Save As) and SetTempName, and its tab
            // header has to follow. Spec Decision 22.
            doc.PropertyChanged += OnDocumentPropertyChanged;
        }

        void ForgetDocument(Document doc)
        {
            if (doc == null)
                return;

            doc.PropertyChanged -= OnDocumentPropertyChanged;

            DocumentView view;
            if (views.TryGetValue(doc, out view))
            {
                if (ReferenceEquals(contentHost.Content, view))
                    contentHost.Content = null;
                view.Detach();
                views.Remove(doc);
            }

            TabViewItem tab;
            if (tabs.TryGetValue(doc, out tab))
            {
                tabStrip.TabItems.Remove(tab);
                tabs.Remove(doc);
            }
        }

        void OnDocumentPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var doc = sender as Document;
            if (doc == null)
                return;

            if (e.PropertyName == "Title" || e.PropertyName == "Name")
            {
                TabViewItem tab;
                if (tabs.TryGetValue(doc, out tab))
                    tab.Header = doc.Title;
                if (ReferenceEquals(doc, shell.CurrentDocument))
                    UpdateTitle();
            }
            else if (e.PropertyName == "Mode")
            {
                // Only the current document. A background tab changing mode must not take the
                // outline, property grid and toolbox away from the visible one. Spec Decision 22.
                if (ReferenceEquals(doc, shell.CurrentDocument))
                    WireCurrentDocument();
            }
        }

        /// <summary>
        /// Makes <paramref name="doc"/> the current document. Every route into this - a tab
        /// click, a test - goes through here.
        /// </summary>
        /// <remarks>
        /// The logic lives in a method rather than in the TabView's SelectionChanged handler
        /// because that event never fires in the test process (measured), so anything left inside
        /// it would ship unverified. Spec Decision 3.
        /// </remarks>
        internal void ActivateDocument(Document doc)
        {
            if (doc == null || !shell.Documents.Contains(doc))
                return;
            shell.CurrentDocument = doc;
        }

        /// <summary>
        /// Asks about <paramref name="doc"/> and closes it if the answer allows.
        /// </summary>
        /// <remarks>
        /// <paramref name="decideClose"/> is a parameter for the reason the deleted
        /// ReplaceCurrentDocumentAsync had one: in production it is PrepareForCloseAsync, which
        /// shows a ContentDialog and so cannot run in a test process.
        /// <para>
        /// This never touches TabItems. The tab is removed by OnDocumentsChanged when
        /// ShellModel.Close actually removes the document, so a cancelled gate leaves the strip
        /// and the model in agreement by construction. Spec Decision 4.
        /// </para>
        /// </remarks>
        internal async Task RequestCloseAsync(Document doc, Func<Document, Task<CloseDecision>> decideClose)
        {
            if (doc == null)
                return;

            var decision = await decideClose(doc);
            if (!decision.Proceed)
                return;

            if (decision.ClearDirtyBeforeClose)
                doc.MarkSaved();

            // ShellModel.Close removes, re-points CurrentDocument, and disposes. Do not dispose
            // here as well: Close returns false when it refuses (it consults its own
            // AskSaveChanges, which this window answers Cancel), and a document it kept must stay
            // usable. Close owns the document's lifetime; this method only decides.
            shell.Close(doc);
        }

        void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingSelection)
                return;

            var tab = tabStrip.SelectedItem as TabViewItem;
            if (tab == null)
                return;

            foreach (var pair in tabs)
            {
                if (ReferenceEquals(pair.Value, tab))
                {
                    ActivateDocument(pair.Key);
                    return;
                }
            }
        }

        void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            foreach (var pair in tabs)
            {
                if (ReferenceEquals(pair.Value, args.Tab))
                {
                    var doc = pair.Key;
                    Fire(() => RequestCloseAsync(doc, PrepareForCloseAsync));
                    return;
                }
            }
        }

        /// <summary>
        /// Points the strip at whatever ShellModel says is current, one direction at a time.
        /// </summary>
        /// <remarks>
        /// ShellModel.Close re-points CurrentDocument while the TabView is reacting to the same
        /// removal, so without the flag the two pushes re-enter each other. Spec Decision 5.
        /// </remarks>
        void SyncSelectedTab()
        {
            syncingSelection = true;
            try
            {
                var doc = shell.CurrentDocument;
                TabViewItem tab;
                tabStrip.SelectedItem = doc != null && tabs.TryGetValue(doc, out tab) ? tab : null;
            }
            finally
            {
                syncingSelection = false;
            }
        }
```

- [ ] **Step 6: Rework `WireCurrentDocument`**

Replace the two `designHost.Content` assignments. In the no-document branch (`MainWindow.cs:337`):

```csharp
                contentHost.Content = null;
```

and in the populated branch (`MainWindow.cs:344`):

```csharp
                contentHost.Content = ViewFor(wiredDocument);
```

Add `SyncSelectedTab();` immediately before the closing `UpdateTitle(); RefreshCommands();` pair at the end of the method.

- [ ] **Step 7: Make New and Open additive, and delete the replacement path**

Delete `ReplaceCurrentDocumentAsync` (both overloads) and the `CloseDecision`-returning remark block above them — but **keep the `CloseDecision` struct itself**, which `PrepareForCloseAsync` and `RequestCloseAsync` both use. Replace `NewAsync` and `OpenAsync` with:

```csharp
        Task NewAsync()
        {
            // Additive. The replacement path existed only because the shell had no tab strip;
            // with one, a document left in the collection is reachable. Spec Decision 6.
            shell.New();
            return Task.CompletedTask;
        }

        async Task OpenAsync()
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            // Unpackaged WinUI requires the picker to be associated with a window's HWND;
            // without this it throws when shown.
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".xaml");

            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            shell.Open(file.Path);
        }
```

- [ ] **Step 8: Delete the fixture that covered the removed code**

```bash
git rm tests/WinUIDesigner.Shell.Tests/MainWindowCloseOrderingTests.cs
```

- [ ] **Step 9: Run the tests to verify they pass**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```
Expected: `Test Count: 65, Passed: 64, Failed: 0, Skipped: 1` (65 = 62 + 3 from Task 4 + 4 new − 4 deleted).

If `MainWindowInteractionTests`' `OneTimeSetUp` starts failing on `FindDescendant<DesignSurface>`, the surface has been put inside a `TabViewItem` — that is the failure Decision 1 exists to prevent. Fix the layout, not the assertion.

- [ ] **Step 10: Commit**

```bash
git add -A src/WinUIDesigner.Shell/MainWindow.cs tests/WinUIDesigner.Shell.Tests/
git commit -m "feat: host documents in a tab strip and make New and Open additive"
```

---

### Task 6: Close, Close All and Save All

**Files:**
- Modify: `src/WinUIDesigner.Shell/MainWindow.cs` (`BuildMenu` `:160-193`, `RefreshCommands` `:416-427`)
- Test: `tests/WinUIDesigner.Shell.Tests/MainWindowInteractionTests.cs`

**Interfaces:**
- Consumes: `RequestCloseAsync`, `PrepareForCloseAsync`, `CloseDecision` (Task 5).
- Produces: `internal Task CloseAllAsync(Func<Document, Task<CloseDecision>> decideClose)`; `internal Task SaveAllAsync(Func<Document, bool, Task> saveOne)`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/WinUIDesigner.Shell.Tests/MainWindowInteractionTests.cs`:

```csharp
        [Test]
        public void CloseAllAsksEveryDocumentBeforeClosingAny()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                shell.New();
                shell.New();
                var all = shell.Documents.ToList();
                Assert.That(all.Count, Is.GreaterThanOrEqualTo(3));

                var asked = new List<Document>();
                var closedWhileAsking = 0;

                window.CloseAllAsync(d =>
                {
                    asked.Add(d);
                    closedWhileAsking = all.Count - shell.Documents.Count;
                    return Task.FromResult(new MainWindow.CloseDecision
                    {
                        Proceed = true,
                        ClearDirtyBeforeClose = true
                    });
                }).Wait();

                Assert.That(asked.Count, Is.EqualTo(all.Count), "every document is asked");
                Assert.That(closedWhileAsking, Is.EqualTo(0),
                    "nothing may close until every document has answered - a Cancel halfway " +
                    "must leave the user with all of them, not half");
                Assert.That(shell.Documents, Is.Empty);
            });
        }

        [Test]
        public void ACancelPartWayThroughCloseAllClosesNothing()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                shell.New();
                shell.New();
                var before = shell.Documents.Count;
                var seen = 0;

                window.CloseAllAsync(d =>
                {
                    seen++;
                    return Task.FromResult(new MainWindow.CloseDecision { Proceed = seen < 2 });
                }).Wait();

                Assert.That(shell.Documents.Count, Is.EqualTo(before));
                Assert.That(window.TabStripForTest.TabItems.Count, Is.EqualTo(before));
            });
        }

        [Test]
        public void SaveAllAsksOncePerDocumentAndStopsWhenOneIsAbandoned()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                shell.New();
                var count = shell.Documents.Count;

                var forcedAsk = new List<Document>();
                var stopAfter = 1;

                window.SaveAllAsync((doc, forceAsk) =>
                {
                    if (forceAsk)
                        forcedAsk.Add(doc);
                    // Leaving the document dirty is how a dismissed picker reports itself.
                    if (forcedAsk.Count >= stopAfter)
                        doc.MarkSuspect();
                    return Task.CompletedTask;
                }).Wait();

                Assert.That(forcedAsk.Count, Is.EqualTo(1),
                    "every document here is pathless, so each needs Save As - and the run stops " +
                    "at the first one the user abandons rather than queueing dialogs that " +
                    "ShowDialogAsync would drop on the floor");
                Assert.That(count, Is.GreaterThan(1));
            });
        }
```

Add `using System.Collections.Generic;` and `using System.Linq;` to the fixture if absent.

- [ ] **Step 2: Run to verify it fails**

```
dotnet build WinUIDesigner.sln
```
Expected: build errors — `CloseAllAsync` and `SaveAllAsync` do not exist.

- [ ] **Step 3: Implement the three commands**

Add to `MainWindow`:

```csharp
        /// <summary>
        /// Asks about every document, then closes them all. Cancel at any prompt abandons the
        /// whole operation with nothing closed.
        /// </summary>
        /// <remarks>
        /// Decide-all-then-discharge, the ordering ConfirmThenCloseAsync already uses for window
        /// close. Clearing dirty flags inside the loop would leave the documents already asked
        /// about looking clean when a later one answers Cancel. Spec Decision 9.
        /// </remarks>
        internal async Task CloseAllAsync(Func<Document, Task<CloseDecision>> decideClose)
        {
            var order = shell.Documents.ToList();
            var decisions = new List<KeyValuePair<Document, CloseDecision>>();

            foreach (var doc in order)
            {
                var decision = await decideClose(doc);
                if (!decision.Proceed)
                    return;
                decisions.Add(new KeyValuePair<Document, CloseDecision>(doc, decision));
            }

            foreach (var pair in decisions)
            {
                if (pair.Value.ClearDirtyBeforeClose)
                    pair.Key.MarkSaved();
            }

            // Close owns disposal - see RequestCloseAsync. Nothing here disposes.
            foreach (var pair in decisions)
                shell.Close(pair.Key);
        }

        /// <summary>
        /// Saves every dirty document, asking for a path one at a time for any that has none.
        /// </summary>
        /// <remarks>
        /// Sequential and short-circuiting on purpose. ShellModel.Save refuses a null FilePath and
        /// raises an error, and ShowDialogAsync returns null for a second concurrent dialog, so a
        /// naive loop over three unsaved documents would swallow two failures in silence. The
        /// reference routes a pathless document to SaveAs the same way (XamlDesigner/Shell.cs:153-162).
        /// Spec Decision 8.
        /// </remarks>
        internal async Task SaveAllAsync(Func<Document, bool, Task> saveOne)
        {
            foreach (var doc in shell.Documents.ToList())
            {
                if (!doc.IsDirty)
                    continue;

                await saveOne(doc, doc.FilePath == null);

                // Still dirty means the save failed or the picker was dismissed. Stop rather than
                // opening another dialog the user did not ask for.
                if (doc.IsDirty)
                    return;
            }
        }
```

In `BuildMenu`, insert into the File menu between `Open...` and `Save`:

```csharp
            closeItem = Item("Close", VirtualKey.None, VirtualKeyModifiers.None,
                (s, e) => Fire(() => RequestCloseAsync(shell.CurrentDocument, PrepareForCloseAsync)));
            closeAllItem = Item("Close All", VirtualKey.None, VirtualKeyModifiers.None,
                (s, e) => Fire(() => CloseAllAsync(PrepareForCloseAsync)));
            file.Items.Add(closeItem);
            file.Items.Add(closeAllItem);
            file.Items.Add(new MenuFlyoutSeparator());
```

and after `Save As...`:

```csharp
            saveAllItem = Item("Save All", VirtualKey.None, VirtualKeyModifiers.None,
                (s, e) => Fire(() => SaveAllAsync(SaveAsync)));
            file.Items.Add(saveAllItem);
```

Declare the three fields beside `undoItem`:

```csharp
        MenuFlyoutItem closeItem, closeAllItem, saveAllItem;
```

In `RefreshCommands`, add:

```csharp
            var hasDocument = shell.CurrentDocument != null;
            closeItem.IsEnabled = hasDocument;
            closeAllItem.IsEnabled = hasDocument;
            saveAllItem.IsEnabled = hasDocument;
```

`SaveAsync(Document, bool)` already has the `Func<Document, bool, Task>` shape `SaveAllAsync` wants, so `SaveAllAsync(SaveAsync)` compiles as written.

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```
Expected: `Test Count: 68, Passed: 67, Failed: 0, Skipped: 1`.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Shell/MainWindow.cs tests/WinUIDesigner.Shell.Tests/MainWindowInteractionTests.cs
git commit -m "feat: add Close, Close All and Save All through the async close gate"
```

---

### Task 7: The error row in the window

**Files:**
- Modify: `src/WinUIDesigner.Shell/MainWindow.cs` (`BuildLayout` root grid, `WireCurrentDocument`)
- Test: `tests/WinUIDesigner.Shell.Tests/MainWindowInteractionTests.cs`

**Interfaces:**
- Consumes: `ErrorListView.Show(IReadOnlyList<XamlError>)`, `ErrorListView.JumpRequested` (Task 4); `DocumentView.JumpToError` (Task 3); `ViewFor` (Task 5).
- Produces: `internal ErrorListView ErrorsForTest { get; }`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/WinUIDesigner.Shell.Tests/MainWindowInteractionTests.cs`:

```csharp
        [Test]
        public void TheErrorRowIsCollapsedForACleanDocumentAndForNoDocumentAtAll()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                Assert.That(shell.CurrentDocument, Is.Not.Null);
                Assert.That(shell.CurrentDocument.LoadErrors, Is.Empty);
                Assert.That(window.ErrorsForTest.Visibility, Is.EqualTo(Visibility.Collapsed));

                foreach (var doc in shell.Documents.ToList())
                {
                    doc.MarkSaved();
                    shell.Close(doc);
                }

                Assert.That(shell.CurrentDocument, Is.Null);
                Assert.That(window.ErrorsForTest.Visibility, Is.EqualTo(Visibility.Collapsed));
            });
        }

        [Test]
        public void ADocumentThatFailedToParseFillsTheErrorRow()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                var doc = shell.CurrentDocument;

                doc.Mode = DocumentMode.Xaml;
                doc.Text = "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
                doc.Mode = DocumentMode.Design;

                Assert.That(doc.LoadFailed, Is.True);
                Assert.That(window.ErrorsForTest.Visibility, Is.EqualTo(Visibility.Visible));
                var rows = (IReadOnlyList<XamlErrorNode>)window.ErrorsForTest.List.ItemsSource;
                Assert.That(rows, Is.Not.Empty);
            });
        }
```

Add `using Microsoft.UI.Xaml;` and `using WinUIDesigner.DesignModel.Xaml;` if absent.

- [ ] **Step 2: Run to verify it fails**

```
dotnet build WinUIDesigner.sln
```
Expected: build errors — `ErrorsForTest` does not exist.

- [ ] **Step 3: Add the row**

Declare the field beside the other views:

```csharp
        readonly ErrorListView errors = new ErrorListView();
```

In `BuildLayout`, give the root grid a third row and put the list in it. Replace the two-row setup at `MainWindow.cs:99-100` and the tail of the method:

```csharp
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
```

and, after `root.Children.Add(columns);`:

```csharp
            // A full-width strip rather than the reference's right-column pane: that column
            // already holds three things and a XAML parse message wraps several times at 280px.
            // The list caps its own height - an Auto row is exactly the unbounded parent that
            // makes a ListView realize every container. Spec Decision 18.
            errors.JumpRequested += OnJumpToErrorRequested;
            Grid.SetRow(errors, 2);
            root.Children.Add(errors);
```

Add to `MainWindow`:

```csharp
        /// <summary>Test seam: the error strip.</summary>
        internal ErrorListView ErrorsForTest { get { return errors; } }

        void OnJumpToErrorRequested(object sender, WinUIDesigner.DesignModel.Xaml.XamlError error)
        {
            var view = ViewFor(shell.CurrentDocument);
            if (view != null)
                view.JumpToError(error);
        }
```

In `WireCurrentDocument`, show the current document's errors. In the no-document branch, beside `contentHost.Content = null;`:

```csharp
                errors.Show(null);
```

and in the populated branch, beside `contentHost.Content = ViewFor(wiredDocument);`:

```csharp
                errors.Show(wiredDocument.LoadErrors);
```

`WireCurrentDocument` already runs on every `Mode` change through `OnDocumentPropertyChanged` (Task 5), so a document that fails to parse on the way into Design mode refreshes the row without any extra wiring.

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```
Expected: `Test Count: 70, Passed: 69, Failed: 0, Skipped: 1`.

- [ ] **Step 5: Run all four suites**

```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
tests/WinUIDesigner.Shell.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.Tests.exe --noresult
```
Expected: XamlDom `165 (161 passed, 0 failed, 4 pre-existing ignores)`, DesignModel `77/77`, Designer `248/248`, Shell `70 (69 passed, 0 failed, 1 explicit)`.

- [ ] **Step 6: Verify the constraints that no test can check**

```bash
find src -name "*.xaml" | wc -l          # must print 1
find src -name "app.manifest"            # must print nothing
grep -rn "PackageReference" src/WinUIDesigner.Shell/WinUIDesigner.Shell.csproj
```
The package list must be unchanged: `Microsoft.WindowsAppSDK` 1.7.250606001 and `Microsoft.Windows.SDK.BuildTools` 10.0.26100.4188, nothing else.

- [ ] **Step 7: Commit**

```bash
git add src/WinUIDesigner.Shell/MainWindow.cs tests/WinUIDesigner.Shell.Tests/MainWindowInteractionTests.cs
git commit -m "feat: show the current document's parse errors in a bottom strip"
```

---

## Manual walkthrough (not provable in-process)

Run `src/WinUIDesigner.Shell.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Shell.App.exe` and check by hand what the test process cannot see — the `TabView` renders nothing there, and the property grid is detached for the whole interaction fixture:

1. Tab headers show each document's name, with `*` while dirty.
2. Clicking a tab switches the surface, outline, property grid and window title.
3. The tab close button runs the save prompt; Cancel leaves the tab.
4. The Design / XAML buttons switch the pane, per document and independently.
5. Editing in XAML mode then pressing Ctrl+S writes the typed text to disk.
6. Opening a file that is already open switches to its tab instead of adding one.
7. Double-clicking an error jumps into XAML mode with the token selected.
8. Save All prompts once per never-saved document.

**Walked on 2026-09-08 against `6c779e2`, driven by synthetic input rather than by hand.**
All eight pass. What each one was checked against:

| # | Evidence |
|---|----------|
| 1 | Tab headers read `New1` and `Broken.xaml`; typing in the editor turned the header into `Broken.xaml*` and the window title into `Broken.xaml* - WinUI Designer`; saving cleared both, and Save As renamed the header to `New1.xaml`. |
| 2 | Clicking the `New1` tab switched the title to `New1 - WinUI Designer`, swapped the outline from `Grid/Canvas/Button/Button` back to a lone `Canvas`, and collapsed the error strip; clicking back restored all three. |
| 3 | The tab close button raised "Unsaved changes — Save changes to \"Broken.xaml\"?" with Save / Discard / Cancel. Cancel left the tab in place, still dirty, with its text intact. |
| 4 | With `New1` set to Design and `Broken.xaml` set to XAML, switching between the tabs kept each document's own mode. |
| 5 | Typing ` EDITED` into the XAML editor and pressing Ctrl+S wrote that exact text to `Broken.xaml` on disk and cleared the dirty marker. |
| 6 | Re-opening the already-open `Broken.xaml` through Ctrl+O switched to its existing tab: still two tabs, no reload, no second error dialog. |
| 7 | Double-clicking the error row switched to XAML mode and selected `Width=` at line 7, column 50 — the token the error names. |
| 8 | With both documents dirty, Save All raised exactly one Save As dialog (for the never-saved `New1`, defaulted to `New1.xaml`), then wrote both files. |

Two things the walkthrough settled that the test suite could not:

- The editor's monospace font resolves — the XAML pane renders in a fixed-pitch
  face, closing the spec's "font string unverified" gap.
- An opened file keeps its own text in the XAML view: the leading comment, the
  original indentation and the original attribute order all survived, which is
  what skipping re-serialization for an untouched document was meant to buy.

One defect found, and it predates this phase: the property grid's rows are wider
than the 280px right column (`MainWindow.cs:134`), so a long value is cut off at
the window edge with no horizontal scroll — `HorizontalAlignment` rendered as
`Windows.Foundatio`. The tabs phase did not touch that column.

## Plan self-review

**Spec coverage.** Decision 1 → Task 5 Step 3. 2 → Tasks 3 and 5. 3 → Task 5 Step 5. 4 → Task 5 Steps 4 and 5. 5 → Task 5 (`SyncSelectedTab`, `syncingSelection`). 6 → Task 5 Steps 7–8 and Task 2 Step 4 (`Open` de-dupe). 7 → Tasks 5 and 6 (every command routes through `PrepareForCloseAsync`). 8 → Task 6 `SaveAllAsync`. 9 → Task 6 `CloseAllAsync`. 10 → Task 1 `Mode` guard + `ReAssertingTheCurrentModeIsANoOp...`. 11 → Task 1 `DocumentMode.cs`. 12 → Task 2 Step 3. 13 → Task 3 editor. 14 → Task 1 `Text` setter + `NormaliseLineEndings`, Task 3 sync + `OpeningTheXamlViewDoesNotDirty...`. 15 → Task 1 `SelectionService` + `OnUndoStackChanged`. 16 → Task 1 `MalformedTextSwitchedToDesignMode...`. 17 → Task 1 remark (no code change; the null-checks in `OnSelectionChanged`/`OnUndoStackChanged` are left alone). 18 → Task 4 `MaxListHeight` + Task 7 row. 19 → Task 4 `XamlErrorNode`. 20 → Task 3 `JumpToError`/`OffsetOf`/`TokenLength` + the Step 5 delay measurement. 21 → Task 3 group name and suppression flag and layout. 22 → Task 5 `RememberDocument`/`ForgetDocument`/`OnDocumentPropertyChanged`. 23 → nothing to do; verified by Task 7 Step 6.

DoD 1 → Task 5 tests + `OnDocumentPropertyChanged` header update. 2 → Task 5 `ActivatingADocument...`. 3 → Task 5 `ACancelledTabClose...`. 4 → Task 6. 5 → Task 3 `ExactlyOneOfTheSurface...` and Task 1 (mode is per-`Document`). 6 → Task 1 `SwitchingToXamlMode...` and `AnEditMadeInXamlMode...`. 7 → Task 2. 8 → Task 1 `MalformedText...` and Task 7. 9 → Task 1 `ReAsserting...` and Task 3 `OffsetOf`. 10 → Tasks 4 and 7. 11 → Task 7 Step 6. 12 → Task 7 Step 5 and Task 5 Step 8.

**Placeholder scan.** No "TBD", "TODO", "handle edge cases" or "similar to Task N". Task 3 Step 5 is the one step whose outcome is not predetermined; it is written as a measurement with both branches spelled out, which is the honest form, not a placeholder.

**Type consistency.** `DocumentMode` is `{ Design, Xaml }` in Task 1 and used with those names in Tasks 2, 3, 5 and 7. `Document.NormaliseLineEndings` is `internal static string` in Task 1 and called that way from Tasks 1 and 3. `DocumentView(Document)`, `.Editor`, `.SurfaceVisibility`, `.Detach()`, `.JumpToError(XamlError)`, `.OffsetOf`, `.TokenLength` are declared in Task 3 and used with those exact names in Tasks 3, 5 and 7. `ErrorListView.Show(IReadOnlyList<XamlError>)`, `.List`, `.JumpRequested` (an `EventHandler<XamlError>`) and `MaxListHeight` are declared in Task 4 and used in Tasks 4 and 7. `MainWindow.CloseDecision` keeps the existing `{ Proceed, ClearDirtyBeforeClose }` shape and is used by `RequestCloseAsync` (Task 5) and `CloseAllAsync` (Task 6). `SaveAsync(Document, bool)` already exists with the signature `SaveAllAsync(Func<Document, bool, Task>)` expects.

**Ownership check.** `ShellModel.Close` removes the document, re-points `CurrentDocument`, and calls `doc.Dispose()` itself (`ShellModel.cs:179-183`) — and returns `false` without doing any of it when it refuses. So neither `RequestCloseAsync` nor `CloseAllAsync` disposes; a first draft of both did, which would have destroyed a document `Close` deliberately kept. Both are written above without it, and `ForgetDocument` (Task 5) does the view-side teardown from the `CollectionChanged` that `Close`'s `Documents.Remove` raises, so the view and the document are released by the same event rather than by two racing paths.

**One residual risk, stated rather than hidden.** `MainWindowInteractionTests` shares one `MainWindow` across the whole fixture via `OneTimeSetUp`, and Tasks 5–7 add tests that open and close documents in it. Test order is therefore load-bearing in a way it was not before. If the new tests interfere, the fix is for each new test to restore the document set it found — not to reorder them with `[Order]`, which hides the coupling instead of removing it.
