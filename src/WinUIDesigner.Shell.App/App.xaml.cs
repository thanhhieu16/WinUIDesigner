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
        bool dialogOpen;

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
            // The dialog needs a live XamlRoot, and only one ContentDialog may be open at a time
            // - a second show throws, which from inside the unhandled-exception handler is the
            // worst possible place to throw. When it cannot be shown, trace it: returning mute
            // here used to lose the message entirely. Fix per code review.
            if (dialogOpen || window == null || window.Content == null || window.Content.XamlRoot == null)
            {
                System.Diagnostics.Debug.WriteLine("[App] unhandled exception, no dialog available: " + e.Message);
                return;
            }

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
            ShowDialog(dialog);
        }

        /// <summary>
        /// Shows a dialog nobody awaits, logging whatever it throws.
        /// </summary>
        /// <remarks>
        /// `_ = dialog.ShowAsync()` parks any exception in an unobserved Task that never reaches
        /// this very handler and surfaces only at GC finalization. Fix per code review.
        /// </remarks>
        async void ShowDialog(ContentDialog dialog)
        {
            dialogOpen = true;
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[App] could not show the error dialog: " + ex);
            }
            finally
            {
                dialogOpen = false;
            }
        }
    }
}
