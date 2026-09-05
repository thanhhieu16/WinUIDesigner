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

        // Was keyed on Button's "Width" (a double) - proved the TextBox *fallback* path back when
        // no numeric editor existed yet. Task 5's NumberEditor now legitimately claims every
        // numeric CLR type (spec Decision - no carve-outs), so "Width" resolves to a NumberBox
        // instead, and this test needs a property whose REFLECTED type is genuinely
        // System.String to keep exercising the TextBox path: DesignItemProperty.TypeConverter is
        // TypeDescriptor.GetConverter(ReturnType) (DesignItemProperty.cs:34-36) - for
        // System.String that's System.ComponentModel.StringConverter, which overrides
        // CanConvertFrom to explicitly return true for sourceType==typeof(string) (and the base
        // TypeConverter.CanConvertTo already returns true whenever destinationType==typeof(string)
        // regardless of subclass), so PropertyNode.hasStringConverter comes out true and
        // ValueString's round-trip works. An object-typed property like Button.Content or .Tag
        // would NOT work here even though it often holds a string at runtime: the *reflected*
        // type is object, so TypeDescriptor.GetConverter(typeof(object)) returns the plain base
        // TypeConverter, whose CanConvertFrom(typeof(string)) is false by default (confirmed
        // empirically - swapping to Content threw/failed before this comment was written) -
        // exactly the trap the plan's own review caught. Neither Button nor the shared Grid
        // fixture (GridXaml/Load(), used by 4 other tests below) has any genuinely
        // string-typed property, so this test loads its own single-control TextBox fixture
        // (Text is System.String) rather than touching the shared fixture other tests depend on.
        [Test]
        public void EditingThroughTheTextBoxEditorRoundTripsThroughSetValueAndUndo()
        {
            const string TextBoxXaml =
                "<TextBox xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Name=\"T\" Text=\"a\" />";

            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(TextBoxXaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                var item = surface.DesignContext.RootItem;

                var grid = new PropertyGridControl();
                grid.SelectedItems = new[] { item };

                var node = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Text");
                var textBox = (TextBox)node.Editor;
                textBox.Text = "123";
                RaiseLostFocus(textBox);

                Assert.AreEqual("123", ((TextBox)item.Component).Text);
                Assert.IsTrue(item.Services.GetService<UndoService>().CanUndo);
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

        [Test]
        public void ABoolPropertyGetsACheckBoxEditorThatRoundTripsThroughSetValue()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, _) = Load(window);
                var grid = new PropertyGridControl();
                grid.SelectedItems = new[] { a };

                var node = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "IsEnabled");
                var checkBox = (CheckBox)node.Editor;
                Assert.IsTrue(checkBox.IsChecked == true, "IsEnabled defaults to true");

                checkBox.IsChecked = false;
                RaiseToggle(checkBox);

                Assert.IsFalse(((Button)a.Component).IsEnabled);
            });
        }

        [Test]
        public void AnEnumPropertyGetsAComboBoxEditorPopulatedFromMetadataStandardValues()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, _) = Load(window);
                var grid = new PropertyGridControl();
                grid.SelectedItems = new[] { a };

                var node = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Visibility");
                var comboBox = (ComboBox)node.Editor;

                CollectionAssert.Contains(((System.Collections.IEnumerable)comboBox.ItemsSource).Cast<object>().ToList(), Microsoft.UI.Xaml.Visibility.Collapsed);

                comboBox.SelectedItem = Microsoft.UI.Xaml.Visibility.Collapsed;
                RaiseSelectionChanged(comboBox);

                Assert.AreEqual(Microsoft.UI.Xaml.Visibility.Collapsed, ((Button)a.Component).Visibility);
            });
        }

        [Test]
        public void ANumericPropertyWithARegisteredRangeGetsANumberBoxEditorWithThatRange()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, _) = Load(window);
                Metadata.AddValueRange(Microsoft.UI.Xaml.FrameworkElement.WidthProperty, 0, 500);
                try {
                    var grid = new PropertyGridControl();
                    grid.SelectedItems = new[] { a };

                    var node = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Width");
                    var numberBox = (NumberBox)node.Editor;

                    Assert.AreEqual(0, numberBox.Minimum);
                    Assert.AreEqual(500, numberBox.Maximum);

                    numberBox.Value = 200;
                    RaiseNumberBoxValueChanged(numberBox);

                    Assert.AreEqual(200.0, (double)((Button)a.Component).Width);
                } finally {
                    ResetMetadataValueRanges();
                }
            });
        }

        static void ResetMetadataValueRanges()
        {
            // Metadata.ranges is `static readonly` - SetValue-ing the field itself throws
            // FieldAccessException once the type is initialized (confirmed empirically here;
            // same class of incident as ResetMetadataPopularProperties above and this project's
            // two prior readonly-removal reverts). Mutate the existing Dictionary in place via its
            // non-generic IDictionary surface instead of replacing the field's reference.
            var field = typeof(Metadata).GetField("ranges", BindingFlags.NonPublic | BindingFlags.Static);
            var dictionary = (System.Collections.IDictionary)field.GetValue(null);
            dictionary.Clear();
        }

        [Test]
        public void APropertyWithNamedStandardValuesGetsAComboBoxEditorPopulatedFromMetadataNamedStandardValues()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, _) = Load(window);
                // Background's ReturnType is Microsoft.UI.Xaml.Media.Brush (XamlNormalPropertyInfo.ReturnType
                // is _propertyDescriptor.PropertyType - the reflected CLR property type) - not an enum and
                // nothing registers plain Metadata.GetStandardValues for Brush anywhere, so
                // ComboBoxEditor.Create's own standard-values check (re-derived independently of
                // EditorManager's) falls through to its else branch and calls Metadata.GetNamedStandardValues
                // instead - the branch under test.
                Metadata.AddDoubleNamedStandardValues(typeof(Microsoft.UI.Xaml.Media.Brush), typeof(NamedBrushValues));
                try {
                    var grid = new PropertyGridControl();
                    grid.SelectedItems = new[] { a };

                    var node = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Background");
                    var comboBox = (ComboBox)node.Editor;

                    Assert.AreEqual("Name", comboBox.DisplayMemberPath);
                    Assert.AreEqual("Value", comboBox.SelectedValuePath);
                    var items = ((System.Collections.IEnumerable)comboBox.ItemsSource).Cast<object>().ToList();
                    Assert.AreEqual(2, items.Count, "AddDoubleNamedStandardValues reflects NamedBrushValues' 2 public static properties");

                    // Full round-trip, fix round 2: Commit now reads comboBox.SelectedValue (not
                    // .SelectedItem, which was the opaque NamedValue wrapper - the bug reported after
                    // fix round 1). Setting SelectedValue directly to the real underlying value (not
                    // the wrapper) exercises WinUI's Selector reverse lookup: it searches ItemsSource
                    // for whichever item's "Value" (SelectedValuePath) matches and selects that item,
                    // proving DisplayMemberPath/SelectedValuePath are correctly wired both directions.
                    // This assignment alone already fires a real SelectionChanged synchronously (the
                    // same mechanism the re-entrancy guard above protects against in the model->control
                    // sync direction), which commits for real right here - deliberately NOT also calling
                    // RaiseSelectionChanged/CommitForTest afterward (unlike the enum test above): that
                    // would be a REDUNDANT second commit of the same live Brush instance, and
                    // XamlModelProperty.SetValue rejects a second identical commit of a DependencyObject
                    // ("already has a parent", confirmed empirically) once the first commit has already
                    // parented it - unlike the enum test's boxed values, which never go through that
                    // DesignItem-registration path at all.
                    comboBox.SelectedValue = NamedBrushValues.First;
                    Assert.AreSame(NamedBrushValues.First, ((Button)a.Component).Background);
                } finally {
                    ResetMetadataNamedStandardValues();
                }
            });
        }

        static class NamedBrushValues
        {
            public static Microsoft.UI.Xaml.Media.Brush First { get; } = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            public static Microsoft.UI.Xaml.Media.Brush Second { get; } = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Blue);
        }

        static void ResetMetadataNamedStandardValues()
        {
            // Metadata.standardNamedValues is `static readonly` - same IDictionary.Clear()-in-place
            // pattern as ResetMetadataValueRanges/ResetMetadataPopularProperties above, never SetValue
            // on the field itself.
            var field = typeof(Metadata).GetField("standardNamedValues", BindingFlags.NonPublic | BindingFlags.Static);
            var dictionary = (System.Collections.IDictionary)field.GetValue(null);
            dictionary.Clear();
        }

        static void RaiseToggle(CheckBox checkBox)
        {
            WinUIDesigner.Designer.PropertyGrid.Editors.BoolEditor.CommitForTest(checkBox);
        }

        static void RaiseSelectionChanged(ComboBox comboBox)
        {
            WinUIDesigner.Designer.PropertyGrid.Editors.ComboBoxEditor.CommitForTest(comboBox);
        }

        static void RaiseNumberBoxValueChanged(NumberBox numberBox)
        {
            WinUIDesigner.Designer.PropertyGrid.Editors.NumberEditor.CommitForTest(numberBox);
        }
    }
}
