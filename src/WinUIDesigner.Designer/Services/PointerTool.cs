using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Services
{
	/// <summary>The default tool: click-to-select. Extracted from DesignPanel's own constructor
	/// (which used to wire this unconditionally) so other tools (CreateComponentTool, Draw*
	/// tools) can take over PointerPressed while active - see spec decision 1.</summary>
	public sealed class PointerTool : ITool
	{
		public InputCursor Cursor => InputSystemCursor.Create(InputSystemCursorShape.Arrow);

		public void Activate(IDesignPanel designPanel)
		{
			designPanel.PointerPressed += OnPointerPressed;
		}

		public void Deactivate(IDesignPanel designPanel)
		{
			designPanel.PointerPressed -= OnPointerPressed;
		}

		void OnPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			// sender is the real DesignPanel raising its own inherited UIElement.PointerPressed -
			// HandlePointerPressed is DesignPanel's own existing (unchanged, already-tested)
			// click-to-select logic.
			var designPanel = (DesignPanel)sender;
			var point = e.GetCurrentPoint(designPanel).Position;
			designPanel.HandlePointerPressed(point, e.KeyModifiers);
		}
	}
}
