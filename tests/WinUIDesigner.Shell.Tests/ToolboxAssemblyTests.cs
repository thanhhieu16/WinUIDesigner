using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// Adding a control assembly to the toolbox, using this test assembly as the library.
    /// </summary>
    /// <remarks>
    /// <c>Toolbox.Instance</c> is process-wide, so the tests here leave this assembly in it, and
    /// each one takes it out first when the point is to watch it go in.
    /// </remarks>
    [TestFixture]
    public class ToolboxAssemblyTests
    {
        static readonly string TestAssemblyName = typeof(TestBadge).Assembly.GetName().Name;

        static string TestAssemblyPath
        {
            get { return typeof(TestBadge).Assembly.Location; }
        }

        static string TempPath(string extension)
        {
            return Path.Combine(Path.GetTempPath(), "wd-toolbox-" + Guid.NewGuid().ToString("N") + extension);
        }

        [Test]
        public void AddingAnAssemblyListsItsControlsAndReportsTheOnesThatThrow()
        {
            WinUITestHost.Run(() =>
            {
                var toolbox = Toolbox.Instance;
                toolbox.RemoveAssemblyForTest(TestAssemblyName);

                var result = toolbox.AddAssembly(typeof(TestBadge).Assembly);

                Assert.That(result.AlreadyPresent, Is.False);
                Assert.That(result.Added, Is.Not.Null);
                Assert.That(result.Added.Controls.Select(c => c.Type), Does.Contain(typeof(TestBadge)));
                Assert.That(result.Added.Controls.Select(c => c.Type), Does.Not.Contain(typeof(ThrowingControl)));
                Assert.That(result.Skipped, Does.Contain(typeof(ThrowingControl).FullName));
                Assert.That(toolbox.AssemblyNodes.Last(), Is.SameAs(result.Added), "added after WinUI's group");

                var again = toolbox.AddAssembly(typeof(TestBadge).Assembly);
                Assert.That(again.AlreadyPresent, Is.True);
                Assert.That(again.Added, Is.Null);
                Assert.That(toolbox.AssemblyNodes.Count(n => n.Name == TestAssemblyName), Is.EqualTo(1));
            });
        }

        [Test]
        public void TheToolboxViewShowsAnAddedAssemblyAndKeepsItsFilter()
        {
            WinUITestHost.Run(() =>
            {
                var toolbox = Toolbox.Instance;
                toolbox.RemoveAssemblyForTest(TestAssemblyName);
                var view = new ToolboxView();
                view.Filter = "Badge";
                Assert.That(view.VisibleTypesForTest, Does.Not.Contain(typeof(TestBadge)));

                toolbox.AddAssembly(typeof(TestBadge).Assembly);

                Assert.That(view.VisibleTypesForTest, Is.EqualTo(new[] { typeof(TestBadge) }),
                    "the view follows the toolbox, and the filter still applies");
                view.Filter = string.Empty;
                Assert.That(view.VisibleTypesForTest, Does.Contain(typeof(Button)));
                Assert.That(view.VisibleTypesForTest.Last(), Is.EqualTo(typeof(TestBadge)),
                    "an added group comes after WinUI's");
            });
        }

        [Test]
        public void AddingFromAPathRemembersItAndAFileThatIsNotAnAssemblyIsReported()
        {
            WinUITestHost.Run(() =>
            {
                var settingsPath = TempPath(".json");
                var notAnAssembly = TempPath(".dll");
                try
                {
                    File.WriteAllText(notAnAssembly, "not an assembly");
                    var shell = new ShellModel(null, settingsPath);
                    string reported = null;
                    shell.ReportError += (s, e) => reported = e.Message;

                    Assert.That(shell.AddAssembly(notAnAssembly, true), Is.False);
                    Assert.That(reported, Does.Contain("is not a .NET assembly"));
                    Assert.That(ShellSettings.Load(settingsPath).ToolboxAssemblies, Is.Empty);

                    Toolbox.Instance.RemoveAssemblyForTest(TestAssemblyName);
                    reported = null;
                    Assert.That(shell.AddAssembly(TestAssemblyPath, true), Is.True);
                    Assert.That(reported, Does.Contain(typeof(ThrowingControl).FullName),
                        "the skipped control is named");
                    Assert.That(ShellSettings.Load(settingsPath).ToolboxAssemblies,
                        Is.EqualTo(new[] { Path.GetFullPath(TestAssemblyPath) }));

                    reported = null;
                    Assert.That(shell.AddAssembly(TestAssemblyPath, true), Is.True);
                    Assert.That(reported, Does.Contain("already in the toolbox"));
                    Assert.That(ShellSettings.Load(settingsPath).ToolboxAssemblies, Has.Count.EqualTo(1));
                }
                finally
                {
                    foreach (var path in new[] { settingsPath, notAnAssembly })
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                }
            });
        }

        [Test]
        public void ARememberedAssemblyThatIsGoneIsReportedOnceAndStaysRemembered()
        {
            WinUITestHost.Run(() =>
            {
                var window = new MainWindow();
                window.Content = null;
                var missing = TempPath(".dll");
                ShellSettings.Update(ShellSettings.DefaultPath, settings => settings.ToolboxAssemblies.Add(missing));
                try
                {
                    var errors = window.LoadRememberedAssemblies(new[] { missing, TestAssemblyPath });

                    Assert.That(errors.Select(e => e.Path), Is.EqualTo(new[] { missing }),
                        "only the missing one; an assembly already in the toolbox is quiet at startup");
                    Assert.That(ShellSettings.Load(ShellSettings.DefaultPath).ToolboxAssemblies, Does.Contain(missing));
                    Assert.That(Toolbox.Instance.AssemblyNodes.Any(n => n.Name == TestAssemblyName), Is.True);
                }
                finally
                {
                    ShellSettings.Update(ShellSettings.DefaultPath, settings => settings.ToolboxAssemblies.Remove(missing));
                }
            });
        }

        [Test]
        public void APlacedLibraryControlSavesAsAUsingNamespaceAndReloads()
        {
            WinUITestHost.Run(() =>
            {
                Toolbox.Instance.AddAssembly(typeof(TestBadge).Assembly);
                var window = new MainWindow();
                window.Content = null;
                var doc = window.ShellForTest.CurrentDocument;
                var root = doc.DesignContext.RootItem;

                var badge = doc.DesignContext.Services.Component.RegisterComponentForDesigner(new TestBadge());
                root.ContentProperty.CollectionElements.Add(badge);
                doc.UpdateXaml();
                var saved = doc.Text;

                Assert.That(saved, Does.Contain("\"using:WinUIDesigner.Shell.Tests\""), saved);
                Assert.That(saved, Does.Not.Contain("clr-namespace:"), saved);

                doc.Mode = DocumentMode.Xaml;
                doc.Text = saved;
                doc.Mode = DocumentMode.Design;
                var reloaded = doc.DesignContext.RootItem.ContentProperty.CollectionElements;
                Assert.That(reloaded.Single().Component, Is.InstanceOf<TestBadge>(), saved);
            });
        }

        [Test]
        public void ADropAddsOnlyDllsToTheToolbox()
        {
            var paths = MainWindow.AssemblyPathsIn(new[]
            {
                @"C:\a\Controls.dll", @"C:\a\More.DLL", @"C:\a\One.xaml", @"C:\a\dll", null, ""
            });

            Assert.That(paths, Is.EqualTo(new[] { @"C:\a\Controls.dll", @"C:\a\More.DLL" }));
        }
    }
}
