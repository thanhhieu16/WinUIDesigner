using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinUIDesigner.Designer.Controls
{
	/// <summary>
	/// Minimal highlight adorner for the current primary selection - not a port of the
	/// reference's Controls/SelectionFrame.cs, which carries resize-thumb layout Phase 4 doesn't
	/// need yet (spec).
	/// </summary>
	/// <remarks>
	/// Derives from <see cref="Grid"/>, not <see cref="Border"/> as the brief's code literally
	/// shows: empirically confirmed (Phase 3, Task 4) that <c>Microsoft.UI.Xaml.Controls.Border</c>
	/// is a sealed class in Windows App SDK 1.7.250606001 (CS0509 "cannot derive from sealed
	/// type 'Border'" at build). <c>Grid</c> exposes the same <c>BorderBrush</c>/
	/// <c>BorderThickness</c> properties (confirmed by successful build) and renders an
	/// equivalent rectangular outline, so it is a drop-in substitute for this minimal adorner's
	/// purposes.
	/// </remarks>
	public sealed class SelectionHighlight : Grid
	{
		public SelectionHighlight()
		{
			BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Blue);
			BorderThickness = new Thickness(2);
			IsHitTestVisible = false;
		}
	}
}
