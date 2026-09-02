using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Extensions;

namespace WinUIDesigner.Designer.Tests
{
    /// <summary>
    /// RasterPlacementBehavior and SnaplinePlacementBehavior carry no [ExtensionFor] of their
    /// own (the reference doesn't either) - they exist only as bases for
    /// CanvasPlacementSupport/GridPlacementSupport (Tasks 9-10), and ExtensionForAttribute is
    /// Inherited=false, read by ExtensionManager with GetCustomAttributes(..., inherit: false).
    /// So nothing applies them to a real item and nothing can test them until a subclass
    /// registers itself. This is that subclass. Rebased onto SnaplinePlacementBehavior once
    /// Task 6 lands (below) - Task 5's own tests keep passing because they force
    /// UseSnaplinePlacement = false.
    ///
    /// SwapChainPanel is the container type on purpose: it is a real Panel (collection
    /// ContentProperty "Children"), it arranges a Left/Top-aligned child at its Margin offset -
    /// verified empirically, unlike RelativePanel, which puts every child at (0,0) - and no
    /// extension in this port or anywhere in Phase 4b's plan registers for it (the
    /// container-specific behaviors claim Canvas, Grid and StackPanel only), so this test-only
    /// registration can never collide with a production one.
    /// </summary>
    [ExtensionFor(typeof(SwapChainPanel), OverrideExtension = typeof(DefaultPlacementBehavior))]
    public sealed class TestContainerPlacementBehavior : SnaplinePlacementBehavior
    {
    }
}
