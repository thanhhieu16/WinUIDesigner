using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class CopyPasteServiceTests
    {
        const string OneButtonXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"A\" Width=\"20\" Height=\"20\" /></Canvas>";

        [Test]
        public void CanDeleteAndDeleteWorkThroughTheDesignContext()
        {
            WinUITestHost.RunWithWindow(window => {
                var settings = new XamlLoadSettings();
                settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
                settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new DefaultSelectionService()));
                // See ModelToolsContextMenuTests.Load for why Context must be wired up explicitly:
                // CopyPasteService.Delete -> ModelTools.DeleteComponents -> PlacementOperation.Start
                // walks into SnaplinePlacementBehavior.BeginPlacement, which reads
                // Services.DesignPanel.Context.RootItem.
                var designPanel = new WinUIDesigner.Designer.DesignPanel();
                settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), designPanel));
                using var reader = XmlReader.Create(new StringReader(OneButtonXaml));
                var context = new XamlDesignContext(reader, settings);
                designPanel.Context = context;
                window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)context.RootItem.Component;
                var buttonItem = context.Services.Component.GetDesignItem(canvas.Children[0]);
                context.Services.Selection.SetSelectedComponents(new[] { buttonItem });

                var service = new CopyPasteService();
                Assert.IsTrue(service.CanDelete(context));

                service.Delete(context);

                Assert.AreEqual(0, canvas.Children.Count);
            });
        }

        [Test]
        public void ANullContextIsRefusedAndHarmless()
        {
            var service = new CopyPasteService(new FakeClipboard { Content = "<Button />" });
            Assert.IsFalse(service.CanCopy(null));
            Assert.IsFalse(service.CanCut(null));
            Assert.IsFalse(service.CanPaste(null));
            Assert.DoesNotThrow(() => service.Copy(null));
            Assert.DoesNotThrow(() => service.Cut(null));
            Assert.DoesNotThrow(() => service.Paste(null));
        }

        // Cut/Copy/Paste (docs/superpowers/specs/2026-10-26-clipboard-design.md), all through a
        // fake clipboard: no test may depend on the clipboard of the machine running it.

        const string ClipboardXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Button x:Name=\"Ok\" Content=\"Ok\" Canvas.Left=\"20\" Canvas.Top=\"30\" Width=\"75\" Height=\"32\" />" +
            "<StackPanel x:Name=\"Stack\" Canvas.Left=\"200\" Canvas.Top=\"10\" Width=\"100\">" +
            "<TextBlock Text=\"inside\" />" +
            "</StackPanel>" +
            "</Canvas>";

        sealed class FakeClipboard : IDesignerClipboard
        {
            public string Content;
            public void SetXaml(string xaml) { Content = xaml; }
            public bool ContainsXaml { get { return Content != null; } }
            public System.Threading.Tasks.Task<string> GetXamlAsync() { return System.Threading.Tasks.Task.FromResult(Content); }
        }

        static DesignSurface LoadSurface(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(ClipboardXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        static Microsoft.UI.Xaml.Controls.Canvas Root(DesignSurface surface)
        {
            return (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
        }

        static DesignItem Named(DesignSurface surface, string name)
        {
            return Named(surface.DesignContext.RootItem, name);
        }

        static DesignItem Named(DesignItem item, string name)
        {
            if (item.Name == name)
                return item;
            var content = item.ContentProperty;
            if (content == null)
                return null;
            if (content.IsCollection) {
                foreach (var child in content.CollectionElements) {
                    var found = Named(child, name);
                    if (found != null)
                        return found;
                }
                return null;
            }
            return content.Value == null ? null : Named(content.Value, name);
        }

        static void Select(DesignSurface surface, params DesignItem[] items)
        {
            surface.DesignContext.Services.Selection.SetSelectedComponents(items);
        }

        static UndoService Undo(DesignSurface surface)
        {
            return surface.DesignContext.Services.GetService<UndoService>();
        }

        [Test]
        public void CopyPutsXamlThatParsesOnItsOwnAndChangesNothing()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window);
                var clipboard = new FakeClipboard();
                var service = new CopyPasteService(clipboard);
                Select(surface, Named(surface, "Ok"));

                Assert.IsTrue(service.CanCopy(surface.DesignContext));
                service.Copy(surface.DesignContext);

                StringAssert.StartsWith("<Button", clipboard.Content);
                StringAssert.Contains("xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"", clipboard.Content,
                    "GetXaml carries the ancestors' namespaces, so the snippet parses alone");
                Assert.IsFalse(Undo(surface).CanUndo, "a copy is not an edit");
            });
        }

        [Test]
        public void PasteOnACanvasLandsOffsetSelectedAndAsOneUndoStep()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window);
                var service = new CopyPasteService(new FakeClipboard());
                var ok = Named(surface, "Ok");
                Select(surface, ok);
                service.Copy(surface.DesignContext);

                Assert.IsTrue(service.CanPaste(surface.DesignContext));
                service.Paste(surface.DesignContext);
                surface.UpdateLayout();

                var canvas = Root(surface);
                Assert.AreEqual(3, canvas.Children.Count, "pasted beside the selected button, into its Canvas - not into the button");
                var pasted = (Microsoft.UI.Xaml.Controls.Button)canvas.Children[2];
                Assert.AreEqual(30, Microsoft.UI.Xaml.Controls.Canvas.GetLeft(pasted), 0.5, "20 + PasteOffset");
                Assert.AreEqual(40, Microsoft.UI.Xaml.Controls.Canvas.GetTop(pasted), 0.5, "30 + PasteOffset");
                Assert.AreEqual(20, Microsoft.UI.Xaml.Controls.Canvas.GetLeft((Microsoft.UI.Xaml.UIElement)ok.Component), 0.5, "the original stays put");
                Assert.AreSame(pasted, surface.DesignContext.Services.Selection.PrimarySelection?.Component);

                var undo = Undo(surface);
                undo.Undo();
                Assert.AreEqual(2, canvas.Children.Count);
                Assert.IsFalse(undo.CanUndo, "the paste was one undo entry");
            });
        }

        [Test]
        public void ATakenNameIsRenamedOnPaste()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window);
                var service = new CopyPasteService(new FakeClipboard());
                Select(surface, Named(surface, "Ok"));
                service.Copy(surface.DesignContext);
                service.Paste(surface.DesignContext);

                Assert.IsNotNull(Named(surface, "Ok_Copy"));
                Assert.IsNotNull(Named(surface, "Ok"), "the original keeps its name");
            });
        }

        [Test]
        public void CutRemovesAsOneUndoStepAndPasteBringsItBack()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window);
                var service = new CopyPasteService(new FakeClipboard());
                Select(surface, Named(surface, "Ok"));

                Assert.IsTrue(service.CanCut(surface.DesignContext));
                service.Cut(surface.DesignContext);

                var canvas = Root(surface);
                Assert.AreEqual(1, canvas.Children.Count);
                Assert.IsNull(Named(surface, "Ok"));

                Select(surface);
                service.Paste(surface.DesignContext);
                Assert.AreEqual(2, canvas.Children.Count, "with nothing selected the paste goes into the root");
                Assert.IsNotNull(Named(surface, "Ok"), "the name is free again, so it is kept");

                var undo = Undo(surface);
                undo.Undo();
                undo.Undo();
                Assert.AreEqual(2, canvas.Children.Count, "undoing the paste and then the cut restores the original");
                Assert.IsFalse(undo.CanUndo, "the cut was one undo entry");
            });
        }

        [Test]
        public void ASelectedPanelReceivesThePaste()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window);
                var service = new CopyPasteService(new FakeClipboard());
                Select(surface, Named(surface, "Ok"));
                service.Copy(surface.DesignContext);

                var stack = Named(surface, "Stack");
                Select(surface, stack);
                service.Paste(surface.DesignContext);

                var panel = (Microsoft.UI.Xaml.Controls.StackPanel)stack.Component;
                Assert.AreEqual(2, panel.Children.Count);
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Controls.Button>(panel.Children[1]);
                Assert.AreEqual(2, Root(surface).Children.Count, "nothing landed in the root");
            });
        }

        [Test]
        public void SeveralItemsCopiedTogetherPasteTogetherAsOneUndoStep()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window);
                var service = new CopyPasteService(new FakeClipboard());
                Select(surface, Named(surface, "Ok"), Named(surface, "Stack"));
                service.Copy(surface.DesignContext);

                Select(surface);
                service.Paste(surface.DesignContext);

                var canvas = Root(surface);
                Assert.AreEqual(4, canvas.Children.Count);
                Assert.AreEqual(2, surface.DesignContext.Services.Selection.SelectedItems.Count, "both pasted items are selected");
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Controls.StackPanel>(canvas.Children[3]);
                Assert.AreEqual(1, ((Microsoft.UI.Xaml.Controls.StackPanel)canvas.Children[3]).Children.Count, "a pasted panel brings its children");

                Undo(surface).Undo();
                Assert.AreEqual(2, canvas.Children.Count);
            });
        }

        [Test]
        public void CanExecuteFollowsTheSelectionAndTheClipboard()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window);
                var clipboard = new FakeClipboard();
                var service = new CopyPasteService(clipboard);
                var context = surface.DesignContext;

                Select(surface);
                Assert.IsFalse(service.CanCopy(context), "nothing selected");
                Assert.IsFalse(service.CanCut(context));

                Select(surface, context.RootItem);
                Assert.IsFalse(service.CanCopy(context), "the root has no container to copy out of");
                Assert.IsFalse(service.CanCut(context));

                Assert.IsFalse(service.CanPaste(context), "empty clipboard");
                clipboard.Content = "<Button xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />";
                Assert.IsTrue(service.CanPaste(context));
            });
        }

        [Test]
        public void AnUnparseablePayloadPastesNothingAndLeavesNoUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = LoadSurface(window);
                var service = new CopyPasteService(new FakeClipboard { Content = "<NotAType xmlns=\"urn:nothing\" /><" });

                service.Paste(surface.DesignContext);

                Assert.AreEqual(2, Root(surface).Children.Count);
                Assert.IsFalse(Undo(surface).CanUndo);
            });
        }
    }
}
