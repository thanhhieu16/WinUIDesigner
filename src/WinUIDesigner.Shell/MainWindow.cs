using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
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

            // Before shell.New() below, and not merely for tidiness: BuildLayout builds the menu,
            // which is what creates undoItem/redoItem/cutItem/copyItem/pasteItem/deleteItem.
            // shell.New() raises CurrentDocument, which lands in WireCurrentDocument, which calls
            // RefreshCommands, which dereferences every one of them. Moving this line after
            // shell.New() is a NullReferenceException in the constructor.
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
            deleteItem = Item("Delete", VirtualKey.Delete, VirtualKeyModifiers.None,
                (s, e) => DeleteSelection(), OnDeleteAcceleratorInvoked);

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

        MenuFlyoutItem Item(string text, VirtualKey key, VirtualKeyModifiers modifiers, RoutedEventHandler onClick,
            TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> onInvoked = null)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += onClick;
            if (key != VirtualKey.None)
            {
                var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
                if (onInvoked != null)
                    accelerator.Invoked += onInvoked;
                item.KeyboardAccelerators.Add(accelerator);
            }
            return item;
        }

        /// <summary>
        /// The Delete command's one implementation, reached from the menu item's Click and from
        /// its accelerator. Refuses to act while a text-entry control has focus. Spec Decision 21.
        /// </summary>
        /// <remarks>
        /// The guard lives here rather than only in the accelerator handler because an accelerator
        /// this handler declines does NOT stop there: leaving KeyboardAcceleratorInvokedEventArgs.
        /// Handled false (its default) lets the framework go on to activate the owning
        /// MenuFlyoutItem's UIA Invoke pattern, which raises Click - so the guard has to sit on the
        /// action both routes share. Clicking Delete in the menu is unaffected: opening the flyout
        /// moves focus off the text box and onto the menu.
        /// </remarks>
        void DeleteSelection()
        {
            if (IsTextEntryFocused())
                return;
            WithClipboard((svc, ctx) => { if (svc.CanDelete(ctx)) svc.Delete(ctx); });
        }

        /// <summary>
        /// The Delete accelerator. Unlike the Ctrl-modified ones this is modifier-less and
        /// window-scoped, so it competes with every text caret in the window.
        /// </summary>
        /// <remarks>
        /// Handled is set only on the branch that acts, and that asymmetry is deliberate.
        /// Documented XAML input order is: preview key events, then app accelerators, then the
        /// KeyDown event - and "when the accelerator event is handled, the KeyDown event is also
        /// marked as handled". So setting Handled on the text-entry branch would eat the very
        /// keystroke the user meant for their text box. Declining instead lets Delete reach the
        /// caret; the framework's fallback Invoke of the menu item is what
        /// <see cref="DeleteSelection"/>'s own guard absorbs. On the acting branch Handled must be
        /// set, or that same fallback Invoke would delete a second time into a second undo group.
        /// </remarks>
        void OnDeleteAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (IsTextEntryFocused())
                return;

            DeleteSelection();
            args.Handled = true;
        }

        /// <summary>True when keyboard focus is inside a control that edits text.</summary>
        /// <remarks>
        /// The window hosts the outline filter (a TextBox) and a property grid whose editors are,
        /// as of Phase 4f, a TextBox (TextBoxEditor), a NumberBox (NumberEditor), a ComboBox
        /// (ComboBoxEditor) and a CheckBox (BoolEditor). Only the first two take typed text: a
        /// NumberBox's focusable part is its own inner TextBox, so `is TextBox` already covers it
        /// in practice and NumberBox is named as well only so a template change cannot silently
        /// reopen the hole. The two non-text editors are left out on purpose - Delete on a
        /// ComboBox or CheckBox is not a text edit, and swallowing it there would be the same
        /// class of bug in reverse. RichEditBox appears nowhere in this window today and is
        /// listed for the same defensive reason as NumberBox.
        /// </remarks>
        bool IsTextEntryFocused()
        {
            if (Content == null || Content.XamlRoot == null)
                return false;

            var focused = FocusManager.GetFocusedElement(Content.XamlRoot);
            return focused is TextBox || focused is RichEditBox || focused is NumberBox;
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

        /// <summary>
        /// Forces the current document dirty after an unhandled exception, so the close gate
        /// prompts rather than discarding a half-mutated tree silently. Spec Decision 18.
        /// </summary>
        /// <remarks>
        /// Called by App's UnhandledException handler. Safe with no document open. UpdateTitle is
        /// called explicitly because nothing here subscribes to Document.PropertyChanged - the
        /// title normally re-reads through OnUndoStackChanged, and this path does not touch the
        /// undo stack.
        /// </remarks>
        public void MarkCurrentDocumentSuspect()
        {
            var doc = shell.CurrentDocument;
            if (doc == null)
                return;

            doc.MarkSuspect();
            UpdateTitle();
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
        /// <summary>
        /// What <see cref="PrepareForCloseAsync"/> decided about an outgoing document.
        /// </summary>
        /// <remarks>
        /// Two fields rather than a bare bool because "the user said Discard" and "the document's
        /// dirty flag has been cleared" must stay separate facts. See PrepareForCloseAsync.
        /// </remarks>
        struct CloseDecision
        {
            /// <summary>False only when the user cancelled, or a requested save did not happen.</summary>
            public bool Proceed;

            /// <summary>
            /// True when the user chose Discard and the caller therefore owes the document a
            /// <see cref="Document.MarkSaved"/> - but only once its replacement exists.
            /// </summary>
            public bool ClearDirtyBeforeClose;
        }

        /// <summary>
        /// Replaces the current document: ask about the outgoing one, build the replacement, and
        /// only then discharge the answer and close the outgoing one.
        /// </summary>
        /// <remarks>
        /// The ordering is the whole point, and New and Open share this method so it cannot drift
        /// between them. Clearing the dirty flag before the replacement is known to exist loses
        /// unsaved work outright: <paramref name="createReplacement"/> can fail (an unreadable
        /// file, a template that will not load), both callers then keep the outgoing document,
        /// and a document left in front of the user with IsDirty already false never trips
        /// ShellModel.IsSomethingDirty again - so the close gate stays quiet and the edits go
        /// over the side with no prompt. Fix per final review.
        /// </remarks>
        async Task ReplaceCurrentDocumentAsync(Func<bool> createReplacement)
        {
            var current = shell.CurrentDocument;

            var decision = await PrepareForCloseAsync(current);
            if (!decision.Proceed)
                return;

            // Create first, close second: if this fails the shell reports it and keeps the
            // document the user already had, rather than leaving the app with none.
            if (!createReplacement())
                return;

            if (current != null)
            {
                // Only here, with the replacement known to exist, is a Discard answer discharged.
                if (decision.ClearDirtyBeforeClose)
                    current.MarkSaved();
                shell.Close(current);
            }
        }

        async Task NewAsync()
        {
            await ReplaceCurrentDocumentAsync(() => shell.New() != null);
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

            // The ask happens inside ReplaceCurrentDocumentAsync rather than by letting
            // ShellModel.Close raise its synchronous AskSaveChanges: that event cannot await a
            // dialog, so OnAskSaveChanges answers Cancel and Close silently fails.
            await ReplaceCurrentDocumentAsync(() => shell.Open(file.Path));
        }

        /// <summary>
        /// Asks about a dirty document and acts on the answer, reporting what the caller must do
        /// next. A Save is carried out here; a Discard is only decided here.
        /// </summary>
        /// <remarks>
        /// This method does not mutate <paramref name="doc"/> on the Discard path, deliberately.
        /// It used to call MarkSaved itself, which meant the dirty flag was gone before the
        /// caller had a replacement document in hand - and a caller that then failed left the
        /// user in their original document with unsaved edits nothing would ever prompt about
        /// again. The flag is cleared by the caller, immediately before its own close. Fix per
        /// final review.
        /// </remarks>
        async Task<CloseDecision> PrepareForCloseAsync(Document doc)
        {
            if (doc == null || !doc.IsDirty)
                return new CloseDecision { Proceed = true, ClearDirtyBeforeClose = false };

            var answer = await AskSaveChangesAsync(doc);
            if (answer == SaveChangesResult.Cancel)
                return new CloseDecision { Proceed = false, ClearDirtyBeforeClose = false };

            if (answer == SaveChangesResult.Save)
            {
                await SaveAsync(doc, doc.FilePath == null);
                // Still dirty means the save failed or the Save As picker was dismissed;
                // abandon rather than discarding the edits the user asked to keep. A save that
                // did go through has already cleared the flag, so the caller owes nothing.
                return new CloseDecision { Proceed = !doc.IsDirty, ClearDirtyBeforeClose = false };
            }

            return new CloseDecision { Proceed = true, ClearDirtyBeforeClose = true };
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
            var discarded = new List<Document>();
            foreach (var doc in shell.Documents.ToList())
            {
                var decision = await PrepareForCloseAsync(doc);
                if (!decision.Proceed)
                    return;
                if (decision.ClearDirtyBeforeClose)
                    discarded.Add(doc);
            }

            // Only after every document has answered. Clearing inside the loop would leave the
            // documents already asked about looking clean if a later one answers Cancel - the
            // same defect this ordering exists to prevent, one scope up.
            foreach (var doc in discarded)
                doc.MarkSaved();

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
