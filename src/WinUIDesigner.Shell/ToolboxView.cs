using System;
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
    /// keeps the same control placeable twice in a row. Spec Decision 12.
    /// </para>
    /// </remarks>
    public sealed class ToolboxView : UserControl
    {
        readonly ListView list = new ListView();
        IToolService toolService;
        bool suppressSelectionChanged;

        public ToolboxView()
        {
            list.SelectionMode = ListViewSelectionMode.Single;
            list.ItemsSource = Toolbox.Instance.AssemblyNodes
                .SelectMany(n => n.Controls)
                .Select(c => c.Type)
                .ToList();
            list.ItemTemplate = null;
            list.DisplayMemberPath = "Name";
            list.SelectionChanged += OnSelectionChanged;

            Content = new ScrollViewer
            {
                Content = list,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        public void Attach(IToolService service)
        {
            Detach();
            toolService = service;
            if (toolService != null)
                toolService.CurrentToolChanged += OnCurrentToolChanged;
        }

        public void Detach()
        {
            if (toolService != null)
                toolService.CurrentToolChanged -= OnCurrentToolChanged;
            toolService = null;
        }

        /// <summary>Selects a control type as a user click would. Test seam.</summary>
        internal void SelectForTest(Type type)
        {
            list.SelectedItem = type;
            // ListView.SelectedItem does not raise SelectionChanged without a live item
            // container, so drive the same handler the UI would.
            Arm(type);
        }

        void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressSelectionChanged)
                return;
            Arm(list.SelectedItem as Type);
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
