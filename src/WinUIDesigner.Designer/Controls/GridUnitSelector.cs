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
    ///
    /// Three plain ToggleButtons (rather than RadioButtons) also sidesteps a problem the reference
    /// never had to face: GridRailAdorner builds TWO of these (one for rows, one for columns) as
    /// siblings inside the SAME AdornerPanel, outside any NameScope. A RadioButton's mutual
    /// exclusion is scoped by GroupName across the whole visual tree, so two selectors sharing (or
    /// both omitting) a GroupName would fight each other's selection. Mutual exclusion here is
    /// hand-rolled per instance (OnUnitPicked clears the other two of THIS selector's own fields),
    /// so there is no shared/static state for a second instance to collide with.
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
