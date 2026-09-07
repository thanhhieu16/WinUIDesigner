using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.Designer.Services;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The list of controls a user can place, and the arming of the tool that places them.
    /// </summary>
    /// <remarks>
    /// Written in C# rather than XAML: App.xaml is the only XAML file in this project. Ported in
    /// spirit from the reference's ToolboxView.xaml.
    /// <para>
    /// CreateComponentTool is single-use - the engine resets IToolService.CurrentTool to
    /// PointerTool once a placement finishes. A ListView whose SelectionChanged arms the tool
    /// would therefore be stuck after one placement, because the placed entry stays selected and
    /// re-clicking it raises no event. Clearing the selection when the engine resets is what
    /// keeps the same control placeable twice in a row. Phase 5 Decision 12.
    /// </para>
    /// </remarks>
    public sealed class ToolboxView : UserControl
    {
        readonly ListView list = new ListView();
        readonly List<ControlNode> nodes;
        IToolService toolService;
        bool suppressSelectionChanged;

        public ToolboxView()
        {
            // ItemsSource holds the ControlNodes themselves, not their Type, because typeof(X)
            // is an instance of System.RuntimeType (non-public) rather than System.Type, and
            // WinUI's binding engine reflects against the object's actual runtime type - a
            // DisplayMemberPath of "Name" against a RuntimeType instance never resolves, so
            // every row rendered as an empty band. ControlNode (Task 2) is public and has a
            // public Name, so the same DisplayMemberPath now works. Fix per code review.
            nodes = Toolbox.Instance.AssemblyNodes
                .SelectMany(n => n.Controls)
                .ToList();

            list.SelectionMode = ListViewSelectionMode.Single;
            list.ItemsSource = nodes;
            list.DisplayMemberPath = "Name";
            list.SelectionChanged += OnSelectionChanged;

            // A ListView already contains its own ScrollViewer. Wrapping it in another one
            // hands the inner list an infinite available height, so its virtualizing panel
            // realizes every container against an unbounded viewport instead of the real one -
            // measured at 6360px for 159 items inside a 680px-tall host. Fix per code review.
            Content = list;
        }

        public void Attach(IToolService service)
        {
            Detach();
            toolService = service;
            if (toolService != null)
                toolService.CurrentToolChanged += OnCurrentToolChanged;
        }

        /// <summary>
        /// Stops driving, and being driven by, the tool service handed to <see cref="Attach"/>.
        /// </summary>
        /// <remarks>
        /// Clearing the selection is part of the contract, not tidying: an entry armed but never
        /// placed stays selected, and the incoming document's tool service starts at its
        /// PointerTool. Leaving the stale selection behind means clicking that same entry again
        /// raises no SelectionChanged, so nothing re-arms and the toolbox is dead until the user
        /// picks a different control - Phase 5 Decision 12's regression, reintroduced across the document
        /// boundary. Cleared under the same suppression guard OnCurrentToolChanged uses, so the
        /// clearing itself cannot arm a tool on the outgoing service.
        /// </remarks>
        public void Detach()
        {
            if (toolService != null)
                toolService.CurrentToolChanged -= OnCurrentToolChanged;
            toolService = null;

            suppressSelectionChanged = true;
            try
            {
                list.SelectedItem = null;
            }
            finally
            {
                suppressSelectionChanged = false;
            }
        }

        /// <summary>The type the list currently has selected, or null. Test seam.</summary>
        /// <remarks>
        /// Exists so a test can assert the Phase 5 Decision 12 reset actually cleared the selection,
        /// rather than only that a second tool got armed - which arming unconditionally would
        /// guarantee by construction whether or not the reset works.
        /// </remarks>
        internal Type SelectedTypeForTest
        {
            get { return (list.SelectedItem as ControlNode)?.Type; }
        }

        /// <summary>Selects a control type as a user click would. Test seam.</summary>
        /// <remarks>
        /// Faithful to a click in the one way that matters here: clicking an entry that is
        /// already selected changes nothing and raises no SelectionChanged, so nothing re-arms.
        /// That is exactly the Phase 5 Decision 12 regression, and it is only avoided because
        /// <see cref="OnCurrentToolChanged"/> clears the selection when the engine resets.
        /// </remarks>
        internal void SelectForTest(Type type)
        {
            var node = nodes.FirstOrDefault(n => n.Type == type);
            if (ReferenceEquals(list.SelectedItem, node))
                return;

            list.SelectedItem = node;
            // A ListView with no realized item containers does not reliably raise
            // SelectionChanged from a programmatic SelectedItem assignment, so drive the same
            // handler the UI would. If the real event does fire as well, the handler simply
            // arms an equivalent second tool, which nothing depends on.
            OnSelectionChanged(list, null);
        }

        void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressSelectionChanged)
                return;
            Arm((list.SelectedItem as ControlNode)?.Type);
        }

        void Arm(Type type)
        {
            if (type == null || toolService == null)
                return;
            toolService.CurrentTool = new CreateComponentTool(type);
        }

        void OnCurrentToolChanged(object sender, EventArgs e)
        {
            if (toolService == null)
                return;
            if (!ReferenceEquals(toolService.CurrentTool, toolService.PointerTool))
                return;

            suppressSelectionChanged = true;
            try
            {
                list.SelectedItem = null;
            }
            finally
            {
                suppressSelectionChanged = false;
            }
        }
    }
}
