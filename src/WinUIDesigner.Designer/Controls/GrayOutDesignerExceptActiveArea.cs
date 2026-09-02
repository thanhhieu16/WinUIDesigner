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
