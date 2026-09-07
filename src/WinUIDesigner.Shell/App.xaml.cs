using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The shell's application object.
    /// </summary>
    /// <remarks>
    /// App.xaml is the only XAML file in src/, and it exists for a measured reason: a project
    /// containing no compiled XAML produces no app-level merged resources.pri, ms-appx:/// then
    /// cannot be resolved, XamlControlsResources fails to load themeresources.xaml, and every
    /// control whose default template references a theme brush fail-fasts the process with
    /// 0xC0000409 during the first layout pass. See the Phase 5 spec.
    /// </remarks>
    public partial class App : Application
    {
        Window window;

        public App()
        {
            InitializeComponent();
            UnhandledException += OnUnhandledException;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Replaced by MainWindow in Task 6. Until then this proves the app starts and
            // that a templated control lays out, which is the whole risk in this task.
            window = new Window();
            window.Content = new TextBox { Text = "WinUIDesigner shell", Margin = new Thickness(24) };
            window.Activate();
        }

        void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Task 6 replaces this with a dialog. Handled=true keeps a defect in a design-time
            // behavior from ending the process mid-drag; see spec Decision 18.
            e.Handled = true;
            System.Diagnostics.Debug.WriteLine("Unhandled: " + e.Message);
        }
    }
}
