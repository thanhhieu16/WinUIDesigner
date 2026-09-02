namespace WinUIDesigner.Designer.Services
{
    /// <summary>
    /// Designer-wide options. Ported whole from the reference's Services/OptionService.cs - the
    /// whole reference class is these two fields, and both have a Phase 4b caller
    /// (SnaplinePlacementRoundValues from Task 6's SnaplinePlacementBehavior;
    /// GrayOutDesignSurfaceExceptParentContainerWhenDragging from Task 8's
    /// GrayOutDesignerExceptActiveArea), so the "port only the surface this phase calls"
    /// precedent set by ModelTools.cs/PlacementOperation.cs is satisfied by porting both. The
    /// reference's third consumer, InfoTextEnterArea.cs, was dropped in Phase 4a and is not
    /// coming back. Public fields, not properties, matching the reference.
    /// </summary>
    public sealed class OptionService
    {
        /// <summary>Whether the design surface is grayed out except the active container while
        /// dragging/selecting.</summary>
        public bool GrayOutDesignSurfaceExceptParentContainerWhenDragging = true;

        /// <summary>Whether the values should be rounded when using snapline placement.</summary>
        public bool SnaplinePlacementRoundValues = false;
    }
}
