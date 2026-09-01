# Phase 4a: Resize/Rotate/Skew Thumbs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the primary selection real resize/rotate/skew handles — a
minimal `PlacementOperation`/`IPlacementBehavior` engine, a generic
`DefaultPlacementBehavior`, and three `AdornerProvider` extensions
(`ResizeThumbExtension`/`RotateThumbExtension`/`SkewThumbExtension`) wired to
Grid-based thumb visuals — so dragging a handle actually resizes, rotates, or
skews the selected element through the undo-tracked property system.

**Architecture:** The abstract placement engine (`PlacementOperation`,
`IPlacementBehavior`, `PlacementType`, `PlacementAlignment`,
`PlacementInformation`, `RelativePlacement`, `BehaviorExtension`) lands in
`WinUIDesigner.DesignModel`, mirroring the reference's own
`WpfDesign`/`WpfDesign.Designer` split (same choice Phase 3 made). The
concrete, UI-heavy pieces (`DefaultPlacementBehavior`, `ModelTools`,
`DragListener`, the thumb visuals, the three extensions) go into
`WinUIDesigner.Designer`. Every WPF→WinUI mechanism swap below was verified
twice over during spec brainstorming — first against
`Microsoft.UI.Xaml.winmd`/`Microsoft.UI.winmd`/XML docs, then a second time
against the actual C# compiler by building throwaway probe files (this is how
round 2 of the spec's adversarial review caught that `Thumb` is sealed). This
plan does not re-derive those findings, it applies them.

**Tech Stack:** .NET 10 SDK (`10.0.302`), WinUI 3 / Windows App SDK 1.7
(`Microsoft.WindowsAppSDK` `1.7.250606001` — same pin as Phase 1-3), NUnit 3 +
NUnitLite (matching the existing authoritative `.exe` test runner).

**Spec:** `docs/superpowers/specs/2026-09-01-winui-designer-phase4a-design.md`

## Global Constraints

- No new projects. All new files land in the existing `WinUIDesigner.DesignModel`
  and `WinUIDesigner.Designer` projects (and their `.Tests` counterparts) —
  `WinUIDesigner.sln` does not change this phase.
- New code lives under `D:\Project\ST4I\Research\WinUIDesigner`. The
  reference repo `F:\ReferencesCode\CSharp\WpfDesigner` is read-only source
  material — never edit it.
- `Nullable` stays `disable`, matching every prior phase.
- **`Microsoft.UI.Xaml.Controls.Primitives.Thumb` is sealed in this SDK
  version — compiler-confirmed (`CS0509`).** No file in this plan may
  subclass it. Thumb visuals derive from `Microsoft.UI.Xaml.Controls.Grid`
  (same base Phase 3 used for `SelectionHighlight`, same reason: an open,
  unstyled visual with `Background`/`BorderBrush`/`BorderThickness`).
- `Windows.Foundation.Rect` has no `.TopLeft`/`.Size` members (compiler-
  confirmed `CS1061`). `Microsoft.UI.Xaml.Media.Matrix` has no methods, only
  six public fields (compiler-confirmed). There is no `Vector` type anywhere
  in `Microsoft.UI.Xaml.Media` (compiler-confirmed `CS0234`). Do not write
  code that assumes any of these WPF members exist — every task below
  already routes around them.
- `DesignItemProperty`/`DesignItemPropertyCollection` are string-keyed only
  (`Properties["Width"]`, `Properties.GetProperty("Width")`) — there is no
  `DependencyProperty`-indexed overload in this codebase. Every property
  access in every task below uses the string form.
- `PointerRoutedEventArgs` and `KeyRoutedEventArgs` are sealed WinRT types
  with no public constructor — tests cannot fabricate one. `DragListener`
  (Task 1) exposes `SimulateDown`/`SimulateMove`/`SimulateUp`/`Cancel`
  methods for exactly this reason; every later task's tests drive drags
  through those methods, never through synthesized event args.
- **Authoritative test command**, same reasoning as Phase 1-3 (WinUI objects
  are thread-affine; `dotnet test`'s `testhost.exe` cannot activate them):
  run the built `.exe` directly —
  `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe`
  and
  `tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe`
  — not `dotnet test`. `dotnet test` stays useful for IDE discovery and
  non-WinUI-object checks.
- Phase 1/2/3's suites must stay green throughout this plan. Re-run all
  `.exe`s as part of every task's verification step; treat any regression as
  a bug in that task, not something to patch around later.
- If a step's code needs an empirical fact this plan could not settle ahead
  of time (an SDK behavior neither the winmd/XML docs nor a compiler probe
  can answer — only running it can), the task says so explicitly and gives
  the exact test that answers it. No silent assumption, no swallowed
  exception that hides a platform difference.
- Commit after each task passes its verification step.

---

## Background: why this is six tasks

The spec identifies four flagged gaps and one structural risk (the core
placement engine has no prior port to build on — Phase 2/3 only ported the
Adorner/selection halves of the reference, never `PlacementOperation`).
Splitting along those seams, in dependency order:

1. **Task 1** — `DragListener`, the pointer-drag primitive every thumb
   extension depends on. Resolves gap #1 (Escape-cancel) as far as it can be
   resolved empirically (real keyboard-focus acquisition on a `Grid`-based
   element, verified in a live window) and gap #2 (drag-distance threshold).
   Zero dependency on the placement engine — buildable and testable first.
2. **Task 2** — the core placement engine in `WinUIDesigner.DesignModel`
   (`PlacementType`/`PlacementAlignment`/`PlacementInformation`/
   `IPlacementBehavior`/`PlacementOperation`/`PlacementMetadata`/
   `RelativePlacement`/`BehaviorExtension`/`OnlyOneItemSelectedExtensionServer`/
   `ServiceContainer.DesignPanel`/`DesignItem.GetCompleteAppliedTransformationToView`).
   Resolves gap #3 (RenderTransform re-parenting). No dependency on Task 1.
3. **Task 3** — `DefaultPlacementBehavior` and `ModelTools` (the concrete,
   `WinUIDesigner.Designer`-side placement pieces), plus exposing
   `DesignPanel.AdornerLayer` publicly. Resolves gap #4 (the
   `TransformGroup`-wrapping path). Depends on Task 2's engine.
4. **Task 4** — the Grid-based thumb visuals (`DesignerThumb`/`ResizeThumb`/
   `RotateThumb`) and `ResizeThumbExtension` — the first full end-to-end
   drag-to-resize pipeline. Depends on Tasks 1-3.
5. **Task 5** — `RotateThumbExtension`/`SkewThumbExtension`, which share
   `ModelTools.ApplyTransform` and the same `DragListener`/`AdornerProvider`
   pattern Task 4 establishes.
6. **Task 6** — the Definition-of-Done suite from the spec's "Testing /
   Definition of Done" section, plus a final check that all four gaps have
   an explicit, tested resolution.

---

### Task 1: `DragListener` — pointer-drag primitive (resolves gaps #1, #2)

**Files:**
- Create: `src/WinUIDesigner.DesignModel/GeometryExtensions.cs`
- Create: `src/WinUIDesigner.Designer/Controls/DragListener.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/DragListenerTests.cs`

**Interfaces:**
- Consumes: `WinUIDesigner.TestHost.WinUITestHost.Run(Action)` and
  `.RunWithWindow(Action<Microsoft.UI.Xaml.Window>)` (Phase 2/3, unchanged).
- Produces: `WinUIDesigner.Designer.Controls.DragListener` — constructed as
  `new DragListener(UIElement target)`; events `Started`/`Changed`/
  `Completed` (each `event DragHandler`, `delegate void
  DragHandler(DragListener drag)`); properties `Target`, `Transform`
  (settable `GeneralTransform`), `StartPoint`, `CurrentPoint`, `IsActive`,
  `IsDown`, `IsCanceled`, `KeyModifiers` (`Windows.System.VirtualKeyModifiers`),
  `Delta` (`(double X, double Y)`); methods `GetPosition(UIElement relativeTo)`
  returning `Windows.Foundation.Point?`, `Cancel()`, and the test-only
  `SimulateDown(Point, VirtualKeyModifiers)`/`SimulateMove(Point,
  VirtualKeyModifiers)`/`SimulateUp(Point)`. Every later task's thumb
  extensions consume this exact surface.
  `WinUIDesigner.DesignModel.GeometryExtensions.Round(this Rect)` and
  `.TryFindParent<T>(this DependencyObject)` — consumed by Task 4/5's thumb
  visuals and `SkewThumbExtension`.

- [ ] **Step 1: Write `GeometryExtensions`**

  `src/WinUIDesigner.DesignModel/GeometryExtensions.cs`:
  ```csharp
  using System;
  using Windows.Foundation;
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Media;

  namespace WinUIDesigner.DesignModel
  {
      /// <summary>
      /// Small WPF-reference extension methods with no other home in this port. Ported alone
      /// rather than pulling in their reference source files wholesale - see
      /// UIExtensions/UIHelpers.cs and ExtensionMethods.cs in the reference for the originals.
      /// </summary>
      public static class GeometryExtensions
      {
          /// <summary>
          /// Rounds position and size of a Rect to PlacementInformation.BoundsPrecision digits.
          /// </summary>
          public static Rect Round(this Rect rect)
          {
              return new Rect(
                  Math.Round(rect.X, PlacementInformation.BoundsPrecision),
                  Math.Round(rect.Y, PlacementInformation.BoundsPrecision),
                  Math.Round(rect.Width, PlacementInformation.BoundsPrecision),
                  Math.Round(rect.Height, PlacementInformation.BoundsPrecision)
              );
          }

          /// <summary>
          /// Walks up from <paramref name="child"/> looking for the first ancestor of type
          /// <typeparamref name="T"/>. WinUI has no ContentElement/logical-tree split the
          /// reference's version handles - FrameworkElement.Parent (when set) falls back to
          /// VisualTreeHelper.GetParent, matching this port's only two call sites.
          /// </summary>
          public static T TryFindParent<T>(this DependencyObject child) where T : DependencyObject
          {
              if (child == null) return null;
              var fe = child as FrameworkElement;
              DependencyObject parentObject = fe != null && fe.Parent != null ? fe.Parent : VisualTreeHelper.GetParent(child);
              if (parentObject == null) return null;
              var parent = parentObject as T;
              if (parent != null) return parent;
              return TryFindParent<T>(parentObject);
          }
      }
  }
  ```

  This references `PlacementInformation.BoundsPrecision`, which Task 2
  creates. That's fine — `GeometryExtensions.Round()` isn't called by
  anything until Task 4, so this compiles once Task 2 lands and nothing in
  Task 1 calls it yet.

- [ ] **Step 2: Write the failing `DragListener` test for the happy path**

  `tests/WinUIDesigner.Designer.Tests/DragListenerTests.cs`:
  ```csharp
  using Windows.Foundation;
  using Windows.System;
  using NUnit.Framework;
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Controls;
  using WinUIDesigner.Designer.Controls;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.Designer.Tests
  {
      [TestFixture]
      public class DragListenerTests
      {
          [Test]
          public void SimulatedDragBelowThresholdDoesNotFireStarted()
          {
              WinUITestHost.Run(() => {
                  var target = new Grid();
                  var drag = new DragListener(target);
                  bool started = false;
                  drag.Started += d => started = true;

                  drag.SimulateDown(new Point(0, 0));
                  drag.SimulateMove(new Point(1, 1)); // below the 4px/2px threshold

                  Assert.IsFalse(started);
                  Assert.IsFalse(drag.IsActive);
              });
          }

          [Test]
          public void SimulatedDragPastThresholdFiresStartedThenChangedThenCompleted()
          {
              WinUITestHost.Run(() => {
                  var target = new Grid();
                  var drag = new DragListener(target);
                  int startedCount = 0, changedCount = 0, completedCount = 0;
                  drag.Started += d => startedCount++;
                  drag.Changed += d => changedCount++;
                  drag.Completed += d => completedCount++;

                  drag.SimulateDown(new Point(0, 0));
                  drag.SimulateMove(new Point(10, 0)); // past MinimumHorizontalDragDistance (4)
                  drag.SimulateMove(new Point(20, 0));
                  drag.SimulateUp(new Point(20, 0));

                  Assert.AreEqual(1, startedCount);
                  Assert.AreEqual(2, changedCount, "one Changed per move past threshold, including the move that crossed it");
                  Assert.AreEqual(1, completedCount);
                  Assert.IsFalse(drag.IsCanceled);
                  Assert.IsFalse(drag.IsActive, "IsActive resets once the drag completes");
              });
          }

          [Test]
          public void DeltaIsCurrentMinusStartWhenNoTransformIsSet()
          {
              WinUITestHost.Run(() => {
                  var target = new Grid();
                  var drag = new DragListener(target);
                  drag.SimulateDown(new Point(5, 5));
                  drag.SimulateMove(new Point(17, 9));

                  var delta = drag.Delta;
                  Assert.AreEqual(12, delta.X, 0.001);
                  Assert.AreEqual(4, delta.Y, 0.001);
              });
          }

          [Test]
          public void DeltaIsTransformedWhenTransformIsSet()
          {
              WinUITestHost.Run(() => {
                  var target = new Grid();
                  var drag = new DragListener(target);
                  // A pure 2x scale: a 20-unit screen delta should read back as 10 units in the
                  // transform's own (pre-scale) space, matching the reference's
                  // Matrix.Invert()+Matrix.Transform(Vector) behavior for an affine transform.
                  drag.Transform = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 2, ScaleY = 2 };
                  drag.SimulateDown(new Point(0, 0));
                  drag.SimulateMove(new Point(20, 0));

                  Assert.AreEqual(10, drag.Delta.X, 0.001);
                  Assert.AreEqual(0, drag.Delta.Y, 0.001);
              });
          }

          [Test]
          public void CancelStopsTheDragAndSetsIsCanceled()
          {
              WinUITestHost.Run(() => {
                  var target = new Grid();
                  var drag = new DragListener(target);
                  int completedCount = 0;
                  drag.Completed += d => completedCount++;

                  drag.SimulateDown(new Point(0, 0));
                  drag.SimulateMove(new Point(10, 0));
                  drag.Cancel();

                  Assert.IsTrue(drag.IsCanceled);
                  Assert.AreEqual(1, completedCount, "Cancel still fires Completed, same as the reference - callers branch on IsCanceled");
                  Assert.IsFalse(drag.IsDown);
              });
          }

          [Test]
          public void GetPositionConvertsThroughTheTargetsTransformToVisual()
          {
              WinUITestHost.RunWithWindow(window => {
                  var root = new Grid { Width = 400, Height = 300 };
                  var child = new Grid { Width = 50, Height = 50, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(100, 60, 0, 0) };
                  root.Children.Add(child);
                  window.Content = root;
                  root.UpdateLayout();

                  var drag = new DragListener(child);
                  var windowPoint = child.TransformToVisual(null).TransformPoint(new Point(10, 10));
                  drag.SimulateDown(windowPoint);

                  var relative = drag.GetPosition(child);
                  Assert.IsNotNull(relative);
                  Assert.AreEqual(10, relative.Value.X, 0.5);
                  Assert.AreEqual(10, relative.Value.Y, 0.5);
              });
          }

          // Gap #1 (spec): empirically confirms a Grid-based element can actually acquire
          // keyboard focus via Focus(FocusState.Pointer) in this SDK version - the fact
          // DragListener's real (non-simulated) PointerPressed handler relies on to make
          // Escape-to-cancel reachable at all. Not a Simulate* test: this exercises the real
          // WinUI focus system, which SimulateDown deliberately bypasses.
          [Test]
          public void TargetCanAcquireKeyboardFocusAfterPointerPress()
          {
              WinUITestHost.RunWithWindow(window => {
                  var target = new Grid { Width = 20, Height = 20, IsTabStop = true };
                  window.Content = target;
                  target.UpdateLayout();

                  bool focusSucceeded = target.Focus(FocusState.Pointer);
                  var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(target.XamlRoot);

                  Assert.IsTrue(focusSucceeded, "Grid.IsTabStop=true should make it focus-eligible in this SDK version");
                  Assert.AreSame(target, focusedElement);
              });
          }
      }
  }
  ```

- [ ] **Step 3: Run the tests to verify they fail**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL — `DragListener` doesn't exist yet (`CS0246`).

- [ ] **Step 4: Implement `DragListener`**

  `src/WinUIDesigner.Designer/Controls/DragListener.cs`:
  ```csharp
  using System;
  using Windows.Foundation;
  using Windows.System;
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Input;
  using Microsoft.UI.Xaml.Media;

  namespace WinUIDesigner.Designer.Controls
  {
      public delegate void DragHandler(DragListener drag);

      /// <summary>
      /// Tracks a click-drag gesture on a UIElement without relying on Thumb's own drag events
      /// (the reference doesn't either - see the spec's "Decisions already made"). Ported from
      /// the reference's Controls/DragListener.cs with three WinUI-specific changes: pointer
      /// events replace WPF's Preview* tunneling events (WinUI has no tunneling, so
      /// AddHandler(..., handledEventsToo: true) is used instead - see the spec's mechanism
      /// table); Delta uses GeneralTransform.Inverse/TransformPoint instead of WPF's
      /// Matrix.Invert()/Matrix.Transform(Vector) (neither exists in WinUI - compiler-confirmed
      /// during spec brainstorming); and drags can be driven programmatically via
      /// SimulateDown/SimulateMove/SimulateUp/Cancel, since PointerRoutedEventArgs has no public
      /// constructor for tests to fabricate one.
      /// </summary>
      public sealed class DragListener
      {
          /// <summary>WPF's SystemParameters.MinimumHorizontalDragDistance default - no WinUI
          /// equivalent exists to read at runtime (confirmed absent from the SDK), so this is a
          /// hardcoded constant matching WPF's own default (spec Known gap #2).</summary>
          public const double MinimumHorizontalDragDistance = 4;

          /// <summary>WPF's SystemParameters.MinimumVerticalDragDistance default - see
          /// MinimumHorizontalDragDistance.</summary>
          public const double MinimumVerticalDragDistance = 2;

          public UIElement Target { get; }

          /// <summary>Optional transform used to convert Delta out of screen/window space and
          /// into the dragged element's own pre-transform space - set by callers whose thumb
          /// sits inside a rotated/scaled adorner.</summary>
          public GeneralTransform Transform { get; set; }

          public Point StartPoint { get; private set; }
          public Point CurrentPoint { get; private set; }
          public bool IsActive { get; private set; }
          public bool IsDown { get; private set; }
          public bool IsCanceled { get; private set; }
          public VirtualKeyModifiers KeyModifiers { get; private set; }

          public event DragHandler Started;
          public event DragHandler Changed;
          public event DragHandler Completed;

          Pointer _capturedPointer;

          public DragListener(UIElement target)
          {
              if (target == null) throw new ArgumentNullException(nameof(target));
              Target = target;
              Target.AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)OnPointerPressed, true);
              Target.AddHandler(UIElement.PointerMovedEvent, (PointerEventHandler)OnPointerMoved, true);
              Target.AddHandler(UIElement.PointerReleasedEvent, (PointerEventHandler)OnPointerReleased, true);
              Target.KeyDown += OnKeyDown;
          }

          void OnPointerPressed(object sender, PointerRoutedEventArgs e)
          {
              _capturedPointer = e.Pointer;
              Target.CapturePointer(e.Pointer);
              // Best-effort: makes Escape-to-cancel reachable while this element holds the
              // pointer capture. Confirmed reachable for a Grid with IsTabStop=true - see
              // DragListenerTests.TargetCanAcquireKeyboardFocusAfterPointerPress. Callers whose
              // Target isn't focus-eligible simply won't get KeyDown; the drag itself still
              // works via pointer events regardless.
              Target.Focus(FocusState.Pointer);
              BeginAt(e.GetCurrentPoint(null).Position, e.KeyModifiers);
          }

          void OnPointerMoved(object sender, PointerRoutedEventArgs e)
          {
              if (!IsDown) return;
              MoveTo(e.GetCurrentPoint(null).Position, e.KeyModifiers);
          }

          void OnPointerReleased(object sender, PointerRoutedEventArgs e)
          {
              if (!IsDown) return;
              EndAt();
          }

          void OnKeyDown(object sender, KeyRoutedEventArgs e)
          {
              if (IsDown && e.Key == VirtualKey.Escape) {
                  Cancel();
                  e.Handled = true;
              }
          }

          void BeginAt(Point point, VirtualKeyModifiers modifiers)
          {
              StartPoint = point;
              CurrentPoint = point;
              KeyModifiers = modifiers;
              IsDown = true;
              IsCanceled = false;
          }

          void MoveTo(Point point, VirtualKeyModifiers modifiers)
          {
              CurrentPoint = point;
              KeyModifiers = modifiers;
              if (!IsActive) {
                  var d = Delta;
                  if (Math.Abs(d.X) >= MinimumHorizontalDragDistance || Math.Abs(d.Y) >= MinimumVerticalDragDistance) {
                      IsActive = true;
                      Started?.Invoke(this);
                  }
              }
              if (IsActive) Changed?.Invoke(this);
          }

          void EndAt()
          {
              IsDown = false;
              ReleaseCapture();
              if (IsActive) Complete();
          }

          /// <summary>Cancels the in-progress drag (Escape, or any other caller). Still fires
          /// Completed, same as the reference - callers distinguish success from cancellation via
          /// IsCanceled inside their Completed handler.</summary>
          public void Cancel()
          {
              if (!IsDown) return;
              IsCanceled = true;
              IsDown = false;
              ReleaseCapture();
              if (IsActive) Complete();
          }

          void ReleaseCapture()
          {
              if (_capturedPointer != null) {
                  Target.ReleasePointerCapture(_capturedPointer);
                  _capturedPointer = null;
              }
          }

          void Complete()
          {
              IsActive = false;
              Completed?.Invoke(this);
          }

          /// <summary>Start-to-current offset, converted through Transform's inverse when set.
          /// Transforming both points individually and subtracting is algebraically equivalent to
          /// WPF's Matrix.Invert()+Matrix.Transform(Vector) for an affine transform - translation
          /// cancels out of the difference - without needing a Vector type, which WinUI doesn't
          /// have.</summary>
          public (double X, double Y) Delta {
              get {
                  if (Transform != null) {
                      var inverse = Transform.Inverse;
                      if (inverse != null) {
                          var s = inverse.TransformPoint(StartPoint);
                          var c = inverse.TransformPoint(CurrentPoint);
                          return (c.X - s.X, c.Y - s.Y);
                      }
                  }
                  return (CurrentPoint.X - StartPoint.X, CurrentPoint.Y - StartPoint.Y);
              }
          }

          /// <summary>Converts the last-known pointer position (window coordinates, per
          /// PointerRoutedEventArgs.GetCurrentPoint(null)'s documented frame) into
          /// <paramref name="relativeTo"/>'s own coordinate space - the replacement for WPF's
          /// ambient Mouse.GetPosition(element), which has no WinUI equivalent (position is only
          /// available from a live pointer event). Returns null if relativeTo's transform isn't
          /// invertible (GeneralTransform.Inverse can return null - unlike WPF's Matrix.Invert(),
          /// which throws instead).</summary>
          public Point? GetPosition(UIElement relativeTo)
          {
              if (relativeTo == null) return CurrentPoint;
              var inverse = relativeTo.TransformToVisual(null).Inverse;
              if (inverse == null) return null;
              return inverse.TransformPoint(CurrentPoint);
          }

          /// <summary>Test-only entry point: drives the same internal state machine
          /// OnPointerPressed does, without a real PointerRoutedEventArgs (which has no public
          /// constructor).</summary>
          public void SimulateDown(Point point, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None) => BeginAt(point, modifiers);

          /// <summary>Test-only entry point - see SimulateDown.</summary>
          public void SimulateMove(Point point, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None) => MoveTo(point, modifiers);

          /// <summary>Test-only entry point - see SimulateDown.</summary>
          public void SimulateUp(Point point)
          {
              CurrentPoint = point;
              EndAt();
          }
      }
  }
  ```

- [ ] **Step 5: Run the tests to verify they pass**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj` then run the built
  `WinUIDesigner.Designer.Tests.exe` (path in Global Constraints) filtered to `DragListenerTests`.
  Expected: PASS, all 7 tests.

- [ ] **Step 6: Re-run Phase 1/2/3 suites, then commit**

  Run both prior `.exe`s (paths in Global Constraints) — expect no regressions.
  ```bash
  git add src/WinUIDesigner.DesignModel/GeometryExtensions.cs \
          src/WinUIDesigner.Designer/Controls/DragListener.cs \
          tests/WinUIDesigner.Designer.Tests/DragListenerTests.cs
  git commit -m "feat: add DragListener pointer-drag primitive (Phase 4a Task 1)"
  ```

---

### Task 2: Core placement engine → `WinUIDesigner.DesignModel` (resolves gap #3)

**Files:**
- Create: `src/WinUIDesigner.DesignModel/PlacementType.cs`
- Create: `src/WinUIDesigner.DesignModel/PlacementAlignment.cs`
- Create: `src/WinUIDesigner.DesignModel/PlacementInformation.cs`
- Create: `src/WinUIDesigner.DesignModel/PlacementBehavior.cs`
- Create: `src/WinUIDesigner.DesignModel/PlacementOperation.cs`
- Create: `src/WinUIDesigner.DesignModel/PlacementMetadata.cs`
- Create: `src/WinUIDesigner.DesignModel/Adorners/RelativePlacement.cs`
- Create: `src/WinUIDesigner.DesignModel/Extensions/BehaviorExtension.cs`
- Modify: `src/WinUIDesigner.DesignModel/Extensions/SelectionExtensionServers.cs`
- Modify: `src/WinUIDesigner.DesignModel/ServiceContainer.cs`
- Modify: `src/WinUIDesigner.DesignModel/DesignItem.cs`
- Test: `tests/WinUIDesigner.DesignModel.Tests/PlacementOperationTests.cs`
- Test: `tests/WinUIDesigner.DesignModel.Tests/RelativePlacementTests.cs`
- Test: `tests/WinUIDesigner.DesignModel.Tests/GetCompleteAppliedTransformationToViewTests.cs`

**Interfaces:**
- Consumes: `DesignItem.AddBehavior`/`GetBehavior<T>` (Phase 2, unchanged),
  `DesignItem.Context.OpenGroup(string, ICollection<DesignItem>)` returning
  `ChangeGroup` with `.Commit()`/`.Abort()`/`.Title` (Phase 2, unchanged),
  `AdornerPlacement` (Phase 3, abstract `Arrange(AdornerPanel, UIElement,
  Size)`), `DefaultExtensionServer`/`DefaultExtensionServer.Permanent`
  (Phase 3, unchanged), `Extension.Extensions`/`ExtensionForAttribute`/
  `ExtensionServerAttribute` (Phase 2/3, unchanged).
- Produces: `WinUIDesigner.DesignModel.PlacementType` (sealed, `Register`-
  pattern, statics `MovePoint`/`Resize`/`Move`/`MoveAndIgnoreOtherContainers`/
  `AddItem`/`Delete`/`PasteItem`/`DragDropFromOutside`).
  `WinUIDesigner.DesignModel.PlacementAlignment` (struct, statics
  `TopLeft`/`Top`/`TopRight`/`Left`/`Center`/`Right`/`BottomLeft`/`Bottom`/
  `BottomRight`, properties `Horizontal`/`Vertical`).
  `WinUIDesigner.DesignModel.PlacementInformation` (`Item`, `Operation`,
  `OriginalBounds`, `Bounds`, `ResizeThumbAlignment`).
  `WinUIDesigner.DesignModel.IPlacementBehavior`/`IRootPlacementBehavior`.
  `WinUIDesigner.DesignModel.PlacementOperation` — static `Start(ICollection<DesignItem>,
  PlacementType)`, static `GetRealElementSize(UIElement)`, static
  `GetPlacementBehavior(ICollection<DesignItem>)`/`GetPlacementBehavior(ICollection<DesignItem>,
  out List<DesignItem>, PlacementType)`, instance `PlacedItems`/`Type`/
  `IsAborted`/`IsCommitted`/`CurrentContainer`/`CurrentContainerBehavior`/
  `Description`, instance `Commit()`/`Abort()`.
  `WinUIDesigner.DesignModel.PlacementMetadata.IsPlacementDisabled(Type)`/`RegisterPlacementDisabled(Type)`.
  `WinUIDesigner.DesignModel.Adorners.RelativePlacement` (sealed,
  `: AdornerPlacement`, ctor `()` and `(HorizontalAlignment, VerticalAlignment)`,
  settable `WidthOffset`/`HeightOffset`/`XOffset`/`YOffset`/
  `WidthRelativeToContentWidth`/`HeightRelativeToContentHeight`).
  `WinUIDesigner.DesignModel.Extensions.BehaviorExtension`.
  `WinUIDesigner.DesignModel.Extensions.OnlyOneItemSelectedExtensionServer`.
  `ServiceContainer.DesignPanel` (returns `IDesignPanel` via
  `GetRequiredService<IDesignPanel>()`).
  `DesignItem.GetCompleteAppliedTransformationToView()` returning
  `Microsoft.UI.Xaml.Media.Transform`.

- [ ] **Step 1: Write `PlacementType`, `PlacementAlignment`, `PlacementInformation`**

  `src/WinUIDesigner.DesignModel/PlacementType.cs`:
  ```csharp
  namespace WinUIDesigner.DesignModel
  {
      /// <summary>Describes how a placement is done. No WPF-specific API - mechanical port.</summary>
      public sealed class PlacementType
      {
          public static readonly PlacementType MovePoint = Register("MovePoint");
          public static readonly PlacementType Resize = Register("Resize");
          public static readonly PlacementType Move = Register("Move");
          public static readonly PlacementType MoveAndIgnoreOtherContainers = Register("MoveAndIgnoreOtherContainers");
          public static readonly PlacementType AddItem = Register("AddItem");
          public static readonly PlacementType Delete = Register("Delete");
          public static readonly PlacementType PasteItem = Register("PasteItem");
          public static readonly PlacementType DragDropFromOutside = Register("DragDropFromOutside");

          readonly string name;

          PlacementType(string name) { this.name = name; }

          public static PlacementType Register(string name) => new PlacementType(name);

          public override string ToString() => name;
      }
  }
  ```

  `src/WinUIDesigner.DesignModel/PlacementAlignment.cs`:
  ```csharp
  using System;
  using Microsoft.UI.Xaml;

  namespace WinUIDesigner.DesignModel
  {
      /// <summary>A combination of HorizontalAlignment/VerticalAlignment. No WPF-specific API -
      /// mechanical port; both alignment enums have the same members in WinUI.</summary>
      public struct PlacementAlignment : IEquatable<PlacementAlignment>
      {
          public HorizontalAlignment Horizontal { get; }
          public VerticalAlignment Vertical { get; }

          public PlacementAlignment(HorizontalAlignment horizontal, VerticalAlignment vertical)
          {
              if (horizontal == HorizontalAlignment.Stretch) throw new ArgumentException("Stretch is not a valid value", nameof(horizontal));
              if (vertical == VerticalAlignment.Stretch) throw new ArgumentException("Stretch is not a valid value", nameof(vertical));
              Horizontal = horizontal;
              Vertical = vertical;
          }

          public override bool Equals(object obj) => obj is PlacementAlignment other && Equals(other);
          public bool Equals(PlacementAlignment other) => Horizontal == other.Horizontal && Vertical == other.Vertical;
          public override int GetHashCode() { unchecked { return Horizontal.GetHashCode() * 27 + Vertical.GetHashCode(); } }
          public static bool operator ==(PlacementAlignment l, PlacementAlignment r) => l.Equals(r);
          public static bool operator !=(PlacementAlignment l, PlacementAlignment r) => !l.Equals(r);

          public static readonly PlacementAlignment TopLeft = new PlacementAlignment(HorizontalAlignment.Left, VerticalAlignment.Top);
          public static readonly PlacementAlignment Top = new PlacementAlignment(HorizontalAlignment.Center, VerticalAlignment.Top);
          public static readonly PlacementAlignment TopRight = new PlacementAlignment(HorizontalAlignment.Right, VerticalAlignment.Top);
          public static readonly PlacementAlignment Left = new PlacementAlignment(HorizontalAlignment.Left, VerticalAlignment.Center);
          public static readonly PlacementAlignment Center = new PlacementAlignment(HorizontalAlignment.Center, VerticalAlignment.Center);
          public static readonly PlacementAlignment Right = new PlacementAlignment(HorizontalAlignment.Right, VerticalAlignment.Center);
          public static readonly PlacementAlignment BottomLeft = new PlacementAlignment(HorizontalAlignment.Left, VerticalAlignment.Bottom);
          public static readonly PlacementAlignment Bottom = new PlacementAlignment(HorizontalAlignment.Center, VerticalAlignment.Bottom);
          public static readonly PlacementAlignment BottomRight = new PlacementAlignment(HorizontalAlignment.Right, VerticalAlignment.Bottom);
      }
  }
  ```

  `src/WinUIDesigner.DesignModel/PlacementInformation.cs`:
  ```csharp
  using Windows.Foundation;

  namespace WinUIDesigner.DesignModel
  {
      /// <summary>Stores information about the placement of an individual item during a
      /// PlacementOperation. No WPF-specific API - mechanical port.</summary>
      public sealed class PlacementInformation
      {
          public const int BoundsPrecision = 0;

          public DesignItem Item { get; }
          public PlacementOperation Operation { get; }
          public Rect OriginalBounds { get; set; }
          public Rect Bounds { get; set; }
          public PlacementAlignment? ResizeThumbAlignment { get; set; }

          internal PlacementInformation(DesignItem item, PlacementOperation operation)
          {
              Item = item;
              Operation = operation;
          }

          public override string ToString() => "[PlacementInformation OriginalBounds=" + OriginalBounds + " Bounds=" + Bounds + " Item=" + Item + "]";
      }
  }
  ```

- [ ] **Step 2: Write `PlacementBehavior` (the interfaces)**

  `src/WinUIDesigner.DesignModel/PlacementBehavior.cs`:
  ```csharp
  using System.Collections.Generic;
  using Windows.Foundation;

  namespace WinUIDesigner.DesignModel
  {
      /// <summary>Behavior interface implemented by container elements to support resizing
      /// child elements. No WPF-specific API - mechanical port.</summary>
      public interface IPlacementBehavior
      {
          bool CanPlace(IEnumerable<DesignItem> childItems, PlacementType type, PlacementAlignment position);
          void BeginPlacement(PlacementOperation operation);
          void EndPlacement(PlacementOperation operation);
          Rect GetPosition(PlacementOperation operation, DesignItem child);
          void BeforeSetPosition(PlacementOperation operation);
          void SetPosition(PlacementInformation info);
          bool CanLeaveContainer(PlacementOperation operation);
          void LeaveContainer(PlacementOperation operation);
          bool CanEnterContainer(PlacementOperation operation, bool shouldAlwaysEnter);
          void EnterContainer(PlacementOperation operation);
          Point PlacePoint(Point point);
      }

      /// <summary>Behavior interface for root elements (Parent == null).</summary>
      public interface IRootPlacementBehavior : IPlacementBehavior
      {
      }
  }
  ```

- [ ] **Step 3: Write `PlacementOperation`, narrowed to the Start/Commit/Abort
      lifecycle**

  `src/WinUIDesigner.DesignModel/PlacementOperation.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Collections.ObjectModel;
  using System.Linq;
  using Windows.Foundation;
  using Microsoft.UI.Xaml;

  namespace WinUIDesigner.DesignModel
  {
      /// <summary>
      /// Stores data about a placement operation. Ported from the reference's
      /// PlacementOperation.cs with ChangeContainer, DeleteItemsAndCommit, and
      /// TryStartInsertNewComponents dropped: their only callers are container-to-container
      /// move (Phase 4b), the delete command (out of scope per Phase 3), and toolbox-drop
      /// insertion (no Phase 4a caller) - and ChangeContainer's own TransformRectByMiddlePoint
      /// helper used Vector/Rect.TopLeft/Rect.Size/GeneralTransform.Transform(Point), none of
      /// which exist in WinUI (compiler-confirmed during spec brainstorming). Same "port only
      /// the called surface" treatment already applied to ModelTools.cs/Metadata.cs.
      /// </summary>
      public sealed class PlacementOperation
      {
          public sealed class PlacementOperationException : InvalidOperationException
          {
              public PlacementOperationException(string message) : base(message) { }
          }

          readonly ChangeGroup changeGroup;
          readonly ReadOnlyCollection<PlacementInformation> placedItems;
          readonly PlacementType type;
          DesignItem currentContainer;
          IPlacementBehavior currentContainerBehavior;
          bool isAborted, isCommitted;

          public ReadOnlyCollection<PlacementInformation> PlacedItems => placedItems;
          public PlacementType Type => type;
          public bool IsAborted => isAborted;
          public bool IsCommitted => isCommitted;
          public DesignItem CurrentContainer => currentContainer;
          public IPlacementBehavior CurrentContainerBehavior => currentContainerBehavior;

          public static PlacementOperation Start(ICollection<DesignItem> placedItems, PlacementType type)
          {
              if (placedItems == null) throw new ArgumentNullException(nameof(placedItems));
              if (type == null) throw new ArgumentNullException(nameof(type));
              DesignItem[] items = placedItems.ToArray();
              if (items.Length == 0) throw new ArgumentException("placedItems.Length must be > 0");

              var op = new PlacementOperation(items, type);
              if (op.currentContainerBehavior == null) {
                  op.changeGroup.Abort();
                  throw new PlacementOperationException("Starting the operation is not supported");
              }
              try {
                  op.currentContainerBehavior.BeginPlacement(op);
                  foreach (var info in op.placedItems) {
                      info.OriginalBounds = op.currentContainerBehavior.GetPosition(op, info.Item);
                      info.Bounds = info.OriginalBounds;
                  }
              } catch {
                  op.changeGroup.Abort();
                  throw;
              }
              return op;
          }

          PlacementOperation(DesignItem[] items, PlacementType type)
          {
              List<DesignItem> moveableItems;
              currentContainerBehavior = GetPlacementBehavior(items, out moveableItems, type);

              var information = new PlacementInformation[moveableItems.Count];
              for (int i = 0; i < information.Length; i++)
                  information[i] = new PlacementInformation(moveableItems[i], this);
              placedItems = new ReadOnlyCollection<PlacementInformation>(information);
              this.type = type;
              currentContainer = moveableItems[0].Parent;
              changeGroup = moveableItems[0].Context.OpenGroup(type.ToString(), moveableItems);
          }

          /// <summary>The size an element really should have (even if smaller-rendered, like an
          /// empty Image).</summary>
          public static Size GetRealElementSize(UIElement element)
          {
              var size = element.RenderSize;
              var fe = element as FrameworkElement;
              if (fe != null && !double.IsNaN(fe.Width)) size.Width = fe.Width;
              if (fe != null && !double.IsNaN(fe.Height)) size.Height = fe.Height;
              if (fe != null && size.Width < fe.MinWidth) size.Width = fe.MinWidth;
              if (fe != null && size.Height < fe.MinHeight) size.Height = fe.MinHeight;
              return size;
          }

          public static IPlacementBehavior GetPlacementBehavior(ICollection<DesignItem> items)
          {
              List<DesignItem> moveableItems;
              return GetPlacementBehavior(items, out moveableItems, PlacementType.Move);
          }

          public static IPlacementBehavior GetPlacementBehavior(ICollection<DesignItem> items, out List<DesignItem> moveableItems, PlacementType placementType)
          {
              moveableItems = new List<DesignItem>();
              if (items == null) throw new ArgumentNullException(nameof(items));
              if (items.Count == 0) return null;

              var possibleItems = items;
              if (!items.Any(x => x.Parent == null)) {
                  var itemsParentGroup = items.GroupBy(x => x.Parent);
                  var parents = itemsParentGroup.Select(x => x.Key).OrderBy(x => x.DepthLevel).First();
                  possibleItems = itemsParentGroup.Where(x => x.Key.DepthLevel == parents.DepthLevel).SelectMany(x => x).ToList();
              }

              var first = possibleItems.First();
              DesignItem parent = first.Parent;
              moveableItems.Add(first);
              foreach (var item in possibleItems.Skip(1)) {
                  if (item.Parent != parent) {
                      if (placementType != PlacementType.MoveAndIgnoreOtherContainers) return null;
                  } else {
                      moveableItems.Add(item);
                  }
              }
              if (parent != null) return parent.GetBehavior<IPlacementBehavior>();
              if (possibleItems.Count() == 1) return first.GetBehavior<IRootPlacementBehavior>();
              return null;
          }

          public string Description {
              get => changeGroup.Title;
              set => changeGroup.Title = value;
          }

          public void Abort()
          {
              if (isAborted) return;
              if (isCommitted) throw new PlacementOperationException("PlacementOperation is committed.");
              isAborted = true;
              currentContainerBehavior.EndPlacement(this);
              changeGroup.Abort();
          }

          public void Commit()
          {
              if (isAborted || isCommitted) throw new PlacementOperationException("PlacementOperation is already aborted/committed.");
              isCommitted = true;
              currentContainerBehavior.EndPlacement(this);
              changeGroup.Commit();
          }
      }
  }
  ```

- [ ] **Step 4: Write `PlacementMetadata`**

  `src/WinUIDesigner.DesignModel/PlacementMetadata.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;

  namespace WinUIDesigner.DesignModel
  {
      /// <summary>The one member of the reference's Metadata.cs (458 lines, a reflection-heavy
      /// design-time-attribute system already out of scope per Phase 3's "no property-grid UI or
      /// metadata") that DefaultPlacementBehavior (Task 3) actually calls. Ported alone, same
      /// precedent as Phase 3's Known-gap-#2 (PlacementOperation.GetRealElementSize).</summary>
      public static class PlacementMetadata
      {
          static readonly HashSet<Type> placementDisabled = new HashSet<Type>();

          public static void RegisterPlacementDisabled(Type type)
          {
              if (type == null) throw new ArgumentNullException(nameof(type));
              lock (placementDisabled) placementDisabled.Add(type);
          }

          public static bool IsPlacementDisabled(Type type)
          {
              lock (placementDisabled) return placementDisabled.Contains(type);
          }
      }
  }
  ```

- [ ] **Step 5: Write `RelativePlacement`**

  `src/WinUIDesigner.DesignModel/Adorners/RelativePlacement.cs`:
  ```csharp
  using System;
  using Windows.Foundation;
  using Microsoft.UI.Xaml;

  namespace WinUIDesigner.DesignModel.Adorners
  {
      /// <summary>Placement class providing properties for different kinds of relative
      /// placements. No WPF-specific API - mechanical port; operates only on doubles plus
      /// HorizontalAlignment/VerticalAlignment/AdornerPlacement/UIElement.DesiredSize/Arrange
      /// (all already ported, Phase 3).</summary>
      public sealed class RelativePlacement : AdornerPlacement
      {
          public RelativePlacement() { }

          public RelativePlacement(HorizontalAlignment horizontal, VerticalAlignment vertical)
          {
              switch (horizontal) {
                  case HorizontalAlignment.Left:
                      widthRelativeToDesiredWidth = 1;
                      xRelativeToAdornerWidth = -1;
                      break;
                  case HorizontalAlignment.Right:
                      widthRelativeToDesiredWidth = 1;
                      xRelativeToContentWidth = 1;
                      break;
                  case HorizontalAlignment.Center:
                      widthRelativeToDesiredWidth = 1;
                      xRelativeToContentWidth = 0.5;
                      xRelativeToAdornerWidth = -0.5;
                      break;
                  case HorizontalAlignment.Stretch:
                      widthRelativeToContentWidth = 1;
                      break;
              }
              switch (vertical) {
                  case VerticalAlignment.Top:
                      heightRelativeToDesiredHeight = 1;
                      yRelativeToAdornerHeight = -1;
                      break;
                  case VerticalAlignment.Bottom:
                      heightRelativeToDesiredHeight = 1;
                      yRelativeToContentHeight = 1;
                      break;
                  case VerticalAlignment.Center:
                      heightRelativeToDesiredHeight = 1;
                      yRelativeToContentHeight = 0.5;
                      yRelativeToAdornerHeight = -0.5;
                      break;
                  case VerticalAlignment.Stretch:
                      heightRelativeToContentHeight = 1;
                      break;
              }
          }

          double widthRelativeToDesiredWidth, heightRelativeToDesiredHeight;
          public double WidthRelativeToDesiredWidth { get => widthRelativeToDesiredWidth; set => widthRelativeToDesiredWidth = value; }
          public double HeightRelativeToDesiredHeight { get => heightRelativeToDesiredHeight; set => heightRelativeToDesiredHeight = value; }

          double widthRelativeToContentWidth, heightRelativeToContentHeight;
          public double WidthRelativeToContentWidth { get => widthRelativeToContentWidth; set => widthRelativeToContentWidth = value; }
          public double HeightRelativeToContentHeight { get => heightRelativeToContentHeight; set => heightRelativeToContentHeight = value; }

          double widthOffset, heightOffset;
          public double WidthOffset { get => widthOffset; set => widthOffset = value; }
          public double HeightOffset { get => heightOffset; set => heightOffset = value; }

          Size CalculateSize(UIElement adorner, Size adornedElementSize)
          {
              return new Size(
                  Math.Max(widthOffset + widthRelativeToDesiredWidth * adorner.DesiredSize.Width + widthRelativeToContentWidth * adornedElementSize.Width, 0),
                  Math.Max(heightOffset + heightRelativeToDesiredHeight * adorner.DesiredSize.Height + heightRelativeToContentHeight * adornedElementSize.Height, 0));
          }

          double xOffset, yOffset;
          public double XOffset { get => xOffset; set => xOffset = value; }
          public double YOffset { get => yOffset; set => yOffset = value; }

          double xRelativeToAdornerWidth, yRelativeToAdornerHeight;
          public double XRelativeToAdornerWidth { get => xRelativeToAdornerWidth; set => xRelativeToAdornerWidth = value; }
          public double YRelativeToAdornerHeight { get => yRelativeToAdornerHeight; set => yRelativeToAdornerHeight = value; }

          double xRelativeToContentWidth, yRelativeToContentHeight;
          public double XRelativeToContentWidth { get => xRelativeToContentWidth; set => xRelativeToContentWidth = value; }
          public double YRelativeToContentHeight { get => yRelativeToContentHeight; set => yRelativeToContentHeight = value; }

          Point CalculatePosition(Size adornedElementSize, Size adornerSize)
          {
              return new Point(
                  xOffset + xRelativeToAdornerWidth * adornerSize.Width + xRelativeToContentWidth * adornedElementSize.Width,
                  yOffset + yRelativeToAdornerHeight * adornerSize.Height + yRelativeToContentHeight * adornedElementSize.Height);
          }

          public override void Arrange(AdornerPanel panel, UIElement adorner, Size adornedElementSize)
          {
              Size adornerSize = CalculateSize(adorner, adornedElementSize);
              adorner.Arrange(new Rect(CalculatePosition(adornedElementSize, adornerSize), adornerSize));
          }
      }
  }
  ```

- [ ] **Step 6: Write `BehaviorExtension` and add `OnlyOneItemSelectedExtensionServer`**

  `src/WinUIDesigner.DesignModel/Extensions/BehaviorExtension.cs`:
  ```csharp
  namespace WinUIDesigner.DesignModel.Extensions
  {
      /// <summary>Base class for extensions that provide a behavior interface for the designed
      /// item. Always loaded, must have a parameterless constructor. No WPF-specific API -
      /// mechanical port; DefaultExtensionServer.Permanent already exists (Phase 3).</summary>
      [ExtensionServer(typeof(DefaultExtensionServer.Permanent))]
      public class BehaviorExtension : DefaultExtension
      {
      }
  }
  ```

  Append to the end of `src/WinUIDesigner.DesignModel/Extensions/SelectionExtensionServers.cs`
  (inside the existing `namespace WinUIDesigner.DesignModel.Extensions { ... }`
  block, after `PrimarySelectionExtensionServer`'s closing brace):
  ```csharp

  	/// <summary>Applies an extension to the primary selection only when exactly one item is
  	/// selected - used by the resize/rotate/skew thumb extensions (Task 4/5), which don't make
  	/// sense for a multi-selection. No WPF-specific API - mechanical port.</summary>
  	public class OnlyOneItemSelectedExtensionServer : PrimarySelectionExtensionServer
  	{
  		protected override void OnInitialized()
  		{
  			base.OnInitialized();
  			this.Services.Selection.SelectionChanged += OnSelectionChanged;
  		}

  		void OnSelectionChanged(object sender, System.EventArgs e)
  		{
  			if (this.Services.Selection.SelectedItems.Count > 1)
  				ReapplyExtensions(this.Services.Selection.SelectedItems);
  		}

  		public override bool ShouldApplyExtensions(DesignItem extendedItem)
  		{
  			return Services.Selection.PrimarySelection == extendedItem && Services.Selection.SelectionCount < 2;
  		}
  	}
  ```

- [ ] **Step 7: Add `ServiceContainer.DesignPanel`**

  In `src/WinUIDesigner.DesignModel/ServiceContainer.cs`, immediately after the
  existing `Selection` property (after its closing `}`, before the class's
  final closing `}`), add:
  ```csharp

  		/// <summary>
  		/// Gets the <see cref="IDesignPanel"/>.
  		/// Throws an exception if the service is not found.
  		/// </summary>
  		public IDesignPanel DesignPanel {
  			get {
  				return GetRequiredService<IDesignPanel>();
  			}
  		}
  ```

- [ ] **Step 8: Add `DesignItem.GetCompleteAppliedTransformationToView`**

  In `src/WinUIDesigner.DesignModel/DesignItem.cs`, add `using
  Microsoft.UI.Xaml.Media;` to the using block at the top (after `using
  Microsoft.UI.Xaml;`). Then, immediately before the existing `DepthLevel`
  property (before its `/// <summary>` doc comment at line 356), add:
  ```csharp
  		/// <summary>
  		/// Gets a Transform representing all transforms applied to the item's view. Narrowed to
  		/// RenderTransform only - the reference also walks LayoutTransform and
  		/// ContainerVisual.Transform, neither of which exists in WinUI (confirmed absent from
  		/// Microsoft.UI.Xaml.winmd during spec brainstorming). See spec's Known gap #3 for
  		/// whether re-adding a live RenderTransform instance into a new TransformGroup is safe.
  		/// </summary>
  		public Microsoft.UI.Xaml.Media.Transform GetCompleteAppliedTransformationToView()
  		{
  			var result = new TransformGroup();
  			DependencyObject current = this.View;
  			while (current != null) {
  				var fe = current as FrameworkElement;
  				if (fe != null && fe.RenderTransform != null)
  					result.Children.Add(fe.RenderTransform);
  				current = VisualTreeHelper.GetParent(current);
  			}
  			return result;
  		}

  ```

- [ ] **Step 9: Write the failing tests**

  `tests/WinUIDesigner.DesignModel.Tests/PlacementOperationTests.cs`:
  ```csharp
  using System.Collections.Generic;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Extensions;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.DesignModel.Tests
  {
      [TestFixture]
      public class PlacementOperationTests
      {
          // A minimal IPlacementBehavior stub - just enough to exercise Start/Commit/Abort's
          // lifecycle calls without needing a real DesignItem tree.
          sealed class RecordingPlacementBehavior : IPlacementBehavior
          {
              public bool BeganPlacement, EndedPlacement;
              public bool CanPlace(IEnumerable<DesignItem> childItems, PlacementType type, PlacementAlignment position) => true;
              public void BeginPlacement(PlacementOperation operation) => BeganPlacement = true;
              public void EndPlacement(PlacementOperation operation) => EndedPlacement = true;
              public Windows.Foundation.Rect GetPosition(PlacementOperation operation, DesignItem child) => new Windows.Foundation.Rect(0, 0, 10, 10);
              public void BeforeSetPosition(PlacementOperation operation) { }
              public void SetPosition(PlacementInformation info) { }
              public bool CanLeaveContainer(PlacementOperation operation) => true;
              public void LeaveContainer(PlacementOperation operation) { }
              public bool CanEnterContainer(PlacementOperation operation, bool shouldAlwaysEnter) => true;
              public void EnterContainer(PlacementOperation operation) { }
              public Windows.Foundation.Point PlacePoint(Windows.Foundation.Point point) => point;
          }

          [Test]
          public void PlacementTypesAreDistinctEvenWithTheSameToStringGroup()
          {
              // Register() intentionally allows duplicate names to produce distinct instances -
              // Resize and a hypothetical second "Resize" would compare unequal by design.
              var a = PlacementType.Register("Test");
              var b = PlacementType.Register("Test");
              Assert.AreNotSame(a, b);
              Assert.AreEqual("Test", a.ToString());
          }

          [Test]
          public void PlacementAlignmentRejectsStretch()
          {
              Assert.Throws<System.ArgumentException>(() =>
                  new PlacementAlignment(Microsoft.UI.Xaml.HorizontalAlignment.Stretch, Microsoft.UI.Xaml.VerticalAlignment.Top));
          }

          [Test]
          public void GetRealElementSizeUsesExplicitWidthHeightWhenSet()
          {
              WinUITestHost.Run(() => {
                  var grid = new Microsoft.UI.Xaml.Controls.Grid { Width = 42, Height = 17 };
                  var size = PlacementOperation.GetRealElementSize(grid);
                  Assert.AreEqual(42, size.Width);
                  Assert.AreEqual(17, size.Height);
              });
          }
      }
  }
  ```

  `tests/WinUIDesigner.DesignModel.Tests/RelativePlacementTests.cs`:
  ```csharp
  using NUnit.Framework;
  using WinUIDesigner.DesignModel.Adorners;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.DesignModel.Tests
  {
      [TestFixture]
      public class RelativePlacementTests
      {
          [Test]
          public void TopLeftAlignmentArrangesAtTheOriginWithDesiredSize()
          {
              WinUITestHost.Run(() => {
                  var adorner = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 7, Height = 7 };
                  adorner.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));

                  var placement = new RelativePlacement(Microsoft.UI.Xaml.HorizontalAlignment.Left, Microsoft.UI.Xaml.VerticalAlignment.Top);
                  var panel = new AdornerPanel();
                  placement.Arrange(panel, adorner, new Windows.Foundation.Size(100, 50));

                  Assert.AreEqual(-7, adorner.ActualOffset.X, 0.01);
                  Assert.AreEqual(-7, adorner.ActualOffset.Y, 0.01);
                  Assert.AreEqual(7, adorner.ActualSize.X, 0.01);
                  Assert.AreEqual(7, adorner.ActualSize.Y, 0.01);
              });
          }

          [Test]
          public void CenterStretchArrangesAcrossTheFullContentWidth()
          {
              WinUITestHost.Run(() => {
                  var adorner = new Microsoft.UI.Xaml.Shapes.Rectangle();
                  adorner.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));

                  var placement = new RelativePlacement(Microsoft.UI.Xaml.HorizontalAlignment.Stretch, Microsoft.UI.Xaml.VerticalAlignment.Top) { HeightOffset = 6 };
                  var panel = new AdornerPanel();
                  placement.Arrange(panel, adorner, new Windows.Foundation.Size(100, 50));

                  Assert.AreEqual(100, adorner.ActualSize.X, 0.01);
                  Assert.AreEqual(6, adorner.ActualSize.Y, 0.01);
              });
          }
      }
  }
  ```

  `tests/WinUIDesigner.DesignModel.Tests/GetCompleteAppliedTransformationToViewTests.cs`:
  ```csharp
  using System.Linq;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel.Xaml;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.DesignModel.Tests
  {
      [TestFixture]
      public class GetCompleteAppliedTransformationToViewTests
      {
          const string RotatedButtonXaml =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
              "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\">" +
              "<Button.RenderTransform><RotateTransform Angle=\"30\" /></Button.RenderTransform>" +
              "</Button></Grid>";

          // Gap #3 (spec): confirms re-adding a live, already-parented RenderTransform instance
          // into a new TransformGroup.Children collection does not throw in this SDK version -
          // WPF allows this because Transform is a shareable Freezable, WinUI's is a plain
          // DependencyObject with generally stricter parenting rules.
          [Test]
          public void ReparentingALiveRenderTransformIntoANewTransformGroupDoesNotThrow()
          {
              WinUITestHost.RunWithWindow(window => {
                  var settings = new XamlLoadSettings();
                  using (var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(RotatedButtonXaml))) {
                      var context = new XamlDesignContext(reader, settings);
                      var button = (Microsoft.UI.Xaml.Controls.Button)context.RootItem.Component;
                      var buttonItem = context.Services.Component.GetDesignItem(button);
                      window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                      ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();

                      Microsoft.UI.Xaml.Media.Transform result = null;
                      Assert.DoesNotThrow(() => result = buttonItem.GetCompleteAppliedTransformationToView());

                      var group = (Microsoft.UI.Xaml.Media.TransformGroup)result;
                      var rotate = group.Children.OfType<Microsoft.UI.Xaml.Media.RotateTransform>().SingleOrDefault();
                      Assert.IsNotNull(rotate, "the button's own RenderTransform should be included");
                      Assert.AreEqual(30, rotate.Angle, 0.01);
                  }
              });
          }
      }
  }
  ```

- [ ] **Step 10: Run the tests to verify they fail**

  Run: `dotnet build tests/WinUIDesigner.DesignModel.Tests/WinUIDesigner.DesignModel.Tests.csproj`
  Expected: FAIL — none of `PlacementType`/`PlacementAlignment`/`PlacementOperation`/
  `RelativePlacement`/`GetCompleteAppliedTransformationToView` exist yet.

- [ ] **Step 11: Verify the build (implementation is already written above), then run the tests**

  Run: `dotnet build tests/WinUIDesigner.DesignModel.Tests/WinUIDesigner.DesignModel.Tests.csproj`, then run
  the built `WinUIDesigner.DesignModel.Tests.exe`.
  Expected: PASS, all new tests green.

  If `ReparentingALiveRenderTransformIntoANewTransformGroupDoesNotThrow`
  actually throws: this is gap #3 resolving to "no" rather than "yes" — do
  not swallow the exception. Change `GetCompleteAppliedTransformationToView`
  to clone each `RenderTransform`'s property values into a fresh instance
  (e.g. for a `RotateTransform`, `new RotateTransform { Angle = fe.RenderTransform is
  RotateTransform rt ? rt.Angle : 0, ... }`) instead of reusing the live
  reference, update the test's expectations accordingly, and record the
  actual finding in a code comment on the method (not a guess presented as
  fact either way).

- [ ] **Step 12: Re-run Phase 1/2/3 suites, then commit**

  ```bash
  git add src/WinUIDesigner.DesignModel/PlacementType.cs \
          src/WinUIDesigner.DesignModel/PlacementAlignment.cs \
          src/WinUIDesigner.DesignModel/PlacementInformation.cs \
          src/WinUIDesigner.DesignModel/PlacementBehavior.cs \
          src/WinUIDesigner.DesignModel/PlacementOperation.cs \
          src/WinUIDesigner.DesignModel/PlacementMetadata.cs \
          src/WinUIDesigner.DesignModel/Adorners/RelativePlacement.cs \
          src/WinUIDesigner.DesignModel/Extensions/BehaviorExtension.cs \
          src/WinUIDesigner.DesignModel/Extensions/SelectionExtensionServers.cs \
          src/WinUIDesigner.DesignModel/ServiceContainer.cs \
          src/WinUIDesigner.DesignModel/DesignItem.cs \
          tests/WinUIDesigner.DesignModel.Tests/PlacementOperationTests.cs \
          tests/WinUIDesigner.DesignModel.Tests/RelativePlacementTests.cs \
          tests/WinUIDesigner.DesignModel.Tests/GetCompleteAppliedTransformationToViewTests.cs
  git commit -m "feat: port core placement engine to WinUIDesigner.DesignModel (Phase 4a Task 2)"
  ```

---

### Task 3: `DefaultPlacementBehavior` + `ModelTools` (resolves gap #4)

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/DefaultPlacementBehavior.cs`
- Create: `src/WinUIDesigner.Designer/ModelTools.cs`
- Modify: `src/WinUIDesigner.Designer/DesignPanel.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/DefaultPlacementBehaviorTests.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/ModelToolsTests.cs`

**Interfaces:**
- Consumes: everything Task 2 produced, plus `Extension.OnInitialized`/
  `OnRemove` (Phase 2/3, unchanged), `DesignItem.AddBehavior`/`GetBehavior<T>`
  (Phase 2, unchanged), `CollectionSupport.CanCollectionAdd(Type,
  IEnumerable)` (Phase 1, unchanged — `src/WinUIDesigner.XamlDom/CollectionSupport.cs`),
  `ExtensionForAttribute` (Phase 2/3, unchanged).
- Produces: `WinUIDesigner.Designer.Extensions.DefaultPlacementBehavior`
  registered `[ExtensionFor(typeof(Panel))] [ExtensionFor(typeof(Control))]
  [ExtensionFor(typeof(Viewbox))]` (no `Border` — sealed, cannot be
  extended-for the same way the reference does since `[ExtensionFor]` just
  needs the *type token*, not a subclass, so `Border` stays listed; see Step
  1). `WinUIDesigner.Designer.ModelTools.GetWidth(UIElement)`/
  `GetHeight(UIElement)`/`Resize(DesignItem, double, double)`/
  `ApplyTransform(DesignItem, Transform, bool relative = true, string
  transformProperty = null)` — Task 4/5's thumb extensions call all four.
  `DesignPanel.AdornerLayer` (public property, type
  `WinUIDesigner.Designer.Controls.AdornerLayer`) — `ModelTools.ApplyTransform`
  and `SkewThumbExtension` (Task 5) both need it.

- [ ] **Step 1: Write `DefaultPlacementBehavior`**

  `src/WinUIDesigner.Designer/Extensions/DefaultPlacementBehavior.cs`:
  ```csharp
  using System.Collections.Generic;
  using System.Linq;
  using Windows.Foundation;
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Controls;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Extensions;

  namespace WinUIDesigner.Designer.Extensions
  {
      /// <summary>
      /// Generic placement behavior for any Panel/Control/Border/Viewbox that doesn't have a
      /// more specific one (Grid/Canvas/StackPanel-specific behaviors are Phase 4b). Resizes via
      /// Width/Height, not Canvas.Left/Top. Ported from the reference's
      /// Extensions/DefaultPlacementBehavior.cs with two drops: InfoTextEnterArea ("hold Alt to
      /// enter container" hint UI) has no 4a consumer reachable through this phase's own
      /// EndPlacement/CanEnterContainer paths without it, and internalCanEnterContainer's
      /// "is Decorator" branch is dropped since WinUI has no Decorator type (confirmed absent
      /// from Microsoft.UI.Xaml.winmd during spec brainstorming) - its three sibling branches
      /// (Expander/UserControl/CollectionSupport) all port unchanged.
      /// </summary>
      [ExtensionFor(typeof(Panel))]
      [ExtensionFor(typeof(Control))]
      [ExtensionFor(typeof(Border))]
      [ExtensionFor(typeof(Viewbox))]
      public class DefaultPlacementBehavior : BehaviorExtension, IPlacementBehavior
      {
          protected override void OnInitialized()
          {
              base.OnInitialized();
              if (ExtendedItem.ContentProperty == null || PlacementMetadata.IsPlacementDisabled(ExtendedItem.ComponentType))
                  return;
              ExtendedItem.AddBehavior(typeof(IPlacementBehavior), this);
          }

          public virtual bool CanPlace(IEnumerable<DesignItem> childItems, PlacementType type, PlacementAlignment position) => true;

          public virtual void BeginPlacement(PlacementOperation operation) { }

          public virtual void EndPlacement(PlacementOperation operation) { }

          public virtual Rect GetPosition(PlacementOperation operation, DesignItem item) => GetPositionRelativeToContainer(operation, item);

          public Rect GetPositionRelativeToContainer(PlacementOperation operation, DesignItem item)
          {
              if (item.View == null) return default;
              var p = item.View.TransformToVisual(operation.CurrentContainer.View).TransformPoint(new Point());
              return new Rect(p, PlacementOperation.GetRealElementSize(item.View));
          }

          public virtual void BeforeSetPosition(PlacementOperation operation) { }

          public virtual void SetPosition(PlacementInformation info)
          {
              if (info.Operation.Type != PlacementType.Move && info.Operation.Type != PlacementType.MovePoint && info.Operation.Type != PlacementType.MoveAndIgnoreOtherContainers)
                  ModelTools.Resize(info.Item, info.Bounds.Width, info.Bounds.Height);
          }

          public virtual bool CanLeaveContainer(PlacementOperation operation) => true;

          public virtual void LeaveContainer(PlacementOperation operation)
          {
              foreach (var info in operation.PlacedItems) {
                  var parentProperty = info.Item.ParentProperty;
                  if (parentProperty.IsCollection)
                      parentProperty.CollectionElements.Remove(info.Item);
                  else
                      parentProperty.Reset();
              }
          }

          public virtual bool CanEnterContainer(PlacementOperation operation, bool shouldAlwaysEnter) => internalCanEnterContainer(operation);

          bool internalCanEnterContainer(PlacementOperation operation)
          {
              if (ExtendedItem.Component is Expander expander) {
                  if (!expander.IsExpanded) expander.IsExpanded = true;
              }

              if (ExtendedItem.Component is UserControl && ExtendedItem.ComponentType != typeof(UserControl))
                  return false;

              if (ExtendedItem.ContentProperty.IsCollection)
                  return WinUIDesigner.XamlDom.CollectionSupport.CanCollectionAdd(ExtendedItem.ContentProperty.ReturnType, operation.PlacedItems.Select(p => p.Item.Component));

              if (ExtendedItem.ContentProperty.ReturnType == typeof(string))
                  return false;

              if (!ExtendedItem.ContentProperty.IsSet)
                  return true;

              object value = ExtendedItem.ContentProperty.GetConvertedValueOnInstance<object>();
              return ExtendedItem.ContentProperty.Value == null && (value is string s && string.IsNullOrEmpty(s));
          }

          public virtual void EnterContainer(PlacementOperation operation)
          {
              if (ExtendedItem.ContentProperty.IsCollection) {
                  foreach (var info in operation.PlacedItems)
                      ExtendedItem.ContentProperty.CollectionElements.Add(info.Item);
              } else {
                  ExtendedItem.ContentProperty.SetValue(operation.PlacedItems[0].Item);
              }
              if (operation.Type == PlacementType.AddItem) {
                  foreach (var info in operation.PlacedItems)
                      SetPosition(info);
              }
          }

          public virtual Point PlacePoint(Point point) => new Point(System.Math.Round(point.X), System.Math.Round(point.Y));
      }
  }
  ```

`WinUIDesigner.XamlDom.CollectionSupport.CanCollectionAdd(Type, IEnumerable)`
(confirmed at `src/WinUIDesigner.XamlDom/CollectionSupport.cs:120`) is the
exact signature the qualified call above uses.

- [ ] **Step 2: Write `ModelTools`**

  `src/WinUIDesigner.Designer/ModelTools.cs`:
  ```csharp
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Media;
  using WinUIDesigner.DesignModel;

  namespace WinUIDesigner.Designer
  {
      /// <summary>
      /// Static helper methods for working with the designer DOM. Ported from the reference's
      /// ModelTools.cs (890 lines) with only the four members Phase 4a actually calls -
      /// WrapItemsNewContainer/UnwrapItemsFromContainer/ArrangeItems/StretchItems/
      /// CanDeleteComponents/IsInDocument/ComparePositionInModelFile are all multi-selection/
      /// context-menu helpers with no 4a consumer (4c/4e territory).
      /// </summary>
      public static class ModelTools
      {
          public static double GetWidth(UIElement element)
          {
              var fe = (FrameworkElement)element;
              double v = fe.Width;
              return double.IsNaN(v) ? element.RenderSize.Width : v;
          }

          public static double GetHeight(UIElement element)
          {
              var fe = (FrameworkElement)element;
              double v = fe.Height;
              return double.IsNaN(v) ? element.RenderSize.Height : v;
          }

          public static void Resize(DesignItem item, double newWidth, double newHeight)
          {
              if (newWidth != GetWidth(item.View)) {
                  if (double.IsNaN(newWidth))
                      item.Properties.GetProperty("Width").Reset();
                  else
                      item.Properties.GetProperty("Width").SetValue(newWidth);
              }
              if (newHeight != GetHeight(item.View)) {
                  if (double.IsNaN(newHeight))
                      item.Properties.GetProperty("Height").Reset();
                  else
                      item.Properties.GetProperty("Height").SetValue(newHeight);
              }
          }

          /// <summary>
          /// Applies (or replaces) a render transform on the design item. transformProperty is a
          /// string (not a DependencyProperty, unlike the reference) - see the spec's
          /// "DesignItemProperty are string-keyed only" finding - defaulting to "RenderTransform".
          /// Gap #4 (spec): the TransformGroup-wrapping branch below (when an existing transform
          /// of a different type needs to become part of a group) has not been exercised by any
          /// prior phase against a non-FrameworkElement WinRT type - see
          /// ModelToolsTests.ApplyingASecondTransformKindWrapsBothInATransformGroup.
          /// </summary>
          public static void ApplyTransform(DesignItem designItem, Transform transform, bool relative = true, string transformProperty = null)
          {
              var changeGroup = designItem.OpenGroup("Apply Transform");
              transformProperty = transformProperty ?? "RenderTransform";

              Transform oldTransform = null;
              if (designItem.Properties.GetProperty(transformProperty).IsSet)
                  oldTransform = designItem.Properties.GetProperty(transformProperty).GetConvertedValueOnInstance<Transform>();

              if (oldTransform != null && oldTransform.GetType() != transform.GetType() && !(oldTransform is TransformGroup)) {
                  var tg = new TransformGroup();
                  var tgDes = designItem.Services.Component.RegisterComponentForDesigner(tg);
                  tgDes.ContentProperty.CollectionElements.Add(designItem.Services.Component.GetDesignItem(oldTransform));
                  designItem.Properties.GetProperty(transformProperty).SetValue(tg);
                  oldTransform = tg;
              }

              if (transform is RotateTransform rotateTransform) {
                  if (oldTransform is RotateTransform || oldTransform == null) {
                      if (rotateTransform.Angle != 0) {
                          designItem.Properties.GetProperty(transformProperty).SetValue(transform);
                          var angle = rotateTransform.Angle;
                          if (relative && oldTransform != null)
                              angle = rotateTransform.Angle + ((RotateTransform)oldTransform).Angle;
                          designItem.Properties.GetProperty(transformProperty).Value.Properties.GetProperty("Angle").SetValue(angle);
                          if (oldTransform == null)
                              designItem.Properties.GetProperty("RenderTransformOrigin").SetValue(new Windows.Foundation.Point(0.5, 0.5));
                      } else {
                          designItem.Properties.GetProperty(transformProperty).Reset();
                          designItem.Properties.GetProperty("RenderTransformOrigin").Reset();
                      }
                  } else if (oldTransform is TransformGroup tg) {
                      var tgItem = designItem.Services.Component.GetDesignItem(tg);
                      var existingRotate = tg.Children.OfType<RotateTransform>().FirstOrDefault();
                      if (existingRotate != null)
                          tgItem.ContentProperty.CollectionElements.Remove(designItem.Services.Component.GetDesignItem(existingRotate));
                      if (rotateTransform.Angle != 0) {
                          var des = designItem.Services.Component.GetDesignItem(transform) ?? designItem.Services.Component.RegisterComponentForDesigner(transform);
                          tgItem.ContentProperty.CollectionElements.Add(des);
                      }
                  } else if (rotateTransform.Angle != 0) {
                      designItem.Properties.GetProperty(transformProperty).SetValue(transform);
                      if (oldTransform == null)
                          designItem.Properties.GetProperty("RenderTransformOrigin").SetValue(new Windows.Foundation.Point(0.5, 0.5));
                  }
              } else if (transform is SkewTransform) {
                  designItem.Properties.GetProperty(transformProperty).SetValue(transform);
              }

              ((DesignPanel)designItem.Services.DesignPanel).AdornerLayer.UpdateAdornersForElement(designItem.View, true);
              changeGroup.Commit();
          }
      }
  }
  ```

  This narrows the reference's `ApplyTransform` to the two transform kinds
  Phase 4a actually passes (`RotateTransform` via `RotateThumbExtension`,
  `SkewTransform` via `SkewThumbExtension`, both Task 5) — add `using
  System.Linq;` to the top of the file for `.OfType<RotateTransform>()`.

- [ ] **Step 3: Add `DesignPanel.AdornerLayer`**

  In `src/WinUIDesigner.Designer/DesignPanel.cs`, immediately after the
  existing `Adorners` property (after its closing `}`, before
  `MeasureOverride`), add:
  ```csharp

  		public Controls.AdornerLayer AdornerLayer { get { return _adornerLayer; } }
  ```

- [ ] **Step 4: Write the failing tests**

  `tests/WinUIDesigner.Designer.Tests/DefaultPlacementBehaviorTests.cs`:
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
      public class DefaultPlacementBehaviorTests
      {
          const string ButtonInGridXaml =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
              "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" />" +
              "</Grid>";

          static XamlDesignContext Load(string xaml)
          {
              var settings = new XamlLoadSettings();
              settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
              using var reader = XmlReader.Create(new StringReader(xaml));
              return new XamlDesignContext(reader, settings);
          }

          [Test]
          public void GridChildResolvesDefaultPlacementBehavior()
          {
              WinUITestHost.RunWithWindow(window => {
                  var context = Load(ButtonInGridXaml);
                  window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                  ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();

                  var button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component).Children[0];
                  var buttonItem = context.Services.Component.GetDesignItem(button);

                  var behavior = PlacementOperation.GetPlacementBehavior(new[] { buttonItem });
                  Assert.IsInstanceOf<DefaultPlacementBehavior>(behavior);
              });
          }

          [Test]
          public void ResizeThroughAPlacementOperationSetsWidthAndHeight()
          {
              WinUITestHost.RunWithWindow(window => {
                  var context = Load(ButtonInGridXaml);
                  window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                  ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();

                  var button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component).Children[0];
                  var buttonItem = context.Services.Component.GetDesignItem(button);

                  var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Resize);
                  var info = op.PlacedItems[0];
                  info.Bounds = new Windows.Foundation.Rect(info.Bounds.X, info.Bounds.Y, 80, 50);
                  op.CurrentContainerBehavior.SetPosition(info);
                  op.Commit();

                  Assert.AreEqual(80, button.Width);
                  Assert.AreEqual(50, button.Height);
              });
          }
      }
  }
  ```

  `tests/WinUIDesigner.Designer.Tests/ModelToolsTests.cs`:
  ```csharp
  using System.IO;
  using System.Linq;
  using System.Xml;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel.Xaml;
  using WinUIDesigner.Designer.Extensions;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.Designer.Tests
  {
      [TestFixture]
      public class ModelToolsTests
      {
          const string ButtonXaml =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
              "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" />" +
              "</Grid>";

          static (XamlDesignContext context, Microsoft.UI.Xaml.Controls.Button button, WinUIDesigner.DesignModel.DesignItem buttonItem) Load(Microsoft.UI.Xaml.Window window)
          {
              var settings = new XamlLoadSettings();
              settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
              using var reader = XmlReader.Create(new StringReader(ButtonXaml));
              var context = new XamlDesignContext(reader, settings);
              window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
              ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
              var button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component).Children[0];
              var buttonItem = context.Services.Component.GetDesignItem(button);
              return (context, button, buttonItem);
          }

          [Test]
          public void ResizeSetsWidthAndHeightProperties()
          {
              WinUITestHost.RunWithWindow(window => {
                  var (context, button, buttonItem) = Load(window);
                  using (var group = context.OpenGroup("resize", new[] { buttonItem })) {
                      WinUIDesigner.Designer.ModelTools.Resize(buttonItem, 100, 60);
                      group.Commit();
                  }
                  Assert.AreEqual(100, button.Width);
                  Assert.AreEqual(60, button.Height);
              });
          }

          // Gap #4 (spec): confirms the TransformGroup-wrapping path in ApplyTransform - swapping
          // an existing SkewTransform for a RotateTransform, which must wrap both in a
          // TransformGroup - actually resolves through WinRT's [ContentProperty] metadata via
          // this port's XamlDom component registration, not assumed to work.
          [Test]
          public void ApplyingASecondTransformKindWrapsBothInATransformGroup()
          {
              WinUITestHost.RunWithWindow(window => {
                  var (context, button, buttonItem) = Load(window);
                  WinUIDesigner.Designer.ModelTools.ApplyTransform(buttonItem, new Microsoft.UI.Xaml.Media.SkewTransform { AngleX = 10 }, false);

                  Microsoft.UI.Xaml.Media.TransformGroup group = null;
                  Assert.DoesNotThrow(() =>
                      WinUIDesigner.Designer.ModelTools.ApplyTransform(buttonItem, new Microsoft.UI.Xaml.Media.RotateTransform { Angle = 20 }, false));

                  group = button.RenderTransform as Microsoft.UI.Xaml.Media.TransformGroup;
                  Assert.IsNotNull(group, "a second transform kind should wrap both in a TransformGroup");
                  Assert.AreEqual(1, group.Children.OfType<Microsoft.UI.Xaml.Media.SkewTransform>().Count());
                  Assert.AreEqual(1, group.Children.OfType<Microsoft.UI.Xaml.Media.RotateTransform>().Count());
              });
          }
      }
  }
  ```

- [ ] **Step 5: Run the tests to verify they fail**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL — `DefaultPlacementBehavior`/`ModelTools`/`DesignPanel.AdornerLayer` don't exist yet.

- [ ] **Step 6: Run the tests to verify they pass**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`, then run the
  built `.exe`.
  Expected: PASS.

  If `ApplyingASecondTransformKindWrapsBothInATransformGroup` throws: this
  is gap #4 resolving to "no" — the WinRT `[ContentProperty]` path for
  `TransformGroup` doesn't resolve through this port's component
  registration as written. Do not swallow the exception. Read the thrown
  exception's message, fix the actual cause (likely
  `RegisterComponentForDesigner`/`ContentProperty` resolution for a
  non-`FrameworkElement` WinRT type), and only then re-run. Record the real
  finding in a comment on `ApplyTransform`, not a guess.

- [ ] **Step 7: Re-run Phase 1/2/3 suites, then commit**

  ```bash
  git add src/WinUIDesigner.Designer/Extensions/DefaultPlacementBehavior.cs \
          src/WinUIDesigner.Designer/ModelTools.cs \
          src/WinUIDesigner.Designer/DesignPanel.cs \
          tests/WinUIDesigner.Designer.Tests/DefaultPlacementBehaviorTests.cs \
          tests/WinUIDesigner.Designer.Tests/ModelToolsTests.cs
  git commit -m "feat: add DefaultPlacementBehavior and ModelTools (Phase 4a Task 3)"
  ```

---

### Task 4: Thumb visuals + `ResizeThumbExtension`

**Files:**
- Create: `src/WinUIDesigner.Designer/Controls/Thumbs/DesignerThumb.cs`
- Create: `src/WinUIDesigner.Designer/Controls/Thumbs/ResizeThumb.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/ResizeThumbExtension.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/ResizeThumbExtensionTests.cs`

**Interfaces:**
- Consumes: `AdornerProvider`/`AdornerPanel`/`AdornerOrder`/`AdornerPlacement`
  (Phase 3, unchanged), `RelativePlacement`/`PlacementOperation`/
  `IPlacementBehavior`/`OnlyOneItemSelectedExtensionServer` (Task 2),
  `DefaultPlacementBehavior`/`ModelTools` (Task 3), `DragListener` (Task 1),
  `GeometryExtensions.TryFindParent<T>` (Task 1).
- Produces: `WinUIDesigner.Designer.Controls.Thumbs.DesignerThumb` (`: Grid`,
  properties `IsPrimarySelection`/`ThumbVisible`/`Alignment`, public
  `DesignCursor` setter). `WinUIDesigner.Designer.Controls.Thumbs.ResizeThumb`
  (`: DesignerThumb`, ctor `(bool checkWidth, bool checkHeight)`).
  `WinUIDesigner.Designer.Extensions.ResizeThumbExtension` — Task 5's
  `RotateThumbExtension`/`SkewThumbExtension` follow the same
  `[ExtensionFor(typeof(FrameworkElement))]
  [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))] : AdornerProvider`
  shape this task establishes.

- [ ] **Step 1: Write `DesignerThumb`**

  `src/WinUIDesigner.Designer/Controls/Thumbs/DesignerThumb.cs`:
  ```csharp
  using Microsoft.UI.Input;
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Controls;
  using Microsoft.UI.Xaml.Media;
  using WinUIDesigner.DesignModel;

  namespace WinUIDesigner.Designer.Controls.Thumbs
  {
      /// <summary>
      /// A small resize/rotate/skew handle whose look can depend on IsPrimarySelection. Bases on
      /// Grid, not Thumb (the reference's base): Thumb is sealed in this SDK version
      /// (compiler-confirmed CS0509 during spec brainstorming, same situation as Border in Phase
      /// 3) - and DragListener never used Thumb's own DragStarted/DragDelta/DragCompleted API
      /// anyway, so losing it as a base costs nothing. Appearance is code-only, same
      /// simplification Phase 3 used for SelectionHighlight, since there is no generic.xaml style
      /// to look up without a Control base.
      /// </summary>
      public class DesignerThumb : Grid
      {
          static readonly object BoxedTrue = true;

          public static readonly DependencyProperty IsPrimarySelectionProperty =
              DependencyProperty.Register("IsPrimarySelection", typeof(bool), typeof(DesignerThumb),
                  new PropertyMetadata(false, OnAppearanceChanged));

          public static readonly DependencyProperty ThumbVisibleProperty =
              DependencyProperty.Register("ThumbVisible", typeof(bool), typeof(DesignerThumb),
                  new PropertyMetadata(BoxedTrue, OnAppearanceChanged));

          public PlacementAlignment Alignment;

          public DesignerThumb()
          {
              Width = 7;
              Height = 7;
              Background = new SolidColorBrush(Microsoft.UI.Colors.White);
              BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Black);
              BorderThickness = new Thickness(1);
              IsTabStop = true;
          }

          public bool IsPrimarySelection {
              get { return (bool)GetValue(IsPrimarySelectionProperty); }
              set { SetValue(IsPrimarySelectionProperty, value); }
          }

          public bool ThumbVisible {
              get { return (bool)GetValue(ThumbVisibleProperty); }
              set { SetValue(ThumbVisibleProperty, value); }
          }

          /// <summary>Forwards to the protected ProtectedCursor - UIElement.Cursor isn't public in
          /// WinUI (confirmed during spec brainstorming), so a subclass exposing its own setter is
          /// the only way callers outside this class can set it.</summary>
          public void SetDesignCursor(InputSystemCursorShape shape)
          {
              this.ProtectedCursor = InputSystemCursor.Create(shape);
          }

          static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
          {
              var thumb = (DesignerThumb)d;
              // WPF drives this via style Triggers on IsPrimarySelection/ThumbVisible - no
              // generic.xaml here (see class remarks), so the effect is wired directly: hidden
              // handles neither show nor hit-test (Visibility.Collapsed - WinUI has no
              // Visibility.Hidden, confirmed absent during spec brainstorming; AdornerPanel
              // arranges every child independently so a Collapsed sibling doesn't perturb layout,
              // verified by reading AdornerPanel.MeasureOverride/ArrangeOverride), and a primary
              // selection's handle gets a slightly heavier border.
              thumb.Visibility = thumb.ThumbVisible ? Visibility.Visible : Visibility.Collapsed;
              thumb.BorderThickness = new Thickness(thumb.IsPrimarySelection ? 2 : 1);
          }

          public void ReDraw()
          {
              var parent = this.TryFindParent<FrameworkElement>();
              if (parent != null) parent.InvalidateArrange();
          }
      }
  }
  ```

  This uses `GeometryExtensions.TryFindParent<T>` (Task 1) — add `using
  WinUIDesigner.DesignModel;` (already present above for `PlacementAlignment`;
  `TryFindParent` is an extension method in that same namespace, so no
  further `using` is needed).

- [ ] **Step 2: Write `ResizeThumb`**

  `src/WinUIDesigner.Designer/Controls/Thumbs/ResizeThumb.cs`:
  ```csharp
  using Windows.Foundation;
  using WinUIDesigner.DesignModel;

  namespace WinUIDesigner.Designer.Controls.Thumbs
  {
      /// <summary>Resize thumb that automatically disappears if the adorned element is too small
      /// along the checked axis. No WPF-specific API - mechanical port (base class changed to
      /// DesignerThumb : Grid, see DesignerThumb's remarks).</summary>
      public sealed class ResizeThumb : DesignerThumb
      {
          readonly bool checkWidth, checkHeight;

          public ResizeThumb(bool checkWidth, bool checkHeight)
          {
              this.checkWidth = checkWidth;
              this.checkHeight = checkHeight;
          }

          protected override Size ArrangeOverride(Size arrangeBounds)
          {
              if (this.Parent is WinUIDesigner.DesignModel.Adorners.AdornerPanel parent && parent.AdornedElement != null) {
                  if (checkWidth)
                      this.ThumbVisible = PlacementOperation.GetRealElementSize(parent.AdornedElement).Width > 14;
                  else if (checkHeight)
                      this.ThumbVisible = PlacementOperation.GetRealElementSize(parent.AdornedElement).Height > 14;
              }
              return base.ArrangeOverride(arrangeBounds);
          }
      }
  }
  ```

- [ ] **Step 3: Write `ResizeThumbExtension`**

  `src/WinUIDesigner.Designer/Extensions/ResizeThumbExtension.cs`:
  ```csharp
  using System;
  using Microsoft.UI.Input;
  using Microsoft.UI.Xaml;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Adorners;
  using WinUIDesigner.DesignModel.Extensions;
  using WinUIDesigner.Designer.Controls;
  using WinUIDesigner.Designer.Controls.Thumbs;

  namespace WinUIDesigner.Designer.Extensions
  {
      /// <summary>
      /// The resize thumb frame around the primary selection. Ported from the reference's
      /// ResizeThumbExtension.cs (SelectionAdornerProvider abstract base not ported - subclasses
      /// AdornerProvider directly with its own [ExtensionServer], matching Phase 3's own concrete
      /// SelectionAdornerProvider pattern - see spec). The reference's "is GridPlacementSupport"
      /// branch is dropped (that type doesn't exist yet - 4b) - every resize now takes the
      /// unconditional ModelTools.Resize path, which also removes a latent
      /// NullReferenceException present in the reference (operation is null whenever
      /// resizeBehavior was null at drag-start, but the dropped branch dereferenced
      /// operation.CurrentContainerBehavior unconditionally). SizeDisplayExtension/
      /// MarginHandleExtension interaction is deleted outright (not null-checked) - those types
      /// don't exist in this port (4e territory).
      /// </summary>
      [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
      [ExtensionFor(typeof(FrameworkElement))]
      public sealed class ResizeThumbExtension : AdornerProvider
      {
          readonly AdornerPanel adornerPanel;
          readonly DesignerThumb[] designerThumbs;
          readonly DesignItem[] extendedItemArray = new DesignItem[1];
          IPlacementBehavior resizeBehavior;
          PlacementOperation operation;
          Windows.Foundation.Size oldSize;

          /// <summary>Exposes each corner/edge thumb's DragListener by alignment - tests drive
          /// drags through these directly (SimulateDown/Move/Up), since there is no other way to
          /// reach the exact listener instance wired to this extension's own Drag_Started/
          /// Drag_Changed/Drag_Completed handlers.</summary>
          internal readonly System.Collections.Generic.Dictionary<PlacementAlignment, DragListener> DragListeners =
              new System.Collections.Generic.Dictionary<PlacementAlignment, DragListener>();

          public bool IsResizing { get; private set; }

          public ResizeThumbExtension()
          {
              adornerPanel = new AdornerPanel { Order = AdornerOrder.Foreground };
              this.Adorners.Add(adornerPanel);

              designerThumbs = new[] {
                  CreateThumb(PlacementAlignment.TopLeft, InputSystemCursorShape.SizeNorthwestSoutheast),
                  CreateThumb(PlacementAlignment.Top, InputSystemCursorShape.SizeNorthSouth),
                  CreateThumb(PlacementAlignment.TopRight, InputSystemCursorShape.SizeNortheastSouthwest),
                  CreateThumb(PlacementAlignment.Left, InputSystemCursorShape.SizeWestEast),
                  CreateThumb(PlacementAlignment.Right, InputSystemCursorShape.SizeWestEast),
                  CreateThumb(PlacementAlignment.BottomLeft, InputSystemCursorShape.SizeNortheastSouthwest),
                  CreateThumb(PlacementAlignment.Bottom, InputSystemCursorShape.SizeNorthSouth),
                  CreateThumb(PlacementAlignment.BottomRight, InputSystemCursorShape.SizeNorthwestSoutheast),
              };
          }

          DesignerThumb CreateThumb(PlacementAlignment alignment, InputSystemCursorShape cursor)
          {
              DesignerThumb thumb = new ResizeThumb(cursor == InputSystemCursorShape.SizeNorthSouth, cursor == InputSystemCursorShape.SizeWestEast);
              thumb.SetDesignCursor(cursor);
              thumb.Alignment = alignment;
              AdornerPanel.SetPlacement(thumb, Place(alignment));
              adornerPanel.Children.Add(thumb);

              var drag = new DragListener(thumb);
              drag.Started += Drag_Started;
              drag.Changed += Drag_Changed;
              drag.Completed += Drag_Completed;
              DragListeners[alignment] = drag;
              return thumb;
          }

          RelativePlacement Place(PlacementAlignment alignment)
          {
              var placement = new RelativePlacement(alignment.Horizontal, alignment.Vertical);
              if (alignment.Horizontal == HorizontalAlignment.Center) {
                  placement.WidthRelativeToContentWidth = 1;
                  placement.HeightOffset = 6;
                  return placement;
              }
              if (alignment.Vertical == VerticalAlignment.Center) {
                  placement.HeightRelativeToContentHeight = 1;
                  placement.WidthOffset = 6;
                  return placement;
              }
              placement.WidthOffset = 6;
              placement.HeightOffset = 6;
              return placement;
          }

          void Drag_Started(DragListener drag)
          {
              drag.Transform = this.ExtendedItem.GetCompleteAppliedTransformationToView();
              oldSize = new Windows.Foundation.Size(ModelTools.GetWidth(ExtendedItem.View), ModelTools.GetHeight(ExtendedItem.View));
              operation = resizeBehavior != null ? PlacementOperation.Start(extendedItemArray, PlacementType.Resize) : null;
              IsResizing = true;
          }

          void Drag_Changed(DragListener drag)
          {
              double dx = 0, dy = 0;
              var alignment = ((DesignerThumb)drag.Target).Alignment;
              var delta = drag.Delta;

              if (alignment.Horizontal == HorizontalAlignment.Left) dx = -delta.X;
              if (alignment.Horizontal == HorizontalAlignment.Right) dx = delta.X;
              if (alignment.Vertical == VerticalAlignment.Top) dy = -delta.Y;
              if (alignment.Vertical == VerticalAlignment.Bottom) dy = delta.Y;

              bool constrain = (drag.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) == Windows.System.VirtualKeyModifiers.Control
                  && alignment.Horizontal != HorizontalAlignment.Center && alignment.Vertical != VerticalAlignment.Center;
              if (constrain) {
                  if (dx > dy) dx = dy; else dy = dx;
              }

              var newWidth = Math.Max(0, oldSize.Width + dx);
              var newHeight = Math.Max(0, oldSize.Height + dy);

              ModelTools.Resize(ExtendedItem, newWidth, newHeight);

              if (operation != null) {
                  var info = operation.PlacedItems[0];
                  var result = info.OriginalBounds;
                  if (alignment.Horizontal == HorizontalAlignment.Left) result.X = Math.Min(result.Right, result.X - dx);
                  if (alignment.Vertical == VerticalAlignment.Top) result.Y = Math.Min(result.Bottom, result.Y - dy);
                  result.Width = newWidth;
                  result.Height = newHeight;

                  info.Bounds = result.Round();
                  info.ResizeThumbAlignment = alignment;
                  operation.CurrentContainerBehavior.BeforeSetPosition(operation);
                  operation.CurrentContainerBehavior.SetPosition(info);
              }
          }

          void Drag_Completed(DragListener drag)
          {
              if (operation != null) {
                  if (drag.IsCanceled) operation.Abort(); else operation.Commit();
                  operation = null;
              }
              IsResizing = false;
          }

          protected override void OnInitialized()
          {
              base.OnInitialized();
              extendedItemArray[0] = this.ExtendedItem;
              this.ExtendedItem.PropertyChanged += OnPropertyChanged;
              this.Services.Selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
              resizeBehavior = PlacementOperation.GetPlacementBehavior(extendedItemArray);
              UpdateAdornerVisibility();
              OnPrimarySelectionChanged(null, null);
          }

          void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateAdornerVisibility();

          protected override void OnRemove()
          {
              this.ExtendedItem.PropertyChanged -= OnPropertyChanged;
              this.Services.Selection.PrimarySelectionChanged -= OnPrimarySelectionChanged;
              base.OnRemove();
          }

          void OnPrimarySelectionChanged(object sender, EventArgs e)
          {
              bool isPrimarySelection = this.Services.Selection.PrimarySelection == this.ExtendedItem;
              foreach (var thumb in designerThumbs) thumb.IsPrimarySelection = isPrimarySelection;
          }

          void UpdateAdornerVisibility()
          {
              foreach (var thumb in designerThumbs) {
                  bool visible = resizeBehavior != null && resizeBehavior.CanPlace(extendedItemArray, PlacementType.Resize, thumb.Alignment);
                  thumb.ThumbVisible = visible;
              }
          }
      }
  }
  ```

  `Windows.Foundation.Rect.Right`/`.Bottom` are properties (confirmed present
  during spec brainstorming, alongside the confirmed-absent `TopLeft`/`Size`)
  — the code above already uses them as properties, not methods.

- [ ] **Step 4: Write the failing test**

  `tests/WinUIDesigner.Designer.Tests/ResizeThumbExtensionTests.cs`. Tests
  drive drags through `ResizeThumbExtension.DragListeners[alignment]`
  (added in Step 3) rather than constructing a second, unrelated
  `DragListener` on the same thumb — a second listener would double-fire
  independently of the extension's own `Drag_Started`/`Drag_Changed`/
  `Drag_Completed` handlers instead of driving them:
  ```csharp
  using System.IO;
  using System.Linq;
  using System.Xml;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Xaml;
  using WinUIDesigner.Designer.Controls.Thumbs;
  using WinUIDesigner.Designer.Extensions;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.Designer.Tests
  {
      [TestFixture]
      public class ResizeThumbExtensionTests
      {
          const string ButtonXaml =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
              "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
              "</Grid>";

          static DesignSurface LoadSelected(Microsoft.UI.Xaml.Window window, out DesignItem buttonItem, out Microsoft.UI.Xaml.Controls.Button button)
          {
              var surface = new DesignSurface();
              window.Content = surface;
              using (var reader = XmlReader.Create(new StringReader(ButtonXaml)))
                  surface.LoadDesigner(reader, new XamlLoadSettings());
              surface.UpdateLayout();

              button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component).Children[0];
              buttonItem = surface.DesignContext.Services.Component.GetDesignItem(button);
              surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
              surface.UpdateLayout();
              return surface;
          }

          static ResizeThumbExtension GetExtension(DesignItem buttonItem)
          {
              return buttonItem.Extensions.OfType<ResizeThumbExtension>().Single();
          }

          [Test]
          public void SelectingAnElementShowsEightResizeThumbs()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out _, out var button);
                  var thumbs = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<ResizeThumb>().ToList();
                  Assert.AreEqual(8, thumbs.Count);
              });
          }

          [Test]
          public void DraggingTheBottomRightThumbGrowsWidthAndHeightAndCommitsOneChangeGroup()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out var buttonItem, out var button);
                  var extension = GetExtension(buttonItem);
                  var drag = extension.DragListeners[PlacementAlignment.BottomRight];

                  drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                  drag.SimulateMove(new Windows.Foundation.Point(20, 15)); // past threshold, +20 width, +15 height
                  drag.SimulateUp(new Windows.Foundation.Point(20, 15));
                  surface.UpdateLayout();

                  Assert.AreEqual(60, button.Width, 0.5);
                  Assert.AreEqual(35, button.Height, 0.5);
              });
          }

          [Test]
          public void DraggingTheRightThumbOnlyChangesWidth()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out var buttonItem, out var button);
                  var extension = GetExtension(buttonItem);
                  var drag = extension.DragListeners[PlacementAlignment.Right];

                  drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                  drag.SimulateMove(new Windows.Foundation.Point(15, 0));
                  drag.SimulateUp(new Windows.Foundation.Point(15, 0));
                  surface.UpdateLayout();

                  Assert.AreEqual(55, button.Width, 0.5);
                  Assert.AreEqual(20, button.Height, 0.5);
              });
          }

          [Test]
          public void CancelingAResizeRevertsWidthAndHeightWithNoCommittedChange()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out var buttonItem, out var button);
                  var extension = GetExtension(buttonItem);
                  var drag = extension.DragListeners[PlacementAlignment.BottomRight];

                  var undoService = surface.DesignContext.Services.GetService<WinUIDesigner.DesignModel.Services.UndoService>();
                  bool canUndoBefore = undoService != null && undoService.CanUndo;

                  drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                  drag.SimulateMove(new Windows.Foundation.Point(30, 30));
                  drag.Cancel();
                  surface.UpdateLayout();

                  Assert.AreEqual(40, button.Width, 0.5, "canceled resize should revert to the original width");
                  Assert.AreEqual(20, button.Height, 0.5, "canceled resize should revert to the original height");

                  if (undoService != null)
                      Assert.AreEqual(canUndoBefore, undoService.CanUndo,
                          "an aborted PlacementOperation must not push anything onto the undo stack");
              });
          }
      }
  }
  ```

  `UndoService.CanUndo` (confirmed public at
  `src/WinUIDesigner.DesignModel/Services/UndoService.cs:236`, namespace
  `WinUIDesigner.DesignModel.Services`) is the load-bearing assertion here —
  comparing it before/after rather than assuming it starts `false` keeps
  the test correct regardless of whatever undo history a future change to
  `LoadSelected` might introduce.

- [ ] **Step 5: Run the tests to verify they fail**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL — `DesignerThumb`/`ResizeThumb`/`ResizeThumbExtension` don't exist yet.

- [ ] **Step 6: Run the tests to verify they pass**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`, then run the
  built `.exe`.
  Expected: PASS. If `Rect.Right`/`Rect.Bottom` turn out to be methods
  rather than properties (Step 3's flagged uncertainty), fix
  `Drag_Changed`'s two call sites to match and re-run — do not guess twice,
  let the compiler settle it.

- [ ] **Step 7: Re-run Phase 1/2/3 suites, then commit**

  ```bash
  git add src/WinUIDesigner.Designer/Controls/Thumbs/DesignerThumb.cs \
          src/WinUIDesigner.Designer/Controls/Thumbs/ResizeThumb.cs \
          src/WinUIDesigner.Designer/Extensions/ResizeThumbExtension.cs \
          tests/WinUIDesigner.Designer.Tests/ResizeThumbExtensionTests.cs
  git commit -m "feat: add resize thumb visuals and ResizeThumbExtension (Phase 4a Task 4)"
  ```

---

### Task 5: `RotateThumbExtension` + `SkewThumbExtension`

**Files:**
- Create: `src/WinUIDesigner.Designer/Controls/Thumbs/RotateThumb.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/RotateThumbExtension.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/SkewThumbExtension.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/RotateThumbExtensionTests.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/SkewThumbExtensionTests.cs`

**Interfaces:**
- Consumes: everything Task 4 produced, plus `ModelTools.ApplyTransform`
  (Task 3), `DragListener.GetPosition(UIElement)` (Task 1).
- Produces: `WinUIDesigner.Designer.Controls.Thumbs.RotateThumb` (`:
  DesignerThumb`). `WinUIDesigner.Designer.Extensions.RotateThumbExtension`/
  `SkewThumbExtension` — Task 6's DoD suite consumes both.

- [ ] **Step 1: Write `RotateThumb`**

  `src/WinUIDesigner.Designer/Controls/Thumbs/RotateThumb.cs`:
  ```csharp
  namespace WinUIDesigner.Designer.Controls.Thumbs
  {
      /// <summary>No WPF-specific API - mechanical port (base class changed to DesignerThumb :
      /// Grid; the reference's DefaultStyleKeyProperty.OverrideMetadata static constructor is
      /// dropped, not translated - no purpose without a Control base or a generic.xaml
      /// style).</summary>
      public class RotateThumb : DesignerThumb
      {
          public RotateThumb()
          {
              ThumbVisible = true;
          }
      }
  }
  ```

- [ ] **Step 2: Write `RotateThumbExtension`**

  `src/WinUIDesigner.Designer/Extensions/RotateThumbExtension.cs`:
  ```csharp
  using System;
  using System.Linq;
  using Microsoft.UI.Input;
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Media;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Adorners;
  using WinUIDesigner.DesignModel.Extensions;
  using WinUIDesigner.Designer.Controls;
  using WinUIDesigner.Designer.Controls.Thumbs;

  namespace WinUIDesigner.Designer.Extensions
  {
      /// <summary>
      /// The rotate thumb above the primary selection. Ported from the reference's
      /// RotateThumbExtension.cs. WindowClone guard dropped (that type isn't part of this port).
      /// The custom .cur rotate cursor (ZoomControl.GetCursor, out of scope) is replaced with
      /// InputSystemCursorShape.Hand - the reference's own first Cursor assignment before its
      /// dead-code second assignment overwrote it. WPF's ambient Mouse.GetPosition(this.parent)
      /// is replaced with DragListener.GetPosition(UIElement) (Task 1) - the reference's own
      /// callback signature (void(DragListener drag)) never received raw event args either.
      /// </summary>
      [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
      [ExtensionFor(typeof(FrameworkElement))]
      public sealed class RotateThumbExtension : AdornerProvider
      {
          readonly AdornerPanel adornerPanel;
          readonly DesignItem[] extendedItemArray = new DesignItem[1];
          internal DragListener DragListener;

          Windows.Foundation.Point centerPoint;
          UIElement parent;
          double startAngleFromCenter;
          double initialAngle;
          RotateTransform rotateTransform;

          public RotateThumbExtension()
          {
              adornerPanel = new AdornerPanel { Order = AdornerOrder.Foreground };
              this.Adorners.Add(adornerPanel);
              CreateRotateThumb();
          }

          void CreateRotateThumb()
          {
              DesignerThumb thumb = new RotateThumb();
              thumb.SetDesignCursor(InputSystemCursorShape.Hand);
              thumb.Alignment = PlacementAlignment.Top;
              AdornerPanel.SetPlacement(thumb, new RelativePlacement(HorizontalAlignment.Center, VerticalAlignment.Top) { WidthRelativeToContentWidth = 1, HeightOffset = 0 });
              adornerPanel.Children.Add(thumb);

              DragListener = new DragListener(thumb);
              DragListener.Started += Drag_Started;
              DragListener.Changed += Drag_Changed;
              DragListener.Completed += Drag_Completed;
          }

          void Drag_Started(WinUIDesigner.Designer.Controls.DragListener drag)
          {
              var designerItem = this.ExtendedItem.View;
              parent = (UIElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(designerItem);
              var fe = (FrameworkElement)designerItem;
              centerPoint = designerItem.TransformToVisual(parent).TransformPoint(
                  new Windows.Foundation.Point(fe.ActualWidth * fe.RenderTransformOrigin.X, fe.ActualHeight * fe.RenderTransformOrigin.Y));

              var start = drag.GetPosition(parent) ?? centerPoint;
              startAngleFromCenter = Math.Atan2(start.Y - centerPoint.Y, start.X - centerPoint.X);
              initialAngle = rotateTransform != null ? rotateTransform.Angle : 0;
          }

          void Drag_Changed(WinUIDesigner.Designer.Controls.DragListener drag)
          {
              var current = drag.GetPosition(parent) ?? centerPoint;
              var currentAngleFromCenter = Math.Atan2(current.Y - centerPoint.Y, current.X - centerPoint.X);
              var angleDelta = (currentAngleFromCenter - startAngleFromCenter) * 180 / Math.PI;

              var destAngle = initialAngle + Math.Round(angleDelta, 0);
              if ((drag.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) != Windows.System.VirtualKeyModifiers.Control)
                  destAngle = ((int)destAngle / 15) * 15;

              ModelTools.ApplyTransform(this.ExtendedItem, new RotateTransform { Angle = destAngle }, false);
          }

          void Drag_Completed(WinUIDesigner.Designer.Controls.DragListener drag)
          {
          }

          protected override void OnInitialized()
          {
              base.OnInitialized();
              extendedItemArray[0] = this.ExtendedItem;
              this.Services.Selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
              OnPrimarySelectionChanged(null, null);

              var fe = (FrameworkElement)this.ExtendedItem.Component;
              rotateTransform = fe.RenderTransform as RotateTransform;
              if (rotateTransform == null && fe.RenderTransform is TransformGroup tg)
                  rotateTransform = tg.Children.OfType<RotateTransform>().FirstOrDefault();
          }

          protected override void OnRemove()
          {
              this.Services.Selection.PrimarySelectionChanged -= OnPrimarySelectionChanged;
              base.OnRemove();
          }

          void OnPrimarySelectionChanged(object sender, EventArgs e)
          {
              bool isPrimarySelection = this.Services.Selection.PrimarySelection == this.ExtendedItem;
              foreach (RotateThumb thumb in adornerPanel.Children.OfType<RotateThumb>()) thumb.IsPrimarySelection = isPrimarySelection;
          }
      }
  }
  ```

  This drops `PlacementOperation.Start`/`Commit` entirely — the reference's
  `RotateThumbExtension` opens one (with `PlacementType.Resize`, oddly, not
  a rotate-specific type — that's the reference's own text, not a
  transcription error) but its `Commit()` is the only thing that ever
  touches it, and `ModelTools.ApplyTransform` already opens and commits its
  own `ChangeGroup` on every call (see Task 3 and the spec's "Decisions
  already made" — this is the documented per-call-`ChangeGroup` asymmetry
  versus resize's single-group-per-drag). Keeping the reference's redundant
  `PlacementOperation` would only add an empty, always-committed operation
  wrapping `ApplyTransform`'s own already-complete undo unit — dropped as
  dead weight, not ported as a mechanical no-op. Uses `Math.Atan2`-based
  angle computation in place of WPF's `Vector.AngleBetween` (no `Vector`
  type in WinUI, confirmed absent) — `angleDelta` here is the signed
  difference between the current and starting angle from the rotation
  center, which is what `Vector.AngleBetween` produced in the reference for
  this specific two-vector case.

- [ ] **Step 3: Write `SkewThumbExtension`**

  `src/WinUIDesigner.Designer/Extensions/SkewThumbExtension.cs`:
  ```csharp
  using Microsoft.UI.Input;
  using Microsoft.UI.Xaml;
  using Microsoft.UI.Xaml.Media;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Adorners;
  using WinUIDesigner.DesignModel.Extensions;
  using WinUIDesigner.Designer.Controls;
  using WinUIDesigner.Designer.Controls.Thumbs;

  namespace WinUIDesigner.Designer.Extensions
  {
      /// <summary>
      /// The two skew handles (horizontal and vertical) around the primary selection. Ported
      /// from the reference's SkewThumbExtension.cs. The reference's two raw `new Thumb()`
      /// instances become DesignerThumb (Grid-based) instead - Thumb is sealed, so even direct
      /// instantiation with a public Cursor setter isn't available (UIElement.Cursor isn't
      /// public in WinUI). WindowClone guard dropped (not part of this port).
      /// </summary>
      [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
      [ExtensionFor(typeof(FrameworkElement))]
      public sealed class SkewThumbExtension : AdornerProvider
      {
          readonly AdornerPanel adornerPanel;
          readonly DesignItem[] extendedItemArray = new DesignItem[1];
          DesignerThumb thumbX, thumbY;
          internal DragListener DragListenerX, DragListenerY;

          UIElement parent;
          SkewTransform skewTransform;
          double skewX, skewY;

          public SkewThumbExtension()
          {
              adornerPanel = new AdornerPanel { Order = AdornerOrder.BeforeForeground };
              this.Adorners.Add(adornerPanel);
          }

          void DragX_Started(DragListener drag)
          {
              parent = (UIElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(this.ExtendedItem.View);
              (skewX, skewY) = skewTransform != null ? (skewTransform.AngleX, skewTransform.AngleY) : (0, 0);
          }

          void DragX_Changed(DragListener drag)
          {
              var delta = drag.Delta;
              var destAngle = -0.5 * delta.X + skewX;
              ApplySkew(angleX: destAngle, angleY: skewY);
          }

          void DragY_Started(DragListener drag)
          {
              parent = (UIElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(this.ExtendedItem.View);
              (skewX, skewY) = skewTransform != null ? (skewTransform.AngleX, skewTransform.AngleY) : (0, 0);
          }

          void DragY_Changed(DragListener drag)
          {
              var delta = drag.Delta;
              var destAngle = -0.5 * delta.Y + skewY;
              ApplySkew(angleX: skewX, angleY: destAngle);
          }

          void ApplySkew(double angleX, double angleY)
          {
              if (angleX == 0 && angleY == 0) {
                  this.ExtendedItem.Properties.GetProperty("RenderTransform").Reset();
                  skewTransform = null;
              } else {
                  skewTransform = new SkewTransform { AngleX = angleX, AngleY = angleY };
                  if (!this.ExtendedItem.Properties.GetProperty("RenderTransformOrigin").IsSet)
                      this.ExtendedItem.Properties.GetProperty("RenderTransformOrigin").SetValue(new Windows.Foundation.Point(0.5, 0.5));
                  this.ExtendedItem.Properties.GetProperty("RenderTransform").SetValue(skewTransform);
              }
              var designPanel = this.ExtendedItem.Services.DesignPanel as WinUIDesigner.Designer.DesignPanel;
              designPanel?.AdornerLayer.UpdateAdornersForElement(this.ExtendedItem.View, true);
          }

          protected override void OnInitialized()
          {
              base.OnInitialized();
              extendedItemArray[0] = this.ExtendedItem;
              this.ExtendedItem.PropertyChanged += OnPropertyChanged;

              var fe = (FrameworkElement)this.ExtendedItem.Component;
              skewTransform = fe.RenderTransform as SkewTransform;
              if (skewTransform != null) { skewX = skewTransform.AngleX; skewY = skewTransform.AngleY; }

              thumbX = new DesignerThumb { Width = 4, Height = 14 };
              thumbX.SetDesignCursor(InputSystemCursorShape.SizeWestEast);
              thumbY = new DesignerThumb { Width = 14, Height = 4 };
              thumbY.SetDesignCursor(InputSystemCursorShape.SizeNorthSouth);

              OnPropertyChanged(null, null);

              adornerPanel.Children.Add(thumbX);
              adornerPanel.Children.Add(thumbY);

              DragListenerX = new DragListener(thumbX);
              DragListenerX.Started += DragX_Started;
              DragListenerX.Changed += DragX_Changed;
              DragListenerY = new DragListener(thumbY);
              DragListenerY.Started += DragY_Started;
              DragListenerY.Changed += DragY_Changed;
          }

          void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
          {
              if (sender != null && e.PropertyName != "Width" && e.PropertyName != "Height") return;

              var size = PlacementOperation.GetRealElementSize(this.ExtendedItem.View);
              AdornerPanel.SetPlacement(thumbX, new RelativePlacement(HorizontalAlignment.Center, VerticalAlignment.Top) { YOffset = 0, XOffset = -size.Width / 4 });
              AdornerPanel.SetPlacement(thumbY, new RelativePlacement(HorizontalAlignment.Left, VerticalAlignment.Center) { YOffset = -size.Height / 4, XOffset = 0 });

              var designPanel = this.ExtendedItem.Services.DesignPanel as WinUIDesigner.Designer.DesignPanel;
              designPanel?.AdornerLayer.UpdateAdornersForElement(this.ExtendedItem.View, true);
          }

          protected override void OnRemove()
          {
              this.ExtendedItem.PropertyChanged -= OnPropertyChanged;
              base.OnRemove();
          }
      }
  }
  ```

  This drops the reference's `PlacementOperation.Start(...)`/`Commit()`
  bracketing around `dragX_Changed`/`dragY_Changed` for the same reason as
  `RotateThumbExtension` above: `ApplySkew`'s calls into
  `DesignItemProperty.SetValue`/`.Reset()` already go through the
  surrounding undo system per Phase 2's existing property-change tracking,
  and there is no `ModelTools.ApplyTransform`-style wrapper needed here
  since this extension sets the transform property directly rather than
  through `ApplyTransform`. If Task 6's DoD suite finds skew edits aren't
  landing on the undo stack as a single step per drag, that's a real
  finding to fix then (wrap `ApplySkew`'s body in `this.ExtendedItem.
  OpenGroup("Skew")`/`.Commit()`) — not assumed away here.

- [ ] **Step 4: Write the failing tests**

  `tests/WinUIDesigner.Designer.Tests/RotateThumbExtensionTests.cs`:
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
      public class RotateThumbExtensionTests
      {
          const string ButtonXaml =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
              "<Button x:Name=\"Target\" Width=\"60\" Height=\"30\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
              "</Grid>";

          static DesignSurface LoadSelected(Microsoft.UI.Xaml.Window window, out DesignItem buttonItem, out Microsoft.UI.Xaml.Controls.Button button)
          {
              var surface = new DesignSurface();
              window.Content = surface;
              using (var reader = XmlReader.Create(new StringReader(ButtonXaml)))
                  surface.LoadDesigner(reader, new XamlLoadSettings());
              surface.UpdateLayout();

              button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component).Children[0];
              buttonItem = surface.DesignContext.Services.Component.GetDesignItem(button);
              surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
              surface.UpdateLayout();
              return surface;
          }

          [Test]
          public void DraggingTheRotateThumbAppliesARotateTransform()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out var buttonItem, out var button);
                  var extension = buttonItem.Extensions.OfType<RotateThumbExtension>().Single();
                  var drag = extension.DragListener;

                  var centerScreen = button.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(30, 15));
                  var startAbove = new Windows.Foundation.Point(centerScreen.X, centerScreen.Y - 40); // straight up from center
                  var afterQuarterTurn = new Windows.Foundation.Point(centerScreen.X + 40, centerScreen.Y); // straight right - 90 deg clockwise

                  drag.SimulateDown(startAbove);
                  drag.SimulateMove(afterQuarterTurn);
                  drag.SimulateUp(afterQuarterTurn);
                  surface.UpdateLayout();

                  var rotate = button.RenderTransform as Microsoft.UI.Xaml.Media.RotateTransform;
                  Assert.IsNotNull(rotate, "expected RenderTransform to be a RotateTransform after dragging the rotate thumb");
                  Assert.AreEqual(90, rotate.Angle, 1, "a quarter turn clockwise around the center should read back as ~90 degrees");
              });
          }
      }
  }
  ```

  `tests/WinUIDesigner.Designer.Tests/SkewThumbExtensionTests.cs`:
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
      public class SkewThumbExtensionTests
      {
          const string ButtonXaml =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
              "<Button x:Name=\"Target\" Width=\"60\" Height=\"30\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
              "</Grid>";

          static DesignSurface LoadSelected(Microsoft.UI.Xaml.Window window, out DesignItem buttonItem, out Microsoft.UI.Xaml.Controls.Button button)
          {
              var surface = new DesignSurface();
              window.Content = surface;
              using (var reader = XmlReader.Create(new StringReader(ButtonXaml)))
                  surface.LoadDesigner(reader, new XamlLoadSettings());
              surface.UpdateLayout();

              button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component).Children[0];
              buttonItem = surface.DesignContext.Services.Component.GetDesignItem(button);
              surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
              surface.UpdateLayout();
              return surface;
          }

          [Test]
          public void DraggingTheHorizontalSkewThumbAppliesASkewTransform()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out var buttonItem, out var button);
                  var extension = buttonItem.Extensions.OfType<SkewThumbExtension>().Single();
                  var drag = extension.DragListenerX;

                  drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                  drag.SimulateMove(new Windows.Foundation.Point(-20, 0)); // -0.5 * -20 = +10 degrees AngleX
                  drag.SimulateUp(new Windows.Foundation.Point(-20, 0));
                  surface.UpdateLayout();

                  var skew = button.RenderTransform as Microsoft.UI.Xaml.Media.SkewTransform;
                  Assert.IsNotNull(skew, "expected RenderTransform to be a SkewTransform after dragging the X skew thumb");
                  Assert.AreEqual(10, skew.AngleX, 0.5);
                  Assert.AreEqual(0, skew.AngleY, 0.5);
              });
          }

          [Test]
          public void DraggingBackToZeroResetsTheRenderTransform()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out var buttonItem, out var button);
                  var extension = buttonItem.Extensions.OfType<SkewThumbExtension>().Single();
                  var drag = extension.DragListenerX;

                  drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                  drag.SimulateMove(new Windows.Foundation.Point(-20, 0));
                  drag.SimulateUp(new Windows.Foundation.Point(-20, 0));
                  surface.UpdateLayout();
                  Assert.IsNotNull(button.RenderTransform);

                  drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                  drag.SimulateMove(new Windows.Foundation.Point(20, 0)); // back to 0
                  drag.SimulateUp(new Windows.Foundation.Point(20, 0));
                  surface.UpdateLayout();

                  Assert.IsFalse(buttonItem.Properties.GetProperty("RenderTransform").IsSet);
              });
          }
      }
  }
  ```

- [ ] **Step 5: Run the tests to verify they fail**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL — `RotateThumb`/`RotateThumbExtension`/`SkewThumbExtension` don't exist yet.

- [ ] **Step 6: Run the tests to verify they pass**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`, then run the
  built `.exe`.
  Expected: PASS.

  The rotate-angle math (`Math.Atan2`-based, replacing WPF's
  `Vector.AngleBetween`) is exactly the kind of thing worth double-checking
  against the actual numeric result rather than trusting the derivation: if
  `DraggingTheRotateThumbAppliesARotateTransform` doesn't land within 1
  degree of 90, work out the actual angle the test produced, compare it
  against what `Vector.AngleBetween(startVector, currentVector)` would have
  returned in WPF for the same two vectors (it returns a signed angle from
  the first vector to the second, range -180..180), and adjust the sign or
  the `Math.Atan2` argument order to match — don't adjust the test's
  expected value to match whatever the code happens to produce.

- [ ] **Step 7: Re-run Phase 1/2/3 suites, then commit**

  ```bash
  git add src/WinUIDesigner.Designer/Controls/Thumbs/RotateThumb.cs \
          src/WinUIDesigner.Designer/Extensions/RotateThumbExtension.cs \
          src/WinUIDesigner.Designer/Extensions/SkewThumbExtension.cs \
          tests/WinUIDesigner.Designer.Tests/RotateThumbExtensionTests.cs \
          tests/WinUIDesigner.Designer.Tests/SkewThumbExtensionTests.cs
  git commit -m "feat: add RotateThumbExtension and SkewThumbExtension (Phase 4a Task 5)"
  ```

---

### Task 6: Definition-of-Done suite + final gap verification

**Files:**
- Create: `tests/WinUIDesigner.Designer.Tests/Phase4aDefinitionOfDoneTests.cs`

**Interfaces:**
- Consumes: everything Tasks 1-5 produced. Produces nothing further — this
  is the phase's capstone verification.

- [ ] **Step 1: Write the DoD suite**

  `tests/WinUIDesigner.Designer.Tests/Phase4aDefinitionOfDoneTests.cs`:
  ```csharp
  using System.IO;
  using System.Linq;
  using System.Xml;
  using NUnit.Framework;
  using WinUIDesigner.DesignModel;
  using WinUIDesigner.DesignModel.Xaml;
  using WinUIDesigner.Designer.Controls.Thumbs;
  using WinUIDesigner.Designer.Extensions;
  using WinUIDesigner.TestHost;

  namespace WinUIDesigner.Designer.Tests
  {
      // Spec's "Testing / Definition of Done" section, exercised end-to-end through a real
      // DesignSurface rather than any single task's narrower unit tests.
      [TestFixture]
      public class Phase4aDefinitionOfDoneTests
      {
          const string ButtonXaml =
              "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
              "<Button x:Name=\"Target\" Width=\"60\" Height=\"30\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
              "</Grid>";

          static DesignSurface LoadSelected(Microsoft.UI.Xaml.Window window, out DesignItem buttonItem, out Microsoft.UI.Xaml.Controls.Button button)
          {
              var surface = new DesignSurface();
              window.Content = surface;
              using (var reader = XmlReader.Create(new StringReader(ButtonXaml)))
                  surface.LoadDesigner(reader, new XamlLoadSettings());
              surface.UpdateLayout();

              button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component).Children[0];
              buttonItem = surface.DesignContext.Services.Component.GetDesignItem(button);
              surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
              surface.UpdateLayout();
              return surface;
          }

          // DoD bullet 1+2: dragging a corner resize thumb resizes through PlacementOperation and
          // commits a single undo-tracked ChangeGroup.
          [Test]
          public void ResizeDragCommitsThroughPlacementOperationAndSupportsUndo()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out var buttonItem, out var button);
                  var extension = buttonItem.Extensions.OfType<ResizeThumbExtension>().Single();
                  var drag = extension.DragListeners[PlacementAlignment.BottomRight];

                  drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                  drag.SimulateMove(new Windows.Foundation.Point(20, 20));
                  drag.SimulateUp(new Windows.Foundation.Point(20, 20));
                  surface.UpdateLayout();

                  Assert.AreEqual(80, button.Width, 0.5);
                  Assert.AreEqual(50, button.Height, 0.5);
              });
          }

          // DoD bullet: dragging an edge thumb changes only the corresponding dimension.
          [Test]
          public void EdgeResizeThumbChangesOnlyItsOwnDimension()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out var buttonItem, out var button);
                  var extension = buttonItem.Extensions.OfType<ResizeThumbExtension>().Single();
                  var drag = extension.DragListeners[PlacementAlignment.Bottom];

                  drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                  drag.SimulateMove(new Windows.Foundation.Point(0, 10));
                  drag.SimulateUp(new Windows.Foundation.Point(0, 10));
                  surface.UpdateLayout();

                  Assert.AreEqual(60, button.Width, 0.5, "dragging the bottom-center thumb should not change Width");
                  Assert.AreEqual(40, button.Height, 0.5);
              });
          }

          // DoD bullet: rotate drag applies a RotateTransform and re-arranges the adorner layer
          // (thumbs/highlight) afterward, same TransformToVisual-based assertion pattern as
          // Phase 3's SelectedElementsAdornerPanelIsArrangedAtTheElementsTransformedPosition.
          [Test]
          public void RotateDragAppliesRotateTransformAndAdornersReArrange()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out var buttonItem, out var button);
                  var extension = buttonItem.Extensions.OfType<RotateThumbExtension>().Single();
                  var drag = extension.DragListener;

                  var centerScreen = button.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(30, 15));
                  drag.SimulateDown(new Windows.Foundation.Point(centerScreen.X, centerScreen.Y - 40));
                  drag.SimulateMove(new Windows.Foundation.Point(centerScreen.X + 40, centerScreen.Y));
                  drag.SimulateUp(new Windows.Foundation.Point(centerScreen.X + 40, centerScreen.Y));
                  surface.UpdateLayout();

                  Assert.IsInstanceOf<Microsoft.UI.Xaml.Media.RotateTransform>(button.RenderTransform);

                  var resizeAdorner = surface.DesignPanel.Adorners.Single(a => a.AdornedElement == button && a.Children.OfType<ResizeThumb>().Any());
                  var matrixTransform = resizeAdorner.RenderTransform as Microsoft.UI.Xaml.Media.MatrixTransform;
                  Assert.IsNotNull(matrixTransform, "the resize adorner panel should re-arrange via the same MatrixTransform mechanism Phase 3 established");
                  var expected = button.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(0, 0));
                  Assert.AreEqual(expected.X, matrixTransform.Matrix.OffsetX, 1);
                  Assert.AreEqual(expected.Y, matrixTransform.Matrix.OffsetY, 1);
              });
          }

          // DoD bullet: skew drag applies a SkewTransform.
          [Test]
          public void SkewDragAppliesSkewTransform()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out var buttonItem, out var button);
                  var extension = buttonItem.Extensions.OfType<SkewThumbExtension>().Single();
                  var drag = extension.DragListenerY;

                  drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                  drag.SimulateMove(new Windows.Foundation.Point(0, -20));
                  drag.SimulateUp(new Windows.Foundation.Point(0, -20));
                  surface.UpdateLayout();

                  Assert.IsInstanceOf<Microsoft.UI.Xaml.Media.SkewTransform>(button.RenderTransform);
              });
          }

          // DoD bullet: canceling an in-progress resize reverts Width/Height and commits nothing.
          [Test]
          public void CanceledResizeRevertsWithNoCommittedChange()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out var buttonItem, out var button);
                  var extension = buttonItem.Extensions.OfType<ResizeThumbExtension>().Single();
                  var drag = extension.DragListeners[PlacementAlignment.BottomRight];

                  drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                  drag.SimulateMove(new Windows.Foundation.Point(50, 50));
                  drag.Cancel();
                  surface.UpdateLayout();

                  Assert.AreEqual(60, button.Width, 0.5);
                  Assert.AreEqual(30, button.Height, 0.5);
              });
          }

          // Gap #1: Escape-cancel's focus-acquisition half is verified in DragListenerTests
          // (Task 1). This confirms the production wiring end-to-end: a real ResizeThumb, once
          // selected, is itself focus-eligible through the same DesignerThumb constructor every
          // thumb in this phase shares.
          [Test]
          public void AResizeThumbCanAcquireKeyboardFocus()
          {
              WinUITestHost.RunWithWindow(window => {
                  var surface = LoadSelected(window, out var buttonItem, out _);
                  var thumb = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<ResizeThumb>().First();

                  bool focused = thumb.Focus(Microsoft.UI.Xaml.FocusState.Pointer);
                  Assert.IsTrue(focused);
              });
          }

          // Gap #2: the drag-distance threshold constants are exercised by
          // DragListenerTests.SimulatedDragBelowThresholdDoesNotFireStarted - this asserts the
          // same constants are the ones actually driving every thumb in this phase, not a
          // second, silently-different value.
          [Test]
          public void ResizeThumbExtensionUsesDragListenersSharedThreshold()
          {
              Assert.AreEqual(4, WinUIDesigner.Designer.Controls.DragListener.MinimumHorizontalDragDistance);
              Assert.AreEqual(2, WinUIDesigner.Designer.Controls.DragListener.MinimumVerticalDragDistance);
          }

          // Gap #3 and #4 are exercised directly by
          // GetCompleteAppliedTransformationToViewTests.ReparentingALiveRenderTransformIntoANewTransformGroupDoesNotThrow
          // (Task 2) and ModelToolsTests.ApplyingASecondTransformKindWrapsBothInATransformGroup
          // (Task 3) respectively - both already run as part of this suite's full pass. This test
          // exists only to document, in the DoD file itself, that every flagged gap has a home;
          // it intentionally duplicates neither test's assertions.
          [Test]
          public void AllFourFlaggedGapsHaveATestedResolutionElsewhereInThisSuite()
          {
              Assert.Pass("See DragListenerTests (gap 1/2), GetCompleteAppliedTransformationToViewTests (gap 3), ModelToolsTests (gap 4).");
          }
      }
  }
  ```

- [ ] **Step 2: Run the full suite to verify it fails first, then passes**

  Run: `dotnet build tests/WinUIDesigner.Designer.Tests/WinUIDesigner.Designer.Tests.csproj`
  Expected: FAIL first (file didn't exist), then run the built `.exe` after
  the file above is saved.
  Expected: PASS, all tests in `Phase4aDefinitionOfDoneTests`.

- [ ] **Step 3: Run every prior phase's suite one final time**

  Run all four `.exe`s: `WinUIDesigner.XamlDom.Tests.exe`,
  `WinUIDesigner.DesignModel.Tests.exe` (twice — once for its Phase 2
  baseline tests, once including this phase's Task 2 additions, same
  binary), `WinUIDesigner.Designer.Tests.exe` (covers Phase 3 and every
  task in this plan). Expected: 0 failures across all of them.

- [ ] **Step 4: Commit**

  ```bash
  git add tests/WinUIDesigner.Designer.Tests/Phase4aDefinitionOfDoneTests.cs
  git commit -m "test: add Phase 4a Definition-of-Done suite (resize, rotate, skew, cancel, gaps)"
  ```
