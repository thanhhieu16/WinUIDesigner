using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.Designer;
using WinUIDesigner.Designer.OutlineView;
using WinUIDesigner.Designer.PropertyGrid;
using WinUIDesigner.Designer.Services;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The shell's only window: layout, menu, shortcuts, dialogs, file pickers, and every wire
    /// into the current document's engine services.
    /// </summary>
    /// <remarks>
    /// Written in C# - App.xaml is the only XAML file in this project. ShellModel deliberately
    /// owns none of this: it raises events and this class answers them, so the logic stays
    /// testable without an XamlRoot. Phase 5 Decisions 6, 7, 10, 19, 20 and 21, and this phase's
    /// Decisions 1, 3, 4, 5, 6, 8, 9, 18 and 22.
    /// <para>
    /// The two decision sets are numbered independently and their numbers collide, so every
    /// reference in this project names which phase it means: a bare "Spec Decision N" is this
    /// phase's, "Phase 5 Decision N" is the earlier shell phase's.
    /// </para>
    /// </remarks>
    public sealed class MainWindow : Window
    {
        const string ApplicationTitle = "WinUI Designer";

        readonly ShellModel shell = new ShellModel();

        /// <summary>Test seam: the model this window drives. Not for production callers.</summary>
        internal ShellModel ShellForTest { get { return shell; } }
        readonly ToolboxView toolboxView = new ToolboxView();
        readonly OutlineViewControl outlineView = new OutlineViewControl();
        readonly TextBox outlineFilter = new TextBox { PlaceholderText = "Filter", Margin = new Thickness(4) };
        readonly TabView tabStrip = new TabView
        {
            IsAddTabButtonVisible = false,
            CanDragTabs = false
        };
        readonly ContentControl contentHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        readonly ErrorListView errors = new ErrorListView();
        readonly Dictionary<Document, DocumentView> views = new Dictionary<Document, DocumentView>();
        readonly Dictionary<Document, TabViewItem> tabs = new Dictionary<Document, TabViewItem>();
        bool syncingSelection;
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

        MenuFlyoutItem undoItem, redoItem, cutItem, copyItem, pasteItem, deleteItem, selectAllItem, refreshItem;
        MenuFlyoutItem closeItem, closeAllItem, saveAllItem;

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

            // Paste's enabled state follows the clipboard, which other applications change while
            // this window is in the background; coming back to the foreground is when it can have
            // gone stale. Clipboard spec Decision 8.
            Activated += OnWindowActivated;

            // Before shell.New() below: New does Documents.Add then sets CurrentDocument, so
            // subscribing after it means the first document never gets a tab. Spec Decision 4.
            shell.Documents.CollectionChanged += OnDocumentsChanged;
            tabStrip.SelectionChanged += OnTabSelectionChanged;
            tabStrip.TabCloseRequested += OnTabCloseRequested;

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
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var menu = BuildMenu();
            Grid.SetRow(menu, 0);
            root.Children.Add(menu);

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

            Grid.SetColumn(toolboxView, 0);
            columns.Children.Add(toolboxView);

            // TabView is a header strip only. Measured: its template never applies in the test
            // process - zero visual children, a selected item's Content never enters the visual
            // tree - so anything hosted inside a tab is invisible to every test, including the
            // DesignSurface that MainWindowInteractionTests asserts it can find. Spec Decision 1.
            var centre = new Grid();
            centre.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            centre.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(tabStrip, 0);
            Grid.SetRow(contentHost, 1);
            centre.Children.Add(tabStrip);
            centre.Children.Add(contentHost);
            Grid.SetColumn(centre, 1);
            columns.Children.Add(centre);

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(outlineFilter, 0);
            right.Children.Add(outlineFilter);

            // OutlineViewControl and PropertyGridControl both derive from StackPanel and contain
            // no ScrollViewer of their own, so without the two wrappers below they clip on any
            // real document. Phase 5 Decision 10.
            var outlineScroll = new ScrollViewer
            {
                Content = outlineView,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(outlineScroll, 1);
            right.Children.Add(outlineScroll);

            // Horizontal scrolling disabled, unlike the outline above: a ScrollViewer that may
            // scroll horizontally measures its content at infinite width, so each row's Star
            // editor column grew to whatever its editor asked for and the rows ran past the 280px
            // column, cutting values off at the window edge. Disabled, the rows are measured at
            // the column's own width and a long value is clipped inside its editor instead.
            var gridScroll = new ScrollViewer
            {
                Content = propertyGridHost,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(gridScroll, 2);
            right.Children.Add(gridScroll);

            Grid.SetColumn(right, 2);
            columns.Children.Add(right);

            Grid.SetRow(columns, 1);
            root.Children.Add(columns);

            // A full-width strip rather than the reference's right-column pane: that column
            // already holds three things and a XAML parse message wraps several times at 280px.
            // The list caps its own height - an Auto row is exactly the unbounded parent that
            // makes a ListView realize every container. Spec Decision 18.
            errors.JumpRequested += OnJumpToErrorRequested;
            Grid.SetRow(errors, 2);
            root.Children.Add(errors);
            return root;
        }

        MenuBar BuildMenu()
        {
            var file = new MenuBarItem { Title = "File" };
            file.Items.Add(Item("New", VirtualKey.N, VirtualKeyModifiers.Control, (s, e) => Fire(NewAsync)));
            file.Items.Add(Item("Open...", VirtualKey.O, VirtualKeyModifiers.Control, (s, e) => Fire(OpenAsync)));
            closeItem = Item("Close", VirtualKey.None, VirtualKeyModifiers.None,
                (s, e) => Fire(() => RequestCloseAsync(shell.CurrentDocument, PrepareForCloseAsync)));
            closeAllItem = Item("Close All", VirtualKey.None, VirtualKeyModifiers.None,
                (s, e) => Fire(() => CloseAllAsync(PrepareForCloseAsync)));
            file.Items.Add(closeItem);
            file.Items.Add(closeAllItem);
            file.Items.Add(new MenuFlyoutSeparator());
            file.Items.Add(Item("Save", VirtualKey.S, VirtualKeyModifiers.Control, (s, e) => Fire(() => SaveAsync(shell.CurrentDocument, false))));
            file.Items.Add(Item("Save As...", VirtualKey.None, VirtualKeyModifiers.None, (s, e) => Fire(() => SaveAsync(shell.CurrentDocument, true))));
            saveAllItem = Item("Save All", VirtualKey.None, VirtualKeyModifiers.None, (s, e) => Fire(() => SaveAllAsync(SaveAsync)));
            file.Items.Add(saveAllItem);
            file.Items.Add(new MenuFlyoutSeparator());
            file.Items.Add(Item("Exit", VirtualKey.None, VirtualKeyModifiers.None, (s, e) => Close()));

            undoItem = Item("Undo", VirtualKey.Z, VirtualKeyModifiers.Control, (s, e) => WithUndo(u => u.Undo()));
            redoItem = Item("Redo", VirtualKey.Y, VirtualKeyModifiers.Control, (s, e) => WithUndo(u => u.Redo()));
            cutItem = Item("Cut", VirtualKey.X, VirtualKeyModifiers.Control,
                (s, e) => CutSelection(), Guarded(CutSelection));
            copyItem = Item("Copy", VirtualKey.C, VirtualKeyModifiers.Control,
                (s, e) => CopySelection(), Guarded(CopySelection));
            pasteItem = Item("Paste", VirtualKey.V, VirtualKeyModifiers.Control,
                (s, e) => PasteClipboard(), Guarded(PasteClipboard));
            deleteItem = Item("Delete", VirtualKey.Delete, VirtualKeyModifiers.None,
                (s, e) => DeleteSelection(), OnDeleteAcceleratorInvoked);
            selectAllItem = Item("Select All", VirtualKey.A, VirtualKeyModifiers.Control,
                (s, e) => SelectAll(), OnSelectAllAcceleratorInvoked);
            refreshItem = Item("Refresh", VirtualKey.F5, VirtualKeyModifiers.None,
                (s, e) => RefreshCurrentDocument());

            var edit = new MenuBarItem { Title = "Edit" };
            edit.Items.Add(undoItem);
            edit.Items.Add(redoItem);
            edit.Items.Add(new MenuFlyoutSeparator());
            edit.Items.Add(cutItem);
            edit.Items.Add(copyItem);
            edit.Items.Add(pasteItem);
            edit.Items.Add(deleteItem);
            edit.Items.Add(new MenuFlyoutSeparator());
            edit.Items.Add(selectAllItem);
            edit.Items.Add(refreshItem);

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
        /// its accelerator. Refuses to act while a text-entry control has focus. Phase 5 Decision 21.
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

        /// <summary>
        /// Cut, Copy and Paste, each shared by its menu item and its accelerator. Same text-entry
        /// guard as <see cref="DeleteSelection"/>: Ctrl+C/X/V in the XAML editor or the outline
        /// filter act on that box's text, not on the controls. Clipboard spec Decision 9.
        /// </summary>
        void CutSelection()
        {
            if (IsTextEntryFocused())
                return;
            WithClipboard((svc, ctx) => { if (svc.CanCut(ctx)) svc.Cut(ctx); });
        }

        void CopySelection()
        {
            if (IsTextEntryFocused())
                return;
            WithClipboard((svc, ctx) => { if (svc.CanCopy(ctx)) svc.Copy(ctx); });
        }

        void PasteClipboard()
        {
            if (IsTextEntryFocused())
                return;
            WithClipboard((svc, ctx) => { if (svc.CanPaste(ctx)) svc.Paste(ctx); });
        }

        /// <summary>An accelerator handler with the Handled asymmetry of
        /// <see cref="OnDeleteAcceleratorInvoked"/>: it declines while a text box has focus, so the
        /// keystroke reaches the box, and marks the event handled only when it acts.</summary>
        TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> Guarded(Action command)
        {
            return (sender, args) =>
            {
                if (IsTextEntryFocused())
                    return;
                command();
                args.Handled = true;
            };
        }

        void OnWindowActivated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
                RefreshCommands();
        }

        /// <summary>
        /// Select All's one implementation, shared by the menu item and the Ctrl+A accelerator.
        /// Same text-entry guard as <see cref="DeleteSelection"/>, for the same reason: Ctrl+A in
        /// the XAML editor or the outline filter selects that box's text, not the controls.
        /// </summary>
        void SelectAll()
        {
            if (IsTextEntryFocused() || wiredSelection == null || wiredDocument == null || wiredDocument.DesignContext == null)
                return;
            var targets = SelectAllTargets(wiredDocument.DesignContext, wiredSelection.PrimarySelection);
            if (targets.Count > 0)
                wiredSelection.SetSelectedComponents(targets);
        }

        /// <summary>Same Handled asymmetry as <see cref="OnDeleteAcceleratorInvoked"/>.</summary>
        void OnSelectAllAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (IsTextEntryFocused())
                return;

            SelectAll();
            args.Handled = true;
        }

        /// <summary>
        /// What Select All selects: every child of the container holding the primary selection,
        /// or the root's children when the root or nothing is selected.
        /// </summary>
        /// <remarks>
        /// "All" is ambiguous for a tree - the Phase 5 spec left Select All out for exactly that
        /// reason. Selecting every descendant would hand a move or a resize both a panel and its
        /// own children at once; selecting siblings is what a designer's Ctrl+A does, and it
        /// widens naturally from wherever the user already is. A design-time-locked child is left
        /// out, matching DesignPanel.HitTest, which never lets a locked item be clicked.
        /// </remarks>
        internal static IList<DesignItem> SelectAllTargets(DesignContext context, DesignItem primary)
        {
            var root = context.RootItem;
            var container = primary == null || primary == root ? root : (primary.Parent ?? root);
            var content = container?.ContentProperty;
            if (content == null)
                return new DesignItem[0];

            var children = content.IsCollection
                ? content.CollectionElements.ToList()
                : (content.Value != null ? new List<DesignItem> { content.Value } : new List<DesignItem>());
            return children
                .Where(item => item.View == null || !WinUIDesigner.XamlDom.DesignTimeProperties.GetIsLocked(item.View))
                .ToList();
        }

        /// <summary>F5: rebuilds the current document's surface from its XAML.</summary>
        internal void RefreshCurrentDocument()
        {
            var doc = shell.CurrentDocument;
            if (doc != null)
                doc.Refresh();
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

            var focused = FocusManager.GetFocusedElement(Content.XamlRoot) as DependencyObject;
            if (!(focused is TextBox || focused is RichEditBox || focused is NumberBox))
                return false;

            // A text box that is not on screen is not what the user is typing into. Switching a
            // document from XAML to Design collapses its editor and leaves focus on it, and
            // nothing on the surface takes focus when clicked, so without this the editor kept
            // every guarded command - Delete, Select All, Cut, Copy, Paste - declining after any
            // XAML round trip (found by the walkthrough, steps 12c-12f).
            for (var element = focused; element != null; element = VisualTreeHelper.GetParent(element))
            {
                var ui = element as UIElement;
                if (ui != null && ui.Visibility == Visibility.Collapsed)
                    return false;
            }
            return true;
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
            // Phase 5 Decision 19.
            propertyGrid = new PropertyGridControl();
            propertyGridHost.Content = propertyGrid;

            if (wiredDocument == null)
            {
                contentHost.Content = null;
                errors.Show(null);
                outlineView.Root = null;
                Title = ApplicationTitle;
                RefreshCommands();
                return;
            }

            contentHost.Content = ViewFor(wiredDocument);
            errors.Show(wiredDocument.LoadErrors);
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

            SyncSelectedTab();
            UpdateTitle();
            RefreshCommands();
        }

        /// <summary>
        /// Forces the current document dirty after an unhandled exception, so the close gate
        /// prompts rather than discarding a half-mutated tree silently. Phase 5 Decision 18.
        /// </summary>
        /// <remarks>
        /// Called by App's UnhandledException handler. Safe with no document open. The title
        /// repaints through OnDocumentPropertyChanged: MarkSuspect raises PropertyChanged("Title")
        /// when it flips IsDirty, like every other title change.
        /// </remarks>
        public void MarkCurrentDocumentSuspect()
        {
            var doc = shell.CurrentDocument;
            if (doc == null)
                return;

            doc.MarkSuspect();
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
            selectAllItem.IsEnabled = wiredSelection != null;
            refreshItem.IsEnabled = wiredDocument != null;

            var ctx = wiredDocument == null ? null : wiredDocument.DesignContext;
            var svc = ctx == null ? null : ctx.Services.GetService<ICopyPasteService>();
            cutItem.IsEnabled = svc != null && svc.CanCut(ctx);
            copyItem.IsEnabled = svc != null && svc.CanCopy(ctx);
            pasteItem.IsEnabled = svc != null && svc.CanPaste(ctx);
            deleteItem.IsEnabled = svc != null && svc.CanDelete(ctx);

            var hasDocument = shell.CurrentDocument != null;
            closeItem.IsEnabled = hasDocument;
            closeAllItem.IsEnabled = hasDocument;
            saveAllItem.IsEnabled = hasDocument;
        }

        /// <summary>Test seam: the tab strip.</summary>
        internal TabView TabStripForTest { get { return tabStrip; } }

        /// <summary>Test seam: the host that shows the current document's view.</summary>
        internal ContentControl ContentHostForTest { get { return contentHost; } }

        /// <summary>Test seam: the error strip.</summary>
        internal ErrorListView ErrorsForTest { get { return errors; } }

        /// <summary>Test seam: File &gt; Close.</summary>
        internal MenuFlyoutItem CloseItemForTest { get { return closeItem; } }

        /// <summary>Test seam: File &gt; Close All.</summary>
        internal MenuFlyoutItem CloseAllItemForTest { get { return closeAllItem; } }

        /// <summary>Test seam: File &gt; Save All.</summary>
        internal MenuFlyoutItem SaveAllItemForTest { get { return saveAllItem; } }

        internal MenuFlyoutItem SelectAllItemForTest { get { return selectAllItem; } }

        internal MenuFlyoutItem RefreshItemForTest { get { return refreshItem; } }

        /// <summary>Test seam: Edit &gt; Cut.</summary>
        internal MenuFlyoutItem CutItemForTest { get { return cutItem; } }

        /// <summary>Test seam: Edit &gt; Copy.</summary>
        internal MenuFlyoutItem CopyItemForTest { get { return copyItem; } }

        /// <summary>Runs Select All as the menu would. Test seam.</summary>
        internal void SelectAllForTest()
        {
            SelectAll();
        }

        /// <summary>The tab built for <paramref name="doc"/>, or null once it has closed.</summary>
        internal TabViewItem TabFor(Document doc)
        {
            TabViewItem tab;
            return doc != null && tabs.TryGetValue(doc, out tab) ? tab : null;
        }

        void OnJumpToErrorRequested(object sender, WinUIDesigner.DesignModel.Xaml.XamlError error)
        {
            var view = ViewFor(shell.CurrentDocument);
            if (view != null)
                view.JumpToError(error);
        }

        /// <summary>The view built for <paramref name="doc"/>, or null once it has closed.</summary>
        internal DocumentView ViewFor(Document doc)
        {
            DocumentView view;
            return doc != null && views.TryGetValue(doc, out view) ? view : null;
        }

        /// <summary>
        /// Applies a Documents change to the strip: removes tabs/views for what left, adds them
        /// for what arrived, then syncs the selection to whatever ShellModel now says is current.
        /// </summary>
        /// <remarks>
        /// The whole body runs under syncingSelection, not just the SyncSelectedTab call at the
        /// end. In a real, template-applying TabView, ForgetDocument's TabItems.Remove(tab) - when
        /// the removed tab was the selected one - makes the TabView auto-select a neighbour and
        /// raise SelectionChanged synchronously, still inside this call stack and therefore still
        /// inside ShellModel.Close's own Documents.Remove, before Close reaches its own "if
        /// (CurrentDocument == doc) CurrentDocument = Documents[0]" line. An unguarded
        /// OnTabSelectionChanged would activate that neighbour there and then, so by the time
        /// Close's own reassignment runs it would be a no-op and the final current document would
        /// be whatever the TabView picked rather than what ShellModel.Close chose - the opposite of
        /// Spec Decision 5. Cannot be covered by a test here: SelectionChanged never fires in this
        /// test process, so this has to be right by construction rather than by a passing test.
        /// </remarks>
        void OnDocumentsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var wasSyncing = syncingSelection;
            syncingSelection = true;
            try
            {
                if (e.OldItems != null)
                {
                    foreach (Document doc in e.OldItems)
                        ForgetDocument(doc);
                }

                if (e.NewItems != null)
                {
                    foreach (Document doc in e.NewItems)
                        RememberDocument(doc);
                }

                if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    foreach (var doc in new List<Document>(views.Keys))
                        ForgetDocument(doc);
                }

                SyncSelectedTab();
            }
            finally
            {
                syncingSelection = wasSyncing;
            }

            RefreshCommands();
        }

        void RememberDocument(Document doc)
        {
            if (doc == null || views.ContainsKey(doc))
                return;

            var view = new DocumentView(doc);
            views.Add(doc, view);

            var tab = new TabViewItem { Header = doc.Title, IsClosable = true, Content = null };
            tabs.Add(doc, tab);
            tabStrip.TabItems.Add(tab);

            // Every document, not only the current one: a background document's Title changes
            // through MarkSaved (Save All), FilePath (Save As) and SetTempName, and its tab
            // header has to follow. Spec Decision 22.
            doc.PropertyChanged += OnDocumentPropertyChanged;
        }

        void ForgetDocument(Document doc)
        {
            if (doc == null)
                return;

            doc.PropertyChanged -= OnDocumentPropertyChanged;

            DocumentView view;
            if (views.TryGetValue(doc, out view))
            {
                if (ReferenceEquals(contentHost.Content, view))
                    contentHost.Content = null;
                view.Detach();
                views.Remove(doc);
            }

            TabViewItem tab;
            if (tabs.TryGetValue(doc, out tab))
            {
                tabStrip.TabItems.Remove(tab);
                tabs.Remove(doc);
            }
        }

        void OnDocumentPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var doc = sender as Document;
            if (doc == null)
                return;

            if (e.PropertyName == "Title" || e.PropertyName == "Name")
            {
                TabViewItem tab;
                if (tabs.TryGetValue(doc, out tab))
                    tab.Header = doc.Title;
                if (ReferenceEquals(doc, shell.CurrentDocument))
                    UpdateTitle();
            }
            else if (e.PropertyName == "Mode" || e.PropertyName == "DesignContext")
            {
                // Only the current document. A background tab changing mode must not take the
                // outline, property grid and toolbox away from the visible one. Spec Decision 22.
                if (ReferenceEquals(doc, shell.CurrentDocument))
                    WireCurrentDocument();
            }
        }

        /// <summary>
        /// Makes <paramref name="doc"/> the current document. Every route into this - a tab
        /// click, a test - goes through here.
        /// </summary>
        /// <remarks>
        /// The logic lives in a method rather than in the TabView's SelectionChanged handler
        /// because that event never fires in the test process (measured), so anything left inside
        /// it would ship unverified. Spec Decision 3.
        /// </remarks>
        internal void ActivateDocument(Document doc)
        {
            if (doc == null || !shell.Documents.Contains(doc))
                return;
            shell.CurrentDocument = doc;
        }

        /// <summary>
        /// Asks about <paramref name="doc"/> and closes it if the answer allows.
        /// </summary>
        /// <remarks>
        /// <paramref name="decideClose"/> is a parameter for the reason the deleted
        /// ReplaceCurrentDocumentAsync had one: in production it is PrepareForCloseAsync, which
        /// shows a ContentDialog and so cannot run in a test process.
        /// <para>
        /// This never touches TabItems. The tab is removed by OnDocumentsChanged when
        /// ShellModel.Close actually removes the document, so a cancelled gate leaves the strip
        /// and the model in agreement by construction. Spec Decision 4.
        /// </para>
        /// </remarks>
        internal async Task RequestCloseAsync(Document doc, Func<Document, Task<CloseDecision>> decideClose)
        {
            if (doc == null)
                return;

            var decision = await decideClose(doc);
            if (!decision.Proceed)
                return;

            if (decision.ClearDirtyBeforeClose)
                doc.MarkSaved();

            // ShellModel.Close removes, re-points CurrentDocument, and disposes. Do not dispose
            // here as well: Close returns false when it refuses (it consults its own
            // AskSaveChanges, which this window answers Cancel), and a document it kept must stay
            // usable. Close owns the document's lifetime; this method only decides.
            shell.Close(doc);
        }

        /// <summary>
        /// Asks about every document, then closes them all. Cancel at any prompt abandons the
        /// whole operation with nothing closed.
        /// </summary>
        /// <remarks>
        /// Decide-all-then-discharge, the ordering ConfirmThenCloseAsync already uses for window
        /// close. Clearing dirty flags inside the loop would leave the documents already asked
        /// about looking clean when a later one answers Cancel. Spec Decision 9.
        /// </remarks>
        internal async Task CloseAllAsync(Func<Document, Task<CloseDecision>> decideClose)
        {
            var order = shell.Documents.ToList();
            var decisions = new List<KeyValuePair<Document, CloseDecision>>();

            foreach (var doc in order)
            {
                var decision = await decideClose(doc);
                if (!decision.Proceed)
                    return;
                decisions.Add(new KeyValuePair<Document, CloseDecision>(doc, decision));
            }

            foreach (var pair in decisions)
            {
                if (pair.Value.ClearDirtyBeforeClose)
                    pair.Key.MarkSaved();
            }

            // Close owns disposal - see RequestCloseAsync. Nothing here disposes.
            foreach (var pair in decisions)
                shell.Close(pair.Key);
        }

        /// <summary>
        /// Saves every dirty document, asking for a path one at a time for any that has none.
        /// </summary>
        /// <remarks>
        /// Sequential and short-circuiting on purpose. ShellModel.Save refuses a null FilePath and
        /// raises an error, and ShowDialogAsync returns null for a second concurrent dialog, so a
        /// naive loop over three unsaved documents would swallow two failures in silence. The
        /// reference routes a pathless document to SaveAs the same way (XamlDesigner/Shell.cs:153-162).
        /// Spec Decision 8.
        /// </remarks>
        internal async Task SaveAllAsync(Func<Document, bool, Task> saveOne)
        {
            foreach (var doc in shell.Documents.ToList())
            {
                if (!doc.IsDirty)
                    continue;

                await saveOne(doc, doc.FilePath == null);

                // Still dirty means the save failed or the picker was dismissed. Stop rather than
                // opening another dialog the user did not ask for.
                if (doc.IsDirty)
                    return;
            }
        }

        void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingSelection)
                return;

            var tab = tabStrip.SelectedItem as TabViewItem;
            if (tab == null)
                return;

            foreach (var pair in tabs)
            {
                if (ReferenceEquals(pair.Value, tab))
                {
                    ActivateDocument(pair.Key);
                    return;
                }
            }
        }

        void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            foreach (var pair in tabs)
            {
                if (ReferenceEquals(pair.Value, args.Tab))
                {
                    var doc = pair.Key;
                    Fire(() => RequestCloseAsync(doc, PrepareForCloseAsync));
                    return;
                }
            }
        }

        /// <summary>
        /// Points the strip at whatever ShellModel says is current, one direction at a time.
        /// </summary>
        /// <remarks>
        /// ShellModel.Close re-points CurrentDocument while the TabView is reacting to the same
        /// removal, so without the flag the two pushes re-enter each other. Spec Decision 5.
        /// Saves and restores the previous flag value rather than unconditionally clearing it,
        /// because this can run nested inside OnDocumentsChanged's own guard (it calls this as its
        /// last step); unconditionally setting false on the way out would clear that outer guard
        /// while OnDocumentsChanged is still unwinding through ForgetDocument's TabItems.Remove.
        /// </remarks>
        void SyncSelectedTab()
        {
            var wasSyncing = syncingSelection;
            syncingSelection = true;
            try
            {
                var doc = shell.CurrentDocument;
                TabViewItem tab;
                tabStrip.SelectedItem = doc != null && tabs.TryGetValue(doc, out tab) ? tab : null;
            }
            finally
            {
                syncingSelection = wasSyncing;
            }
        }

        /// <summary>
        /// What <see cref="PrepareForCloseAsync"/> decided about an outgoing document.
        /// </summary>
        /// <remarks>
        /// Two fields rather than a bare bool because "the user said Discard" and "the document's
        /// dirty flag has been cleared" must stay separate facts. See PrepareForCloseAsync.
        /// </remarks>
        internal struct CloseDecision
        {
            /// <summary>False only when the user cancelled, or a requested save did not happen.</summary>
            public bool Proceed;

            /// <summary>
            /// True when the user chose Discard and the caller therefore owes the document a
            /// <see cref="Document.MarkSaved"/>.
            /// </summary>
            public bool ClearDirtyBeforeClose;
        }

        Task NewAsync()
        {
            // Additive. The replacement path existed only because the shell had no tab strip;
            // with one, a document left in the collection is reachable. Spec Decision 6.
            shell.New();
            return Task.CompletedTask;
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

            shell.Open(file.Path);
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

            // No UpdateTitle here, deliberately: doc may be a background tab (Save All), and the
            // window title belongs to the current one. A save that changes anything visible -
            // IsDirty cleared, FilePath assigned - raises Document.PropertyChanged("Title"), which
            // OnDocumentPropertyChanged routes to that document's own tab header, and to the
            // window title only when doc is the current document.
            if (!forceAsk && doc.FilePath != null)
            {
                shell.Save(doc);
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
        }

        void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            if (closingConfirmed || !shell.IsSomethingDirty)
                return;

            // AppWindowClosingEventArgs.Cancel is synchronous; ContentDialog.ShowAsync is not.
            // So cancel this close, ask, and close again once the answer is in. Phase 5 Decision 20.
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
