using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinUIDesigner.SampleControls
{
    /// <summary>
    /// A templated control whose look lives in this library's Themes/Generic.xaml. Loaded at
    /// runtime into an unpackaged app, that XAML is not in the app's merged resources.pri, so
    /// this control is the measurement case for what the toolbox can show from such a library.
    /// Toolbox-assemblies spec Decision 7.
    /// </summary>
    public class SampleCard : Control
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            "Title", typeof(string), typeof(SampleCard), new PropertyMetadata("Sample card"));

        public SampleCard()
        {
            DefaultStyleKey = typeof(SampleCard);
        }

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }
    }
}
