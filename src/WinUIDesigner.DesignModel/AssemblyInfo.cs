using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("WinUIDesigner.DesignModel")]
[assembly: AssemblyDescription("Design-item object model for WinUI 3 XAML")]

// DefaultExtensionServer.PermanentWithDesignPanel (DefaultExtension.cs, Task 3) is internal by
// design - not part of the public ExtensionServer API - but Task 3's own brief already named
// WinUIDesigner.Designer's PanelMove (Task 15) as its intended consumer, and that type lives in
// a different assembly. Without this, PanelMove.cs fails to compile (CS0122): internal doesn't
// cross assembly boundaries without InternalsVisibleTo. Same pattern as WinUIDesigner.Designer's
// own AssemblyInfo.cs granting its Tests project access to ResizeThumbExtension.DragListeners.
[assembly: InternalsVisibleTo("WinUIDesigner.Designer")]
