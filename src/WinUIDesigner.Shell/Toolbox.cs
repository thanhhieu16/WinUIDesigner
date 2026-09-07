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

    /// <summary>
    /// The set of control types a user can place on the design surface.
    /// </summary>
    /// <remarks>
    /// Ported from the reference's Toolbox.cs, with one addition: a type that passes
    /// <see cref="IsControl"/> but throws from Activator.CreateInstance is skipped. Measured on
    /// this SDK, exactly one of the 160 candidates does (PivotHeaderItem). Spec Decision 11.
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

        public void AddAssembly(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException("assembly");

            ShellTypeFinder.Instance.RegisterAssembly(assembly);

            var node = new AssemblyNode();
            node.Assembly = assembly;

            foreach (var t in assembly.GetExportedTypes())
            {
                if (IsControl(t) && CanActivate(t))
                    node.Controls.Add(new ControlNode { Type = t });
            }

            node.Controls.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            AssemblyNodes.Add(node);
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
                // Deliberately silent, and deliberately catching everything: the alternative is
                // a crash while building the toolbox list. The type is simply absent.
                return false;
            }
        }
    }
}
