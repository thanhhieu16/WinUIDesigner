using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    /// <summary>Moving the selection with the arrow keys (keyboard-move spec).</summary>
    [TestFixture]
    public class KeyboardMoveTests
    {
        const string Xaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Button x:Name=\"A\" Canvas.Left=\"20\" Canvas.Top=\"30\" Width=\"40\" Height=\"20\" />" +
            "<Button x:Name=\"B\" Canvas.Left=\"100\" Canvas.Top=\"100\" Width=\"40\" Height=\"20\" />" +
            "</Canvas>";

        sealed class Loaded
        {
            public DesignPanel Panel;
            public XamlDesignContext Context;
            public DesignItem A, B;
        }

        static Loaded Load(Window window)
        {
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(ISelectionService), new DefaultSelectionService()));
            var panel = new DesignPanel();
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(IDesignPanel), panel));
            using var reader = XmlReader.Create(new StringReader(Xaml));
            var context = new XamlDesignContext(reader, settings);
            panel.Context = context;
            window.Content = (UIElement)context.RootItem.View;
            ((FrameworkElement)window.Content).UpdateLayout();
            var canvas = (Canvas)context.RootItem.Component;
            return new Loaded
            {
                Panel = panel,
                Context = context,
                A = context.Services.Component.GetDesignItem(canvas.Children[0]),
                B = context.Services.Component.GetDesignItem(canvas.Children[1])
            };
        }

        static UndoService Undo(Loaded l)
        {
            return l.Context.Services.GetService<UndoService>();
        }

        [Test]
        public void ArrowsMoveTheSelectionOnePixelAndShiftTen()
        {
            WinUITestHost.RunWithWindow(window =>
            {
                var l = Load(window);
                l.Context.Services.Selection.SetSelectedComponents(new[] { l.A });

                Assert.That(l.Panel.HandleArrowKeyDown(VirtualKey.Right, false), Is.True);
                l.Panel.HandleArrowKeyUp();
                Assert.That(l.Panel.HandleArrowKeyDown(VirtualKey.Down, true), Is.True);
                l.Panel.HandleArrowKeyUp();

                var view = (Button)l.A.View;
                Assert.That(Canvas.GetLeft(view), Is.EqualTo(21));
                Assert.That(Canvas.GetTop(view), Is.EqualTo(40));
            });
        }

        [Test]
        public void AHeldKeyIsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window =>
            {
                var l = Load(window);
                l.Context.Services.Selection.SetSelectedComponents(new[] { l.A });
                int before = Undo(l).UndoActions.Count();

                // Auto-repeat: several KeyDowns, one KeyUp.
                for (int i = 0; i < 5; i++)
                    l.Panel.HandleArrowKeyDown(VirtualKey.Left, false);
                l.Panel.HandleArrowKeyUp();

                var view = (Button)l.A.View;
                Assert.That(Canvas.GetLeft(view), Is.EqualTo(15));
                Assert.That(Undo(l).UndoActions.Count(), Is.EqualTo(before + 1));

                Undo(l).Undo();
                Assert.That(Canvas.GetLeft(view), Is.EqualTo(20));
            });
        }

        [Test]
        public void SeveralSelectedItemsMoveTogether()
        {
            WinUITestHost.RunWithWindow(window =>
            {
                var l = Load(window);
                l.Context.Services.Selection.SetSelectedComponents(new[] { l.A, l.B });

                l.Panel.HandleArrowKeyDown(VirtualKey.Up, true);
                l.Panel.HandleArrowKeyUp();

                Assert.That(Canvas.GetTop((Button)l.A.View), Is.EqualTo(20));
                Assert.That(Canvas.GetTop((Button)l.B.View), Is.EqualTo(90));
            });
        }

        [Test]
        public void TheRootAloneOrNoSelectionIsNotMoved()
        {
            WinUITestHost.RunWithWindow(window =>
            {
                var l = Load(window);
                l.Context.Services.Selection.SetSelectedComponents(new[] { l.Context.RootItem });
                Assert.That(l.Panel.HandleArrowKeyDown(VirtualKey.Right, false), Is.False);

                l.Context.Services.Selection.SetSelectedComponents(new DesignItem[0]);
                Assert.That(l.Panel.HandleArrowKeyDown(VirtualKey.Right, false), Is.False);
                Assert.That(l.Panel.HandleArrowKeyDown(VirtualKey.A, false), Is.False);
            });
        }
    }
}
