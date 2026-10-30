using NUnit.Framework;
using Windows.Foundation;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>The zoom bar's arithmetic (zoom spec). The zoom itself is the ScrollViewer's and is
    /// verified on the real app by the walkthrough.</summary>
    [TestFixture]
    public class ZoomTests
    {
        [Test]
        public void NextZoom_StepsThroughTheLevels()
        {
            Assert.That(DocumentView.NextZoom(1, true), Is.EqualTo(1.25));
            Assert.That(DocumentView.NextZoom(1, false), Is.EqualTo(0.75));
            Assert.That(DocumentView.NextZoom(2, true), Is.EqualTo(3));
        }

        [Test]
        public void NextZoom_FromBetweenStepsGoesToTheNearestInThatDirection()
        {
            Assert.That(DocumentView.NextZoom(1.1, true), Is.EqualTo(1.25));
            Assert.That(DocumentView.NextZoom(1.1, false), Is.EqualTo(1));
            // A float ZoomFactor that is 1 to within rounding is 1, not a step below it.
            Assert.That(DocumentView.NextZoom(0.999f, true), Is.EqualTo(1.25));
        }

        [Test]
        public void NextZoom_StopsAtTheEnds()
        {
            Assert.That(DocumentView.NextZoom(4, true), Is.EqualTo(4));
            Assert.That(DocumentView.NextZoom(0.25, false), Is.EqualTo(0.25));
        }

        [Test]
        public void FitZoom_ShowsTheWholeContent()
        {
            Assert.That(DocumentView.FitZoom(new Size(500, 400), new Size(1000, 400)), Is.EqualTo(0.5));
            Assert.That(DocumentView.FitZoom(new Size(500, 400), new Size(100, 100)), Is.EqualTo(4));
            Assert.That(DocumentView.FitZoom(new Size(500, 400), new Size(10000, 10000)), Is.EqualTo(0.25));
            Assert.That(DocumentView.FitZoom(new Size(500, 400), new Size(0, 0)), Is.EqualTo(1));
        }
    }
}
