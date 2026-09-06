using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("WinUIDesigner.XamlDom")]
[assembly: AssemblyDescription("WinUI 3 XAML object model and parser")]

// XamlValueConverter is internal - GetCustomTypeConverter hands it out as a plain TypeConverter,
// so no consumer outside this assembly needs the name - but its tests construct it directly to
// exercise hosts that are deliberately not in the table. Same pattern as
// WinUIDesigner.Designer's and WinUIDesigner.DesignModel's own AssemblyInfo.cs.
[assembly: InternalsVisibleTo("WinUIDesigner.XamlDom.Tests")]
