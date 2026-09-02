using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class GridAdornerProviderTests
    {
        // Row/column sizes are deliberately left unset (each definition defaults to 1*).
        // GridLength has NO from-string TypeConverter in this port (Global Constraints), so
        // Height="40" in XAML never reaches the live RowDefinition - a fixture must never rely
        // on it. 200x120 with two rows/two columns gives an exact 60/60 and 100/100 split.
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"120\">" +
            "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
            "<Grid.ColumnDefinitions><ColumnDefinition /><ColumnDefinition /></Grid.ColumnDefinitions>" +
            "<Button x:Name=\"Target\" Grid.Row=\"1\" Grid.Column=\"1\" Content=\"x\" />" +
            "</Grid>";

        const string SpanXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"200\" Height=\"120\">" +
            "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
            "<Button x:Name=\"Target\" Grid.Row=\"0\" Grid.RowSpan=\"2\" Content=\"x\" />" +
            "</Grid>";

        internal static DesignSurface Load(Window window, string xaml,
                                           out DesignItem gridItem, out Grid grid, out GridAdornerProvider provider)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();

            gridItem = surface.DesignContext.RootItem;
            grid = (Grid)gridItem.Component;
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { gridItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            provider = gridItem.Extensions.OfType<GridAdornerProvider>().Single();
            // The rebuild is coalesced onto the dispatcher; a test body is one dispatcher turn, so
            // the enqueued callback can never run inside it. Flush it explicitly instead.
            provider.FlushPendingRebuild();
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void SelectingAGridShowsTwoRails()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out _, out var provider);
                var rails = surface.DesignPanel.Adorners.SelectMany(a => a.Children).OfType<GridRailAdorner>().ToList();
                Assert.AreEqual(2, rails.Count);
                Assert.AreEqual(120, provider.LeftRail.ActualHeight, 0.5);
                Assert.AreEqual(10, provider.LeftRail.ActualWidth, 0.5);
                Assert.AreEqual(200, provider.TopRail.ActualWidth, 0.5);
                Assert.AreEqual(10, provider.TopRail.ActualHeight, 0.5);
            });
        }

        [Test]
        public void RailsShowOneSizeUnitCaptionPerRowAndColumn()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out _, out var provider);
                CollectionAssert.AreEqual(new[] { "*", "*" }, provider.LeftRail.LabelTexts.ToList());
                CollectionAssert.AreEqual(new[] { "*", "*" }, provider.TopRail.LabelTexts.ToList());

                provider.LeftRail.SimulateRailPress(30);
                surface.UpdateLayout();
                CollectionAssert.AreEqual(new[] { "0.5*", "0.5*", "*" }, provider.LeftRail.LabelTexts.ToList());
            });
        }

        [Test]
        public void RailClickCreatesRowAndShiftsLaterSiblings()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                var button = (Button)grid.Children[0];
                Assert.AreEqual(60, grid.RowDefinitions[0].ActualHeight, 0.5, "precondition");

                provider.LeftRail.SimulateRailPress(30);
                surface.UpdateLayout();

                Assert.AreEqual(3, grid.RowDefinitions.Count);
                Assert.AreEqual(30, grid.RowDefinitions[0].ActualHeight, 0.5);
                Assert.AreEqual(30, grid.RowDefinitions[1].ActualHeight, 0.5);
                Assert.AreEqual(2, Grid.GetRow(button), "a later sibling shifts down one row");
                Assert.AreEqual(1, undo.UndoActions.Count(), "creation + every sibling rewrite is ONE undo entry");

                undo.Undo();
                surface.UpdateLayout();
                Assert.AreEqual(2, grid.RowDefinitions.Count);
                Assert.AreEqual(1, Grid.GetRow(button));
                Assert.AreEqual(60, grid.RowDefinitions[0].ActualHeight, 0.5,
                    "undo must restore the split row's live size, not just the XAML");
            });
        }

        [Test]
        public void SplittingAStraddlingChildGrowsItsRowSpan()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, SpanXaml, out _, out var grid, out var provider);
                var button = (Button)grid.Children[0];
                provider.LeftRail.SimulateRailPress(30);
                surface.UpdateLayout();
                Assert.AreEqual(3, grid.RowDefinitions.Count);
                Assert.AreEqual(0, Grid.GetRow(button));
                Assert.AreEqual(3, Grid.GetRowSpan(button));
            });
        }

        [Test]
        public void ColumnRailClickCreatesColumn()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window, GridXaml, out _, out var grid, out var provider);
                provider.TopRail.SimulateRailPress(50);
                surface.UpdateLayout();
                Assert.AreEqual(3, grid.ColumnDefinitions.Count);
                Assert.AreEqual(2, Grid.GetColumn((Button)grid.Children[0]));
            });
        }
    }
}
