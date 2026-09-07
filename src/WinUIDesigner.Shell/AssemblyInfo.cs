using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("WinUIDesigner.Shell")]
[assembly: AssemblyDescription("WinUI 3 designer shell application")]

// Toolbox's IsControl/CanActivate predicates and ShellMetadata's registration flag are internal
// seams the test suite drives directly. Same pattern as WinUIDesigner.Designer's and
// WinUIDesigner.XamlDom's own AssemblyInfo.cs.
[assembly: InternalsVisibleTo("WinUIDesigner.Shell.Tests")]
