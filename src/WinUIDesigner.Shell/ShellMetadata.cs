using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    /// have. Spec Decision 22.
    /// <para>
    /// Registering by DependencyProperty rather than by (Type, name): Metadata.IsPopularProperty
    /// checks DesignItemProperty.DependencyProperty first, which is unambiguous, while the name
    /// form depends on which type DesignItemProperty.FullName reports.
    /// </para>
    /// </remarks>
    public static class ShellMetadata
    {
        static readonly object gate = new object();
        static bool registered;

        public static void Register()
        {
            lock (gate)
            {
                if (registered)
                    return;
                registered = true;
            }

            RegisterDefaultSizes();
            RegisterPopularProperties();
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
        }
    }
}
