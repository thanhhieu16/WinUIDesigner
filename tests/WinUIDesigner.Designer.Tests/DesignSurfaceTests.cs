using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
	[TestFixture]
	public class DesignSurfaceTests
	{
		const string Xaml =
			"<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
			"xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
			"<Button x:Name=\"MyButton\" />" +
			"</Grid>";

		[Test]
		public void LoadDesignerRegistersIDesignPanelAndISelectionService()
		{
			WinUITestHost.RunWithWindow(window => {
				var surface = new DesignSurface();
				window.Content = surface;

				using (var reader = XmlReader.Create(new StringReader(Xaml)))
					surface.LoadDesigner(reader, new XamlLoadSettings());

				Assert.AreSame(surface.DesignPanel, surface.DesignContext.Services.GetService<IDesignPanel>());
				Assert.IsNotNull(surface.DesignContext.Services.GetService<ISelectionService>());
				Assert.AreSame(surface.DesignContext.RootItem.View, surface.DesignPanel.Content);
			});
		}

		[Test]
		public void SelectingTheRootItemAppliesExactlyOneSelectionHighlight()
		{
			WinUITestHost.RunWithWindow(window => {
				var surface = new DesignSurface();
				window.Content = surface;

				using (var reader = XmlReader.Create(new StringReader(Xaml)))
					surface.LoadDesigner(reader, new XamlLoadSettings());
				surface.UpdateLayout();

				surface.DesignContext.Services.Selection.SetSelectedComponents(
					new DesignItem[] { surface.DesignContext.RootItem }, SelectionTypes.Primary);
				surface.UpdateLayout();

				var highlights = surface.DesignPanel.Adorners
					.SelectMany(a => a.Children)
					.OfType<SelectionHighlight>()
					.ToList();
				Assert.AreEqual(1, highlights.Count);
			});
		}
	}
}
