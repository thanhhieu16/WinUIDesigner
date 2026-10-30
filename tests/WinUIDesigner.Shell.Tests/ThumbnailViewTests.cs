using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using NUnit.Framework;
using Windows.Foundation;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>The thumbnail pane (thumbnail spec).</summary>
    [TestFixture]
    public class ThumbnailViewTests
    {
        const string BigCanvas =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "Width=\"800\" Height=\"600\" />";

        [Test]
        public void ImageRect_FitsAndCentresTheContent()
        {
            var rect = ThumbnailView.ImageRect(new Size(200, 100), new Size(800, 600));
            // Height limits: scale 1/6, so 133.3 x 100, centred horizontally.
            Assert.That(rect.Height, Is.EqualTo(100).Within(0.01));
            Assert.That(rect.Width, Is.EqualTo(800.0 / 6).Within(0.01));
            Assert.That(rect.X, Is.EqualTo((200 - 800.0 / 6) / 2).Within(0.01));
            Assert.That(rect.Y, Is.EqualTo(0).Within(0.01));
        }

        [Test]
        public void ImageRect_NeverEnlargesASmallDesign()
        {
            var rect = ThumbnailView.ImageRect(new Size(200, 100), new Size(40, 20));
            Assert.That(rect, Is.EqualTo(new Rect(80, 40, 40, 20)));
        }

        [Test]
        public void ImageRect_EmptyForNoContent()
        {
            Assert.That(ThumbnailView.ImageRect(new Size(200, 100), new Size(0, 0)).IsEmpty, Is.True);
        }

        [Test]
        public void FrameRect_ScalesAndClipsTheVisibleRegion()
        {
            var image = new Rect(10, 0, 200, 150);
            var content = new Size(800, 600);

            var inside = ThumbnailView.FrameRect(image, content, new Rect(100, 100, 400, 200));
            Assert.That(inside, Is.EqualTo(new Rect(35, 25, 100, 50)));

            // A viewport larger than the content, scrolled to the origin, frames the whole picture.
            var all = ThumbnailView.FrameRect(image, content, new Rect(-50, -50, 2000, 2000));
            Assert.That(all, Is.EqualTo(image));

            Assert.That(ThumbnailView.FrameRect(image, content, new Rect(900, 0, 100, 100)).IsEmpty, Is.True);
        }

        [Test]
        public void ContentPoint_MapsBackAndClamps()
        {
            var image = new Rect(10, 0, 200, 150);
            var content = new Size(800, 600);
            Assert.That(ThumbnailView.ContentPoint(image, content, new Point(60, 50)), Is.EqualTo(new Point(200, 200)));
            Assert.That(ThumbnailView.ContentPoint(image, content, new Point(-20, 500)), Is.EqualTo(new Point(0, 600)));
        }

        [Test]
        /// <remarks>The canvas has no Background and nothing in it, like a new document: the
        /// case the runner showed drawing nothing before RenderWholeAsync.</remarks>
        public void ADesignIsDrawnFramedAndAPressScrollsTheSurface()
        {
            WinUITestHost.RunWithWindowAsync(async window =>
            {
                using (var doc = new Document(null, BigCanvas))
                {
                    var thumbnail = new ThumbnailView { Width = 160, Height = 120 };
                    var host = new ContentControl
                    {
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        VerticalContentAlignment = VerticalAlignment.Stretch,
                        Content = doc.DesignSurface
                    };
                    var scroll = new ScrollViewer
                    {
                        Width = 300,
                        Height = 200,
                        Content = host,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    };
                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    panel.Children.Add(scroll);
                    panel.Children.Add(thumbnail);
                    window.Content = panel;
                    panel.UpdateLayout();

                    thumbnail.Attach(doc, scroll, host);
                    await thumbnail.RenderNowAsync();

                    var bitmap = thumbnail.ImageSourceForTest as RenderTargetBitmap;
                    Assert.That(bitmap, Is.Not.Null, "the design should be drawn");
                    Assert.That(bitmap.PixelWidth, Is.GreaterThan(0));
                    // 800x600 into 160x120: scale 0.2, filling the pane.
                    Assert.That(thumbnail.ImageRectForTest.Width, Is.EqualTo(160).Within(0.5));
                    Assert.That(thumbnail.ImageRectForTest.Height, Is.EqualTo(120).Within(0.5));

                    // The frame is the 300x200 viewport (less scroll bars) at scale 0.2, in the top left.
                    Assert.That(thumbnail.FrameVisibilityForTest, Is.EqualTo(Visibility.Visible));
                    var frame = thumbnail.FrameRectForTest;
                    Assert.That(frame.Width, Is.LessThanOrEqualTo(60.5).And.GreaterThan(40));
                    Assert.That(frame.Height, Is.LessThanOrEqualTo(40.5).And.GreaterThan(25));

                    thumbnail.ScrollTo(new Point(150, 110));
                    await Task.Delay(300);
                    panel.UpdateLayout();
                    Assert.That(scroll.HorizontalOffset, Is.GreaterThan(0), "a press near the bottom right scrolls right");
                    Assert.That(scroll.VerticalOffset, Is.GreaterThan(0), "and down");

                    doc.Mode = DocumentMode.Xaml;
                    await thumbnail.RenderNowAsync();
                    Assert.That(thumbnail.ImageSourceForTest, Is.Null, "XAML mode has no design to draw");

                    thumbnail.Attach(null, null, null);
                    window.Content = null;
                }
            });
        }
    }
}
