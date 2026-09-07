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
            window = new MainWindow();
            window.Activate();
        }

        void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Phase B's rule - never throw from the write path - rested on there being no
            // handler anywhere in src/. This is the first phase that owns an Application and can
            // install one. Handled=true keeps a defect in a design-time behaviour from ending the
            // process mid-drag; the message is shown rather than swallowed. Spec Decision 18.
            e.Handled = true;
            var window = this.window;
            if (window == null || window.Content == null)
                return;

            var dialog = new ContentDialog
            {
                XamlRoot = window.Content.XamlRoot,
                Title = "Unexpected error",
                Content = new ScrollViewer
                {
                    Content = new TextBlock { Text = e.Message, TextWrapping = TextWrapping.Wrap }
                },
                CloseButtonText = "Close"
            };
            _ = dialog.ShowAsync();
        }
    }
}
