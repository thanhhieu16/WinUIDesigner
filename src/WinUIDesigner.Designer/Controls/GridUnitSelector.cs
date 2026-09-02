using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Controls
{
    /// <summary>
    /// The Auto / Star(*) / Fixed(px) picker GridRailAdorner shows next to a hovered row/column.
    /// Ported from the reference's GridUnitSelector.xaml/.xaml.cs. This port builds every
    /// designer-chrome visual in code rather than loose XAML (the convention Phase 3's
    /// SelectionHighlight and Phase 4a's DesignerThumb already established - no
    /// InitializeComponent/BAML pipeline exists here), so the reference's
    /// Border+StackPanel+3-RadioButton visual tree is constructed directly instead. Base is
    /// Grid, not UserControl/Border: Border is sealed in this SDK (the same CS0509 DesignerThumb's
    /// remarks note for Thumb), and Grid already gives Background/BorderBrush/BorderThickness
    /// without a Control's style/template resolution - the same choice GridRailAdorner/
    /// GridSplitterAdorner made.
    ///
    /// GridRailAdorner's constructor builds one of these per rail (one for rows, one for
    /// columns) and adds both to the SAME AdornerPanel as siblings, so the reference's
    /// UserControl-scoped RadioButton GroupName ("group", isolated per instance by the
    /// UserControl's own NameScope) is replaced with a GroupName unique to this instance -
    /// otherwise the two pickers would fight over the same three-way selection.
    /// </summary>
    public sealed class GridUnitSelector : Grid
    {
        readonly GridRailAdorner rail;
        readonly StackPanel stack;
        readonly RadioButton fixedButton;
        readonly RadioButton starButton;
        readonly RadioButton autoButton;
        bool suppressChecked;

        public static readonly DependencyProperty OrientationProperty =
            DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(GridUnitSelector),
                new PropertyMetadata(Orientation.Vertical, OnOrientationChanged));

        public GridUnitSelector(GridRailAdorner rail)
        {
            if (rail == null) throw new ArgumentNullException(nameof(rail));
            this.rail = rail;

            Background = new SolidColorBrush(Microsoft.UI.Colors.LightBlue);
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.CornflowerBlue);
            BorderThickness = new Thickness(1);
            Visibility = Visibility.Collapsed;

            stack = new StackPanel { Margin = new Thickness(6) };
            Children.Add(stack);

            string groupName = "GridUnitSelector-" + GetHashCode();
            fixedButton = CreateOption("Fixed", groupName);
            starButton = CreateOption("*", groupName);
            autoButton = CreateOption("Auto", groupName);

            stack.Children.Add(fixedButton);
            stack.Children.Add(starButton);
            stack.Children.Add(autoButton);

            AddHandler(UIElement.PointerExitedEvent, (PointerEventHandler)OnPointerExited, true);
        }

        RadioButton CreateOption(string content, string groupName)
        {
            var button = new RadioButton { Content = content, GroupName = groupName, FontSize = 10, Margin = new Thickness(1) };
            button.Checked += OnOptionChecked;
            return button;
        }

        void OnOptionChecked(object sender, RoutedEventArgs e)
        {
            if (suppressChecked) return;
            rail.SetGridLengthUnit(Unit);
        }

        void OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
        }

        public Orientation Orientation {
            get { return (Orientation)GetValue(OrientationProperty); }
            set { SetValue(OrientationProperty, value); }
        }

        static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((GridUnitSelector)d).stack.Orientation = (Orientation)e.NewValue;
        }

        public DesignItem SelectedItem { get; set; }

        public GridUnitType Unit {
            get {
                if (autoButton.IsChecked == true) return GridUnitType.Auto;
                if (starButton.IsChecked == true) return GridUnitType.Star;
                return GridUnitType.Pixel;
            }
            set {
                // Reflects existing state into the radio group without re-triggering
                // rail.SetGridLengthUnit - only a user click should rewrite the model.
                suppressChecked = true;
                switch (value) {
                    case GridUnitType.Auto: autoButton.IsChecked = true; break;
                    case GridUnitType.Star: starButton.IsChecked = true; break;
                    default: fixedButton.IsChecked = true; break;
                }
                suppressChecked = false;
            }
        }
    }
}
