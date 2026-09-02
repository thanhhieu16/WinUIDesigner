using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class CanvasPlacementSupportTests
    {
        const string CanvasXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Canvas x:Name=\"HostCanvas\" Width=\"200\" Height=\"200\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" Canvas.Left=\"10\" Canvas.Top=\"15\" />" +
            "</Canvas></Grid>";

        static (DesignSurface surface, DesignItem buttonItem) LoadSelected(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(CanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();

            var root = (Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component;
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
            var button = (Microsoft.UI.Xaml.Controls.Button)canvas.Children[0];
            var services = surface.DesignContext.Services;
            var buttonItem = services.Component.GetDesignItem(button);
            services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            return (surface, buttonItem);
        }

        [Test]
        public void ResizingAnItemInsideACanvasUpdatesCanvasLeftAndTopNotJustWidthAndHeight()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem) = LoadSelected(window);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Resize);
                var info = op.PlacedItems[0];
                info.Bounds = new Windows.Foundation.Rect(30, 40, 60, 25);
                op.CurrentContainerBehavior.SetPosition(info);
                op.Commit();
                surface.UpdateLayout();

                Assert.AreEqual(30, buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left").GetConvertedValueOnInstance<double>(), 0.5);
                Assert.AreEqual(40, buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top").GetConvertedValueOnInstance<double>(), 0.5);
                Assert.AreEqual(60, ((Microsoft.UI.Xaml.FrameworkElement)buttonItem.Component).Width, 0.5);
                Assert.AreEqual(25, ((Microsoft.UI.Xaml.FrameworkElement)buttonItem.Component).Height, 0.5);
            });
        }

        [Test]
        public void GetPositionReadsCanvasLeftAndTopWhenTheyAreSet()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem) = LoadSelected(window);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Resize);
                var bounds = op.PlacedItems[0].OriginalBounds;
                op.Abort();

                Assert.AreEqual(10, bounds.X, 0.5, "GetPosition should read the already-set Canvas.Left, not compute from the live render transform");
                Assert.AreEqual(15, bounds.Y, 0.5);
            });
        }

        [Test]
        public void LeavingTheCanvasResetsCanvasLeftAndTop()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem) = LoadSelected(window);
                var canvasItem = buttonItem.Parent;

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                op.CurrentContainerBehavior.LeaveContainer(op);
                surface.UpdateLayout();

                // Asserted before Abort() deliberately: PlacementOperation.Abort() rolls back
                // the whole open UndoService transaction (see XamlModelProperty.
                // PropertyChangeAction.Undo), which would restore Canvas.Left/Top to their
                // pre-operation values and always fail this assertion regardless of whether
                // LeaveContainer's reset is correct. What's under test is that LeaveContainer
                // itself performs the reset while the operation is still open - the same point
                // in the lifecycle where ChangeContainer calls it mid-drag. Abort() still runs
                // afterward, only to close the operation cleanly.
                var leftProp = buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Left");
                var topProp = buttonItem.Properties.GetAttachedProperty(typeof(Microsoft.UI.Xaml.Controls.Canvas), "Top");
                Assert.IsFalse(leftProp.IsSet, "Canvas.Left should be reset once the item leaves the Canvas");
                Assert.IsFalse(topProp.IsSet, "Canvas.Top should be reset once the item leaves the Canvas");

                op.Abort();
            });
        }
    }
}
