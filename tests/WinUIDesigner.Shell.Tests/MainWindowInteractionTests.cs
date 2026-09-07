using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using Windows.System;
using WinUIDesigner.Designer;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// Drives a real, live <see cref="MainWindow"/> the way the interactive walkthrough would:
    /// through the menu items' UIA Invoke pattern and real keyboard focus, asserting on the
    /// document model and the live visual tree rather than on pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two theme brushes have to be seeded before the window is built. A process with no compiled
    /// XAML cannot resolve ms-appx:///, so XamlControlsResources throws (measured: COMException
    /// "Unspecified error") and the keys only it defines are missing. Almost every control in this
    /// window is unaffected - ListView, Button and TextBox take their templates from the
    /// framework's own built-in resources - but <see cref="MenuBar"/>'s default template
    /// references TabViewButtonBackground and TabViewScrollButtonBackground, and a missing key
    /// there fails the layout pass with an unhandled WinRT exception that takes the whole process
    /// down with 0xC000027B (STOWED_EXCEPTION). Seeding exactly those two keys is what makes an
    /// in-process MainWindow testable at all; the shipping app needs no such help because App.xaml
    /// gives it a real merged resources.pri.
    /// </para>
    /// <para>
    /// The window is created once and never closed - closing the last live WinUI Window ends the
    /// shared dispatcher's message loop and kills every later test, the same constraint
    /// <see cref="WinUITestHost"/> documents for its own shared window.
    /// </para>
    /// <para>
    /// Menu items are activated through their automation peer's Invoke pattern rather than by
    /// injecting keystrokes. That is not a shortcut around the real path: it IS the fallback path
    /// the framework itself takes for an accelerator whose handler declines, which is precisely
    /// the mechanism MainWindow.OnDeleteAcceleratorInvoked relies on.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class MainWindowInteractionTests
    {
        static MainWindow window;
        static MenuBar menuBar;
        static TextBox outlineFilter;
        static DesignSurface surface;
        static ToolboxView toolbox;

        static FrameworkElement shellContent;

        [OneTimeSetUp]
        public void CreateWindow()
        {
            WinUITestHost.RunOnWindow(host =>
            {
                Application.Current.Resources["TabViewButtonBackground"] = new SolidColorBrush(Colors.Transparent);
                Application.Current.Resources["TabViewScrollButtonBackground"] = new SolidColorBrush(Colors.Transparent);

                window = new MainWindow();
                shellContent = (FrameworkElement)window.Content;

                // The shell tree is hosted in the test host's shared window rather than in the
                // MainWindow's own one. A second live Window in this process is fine in general -
                // ListView, ToolboxView, OutlineViewControl, DesignSurface and ScrollViewer were
                // each measured going live in one - but a TextBox inside one fail-fasts the host
                // with 0xC000027B during the first layout pass, and this window has two of them
                // (the outline filter and the property grid's editors). In the already-warmed
                // shared island the same TextBox lays out normally.
                window.Content = null;
                host.Content = shellContent;

                // The property-grid pane comes out of the live tree for the duration of the
                // fixture. Its rows are built from TextBox and NumberBox editors whose styles live
                // in XamlControlsResources, which no process without a merged resources.pri can
                // load, so every selection change would fail with a bare E_FAIL carrying no
                // resource name to seed around. Detached, the very same Reload() runs fine
                // (measured), and nothing here asserts on the grid. Adding an ApplicationDefinition
                // to this test project to get a real PRI was tried and rejected: it fail-fasts the
                // host before any test runs, the same way a ProjectReference to one does.
                DetachPropertyGridPane();

                SeedingMissingThemeKeys(() => shellContent.UpdateLayout());

                menuBar = FindDescendant<MenuBar>(shellContent, x => true);
                outlineFilter = FindDescendant<TextBox>(shellContent, t => t.PlaceholderText == "Filter");
                surface = FindDescendant<DesignSurface>(shellContent, x => true);
                toolbox = FindDescendant<ToolboxView>(shellContent, x => true);
            });

            Assert.IsNotNull(menuBar, "MenuBar should be in the live tree");
            Assert.IsNotNull(outlineFilter, "the outline filter TextBox should be in the live tree");
            Assert.IsNotNull(surface, "the DesignSurface should be in the live tree");
            Assert.IsNotNull(toolbox, "the ToolboxView should be in the live tree");
            TestContext.WriteLine("seeded theme keys (" + seededKeys.Count + "): " + string.Join(", ", seededKeys));
        }

        /// <summary>
        /// Takes the property-grid pane out of the live tree. Runs before every test body, not
        /// once in setup: MainWindow puts a freshly built PropertyGridControl back into its host
        /// whenever it wires a document, so a single detach in setup does not stay detached.
        /// </summary>
        static void DetachPropertyGridPane()
        {
            var host = FindDescendant<ContentControl>(shellContent,
                c => c.Content is WinUIDesigner.Designer.PropertyGrid.PropertyGridControl);
            if (host != null)
                host.Content = null;
        }

        /// <summary>Runs the body on the dispatcher against the live shell tree.</summary>
        /// <remarks>
        /// Re-finds the live <see cref="DesignSurface"/> before every test body, not only in
        /// <see cref="CreateWindow"/>. Task 6's CloseAllAsync can close the very document `surface`
        /// was captured from in OneTimeSetUp - ShellModel.Close disposes it, which tears its
        /// DesignContext down for good - so a fixture-wide static snapshot taken once would go
        /// stale for every test that runs afterward, in whatever order NUnit happens to pick.
        /// Re-finding here, inside the same SeedingMissingThemeKeys-guarded layout pass this
        /// already ran, keeps `surface` pointed at whatever document is actually current without
        /// adding a second, unguarded layout call of its own.
        /// </remarks>
        static void WithShell(Action body)
        {
            WinUITestHost.RunOnWindow(host =>
            {
                if (!ReferenceEquals(host.Content, shellContent))
                    host.Content = shellContent;
                DetachPropertyGridPane();
                SeedingMissingThemeKeys(() =>
                {
                    shellContent.UpdateLayout();
                    var liveSurface = FindDescendant<DesignSurface>(shellContent, x => true);
                    if (liveSurface != null)
                        surface = liveSurface;
                    body();
                });
            });
        }

        static readonly Regex MissingResourceKey = new Regex(@"Cannot find a Resource with the Name/Key (\S+)");

        /// <summary>Keys this fixture had to define itself; see the class remarks for why.</summary>
        static readonly List<string> seededKeys = new List<string>();

        /// <summary>
        /// Runs <paramref name="body"/>, defining any theme key it turns out to be missing and
        /// retrying, so the exact set is discovered rather than guessed. The key name arrives in
        /// the COMException's Data["RestrictedDescription"], not in its Message, which is the
        /// unhelpful "No message provided".
        /// </summary>
        static void SeedingMissingThemeKeys(Action body)
        {
            for (int round = 0; round < 80; round++)
            {
                try
                {
                    body();
                    return;
                }
                catch (COMException ex)
                {
                    var text = ex.Message ?? string.Empty;
                    foreach (System.Collections.DictionaryEntry entry in ex.Data)
                        text += " | " + entry.Key + "=" + entry.Value;

                    var match = MissingResourceKey.Match(text);
                    if (!match.Success)
                        throw;

                    var key = match.Groups[1].Value;
                    Application.Current.Resources[key] = new SolidColorBrush(Colors.Transparent);
                    seededKeys.Add(key);
                }
            }
            throw new InvalidOperationException(
                "theme keys still missing after 80 rounds; seeded so far: " + string.Join(", ", seededKeys));
        }

        // ---- helpers -------------------------------------------------------

        static T FindDescendant<T>(DependencyObject from, Func<T, bool> predicate) where T : class
        {
            var typed = from as T;
            if (typed != null && predicate(typed))
                return typed;
            var count = VisualTreeHelper.GetChildrenCount(from);
            for (int i = 0; i < count; i++)
            {
                var found = FindDescendant(VisualTreeHelper.GetChild(from, i), predicate);
                if (found != null)
                    return found;
            }
            return null;
        }

        static MenuFlyoutItem MenuItem(string menuTitle, string itemText)
        {
            var bar = menuBar.Items.OfType<MenuBarItem>().FirstOrDefault(m => m.Title == menuTitle);
            Assert.IsNotNull(bar, "no '" + menuTitle + "' menu");
            var item = bar.Items.OfType<MenuFlyoutItem>().FirstOrDefault(m => m.Text == itemText);
            Assert.IsNotNull(item, "no '" + itemText + "' item under '" + menuTitle + "'");
            return item;
        }

        /// <summary>Activates a menu item exactly as the framework does for a declined accelerator.</summary>
        static void InvokeMenuItem(MenuFlyoutItem item)
        {
            var peer = new MenuFlyoutItemAutomationPeer(item);
            var invoke = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            Assert.IsNotNull(invoke, "MenuFlyoutItem should expose the Invoke pattern");
            invoke.Invoke();
        }

        static Canvas RootCanvas()
        {
            return (Canvas)surface.DesignContext.RootItem.Component;
        }

        static WinUIDesigner.DesignModel.DesignItem AddButtonToRoot()
        {
            var root = surface.DesignContext.RootItem;
            var item = surface.DesignContext.Services.Component.RegisterComponentForDesigner(new Button { Content = "X" });
            root.ContentProperty.CollectionElements.Add(item);
            return item;
        }

        static int ChildCount()
        {
            return surface.DesignContext.RootItem.ContentProperty.CollectionElements.Count;
        }

        // ---- item 3: the keyboard accelerators -----------------------------

        [Test]
        public void EveryDocumentedAcceleratorIsRegisteredOnItsMenuItem()
        {
            WithShell(() =>
            {
                var expected = new (string Menu, string Item, VirtualKey Key, VirtualKeyModifiers Mods)[]
                {
                    ("File", "New",     VirtualKey.N,      VirtualKeyModifiers.Control),
                    ("File", "Open...", VirtualKey.O,      VirtualKeyModifiers.Control),
                    ("File", "Save",    VirtualKey.S,      VirtualKeyModifiers.Control),
                    ("Edit", "Undo",    VirtualKey.Z,      VirtualKeyModifiers.Control),
                    ("Edit", "Redo",    VirtualKey.Y,      VirtualKeyModifiers.Control),
                    ("Edit", "Cut",     VirtualKey.X,      VirtualKeyModifiers.Control),
                    ("Edit", "Copy",    VirtualKey.C,      VirtualKeyModifiers.Control),
                    ("Edit", "Paste",   VirtualKey.V,      VirtualKeyModifiers.Control),
                    ("Edit", "Delete",  VirtualKey.Delete, VirtualKeyModifiers.None),
                };

                foreach (var e in expected)
                {
                    var item = MenuItem(e.Menu, e.Item);
                    Assert.AreEqual(1, item.KeyboardAccelerators.Count,
                        e.Item + " should carry exactly one accelerator");
                    var accelerator = item.KeyboardAccelerators[0];
                    Assert.AreEqual(e.Key, accelerator.Key, e.Item + " accelerator key");
                    Assert.AreEqual(e.Mods, accelerator.Modifiers, e.Item + " accelerator modifiers");
                }

                Assert.AreEqual(0, MenuItem("File", "Save As...").KeyboardAccelerators.Count,
                    "Save As... is deliberately unbound");
                Assert.AreEqual(0, MenuItem("File", "Exit").KeyboardAccelerators.Count,
                    "Exit is deliberately unbound");
            });
        }

        // ---- item 2: the menus are real and populated ----------------------

        [Test]
        public void BothMenusRenderAndCarryTheirItems()
        {
            WithShell(() =>
            {
                // No ActualHeight assertion: MenuBar's default style also lives in
                // XamlControlsResources, so it measures to zero in this host however many keys
                // are seeded. What it renders is the one thing here that still needs a human.
                // Structure only. What the MenuBar draws cannot be asserted in this process at
                // all - see CreateWindow - so whether the menus visually drop open is the one
                // item here that still needs a human at the keyboard.

                CollectionAssert.AreEqual(
                    new[] { "File", "Edit" },
                    menuBar.Items.OfType<MenuBarItem>().Select(m => m.Title).ToList());

                var file = menuBar.Items.OfType<MenuBarItem>().First();
                CollectionAssert.AreEqual(
                    new[] { "New", "Open...", "Close", "Close All", "Save", "Save As...", "Save All", "Exit" },
                    file.Items.OfType<MenuFlyoutItem>().Select(i => i.Text).ToList());

                var edit = menuBar.Items.OfType<MenuBarItem>().Last();
                CollectionAssert.AreEqual(
                    new[] { "Undo", "Redo", "Cut", "Copy", "Paste", "Delete" },
                    edit.Items.OfType<MenuFlyoutItem>().Select(i => i.Text).ToList());
            });
        }

        // ---- item 1: Delete while the caret sits in the outline filter ------

        /// <summary>
        /// Cannot run in this host, and is kept so the gap stays visible rather than merely
        /// absent. MainWindow.IsTextEntryFocused reads Content.XamlRoot, so the guard only has
        /// anything to say while the MainWindow owns its own live content - and a MainWindow that
        /// owns its own live content needs a second Window, which is exactly what a TextBox
        /// fail-fasts in this process. The two requirements are mutually exclusive here, measured
        /// both ways. Hosted in the shared window as the rest of this fixture is, Content is null,
        /// the guard returns false for every input, and this test would pass without proving
        /// anything. It is the one walkthrough item that still needs a human at the keyboard.
        /// </summary>
        [Test]
        [Explicit("Needs MainWindow to own live content; a TextBox in a second Window fail-fasts this host.")]
        public void DeleteLeavesTheSelectionAloneWhileTheOutlineFilterHasFocus()
        {
            WithShell(() =>
            {
                var added = AddButtonToRoot();
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { added });
                var before = ChildCount();

                Assert.IsTrue(outlineFilter.Focus(FocusState.Programmatic),
                    "the outline filter should take programmatic focus");

                InvokeMenuItem(MenuItem("Edit", "Delete"));

                Assert.AreEqual(before, ChildCount(),
                    "Delete must not remove a design item while a text box has the caret");
                CollectionAssert.Contains(
                    surface.DesignContext.RootItem.ContentProperty.CollectionElements.ToList(), added);

                surface.DesignContext.RootItem.ContentProperty.CollectionElements.Remove(added);
            });
        }

        /// <summary>
        /// Covers the Delete command path end to end - menu item, Click, DeleteSelection, the
        /// clipboard service, the document. It deliberately does NOT prove the focus guard: see
        /// <see cref="DeleteLeavesTheSelectionAloneWhileTheOutlineFilterHasFocus"/> for why that
        /// half cannot be exercised here.
        /// </summary>
        [Test]
        public void DeleteRemovesTheSelectionWhenFocusIsNotInATextBox()
        {
            WithShell(() =>
            {
                var added = AddButtonToRoot();
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { added });
                var before = ChildCount();

                toolbox.Focus(FocusState.Programmatic);

                InvokeMenuItem(MenuItem("Edit", "Delete"));

                Assert.AreEqual(before - 1, ChildCount(),
                    "Delete should remove the selected design item when no text box has the caret");
                CollectionAssert.DoesNotContain(
                    surface.DesignContext.RootItem.ContentProperty.CollectionElements.ToList(), added);
            });
        }

        // ---- item 6: undo and redo, driven from the Edit menu --------------

        [Test]
        public void UndoAndRedoFromTheMenuMoveTheDocumentBackAndForward()
        {
            WithShell(() =>
            {
                var context = surface.DesignContext;
                var root = context.RootItem;
                var before = ChildCount();

                using (var group = context.OpenGroup("add", new[] { root }))
                {
                    var item = context.Services.Component.RegisterComponentForDesigner(new Button { Content = "U" });
                    root.ContentProperty.CollectionElements.Add(item);
                    group.Commit();
                }
                Assert.AreEqual(before + 1, ChildCount(), "the edit should have applied");

                toolbox.Focus(FocusState.Programmatic);

                InvokeMenuItem(MenuItem("Edit", "Undo"));
                Assert.AreEqual(before, ChildCount(), "Undo should have reverted the edit");

                InvokeMenuItem(MenuItem("Edit", "Redo"));
                Assert.AreEqual(before + 1, ChildCount(), "Redo should have reapplied the edit");

                InvokeMenuItem(MenuItem("Edit", "Undo"));
                Assert.AreEqual(before, ChildCount());
            });
        }

        // ---- item 4: the toolbox arms a tool on the real design surface ----

        [Test]
        public void PickingAToolboxEntryArmsACreateToolOnTheLiveSurface()
        {
            WithShell(() =>
            {
                var tools = surface.DesignContext.Services.GetService<WinUIDesigner.DesignModel.IToolService>();
                Assert.AreSame(tools.PointerTool, tools.CurrentTool, "the surface should start on the pointer tool");

                toolbox.SelectForTest(typeof(Button));

                Assert.AreNotSame(tools.PointerTool, tools.CurrentTool,
                    "picking a toolbox entry should arm a create tool on the window's own tool service");

                tools.CurrentTool = tools.PointerTool;
            });
        }

        // ---- item 5: a model edit reaches the live canvas ------------------

        [Test]
        public void APropertyEditReachesTheLiveVisualTree()
        {
            WithShell(() =>
            {
                var added = AddButtonToRoot();
                var element = (Button)added.Component;
                RootCanvas().UpdateLayout();

                added.Properties["Width"].SetValue(123.0);
                RootCanvas().UpdateLayout();

                Assert.AreEqual(123.0, element.Width, 0.001, "the property edit should have reached the live element");
                Assert.AreEqual(123.0, element.ActualWidth, 0.5, "and a layout pass should have honoured it");

                surface.DesignContext.RootItem.ContentProperty.CollectionElements.Remove(added);
            });
        }

        // ---- tabs -----------------------------------------------------------

        /// <summary>
        /// Amendment A: asserts relative to the count found on entry, not an absolute number, and
        /// restores the document set and current document in a finally - this fixture builds one
        /// MainWindow in OneTimeSetUp and shares it across every test in the fixture, run in an
        /// order NUnit does not guarantee.
        /// </summary>
        [Test]
        public void OpeningASecondDocumentLeavesBothOpenWithATabEach()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                var first = shell.CurrentDocument;
                Assert.That(first, Is.Not.Null);
                var before = shell.Documents.Count;

                var second = shell.New();
                try
                {
                    Assert.That(shell.Documents.Count, Is.EqualTo(before + 1),
                        "New must add, not replace - the tab strip is what makes both reachable");
                    Assert.That(shell.CurrentDocument, Is.SameAs(second));
                    Assert.That(window.TabStripForTest.TabItems.Count, Is.EqualTo(before + 1));
                    Assert.That(window.ViewFor(first), Is.Not.Null);
                    Assert.That(window.ViewFor(second), Is.Not.Null);
                    Assert.That(window.ViewFor(first), Is.Not.SameAs(window.ViewFor(second)));
                }
                finally
                {
                    second.MarkSaved();
                    shell.Close(second);
                    window.ActivateDocument(first);
                }
            });
        }

        /// <summary>Amendment A: restores the document set and current document in a finally.</summary>
        [Test]
        public void ActivatingADocumentMakesItCurrentAndShowsItsView()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                var first = shell.CurrentDocument;
                var second = shell.New();

                try
                {
                    window.ActivateDocument(first);

                    Assert.That(shell.CurrentDocument, Is.SameAs(first));
                    Assert.That(window.ContentHostForTest.Content, Is.SameAs(window.ViewFor(first)));

                    window.ActivateDocument(second);
                    Assert.That(window.ContentHostForTest.Content, Is.SameAs(window.ViewFor(second)));
                }
                finally
                {
                    second.MarkSaved();
                    shell.Close(second);
                    window.ActivateDocument(first);
                }
            });
        }

        [Test]
        public void ACancelledTabCloseLeavesTheTabAndTheDocumentInPlace()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                var doc = shell.CurrentDocument;
                var before = shell.Documents.Count;

                window.RequestCloseAsync(doc,
                    d => Task.FromResult(new MainWindow.CloseDecision { Proceed = false })).Wait();

                Assert.That(shell.Documents.Count, Is.EqualTo(before));
                Assert.That(window.TabStripForTest.TabItems.Count, Is.EqualTo(before));
                Assert.That(window.ViewFor(doc), Is.Not.Null);
            });
        }

        [Test]
        public void ADiscardedTabCloseRemovesBothTheTabAndTheDocument()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                var doomed = shell.New();
                var before = shell.Documents.Count;

                window.RequestCloseAsync(doomed,
                    d => Task.FromResult(new MainWindow.CloseDecision
                    {
                        Proceed = true,
                        ClearDirtyBeforeClose = true
                    })).Wait();

                Assert.That(shell.Documents.Count, Is.EqualTo(before - 1));
                Assert.That(shell.Documents.Contains(doomed), Is.False);
                Assert.That(window.TabStripForTest.TabItems.Count, Is.EqualTo(before - 1));
                Assert.That(window.ViewFor(doomed), Is.Null,
                    "the view must be detached and forgotten with its document");
            });
        }

        // ---- Close All / Save All -------------------------------------------

        /// <summary>
        /// Amendment C3: this test empties the shared document set. Restores it in a finally so
        /// later tests in this fixture - run in an order NUnit does not guarantee - still find a
        /// current document.
        /// </summary>
        [Test]
        public void CloseAllAsksEveryDocumentBeforeClosingAny()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                shell.New();
                shell.New();
                var all = shell.Documents.ToList();
                Assert.That(all.Count, Is.GreaterThanOrEqualTo(3));

                var asked = new List<Document>();
                var closedWhileAsking = 0;

                try
                {
                    window.CloseAllAsync(d =>
                    {
                        asked.Add(d);
                        closedWhileAsking = all.Count - shell.Documents.Count;
                        return Task.FromResult(new MainWindow.CloseDecision
                        {
                            Proceed = true,
                            ClearDirtyBeforeClose = true
                        });
                    }).Wait();

                    Assert.That(asked.Count, Is.EqualTo(all.Count), "every document is asked");
                    Assert.That(closedWhileAsking, Is.EqualTo(0),
                        "nothing may close until every document has answered - a Cancel halfway " +
                        "must leave the user with all of them, not half");
                    Assert.That(shell.Documents, Is.Empty);
                }
                finally
                {
                    // CloseAllAsync closed every document this test found, including the one the
                    // fixture built in OneTimeSetUp - the one `surface` was originally captured
                    // from. Document.Dispose tears its DesignContext down for good, so a merely-new
                    // document left behind is not enough on its own; WithShell re-finds the live
                    // DesignSurface before every test body runs (see its remarks) specifically so
                    // this does not leave the rest of the fixture holding a dead reference.
                    if (shell.Documents.Count == 0)
                        shell.New();
                }
            });
        }

        /// <summary>Amendment C3: restores the two documents this test creates in a finally.</summary>
        [Test]
        public void ACancelPartWayThroughCloseAllClosesNothing()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                var first = shell.New();
                var second = shell.New();
                var before = shell.Documents.Count;
                var seen = 0;

                try
                {
                    window.CloseAllAsync(d =>
                    {
                        seen++;
                        return Task.FromResult(new MainWindow.CloseDecision { Proceed = seen < 2 });
                    }).Wait();

                    Assert.That(shell.Documents.Count, Is.EqualTo(before));
                    Assert.That(window.TabStripForTest.TabItems.Count, Is.EqualTo(before));
                }
                finally
                {
                    first.MarkSaved();
                    second.MarkSaved();
                    shell.Close(first);
                    shell.Close(second);
                }
            });
        }

        /// <summary>
        /// Amendment C1/C2/C3: <c>shell.New()</c> produces a clean document, so SaveAllAsync's
        /// dirty-only filter would never reach the delegate against the brief's original setup -
        /// this test dirties two documents of its own with MarkSuspect() first. The
        /// order-dependent "more than one document open" assertion is dropped (C2); what remains
        /// is the test's actual subject, that Save All asks once and stops at the first document
        /// the user abandons. Restores both documents in a finally (C3).
        /// </summary>
        [Test]
        public void SaveAllAsksOncePerDocumentAndStopsWhenOneIsAbandoned()
        {
            WithShell(() =>
            {
                var shell = window.ShellForTest;
                var first = shell.New();
                var second = shell.New();
                first.MarkSuspect();
                second.MarkSuspect();

                var forcedAsk = new List<Document>();

                try
                {
                    window.SaveAllAsync((doc, forceAsk) =>
                    {
                        if (forceAsk)
                            forcedAsk.Add(doc);
                        // Leaving the document dirty is how a dismissed picker reports itself.
                        doc.MarkSuspect();
                        return Task.CompletedTask;
                    }).Wait();

                    Assert.That(forcedAsk.Count, Is.EqualTo(1),
                        "every document here is pathless, so each needs Save As - and the run " +
                        "stops at the first one the user abandons rather than queueing dialogs " +
                        "that ShowDialogAsync would drop on the floor");
                }
                finally
                {
                    first.MarkSaved();
                    second.MarkSaved();
                    shell.Close(first);
                    shell.Close(second);
                }
            });
        }
    }
}
