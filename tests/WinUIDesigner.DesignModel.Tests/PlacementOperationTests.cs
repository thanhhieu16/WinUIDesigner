using System.Collections.Generic;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class PlacementOperationTests
    {
        // A minimal IPlacementBehavior stub - just enough to exercise Start/Commit/Abort's
        // lifecycle calls without needing a real DesignItem tree.
        sealed class RecordingPlacementBehavior : IPlacementBehavior
        {
            public bool BeganPlacement, EndedPlacement;
            public bool CanPlace(IEnumerable<DesignItem> childItems, PlacementType type, PlacementAlignment position) => true;
            public void BeginPlacement(PlacementOperation operation) => BeganPlacement = true;
            public void EndPlacement(PlacementOperation operation) => EndedPlacement = true;
            public Windows.Foundation.Rect GetPosition(PlacementOperation operation, DesignItem child) => new Windows.Foundation.Rect(0, 0, 10, 10);
            public void BeforeSetPosition(PlacementOperation operation) { }
            public void SetPosition(PlacementInformation info) { }
            public bool CanLeaveContainer(PlacementOperation operation) => true;
            public void LeaveContainer(PlacementOperation operation) { }
            public bool CanEnterContainer(PlacementOperation operation, bool shouldAlwaysEnter) => true;
            public void EnterContainer(PlacementOperation operation) { }
            public Windows.Foundation.Point PlacePoint(Windows.Foundation.Point point) => point;
        }

        [Test]
        public void PlacementTypesAreDistinctEvenWithTheSameToStringGroup()
        {
            // Register() intentionally allows duplicate names to produce distinct instances -
            // Resize and a hypothetical second "Resize" would compare unequal by design.
            var a = PlacementType.Register("Test");
            var b = PlacementType.Register("Test");
            Assert.AreNotSame(a, b);
            Assert.AreEqual("Test", a.ToString());
        }

        [Test]
        public void PlacementAlignmentRejectsStretch()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new PlacementAlignment(Microsoft.UI.Xaml.HorizontalAlignment.Stretch, Microsoft.UI.Xaml.VerticalAlignment.Top));
        }

        [Test]
        public void GetRealElementSizeUsesExplicitWidthHeightWhenSet()
        {
            WinUITestHost.Run(() => {
                var grid = new Microsoft.UI.Xaml.Controls.Grid { Width = 42, Height = 17 };
                var size = PlacementOperation.GetRealElementSize(grid);
                Assert.AreEqual(42, size.Width);
                Assert.AreEqual(17, size.Height);
            });
        }
    }
}
