using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class SelectionExtensionServersTests
    {
        sealed class FakeDesignPanel : WinUIDesigner.DesignModel.IDesignPanel
        {
            public DesignContext Context { get; set; }
            public bool IsContentHitTestVisible { get; set; }
            public bool IsAdornerLayerHitTestVisible { get; set; }
            public ICollection<WinUIDesigner.DesignModel.Adorners.AdornerPanel> Adorners { get; } = new List<WinUIDesigner.DesignModel.Adorners.AdornerPanel>();
            public WinUIDesigner.DesignModel.DesignPanelHitTestResult HitTest(Windows.Foundation.Point p, bool a, bool b, WinUIDesigner.DesignModel.HitTestType t) { return DesignPanelHitTestResult.NoHit; }
            public void HitTest(Windows.Foundation.Point p, bool a, bool b, Predicate<WinUIDesigner.DesignModel.DesignPanelHitTestResult> c, WinUIDesigner.DesignModel.HitTestType t) { }
        }

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
                if (SelectionChanging != null) SelectionChanging(this, EventArgs.Empty);
                _selected = components != null ? new List<DesignItem>(components) : new List<DesignItem>();
                var newPrimary = _selected.Count > 0 ? _selected[0] : null;
                if (newPrimary != PrimarySelection) {
                    if (PrimarySelectionChanging != null) PrimarySelectionChanging(this, EventArgs.Empty);
                    PrimarySelection = newPrimary;
                    if (PrimarySelectionChanged != null) PrimarySelectionChanged(this, EventArgs.Empty);
                }
                if (SelectionChanged != null) SelectionChanged(this, new DesignItemCollectionEventArgs(_selected));
            }
        }

        [Test]
        public void PrimarySelectionParentExtensionServerAppliesOnlyToTheSelectedItemsParent()
        {
            WinUITestHost.Run(() => {
                const string xaml = "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Canvas x:Name=\"Inner\"><Button x:Name=\"Target\" /></Canvas></Grid>";

                var loadSettings = new XamlLoadSettings();
                var fakePanel = new FakeDesignPanel();
                var fakeSelection = new FakeSelectionService();
                loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), fakePanel));
                loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(ISelectionService), fakeSelection));

                using (var reader = XmlReader.Create(new StringReader(xaml))) {
                    var context = new XamlDesignContext(reader, loadSettings);
                    fakePanel.Context = context;

                    var root = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                    var canvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
                    var button = (Microsoft.UI.Xaml.Controls.Button)canvas.Children[0];
                    var services = context.Services;
                    var buttonItem = services.Component.GetDesignItem(button);
                    var canvasItem = services.Component.GetDesignItem(canvas);
                    var rootItem = context.RootItem;

                    var server = services.ExtensionManager.GetExtensionServer(
                        new ExtensionServerAttribute(typeof(PrimarySelectionParentExtensionServer)));

                    Assert.IsFalse(server.ShouldApplyExtensions(canvasItem), "no selection yet - nothing should apply");

                    fakeSelection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);

                    Assert.IsTrue(server.ShouldApplyExtensions(canvasItem), "Canvas is the selected button's parent");
                    Assert.IsFalse(server.ShouldApplyExtensions(rootItem), "the root Grid is not the immediate parent");
                    Assert.IsFalse(server.ShouldApplyExtensions(buttonItem), "the button itself is not its own parent");
                }
            });
        }

        [Test]
        public void PrimarySelectionButOnlyWhenMultipleSelectedAppliesOnlyWithTwoOrMoreSelected()
        {
            WinUITestHost.Run(() => {
                const string xaml = "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<Button x:Name=\"A\" /><Button x:Name=\"B\" /></Grid>";

                var loadSettings = new XamlLoadSettings();
                var fakePanel = new FakeDesignPanel();
                var fakeSelection = new FakeSelectionService();
                loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), fakePanel));
                loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(ISelectionService), fakeSelection));

                using (var reader = XmlReader.Create(new StringReader(xaml))) {
                    var context = new XamlDesignContext(reader, loadSettings);
                    fakePanel.Context = context;

                    var root = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                    var a = (Microsoft.UI.Xaml.Controls.Button)root.Children[0];
                    var b = (Microsoft.UI.Xaml.Controls.Button)root.Children[1];
                    var services = context.Services;
                    var aItem = services.Component.GetDesignItem(a);
                    var bItem = services.Component.GetDesignItem(b);

                    var server = services.ExtensionManager.GetExtensionServer(
                        new ExtensionServerAttribute(typeof(PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer)));

                    fakeSelection.SetSelectedComponents(new[] { aItem }, SelectionTypes.Primary);
                    Assert.IsFalse(server.ShouldApplyExtensions(aItem), "only one item selected - should not apply");

                    fakeSelection.SetSelectedComponents(new[] { aItem, bItem }, SelectionTypes.Replace);
                    Assert.IsTrue(server.ShouldApplyExtensions(aItem), "primary selection with 2 selected - should apply");
                    Assert.IsFalse(server.ShouldApplyExtensions(bItem), "not the primary selection - should not apply");
                }
            });
        }
    }
}
