using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
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
    }
}
