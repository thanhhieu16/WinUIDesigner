using System.IO;
using System.Threading;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class GrayOutDesignerExceptActiveAreaTests
    {
        const string CanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"300\" Height=\"200\">" +
            "<Button x:Name=\"Target\" Canvas.Left=\"20\" Canvas.Top=\"30\" Width=\"60\" Height=\"40\" />" +
            "</Canvas>";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(CanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void StartBuildsAnEvenOddCoverWithAHoleAndAddsItAsAnAdorner()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var root = surface.DesignContext.RootItem;
                GrayOutDesignerExceptActiveArea grayOut = null;

                GrayOutDesignerExceptActiveArea.Start(ref grayOut, surface.DesignContext.Services, root.View);
                surface.UpdateLayout();

                Assert.IsNotNull(grayOut);
                Assert.IsTrue(surface.DesignPanel.Adorners.Contains(grayOut.OwnerAdornerPanel));
                Assert.AreEqual(Microsoft.UI.Xaml.Media.FillRule.EvenOdd, grayOut.Geometry.FillRule,
                    "the cover-with-a-hole effect depends on even-odd filling");
                Assert.AreEqual(2, grayOut.Geometry.Figures.Count,
                    "one figure for the full-surface cover, one for the hole");

                // Start's 3-arg overload uses the whole active container as the hole.
                Assert.AreEqual(0, grayOut.ActiveAreaLeft, 0.01);
                Assert.AreEqual(0, grayOut.ActiveAreaTop, 0.01);
                Assert.AreEqual(300, grayOut.ActiveAreaWidth, 0.01);
                Assert.AreEqual(200, grayOut.ActiveAreaHeight, 0.01);
            });
        }

        [Test]
        public void AnimateActiveAreaRectToMovesTheHoleOverSeveralFramesAndRewritesTheHoleFigure()
        {
            GrayOutDesignerExceptActiveArea grayOut = null;
            int updatesBeforeAnimation = 0;

            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var root = surface.DesignContext.RootItem;
                GrayOutDesignerExceptActiveArea.Start(ref grayOut, surface.DesignContext.Services, root.View);
                surface.UpdateLayout();

                updatesBeforeAnimation = grayOut.HoleUpdateCount;
                grayOut.AnimateActiveAreaRectTo(new Windows.Foundation.Rect(10, 10, 100, 80));
            });

            // The transition is 100ms; give the dispatcher loop time to run it to completion.
            Thread.Sleep(500);

            WinUITestHost.Run(() => {
                Assert.AreEqual(10, grayOut.ActiveAreaLeft, 0.01);
                Assert.AreEqual(10, grayOut.ActiveAreaTop, 0.01);
                Assert.AreEqual(100, grayOut.ActiveAreaWidth, 0.01);
                Assert.AreEqual(80, grayOut.ActiveAreaHeight, 0.01);
                Assert.Greater(grayOut.HoleUpdateCount, updatesBeforeAnimation + 1,
                    "a real transition, not a single snap: the DoubleAnimations must tick more than once");

                var corner = ((Microsoft.UI.Xaml.Media.LineSegment)grayOut.Geometry.Figures[1].Segments[1]).Point;
                Assert.AreEqual(110, corner.X, 0.01);
                Assert.AreEqual(90, corner.Y, 0.01);
            });
        }

        [Test]
        public void StopClearsTheCallersReferenceAndLeavesTheAdornerUpForTheFadeOut()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var root = surface.DesignContext.RootItem;
                GrayOutDesignerExceptActiveArea grayOut = null;
                GrayOutDesignerExceptActiveArea.Start(ref grayOut, surface.DesignContext.Services, root.View);
                surface.UpdateLayout();

                var panel = grayOut.OwnerAdornerPanel;
                GrayOutDesignerExceptActiveArea.Stop(ref grayOut);

                Assert.IsNull(grayOut, "Stop must null the caller's field so a later Start can run");
                Assert.IsTrue(surface.DesignPanel.Adorners.Contains(panel),
                    "the adorner stays up until the fade-out finishes; its removal is timer-driven");
            });
        }

        [Test]
        public void StartDoesNothingWhenTheOptionIsTurnedOff()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var root = surface.DesignContext.RootItem;
                surface.DesignContext.Services.GetService<OptionService>()
                    .GrayOutDesignSurfaceExceptParentContainerWhenDragging = false;

                GrayOutDesignerExceptActiveArea grayOut = null;
                int adornersBefore = surface.DesignPanel.Adorners.Count;
                GrayOutDesignerExceptActiveArea.Start(ref grayOut, surface.DesignContext.Services, root.View);

                Assert.IsNull(grayOut);
                Assert.AreEqual(adornersBefore, surface.DesignPanel.Adorners.Count);
            });
        }
    }
}
