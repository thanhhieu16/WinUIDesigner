using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.PropertyGrid;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.PropertyGrid;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PropertyGridControlTests
    {
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
            "<Button x:Name=\"A\" Grid.Row=\"1\" Width=\"40\" Content=\"a\" />" +
            "<Button x:Name=\"B\" Width=\"55\" Content=\"b\" />" +
            "</Grid>";

        static (DesignItem gridItem, DesignItem a, DesignItem b) Load(Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(GridXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            var grid = (Grid)surface.DesignContext.RootItem.Component;
            var a = surface.DesignContext.Services.Component.GetDesignItem(grid.Children[0]);
            var b = surface.DesignContext.Services.Component.GetDesignItem(grid.Children[1]);
            return (surface.DesignContext.RootItem, a, b);
        }

        static void ResetMetadataPopularProperties()
        {
            // Metadata.popularProperties is `static readonly` - SetValue-ing the field itself
            // throws FieldAccessException once the type is initialized (same class of incident
            // as this project's two prior readonly-removal reverts). The established correct fix
            // is to mutate the existing collection in place instead of replacing the field's
            // reference: fetch it and Clear() it through its generic ICollection<string> surface
            // (HashSet<string> has no non-generic ICollection to cast to, unlike IDictionary in
            // the Task 2 precedent, but the effect - clear in place, readonly untouched - is the
            // same).
            var field = typeof(Metadata).GetField("popularProperties", BindingFlags.NonPublic | BindingFlags.Static);
            var collection = (System.Collections.Generic.ICollection<string>)field.GetValue(null);
            collection.Clear();
        }

        [Test]
        public void SelectingAnItemBucketsItsPropertiesIntoPopularOtherAndAttachedCategories()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, _) = Load(window);
                // Metadata.AddPopularProperty(Type,string) keys by "<type>.<member>" and
                // Metadata.IsPopularProperty compares against DesignItemProperty.FullName, which is
                // "<DeclaringType>.<Name>" using the property's ACTUAL reflected declaring type
                // (PropertyDescriptor.ComponentType via XamlNormalPropertyInfo.TargetType), not the
                // concrete component's runtime type. Content is declared on ContentControl and
                // inherited by Button, so it must be registered against ContentControl for the
                // FullName match to succeed (confirmed empirically: a live Button's Content property
                // reports DeclaringType.FullName == "Microsoft.UI.Xaml.Controls.ContentControl").
                Metadata.AddPopularProperty(typeof(Microsoft.UI.Xaml.Controls.ContentControl), "Content");
                try {
                    var grid = new PropertyGridControl();
                    grid.SelectedItems = new[] { a };

                    var popular = grid.Categories.First(c => c.Name == "Popular");
                    var other = grid.Categories.First(c => c.Name == "Other");
                    var attached = grid.Categories.First(c => c.Name == "Attached");

                    Assert.IsTrue(popular.Properties.Any(p => p.Name == "Content"));
                    Assert.IsTrue(other.Properties.Any(p => p.Name == "Width"));
                    Assert.IsTrue(attached.Properties.Any(p => p.Name == "Grid.Row"));
                } finally {
                    ResetMetadataPopularProperties();
                }
            });
        }

        [Test]
        public void EditingThroughTheTextBoxEditorRoundTripsThroughSetValueAndUndo()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, _) = Load(window);
                var grid = new PropertyGridControl();
                grid.SelectedItems = new[] { a };

                var node = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Width");
                var textBox = (TextBox)node.Editor;
                textBox.Text = "123";
                RaiseLostFocus(textBox);

                Assert.AreEqual(123.0, (double)((Button)a.Component).Width);
                Assert.IsTrue(a.Services.GetService<UndoService>().CanUndo);
            });
        }

        [Test]
        public void MultiSelectShowsAmbiguousForACommonPropertyWithDifferingValues()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, b) = Load(window);
                var grid = new PropertyGridControl();
                grid.SelectedItems = new[] { a, b };

                var node = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Width");
                Assert.IsTrue(node.IsAmbiguous, "A.Width=40, B.Width=55 - genuinely different");
            });
        }

        [Test]
        public void ReselectingTheSameItemReusesTheCachedPropertyNodeAndEditor()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, b) = Load(window);
                var grid = new PropertyGridControl();

                grid.SelectedItems = new[] { a };
                var firstNode = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Width");
                var firstEditor = firstNode.Editor;

                grid.SelectedItems = new[] { b };
                grid.SelectedItems = new[] { a };
                var secondNode = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Width");

                Assert.AreSame(firstNode, secondNode);
                Assert.AreSame(firstEditor, secondNode.Editor);
            });
        }

        static void RaiseLostFocus(TextBox textBox)
        {
            // A real LostFocus RoutedEventArgs can't be fabricated (no public constructor -
            // Global Constraints' established pattern for routed events in this codebase) -
            // TextBoxEditor exposes CommitForTest as the same "Handle*-extracted, internal test
            // seam" pattern used throughout this codebase (e.g. DesignPanel.HandlePointerPressed).
            WinUIDesigner.Designer.PropertyGrid.Editors.TextBoxEditor.CommitForTest(textBox);
        }
    }
}
