using System.Reflection;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// Resolves assemblies named in a document's XAML namespace declarations against the
    /// assemblies the toolbox knows about.
    /// </summary>
    /// <remarks>
    /// Ported from the reference's MyTypeFinder.cs. XamlTypeFinder.ImportFrom is protected, so
    /// the WinUI seed is imported from inside this type - CreateWinUITypeFinder() returns a
    /// XamlTypeFinder, not a ShellTypeFinder, so it cannot simply be assigned. Phase 5 Decision 14.
    /// </remarks>
    public sealed class ShellTypeFinder : XamlTypeFinder
    {
        static readonly object gate = new object();
        static ShellTypeFinder instance;

        public static ShellTypeFinder Instance
        {
            get
            {
                lock (gate)
                {
                    if (instance == null)
                    {
                        var created = new ShellTypeFinder();
                        created.ImportFrom(CreateWinUITypeFinder());
                        instance = created;
                    }
                    return instance;
                }
            }
        }

        public override Assembly LoadAssembly(string name)
        {
            foreach (var registered in RegisteredAssemblies)
            {
                if (registered.GetName().Name == name)
                    return registered;
            }

            foreach (var node in Toolbox.Instance.AssemblyNodes)
            {
                if (node.Name == name)
                    return node.Assembly;
            }

            return null;
        }

        public override XamlTypeFinder Clone()
        {
            return Instance;
        }
    }
}
