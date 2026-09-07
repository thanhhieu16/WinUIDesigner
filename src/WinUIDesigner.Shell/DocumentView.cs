using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUIDesigner.DesignModel.Xaml;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// One document's centre pane: its design surface, its XAML text, and the bar that switches
    /// between them.
    /// </summary>
    /// <remarks>
    /// One instance per <see cref="Document"/>, held by MainWindow and shown in a single
    /// ContentControl while its document is current. It is never a TabViewItem's Content: a
    /// TabView's template does not apply in the test process (measured - zero visual children,
    /// content never realized), so anything inside a tab is invisible to every test.
    /// A DesignSurface is therefore never re-parented; it stays inside this view for the view's
    /// whole life. Spec Decisions 1 and 2.
    /// </remarks>
    public sealed class DocumentView : UserControl
    {
        readonly Document document;
        readonly ContentControl surfaceHost;
        readonly ScrollViewer surfaceScroll;
        readonly TextBox editor;
        readonly RadioButton designButton;
        readonly RadioButton xamlButton;

        bool suppressModeButtons;
        bool suppressTextSync;
        bool detached;

        public DocumentView(Document document)
        {
            if (document == null)
                throw new ArgumentNullException("document");

            this.document = document;

            surfaceHost = new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = document.DesignSurface
            };

            // The surface's own ScrollViewer, moved down from MainWindow. The editor must NOT be
            // inside it: a TextBox brings its own, and nesting hands the inner one an infinite
            // available height - the trap CLAUDE.md measured on ListView.
            surfaceScroll = new ScrollViewer
            {
                Content = surfaceHost,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            editor = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                IsSpellCheckEnabled = false,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New")
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);

            // Radio groups resolve by name across the whole visual root, not per parent, so two
            // open documents sharing a group name would uncheck each other's buttons. Spec
            // Decision 21.
            var group = "DocumentMode_" + Guid.NewGuid().ToString("N");
            designButton = new RadioButton { GroupName = group, Content = "Design", Margin = new Thickness(4, 2, 4, 2) };
            xamlButton = new RadioButton { GroupName = group, Content = "XAML", Margin = new Thickness(4, 2, 4, 2) };

            var modeBar = new StackPanel { Orientation = Orientation.Horizontal };
            modeBar.Children.Add(designButton);
            modeBar.Children.Add(xamlButton);

            var body = new Grid();
            body.Children.Add(surfaceScroll);
            body.Children.Add(editor);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(body, 0);
            Grid.SetRow(modeBar, 1);
            root.Children.Add(body);
            root.Children.Add(modeBar);
            Content = root;

            // Seed BEFORE subscribing. The other order makes this assignment echo back through
            // TextChanged into Document.Text's setter and opens every document dirty. The
            // reference wires TextChanged in its Loaded handler for the same reason.
            editor.Text = document.Text ?? string.Empty;
            editor.TextChanged += OnEditorTextChanged;

            designButton.Checked += OnModeButtonChecked;
            xamlButton.Checked += OnModeButtonChecked;

            document.PropertyChanged += OnDocumentPropertyChanged;
            ApplyMode();
        }

        public Document Document
        {
            get { return document; }
        }

        /// <summary>Test seam: the XAML editor.</summary>
        internal TextBox Editor
        {
            get { return editor; }
        }

        /// <summary>Test seam: the design surface's visibility, without walking the tree.</summary>
        internal Visibility SurfaceVisibility
        {
            get { return surfaceScroll.Visibility; }
        }

        /// <summary>
        /// Releases the document subscription. Called when the document closes; without it the
        /// view keeps listening to a document nobody can reach.
        /// </summary>
        public void Detach()
        {
            if (detached)
                return;
            detached = true;
            document.PropertyChanged -= OnDocumentPropertyChanged;
            editor.TextChanged -= OnEditorTextChanged;
            designButton.Checked -= OnModeButtonChecked;
            xamlButton.Checked -= OnModeButtonChecked;
            surfaceHost.Content = null;
        }

        /// <summary>
        /// Switches to XAML mode and selects the token at <paramref name="error"/>'s position.
        /// </summary>
        /// <remarks>
        /// The offset is computed against the editor's text, not the document's: TextBox
        /// normalises CRLF to lone CR (measured), so the two differ by one character per
        /// preceding line and a line-40 error computed against Document.Text lands 39 characters
        /// early. Spec Decision 20.
        /// <para>
        /// Selects nothing at all when the buffer is no longer the text those coordinates were
        /// reported against. That guard used to refuse every jump the error strip could offer:
        /// XamlParser.ReportException reports and continues whenever an error sink is present, so
        /// a document with an unknown property or a bad conversion parses successfully -
        /// LoadFailed false, LoadErrors non-empty - which is the only way the strip fills on a
        /// freshly opened file, and the mode switch above then re-serialized the parsed tree over
        /// the buffer the coordinates addressed. Document.Mode's setter no longer re-serializes a
        /// document that has not moved on from what was parsed, so for that case the buffer here
        /// is still the file's own text and the jump lands. The guard stays for the cases where
        /// the buffer genuinely has moved on - a surface edit, typing, or a save from Design mode
        /// - because selecting at coordinates into text that no longer exists would silently take
        /// the user to an unrelated token.
        /// </para>
        /// </remarks>
        public void JumpToError(XamlError error)
        {
            if (error == null)
                return;

            document.Mode = DocumentMode.Xaml;

            var text = editor.Text ?? string.Empty;
            if (Document.NormaliseLineEndings(text) != document.ParsedText)
            {
                editor.SelectionStart = 0;
                editor.SelectionLength = 0;
                return;
            }

            int start = OffsetOf(text, error.Line, error.Column);
            if (start < 0)
            {
                editor.SelectionStart = 0;
                editor.SelectionLength = 0;
                return;
            }

            editor.SelectionStart = start;
            editor.SelectionLength = TokenLength(text, start);
            editor.Focus(FocusState.Programmatic);
        }

        /// <summary>
        /// The offset of 1-based <paramref name="line"/>/<paramref name="column"/> in
        /// <paramref name="text"/>, or -1 when either is out of range. CR, LF and CRLF each count
        /// as one break.
        /// </summary>
        internal static int OffsetOf(string text, int line, int column)
        {
            if (text == null || line < 1 || column < 1)
                return -1;

            int i = 0;
            int currentLine = 1;
            while (currentLine < line && i < text.Length)
            {
                char c = text[i];
                if (c == '\r')
                {
                    i++;
                    if (i < text.Length && text[i] == '\n')
                        i++;
                    currentLine++;
                }
                else if (c == '\n')
                {
                    i++;
                    currentLine++;
                }
                else
                {
                    i++;
                }
            }

            if (currentLine < line)
                return -1;

            // The column has to land inside the line just located, not merely inside the string.
            // Bounding it by text.Length instead let OffsetOf("one\r\ntwo", 1, 6) return 5 - the
            // first character of line 2 - where Spec Decision 20 says an out-of-range line OR
            // column selects nothing. One past the last character is still in range, matching the
            // end-of-text case this replaced.
            int lineEnd = i;
            while (lineEnd < text.Length && text[lineEnd] != '\r' && text[lineEnd] != '\n')
                lineEnd++;

            int offset = i + (column - 1);
            return offset > lineEnd ? -1 : offset;
        }

        /// <summary>
        /// How far forward from <paramref name="start"/> the token runs. Ported from the
        /// reference's scan (space, '.', '&lt;', '&gt;', '"'), with one addition it did not need:
        /// this one stops at the end of the text rather than reading past it.
        /// </summary>
        internal static int TokenLength(string text, int start)
        {
            if (text == null || start < 0 || start >= text.Length)
                return 0;

            int n = 0;
            while (start + n < text.Length)
            {
                char c = text[start + n];
                if (c == ' ' || c == '.' || c == '<' || c == '>' || c == '"')
                    break;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Pushes the editor's text into the document. The seam the tests drive.
        /// </summary>
        /// <remarks>
        /// Measured: TextBox.TextChanged does not fire for a programmatic assignment in a test
        /// process - not detached, not in a live tree, not even through SetValue on TextProperty,
        /// while Text itself updates correctly every time. Real typing raises it, so production is
        /// unaffected, but no test here can drive that path. Keeping the body in a method rather
        /// than in the handler is the same move Decision 3 makes for the tab strip, and for the
        /// same reason: logic left inside an event handler is untestable in this repo.
        /// </remarks>
        internal void PushEditorTextToDocument()
        {
            if (suppressTextSync)
                return;
            document.Text = editor.Text;
        }

        void OnEditorTextChanged(object sender, TextChangedEventArgs e)
        {
            PushEditorTextToDocument();
        }

        void OnDocumentPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Text")
            {
                var wanted = document.Text ?? string.Empty;
                // Compare normalised: the editor holds lone CR where the document holds CRLF, so
                // a raw comparison would reassign on every raise and stamp on the caret.
                if (Document.NormaliseLineEndings(editor.Text) == Document.NormaliseLineEndings(wanted))
                    return;

                suppressTextSync = true;
                try
                {
                    editor.Text = wanted;
                }
                finally
                {
                    suppressTextSync = false;
                }
            }
            else if (e.PropertyName == "Mode")
            {
                ApplyMode();
            }
        }

        void ApplyMode()
        {
            surfaceScroll.Visibility = document.InDesignMode ? Visibility.Visible : Visibility.Collapsed;
            editor.Visibility = document.InXamlMode ? Visibility.Visible : Visibility.Collapsed;

            // Pushing Mode back into IsChecked raises Checked, which would re-enter the Mode
            // setter. The setter's equality guard makes that harmless, but the flag keeps the
            // two from depending on each other. Spec Decision 21.
            suppressModeButtons = true;
            try
            {
                designButton.IsChecked = document.InDesignMode;
                xamlButton.IsChecked = document.InXamlMode;
            }
            finally
            {
                suppressModeButtons = false;
            }
        }

        void OnModeButtonChecked(object sender, RoutedEventArgs e)
        {
            if (suppressModeButtons)
                return;
            document.Mode = ReferenceEquals(sender, xamlButton) ? DocumentMode.Xaml : DocumentMode.Design;
        }
    }
}
