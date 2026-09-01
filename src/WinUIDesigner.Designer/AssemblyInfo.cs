using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("WinUIDesigner.Designer")]
[assembly: AssemblyDescription("DesignSurface, overlay layer, and hit-testing for WinUI 3")]

// ResizeThumbExtension.DragListeners (Task 4) is internal by design - it is a test seam for
// driving a specific thumb's DragListener directly, not part of the public extension API - so
// WinUIDesigner.Designer.Tests needs InternalsVisibleTo to reach it. No other internal member in
// this assembly is currently exercised from tests (confirmed by grep before adding this), so this
// is a new requirement introduced by Task 4, not a pre-existing gap.
[assembly: InternalsVisibleTo("WinUIDesigner.Designer.Tests")]
