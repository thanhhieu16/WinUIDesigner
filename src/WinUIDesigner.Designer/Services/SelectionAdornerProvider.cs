using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;

namespace WinUIDesigner.Designer.Services
{
	[ExtensionFor(typeof(Microsoft.UI.Xaml.UIElement))]
	[ExtensionServer(typeof(PrimarySelectionExtensionServer))]
	public sealed class SelectionAdornerProvider : AdornerProvider
	{
		protected override void OnInitialized()
		{
			base.OnInitialized();
			AddAdorner(AdornerPlacement.FillContent, AdornerOrder.Foreground, new SelectionHighlight());
		}
	}
}
