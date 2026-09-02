using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class PlacementOperationDeleteTests
    {
        const string TwoButtonsInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" Canvas.Left=\"0\" Canvas.Top=\"0\" />" +
            "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" Canvas.Left=\"40\" Canvas.Top=\"0\" />" +
            "</Canvas>";

        // Minimal real IPlacementBehavior, standing in for the Designer-layer
        // DefaultPlacementBehavior this test project cannot reference. Resizes/repositions are
        // no-ops (not exercised by these tests) - only the container-membership machinery
        // (CanLeaveContainer/LeaveContainer/CanEnterContainer/EnterContainer) needs to be real.
        [ExtensionFor(typeof(object))]
        public sealed class FakePlacementBehavior : BehaviorExtension, IPlacementBehavior
        {
            protected override void OnInitialized()
            {
                base.OnInitialized();
                ExtendedItem.AddBehavior(typeof(IPlacementBehavior), this);
            }

            public bool CanPlace(IEnumerable<DesignItem> childItems, PlacementType type, PlacementAlignment position) => true;
            public void BeginPlacement(PlacementOperation operation) { }
            public void EndPlacement(PlacementOperation operation) { }
            public Windows.Foundation.Rect GetPosition(PlacementOperation operation, DesignItem child) => new Windows.Foundation.Rect(0, 0, 20, 20);
            public void BeforeSetPosition(PlacementOperation operation, Windows.System.VirtualKeyModifiers modifiers) { }
            public void SetPosition(PlacementInformation info) { }
            public bool CanLeaveContainer(PlacementOperation operation) => true;
            public void LeaveContainer(PlacementOperation operation)
            {
                foreach (var info in operation.PlacedItems)
                    info.Item.ParentProperty.CollectionElements.Remove(info.Item);
            }
            public bool CanEnterContainer(PlacementOperation operation, bool shouldAlwaysEnter) => true;
            public void EnterContainer(PlacementOperation operation)
            {
                foreach (var info in operation.PlacedItems)
                    operation.CurrentContainer.ContentProperty.CollectionElements.Add(info.Item);
            }
            public Windows.Foundation.Point PlacePoint(Windows.Foundation.Point point, Windows.System.VirtualKeyModifiers modifiers) => point;
        }

        // ExtensionManager.RegisterAssembly scans every [ExtensionFor] type in the whole
        // registered assembly, not just this file's - registering this test project's own
        // assembly (needed so FakePlacementBehavior below gets picked up) also pulls in
        // SelectionAndAdornerIntegrationTests.TestSelectionAdorner, whose
        // [ExtensionServer(typeof(PrimarySelectionExtensionServer))] requires ISelectionService
        // to initialize. Nothing in these tests exercises selection, so a no-op stub is enough
        // to satisfy that requirement.
        sealed class NoOpSelectionService : ISelectionService
        {
            public event EventHandler SelectionChanging { add { } remove { } }
            public event EventHandler<DesignItemCollectionEventArgs> SelectionChanged { add { } remove { } }
            public event EventHandler PrimarySelectionChanging { add { } remove { } }
            public event EventHandler PrimarySelectionChanged { add { } remove { } }
            public bool IsComponentSelected(DesignItem component) => false;
            public ICollection<DesignItem> SelectedItems { get; } = new List<DesignItem>();
            public void SetSelectedComponents(ICollection<DesignItem> components) { }
            public void SetSelectedComponents(ICollection<DesignItem> components, SelectionTypes selectionType) { }
            public DesignItem PrimarySelection => null;
            public int SelectionCount => 0;
        }

        static XamlLoadSettings Settings()
        {
            var settings = new XamlLoadSettings { DesignerAssemblies = { typeof(PlacementOperationDeleteTests).Assembly } };
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(ISelectionService), new NoOpSelectionService()));
            return settings;
        }

        [Test]
        public void DeleteItemsAndCommitRemovesItemFromParentCollection()
        {
            WinUITestHost.Run(() => {
                using (var reader = XmlReader.Create(new StringReader(TwoButtonsInCanvasXaml))) {
                    var context = new XamlDesignContext(reader, Settings());
                    var canvas = (Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component;
                    var aItem = context.Services.Component.GetDesignItem(canvas.Children[0]);

                    var operation = PlacementOperation.Start(new[] { aItem }, PlacementType.Delete);
                    operation.DeleteItemsAndCommit();

                    Assert.AreEqual(1, canvas.Children.Count);
                    Assert.IsFalse(context.RootItem.ContentProperty.CollectionElements.Contains(aItem));
                }
            });
        }

        [Test]
        public void TryStartInsertNewComponentsAddsItemToContainerAtGivenPosition()
        {
            WinUITestHost.Run(() => {
                const string emptyCanvasXaml =
                    "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />";
                using (var reader = XmlReader.Create(new StringReader(emptyCanvasXaml))) {
                    var context = new XamlDesignContext(reader, Settings());
                    var canvasItem = context.RootItem;
                    var newButton = new Microsoft.UI.Xaml.Controls.Button { Width = 20, Height = 20 };
                    var newButtonItem = context.Services.Component.RegisterComponentForDesigner(newButton);

                    var op = PlacementOperation.TryStartInsertNewComponents(
                        canvasItem,
                        new[] { newButtonItem },
                        new[] { new Windows.Foundation.Rect(5, 5, 20, 20) },
                        PlacementType.AddItem);

                    Assert.IsNotNull(op);
                    op.Commit();

                    var canvas = (Microsoft.UI.Xaml.Controls.Canvas)canvasItem.Component;
                    Assert.AreEqual(1, canvas.Children.Count);
                    Assert.AreSame(newButton, canvas.Children[0]);
                }
            });
        }
    }
}
