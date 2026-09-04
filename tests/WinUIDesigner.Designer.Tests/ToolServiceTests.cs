using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class ToolServiceTests
    {
        const string ButtonXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</Grid>";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(ButtonXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        sealed class RecordingTool : ITool
        {
            public int ActivateCount, DeactivateCount;
            public Microsoft.UI.Input.InputCursor Cursor => Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Cross);
            public void Activate(IDesignPanel designPanel) => ActivateCount++;
            public void Deactivate(IDesignPanel designPanel) => DeactivateCount++;
        }

        [Test]
        public void PointerToolIsActiveImmediatelyAfterLoad()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var toolService = surface.DesignContext.Services.GetService<IToolService>();
                Assert.IsNotNull(toolService);
                Assert.AreSame(toolService.PointerTool, toolService.CurrentTool);
            });
        }

        [Test]
        public void SwappingCurrentToolDeactivatesTheOldOneAndActivatesTheNewOneExactlyOnce()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var toolService = surface.DesignContext.Services.GetService<IToolService>();
                var recording = new RecordingTool();

                toolService.CurrentTool = recording;
                Assert.AreEqual(1, recording.ActivateCount);
                Assert.AreEqual(0, recording.DeactivateCount);

                toolService.CurrentTool = toolService.PointerTool;
                Assert.AreEqual(1, recording.ActivateCount);
                Assert.AreEqual(1, recording.DeactivateCount);
            });
        }

        [Test]
        public void ClickToSelectStillWorksWithPointerToolActiveByDefault()
        {
            // Regression check: HandlePointerPressed itself is untouched by this task, and the
            // full existing selection suite (MoveTests, ResizeThumbExtensionTests, etc.) already
            // proves this end-to-end - this is a narrow smoke test specific to this task's own
            // deliverable, not a replacement for that suite.
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component).Children[0];
                var buttonItem = surface.DesignContext.Services.Component.GetDesignItem(button);

                surface.DesignPanel.HandlePointerPressed(new Windows.Foundation.Point(10, 10), Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();

                Assert.AreSame(buttonItem, surface.DesignContext.Services.Selection.PrimarySelection);
            });
        }
    }
}
