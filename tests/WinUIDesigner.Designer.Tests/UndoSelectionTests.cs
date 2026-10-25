using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using Windows.Foundation;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    /// <summary>
    /// An undo or redo that takes a control out of the document also takes it out of the
    /// selection. Found in the shell walkthrough: undoing a placement left the removed button in
    /// the property grid.
    /// </summary>
    [TestFixture]
    public class UndoSelectionTests
    {
        const string CanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\" />";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(CanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        /// <summary>Places a Button through the drop path and returns its DesignItem.</summary>
        static DesignItem PlaceButton(DesignSurface surface)
        {
            var tool = new CreateComponentTool(typeof(Button));
            surface.DesignContext.Services.GetService<IToolService>().CurrentTool = tool;
            var canvas = (Canvas)surface.DesignContext.RootItem.Component;
            var point = canvas.TransformToVisual(surface.DesignPanel).TransformPoint(new Point(50, 60));
            var marker = new Dictionary<string, object> { [CreateComponentTool.ToolboxTypeKey] = typeof(Button).AssemblyQualifiedName };
            Assert.IsTrue(tool.HandleDrop(surface.DesignPanel, point, marker));
            return surface.DesignContext.Services.Selection.PrimarySelection;
        }

        [Test]
        public void UndoingAPlacementDeselectsThePlacedControl()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var selection = surface.DesignContext.Services.Selection;
                var button = PlaceButton(surface);
                Assert.IsInstanceOf<Button>(button?.Component);
                Assert.IsTrue(selection.IsComponentSelected(button));

                int changes = 0;
                selection.SelectionChanged += (s, e) => changes++;
                surface.DesignContext.Services.GetService<UndoService>().Undo();

                Assert.IsFalse(selection.IsComponentSelected(button), "the undone control is no longer selected");
                Assert.IsNull(selection.PrimarySelection);
                Assert.AreEqual(1, changes, "listeners such as the property grid hear about it");
            });
        }

        [Test]
        public void AnUndoThatRemovesNothingLeavesTheSelectionAlone()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var selection = surface.DesignContext.Services.Selection;
                var button = PlaceButton(surface);
                var undo = surface.DesignContext.Services.GetService<UndoService>();

                // Undo the placement, redo it, and select it again: the redo re-adds the button.
                undo.Undo();
                undo.Redo();
                selection.SetSelectedComponents(new[] { button });

                // Move it, then undo the move: the button stays in the document, so stays selected.
                var operation = PlacementOperation.Start(new[] { button }, PlacementType.Move);
                var info = operation.PlacedItems.First();
                info.Bounds = new Rect(info.OriginalBounds.X + 10, info.OriginalBounds.Y, info.OriginalBounds.Width, info.OriginalBounds.Height);
                operation.CurrentContainerBehavior.SetPosition(info);
                operation.Commit();
                undo.Undo();

                Assert.IsTrue(selection.IsComponentSelected(button));
            });
        }
    }
}
