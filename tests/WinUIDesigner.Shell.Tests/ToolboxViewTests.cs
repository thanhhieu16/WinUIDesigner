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
        public void DetachClearsAnArmedButUnplacedSelection()
        {
            WinUITestHost.Run(() =>
            {
                var first = new FakeToolService(new FakePointerTool());
                var view = new ToolboxView();
                view.Attach(first);

                // Arm a control and never place it, then swap documents underneath the toolbox.
                view.SelectForTest(typeof(Button));
                Assert.That(view.SelectedTypeForTest, Is.EqualTo(typeof(Button)));

                view.Detach();

                Assert.That(view.SelectedTypeForTest, Is.Null,
                    "Detach must clear the selection, or the entry stays highlighted against a " +
                    "tool service that no longer exists");

                // The incoming document's service starts at its own pointer tool. Clicking the
                // same entry again must arm it there. With a stale selection left behind, this
                // click lands on an already-selected row, raises no SelectionChanged, arms
                // nothing - and the toolbox is dead until the user picks a different control.
                // Decision 12's regression, across the document boundary this time.
                var second = new FakeToolService(new FakePointerTool());
                view.Attach(second);
                view.SelectForTest(typeof(Button));

                var armed = second.CurrentTool as WinUIDesigner.Designer.Services.CreateComponentTool;
                Assert.That(armed, Is.Not.Null,
                    "the same control must still be placeable in the document that follows a swap");
                Assert.That(armed.ComponentType, Is.EqualTo(typeof(Button)));
            });
        }

        [Test]
        public void DetachStopsListeningSoAClosedDocumentCannotDriveTheView()
        {
            WinUITestHost.Run(() =>
            {
                var closed = new FakeToolService(new FakePointerTool());
                var view = new ToolboxView();
                view.Attach(closed);
                view.SelectForTest(typeof(Button));

                view.Detach();

                // Detach now clears the selection itself (see the test above), so "the selection
                // survived" is no longer available as proof of unsubscription. This is: attach a
                // live service, arm against it, then raise CurrentToolChanged on the CLOSED one.
                // A leaked subscription would run OnCurrentToolChanged against the live service -
                // which sits at its pointer tool, so the guard inside it passes - and wipe the
                // selection the user just made in the document that is actually open.
                var live = new FakeToolService(new FakePointerTool());
                view.Attach(live);
                view.SelectForTest(typeof(Button));
                Assert.That(view.SelectedTypeForTest, Is.EqualTo(typeof(Button)));

                closed.CurrentTool = new FakePointerTool();

                Assert.That(view.SelectedTypeForTest, Is.EqualTo(typeof(Button)),
                    "Detach must unsubscribe: a closed document's tool service still drove the view");
            });
        }
    }
}
