using System;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Services
{
	/// <summary>Real IToolService implementation - see spec decision 1. The constructor activates
	/// PointerTool immediately so nothing else needs to remember to do it (a gap the phase's own
	/// spec review caught: registering this service alone does not activate anything unless the
	/// activation is self-contained here).</summary>
	public sealed class ToolService : IToolService
	{
		readonly IDesignPanel _designPanel;
		ITool _currentTool;

		public ITool PointerTool { get; }
		public event EventHandler CurrentToolChanged;

		public ToolService(IDesignPanel designPanel)
		{
			_designPanel = designPanel;
			PointerTool = new Services.PointerTool();
			CurrentTool = PointerTool;
		}

		public ITool CurrentTool
		{
			get => _currentTool;
			set
			{
				if (_currentTool == value) return;
				_currentTool?.Deactivate(_designPanel);
				_currentTool = value;
				if (_currentTool != null) {
					_currentTool.Activate(_designPanel);
					_designPanel.SetCursor(_currentTool.Cursor);
				}
				CurrentToolChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}
}
