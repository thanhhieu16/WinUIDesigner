using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.Shell
{
    /// <summary>One control the toolbox offers.</summary>
    public sealed class ControlNode
    {
        public Type Type { get; set; }

        public string Name
        {
            get { return Type.Name; }
        }
    }

    /// <summary>One assembly's worth of toolbox controls.</summary>
    public sealed class AssemblyNode
    {
        public AssemblyNode()
        {
            Controls = new List<ControlNode>();
        }

        public Assembly Assembly { get; set; }

        public List<ControlNode> Controls { get; private set; }

        public string Name
        {
            get { return Assembly.GetName().Name; }
        }
    }

    /// <summary>What <see cref="Toolbox.AddAssembly"/> did.</summary>
    public sealed class AddAssemblyResult
    {
        public AddAssemblyResult()
        {
            Skipped = new List<string>();
        }

        /// <summary>The new toolbox group, or null when nothing was added.</summary>
        public AssemblyNode Added { get; set; }

        /// <summary>True when an assembly of the same name was already in the toolbox.</summary>
        public bool AlreadyPresent { get; set; }

        /// <summary>Full names of the controls left out because constructing one threw.</summary>
        public List<string> Skipped { get; private set; }
    }

    /// <summary>
    /// The set of control types a user can place on the design surface.
    /// </summary>
    /// <remarks>
    /// Ported from the reference's Toolbox.cs, with one addition: a type that passes
    /// <see cref="IsControl"/> but throws from Activator.CreateInstance is skipped. Measured on
    /// this SDK, exactly one of the 160 candidates does (PivotHeaderItem). Phase 5 Decision 11.
    /// <para>
    /// Every WinUI type is a WinRT class with thread affinity, so <see cref="CanActivate"/> -
    /// and therefore <see cref="AddAssembly"/> and the first touch of <see cref="Instance"/> -
    /// must run on the WinUI dispatcher thread.
    /// </para>
    /// </remarks>
    public sealed class Toolbox
    {
        static readonly object gate = new object();
        static Toolbox instance;

        public static Toolbox Instance
        {
            get
            {
                lock (gate)
                {
                    if (instance == null)
                    {
                        instance = new Toolbox();
                        instance.AddAssembly(typeof(Microsoft.UI.Xaml.Controls.Button).Assembly);
                    }
                    return instance;
                }
            }
        }

        Toolbox()
        {
            AssemblyNodes = new ObservableCollection<AssemblyNode>();
        }

        public ObservableCollection<AssemblyNode> AssemblyNodes { get; private set; }

        /// <summary>
        /// Adds <paramref name="assembly"/>'s controls as one group, reporting the ones that
        /// threw on construction rather than dropping them silently. An assembly already in the
        /// toolbox (by simple name) is left as it is. Toolbox-assemblies spec Decision 2.
        /// </summary>
        public AddAssemblyResult AddAssembly(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException("assembly");

            var result = new AddAssemblyResult();
            var name = assembly.GetName().Name;
            foreach (var existing in AssemblyNodes)
            {
                if (existing.Name == name)
                {
                    result.AlreadyPresent = true;
                    return result;
                }
            }

            ShellTypeFinder.Instance.RegisterAssembly(assembly);

            var node = new AssemblyNode();
            node.Assembly = assembly;

            Type[] exported;
            try
            {
                exported = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // A dependency is missing; the types that did load are still worth offering.
                exported = Array.FindAll(ex.Types, t => t != null && t.IsPublic);
            }

            foreach (var t in exported)
            {
                if (!IsControl(t))
                    continue;
                if (CanActivate(t))
                    node.Controls.Add(new ControlNode { Type = t });
                else
                    result.Skipped.Add(t.FullName);
            }

            node.Controls.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            result.Skipped.Sort(StringComparer.Ordinal);
            // An assembly with nothing placeable gets no empty group.
            if (node.Controls.Count == 0)
                return result;
            AssemblyNodes.Add(node);
            result.Added = node;
            return result;
        }

        /// <summary>
        /// Test seam: takes a group back out, so a test can watch it being added. Production
        /// never removes one: an assembly cannot be unloaded (toolbox-assemblies spec Decision 1).
        /// </summary>
        internal void RemoveAssemblyForTest(string name)
        {
            foreach (var node in AssemblyNodes)
            {
                if (node.Name == name)
                {
                    AssemblyNodes.Remove(node);
                    return;
                }
            }
        }

        /// <summary>
        /// Loads the assembly at <paramref name="path"/> into the default context, so its
        /// dependencies are found beside it. Returns null with <paramref name="error"/> set for
        /// anything that is not a loadable .NET assembly. Toolbox-assemblies spec Decision 1.
        /// </summary>
        public static Assembly LoadAssembly(string path, out string error)
        {
            error = null;
            try
            {
                return Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException)
            {
                error = System.IO.Path.GetFileName(path) + " is not a .NET assembly.";
            }
            catch (Exception ex)
            {
                // Broad, as ShellModel.Open's catch is: LoadFrom's exception set shares no base
                // beyond Exception, and nothing here may throw into the UI.
                error = ex.Message;
            }
            return null;
        }

        internal static bool IsControl(Type t)
        {
            return !t.IsAbstract
                && !t.IsGenericTypeDefinition
                && t.IsSubclassOf(typeof(UIElement))
                && t.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null) != null;
        }

        internal static bool CanActivate(Type t)
        {
            try
            {
                Activator.CreateInstance(t);
                return true;
            }
            catch (Exception)
            {
                // Deliberately catching everything: the alternative is a crash while building
                // the toolbox list. AddAssembly reports the type as skipped.
                return false;
            }
        }
    }
}
