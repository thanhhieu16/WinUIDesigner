using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// What the shell keeps between runs: one JSON file under the user's local app data.
    /// </summary>
    /// <remarks>
    /// Not ApplicationData.Current, which throws for an unpackaged app, and not the reference's
    /// Settings.Default (a WPF ApplicationSettingsBase), whose nearest .NET equivalent writes
    /// user.config under an evidence-hash path nobody can find to reset. Recent-files spec
    /// Decision 1.
    /// </remarks>
    public sealed class ShellSettings
    {
        /// <summary>
        /// Where the window's settings live. Replaced by the Shell test assembly's setup fixture,
        /// so constructing a MainWindow in a test never touches the settings of whoever runs the
        /// suite. Recent-files spec Decision 6.
        /// </summary>
        internal static string DefaultPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinUIDesigner", "settings.json");

        public List<string> RecentFiles { get; set; } = new List<string>();

        /// <summary>Full paths of the assemblies added to the toolbox, loaded again at startup.
        /// Toolbox-assemblies spec Decision 5.</summary>
        public List<string> ToolboxAssemblies { get; set; } = new List<string>();

        /// <summary>The main window's last bounds, or null before it has ever closed.
        /// Window-placement spec Decision 1.</summary>
        public WindowBounds Window { get; set; }

        /// <summary>Loads the file at <paramref name="path"/>, applies <paramref name="change"/>, and
        /// saves it, so a change to one setting keeps the others as they are on disk.</summary>
        public static void Update(string path, Action<ShellSettings> change)
        {
            var settings = Load(path);
            change(settings);
            settings.Save(path);
        }

        /// <summary>
        /// The settings at <paramref name="path"/>, or empty settings when the file is missing,
        /// unreadable or malformed. Never throws: a settings file must not stop the app starting.
        /// </summary>
        public static ShellSettings Load(string path)
        {
            try
            {
                if (path == null || !File.Exists(path))
                    return new ShellSettings();
                var settings = JsonSerializer.Deserialize<ShellSettings>(File.ReadAllText(path));
                if (settings == null)
                    return new ShellSettings();
                if (settings.RecentFiles == null)
                    settings.RecentFiles = new List<string>();
                if (settings.ToolboxAssemblies == null)
                    settings.ToolboxAssemblies = new List<string>();
                return settings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ShellSettings] could not load " + path + ": " + ex.Message);
                return new ShellSettings();
            }
        }

        /// <summary>
        /// Writes to a temporary file beside <paramref name="path"/> and then replaces it, so a
        /// crash mid-write leaves the previous file whole. Returns false, and only traces, when
        /// the write fails: a lost recent-files entry is not worth an error dialog.
        /// </summary>
        public bool Save(string path)
        {
            if (path == null)
                return false;
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, path, true);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ShellSettings] could not save " + path + ": " + ex.Message);
                return false;
            }
        }
    }
}
