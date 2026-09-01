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
