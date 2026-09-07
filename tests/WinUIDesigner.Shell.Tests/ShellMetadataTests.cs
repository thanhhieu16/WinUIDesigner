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
    }
}
