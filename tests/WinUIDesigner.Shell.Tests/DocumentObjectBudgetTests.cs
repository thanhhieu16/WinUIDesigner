using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// Bounds what one document open costs, by counting the design-time objects the load path
    /// builds rather than by measuring process memory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This fixture replaces the byte-slope soak test Phase 5 shipped. That test measured
    /// private-byte growth per open/dispose cycle against a 4,700 byte ceiling, was red by
    /// design, and reported roughly 73,000 bytes per cycle - which the spec attributed to the
    /// engine's own load path and told the next reader to start looking for in
    /// <c>DesignSurface.UnloadDesigner()</c>. That attribution was wrong, and the measurements
    /// that disproved it are worth recording here so nobody repeats them:
    /// </para>
    /// <para>
    /// Bisecting the load path gave 5,448 bytes/iteration for a bare <c>XamlParser.Parse</c>,
    /// 8,617 with a full <c>XamlDesignContext</c> on top, 6,400 for the whole service set plus a
    /// <c>DesignPanel</c> but no extensions, and 42,752 once the Designer assembly's extensions
    /// attach - so roughly two thirds of the total arrived with the extensions. Stripping every
    /// line of designer code away then reproduced the same growth from nothing but WinUI:
    /// constructing and dropping ten <c>Button</c>s per iteration cost 14,981 bytes/iteration,
    /// ten <c>Border</c>s 9,288, ten <c>SolidColorBrush</c>es 4,296, and ten
    /// <c>Windows.Foundation.Collections.ValueSet</c>s (a plain WinRT runtime class, not a
    /// <c>DependencyObject</c>) nothing at all. Constructing any <c>DependencyObject</c> in a
    /// Windows App SDK 1.7.250606001 process commits native memory that is never returned:
    /// about 430 bytes for a brush, 900 for a Border or Grid, 1,500 for a Button.
    /// </para>
    /// <para>
    /// Four explanations were tested and ruled out, not assumed: the extension teardown retains
    /// nothing (after <c>UnloadDesigner</c>, weak references to the <c>DesignContext</c>, the
    /// root <c>DesignItem</c>, its view and all five of its extensions were all dead); the
    /// soak test's own recorded confound is not the cause (giving each iteration its own
    /// dispatcher turn measured 15,068 bytes/iteration against 14,981 for the unpumped loop);
    /// neither is the absence of a rendering window (with the shared live window created first,
    /// 9,042 against 9,288); and nothing roots the objects on the managed side, unlike
    /// microsoft-ui-xaml#4005 (20 bare <c>Border</c>s, 0 alive after a full collect). Nor does
    /// it plateau: 5,000 iterations of ten Borders held a slope of 15,499 then 17,575 then
    /// 16,939 bytes/iteration while private bytes went from 37 MB to 113 MB.
    /// </para>
    /// <para>
    /// So a byte budget for a document open cannot be written in this port: its floor is set by
    /// the platform and scales with however many <c>DependencyObject</c>s a document contains.
    /// What this port does control is how many design-time objects it builds per open, and that
    /// is what the tests below pin. They also cover, without any dependence on process memory,
    /// the thing the byte test was actually hunting: state surviving a document's disposal.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class DocumentObjectBudgetTests
    {
        // Small and fixed on purpose: this measures the constant per-open cost, not the
        // per-attribute one, and three components is enough to cover a container, a templated
        // child and a bare one.
        const string Fixture =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"150\">" +
            "<Button x:Name=\"A\" Width=\"60\" Height=\"24\" Canvas.Left=\"10\" Canvas.Top=\"10\" />" +
            "<TextBlock x:Name=\"B\" Text=\"x\" Canvas.Left=\"10\" Canvas.Top=\"50\" />" +
            "</Canvas>";

        const int Cycles = 50;

        /// <summary>
        /// The design-time objects one open of <see cref="Fixture"/> builds: every component in
        /// the tree, and the extensions attached to each. Formatted as one line so a regression
        /// reports what changed rather than just that a number moved.
        /// </summary>
        static string Budget(Document document)
        {
            var items = new List<DesignItem>();
            Collect(document.DesignContext.RootItem, items);

            return string.Join(" | ", items.Select(item =>
                item.ComponentType.Name + ": " + (item.Extensions.Any()
                    ? string.Join(", ", item.Extensions.Select(e => e.GetType().Name).OrderBy(n => n))
                    : "(none)")));
        }

        static void Collect(DesignItem item, List<DesignItem> into)
        {
            if (item == null || into.Any(seen => ReferenceEquals(seen, item)))
                return;
            into.Add(item);

            foreach (var property in item.Properties)
            {
                if (property.IsEvent)
                    continue;
                if (property.IsCollection)
                {
                    foreach (var child in property.CollectionElements)
                        Collect(child, into);
                }
                else if (property.IsSet)
                {
                    Collect(property.Value, into);
                }
            }
        }

        // The expected budget, written out rather than computed, so that adding an extension or
        // changing which types one applies to has to be acknowledged here. Extension names are
        // sorted, so this pins the set and not the order the ExtensionManager happens to yield.
        // The Canvas carries the three draw tools plus its own placement behaviour and the
        // container body-drag overlay; the Button gets the generic placement behaviour and, as a
        // ContentControl, the same overlay; a TextBlock is extended by nothing.
        const string ExpectedBudget =
            "Canvas: CanvasPlacementSupport, DrawLineExtension, DrawPathExtension, DrawPolyLineExtension, PanelMove"
            + " | Button: DefaultPlacementBehavior, PanelMove"
            + " | TextBlock: (none)";

        [Test]
        public void OpeningADocumentBuildsExactlyTheExpectedDesignTimeObjects()
        {
            WinUITestHost.Run(() =>
            {
                using (var document = new Document(null, Fixture))
                {
                    Assert.That(document.LoadFailed, Is.False, "the fixture should load");
                    Assert.That(Budget(document), Is.EqualTo(ExpectedBudget));
                }
            });
        }

        [Test]
        public void RepeatedOpenAndDisposeDoesNotGrowThePerDocumentBudget()
        {
            WinUITestHost.Run(() =>
            {
                for (int cycle = 0; cycle < Cycles; cycle++)
                {
                    using (var document = new Document(null, Fixture))
                    {
                        Assert.That(document.LoadFailed, Is.False, "cycle " + cycle + " failed to load");
                        Assert.That(Budget(document), Is.EqualTo(ExpectedBudget),
                            "the budget drifted on cycle " + cycle
                            + ", so one document open is leaving state behind for the next");
                    }
                }
            });
        }

        sealed class Captured
        {
            internal WeakReference Context;
            internal WeakReference RootItem;
            internal WeakReference RootView;
            internal readonly List<KeyValuePair<string, WeakReference>> Extensions
                = new List<KeyValuePair<string, WeakReference>>();
        }

        // NoInlining, and every strong reference confined to this frame: a Debug build keeps
        // locals alive to the end of their method, so capturing and collecting in one method
        // would report everything as still reachable whether or not anything actually retained
        // it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static Captured CaptureThenDispose(Document document)
        {
            var captured = new Captured();
            var context = document.DesignContext;
            captured.Context = new WeakReference(context);
            captured.RootItem = new WeakReference(context.RootItem);
            captured.RootView = new WeakReference(context.RootItem.View);
            foreach (var extension in context.RootItem.Extensions)
                captured.Extensions.Add(new KeyValuePair<string, WeakReference>(
                    extension.GetType().Name, new WeakReference(extension)));

            document.Dispose();
            return captured;
        }

        [Test]
        public void DisposingADocumentLeavesNothingOfItReachable()
        {
            WinUITestHost.Run(() =>
            {
                var captured = CaptureThenDispose(new Document(null, Fixture));

                for (int i = 0; i < 4; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                GC.Collect();

                Assert.That(captured.Extensions, Is.Not.Empty, "the fixture should attach extensions");
                Assert.That(captured.Context.Target, Is.Null, "the DesignContext outlived Dispose");
                Assert.That(captured.RootItem.Target, Is.Null, "the root DesignItem outlived Dispose");
                Assert.That(captured.RootView.Target, Is.Null, "the root item's view outlived Dispose");

                var survivors = captured.Extensions
                    .Where(e => e.Value.Target != null)
                    .Select(e => e.Key)
                    .ToList();
                Assert.That(survivors, Is.Empty,
                    "these extensions outlived Dispose: " + string.Join(", ", survivors));
            });
        }
    }
}
