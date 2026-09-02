using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PlacementOperationChangeContainerTests
    {
        const string TwoCanvasXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Canvas x:Name=\"SourceCanvas\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" />" +
            "</Canvas>" +
            "<Canvas x:Name=\"TargetCanvas\" />" +
            "</Grid>";

        static (DesignSurface surface, DesignItem buttonItem, DesignItem sourceItem, DesignItem targetItem)
            LoadTwoCanvases(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(TwoCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();

            var root = (Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component;
            var sourceCanvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[0];
            var targetCanvas = (Microsoft.UI.Xaml.Controls.Canvas)root.Children[1];
            var button = (Microsoft.UI.Xaml.Controls.Button)sourceCanvas.Children[0];

            var services = surface.DesignContext.Services;
            var buttonItem = services.Component.GetDesignItem(button);
            var sourceItem = services.Component.GetDesignItem(sourceCanvas);
            var targetItem = services.Component.GetDesignItem(targetCanvas);
            return (surface, buttonItem, sourceItem, targetItem);
        }

        [Test]
        public void ChangeContainerMovesTheItemFromOneLiveParentToAnotherInTheSameDocument()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, targetItem) = LoadTwoCanvases(window);
                Assert.AreSame(sourceItem, buttonItem.Parent, "precondition: button starts under SourceCanvas");

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                op.ChangeContainer(targetItem);
                op.Commit();
                surface.UpdateLayout();

                Assert.AreSame(targetItem, buttonItem.Parent,
                    "button's DesignItem parent should now be TargetCanvas");
                Assert.IsFalse(sourceItem.ContentProperty.CollectionElements.Contains(buttonItem),
                    "SourceCanvas's Children collection should no longer contain the button");
                Assert.IsTrue(targetItem.ContentProperty.CollectionElements.Contains(buttonItem),
                    "TargetCanvas's Children collection should now contain the button");
            });
        }

        [Test]
        public void ChangeContainerToTheSameContainerIsANoOp()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, _) = LoadTwoCanvases(window);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                op.ChangeContainer(sourceItem);
                op.Commit();
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, buttonItem.Parent);
                Assert.IsTrue(sourceItem.ContentProperty.CollectionElements.Contains(buttonItem));
            });
        }

        [Test]
        public void UndoAfterChangeContainerRestoresTheOriginalParent()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, targetItem) = LoadTwoCanvases(window);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                op.ChangeContainer(targetItem);
                op.Commit();
                surface.UpdateLayout();

                var undoService = surface.DesignContext.Services.GetService<UndoService>();
                Assert.IsTrue(undoService.CanUndo);
                undoService.Undo();
                surface.UpdateLayout();

                Assert.AreSame(sourceItem, buttonItem.Parent,
                    "undo should restore the button's DesignItem parent to SourceCanvas");
                Assert.IsTrue(sourceItem.ContentProperty.CollectionElements.Contains(buttonItem));
                Assert.IsFalse(targetItem.ContentProperty.CollectionElements.Contains(buttonItem));
            });
        }

        [Test]
        public void ChangeContainerAfterCommitOrAbortThrows()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, buttonItem, sourceItem, targetItem) = LoadTwoCanvases(window);

                var op = PlacementOperation.Start(new[] { buttonItem }, PlacementType.Move);
                op.Commit();

                Assert.Throws<PlacementOperation.PlacementOperationException>(
                    () => op.ChangeContainer(targetItem));
            });
        }
    }
}
