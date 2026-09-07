using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.Designer;
using WinUIDesigner.Designer.OutlineView;
using WinUIDesigner.Designer.PropertyGrid;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The shell's only window: layout, menu, shortcuts, dialogs, file pickers, and every wire
    /// into the current document's engine services.
    /// </summary>
    /// <remarks>
    /// Written in C# - App.xaml is the only XAML file in this project. ShellModel deliberately
    /// owns none of this: it raises events and this class answers them, so the logic stays
    /// testable without an XamlRoot. Spec Decisions 6, 7, 10, 19, 20 and 21.
    /// </remarks>
    public sealed class MainWindow : Window
    {
        const string ApplicationTitle = "WinUI Designer";

        readonly ShellModel shell = new ShellModel();
        readonly ToolboxView toolboxView = new ToolboxView();
        readonly OutlineViewControl outlineView = new OutlineViewControl();
        readonly TextBox outlineFilter = new TextBox { PlaceholderText = "Filter", Margin = new Thickness(4) };
        readonly ContentControl designHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        readonly ContentControl propertyGridHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };

        PropertyGridControl propertyGrid;
        Document wiredDocument;
        ISelectionService wiredSelection;
        UndoService wiredUndo;
        bool closingConfirmed;
        bool dialogOpen;

        MenuFlyoutItem undoItem, redoItem, cutItem, copyItem, pasteItem, deleteItem;

        public MainWindow()
        {
            ShellMetadata.Register();

            Title = ApplicationTitle;
            Content = BuildLayout();

            shell.ReportError += OnReportError;
            shell.AskSaveChanges += OnAskSaveChanges;
            shell.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "CurrentDocument")
                    WireCurrentDocument();
            };

            outlineFilter.TextChanged += (s, e) => outlineView.Filter = outlineFilter.Text;

            AppWindow.Closing += OnAppWindowClosing;

            shell.New();
        }

        UIElement BuildLayout()
        {
            var root = new Grid();

            // A blank Canvas, a ListView and DesignSurface all have a null Background by
            // default, so without one here they show through to whatever is behind the window
            // rather than to a page surface. Looked up from the theme rather than hardcoded so
            // it follows light/dark mode; left unset (matching the prior behaviour) if the key
            // does not resolve.
            if (Application.Current.Resources.TryGetValue("ApplicationPageBackgroundThemeBrush", out object pageBackground))
                root.Background = pageBackground as Microsoft.UI.Xaml.Media.Brush;

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var menu = BuildMenu();
            Grid.SetRow(menu, 0);
            root.Children.Add(menu);

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

            Grid.SetColumn(toolboxView, 0);
            columns.Children.Add(toolboxView);

            var surfaceScroll = new ScrollViewer
            {
                Content = designHost,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetColumn(surfaceScroll, 1);
            columns.Children.Add(surfaceScroll);

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(outlineFilter, 0);
            right.Children.Add(outlineFilter);

            // OutlineViewControl and PropertyGridControl both derive from StackPanel and contain
            // no ScrollViewer of their own, so without the two wrappers below they clip on any
            // real document. Spec Decision 10.
            var outlineScroll = new ScrollViewer
            {
                Content = outlineView,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(outlineScroll, 1);
            right.Children.Add(outlineScroll);

            var gridScroll = new ScrollViewer
            {
                Content = propertyGridHost,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(gridScroll, 2);
            right.Children.Add(gridScroll);

            Grid.SetColumn(right, 2);
            columns.Children.Add(right);

            Grid.SetRow(columns, 1);
            root.Children.Add(columns);
            return root;
        }

        MenuBar BuildMenu()
        {
            var file = new MenuBarItem { Title = "File" };
            file.Items.Add(Item("New", VirtualKey.N, VirtualKeyModifiers.Control, (s, e) => Fire(NewAsync)));
            file.Items.Add(Item("Open...", VirtualKey.O, VirtualKeyModifiers.Control, (s, e) => Fire(OpenAsync)));
            file.Items.Add(Item("Save", VirtualKey.S, VirtualKeyModifiers.Control, (s, e) => Fire(() => SaveAsync(shell.CurrentDocument, false))));
            file.Items.Add(Item("Save As...", VirtualKey.None, VirtualKeyModifiers.None, (s, e) => Fire(() => SaveAsync(shell.CurrentDocument, true))));
            file.Items.Add(new MenuFlyoutSeparator());
            file.Items.Add(Item("Exit", VirtualKey.None, VirtualKeyModifiers.None, (s, e) => Close()));

            undoItem = Item("Undo", VirtualKey.Z, VirtualKeyModifiers.Control, (s, e) => WithUndo(u => u.Undo()));
            redoItem = Item("Redo", VirtualKey.Y, VirtualKeyModifiers.Control, (s, e) => WithUndo(u => u.Redo()));
            cutItem = Item("Cut", VirtualKey.X, VirtualKeyModifiers.Control, (s, e) => WithClipboard((svc, ctx) => { if (svc.CanCut(ctx)) svc.Cut(ctx); }));
            copyItem = Item("Copy", VirtualKey.C, VirtualKeyModifiers.Control, (s, e) => WithClipboard((svc, ctx) => { if (svc.CanCopy(ctx)) svc.Copy(ctx); }));
            pasteItem = Item("Paste", VirtualKey.V, VirtualKeyModifiers.Control, (s, e) => WithClipboard((svc, ctx) => { if (svc.CanPaste(ctx)) svc.Paste(ctx); }));
            deleteItem = Item("Delete", VirtualKey.Delete, VirtualKeyModifiers.None, (s, e) => WithClipboard((svc, ctx) => { if (svc.CanDelete(ctx)) svc.Delete(ctx); }));

            var edit = new MenuBarItem { Title = "Edit" };
            edit.Items.Add(undoItem);
            edit.Items.Add(redoItem);
            edit.Items.Add(new MenuFlyoutSeparator());
            edit.Items.Add(cutItem);
            edit.Items.Add(copyItem);
            edit.Items.Add(pasteItem);
            edit.Items.Add(deleteItem);

            var bar = new MenuBar();
            bar.Items.Add(file);
            bar.Items.Add(edit);
            return bar;
        }

        MenuFlyoutItem Item(string text, VirtualKey key, VirtualKeyModifiers modifiers, RoutedEventHandler onClick)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += onClick;
            if (key != VirtualKey.None)
            {
                item.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = key, Modifiers = modifiers });
            }
            return item;
        }

        void WithUndo(Action<UndoService> action)
        {
            if (wiredUndo != null)
                action(wiredUndo);
            RefreshCommands();
        }

        /// <summary>
        /// Runs a task no caller awaits, logging whatever it throws.
        /// </summary>
        /// <remarks>
        /// An `async Task` discarded with `_ =` parks its exception in an unobserved Task: it
        /// never reaches Application.UnhandledException and surfaces only at GC finalization, so
        /// the riskiest paths here (dialogs, window close) would be the silent ones. `async void`
        /// with a catch is what keeps them audible. Fix per code review.
        /// </remarks>
        static async void Fire(Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[MainWindow] unobserved failure: " + ex);
            }
        }

        void WithClipboard(Action<ICopyPasteService, DesignContext> action)
        {
            // wiredDocument, not shell.CurrentDocument: RefreshCommands below decides what is
            // enabled from wiredDocument, so both must read the same document or the enabled
            // state and the action could disagree.
            var doc = wiredDocument;
            if (doc == null || doc.DesignContext == null)
                return;
            var svc = doc.DesignContext.Services.GetService<ICopyPasteService>();
            if (svc == null)
                return;
            action(svc, doc.DesignContext);
            RefreshCommands();
        }

        void WireCurrentDocument()
        {
            // Unsubscribe from the outgoing document first. Each LoadDesigner installs a fresh
            // DefaultSelectionService, so a missed unsubscribe leaks a handler per document.
            if (wiredSelection != null)
                wiredSelection.SelectionChanged -= OnSelectionChanged;
            if (wiredUndo != null)
                wiredUndo.UndoStackChanged -= OnUndoStackChanged;
            toolboxView.Detach();

            wiredSelection = null;
            wiredUndo = null;
            wiredDocument = shell.CurrentDocument;

            // A fresh PropertyGridControl per document. Phase 4f recorded that its nodeCache and
            // editorHosts are never invalidated across documents and handed the problem to this
            // phase; replacing the control sidesteps both caches without touching Designer code.
            // Spec Decision 19.
            propertyGrid = new PropertyGridControl();
            propertyGridHost.Content = propertyGrid;

            if (wiredDocument == null)
            {
                designHost.Content = null;
                outlineView.Root = null;
                Title = ApplicationTitle;
                RefreshCommands();
                return;
            }

            designHost.Content = wiredDocument.DesignSurface;
            outlineView.Root = wiredDocument.OutlineRoot;

            wiredSelection = wiredDocument.SelectionService;
            if (wiredSelection != null)
            {
                wiredSelection.SelectionChanged += OnSelectionChanged;
                // Seed the fresh grid from the selection the incoming document already has.
                // Without this it stays empty until the next selection change. Fix per review.
                propertyGrid.SelectedItems = wiredSelection.SelectedItems;
            }

            wiredUndo = wiredDocument.UndoService;
            if (wiredUndo != null)
            {
                // Subscription order is load-bearing: Document subscribed to this same event in
                // its constructor, so its IsDirty handler always runs before OnUndoStackChanged
                // reads Document.Title below. Do not move this earlier.
                wiredUndo.UndoStackChanged += OnUndoStackChanged;
            }

            if (wiredDocument.DesignContext != null)
            {
                var tools = wiredDocument.DesignContext.Services.GetService<IToolService>();
                if (tools != null)
                    toolboxView.Attach(tools);
            }

            UpdateTitle();
            RefreshCommands();
        }

        void OnSelectionChanged(object sender, DesignItemCollectionEventArgs e)
        {
            if (propertyGrid != null && wiredSelection != null)
                propertyGrid.SelectedItems = wiredSelection.SelectedItems;
            RefreshCommands();
        }

        void OnUndoStackChanged(object sender, EventArgs e)
        {
            UpdateTitle();
            RefreshCommands();
        }

        void UpdateTitle()
        {
            Title = wiredDocument == null
                ? ApplicationTitle
                : wiredDocument.Title + " - " + ApplicationTitle;
        }

        void RefreshCommands()
        {
            undoItem.IsEnabled = wiredUndo != null && wiredUndo.CanUndo;
            redoItem.IsEnabled = wiredUndo != null && wiredUndo.CanRedo;

            var ctx = wiredDocument == null ? null : wiredDocument.DesignContext;
            var svc = ctx == null ? null : ctx.Services.GetService<ICopyPasteService>();
            cutItem.IsEnabled = svc != null && svc.CanCut(ctx);
            copyItem.IsEnabled = svc != null && svc.CanCopy(ctx);
            pasteItem.IsEnabled = svc != null && svc.CanPaste(ctx);
            deleteItem.IsEnabled = svc != null && svc.CanDelete(ctx);
        }

        /// <summary>
        /// Replaces the current document with a blank one.
        /// </summary>
        /// <remarks>
        /// The outgoing document is asked about through the async path and closed, so Documents
        /// never holds more than one. The shell is single-document and has no tab strip, so a
        /// document left in the collection would be unreachable - still dirty, still holding a
        /// live DesignSurface, and impossible to make current again. Fix per code review.
        /// </remarks>
        async Task NewAsync()
        {
            var current = shell.CurrentDocument;
            if (!await PrepareForCloseAsync(current))
                return;

            // Create first, close second: if New fails the shell reports it and keeps the
            // document the user already had, rather than leaving the app with none.
            if (shell.New() == null)
                return;

            if (current != null)
                shell.Close(current);
        }

        async Task OpenAsync()
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            // Unpackaged WinUI requires the picker to be associated with a window's HWND;
            // without this it throws when shown.
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".xaml");

            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            // Ask here rather than letting ShellModel.Close raise its synchronous AskSaveChanges:
            // that event cannot await a dialog, so OnAskSaveChanges answers Cancel and Close
            // silently fails. Fix per code review.
            var current = shell.CurrentDocument;
            if (!await PrepareForCloseAsync(current))
                return;

            // Open before closing, so a file that turns out to be unreadable leaves the current
            // document in place instead of leaving the app with no document at all.
            if (!shell.Open(file.Path))
                return;

            if (current != null)
                shell.Close(current);
        }

        /// <summary>
        /// Asks about a dirty document and acts on the answer, leaving it in a state where a
        /// later <see cref="ShellModel.Close"/> completes without raising AskSaveChanges.
        /// Returns false when the user cancelled, or when a requested save did not go through.
        /// </summary>
        async Task<bool> PrepareForCloseAsync(Document doc)
        {
            if (doc == null || !doc.IsDirty)
                return true;

            var answer = await AskSaveChangesAsync(doc);
            if (answer == SaveChangesResult.Cancel)
                return false;

            if (answer == SaveChangesResult.Save)
            {
                await SaveAsync(doc, doc.FilePath == null);
                // Still dirty means the save failed or the Save As picker was dismissed;
                // abandon rather than discarding the edits the user asked to keep.
                return !doc.IsDirty;
            }

            // Discard: clear the flag so the close is silent.
            doc.MarkSaved();
            return true;
        }

        async Task SaveAsync(Document doc, bool forceAsk)
        {
            if (doc == null)
                return;

            if (!forceAsk && doc.FilePath != null)
            {
                shell.Save(doc);
                UpdateTitle();
                return;
            }

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("XAML", new List<string> { ".xaml" });
            picker.SuggestedFileName = doc.FileName ?? doc.Name;

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            shell.SaveAs(doc, file.Path);
            UpdateTitle();
        }

        void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            if (closingConfirmed || !shell.IsSomethingDirty)
                return;

            // AppWindowClosingEventArgs.Cancel is synchronous; ContentDialog.ShowAsync is not.
            // So cancel this close, ask, and close again once the answer is in. Spec Decision 20.
            args.Cancel = true;
            Fire(ConfirmThenCloseAsync);
        }

        async Task ConfirmThenCloseAsync()
        {
            // Every document, not just CurrentDocument: OnAppWindowClosing gates on
            // shell.IsSomethingDirty, which scans all of them, so asking about only one could
            // close the window on another's unsaved edits. Fix per code review.
            foreach (var doc in shell.Documents.ToList())
            {
                if (!await PrepareForCloseAsync(doc))
                    return;
            }

            closingConfirmed = true;
            Close();
        }

        void OnAskSaveChanges(object sender, AskSaveChangesEventArgs e)
        {
            // ShellModel's synchronous event cannot await a dialog. Every path that closes a
            // document (NewAsync, OpenAsync, ConfirmThenCloseAsync) runs PrepareForCloseAsync
            // first, which leaves the document non-dirty, so ShellModel.Close never raises this
            // for them. Reaching here therefore means no one asked: answer Cancel, which is the
            // safe default and leaves the document open.
            e.Result = SaveChangesResult.Cancel;
        }

        async Task<SaveChangesResult> AskSaveChangesAsync(Document doc)
        {
            var dialog = new ContentDialog
            {
                Title = "Unsaved changes",
                Content = "Save changes to \"" + doc.Name + "\"?",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Discard",
                CloseButtonText = "Cancel"
            };

            var result = await ShowDialogAsync(dialog);
            // Could not ask - no XamlRoot yet, or another dialog already owns the window.
            // Cancel is the safe answer: it changes nothing.
            if (result == null)
                return SaveChangesResult.Cancel;
            if (result == ContentDialogResult.Primary)
                return SaveChangesResult.Save;
            if (result == ContentDialogResult.Secondary)
                return SaveChangesResult.Discard;
            return SaveChangesResult.Cancel;
        }

        /// <summary>
        /// Shows a dialog, or returns null without showing it when it cannot be shown.
        /// </summary>
        /// <remarks>
        /// Guards the two ways ContentDialog.ShowAsync throws here. A null XamlRoot: the window
        /// has Content from the constructor but no XamlRoot until it is activated, and
        /// shell.New() runs in the constructor, so a startup failure would report through a
        /// dialog that cannot exist yet. And a second dialog: only one ContentDialog may be open
        /// at a time, so clicking the close box twice while the save prompt is up would throw on
        /// the second show. Fix per code review.
        /// </remarks>
        async Task<ContentDialogResult?> ShowDialogAsync(ContentDialog dialog)
        {
            if (dialogOpen || Content == null || Content.XamlRoot == null)
                return null;

            dialog.XamlRoot = Content.XamlRoot;
            dialogOpen = true;
            try
            {
                return await dialog.ShowAsync();
            }
            finally
            {
                dialogOpen = false;
            }
        }

        void OnReportError(object sender, ShellErrorEventArgs e)
        {
            Fire(() => ShowErrorAsync(e));
        }

        async Task ShowErrorAsync(ShellErrorEventArgs e)
        {
            var text = new StringBuilder();
            text.AppendLine(e.Message);

            if (e.Errors != null && e.Errors.Count > 0)
            {
                text.AppendLine();
                foreach (var error in e.Errors.Take(10))
                    text.AppendLine(error.Message + " (" + error.Line + ", " + error.Column + ")");
                if (e.Errors.Count > 10)
                    text.AppendLine("... and " + (e.Errors.Count - 10) + " more.");
            }

            var dialog = new ContentDialog
            {
                Title = "Error",
                Content = new ScrollViewer { Content = new TextBlock { Text = text.ToString(), TextWrapping = TextWrapping.Wrap } },
                CloseButtonText = "Close"
            };

            if (await ShowDialogAsync(dialog) == null)
            {
                // No window to show it in. Losing the message entirely is worse than a trace.
                System.Diagnostics.Debug.WriteLine("[MainWindow] could not show error dialog: " + text);
            }
        }
    }
}
