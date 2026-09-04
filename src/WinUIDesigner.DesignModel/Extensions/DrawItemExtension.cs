using System;
using Windows.Foundation;
using Windows.System;

namespace WinUIDesigner.DesignModel
{
	/// <summary>Behavior interface implemented by container elements (Canvas/Grid) to support
	/// drawing new shapes with container-specific logic (Line/Polyline/Path). Ported from the
	/// reference's DrawItemExtension.cs (WpfDesign core, not WpfDesign.Designer - matches this
	/// port's layering, spec decision 2). StartDrawItem's signature is decomposed primitives
	/// (Point/VirtualKeyModifiers), not a raw pointer event - only ever called from
	/// CreateComponentTool's click path (never from toolbox-drop, which never checks this
	/// interface in the reference either), and pointer capture already happened one level up in
	/// CreateComponentTool before this is called (spec decision 2's "Where pointer capture for a
	/// draw gesture actually happens").</summary>
	public interface IDrawItemExtension
	{
		bool CanItemBeDrawn(Type createItemType);

		/// <param name="drawItemCallback">Invoked with the newly-created DesignItem right after
		/// it's created, before the draw gesture starts tracking further pointer moves - mirrors
		/// the reference's identically-named callback (may be null).</param>
		void StartDrawItem(DesignItem clickedOn, Type createItemType, IDesignPanel panel, Point point, VirtualKeyModifiers modifiers, Action<DesignItem> drawItemCallback);
	}
}
