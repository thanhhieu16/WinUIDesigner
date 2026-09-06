using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.OutlineView
{
    /// <summary>
    /// The design-time document-tree panel.
    ///
    /// Derives from StackPanel and builds its own row tree rather than hosting a
    /// Microsoft.UI.Xaml.Controls.TreeView: a TreeView cannot be realized in this project's test
    /// host at all - UpdateLayout() throws COMException "Cannot find a Resource with the Name/Key
    /// TabViewScrollButtonBackground", and merging XamlControlsResources to fix that fail-fast
    /// crashes the process (see TreeViewPlatformProbeTests, spec Decisions 4 and 5). This is the
    /// same shape PropertyGridControl uses, for the same reason a templateless Control would not
    /// work here: this codebase has zero .xaml files in which to declare a ControlTemplate.
    ///
    /// No shell app exists yet (Phase 5), so a host sets Root directly - the same host-less
    /// control-testing pattern PropertyGridControl uses.
    /// </summary>
    public class OutlineViewControl : StackPanel
    {
        const double IndentPerLevel = 12;

        readonly StackPanel rows = new StackPanel();
        readonly Dictionary<FrameworkElement, IOutlineNode> nodeFromRow = new Dictionary<FrameworkElement, IOutlineNode>();
        readonly Dictionary<IOutlineNode, FrameworkElement> rowFromNode = new Dictionary<IOutlineNode, FrameworkElement>();
        readonly Dictionary<IOutlineNode, ToggleButton> expandToggleFromNode = new Dictionary<IOutlineNode, ToggleButton>();
        readonly List<IOutlineNode> subscribed = new List<IOutlineNode>();
        readonly Brush selectionBrush = new SolidColorBrush(Colors.LightSteelBlue);

        // Guards against re-entrancy between the row, ISelectionService and the node - the same
        // guard-flag pattern the PropertyGrid editors use. No current call path actually reaches
        // it: ApplySelectionHighlight only ever sets Grid.Background, so it never re-enters
        // Node_PropertyChanged. Kept anyway as harmless defensive code - a later task may add a
        // path that does close the loop, and this flag will already be in place for it.
        bool syncingSelection;

        public OutlineViewControl()
        {
            Children.Add(rows);
        }

        IOutlineNode root;

        public IOutlineNode Root {
            get { return root; }
            set {
                root = value;
                Rebuild();
            }
        }

        internal IOutlineNode NodeFor(FrameworkElement row)
        {
            IOutlineNode node;
            return nodeFromRow.TryGetValue(row, out node) ? node : null;
        }

        internal FrameworkElement RowFor(IOutlineNode node)
        {
            FrameworkElement row;
            return rowFromNode.TryGetValue(node, out row) ? row : null;
        }

        // IOutlineNode does not declare INotifyPropertyChanged, but every implementation in this
        // assembly derives from OutlineNodeBase, which does. These two keep the cast in one place.
        void Subscribe(IOutlineNode node)
        {
            var source = node as INotifyPropertyChanged;
            if (source != null)
                source.PropertyChanged += Node_PropertyChanged;
            node.Children.CollectionChanged += Node_ChildrenChanged;
            subscribed.Add(node);
        }

        void Unsubscribe(IOutlineNode node)
        {
            var source = node as INotifyPropertyChanged;
            if (source != null)
                source.PropertyChanged -= Node_PropertyChanged;
            node.Children.CollectionChanged -= Node_ChildrenChanged;
        }

        /// <summary>Discards and rebuilds the whole row tree from Root.</summary>
        internal void Rebuild()
        {
            foreach (var node in subscribed) {
                Unsubscribe(node);
            }
            subscribed.Clear();
            nodeFromRow.Clear();
            rowFromNode.Clear();
            expandToggleFromNode.Clear();
            rows.Children.Clear();

            if (root == null)
                return;

            AppendNode(root, 0, rows);
        }

        void AppendNode(IOutlineNode node, int level, Panel host)
        {
            var row = BuildRow(node, level);
            nodeFromRow[row] = node;
            rowFromNode[node] = row;
            Subscribe(node);
            host.Children.Add(row);

            ApplySelectionHighlight(node);

            var childHost = new StackPanel {
                Visibility = node.IsExpanded ? Visibility.Visible : Visibility.Collapsed,
            };
            host.Children.Add(childHost);

            ToggleButton expandToggle;
            if (expandToggleFromNode.TryGetValue(node, out expandToggle)) {
                expandToggle.Checked += (s, e) => {
                    node.IsExpanded = true;
                    childHost.Visibility = Visibility.Visible;
                };
                expandToggle.Unchecked += (s, e) => {
                    node.IsExpanded = false;
                    childHost.Visibility = Visibility.Collapsed;
                };
            }

            foreach (var child in node.Children) {
                AppendNode(child, level + 1, childHost);
            }
        }

        /// <summary>The row visual for one node. Task 8 adds the hide and lock toggles into
        /// columns 1 and 2; this task fills column 0 with the expand toggle and column 3 with
        /// the name.</summary>
        protected virtual FrameworkElement BuildRow(IOutlineNode node, int level)
        {
            var row = new Grid { Margin = new Thickness(level * IndentPerLevel, 0, 0, 0) };
            // Four fixed columns, whether or not this node uses each: expand (0), hide (1),
            // lock (2), name (3). Sharing a column between a toggle and the name makes them
            // overlap - the same mistake an early draft of PropertyGridControl.BuildPropertyRow
            // made and its comment still warns about.
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Pixel) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (node.Children.Count > 0) {
                var expandToggle = new ToggleButton { Content = "▶", IsChecked = node.IsExpanded };
                Grid.SetColumn(expandToggle, 0);
                row.Children.Add(expandToggle);
                expandToggleFromNode[node] = expandToggle;
            }

            var nameText = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameText, 3);
            row.Children.Add(nameText);

            row.Tapped += (s, e) => HandleRowInvoked(node, false);
            return row;
        }

        /// <summary>The real row-click selection path, exposed so a test can drive it directly.
        /// Tapped carries no public constructor for its args, so this internal seam - reachable
        /// from WinUIDesigner.Designer.Tests via InternalsVisibleTo - is what makes the real path
        /// testable, exactly like DesignPanel's own extracted click-to-select seam.</summary>
        internal void HandleRowInvoked(IOutlineNode node, bool extend)
        {
            if (node.DesignItem == null)
                return;
            if (!extend) {
                node.SelectionService.SetSelectedComponents(new[] { node.DesignItem });
                return;
            }
            node.IsSelected = !node.IsSelected;
        }

        void ApplySelectionHighlight(IOutlineNode node)
        {
            FrameworkElement row;
            if (!rowFromNode.TryGetValue(node, out row))
                return;
            var grid = row as Grid;
            if (grid != null)
                grid.Background = node.IsSelected ? selectionBrush : null;
        }

        void Node_ChildrenChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // A node's Children collection is rebuilt wholesale by OutlineNode.UpdateChildren, so
            // resynchronising the whole row tree is both correct and the simplest thing that is.
            Rebuild();
        }

        void Node_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var node = sender as IOutlineNode;
            if (node == null || !rowFromNode.ContainsKey(node))
                return;

            if (e.PropertyName == "Name") {
                Rebuild();
            } else if (e.PropertyName == "IsSelected") {
                if (syncingSelection)
                    return;
                syncingSelection = true;
                try {
                    ApplySelectionHighlight(node);
                } finally {
                    syncingSelection = false;
                }
            }
        }
    }
}
