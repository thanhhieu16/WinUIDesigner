using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>The settings store and the recent-files list. Neither touches WinUI.</summary>
    [TestFixture]
    public class RecentFileListTests
    {
        string store;

        [SetUp]
        public void NewStore()
        {
            store = Path.Combine(Path.GetTempPath(), "wd-recent-" + Guid.NewGuid().ToString("N"), "settings.json");
        }

        [TearDown]
        public void DeleteStore()
        {
            var directory = Path.GetDirectoryName(store);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        [Test]
        public void SettingsRoundTripThroughTheFile()
        {
            var settings = new ShellSettings();
            settings.RecentFiles.Add(@"C:\a\One.xaml");
            settings.RecentFiles.Add(@"C:\a\Two.xaml");

            Assert.That(settings.Save(store), Is.True, "the folder is created on the first save");
            Assert.That(ShellSettings.Load(store).RecentFiles, Is.EqualTo(new[] { @"C:\a\One.xaml", @"C:\a\Two.xaml" }));
            Assert.That(File.Exists(store + ".tmp"), Is.False, "the temporary file replaces the target");
        }

        [Test]
        public void AMissingSettingsFileLoadsEmpty()
        {
            Assert.That(ShellSettings.Load(store).RecentFiles, Is.Empty);
        }

        [Test]
        public void AMalformedSettingsFileLoadsEmptyWithoutThrowing()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(store));
            File.WriteAllText(store, "{ this is not json");

            Assert.That(ShellSettings.Load(store).RecentFiles, Is.Empty);

            File.WriteAllText(store, "{\"RecentFiles\": null}");
            Assert.That(ShellSettings.Load(store).RecentFiles, Is.Empty);
        }

        [Test]
        public void AddPutsAnEntryFirstAndKeepsItInTheFile()
        {
            var list = new RecentFileList(store);
            int changes = 0;
            list.Changed += (s, e) => changes++;

            list.Add(@"C:\a\One.xaml");
            list.Add(@"C:\a\Two.xaml");

            Assert.That(list.Items, Is.EqualTo(new[] { @"C:\a\Two.xaml", @"C:\a\One.xaml" }));
            Assert.That(changes, Is.EqualTo(2));
            Assert.That(new RecentFileList(store).Items, Is.EqualTo(list.Items), "every change is saved at once");
        }

        [Test]
        public void AddingAListedFileMovesItUpRatherThanDuplicatingIt()
        {
            var list = new RecentFileList(store);
            list.Add(@"C:\a\One.xaml");
            list.Add(@"C:\a\Two.xaml");

            list.Add(@"c:\A\one.XAML");

            Assert.That(list.Items, Is.EqualTo(new[] { @"c:\A\one.XAML", @"C:\a\Two.xaml" }));
        }

        [Test]
        public void TheListKeepsTenEntriesAndDropsTheOldest()
        {
            var list = new RecentFileList(store);
            for (int i = 0; i <= RecentFileList.Capacity; i++)
                list.Add(@"C:\a\File" + i + ".xaml");

            Assert.That(list.Items.Count, Is.EqualTo(RecentFileList.Capacity));
            Assert.That(list.Items.First(), Is.EqualTo(@"C:\a\File10.xaml"));
            Assert.That(list.Items, Does.Not.Contain(@"C:\a\File0.xaml"));
        }

        [Test]
        public void RemoveAndClearAreSaved()
        {
            var list = new RecentFileList(store);
            list.Add(@"C:\a\One.xaml");
            list.Add(@"C:\a\Two.xaml");

            list.Remove(@"C:\A\ONE.xaml");
            Assert.That(new RecentFileList(store).Items, Is.EqualTo(new[] { @"C:\a\Two.xaml" }));

            list.Clear();
            Assert.That(new RecentFileList(store).Items, Is.Empty);
        }
    }
}
