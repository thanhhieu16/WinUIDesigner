using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel.Xaml;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// One row of the error list.
    /// </summary>
    /// <remarks>
    /// A wrapper rather than binding XamlError directly, for the reason ToolboxView's ControlNode
    /// exists: with no XAML in this project there is no DataTemplate, so a ListView shows exactly
    /// one property through DisplayMemberPath, and an error needs its line, column and message
    /// together. Spec Decision 19.
    /// </remarks>
    public sealed class XamlErrorNode
    {
        public XamlErrorNode(XamlError error)
        {
            Error = error;
        }

        public XamlError Error { get; private set; }

        public string Description
        {
            get
            {
                if (Error == null)
                    return string.Empty;
                return Error.Line + ", " + Error.Column + " - " + Error.Message;
            }
        }
    }

    /// <summary>
    /// The errors the current document reported while parsing. Collapsed when there are none.
    /// </summary>
    public sealed class ErrorListView : UserControl
    {
        /// <summary>
        /// The list's height ceiling.
        /// </summary>
        /// <remarks>
        /// The row that hosts this control is Auto-height, which hands a ListView an infinite
        /// available height: CLAUDE.md records a measured 200x6360 ListView realizing every
        /// container inside a 200x680 parent. A document with two hundred parse errors would push
        /// the whole three-column area off screen. Spec Decision 18.
        /// </remarks>
        public const double MaxListHeight = 160;

        readonly ListView list;

        public ErrorListView()
        {
            list = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                DisplayMemberPath = "Description",
                MaxHeight = MaxListHeight
            };
            list.DoubleTapped += OnDoubleTapped;

            Content = list;
            Visibility = Visibility.Collapsed;
        }

        /// <summary>Raised when the user double-taps a row. Carries the error to jump to.</summary>
        public event EventHandler<XamlError> JumpRequested;

        /// <summary>Test seam: the list itself.</summary>
        internal ListView List
        {
            get { return list; }
        }

        /// <summary>
        /// Shows <paramref name="errors"/>, or collapses the control when there are none.
        /// </summary>
        public void Show(IReadOnlyList<XamlError> errors)
        {
            var rows = new List<XamlErrorNode>();
            if (errors != null)
            {
                foreach (var error in errors)
                    rows.Add(new XamlErrorNode(error));
            }

            list.ItemsSource = rows;
            Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void OnDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            var node = list.SelectedItem as XamlErrorNode;
            if (node == null || node.Error == null)
                return;

            var handler = JumpRequested;
            if (handler != null)
                handler(this, node.Error);
        }
    }
}
