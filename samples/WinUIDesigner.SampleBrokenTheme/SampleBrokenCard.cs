using Microsoft.UI.Xaml.Controls;

namespace WinUIDesigner.SampleBrokenTheme
{
    /// <summary>A templated control whose library theme cannot be loaded at runtime.</summary>
    public class SampleBrokenCard : Control
    {
        public SampleBrokenCard()
        {
            DefaultStyleKey = typeof(SampleBrokenCard);
        }
    }
}
