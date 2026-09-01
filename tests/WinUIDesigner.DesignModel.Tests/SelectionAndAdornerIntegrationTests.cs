using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class SelectionAndAdornerIntegrationTests
    {
        sealed class FakeSelectionService : ISelectionService
        {
            public event EventHandler SelectionChanging;
            public event EventHandler<DesignItemCollectionEventArgs> SelectionChanged;
            public event EventHandler PrimarySelectionChanging;
            public event EventHandler PrimarySelectionChanged;

            List<DesignItem> _selected = new List<DesignItem>();
            public DesignItem PrimarySelection { get; private set; }
            public ICollection<DesignItem> SelectedItems { get { return _selected; } }
            public int SelectionCount { get { return _selected.Count; } }
            public bool IsComponentSelected(DesignItem component) { return _selected.Contains(component); }
            public void SetSelectedComponents(ICollection<DesignItem> components) { SetSelectedComponents(components, SelectionTypes.Replace); }

            public void SetSelectedComponents(ICollection<DesignItem> components, SelectionTypes selectionType)
            {
                // Mirrors DefaultSelectionService's real firing order: SelectionChanging fires
                // unconditionally before any state change, PrimarySelectionChanging only when the
                // primary is actually about to change, right before it does.
                if (SelectionChanging != null) SelectionChanging(this, EventArgs.Empty);
                _selected = components != null ? components.ToList() : new List<DesignItem>();
                var newPrimary = _selected.FirstOrDefault();
                if (newPrimary != PrimarySelection) {
                    if (PrimarySelectionChanging != null) PrimarySelectionChanging(this, EventArgs.Empty);
                    PrimarySelection = newPrimary;
                    if (PrimarySelectionChanged != null) PrimarySelectionChanged(this, EventArgs.Empty);
                }
                if (SelectionChanged != null) SelectionChanged(this, new DesignItemCollectionEventArgs(_selected));
            }
        }

        [ExtensionFor(typeof(Microsoft.UI.Xaml.UIElement))]
        [ExtensionServer(typeof(PrimarySelectionExtensionServer))]
        public sealed class TestSelectionAdorner : AdornerProvider
        {
            protected override void OnInitialized()
            {
                base.OnInitialized();
                AddAdorner(AdornerPlacement.FillContent, AdornerOrder.Foreground, new Microsoft.UI.Xaml.Shapes.Rectangle());
            }
        }

        sealed class FakeDesignPanel : WinUIDesigner.DesignModel.IDesignPanel
        {
            public DesignContext Context { get; set; }
            public bool IsContentHitTestVisible { get; set; }
            public bool IsAdornerLayerHitTestVisible { get; set; }
            public ICollection<AdornerPanel> Adorners { get; } = new List<AdornerPanel>();
            public DesignPanelHitTestResult HitTest(Windows.Foundation.Point p, bool a, bool b, HitTestType t) { return DesignPanelHitTestResult.NoHit; }
            public void HitTest(Windows.Foundation.Point p, bool a, bool b, Predicate<DesignPanelHitTestResult> c, HitTestType t) { }
        }

        [Test]
        public void SelectingAnItemAppliesTheAdornerAndDeselectingRemovesIt()
        {
            WinUITestHost.Run(() => {
                const string xaml = "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Button x:Name=\"A\" />" +
                    "<Button x:Name=\"B\" />" +
                    "</Grid>";

                var loadSettings = new XamlLoadSettings();
                loadSettings.DesignerAssemblies.Add(typeof(SelectionAndAdornerIntegrationTests).Assembly);
                var fakePanel = new FakeDesignPanel();
                var fakeSelection = new FakeSelectionService();
                loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), fakePanel));
                loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(ISelectionService), fakeSelection));

                using (var reader = XmlReader.Create(new StringReader(xaml)))
                {
                    var context = new XamlDesignContext(reader, loadSettings);
                    fakePanel.Context = context;

                    var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                    var buttonA = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                    var buttonB = (Microsoft.UI.Xaml.Controls.Button)grid.Children[1];
                    var itemA = context.Services.Component.GetDesignItem(buttonA);
                    var itemB = context.Services.Component.GetDesignItem(buttonB);

                    Assert.AreEqual(0, fakePanel.Adorners.Count, "nothing selected yet");

                    fakeSelection.SetSelectedComponents(new[] { itemA }, SelectionTypes.Primary);
                    Assert.AreEqual(1, fakePanel.Adorners.Count, "selecting A should apply one adorner");
                    Assert.AreSame(buttonA, fakePanel.Adorners.First().AdornedElement);

                    fakeSelection.SetSelectedComponents(new[] { itemB }, SelectionTypes.Primary);
                    Assert.AreEqual(1, fakePanel.Adorners.Count, "selecting B should remove A's adorner and apply one for B");
                    Assert.AreSame(buttonB, fakePanel.Adorners.First().AdornedElement);

                    fakeSelection.SetSelectedComponents(null, SelectionTypes.Replace);
                    Assert.AreEqual(0, fakePanel.Adorners.Count, "clearing the selection should remove the adorner");
                }
            });
        }
    }
}
