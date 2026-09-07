using System;
using System.Threading.Tasks;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// Drives <c>MainWindow.ReplaceCurrentDocumentAsync</c> - the ordering New and Open share -
    /// with the close decision supplied instead of asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ordering is decide, create, discharge, close, and getting it wrong loses unsaved work:
    /// a Discard answer discharged before the replacement is known to exist leaves a document in
    /// front of the user whose dirty flag is already gone, so <c>ShellModel.IsSomethingDirty</c>
    /// never trips again and the close gate stays quiet while the edits go over the side. A final
    /// review rewrote it for exactly that reason, and until now nothing covered it: the decision
    /// came from <c>PrepareForCloseAsync</c>, which shows a <c>ContentDialog</c> and so cannot run
    /// in a test process. The internal overload takes the decision as a parameter, the way it
    /// already took <c>createReplacement</c>.
    /// </para>
    /// <para>
    /// Each test builds its own <c>MainWindow</c> and drops the window's content immediately.
    /// Constructing the window is safe; letting it own live content is not - a <c>TextBox</c> (the
    /// outline filter) fail-fasts a second live Window in this process with 0xC000027B, which is
    /// why <c>MainWindowInteractionTests</c> re-parents the shell tree into the shared window.
    /// Nothing here needs layout, so nothing here re-parents anything. The windows are left open:
    /// closing the process's last WinUI Window ends the dispatcher's message loop and kills every
    /// later test.
    /// </para>
    /// <para>
    /// Mutation-verified rather than argued: moving the <c>MarkSaved</c> call ahead of
    /// <c>createReplacement</c> - the exact defect the final review fixed - makes
    /// <see cref="AFailedReplacementLeavesTheOutgoingDocumentOpenAndStillDirty"/> fail.
    /// <see cref="ADiscardIsDischargedOnlyOnceTheReplacementExists"/> deliberately does not catch
    /// that mutation, because with the replacement succeeding both orders end identically; what it
    /// catches is <c>MarkSaved</c> being dropped altogether, which leaves <c>ShellModel.Close</c>
    /// refusing a dirty document.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class MainWindowCloseOrderingTests
    {
        static MainWindow NewWindowWithADocument()
        {
            var window = new MainWindow();
            window.Content = null;
            Assert.That(window.ShellForTest.CurrentDocument, Is.Not.Null,
                "the constructor opens a document");
            return window;
        }

        static MainWindow NewWindowWithADirtyDocument()
        {
            var window = NewWindowWithADocument();
            // The only public way to raise IsDirty without mutating the design tree. Its own
            // meaning ("dirty but suspect") is beside the point here; the close gate reads IsDirty.
            window.ShellForTest.CurrentDocument.MarkSuspect();
            Assert.That(window.ShellForTest.CurrentDocument.IsDirty, Is.True);
            return window;
        }

        static Task<MainWindow.CloseDecision> Decision(bool proceed, bool clearDirtyBeforeClose)
        {
            return Task.FromResult(new MainWindow.CloseDecision
            {
                Proceed = proceed,
                ClearDirtyBeforeClose = clearDirtyBeforeClose
            });
        }

        /// <summary>
        /// Observes a call that is expected not to suspend, and rethrows whatever it threw.
        /// </summary>
        /// <remarks>
        /// <c>WinUITestHost.Run</c> takes an <c>Action</c>, so an <c>async</c> lambda there would
        /// be <c>async void</c>: Run would return at the first suspension and every assertion
        /// after it would be lost. Blocking on the task instead would deadlock, because its
        /// continuations post back to the dispatcher thread this call is already occupying. Every
        /// await on this path completes synchronously - the injected decision is a completed task,
        /// and no other await is reached - so the task is already finished here. The assertion
        /// states that rather than assuming it: if the path ever does suspend, this fails loudly
        /// instead of quietly passing on a half-run call.
        /// </remarks>
        static void Finished(Task task)
        {
            Assert.That(task.IsCompleted, Is.True,
                "this path is expected to complete synchronously; it now suspends, so the test " +
                "needs a dispatcher-aware way to await it rather than this assertion");
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void ACancelledDecisionNeverBuildsAReplacement()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindowWithADirtyDocument();
                var shell = window.ShellForTest;
                var outgoing = shell.CurrentDocument;
                var built = false;

                Finished(window.ReplaceCurrentDocumentAsync(
                    () => { built = true; return true; },
                    doc => Decision(proceed: false, clearDirtyBeforeClose: false)));

                Assert.That(built, Is.False, "a cancelled close must not build a replacement");
                Assert.That(shell.CurrentDocument, Is.SameAs(outgoing));
                Assert.That(outgoing.IsDirty, Is.True, "cancelling must not discard the edits");
            });
        }

        [Test]
        public void AFailedReplacementLeavesTheOutgoingDocumentOpenAndStillDirty()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindowWithADirtyDocument();
                var shell = window.ShellForTest;
                var outgoing = shell.CurrentDocument;

                // The user answered Discard, and then the replacement failed to materialise - an
                // unreadable file, a template that will not load. This is the case the
                // final-review fix exists for.
                Finished(window.ReplaceCurrentDocumentAsync(
                    () => false,
                    doc => Decision(proceed: true, clearDirtyBeforeClose: true)));

                Assert.That(shell.CurrentDocument, Is.SameAs(outgoing),
                    "a failed replacement must leave the user the document they had");
                Assert.That(shell.Documents, Has.Member(outgoing));
                Assert.That(outgoing.IsDirty, Is.True,
                    "the Discard answer must not be discharged until the replacement exists, or " +
                    "the document is left in front of the user with nothing that will ever prompt " +
                    "about its unsaved edits again");
            });
        }

        [Test]
        public void ADiscardIsDischargedOnlyOnceTheReplacementExists()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindowWithADirtyDocument();
                var shell = window.ShellForTest;
                var outgoing = shell.CurrentDocument;

                Finished(window.ReplaceCurrentDocumentAsync(
                    () => shell.New() != null,
                    doc => Decision(proceed: true, clearDirtyBeforeClose: true)));

                // ShellModel.Close refuses a dirty document: it raises AskSaveChanges, whose
                // MainWindow handler answers Cancel because that event is synchronous and cannot
                // await a dialog. So the outgoing document having actually left the shell is
                // itself the proof that MarkSaved ran before Close, not after it.
                Assert.That(shell.Documents, Has.No.Member(outgoing),
                    "the outgoing document was not closed, so its dirty flag was still set when " +
                    "Close ran");
                Assert.That(outgoing.IsDirty, Is.False);
                Assert.That(shell.CurrentDocument, Is.Not.Null);
                Assert.That(shell.CurrentDocument, Is.Not.SameAs(outgoing));
            });
        }

        [Test]
        public void ACleanDocumentIsReplacedWithoutBeingMarkedAtAll()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindowWithADocument();
                var shell = window.ShellForTest;
                var outgoing = shell.CurrentDocument;
                Assert.That(outgoing.IsDirty, Is.False, "a freshly opened document is clean");

                // What PrepareForCloseAsync returns for a document that was never dirty, and for
                // one whose requested Save went through: proceed, and the caller owes nothing.
                Finished(window.ReplaceCurrentDocumentAsync(
                    () => shell.New() != null,
                    doc => Decision(proceed: true, clearDirtyBeforeClose: false)));

                Assert.That(shell.Documents, Has.No.Member(outgoing));
                Assert.That(shell.CurrentDocument, Is.Not.Null);
                Assert.That(shell.CurrentDocument, Is.Not.SameAs(outgoing));
            });
        }
    }
}
