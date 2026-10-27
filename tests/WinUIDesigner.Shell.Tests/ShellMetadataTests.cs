using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using Windows.Foundation;
using WinUIDesigner.DesignModel;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class ShellMetadataTests
    {
        const string CanvasWithButton =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"640\" Height=\"480\">" +
            "<Button x:Name=\"Ok\" Content=\"Ok\" />" +
            "</Canvas>";

        [Test]
        public void RegisterSeedsDefaultSizesForCommonControls()
        {
            WinUITestHost.Run(() =>
            {
                ShellMetadata.Register();

                Assert.That(Metadata.GetDefaultSize(typeof(Button), false), Is.EqualTo(new Size(75, 32)));
                Assert.That(Metadata.GetDefaultSize(typeof(TextBox), false), Is.EqualTo(new Size(120, 32)));
                Assert.That(Metadata.GetDefaultSize(typeof(Canvas), false), Is.EqualTo(new Size(160, 120)));
            });
        }

        [Test]
        public void RegisterIsIdempotent()
        {
            WinUITestHost.Run(() =>
            {
                ShellMetadata.Register();
                Assert.DoesNotThrow(() => ShellMetadata.Register());
                Assert.That(Metadata.GetDefaultSize(typeof(Button), false), Is.EqualTo(new Size(75, 32)));
            });
        }

        [Test]
        public void SeededPropertiesShowUpAsPopularOnARealDesignItem()
        {
            WinUITestHost.Run(() =>
            {
                ShellMetadata.Register();

                using (var doc = new Document(null, CanvasWithButton))
                {
                    var button = doc.DesignContext.RootItem.ContentProperty.CollectionElements[0];

                    // Registered by DependencyProperty rather than by name: Metadata matches
                    // DesignItemProperty.DependencyProperty first, which is unambiguous, whereas
                    // the name form depends on which type FullName reports.
                    Assert.That(Metadata.IsPopularProperty(button.Properties["Width"]), Is.True);
                    Assert.That(Metadata.IsPopularProperty(button.Properties["Height"]), Is.True);
                    Assert.That(Metadata.IsPopularProperty(button.Properties["Margin"]), Is.True);
                    Assert.That(Metadata.IsPopularProperty(button.Properties["Background"]), Is.True);
                }
            });
        }
    

        [Test]
        public void APlacedControlGetsItsDefaultContentAndAShapeAFill()
        {
            // The path CreateComponentTool takes for a control placed from the toolbox: register
            // it, then apply the table's defaults.
            WinUITestHost.Run(() =>
            {
                ShellMetadata.Register();

                using (var doc = new Document(null, CanvasWithButton))
                {
                    var components = doc.DesignContext.Services.Component;

                    var button = components.RegisterComponentForDesigner(new Button());
                    components.SetDefaultPropertyValues(button);
                    Assert.That(((Button)button.Component).Content, Is.EqualTo("Button"));

                    var text = components.RegisterComponentForDesigner(new TextBlock());
                    components.SetDefaultPropertyValues(text);
                    Assert.That(((TextBlock)text.Component).Text, Is.EqualTo("TextBlock"));

                    var rectangle = components.RegisterComponentForDesigner(new Microsoft.UI.Xaml.Shapes.Rectangle());
                    components.SetDefaultPropertyValues(rectangle);
                    var fill = ((Microsoft.UI.Xaml.Shapes.Rectangle)rectangle.Component).Fill as Microsoft.UI.Xaml.Media.SolidColorBrush;
                    Assert.That(fill, Is.Not.Null, "a placed Rectangle is visible");
                    Assert.That(fill.Color, Is.EqualTo(Microsoft.UI.Colors.LightGray));
                }
            });
        }

        [Test]
        public void NumberPropertiesAreBoundedAndFocusNavigationIsHidden()
        {
            WinUITestHost.Run(() =>
            {
                ShellMetadata.Register();

                using (var doc = new Document(null, CanvasWithButton))
                {
                    var button = doc.DesignContext.RootItem.ContentProperty.CollectionElements[0];

                    var opacity = Metadata.GetValueRange(button.Properties["Opacity"]);
                    Assert.That(opacity, Is.Not.Null);
                    Assert.That(opacity.Min, Is.EqualTo(0));
                    Assert.That(opacity.Max, Is.EqualTo(1));
                    Assert.That(Metadata.GetValueRange(button.Properties["Width"]).Min, Is.EqualTo(0),
                        "a negative Width is not offered");

                    Assert.That(Metadata.IsBrowsable(button.Properties["XYFocusUp"]), Is.False);
                    Assert.That(Metadata.IsBrowsable(button.Properties["FocusVisualPrimaryBrush"]), Is.False);
                    Assert.That(Metadata.IsBrowsable(button.Properties["Width"]), Is.True);
                }
            });
        }

        [Test]
        public void ATextBlocksOwnLookPropertiesArePopular()
        {
            // TextBlock is not a Control: its Foreground and FontSize are its own dependency
            // properties, which Control's do not cover.
            WinUITestHost.Run(() =>
            {
                ShellMetadata.Register();

                using (var doc = new Document(null, CanvasWithButton))
                {
                    var text = doc.DesignContext.Services.Component.RegisterComponentForDesigner(new TextBlock());
                    Assert.That(Metadata.IsPopularProperty(text.Properties["Foreground"]), Is.True);
                    Assert.That(Metadata.IsPopularProperty(text.Properties["FontSize"]), Is.True);
                    Assert.That(Metadata.IsPopularProperty(text.Properties["Text"]), Is.True);
                }
            });
        }
    }
}
