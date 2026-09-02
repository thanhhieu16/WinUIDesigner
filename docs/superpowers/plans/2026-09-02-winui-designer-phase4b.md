# Phase 4b: Container Placement, Snaplines, Drag-Move Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give container-specific placement behavior to Canvas/Grid/
StackPanel (position via `Canvas.Left/Top`, `Grid.Row/Column`, or reorder-
by-index instead of 4a's generic `Width`/`Height`-only resize), snap-to-
sibling-edge and snap-to-raster assistance during any resize or move, a
move affordance (corner drag handle + click-container-body) that can
reparent an item across containers with a single undo entry, and Grid
row/column structural editing (create/resize/pick-unit via
`GridAdornerProvider`).

**Architecture:** A short `IPlacementBehavior` chain
(`DefaultPlacementBehavior` [4a] → `RasterPlacementBehavior` →
`SnaplinePlacementBehavior` → `CanvasPlacementSupport`/
`GridPlacementSupport`) layers container-specific placement on top of
4a's engine without touching it; `StackPanelPlacementSupport` bases
directly on `DefaultPlacementBehavior` since reordering isn't a
coordinate-snapping concern. `PlacementOperation.ChangeContainer` (new)
and 4a's already-shipped-but-never-exercised
`DefaultPlacementBehavior.LeaveContainer`/`EnterContainer` are the
reparenting mechanism a new `MoveLogic` class drives, itself triggered by
two thin `AdornerProvider` extensions built on 4a's `DragListener`
primitive (extended with `PointerCaptureLost` handling).
`GridAdornerProvider`'s row/column editing is a separate, independent
drag life cycle from `DragListener`, since it resizes existing Grid
structure rather than moving a selected item.

**Tech Stack:** .NET 10 SDK (`10.0.302`), WinUI 3 / Windows App SDK 1.7
(`Microsoft.WindowsAppSDK` `1.7.250606001` — same pin as every prior
phase), NUnit 3 + NUnitLite (the existing authoritative `.exe` test
runner).

**Spec:** `docs/superpowers/specs/2026-09-02-winui-designer-phase4b-design.md`

## Global Constraints

- No new projects. All new files land in the existing
  `WinUIDesigner.DesignModel` and `WinUIDesigner.Designer` projects (and
  their `.Tests` counterparts) — `WinUIDesigner.sln` does not change this
  phase.
- New code lives under `D:\Project\ST4I\Research\WinUIDesigner`. The
  reference repo `F:\ReferencesCode\CSharp\WpfDesigner` is read-only
  source material — never edit it.
- `Nullable` stays `disable`, matching every prior phase.
- **Compiler-confirmed absent from WinUI (do not write code assuming
  otherwise):** `Rect.Inflate`, the static `Brushes` class,
  `UIElement.TranslatePoint`, `RowDefinition.Offset`/
  `ColumnDefinition.Offset`, `CombinedGeometry`/`GeometryCombineMode`,
  `Animatable`/`Geometry.BeginAnimation`/`RectAnimation`/the 3-arg
  `DoubleAnimation` constructor, `Visibility.Hidden` (only `Visible`/
  `Collapsed` exist), `DispatcherPriority.Loaded` (`DispatcherQueuePriority`
  has only `Low`/`Normal`/`High`), `InvalidateVisual`,
  `SolidColorBrush.Freeze()`, `DoubleCollection(IEnumerable<double>)`,
  single-arg `TextBox.GetRectFromCharacterIndex`. Every task below already
  routes around these — see the spec's "WinUI API surface" section for the
  exact compiler diagnostics if you need to re-verify.
- **Compiler-confirmed present, already used elsewhere in this port —
  reuse the same idiom:** `UIElement.PointerCaptureLost`,
  `PathGeometry.FillRule = FillRule.EvenOdd`,
  `Microsoft.UI.Dispatching.DispatcherQueuePriority`,
  `RowDefinition.ActualHeight`/`ColumnDefinition.ActualWidth`,
  `element.TransformToVisual(target).TransformPoint(point)` (the
  established replacement for every WPF coordinate-transform call).
- `DesignItemProperty`/`DesignItemPropertyCollection` are string-keyed
  only. Attached properties use `GetAttachedProperty(Type ownerType,
  string name)` (`src/WinUIDesigner.DesignModel/DesignItemProperty.cs:89`)
  — e.g. `Properties.GetAttachedProperty(typeof(Canvas), "Left")`, never a
  `DependencyProperty`-indexed call.
- `PointerRoutedEventArgs`/`KeyRoutedEventArgs` are sealed WinRT types
  with no public constructor — tests cannot fabricate one.
  `DragListener` (4a) exposes `SimulateDown`/`SimulateMove`/`SimulateUp`/
  `Cancel` for exactly this reason; every task whose drag is
  `DragListener`-based drives it through those methods only. The
  independent `GridAdornerProvider` splitter-drag controller (Tasks 12)
  is not `DragListener`-based and needs its own equivalent simulate seam
  — defined in that task.
- **Authoritative test command**, same reasoning as every prior phase
  (WinUI objects are thread-affine; `dotnet test`'s `testhost.exe` cannot
  activate them): run the built `.exe` directly —
  `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe`
  and
  `tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe`
  — not `dotnet test`. Build first with
  `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  (or the `.DesignModel.Tests` equivalent), then run the `.exe`, optionally
  filtered with `--test=Namespace.ClassName`.
- Phases 1-4a's suites must stay green throughout this plan. Re-run all
  `.exe`s as part of every task's verification step; treat any regression
  as a bug in that task, not something to patch around later.
- **`<RowDefinition Height="30"/>`/`<ColumnDefinition Width="40"/>` in XAML
  never reaches the live instance in this port (empirically confirmed,
  Task 10's implementation).** The model round-trips it correctly
  (`IsSet=True`, `TextValue="30"`, XAML output unchanged) but the
  string→`GridLength` conversion silently never applies to the live
  `RowDefinition`/`ColumnDefinition` object — `ActualHeight`/`ActualWidth`
  stay at the default star-share regardless of the XAML attribute. Setting
  a typed `GridLength` through the model **does** take effect
  (`rowItem.Properties["Height"].SetValue(new
  Microsoft.UI.Xaml.GridLength(30))`, confirmed). **Every task with a Grid
  test that needs specific row/column sizes must set them this way after
  load, not rely on the XAML attribute** — this is not specific to Task
  10, it applies to Tasks 11-13's `GridAdornerProvider` tests too.
- **`DesignItemProperty.Reset()` clears the model but not the live
  instance value** (empirically confirmed, Task 10). After `Reset()`,
  `IsSet` is `false` and the XAML attribute disappears, but the live
  `DependencyProperty` keeps its old value (e.g. `button.Width` stays at
  its prior number). Assert `IsSet == false` on the model for a `Reset()`
  outcome — never assert on the live value directly for that case.
- If a step's code needs an empirical fact this plan could not settle
  ahead of time, the task says so explicitly and gives the exact test
  that answers it. No silent assumption, no swallowed exception that
  hides a platform difference.
- Commit after each task passes its verification step.

---

## Background: why this is sixteen tasks, in this order

The spec's own revision history is the reason for the ordering below: two
rounds of adversarial review found the naive dependency order (build the
"main" placement behaviors first, treat `GridAdornerProvider` and
drag-move as an afterthought) was backwards, and found real load-bearing
files/members missing at every layer. This plan's task order follows the
corrected dependency graph directly:

1. **Task 1** resolves the spec's Known gap #1 — whether 4a's already-
   shipped-but-dead `LeaveContainer`/`EnterContainer` correctly reparent a
   `DesignItem` when actually driven through a `ChangeContainer` call.
   Every later task that touches cross-container move depends on this
   being proven, not assumed — same "investigate the biggest empirical
   risk first" precedent as Phase 3's Task 1 and Phase 4a's Task 1.
2. **Task 2** is the other small empirical-risk item the spec flagged
   (`DragListener` has no `PointerCaptureLost` handler) — independent of
   Task 1, cheap to prove now before Task 14 builds move-drag on top of
   it.
3. **Task 3** ports three small, independent `ExtensionServer` classes
   (`LogicalOrExtensionServer<A,B>`, `PrimarySelectionParentExtensionServer`,
   `DefaultExtensionServer.PermanentWithDesignPanel`) that Tasks 11-13 and
   15 need later — bundled into one task because each is a few lines,
   mechanically verified against the reference, and none has any
   consumer-side design decision attached.
4. **Tasks 4-6** build the `RasterPlacementBehavior`/
   `SnaplinePlacementBehavior` base chain (corrected order — the spec's
   first draft had this backwards). Task 4 is the shared plumbing
   (interface change, new `DesignPanel` properties, a new
   `OptionService`) both behaviors need; Tasks 5-6 are the behaviors
   themselves, in inheritance order.
5. **Task 7** ports `StackPanelPlacementSupport` — independent of the
   Raster/Snapline chain (it bases directly on `DefaultPlacementBehavior`
   per the spec), placed here because it's fully unblocked by this point
   and keeps unrelated small tasks from queueing up.
6. **Task 8** redesigns `GrayOutDesignerExceptActiveArea` — needed by both
   Task 9 (Canvas) and Task 10 (Grid), so it must land before either.
7. **Task 9** ports `CanvasPlacementSupport` (needs Tasks 6 and 8).
8. **Task 10** ports `GridPlacementSupport` (needs Tasks 6 and 8, same as
   Canvas).
9. **Tasks 11-13** port `GridAdornerProvider`'s three parts (rail visuals
   + row/column creation; the independent splitter-drag controller;
   `GridUnitSelector`) — the spec's biggest single finding (round 2): this
   is a three-part interaction system, not a rendering redesign, and each
   part is large enough to deserve its own task/review gate, matching the
   spec's own three separate DoD bullets for it. Needs Task 3's
   `LogicalOrExtensionServer`/`PrimarySelectionParentExtensionServer`.
10. **Task 14** builds `MoveLogic` and `TopLeftContainerDragHandle` — the
    primary move affordance, exercising Task 1's `ChangeContainer` and
    Task 2's extended `DragListener` end-to-end (drag, cross-container
    reparent, undo) for the first time.
11. **Task 15** adds `PanelMove`, a thin second trigger surface reusing
    Task 14's `MoveLogic` — small once Task 14 exists.
12. **Task 16** is the Definition-of-Done suite, exercising every DoD
    bullet from the spec end-to-end and giving Known gaps #1 and #3 their
    required tested resolutions.

---

### Task 1: PlacementOperation.ChangeContainer — resolves Known gap #1

**Files:**
- Modify: `src/WinUIDesigner.DesignModel/PlacementOperation.cs`
- Test: Create `tests/WinUIDesigner.Designer.Tests/PlacementOperationChangeContainerTests.cs`

**Interfaces:**
- Consumes: `DesignItem.GetBehavior<IPlacementBehavior>()` (Phase 2),
  `IPlacementBehavior.CanLeaveContainer`/`LeaveContainer`/`EnterContainer`
  (Phase 4a interface, implemented today only by `DefaultPlacementBehavior`
  — `src/WinUIDesigner.Designer/Extensions/DefaultPlacementBehavior.cs:61-70,96-108`
  — this task is their first real exercise), `UIElement.TransformToVisual(UIElement)`
  → `GeneralTransform.TransformPoint(Point)`, `PlacementInformation.OriginalBounds`/
  `.Bounds` (settable, Phase 4a), `Rect.Round()` extension
  (`src/WinUIDesigner.DesignModel/GeometryExtensions.cs`, Phase 4a),
  `DesignItem.Parent`, `DesignItem.View`, `DesignItem.ContentProperty`.
- Produces: `public void PlacementOperation.ChangeContainer(DesignItem newContainer)`
  — Task 14's `MoveLogic` calls this every time a drag crosses into a new
  container.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PlacementOperationChangeContainerTests
    {
        const string TwoCanvasXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Canvas x:Name=\"SourceCanvas\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" />" +
            "</Canvas>" +
            "<Canvas x:Name=\"TargetCanvas\" />" +
            "</Grid>";

        static (DesignSurface surface, DesignItem buttonItem, DesignItem sourceItem, DesignItem targetItem)
            LoadTwoCanvases(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(TwoCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();

            var root = (Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component;
            var sourceCanvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
            var targetCanvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[1];
            var button = (Microsoft.UI.Xaml.Controls.Button)sourceCanvas.Children[0];

            var services = surface.DesignContext.Services;
            var buttonItem = services.Component.GetDesignItem(button);
            var sourceItem = services.Component.GetDesignItem(sourceCanvas);
            var targetItem = services.Component.GetDesignItem(targetCanvas);
            return (surface, buttonItem, sourceItem, targetItem);
        }

        [Test]
        public void ChangeContainerMovesTheItemFromOneLiveParentToAnotherInTheSameDocument()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, targetItem) = LoadTwoCanvases(window);
                Assert.AreSame(sourceItem, buttonItem.Parent, "precondition: button starts under SourceCanvas");

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                op.ChangeContainer(targetItem);
                op.Commit();
                surface.UpdateLayout();

                Assert.AreSame(targetItem, buttonItem.Parent,
                    "button's DesignItem parent should now be TargetCanvas");
                Assert.IsFalse(sourceItem.ContentProperty.CollectionElements.Contains(buttonItem),
                    "SourceCanvas's Children collection should no longer contain the button");
                Assert.IsTrue(targetItem.ContentProperty.CollectionElements.Contains(buttonItem),
                    "TargetCanvas's Children collection should now contain the button");
            });
        }

        [Test]
        public void ChangeContainerToTheSameContainerIsANoOp()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, _) = LoadTwoCanvases(window);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                op.ChangeContainer(sourceItem);
                op.Commit();
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, buttonItem.Parent);
                Assert.IsTrue(sourceItem.ContentProperty.CollectionElements.Contains(buttonItem));
            });
        }

        [Test]
        public void UndoAfterChangeContainerRestoresTheOriginalParent()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, targetItem) = LoadTwoCanvases(window);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                op.ChangeContainer(targetItem);
                op.Commit();
                surface.UpdateLayout();

                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                Assert.IsTrue(undoService.CanUndo);
                undoService.Undo();
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, buttonItem.Parent,
                    "undo should restore the button's DesignItem parent to SourceCanvas");
                Assert.IsTrue(sourceItem.ContentProperty.CollectionElements.Contains(buttonItem));
                Assert.IsFalse(targetItem.ContentProperty.CollectionElements.Contains(buttonItem));
            });
        }

        [Test]
        public void ChangeContainerAfterCommitOrAbortThrows()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, targetItem) = LoadTwoCanvases(window);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                op.Commit();

                Assert.Throws<PlacementOperation.PlacementOperationException>(
                    () => op.ChangeContainer(targetItem));
            });
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL to compile with `CS1061: 'PlacementOperation' does not contain a definition for 'ChangeContainer'`.

- [ ] **Step 3: Implement ChangeContainer**

  Modify `src/WinUIDesigner.DesignModel/PlacementOperation.cs` — add the
  `using Microsoft.UI.Xaml.Media;` needed for `GeneralTransform`, then add
  this method and its private helper anywhere inside the `PlacementOperation`
  class (e.g. right after the constructor):

```csharp
        /// <summary>
        /// Makes the placed item(s) switch container. Ported from the reference's
        /// ChangeContainer - the only two changes from the reference are the coordinate math
        /// (GeneralTransform.Transform(Point)/Vector/Rect.TopLeft/Rect.Size don't exist in
        /// WinUI - TransformPoint plus component-wise double arithmetic replaces them) and a
        /// latent NullReferenceException the reference has (it Debug.Asserts
        /// currentContainerBehavior non-null after reassigning it, then dereferences
        /// unconditionally - Debug.Assert is a no-op in Release builds). This port validates
        /// the new behavior before it ever replaces the field, so a null new-container
        /// behavior throws a clear PlacementOperationException instead, and the catch below
        /// still finds the OLD (already-left) behavior on currentContainerBehavior to run
        /// Abort()'s EndPlacement against, instead of null-derefing there too.
        /// </summary>
        public void ChangeContainer(DesignItem newContainer)
        {
            if (newContainer == null) throw new ArgumentNullException(nameof(newContainer));
            if (isAborted || isCommitted) throw new PlacementOperationException("The operation is not running anymore.");
            if (currentContainer == newContainer) return;

            if (!currentContainerBehavior.CanLeaveContainer(this))
                throw new PlacementOperationException("The items cannot be removed from their parent container.");

            try {
                currentContainerBehavior.LeaveContainer(this);

                var transform = currentContainer.View.TransformToVisual(newContainer.View);
                foreach (var info in placedItems) {
                    info.OriginalBounds = TransformRectByMiddlePoint(transform, info.OriginalBounds);
                    info.Bounds = TransformRectByMiddlePoint(transform, info.Bounds).Round();
                }

                var newBehavior = newContainer.GetBehavior<IPlacementBehavior>();
                if (newBehavior == null)
                    throw new PlacementOperationException("The new container has no placement behavior.");

                currentContainer = newContainer;
                currentContainerBehavior = newBehavior;
                currentContainerBehavior.EnterContainer(this);
            } catch {
                Abort();
                throw;
            }
        }

        static Rect TransformRectByMiddlePoint(GeneralTransform transform, Rect r)
        {
            // We don't want to adjust the size of the control when moving it out of a scaled
            // container - just move it correctly. Transforming the middle point and taking the
            // component-wise delta is the Vector-free equivalent of the reference's
            // "transform.Transform(p) - p" Vector subtraction (Vector doesn't exist in WinUI).
            var p = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            var transformedP = transform.TransformPoint(p);
            double dx = transformedP.X - p.X;
            double dy = transformedP.Y - p.Y;
            return new Rect(r.X + dx, r.Y + dy, r.Width, r.Height);
        }
```

  Also update the stale doc comment at the top of the class (currently:
  "Ported from the reference's PlacementOperation.cs with ChangeContainer,
  DeleteItemsAndCommit, and TryStartInsertNewComponents dropped...") to
  remove `ChangeContainer` from that sentence, since it's ported now —
  `DeleteItemsAndCommit`/`TryStartInsertNewComponents` stay dropped (no
  4b caller either).

  Also update the comment above `Abort()`/`Commit()` (currently claims
  "Safe today because Start() only ever returns a PlacementOperation once
  currentContainerBehavior is non-null") — this is still true (the
  `ChangeContainer` guard above never lets `currentContainerBehavior`
  become null either), but reword it to say so explicitly rather than
  silently going stale now that `ChangeContainer` can reassign the field:

```csharp
        // Abort/Commit dereference currentContainerBehavior unguarded (no null check). Safe
        // because Start() only ever returns a PlacementOperation once currentContainerBehavior
        // is non-null, and ChangeContainer validates the new behavior before ever assigning it
        // to this field (see ChangeContainer above) - currentContainerBehavior can never become
        // null over this object's lifetime.
```

- [ ] **Step 4: Run tests to verify they pass**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj` then run the built `.exe` (path in Global Constraints) filtered with `--test=WinUIDesigner.Designer.Tests.PlacementOperationChangeContainerTests`.
  Expected: PASS, all 4 tests.

- [ ] **Step 5: Re-run Phase 1-4a suites, then commit**

  Run all four prior `.exe`s — expect no regressions.
  ```bash
  git add src/WinUIDesigner.DesignModel/PlacementOperation.cs \
          tests/WinUIDesigner.Designer.Tests/PlacementOperationChangeContainerTests.cs
  git commit -m "feat: add PlacementOperation.ChangeContainer (Phase 4b Task 1)"
  ```

---

### Task 2: DragListener PointerCaptureLost handling

**Files:**
- Modify: `src/WinUIDesigner.Designer/Controls/DragListener.cs`
- Modify: `tests/WinUIDesigner.Designer.Tests/DragListenerTests.cs`

**Interfaces:**
- Consumes: nothing new — extends the existing `DragListener` class
  (`Target.AddHandler`, `Cancel()`, `_capturedPointer`, all already in the
  4a file).
- Produces: no new public surface — `DragListener` now also self-heals
  from an OS-initiated pointer-capture loss, which every later task that
  builds on `DragListener` (Task 14) benefits from without doing anything
  extra.

- [ ] **Step 1: Write the failing test**

  Open `tests/WinUIDesigner.Designer.Tests/DragListenerTests.cs` and add
  this test (match the existing file's fixture helper for constructing a
  `DragListener` against a live target — reuse whatever helper the file
  already has for that; if the existing tests construct their target/
  fixture inline per test rather than via a shared helper, follow that
  same inline style so this test doesn't require a new abstraction):

```csharp
        [Test]
        public void PointerCaptureLostMidDragCancelsTheGesture()
        {
            WinUITestHost.RunWithWindow(window => {
                var target = new Microsoft.UI.Xaml.Controls.Grid { Width = 100, Height = 100 };
                window.Content = target;
                window.Activate();
                var drag = new DragListener(target);

                bool completedFired = false;
                bool canceledAtCompletion = false;
                drag.Completed += d => { completedFired = true; canceledAtCompletion = d.IsCanceled; };

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(20, 20));
                Assert.IsTrue(drag.IsActive, "precondition: the drag must be active before capture loss");

                drag.RaisePointerCaptureLostForTest();

                Assert.IsFalse(drag.IsDown, "a lost capture must end the gesture the same way a release does");
                Assert.IsTrue(completedFired, "Completed must still fire, so callers can distinguish success from cancellation");
                Assert.IsTrue(canceledAtCompletion, "the gesture must be reported as canceled, not completed");
            });
        }
```

  This test needs a way to simulate `PointerCaptureLost` without a real
  `PointerRoutedEventArgs` (sealed, no public constructor — same reason
  `DragListener` already has `SimulateDown`/`SimulateMove`/`SimulateUp`).
  Step 3 adds `RaisePointerCaptureLostForTest()` alongside those for
  exactly this purpose.

- [ ] **Step 2: Run test to verify it fails**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL to compile with `CS1061: 'DragListener' does not contain a definition for 'RaisePointerCaptureLostForTest'`.

- [ ] **Step 3: Add PointerCaptureLost handling**

  In `src/WinUIDesigner.Designer/Controls/DragListener.cs`:

  1. In the constructor, alongside the three existing `AddHandler` calls
     and the `KeyDown` subscription, add:
     ```csharp
     Target.PointerCaptureLost += OnPointerCaptureLost;
     ```

  2. Add the handler near `OnPointerReleased` (same guard shape — only
     react if this is the pointer that started the drag):
     ```csharp
        void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            // The OS can revoke capture without a matching PointerReleased (e.g. a system
            // interruption). Without this handler IsDown would stay true forever and Completed
            // would never fire - stranding an open ChangeGroup, and for a move gesture that had
            // already called PlacementOperation.ChangeContainer mid-drag, a partially-reparented
            // item too. Cancel() is the same safe unwind Escape already uses.
            if (!IsDown || e.Pointer.PointerId != _capturedPointer?.PointerId) return;
            Cancel();
        }
     ```

  3. Add the test-only seam next to `SimulateDown`/`SimulateMove`/
     `SimulateUp`:
     ```csharp
        /// <summary>Test-only entry point: drives the same PointerCaptureLost path
        /// OnPointerCaptureLost does, without a real PointerRoutedEventArgs (which has no
        /// public constructor). See SimulateDown.</summary>
        public void RaisePointerCaptureLostForTest()
        {
            if (!IsDown) return;
            Cancel();
        }
     ```

  Note: `Cancel()` already calls `ReleaseCapture()`, which calls
  `Target.ReleasePointerCapture(_capturedPointer)` — releasing a capture
  that was already lost is a documented no-op in WinUI (it doesn't throw),
  so no extra guard is needed there.

- [ ] **Step 4: Run test to verify it passes**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj` then run the built `.exe` filtered with `--test=WinUIDesigner.Designer.Tests.DragListenerTests`.
  Expected: PASS, all tests (the new one plus every existing `DragListenerTests` test, unaffected).

- [ ] **Step 5: Re-run Phase 1-4a suites, then commit**

  ```bash
  git add src/WinUIDesigner.Designer/Controls/DragListener.cs \
          tests/WinUIDesigner.Designer.Tests/DragListenerTests.cs
  git commit -m "feat: add PointerCaptureLost handling to DragListener (Phase 4b Task 2)"
  ```

---

### Task 3: Small ExtensionServer ports

**Files:**
- Create: `src/WinUIDesigner.DesignModel/Extensions/LogicalExtensionServers.cs`
- Modify: `src/WinUIDesigner.DesignModel/Extensions/SelectionExtensionServers.cs`
- Modify: `src/WinUIDesigner.DesignModel/Extensions/DefaultExtension.cs`
- Test: Create `tests/WinUIDesigner.DesignModel.Tests/LogicalExtensionServersTests.cs`
- Test: Modify `tests/WinUIDesigner.DesignModel.Tests/SelectionExtensionServersTests.cs` if it exists, else create it

**Interfaces:**
- Consumes: `ExtensionServer`/`DefaultExtensionServer` base classes
  (`src/WinUIDesigner.DesignModel/Extensions/ExtensionServer.cs`,
  `DefaultExtension.cs`), `ExtensionServerAttribute(Type)`
  (`ExtensionServerAttribute.cs`), `ServiceContainer.ExtensionManager`,
  `ExtensionManager.GetExtensionServer(ExtensionServerAttribute)`
  (`ExtensionManager.cs:235`), `DesignItem.ParentChanged` event,
  `ISelectionService.PrimarySelectionChanged`.
- Produces: `LogicalOrExtensionServer<A, B> : DefaultExtensionServer`
  (`WinUIDesigner.DesignModel.Extensions` namespace) — Task 11 uses
  `LogicalOrExtensionServer<PrimarySelectionExtensionServer,
  PrimarySelectionParentExtensionServer>`.
  `PrimarySelectionParentExtensionServer : DefaultExtensionServer` — same
  consumer. `DefaultExtensionServer.PermanentWithDesignPanel` (nested
  class, `DefaultExtension.cs`) — Task 15's `PanelMove` uses it.

- [ ] **Step 1: Write the failing tests**

  Create `tests/WinUIDesigner.DesignModel.Tests/LogicalExtensionServersTests.cs`:

```csharp
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class LogicalExtensionServersTests
    {
        // A minimal ExtensionServer pair whose ShouldApplyExtensions is driven directly by
        // the test, so LogicalOrExtensionServer's OR logic can be verified without any real
        // selection/parent machinery.
        class AlwaysTrueServer : DefaultExtensionServer
        {
            public override bool ShouldApplyExtensions(DesignItem extendedItem) => true;
        }

        class AlwaysFalseServer : DefaultExtensionServer
        {
            public override bool ShouldApplyExtensions(DesignItem extendedItem) => false;
        }

        [Test]
        public void ReturnsTrueWhenEitherUnderlyingServerReturnsTrue()
        {
            WinUIDesigner.TestHost.WinUITestHost.RunWithWindow(window => {
                var context = WinUIDesigner.DesignModel.Xaml.XamlDesignContext.CreateForNewDocument(
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />");
                var server = context.Services.ExtensionManager.GetExtensionServer(
                    new ExtensionServerAttribute(typeof(LogicalOrExtensionServer<AlwaysTrueServer, AlwaysFalseServer>)));

                Assert.IsTrue(server.ShouldApplyExtensions(context.RootItem));
            });
        }

        [Test]
        public void ReturnsFalseWhenBothUnderlyingServersReturnFalse()
        {
            WinUIDesigner.TestHost.WinUITestHost.RunWithWindow(window => {
                var context = WinUIDesigner.DesignModel.Xaml.XamlDesignContext.CreateForNewDocument(
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />");
                var server = context.Services.ExtensionManager.GetExtensionServer(
                    new ExtensionServerAttribute(typeof(LogicalOrExtensionServer<AlwaysFalseServer, AlwaysFalseServer>)));

                Assert.IsFalse(server.ShouldApplyExtensions(context.RootItem));
            });
        }
    }
}
```

  > This test calls `XamlDesignContext.CreateForNewDocument(string xaml)` and
  > `context.RootItem`/`context.Services` — if the exact factory method name
  > differs in this codebase, use whichever existing helper Phase 2/3's own
  > `DesignModel.Tests` fixtures use to get a minimal live `DesignContext`
  > without a full `DesignSurface` (check an existing test file in
  > `tests/WinUIDesigner.DesignModel.Tests/` for the established pattern and
  > match its exact call, since `ExtensionManager`/`GetExtensionServer` only
  > needs a `DesignContext`, not a real visual tree).

  Add a test for `PrimarySelectionParentExtensionServer` to the same file
  or a new `SelectionExtensionServersTests.cs` (create it if it doesn't
  exist — search first):

```csharp
        [Test]
        public void PrimarySelectionParentExtensionServerAppliesOnlyToTheSelectedItemsParent()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Canvas x:Name=\"Inner\"><Button x:Name=\"Target\" /></Canvas></Grid>")))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var root = (Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component;
                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
                var button = (Microsoft.UI.Xaml.Controls.Button)canvas.Children[0];
                var services = surface.DesignContext.Services;
                var buttonItem = services.Component.GetDesignItem(button);
                var canvasItem = services.Component.GetDesignItem(canvas);
                var rootItem = surface.DesignContext.RootItem;

                var server = services.ExtensionManager.GetExtensionServer(
                    new ExtensionServerAttribute(typeof(PrimarySelectionParentExtensionServer)));

                Assert.IsFalse(server.ShouldApplyExtensions(canvasItem), "no selection yet - nothing should apply");

                services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);

                Assert.IsTrue(server.ShouldApplyExtensions(canvasItem), "Canvas is the selected button's parent");
                Assert.IsFalse(server.ShouldApplyExtensions(rootItem), "the root Grid is not the immediate parent");
                Assert.IsFalse(server.ShouldApplyExtensions(buttonItem), "the button itself is not its own parent");
            });
        }
```

  Add `using WinUIDesigner.DesignModel.Extensions;` to that file's usings.

- [ ] **Step 2: Run tests to verify they fail**

  Run: `dotnet build tests/WinUIDesigner.DesignModel.Tests/WinUIDesigner.DesignModel.Tests.csproj` and `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL to compile — `LogicalOrExtensionServer`/`PrimarySelectionParentExtensionServer` don't exist yet.

- [ ] **Step 3: Implement the three servers**

  Create `src/WinUIDesigner.DesignModel/Extensions/LogicalExtensionServers.cs`:

```csharp
using System;

namespace WinUIDesigner.DesignModel.Extensions
{
    /// <summary>
    /// Combines two extension servers using a logical OR. Ported from the reference's
    /// LogicalExtensionServers.cs - only LogicalOrExtensionServer&lt;A,B&gt; is ported
    /// (LogicalAndExtensionServer has no Phase 4b consumer).
    /// </summary>
    public sealed class LogicalOrExtensionServer<A, B> : DefaultExtensionServer
        where A : ExtensionServer
        where B : ExtensionServer
    {
        ExtensionServer _a;
        ExtensionServer _b;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            _a = Services.ExtensionManager.GetExtensionServer(new ExtensionServerAttribute(typeof(A)));
            _b = Services.ExtensionManager.GetExtensionServer(new ExtensionServerAttribute(typeof(B)));
            _a.ShouldApplyExtensionsInvalidated += OnShouldApplyExtensionsInvalidated;
            _b.ShouldApplyExtensionsInvalidated += OnShouldApplyExtensionsInvalidated;
        }

        void OnShouldApplyExtensionsInvalidated(object sender, DesignItemCollectionEventArgs e)
        {
            ReapplyExtensions(e.Items);
        }

        public override bool ShouldApplyExtensions(DesignItem extendedItem)
        {
            return _a.ShouldApplyExtensions(extendedItem) || _b.ShouldApplyExtensions(extendedItem);
        }
    }
}
```

  Add to `src/WinUIDesigner.DesignModel/Extensions/SelectionExtensionServers.cs`
  (inside the existing `namespace WinUIDesigner.DesignModel.Extensions` block,
  alongside `PrimarySelectionExtensionServer`/`OnlyOneItemSelectedExtensionServer`):

```csharp
    /// <summary>Applies an extension to whichever DesignItem is the primary selection's
    /// immediate parent - used by GridAdornerProvider (Task 11), so its rails show whether the
    /// Grid or one of its direct children is selected. Ported from the reference unchanged;
    /// DesignItem.ParentChanged already exists (Phase 2), no WPF-specific API.</summary>
    public class PrimarySelectionParentExtensionServer : DefaultExtensionServer
    {
        DesignItem primarySelection;
        DesignItem primarySelectionParent;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            this.Services.Selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
        }

        void OnPrimarySelectionChanged(object sender, EventArgs e)
        {
            DesignItem newPrimarySelection = this.Services.Selection.PrimarySelection;
            if (primarySelection != newPrimarySelection) {
                if (primarySelection != null) primarySelection.ParentChanged -= OnParentChanged;
                if (newPrimarySelection != null) newPrimarySelection.ParentChanged += OnParentChanged;
                primarySelection = newPrimarySelection;
                OnParentChanged(sender, e);
            }
        }

        void OnParentChanged(object sender, EventArgs e)
        {
            DesignItem newPrimarySelectionParent = primarySelection != null ? primarySelection.Parent : null;
            if (primarySelectionParent != newPrimarySelectionParent) {
                DesignItem oldPrimarySelectionParent = primarySelectionParent;
                primarySelectionParent = newPrimarySelectionParent;
                ReapplyExtensions(new DesignItem[] { oldPrimarySelectionParent, newPrimarySelectionParent });
            }
        }

        public override bool ShouldApplyExtensions(DesignItem extendedItem)
        {
            return primarySelectionParent == extendedItem;
        }
    }
```

  (`ReapplyExtensions` already guards against `null` entries in the
  collection it's given — `ExtensionManager.cs:53-60` — so passing
  possibly-`null` old/new parents through is safe, matching the
  reference's own behavior exactly.)

  Add to `src/WinUIDesigner.DesignModel/Extensions/DefaultExtension.cs`,
  inside the existing `DefaultExtensionServer` class alongside `Permanent`:

```csharp
        /// <summary>Special extension server like Permanent - skips applying extensions if
        /// there is no design panel (e.g. in a bare unit-test fixture without a real
        /// DesignSurface). Used by PanelMove (Task 15).</summary>
        internal sealed class PermanentWithDesignPanel : DefaultExtensionServer
        {
            public override bool ShouldApplyExtensions(DesignItem extendedItem)
            {
                return Services.GetService(typeof(WinUIDesigner.DesignModel.IDesignPanel)) != null;
            }
        }
```

  > If `IDesignPanel`'s exact namespace differs from
  > `WinUIDesigner.DesignModel.IDesignPanel`, use whichever one
  > `ServiceContainer.DesignPanel` (added in Phase 4a) actually resolves —
  > check `src/WinUIDesigner.DesignModel/ServiceContainer.cs` for the exact
  > type it uses.

- [ ] **Step 4: Run tests to verify they pass**

  Run: `dotnet build tests/WinUIDesigner.DesignModel.Tests/WinUIDesigner.DesignModel.Tests.csproj tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj` then run both built `.exe`s filtered to the new test classes.
  Expected: PASS, all tests.

- [ ] **Step 5: Re-run Phase 1-4a suites, then commit**

  ```bash
  git add src/WinUIDesigner.DesignModel/Extensions/LogicalExtensionServers.cs \
          src/WinUIDesigner.DesignModel/Extensions/SelectionExtensionServers.cs \
          src/WinUIDesigner.DesignModel/Extensions/DefaultExtension.cs \
          tests/WinUIDesigner.DesignModel.Tests/LogicalExtensionServersTests.cs \
          tests/WinUIDesigner.DesignModel.Tests/SelectionExtensionServersTests.cs
  git commit -m "feat: port LogicalOrExtensionServer, PrimarySelectionParentExtensionServer, PermanentWithDesignPanel (Phase 4b Task 3)"
  ```

---

### Task 4: Raster/Snapline plumbing — DesignPanel options, OptionService, IPlacementBehavior modifier parameter

**Files:**
- Modify: `src/WinUIDesigner.DesignModel/PlacementBehavior.cs`
- Modify: `src/WinUIDesigner.Designer/Extensions/DefaultPlacementBehavior.cs`
- Modify: `src/WinUIDesigner.Designer/Extensions/ResizeThumbExtension.cs`
- Modify: `src/WinUIDesigner.Designer/DesignPanel.cs`
- Create: `src/WinUIDesigner.Designer/Services/OptionService.cs`
- Modify: `src/WinUIDesigner.Designer/DesignSurface.cs`
- Modify: `tests/WinUIDesigner.DesignModel.Tests/PlacementOperationTests.cs` (the `RecordingPlacementBehavior` stub implements `IPlacementBehavior` and must follow the signature change)
- Test: Create `tests/WinUIDesigner.Designer.Tests/PlacementModifierThreadingTests.cs`

**Interfaces:**
- Consumes: `PlacementOperation.Start`/`CurrentContainerBehavior`/`PlacedItems`, `DragListener.KeyModifiers` (`Windows.System.VirtualKeyModifiers`), `ServiceContainer.GetService<T>()`, `DesignSurface.LoadDesigner(XmlReader, XamlLoadSettings)`.
- Produces (resolves spec Known gaps #5 and #6):
  - `void IPlacementBehavior.BeforeSetPosition(PlacementOperation operation, Windows.System.VirtualKeyModifiers modifiers)` (was `BeforeSetPosition(PlacementOperation)`)
  - `Windows.Foundation.Point IPlacementBehavior.PlacePoint(Windows.Foundation.Point point, Windows.System.VirtualKeyModifiers modifiers)` (was `PlacePoint(Point)`)
  - `bool DesignPanel.UseSnaplinePlacement { get; set; }` (default `true`), `bool DesignPanel.UseRasterPlacement { get; set; }` (default `false`), `int DesignPanel.RasterWidth { get; set; }` (default `5`)
  - `WinUIDesigner.Designer.Services.OptionService`, public fields `bool SnaplinePlacementRoundValues` (default `false`) and `bool GrayOutDesignSurfaceExceptParentContainerWhenDragging` (default `true`) — **both** fields, resolving Known gap #6: the reference class is exactly these two fields and both have a Phase 4b caller (the first from Task 6's `SnaplinePlacementBehavior`, the second from Task 8's `GrayOutDesignerExceptActiveArea`) — registered by `DesignSurface.LoadDesigner`. **Task 8 consumes this file and this registration line; it must not recreate either.**

> This task's blast radius on the existing interface is small and fully accounted for: `PlacePoint` currently has zero callers anywhere in the port; `BeforeSetPosition` has exactly one (`ResizeThumbExtension.cs`); and the `RecordingPlacementBehavior` test stub in `PlacementOperationTests.cs` is the only other `IPlacementBehavior` implementor outside this plan's own new files.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/WinUIDesigner.Designer.Tests/PlacementModifierThreadingTests.cs
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    /// <summary>
    /// Records the VirtualKeyModifiers value each IPlacementBehavior entry point is handed, so a
    /// test can prove the modifier really travels from the originating pointer event down into
    /// the behavior instead of being read ambiently (which WinUI cannot do). RelativePanel is
    /// the container type on purpose: nothing else in this port's Phase 4b scope registers a
    /// placement behavior for it (Canvas/Grid/StackPanel only), so this extension is the one and
    /// only IPlacementBehavior on that item and no later task can collide with it.
    /// </summary>
    [ExtensionFor(typeof(RelativePanel), OverrideExtension = typeof(DefaultPlacementBehavior))]
    public sealed class RecordingModifierPlacementBehavior : DefaultPlacementBehavior
    {
        public VirtualKeyModifiers? LastBeforeSetPositionModifiers { get; private set; }
        public VirtualKeyModifiers? LastPlacePointModifiers { get; private set; }

        public override void BeforeSetPosition(PlacementOperation operation, VirtualKeyModifiers modifiers)
        {
            LastBeforeSetPositionModifiers = modifiers;
            base.BeforeSetPosition(operation, modifiers);
        }

        public override Point PlacePoint(Point point, VirtualKeyModifiers modifiers)
        {
            LastPlacePointModifiers = modifiers;
            return base.PlacePoint(point, modifiers);
        }
    }

    [TestFixture]
    public class PlacementModifierThreadingTests
    {
        const string PanelXaml =
            "<RelativePanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</RelativePanel>";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window, out DesignItem panelItem, out DesignItem buttonItem)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            var settings = new XamlLoadSettings();
            // The test assembly is not one of DesignSurface.LoadDesigner's own DesignerAssemblies,
            // so its [ExtensionFor] types are only discovered when it is added explicitly.
            settings.DesignerAssemblies.Add(typeof(PlacementModifierThreadingTests).Assembly);
            using (var reader = XmlReader.Create(new StringReader(PanelXaml)))
                surface.LoadDesigner(reader, settings);
            surface.UpdateLayout();

            panelItem = surface.DesignContext.RootItem;
            var button = (Button)((RelativePanel)panelItem.Component).Children[0];
            buttonItem = surface.DesignContext.Services.Component.GetDesignItem(button);
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void DesignPanelExposesTheRasterAndSnaplineOptionsWithTheReferenceDefaults()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out _, out _);
                Assert.IsTrue(surface.DesignPanel.UseSnaplinePlacement, "UseSnaplinePlacement defaults to true in the reference");
                Assert.IsFalse(surface.DesignPanel.UseRasterPlacement, "UseRasterPlacement defaults to false in the reference");
                Assert.AreEqual(5, surface.DesignPanel.RasterWidth, "RasterWidth defaults to 5 in the reference");
            });
        }

        [Test]
        public void OptionServiceIsRegisteredOnALoadedDesignerWithRoundingOff()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out _, out _);
                var options = surface.DesignContext.Services.GetService<OptionService>();
                Assert.IsNotNull(options, "DesignSurface.LoadDesigner must register OptionService");
                Assert.IsFalse(options.SnaplinePlacementRoundValues, "SnaplinePlacementRoundValues defaults to false in the reference");
                Assert.IsTrue(options.GrayOutDesignSurfaceExceptParentContainerWhenDragging,
                    "GrayOutDesignSurfaceExceptParentContainerWhenDragging defaults to true in the reference");
            });
        }

        [Test]
        public void PlacePointAndBeforeSetPositionReceiveTheModifiersTheyArePassed()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var panelItem, out var buttonItem);
                var behavior = panelItem.Extensions.OfType<RecordingModifierPlacementBehavior>().Single();
                Assert.AreSame(behavior, panelItem.GetBehavior<IPlacementBehavior>(),
                    "OverrideExtension must make the recording behavior win over DefaultPlacementBehavior");

                var operation = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                try {
                    operation.CurrentContainerBehavior.PlacePoint(new Point(1, 2), VirtualKeyModifiers.Control);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.Shift);
                } finally {
                    operation.Abort();
                }

                Assert.AreEqual(VirtualKeyModifiers.Control, behavior.LastPlacePointModifiers);
                Assert.AreEqual(VirtualKeyModifiers.Shift, behavior.LastBeforeSetPositionModifiers);
            });
        }

        [Test]
        public void ResizingWithControlHeldThreadsThatModifierAllTheWayIntoBeforeSetPosition()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var panelItem, out var buttonItem);
                var behavior = panelItem.Extensions.OfType<RecordingModifierPlacementBehavior>().Single();
                var extension = buttonItem.Extensions.OfType<ResizeThumbExtension>().Single();
                var drag = extension.DragListeners[PlacementAlignment.BottomRight];

                drag.SimulateDown(new Point(0, 0));
                drag.SimulateMove(new Point(20, 15), VirtualKeyModifiers.Control);
                Assert.AreEqual(VirtualKeyModifiers.Control, behavior.LastBeforeSetPositionModifiers,
                    "ResizeThumbExtension must pass the drag's live KeyModifiers, not a default");

                drag.SimulateMove(new Point(25, 20), VirtualKeyModifiers.None);
                Assert.AreEqual(VirtualKeyModifiers.None, behavior.LastBeforeSetPositionModifiers,
                    "releasing Ctrl mid-drag must be visible to the behavior on the very next tick");

                drag.SimulateUp(new Point(25, 20));
            });
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL — `CS0246: The type or namespace name 'OptionService' could not be found`, `CS1061: 'DesignPanel' does not contain a definition for 'UseSnaplinePlacement'` (and `UseRasterPlacement`/`RasterWidth`), `CS1501: No overload for method 'BeforeSetPosition' takes 2 arguments`, `CS0115: 'RecordingModifierPlacementBehavior.PlacePoint(Point, VirtualKeyModifiers)': no suitable method found to override`.

- [ ] **Step 3: Write the implementation**

  **3a.** Replace `src/WinUIDesigner.DesignModel/PlacementBehavior.cs` in full:

```csharp
using System.Collections.Generic;
using Windows.Foundation;
using Windows.System;

namespace WinUIDesigner.DesignModel
{
    /// <summary>Behavior interface implemented by container elements to support resizing child
    /// elements. No WPF-specific API - mechanical port, except that PlacePoint and
    /// BeforeSetPosition carry an explicit VirtualKeyModifiers argument: the reference reads
    /// Keyboard.IsKeyDown(Key.LeftCtrl) ambiently inside RasterPlacementBehavior/
    /// SnaplinePlacementBehavior, and WinUI has no ambient keyboard-state query - modifiers are
    /// only available from the originating pointer event (same choice Phase 3's DesignPanel
    /// selection code and Phase 4a's ResizeThumbExtension aspect-lock already made).</summary>
    public interface IPlacementBehavior
    {
        bool CanPlace(IEnumerable<DesignItem> childItems, PlacementType type, PlacementAlignment position);
        void BeginPlacement(PlacementOperation operation);
        void EndPlacement(PlacementOperation operation);
        Rect GetPosition(PlacementOperation operation, DesignItem child);
        void BeforeSetPosition(PlacementOperation operation, VirtualKeyModifiers modifiers);
        void SetPosition(PlacementInformation info);
        bool CanLeaveContainer(PlacementOperation operation);
        void LeaveContainer(PlacementOperation operation);
        bool CanEnterContainer(PlacementOperation operation, bool shouldAlwaysEnter);
        void EnterContainer(PlacementOperation operation);
        Point PlacePoint(Point point, VirtualKeyModifiers modifiers);
    }

    /// <summary>Behavior interface for root elements (Parent == null).</summary>
    public interface IRootPlacementBehavior : IPlacementBehavior
    {
    }
}
```

  **3b.** In `src/WinUIDesigner.Designer/Extensions/DefaultPlacementBehavior.cs`, update the two overrides:

```csharp
        public virtual void BeforeSetPosition(PlacementOperation operation, Windows.System.VirtualKeyModifiers modifiers) { }
```

```csharp
        public virtual Point PlacePoint(Point point, Windows.System.VirtualKeyModifiers modifiers) => new Point(System.Math.Round(point.X), System.Math.Round(point.Y));
```

  **3c.** In `src/WinUIDesigner.Designer/Extensions/ResizeThumbExtension.cs`, its one `BeforeSetPosition` call site:

```csharp
                operation.CurrentContainerBehavior.BeforeSetPosition(operation, drag.KeyModifiers);
```

  **3d.** In `tests/WinUIDesigner.DesignModel.Tests/PlacementOperationTests.cs`, update the `RecordingPlacementBehavior` stub's two matching members:

```csharp
            public void BeforeSetPosition(PlacementOperation operation, Windows.System.VirtualKeyModifiers modifiers) { }
```

```csharp
            public Windows.Foundation.Point PlacePoint(Windows.Foundation.Point point, Windows.System.VirtualKeyModifiers modifiers) => point;
```

  **3e.** In `src/WinUIDesigner.Designer/DesignPanel.cs`, insert immediately after the `IsAdornerLayerHitTestVisible` property:

```csharp
		/// <summary>Enables/disables snapline placement. Same default (true) as the reference's
		/// DesignPanel.UseSnaplinePlacement. Plain auto-property, not the reference's
		/// INotifyPropertyChanged-backed one: the reference raises OnPropertyChanged purely so
		/// DesignSurface.xaml's toolbar can two-way bind to it, and this port's DesignSurface is
		/// code-only with no such toolbar.</summary>
		public bool UseSnaplinePlacement { get; set; } = true;

		/// <summary>Enables/disables raster placement. Same default (false) as the reference's
		/// DesignPanel.UseRasterPlacement - see UseSnaplinePlacement for why this is a plain
		/// auto-property.</summary>
		public bool UseRasterPlacement { get; set; } = false;

		/// <summary>The raster cell size used when UseRasterPlacement is on. Same default (5)
		/// and same int type as the reference's DesignPanel.RasterWidth.</summary>
		public int RasterWidth { get; set; } = 5;
```

  **3f.** Create `src/WinUIDesigner.Designer/Services/OptionService.cs`:

```csharp
namespace WinUIDesigner.Designer.Services
{
    /// <summary>
    /// Designer-wide options. Ported whole from the reference's Services/OptionService.cs - the
    /// whole reference class is these two fields, and both have a Phase 4b caller
    /// (SnaplinePlacementRoundValues from Task 6's SnaplinePlacementBehavior;
    /// GrayOutDesignSurfaceExceptParentContainerWhenDragging from Task 8's
    /// GrayOutDesignerExceptActiveArea), so the "port only the surface this phase calls"
    /// precedent set by ModelTools.cs/PlacementOperation.cs is satisfied by porting both. The
    /// reference's third consumer, InfoTextEnterArea.cs, was dropped in Phase 4a and is not
    /// coming back. Public fields, not properties, matching the reference.
    /// </summary>
    public sealed class OptionService
    {
        /// <summary>Whether the design surface is grayed out except the active container while
        /// dragging/selecting.</summary>
        public bool GrayOutDesignSurfaceExceptParentContainerWhenDragging = true;

        /// <summary>Whether the values should be rounded when using snapline placement.</summary>
        public bool SnaplinePlacementRoundValues = false;
    }
}
```

  **3g.** In `src/WinUIDesigner.Designer/DesignSurface.cs`, inside `LoadDesigner`, immediately after the `ISelectionService` registration line:

```csharp
			// The reference registers OptionService inside XamlDesignContext's own constructor
			// (Xaml/XamlDesignContext.cs:80). That is not possible here: OptionService lives in
			// WinUIDesigner.Designer and XamlDesignContext lives in WinUIDesigner.DesignModel,
			// which does not (and must not) reference Designer. Registered here instead, exactly
			// like IDesignPanel and ISelectionService above.
			loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(OptionService), new OptionService()));
```

- [ ] **Step 4: Run test to verify it passes**

  Run:
  ```
  dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj
  dotnet build tests/WinUIDesigner.DesignModel.Tests/WinUIDesigner.DesignModel.Tests.csproj
  tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe
  tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe
  ```
  Expected: PASS, no regressions from the interface change.

- [ ] **Step 5: Commit**
  ```bash
  git add src/WinUIDesigner.DesignModel/PlacementBehavior.cs src/WinUIDesigner.Designer/Extensions/DefaultPlacementBehavior.cs src/WinUIDesigner.Designer/Extensions/ResizeThumbExtension.cs src/WinUIDesigner.Designer/DesignPanel.cs src/WinUIDesigner.Designer/Services/OptionService.cs src/WinUIDesigner.Designer/DesignSurface.cs tests/WinUIDesigner.DesignModel.Tests/PlacementOperationTests.cs tests/WinUIDesigner.Designer.Tests/PlacementModifierThreadingTests.cs
  git commit -m "feat: add raster/snapline plumbing - DesignPanel options, OptionService, IPlacementBehavior modifier parameter (Phase 4b Task 4)"
  ```

---

### Task 5: RasterPlacementBehavior.cs

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/RasterPlacementBehavior.cs`
- Create: `tests/WinUIDesigner.Designer.Tests/TestPlacementBehaviors.cs`
- Test: Create `tests/WinUIDesigner.Designer.Tests/RasterPlacementBehaviorTests.cs`

**Interfaces:**
- Consumes (Task 4): `IPlacementBehavior.PlacePoint(Point, VirtualKeyModifiers)`, `.BeforeSetPosition(PlacementOperation, VirtualKeyModifiers)`, `DesignPanel.UseRasterPlacement`, `DesignPanel.RasterWidth`. Consumes (existing): `DefaultPlacementBehavior` (base), `ModelTools.GetWidth/GetHeight(UIElement)`, `AdornerPanel.SetAdornedElement/SetPlacement`, `AdornerPlacement.FillContent`.
- Produces: `public class WinUIDesigner.Designer.Extensions.RasterPlacementBehavior : DefaultPlacementBehavior`, overriding `BeginPlacement`/`EndPlacement`/`EnterContainer`/`LeaveContainer`/`BeforeSetPosition`/`PlacePoint` — the base class Task 6 subclasses. Carries **no** `[ExtensionFor]`, exactly like the reference — it exists only as a base class, so it is unreachable by any test until a subclass registers itself (see the test-only `TestContainerPlacementBehavior` below, on `SwapChainPanel` — a real `Panel` no production extension in this port's Phase 4b scope claims, chosen because `RelativePanel` arranges every child at `(0,0)` regardless of `Margin`, verified empirically, and Canvas/Grid/StackPanel are all claimed by other tasks).

- [ ] **Step 1: Write the failing test**

  Create `tests/WinUIDesigner.Designer.Tests/TestPlacementBehaviors.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Extensions;

namespace WinUIDesigner.Designer.Tests
{
    /// <summary>
    /// RasterPlacementBehavior and SnaplinePlacementBehavior carry no [ExtensionFor] of their
    /// own (the reference doesn't either) - they exist only as bases for
    /// CanvasPlacementSupport/GridPlacementSupport (Tasks 9-10), and ExtensionForAttribute is
    /// Inherited=false, read by ExtensionManager with GetCustomAttributes(..., inherit: false).
    /// So nothing applies them to a real item and nothing can test them until a subclass
    /// registers itself. This is that subclass. Rebased onto SnaplinePlacementBehavior once
    /// Task 6 lands (below) - Task 5's own tests keep passing because they force
    /// UseSnaplinePlacement = false.
    ///
    /// SwapChainPanel is the container type on purpose: it is a real Panel (collection
    /// ContentProperty "Children"), it arranges a Left/Top-aligned child at its Margin offset -
    /// verified empirically, unlike RelativePanel, which puts every child at (0,0) - and no
    /// extension in this port or anywhere in Phase 4b's plan registers for it (the
    /// container-specific behaviors claim Canvas, Grid and StackPanel only), so this test-only
    /// registration can never collide with a production one.
    /// </summary>
    [ExtensionFor(typeof(SwapChainPanel), OverrideExtension = typeof(DefaultPlacementBehavior))]
    public sealed class TestContainerPlacementBehavior : RasterPlacementBehavior
    {
    }
}
```

  Create `tests/WinUIDesigner.Designer.Tests/RasterPlacementBehaviorTests.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class RasterPlacementBehaviorTests
    {
        const string PanelXaml =
            "<SwapChainPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</SwapChainPanel>";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window, out DesignItem buttonItem)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(RasterPlacementBehaviorTests).Assembly);
            using (var reader = XmlReader.Create(new StringReader(PanelXaml)))
                surface.LoadDesigner(reader, settings);
            surface.UpdateLayout();

            var panel = (SwapChainPanel)surface.DesignContext.RootItem.Component;
            buttonItem = surface.DesignContext.Services.Component.GetDesignItem(panel.Children[0]);

            // Isolate the raster path: this same test-only extension is rebased onto
            // SnaplinePlacementBehavior by Task 6, and snapline placement (on by default) would
            // otherwise take precedence over raster in PlacePoint.
            surface.DesignPanel.UseSnaplinePlacement = false;
            surface.DesignPanel.UseRasterPlacement = true;
            return surface;
        }

        static int RasterLineCount(DesignSurface surface)
        {
            return surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<Canvas>()
                .Sum(c => c.Children.OfType<Line>().Count());
        }

        [Test]
        public void PlacePointRoundsDownToTheRasterCellWhileRasterPlacementIsOn()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var buttonItem);
                var operation = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                try {
                    Assert.AreEqual(new Point(10, 25),
                        operation.CurrentContainerBehavior.PlacePoint(new Point(13.4, 27.9), VirtualKeyModifiers.None));
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void PlacePointFallsBackToPlainRoundingWhenRasterPlacementIsOff()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var buttonItem);
                surface.DesignPanel.UseRasterPlacement = false;
                var operation = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                try {
                    Assert.AreEqual(new Point(13, 28),
                        operation.CurrentContainerBehavior.PlacePoint(new Point(13.4, 27.9), VirtualKeyModifiers.None));
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void HoldingControlSuppressesRasterSnappingForBothEntryPoints()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var buttonItem);
                var operation = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                try {
                    Assert.AreEqual(new Point(13, 28),
                        operation.CurrentContainerBehavior.PlacePoint(new Point(13.4, 27.9), VirtualKeyModifiers.Control),
                        "Ctrl must fall through to the base (plain-rounding) PlacePoint");

                    operation.PlacedItems[0].Bounds = new Rect(13, 27, 41, 22);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.Control);
                    Assert.AreEqual(new Rect(13, 27, 41, 22), operation.PlacedItems[0].Bounds,
                        "Ctrl must leave the operation bounds untouched");
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void BeforeSetPositionSnapsTheOperationBoundsToTheRaster()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var buttonItem);
                var operation = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                try {
                    operation.PlacedItems[0].Bounds = new Rect(13, 27, 41, 22);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                    Assert.AreEqual(new Rect(10, 25, 40, 20), operation.PlacedItems[0].Bounds);
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void TheRasterSurfaceIsCreatedOnBeginPlacementDrawnOnceAndTornDownOnEndPlacement()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var buttonItem);
                Assert.AreEqual(0, surface.DesignPanel.Adorners.Count, "no placement operation, no raster surface");

                var operation = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                Assert.GreaterOrEqual(surface.DesignPanel.Adorners.Count, 1, "BeginPlacement adds the raster surface");
                Assert.AreEqual(0, RasterLineCount(surface), "the raster is not drawn until it is actually used");

                operation.CurrentContainerBehavior.PlacePoint(new Point(13.4, 27.9), VirtualKeyModifiers.None);
                Assert.AreEqual(81, RasterLineCount(surface), "400px tall, 5px raster, inclusive loop => 81 horizontal lines");

                operation.CurrentContainerBehavior.PlacePoint(new Point(21.4, 33.9), VirtualKeyModifiers.None);
                Assert.AreEqual(81, RasterLineCount(surface), "rasterDrawn must stop a second draw pass");

                operation.CurrentContainerBehavior.PlacePoint(new Point(13.4, 27.9), VirtualKeyModifiers.Control);
                Assert.AreEqual(0, RasterLineCount(surface), "Ctrl clears the raster visuals");

                operation.Abort();
                Assert.AreEqual(0, surface.DesignPanel.Adorners.Count, "EndPlacement removes the raster surface");
            });
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL — `CS0246: The type or namespace name 'RasterPlacementBehavior' could not be found`.

- [ ] **Step 3: Write the implementation**

  Create `src/WinUIDesigner.Designer/Extensions/RasterPlacementBehavior.cs`:

```csharp
using System;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// Snap-to-raster placement base class. Ported from the reference's
    /// Extensions/RasterPlacementBehavior.cs. Like the reference it carries no [ExtensionFor]
    /// attribute of its own - it exists only as the base of SnaplinePlacementBehavior (Task 6)
    /// and, through it, CanvasPlacementSupport/GridPlacementSupport (Tasks 9-10).
    ///
    /// Three WinUI-specific changes from the reference:
    /// - Keyboard.IsKeyDown(Key.LeftCtrl) has no WinUI equivalent; the Ctrl check reads the
    ///   modifiers argument threaded from the originating pointer event instead (Task 4).
    /// - Brushes.Black doesn't exist in Microsoft.UI.Xaml.Media (compiler-confirmed) -
    ///   new SolidColorBrush(Microsoft.UI.Colors.Black).
    /// - ExtendedItem.Services.DesignPanel is GetRequiredService-backed in this port and throws
    ///   when no IDesignPanel is registered, unlike the reference's null-returning property.
    ///   Every "DesignPanel designPanel = ...Services.DesignPanel as DesignPanel" in the
    ///   reference becomes Services.GetService&lt;IDesignPanel&gt;() as DesignPanel, which
    ///   preserves the reference's null-check semantics.
    /// </summary>
    public class RasterPlacementBehavior : DefaultPlacementBehavior
    {
        Canvas surface;
        AdornerPanel adornerPanel;
        bool rasterDrawn = false;
        int raster = 5;

        public override void BeginPlacement(PlacementOperation operation)
        {
            base.BeginPlacement(operation);

            DesignPanel designPanel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            if (designPanel != null)
                raster = designPanel.RasterWidth;

            CreateSurface(operation);
        }

        public override void EndPlacement(PlacementOperation operation)
        {
            base.EndPlacement(operation);
            DeleteSurface();
        }

        public override void EnterContainer(PlacementOperation operation)
        {
            base.EnterContainer(operation);
            CreateSurface(operation);
        }

        public override void LeaveContainer(PlacementOperation operation)
        {
            base.LeaveContainer(operation);
            DeleteSurface();
        }

        void CreateSurface(PlacementOperation operation)
        {
            if (ExtendedItem.Services.GetService<IDesignPanel>() != null) {
                surface = new Canvas();
                adornerPanel = new AdornerPanel();
                adornerPanel.SetAdornedElement(ExtendedItem.View, ExtendedItem);
                AdornerPanel.SetPlacement(surface, AdornerPlacement.FillContent);
                adornerPanel.Children.Add(surface);
                ExtendedItem.Services.DesignPanel.Adorners.Add(adornerPanel);
            }
        }

        void DeleteSurface()
        {
            rasterDrawn = false;
            if (surface != null) {
                ExtendedItem.Services.DesignPanel.Adorners.Remove(adornerPanel);
                adornerPanel = null;
                surface = null;
            }
        }

        public override void BeforeSetPosition(PlacementOperation operation, VirtualKeyModifiers modifiers)
        {
            base.BeforeSetPosition(operation, modifiers);
            if (surface == null) return;

            DesignPanel designPanel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            if (designPanel == null || !designPanel.UseRasterPlacement)
                return;

            if ((modifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control) {
                surface.Children.Clear();
                rasterDrawn = false;
                return;
            }

            drawRaster();

            var bounds = operation.PlacedItems[0].Bounds;
            bounds.Y = ((int)bounds.Y / raster) * raster;
            bounds.X = ((int)bounds.X / raster) * raster;
            bounds.Width = Convert.ToInt32((bounds.Width / raster)) * raster;
            bounds.Height = Convert.ToInt32((bounds.Height / raster)) * raster;
            operation.PlacedItems[0].Bounds = bounds;
        }

        public override Point PlacePoint(Point point, VirtualKeyModifiers modifiers)
        {
            if (surface == null)
                return base.PlacePoint(point, modifiers);

            DesignPanel designPanel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            if (designPanel == null || !designPanel.UseRasterPlacement)
                return base.PlacePoint(point, modifiers);

            if ((modifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control) {
                surface.Children.Clear();
                rasterDrawn = false;
                return base.PlacePoint(point, modifiers);
            }

            drawRaster();

            point.Y = ((int)point.Y / raster) * raster;
            point.X = ((int)point.X / raster) * raster;

            return point;
        }

        void drawRaster()
        {
            if (!rasterDrawn) {
                rasterDrawn = true;

                var w = ModelTools.GetWidth(ExtendedItem.View);
                var h = ModelTools.GetHeight(ExtendedItem.View);
                // A DoubleCollection cannot be shared between two Shapes in WinUI - a second
                // StrokeDashArray assignment of the same instance throws ArgumentException
                // (E_INVALIDARG), verified at runtime - so unlike the reference's single hoisted
                // `dash` collection, this rebuilds the dash array per line.
                for (int i = 0; i <= h; i += raster) {
                    var line = new Line() {
                        X1 = 0,
                        Y1 = i,
                        X2 = w,
                        Y2 = i,
                        StrokeThickness = 1,
                        Stroke = new SolidColorBrush(Microsoft.UI.Colors.Black),
                        StrokeDashArray = new DoubleCollection() { 1, raster - 1 },
                    };
                    surface.Children.Add(line);
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

  Run:
  ```
  dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj
  tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe
  tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe
  ```
  Expected: PASS, no regressions. If `TheRasterSurfaceIsCreatedOnBeginPlacementDrawnOnceAndTornDownOnEndPlacement` fails with `System.ArgumentException: Value does not fall within the expected range` out of `Shape.set_StrokeDashArray`, the `new DoubleCollection()` was hoisted back out of the loop — that is the shared-collection restriction described in the comment, not a bad test expectation.

- [ ] **Step 5: Commit**
  ```bash
  git add src/WinUIDesigner.Designer/Extensions/RasterPlacementBehavior.cs tests/WinUIDesigner.Designer.Tests/TestPlacementBehaviors.cs tests/WinUIDesigner.Designer.Tests/RasterPlacementBehaviorTests.cs
  git commit -m "feat: port RasterPlacementBehavior (Phase 4b Task 5)"
  ```

---

### Task 6: SnaplinePlacementBehavior.cs

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/SnaplinePlacementBehavior.cs`
- Modify: `tests/WinUIDesigner.Designer.Tests/TestPlacementBehaviors.cs` (rebase `TestContainerPlacementBehavior` from `RasterPlacementBehavior` onto `SnaplinePlacementBehavior`, mirroring the production chain)
- Test: Create `tests/WinUIDesigner.Designer.Tests/SnaplinePlacementBehaviorTests.cs`

**Interfaces:**
- Consumes (Task 4): the modifier parameter, `DesignPanel.UseSnaplinePlacement`, `OptionService.SnaplinePlacementRoundValues`. Consumes (Task 5): `RasterPlacementBehavior` (base class). Consumes (existing): `DefaultPlacementBehavior.GetPositionRelativeToContainer`, `.CanPlace`, `PlacementInformation.ResizeThumbAlignment`, `DesignItem.ContentProperty.{IsCollection,CollectionElements,Value}`, `ModelTools.GetWidth/GetHeight`.
- Produces: `public class WinUIDesigner.Designer.Extensions.SnaplinePlacementBehavior : RasterPlacementBehavior` with `static bool GetDisableSnaplines(DependencyObject)`/`SetDisableSnaplines`/`DisableSnaplinesProperty`, `static double SnaplineMargin`/`SnaplineAccuracy`, `protected virtual void AddContainerSnaplines(Rect, List<Snapline>, List<Snapline>)`, `protected IEnumerable<DesignItem> GetSnapToDesignItems(PlacementOperation)`, `protected class Snapline` (public fields `double Offset, Start, End; bool RequireOverlap; int Group; double DrawOffset`) — the base class Task 9's `CanvasPlacementSupport` and Task 10's `GridPlacementSupport` subclass. **Task 10 already consumes `AddContainerSnaplines`/`Snapline`/`SnaplineMargin` at exactly this shape.**

- [ ] **Step 1: Write the failing test**

  Modify `tests/WinUIDesigner.Designer.Tests/TestPlacementBehaviors.cs` — change the one declaration line:

```csharp
    public sealed class TestContainerPlacementBehavior : SnaplinePlacementBehavior
```

  Create `tests/WinUIDesigner.Designer.Tests/SnaplinePlacementBehaviorTests.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class SnaplinePlacementBehaviorTests
    {
        // Sibling "A" is positioned from code rather than a XAML Margin literal: this port's
        // XAML parser converts a Thickness attribute string to the default 0,0,0,0 (verified -
        // Margin="150,150,0,0" loads as 0,0,0,0), a pre-existing Phase 1 XamlDom limitation
        // unrelated to placement. The snapline map is built from live layout
        // (TransformToVisual + real size), so setting the live Margin is exactly what these
        // tests need.
        const string PanelXaml =
            "<SwapChainPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Button x:Name=\"A\" Width=\"40\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "<Button x:Name=\"B\" Width=\"40\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</SwapChainPanel>";

        /// <summary>Loads the fixture and leaves sibling A occupying (150,150,40,20) and the
        /// item under test, B, at (0,0,40,20).</summary>
        static DesignSurface Load(Microsoft.UI.Xaml.Window window, out DesignItem movedItem)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(SnaplinePlacementBehaviorTests).Assembly);
            using (var reader = XmlReader.Create(new StringReader(PanelXaml)))
                surface.LoadDesigner(reader, settings);
            surface.UpdateLayout();

            var panel = (SwapChainPanel)surface.DesignContext.RootItem.Component;
            ((FrameworkElement)panel.Children[0]).Margin = new Thickness(150, 150, 0, 0);
            surface.UpdateLayout();

            movedItem = surface.DesignContext.Services.Component.GetDesignItem(panel.Children[1]);
            surface.DesignPanel.UseSnaplinePlacement = true;
            surface.DesignPanel.UseRasterPlacement = false;
            return surface;
        }

        static int SnaplineCount(DesignSurface surface)
        {
            return surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<Canvas>()
                .Sum(c => c.Children.OfType<Line>().Count());
        }

        [Test]
        public void PlacePointSnapsToASiblingsLeftAndTopEdges()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Move);
                try {
                    var behavior = operation.CurrentContainerBehavior;
                    Assert.AreEqual(new Point(150, 300), behavior.PlacePoint(new Point(152, 300), VirtualKeyModifiers.None),
                        "X snaps to sibling A's left edge, Y has nothing within SnaplineAccuracy");
                    Assert.AreEqual(new Point(300, 150), behavior.PlacePoint(new Point(300, 152), VirtualKeyModifiers.None),
                        "Y snaps to sibling A's top edge");
                    Assert.AreEqual(new Point(300, 300), behavior.PlacePoint(new Point(300, 300), VirtualKeyModifiers.None),
                        "a point with no snapline within range is left where it is");
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void HoldingControlSuppressesSnappingForBothEntryPoints()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Move);
                try {
                    Assert.AreEqual(new Point(152, 300),
                        operation.CurrentContainerBehavior.PlacePoint(new Point(152, 300), VirtualKeyModifiers.Control));

                    operation.PlacedItems[0].Bounds = new Rect(152, 300, 40, 20);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.Control);
                    Assert.AreEqual(new Rect(152, 300, 40, 20), operation.PlacedItems[0].Bounds);
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void BeforeSetPositionSnapsAMovesBoundsToTheSibling()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Move);
                try {
                    operation.PlacedItems[0].Bounds = new Rect(152, 300, 40, 20);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                    Assert.AreEqual(new Rect(150, 300, 40, 20), operation.PlacedItems[0].Bounds);

                    operation.PlacedItems[0].Bounds = new Rect(300, 300, 40, 20);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                    Assert.AreEqual(new Rect(300, 300, 40, 20), operation.PlacedItems[0].Bounds,
                        "nothing in range means nothing moves");
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void AResizeSnapsOnlyTheEdgeTheThumbIsDragging()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Resize);
                try {
                    var info = operation.PlacedItems[0];
                    info.ResizeThumbAlignment = PlacementAlignment.BottomRight;
                    info.Bounds = new Rect(0, 0, 152, 20);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                    Assert.AreEqual(new Rect(0, 0, 150, 20), info.Bounds,
                        "the dragged right edge snaps to sibling A's left edge; X and Height stay put");
                } finally {
                    operation.Abort();
                }
            });
        }

        [Test]
        public void SnaplinePlacementRoundValuesRoundsTheSnappedResizeBounds()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                var options = surface.DesignContext.Services.GetService<OptionService>();

                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Resize);
                try {
                    var info = operation.PlacedItems[0];
                    info.ResizeThumbAlignment = PlacementAlignment.Top;

                    options.SnaplinePlacementRoundValues = false;
                    info.Bounds = new Rect(0, 152, 40, 148.6);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                    Assert.AreEqual(new Rect(0, 150, 40, 150.6), info.Bounds,
                        "top-thumb resize moves Y to the snapline and grows Height by the same delta");

                    options.SnaplinePlacementRoundValues = true;
                    info.Bounds = new Rect(0, 152, 40, 148.6);
                    operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                    Assert.AreEqual(new Rect(0, 150, 40, 151), info.Bounds,
                        "with rounding on, the fractional Height is rounded");
                } finally {
                    options.SnaplinePlacementRoundValues = false;
                    operation.Abort();
                }
            });
        }

        [Test]
        public void SnaplineVisualsAreDrawnOnlyWhileAnEdgeIsActuallySnapped()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                Assert.AreEqual(0, surface.DesignPanel.Adorners.Count);

                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Move);
                // Two surfaces: RasterPlacementBehavior's and SnaplinePlacementBehavior's - both
                // BeginPlacement overrides run, one per level of the chain.
                Assert.AreEqual(2, surface.DesignPanel.Adorners.Count);
                Assert.AreEqual(0, SnaplineCount(surface));

                operation.PlacedItems[0].Bounds = new Rect(152, 300, 40, 20);
                operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                Assert.Greater(SnaplineCount(surface), 0, "a snapped edge draws its guide line");

                operation.PlacedItems[0].Bounds = new Rect(300, 300, 40, 20);
                operation.CurrentContainerBehavior.BeforeSetPosition(operation, VirtualKeyModifiers.None);
                Assert.AreEqual(0, SnaplineCount(surface), "moving out of range clears the guides");

                operation.Abort();
                Assert.AreEqual(0, surface.DesignPanel.Adorners.Count, "EndPlacement tears both surfaces down");
            });
        }

        [Test]
        public void DisableSnaplinesOnASiblingRemovesItFromTheSnaplineMap()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, out var movedItem);
                var panel = (SwapChainPanel)surface.DesignContext.RootItem.Component;
                SnaplinePlacementBehavior.SetDisableSnaplines(panel.Children[0], true);
                Assert.IsTrue(SnaplinePlacementBehavior.GetDisableSnaplines(panel.Children[0]));

                var operation = PlacementOperation.Start(new[] { movedItem }, PlacementType.Move);
                try {
                    Assert.AreEqual(new Point(152, 300),
                        operation.CurrentContainerBehavior.PlacePoint(new Point(152, 300), VirtualKeyModifiers.None),
                        "a sibling with DisableSnaplines set contributes no snaplines");
                } finally {
                    operation.Abort();
                }
            });
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL — `CS0246: The type or namespace name 'SnaplinePlacementBehavior' could not be found`.

- [ ] **Step 3: Write the implementation**

  Create `src/WinUIDesigner.Designer/Extensions/SnaplinePlacementBehavior.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.Designer.Services;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// Snap-to-sibling-edge placement base class. Ported from the reference's
    /// Extensions/SnaplinePlacementBehavior.cs. Like RasterPlacementBehavior (its base, per the
    /// reference's own chain SnaplinePlacementBehavior : RasterPlacementBehavior :
    /// DefaultPlacementBehavior) it carries no [ExtensionFor] of its own; it is the base of
    /// CanvasPlacementSupport/GridPlacementSupport (Tasks 9-10).
    ///
    /// WinUI substitutions beyond the ones RasterPlacementBehavior already documents
    /// (Keyboard.IsKeyDown, Brushes.X, Services.DesignPanel), all compiler-confirmed absent:
    /// - Rect.Inflate(dx, dy) - reconstructed as the private InflateRect helper below, which
    ///   also reproduces WPF's "an over-shrunk rect becomes Rect.Empty" rule that the reference
    ///   relies on for the negative-inflate call in BuildMaps (Windows.Foundation.Rect's
    ///   constructor throws on a negative width, so plain arithmetic is not enough).
    /// - UIElement.TranslatePoint(Point, UIElement) - TransformToVisual(target)
    ///   .TransformPoint(point), the replacement Phase 3/4a already established.
    /// - DoubleCollection(IEnumerable&lt;double&gt;) - the type has no such constructor
    ///   (CS1729); it does implement IList&lt;double&gt;, so a collection initializer produces the
    ///   identical value.
    /// - TextBox.GetRectFromCharacterIndex(int) - this SDK requires the two-argument
    ///   (int index, bool trailingEdge) overload; false selects the leading edge, matching WPF's
    ///   single-argument behavior.
    /// - OptionService is looked up defensively (null =&gt; do not round). The reference
    ///   dereferences GetService&lt;OptionService&gt;() unguarded because its XamlDesignContext
    ///   registers the service unconditionally; here it's registered by DesignSurface.
    ///   LoadDesigner (Task 4), so a bare XamlDesignContext fixture would NRE instead of simply
    ///   not rounding.
    /// </summary>
    public class SnaplinePlacementBehavior : RasterPlacementBehavior
    {
        public static bool GetDisableSnaplines(DependencyObject obj)
        {
            return (bool)obj.GetValue(DisableSnaplinesProperty);
        }

        public static void SetDisableSnaplines(DependencyObject obj, bool value)
        {
            obj.SetValue(DisableSnaplinesProperty, value);
        }

        public static readonly DependencyProperty DisableSnaplinesProperty =
            DependencyProperty.RegisterAttached("DisableSnaplines", typeof(bool), typeof(SnaplinePlacementBehavior), new PropertyMetadata(false));

        AdornerPanel adornerPanel;
        Canvas surface;
        List<Snapline> horizontalMap;
        List<Snapline> verticalMap;
        double? baseline;

        static double _snaplineMargin = 8;
        public static double SnaplineMargin {
            get { return _snaplineMargin; }
            set { _snaplineMargin = value; }
        }

        static double _snaplineAccuracy = 5;
        public static double SnaplineAccuracy {
            get { return _snaplineAccuracy; }
            set { _snaplineAccuracy = value; }
        }

        public override void BeginPlacement(PlacementOperation operation)
        {
            base.BeginPlacement(operation);
            CreateSurface(operation);
        }

        public override void EndPlacement(PlacementOperation operation)
        {
            base.EndPlacement(operation);
            DeleteSurface();
        }

        public override void EnterContainer(PlacementOperation operation)
        {
            base.EnterContainer(operation);
            CreateSurface(operation);
        }

        public override void LeaveContainer(PlacementOperation operation)
        {
            base.LeaveContainer(operation);
            DeleteSurface();
        }

        public override Point PlacePoint(Point point, VirtualKeyModifiers modifiers)
        {
            if (surface == null)
                return base.PlacePoint(point, modifiers);

            DesignPanel designPanel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            if (designPanel == null || !designPanel.UseSnaplinePlacement)
                return base.PlacePoint(point, modifiers);

            surface.Children.Clear();
            if ((modifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control)
                return base.PlacePoint(point, modifiers);

            Rect bounds = new Rect(point.X, point.Y, 0, 0);

            var horizontalInput = new List<Snapline>();
            var verticalInput = new List<Snapline>();

            AddLines(bounds, 0, false, horizontalInput, verticalInput, null);
            if (baseline.HasValue) {
                var textOffset = bounds.Top + baseline.Value;
                horizontalInput.Add(new Snapline() { Group = 1, Offset = textOffset, Start = bounds.Left, End = bounds.Right });
            }

            List<Snapline> drawLines;
            double delta;

            var newPoint = base.PlacePoint(point, modifiers);
            if (Snap(horizontalInput, horizontalMap, SnaplineAccuracy, out drawLines, out delta)) {
                foreach (var d in drawLines)
                    DrawLine(d.Start, d.Offset + d.DrawOffset, d.End, d.Offset + d.DrawOffset);
                point.Y += delta;
            } else
                point.Y = newPoint.Y;

            if (Snap(verticalInput, verticalMap, SnaplineAccuracy, out drawLines, out delta)) {
                foreach (var d in drawLines)
                    DrawLine(d.Offset + d.DrawOffset, d.Start, d.Offset + d.DrawOffset, d.End);
                point.X += delta;
            } else
                point.X = newPoint.X;

            return point;
        }

        public override void BeforeSetPosition(PlacementOperation operation, VirtualKeyModifiers modifiers)
        {
            base.BeforeSetPosition(operation, modifiers);
            if (surface == null) return;

            DesignPanel designPanel = ExtendedItem.Services.GetService<IDesignPanel>() as DesignPanel;
            if (designPanel == null || !designPanel.UseSnaplinePlacement)
                return;

            surface.Children.Clear();
            if ((modifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control) return;

            Rect bounds = Rect.Empty;
            foreach (var item in operation.PlacedItems)
                bounds.Union(item.Bounds);

            var horizontalInput = new List<Snapline>();
            var verticalInput = new List<Snapline>();
            var info = operation.PlacedItems[0];

            if (operation.Type == PlacementType.Resize) {
                AddLines(bounds, 0, false, horizontalInput, verticalInput, info.ResizeThumbAlignment);
            } else {
                AddLines(bounds, 0, false, horizontalInput, verticalInput, null);
                if (baseline.HasValue) {
                    var textOffset = bounds.Top + baseline.Value;
                    horizontalInput.Add(new Snapline() { Group = 1, Offset = textOffset, Start = bounds.Left, End = bounds.Right });
                }
            }

            List<Snapline> drawLines;
            double delta;

            if (Snap(horizontalInput, horizontalMap, SnaplineAccuracy, out drawLines, out delta)) {
                if (operation.Type == PlacementType.Resize) {
                    if (info.ResizeThumbAlignment != null && info.ResizeThumbAlignment.Value.Vertical == VerticalAlignment.Top) {
                        bounds.Y += delta;
                        bounds.Height = Math.Max(0, bounds.Height - delta);
                        if (RoundValues(operation)) { bounds.Y = Math.Round(bounds.Y); bounds.Height = Math.Round(bounds.Height); }
                    } else {
                        bounds.Height = Math.Max(0, bounds.Height + delta);
                        if (RoundValues(operation)) bounds.Height = Math.Round(bounds.Height);
                    }
                    info.Bounds = bounds;
                } else {
                    foreach (var item in operation.PlacedItems) {
                        var r = item.Bounds;
                        r.Y += delta;
                        if (RoundValues(operation)) r.Y = Math.Round(r.Y);
                        item.Bounds = r;
                    }
                }
                foreach (var d in drawLines)
                    DrawLine(d.Start, d.Offset + d.DrawOffset, d.End, d.Offset + d.DrawOffset);
            }

            if (Snap(verticalInput, verticalMap, SnaplineAccuracy, out drawLines, out delta)) {
                if (operation.Type == PlacementType.Resize) {
                    if (info.ResizeThumbAlignment != null && info.ResizeThumbAlignment.Value.Horizontal == HorizontalAlignment.Left) {
                        bounds.X += delta;
                        bounds.Width = Math.Max(0, bounds.Width - delta);
                        if (RoundValues(operation)) { bounds.X = Math.Round(bounds.X); bounds.Width = Math.Round(bounds.Width); }
                    } else {
                        bounds.Width = Math.Max(0, bounds.Width + delta);
                        if (RoundValues(operation)) bounds.Width = Math.Round(bounds.Width);
                    }
                    info.Bounds = bounds;
                } else {
                    foreach (var item in operation.PlacedItems) {
                        var r = item.Bounds;
                        r.X += delta;
                        if (RoundValues(operation)) r.X = Math.Round(r.X);
                        item.Bounds = r;
                    }
                }
                foreach (var d in drawLines)
                    DrawLine(d.Offset + d.DrawOffset, d.Start, d.Offset + d.DrawOffset, d.End);
            }
        }

        /// <summary>Replaces the reference's six inline
        /// operation.CurrentContainer.Services.GetService&lt;OptionService&gt;()
        /// .SnaplinePlacementRoundValues reads - see the class doc comment for why this is
        /// null-guarded here and not in the reference.</summary>
        static bool RoundValues(PlacementOperation operation)
        {
            var options = operation.CurrentContainer.Services.GetService<OptionService>();
            return options != null && options.SnaplinePlacementRoundValues;
        }

        void CreateSurface(PlacementOperation operation)
        {
            if (ExtendedItem.Services.GetService<IDesignPanel>() != null) {
                surface = new Canvas();
                adornerPanel = new AdornerPanel();
                adornerPanel.SetAdornedElement(ExtendedItem.View, ExtendedItem);
                AdornerPanel.SetPlacement(surface, AdornerPlacement.FillContent);
                adornerPanel.Children.Add(surface);
                ExtendedItem.Services.DesignPanel.Adorners.Add(adornerPanel);

                BuildMaps(operation);

                if (operation.Type != PlacementType.Resize && operation.PlacedItems.Count == 1)
                    baseline = GetBaseline(operation.PlacedItems[0].Item.View);
            }
        }

        IEnumerable<DesignItem> AllDesignItems(DesignItem designItem = null)
        {
            if (designItem == null && this.ExtendedItem.Services.GetService<IDesignPanel>() is DesignPanel)
                designItem = this.ExtendedItem.Services.DesignPanel.Context.RootItem;

            if (designItem?.ContentProperty != null) {
                if (designItem.ContentProperty.IsCollection) {
                    foreach (var collectionElement in designItem.ContentProperty.CollectionElements) {
                        if (collectionElement != null) yield return collectionElement;
                        foreach (var el in AllDesignItems(collectionElement))
                            if (el != null) yield return el;
                    }
                } else if (designItem.ContentProperty.Value != null) {
                    yield return designItem.ContentProperty.Value;
                    foreach (var el in AllDesignItems(designItem.ContentProperty.Value))
                        if (el != null) yield return el;
                }
            }
        }

        /// <summary>Returns the DesignItems for which snaplines are created.</summary>
        protected IEnumerable<DesignItem> GetSnapToDesignItems(PlacementOperation operation)
        {
            return AllDesignItems();
        }

        void BuildMaps(PlacementOperation operation)
        {
            horizontalMap = new List<Snapline>();
            verticalMap = new List<Snapline>();

            var containerRect = new Rect(0, 0, ModelTools.GetWidth(ExtendedItem.View), ModelTools.GetHeight(ExtendedItem.View));
            if (SnaplineMargin > 0)
                AddLines(containerRect, -SnaplineMargin, false);

            AddLines(containerRect, 0, false);

            AddContainerSnaplines(containerRect, horizontalMap, verticalMap);

            if (!CanPlace(operation.PlacedItems.Select(x => x.Item), operation.Type, PlacementAlignment.Center))
                return;

            foreach (var item in GetSnapToDesignItems(operation)
                     .Except(operation.PlacedItems.Select(f => f.Item))
                     .Where(x => x.View != null && !GetDisableSnaplines(x.View))) {
                if (item != null) {
                    var bounds = GetPositionRelativeToContainer(operation, item);
                    AddLines(bounds, 0, false);
                    if (SnaplineMargin > 0) AddLines(bounds, SnaplineMargin, true);
                    AddBaseline(item, bounds, horizontalMap);
                }
            }
        }

        protected virtual void AddContainerSnaplines(Rect containerRect, List<Snapline> horizontalMap, List<Snapline> verticalMap)
        {
        }

        void AddLines(Rect r, double inflate, bool requireOverlap)
        {
            AddLines(r, inflate, requireOverlap, horizontalMap, verticalMap, null);
        }

        void AddLines(Rect r, double inflate, bool requireOverlap, List<Snapline> h, List<Snapline> v, PlacementAlignment? filter)
        {
            if (r != Rect.Empty) {
                Rect r2 = InflateRect(r, inflate);

                if (filter == null || filter.Value.Vertical == VerticalAlignment.Top)
                    h.Add(new Snapline() { RequireOverlap = requireOverlap, Offset = r2.Top - 1, Start = r.Left, End = r.Right });
                if (filter == null || filter.Value.Vertical == VerticalAlignment.Bottom)
                    h.Add(new Snapline() { RequireOverlap = requireOverlap, Offset = r2.Bottom - 1, Start = r.Left, End = r.Right });
                if (filter == null || filter.Value.Horizontal == HorizontalAlignment.Left)
                    v.Add(new Snapline() { RequireOverlap = requireOverlap, Offset = r2.Left - 1, Start = r.Top, End = r.Bottom });
                if (filter == null || filter.Value.Horizontal == HorizontalAlignment.Right)
                    v.Add(new Snapline() { RequireOverlap = requireOverlap, Offset = r2.Right - 1, Start = r.Top, End = r.Bottom });

                if (filter == null) {
                    h.Add(new Snapline() { RequireOverlap = requireOverlap, Offset = r2.Top + Math.Abs((r2.Top - r2.Bottom) / 2) - 1, DrawOffset = 1, Start = r.Left, End = r.Right });
                    v.Add(new Snapline() { RequireOverlap = requireOverlap, Offset = r2.Left + Math.Abs((r2.Left - r2.Right) / 2) - 1, DrawOffset = 1, Start = r.Top, End = r.Bottom });
                }
            }
        }

        /// <summary>Windows.Foundation.Rect has no Inflate (CS1061). This reproduces WPF's
        /// Rect.Inflate(width, height) exactly, including its rule that a rect shrunk past zero
        /// in either dimension becomes Rect.Empty - BuildMaps depends on that rule for its
        /// negative-inflate (-SnaplineMargin) call on a small container.</summary>
        static Rect InflateRect(Rect r, double inflate)
        {
            double x = r.X - inflate;
            double y = r.Y - inflate;
            double width = r.Width + inflate + inflate;
            double height = r.Height + inflate + inflate;
            if (!(width >= 0 && height >= 0)) return Rect.Empty;
            return new Rect(x, y, width, height);
        }

        void AddBaseline(DesignItem item, Rect bounds, List<Snapline> list)
        {
            var baseline = GetBaseline(item.View);
            if (baseline.HasValue) {
                var textOffset = item.View.TransformToVisual(ExtendedItem.View).TransformPoint(new Point(0, baseline.Value)).Y;
                list.Add(new Snapline() { Group = 1, Offset = textOffset, Start = bounds.Left, End = bounds.Right });
            }
        }

        void DeleteSurface()
        {
            if (surface != null) {
                ExtendedItem.Services.DesignPanel.Adorners.Remove(adornerPanel);
                adornerPanel = null;
                surface = null;
                horizontalMap = null;
                verticalMap = null;
            }
        }

        void DrawLine(double x1, double y1, double x2, double y2)
        {
            if (double.IsInfinity(x1) || double.IsNaN(x1) || double.IsInfinity(y1) || double.IsNaN(y1) ||
                double.IsInfinity(x2) || double.IsNaN(x2) || double.IsInfinity(y2) || double.IsNaN(y2))
                return;

            var line1 = new Line() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = 1, Stroke = new SolidColorBrush(Microsoft.UI.Colors.White) };
            surface.Children.Add(line1);

            var line2 = new Line() {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = 1,
                Stroke = new SolidColorBrush(Microsoft.UI.Colors.Orange),
                StrokeDashArray = new DoubleCollection() { 5, 2 },
                StrokeDashOffset = x1 + y1
            };
            surface.Children.Add(line2);
        }

        static double? GetBaseline(UIElement element)
        {
            var textBox = element as TextBox;
            if (textBox != null) {
                var r = textBox.GetRectFromCharacterIndex(0, false).Bottom;
                return textBox.TransformToVisual(element).TransformPoint(new Point(0, r)).Y;
            }
            var textBlock = element as TextBlock;
            if (textBlock != null)
                return textBlock.TransformToVisual(element).TransformPoint(new Point(0, textBlock.ActualHeight)).Y;
            return null;
        }

        static bool Snap(List<Snapline> input, List<Snapline> map, double accuracy, out List<Snapline> drawLines, out double delta)
        {
            delta = double.MaxValue;
            drawLines = null;

            foreach (var inputLine in input) {
                foreach (var mapLine in map) {
                    if (Math.Abs(mapLine.Offset - inputLine.Offset) <= accuracy) {
                        if (!inputLine.RequireOverlap && !mapLine.RequireOverlap ||
                            Math.Max(inputLine.Start, mapLine.Start) < Math.Min(inputLine.End, mapLine.End)) {
                            if (mapLine.Group == inputLine.Group)
                                delta = mapLine.Offset - inputLine.Offset;
                        }
                    }
                }
            }

            if (delta == double.MaxValue) return false;
            var offsetDict = new Dictionary<double, Snapline>();

            foreach (var inputLine in input) {
                inputLine.Offset += delta;
                foreach (var mapLine in map) {
                    if (inputLine.Offset == mapLine.Offset) {
                        var offset = mapLine.Offset;
                        Snapline drawLine;
                        if (!offsetDict.TryGetValue(offset, out drawLine)) {
                            drawLine = new Snapline { Start = double.MaxValue, End = double.MinValue, DrawOffset = mapLine.DrawOffset };
                            offsetDict[offset] = drawLine;
                        }
                        drawLine.Offset = offset;
                        drawLine.Start = Math.Min(drawLine.Start, Math.Min(inputLine.Start, mapLine.Start));
                        drawLine.End = Math.Max(drawLine.End, Math.Max(inputLine.End, mapLine.End));
                    }
                }
            }

            drawLines = offsetDict.Values.ToList();
            return true;
        }

        [DebuggerDisplay("Snapline: {Offset}")]
        protected class Snapline
        {
            public double Offset;
            public double Start;
            public double End;
            public bool RequireOverlap;
            public int Group;
            public double DrawOffset = 0;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

  Run:
  ```
  dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj
  tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe
  tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe
  ```
  Expected: PASS, no regressions. Task 5's five raster tests must still pass unchanged after the rebase — they force `UseSnaplinePlacement = false`, so `SnaplinePlacementBehavior.PlacePoint`/`BeforeSetPosition` delegate straight to the raster implementations. If `SnaplinePlacementRoundValuesRoundsTheSnappedResizeBounds` reports the un-snapped bounds back, check that `InflateRect` returns `Rect.Empty` (not a throwing `new Rect(...)`) for the negative-inflate call.

- [ ] **Step 5: Commit**
  ```bash
  git add src/WinUIDesigner.Designer/Extensions/SnaplinePlacementBehavior.cs tests/WinUIDesigner.Designer.Tests/TestPlacementBehaviors.cs tests/WinUIDesigner.Designer.Tests/SnaplinePlacementBehaviorTests.cs
  git commit -m "feat: port SnaplinePlacementBehavior (Phase 4b Task 6)"
  ```

---

### Task 7: StackPanelPlacementSupport + ResizeThumbExtension.IsResizing

**Files:**
- Modify: `src/WinUIDesigner.Designer/Extensions/ResizeThumbExtension.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/StackPanelPlacementSupport.cs`
- Test: Create `tests/WinUIDesigner.Designer.Tests/StackPanelPlacementSupportTests.cs`

**Interfaces:**
- Consumes: `DefaultPlacementBehavior`'s virtuals — `BeginPlacement`/`EndPlacement`/`EnterContainer`/`LeaveContainer`/`SetPosition`; `DesignItemProperty.IsCollection`/`.CollectionElements` (`IObservableList<DesignItem>`: `IndexOf`/`Remove`/`Insert(int, DesignItem)`); `ServiceContainer.DesignPanel` → `IDesignPanel.Adorners`; `AdornerPanel.SetAdornedElement`/`SetPlacement`, `RelativePlacement` with `XOffset`/`YOffset`; `PlacementOperation.Start`/`.PlacedItems`/`.CurrentContainerBehavior`/`.Commit()`; `ResizeThumbExtension.DragListeners[PlacementAlignment]` (internal, `InternalsVisibleTo("WinUIDesigner.Designer.Tests")`, Phase 4a).
- Produces: `public bool ResizeThumbExtension.IsResizing { get; private set; }` — later phases and this task's own `StackPanelPlacementSupport` read it to suppress reorder work while an actual resize is in progress. `public class WinUIDesigner.Designer.Extensions.StackPanelPlacementSupport : DefaultPlacementBehavior` carrying `[ExtensionFor(typeof(StackPanel), OverrideExtension = typeof(DefaultPlacementBehavior))]`, plus internal test seams `AdornerPanel IndicatorAdornerPanel` and `Rectangle IndicatorRectangle`. No dependency on Tasks 4-6 (StackPanel reordering bases directly on `DefaultPlacementBehavior`, no Raster/Snapline chain — reordering isn't a coordinate-snapping concern).

- [ ] **Step 1: Write the failing test**

  Create `tests/WinUIDesigner.Designer.Tests/StackPanelPlacementSupportTests.cs`:

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
    public class StackPanelPlacementSupportTests
    {
        // Three 50px-tall children stacked vertically: rects (0,0,200,50), (0,50,200,50),
        // (0,100,200,50); their centers are at y = 25, 75, 125.
        const string VerticalXaml =
            "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Orientation=\"Vertical\" Width=\"200\" Height=\"300\">" +
            "<Button x:Name=\"A\" Height=\"50\" /><Button x:Name=\"B\" Height=\"50\" /><Button x:Name=\"C\" Height=\"50\" />" +
            "</StackPanel>";

        // Three 60px-wide children in a row: rects (0,0,60,100), (60,0,60,100), (120,0,60,100);
        // their centers are at x = 30, 90, 150.
        const string HorizontalXaml =
            "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Orientation=\"Horizontal\" Width=\"300\" Height=\"100\">" +
            "<Button x:Name=\"A\" Width=\"60\" /><Button x:Name=\"B\" Width=\"60\" /><Button x:Name=\"C\" Width=\"60\" />" +
            "</StackPanel>";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window, string xaml)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        /// <summary>Drives one placement tick the way a drag-move gesture will in Task 14
        /// (MoveLogic doesn't exist yet), and returns the dragged item's new collection index.</summary>
        static int MoveFirstChildTo(DesignSurface surface, Windows.Foundation.Rect bounds)
        {
            var root = surface.DesignContext.RootItem;
            var a = root.ContentProperty.CollectionElements[0];
            var op = PlacementOperation.Start(new[] { a }, PlacementType.Move);
            var info = op.PlacedItems[0];
            info.Bounds = bounds;
            op.CurrentContainerBehavior.SetPosition(info);
            op.Commit();
            surface.UpdateLayout();
            return root.ContentProperty.CollectionElements.IndexOf(a);
        }

        [Test]
        public void AStackPanelResolvesToStackPanelPlacementSupportInsteadOfTheGenericBehavior()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, VerticalXaml);
                var behavior = surface.DesignContext.RootItem.GetBehavior<IPlacementBehavior>();
                Assert.IsInstanceOf<StackPanelPlacementSupport>(behavior);
            });
        }

        [Test]
        public void DraggingTheFirstChildPastBothSiblingsMovesItToTheEndOfTheCollection()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, VerticalXaml);
                // center y = 145, past every sibling center -> insert index 3, adjusted to 2.
                Assert.AreEqual(2, MoveFirstChildTo(surface, new Windows.Foundation.Rect(0, 120, 200, 50)));
                Assert.AreEqual(3, surface.DesignContext.RootItem.ContentProperty.CollectionElements.Count);
            });
        }

        [Test]
        public void DraggingTheFirstChildPastOneSiblingMovesItToTheMiddleOfTheCollection()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, VerticalXaml);
                // center y = 85: first sibling center >= 85 is C's (125) at index 2 -> adjusted to 1.
                Assert.AreEqual(1, MoveFirstChildTo(surface, new Windows.Foundation.Rect(0, 60, 200, 50)));
            });
        }

        // Guards the reference-bug fix in FindVerticalRectanglePlacementOffset: the reference
        // compares (Left + Top)/2 instead of (Left + Right)/2, which for this exact input yields
        // index 2 instead of the correct 1.
        [Test]
        public void DraggingInAHorizontalStackPanelUsesHorizontalCentersNotTheReferencesLeftPlusTop()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, HorizontalXaml);
                // center x = 100: first sibling center >= 100 is C's (150) at index 2 -> adjusted to 1.
                Assert.AreEqual(1, MoveFirstChildTo(surface, new Windows.Foundation.Rect(70, 0, 60, 100)));
            });
        }

        [Test]
        public void TheInsertionIndicatorIsAddedForTheDurationOfThePlacementOperationOnly()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, VerticalXaml);
                var root = surface.DesignContext.RootItem;
                var a = root.ContentProperty.CollectionElements[0];
                var extension = root.Extensions.OfType<StackPanelPlacementSupport>().Single();

                var op = PlacementOperation.Start(new[] { a }, PlacementType.Move);
                Assert.IsTrue(surface.DesignPanel.Adorners.Contains(extension.IndicatorAdornerPanel),
                    "BeginPlacement must add the insertion-point adorner");

                var info = op.PlacedItems[0];
                info.Bounds = new Windows.Foundation.Rect(0, 60, 200, 50);
                op.CurrentContainerBehavior.SetPosition(info);
                Assert.AreEqual(2, extension.IndicatorRectangle.Height, "a vertical StackPanel draws a 2px horizontal bar");

                op.Commit();
                Assert.IsFalse(surface.DesignPanel.Adorners.Contains(extension.IndicatorAdornerPanel),
                    "EndPlacement must remove the insertion-point adorner");
            });
        }

        [Test]
        public void ResizingAChildDoesNotReorderIt()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, VerticalXaml);
                var root = surface.DesignContext.RootItem;
                var a = root.ContentProperty.CollectionElements[0];
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { a }, SelectionTypes.Primary);
                surface.UpdateLayout();

                var resize = a.Extensions.OfType<ResizeThumbExtension>().Single();
                var drag = resize.DragListeners[PlacementAlignment.BottomRight];

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                Assert.IsFalse(resize.IsResizing, "IsResizing only turns on once the drag threshold is passed");

                drag.SimulateMove(new Windows.Foundation.Point(30, 200)); // far enough down to cross both sibling centers
                Assert.IsTrue(resize.IsResizing, "Drag_Started must set IsResizing");
                drag.SimulateUp(new Windows.Foundation.Point(30, 200));
                surface.UpdateLayout();

                Assert.IsFalse(resize.IsResizing, "Drag_Completed must clear IsResizing");
                Assert.AreEqual(0, root.ContentProperty.CollectionElements.IndexOf(a),
                    "a resize must not reorder the item inside its StackPanel");
            });
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL — `CS0246: The type or namespace name 'StackPanelPlacementSupport' could not be found` and `CS1061: 'ResizeThumbExtension' does not contain a definition for 'IsResizing'`.

- [ ] **Step 3: Write the implementation**

  Three edits to `src/WinUIDesigner.Designer/Extensions/ResizeThumbExtension.cs` — add the property just above the constructor:

```csharp
        /// <summary>True while a resize drag is in progress on this item. Read by
        /// StackPanelPlacementSupport, which must not reorder an item that is being resized
        /// rather than moved. Mirrors the reference's ResizeThumbExtension._isResizing (set at
        /// the end of drag_Started, cleared at the end of drag_Completed); note this only goes
        /// true once DragListener's threshold is crossed, since Drag_Started is what
        /// DragListener raises then - not on pointer-down.</summary>
        public bool IsResizing { get; private set; }

        public ResizeThumbExtension()
```

  set it at the end of `Drag_Started`:

```csharp
            operation = resizeBehavior != null ? PlacementOperation.Start(extendedItemArray, PlacementType.Resize) : null;
            IsResizing = true;
        }
```

  and clear it at the end of `Drag_Completed` (outside the `if (operation != null)` block, so a cancelled or behavior-less drag also clears it):

```csharp
                if (drag.IsCanceled) operation.Abort(); else operation.Commit();
                operation = null;
            }
            IsResizing = false;
        }
```

  New file `src/WinUIDesigner.Designer/Extensions/StackPanelPlacementSupport.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// Placement behavior for StackPanel. Ported from the reference's
    /// Extensions/StackPanelPlacementSupport.cs. It does NOT constrain a resize: it compares the
    /// dragged item's center against its siblings' centers to compute an insertion index,
    /// reorders the item to that index inside the parent's ContentProperty collection, and draws
    /// a 2px insertion-point bar. It bases directly on DefaultPlacementBehavior (no Raster/
    /// Snapline chain - reordering is not a coordinate-snapping concern).
    ///
    /// WinUI substitutions: UIElement.TranslatePoint -> TransformToVisual().TransformPoint()
    /// (absent, compiler-confirmed); Brushes.Black -> new SolidColorBrush(Microsoft.UI.Colors.Black)
    /// (no Brushes class in WinUI); Visibility.Hidden -> Visibility.Collapsed (WinUI's Visibility
    /// has only Visible/Collapsed).
    ///
    /// Three deliberate deviations from the reference, all bug fixes:
    /// (1) FindVerticalRectanglePlacementOffset compares (Left + Right)/2. The reference
    ///     computes (Left + Top)/2 - an X coordinate averaged with a Y coordinate - while its
    ///     vertical-orientation twin correctly uses (Top + Bottom)/2.
    /// (2) SetPosition recomputes "is this item being resized" on every call. The reference only
    ///     reassigns its _isItemGettingResized field when a ResizeThumbExtension is found, so the
    ///     flag goes stale when the item is not selected and has no such extension.
    /// (3) OnInitialized/BeginPlacement guard ContentProperty for null/non-collection. The
    ///     reference dereferences it unguarded, which throws for exactly the shapes
    ///     DefaultPlacementBehavior.OnInitialized already tolerates.
    /// ChangePositionTo additionally skips the remove+insert when the target index is the
    /// current one - identity in the reference too, but here it would otherwise push two no-op
    /// transaction items onto the open ChangeGroup on every drag tick.
    /// </summary>
    [ExtensionFor(typeof(StackPanel), OverrideExtension = typeof(DefaultPlacementBehavior))]
    public class StackPanelPlacementSupport : DefaultPlacementBehavior
    {
        StackPanel _stackPanel;
        AdornerPanel _adornerPanel;
        Rectangle _rectangle = new Rectangle();     // the insertion-point indicator
        readonly List<Rect> _rects = new List<Rect>(); // bounds of every child, panel-relative
        int _indexToInsert;

        /// <summary>Test seams, same idiom as ResizeThumbExtension.DragListeners: there is no
        /// other way to reach the exact adorner instance this behavior created.</summary>
        internal AdornerPanel IndicatorAdornerPanel { get { return _adornerPanel; } }
        internal Rectangle IndicatorRectangle { get { return _rectangle; } }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            _stackPanel = this.ExtendedItem.View as StackPanel;
            CollectChildRects();
        }

        void CollectChildRects()
        {
            _rects.Clear();
            var content = this.ExtendedItem.ContentProperty;
            if (content == null || !content.IsCollection || this.ExtendedItem.View == null) return;
            foreach (var child in content.CollectionElements) {
                if (child.View == null) continue;
                var p = child.View.TransformToVisual(this.ExtendedItem.View).TransformPoint(new Point(0, 0));
                _rects.Add(new Rect(p, child.View.RenderSize));
            }
        }

        public override void BeginPlacement(PlacementOperation operation)
        {
            base.BeginPlacement(operation);
            CollectChildRects();

            var designPanel = this.ExtendedItem.Services.DesignPanel;
            if (_adornerPanel != null && designPanel.Adorners.Contains(_adornerPanel))
                designPanel.Adorners.Remove(_adornerPanel);

            _adornerPanel = new AdornerPanel { Order = AdornerOrder.Foreground };
            _rectangle = new Rectangle { Fill = new SolidColorBrush(Microsoft.UI.Colors.Black), IsHitTestVisible = false };
            _adornerPanel.SetAdornedElement(this.ExtendedItem.View, this.ExtendedItem);
            _adornerPanel.Children.Add(_rectangle);
            designPanel.Adorners.Add(_adornerPanel);
        }

        public override void EndPlacement(PlacementOperation operation)
        {
            base.EndPlacement(operation);
            var designPanel = this.ExtendedItem.Services.DesignPanel;
            if (_adornerPanel != null && designPanel.Adorners.Contains(_adornerPanel))
                designPanel.Adorners.Remove(_adornerPanel);
        }

        public override void EnterContainer(PlacementOperation operation)
        {
            base.EnterContainer(operation);
            _rectangle.Visibility = Visibility.Visible;
        }

        public override void LeaveContainer(PlacementOperation operation)
        {
            base.LeaveContainer(operation);
            // Hide the bar while the item is being dragged into another container, otherwise it
            // renders on top of that container.
            _rectangle.Visibility = Visibility.Collapsed;
        }

        public override void SetPosition(PlacementInformation info)
        {
            base.SetPosition(info);

            bool isItemGettingResized = info.Item.Extensions.OfType<ResizeThumbExtension>().Any(e => e.IsResizing);
            if (_stackPanel == null || isItemGettingResized) return;

            if (_stackPanel.Orientation == Orientation.Vertical)
                DrawHorizontalRectangle(FindHorizontalRectanglePlacementOffset(info.Bounds));
            else
                DrawVerticalRectangle(FindVerticalRectanglePlacementOffset(info.Bounds));

            ChangePositionTo(info.Item, _indexToInsert);
        }

        void ChangePositionTo(DesignItem element, int index)
        {
            var content = this.ExtendedItem.ContentProperty;
            if (content == null || !content.IsCollection) return;
            var elements = content.CollectionElements;
            int elementIndex = elements.IndexOf(element);
            if (elementIndex >= 0 && index > elementIndex)
                index--;
            if (elementIndex == index) return;
            if (elementIndex >= 0)
                elements.Remove(element);
            elements.Insert(index, element);
        }

        double FindHorizontalRectanglePlacementOffset(Rect rect)
        {
            _rects.Sort((r1, r2) => r1.Top.CompareTo(r2.Top));
            var itemCenter = (rect.Top + rect.Bottom) / 2;
            for (int i = 0; i < _rects.Count; i++) {
                var rectCenter = (_rects[i].Top + _rects[i].Bottom) / 2;
                if (rectCenter >= itemCenter) {
                    _indexToInsert = i;
                    return _rects[i].Top;
                }
            }
            _indexToInsert = _rects.Count;
            return _rects.Count > 0 ? _rects[_rects.Count - 1].Bottom : 0;
        }

        double FindVerticalRectanglePlacementOffset(Rect rect)
        {
            _rects.Sort((r1, r2) => r1.Left.CompareTo(r2.Left));
            var itemCenter = (rect.Left + rect.Right) / 2;
            for (int i = 0; i < _rects.Count; i++) {
                // Reference bug fixed here - see the class comment.
                var rectCenter = (_rects[i].Left + _rects[i].Right) / 2;
                if (rectCenter >= itemCenter) {
                    _indexToInsert = i;
                    return _rects[i].Left;
                }
            }
            _indexToInsert = _rects.Count;
            return _rects.Count > 0 ? _rects[_rects.Count - 1].Right : 0;
        }

        void DrawHorizontalRectangle(double offset)
        {
            _rectangle.Height = 2;
            // Clear the other axis so a stale explicit size can't survive an orientation flip
            // (the reference leaves it set).
            _rectangle.Width = double.NaN;
            AdornerPanel.SetPlacement(_rectangle,
                new RelativePlacement(HorizontalAlignment.Stretch, VerticalAlignment.Top) { YOffset = offset });
        }

        void DrawVerticalRectangle(double offset)
        {
            _rectangle.Width = 2;
            _rectangle.Height = double.NaN;
            AdornerPanel.SetPlacement(_rectangle,
                new RelativePlacement(HorizontalAlignment.Left, VerticalAlignment.Stretch) { XOffset = offset });
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`, then run the built `.exe` filtered with `--test=WinUIDesigner.Designer.Tests.StackPanelPlacementSupportTests`.
  Expected: PASS, all 6 tests. Then re-run every prior `.exe` unfiltered — `IsResizing` is additive, and `OverrideExtension` only displaces `DefaultPlacementBehavior` on `StackPanel`, which no Phase 3/4a test uses.

- [ ] **Step 5: Commit**

  ```bash
  git add src/WinUIDesigner.Designer/Extensions/ResizeThumbExtension.cs \
          src/WinUIDesigner.Designer/Extensions/StackPanelPlacementSupport.cs \
          tests/WinUIDesigner.Designer.Tests/StackPanelPlacementSupportTests.cs
  git commit -m "feat: add StackPanelPlacementSupport and ResizeThumbExtension.IsResizing (Phase 4b Task 7)"
  ```

---

### Task 8: GrayOutDesignerExceptActiveArea.cs (redesigned)

**Files:**
- Create: `src/WinUIDesigner.Designer/Controls/GrayOutDesignerExceptActiveArea.cs`
- Test: Create `tests/WinUIDesigner.Designer.Tests/GrayOutDesignerExceptActiveAreaTests.cs`

**Interfaces:**
- Consumes: `ServiceContainer.GetService<T>()`; `IDesignPanel` downcast to `WinUIDesigner.Designer.DesignPanel` for `.Context`; `DesignContext.RootItem.View`; `IDesignPanel.Adorners`; `AdornerPanel.SetAdornedElement(UIElement, DesignItem)` (accepts a null `DesignItem`), `AdornerOrder.BehindForeground`; `AdornerPlacement.FillContent`. Consumes (Task 4): `OptionService.GrayOutDesignSurfaceExceptParentContainerWhenDragging`, already registered by `DesignSurface.LoadDesigner` — **this task does not recreate `OptionService.cs` or touch `DesignSurface.cs` again; both already exist from Task 4.**
- Produces, matching the exact call shape Task 9's `CanvasPlacementSupport` and Task 10's `GridPlacementSupport` already use:
  - `public static void Start(ref GrayOutDesignerExceptActiveArea grayOut, ServiceContainer services, UIElement activeContainer)`
  - `public static void Start(ref GrayOutDesignerExceptActiveArea grayOut, ServiceContainer services, UIElement activeContainer, Rect activeRectInActiveContainer)`
  - `public static void Stop(ref GrayOutDesignerExceptActiveArea grayOut)`
  - `public void AnimateActiveAreaRectTo(Rect newRect)`, `public void SetActiveAreaRect(Rect rect)`

  Resolves the visual/geometry half of spec Known gap #2 (the transition-timing half is covered by Step 6/7 below).

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Threading;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class GrayOutDesignerExceptActiveAreaTests
    {
        const string CanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"300\" Height=\"200\">" +
            "<Button x:Name=\"Target\" Canvas.Left=\"20\" Canvas.Top=\"30\" Width=\"60\" Height=\"40\" />" +
            "</Canvas>";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(CanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void StartBuildsAnEvenOddCoverWithAHoleAndAddsItAsAnAdorner()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var root = surface.DesignContext.RootItem;
                GrayOutDesignerExceptActiveArea grayOut = null;

                GrayOutDesignerExceptActiveArea.Start(ref grayOut, surface.DesignContext.Services, root.View);
                surface.UpdateLayout();

                Assert.IsNotNull(grayOut);
                Assert.IsTrue(surface.DesignPanel.Adorners.Contains(grayOut.OwnerAdornerPanel));
                Assert.AreEqual(Microsoft.UI.Xaml.Media.FillRule.EvenOdd, grayOut.Geometry.FillRule,
                    "the cover-with-a-hole effect depends on even-odd filling");
                Assert.AreEqual(2, grayOut.Geometry.Figures.Count,
                    "one figure for the full-surface cover, one for the hole");

                // Start's 3-arg overload uses the whole active container as the hole.
                Assert.AreEqual(0, grayOut.ActiveAreaLeft, 0.01);
                Assert.AreEqual(0, grayOut.ActiveAreaTop, 0.01);
                Assert.AreEqual(300, grayOut.ActiveAreaWidth, 0.01);
                Assert.AreEqual(200, grayOut.ActiveAreaHeight, 0.01);
            });
        }

        [Test]
        public void AnimateActiveAreaRectToMovesTheHoleOverSeveralFramesAndRewritesTheHoleFigure()
        {
            GrayOutDesignerExceptActiveArea grayOut = null;
            int updatesBeforeAnimation = 0;

            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var root = surface.DesignContext.RootItem;
                GrayOutDesignerExceptActiveArea.Start(ref grayOut, surface.DesignContext.Services, root.View);
                surface.UpdateLayout();

                updatesBeforeAnimation = grayOut.HoleUpdateCount;
                grayOut.AnimateActiveAreaRectTo(new Windows.Foundation.Rect(10, 10, 100, 80));
            });

            // The transition is 100ms; give the dispatcher loop time to run it to completion.
            Thread.Sleep(500);

            WinUITestHost.Run(() => {
                Assert.AreEqual(10, grayOut.ActiveAreaLeft, 0.01);
                Assert.AreEqual(10, grayOut.ActiveAreaTop, 0.01);
                Assert.AreEqual(100, grayOut.ActiveAreaWidth, 0.01);
                Assert.AreEqual(80, grayOut.ActiveAreaHeight, 0.01);
                Assert.Greater(grayOut.HoleUpdateCount, updatesBeforeAnimation + 1,
                    "a real transition, not a single snap: the DoubleAnimations must tick more than once");

                var corner = ((Microsoft.UI.Xaml.Media.LineSegment)grayOut.Geometry.Figures[1].Segments[1]).Point;
                Assert.AreEqual(110, corner.X, 0.01);
                Assert.AreEqual(90, corner.Y, 0.01);
            });
        }

        [Test]
        public void StopClearsTheCallersReferenceAndLeavesTheAdornerUpForTheFadeOut()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var root = surface.DesignContext.RootItem;
                GrayOutDesignerExceptActiveArea grayOut = null;
                GrayOutDesignerExceptActiveArea.Start(ref grayOut, surface.DesignContext.Services, root.View);
                surface.UpdateLayout();

                var panel = grayOut.OwnerAdornerPanel;
                GrayOutDesignerExceptActiveArea.Stop(ref grayOut);

                Assert.IsNull(grayOut, "Stop must null the caller's field so a later Start can run");
                Assert.IsTrue(surface.DesignPanel.Adorners.Contains(panel),
                    "the adorner stays up until the fade-out finishes; its removal is timer-driven");
            });
        }

        [Test]
        public void StartDoesNothingWhenTheOptionIsTurnedOff()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var root = surface.DesignContext.RootItem;
                surface.DesignContext.Services.GetService<OptionService>()
                    .GrayOutDesignSurfaceExceptParentContainerWhenDragging = false;

                GrayOutDesignerExceptActiveArea grayOut = null;
                int adornersBefore = surface.DesignPanel.Adorners.Count;
                GrayOutDesignerExceptActiveArea.Start(ref grayOut, surface.DesignContext.Services, root.View);

                Assert.IsNull(grayOut);
                Assert.AreEqual(adornersBefore, surface.DesignPanel.Adorners.Count);
            });
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL — `CS0246: The type or namespace name 'GrayOutDesignerExceptActiveArea' could not be found`.

- [ ] **Step 3: Write the implementation**

  New file `src/WinUIDesigner.Designer/Controls/GrayOutDesignerExceptActiveArea.cs`:

```csharp
using System;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.Designer.Services;

namespace WinUIDesigner.Designer.Controls
{
    /// <summary>
    /// Grays out the whole design surface except one active rectangle. Redesigned rather than
    /// ported: three WPF mechanisms the reference is built on are absent from WinUI, all
    /// compiler-confirmed in the spec's "WinUI API surface" section.
    ///
    /// 1. CombinedGeometry / GeometryCombineMode.Exclude - neither type exists in
    ///    Microsoft.UI.Xaml.Media. Replaced by one PathGeometry with FillRule.EvenOdd holding
    ///    two closed rectangular figures: the full-surface cover and the hole. Even-odd filling
    ///    leaves the overlap unpainted, which is the same cover-with-a-hole result without a
    ///    geometry boolean op.
    /// 2. FrameworkElement.OnRender(DrawingContext) - WinUI has no immediate-mode render
    ///    override. This is an ordinary visual-tree Grid holding one Shapes.Path, the same
    ///    "code-only control, no generic.xaml" idiom as Controls/SelectionHighlight and
    ///    Controls/Thumbs. It derives from Grid because Border is sealed in this SDK (Phase 3
    ///    finding).
    /// 3. Animatable / Freezable.BeginAnimation / RectAnimation / the 3-arg DoubleAnimation
    ///    constructor - all absent, so AnimateActiveAreaRectTo cannot animate
    ///    RectangleGeometry.RectProperty the way the reference does. Replaced by four
    ///    DoubleAnimations inside a declarative Storyboard, targeting four custom
    ///    DependencyProperties on this element whose PropertyChangedCallback rewrites the hole
    ///    figure's four points. Verified by running it, not by reading docs: with
    ///    EnableDependentAnimation = true, Storyboard.SetTargetProperty(anim, "ActiveAreaLeft")
    ///    resolves a custom C#-registered DP, and the callback fires on the order of ten times
    ///    across a short animation - i.e. a real per-frame transition, not a single snap.
    ///
    /// Visual correctness (does the rendered result actually read as "everything gray except
    /// this rectangle", with no seam at the hole edge and correct z-order?) is the spec's Known
    /// gap #2 and is not unit-testable - see this task's human-eyeball step.
    /// </summary>
    public sealed class GrayOutDesignerExceptActiveArea : Grid
    {
        public const double MaxOpacity = 0.3;
        /// <summary>0.2s, the reference's own fade duration (new TimeSpan(2000000) ticks).</summary>
        static readonly TimeSpan FadeTime = new TimeSpan(2000000);
        /// <summary>100ms, the reference's own AnimateActiveAreaRectTo duration.</summary>
        static readonly TimeSpan RectTime = new TimeSpan(0, 0, 0, 0, 100);

        public static readonly DependencyProperty ActiveAreaLeftProperty = DependencyProperty.Register(
            "ActiveAreaLeft", typeof(double), typeof(GrayOutDesignerExceptActiveArea), new PropertyMetadata(0.0, OnHoleChanged));
        public static readonly DependencyProperty ActiveAreaTopProperty = DependencyProperty.Register(
            "ActiveAreaTop", typeof(double), typeof(GrayOutDesignerExceptActiveArea), new PropertyMetadata(0.0, OnHoleChanged));
        public static readonly DependencyProperty ActiveAreaWidthProperty = DependencyProperty.Register(
            "ActiveAreaWidth", typeof(double), typeof(GrayOutDesignerExceptActiveArea), new PropertyMetadata(0.0, OnHoleChanged));
        public static readonly DependencyProperty ActiveAreaHeightProperty = DependencyProperty.Register(
            "ActiveAreaHeight", typeof(double), typeof(GrayOutDesignerExceptActiveArea), new PropertyMetadata(0.0, OnHoleChanged));

        public double ActiveAreaLeft { get { return (double)GetValue(ActiveAreaLeftProperty); } set { SetValue(ActiveAreaLeftProperty, value); } }
        public double ActiveAreaTop { get { return (double)GetValue(ActiveAreaTopProperty); } set { SetValue(ActiveAreaTopProperty, value); } }
        public double ActiveAreaWidth { get { return (double)GetValue(ActiveAreaWidthProperty); } set { SetValue(ActiveAreaWidthProperty, value); } }
        public double ActiveAreaHeight { get { return (double)GetValue(ActiveAreaHeightProperty); } set { SetValue(ActiveAreaHeightProperty, value); } }

        static void OnHoleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((GrayOutDesignerExceptActiveArea)d).UpdateHoleFigure();
        }

        readonly Path _path;
        readonly PathGeometry _geometry;
        readonly PathFigure _coverFigure;
        readonly PathFigure _holeFigure;
        readonly SolidColorBrush _grayOutBrush;
        AdornerPanel _adornerPanel;
        DesignPanel _designPanel;
        Storyboard _rectStoryboard;
        Rect _currentAnimateActiveAreaRectToTarget;
        int _holeUpdateCount;

        /// <summary>Internal test seams, same idiom as ResizeThumbExtension.DragListeners.
        /// HoleUpdateCount is how a test tells a real multi-frame transition from a single snap.</summary>
        internal AdornerPanel OwnerAdornerPanel { get { return _adornerPanel; } }
        internal PathGeometry Geometry { get { return _geometry; } }
        internal int HoleUpdateCount { get { return _holeUpdateCount; } }

        public GrayOutDesignerExceptActiveArea()
        {
            _grayOutBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = MaxOpacity };
            _coverFigure = CreateRectFigure();
            _holeFigure = CreateRectFigure();
            // Both Figures and Segments must be assigned explicitly - WinUI does not create the
            // collections for you (verified by running it).
            _geometry = new PathGeometry { FillRule = FillRule.EvenOdd, Figures = new PathFigureCollection() };
            _geometry.Figures.Add(_coverFigure);
            _geometry.Figures.Add(_holeFigure);
            _path = new Path { Data = _geometry, Fill = _grayOutBrush, IsHitTestVisible = false };
            Children.Add(_path);
            IsHitTestVisible = false;
        }

        public Brush GrayOutBrush { get { return _grayOutBrush; } }

        static PathFigure CreateRectFigure()
        {
            var figure = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true, IsFilled = true, Segments = new PathSegmentCollection() };
            figure.Segments.Add(new LineSegment { Point = new Point(0, 0) });
            figure.Segments.Add(new LineSegment { Point = new Point(0, 0) });
            figure.Segments.Add(new LineSegment { Point = new Point(0, 0) });
            return figure;
        }

        static void SetFigureRect(PathFigure figure, Rect rect)
        {
            figure.StartPoint = new Point(rect.X, rect.Y);
            ((LineSegment)figure.Segments[0]).Point = new Point(rect.X + rect.Width, rect.Y);
            ((LineSegment)figure.Segments[1]).Point = new Point(rect.X + rect.Width, rect.Y + rect.Height);
            ((LineSegment)figure.Segments[2]).Point = new Point(rect.X, rect.Y + rect.Height);
        }

        /// <summary>Sets the full-cover figure - the reference's designSurfaceRectangle.</summary>
        public void SetDesignSurfaceRect(Rect rect) { SetFigureRect(_coverFigure, rect); }

        void UpdateHoleFigure()
        {
            _holeUpdateCount++;
            SetFigureRect(_holeFigure, new Rect(ActiveAreaLeft, ActiveAreaTop, Math.Max(0, ActiveAreaWidth), Math.Max(0, ActiveAreaHeight)));
        }

        /// <summary>Moves the hole immediately, with no transition.</summary>
        public void SetActiveAreaRect(Rect rect)
        {
            StopRectStoryboard();
            ActiveAreaLeft = rect.X;
            ActiveAreaTop = rect.Y;
            ActiveAreaWidth = rect.Width;
            ActiveAreaHeight = rect.Height;
            _currentAnimateActiveAreaRectToTarget = rect;
        }

        /// <summary>The reference's AnimateActiveAreaRectTo, called on every
        /// CanvasPlacementSupport/GridPlacementSupport SetPosition.</summary>
        public void AnimateActiveAreaRectTo(Rect newRect)
        {
            if (newRect.Equals(_currentAnimateActiveAreaRectToTarget)) return;
            _currentAnimateActiveAreaRectToTarget = newRect;
            StopRectStoryboard();
            _rectStoryboard = new Storyboard();
            AddDouble(_rectStoryboard, this, "ActiveAreaLeft", newRect.X, RectTime);
            AddDouble(_rectStoryboard, this, "ActiveAreaTop", newRect.Y, RectTime);
            AddDouble(_rectStoryboard, this, "ActiveAreaWidth", newRect.Width, RectTime);
            AddDouble(_rectStoryboard, this, "ActiveAreaHeight", newRect.Height, RectTime);
            _rectStoryboard.Begin();
        }

        /// <summary>WinUI's replacement for WPF's HandoffBehavior.SnapshotAndReplace, which has
        /// no WinUI equivalent: Storyboard.Stop() drops the held animated value back to the base
        /// value, so snapshot the current (animated) values first and write them back as the
        /// base values. Without this, retargeting mid-transition makes the hole jump.</summary>
        void StopRectStoryboard()
        {
            if (_rectStoryboard == null) return;
            double l = ActiveAreaLeft, t = ActiveAreaTop, w = ActiveAreaWidth, h = ActiveAreaHeight;
            _rectStoryboard.Stop();
            _rectStoryboard = null;
            ActiveAreaLeft = l; ActiveAreaTop = t; ActiveAreaWidth = w; ActiveAreaHeight = h;
        }

        /// <summary>EnableDependentAnimation is required: none of these targets are
        /// composition-thread-animatable properties, so without it the animation is dropped.</summary>
        static void AddDouble(Storyboard storyboard, DependencyObject target, string property, double to, TimeSpan duration)
        {
            var animation = new DoubleAnimation { To = to, Duration = new Duration(duration), EnableDependentAnimation = true, FillBehavior = FillBehavior.HoldEnd };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        static void AnimateOpacity(Brush brush, double to)
        {
            var storyboard = new Storyboard();
            AddDouble(storyboard, brush, "Opacity", to, FadeTime);
            storyboard.Begin();
        }

        public static void Start(ref GrayOutDesignerExceptActiveArea grayOut, ServiceContainer services, UIElement activeContainer)
        {
            if (activeContainer == null) throw new ArgumentNullException("activeContainer");
            Start(ref grayOut, services, activeContainer, new Rect(new Point(0, 0), activeContainer.RenderSize));
        }

        public static void Start(ref GrayOutDesignerExceptActiveArea grayOut, ServiceContainer services, UIElement activeContainer, Rect activeRectInActiveContainer)
        {
            if (services == null) throw new ArgumentNullException("services");
            if (activeContainer == null) throw new ArgumentNullException("activeContainer");

            var designPanel = services.GetService<IDesignPanel>() as DesignPanel;
            var optionService = services.GetService<OptionService>();
            if (designPanel == null || grayOut != null || optionService == null
                || !optionService.GrayOutDesignSurfaceExceptParentContainerWhenDragging)
                return;
            if (designPanel.Context == null || designPanel.Context.RootItem == null || designPanel.Context.RootItem.View == null)
                return;

            // The reference reads ((Border)designPanel.Child).Child.RenderSize; this port has no
            // Border/zoom wrapper - DesignPanel.Content is the root item's view itself.
            var rootView = designPanel.Context.RootItem.View;

            grayOut = new GrayOutDesignerExceptActiveArea();
            grayOut.SetDesignSurfaceRect(new Rect(new Point(0, 0), rootView.RenderSize));
            grayOut._designPanel = designPanel;
            grayOut._adornerPanel = new AdornerPanel { Order = AdornerOrder.BehindForeground };
            grayOut._adornerPanel.SetAdornedElement(rootView, null); // a null DesignItem is accepted
            grayOut._adornerPanel.Children.Add(grayOut);
            grayOut.SetActiveAreaRect(TransformRect(activeContainer, rootView, activeRectInActiveContainer));
            grayOut._grayOutBrush.Opacity = 0;
            AnimateOpacity(grayOut._grayOutBrush, MaxOpacity);
            designPanel.Adorners.Add(grayOut._adornerPanel);
        }

        /// <summary>Replaces the reference's 4-arg RectangleGeometry(rect, rx, ry, transform)
        /// ctor, which WinUI does not have. Transforms the rect's two opposite corners and
        /// rebuilds an axis-aligned Rect - equivalent for translation/scale (the only transforms
        /// the design surface applies between a container and the root), and a bounding box
        /// rather than an exact shape if a rotation is ever involved.</summary>
        static Rect TransformRect(UIElement from, UIElement to, Rect rect)
        {
            var transform = from.TransformToVisual(to);
            var p1 = transform.TransformPoint(new Point(rect.X, rect.Y));
            var p2 = transform.TransformPoint(new Point(rect.X + rect.Width, rect.Y + rect.Height));
            return new Rect(p1, p2);
        }

        public static void Stop(ref GrayOutDesignerExceptActiveArea grayOut)
        {
            if (grayOut == null) return;
            AnimateOpacity(grayOut._grayOutBrush, 0);
            var designPanel = grayOut._designPanel;
            var panelToRemove = grayOut._adornerPanel;
            // Same shape as the reference's DispatcherTimer: let the fade-out finish, then drop
            // the adorner. Microsoft.UI.Xaml.DispatcherTimer exists (compiler-confirmed).
            var timer = new DispatcherTimer { Interval = FadeTime };
            timer.Tick += (s, e) => {
                timer.Stop();
                if (designPanel != null && panelToRemove != null && designPanel.Adorners.Contains(panelToRemove))
                    designPanel.Adorners.Remove(panelToRemove);
            };
            timer.Start();
            grayOut = null;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`, then run the built `.exe` filtered with `--test=WinUIDesigner.Designer.Tests.GrayOutDesignerExceptActiveAreaTests`.
  Expected: PASS, all 4 tests. Then re-run every prior `.exe` unfiltered — this task touches no shared file (`OptionService`/`DesignSurface.cs` already landed in Task 4).

  If `AnimateActiveAreaRectToMovesTheHoleOverSeveralFramesAndRewritesTheHoleFigure` fails on the `HoleUpdateCount` assertion (one update instead of several), the cause is `EnableDependentAnimation` not being honored for this target — do not weaken the assertion; the transition is the point.

- [ ] **Step 5: Commit**

  ```bash
  git add src/WinUIDesigner.Designer/Controls/GrayOutDesignerExceptActiveArea.cs \
          tests/WinUIDesigner.Designer.Tests/GrayOutDesignerExceptActiveAreaTests.cs
  git commit -m "feat: add GrayOutDesignerExceptActiveArea (Phase 4b Task 8)"
  ```

- [ ] **Step 6: Human eyeball check (spec Known gap #2 — not unit-testable)**

  Once Task 9/10 wire `Start`/`AnimateActiveAreaRectTo`/`Stop` into `CanvasPlacementSupport`/
  `GridPlacementSupport`, run the designer and drag an item. A human must confirm four things a
  test cannot: (a) the surface reads as *gray everywhere except* the active container, not the
  inverse; (b) there is no bright or dark seam at the hole's edge; (c) the hole slides smoothly
  to the new rectangle rather than snapping; (d) the gray-out sits behind the selection/resize
  adorners (`AdornerOrder.BehindForeground`) and not on top of them. Record the result in this
  plan (append a line under this step once checked) before treating gap #2 as closed.

- [ ] **Step 7 (alternate — only if Step 6 shows the geometry does not render correctly)**

  The spec's documented fallback: drop the hole-punched `PathGeometry` for four separate opaque
  rectangles covering the four strips around the active area. Replace the `_path`/`_geometry`/
  `_coverFigure`/`_holeFigure` members and `SetDesignSurfaceRect`/`SetFigureRect`/
  `UpdateHoleFigure` with four `Rectangle`s placed on a child `Canvas` (`Canvas.SetLeft`/`SetTop`
  plus `Width`/`Height` per rectangle, computed from the surface rect minus the hole rect) and
  leave everything else (the four DPs, both `Start` overloads, `AnimateActiveAreaRectTo`,
  `StopRectStoryboard`, `Stop`) untouched — the animation wiring is independent of how the shape
  is drawn, so the transition survives this swap. Re-run Steps 2 and 4 with the `FillRule`/
  `Figures.Count`/`LineSegment` assertions rewritten against the four rectangles' `Canvas.Left`/
  `Width` instead, and re-do Step 6.

---

### Task 9: CanvasPlacementSupport.cs

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/CanvasPlacementSupport.cs`
- Test: Create `tests/WinUIDesigner.Designer.Tests/CanvasPlacementSupportTests.cs`

**Interfaces:**
- Consumes: `SnaplinePlacementBehavior` (Task 6) as its base class.
  `GrayOutDesignerExceptActiveArea` (Task 8) — this task calls it exactly
  as `static void Start(ref GrayOutDesignerExceptActiveArea grayOut,
  ServiceContainer services, UIElement view, Rect activeArea)`,
  `static void Stop(ref GrayOutDesignerExceptActiveArea grayOut)`, and the
  instance method `void AnimateActiveAreaRectTo(Rect newActiveArea)` —
  Task 8 must produce exactly this shape (verified directly against the
  reference's own `CanvasPlacementSupport.cs` call sites; if Task 8 lands
  a different shape, update the call sites here to match rather than the
  other way around, since this exact shape is what the reference itself
  calls). `DesignItemProperty.GetAttachedProperty(Type, string)`
  (`DesignItemProperty.cs:89`), `DesignItemPropertyCollection`'s
  string indexer (`Properties["Margin"]`, `DesignItemProperty.cs:84`),
  `PlacementOperation.GetRealElementSize(UIElement)` (Phase 4a),
  `Services.Selection.PrimarySelection` (Phase 2/3),
  `Microsoft.UI.Xaml.Controls.Canvas.LeftProperty`/`TopProperty`/
  `RightProperty`/`BottomProperty` (real WinUI attached
  `DependencyProperty`s), `UIElement.ReadLocalValue`/
  `DependencyProperty.UnsetValue` (both confirmed present).
- Produces: nothing new later tasks depend on — `CanvasPlacementSupport`
  is a leaf `IPlacementBehavior` implementation, resolved automatically
  by `ExtensionForAttribute.OverrideExtension` whenever a placement
  operation's container is a live `Canvas`.

- [ ] **Step 1: Write the failing tests**

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
    public class CanvasPlacementSupportTests
    {
        const string CanvasXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Canvas x:Name=\"HostCanvas\" Width=\"200\" Height=\"200\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" Canvas.Left=\"10\" Canvas.Top=\"15\" />" +
            "</Canvas></Grid>";

        static (DesignSurface surface, DesignItem buttonItem) LoadSelected(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(CanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();

            var root = (Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component;
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
            var button = (Microsoft.UI.Xaml.Controls.Button)canvas.Children[0];
            var services = surface.DesignContext.Services;
            var buttonItem = services.Component.GetDesignItem(button);
            services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            return (surface, buttonItem);
        }

        [Test]
        public void ResizingAnItemInsideACanvasUpdatesCanvasLeftAndTopNotJustWidthAndHeight()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem) = LoadSelected(window);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Resize);
                var info = op.PlacedItems[0];
                info.Bounds = new Windows.Foundation.Rect(30, 40, 60, 25);
                op.CurrentContainerBehavior.SetPosition(info);
                op.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(30, buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>(), 0.5);
                Assert.AreEqual(40, buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>(), 0.5);
                Assert.AreEqual(60, ((Microsoft.UI.Xaml.FrameworkElement)buttonItem.Component).Width, 0.5);
                Assert.AreEqual(25, ((Microsoft.UI.Xaml.FrameworkElement)buttonItem.Component).Height, 0.5);
            });
        }

        [Test]
        public void GetPositionReadsCanvasLeftAndTopWhenTheyAreSet()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem) = LoadSelected(window);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Resize);
                var bounds = op.PlacedItems[0].OriginalBounds;
                op.Abort();

                Assert.AreEqual(10, bounds.X, 0.5, "GetPosition should read the already-set Canvas.Left, not compute from the live render transform");
                Assert.AreEqual(15, bounds.Y, 0.5);
            });
        }

        [Test]
        public void LeavingTheCanvasResetsCanvasLeftAndTop()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem) = LoadSelected(window);
                var canvasItem = buttonItem.Parent;

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                op.CurrentContainerBehavior.LeaveContainer(op);
                op.Abort();
                surface.UpdateLayout();

                var leftProp = buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left");
                var topProp = buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top");
                Assert.IsFalse(leftProp.IsSet, "Canvas.Left should be reset once the item leaves the Canvas");
                Assert.IsFalse(topProp.IsSet, "Canvas.Top should be reset once the item leaves the Canvas");
            });
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL — `CanvasPlacementSupport` doesn't exist, so the generic
  `DefaultPlacementBehavior` handles the Canvas instead: `GetPosition`
  returns the live rendered position (from `TransformToVisual`, not
  `Canvas.Left`/`Top`), so `GetPositionReadsCanvasLeftAndTopWhenTheyAreSet`
  fails its assertion (not a compile error this time, since every member
  this test calls already exists — this is intentional, verifying the
  *current* generic behavior is wrong before Step 3 fixes it).

- [ ] **Step 3: Implement CanvasPlacementSupport**

```csharp
using System;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// IPlacementBehavior for Canvas - positions via Canvas.Left/Top (or Right/Bottom,
    /// whichever the child already had set), instead of DefaultPlacementBehavior's
    /// Width/Height-only resize. Ported from the reference's CanvasPlacementSupport.cs.
    /// GetCanvasProperty/IsPropertySet operate on the live UIElement's real WinUI
    /// Canvas.LeftProperty/etc DependencyProperty directly (not the string-keyed
    /// DesignItemProperty convention, which only applies to the undo-tracked SetValue side) -
    /// Canvas.LeftProperty et al are real WinUI attached DependencyProperties, unchanged from
    /// WPF. The PasteItem branch in the reference's EnterContainer is dropped - no
    /// clipboard/paste feature exists anywhere in this port (spec's "Decisions already made").
    /// </summary>
    [ExtensionFor(typeof(Canvas), OverrideExtension = typeof(DefaultPlacementBehavior))]
    public class CanvasPlacementSupport : SnaplinePlacementBehavior
    {
        GrayOutDesignerExceptActiveArea grayOut;
        FrameworkElement extendedComponent;
        FrameworkElement extendedView;

        static double GetCanvasProperty(UIElement element, DependencyProperty d)
        {
            double v = (double)element.GetValue(d);
            return double.IsNaN(v) ? 0 : v;
        }

        static bool IsPropertySet(UIElement element, DependencyProperty d)
        {
            return element.ReadLocalValue(d) != DependencyProperty.UnsetValue;
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            extendedComponent = (FrameworkElement)ExtendedItem.Component;
            extendedView = (FrameworkElement)ExtendedItem.View;
        }

        public override Rect GetPosition(PlacementOperation operation, DesignItem item)
        {
            UIElement child = item.View;
            if (child == null) return default;

            double x, y;
            if (IsPropertySet(child, Canvas.LeftProperty) || !IsPropertySet(child, Canvas.RightProperty)) {
                x = GetCanvasProperty(child, Canvas.LeftProperty);
            } else {
                x = extendedComponent.ActualWidth - GetCanvasProperty(child, Canvas.RightProperty) - PlacementOperation.GetRealElementSize(child).Width;
            }

            if (IsPropertySet(child, Canvas.TopProperty) || !IsPropertySet(child, Canvas.BottomProperty)) {
                y = GetCanvasProperty(child, Canvas.TopProperty);
            } else {
                y = extendedComponent.ActualHeight - GetCanvasProperty(child, Canvas.BottomProperty) - PlacementOperation.GetRealElementSize(child).Height;
            }

            return new Rect(new Point(x, y), PlacementOperation.GetRealElementSize(item.View));
        }

        public override void SetPosition(PlacementInformation info)
        {
            base.SetPosition(info);
            info.Item.Properties["Margin"].Reset();

            UIElement child = info.Item.View;
            Rect newPosition = info.Bounds;

            if (IsPropertySet(child, Canvas.LeftProperty) || !IsPropertySet(child, Canvas.RightProperty)) {
                if (newPosition.X != GetCanvasProperty(child, Canvas.LeftProperty))
                    info.Item.Properties.GetAttachedProperty(typeof(Canvas), "Left").SetValue(newPosition.X);
            } else {
                var newR = extendedComponent.ActualWidth - (newPosition.X + newPosition.Width);
                if (newR != GetCanvasProperty(child, Canvas.RightProperty))
                    info.Item.Properties.GetAttachedProperty(typeof(Canvas), "Right").SetValue(newR);
            }

            if (IsPropertySet(child, Canvas.TopProperty) || !IsPropertySet(child, Canvas.BottomProperty)) {
                if (newPosition.Y != GetCanvasProperty(child, Canvas.TopProperty))
                    info.Item.Properties.GetAttachedProperty(typeof(Canvas), "Top").SetValue(newPosition.Y);
            } else {
                var newB = extendedComponent.ActualHeight - (newPosition.Y + newPosition.Height);
                if (newB != GetCanvasProperty(child, Canvas.BottomProperty))
                    info.Item.Properties.GetAttachedProperty(typeof(Canvas), "Bottom").SetValue(newB);
            }

            if (info.Item == Services.Selection.PrimarySelection) {
                var b = new Rect(0, 0, extendedView.ActualWidth, extendedView.ActualHeight);
                if (grayOut != null)
                    grayOut.AnimateActiveAreaRectTo(b);
                else
                    GrayOutDesignerExceptActiveArea.Start(ref grayOut, Services, ExtendedItem.View, b);
            }
        }

        public override void LeaveContainer(PlacementOperation operation)
        {
            GrayOutDesignerExceptActiveArea.Stop(ref grayOut);
            base.LeaveContainer(operation);
            foreach (var info in operation.PlacedItems) {
                info.Item.Properties.GetAttachedProperty(typeof(Canvas), "Left").Reset();
                info.Item.Properties.GetAttachedProperty(typeof(Canvas), "Top").Reset();
            }
        }

        public override void EnterContainer(PlacementOperation operation)
        {
            base.EnterContainer(operation);
            foreach (var info in operation.PlacedItems) {
                info.Item.Properties["HorizontalAlignment"].Reset();
                info.Item.Properties["VerticalAlignment"].Reset();
                info.Item.Properties["Margin"].Reset();
            }
        }

        public override void EndPlacement(PlacementOperation operation)
        {
            GrayOutDesignerExceptActiveArea.Stop(ref grayOut);
            base.EndPlacement(operation);
        }
    }
}
```

  > Note on `SetPosition`'s Right/Bottom math: the reference computes
  > `extendedComponent.ActualWidth - newPosition.Right` using WPF's
  > `Rect.Right` (`= X + Width`, a read-only computed member that exists
  > identically on `Windows.Foundation.Rect`) — this port writes it out as
  > `newPosition.X + newPosition.Width` explicitly since `Rect.Right` is
  > confirmed present on `Windows.Foundation.Rect` too (it's in the
  > "present" list, not the absent one — safe to use directly as
  > `newPosition.Right` instead if preferred; both are equivalent).

- [ ] **Step 4: Run tests to verify they pass**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj` then run the built `.exe` filtered with `--test=WinUIDesigner.Designer.Tests.CanvasPlacementSupportTests`.
  Expected: PASS, all 3 tests.

- [ ] **Step 5: Re-run Phase 1-4a suites, then commit**

  ```bash
  git add src/WinUIDesigner.Designer/Extensions/CanvasPlacementSupport.cs \
          tests/WinUIDesigner.Designer.Tests/CanvasPlacementSupportTests.cs
  git commit -m "feat: add CanvasPlacementSupport (Phase 4b Task 9)"
  ```

---

### Task 10: GridPlacementSupport.cs

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/GridPlacementSupport.cs`
- Test: Create `tests/WinUIDesigner.Designer.Tests/GridPlacementSupportTests.cs`

**Interfaces:**
- Consumes: `SnaplinePlacementBehavior` (Task 6) as its base class, and
  three Snapline-specific members beyond the `DefaultPlacementBehavior`
  surface: `protected virtual void AddContainerSnaplines(Rect containerRect,
  List<SnaplinePlacementBehavior.Snapline> horizontalMap,
  List<SnaplinePlacementBehavior.Snapline> verticalMap)`, `protected class
  Snapline` (public fields `double Offset, Start, End; bool
  RequireOverlap; int Group; double DrawOffset`), and `public static
  double SnaplineMargin`. **If Task 6 lands different names, fix the call
  sites in this task, not Task 6** — this is the exact shape the reference
  itself calls. If Task 6 drops `AddContainerSnaplines` entirely, delete
  the override below and record the loss (grid-boundary snaplines) as a
  scope cut rather than inventing a substitute.
  `GrayOutDesignerExceptActiveArea` (Task 8) — called exactly as Task 9's
  `CanvasPlacementSupport` calls it: `static void Start(ref
  GrayOutDesignerExceptActiveArea grayOut, ServiceContainer services,
  UIElement activeContainer, Rect activeRectInActiveContainer)`, `static
  void Stop(ref GrayOutDesignerExceptActiveArea grayOut)`, instance `void
  AnimateActiveAreaRectTo(Rect newRect)`.
  `DefaultPlacementBehavior` (Phase 4a) for `SetPosition`/`EnterContainer`/
  `LeaveContainer`/`EndPlacement` overrides and the `[ExtensionFor(...,
  OverrideExtension = typeof(DefaultPlacementBehavior))]` mechanism (Task
  9 already exercises the same mechanism for `Canvas`).
  `DesignItemPropertyCollection.GetAttachedProperty(Type, string)`
  (`DesignItemProperty.cs:89`) and the string indexer (`:84`).
- Produces: nothing new later tasks compile against — but Tasks 11-13's
  `GridAdornerProvider` row/column *delete* depends on this file's
  `LeaveContainer` type-dispatch branch (see the class doc comment below)
  to actually remove a `RowDefinition`/`ColumnDefinition` `DesignItem`
  from the Grid's definitions collection when that's the item being
  "left."

> **Correction to the spec's own account of this file** (found while
> reading the full reference for this task, not assumed from the spec's
> summary): the spec describes `LeaveContainer` as deleting "now-unneeded"
> row/column `DesignItem`s implying an emptiness check. The reference has
> no such check — `LeaveContainer`'s `RowDefinition`/`ColumnDefinition`
> branch fires when the *placed item itself is* a `RowDefinition`/
> `ColumnDefinition` `DesignItem` (i.e. this is how `GridAdornerProvider`'s
> row/column delete removes one, Tasks 11-13), not a cleanup pass over an
> ordinary child's vacated cell. The class doc comment and Step 3's code
> below reflect the real behavior; if updating the spec document
> separately, correct this there too.

- [ ] **Step 1: Write the failing tests**

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
    public class GridPlacementSupportTests
    {
        // A 100x100 Grid with deliberately UNEQUAL rows (30/70) and columns (40/60), so every
        // index assertion below distinguishes the running-sum-of-ActualHeight/ActualWidth
        // reconstruction of WPF's RowDefinition.Offset/ColumnDefinition.Offset from a naive
        // "equal shares" calculation. See SetUnequalDefinitionSizes for why the Height="30"/
        // Width="40" attributes below are not enough on their own (Global Constraints).
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"100\" Height=\"100\">" +
            "<Grid.RowDefinitions><RowDefinition Height=\"30\" /><RowDefinition Height=\"70\" /></Grid.RowDefinitions>" +
            "<Grid.ColumnDefinitions><ColumnDefinition Width=\"40\" /><ColumnDefinition Width=\"60\" /></Grid.ColumnDefinitions>" +
            "<Button x:Name=\"Target\" Width=\"20\" Height=\"10\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</Grid>";

        /// <summary>
        /// Re-applies the row/column sizes as typed GridLength values through the design model.
        /// Measured, not assumed (Global Constraints): the XAML Height="30"/Width="40" attributes
        /// round-trip through the model but never reach the live instance - every row/column lays
        /// out at its default star share until set this way.
        /// </summary>
        static void SetUnequalDefinitionSizes(DesignSurface surface)
        {
            var rootItem = surface.DesignContext.RootItem;
            var rows = rootItem.Properties["RowDefinitions"].CollectionElements;
            rows[0].Properties["Height"].SetValue(new Microsoft.UI.Xaml.GridLength(30));
            rows[1].Properties["Height"].SetValue(new Microsoft.UI.Xaml.GridLength(70));
            var cols = rootItem.Properties["ColumnDefinitions"].CollectionElements;
            cols[0].Properties["Width"].SetValue(new Microsoft.UI.Xaml.GridLength(40));
            cols[1].Properties["Width"].SetValue(new Microsoft.UI.Xaml.GridLength(60));
            surface.UpdateLayout();
        }

        static DesignSurface LoadSelected(Microsoft.UI.Xaml.Window window, out DesignItem buttonItem,
                                          out Microsoft.UI.Xaml.Controls.Button button)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(GridXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            SetUnequalDefinitionSizes(surface);

            var grid = (Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component;
            button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
            buttonItem = surface.DesignContext.Services.Component.GetDesignItem(button);
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            return surface;
        }

        static DesignItemProperty Attached(DesignItem item, string name)
        {
            return item.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Grid), name);
        }

        [Test]
        public void PlacingAnItemAcrossCellBoundariesSetsRowColumnAndBothSpans()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Resize);
                Assert.IsInstanceOf<GridPlacementSupport>(op.CurrentContainerBehavior,
                    "[ExtensionFor(typeof(Grid), OverrideExtension = typeof(DefaultPlacementBehavior))] " +
                    "must make GridPlacementSupport win over the generic DefaultPlacementBehavior");

                var info = op.PlacedItems[0];
                // Left=20, Top=10, Right=80, Bottom=90 - starts in column 0 / row 0 and ends in
                // column 1 / row 1, so both spans must become 2.
                info.Bounds = new Windows.Foundation.Rect(20, 10, 60, 80);
                op.CurrentContainerBehavior.SetPosition(info);
                op.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(0, Microsoft.UI.Xaml.Controls.Grid.GetRow(button), "Grid.Row");
                Assert.AreEqual(0, Microsoft.UI.Xaml.Controls.Grid.GetColumn(button), "Grid.Column");
                Assert.AreEqual(2, Microsoft.UI.Xaml.Controls.Grid.GetRowSpan(button), "Grid.RowSpan");
                Assert.AreEqual(2, Microsoft.UI.Xaml.Controls.Grid.GetColumnSpan(button), "Grid.ColumnSpan");

                // HorizontalAlignment/VerticalAlignment stay Left/Top (not an EnterContainer), so the
                // margin is measured from the top-left of the spanned area, i.e. offsets (0, 0).
                Assert.AreEqual(new Microsoft.UI.Xaml.Thickness(20, 10, 0, 0), button.Margin, "Margin");
                Assert.AreEqual(60, button.Width, 0.5);
                Assert.AreEqual(80, button.Height, 0.5);
            });
        }

        [Test]
        public void UnequalRowAndColumnSizesAreResolvedThroughTheActualSizeRunningSum()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var grid = (Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component;

                // Guard the fixture itself: if the GridLength values had not taken effect these
                // would be 50/50 and every index assertion below would silently test nothing.
                Assert.AreEqual(30, grid.RowDefinitions[0].ActualHeight, 0.5);
                Assert.AreEqual(70, grid.RowDefinitions[1].ActualHeight, 0.5);
                Assert.AreEqual(40, grid.ColumnDefinitions[0].ActualWidth, 0.5);
                Assert.AreEqual(60, grid.ColumnDefinitions[1].ActualWidth, 0.5);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Resize);
                var info = op.PlacedItems[0];
                // Left=45 > the 40px column boundary and Top=35 > the 30px row boundary, so this
                // lands wholly in row 1 / column 1. With equal 50/50 rows and columns the same
                // point would resolve to row 0 / column 0 - this is the assertion that fails if the
                // Offset replacement is anything but a running sum of the real ActualWidth/
                // ActualHeight values in definition order.
                info.Bounds = new Windows.Foundation.Rect(45, 35, 30, 30);
                op.CurrentContainerBehavior.SetPosition(info);
                op.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetRow(button), "Grid.Row");
                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetColumn(button), "Grid.Column");
                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetRowSpan(button), "Grid.RowSpan");
                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetColumnSpan(button), "Grid.ColumnSpan");
                // A span of 1 is written as a Reset, not as SetValue(1), so no RowSpan/ColumnSpan
                // attribute is left behind in the XAML.
                Assert.IsFalse(Attached(buttonItem, "RowSpan").IsSet, "a span of 1 must Reset, not SetValue(1)");
                Assert.IsFalse(Attached(buttonItem, "ColumnSpan").IsSet, "a span of 1 must Reset, not SetValue(1)");
                // Margin measured from column 1's start (40) and row 1's start (30).
                Assert.AreEqual(new Microsoft.UI.Xaml.Thickness(5, 5, 0, 0), button.Margin, "Margin");
            });
        }

        [Test]
        public void EnteringAGridCellSuggestsAlignmentAndResetsMarginWidthAndHeight()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var grid = (Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component;

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                var behavior = op.CurrentContainerBehavior;
                Assert.IsInstanceOf<GridPlacementSupport>(behavior);

                // Leave and re-enter the same Grid: that is what sets the enteredIntoNewContainer
                // latch, which is the only thing that makes SetPosition *suggest* an alignment
                // rather than preserve the item's existing one. (ChangeContainer, which drives this
                // pair in production - Task 1/14 - exercises the same two calls in the same order.)
                behavior.LeaveContainer(op);
                Assert.AreEqual(0, grid.Children.Count, "LeaveContainer must detach the child");
                behavior.EnterContainer(op);
                Assert.AreEqual(1, grid.Children.Count, "EnterContainer must re-attach the child");

                var info = op.PlacedItems[0];
                // Cell (row 1, column 1) spans x 40..100 and y 30..100. Left=45 is within the
                // left quarter (<55) and Right=90 is within the right quarter (>85), so the
                // horizontal suggestion is Stretch; likewise Top=35 (<47.5) and Bottom=90 (>82.5)
                // give a vertical Stretch. Stretch on both axes exercises all four margin sides
                // and both Width/Height resets in one placement.
                info.Bounds = new Windows.Foundation.Rect(45, 35, 45, 55);
                behavior.SetPosition(info);
                op.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetRow(button), "Grid.Row");
                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetColumn(button), "Grid.Column");
                Assert.AreEqual(Microsoft.UI.Xaml.HorizontalAlignment.Stretch, button.HorizontalAlignment);
                Assert.AreEqual(Microsoft.UI.Xaml.VerticalAlignment.Stretch, button.VerticalAlignment);
                // Left = 45-40, Top = 35-30, Right = 100-90, Bottom = 100-90.
                Assert.AreEqual(new Microsoft.UI.Xaml.Thickness(5, 5, 10, 10), button.Margin, "all four Margin sides");

                // Asserted on the model, not on button.Width (Global Constraints): Reset() clears
                // the model but leaves the live DependencyProperty untouched, so button.Width would
                // still read 20 here and prove nothing.
                Assert.IsFalse(buttonItem.Properties["Width"].IsSet, "Width must be reset when stretched horizontally");
                Assert.IsFalse(buttonItem.Properties["Height"].IsSet, "Height must be reset when stretched vertically");
            });
        }

        [Test]
        public void LeavingTheGridRemovesARowOrColumnDefinitionDesignItemFromItsCollection()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out _, out _);
                var rootItem = surface.DesignContext.RootItem;
                var grid = (Microsoft.UI.Xaml.Controls.Grid)rootItem.Component;

                var rowItem = rootItem.Properties["RowDefinitions"].CollectionElements[1];
                Assert.AreEqual(typeof(Microsoft.UI.Xaml.Controls.RowDefinition), rowItem.ComponentType);

                var rowOp = PlacementOperation.Start(new[] { rowItem }, PlacementType.Delete);
                Assert.IsInstanceOf<GridPlacementSupport>(rowOp.CurrentContainerBehavior,
                    "a RowDefinition DesignItem's container behavior is the Grid's own");
                rowOp.CurrentContainerBehavior.LeaveContainer(rowOp);
                rowOp.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(1, grid.RowDefinitions.Count, "the RowDefinition must be gone from the live Grid");
                Assert.AreEqual(1, rootItem.Properties["RowDefinitions"].CollectionElements.Count,
                    "the RowDefinition DesignItem must be gone from the model collection");

                var colItem = rootItem.Properties["ColumnDefinitions"].CollectionElements[1];
                Assert.AreEqual(typeof(Microsoft.UI.Xaml.Controls.ColumnDefinition), colItem.ComponentType);

                var colOp = PlacementOperation.Start(new[] { colItem }, PlacementType.Delete);
                colOp.CurrentContainerBehavior.LeaveContainer(colOp);
                colOp.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(1, grid.ColumnDefinitions.Count);
                Assert.AreEqual(1, rootItem.Properties["ColumnDefinitions"].CollectionElements.Count);
            });
        }

        [Test]
        public void LeavingTheGridResetsGridAttachedPropertiesAndRestoresStretchedSizes()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);

                Attached(buttonItem, "Row").SetValue(1);
                Attached(buttonItem, "ColumnSpan").SetValue(2);
                // Stretch horizontally only: LeaveContainer must write Width back from the bounds
                // and leave Height alone, since VerticalAlignment is still Top.
                buttonItem.Properties["HorizontalAlignment"].SetValue(Microsoft.UI.Xaml.HorizontalAlignment.Stretch);
                surface.UpdateLayout();

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                op.PlacedItems[0].Bounds = new Windows.Foundation.Rect(0, 0, 33, 22);
                op.CurrentContainerBehavior.LeaveContainer(op);
                op.Commit();
                surface.UpdateLayout();

                Assert.IsFalse(Attached(buttonItem, "Row").IsSet, "Grid.Row must be reset on leaving");
                Assert.IsFalse(Attached(buttonItem, "Column").IsSet, "Grid.Column must be reset on leaving");
                Assert.IsFalse(Attached(buttonItem, "RowSpan").IsSet, "Grid.RowSpan must be reset on leaving");
                Assert.IsFalse(Attached(buttonItem, "ColumnSpan").IsSet, "Grid.ColumnSpan must be reset on leaving");
                Assert.AreEqual(33, button.Width, 0.5, "a horizontally stretched item gets an explicit Width back");
                Assert.AreEqual(10, button.Height, 0.5, "VerticalAlignment is Top, so Height must be untouched");
            });
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL to compile — `GridPlacementSupport` doesn't exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// Provides IPlacementBehavior for Grid: writes Grid.Row/Column/RowSpan/ColumnSpan from a
    /// placement's bounds, derives HorizontalAlignment/VerticalAlignment/Margin/Width/Height for
    /// the target cell, and cleans up on the way out. Ported from the reference's
    /// Extensions/GridPlacementSupport.cs with three changes:
    ///
    /// 1. RowDefinition.Offset/ColumnDefinition.Offset do not exist in WinUI (spec, compiler-
    ///    confirmed). All six reference call sites go through GetRowOffset/GetColumnOffset below,
    ///    which reconstruct it as a running sum of ActualHeight/ActualWidth in definition order -
    ///    WPF's Offset is the distance from the Grid's edge to the *start* of that definition, so
    ///    the running sum stops one short of the requested index.
    /// 2. Attached properties are addressed by (Type, string), not DependencyProperty - this
    ///    port's established convention (DesignItemProperty.cs:89). Ordinary properties go
    ///    through the string indexer.
    /// 3. The reference's PlacementType.PasteItem branch in EnterContainer is dropped - no
    ///    clipboard/paste feature exists anywhere in this port.
    ///
    /// Note on LeaveContainer: its RowDefinition/ColumnDefinition branch is NOT an "is this row
    /// now empty" check (correcting this spec's own paraphrase, see this task's note in the
    /// plan). It fires when the *placed item itself is* a RowDefinition or ColumnDefinition
    /// DesignItem - how GridAdornerProvider (Tasks 11-13) deletes a row or column - and removes
    /// it from the Grid's definitions collection. base.LeaveContainer has already removed it via
    /// ParentProperty by then, so this second removal is a defensive no-op (confirmed: removing
    /// an absent element from this port's CollectionElements does not throw).
    /// </summary>
    [ExtensionFor(typeof(Grid), OverrideExtension = typeof(DefaultPlacementBehavior))]
    public sealed class GridPlacementSupport : SnaplinePlacementBehavior
    {
        Grid grid;
        bool enteredIntoNewContainer;
        GrayOutDesignerExceptActiveArea grayOut;

        const double epsilon = 0.00000001;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            grid = (Grid)ExtendedItem.Component;
        }

        /// <summary>Distance from the Grid's left edge to the start of column
        /// <paramref name="index"/>, reconstructed as a running sum of
        /// ColumnDefinition.ActualWidth - replaces the reference's
        /// grid.ColumnDefinitions[index].Offset (absent in WinUI).</summary>
        double GetColumnOffset(int index)
        {
            if (index <= 0) return 0;
            if (index >= grid.ColumnDefinitions.Count) return grid.ActualWidth;
            double offset = 0;
            for (int i = 0; i < index; i++) offset += grid.ColumnDefinitions[i].ActualWidth;
            return offset;
        }

        /// <summary>Row equivalent of GetColumnOffset, summing RowDefinition.ActualHeight.</summary>
        double GetRowOffset(int index)
        {
            if (index <= 0) return 0;
            if (index >= grid.RowDefinitions.Count) return grid.ActualHeight;
            double offset = 0;
            for (int i = 0; i < index; i++) offset += grid.RowDefinitions[i].ActualHeight;
            return offset;
        }

        int GetColumnIndex(double x)
        {
            if (grid.ColumnDefinitions.Count == 0) return 0;
            for (int i = 1; i < grid.ColumnDefinitions.Count; i++) {
                if (x < GetColumnOffset(i) - epsilon) return i - 1;
            }
            return grid.ColumnDefinitions.Count - 1;
        }

        int GetRowIndex(double y)
        {
            if (grid.RowDefinitions.Count == 0) return 0;
            for (int i = 1; i < grid.RowDefinitions.Count; i++) {
                if (y < GetRowOffset(i) - epsilon) return i - 1;
            }
            return grid.RowDefinitions.Count - 1;
        }

        int GetEndColumnIndex(double x)
        {
            if (grid.ColumnDefinitions.Count == 0) return 0;
            for (int i = 1; i < grid.ColumnDefinitions.Count; i++) {
                if (x <= GetColumnOffset(i) + epsilon) return i - 1;
            }
            return grid.ColumnDefinitions.Count - 1;
        }

        int GetEndRowIndex(double y)
        {
            if (grid.RowDefinitions.Count == 0) return 0;
            for (int i = 1; i < grid.RowDefinitions.Count; i++) {
                if (y <= GetRowOffset(i) + epsilon) return i - 1;
            }
            return grid.RowDefinitions.Count - 1;
        }

        /// <summary>Publishes a snapline at every row/column boundary so a dragged or resized
        /// child snaps to the grid lines themselves. The reference already computes these via a
        /// running sum of ActualHeight/ActualWidth (not Definition.Offset), so this override is
        /// an unmodified port once Task 6's Snapline/SnaplineMargin/AddContainerSnaplines shape
        /// is available (see this task's Interfaces block).</summary>
        protected override void AddContainerSnaplines(Rect containerRect,
                                                      List<SnaplinePlacementBehavior.Snapline> horizontalMap,
                                                      List<SnaplinePlacementBehavior.Snapline> verticalMap)
        {
            var view = (Grid)ExtendedItem.View;
            double offset = 0;
            foreach (RowDefinition r in view.RowDefinitions) {
                offset += r.ActualHeight;
                horizontalMap.Add(new Snapline { RequireOverlap = false, Offset = offset, Start = offset, End = containerRect.Right });
                if (SnaplineMargin > 0) {
                    horizontalMap.Add(new Snapline { RequireOverlap = false, Offset = offset - SnaplineMargin, Start = offset, End = containerRect.Right });
                    horizontalMap.Add(new Snapline { RequireOverlap = false, Offset = offset + SnaplineMargin, Start = offset, End = containerRect.Right });
                }
            }
            offset = 0;
            foreach (ColumnDefinition c in view.ColumnDefinitions) {
                offset += c.ActualWidth;
                verticalMap.Add(new Snapline { RequireOverlap = false, Offset = offset, Start = containerRect.Top, End = containerRect.Bottom });
                if (SnaplineMargin > 0) {
                    verticalMap.Add(new Snapline { RequireOverlap = false, Offset = offset - SnaplineMargin, Start = containerRect.Top, End = containerRect.Bottom });
                    verticalMap.Add(new Snapline { RequireOverlap = false, Offset = offset + SnaplineMargin, Start = containerRect.Top, End = containerRect.Bottom });
                }
            }
        }

        static void SetColumn(DesignItem item, int column, int columnSpan)
        {
            Debug.Assert(item != null && column >= 0 && columnSpan > 0);
            item.Properties.GetAttachedProperty(typeof(Grid), "Column").SetValue(column);
            if (columnSpan == 1) item.Properties.GetAttachedProperty(typeof(Grid), "ColumnSpan").Reset();
            else item.Properties.GetAttachedProperty(typeof(Grid), "ColumnSpan").SetValue(columnSpan);
        }

        static void SetRow(DesignItem item, int row, int rowSpan)
        {
            Debug.Assert(item != null && row >= 0 && rowSpan > 0);
            item.Properties.GetAttachedProperty(typeof(Grid), "Row").SetValue(row);
            if (rowSpan == 1) item.Properties.GetAttachedProperty(typeof(Grid), "RowSpan").Reset();
            else item.Properties.GetAttachedProperty(typeof(Grid), "RowSpan").SetValue(rowSpan);
        }

        static HorizontalAlignment SuggestHorizontalAlignment(Rect itemBounds, Rect availableSpaceRect)
        {
            bool isLeft = itemBounds.Left < availableSpaceRect.Left + availableSpaceRect.Width / 4;
            bool isRight = itemBounds.Right > availableSpaceRect.Right - availableSpaceRect.Width / 4;
            if (isLeft && isRight) return HorizontalAlignment.Stretch;
            if (isRight) return HorizontalAlignment.Right;
            return HorizontalAlignment.Left;
        }

        static VerticalAlignment SuggestVerticalAlignment(Rect itemBounds, Rect availableSpaceRect)
        {
            bool isTop = itemBounds.Top < availableSpaceRect.Top + availableSpaceRect.Height / 4;
            bool isBottom = itemBounds.Bottom > availableSpaceRect.Bottom - availableSpaceRect.Height / 4;
            if (isTop && isBottom) return VerticalAlignment.Stretch;
            if (isBottom) return VerticalAlignment.Bottom;
            return VerticalAlignment.Top;
        }

        public override void EnterContainer(PlacementOperation operation)
        {
            enteredIntoNewContainer = true;
            // Ensures the ActualWidth/ActualHeight values the offset reconstruction reads are
            // current for the Grid the item is arriving into.
            grid.UpdateLayout();
            base.EnterContainer(operation);
        }

        public override void EndPlacement(PlacementOperation operation)
        {
            GrayOutDesignerExceptActiveArea.Stop(ref grayOut);
            enteredIntoNewContainer = false;
            base.EndPlacement(operation);
        }

        public override void SetPosition(PlacementInformation info)
        {
            base.SetPosition(info);

            int leftColumnIndex = GetColumnIndex(info.Bounds.Left);
            int rightColumnIndex = GetEndColumnIndex(info.Bounds.Right);
            if (rightColumnIndex < leftColumnIndex) rightColumnIndex = leftColumnIndex;
            SetColumn(info.Item, leftColumnIndex, rightColumnIndex - leftColumnIndex + 1);

            int topRowIndex = GetRowIndex(info.Bounds.Top);
            int bottomRowIndex = GetEndRowIndex(info.Bounds.Bottom);
            if (bottomRowIndex < topRowIndex) bottomRowIndex = topRowIndex;
            SetRow(info.Item, topRowIndex, bottomRowIndex - topRowIndex + 1);

            var availableSpaceRect = new Rect(
                new Point(GetColumnOffset(leftColumnIndex), GetRowOffset(topRowIndex)),
                new Point(GetColumnOffset(rightColumnIndex + 1), GetRowOffset(bottomRowIndex + 1)));

            if (info.Item == Services.Selection.PrimarySelection) {
                if (grayOut != null) grayOut.AnimateActiveAreaRectTo(availableSpaceRect);
                else GrayOutDesignerExceptActiveArea.Start(ref grayOut, Services, ExtendedItem.View, availableSpaceRect);
            }

            var ha = info.Item.Properties["HorizontalAlignment"].GetConvertedValueOnInstance<HorizontalAlignment>();
            var va = info.Item.Properties["VerticalAlignment"].GetConvertedValueOnInstance<VerticalAlignment>();
            if (enteredIntoNewContainer) {
                ha = SuggestHorizontalAlignment(info.Bounds, availableSpaceRect);
                va = SuggestVerticalAlignment(info.Bounds, availableSpaceRect);
            }
            info.Item.Properties["HorizontalAlignment"].SetValue(ha);
            info.Item.Properties["VerticalAlignment"].SetValue(va);

            var margin = new Thickness(0, 0, 0, 0);
            if (ha == HorizontalAlignment.Left || ha == HorizontalAlignment.Stretch)
                margin.Left = info.Bounds.Left - GetColumnOffset(leftColumnIndex);
            if (va == VerticalAlignment.Top || va == VerticalAlignment.Stretch)
                margin.Top = info.Bounds.Top - GetRowOffset(topRowIndex);
            if (ha == HorizontalAlignment.Right || ha == HorizontalAlignment.Stretch)
                margin.Right = GetColumnOffset(rightColumnIndex + 1) - info.Bounds.Right;
            if (va == VerticalAlignment.Bottom || va == VerticalAlignment.Stretch)
                margin.Bottom = GetRowOffset(bottomRowIndex + 1) - info.Bounds.Bottom;
            info.Item.Properties["Margin"].SetValue(margin);

            if (ha == HorizontalAlignment.Stretch) info.Item.Properties["Width"].Reset();
            if (va == VerticalAlignment.Stretch) info.Item.Properties["Height"].Reset();
        }

        public override void LeaveContainer(PlacementOperation operation)
        {
            GrayOutDesignerExceptActiveArea.Stop(ref grayOut);
            base.LeaveContainer(operation);
            foreach (var info in operation.PlacedItems) {
                if (info.Item.ComponentType == typeof(ColumnDefinition)) {
                    ExtendedItem.Properties["ColumnDefinitions"].CollectionElements.Remove(info.Item);
                } else if (info.Item.ComponentType == typeof(RowDefinition)) {
                    ExtendedItem.Properties["RowDefinitions"].CollectionElements.Remove(info.Item);
                } else {
                    info.Item.Properties.GetAttachedProperty(typeof(Grid), "Row").Reset();
                    info.Item.Properties.GetAttachedProperty(typeof(Grid), "Column").Reset();
                    info.Item.Properties.GetAttachedProperty(typeof(Grid), "RowSpan").Reset();
                    info.Item.Properties.GetAttachedProperty(typeof(Grid), "ColumnSpan").Reset();

                    var ha = info.Item.Properties["HorizontalAlignment"].GetConvertedValueOnInstance<HorizontalAlignment>();
                    var va = info.Item.Properties["VerticalAlignment"].GetConvertedValueOnInstance<VerticalAlignment>();
                    if (ha == HorizontalAlignment.Stretch) info.Item.Properties["Width"].SetValue(info.Bounds.Width);
                    if (va == VerticalAlignment.Stretch) info.Item.Properties["Height"].SetValue(info.Bounds.Height);
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj` then run the built `.exe` filtered with `--test=WinUIDesigner.Designer.Tests.GridPlacementSupportTests`.
  Expected: PASS, all 5 tests.

  `GridPlacementSupport` overrides `DefaultPlacementBehavior` for every
  `Grid`, and several Phase 3/4a fixtures (`ResizeThumbExtensionTests`,
  `RotateThumbExtensionTests`, `SkewThumbExtensionTests`,
  `Phase4aDefinitionOfDoneTests`) use a `Grid` root, so they now run
  through this file too — re-run every prior `.exe` unfiltered and confirm
  no regression.

- [ ] **Step 5: Commit**

  ```bash
  git add src/WinUIDesigner.Designer/Extensions/GridPlacementSupport.cs \
          tests/WinUIDesigner.Designer.Tests/GridPlacementSupportTests.cs
  git commit -m "feat: add GridPlacementSupport (Phase 4b Task 10)"
  ```

---

### Task 11: Grid rails + click-to-create a row/column

**Files:**
- Create: `src/WinUIDesigner.Designer/Controls/GridAdorner.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/GridAdornerProvider.cs`
- Modify: `src/WinUIDesigner.XamlDom/XamlPropertyInfo.cs`
- Test: Create `tests/WinUIDesigner.Designer.Tests/GridAdornerProviderTests.cs`

**Interfaces:**
- Consumes: `AdornerProvider` (`OnInitialized`/`OnRemove`/`Adorners`/`ExtendedItem`/`Services`), `AdornerPanel.SetPlacement(UIElement, AdornerPlacement)`, `RelativePlacement(HorizontalAlignment, VerticalAlignment)` + `XOffset`/`YOffset`, `DesignItem.OpenGroup(string)`, `DesignItem.Properties[string]`, `DesignItemPropertyCollection.GetAttachedProperty(Type, string)`, `DesignItemProperty.CollectionElements` (`IObservableList<DesignItem>`: `Insert`/`Add`/`Count`/indexer), `DesignItemProperty.GetConvertedValueOnInstance<T>()`, `DesignItemProperty.SetValue(object)`, `IComponentService.RegisterComponentForDesigner(object)`/`GetDesignItem(object)`, `[ExtensionFor]`/`[ExtensionServer]`. From Task 3: `WinUIDesigner.DesignModel.Extensions.LogicalOrExtensionServer<A,B>` and `PrimarySelectionParentExtensionServer`.
- Produces:
  - `public class WinUIDesigner.Designer.Controls.GridRailAdorner : Microsoft.UI.Xaml.Controls.Grid` — `GridRailAdorner(DesignItem gridItem, AdornerPanel adornerPanel, Orientation orientation)`, `public const double RailSize = 10, RailDistance = 6, SplitterWidth = 10`, `public void RefreshLabels()`, `internal IEnumerable<string> LabelTexts { get; }` (test seam replacing the reference's unreadable `OnRender` output), `internal void SimulateRailPress(double insertionPosition)` (test seam — no public `PointerRoutedEventArgs` ctor), `internal static double GetOffset(Grid grid, Orientation orientation, int index)` (the `RowDefinition.Offset` replacement, consumed by Task 12's placements), `internal static string GridLengthToText(GridLength)`, `internal static void SplitLength(GridLength, double, double, out GridLength, out GridLength)`.
  - `public sealed class WinUIDesigner.Designer.Extensions.GridAdornerProvider : AdornerProvider` — `internal GridRailAdorner LeftRail { get; }`, `internal GridRailAdorner TopRail { get; }`, `internal void FlushPendingRebuild()` (test seam for the coalesced async rebuild, resolves spec Known gap #3).

> **This task also fixes a real undo bug found while building it** (unrelated to Grid specifically — it's in `WinUIDesigner.XamlDom`): `XamlNormalPropertyInfo.ResetValue` calls `PropertyDescriptor.ResetValue`, which is a no-op for WinUI's DP-backed CLR wrappers. `PropertyChangeAction.Undo` routes every `oldIsSet == false && DependencyProperty != null` case there — so undoing a *newly set* property reverted the XAML but left the live instance holding the new value. Measured: after a splitter drag on two `1*` rows (Task 12), undo popped the transaction but left the live grid at `1.333*/0.667*`. **Both this task's and Task 12's "single undo reverts it" DoD assertions depend on this fix.**

- [ ] **Step 1: Write the failing test(s)**

```csharp
// tests/WinUIDesigner.Designer.Tests/GridAdornerProviderTests.cs
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class GridAdornerProviderTests
    {
        // Row/column sizes are deliberately left unset (each definition defaults to 1*).
        // GridLength has NO from-string TypeConverter in this port (Global Constraints), so
        // Height="40" in XAML never reaches the live RowDefinition - a fixture must never rely
        // on it. 200x120 with two rows/two columns gives an exact 60/60 and 100/100 split.
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"120\">" +
            "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
            "<Grid.ColumnDefinitions><ColumnDefinition /><ColumnDefinition /></Grid.ColumnDefinitions>" +
            "<Button x:Name=\"Target\" Grid.Row=\"1\" Grid.Column=\"1\" Content=\"x\" />" +
            "</Grid>";

        const string SpanXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"120\">" +
            "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
            "<Button x:Name=\"Target\" Grid.Row=\"0\" Grid.RowSpan=\"2\" Content=\"x\" />" +
            "</Grid>";

        internal static DesignSurface Load(Window window, string xaml,
                                           out DesignItem gridItem, out Grid grid, out GridAdornerProvider provider)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();

            gridItem = surface.DesignContext.RootItem;
            grid = (Grid)gridItem.Component;
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { gridItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            provider = gridItem.Extensions.OfType<GridAdornerProvider>().Single();
            // The rebuild is coalesced onto the dispatcher; a test body is one dispatcher turn, so
            // the enqueued callback can never run inside it. Flush it explicitly instead.
            provider.FlushPendingRebuild();
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void SelectingAGridShowsTwoRails()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out _, out var provider);
                var rails = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<GridRailAdorner>().ToList();
                Assert.AreEqual(2, rails.Count);
                Assert.AreEqual(120, provider.LeftRail.ActualHeight, 0.5);
                Assert.AreEqual(10, provider.LeftRail.ActualWidth, 0.5);
                Assert.AreEqual(200, provider.TopRail.ActualWidth, 0.5);
                Assert.AreEqual(10, provider.TopRail.ActualHeight, 0.5);
            });
        }

        [Test]
        public void RailsShowOneSizeUnitCaptionPerRowAndColumn()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out _, out var provider);
                CollectionAssert.AreEqual(new[] { "*", "*" }, provider.LeftRail.LabelTexts.ToList());
                CollectionAssert.AreEqual(new[] { "*", "*" }, provider.TopRail.LabelTexts.ToList());

                provider.LeftRail.SimulateRailPress(30);
                surface.UpdateLayout();
                CollectionAssert.AreEqual(new[] { "0.5*", "0.5*", "*" }, provider.LeftRail.LabelTexts.ToList());
            });
        }

        [Test]
        public void RailClickCreatesRowAndShiftsLaterSiblings()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                var button = (Button)grid.Children[0];
                Assert.AreEqual(60, grid.RowDefinitions[0].ActualHeight, 0.5, "precondition");

                provider.LeftRail.SimulateRailPress(30);
                surface.UpdateLayout();

                Assert.AreEqual(3, grid.RowDefinitions.Count);
                Assert.AreEqual(30, grid.RowDefinitions[0].ActualHeight, 0.5);
                Assert.AreEqual(30, grid.RowDefinitions[1].ActualHeight, 0.5);
                Assert.AreEqual(2, Grid.GetRow(button), "a later sibling shifts down one row");
                Assert.AreEqual(1, undo.UndoActions.Count(), "creation + every sibling rewrite is ONE undo entry");

                undo.Undo();
                surface.UpdateLayout();
                Assert.AreEqual(2, grid.RowDefinitions.Count);
                Assert.AreEqual(1, Grid.GetRow(button));
                Assert.AreEqual(60, grid.RowDefinitions[0].ActualHeight, 0.5,
                    "undo must restore the split row's live size, not just the XAML");
            });
        }

        [Test]
        public void SplittingAStraddlingChildGrowsItsRowSpan()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, SpanXaml, out _, out var grid, out var provider);
                var button = (Button)grid.Children[0];
                provider.LeftRail.SimulateRailPress(30);
                surface.UpdateLayout();
                Assert.AreEqual(3, grid.RowDefinitions.Count);
                Assert.AreEqual(0, Grid.GetRow(button));
                Assert.AreEqual(3, Grid.GetRowSpan(button));
            });
        }

        [Test]
        public void ColumnRailClickCreatesColumn()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                provider.TopRail.SimulateRailPress(50);
                surface.UpdateLayout();
                Assert.AreEqual(3, grid.ColumnDefinitions.Count);
                Assert.AreEqual(2, Grid.GetColumn((Button)grid.Children[0]));
            });
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL — `CS0246: The type or namespace name 'GridAdornerProvider' could not be found` and `... 'GridRailAdorner' could not be found`.

- [ ] **Step 3: Write the implementation**

  **3a.** Fix the undo gap in `src/WinUIDesigner.XamlDom/XamlPropertyInfo.cs` — replace `XamlNormalPropertyInfo.ResetValue` (the file uses tabs):

```csharp
		public override void ResetValue(object instance)
		{
			// PropertyDescriptor.ResetValue is a no-op for WinUI's DP-backed CLR wrappers (there is
			// no DefaultValueAttribute and no ResetXxx method on them), so undoing a *newly set*
			// property used to leave the live instance holding the new value while the XAML was
			// correctly reverted. WPF never hit this because DependencyPropertyDescriptor.ResetValue
			// cleared the DP. Clear the DependencyProperty directly whenever we know which one backs
			// this property (XamlNormalPropertyInfo already resolves it via WinUIStaticMemberLocator).
			var depObj = instance as DependencyObject;
			if (dependencyProperty != null && depObj != null) {
				depObj.ClearValue(dependencyProperty);
				return;
			}
			try {
				_propertyDescriptor.ResetValue(instance);
			} catch (Exception) {
				//For Example "UndoRedoSimpleBinding" will raise a exception here => look if it has Side Effects if we generally catch here?
			}
		}
```

  **3b.** `src/WinUIDesigner.Designer/Controls/GridAdorner.cs` (new):

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;

namespace WinUIDesigner.Designer.Controls
{
    /// <summary>
    /// The blue bar drawn beside a selected Grid's rows/columns; clicking it splits the row or
    /// column under the pointer. Ported from the reference's GridRailAdorner with one structural
    /// change: WinUI has no OnRender/DrawingContext/FormattedText, so the per-row/column size
    /// captions the reference painted immediately are ordinary TextBlocks in a non-hit-testable
    /// child Canvas, repositioned by RefreshLabels(). RefreshLabels must never be called from a
    /// layout override - mutating Canvas.Children during ArrangeOverride throws a reentrancy
    /// COMException out of UpdateLayout; it is called at the same explicit points the reference
    /// called InvalidateVisual(). The reference's row.Offset / column.Offset (absent from
    /// WinUI) becomes the GetOffset running sum. Base is Grid, not FrameworkElement: an
    /// unstyled Grid gives Background/BorderBrush and IsTabStop for Escape-reachable focus, the
    /// same choice Controls/Thumbs/DesignerThumb made.
    /// </summary>
    public class GridRailAdorner : Grid
    {
        readonly DesignItem gridItem;
        readonly Grid grid;
        readonly AdornerPanel adornerPanel;
        readonly GridSplitterAdorner previewAdorner;
        readonly GridUnitSelector unitSelector;
        readonly Orientation orientation;
        readonly Canvas labelCanvas = new Canvas { IsHitTestVisible = false };
        bool displayUnitSelector;

        public const double RailSize = 10;
        public const double RailDistance = 6;
        public const double SplitterWidth = 10;

        public GridRailAdorner(DesignItem gridItem, AdornerPanel adornerPanel, Orientation orientation)
        {
            if (gridItem == null) throw new ArgumentNullException(nameof(gridItem));
            if (adornerPanel == null) throw new ArgumentNullException(nameof(adornerPanel));

            this.gridItem = gridItem;
            this.grid = (Grid)gridItem.Component;
            this.adornerPanel = adornerPanel;
            this.orientation = orientation;

            // Same ARGB the reference's frozen bgBrush used. Brushes.X doesn't exist in WinUI
            // and neither does SolidColorBrush.Freeze.
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x35, 0x1E, 0x90, 0xFF));
            IsTabStop = true;
            Children.Add(labelCanvas);
            // UIElement.Cursor isn't public in WinUI; ProtectedCursor is, and this class is the
            // subclass - same InputSystemCursorShape replacement for CursorProperty.OverrideMetadata
            // that Phase 4a established.
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);

            this.unitSelector = new GridUnitSelector(this);
            adornerPanel.Children.Add(unitSelector);

            if (orientation == Orientation.Horizontal) {
                this.Height = RailSize;
                previewAdorner = new GridColumnSplitterAdorner(this, gridItem, null, null);
            } else {
                this.Width = RailSize;
                previewAdorner = new GridRowSplitterAdorner(this, gridItem, null, null);
            }
            previewAdorner.IsPreview = true;
            previewAdorner.IsHitTestVisible = false;
            unitSelector.Orientation = orientation;

            // WinUI has no tunneling Preview* events, so handledEventsToo: true - the same idiom
            // Controls/DragListener.cs uses.
            AddHandler(UIElement.PointerEnteredEvent, (PointerEventHandler)OnRailPointerEntered, true);
            AddHandler(UIElement.PointerMovedEvent, (PointerEventHandler)OnRailPointerMoved, true);
            AddHandler(UIElement.PointerExitedEvent, (PointerEventHandler)OnRailPointerExited, true);
            AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)OnRailPointerPressed, true);
        }

        /// <summary>Test seam: the size-unit captions this rail currently shows, in row/column
        /// order. Replaces the reference's OnRender/FormattedText output, which had no readable
        /// equivalent at all.</summary>
        internal IEnumerable<string> LabelTexts {
            get { return labelCanvas.Children.OfType<TextBlock>().Select(t => t.Text); }
        }

        internal GridSplitterAdorner PreviewAdorner { get { return previewAdorner; } }
        internal GridUnitSelector UnitSelector { get { return unitSelector; } }

        /// <summary>Replacement for RowDefinition.Offset / ColumnDefinition.Offset, neither of
        /// which exists in WinUI: the running sum of ActualHeight / ActualWidth over the
        /// definitions before <paramref name="index"/>.</summary>
        internal static double GetOffset(Grid grid, Orientation orientation, int index)
        {
            double offset = 0;
            if (orientation == Orientation.Vertical) {
                for (int i = 0; i < index && i < grid.RowDefinitions.Count; i++) offset += grid.RowDefinitions[i].ActualHeight;
            } else {
                for (int i = 0; i < index && i < grid.ColumnDefinitions.Count; i++) offset += grid.ColumnDefinitions[i].ActualWidth;
            }
            return offset;
        }

        /// <summary>The reference's FirstOrDefault(r => pos >= r.Offset && pos <= r.Offset + r.Actual*),
        /// reconstructed against the running sum. Returns -1 when the position is past the last
        /// row/column.</summary>
        int IndexAt(double position)
        {
            double offset = 0;
            if (orientation == Orientation.Vertical) {
                for (int i = 0; i < grid.RowDefinitions.Count; i++) {
                    double h = grid.RowDefinitions[i].ActualHeight;
                    if (position >= offset && position <= offset + h) return i;
                    offset += h;
                }
            } else {
                for (int i = 0; i < grid.ColumnDefinitions.Count; i++) {
                    double w = grid.ColumnDefinitions[i].ActualWidth;
                    if (position >= offset && position <= offset + w) return i;
                    offset += w;
                }
            }
            return -1;
        }

        public void RefreshLabels()
        {
            labelCanvas.Children.Clear();
            var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
            if (orientation == Orientation.Horizontal) {
                double offset = 0;
                foreach (var column in grid.ColumnDefinitions) {
                    double w = column.ActualWidth;
                    if (w >= 0) {
                        var label = CreateLabel(GridLengthToText(column.Width));
                        label.Measure(infinite);
                        Canvas.SetLeft(label, offset + w / 2 - label.DesiredSize.Width / 2);
                        Canvas.SetTop(label, RailSize / 2 - label.DesiredSize.Height / 2);
                        labelCanvas.Children.Add(label);
                    }
                    offset += w;
                }
            } else {
                double offset = 0;
                foreach (var row in grid.RowDefinitions) {
                    double h = row.ActualHeight;
                    if (h >= 0) {
                        var label = CreateLabel(GridLengthToText(row.Height));
                        label.Measure(infinite);
                        // The reference rotated the whole drawing context by -90 before drawing the
                        // row captions; rotating each label about its own centre puts the same text
                        // at the same place without an immediate-mode transform stack.
                        label.RenderTransformOrigin = new Point(0.5, 0.5);
                        label.RenderTransform = new RotateTransform { Angle = -90 };
                        Canvas.SetLeft(label, RailSize / 2 - label.DesiredSize.Width / 2);
                        Canvas.SetTop(label, offset + h / 2 - label.DesiredSize.Height / 2);
                        labelCanvas.Children.Add(label);
                    }
                    offset += h;
                }
            }
        }

        static TextBlock CreateLabel(string text)
        {
            return new TextBlock { Text = text, FontSize = 10, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black), IsHitTestVisible = false };
        }

        double GetCoordinate(Point point)
        {
            return orientation == Orientation.Vertical ? point.Y : point.X;
        }

        #region hover / preview
        void OnRailPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            HoverAt(GetCoordinate(e.GetCurrentPoint(this).Position));
            if (!adornerPanel.Children.Contains(previewAdorner))
                adornerPanel.Children.Add(previewAdorner);
        }

        void OnRailPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            HoverAt(GetCoordinate(e.GetCurrentPoint(this).Position));
        }

        void OnRailPointerExited(object sender, PointerRoutedEventArgs e)
        {
            unitSelector.Visibility = Visibility.Collapsed;
            displayUnitSelector = false;
            if (adornerPanel.Children.Contains(previewAdorner))
                adornerPanel.Children.Remove(previewAdorner);
        }

        /// <summary>Test seam - see SimulateRailPress.</summary>
        internal void SimulateHover(double insertionPosition)
        {
            if (!adornerPanel.Children.Contains(previewAdorner))
                adornerPanel.Children.Add(previewAdorner);
            HoverAt(insertionPosition);
        }

        /// <summary>Parks the ghost splitter where a click would split and positions
        /// GridUnitSelector over the hovered row/column, using the same RelativePlacement
        /// geometry the reference computed in OnMouseMove.</summary>
        void HoverAt(double insertionPosition)
        {
            var rp = new RelativePlacement();
            var rpUnitSelector = new RelativePlacement();
            int index = IndexAt(insertionPosition);

            if (orientation == Orientation.Vertical) {
                rp.XOffset = -(RailSize + RailDistance);
                rp.WidthOffset = RailSize + RailDistance;
                rp.WidthRelativeToContentWidth = 1;
                rp.HeightOffset = SplitterWidth;
                rp.YOffset = insertionPosition - SplitterWidth / 2;

                if (index >= 0) {
                    var row = grid.RowDefinitions[index];
                    DesignItem component = gridItem.Services.Component.GetDesignItem(row);
                    rpUnitSelector.XOffset = -(RailSize + RailDistance) * 2.75 - 6;
                    rpUnitSelector.WidthOffset = RailSize + RailDistance;
                    rpUnitSelector.WidthRelativeToContentWidth = 1;
                    rpUnitSelector.HeightOffset = 55;
                    rpUnitSelector.YOffset = GetOffset(grid, orientation, index) + row.ActualHeight / 2 - 25;
                    unitSelector.SelectedItem = component;
                    if (component != null)
                        unitSelector.Unit = component.Properties["Height"].GetConvertedValueOnInstance<GridLength>().GridUnitType;
                    displayUnitSelector = true;
                } else {
                    displayUnitSelector = false;
                }
            } else {
                rp.YOffset = -(RailSize + RailDistance);
                rp.HeightOffset = RailSize + RailDistance;
                rp.HeightRelativeToContentHeight = 1;
                rp.WidthOffset = SplitterWidth;
                rp.XOffset = insertionPosition - SplitterWidth / 2;

                if (index >= 0) {
                    var column = grid.ColumnDefinitions[index];
                    DesignItem component = gridItem.Services.Component.GetDesignItem(column);
                    rpUnitSelector.YOffset = -(RailSize + RailDistance) * 2.20 - 6;
                    rpUnitSelector.HeightOffset = RailSize + RailDistance;
                    rpUnitSelector.HeightRelativeToContentHeight = 1;
                    rpUnitSelector.WidthOffset = 75;
                    rpUnitSelector.XOffset = GetOffset(grid, orientation, index) + column.ActualWidth / 2 - 35;
                    unitSelector.SelectedItem = component;
                    if (component != null)
                        unitSelector.Unit = component.Properties["Width"].GetConvertedValueOnInstance<GridLength>().GridUnitType;
                    displayUnitSelector = true;
                } else {
                    displayUnitSelector = false;
                }
            }

            AdornerPanel.SetPlacement(previewAdorner, rp);
            unitSelector.Visibility = displayUnitSelector ? Visibility.Visible : Visibility.Collapsed;
            if (displayUnitSelector)
                AdornerPanel.SetPlacement(unitSelector, rpUnitSelector);
        }
        #endregion

        #region create a new row/column
        void OnRailPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Only a primary/left press splits - a right-click must not, same guard DragListener
            // grew in 4a's final review.
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Focus(FocusState.Pointer);
            // The rail is arranged flush with the Grid along its long axis (RelativePlacement
            // Left/Stretch and Stretch/Top), so a rail-local coordinate IS the grid-local one -
            // exactly what the reference's e.GetPosition(this) produced.
            SplitAt(GetCoordinate(e.GetCurrentPoint(this).Position));
        }

        /// <summary>Test seam: drives the same split the pointer handler does.
        /// PointerRoutedEventArgs has no public constructor, so tests cannot synthesize a
        /// press - same reason DragListener exposes SimulateDown/Move/Up.</summary>
        internal void SimulateRailPress(double insertionPosition)
        {
            SplitAt(insertionPosition);
        }

        void SplitAt(double insertionPosition)
        {
            if (adornerPanel.Children.Contains(previewAdorner))
                adornerPanel.Children.Remove(previewAdorner);

            string collectionName = orientation == Orientation.Vertical ? "RowDefinitions" : "ColumnDefinitions";
            string sizeName = orientation == Orientation.Vertical ? "Height" : "Width";
            string indexName = orientation == Orientation.Vertical ? "Row" : "Column";
            string spanName = orientation == Orientation.Vertical ? "RowSpan" : "ColumnSpan";
            string title = orientation == Orientation.Vertical ? "Split grid row" : "Split grid column";

            DesignItemProperty collection = gridItem.Properties[collectionName];
            DesignItem current = null;

            // ONE ChangeGroup for the definition insert, both size writes and every sibling
            // rewrite - a single undo entry reverts the whole split.
            using (ChangeGroup changeGroup = gridItem.OpenGroup(title)) {
                if (collection.CollectionElements.Count == 0) {
                    DesignItem first = gridItem.Services.Component.RegisterComponentForDesigner(
                        orientation == Orientation.Vertical ? (object)new RowDefinition() : new ColumnDefinition());
                    collection.CollectionElements.Add(first);
                    grid.UpdateLayout(); // let WinUI assign the new definition's ActualHeight/Width
                    current = first;
                } else {
                    int hit = IndexAt(insertionPosition);
                    if (hit >= 0) current = collection.CollectionElements[hit];
                }

                if (current == null)
                    current = collection.CollectionElements[collection.CollectionElements.Count - 1];
                unitSelector.SelectedItem = current;

                int count = orientation == Orientation.Vertical ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count;
                double offset = 0;
                for (int i = 0; i < count; i++) {
                    double actual = orientation == Orientation.Vertical
                        ? grid.RowDefinitions[i].ActualHeight
                        : grid.ColumnDefinitions[i].ActualWidth;
                    if (offset > insertionPosition || offset + actual < insertionPosition) {
                        offset += actual;
                        continue;
                    }

                    GridLength oldLength = collection.CollectionElements[i].Properties[sizeName].GetConvertedValueOnInstance<GridLength>();
                    GridLength newLength1, newLength2;
                    SplitLength(oldLength, insertionPosition - offset, actual, out newLength1, out newLength2);

                    DesignItem newDefinition = gridItem.Services.Component.RegisterComponentForDesigner(
                        orientation == Orientation.Vertical ? (object)new RowDefinition() : new ColumnDefinition());
                    collection.CollectionElements.Insert(i + 1, newDefinition);
                    collection.CollectionElements[i].Properties[sizeName].SetValue(newLength1);
                    newDefinition.Properties[sizeName].SetValue(newLength2);
                    // Layout must settle before FixIndicesAfterSplit reads child positions - there
                    // is no DispatcherPriority.Loaded in WinUI, so force it (spec Known gap #3).
                    grid.UpdateLayout();
                    FixIndicesAfterSplit(i, indexName, spanName, insertionPosition);
                    grid.UpdateLayout();
                    changeGroup.Commit();
                    break;
                }
                // No matching row/column: the group is never committed, so leaving the using block
                // disposes and aborts it - the reference's behaviour, kept.
            }
            RefreshLabels();
        }

        /// <summary>The reference's FixIndicesAfterSplit: hold every child's visual position stable
        /// across the split. WinUI substitutions - View.TranslatePoint becomes
        /// TransformToVisual(...).TransformPoint(...), and every DependencyProperty-indexed
        /// Properties[...]/GetAttachedProperty(...) becomes this port's string-keyed form.</summary>
        void FixIndicesAfterSplit(int splitIndex, string indexName, string spanName, double insertionPosition)
        {
            foreach (DesignItem child in gridItem.Properties["Children"].CollectionElements) {
                if (child.View == null) continue;
                Point topLeft = child.View.TransformToVisual(grid).TransformPoint(new Point(0, 0));
                var margin = child.Properties["Margin"].GetConvertedValueOnInstance<Thickness>();
                var start = child.Properties.GetAttachedProperty(typeof(Grid), indexName).GetConvertedValueOnInstance<int>();
                var span = child.Properties.GetAttachedProperty(typeof(Grid), spanName).GetConvertedValueOnInstance<int>();

                if (start <= splitIndex && splitIndex < start + span) {
                    if (orientation == Orientation.Horizontal) {
                        var width = child.Properties["ActualWidth"].GetConvertedValueOnInstance<double>();
                        if (insertionPosition >= topLeft.X + width) continue;
                        if (insertionPosition > topLeft.X) {
                            child.Properties.GetAttachedProperty(typeof(Grid), spanName).SetValue(span + 1);
                        } else {
                            child.Properties.GetAttachedProperty(typeof(Grid), indexName).SetValue(start + 1);
                            margin.Left = topLeft.X - insertionPosition;
                            child.Properties["Margin"].SetValue(margin);
                        }
                    } else {
                        var height = child.Properties["ActualHeight"].GetConvertedValueOnInstance<double>();
                        if (insertionPosition >= topLeft.Y + height) continue;
                        if (insertionPosition > topLeft.Y) {
                            child.Properties.GetAttachedProperty(typeof(Grid), spanName).SetValue(span + 1);
                        } else {
                            child.Properties.GetAttachedProperty(typeof(Grid), indexName).SetValue(start + 1);
                            margin.Top = topLeft.Y - insertionPosition;
                            child.Properties["Margin"].SetValue(margin);
                        }
                    }
                } else if (start > splitIndex) {
                    child.Properties.GetAttachedProperty(typeof(Grid), indexName).SetValue(start + 1);
                }
            }
        }

        internal static void SplitLength(GridLength oldLength, double insertionPosition, double oldActualValue,
                                         out GridLength newLength1, out GridLength newLength2)
        {
            if (oldLength.IsAuto)
                oldLength = new GridLength(oldActualValue);
            // Guard the reference doesn't have: a zero-sized row/column would divide by zero here.
            double percentage = oldActualValue == 0 ? 0 : insertionPosition / oldActualValue;
            newLength1 = new GridLength(oldLength.Value * percentage, oldLength.GridUnitType);
            newLength2 = new GridLength(oldLength.Value - newLength1.Value, oldLength.GridUnitType);
        }
        #endregion

        internal static string GridLengthToText(GridLength len)
        {
            switch (len.GridUnitType) {
                case GridUnitType.Auto: return "Auto";
                case GridUnitType.Star: return len.Value == 1 ? "*" : Math.Round(len.Value, 2) + "*";
                case GridUnitType.Pixel: return Math.Round(len.Value, 2) + "px";
            }
            return string.Empty;
        }

        /// <summary>Called by GridUnitSelector when a unit button is picked. A single SetValue is
        /// already one undo entry (UndoService.Execute), so no ChangeGroup here - the reference
        /// does the same. Guarded against a null SelectedItem, which the reference asserted on.</summary>
        public void SetGridLengthUnit(GridUnitType unit)
        {
            DesignItem item = unitSelector.SelectedItem;
            if (item == null) return;
            grid.UpdateLayout();
            SetGridLengthUnit(unit, item, orientation == Orientation.Vertical ? "Height" : "Width");
            grid.UpdateLayout();
            RefreshLabels();
        }

        static void SetGridLengthUnit(GridUnitType unit, DesignItem item, string propertyName)
        {
            DesignItemProperty itemProperty = item.Properties[propertyName];
            GridLength oldValue = itemProperty.GetConvertedValueOnInstance<GridLength>();
            // The reference's GetNewGridLength: Auto discards the value, every other unit keeps it.
            GridLength value = unit == GridUnitType.Auto ? GridLength.Auto : new GridLength(oldValue.Value, unit);
            if (!value.Equals(oldValue))
                itemProperty.SetValue(value);
        }
    }

    /// <summary>
    /// Drag an existing row/column boundary to resize the two definitions either side of it.
    /// This is a SECOND, INDEPENDENT drag life cycle - deliberately NOT routed through
    /// Controls/DragListener (the primitive 4a's resize/rotate/skew thumbs share): it owns its
    /// pointer capture, its own ChangeGroup and its own Escape-cancel, exactly as the reference's
    /// GridSplitterAdorner did against WPF's mouse capture. It does follow this port's established
    /// pointer idioms - AddHandler(..., handledEventsToo: true) because WinUI has no tunneling
    /// Preview* events, Focus(FocusState.Pointer) so KeyDown/Escape is reachable while captured,
    /// and it reuses DragListener's MinimumHorizontal/VerticalDragDistance constants rather than
    /// inventing a second threshold. Unlike DragListener it also handles PointerCaptureLost (the
    /// reference's OnLostMouseCapture), so an OS-revoked capture aborts rather than stranding an
    /// open ChangeGroup. Base is Grid, not Control: Control's OnPointerPressed virtuals do not
    /// exist on Panel, so handlers are attached rather than overridden - same as DesignerThumb.
    /// </summary>
    public abstract class GridSplitterAdorner : Grid
    {
        public static readonly DependencyProperty IsPreviewProperty =
            DependencyProperty.Register("IsPreview", typeof(bool), typeof(GridSplitterAdorner), new PropertyMetadata(false));

        protected readonly Grid grid;
        protected readonly DesignItem gridItem;
        protected readonly DesignItem firstDefinition, secondDefinition;
        protected readonly GridRailAdorner rail;

        ChangeGroup activeChangeGroup;
        double startPos;
        bool isDown;
        Pointer capturedPointer;

        internal GridSplitterAdorner(GridRailAdorner rail, DesignItem gridItem, DesignItem firstDefinition, DesignItem secondDefinition)
        {
            if (gridItem == null) throw new ArgumentNullException(nameof(gridItem));
            this.grid = (Grid)gridItem.Component;
            this.gridItem = gridItem;
            this.firstDefinition = firstDefinition;
            this.secondDefinition = secondDefinition;
            this.rail = rail;

            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x60, 0x1E, 0x90, 0xFF));
            IsTabStop = true;
            ProtectedCursor = InputSystemCursor.Create(CursorShape);

            AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)OnSplitterPointerPressed, true);
            AddHandler(UIElement.PointerMovedEvent, (PointerEventHandler)OnSplitterPointerMoved, true);
            AddHandler(UIElement.PointerReleasedEvent, (PointerEventHandler)OnSplitterPointerReleased, true);
            AddHandler(UIElement.PointerCaptureLostEvent, (PointerEventHandler)OnSplitterPointerCaptureLost, true);
            KeyDown += OnSplitterKeyDown;
        }

        public bool IsPreview {
            get { return (bool)GetValue(IsPreviewProperty); }
            set { SetValue(IsPreviewProperty, value); }
        }

        public bool IsDragging { get { return isDown; } }
        public bool HasOpenChangeGroup { get { return activeChangeGroup != null; } }

        protected abstract double GetCoordinate(Point point);
        protected abstract void RememberOriginalSize();
        protected abstract string RowColumnSizePropertyName { get; }
        protected abstract InputSystemCursorShape CursorShape { get; }

        protected GridLength original1, original2;
        protected double originalPixelSize1, originalPixelSize2;

        /// <summary>The ghost preview instance is constructed with null definitions; the reference
        /// would NullReferenceException in RememberOriginalSize if it were ever dragged. Guard it.</summary>
        bool CanDrag {
            get { return !IsPreview && firstDefinition != null && secondDefinition != null; }
        }

        void OnSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (!CanDrag) return;
            if (CapturePointer(e.Pointer)) {
                capturedPointer = e.Pointer;
                Focus(FocusState.Pointer);
                BeginAt(GetCoordinate(e.GetCurrentPoint(grid).Position));
            }
        }

        void OnSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!isDown || e.Pointer.PointerId != capturedPointer?.PointerId) return;
            MoveTo(GetCoordinate(e.GetCurrentPoint(grid).Position));
        }

        void OnSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!isDown || e.Pointer.PointerId != capturedPointer?.PointerId) return;
            Commit();
        }

        void OnSplitterPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (!isDown) return;
            Cancel();
        }

        void OnSplitterKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (isDown && e.Key == VirtualKey.Escape) {
                Cancel();
                e.Handled = true;
            }
        }

        void BeginAt(double position)
        {
            startPos = position;
            isDown = true;
        }

        void MoveTo(double position)
        {
            if (!isDown) return;
            if (activeChangeGroup == null) {
                // The reference compared against SystemParameters.Minimum*DragDistance, which has
                // no WinUI equivalent to read at runtime; DragListener already hardcodes WPF's
                // defaults as public consts (4a), so reuse them instead of a second magic number.
                double threshold = GetCoordinate(new Point(DragListener.MinimumHorizontalDragDistance,
                                                           DragListener.MinimumVerticalDragDistance));
                if (Math.Abs(position - startPos) < threshold) return;
                activeChangeGroup = gridItem.OpenGroup("Change grid row/column size");
                RememberOriginalSize();
            }
            ChangeSize(position - startPos);
        }

        void ChangeSize(double delta)
        {
            if (delta < -originalPixelSize1) delta = -originalPixelSize1;
            if (delta > originalPixelSize2) delta = originalPixelSize2;

            if (original1.IsAuto) original1 = new GridLength(originalPixelSize1);
            if (original2.IsAuto) original2 = new GridLength(originalPixelSize2);

            GridLength new1 = original1.IsStar && originalPixelSize1 > 0
                ? new GridLength(original1.Value * (originalPixelSize1 + delta) / originalPixelSize1, GridUnitType.Star)
                : new GridLength(originalPixelSize1 + delta);
            GridLength new2 = original2.IsStar && originalPixelSize2 > 0
                ? new GridLength(original2.Value * (originalPixelSize2 - delta) / originalPixelSize2, GridUnitType.Star)
                : new GridLength(originalPixelSize2 - delta);

            firstDefinition.Properties[RowColumnSizePropertyName].SetValue(new1);
            secondDefinition.Properties[RowColumnSizePropertyName].SetValue(new2);

            var parent = VisualTreeHelper.GetParent(this) as UIElement;
            if (parent != null) parent.InvalidateArrange();
            // The reference's rail.InvalidateVisual(): force layout so the rail's captions and the
            // splitter placements read settled ActualHeight/ActualWidth on this same tick.
            grid.UpdateLayout();
            if (rail != null) rail.RefreshLabels();
        }

        void Commit()
        {
            isDown = false;
            ReleaseCapture();
            if (activeChangeGroup != null) {
                activeChangeGroup.Commit();
                activeChangeGroup = null;
            }
        }

        /// <summary>Aborts the in-progress drag - Escape, a lost pointer capture, or any caller.
        /// The ChangeGroup's Abort rolls both size writes back.</summary>
        public void Cancel()
        {
            isDown = false;
            ReleaseCapture();
            if (activeChangeGroup != null) {
                activeChangeGroup.Abort();
                activeChangeGroup = null;
            }
            grid.UpdateLayout();
            if (rail != null) rail.RefreshLabels();
        }

        void ReleaseCapture()
        {
            if (capturedPointer != null) {
                ReleasePointerCapture(capturedPointer);
                capturedPointer = null;
            }
        }

        /// <summary>Test seam. This controller is not built on DragListener, so it needs its own
        /// equivalent of DragListener.SimulateDown/Move/Up: PointerRoutedEventArgs is a sealed
        /// WinRT type with no public constructor, so no test can synthesize a real pointer event.
        /// The argument is a point in the Grid's own coordinate space - the same thing
        /// e.GetCurrentPoint(grid).Position gives the real handler - which GetCoordinate collapses
        /// to the single axis this splitter tracks.</summary>
        internal void SimulateDown(Point pointInGrid)
        {
            if (!CanDrag) return;
            BeginAt(GetCoordinate(pointInGrid));
        }

        /// <summary>Test seam - see SimulateDown.</summary>
        internal void SimulateMove(Point pointInGrid)
        {
            MoveTo(GetCoordinate(pointInGrid));
        }

        /// <summary>Test seam - see SimulateDown.</summary>
        internal void SimulateUp(Point pointInGrid)
        {
            MoveTo(GetCoordinate(pointInGrid));
            Commit();
        }

        /// <summary>Test seam: the Escape-while-captured path, without a KeyRoutedEventArgs
        /// (also constructor-less).</summary>
        internal void SimulateEscape()
        {
            Cancel();
        }
    }

    public class GridRowSplitterAdorner : GridSplitterAdorner
    {
        internal GridRowSplitterAdorner(GridRailAdorner rail, DesignItem gridItem, DesignItem firstRow, DesignItem secondRow)
            : base(rail, gridItem, firstRow, secondRow)
        {
        }

        protected override InputSystemCursorShape CursorShape { get { return InputSystemCursorShape.SizeNorthSouth; } }
        protected override double GetCoordinate(Point point) { return point.Y; }

        protected override void RememberOriginalSize()
        {
            var r1 = (RowDefinition)firstDefinition.Component;
            var r2 = (RowDefinition)secondDefinition.Component;
            original1 = r1.Height;
            original2 = r2.Height;
            originalPixelSize1 = r1.ActualHeight;
            originalPixelSize2 = r2.ActualHeight;
        }

        protected override string RowColumnSizePropertyName { get { return "Height"; } }
    }

    public sealed class GridColumnSplitterAdorner : GridSplitterAdorner
    {
        internal GridColumnSplitterAdorner(GridRailAdorner rail, DesignItem gridItem, DesignItem firstColumn, DesignItem secondColumn)
            : base(rail, gridItem, firstColumn, secondColumn)
        {
        }

        protected override InputSystemCursorShape CursorShape { get { return InputSystemCursorShape.SizeWestEast; } }
        protected override double GetCoordinate(Point point) { return point.X; }

        protected override void RememberOriginalSize()
        {
            var c1 = (ColumnDefinition)firstDefinition.Component;
            var c2 = (ColumnDefinition)secondDefinition.Component;
            original1 = c1.Width;
            original2 = c2.Width;
            originalPixelSize1 = c1.ActualWidth;
            originalPixelSize2 = c2.ActualWidth;
        }

        protected override string RowColumnSizePropertyName { get { return "Width"; } }
    }
}
```

  > `GridSplitterAdorner`/`GridRowSplitterAdorner`/`GridColumnSplitterAdorner` and `GridUnitSelector`
  > are shown here as part of the same file/class family the rails depend on at construction time
  > (`GridRailAdorner`'s constructor builds a preview `GridSplitterAdorner` and a
  > `GridUnitSelector` immediately) — write all of `GridAdorner.cs` (this class plus the three
  > splitter classes) and `Controls/GridUnitSelector.cs` (Task 13) together in this step, even
  > though Tasks 12-13 below restate their own pieces with additional test coverage. This task's
  > own tests (Step 1) only exercise rail/creation behavior; Tasks 12-13 exercise the splitter
  > drag and the unit picker.

  **3c.** `src/WinUIDesigner.Designer/Extensions/GridAdornerProvider.cs` (new):

```csharp
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Shows the row/column rails on a Grid that is selected, or whose direct child is
    /// selected.</summary>
    [ExtensionFor(typeof(Grid))]
    [ExtensionServer(typeof(LogicalOrExtensionServer<PrimarySelectionExtensionServer, PrimarySelectionParentExtensionServer>))]
    public sealed class GridAdornerProvider : AdornerProvider
    {
        // Unlike 4a's thumbs, which all use the ready-made RelativePlacement, a rail splitter sits
        // at a specific row/column boundary rather than a fixed corner/edge alignment - so it needs
        // its own AdornerPlacement, mirroring the reference's RowSplitterPlacement /
        // ColumnSplitterPlacement. The reference captured row.Offset; WinUI has no Offset, so the
        // index is captured instead and the running sum is recomputed on every Arrange, which also
        // keeps the splitter correct while a drag resizes the rows.
        sealed class RowSplitterPlacement : AdornerPlacement
        {
            readonly Grid grid;
            readonly int rowIndex;
            public RowSplitterPlacement(Grid grid, int rowIndex) { this.grid = grid; this.rowIndex = rowIndex; }

            public override void Arrange(AdornerPanel panel, UIElement adorner, Size adornedElementSize)
            {
                double offset = GridRailAdorner.GetOffset(grid, Orientation.Vertical, rowIndex);
                adorner.Arrange(new Rect(-(GridRailAdorner.RailSize + GridRailAdorner.RailDistance),
                                         offset - GridRailAdorner.SplitterWidth / 2,
                                         GridRailAdorner.RailSize + GridRailAdorner.RailDistance + adornedElementSize.Width,
                                         GridRailAdorner.SplitterWidth));
            }
        }

        sealed class ColumnSplitterPlacement : AdornerPlacement
        {
            readonly Grid grid;
            readonly int columnIndex;
            public ColumnSplitterPlacement(Grid grid, int columnIndex) { this.grid = grid; this.columnIndex = columnIndex; }

            public override void Arrange(AdornerPanel panel, UIElement adorner, Size adornedElementSize)
            {
                double offset = GridRailAdorner.GetOffset(grid, Orientation.Horizontal, columnIndex);
                adorner.Arrange(new Rect(offset - GridRailAdorner.SplitterWidth / 2,
                                         -(GridRailAdorner.RailSize + GridRailAdorner.RailDistance),
                                         GridRailAdorner.SplitterWidth,
                                         GridRailAdorner.RailSize + GridRailAdorner.RailDistance + adornedElementSize.Height));
            }
        }

        readonly AdornerPanel adornerPanel = new AdornerPanel();
        readonly List<GridSplitterAdorner> splitterList = new List<GridSplitterAdorner>();
        GridRailAdorner topBar, leftBar;
        bool requireRebuild;

        internal GridRailAdorner LeftRail { get { return leftBar; } }
        internal GridRailAdorner TopRail { get { return topBar; } }
        internal IList<GridSplitterAdorner> Splitters { get { return splitterList; } }

        protected override void OnInitialized()
        {
            leftBar = new GridRailAdorner(this.ExtendedItem, adornerPanel, Orientation.Vertical);
            topBar = new GridRailAdorner(this.ExtendedItem, adornerPanel, Orientation.Horizontal);

            var rp = new RelativePlacement(HorizontalAlignment.Left, VerticalAlignment.Stretch);
            rp.XOffset -= GridRailAdorner.RailDistance;
            AdornerPanel.SetPlacement(leftBar, rp);
            rp = new RelativePlacement(HorizontalAlignment.Stretch, VerticalAlignment.Top);
            rp.YOffset -= GridRailAdorner.RailDistance;
            AdornerPanel.SetPlacement(topBar, rp);

            adornerPanel.Children.Add(leftBar);
            adornerPanel.Children.Add(topBar);
            this.Adorners.Add(adornerPanel);

            RequestRebuild();
            this.ExtendedItem.PropertyChanged += OnPropertyChanged;

            base.OnInitialized();
        }

        protected override void OnRemove()
        {
            this.ExtendedItem.PropertyChanged -= OnPropertyChanged;
            base.OnRemove();
        }

        void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "RowDefinitions" || e.PropertyName == "ColumnDefinitions")
                RequestRebuild();
        }

        /// <summary>
        /// The reference delayed this with DispatcherPriority.Loaded for two reasons: coalescing
        /// several collection changes into one rebuild, and guaranteeing a new row/column's layout
        /// had settled before reading it. WinUI's DispatcherQueuePriority has no Loaded member
        /// (spec Known gap #3) and .Normal/.Low carry no "after layout" promise, so the two
        /// concerns are separated: the enqueue only coalesces, and Rebuild calls
        /// grid.UpdateLayout() itself to get the settled-layout guarantee. Measured: reading
        /// ActualHeight straight after inserting a RowDefinition returns a stale 0; after an
        /// explicit UpdateLayout it is correct.
        /// </summary>
        void RequestRebuild()
        {
            if (requireRebuild) return;
            requireRebuild = true;

            var queue = this.ExtendedItem.View != null ? this.ExtendedItem.View.DispatcherQueue : null;
            if (queue == null || !queue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Rebuild))
                Rebuild();
        }

        /// <summary>Test seam: a test body runs inside a single dispatcher turn, so the enqueued
        /// Rebuild can never fire during it. Tests call this instead.</summary>
        internal void FlushPendingRebuild()
        {
            if (requireRebuild) Rebuild();
        }

        void Rebuild()
        {
            requireRebuild = false;
            var grid = this.ExtendedItem.Component as Grid;
            if (grid == null) return;

            foreach (GridSplitterAdorner splitter in splitterList)
                adornerPanel.Children.Remove(splitter);
            splitterList.Clear();

            grid.UpdateLayout();

            // The live Grid and the model collection can disagree for a tick while a collection
            // change is still propagating; take the shorter of the two so an index is never out
            // of range on either side.
            IList<DesignItem> rows = this.ExtendedItem.Properties["RowDefinitions"].CollectionElements;
            for (int i = 1; i < grid.RowDefinitions.Count && i < rows.Count; i++) {
                var splitter = new GridRowSplitterAdorner(leftBar, this.ExtendedItem, rows[i - 1], rows[i]);
                AdornerPanel.SetPlacement(splitter, new RowSplitterPlacement(grid, i));
                adornerPanel.Children.Add(splitter);
                splitterList.Add(splitter);
            }

            IList<DesignItem> columns = this.ExtendedItem.Properties["ColumnDefinitions"].CollectionElements;
            for (int i = 1; i < grid.ColumnDefinitions.Count && i < columns.Count; i++) {
                var splitter = new GridColumnSplitterAdorner(topBar, this.ExtendedItem, columns[i - 1], columns[i]);
                AdornerPanel.SetPlacement(splitter, new ColumnSplitterPlacement(grid, i));
                adornerPanel.Children.Add(splitter);
                splitterList.Add(splitter);
            }

            leftBar.RefreshLabels();
            topBar.RefreshLabels();
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj` then run the built `.exe` filtered with `--test=WinUIDesigner.Designer.Tests.GridAdornerProviderTests`.
  Expected: PASS (5 tests). Then rebuild and re-run **all three** suites (`WinUIDesigner.Designer.Tests`, `WinUIDesigner.DesignModel.Tests`, `WinUIDesigner.XamlDom.Tests`) unfiltered — Step 3a changes `WinUIDesigner.XamlDom`, which every prior phase's tests also exercise.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Designer/Controls/GridAdorner.cs src/WinUIDesigner.Designer/Controls/GridUnitSelector.cs src/WinUIDesigner.Designer/Extensions/GridAdornerProvider.cs src/WinUIDesigner.XamlDom/XamlPropertyInfo.cs tests/WinUIDesigner.Designer.Tests/GridAdornerProviderTests.cs
git commit -m "feat: add Grid rail adorners and click-to-create row/column (Phase 4b Task 11)"
```

  > `Controls/GridUnitSelector.cs` is Task 13's file, but per the note after Step 3b it must exist
  > (even in skeleton form) for this task to compile, since `GridRailAdorner`'s constructor
  > references it directly. Write Task 13's `GridUnitSelector.cs` in full now if Task 13 hasn't
  > run yet, and skip re-creating it when Task 13's own turn comes — same reconciliation the plan
  > already applies to `OptionService.cs` across Tasks 4/8.

---

### Task 12: Independent splitter-drag controller

**Files:**
- Modify: `src/WinUIDesigner.Designer/Controls/GridAdorner.cs` (the three splitter classes already landed in Task 11 — this task adds their test coverage; no further code change is needed beyond Task 11's own content)
- Modify: `src/WinUIDesigner.Designer/Extensions/GridAdornerProvider.cs` (already wires `Splitters`/`Rebuild` in Task 11)
- Test: Modify `tests/WinUIDesigner.Designer.Tests/GridAdornerProviderTests.cs`

**Interfaces:**
- Consumes (Task 11): `GridRailAdorner` ctor, `RailSize`/`RailDistance`/`SplitterWidth`, `GetOffset`, `RefreshLabels()`, `GridAdornerProvider.FlushPendingRebuild()`, `GridAdornerProvider.Splitters`, `GridSplitterAdorner`/`GridRowSplitterAdorner`/`GridColumnSplitterAdorner` and their `SimulateDown`/`SimulateMove`/`SimulateUp`/`SimulateEscape`/`IsDragging`/`HasOpenChangeGroup` test seams — all already implemented as part of Task 11's `GridAdorner.cs` (the reference's `GridRailAdorner`/`GridSplitterAdorner` are defined in the same file and are interdependent at construction time, so Task 11 built both; this task is purely the test-coverage gate for the splitter half).
- Produces: nothing new — this task's deliverable is verifying the splitter-drag behavior Task 11 already implemented.

- [ ] **Step 1: Write the failing test(s)**

  Append to `tests/WinUIDesigner.Designer.Tests/GridAdornerProviderTests.cs` (inside the fixture):

```csharp
        [Test]
        public void OneSplitterExistsPerInteriorRowAndColumnBoundary()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out _, out var provider);
                Assert.AreEqual(2, provider.Splitters.Count, "two rows and two columns => one boundary each");
                Assert.AreEqual(1, provider.Splitters.OfType<GridRowSplitterAdorner>().Count());
                Assert.AreEqual(1, provider.Splitters.OfType<GridColumnSplitterAdorner>().Count());
                var inTree = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<GridSplitterAdorner>().ToList();
                CollectionAssert.IsSubsetOf(provider.Splitters.ToList(), inTree);
            });
        }

        [Test]
        public void DraggingRowSplitterResizesBothRowsAndCommitsOneChangeGroup()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                var splitter = provider.Splitters.OfType<GridRowSplitterAdorner>().Single();

                splitter.SimulateDown(new Windows.Foundation.Point(0, 60));
                splitter.SimulateMove(new Windows.Foundation.Point(0, 80));
                splitter.SimulateUp(new Windows.Foundation.Point(0, 80));
                surface.UpdateLayout();

                Assert.AreEqual(80, grid.RowDefinitions[0].ActualHeight, 0.5);
                Assert.AreEqual(40, grid.RowDefinitions[1].ActualHeight, 0.5);
                Assert.AreEqual(1, undo.UndoActions.Count(), "one ChangeGroup for the whole drag");

                undo.Undo();
                surface.UpdateLayout();
                Assert.AreEqual(60, grid.RowDefinitions[0].ActualHeight, 0.5);
                Assert.AreEqual(60, grid.RowDefinitions[1].ActualHeight, 0.5);
            });
        }

        [Test]
        public void DraggingColumnSplitterResizesBothColumns()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                var splitter = provider.Splitters.OfType<GridColumnSplitterAdorner>().Single();

                splitter.SimulateDown(new Windows.Foundation.Point(100, 0));
                splitter.SimulateMove(new Windows.Foundation.Point(140, 0));
                splitter.SimulateUp(new Windows.Foundation.Point(140, 0));
                surface.UpdateLayout();

                Assert.AreEqual(140, grid.ColumnDefinitions[0].ActualWidth, 0.5);
                Assert.AreEqual(60, grid.ColumnDefinitions[1].ActualWidth, 0.5);
            });
        }

        [Test]
        public void EscapeCancelsSplitterDragWithNoUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                var splitter = provider.Splitters.OfType<GridRowSplitterAdorner>().Single();

                splitter.SimulateDown(new Windows.Foundation.Point(0, 60));
                splitter.SimulateMove(new Windows.Foundation.Point(0, 90));
                splitter.SimulateEscape();
                surface.UpdateLayout();

                Assert.AreEqual(60, grid.RowDefinitions[0].ActualHeight, 0.5, "aborted drag reverts the live size");
                Assert.IsFalse(undo.CanUndo, "an aborted ChangeGroup pushes nothing onto the undo stack");
                Assert.IsFalse(splitter.HasOpenChangeGroup);
                Assert.IsFalse(splitter.IsDragging);
            });
        }

        [Test]
        public void MoveBelowTheDragThresholdOpensNoChangeGroup()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                var splitter = provider.Splitters.OfType<GridRowSplitterAdorner>().Single();

                splitter.SimulateDown(new Windows.Foundation.Point(0, 60));
                splitter.SimulateMove(new Windows.Foundation.Point(0, 61)); // < MinimumVerticalDragDistance
                Assert.IsFalse(splitter.HasOpenChangeGroup);
                splitter.SimulateUp(new Windows.Foundation.Point(0, 61));

                Assert.IsFalse(undo.CanUndo);
                Assert.AreEqual(60, grid.RowDefinitions[0].ActualHeight, 0.5);
            });
        }
```

- [ ] **Step 2: Run to verify it fails**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: only PASS if Task 11 already lands the three splitter classes and `GridAdornerProvider.Splitters` fully (it does — see the note at the end of Task 11). If run as a genuinely separate step before Task 11's splitter code exists, expected FAIL: `CS0246: The type or namespace name 'GridRowSplitterAdorner' could not be found` and `CS1061: 'GridAdornerProvider' does not contain a definition for 'Splitters'`.

- [ ] **Step 3: (implementation already present from Task 11 — this step is a no-op)**

  No new code. If Task 11 was executed exactly as written above, `GridSplitterAdorner`/
  `GridRowSplitterAdorner`/`GridColumnSplitterAdorner` and `GridAdornerProvider.Splitters`/
  `Rebuild()`'s splitter-population loop already exist. Re-confirm by reading
  `src/WinUIDesigner.Designer/Controls/GridAdorner.cs` and
  `src/WinUIDesigner.Designer/Extensions/GridAdornerProvider.cs` before writing Step 1's tests, in
  case Task 11 was adjusted during its own review.

- [ ] **Step 4: Run to verify it passes**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj` then run the built `.exe` filtered with `--test=WinUIDesigner.Designer.Tests.GridAdornerProviderTests`.
  Expected: PASS (10 tests — Task 11's 5 plus these 5). Then re-run all three suites with no filter; all green.

- [ ] **Step 5: Commit**

```bash
git add tests/WinUIDesigner.Designer.Tests/GridAdornerProviderTests.cs
git commit -m "test: cover the independent Grid splitter-drag controller (Phase 4b Task 12)"
```

---

### Task 13: GridUnitSelector (Auto / star / pixel size-unit picker)

**Files:**
- Create: `src/WinUIDesigner.Designer/Controls/GridUnitSelector.cs` (if not already written verbatim as part of Task 11 — see the note at the end of Task 11's Step 3)
- Test: Modify `tests/WinUIDesigner.Designer.Tests/GridAdornerProviderTests.cs`

**Interfaces:**
- Consumes (Tasks 11-12): `GridRailAdorner.GetOffset`, `.RefreshLabels()`, `.SimulateHover(double)`, `.RailSize`/`.RailDistance`, `RelativePlacement` offsets, `DesignItem.Properties["Height"|"Width"].GetConvertedValueOnInstance<GridLength>()`/`.SetValue(object)`.
- Produces: `public sealed class WinUIDesigner.Designer.Controls.GridUnitSelector : Microsoft.UI.Xaml.Controls.Grid` — `GridUnitSelector(GridRailAdorner rail)`, `public Orientation Orientation { get; set; }`, `public DesignItem SelectedItem { get; set; }`, `public GridUnitType Unit { get; set; }`, `internal void SimulatePick(GridUnitType unit)` (test seam — `ToggleButton.Checked` cannot be raised without a real click). `GridRailAdorner.UnitSelector`/`SetGridLengthUnit` already exist from Task 11.

> **Note on form:** the reference is `GridUnitSelector.xaml` + `.xaml.cs` — but its three `RadioButton`s are retemplated into plain `ToggleButton`s by a local `ControlTemplate`. So building it code-only from three `ToggleButton`s with hand-rolled mutual exclusion reproduces the reference's *actual* UI exactly, and needs no `generic.xaml` or style resource dictionary — the same idiom `Controls/Thumbs/DesignerThumb.cs` established in 4a. No deviation to flag.

- [ ] **Step 1: Write the failing test(s)**

  Append to `tests/WinUIDesigner.Designer.Tests/GridAdornerProviderTests.cs` (inside the fixture):

```csharp
        [Test]
        public void UnitSelectorSetsAutoStarAndPixelOnTheHoveredRow()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                provider.LeftRail.SimulateHover(30); // inside row 0 (0..60)
                var selector = provider.LeftRail.UnitSelector;
                Assert.IsNotNull(selector.SelectedItem);
                Assert.AreEqual(GridUnitType.Star, selector.Unit, "an unset RowDefinition.Height is 1*");
                Assert.AreEqual(Visibility.Visible, selector.Visibility);

                selector.SimulatePick(GridUnitType.Auto);
                surface.UpdateLayout();
                Assert.IsTrue(grid.RowDefinitions[0].Height.IsAuto);
                Assert.AreEqual(GridUnitType.Auto, selector.Unit);
                CollectionAssert.AreEqual(new[] { "Auto", "*" }, provider.LeftRail.LabelTexts.ToList(),
                    "the rail caption follows the unit change");

                selector.SimulatePick(GridUnitType.Star);
                surface.UpdateLayout();
                Assert.IsTrue(grid.RowDefinitions[0].Height.IsStar);

                selector.SimulatePick(GridUnitType.Pixel);
                surface.UpdateLayout();
                Assert.IsTrue(grid.RowDefinitions[0].Height.IsAbsolute);
            });
        }

        [Test]
        public void UnitSelectorTargetsTheHoveredColumnOnly()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                provider.TopRail.SimulateHover(150); // inside column 1 (100..200)
                var selector = provider.TopRail.UnitSelector;
                Assert.AreSame(surface.DesignContext.Services.Component.GetDesignItem(grid.ColumnDefinitions[1]),
                               selector.SelectedItem);

                selector.SimulatePick(GridUnitType.Auto);
                surface.UpdateLayout();
                Assert.IsTrue(grid.ColumnDefinitions[1].Width.IsAuto);
                Assert.IsFalse(grid.ColumnDefinitions[0].Width.IsAuto, "the other column is untouched");
            });
        }

        [Test]
        public void HoveringPastTheLastRowHidesTheUnitSelector()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out _, out var provider);
                provider.LeftRail.SimulateHover(500); // past the bottom of the grid
                Assert.AreEqual(Visibility.Collapsed, provider.LeftRail.UnitSelector.Visibility);
            });
        }
```

- [ ] **Step 2: Run to verify it fails**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: PASS to compile if `GridUnitSelector.cs` was already written as part of Task 11 (per its note); otherwise FAIL with `CS1061: 'GridRailAdorner' does not contain a definition for 'UnitSelector'`.

- [ ] **Step 3: Write the implementation**

  `src/WinUIDesigner.Designer/Controls/GridUnitSelector.cs` (new — skip if Task 11 already wrote it verbatim):

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Controls
{
    /// <summary>
    /// Size-unit picker (Fixed / star / Auto) for one RowDefinition or ColumnDefinition, shown
    /// beside the rail while the pointer hovers a row/column. Ported from the reference's
    /// GridUnitSelector.xaml + .xaml.cs. Built code-only, no generic.xaml and no style resource
    /// dictionary - the same idiom Controls/Thumbs/DesignerThumb.cs established in 4a, and no loss
    /// of fidelity here: the reference's three RadioButtons carry a local ControlTemplate that
    /// retemplates each one into a ToggleButton, so three ToggleButtons with hand-rolled mutual
    /// exclusion IS the reference's actual UI. Base is Grid rather than UserControl/Border (Border
    /// is sealed in this SDK) - Grid carries Background/BorderBrush/BorderThickness/CornerRadius/
    /// Padding directly, so the reference's rounded blue frame needs no extra element.
    /// </summary>
    public sealed class GridUnitSelector : Grid
    {
        readonly GridRailAdorner rail;
        readonly StackPanel buttons;
        readonly ToggleButton fixedButton, starButton, autoButton;

        // The reference relied on SetGridLengthUnit's "if (value != oldValue)" to make the Checked
        // event it raises while *displaying* a unit a no-op. An explicit flag is clearer and also
        // stops a display update from opening a spurious undo entry.
        bool updating;

        public GridUnitSelector(GridRailAdorner rail)
        {
            this.rail = rail;

            Background = new SolidColorBrush(Microsoft.UI.Colors.LightBlue);
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.CornflowerBlue);
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(3);
            Padding = new Thickness(1);
            IsHitTestVisible = true;

            fixedButton = CreateButton("Fixed", 8, "Fixed - size is a fixed pixel width/height", GridUnitType.Pixel);
            starButton = CreateButton("*", 16, "Star - size is a weighted proportion", GridUnitType.Star);
            autoButton = CreateButton("Auto", 8, "Auto - size is determined by the content(s)", GridUnitType.Auto);

            buttons = new StackPanel { Orientation = Orientation.Vertical };
            buttons.Children.Add(fixedButton);
            buttons.Children.Add(starButton);
            buttons.Children.Add(autoButton);
            Children.Add(buttons);

            // WinUI has no Visibility.Hidden - Collapsed is the established replacement, and
            // AdornerPanel arranges every child independently so a Collapsed sibling perturbs
            // nothing.
            Visibility = Visibility.Collapsed;
            PointerExited += (s, e) => Visibility = Visibility.Collapsed;
        }

        ToggleButton CreateButton(string content, double fontSize, string tooltip, GridUnitType unit)
        {
            var button = new ToggleButton {
                Content = content,
                FontSize = fontSize,
                Margin = new Thickness(1),
                Padding = new Thickness(6),
                MinWidth = 0,
                MinHeight = 0,
            };
            ToolTipService.SetToolTip(button, tooltip);
            button.Checked += (s, e) => OnUnitPicked(button, unit);
            // A RadioButton group cannot be un-picked; three ToggleButtons can, so re-check the one
            // the user just cleared rather than leaving the row with no unit selected.
            button.Unchecked += (s, e) => {
                if (!updating && !AnyChecked()) {
                    updating = true;
                    button.IsChecked = true;
                    updating = false;
                }
            };
            return button;
        }

        bool AnyChecked()
        {
            return fixedButton.IsChecked == true || starButton.IsChecked == true || autoButton.IsChecked == true;
        }

        void OnUnitPicked(ToggleButton source, GridUnitType unit)
        {
            if (updating) return;
            updating = true;
            try {
                foreach (var other in new[] { fixedButton, starButton, autoButton })
                    if (other != source) other.IsChecked = false;
            } finally {
                updating = false;
            }
            rail.SetGridLengthUnit(unit);
        }

        public Orientation Orientation {
            get { return buttons.Orientation; }
            set { buttons.Orientation = value; }
        }

        public DesignItem SelectedItem { get; set; }

        public GridUnitType Unit {
            get {
                if (autoButton.IsChecked == true) return GridUnitType.Auto;
                if (starButton.IsChecked == true) return GridUnitType.Star;
                return GridUnitType.Pixel;
            }
            set {
                updating = true;
                try {
                    autoButton.IsChecked = value == GridUnitType.Auto;
                    starButton.IsChecked = value == GridUnitType.Star;
                    fixedButton.IsChecked = value == GridUnitType.Pixel;
                } finally {
                    updating = false;
                }
            }
        }

        /// <summary>Test seam: drives the same path a real click on one of the three buttons does.
        /// ToggleButton.Checked cannot be raised without a live click, so tests call this.</summary>
        internal void SimulatePick(GridUnitType unit)
        {
            var button = unit == GridUnitType.Auto ? autoButton : unit == GridUnitType.Star ? starButton : fixedButton;
            OnUnitPicked(button, unit);
            updating = true;
            try {
                button.IsChecked = true;
            } finally {
                updating = false;
            }
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj` then run the built `.exe` filtered with `--test=WinUIDesigner.Designer.Tests.GridAdornerProviderTests`.
  Expected: PASS (13 tests — Tasks 11, 12 and 13 together). Then re-run all three suites with no filter; all green.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Designer/Controls/GridUnitSelector.cs tests/WinUIDesigner.Designer.Tests/GridAdornerProviderTests.cs
git commit -m "feat: add GridUnitSelector size-unit picker (Phase 4b Task 13)"
```

---

### Task 14: MoveLogic + TopLeftContainerDragHandle

**Files:**
- Create: `src/WinUIDesigner.Designer/Controls/MoveLogic.cs`
- Create: `src/WinUIDesigner.Designer/Controls/ContainerDragHandle.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/TopLeftContainerDragHandle.cs`
- Test: Create `tests/WinUIDesigner.Designer.Tests/MoveTests.cs`

**Interfaces:**
- Consumes: `PlacementOperation.ChangeContainer` (Task 1),
  `DragListener` incl. its `PointerCaptureLost` handling (Task 2),
  `DesignPanel.HitTest(Point, bool, bool, Predicate<DesignPanelHitTestResult>, HitTestType)`
  (Phase 3, unchanged), `ISelectionService.IsComponentSelected(DesignItem)`
  (`src/WinUIDesigner.DesignModel/Services.cs:68`),
  `IPlacementBehavior.CanPlace`/`CanLeaveContainer`/`CanEnterContainer`/
  `BeforeSetPosition`/`SetPosition` (Phase 4a interface, Task 4's
  `BeforeSetPosition` signature change — **this task assumes Task 4 lands
  `BeforeSetPosition(PlacementOperation operation, Windows.System.VirtualKeyModifiers modifiers)`
  exactly (matching `DragListener.KeyModifiers`'s existing type) — if
  Task 4 lands a different exact signature, update `MoveLogic.Move`'s one
  call site to match rather than re-deriving this task's design**),
  `PlacementInformation.BoundsPrecision`/`.OriginalBounds`/`.Bounds`
  (Phase 4a), `AdornerProvider.AddAdorner(AdornerPlacement, AdornerOrder, UIElement)`
  (`src/WinUIDesigner.DesignModel/Adorners/AdornerProvider.cs:59`),
  `RelativePlacement` (Phase 4a), `OnlyOneItemSelectedExtensionServer`
  (Phase 4a).
- Produces: `internal class MoveLogic` (constructor `MoveLogic(DesignItem clickedOn)`,
  methods `Start(Point windowPoint)`, `Move(Point windowPoint, VirtualKeyModifiers modifiers)`,
  `Stop()`, `Cancel()`) — Task 15's `PanelMove` reuses this exact class.
  `TopLeftContainerDragHandle : AdornerProvider` with an internal
  `readonly DragListener DragListener` field — this task's own tests use
  it directly (same pattern as 4a's `ResizeThumbExtension.DragListeners`).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class MoveTests
    {
        // Side-by-side (not stacked/overlapping) so a window-coordinate point unambiguously
        // hit-tests into one container or the other - the whole point of these tests is
        // exercising real coordinate-based hit-testing during a drag (Known gap #1's first
        // real exercise of LeaveContainer/EnterContainer via ChangeContainer).
        const string SideBySideCanvasesXaml =
            "<StackPanel Orientation=\"Horizontal\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Canvas x:Name=\"SourceCanvas\" Width=\"150\" Height=\"150\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" Canvas.Left=\"10\" Canvas.Top=\"10\" />" +
            "</Canvas>" +
            "<Canvas x:Name=\"TargetCanvas\" Width=\"150\" Height=\"150\" />" +
            "</StackPanel>";

        static (DesignSurface surface, DesignItem buttonItem, DesignItem sourceItem, DesignItem targetItem)
            LoadSelected(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(SideBySideCanvasesXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();

            var root = (Microsoft.UI.Xaml.Controls.StackPanel)surface.DesignContext.RootItem.Component;
            var sourceCanvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
            var targetCanvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[1];
            var button = (Microsoft.UI.Xaml.Controls.Button)sourceCanvas.Children[0];

            var services = surface.DesignContext.Services;
            var buttonItem = services.Component.GetDesignItem(button);
            var sourceItem = services.Component.GetDesignItem(sourceCanvas);
            var targetItem = services.Component.GetDesignItem(targetCanvas);
            services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            return (surface, buttonItem, sourceItem, targetItem);
        }

        static TopLeftContainerDragHandle GetExtension(DesignItem buttonItem)
        {
            return buttonItem.Extensions.OfType<TopLeftContainerDragHandle>().Single();
        }

        [Test]
        public void SelectingAnElementShowsATopLeftDragHandle()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, buttonItem, _, _) = LoadSelected(window);
                Assert.DoesNotThrow(() => GetExtension(buttonItem));
            });
        }

        [Test]
        public void DraggingTheHandleWithinTheSameContainerMovesTheItemAndCommitsOneChangeGroup()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, _) = LoadSelected(window);
                var extension = GetExtension(buttonItem);
                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                bool canUndoBefore = undoService.CanUndo;

                // Simulated points are window coordinates (DragListener's SimulateDown/Move
                // bypass real pointer hit-testing entirely, but Move()'s own hit-testing math
                // still runs against these as real window coordinates - see MoveLogic).
                extension.DragListener.SimulateDown(new Windows.Foundation.Point(20, 20));
                extension.DragListener.SimulateMove(new Windows.Foundation.Point(40, 35)); // +20,+15, stays inside SourceCanvas (width 150)
                extension.DragListener.SimulateUp(new Windows.Foundation.Point(40, 35));
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, buttonItem.Parent, "a same-container move must not reparent");
                var left = buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>();
                var top = buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>();
                Assert.AreEqual(30, left, 0.5);
                Assert.AreEqual(25, top, 0.5);
                Assert.IsTrue(undoService.CanUndo);
                Assert.AreNotEqual(canUndoBefore, undoService.CanUndo);
            });
        }

        [Test]
        public void DraggingTheHandleAcrossContainersReparentsTheItemWithOneUndoRevertingBoth()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, targetItem) = LoadSelected(window);
                var extension = GetExtension(buttonItem);

                // NOTE: these window-coordinate values assume the StackPanel's content starts
                // at (0,0) in window space, matching every other fixture in this test suite
                // (e.g. ResizeThumbExtensionTests). If a real run shows the hit-test landing on
                // the wrong container, adjust these coordinates to match the actual observed
                // layout rather than the container boundaries assumed here - this is exactly
                // the kind of thing Known gap #1 flagged as needing empirical confirmation.
                extension.DragListener.SimulateDown(new Windows.Foundation.Point(20, 20));
                extension.DragListener.SimulateMove(new Windows.Foundation.Point(180, 20)); // past x=150, into TargetCanvas
                extension.DragListener.SimulateUp(new Windows.Foundation.Point(180, 20));
                surface.UpdateLayout();

                Assert.AreSame(targetItem, buttonItem.Parent, "the item should have reparented into TargetCanvas");
                Assert.IsFalse(sourceItem.ContentProperty.CollectionElements.Contains(buttonItem));
                Assert.IsTrue(targetItem.ContentProperty.CollectionElements.Contains(buttonItem));

                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                Assert.IsTrue(undoService.CanUndo);
                undoService.Undo();
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, buttonItem.Parent,
                    "a single undo must revert the whole gesture - reparent and position together");
                Assert.IsTrue(sourceItem.ContentProperty.CollectionElements.Contains(buttonItem));
                Assert.IsFalse(targetItem.ContentProperty.CollectionElements.Contains(buttonItem));
            });
        }

        [Test]
        public void CancelingAMoveRevertsPositionWithNoCommittedChange()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, _) = LoadSelected(window);
                var extension = GetExtension(buttonItem);
                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                bool canUndoBefore = undoService.CanUndo;

                extension.DragListener.SimulateDown(new Windows.Foundation.Point(20, 20));
                extension.DragListener.SimulateMove(new Windows.Foundation.Point(50, 50));
                extension.DragListener.Cancel();
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, buttonItem.Parent);
                var left = buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>();
                Assert.AreEqual(10, left, 0.5, "canceled move should revert to the original position");
                Assert.AreEqual(canUndoBefore, undoService.CanUndo,
                    "an aborted PlacementOperation must not push anything onto the undo stack");
            });
        }

        [Test]
        public void PointerCaptureLostMidMoveCancelsCleanly()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, _) = LoadSelected(window);
                var extension = GetExtension(buttonItem);
                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                bool canUndoBefore = undoService.CanUndo;

                extension.DragListener.SimulateDown(new Windows.Foundation.Point(20, 20));
                extension.DragListener.SimulateMove(new Windows.Foundation.Point(180, 20)); // would have crossed into TargetCanvas
                extension.DragListener.RaisePointerCaptureLostForTest();
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, buttonItem.Parent,
                    "a capture loss mid-drag must abort, not commit a half-finished cross-container move");
                Assert.AreEqual(canUndoBefore, undoService.CanUndo);
            });
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL to compile — `TopLeftContainerDragHandle` doesn't exist.

- [ ] **Step 3: Implement MoveLogic, ContainerDragHandle, and TopLeftContainerDragHandle**

  Create `src/WinUIDesigner.Designer/Controls/MoveLogic.cs`:

```csharp
using System;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Controls
{
    /// <summary>
    /// Drives a single-item move gesture: opens a PlacementOperation, switches container mid-
    /// drag via hit-testing when the pointer crosses into a different container's bounds, and
    /// updates PlacementInformation.Bounds from the drag delta every tick. Ported from the
    /// reference's Services/MoveLogic.cs, narrowed to a single item (no multi-selection sort
    /// via ModelTools.ComparePositionInModelFile - not ported, no 4b consumer) and with no
    /// double-click-to-create-event-handler behavior (IEventHandlerService doesn't exist in
    /// this port). Driven by DragListener (window-coordinate points) instead of the
    /// reference's DesignPanel-relative MouseGestureBase - every point this class receives is
    /// converted from window space directly via TransformToVisual(null).Inverse, the same
    /// pattern DragListener.GetPosition already uses, rather than routing through an
    /// intermediate "DesignPanel-relative" space the way the reference does.
    /// </summary>
    internal sealed class MoveLogic
    {
        readonly DesignItem clickedOn;
        PlacementOperation operation;
        Point startWindowPoint;

        public MoveLogic(DesignItem clickedOn)
        {
            this.clickedOn = clickedOn ?? throw new ArgumentNullException(nameof(clickedOn));
        }

        public void Start(Point windowPoint)
        {
            startWindowPoint = windowPoint;
            var items = new[] { clickedOn };
            var behavior = PlacementOperation.GetPlacementBehavior(items);
            if (behavior != null && behavior.CanPlace(items, PlacementType.Move, PlacementAlignment.TopLeft))
                operation = PlacementOperation.Start(items, PlacementType.Move);
        }

        public void Move(Point windowPoint, VirtualKeyModifiers modifiers)
        {
            if (operation == null) return;

            if (operation.CurrentContainerBehavior.CanLeaveContainer(operation))
                ChangeContainerIfPossible(windowPoint);

            double dx, dy;
            var container = operation.CurrentContainer.View;
            var inverse = container != null ? container.TransformToVisual(null).Inverse : null;
            if (inverse != null) {
                var p = inverse.TransformPoint(windowPoint);
                var s = inverse.TransformPoint(startWindowPoint);
                dx = p.X - s.X;
                dy = p.Y - s.Y;
            } else {
                dx = windowPoint.X - startWindowPoint.X;
                dy = windowPoint.Y - startWindowPoint.Y;
            }

            foreach (var info in operation.PlacedItems) {
                info.Bounds = new Rect(
                    info.OriginalBounds.X + Math.Round(dx, PlacementInformation.BoundsPrecision),
                    info.OriginalBounds.Y + Math.Round(dy, PlacementInformation.BoundsPrecision),
                    info.OriginalBounds.Width,
                    info.OriginalBounds.Height);
            }
            operation.CurrentContainerBehavior.BeforeSetPosition(operation, modifiers);
            foreach (var info in operation.PlacedItems)
                operation.CurrentContainerBehavior.SetPosition(info);
        }

        public void Stop()
        {
            if (operation == null) return;
            operation.Commit();
            operation = null;
        }

        public void Cancel()
        {
            if (operation == null) return;
            operation.Abort();
            operation = null;
        }

        bool ChangeContainerIfPossible(Point windowPoint)
        {
            var result = HitTestUnselectedModel(windowPoint);
            if (result.ModelHit == null) return false;
            if (result.ModelHit == operation.CurrentContainer) return false;

            var tmp = result.ModelHit;
            while (tmp != null) {
                if (tmp == clickedOn) return false;
                tmp = tmp.Parent;
            }

            var behavior = result.ModelHit.GetBehavior<IPlacementBehavior>();
            if (behavior != null && behavior.CanEnterContainer(operation, false)) {
                operation.ChangeContainer(result.ModelHit);
                return true;
            }
            return false;
        }

        DesignPanelHitTestResult HitTestUnselectedModel(Point windowPoint)
        {
            var result = DesignPanelHitTestResult.NoHit;
            var services = clickedOn.Services;
            var selection = services.Selection;
            var designPanel = services.DesignPanel;
            var designPanelElement = designPanel as UIElement;

            var panelPoint = windowPoint;
            if (designPanelElement != null) {
                var inverse = designPanelElement.TransformToVisual(null).Inverse;
                if (inverse != null) panelPoint = inverse.TransformPoint(windowPoint);
            }

            designPanel.HitTest(panelPoint, false, true, r => {
                if (r.ModelHit == null) return true;
                if (selection.IsComponentSelected(r.ModelHit)) return true;
                result = r;
                return false;
            }, HitTestType.Default);

            return result;
        }
    }
}
```

  Create `src/WinUIDesigner.Designer/Controls/ContainerDragHandle.cs` (small
  fixed-size visual, same "code-only, no generic.xaml" idiom as
  `Controls/Thumbs/DesignerThumb.cs`):

```csharp
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinUIDesigner.Designer.Controls
{
    /// <summary>Small square move-handle visual for TopLeftContainerDragHandle, positioned at
    /// a selected element's top-left corner. Grid-based (not Control), same idiom as
    /// DesignerThumb - code-only appearance, no generic.xaml.</summary>
    public sealed class ContainerDragHandle : Grid
    {
        public ContainerDragHandle()
        {
            Width = 7;
            Height = 7;
            Background = new SolidColorBrush(Colors.White);
            BorderBrush = new SolidColorBrush(Colors.Black);
            BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
        }
    }
}
```

  Create `src/WinUIDesigner.Designer/Extensions/TopLeftContainerDragHandle.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// The move-drag handle shown at the top-left corner of any selected FrameworkElement.
    /// Ported from the reference's TopLeftContainerDragHandle.cs, rebuilt on DragListener
    /// (already extended with PointerCaptureLost handling, Task 2) instead of the reference's
    /// ContainerDragHandle+DragMoveMouseGesture stack - same choice Phase 4a made for
    /// DragListener itself (a from-scratch WinUI replacement, not a port of anything
    /// WPF-mouse-specific).
    /// </summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(FrameworkElement))]
    public class TopLeftContainerDragHandle : AdornerProvider
    {
        internal readonly DragListener DragListener;
        MoveLogic moveLogic;

        public TopLeftContainerDragHandle()
        {
            var handle = new ContainerDragHandle();
            DragListener = new DragListener(handle);
            DragListener.Started += Drag_Started;
            DragListener.Changed += Drag_Changed;
            DragListener.Completed += Drag_Completed;

            var placement = new RelativePlacement(HorizontalAlignment.Left, VerticalAlignment.Top) {
                XOffset = -7,
                YOffset = -7
            };
            AddAdorner(placement, AdornerOrder.Background, handle);
        }

        void Drag_Started(DragListener drag)
        {
            moveLogic = new MoveLogic(ExtendedItem);
            moveLogic.Start(drag.StartPoint);
        }

        void Drag_Changed(DragListener drag)
        {
            moveLogic?.Move(drag.CurrentPoint, drag.KeyModifiers);
        }

        void Drag_Completed(DragListener drag)
        {
            if (moveLogic == null) return;
            if (drag.IsCanceled) moveLogic.Cancel();
            else moveLogic.Stop();
            moveLogic = null;
        }
    }
}
```

  > `AdornerOrder`/`RelativePlacement`/`AdornerProvider.AddAdorner` are all
  > already in the port (Phase 2/3/4a) — verify the exact `using` namespace
  > for `AdornerOrder` if the build complains (it lives alongside
  > `AdornerPanel` in `WinUIDesigner.DesignModel.Adorners`).

- [ ] **Step 4: Run tests to verify they pass**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj` then run the built `.exe` filtered with `--test=WinUIDesigner.Designer.Tests.MoveTests`.
  Expected: PASS, all 6 tests. If
  `DraggingTheHandleAcrossContainersReparentsTheItemWithOneUndoRevertingBoth`
  fails specifically on which container the hit-test landed in (not a
  crash, just the wrong container), adjust the test's simulated window
  coordinates to match the actually-observed layout — do not change
  `MoveLogic`'s hit-testing logic to work around a coordinate assumption
  that turned out wrong; fix the test's assumption instead, per Known
  gap #1's own "must be verified empirically" framing.

- [ ] **Step 5: Re-run Phase 1-4a and Phase 4b suites so far, then commit**

  ```bash
  git add src/WinUIDesigner.Designer/Controls/MoveLogic.cs \
          src/WinUIDesigner.Designer/Controls/ContainerDragHandle.cs \
          src/WinUIDesigner.Designer/Extensions/TopLeftContainerDragHandle.cs \
          tests/WinUIDesigner.Designer.Tests/MoveTests.cs
  git commit -m "feat: add MoveLogic and TopLeftContainerDragHandle (Phase 4b Task 14)"
  ```

---

### Task 15: PanelMove.cs

**Files:**
- Create: `src/WinUIDesigner.Designer/Controls/PanelMoveAdorner.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/PanelMove.cs`
- Test: Modify `tests/WinUIDesigner.Designer.Tests/MoveTests.cs`

**Interfaces:**
- Consumes: `MoveLogic` (Task 14, reused as-is — no changes to that
  class), `DragListener` (Task 2), `DefaultExtensionServer.
  PermanentWithDesignPanel` (Task 3).
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Write the failing test**

  Reference behavior, confirmed by reading `MoveLogic`'s constructor and
  `DragMoveMouseGesture`'s call site in full (`Services/MoveLogic.cs`,
  `Controls/PanelMoveAdorner.cs`): `PanelMoveAdorner` is constructed with
  the *container* (`ExtendedItem`, from `PanelMove.OnInitialized`), and
  `MoveLogic`'s constructor computes its working set from `clickedOn`
  filtered against the *current selection* — if the container passed in
  isn't itself the current selection, the working set is empty and the
  whole gesture becomes a no-op (`PlacementOperation.GetPlacementBehavior`
  returns `null` for zero items, so `operation` never starts). So
  `PanelMove` moves the **container itself** (relative to *its own*
  parent), and only when the container is the thing currently selected —
  it is not a way to move whatever child happens to be selected inside
  it. Task 14's simplified `MoveLogic` doesn't reconstruct that
  selection-filtering (single-item scope, per the spec), so `PanelMove`
  replicates just the one piece of it that actually matters here: only
  start a move when `ExtendedItem` is the primary selection.

  Add to `tests/WinUIDesigner.Designer.Tests/MoveTests.cs`:

```csharp
        const string NestedCanvasXaml =
            "<Canvas x:Name=\"RootCanvas\" Width=\"300\" Height=\"300\" " +
            "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Canvas x:Name=\"InnerCanvas\" Width=\"100\" Height=\"100\" Canvas.Left=\"10\" Canvas.Top=\"10\">" +
            "<Button x:Name=\"Target\" Width=\"20\" Height=\"20\" />" +
            "</Canvas></Canvas>";

        [Test]
        public void ClickingAndDraggingAnUnselectedContainersBodyDoesNothing()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(NestedCanvasXaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var rootCanvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                var innerCanvas = (Microsoft.UI.Xaml.Controls.Canvas)rootCanvas.Children[0];
                var services = surface.DesignContext.Services;
                var innerItem = services.Component.GetDesignItem(innerCanvas);
                // Nothing selected - InnerCanvas is not the primary selection.
                var extension = innerItem.Extensions.OfType<PanelMove>().Single();

                extension.DragListener.SimulateDown(new Windows.Foundation.Point(150, 150));
                extension.DragListener.SimulateMove(new Windows.Foundation.Point(170, 165));
                extension.DragListener.SimulateUp(new Windows.Foundation.Point(170, 165));
                surface.UpdateLayout();

                var left = innerItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>();
                Assert.AreEqual(10, left, 0.5, "dragging an unselected container's body must not move it");
            });
        }

        [Test]
        public void ClickingAndDraggingASelectedContainersOwnBodyMovesTheContainerItself()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(NestedCanvasXaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var rootCanvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                var innerCanvas = (Microsoft.UI.Xaml.Controls.Canvas)rootCanvas.Children[0];
                var services = surface.DesignContext.Services;
                var innerItem = services.Component.GetDesignItem(innerCanvas);
                services.Selection.SetSelectedComponents(new[] { innerItem }, SelectionTypes.Primary);
                surface.UpdateLayout();
                var extension = innerItem.Extensions.OfType<PanelMove>().Single();

                extension.DragListener.SimulateDown(new Windows.Foundation.Point(150, 150));
                extension.DragListener.SimulateMove(new Windows.Foundation.Point(170, 165)); // +20,+15
                extension.DragListener.SimulateUp(new Windows.Foundation.Point(170, 165));
                surface.UpdateLayout();

                var left = innerItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>();
                var top = innerItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>();
                Assert.AreEqual(30, left, 0.5);
                Assert.AreEqual(25, top, 0.5);
            });
        }
```

- [ ] **Step 2: Run test to verify it fails**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL to compile — `PanelMove` doesn't exist.

- [ ] **Step 3: Implement PanelMoveAdorner and PanelMove**

  Create `src/WinUIDesigner.Designer/Controls/PanelMoveAdorner.cs`:

```csharp
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinUIDesigner.Designer.Controls
{
    /// <summary>Transparent full-fill overlay making a container's own body draggable to move
    /// its selected child, without needing to hit TopLeftContainerDragHandle's small corner
    /// handle. Background must be a real (if transparent) brush, not null, for WinUI hit-
    /// testing to reach this element at all. Ported from the reference's
    /// Controls/PanelMoveAdorner.cs with its ZoomControl-driven ScaleTransform/LayoutTransform
    /// binding dropped (LayoutTransform doesn't exist in WinUI - confirmed absent since Phase
    /// 3 - and no ZoomControl exists anywhere in this port) and its IsVisible-binding dropped
    /// (a visibility nicety with no consumer here) - both out of scope, not stubbed.</summary>
    public sealed class PanelMoveAdorner : Grid
    {
        public PanelMoveAdorner()
        {
            Background = new SolidColorBrush(Colors.Transparent);
        }
    }
}
```

  Create `src/WinUIDesigner.Designer/Extensions/PanelMove.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// Makes a container's own body draggable to move its selected child - a bigger, alternate
    /// trigger surface for the exact same MoveLogic-driven move TopLeftContainerDragHandle
    /// (Task 14) provides via its corner handle. Ported from the reference's PanelMove.cs. The
    /// reference bases on the abstract PermanentAdornerProvider - not ported, same precedent
    /// as Phase 4a's SelectionAdornerProvider abstraction (skipping an empty intermediate
    /// class that exists only to carry one attribute changes nothing at runtime): this class
    /// subclasses AdornerProvider directly and carries the ExtensionServer attribute itself.
    /// </summary>
    [ExtensionServer(typeof(DefaultExtensionServer.PermanentWithDesignPanel))]
    [ExtensionFor(typeof(Panel))]
    [ExtensionFor(typeof(Border))]
    [ExtensionFor(typeof(ContentControl))]
    [ExtensionFor(typeof(Viewbox))]
    public class PanelMove : AdornerProvider
    {
        internal readonly DragListener DragListener;
        MoveLogic moveLogic;

        public PanelMove()
        {
            var overlay = new PanelMoveAdorner();
            DragListener = new DragListener(overlay);
            DragListener.Started += Drag_Started;
            DragListener.Changed += Drag_Changed;
            DragListener.Completed += Drag_Completed;

            AddAdorner(AdornerPlacement.FillContent, AdornerOrder.Background, overlay);
        }

        void Drag_Started(DragListener drag)
        {
            // The reference's MoveLogic constructor filters its working set down to "clickedOn,
            // if clickedOn is currently selected" (multi-selection machinery this port doesn't
            // reconstruct - see MoveLogic's doc comment, Task 14) - otherwise the gesture is a
            // no-op. This is the one piece of that filtering that still matters for a
            // single-item MoveLogic: PanelMove moves the container itself, only when the
            // container is the thing currently selected (not whatever child happens to be
            // selected inside it).
            if (!ReferenceEquals(Services.Selection.PrimarySelection, ExtendedItem)) return;
            moveLogic = new MoveLogic(ExtendedItem);
            moveLogic.Start(drag.StartPoint);
        }

        void Drag_Changed(DragListener drag)
        {
            moveLogic?.Move(drag.CurrentPoint, drag.KeyModifiers);
        }

        void Drag_Completed(DragListener drag)
        {
            if (moveLogic == null) return;
            if (drag.IsCanceled) moveLogic.Cancel();
            else moveLogic.Stop();
            moveLogic = null;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj` then run the built `.exe` filtered with `--test=WinUIDesigner.Designer.Tests.MoveTests`.
  Expected: PASS, all tests in the file (the new one plus every `MoveTests` test from Task 14, unaffected).

- [ ] **Step 5: Re-run Phase 1-4a and Phase 4b suites so far, then commit**

  ```bash
  git add src/WinUIDesigner.Designer/Controls/PanelMoveAdorner.cs \
          src/WinUIDesigner.Designer/Extensions/PanelMove.cs \
          tests/WinUIDesigner.Designer.Tests/MoveTests.cs
  git commit -m "feat: add PanelMove (Phase 4b Task 15)"
  ```

---

### Task 16: Phase 4b Definition-of-Done suite

**Files:**
- Test: Create `tests/WinUIDesigner.Designer.Tests/Phase4bDefinitionOfDoneTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-15. This task adds no production code — it is the closing
  cross-task integration gate, matching Phase 4a's own `Phase4aDefinitionOfDoneTests.cs`.
- Produces: nothing later tasks depend on (there are none after this one).

Every one of this spec's Known gaps already has a concrete resolution landed by an earlier task
(not deferred to here):

| Gap | Resolved by | How |
| --- | --- | --- |
| #1 — `LeaveContainer`→`EnterContainer` via `ChangeContainer` | Task 1 (mechanism), Task 14 (first end-to-end exercise via a real drag) | Direct tests + the cross-container test in this task |
| #2 — `GrayOutDesignerExceptActiveArea` visual correctness | Task 8, Step 6 | Human eyeball check (not unit-testable) |
| #3 — Grid layout timing after row/column creation | Task 11 | `grid.UpdateLayout()` forced explicitly, no `DispatcherPriority.Loaded` substitute needed |
| #4 — `GridAdorner` visual/interaction composition | Task 11 | Concrete, verified implementation (rails, captions, splitters) |
| #5 — Ctrl-modifier threading into `IPlacementBehavior` | Task 4 | Interface change + `ResizeThumbExtension`/`MoveLogic` call sites |
| #6 — `OptionService` scope | Task 4 | Both reference fields ported (`SnaplinePlacementRoundValues`, `GrayOutDesignSurfaceExceptParentContainerWhenDragging`) |

This task's own job is narrower: exercise the handful of behaviors that only show up when two or
more tasks interact, which no single task's tests cover, plus a reflection-based check that every
gap above really does have a resolution to point at (matching 4a's own
`AllFourFlaggedGapsHaveATestedResolutionElsewhereInThisSuite` pattern).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class Phase4bDefinitionOfDoneTests
    {
        [Test]
        public void DraggingAnItemFromACanvasIntoAGridUpdatesGridSpecificPositionOnEntry()
        {
            const string Xaml =
                "<StackPanel Orientation=\"Horizontal\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Canvas x:Name=\"SourceCanvas\" Width=\"150\" Height=\"150\">" +
                "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" Canvas.Left=\"10\" Canvas.Top=\"10\" />" +
                "</Canvas>" +
                "<Grid x:Name=\"TargetGrid\" Width=\"150\" Height=\"150\">" +
                "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
                "<Grid.ColumnDefinitions><ColumnDefinition /><ColumnDefinition /></Grid.ColumnDefinitions>" +
                "</Grid>" +
                "</StackPanel>";

            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(Xaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var root = (Microsoft.UI.Xaml.Controls.StackPanel)surface.DesignContext.RootItem.Component;
                var sourceCanvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
                var targetGrid = (Microsoft.UI.Xaml.Controls.Grid)root.Children[1];
                var button = (Microsoft.UI.Xaml.Controls.Button)sourceCanvas.Children[0];

                var services = surface.DesignContext.Services;
                var buttonItem = services.Component.GetDesignItem(button);
                var targetItem = services.Component.GetDesignItem(targetGrid);
                services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
                surface.UpdateLayout();

                var extension = buttonItem.Extensions.OfType<TopLeftContainerDragHandle>().Single();
                extension.DragListener.SimulateDown(new Windows.Foundation.Point(20, 20));
                // Past x=150 (SourceCanvas's width), into TargetGrid's second column.
                extension.DragListener.SimulateMove(new Windows.Foundation.Point(200, 90));
                extension.DragListener.SimulateUp(new Windows.Foundation.Point(200, 90));
                surface.UpdateLayout();

                Assert.AreSame(targetItem, buttonItem.Parent,
                    "the item must have reparented from SourceCanvas to TargetGrid (Known gap #1, exercised end to end)");
                // GridPlacementSupport.EnterContainer sets enteredIntoNewContainer, so SetPosition
                // suggests Grid.Row/Column instead of preserving Canvas-flavored placement.
                Assert.AreEqual(1, Microsoft.UI.Xaml.Controls.Grid.GetColumn(button),
                    "on entry into a Grid, GridPlacementSupport must resolve a real Grid.Column, not leave Canvas semantics behind");

                var undo = services.GetService<UndoService>();
                Assert.IsTrue(undo.CanUndo);
                undo.Undo();
                surface.UpdateLayout();
                Assert.AreSame(services.Component.GetDesignItem(sourceCanvas), buttonItem.Parent,
                    "a single undo must revert the cross-container move, including the Grid-side Column write");
            });
        }

        [Test]
        public void SnaplinesAndRasterBothApplyDuringACanvasMoveNotJustAResize()
        {
            const string Xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
                "<Button x:Name=\"A\" Width=\"40\" Height=\"20\" Canvas.Left=\"150\" Canvas.Top=\"150\" />" +
                "<Button x:Name=\"B\" Width=\"40\" Height=\"20\" Canvas.Left=\"0\" Canvas.Top=\"0\" />" +
                "</Canvas>";

            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(Xaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                var b = (Microsoft.UI.Xaml.Controls.Button)canvas.Children[1];
                var services = surface.DesignContext.Services;
                var bItem = services.Component.GetDesignItem(b);
                services.Selection.SetSelectedComponents(new[] { bItem }, SelectionTypes.Primary);
                surface.UpdateLayout();

                Assert.IsTrue(surface.DesignPanel.UseSnaplinePlacement, "snaplines are on by default");

                var op = PlacementOperation.Start(new[] { bItem }, PlacementType.Move);
                try {
                    op.PlacedItems[0].Bounds = new Windows.Foundation.Rect(152, 300, 40, 20);
                    op.CurrentContainerBehavior.BeforeSetPosition(op, Windows.System.VirtualKeyModifiers.None);
                    Assert.AreEqual(150, op.PlacedItems[0].Bounds.X, 0.5,
                        "CanvasPlacementSupport chains through SnaplinePlacementBehavior - a move (not just a resize) snaps to sibling A's left edge");
                } finally {
                    op.Abort();
                }
            });
        }

        [Test]
        public void EachKnownGapHasAConcreteResolutionElsewhereInThisPlan()
        {
            // Reflection-based, same pattern as Phase 4a's DoD suite: point at the actual type/
            // member each resolution landed, so this test breaks (rather than silently passing)
            // if a later refactor removes the resolution without updating this table.
            Assert.IsNotNull(typeof(PlacementOperation).GetMethod("ChangeContainer"),
                "gap #1: PlacementOperation.ChangeContainer (Task 1)");
            Assert.IsNotNull(typeof(GrayOutDesignerExceptActiveArea).GetMethod("AnimateActiveAreaRectTo"),
                "gap #2: GrayOutDesignerExceptActiveArea.AnimateActiveAreaRectTo exists and compiles (Task 8) - visual correctness itself is human-eyeballed, see Task 8 Step 6");
            Assert.IsNotNull(typeof(GridAdornerProvider).GetMethod("FlushPendingRebuild", BindingFlags.NonPublic | BindingFlags.Instance),
                "gap #3: GridAdornerProvider forces grid.UpdateLayout() instead of a DispatcherPriority.Loaded substitute (Task 11)");
            Assert.IsNotNull(typeof(GridRailAdorner).GetMethod("RefreshLabels"),
                "gap #4: GridAdorner's rail/splitter/unit-selector composition is concrete and tested (Task 11-13)");
            var beforeSetPosition = typeof(IPlacementBehavior).GetMethod("BeforeSetPosition");
            Assert.AreEqual(2, beforeSetPosition.GetParameters().Length,
                "gap #5: IPlacementBehavior.BeforeSetPosition carries the VirtualKeyModifiers parameter (Task 4)");
            Assert.IsNotNull(typeof(WinUIDesigner.Designer.Services.OptionService)
                .GetField("GrayOutDesignSurfaceExceptParentContainerWhenDragging"),
                "gap #6: OptionService ports both reference fields, not a narrowed subset (Task 4)");
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: PASS to compile and mostly PASS to run, since every member this suite touches was
  already implemented by Tasks 1-15 — this task should need no new production code. If any
  assertion fails, that's a real integration gap between two tasks that individually passed their
  own tests; fix the interaction (in whichever task's file is actually wrong), not this test.

- [ ] **Step 3: (no new implementation — integration-only task)**

  If Step 2 finds a genuine cross-task gap (e.g. `GridPlacementSupport`'s entry-alignment logic
  doesn't actually fire when reached via `ChangeContainer` rather than a direct `EnterContainer`
  call), fix it in the task whose file owns that behavior, re-run that task's own test suite to
  confirm no regression, then re-run this task's suite.

- [ ] **Step 4: Run tests to verify they pass**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj` then run the built `.exe` filtered with `--test=WinUIDesigner.Designer.Tests.Phase4bDefinitionOfDoneTests`.
  Expected: PASS, all 3 tests.

  Then re-run **every** `.exe` from every phase unfiltered (`WinUIDesigner.XamlDom.Tests`,
  `WinUIDesigner.DesignModel.Tests`, `WinUIDesigner.Designer.Tests`) — Phase 4b's full test count
  by this point is roughly 90+ new tests across Tasks 1-16, on top of Phase 1-4a's existing suites.
  All must be green.

- [ ] **Step 5: Commit**

```bash
git add tests/WinUIDesigner.Designer.Tests/Phase4bDefinitionOfDoneTests.cs
git commit -m "test: add Phase 4b Definition-of-Done suite (Phase 4b Task 16)"
```

---
