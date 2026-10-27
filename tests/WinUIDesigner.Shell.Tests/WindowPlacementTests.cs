using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Windows.Graphics;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>Where a remembered window may go back to. No WinUI involved.</summary>
    [TestFixture]
    public class WindowPlacementTests
    {
        static readonly IReadOnlyList<RectInt32> OneDisplay = new[] { new RectInt32(0, 0, 1920, 1040) };

        [Test]
        public void BoundsOnADisplayAreRestoredAsSaved()
        {
            var rect = WindowPlacement.Fit(new WindowBounds { X = 100, Y = 80, Width = 900, Height = 700 }, OneDisplay);

            Assert.That(rect, Is.EqualTo(new RectInt32(100, 80, 900, 700)));
        }

        [Test]
        public void NothingSavedLeavesTheWindowWhereTheSystemPutsIt()
        {
            Assert.That(WindowPlacement.Fit(null, OneDisplay), Is.Null);
            Assert.That(WindowPlacement.Fit(new WindowBounds(), OneDisplay), Is.Null);
        }

        [Test]
        public void BoundsOnADisplayThatIsGoneAreNotUsed()
        {
            // Saved on a second display to the right, which is no longer attached.
            var rect = WindowPlacement.Fit(new WindowBounds { X = 2100, Y = 100, Width = 900, Height = 700 }, OneDisplay);

            Assert.That(rect, Is.Null);
        }

        [Test]
        public void ATitleBarMostlyOffTheTopIsNotUsedButOneHalfOnTheSideIs()
        {
            Assert.That(WindowPlacement.Fit(new WindowBounds { X = 100, Y = -500, Width = 900, Height = 700 }, OneDisplay), Is.Null,
                "the title bar is above the display, so the window could not be dragged");
            Assert.That(WindowPlacement.Fit(new WindowBounds { X = 1500, Y = 100, Width = 900, Height = 700 }, OneDisplay), Is.Not.Null,
                "half off the right edge, the title bar can still be grabbed");
        }

        [Test]
        public void ATinyWindowIsGrownToTheMinimum()
        {
            var rect = WindowPlacement.Fit(new WindowBounds { X = 10, Y = 10, Width = 50, Height = 40 }, OneDisplay);

            Assert.That(rect.Value.Width, Is.EqualTo(WindowPlacement.MinWidth));
            Assert.That(rect.Value.Height, Is.EqualTo(WindowPlacement.MinHeight));
        }

        [Test]
        public void TheBoundsRoundTripThroughTheSettingsFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "wd-window-" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                ShellSettings.Update(path, s => s.Window = new WindowBounds { X = 1, Y = 2, Width = 800, Height = 600, Maximized = true });
                ShellSettings.Update(path, s => s.RecentFiles.Add(@"C:\a\One.xaml"));

                var window = ShellSettings.Load(path).Window;
                Assert.That(window, Is.Not.Null, "another setting's update keeps the window bounds");
                Assert.That(new[] { window.X, window.Y, window.Width, window.Height }, Is.EqualTo(new[] { 1, 2, 800, 600 }));
                Assert.That(window.Maximized, Is.True);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
