using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using WinUIDesigner.DesignModel.Adorners;

namespace WinUIDesigner.Designer.Controls.Thumbs
{
    /// <summary>A single point-edit handle: a square for an anchor point, or - toggled via
    /// IsEllipse - a blue circle for a Bezier control point, optionally connected to its anchor
    /// by a dashed line (RelativeToPoint). Ported from the reference's Controls/PointThumb.cs;
    /// no ControlStyles.xaml equivalent exists in this port (DesignerThumb's own class remarks),
    /// so the square/ellipse toggle and connector line are two extra child elements instead of a
    /// ControlTemplate + Trigger.</summary>
    public class PointThumb : DesignerThumb
    {
        public static readonly DependencyProperty IsEllipseProperty =
            DependencyProperty.Register(nameof(IsEllipse), typeof(bool), typeof(PointThumb),
                new PropertyMetadata(false, OnShapeChanged));

        public static readonly DependencyProperty PointProperty =
            DependencyProperty.Register(nameof(Point), typeof(Point), typeof(PointThumb),
                new PropertyMetadata(default(Point), OnPointChanged));

        public static readonly DependencyProperty RelativeToPointProperty =
            DependencyProperty.Register(nameof(RelativeToPoint), typeof(Point?), typeof(PointThumb),
                new PropertyMetadata(null, OnRelativeToPointChanged));

        public static readonly DependencyProperty InnerRenderTransformProperty =
            DependencyProperty.Register(nameof(InnerRenderTransform), typeof(Transform), typeof(PointThumb),
                new PropertyMetadata(null));

        readonly Brush _anchorBackground;
        readonly Ellipse _controlPointShape;
        readonly Line _connector;

        public PointThumb() : this(default(Point)) { }

        public PointThumb(Point point)
        {
            _anchorBackground = Background; // captured from the base DesignerThumb ctor (white)

            _connector = new Line {
                X1 = 3.5,
                Y1 = 3.5,
                Stroke = new SolidColorBrush(Microsoft.UI.Colors.Black),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                Visibility = Visibility.Collapsed
            };
            _controlPointShape = new Ellipse {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.White),
                Stroke = new SolidColorBrush(Microsoft.UI.Colors.Blue),
                StrokeThickness = 1,
                Visibility = Visibility.Collapsed
            };
            Children.Add(_connector);
            Children.Add(_controlPointShape);

            AdornerPlacement = new PointPlacementSupport(point);
            Point = point;
        }

        public bool IsEllipse {
            get { return (bool)GetValue(IsEllipseProperty); }
            set { SetValue(IsEllipseProperty, value); }
        }

        public Point Point {
            get { return (Point)GetValue(PointProperty); }
            set { SetValue(PointProperty, value); }
        }

        /// <summary>Non-null shows the dashed connector line to this anchor point - used when
        /// this thumb is a Bezier control point (Task 11's PathHandlerExtension).</summary>
        public Point? RelativeToPoint {
            get { return (Point?)GetValue(RelativeToPointProperty); }
            set { SetValue(RelativeToPointProperty, value); }
        }

        public Transform InnerRenderTransform {
            get { return (Transform)GetValue(InnerRenderTransformProperty); }
            set { SetValue(InnerRenderTransformProperty, value); }
        }

        public AdornerPlacement AdornerPlacement { get; private set; }

        static void OnShapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var thumb = (PointThumb)d;
            thumb.Background = thumb.IsEllipse ? null : thumb._anchorBackground;
            thumb._controlPointShape.Visibility = thumb.IsEllipse ? Visibility.Visible : Visibility.Collapsed;
        }

        static void OnPointChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var thumb = (PointThumb)d;
            ((PointPlacementSupport)thumb.AdornerPlacement).P = thumb.Point;
            (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(thumb) as AdornerPanel)?.InvalidateArrange();
            UpdateConnector(thumb);
        }

        static void OnRelativeToPointChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            UpdateConnector((PointThumb)d);
        }

        // Visual-only (the dashed anchor connector) - not covered by an automated assertion in
        // this phase's DoD (which checks point/segment VALUES and undo entries, not pixel
        // placement of a decorative line); verify by eye once PathHandlerExtension (Task 11/12)
        // is wired up and a Bezier control point is dragged.
        static void UpdateConnector(PointThumb thumb)
        {
            var rel = thumb.RelativeToPoint;
            thumb._connector.Visibility = rel.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (rel.HasValue) {
                thumb._connector.X2 = rel.Value.X - thumb.Point.X + 3.5;
                thumb._connector.Y2 = rel.Value.Y - thumb.Point.Y + 3.5;
            }
        }

        sealed class PointPlacementSupport : AdornerPlacement
        {
            public Point P;
            public PointPlacementSupport(Point point) { P = point; }
            public override void Arrange(AdornerPanel panel, UIElement adorner, Size adornedElementSize)
            {
                adorner.Arrange(new Rect(P.X - 3.5, P.Y - 3.5, 7, 7));
            }
        }
    }
}
