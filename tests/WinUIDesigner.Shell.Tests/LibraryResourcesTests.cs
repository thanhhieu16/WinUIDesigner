using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// Loading a library's theme from its library layout, with this test assembly as the library
    /// and a temporary folder as the place its .dll would sit.
    /// </summary>
    [TestFixture]
    public class LibraryResourcesTests
    {
        const string Theme =
            "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:local=\"using:WinUIDesigner.Shell.Tests\">" +
            "<Style TargetType=\"local:TestCard\"><Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"local:TestCard\"><Border Width=\"40\" Height=\"20\" /></ControlTemplate>" +
            "</Setter.Value></Setter></Style></ResourceDictionary>";

        string folder;
        string assemblyPath;
        int mergedBefore;

        [SetUp]
        public void NewLayout()
        {
            folder = Path.Combine(Path.GetTempPath(), "wd-layout-" + Guid.NewGuid().ToString("N"));
            assemblyPath = Path.Combine(folder, typeof(TestCard).Assembly.GetName().Name + ".dll");
            Directory.CreateDirectory(folder);
        }

        [TearDown]
        public void Clean()
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        void WriteTheme(string xaml)
        {
            var path = LibraryResources.ThemePath(typeof(TestCard).Assembly, assemblyPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, xaml);
        }

        void RemoveWhatWasMerged()
        {
            var merged = Application.Current.Resources.MergedDictionaries;
            while (merged.Count > mergedBefore)
                merged.RemoveAt(merged.Count - 1);
        }

        /// <remarks>
        /// Asserts the merged dictionary, not a rendered TestCard. Putting a TestCard with the
        /// merged style into the shared window failed alone and, in the full suite, left the host
        /// to die with 0xC000027B in the next test (bisected on the runner): this process has
        /// neither XamlControlsResources nor a resources.pri. That the style gives a real library
        /// control its look is the walkthrough's to show, on the real app (steps 27 and 28).
        /// </remarks>
        [Test]
        public void ALibrarysThemeIsParsedAndMergedIntoTheApplication()
        {
            WinUITestHost.Run(() =>
            {
                mergedBefore = Application.Current.Resources.MergedDictionaries.Count;
                try
                {
                    WriteTheme(Theme);

                    Assert.That(LibraryResources.Load(typeof(TestCard).Assembly, assemblyPath), Is.Null);
                    var merged = Application.Current.Resources.MergedDictionaries;
                    Assert.That(merged.Count, Is.EqualTo(mergedBefore + 1));
                    var style = merged[merged.Count - 1].Values.OfType<Style>().SingleOrDefault();
                    Assert.That(style, Is.Not.Null, "the theme's implicit style is in the merged dictionary");
                    Assert.That(style.TargetType, Is.EqualTo(typeof(TestCard)),
                        "the library's type resolved in the theme");
                }
                finally
                {
                    RemoveWhatWasMerged();
                }
            });
        }

        [Test]
        public void ALibraryWithoutAThemeIsNotAProblem()
        {
            WinUITestHost.Run(() =>
            {
                mergedBefore = Application.Current.Resources.MergedDictionaries.Count;
                Assert.That(LibraryResources.Load(typeof(TestCard).Assembly, assemblyPath), Is.Null);
                Assert.That(Application.Current.Resources.MergedDictionaries.Count, Is.EqualTo(mergedBefore));
            });
        }

        [Test]
        public void AMalformedThemeIsReportedAndMergesNothing()
        {
            WinUITestHost.Run(() =>
            {
                mergedBefore = Application.Current.Resources.MergedDictionaries.Count;
                try
                {
                    WriteTheme("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Style");

                    Assert.That(LibraryResources.Load(typeof(TestCard).Assembly, assemblyPath), Is.Not.Null.And.Not.Empty);
                    Assert.That(Application.Current.Resources.MergedDictionaries.Count, Is.EqualTo(mergedBefore));
                }
                finally
                {
                    RemoveWhatWasMerged();
                }
            });
        }
    }
}
