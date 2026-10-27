using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinUIDesigner.SampleControls
{
    /// <summary>
    /// A badge whose look is built entirely in code, so it needs nothing from this library but
    /// its types: the kind of control the toolbox supports from a library loaded at runtime.
    /// </summary>
    public class SampleBadge : UserControl
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            "Text", typeof(string), typeof(SampleBadge), new PropertyMetadata("Sample badge", OnTextChanged));

        readonly TextBlock label = new TextBlock { Text = "Sample badge", Foreground = new SolidColorBrush(Colors.White) };

        public SampleBadge()
        {
            Content = new Border
            {
                Background = new SolidColorBrush(Colors.SeaGreen),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Child = label
            };
        }

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SampleBadge)d).label.Text = e.NewValue as string;
        }
    }
}
