using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel.PropertyGrid;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class PropertyNodeTests
    {
        const string TwoButtonsXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"A\" Width=\"40\" Content=\"hi\" />" +
            "<Button x:Name=\"B\" Width=\"40\" />" +
            "</Grid>";

        const string ComplexPropertyXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\">" +
            "<Button.RenderTransform><RotateTransform Angle=\"10\" /></Button.RenderTransform>" +
            "</Button>" +
            "</Grid>";

        static (DesignItem a, DesignItem b) LoadTwoButtons()
        {
            using var reader = XmlReader.Create(new StringReader(TwoButtonsXaml));
            var context = new XamlDesignContext(reader, new XamlLoadSettings());
            var grid = (Grid)context.RootItem.Component;
            var a = context.Services.Component.GetDesignItem(grid.Children[0]);
            var b = context.Services.Component.GetDesignItem(grid.Children[1]);
            return (a, b);
        }

        static DesignItem LoadComplexPropertyButton()
        {
            using var reader = XmlReader.Create(new StringReader(ComplexPropertyXaml));
            var context = new XamlDesignContext(reader, new XamlLoadSettings());
            var grid = (Grid)context.RootItem.Component;
            return context.Services.Component.GetDesignItem(grid.Children[0]);
        }

        [SetUp]
        public void RegisterFallbackEditor()
        {
            // PropertyNode.Load() calls EditorManager.CreateEditor - these tests don't exercise
            // editor SELECTION logic (that's EditorManagerTests' job), they just need it to not
            // throw. Re-registering every test is idempotent and avoids depending on NUnit's
            // (unspecified) run order relative to EditorManagerTests' own [TearDown] resets.
            EditorManager.SetDefaultTextBoxEditorFactory(p => new TextBox());
            EditorManager.SetDefaultComboBoxEditorFactory(p => new ComboBox());
        }

        [Test]
        public void LoadResolvesNameFromTheFirstPropertyForAnOrdinaryProperty()
        {
            WinUITestHost.Run(() => {
                var (a, _) = LoadTwoButtons();
                var node = new PropertyNode(new[] { a.Properties.GetProperty("Width") }, null);
                Assert.AreEqual("Width", node.Name);
            });
        }

        [Test]
        public void NameIsQualifiedForAnAttachedProperty()
        {
            WinUITestHost.Run(() => {
                var (a, _) = LoadTwoButtons();
                var rowProperty = a.Properties.GetAttachedProperty(typeof(Grid), "Row");
                var node = new PropertyNode(new[] { rowProperty }, null);
                Assert.AreEqual("Grid.Row", node.Name);
            });
        }

        [Test]
        public void SetValueCoreAppliesToEveryWrappedPropertyAndRoundTripsThroughUndo()
        {
            WinUITestHost.Run(() => {
                var (a, b) = LoadTwoButtons();
                var node = new PropertyNode(new[] { a.Properties.GetProperty("Width"), b.Properties.GetProperty("Width") }, null);

                node.DesignerValue = 99.0;

                Assert.AreEqual(99.0, (double)((Button)a.Component).Width);
                Assert.AreEqual(99.0, (double)((Button)b.Component).Width);
                Assert.IsTrue(a.Services.GetService<UndoService>().CanUndo);
            });
        }

        [Test]
        public void IsAmbiguousIsTrueWhenWrappedPropertiesDifferAndFalseWhenTheyAgree()
        {
            WinUITestHost.Run(() => {
                var (a, b) = LoadTwoButtons();
                var node = new PropertyNode(new[] { a.Properties.GetProperty("Content"), b.Properties.GetProperty("Content") }, null);
                // a.Content == "hi", b.Content == null (unset) - genuinely different.
                Assert.IsTrue(node.IsAmbiguous);

                node.DesignerValue = "same";
                Assert.IsFalse(node.IsAmbiguous);
            });
        }

        [Test]
        public void ResetClearsIsSetForEveryWrappedProperty()
        {
            WinUITestHost.Run(() => {
                var (a, _) = LoadTwoButtons();
                var widthProperty = a.Properties.GetProperty("Width");
                Assert.IsTrue(widthProperty.IsSet, "fixture sets Width=40 explicitly");

                var node = new PropertyNode(new[] { widthProperty }, null);
                node.Reset();

                Assert.IsFalse(widthProperty.IsSet);
            });
        }

        [Test]
        public void ChildrenDrillDownExposesTheComplexSubObjectsOwnBrowsableProperties()
        {
            WinUITestHost.Run(() => {
                var target = LoadComplexPropertyButton();
                var transformProperty = target.Properties.GetProperty("RenderTransform");
                var node = new PropertyNode(new[] { transformProperty }, null);

                // A root-level node (Parent == null) builds its own Children/MoreChildren immediately
                // on Load() - the ported UpdateChildren gate (Parent == null || Parent.IsExpanded) is
                // unconditionally true for a root node. Laziness only applies to a node's GRANDCHILDREN,
                // gated behind this node's own IsExpanded (exercised later, via Task 4's
                // PropertyGridControl and its full recursive nesting - not this task's concern).
                Assert.IsTrue(node.HasChildren);

                var all = node.Children.Concat(node.MoreChildren);
                Assert.IsTrue(all.Any(c => c.Name == "Angle"), "RotateTransform.Angle should surface as a child node");
            });
        }
    }
}
