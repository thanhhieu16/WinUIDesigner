using System;
using System.Xml;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Services;

namespace WinUIDesigner.Designer
{
	public sealed class DesignSurface : Microsoft.UI.Xaml.Controls.ContentControl
	{
		readonly DesignPanel _designPanel;
		DesignContext _designContext;

		public DesignSurface()
		{
			_designPanel = new DesignPanel();
			Content = _designPanel;
			// A bare ContentControl's generic.xaml default (WinAppSDK 1.7.250606001) template-binds
			// the ContentPresenter's alignment to HorizontalContentAlignment/VerticalContentAlignment,
			// which default to Left/Top - so an untemplated DesignSurface would otherwise shrink-wrap
			// its DesignPanel to DesiredSize instead of filling the surface's allocated area.
			HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
			VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
		}

		public DesignPanel DesignPanel { get { return _designPanel; } }
		public DesignContext DesignContext { get { return _designContext; } }

		public void LoadDesigner(XmlReader xamlReader, XamlLoadSettings loadSettings)
		{
			UnloadDesigner();
			loadSettings = loadSettings ?? new XamlLoadSettings();
			loadSettings.DesignerAssemblies.Add(typeof(DesignSurface).Assembly);
			loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(IDesignPanel), _designPanel));
			loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(ISelectionService), new DefaultSelectionService()));
			// The reference registers OptionService inside XamlDesignContext's own constructor
			// (Xaml/XamlDesignContext.cs:80). That is not possible here: OptionService lives in
			// WinUIDesigner.Designer and XamlDesignContext lives in WinUIDesigner.DesignModel,
			// which does not (and must not) reference Designer. Registered here instead, exactly
			// like IDesignPanel and ISelectionService above.
			loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(OptionService), new OptionService()));
			loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.Services.ICopyPasteService), new Services.CopyPasteService()));
			loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(IToolService), new Services.ToolService(_designPanel)));
			// The reference registers both outline services inside XamlDesignContext's own
			// constructor (Xaml/XamlDesignContext.cs:77-78). Same constraint as OptionService
			// above: both implementations live in WinUIDesigner.Designer, and XamlDesignContext
			// lives in WinUIDesigner.DesignModel, which does not (and must not) reference it.
			loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(IOutlineNodeNameService), new OutlineView.OutlineNodeNameService()));
			loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(IOutlineNodeService), new OutlineView.OutlineNode.OutlineNodeService()));
			InitializeDesigner(new XamlDesignContext(xamlReader, loadSettings));
		}

		void InitializeDesigner(DesignContext context)
		{
			_designContext = context;
			_designPanel.Context = context;
			if (context.RootItem != null) {
				_designPanel.Content = context.RootItem.View;
			}
		}

		public void UnloadDesigner()
		{
			if (_designContext != null) {
				foreach (object o in _designContext.Services.AllServices) {
					var d = o as IDisposable;
					if (d != null) d.Dispose();
				}
			}
			_designContext = null;
			_designPanel.Context = null;
			_designPanel.Content = null;
			_designPanel.Adorners.Clear();
		}

		public void SaveDesigner(XmlWriter writer)
		{
			_designContext.Save(writer);
		}
	}
}
