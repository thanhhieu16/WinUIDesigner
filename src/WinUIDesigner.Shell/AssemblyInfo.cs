using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("WinUIDesigner.Shell")]
[assembly: AssemblyDescription("WinUI 3 designer shell application")]

// Toolbox.IsControl/CanActivate and Document.SetTempName are internal seams the test suite
// drives directly. Same pattern as WinUIDesigner.Designer's and WinUIDesigner.XamlDom's own
// AssemblyInfo.cs.
[assembly: InternalsVisibleTo("WinUIDesigner.Shell.Tests")]
