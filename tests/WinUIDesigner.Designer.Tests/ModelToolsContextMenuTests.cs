using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class ModelToolsContextMenuTests
    {
        const string TwoButtonsInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" Canvas.Left=\"0\" Canvas.Top=\"0\" />" +
            "<Button x:Name=\"B\" Width=\"20\" Height=\"20\" Canvas.Left=\"40\" Canvas.Top=\"0\" />" +
            "</Canvas>";

        static (XamlDesignContext context, DesignItem aItem, DesignItem bItem) Load(Microsoft.UI.Xaml.Window window)
        {
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new WinUIDesigner.Designer.Services.DefaultSelectionService()));
            // Unlike ModelToolsTests' Load (ApplyTransform never touches the placement chain), this
            // fixture calls DeleteComponents -> PlacementOperation.Start -> SnaplinePlacementBehavior.
            // BeginPlacement, which reads Services.DesignPanel.Context.RootItem to enumerate sibling
            // items for snaplines (SnaplinePlacementBehavior.cs:256). A bare XamlDesignContext never
            // wires that up automatically - production only does it via DesignSurface.InitializeDesigner
            // (_designPanel.Context = context) - so it must be set explicitly here too, or this NREs.
            var designPanel = new WinUIDesigner.Designer.DesignPanel();
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), designPanel));
            using var reader = XmlReader.Create(new StringReader(TwoButtonsInCanvasXaml));
            var context = new XamlDesignContext(reader, settings);
            designPanel.Context = context;
            window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
            ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component;
            var aItem = context.Services.Component.GetDesignItem(canvas.Children[0]);
            var bItem = context.Services.Component.GetDesignItem(canvas.Children[1]);
            return (context, aItem, bItem);
        }

        [Test]
        public void CanDeleteComponentsIsTrueForANonEmptySelectionAndFalseForEmpty()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, aItem, _) = Load(window);
                Assert.IsTrue(WinUIDesigner.Designer.ModelTools.CanDeleteComponents(new[] { aItem }));
                Assert.IsFalse(WinUIDesigner.Designer.ModelTools.CanDeleteComponents(new WinUIDesigner.DesignModel.DesignItem[0]));
            });
        }

        [Test]
        public void DeleteComponentsRemovesItemsAndCommitsAsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, aItem, bItem) = Load(window);
                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component;
                var undoService = context.Services.GetService<UndoService>();
                var undoCountBefore = undoService.UndoActions.Count();

                WinUIDesigner.Designer.ModelTools.DeleteComponents(new[] { aItem, bItem });

                Assert.AreEqual(0, canvas.Children.Count);
                Assert.AreEqual(undoCountBefore + 1, undoService.UndoActions.Count());

                undoService.Undo();
                Assert.AreEqual(2, canvas.Children.Count);
            });
        }

        [Test]
        public void DeleteComponentsSelectsParentWhenSelectionBecomesEmpty()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, aItem, bItem) = Load(window);
                context.Services.Selection.SetSelectedComponents(new[] { aItem, bItem });

                WinUIDesigner.Designer.ModelTools.DeleteComponents(new[] { aItem, bItem });

                Assert.AreEqual(1, context.Services.Selection.SelectionCount);
                Assert.AreSame(context.RootItem, context.Services.Selection.PrimarySelection);
            });
        }
    }
}
