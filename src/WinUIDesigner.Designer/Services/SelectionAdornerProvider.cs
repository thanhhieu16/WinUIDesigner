using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;

namespace WinUIDesigner.Designer.Services
{
	[ExtensionFor(typeof(Microsoft.UI.Xaml.UIElement))]
	// Every selected item gets a highlight, as in the reference's SelectedElementRectangleExtension;
	// the resize/rotate/skew thumbs stay primary-only through their own servers.
	[ExtensionServer(typeof(SelectionExtensionServer))]
	public sealed class SelectionAdornerProvider : AdornerProvider
	{
		protected override void OnInitialized()
		{
			base.OnInitialized();
			AddAdorner(AdornerPlacement.FillContent, AdornerOrder.Foreground, new SelectionHighlight());
		}
	}
}
