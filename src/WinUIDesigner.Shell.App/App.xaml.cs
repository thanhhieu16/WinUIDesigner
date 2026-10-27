using System;
using System.Linq;
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
    public partial class App : Application, IXamlMetadataProviderHost
    {
        Window window;
        bool dialogOpen;

        public App()
        {
            // Before InitializeComponent, so it is in place for the framework's first resource
            // lookup: XAML resolves ms-appx:/// through this manager, and what the app's own
            // resources.pri lacks is tried against the .pri files of libraries added at runtime.
            // Library-PRI spec Decision 1.
            LibraryPriResources.TracePath = Environment.GetEnvironmentVariable("WINUIDESIGNER_RESOURCE_TRACE");
            ResourceManagerRequested += (sender, args) =>
            {
                var manager = new Microsoft.Windows.ApplicationModel.Resources.ResourceManager();
                manager.ResourceNotFound += LibraryPriResources.OnResourceNotFound;
                args.CustomResourceManager = manager;
                LibraryPriResources.IsActive = true;
            };
            InitializeComponent();
            UnhandledException += OnUnhandledException;
        }

        /// <summary>
        /// Registers a library's metadata provider with the one the XAML compiler generated for
        /// this app. Library-resources spec Decision 2.
        /// </summary>
        /// <remarks>
        /// Through reflection, because the generated code offers no way in. Measured on the
        /// runner (the generated XamlTypeInfo.g.cs, printed by a temporary workflow step): App has
        /// no <c>_AddOtherProvider</c> (CS0103) and its provider no <c>AddOtherProvider</c>, though
        /// the compiler carries templates with both. What it does generate is
        /// <c>App._AppProvider</c> (a XamlMetaDataProvider), its private <c>Provider</c> (the
        /// XamlTypeInfoProvider), and that one's private <c>OtherProviders</c>: the list every
        /// failed type lookup falls through to, created lazily with WinUI's own controls provider
        /// in it. Adding to that list is what registering means. If a future SDK renames any of
        /// the three, this throws and the add dialog says so.
        /// </remarks>
        public void AddProvider(Microsoft.UI.Xaml.Markup.IXamlMetadataProvider provider)
        {
            var appProvider = Member(this, "_AppProvider");
            var typeInfoProvider = Member(appProvider, "Provider");
            var others = Member(typeInfoProvider, "OtherProviders") as System.Collections.Generic.IList<Microsoft.UI.Xaml.Markup.IXamlMetadataProvider>;
            if (others == null)
                throw new InvalidOperationException("This build's generated XAML metadata provider has no OtherProviders list.");
            if (!others.Contains(provider))
                others.Add(provider);
        }

        static object Member(object owner, string property)
        {
            const System.Reflection.BindingFlags Any =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var info = owner?.GetType().GetProperty(property, Any);
            if (info == null)
                throw new InvalidOperationException("This build's generated XAML metadata provider has no " + property + ".");
            return info.GetValue(owner);
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Environment's split arguments rather than args.Arguments: for an unpackaged app
            // that is one unsplit string. Open-from-outside spec Decision 3.
            window = new MainWindow(Environment.GetCommandLineArgs().Skip(1));
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

            // The third of Decision 18's three obligations, and the one that outlives the dialog:
            // Handled=true keeps the process alive, so the design tree survives in whatever
            // half-mutated state the failure left it. Forcing the document dirty is what makes
            // MainWindow's close gate prompt about that tree instead of discarding it silently -
            // IsDirty is otherwise raised only by a change that reached the undo stack, and a
            // behaviour that threw mid-gesture never got that far. Done before the dialog, and
            // ahead of the early return below, so it still happens when no dialog can be shown.
            var main = window as MainWindow;
            if (main != null)
                main.MarkCurrentDocumentSuspect();
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
