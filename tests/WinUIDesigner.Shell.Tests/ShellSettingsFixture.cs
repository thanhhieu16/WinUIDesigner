using System;
using System.IO;
using NUnit.Framework;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// Points every MainWindow this assembly constructs at a temporary settings file, so the suite
    /// never reads or rewrites the settings of whoever runs it. Recent-files spec Decision 6.
    /// </summary>
    [SetUpFixture]
    public class ShellSettingsFixture
    {
        string originalPath;
        string testPath;

        [OneTimeSetUp]
        public void UseATemporarySettingsFile()
        {
            originalPath = ShellSettings.DefaultPath;
            testPath = Path.Combine(Path.GetTempPath(), "wd-settings-" + Guid.NewGuid().ToString("N") + ".json");
            ShellSettings.DefaultPath = testPath;
        }

        [OneTimeTearDown]
        public void Restore()
        {
            ShellSettings.DefaultPath = originalPath;
            if (File.Exists(testPath))
                File.Delete(testPath);
        }
    }
}
