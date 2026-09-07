namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The XAML a new, empty document starts from.
    /// </summary>
    /// <remarks>
    /// A constant rather than the reference's on-disk NewFileTemplate.xaml, and rooted at a
    /// Canvas rather than the reference's Window: Microsoft.UI.Xaml.Window is not a UIElement,
    /// so it cannot be a design root. Canvas rather than Grid because CanvasPlacementSupport
    /// gives free placement, which is what a blank document wants. Phase 5 Decision 15.
    /// </remarks>
    public static class NewDocumentTemplate
    {
        public const string Xaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"640\" Height=\"480\" />";
    }
}
