using System;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Services;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Draws a new Line: inserts it at the click point with X1=Y1=X2=Y2=0 (its own local
    /// origin sits at the click point via Canvas.Left/Top from the insert - matches this Line's
    /// LineHandlerExtension resize math, which applies drag delta directly to X1/Y1/X2/Y2), then
    /// hands the drag to LineHandlerExtension's own already-built BottomRight/X2,Y2 endpoint
    /// DragListener via ExternalStart (spec decision 4). Ported from the reference's Extensions/
    /// DrawLineExtension.cs.</summary>
    [ExtensionFor(typeof(Microsoft.UI.Xaml.Controls.Canvas))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.Controls.Grid))]
    public sealed class DrawLineExtension : BehaviorExtension, IDrawItemExtension
    {
        /// <summary>Test-only seam: exposes the gesture started by the last StartDrawItem call
        /// (null before any draw, or once the gesture has finished/reset the tool). Needed for
        /// the same reason CreateComponentTool.Gesture exists: the actual outer "Draw Line"
        /// changeGroup only closes on a real IDesignPanel.PointerReleased/PointerCaptureLost -
        /// a real PointerRoutedEventArgs has no public constructor for a test to fabricate one
        /// (Global Constraints) - so a test drives the gesture's own internal
        /// HandlePointerReleased directly through this property instead.</summary>
        internal DrawLineMouseGesture Gesture { get; private set; }

        public bool CanItemBeDrawn(Type createItemType) => createItemType == typeof(Line);

        public void StartDrawItem(DesignItem clickedOn, Type createItemType, IDesignPanel panel, Point point, VirtualKeyModifiers modifiers, Action<DesignItem> drawItemCallback)
        {
            var changeGroup = clickedOn.OpenGroup("Draw Line");
            var createdItem = CreateComponentTool.CreateItem(clickedOn.Context, typeof(Line), null);

            // Final-review fix (NaN-Rect consistency): was new Rect(point, new Size(0, 0)). The two
            // sibling draw tools (DrawPolyLineExtension/DrawPathExtension) both use the NaN form,
            // and DrawPolyLineExtension.StartDrawItem's own doc comment records that this exact
            // question was independently re-verified twice - including by temporarily reverting THIS
            // line back to the NaN form and re-running DrawLineExtensionTests, which passed. The NaN
            // form is also strictly better: DefaultPlacementBehavior.SetPosition's
            // ModelTools.Resize(item, NaN, NaN) special-cases double.IsNaN by Reset()-ing the
            // Width/Height property, whereas Size(0, 0) explicitly writes Width=0/Height=0 onto the
            // freshly created Line.
            var operation = PlacementOperation.TryStartInsertNewComponents(clickedOn, new[] { createdItem }, new[] { new Rect(point.X, point.Y, double.NaN, double.NaN) }, PlacementType.AddItem);
            if (operation == null) {
                changeGroup.Abort();
                return;
            }
            clickedOn.Context.Services.Selection.SetSelectedComponents(new[] { createdItem });
            operation.Commit();

            createdItem.Properties["Stroke"].SetValue(new SolidColorBrush(Colors.Black));
            createdItem.Properties["StrokeThickness"].SetValue(2.0);
            createdItem.Properties["Stretch"].SetValue(Stretch.None);

            drawItemCallback?.Invoke(createdItem);

            // Final-review fix: FirstOrDefault + abort, not First(). LineHandlerExtension is
            // attached by OnlyOneItemSelectedExtensionServer against the selection set above, so it
            // is expected to be there - but if it somehow isn't, First() would throw out of
            // StartDrawItem leaving the "Draw Line" ChangeGroup open on UndoService's transaction
            // stack, which blocks every later Undo. Failing by aborting the transaction first is the
            // same unwind the `operation == null` arm above already performs.
            var lineHandler = createdItem.Extensions.OfType<LineHandlerExtension>().FirstOrDefault();
            if (lineHandler == null) {
                changeGroup.Abort();
                return;
            }
            Gesture = new DrawLineMouseGesture(lineHandler, changeGroup);
            Gesture.Start(panel, point);
        }
    }

    /// <summary>Drives LineHandlerExtension's BottomRight DragListener from outside a real
    /// pointer event on that thumb - see DragListener.ExternalStart's own doc comment and spec
    /// decision 4's "Capture responsibility" (capture already happened one level up in
    /// CreateComponentTool - this class never captures or focuses).</summary>
    sealed class DrawLineMouseGesture
    {
        readonly LineHandlerExtension _lineHandler;
        // Final-review fix: not readonly, and nulled the moment it is committed or aborted -
        // matching DrawPolylineMouseGesture/DrawPathMouseGesture, whose otherwise-parallel
        // Commit()/Cancel() already do exactly that. Benign today only because Stop() unsubscribes
        // every event this gesture listens to, so neither path can currently run twice; nulling
        // makes the ?. guards below genuinely single-shot instead of merely unreachable-twice.
        ChangeGroup _changeGroup;
        IDesignPanel _designPanel;

        public DrawLineMouseGesture(LineHandlerExtension lineHandler, ChangeGroup changeGroup)
        {
            _lineHandler = lineHandler;
            _changeGroup = changeGroup;
        }

        public void Start(IDesignPanel designPanel, Point startPoint)
        {
            _designPanel = designPanel;
            designPanel.PointerMoved += OnPointerMoved;
            designPanel.PointerReleased += OnPointerReleased;
            designPanel.PointerCaptureLost += OnPointerCaptureLost;
            designPanel.KeyDown += OnKeyDown;
            _lineHandler.DragListener.ExternalStart(startPoint, VirtualKeyModifiers.None);
        }

        void OnPointerMoved(object sender, PointerRoutedEventArgs e) =>
            _lineHandler.DragListener.ExternalMove(e.GetCurrentPoint((UIElement)sender).Position, e.KeyModifiers);

        void OnPointerReleased(object sender, PointerRoutedEventArgs e) =>
            HandlePointerReleased(e.GetCurrentPoint((UIElement)sender).Position);

        /// <summary>The real pointer-released handling, extracted so a test can drive it
        /// directly - same Handle*-extraction pattern as CreateComponentMouseGesture's own
        /// HandlePointerReleased (CreateComponentTool.cs), needed for the same reason
        /// (PointerRoutedEventArgs has no public constructor - Global Constraints).</summary>
        internal void HandlePointerReleased(Point point)
        {
            _lineHandler.DragListener.ExternalStop(point);
            Commit();
        }

        void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => Cancel();

        void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape) { Cancel(); e.Handled = true; }
        }

        void Commit()
        {
            _changeGroup?.Commit();
            _changeGroup = null;
            Stop();
        }

        /// <summary>The gesture's abort path - what Escape (OnKeyDown) and PointerCaptureLost both
        /// drive. Internal rather than private so a test can exercise the cancellation path
        /// directly, the same seam CreateComponentMouseGesture.Cancel() already provides (neither
        /// KeyRoutedEventArgs nor PointerRoutedEventArgs has a public constructor - Global
        /// Constraints - so the real events can't be raised from a test).</summary>
        internal void Cancel()
        {
            // Close the inner Resize transaction (LineHandlerExtension.SetOperation, opened when
            // ExternalStart's drag crossed the threshold) SYNCHRONOUSLY, before aborting the
            // outer "Draw Line" changeGroup - DragListener.Cancel() is a no-op if the drag never
            // crossed the threshold (Completed never fired, so nothing was ever opened), and
            // fires Drag_Completed(IsCanceled: true) if it did. Aborting outer-before-inner would
            // leave the inner transaction dangling on the UndoService's stack.
            _lineHandler.DragListener.Cancel();
            _changeGroup?.Abort();
            _changeGroup = null;
            Stop();
        }

        void Stop()
        {
            _designPanel.PointerMoved -= OnPointerMoved;
            _designPanel.PointerReleased -= OnPointerReleased;
            _designPanel.PointerCaptureLost -= OnPointerCaptureLost;
            _designPanel.KeyDown -= OnKeyDown;
            _designPanel.ReleasePointerCaptures();
            // DefaultExtension.Services/.ExtendedItem are both public (DefaultExtension.cs:36,58)
            // - no internal accessor needed to reach them from this same-assembly, non-subclass
            // helper class.
            var toolService = _lineHandler.Services.GetService<IToolService>();
            if (toolService.CurrentTool is CreateComponentTool) toolService.CurrentTool = toolService.PointerTool;
        }
    }
}
