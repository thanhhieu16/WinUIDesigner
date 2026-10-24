using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using Windows.ApplicationModel.DataTransfer;
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
                // toolbox is stuck after one placement. Phase 5 Decision 12.
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
                // Phase 5 Decision 12's regression, across the document boundary this time.
                var second = new FakeToolService(new FakePointerTool());
                view.Attach(second);
                view.SelectForTest(typeof(Button));

                var armed = second.CurrentTool as WinUIDesigner.Designer.Services.CreateComponentTool;
                Assert.That(armed, Is.Not.Null,
                    "the same control must still be placeable in the document that follows a swap");
                Assert.That(armed.ComponentType, Is.EqualTo(typeof(Button)));
            });
        }

        /// <summary>
        /// Builds a view, optionally attaches it to <paramref name="tools"/> and optionally
        /// detaches it again, and returns a weak reference to it.
        /// </summary>
        /// <remarks>
        /// NoInlining, with every strong reference to the view confined to this frame: a Debug
        /// build keeps locals alive to the end of their method, so building and collecting in one
        /// method would report the view as reachable whether or not anything actually held it.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference BuildView(FakeToolService tools, bool attach, bool detach)
        {
            var view = new ToolboxView();
            if (attach)
            {
                view.Attach(tools);
                view.SelectForTest(typeof(Button));
            }
            if (detach)
                view.Detach();
            return new WeakReference(view);
        }

        static void CollectFully()
        {
            for (int i = 0; i < 4; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            GC.Collect();
        }

        // A leaked CurrentToolChanged subscription cannot be caught by driving the view, and the
        // test that used to try was vacuous for a structural reason worth writing down:
        // OnCurrentToolChanged ignores its sender and gates on whichever service the view is
        // attached to now, so an extra invocation from a closed service takes exactly the same
        // branch as one from the live service. Raising the event on a detached service therefore
        // proves nothing - the observable difference a leak makes is not behavioural but a
        // lifetime one. These two tests are a pair: the first asserts Detach releases the view,
        // and the second is its mutation control, showing the assertion has teeth because an
        // attached service really does hold the view alive.

        [Test]
        public void DetachUnsubscribesSoAClosedDocumentStopsHoldingTheView()
        {
            WinUITestHost.Run(() =>
            {
                var closed = new FakeToolService(new FakePointerTool());
                var view = BuildView(closed, attach: true, detach: true);

                CollectFully();

                Assert.That(view.Target, Is.Null,
                    "Detach must unsubscribe: a closed document's tool service still holds the " +
                    "toolbox view, so every document ever opened stays reachable through it");
                GC.KeepAlive(closed);
            });
        }

        [Test]
        public void AnAttachedToolServiceHoldsTheViewUntilDetach()
        {
            WinUITestHost.Run(() =>
            {
                var live = new FakeToolService(new FakePointerTool());
                var view = BuildView(live, attach: true, detach: false);

                CollectFully();

                Assert.That(view.Target, Is.Not.Null,
                    "an attached tool service is supposed to hold the view through its " +
                    "CurrentToolChanged subscription - without that, the test above proves nothing");
                GC.KeepAlive(live);
            });
        }

        [Test]
        public void EntriesCanBeDraggedOutButNotReorderedOrDroppedOnto()
        {
            WinUITestHost.Run(() =>
            {
                var list = new ToolboxView().ListForTest;
                Assert.That(list.CanDragItems, Is.True);
                Assert.That(list.CanReorderItems, Is.False);
                Assert.That(list.AllowDrop, Is.False);
            });
        }

        [Test]
        public void BeginningADragArmsTheDraggedTypeAndMarksTheDataPackage()
        {
            WinUITestHost.Run(() =>
            {
                var tools = new FakeToolService(new FakePointerTool());
                var view = new ToolboxView();
                view.Attach(tools);
                var data = new DataPackage();

                Assert.That(view.BeginDragForTest(typeof(Button), data), Is.True);

                var armed = tools.CurrentTool as WinUIDesigner.Designer.Services.CreateComponentTool;
                Assert.That(armed, Is.Not.Null, "a drag arms a CreateComponentTool exactly as a click does");
                Assert.That(armed.ComponentType, Is.EqualTo(typeof(Button)));
                Assert.That(data.RequestedOperation, Is.EqualTo(DataPackageOperation.Copy));
                object marker;
                Assert.That(data.Properties.TryGetValue(WinUIDesigner.Designer.Services.CreateComponentTool.ToolboxTypeKey, out marker), Is.True);
                Assert.That(marker, Is.EqualTo(typeof(Button).AssemblyQualifiedName));
            });
        }

        [Test]
        public void ADragWithNoAttachedToolServiceIsRefused()
        {
            WinUITestHost.Run(() =>
            {
                var view = new ToolboxView();
                var data = new DataPackage();

                Assert.That(view.BeginDragForTest(typeof(Button), data), Is.False,
                    "with no document, or one in XAML mode, there is nowhere for the drop to land");
                object marker;
                Assert.That(data.Properties.TryGetValue(WinUIDesigner.Designer.Services.CreateComponentTool.ToolboxTypeKey, out marker), Is.False);
            });
        }

        [Test]
        public void AnAbandonedDragDisarmsTheToolItArmed()
        {
            WinUITestHost.Run(() =>
            {
                var pointer = new FakePointerTool();
                var tools = new FakeToolService(pointer);
                var view = new ToolboxView();
                view.Attach(tools);

                view.BeginDragForTest(typeof(Button), new DataPackage());
                Assert.That(tools.CurrentTool, Is.Not.SameAs(pointer));

                view.CompleteDragForTest(DataPackageOperation.None);

                Assert.That(tools.CurrentTool, Is.SameAs(pointer),
                    "otherwise the next click on the surface places a control the user abandoned");
                Assert.That(view.SelectedTypeForTest, Is.Null);
            });
        }

        [Test]
        public void AnAbandonedDragLeavesAToolTheUserHasSinceChosenAlone()
        {
            WinUITestHost.Run(() =>
            {
                var pointer = new FakePointerTool();
                var tools = new FakeToolService(pointer);
                var view = new ToolboxView();
                view.Attach(tools);

                view.BeginDragForTest(typeof(Button), new DataPackage());
                view.SelectForTest(typeof(CheckBox));
                var chosen = tools.CurrentTool;

                view.CompleteDragForTest(DataPackageOperation.None);

                Assert.That(tools.CurrentTool, Is.SameAs(chosen));
            });
        }

        [Test]
        public void ACompletedDropLeavesTheToolAsTheDropLeftIt()
        {
            WinUITestHost.Run(() =>
            {
                var tools = new FakeToolService(new FakePointerTool());
                var view = new ToolboxView();
                view.Attach(tools);

                view.BeginDragForTest(typeof(Button), new DataPackage());
                var armed = tools.CurrentTool;

                // The real drop resets the tool itself (CreateComponentTool.HandleDrop); Copy
                // means the toolbox has nothing left to undo. Nothing resets it here, so the
                // armed tool surviving proves CompleteDrag did not touch it.
                view.CompleteDragForTest(DataPackageOperation.Copy);

                Assert.That(tools.CurrentTool, Is.SameAs(armed));
            });
        }
    }
}
