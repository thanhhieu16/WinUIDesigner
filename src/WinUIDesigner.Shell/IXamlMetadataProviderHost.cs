using Microsoft.UI.Xaml.Markup;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// An application that can take another XAML metadata provider at runtime, so XamlReader can
    /// name the types of a library loaded after the app was built.
    /// </summary>
    /// <remarks>
    /// App implements it through the XAML compiler's generated <c>_AddOtherProvider</c>. The test
    /// process's application does not, and needs nothing: it resolves every loaded type by
    /// reflection. Library-resources spec Decision 2.
    /// </remarks>
    public interface IXamlMetadataProviderHost
    {
        void AddProvider(IXamlMetadataProvider provider);
    }
}
