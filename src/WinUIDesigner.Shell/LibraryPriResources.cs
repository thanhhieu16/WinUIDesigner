using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Windows.ApplicationModel.Resources;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// Resolves the resources the XAML framework cannot find in the app's own resources.pri
    /// from the .pri files of libraries added at runtime: a templated control's
    /// Themes/Generic.xaml (as its compiled .xbf), and anything else a library addresses as
    /// ms-appx:///&lt;library&gt;/... .
    /// </summary>
    /// <remarks>
    /// A library shipped as a NuGet package, CommunityToolkit's for one, carries no XAML text at
    /// all - only its .dll and a .pri with the .xbf files embedded (measured on
    /// CommunityToolkit.WinUI.Controls.Segmented 8.2.251219) - so loading Generic.xaml as text
    /// (LibraryResources' first approach) cannot work for it. WinAppSDK lets the app hand XAML its
    /// own MRT ResourceManager (Application.ResourceManagerRequested); App does, and routes that
    /// manager's ResourceNotFound here. Library-PRI spec Decisions 1 and 2.
    /// </remarks>
    public static class LibraryPriResources
    {
        static readonly object gate = new object();
        static readonly List<string> pris = new List<string>();
        static readonly Dictionary<string, ResourceManager> managers =
            new Dictionary<string, ResourceManager>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True once the application has handed XAML a resource manager that routes failed
        /// lookups here. False in the test process, whose application does not.
        /// </summary>
        public static bool IsActive { get; set; }

        /// <summary>
        /// Diagnostics: every lookup that reaches here, appended to this file when set. App sets
        /// it from the WINUIDESIGNER_RESOURCE_TRACE environment variable; the walkthrough does,
        /// and prints the file. Kept because a lookup that silently resolves nothing is otherwise
        /// invisible.
        /// </summary>
        public static string TracePath { get; set; }

        /// <summary>Adds a library's .pri to the files failed lookups are tried against.</summary>
        internal static bool Register(string priPath)
        {
            if (string.IsNullOrEmpty(priPath) || !File.Exists(priPath))
                return false;
            lock (gate)
            {
                foreach (var known in pris)
                {
                    if (string.Equals(known, priPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                pris.Add(priPath);
            }
            Trace("registered " + priPath);
            return true;
        }

        /// <summary>
        /// The app resource manager's ResourceNotFound handler: tries each registered library
        /// .pri for the missing name, and hands the first match back. Never throws - it runs
        /// inside the XAML framework's resource lookup.
        /// </summary>
        public static void OnResourceNotFound(ResourceManager sender, ResourceNotFoundEventArgs args)
        {
            try
            {
                string[] candidates;
                lock (gate)
                    candidates = pris.ToArray();

                var name = args.Name;
                foreach (var pri in candidates)
                {
                    var candidate = Find(Manager(pri), name, args.Context);
                    if (candidate != null)
                    {
                        args.SetResolvedCandidate(candidate);
                        Trace("resolved " + name + " from " + Path.GetFileName(pri));
                        return;
                    }
                }
                Trace("not found " + name);
            }
            catch (Exception ex)
            {
                Trace("failed " + args.Name + ": " + ex.Message);
            }
        }

        static ResourceManager Manager(string pri)
        {
            lock (gate)
            {
                ResourceManager manager;
                if (!managers.TryGetValue(pri, out manager))
                {
                    manager = new ResourceManager(pri);
                    managers.Add(pri, manager);
                }
                return manager;
            }
        }

        /// <summary>
        /// The name as asked, then without its "Files/" root, then under the map's Files subtree:
        /// how a library .pri's map names its files is measured on the runner, not assumed.
        /// </summary>
        static ResourceCandidate Find(ResourceManager manager, string name, ResourceContext context)
        {
            var map = manager.MainResourceMap;
            var found = TryGet(map, name, context);
            if (found != null)
                return found;
            const string files = "Files/";
            if (name.StartsWith(files, StringComparison.OrdinalIgnoreCase))
            {
                var relative = name.Substring(files.Length);
                found = TryGet(map, relative, context);
                if (found != null)
                    return found;
                try
                {
                    var subtree = map.GetSubtree("Files");
                    if (subtree != null)
                        found = TryGet(subtree, relative, context);
                }
                catch (Exception)
                {
                }
            }
            return found;
        }

        static ResourceCandidate TryGet(ResourceMap map, string name, ResourceContext context)
        {
            try
            {
                return (context != null ? map.TryGetValue(name, context) : null) ?? map.TryGetValue(name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void Trace(string line)
        {
            var path = TracePath;
            if (path == null)
                return;
            try
            {
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }
    }
}
