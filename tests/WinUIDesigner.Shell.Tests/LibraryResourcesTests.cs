using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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

        [Test]
        public void ALibrarysThemeGivesItsTemplatedControlItsTemplate()
        {
            WinUITestHost.RunWithWindow(window =>
            {
                mergedBefore = Application.Current.Resources.MergedDictionaries.Count;
                try
                {
                    WriteTheme(Theme);

                    Assert.That(LibraryResources.Load(typeof(TestCard).Assembly, assemblyPath), Is.Null);
                    Assert.That(Application.Current.Resources.MergedDictionaries.Count, Is.EqualTo(mergedBefore + 1));

                    var card = new TestCard();
                    window.Content = card;
                    card.UpdateLayout();
                    Assert.That(VisualTreeHelper.GetChildrenCount(card), Is.EqualTo(1),
                        "the theme's implicit style applied its template");
                    Assert.That(card.ActualWidth, Is.EqualTo(40).Within(0.5));
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
