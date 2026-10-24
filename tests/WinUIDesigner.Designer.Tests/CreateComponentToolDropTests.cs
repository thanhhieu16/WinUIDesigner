using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    /// <summary>
    /// CreateComponentTool's toolbox-drop path (toolbox-drag spec Decisions 3 and 4), driven
    /// through HandleDragOver/HandleDrop with plain values: a DragEventArgs cannot be built in a
    /// test, so whether a real OS drag reaches these is a walkthrough item.
    /// </summary>
    [TestFixture]
    public class CreateComponentToolDropTests
    {
        const string CanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\" />";

        /// <summary>Same fixture as CreateComponentToolTests.Load, including its Transparent
        /// Background on the live Canvas, without which DesignPanel.HitTest finds nothing over
        /// the Canvas's empty interior.</summary>
        static DesignSurface Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(CanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());

            var canvas = (Canvas)surface.DesignContext.RootItem.Component;
            canvas.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

            surface.UpdateLayout();
            return surface;
        }

        static Canvas RootCanvas(DesignSurface surface)
        {
            return (Canvas)surface.DesignContext.RootItem.Component;
        }

        /// <summary>A panel-relative point at canvas-local (x, y). The Canvas is centred in the
        /// shared window's DesignPanel, not flush at (0,0) - see CreateComponentToolTests.</summary>
        static Point PanelPoint(DesignSurface surface, double x, double y)
        {
            return RootCanvas(surface).TransformToVisual(surface.DesignPanel).TransformPoint(new Point(x, y));
        }

        static IReadOnlyDictionary<string, object> Marker(Type type)
        {
            return new Dictionary<string, object> { [CreateComponentTool.ToolboxTypeKey] = type.AssemblyQualifiedName };
        }

        static CreateComponentTool Arm(DesignSurface surface, Type type)
        {
            var tool = new CreateComponentTool(type);
            surface.DesignContext.Services.GetService<IToolService>().CurrentTool = tool;
            return tool;
        }

        [Test]
        public void DragOverAcceptsAMatchingToolboxDragWithoutAddingAnything()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = Arm(surface, typeof(Button));

                var operation = tool.HandleDragOver(surface.DesignPanel, PanelPoint(surface, 50, 60), Marker(typeof(Button)));

                Assert.AreEqual(DataPackageOperation.Copy, operation);
                Assert.AreEqual(0, RootCanvas(surface).Children.Count,
                    "DragOver only decides; inserting there put the control where the cursor entered the surface");
                Assert.IsFalse(surface.DesignContext.Services.GetService<UndoService>().CanUndo);
            });
        }

        [Test]
        public void DragOverRefusesADragThatIsNotThisTools()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = Arm(surface, typeof(Button));
                var point = PanelPoint(surface, 50, 60);

                Assert.AreEqual(DataPackageOperation.None, tool.HandleDragOver(surface.DesignPanel, point, new Dictionary<string, object>()),
                    "a drag from another application carries no marker");
                Assert.AreEqual(DataPackageOperation.None, tool.HandleDragOver(surface.DesignPanel, point, null));
                Assert.AreEqual(DataPackageOperation.None, tool.HandleDragOver(surface.DesignPanel, point, Marker(typeof(CheckBox))),
                    "a marker naming another type is not this tool's drag");
            });
        }

        [Test]
        public void DragOverRefusesAPointWithNothingUnderIt()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = Arm(surface, typeof(Button));

                Assert.AreEqual(DataPackageOperation.None,
                    tool.HandleDragOver(surface.DesignPanel, new Point(-50, -50), Marker(typeof(Button))));
            });
        }

        [Test]
        public void DropPlacesOneSelectedControlAsOneUndoStepAndDisarmsTheTool()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = Arm(surface, typeof(Button));
                var services = surface.DesignContext.Services;

                Assert.IsTrue(tool.HandleDrop(surface.DesignPanel, PanelPoint(surface, 50, 60), Marker(typeof(Button))));
                surface.UpdateLayout();

                var canvas = RootCanvas(surface);
                Assert.AreEqual(1, canvas.Children.Count);
                var button = canvas.Children[0] as Button;
                Assert.IsNotNull(button);
                Assert.AreSame(button, services.Selection.PrimarySelection?.Component, "the dropped control is selected");

                var toolService = services.GetService<IToolService>();
                Assert.AreSame(toolService.PointerTool, toolService.CurrentTool);

                var undo = services.GetService<UndoService>();
                Assert.IsTrue(undo.CanUndo);
                undo.Undo();
                surface.UpdateLayout();
                Assert.AreEqual(0, canvas.Children.Count, "one Undo removes the whole drop");
                Assert.IsFalse(undo.CanUndo, "the drop was one undo entry, not several");
            });
        }

        [Test]
        public void ADropLandsWhereItIsReleased()
        {
            // The regression test for Decision 4: Phase 4d inserted on the first DragOver, so
            // every control landed where the cursor entered the surface. Two drops at points 100px
            // apart must land 100px apart; asserting the difference keeps the test independent of
            // how panel points map onto Canvas.Left/Top.
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var canvas = RootCanvas(surface);

                var first = Arm(surface, typeof(Button));
                first.HandleDragOver(surface.DesignPanel, PanelPoint(surface, 10, 10), Marker(typeof(Button)));
                Assert.IsTrue(first.HandleDrop(surface.DesignPanel, PanelPoint(surface, 50, 60), Marker(typeof(Button))));

                var second = Arm(surface, typeof(Button));
                second.HandleDragOver(surface.DesignPanel, PanelPoint(surface, 10, 10), Marker(typeof(Button)));
                Assert.IsTrue(second.HandleDrop(surface.DesignPanel, PanelPoint(surface, 150, 160), Marker(typeof(Button))));
                surface.UpdateLayout();

                Assert.AreEqual(2, canvas.Children.Count);
                var a = canvas.Children[0];
                var b = canvas.Children[1];
                Assert.AreEqual(100, Canvas.GetLeft(b) - Canvas.GetLeft(a), 0.5);
                Assert.AreEqual(100, Canvas.GetTop(b) - Canvas.GetTop(a), 0.5);
            });
        }

        [Test]
        public void ADropThatIsNotThisToolsOrHitsNothingPlacesNothing()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = Arm(surface, typeof(Button));
                var services = surface.DesignContext.Services;

                Assert.IsFalse(tool.HandleDrop(surface.DesignPanel, PanelPoint(surface, 50, 60), new Dictionary<string, object>()));
                Assert.IsFalse(tool.HandleDrop(surface.DesignPanel, new Point(-50, -50), Marker(typeof(Button))));

                Assert.AreEqual(0, RootCanvas(surface).Children.Count);
                Assert.IsFalse(services.GetService<UndoService>().CanUndo, "a refused drop leaves no undo entry");
                Assert.AreSame(tool, services.GetService<IToolService>().CurrentTool,
                    "a refused drop leaves the tool armed; the toolbox disarms it when the drag ends in None");
            });
        }

        [Test]
        public void TheDesignPanelIsADropTargetOverItsWholeArea()
        {
            WinUITestHost.Run(() => {
                var panel = new DesignPanel();
                Assert.IsTrue(panel.AllowDrop);
                Assert.IsNotNull(panel.Background, "a null Background is not hit-testable, so a drag over empty surface would reach no handler");
            });
        }
    }
}
