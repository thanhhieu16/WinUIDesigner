namespace WinUIDesigner.DesignModel.Extensions
{
    /// <summary>Base class for extensions that provide a behavior interface for the designed
    /// item. Always loaded, must have a parameterless constructor. No WPF-specific API -
    /// mechanical port; DefaultExtensionServer.Permanent already exists (Phase 3).</summary>
    [ExtensionServer(typeof(DefaultExtensionServer.Permanent))]
    public class BehaviorExtension : DefaultExtension
    {
    }
}
