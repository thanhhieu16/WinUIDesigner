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

        [Test]
        [Explicit("Long-running: 2000 document loads. Run deliberately, not on every build.")]
        public void OpeningAndDisposingADocumentDoesNotGrowMemoryPerIteration()
        {
            WinUITestHost.Run(() =>
            {
                long baseline = 0;

                for (int i = 0; i < TotalIterations; i++)
                {
                    using (var doc = new Document(null, Fixture))
                    {
                        Assert.That(doc.LoadFailed, Is.False, "iteration " + i + " failed to load");
                    }

                    if (i == BaselineIteration)
                        baseline = SettledPrivateBytes();
                }

                long final = SettledPrivateBytes();
                long span = TotalIterations - BaselineIteration;
                double bytesPerIteration = (double)(final - baseline) / span;

                Console.WriteLine("SOAK baseline@" + BaselineIteration + " = " + baseline);
                Console.WriteLine("SOAK final@" + TotalIterations + " = " + final);
                Console.WriteLine("SOAK bytes/iteration = " + bytesPerIteration.ToString("F1"));

                // The first 500 iterations are excluded from the measurement on purpose: JIT,
                // the WinUI type cache and the toolbox's one-time assembly scan all inflate the
                // early numbers, and none of them recurs.
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
