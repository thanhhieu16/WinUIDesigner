using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// Gives a library added at runtime its theme styles, so its templated controls get their look.
    /// </summary>
    /// <remarks>
    /// The framework finds a templated control's default style in its library's
    /// Themes/Generic.xaml through ms-appx:///, which resolves through the app's merged
    /// resources.pri - and a library loaded at runtime was never merged into it, so its controls
    /// rendered at zero size (toolbox-assemblies plan, Results). A WinUI class library's build
    /// output carries that file as processed XAML text in its library layout,
    /// &lt;library&gt;/Themes/Generic.xaml beside the .dll. Loading it with XamlReader and merging
    /// it into the application's resources makes its implicit styles apply. Library-resources spec
    /// Decision 1.
    /// </remarks>
    internal static class LibraryResources
    {
        /// <summary>The theme file a library's layout puts beside <paramref name="assemblyPath"/>.</summary>
        internal static string ThemePath(Assembly assembly, string assemblyPath)
        {
            var folder = Path.GetDirectoryName(assemblyPath);
            return Path.Combine(folder ?? string.Empty, assembly.GetName().Name, "Themes", "Generic.xaml");
        }

        /// <summary>
        /// Loads <paramref name="assembly"/>'s theme, if its layout has one, into
        /// Application.Current.Resources. Returns null when that worked or there is no theme, and
        /// the reason otherwise; nothing is merged then. Never throws.
        /// </summary>
        internal static string Load(Assembly assembly, string assemblyPath)
        {
            string path;
            try
            {
                path = ThemePath(assembly, assemblyPath);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            // A library built in code has no theme, which is not a problem.
            if (!File.Exists(path))
                return null;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            // Checked as XML before XamlReader ever sees it. A XamlReader.Load that fails throws
            // a catchable exception and still leaves a stowed error behind: measured, the Shell
            // suite's host died with 0xC000027B a few tests after a malformed theme was caught
            // here, and passed with that one test left out. A file that is not even well-formed
            // XML - the likeliest broken theme - is therefore refused here instead.
            var notXml = WellFormedError(text);
            if (notXml != null)
                return notXml;

            var registered = RegisterProviders(assembly);
            if (registered != null)
                return registered;

            ResourceDictionary dictionary;
            try
            {
                dictionary = XamlReader.Load(text) as ResourceDictionary;
            }
            catch (Exception ex)
            {
                // Typically an ms-appx:/// reference inside the library, which needs its .pri.
                return ex.Message;
            }
            if (dictionary == null)
                return "Themes/Generic.xaml is not a ResourceDictionary.";

            Application.Current.Resources.MergedDictionaries.Add(dictionary);
            return null;
        }

        static string WellFormedError(string text)
        {
            try
            {
                var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit };
                using (var reader = System.Xml.XmlReader.Create(new StringReader(text), settings))
                {
                    while (reader.Read())
                    {
                    }
                }
                return null;
            }
            catch (System.Xml.XmlException ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// Hands the library's own XAML metadata provider to the application, without which
        /// XamlReader cannot name the library's types in its theme. Library-resources spec
        /// Decision 2.
        /// </summary>
        static string RegisterProviders(Assembly assembly)
        {
            var host = Application.Current as IXamlMetadataProviderHost;
            if (host == null)
                return null;
            try
            {
                var providers = assembly.GetExportedTypes()
                    .Where(t => !t.IsAbstract
                        && typeof(IXamlMetadataProvider).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) != null);
                foreach (var type in providers)
                    host.AddProvider((IXamlMetadataProvider)Activator.CreateInstance(type));
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
