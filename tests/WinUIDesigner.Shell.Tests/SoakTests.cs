using System;
using System.Diagnostics;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class SoakTests
    {
        // Small and fixed on purpose: absolute memory is dominated by document size (Phase A
        // measured roughly 7 MB for one 1500-attribute document), and what this test looks for
        // is the constant per-load cost, not the per-attribute one.
        const string Fixture =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"150\">" +
            "<Button x:Name=\"A\" Width=\"60\" Height=\"24\" Canvas.Left=\"10\" Canvas.Top=\"10\" />" +
            "<TextBlock x:Name=\"B\" Text=\"x\" Canvas.Left=\"10\" Canvas.Top=\"50\" />" +
            "</Canvas>";

        const int TotalIterations = 2000;
        const int BaselineIteration = 500;

        static long SettledPrivateBytes()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var process = Process.GetCurrentProcess();
            process.Refresh();
            return process.PrivateMemorySize64;
        }

        // Read alongside SettledPrivateBytes, at the same two sampling points, so the report can
        // tell apart a genuine rooted-object leak from committed-segment retention: private bytes
        // includes GC segments the runtime keeps committed for reuse even after a full blocking
        // collection, so a private-bytes climb alone is consistent with either story. If this
        // slope tracks the private-bytes slope, objects are actually being retained; if this
        // stays flat while private bytes climbs, it is segment retention, not a leak.
        static long SettledManagedHeapBytes()
        {
            return GC.GetTotalMemory(true);
        }

        [Test]
        [Explicit("Long-running: 2000 document loads. Run deliberately, not on every build.")]
        public void OpeningAndDisposingADocumentDoesNotGrowMemoryPerIteration()
        {
            // Note for whoever root-causes this later: WinUITestHost.Run enqueues this entire
            // loop as a single dispatcher callback, so the dispatcher's message queue is never
            // pumped between iterations. Probably moot here - DesignSurface is never attached to
            // a live Window/XamlRoot in this test, so the parsed subtree likely never realizes
            // native composition visuals at all - but exactly the kind of confound that would
            // otherwise be rediscovered from scratch.
            WinUITestHost.Run(() =>
            {
                long baseline = 0;
                long managedBaseline = 0;

                for (int i = 0; i < TotalIterations; i++)
                {
                    using (var doc = new Document(null, Fixture))
                    {
                        Assert.That(doc.LoadFailed, Is.False, "iteration " + i + " failed to load");
                    }

                    if (i == BaselineIteration)
                    {
                        baseline = SettledPrivateBytes();
                        managedBaseline = SettledManagedHeapBytes();
                    }
                }

                long final = SettledPrivateBytes();
                long managedFinal = SettledManagedHeapBytes();
                long span = TotalIterations - BaselineIteration;
                double bytesPerIteration = (double)(final - baseline) / span;
                double managedBytesPerIteration = (double)(managedFinal - managedBaseline) / span;

                Console.WriteLine("SOAK baseline@" + BaselineIteration + " = " + baseline);
                Console.WriteLine("SOAK final@" + TotalIterations + " = " + final);
                Console.WriteLine("SOAK bytes/iteration = " + bytesPerIteration.ToString("F1"));
                Console.WriteLine("SOAK managed-heap baseline@" + BaselineIteration + " = " + managedBaseline);
                Console.WriteLine("SOAK managed-heap final@" + TotalIterations + " = " + managedFinal);
                Console.WriteLine("SOAK managed-heap bytes/iteration = " + managedBytesPerIteration.ToString("F1"));

                // The first 500 iterations are excluded from the measurement on purpose: JIT,
                // the WinUI type cache and the toolbox's one-time assembly scan all inflate the
                // early numbers, and none of them recurs.
                //
                // The assertion below is deliberately on private bytes, not the managed-heap
                // figure logged above: private bytes is the number this test exists to bound
                // (total process footprint), the managed-heap slope is diagnostic evidence for
                // telling a rooted-object leak apart from committed-segment retention, logged for
                // whoever reads the report - see SettledManagedHeapBytes.
                Assert.That(bytesPerIteration, Is.LessThan(SoakCeilingBytesPerIteration),
                    "memory grew " + bytesPerIteration.ToString("F1") + " bytes per open/dispose cycle");
            });
        }

        /// <summary>
        /// Measured on this machine at implementation time: two independent 2000-iteration runs
        /// gave 79934.8 and 85251.4 bytes per open/dispose cycle over iterations 500-2000 - both
        /// roughly 17x the ~4.7 KB per XamlReader.Load that Phase A measured, and reproducible,
        /// not noise. Per the task brief this ceiling is deliberately NOT raised to accommodate
        /// that number: 4700 is kept as the ceiling (the same bound Phase A found for a bare
        /// XamlReader.Load, already the known-worst baseline) so this test keeps failing and
        /// keeps reporting the real per-Document-cycle leak rather than laundering it into a
        /// passing test. See task-7-report.md for the finding.
        /// </summary>
        const double SoakCeilingBytesPerIteration = 4700;
    }
}
