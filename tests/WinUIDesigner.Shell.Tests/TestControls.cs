using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// A control built in code, the kind a library added to the toolbox can show without its own
    /// XAML resources. This assembly stands in for such a library: Toolbox.AddAssembly takes it
    /// the same way it takes one loaded from a .dll. Toolbox-assemblies spec Decision 9.
    /// </summary>
    /// <remarks>A UserControl with its content set in code, not a Border subclass: WinUI's
    /// Border is sealed.</remarks>
    public class TestBadge : UserControl
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            "Text", typeof(string), typeof(TestBadge), new PropertyMetadata("Badge", OnTextChanged));

        readonly TextBlock label = new TextBlock { Text = "Badge" };

        public TestBadge()
        {
            Content = new Border { Padding = new Thickness(6, 2, 6, 2), Child = label };
        }

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((TestBadge)d).label.Text = e.NewValue as string;
        }
    }

    /// <summary>A control whose constructor throws, which the toolbox must report as skipped.</summary>
    public class ThrowingControl : UserControl
    {
        public ThrowingControl()
        {
            throw new InvalidOperationException("ThrowingControl cannot be created.");
        }
    }
}
