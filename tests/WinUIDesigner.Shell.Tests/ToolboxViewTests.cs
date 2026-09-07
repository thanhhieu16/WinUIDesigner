using System;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class ToolboxViewTests
    {
        /// <summary>
        /// Stands in for the design context's real IToolService so the test needs no window.
        /// </summary>
        sealed class FakeToolService : IToolService
        {
            ITool currentTool;

            public FakeToolService(ITool pointerTool)
            {
                PointerTool = pointerTool;
                currentTool = pointerTool;
            }

            public ITool PointerTool { get; private set; }

            public ITool CurrentTool
            {
                get { return currentTool; }
                set
                {
                    currentTool = value;
                    var handler = CurrentToolChanged;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                }
            }

            public event EventHandler CurrentToolChanged;
        }

        sealed class FakePointerTool : ITool
        {
            public Microsoft.UI.Input.InputCursor Cursor { get { return null; } }
            public void Activate(IDesignPanel designPanel) { }
            public void Deactivate(IDesignPanel designPanel) { }
        }

        [Test]
        public void SelectingAControlArmsACreateComponentToolForThatType()
        {
            WinUITestHost.Run(() =>
            {
                var tools = new FakeToolService(new FakePointerTool());
                var view = new ToolboxView();
                view.Attach(tools);

                view.SelectForTest(typeof(Button));

                var created = tools.CurrentTool as WinUIDesigner.Designer.Services.CreateComponentTool;
                Assert.That(created, Is.Not.Null, "selecting a toolbox entry must arm a CreateComponentTool");
                Assert.That(created.ComponentType, Is.EqualTo(typeof(Button)));
            });
        }

        [Test]
        public void TheSameControlCanBeArmedTwiceInARow()
        {
            WinUITestHost.Run(() =>
            {
                var pointer = new FakePointerTool();
                var tools = new FakeToolService(pointer);
                var view = new ToolboxView();
                view.Attach(tools);

                view.SelectForTest(typeof(Button));
                var first = tools.CurrentTool;
                Assert.That(first, Is.InstanceOf<WinUIDesigner.Designer.Services.CreateComponentTool>());
                Assert.That(view.SelectedTypeForTest, Is.EqualTo(typeof(Button)));

                // What the engine does after a placement completes.
                tools.CurrentTool = pointer;

                // The mechanism under test, asserted directly: the reset must clear the list's
                // selection. Without this, SelectForTest below is a click on an entry that is
                // already selected - it raises no SelectionChanged, so nothing re-arms and the
                // toolbox is stuck after one placement. Spec Decision 12.
                Assert.That(view.SelectedTypeForTest, Is.Null,
                    "resetting to the pointer tool must clear the toolbox selection");

                view.SelectForTest(typeof(Button));
                var second = tools.CurrentTool;

                Assert.That(second, Is.InstanceOf<WinUIDesigner.Designer.Services.CreateComponentTool>());
                Assert.That(second, Is.Not.SameAs(first), "a second placement needs a fresh tool");
            });
        }

        [Test]
        public void DetachStopsListeningSoAClosedDocumentCannotDriveTheView()
        {
            WinUITestHost.Run(() =>
            {
                var pointer = new FakePointerTool();
                var tools = new FakeToolService(pointer);
                var view = new ToolboxView();
                view.Attach(tools);
                view.SelectForTest(typeof(Button));
                Assert.That(view.SelectedTypeForTest, Is.EqualTo(typeof(Button)));

                view.Detach();

                // While attached, this reset clears the selection (see the test above). After
                // Detach the handler is gone, so the selection must survive it untouched - the
                // observable proof that a closed document's tool service can no longer drive
                // the view.
                tools.CurrentTool = pointer;

                Assert.That(view.SelectedTypeForTest, Is.EqualTo(typeof(Button)),
                    "Detach must unsubscribe: a detached tool service still cleared the selection");
            });
        }
    }
}
