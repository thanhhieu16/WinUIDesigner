using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// Drives <see cref="MainWindow"/>'s document lifecycle - open, activate, close, Close All,
    /// Save All - with the close and save decisions supplied instead of asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each test builds its own <c>MainWindow</c> and drops the window's content immediately.
    /// Constructing the window is safe; letting it own live content is not - a <c>TextBox</c> (the
    /// outline filter) fail-fasts a second live Window in this process with 0xC000027B, which is
    /// why <see cref="MainWindowInteractionTests"/> re-parents the shell tree into the shared
    /// window. Nothing here needs layout, so nothing here re-parents anything. The windows are
    /// left open: closing the process's last WinUI Window ends the dispatcher's message loop and
    /// kills every later test. This is the pattern the deleted <c>MainWindowCloseOrderingTests</c>
    /// used, which measured 0 deaths in 10 full-suite runs.
    /// </para>
    /// <para>
    /// Everything asserted here reads model state or an unrealized control's own properties -
    /// <c>Documents.Count</c>, <c>TabItems.Count</c>, <c>CurrentDocument</c>,
    /// <c>ContentHostForTest.Content</c>, <c>ViewFor(...)</c>, and the order a decision delegate
    /// was asked in. None of it needs an arranged visual tree, so none of it needs a host. That
    /// matters for more than tidiness: two mechanisms were measured killing this suite, and both
    /// are avoided by construction here. Disposing a <c>Document</c> whose view has been live in
    /// <see cref="MainWindowInteractionTests"/>'s shared window fails the host with 0xC000027B,
    /// and <c>ShellModel.Close</c> disposes - so every closing test had to leave that window. But
    /// building a second <c>MainWindow</c> and hosting it in the shared window instead made things
    /// far worse (~12% of full-suite runs dying, up to ~53%), because each such window commits
    /// another few hundred <c>DependencyObject</c>s' worth of native memory that is never returned
    /// and then drives real layout passes through them. Building the window without ever hosting
    /// it does neither.
    /// </para>
    /// <para>
    /// No theme keys are seeded here, unlike <see cref="MainWindowInteractionTests"/>: the keys
    /// only <c>XamlControlsResources</c> defines go missing during a layout pass, and nothing here
    /// runs one.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class MainWindowDocumentTests
    {
        /// <summary>A window that owns no live content, with the document its constructor opened.</summary>
        static MainWindow NewWindow()
        {
            var window = new MainWindow();
            window.Content = null;
            Assert.That(window.ShellForTest.CurrentDocument, Is.Not.Null,
                "the constructor opens a document");
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
        /// after it would be lost. Blocking on the task instead - which is what these tests did
        /// before, with <c>.Wait()</c> - would deadlock the run with no failing assertion if the
        /// path ever did suspend, because its continuations post back to the dispatcher thread the
        /// call is already occupying. Every delegate injected below returns an already-completed
        /// task and no other await is reached, so the task is already finished here. The assertion
        /// states that rather than assuming it.
        /// </remarks>
        static void Finished(Task task)
        {
            Assert.That(task.IsCompleted, Is.True,
                "this path is expected to complete synchronously; it now suspends, so the test " +
                "needs a dispatcher-aware way to await it rather than this assertion");
            task.GetAwaiter().GetResult();
        }

        // ---- tabs -----------------------------------------------------------

        [Test]
        public void OpeningASecondDocumentLeavesBothOpenWithATabEach()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindow();
                var shell = window.ShellForTest;
                var first = shell.CurrentDocument;
                var before = shell.Documents.Count;

                var second = shell.New();

                Assert.That(shell.Documents.Count, Is.EqualTo(before + 1),
                    "New must add, not replace - the tab strip is what makes both reachable");
                Assert.That(shell.CurrentDocument, Is.SameAs(second));
                Assert.That(window.TabStripForTest.TabItems.Count, Is.EqualTo(before + 1));
                Assert.That(window.ViewFor(first), Is.Not.Null);
                Assert.That(window.ViewFor(second), Is.Not.Null);
                Assert.That(window.ViewFor(first), Is.Not.SameAs(window.ViewFor(second)));
            });
        }

        [Test]
        public void ActivatingADocumentMakesItCurrentAndShowsItsView()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindow();
                var shell = window.ShellForTest;
                var first = shell.CurrentDocument;
                var second = shell.New();

                window.ActivateDocument(first);

                Assert.That(shell.CurrentDocument, Is.SameAs(first));
                Assert.That(window.ContentHostForTest.Content, Is.SameAs(window.ViewFor(first)));

                window.ActivateDocument(second);
                Assert.That(window.ContentHostForTest.Content, Is.SameAs(window.ViewFor(second)));
            });
        }

        [Test]
        public void ACancelledTabCloseLeavesTheTabAndTheDocumentInPlace()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindow();
                var shell = window.ShellForTest;
                var doc = shell.CurrentDocument;
                var before = shell.Documents.Count;

                Finished(window.RequestCloseAsync(doc,
                    d => Decision(proceed: false, clearDirtyBeforeClose: false)));

                Assert.That(shell.Documents.Count, Is.EqualTo(before));
                Assert.That(window.TabStripForTest.TabItems.Count, Is.EqualTo(before));
                Assert.That(window.ViewFor(doc), Is.Not.Null);
            });
        }

        /// <summary>
        /// The document is dirtied first so the Discard decision has something to discharge.
        /// </summary>
        /// <remarks>
        /// <c>shell.New()</c> produces a clean document, and against a clean one
        /// <c>RequestCloseAsync</c>'s <c>MarkSaved()</c> is a no-op: the whole guard could be
        /// deleted and this test would still pass. Dirty, deleting it means
        /// <c>ShellModel.Close</c> sees <c>IsDirty</c>, raises <c>AskSaveChanges</c>,
        /// <c>OnAskSaveChanges</c> answers Cancel, and <c>Close</c> returns false - so the user
        /// who clicked the tab's x and chose Discard gets a tab that does not close and no
        /// explanation. Verified by mutation: with <c>doc.MarkSaved()</c> removed from
        /// <c>RequestCloseAsync</c> this test fails on the document count.
        /// </remarks>
        [Test]
        public void ADiscardedTabCloseRemovesBothTheTabAndTheDocument()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindow();
                var shell = window.ShellForTest;
                var doomed = shell.New();
                doomed.MarkSuspect();
                Assert.That(doomed.IsDirty, Is.True, "a Discard decision needs something to discard");
                var before = shell.Documents.Count;

                Finished(window.RequestCloseAsync(doomed,
                    d => Decision(proceed: true, clearDirtyBeforeClose: true)));

                Assert.That(shell.Documents.Count, Is.EqualTo(before - 1));
                Assert.That(shell.Documents.Contains(doomed), Is.False);
                Assert.That(window.TabStripForTest.TabItems.Count, Is.EqualTo(before - 1));
                Assert.That(window.ViewFor(doomed), Is.Null,
                    "the view must be detached and forgotten with its document");
            });
        }

        /// <summary>
        /// DoD 1's "including for the document that is not current": <c>MainWindow</c> subscribes
        /// to every document's <c>PropertyChanged</c> on add, not only the current one's, so a
        /// background document's dirty marker reaches its own tab header.
        /// </summary>
        /// <remarks>
        /// Reads <c>TabViewItem.Header</c>, which is an ordinary property assignment - it needs no
        /// applied template and so survives this fixture's unhosted window. What the strip does
        /// with that header is the part no test here can see.
        /// </remarks>
        [Test]
        public void ABackgroundDocumentsTitleChangeUpdatesItsOwnTabHeader()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindow();
                var shell = window.ShellForTest;
                var background = shell.CurrentDocument;
                var current = shell.New();
                Assert.That(shell.CurrentDocument, Is.SameAs(current),
                    "the second document is current, so the first is a background one");

                var backgroundTab = window.TabFor(background);
                var currentTab = window.TabFor(current);
                Assert.That(backgroundTab, Is.Not.Null);
                Assert.That(currentTab, Is.Not.Null);

                var cleanHeader = (string)backgroundTab.Header;
                var currentHeader = (string)currentTab.Header;

                background.MarkSuspect();
                Assert.That((string)backgroundTab.Header, Is.EqualTo(background.Title));
                Assert.That((string)backgroundTab.Header, Is.EqualTo(cleanHeader + "*"),
                    "the dirty marker has to reach the tab of a document nobody is looking at");
                Assert.That((string)currentTab.Header, Is.EqualTo(currentHeader),
                    "and only that tab");

                background.MarkSaved();
                Assert.That((string)backgroundTab.Header, Is.EqualTo(cleanHeader));
            });
        }

        /// <summary>
        /// The window title follows the current document's own title changes and ignores a
        /// background document's - the path a save now relies on, since <c>SaveAsync</c> no longer
        /// repaints the title itself (it may be saving a background tab, under Save All).
        /// </summary>
        /// <remarks>
        /// <c>MarkSuspect</c> and <c>MarkSaved</c> flip <c>IsDirty</c> exactly as a save does, and
        /// raise the same <c>PropertyChanged("Title")</c>, without needing a file on disk.
        /// </remarks>
        [Test]
        public void TheWindowTitleFollowsOnlyTheCurrentDocumentsTitleChanges()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindow();
                var shell = window.ShellForTest;
                var background = shell.CurrentDocument;
                var current = shell.New();
                Assert.That(shell.CurrentDocument, Is.SameAs(current));

                var cleanTitle = current.Title + " - WinUI Designer";
                Assert.That(window.Title, Is.EqualTo(cleanTitle));

                background.MarkSuspect();
                Assert.That(window.Title, Is.EqualTo(cleanTitle),
                    "a background document going dirty must not repaint the window title");
                background.MarkSaved();
                Assert.That(window.Title, Is.EqualTo(cleanTitle),
                    "nor must saving it");

                current.MarkSuspect();
                Assert.That(window.Title, Is.EqualTo(current.Title + " - WinUI Designer"));
                Assert.That(window.Title, Does.Contain("*"), "the current document's dirty marker reaches the title");
                current.MarkSaved();
                Assert.That(window.Title, Is.EqualTo(cleanTitle), "and so does its save");
            });
        }

        /// <summary>DoD 4: Close, Close All and Save All are disabled with no document open.</summary>
        [Test]
        public void CloseCloseAllAndSaveAllAreDisabledWithNoDocumentOpen()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindow();
                var shell = window.ShellForTest;

                Assert.That(window.CloseItemForTest.IsEnabled, Is.True, "a document is open");
                Assert.That(window.CloseAllItemForTest.IsEnabled, Is.True);
                Assert.That(window.SaveAllItemForTest.IsEnabled, Is.True);

                foreach (var doc in shell.Documents.ToList())
                    Assert.That(shell.Close(doc), Is.True, "a clean document closes without asking");

                Assert.That(shell.CurrentDocument, Is.Null);
                Assert.That(window.CloseItemForTest.IsEnabled, Is.False);
                Assert.That(window.CloseAllItemForTest.IsEnabled, Is.False);
                Assert.That(window.SaveAllItemForTest.IsEnabled, Is.False);
            });
        }

        // ---- Close All / Save All -------------------------------------------

        /// <summary>
        /// Every document is dirtied first, for the reason
        /// <see cref="ADiscardedTabCloseRemovesBothTheTabAndTheDocument"/> gives: against clean
        /// documents <c>CloseAllAsync</c>'s discharge loop is a no-op and the guard it exists for
        /// is deletable. Verified by mutation: with <c>pair.Key.MarkSaved()</c> removed, the final
        /// "Documents is empty" assertion fails.
        /// </summary>
        [Test]
        public void CloseAllAsksEveryDocumentBeforeClosingAny()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindow();
                var shell = window.ShellForTest;
                shell.New();
                shell.New();
                var all = shell.Documents.ToList();
                Assert.That(all.Count, Is.GreaterThanOrEqualTo(3));

                foreach (var d in all)
                    d.MarkSuspect();

                var asked = new List<Document>();
                var closedWhileAsking = 0;

                Finished(window.CloseAllAsync(d =>
                {
                    asked.Add(d);
                    closedWhileAsking = all.Count - shell.Documents.Count;
                    return Decision(proceed: true, clearDirtyBeforeClose: true);
                }));

                Assert.That(asked.Count, Is.EqualTo(all.Count), "every document is asked");
                Assert.That(closedWhileAsking, Is.EqualTo(0),
                    "nothing may close until every document has answered - a Cancel halfway " +
                    "must leave the user with all of them, not half");
                Assert.That(shell.Documents, Is.Empty);
            });
        }

        [Test]
        public void ACancelPartWayThroughCloseAllClosesNothing()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindow();
                var shell = window.ShellForTest;
                shell.New();
                shell.New();
                var before = shell.Documents.Count;
                var seen = 0;

                Finished(window.CloseAllAsync(d =>
                {
                    seen++;
                    return Decision(proceed: seen < 2, clearDirtyBeforeClose: false);
                }));

                Assert.That(shell.Documents.Count, Is.EqualTo(before));
                Assert.That(window.TabStripForTest.TabItems.Count, Is.EqualTo(before));
            });
        }

        /// <summary>
        /// <c>shell.New()</c> produces a clean document, so SaveAllAsync's dirty-only filter would
        /// never reach the delegate without something being dirtied first - hence the two
        /// MarkSuspect() calls. What is under test is that Save All asks once per document and
        /// stops at the first one the user abandons.
        /// </summary>
        [Test]
        public void SaveAllAsksOncePerDocumentAndStopsWhenOneIsAbandoned()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindow();
                var shell = window.ShellForTest;
                var first = shell.New();
                var second = shell.New();
                first.MarkSuspect();
                second.MarkSuspect();

                var forcedAsk = new List<Document>();

                Finished(window.SaveAllAsync((doc, forceAsk) =>
                {
                    if (forceAsk)
                        forcedAsk.Add(doc);
                    // Leaving the document dirty is how a dismissed picker reports itself.
                    doc.MarkSuspect();
                    return Task.CompletedTask;
                }));

                Assert.That(forcedAsk.Count, Is.EqualTo(1),
                    "every document here is pathless, so each needs Save As - and the run " +
                    "stops at the first one the user abandons rather than queueing dialogs " +
                    "that ShowDialogAsync would drop on the floor");
            });
        }

        // ---- error strip -----------------------------------------------------

        /// <summary>
        /// Both "Collapsed" assertions here have to be reached from a row that was actually
        /// <c>Visible</c> first - <see cref="ErrorListView"/>'s constructor already leaves it
        /// <c>Collapsed</c>, so asserting that value straight off a freshly-opened, never-failed
        /// document would pass even with <c>errors.Show(...)</c> deleted outright from both
        /// branches of <c>WireCurrentDocument</c>. Driving a failed parse first (the same
        /// malformed-XAML dance <see cref="ADocumentThatFailedToParseFillsTheErrorRow"/> uses)
        /// and then a clean one, and again right before closing, makes each "Collapsed" assertion
        /// about the wiring actually clearing the row rather than about an untouched default.
        /// </summary>
        [Test]
        public void TheErrorRowIsCollapsedForACleanDocumentAndForNoDocumentAtAll()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindow();
                var shell = window.ShellForTest;
                var doc = shell.CurrentDocument;
                Assert.That(doc, Is.Not.Null);
                Assert.That(doc.LoadErrors, Is.Empty);

                const string malformed = "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
                const string wellFormed = "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />";

                // Populated branch, failing: proves errors.Show(wiredDocument.LoadErrors) can
                // make the row Visible at all.
                doc.Mode = DocumentMode.Xaml;
                doc.Text = malformed;
                doc.Mode = DocumentMode.Design;
                Assert.That(doc.LoadFailed, Is.True);
                Assert.That(window.ErrorsForTest.Visibility, Is.EqualTo(Visibility.Visible));

                // Populated branch, clean: proves the same call clears the row once the
                // document reparses without errors - this is the "clean document" half of the
                // test's name, now a real transition instead of an unexercised default.
                doc.Mode = DocumentMode.Xaml;
                doc.Text = wellFormed;
                doc.Mode = DocumentMode.Design;
                Assert.That(doc.LoadFailed, Is.False);
                Assert.That(window.ErrorsForTest.Visibility, Is.EqualTo(Visibility.Collapsed));

                // Drive it Visible once more before closing, so the final assertion is about
                // the no-document branch's errors.Show(null) actually clearing the row - not
                // about a row that happened to be empty already.
                doc.Mode = DocumentMode.Xaml;
                doc.Text = malformed;
                doc.Mode = DocumentMode.Design;
                Assert.That(window.ErrorsForTest.Visibility, Is.EqualTo(Visibility.Visible));

                foreach (var d in shell.Documents.ToList())
                {
                    d.MarkSaved();
                    shell.Close(d);
                }

                Assert.That(shell.CurrentDocument, Is.Null);
                Assert.That(window.ErrorsForTest.Visibility, Is.EqualTo(Visibility.Collapsed));
            });
        }

        [Test]
        public void ADocumentThatFailedToParseFillsTheErrorRow()
        {
            WinUITestHost.Run(() =>
            {
                var window = NewWindow();
                var shell = window.ShellForTest;
                var doc = shell.CurrentDocument;

                doc.Mode = DocumentMode.Xaml;
                doc.Text = "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
                doc.Mode = DocumentMode.Design;

                Assert.That(doc.LoadFailed, Is.True);
                Assert.That(window.ErrorsForTest.Visibility, Is.EqualTo(Visibility.Visible));
                var rows = (IReadOnlyList<XamlErrorNode>)window.ErrorsForTest.List.ItemsSource;
                Assert.That(rows, Is.Not.Empty);
            });
        }
    }
}
