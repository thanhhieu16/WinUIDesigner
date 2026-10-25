using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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

        string filter;

        /// <summary>Case-insensitive name filter. A node survives if its own name matches or any
        /// descendant's does; the root always survives, so the filter narrows branches rather
        /// than emptying the panel. Filtering rebuilds the row tree, which discards nothing
        /// persistent - expand state lives on the nodes, not on the rows.
        ///
        /// The text input belongs to the host, not here: no text-input control can be laid out
        /// in this project's test host (a TextBox or AutoSuggestBox throws E_FAIL from
        /// UpdateLayout, and several such tests together crash the test process), and the
        /// reference splits it the same way - its ClearableTextBox lives in Outline.xaml and
        /// binds to the tree control's Filter property. See spec Decision 12.</summary>
        public string Filter {
            get { return filter; }
            set {
                filter = value;
                Rebuild();
            }
        }

        bool Matches(IOutlineNode node)
        {
            if (string.IsNullOrEmpty(filter))
                return true;
            if (node.Name != null && node.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return node.Children.Any(Matches);
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

        /// <summary>Discards and rebuilds the whole row tree from Root. Callers that are not
        /// themselves a model event handler (the Root and Filter setters, TryApplyDrop) call this
        /// directly and get their rows synchronously; model events go through RequestRebuild
        /// instead.</summary>
        internal void Rebuild()
        {
            rebuildPending = false;

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
                if (Matches(child))
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

            // A PropertyOutlineNode stands for a property, not an element: its DesignItem is null,
            // so it gets no hide/lock toggles - both would dereference DesignItem and throw.
            if (node.DesignItem != null) {
                // The reference uses Eye.png/EyeClose.png through an IconItem control; this port
                // ships no image resources, so Segoe Fluent Icons glyphs stand in (spec Decision
                // 3). E7B3 = View, E72E = Lock.
                var hideToggle = new ToggleButton {
                    Content = new FontIcon { Glyph = "" },
                    IsChecked = !node.IsDesignTimeVisible,
                };
                hideToggle.Checked += (s, e) => node.IsDesignTimeVisible = false;
                hideToggle.Unchecked += (s, e) => node.IsDesignTimeVisible = true;
                Grid.SetColumn(hideToggle, 1);
                row.Children.Add(hideToggle);

                var lockToggle = new ToggleButton {
                    Content = new FontIcon { Glyph = "" },
                    IsChecked = node.IsDesignTimeLocked,
                };
                lockToggle.Checked += (s, e) => node.IsDesignTimeLocked = true;
                lockToggle.Unchecked += (s, e) => node.IsDesignTimeLocked = false;
                Grid.SetColumn(lockToggle, 2);
                row.Children.Add(lockToggle);
            }

            var nameText = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameText, 3);
            row.Children.Add(nameText);

            // Final-review fix (F4): HandleRowInvoked's extend branch used to be unreachable -
            // every caller hardcoded false - so Decision 6's multi-select could not happen from
            // the control at all. TappedRoutedEventArgs carries no modifier state, so the keys are
            // read at click time from the thread's keyboard state, the same mechanism (and the
            // same empirically-confirmed-in-this-unpackaged-host API) that
            // DefaultSelectionService.ResolveAutoSelectionType already uses
            // (src/WinUIDesigner.Designer/Services/SelectionService.cs:117-126).
            row.Tapped += (s, e) => { DiagTrace.Write("Outline row Tapped"); HandleRowInvoked(node, IsExtendSelectionKeyDown()); };

            if (node.DesignItem != null) {
                // Final-review fix (F3): DragListener registers its PointerPressed handler with
                // handledEventsToo and captures the pointer unconditionally, and ButtonBase
                // cancels its own click when it loses capture. Every other DragListener in this
                // codebase targets a bare thumb/overlay/handle with no interactive children; this
                // row is the first target that hosts other controls, so a press on the expand,
                // hide or lock toggle also reaches the row's listener. Remember where the press
                // landed and refuse to interpret that gesture as a row drag. A press on the row's
                // background or on its name TextBlock walks up to the row without meeting a
                // ToggleButton, so it still starts a drag normally. Defensive: the gesture cannot
                // be synthesised in this test host, so this is not a demonstrated failure.
                bool pressedOnToggle = false;
                row.AddHandler(UIElement.PointerPressedEvent,
                    (PointerEventHandler)((s, e) => pressedOnToggle = IsInsideToggleButton(e.OriginalSource, row)),
                    true);

                var drag = new Controls.DragListener(row);
                drag.Completed += d => {
                    if (d.IsCanceled || pressedOnToggle)
                        return;
                    var position = d.GetPosition(this);
                    if (position == null)
                        return;
                    var targetRow = RowUnder(position.Value, row);
                    if (targetRow == null)
                        return;
                    var target = NodeFor(targetRow);
                    if (target == null)
                        return;
                    var after = target.Children.Count > 0 ? target.Children[target.Children.Count - 1] : null;
                    TryApplyDrop(target, new List<IOutlineNode> { node }, after, false);
                };
            }

            return row;
        }

        /// <summary>The row whose bounds contain a point expressed in this control's coordinates,
        /// ignoring the row being dragged. There is no insert-line and no 3-zone hit test (spec
        /// Decision 5): a drop lands ON a row, appending to that row's node.</summary>
        FrameworkElement RowUnder(Windows.Foundation.Point point, FrameworkElement exclude)
        {
            foreach (var candidate in rowFromNode.Values) {
                if (candidate == exclude)
                    continue;
                var transform = candidate.TransformToVisual(this);
                var bounds = transform.TransformBounds(
                    new Windows.Foundation.Rect(0, 0, candidate.ActualWidth, candidate.ActualHeight));
                if (bounds.Contains(point))
                    return candidate;
            }
            return null;
        }

        /// <summary>True when <paramref name="source"/> is a ToggleButton, or sits inside one at
        /// or below <paramref name="row"/>. Internal so a test can drive it with a real row's real
        /// toggles: no pointer gesture can be synthesised in this test host, so the predicate is
        /// what is testable, not the press it guards.</summary>
        internal static bool IsInsideToggleButton(object source, FrameworkElement row)
        {
            var obj = source as DependencyObject;
            while (obj != null && obj != row) {
                if (obj is ToggleButton)
                    return true;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return false;
        }

        /// <summary>Ctrl or Shift held right now. See the Tapped wiring in BuildRow (F4).</summary>
        static bool IsExtendSelectionKeyDown()
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            return (ctrl & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down
                || (shift & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
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

        // Insert fires a burst of Children.CollectionChanged events (one per remove, one per
        // insert). Rebuilding on each would discard and recreate the very rows the drop is still
        // walking, so coalesce them into the single Rebuild at the end of TryApplyDrop. Same
        // guard-flag pattern the PropertyGrid editors use (commit b5eb9c1).
        bool suppressRebuild;

        /// <summary>Applies a drop to the model. The drag gesture funnels through here, and the
        /// tests drive it directly rather than synthesising a pointer gesture - the same approach
        /// DragListenerTests and MoveTests already take for drag-driven behaviour in this suite.
        /// Returns false, changing nothing, when the target cannot accept the items.</summary>
        internal bool TryApplyDrop(IOutlineNode target, IList<IOutlineNode> dragged, IOutlineNode after, bool copy)
        {
            if (target == null || target.DesignItem == null || dragged == null)
                return false;

            var items = dragged.Where(n => n != null && n.DesignItem != null).ToList();
            if (items.Count == 0)
                return false;

            // OutlineNodeBase.CanInsert deliberately has no cycle guard (spec Decision 14 - a
            // container reports it can accept its own descendant, matching the reference). Left
            // unchecked, dropping a node onto one of its own descendant rows - an ordinary
            // gesture, since children render directly below and indented under their parent and
            // are expanded by default - would let Insert Remove() the dragged node from its
            // parent and then insert it back into its own descendant's content while that
            // descendant is still among the dragged node's own Children: a live cycle. Rebuild()
            // below then walks node.Children recursively and recurses forever. Decision 14 itself
            // is not touched (Task 6's test pinning it must keep passing); the veto belongs here,
            // in the drag layer, not the model layer.
            if (items.Any(n => IsSelfOrDescendant(target, n)))
                return false;

            if (!target.CanInsert(items, after, copy))
                return false;

            suppressRebuild = true;
            try {
                target.Insert(items, after, copy);
            } finally {
                suppressRebuild = false;
            }
            Rebuild();
            return true;
        }

        /// <summary>True when candidate is dragged itself, or is found anywhere within dragged's
        /// own Children subtree - the cycle case Decision 14 leaves CanInsert free to allow, and
        /// that TryApplyDrop vetoes instead.</summary>
        static bool IsSelfOrDescendant(IOutlineNode candidate, IOutlineNode dragged)
        {
            if (candidate == dragged)
                return true;
            foreach (var child in dragged.Children) {
                if (IsSelfOrDescendant(candidate, child))
                    return true;
            }
            return false;
        }

        // Set while a Rebuild is already queued on the dispatcher - see RequestRebuild.
        bool rebuildPending;

        /// <summary>Queues a single Rebuild onto the dispatcher instead of running one now.
        ///
        /// Final-review fix (F2). Setting IsDesignTimeVisible/IsDesignTimeLocked writes an
        /// attached property, which raises DesignItem.PropertyChanged, which makes
        /// OutlineNodeBase call UpdateChildren(), which calls Children.Clear() - and
        /// ObservableCollection.Clear() raises Reset unconditionally, even when the collection is
        /// empty. Rebuilding straight off that event had two problems: a node with k children
        /// produced k+1 full rebuilds for one property change (Clear plus one Add per re-added
        /// child), and every one of them ran while the ToggleButton whose Checked handler started
        /// it was still on the stack - and the rebuild destroys that very ToggleButton.
        ///
        /// Deferring rather than extending suppressRebuild is deliberate: suppressRebuild can
        /// coalesce the burst, but whatever runs the deferred-until-the-end rebuild would still
        /// be running inside the toggle's own event handler, which is the half of this that
        /// actually bites. One dispatcher turn later, the handler has returned and nothing the
        /// rebuild frees is still in use. The O(n) rebuild itself is unchanged - an accepted cost
        /// recorded in the spec's Known gaps.</summary>
        void RequestRebuild()
        {
            if (suppressRebuild || rebuildPending)
                return;
            rebuildPending = true;
            var queue = this.DispatcherQueue;
            if (queue == null || !queue.TryEnqueue(() => { if (rebuildPending) Rebuild(); })) {
                // No dispatcher to defer onto (an element that never reached a live tree):
                // synchronous is still better than never rebuilding at all.
                Rebuild();
            }
        }

        void Node_ChildrenChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // A node's Children collection is rebuilt wholesale by OutlineNode.UpdateChildren, so
            // resynchronising the whole row tree is both correct and the simplest thing that is.
            RequestRebuild();
        }

        void Node_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var node = sender as IOutlineNode;
            if (node == null || !rowFromNode.ContainsKey(node))
                return;

            if (e.PropertyName == "Name") {
                RequestRebuild();
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
