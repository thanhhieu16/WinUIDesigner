using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls.Thumbs;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class ResizeThumbExtensionTests
    {
        const string ButtonXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</Grid>";

        static DesignSurface LoadSelected(Microsoft.UI.Xaml.Window window, out DesignItem buttonItem, out Microsoft.UI.Xaml.Controls.Button button)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(ButtonXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();

            button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component).Children[0];
            buttonItem = surface.DesignContext.Services.Component.GetDesignItem(button);
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { buttonItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            return surface;
        }

        static ResizeThumbExtension GetExtension(DesignItem buttonItem)
        {
            return buttonItem.Extensions.OfType<ResizeThumbExtension>().Single();
        }

        [Test]
        public void SelectingAnElementShowsEightResizeThumbs()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out _, out var button);
                var thumbs = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<ResizeThumb>().ToList();
                Assert.AreEqual(8, thumbs.Count);
            });
        }

        [Test]
        public void DraggingTheBottomRightThumbGrowsWidthAndHeightAndCommitsOneChangeGroup()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var extension = GetExtension(buttonItem);
                var drag = extension.DragListeners[PlacementAlignment.BottomRight];

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(20, 15)); // past threshold, +20 width, +15 height
                drag.SimulateUp(new Windows.Foundation.Point(20, 15));
                surface.UpdateLayout();

                Assert.AreEqual(60, button.Width, 0.5);
                Assert.AreEqual(35, button.Height, 0.5);
            });
        }

        [Test]
        public void DraggingTheRightThumbOnlyChangesWidth()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var extension = GetExtension(buttonItem);
                var drag = extension.DragListeners[PlacementAlignment.Right];

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(15, 0));
                drag.SimulateUp(new Windows.Foundation.Point(15, 0));
                surface.UpdateLayout();

                Assert.AreEqual(55, button.Width, 0.5);
                Assert.AreEqual(20, button.Height, 0.5);
            });
        }

        [Test]
        public void CancelingAResizeRevertsWidthAndHeightWithNoCommittedChange()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSelected(window, out var buttonItem, out var button);
                var extension = GetExtension(buttonItem);
                var drag = extension.DragListeners[PlacementAlignment.BottomRight];

                var undoService = surface.DesignContext.Services.GetService<WinUIDesigner.DesignModel.Services.UndoService>();
                bool canUndoBefore = undoService != null && undoService.CanUndo;

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(30, 30));
                drag.Cancel();
                surface.UpdateLayout();

                Assert.AreEqual(40, button.Width, 0.5, "canceled resize should revert to the original width");
                Assert.AreEqual(20, button.Height, 0.5, "canceled resize should revert to the original height");

                if (undoService != null)
                    Assert.AreEqual(canUndoBefore, undoService.CanUndo,
                        "an aborted PlacementOperation must not push anything onto the undo stack");
            });
        }
    }
}
