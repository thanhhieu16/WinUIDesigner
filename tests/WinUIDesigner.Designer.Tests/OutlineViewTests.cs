using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.OutlineView;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class OutlineViewTests
    {
        // The reference's own outline fixture, verbatim: HierarchyTests, SelectionTests and
        // InsertTests all use "<Button/><StackPanel><Button/></StackPanel>" inside a Grid.
        // It deliberately sets NO non-content property on the Grid: from Task 5 onwards every set
        // non-content property becomes a leading PropertyOutlineNode, which would shift every
        // index-based assertion in Tasks 4-10. Any test needing a non-visual collection element
        // declares its own XAML instead of adding one here.
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button />" +
            "<StackPanel><Button /></StackPanel>" +
            "</Grid>";

        // The complement of GridXaml, for the index-space tests below: the inner StackPanel has
        // BOTH a set element-syntax object property (RenderTransform - proven to become a leading
        // PropertyOutlineNode by ASetNonContentPropertyBecomesAPropertyNodeWrappingItsValue) and
        // content children, so its Children (3) and its ContentProperty.CollectionElements (2) no
        // longer index alike. GridXaml deliberately never has that shape, which is exactly why it
        // cannot catch a Children-vs-CollectionElements index confusion.
        const string TransformedStackPanelXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<StackPanel>" +
            "<StackPanel.RenderTransform><RotateTransform Angle=\"10\" /></StackPanel.RenderTransform>" +
            "<Button /><Button />" +
            "</StackPanel>" +
            "<Button />" +
            "</Grid>";

        /// <summary>The toggle currently sitting in <paramref name="column"/> of the row the
        /// control currently has for <paramref name="node"/>. Deliberately re-resolved from the
        /// control on every call rather than captured once: a rebuild replaces the row and every
        /// control in it, and a ToggleButton raises Checked whether or not it is still in the
        /// tree, so a stale reference would keep a test passing after the row wiring it is meant
        /// to exercise had been removed.</summary>
        static ToggleButton ToggleInColumn(OutlineViewControl control, IOutlineNode node, int column)
        {
            var row = control.RowFor(node) as Grid;
            if (row == null)
                return null;
            return row.Children.OfType<ToggleButton>().SingleOrDefault(t => Grid.GetColumn(t) == column);
        }

        /// <summary>The deepest first-child descendant of <paramref name="obj"/> - a stand-in for
        /// the templated element a real pointer press would report as its OriginalSource.</summary>
        static DependencyObject DeepestFirstChild(DependencyObject obj)
        {
            while (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(obj) > 0)
                obj = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(obj, 0);
            return obj;
        }

        static bool HasIsLockedProperty(DesignItem item)
        {
            return item.AllSetProperties.Any(p => p.IsAttachedProperty && p.Name == "IsLocked");
        }

        static DesignSurface LoadSurface(Window window, string xaml)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            return surface;
        }

        [Test]
        public void CanSelectComponentIsTrueForAVisualItemAndFalseForANonVisualOne()
        {
            WinUITestHost.RunWithWindow(window => {
                // Own fixture: RowDefinitions gives a non-visual collection element, and keeping
                // it out of GridXaml keeps every other test's child indices stable once Task 5
                // starts emitting PropertyOutlineNodes for set non-content properties.
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
                    "<Button />" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var button = grid.ContentProperty.CollectionElements[0];

                Assert.IsNotNull(button.View);
                Assert.IsTrue(ModelTools.CanSelectComponent(button));

                var rowDefinition = grid.Properties["RowDefinitions"].CollectionElements[0];
                Assert.IsNull(rowDefinition.View,
                    "a RowDefinition is not a UIElement, so its DesignItem must have no View");
                Assert.IsFalse(ModelTools.CanSelectComponent(rowDefinition));
            });
        }

        [Test]
        public void OutlineNodeNameIsTheTypeNameAloneOrWithTheXName()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Button /><Button x:Name=\"A\" />" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var names = surface.DesignContext.Services.GetService<IOutlineNodeNameService>();
                Assert.IsNotNull(names, "IOutlineNodeNameService was not registered");

                Assert.AreEqual("Button", names.GetOutlineNodeName(grid.ContentProperty.CollectionElements[0]));
                Assert.AreEqual("Button (A)", names.GetOutlineNodeName(grid.ContentProperty.CollectionElements[1]));
                Assert.AreEqual("", names.GetOutlineNodeName(null));
            });
        }

        [Test]
        public void TheOutlineMirrorsTheDesignItemHierarchy()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();
                Assert.IsNotNull(outline);

                Assert.AreEqual(2, outline.Children.Count);
                Assert.AreEqual(1, outline.Children[1].Children.Count);

                var button = grid.ContentProperty.CollectionElements[0];
                var stackPanel = grid.ContentProperty.CollectionElements[1];
                var stackPanelButton = stackPanel.ContentProperty.CollectionElements[0];

                Assert.AreEqual(button, outline.Children[0].DesignItem);
                Assert.AreEqual(stackPanel, outline.Children[1].DesignItem);
                Assert.AreEqual(stackPanelButton, outline.Children[1].Children[0].DesignItem);
                Assert.AreEqual("Button", outline.Children[0].Name);
            });
        }

        [Test]
        public void CreateOutlineNodeReturnsTheSameNodeForTheSameDesignItem()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                Assert.AreSame(grid.CreateOutlineNode(), grid.CreateOutlineNode());
            });
        }

        [Test]
        public void SelectingInTheDesignerSelectsTheMatchingOutlineNodes()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();

                var stackPanel = grid.ContentProperty.CollectionElements[1];
                var stackPanelButton = stackPanel.ContentProperty.CollectionElements[0];
                surface.DesignContext.Services.Selection.SetSelectedComponents(
                    new[] { stackPanel, stackPanelButton });

                Assert.IsFalse(outline.IsSelected);
                Assert.IsFalse(outline.Children[0].IsSelected);
                Assert.IsTrue(outline.Children[1].IsSelected);
                Assert.IsTrue(outline.Children[1].Children[0].IsSelected);
            });
        }

        [Test]
        public void SelectingAnOutlineNodeSelectsItInTheDesigner()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();
                var selection = surface.DesignContext.Services.Selection;

                selection.SetSelectedComponents(new DesignItem[0]);
                outline.IsSelected = true;
                outline.Children[0].IsSelected = true;

                Assert.IsTrue(selection.SelectedItems.Contains(grid));
                Assert.IsTrue(selection.SelectedItems.Contains(grid.ContentProperty.CollectionElements[0]));
            });
        }

        [Test]
        public void ASetNonContentPropertyBecomesAPropertyNodeWrappingItsValue()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Button><Button.RenderTransform><RotateTransform Angle=\"10\" /></Button.RenderTransform></Button>" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();

                var buttonNode = outline.Children[0];
                var transformNode = buttonNode.Children.FirstOrDefault(n => n.Name == "RenderTransform");
                Assert.IsNotNull(transformNode, "no PropertyOutlineNode was created for RenderTransform");
                Assert.AreEqual(1, transformNode.Children.Count);
                Assert.AreEqual(typeof(Microsoft.UI.Xaml.Media.RotateTransform),
                    transformNode.Children[0].DesignItem.ComponentType);
            });
        }

        [Test]
        public void AddingAndRemovingACollectionElementUpdatesTheChildNodes()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();
                Assert.AreEqual(2, outline.Children.Count);

                var actions = new List<NotifyCollectionChangedAction>();
                outline.Children.CollectionChanged += (s, e) => actions.Add(e.Action);

                var added = surface.DesignContext.Services.Component
                    .RegisterComponentForDesigner(new Button());
                grid.ContentProperty.CollectionElements.Add(added);
                Assert.AreEqual(3, outline.Children.Count);
                Assert.AreEqual(added, outline.Children[2].DesignItem);
                // An incremental update raises exactly one Add and never rebuilds via Reset; a full
                // UpdateChildren() rebuild (the pre-Task-5 behaviour) would raise a Reset followed by
                // one Add per surviving element instead.
                Assert.AreEqual(1, actions.Count(a => a == NotifyCollectionChangedAction.Add));
                Assert.IsFalse(actions.Contains(NotifyCollectionChangedAction.Reset));

                actions.Clear();
                grid.ContentProperty.CollectionElements.Remove(added);
                Assert.AreEqual(2, outline.Children.Count);
                Assert.IsTrue(outline.Children.All(n => n.DesignItem != added));
                Assert.AreEqual(1, actions.Count(a => a == NotifyCollectionChangedAction.Remove));
                Assert.IsFalse(actions.Contains(NotifyCollectionChangedAction.Reset));
            });
        }

        [Test]
        public void CanInsertReflectsWhetherTheTargetCanAcceptTheItems()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Button /><Image /><StackPanel><StackPanel /></StackPanel>" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();

                var buttonNode = outline.Children[0];
                var imageNode = outline.Children[1];
                var outerStackNode = outline.Children[2];
                var innerStackNode = outerStackNode.Children[0];

                // A Grid takes anything.
                Assert.IsTrue(outline.CanInsert(new[] { imageNode }, buttonNode, false));
                // A ContentControl with an unset Content takes a single item
                // (DefaultPlacementBehavior.internalCanEnterContainer, src/WinUIDesigner.Designer/
                // Extensions/DefaultPlacementBehavior.cs:99-100).
                Assert.IsTrue(buttonNode.CanInsert(new[] { imageNode }, null, false));
                // An Image has no ContentProperty, so DefaultPlacementBehavior.OnInitialized
                // never attaches an IPlacementBehavior to it and CanInsert must be false.
                Assert.IsFalse(imageNode.CanInsert(new[] { buttonNode }, null, false));
                // Ported deliberately without a cycle guard (spec Decision 14): inserting a
                // container into its own descendant reports true, exactly as the reference does.
                // Both nodes here are non-root, so the root guard below does not mask this.
                Assert.IsTrue(innerStackNode.CanInsert(new[] { outerStackNode }, null, false));
                // The document root can never be inserted anywhere: PlacementOperation.Start
                // resolves an IRootPlacementBehavior for a parentless item
                // (src/WinUIDesigner.DesignModel/PlacementOperation.cs:232) and nothing in this
                // port implements that interface - it was deferred in Phase 2 and never revisited
                // - so Start would throw PlacementOperationException. CanInsert must return false
                // rather than let that escape (spec Decision 16); the drop path in Task 10 calls
                // CanInsert to veto, and an exception there would break dragging.
                Assert.IsFalse(outerStackNode.CanInsert(new[] { outline }, null, false));
            });
        }

        [Test]
        public void InsertMovesAnItemBetweenContainersAsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int before = undo.UndoActions.Count();

                var stackPanel = grid.ContentProperty.CollectionElements[1];
                var stackPanelButton = stackPanel.ContentProperty.CollectionElements[0];
                outline.Insert(new[] { outline.Children[1].Children[0] }, outline.Children[0], false);

                Assert.AreEqual(3, grid.ContentProperty.CollectionElements.Count);
                Assert.AreEqual(0, stackPanel.ContentProperty.CollectionElements.Count);
                Assert.AreEqual(stackPanelButton, grid.ContentProperty.CollectionElements[1]);
                Assert.AreEqual(before + 1, undo.UndoActions.Count(),
                    "the whole move must commit as a single undo entry");
            });
        }

        [Test]
        public void InsertInCopyModeLeavesTheSourceInPlace()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();

                var stackPanel = grid.ContentProperty.CollectionElements[1];
                outline.Insert(new[] { outline.Children[1].Children[0] }, outline.Children[0], true);

                Assert.AreEqual(1, stackPanel.ContentProperty.CollectionElements.Count,
                    "copy mode must not remove the source");
                Assert.AreEqual(3, grid.ContentProperty.CollectionElements.Count);
                Assert.AreEqual(typeof(Button), grid.ContentProperty.CollectionElements[1].ComponentType);
            });
        }

        [Test]
        public void TheControlBuildsOneRowPerNode()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var root = control.Root;
                var stackPanelNode = root.Children[1];
                var nestedButtonNode = stackPanelNode.Children[0];

                foreach (var node in new[] { root, root.Children[0], stackPanelNode, nestedButtonNode }) {
                    var row = control.RowFor(node);
                    Assert.IsNotNull(row, "no row was built for " + node.Name);
                    Assert.AreSame(node, control.NodeFor(row), "row/node mapping does not round-trip");
                }

                window.Content = null;
            });
        }

        [Test]
        public void InvokingARowSelectsTheDesignItem()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var button = grid.ContentProperty.CollectionElements[0];
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[0]);
                control.HandleRowInvoked(control.Root.Children[0], false);

                Assert.IsTrue(surface.DesignContext.Services.Selection.SelectedItems.Contains(button));

                window.Content = null;
            });
        }

        [Test]
        public void SelectingInTheDesignerHighlightsTheRow()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var button = grid.ContentProperty.CollectionElements[0];
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var row = control.RowFor(control.Root.Children[0]);
                Assert.IsNull(((Grid)row).Background, "an unselected row must have no highlight");

                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { button });
                Assert.IsNotNull(((Grid)row).Background, "a selected row must be highlighted");

                surface.DesignContext.Services.Selection.SetSelectedComponents(new DesignItem[0]);
                Assert.IsNull(((Grid)row).Background, "deselecting must clear the highlight");

                window.Content = null;
            });
        }

        [Test]
        public void CollapsingANodeHidesItsChildRows()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var stackPanelNode = control.Root.Children[1];
                var childRow = control.RowFor(stackPanelNode.Children[0]);
                var childHost = (FrameworkElement)((Grid)childRow).Parent;
                Assert.AreEqual(Visibility.Visible, childHost.Visibility);

                // Drive the real expand ToggleButton rather than IsExpanded + Rebuild(), so this
                // test actually exercises OutlineViewControl's Checked/Unchecked wiring instead of
                // reaching the same visible result through a side door.
                var stackPanelRow = (Grid)control.RowFor(stackPanelNode);
                var expandToggle = (ToggleButton)stackPanelRow.Children
                    .Single(c => Grid.GetColumn((FrameworkElement)c) == 0);

                expandToggle.IsChecked = false;
                Assert.AreEqual(Visibility.Collapsed, childHost.Visibility);
                Assert.IsFalse(stackPanelNode.IsExpanded);

                expandToggle.IsChecked = true;
                Assert.AreEqual(Visibility.Visible, childHost.Visibility);
                Assert.IsTrue(stackPanelNode.IsExpanded);

                window.Content = null;
            });
        }

        [Test]
        public void HidingANodeSetsAndResettingItClearsTheIsHiddenProperty()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();
                var buttonNode = outline.Children[0];
                var button = (Button)buttonNode.DesignItem.Component;

                buttonNode.IsDesignTimeVisible = false;
                Assert.IsTrue(buttonNode.DesignItem.Properties
                    .GetAttachedProperty(typeof(WinUIDesigner.XamlDom.DesignTimeProperties), "IsHidden").IsSet);
                Assert.AreEqual(Visibility.Collapsed, button.Visibility);

                buttonNode.IsDesignTimeVisible = true;
                Assert.IsFalse(buttonNode.DesignItem.Properties
                    .GetAttachedProperty(typeof(WinUIDesigner.XamlDom.DesignTimeProperties), "IsHidden").IsSet);
            });
        }

        [Test]
        public void LockingANodeWritesTheIsLockedProperty()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var outline = grid.CreateOutlineNode();
                var buttonNode = outline.Children[0];

                buttonNode.IsDesignTimeLocked = true;
                Assert.IsTrue(((XamlDesignItem)buttonNode.DesignItem).IsDesignTimeLocked);
            });
        }

        [Test]
        public void TheRowCarriesAHideToggleAndALockToggle()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var node = control.Root.Children[0];
                Assert.IsNotNull(ToggleInColumn(control, node, 1), "no hide toggle in column 1");
                Assert.IsNotNull(ToggleInColumn(control, node, 2), "no lock toggle in column 2");

                // Each toggle is re-fetched from the row the control holds RIGHT NOW (see
                // ToggleInColumn): clicking the hide toggle used to rebuild the row tree from
                // inside its own Checked handler, so a lock toggle captured beforehand was an
                // orphan by the time it was clicked - and since ToggleButton raises Checked
                // whether or not it is still in the tree, and the closure still held the live
                // node, the assertion below passed even for a row whose lock toggle was never
                // wired at all.
                ToggleInColumn(control, node, 1).IsChecked = true;
                Assert.IsFalse(node.IsDesignTimeVisible);
                ToggleInColumn(control, node, 2).IsChecked = true;
                Assert.IsTrue(node.IsDesignTimeLocked);

                window.Content = null;
            });
        }

        [Test]
        public void HitTestSkipsALockedItemAndItsChildren()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"200\">" +
                    "<StackPanel Width=\"200\" Height=\"200\"><Button Width=\"200\" Height=\"200\" /></StackPanel>" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                surface.UpdateLayout();
                var grid = surface.DesignContext.RootItem;
                var stackPanel = grid.ContentProperty.CollectionElements[0];
                var button = stackPanel.ContentProperty.CollectionElements[0];

                // DesignSurface stretches its DesignPanel to fill the whole window (measured:
                // 1424x720 on this host - see HitTestTests.cs's identical caveat), and this fixture's
                // explicitly-sized 200x200 root Grid then gets centred within that much bigger
                // panel rather than sitting at the panel's local origin (measured here: centred at
                // panel-local (612,260)). So the hit point is computed from the Button's actual
                // measured panel-local position instead of assuming a fixed (100,100) sits inside it.
                var buttonView = (Microsoft.UI.Xaml.FrameworkElement)button.View;
                var buttonOrigin = buttonView.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(0, 0));
                var point = new Windows.Foundation.Point(buttonOrigin.X + 100, buttonOrigin.Y + 100);
                var before = surface.DesignPanel.HitTest(point, false, true, HitTestType.ElementSelection);
                Assert.AreEqual(button, before.ModelHit, "baseline: the Button is what is under the point");

                ((XamlDesignItem)stackPanel).IsDesignTimeLocked = true;
                var after = surface.DesignPanel.HitTest(point, false, true, HitTestType.ElementSelection);
                Assert.AreNotEqual(button, after.ModelHit,
                    "a child of a locked container must not be hit");
                Assert.AreNotEqual(stackPanel, after.ModelHit, "the locked container itself must not be hit");
                // Final-review fix (F7): the two AreNotEqual assertions above are both satisfied by
                // a null ModelHit, so they would survive a regression that killed hit-testing
                // outright (turning the filter's `continue` into a `break`, or pruning the content
                // pass). Pin the actual expected outcome: the point falls through the locked
                // StackPanel and its Button to the root Grid underneath.
                Assert.AreEqual(grid, after.ModelHit,
                    "the hit must fall through to the root Grid, not resolve to nothing at all");
            });
        }

        [Test]
        public void FilterKeepsMatchingNodesAndTheirAncestorsAndAnEmptyFilterRestoresTheTree()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Button /><StackPanel><Image /></StackPanel>" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var buttonNode = control.Root.Children[0];
                var stackPanelNode = control.Root.Children[1];
                var imageNode = stackPanelNode.Children[0];
                Assert.IsNotNull(control.RowFor(buttonNode));

                control.Filter = "image";
                control.UpdateLayout();
                Assert.IsNull(control.RowFor(buttonNode), "a non-matching leaf must be dropped");
                Assert.IsNotNull(control.RowFor(stackPanelNode), "an ancestor of a match must survive");
                Assert.IsNotNull(control.RowFor(imageNode), "the match itself must survive");
                Assert.IsNotNull(control.RowFor(control.Root), "the root always survives");

                control.Filter = "";
                control.UpdateLayout();
                Assert.IsNotNull(control.RowFor(buttonNode), "an empty filter restores the tree");

                window.Content = null;
            });
        }

        [Test]
        public void DroppingANodeOntoAContainerReparentsTheDesignItem()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var stackPanel = grid.ContentProperty.CollectionElements[1];
                var stackPanelButton = stackPanel.ContentProperty.CollectionElements[0];

                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var dragged = control.Root.Children[1].Children[0];
                var after = control.Root.Children[0];

                bool applied = control.TryApplyDrop(control.Root,
                    new List<IOutlineNode> { dragged }, after, false);

                Assert.IsTrue(applied);
                Assert.AreEqual(3, grid.ContentProperty.CollectionElements.Count);
                Assert.AreEqual(stackPanelButton, grid.ContentProperty.CollectionElements[1]);
                Assert.AreEqual(0, stackPanel.ContentProperty.CollectionElements.Count);

                window.Content = null;
            });
        }

        [Test]
        public void DroppingOntoATargetThatCannotAcceptTheItemDoesNothing()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Image /><Button />" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                var grid = surface.DesignContext.RootItem;
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var imageNode = control.Root.Children[0];
                var buttonNode = control.Root.Children[1];

                bool applied = control.TryApplyDrop(imageNode,
                    new List<IOutlineNode> { buttonNode }, null, false);

                Assert.IsFalse(applied, "an Image has no IPlacementBehavior and must reject the drop");
                Assert.AreEqual(2, grid.ContentProperty.CollectionElements.Count);

                window.Content = null;
            });
        }

        [Test]
        public void DroppingOntoADescendantOfTheDraggedNodeDoesNothing()
        {
            WinUITestHost.RunWithWindow(window => {
                // GridXaml's Grid > StackPanel > Button gives an ancestor/descendant pair without
                // a new fixture. CanInsertReflectsWhetherTheTargetCanAcceptTheItems already pins
                // that CanInsert itself has no cycle guard (Decision 14) and reports true for
                // exactly this shape, so this test is only meaningful if TryApplyDrop vetoes it
                // independently of CanInsert.
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var stackPanel = grid.ContentProperty.CollectionElements[1];

                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var stackPanelNode = control.Root.Children[1];
                var stackPanelButtonNode = stackPanelNode.Children[0];

                bool applied = control.TryApplyDrop(stackPanelButtonNode,
                    new List<IOutlineNode> { stackPanelNode }, null, false);

                Assert.IsFalse(applied,
                    "dropping a node onto its own descendant must be rejected to avoid a cycle");
                Assert.AreEqual(2, grid.ContentProperty.CollectionElements.Count);
                Assert.AreEqual(1, stackPanel.ContentProperty.CollectionElements.Count);

                window.Content = null;
            });
        }

        [Test]
        public void DroppingOntoAContainerThatHasAPropertyNodeInsertsAtTheRightCollectionIndex()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, TransformedStackPanelXaml);
                var grid = surface.DesignContext.RootItem;
                var stackPanel = grid.ContentProperty.CollectionElements[0];
                var dragged = grid.ContentProperty.CollectionElements[1];

                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var stackPanelNode = control.Root.Children[0];
                var draggedNode = control.Root.Children[1];
                // The whole point of this fixture: the two index spaces differ by one.
                Assert.AreEqual(3, stackPanelNode.Children.Count, "RenderTransform node + two Buttons");
                Assert.AreEqual(2, stackPanel.ContentProperty.CollectionElements.Count);

                // Exactly what the drag wiring in BuildRow passes: the target's current last child.
                var after = stackPanelNode.Children[stackPanelNode.Children.Count - 1];
                bool applied = control.TryApplyDrop(stackPanelNode,
                    new List<IOutlineNode> { draggedNode }, after, false);

                Assert.IsTrue(applied);
                Assert.AreEqual(3, stackPanel.ContentProperty.CollectionElements.Count);
                Assert.AreEqual(dragged, stackPanel.ContentProperty.CollectionElements[2],
                    "a drop after the target's last child must append it last");
                Assert.AreEqual(1, grid.ContentProperty.CollectionElements.Count);

                window.Content = null;
            });
        }

        [Test]
        public void InsertingAfterANodeThatIsNotInTheContentCollectionAppends()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, TransformedStackPanelXaml);
                var grid = surface.DesignContext.RootItem;
                var stackPanel = grid.ContentProperty.CollectionElements[0];
                var dragged = grid.ContentProperty.CollectionElements[1];
                var stackPanelNode = grid.CreateOutlineNode().Children[0];

                // A PropertyOutlineNode is in Children but has no DesignItem and is in no content
                // collection at all. Naming it as `after` expresses no position, so the item must
                // land at the end - not silently at the front, which is where IndexOf's -1 used to
                // put it.
                var propertyNode = stackPanelNode.Children[0];
                Assert.IsNull(propertyNode.DesignItem, "expected the leading RenderTransform property node");

                stackPanelNode.Insert(new[] { dragged.CreateOutlineNode() }, propertyNode, false);

                Assert.AreEqual(3, stackPanel.ContentProperty.CollectionElements.Count);
                Assert.AreEqual(dragged, stackPanel.ContentProperty.CollectionElements[2],
                    "an `after` this container does not hold must append, not prepend");
            });
        }

        [Test]
        public void AnIncrementalCollectionInsertLandsAfterTheLeadingPropertyNodes()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, TransformedStackPanelXaml);
                var grid = surface.DesignContext.RootItem;
                var stackPanel = grid.ContentProperty.CollectionElements[0];
                var stackPanelNode = grid.CreateOutlineNode().Children[0];

                Assert.AreEqual(3, stackPanelNode.Children.Count);
                Assert.AreEqual("RenderTransform", stackPanelNode.Children[0].Name);

                var added = surface.DesignContext.Services.Component
                    .RegisterComponentForDesigner(new Button());
                stackPanel.ContentProperty.CollectionElements.Insert(0, added);

                // NewStartingIndex 0 is an index into CollectionElements; Children is offset from
                // it by the leading property nodes.
                Assert.AreEqual(4, stackPanelNode.Children.Count);
                Assert.AreEqual("RenderTransform", stackPanelNode.Children[0].Name,
                    "the property node must stay ahead of the content children");
                Assert.AreEqual(added, stackPanelNode.Children[1].DesignItem,
                    "an insert at content index 0 must land at Children index 0 + property-node count");
            });
        }

        [Test]
        public void TogglingHideDoesNotRebuildTheRowTreeFromInsideTheToggleHandler()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var node = control.Root.Children[0];
                var row = control.RowFor(node);
                var hideToggle = ToggleInColumn(control, node, 1);

                hideToggle.IsChecked = true;

                Assert.IsFalse(node.IsDesignTimeVisible, "the toggle must still drive the model");
                Assert.AreSame(row, control.RowFor(node),
                    "the rebuild must not run synchronously from inside the toggle's own Checked " +
                    "handler - it destroys the very ToggleButton that is on the stack");
                Assert.AreSame(hideToggle, ToggleInColumn(control, node, 1),
                    "and it must not have replaced the toggle either");

                window.Content = null;
            });
        }

        [Test]
        public void TheDeferredRebuildStillRunsOnTheNextDispatcherTurn()
        {
            OutlineViewControl control = null;
            IOutlineNode node = null;
            FrameworkElement rowBefore = null;

            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                node = control.Root.Children[0];
                rowBefore = control.RowFor(node);
                ToggleInColumn(control, node, 1).IsChecked = true;
                Assert.AreSame(rowBefore, control.RowFor(node), "same dispatcher turn: no rebuild yet");
            });

            // Deferring must not mean "never": each WinUITestHost call is its own dispatcher turn,
            // and the rebuild the toggle queued above was enqueued before this one, so it has run
            // by the time this body does. Without that guarantee, coalescing would just be a way
            // of dropping the update.
            WinUITestHost.RunWithWindow(window => {
                Assert.IsNotNull(control.RowFor(node), "the node must still have a row");
                Assert.AreNotSame(rowBefore, control.RowFor(node),
                    "the coalesced rebuild must actually happen, one dispatcher turn later");
            });
        }

        [Test]
        public void ExtendingSelectionFromARowAddsToTheSelectionAndTogglesItOff()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var button = grid.ContentProperty.CollectionElements[0];
                var stackPanel = grid.ContentProperty.CollectionElements[1];
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var selection = surface.DesignContext.Services.Selection;
                selection.SetSelectedComponents(new DesignItem[0]);

                control.HandleRowInvoked(control.Root.Children[0], false);
                control.HandleRowInvoked(control.Root.Children[1], true);

                Assert.IsTrue(selection.SelectedItems.Contains(button),
                    "extending must add to the selection, not replace it");
                Assert.IsTrue(selection.SelectedItems.Contains(stackPanel));

                control.HandleRowInvoked(control.Root.Children[1], true);

                Assert.IsTrue(selection.SelectedItems.Contains(button));
                Assert.IsFalse(selection.SelectedItems.Contains(stackPanel),
                    "extending an already-selected node must toggle it back off");

                window.Content = null;
            });
        }

        [Test]
        public void APressOnARowsToggleIsNotTreatedAsTheStartOfARowDrag()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window, GridXaml);
                var grid = surface.DesignContext.RootItem;
                var control = new OutlineViewControl();
                window.Content = control;
                control.Root = grid.CreateOutlineNode();
                control.UpdateLayout();

                var node = control.Root.Children[1];   // the StackPanel: it has all three toggles
                var row = (Grid)control.RowFor(node);

                // The predicate the drag wiring applies to the press's OriginalSource. The gesture
                // itself cannot be synthesised in this test host (PointerRoutedEventArgs has no
                // public constructor), so the predicate is what is testable - driven here with the
                // row's real, laid-out controls rather than stand-ins.
                foreach (var column in new[] { 0, 1, 2 }) {
                    var toggle = ToggleInColumn(control, node, column);
                    Assert.IsNotNull(toggle, "expected a toggle in column " + column);
                    Assert.IsTrue(OutlineViewControl.IsInsideToggleButton(toggle, row),
                        "a press on the toggle in column " + column + " must not start a row drag");
                    // A real press reports something inside the ToggleButton's template as its
                    // OriginalSource, not the ToggleButton itself, so the walk has to climb out of
                    // the template first.
                    var inside = DeepestFirstChild(toggle);
                    Assert.AreNotSame(toggle, inside, "expected a realized template under the toggle in column " + column);
                    Assert.IsTrue(OutlineViewControl.IsInsideToggleButton(inside, row),
                        "a press inside the toggle in column " + column + " must not start a row drag either");
                }

                var nameText = row.Children.OfType<TextBlock>().Single();
                Assert.IsFalse(OutlineViewControl.IsInsideToggleButton(nameText, row),
                    "a press on the row's name must still start a drag");
                Assert.IsFalse(OutlineViewControl.IsInsideToggleButton(row, row),
                    "a press on the row's own background must still start a drag");
                Assert.IsFalse(OutlineViewControl.IsInsideToggleButton(null, row),
                    "a source that is not a DependencyObject must not veto the drag");

                window.Content = null;
            });
        }

        [Test]
        public void HitTestingDoesNotWriteAnIsLockedPropertyIntoTheModel()
        {
            WinUITestHost.RunWithWindow(window => {
                const string xaml =
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"200\">" +
                    "<StackPanel Width=\"200\" Height=\"200\"><Button Width=\"200\" Height=\"200\" /></StackPanel>" +
                    "</Grid>";
                var surface = LoadSurface(window, xaml);
                surface.UpdateLayout();
                var grid = surface.DesignContext.RootItem;
                var stackPanel = grid.ContentProperty.CollectionElements[0];
                var button = stackPanel.ContentProperty.CollectionElements[0];

                Assert.IsFalse(HasIsLockedProperty(grid) || HasIsLockedProperty(stackPanel) || HasIsLockedProperty(button),
                    "baseline: nothing has touched IsLocked yet");

                var buttonView = (Microsoft.UI.Xaml.FrameworkElement)button.View;
                var buttonOrigin = buttonView.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(0, 0));
                var point = new Windows.Foundation.Point(buttonOrigin.X + 100, buttonOrigin.Y + 100);
                var hit = surface.DesignPanel.HitTest(point, false, true, HitTestType.ElementSelection);
                Assert.AreEqual(button, hit.ModelHit, "baseline: the Button is what is under the point");

                // HitTest runs on every pointer move. Reading the lock flag through the model
                // (XamlModelPropertyCollection.GetAttachedProperty -> XamlObject.
                // FindOrCreateAttachedProperty) ADDS a XamlProperty on a miss, so a mere read
                // mutates the document - and AllSetProperties, which the property grid renders with
                // no IsSet filter, would then show a phantom IsLocked row for every element the
                // pointer has passed over.
                Assert.IsFalse(HasIsLockedProperty(button), "hit-testing must not add IsLocked to the hit item");
                Assert.IsFalse(HasIsLockedProperty(stackPanel), "...nor to an ancestor of it");
                Assert.IsFalse(HasIsLockedProperty(grid), "...nor to the root");
            });
        }
    }
}
