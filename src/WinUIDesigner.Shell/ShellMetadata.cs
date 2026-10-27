using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The seed data the engine's Metadata tables need in order to be useful.
    /// </summary>
    /// <remarks>
    /// Phases 4e and 4f both deferred this to the shell. Unseeded, Metadata's tables are empty,
    /// which leaves the property grid's Popular category blank and ModelTools.GetDefaultSize
    /// with nothing to return - the latter directly affects the size a click-placed control
    /// gets. This seeds only what the shell's own tests exercise; the rest is a stated gap in
    /// the Phase 5 spec. The reference's equivalent is BasicMetadata, which this port does not
    /// have. Phase 5 Decision 22. Widened by the shell-metadata spec
    /// (docs/superpowers/specs/2026-10-27-shell-metadata-design.md) to the four tables the engine
    /// already reads: default property values (CreateComponentTool applies them to a placed
    /// control), value ranges (the property grid's NumberEditor), hidden properties (the
    /// property grid) and more popular properties.
    /// <para>
    /// Registering by DependencyProperty rather than by (Type, name): Metadata.IsPopularProperty
    /// checks DesignItemProperty.DependencyProperty first, which is unambiguous, while the name
    /// form depends on which type DesignItemProperty.FullName reports.
    /// </para>
    /// </remarks>
    public static class ShellMetadata
    {
        static readonly object gate = new object();

        // Note for tests: Metadata's tables are static and process-wide, and nothing here can
        // unseed them. Once any test calls Register(), every later test in that process sees the
        // seeding - so no test may assert an unseeded baseline (an empty Popular category, a
        // missing default size), because whether it holds depends only on execution order.
        static bool registered;

        public static void Register()
        {
            lock (gate)
            {
                if (registered)
                    return;

                RegisterDefaultSizes();
                RegisterPopularProperties();
                RegisterDefaultPropertyValues();
                RegisterValueRanges();
                RegisterHiddenProperties();

                // Set last, and inside the lock: a caller that returns from Register() must be
                // able to read the seeded tables, which means no other caller may leave this
                // method while seeding is still in flight.
                registered = true;
            }
        }

        static void RegisterDefaultSizes()
        {
            Metadata.AddDefaultSize(typeof(Button), new Size(75, 32));
            Metadata.AddDefaultSize(typeof(TextBox), new Size(120, 32));
            Metadata.AddDefaultSize(typeof(TextBlock), new Size(100, 20));
            Metadata.AddDefaultSize(typeof(CheckBox), new Size(120, 32));
            Metadata.AddDefaultSize(typeof(RadioButton), new Size(120, 32));
            Metadata.AddDefaultSize(typeof(ComboBox), new Size(120, 32));
            Metadata.AddDefaultSize(typeof(Slider), new Size(160, 32));
            Metadata.AddDefaultSize(typeof(ProgressBar), new Size(160, 8));
            Metadata.AddDefaultSize(typeof(Image), new Size(120, 120));
            Metadata.AddDefaultSize(typeof(Border), new Size(120, 80));
            Metadata.AddDefaultSize(typeof(Canvas), new Size(160, 120));
            Metadata.AddDefaultSize(typeof(Grid), new Size(160, 120));
            Metadata.AddDefaultSize(typeof(StackPanel), new Size(120, 160));
            Metadata.AddDefaultSize(typeof(Microsoft.UI.Xaml.Shapes.Rectangle), new Size(80, 60));
            Metadata.AddDefaultSize(typeof(Microsoft.UI.Xaml.Shapes.Ellipse), new Size(80, 60));
            Metadata.AddDefaultSize(typeof(ToggleButton), new Size(100, 32));
            Metadata.AddDefaultSize(typeof(ToggleSwitch), new Size(160, 60));
            Metadata.AddDefaultSize(typeof(HyperlinkButton), new Size(120, 32));
            Metadata.AddDefaultSize(typeof(PasswordBox), new Size(120, 32));
            Metadata.AddDefaultSize(typeof(NumberBox), new Size(120, 32));
            Metadata.AddDefaultSize(typeof(DatePicker), new Size(300, 32));
            Metadata.AddDefaultSize(typeof(TimePicker), new Size(240, 32));
            Metadata.AddDefaultSize(typeof(ListView), new Size(160, 160));
            Metadata.AddDefaultSize(typeof(ScrollViewer), new Size(160, 160));
        }

        static void RegisterPopularProperties()
        {
            Metadata.AddPopularProperty(FrameworkElement.WidthProperty);
            Metadata.AddPopularProperty(FrameworkElement.HeightProperty);
            Metadata.AddPopularProperty(FrameworkElement.MarginProperty);
            Metadata.AddPopularProperty(FrameworkElement.HorizontalAlignmentProperty);
            Metadata.AddPopularProperty(FrameworkElement.VerticalAlignmentProperty);
            Metadata.AddPopularProperty(Control.BackgroundProperty);
            Metadata.AddPopularProperty(Control.ForegroundProperty);
            Metadata.AddPopularProperty(Control.FontSizeProperty);
            Metadata.AddPopularProperty(ContentControl.ContentProperty);
            Metadata.AddPopularProperty(TextBlock.TextProperty);
            Metadata.AddPopularProperty(UIElement.VisibilityProperty);
            Metadata.AddPopularProperty(UIElement.OpacityProperty);

            // The look-and-text properties of the controls that do not derive from Control, which
            // each declare their own: a TextBlock's Foreground is TextBlock.ForegroundProperty, not
            // Control.ForegroundProperty, so it was not popular until it was listed itself.
            Metadata.AddPopularProperty(Control.PaddingProperty);
            Metadata.AddPopularProperty(Control.BorderBrushProperty);
            Metadata.AddPopularProperty(Control.BorderThicknessProperty);
            Metadata.AddPopularProperty(Control.CornerRadiusProperty);
            Metadata.AddPopularProperty(Control.FontWeightProperty);
            Metadata.AddPopularProperty(Control.FontFamilyProperty);
            Metadata.AddPopularProperty(TextBlock.ForegroundProperty);
            Metadata.AddPopularProperty(TextBlock.FontSizeProperty);
            Metadata.AddPopularProperty(TextBlock.FontWeightProperty);
            Metadata.AddPopularProperty(TextBlock.TextWrappingProperty);
            Metadata.AddPopularProperty(TextBlock.TextAlignmentProperty);
            Metadata.AddPopularProperty(TextBox.TextProperty);
            Metadata.AddPopularProperty(TextBox.PlaceholderTextProperty);
            Metadata.AddPopularProperty(TextBox.HeaderProperty);
            Metadata.AddPopularProperty(Border.BackgroundProperty);
            Metadata.AddPopularProperty(Border.BorderBrushProperty);
            Metadata.AddPopularProperty(Border.BorderThicknessProperty);
            Metadata.AddPopularProperty(Border.CornerRadiusProperty);
            Metadata.AddPopularProperty(Border.PaddingProperty);
            Metadata.AddPopularProperty(Panel.BackgroundProperty);
            Metadata.AddPopularProperty(Shape.FillProperty);
            Metadata.AddPopularProperty(Shape.StrokeProperty);
            Metadata.AddPopularProperty(Shape.StrokeThicknessProperty);
            Metadata.AddPopularProperty(Image.SourceProperty);
            Metadata.AddPopularProperty(Image.StretchProperty);
            Metadata.AddPopularProperty(ToggleButton.IsCheckedProperty);
            Metadata.AddPopularProperty(ToggleSwitch.IsOnProperty);
            Metadata.AddPopularProperty(ToggleSwitch.HeaderProperty);
            Metadata.AddPopularProperty(RangeBase.MinimumProperty);
            Metadata.AddPopularProperty(RangeBase.MaximumProperty);
            Metadata.AddPopularProperty(RangeBase.ValueProperty);
        }

        /// <summary>
        /// What a control gets when it is placed from the toolbox, applied by
        /// CreateComponentTool. Without them a placed Button or CheckBox is an empty frame and a
        /// placed Rectangle or Ellipse is invisible. Keyed by the exact type: Metadata does not
        /// look these up through base types. Shell-metadata spec Decision 1.
        /// </summary>
        static void RegisterDefaultPropertyValues()
        {
            foreach (var type in new[] { typeof(Button), typeof(ToggleButton), typeof(RepeatButton),
                typeof(HyperlinkButton), typeof(CheckBox), typeof(RadioButton) })
            {
                Metadata.AddDefaultPropertyValue(type, "Content", type.Name);
            }
            Metadata.AddDefaultPropertyValue(typeof(TextBlock), "Text", "TextBlock");
            Metadata.AddDefaultPropertyValue(typeof(TextBox), "PlaceholderText", "TextBox");
            Metadata.AddDefaultPropertyValue(typeof(ComboBox), "PlaceholderText", "ComboBox");
            Metadata.AddDefaultPropertyValue(typeof(ToggleSwitch), "Header", "ToggleSwitch");

            // One brush per table entry, shared by every shape placed afterwards: a brush may be
            // shared between elements, and the document stores it as its color name.
            Metadata.AddDefaultPropertyValue(typeof(Rectangle), "Fill", new SolidColorBrush(Colors.LightGray));
            Metadata.AddDefaultPropertyValue(typeof(Ellipse), "Fill", new SolidColorBrush(Colors.LightGray));
            Metadata.AddDefaultPropertyValue(typeof(Border), "BorderBrush", new SolidColorBrush(Colors.Gray));
            Metadata.AddDefaultPropertyValue(typeof(Border), "BorderThickness", new Thickness(1));
        }

        /// <summary>
        /// Bounds for the property grid's number editor, which otherwise allows any double: a
        /// negative Width, an Opacity of 5. Shell-metadata spec Decision 2.
        /// </summary>
        static void RegisterValueRanges()
        {
            Metadata.AddValueRange(UIElement.OpacityProperty, 0, 1);
            Metadata.AddValueRange(Control.FontSizeProperty, 1, 1000);
            Metadata.AddValueRange(TextBlock.FontSizeProperty, 1, 1000);
            Metadata.AddValueRange(Shape.StrokeThicknessProperty, 0, 1000);
            foreach (var size in new[] { FrameworkElement.WidthProperty, FrameworkElement.HeightProperty,
                FrameworkElement.MinWidthProperty, FrameworkElement.MinHeightProperty,
                FrameworkElement.MaxWidthProperty, FrameworkElement.MaxHeightProperty })
            {
                Metadata.AddValueRange(size, 0, 100000);
            }
        }

        /// <summary>
        /// Properties a designer has no use for, kept out of the property grid: keyboard and
        /// gamepad focus navigation (XYFocus*), the focus rectangle's look (FocusVisual*) and
        /// access-key tip placement (KeyTip*). They are still read from and written to the XAML;
        /// they are only not listed. Shell-metadata spec Decision 3.
        /// </summary>
        static void RegisterHiddenProperties()
        {
            foreach (var p in new[] {
                UIElement.XYFocusUpProperty, UIElement.XYFocusDownProperty,
                UIElement.XYFocusLeftProperty, UIElement.XYFocusRightProperty,
                UIElement.XYFocusUpNavigationStrategyProperty, UIElement.XYFocusDownNavigationStrategyProperty,
                UIElement.XYFocusLeftNavigationStrategyProperty, UIElement.XYFocusRightNavigationStrategyProperty,
                UIElement.XYFocusKeyboardNavigationProperty,
                FrameworkElement.FocusVisualMarginProperty,
                FrameworkElement.FocusVisualPrimaryBrushProperty, FrameworkElement.FocusVisualPrimaryThicknessProperty,
                FrameworkElement.FocusVisualSecondaryBrushProperty, FrameworkElement.FocusVisualSecondaryThicknessProperty,
                UIElement.KeyTipPlacementModeProperty, UIElement.KeyTipHorizontalOffsetProperty,
                UIElement.KeyTipVerticalOffsetProperty, UIElement.KeyTipTargetProperty })
            {
                Metadata.HideProperty(p);
            }
        }
    }
}
