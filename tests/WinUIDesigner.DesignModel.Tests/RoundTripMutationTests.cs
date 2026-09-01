using System.IO;
using System.Text;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class RoundTripMutationTests
    {
        const string SimpleXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"MyButton\" Content=\"Hello\" />" +
            "</Grid>";

        static XamlDesignContext Load(string xaml)
        {
            using (var reader = XmlReader.Create(new StringReader(xaml)))
                return new XamlDesignContext(reader, new XamlLoadSettings());
        }

        static string Save(XamlDesignContext context)
        {
            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true }))
                context.Save(writer);
            return sb.ToString();
        }

        [Test]
        public void CommittedMutationSurvivesRoundTripThroughTheRealWinUIOracle()
        {
            WinUITestHost.Run(() => {
                var context = Load(SimpleXaml);
                var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                var buttonItem = context.Services.Component.GetDesignItem(button);

                using (var group = context.OpenGroup("change content", new DesignItem[] { buttonItem })) {
                    buttonItem.Properties["Content"].SetValue("World");
                    group.Commit();
                }

                Assert.AreEqual("World", button.Content, "live instance should reflect the committed mutation immediately");

                var savedXaml = Save(context);
                var reparsed = (Microsoft.UI.Xaml.Controls.Grid)Microsoft.UI.Xaml.Markup.XamlReader.Load(savedXaml);
                var reparsedButton = (Microsoft.UI.Xaml.Controls.Button)reparsed.Children[0];

                Assert.AreEqual("World", reparsedButton.Content, "the real WinUI XamlReader oracle should agree after re-parsing the saved XAML");
            });
        }

        [Test]
        public void AbortedMutationRevertsTheLiveInstance()
        {
            WinUITestHost.Run(() => {
                var context = Load(SimpleXaml);
                var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                var buttonItem = context.Services.Component.GetDesignItem(button);

                using (var group = context.OpenGroup("change content", new DesignItem[] { buttonItem })) {
                    buttonItem.Properties["Content"].SetValue("World");
                    Assert.AreEqual("World", button.Content, "the mutation should be visible immediately, before Abort");
                    group.Abort();
                }

                Assert.AreEqual("Hello", button.Content, "Abort should revert the live instance to its pre-transaction value");
            });
        }

        [Test]
        public void UndoServiceUndoRevertsACommittedMutation()
        {
            WinUITestHost.Run(() => {
                var context = Load(SimpleXaml);
                var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                var buttonItem = context.Services.Component.GetDesignItem(button);
                var undoService = context.Services.GetRequiredService<Services.UndoService>();

                using (var group = context.OpenGroup("change content", new DesignItem[] { buttonItem })) {
                    buttonItem.Properties["Content"].SetValue("World");
                    group.Commit();
                }

                Assert.IsTrue(undoService.CanUndo);
                undoService.Undo();

                Assert.AreEqual("Hello", button.Content, "UndoService.Undo() should revert the committed mutation");
                Assert.IsTrue(undoService.CanRedo);
            });
        }
    }
}
