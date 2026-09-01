using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class CloneTests
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

        // Regression test for a name-table aliasing bug: DesignItem.Clone() re-parses the
        // cloned element's XAML text - which still carries the ORIGINAL's x:Name - into the
        // *same* XamlDocument as the original (see XamlParser.ParseSnippet). Parsing that text
        // calls NameScopeHelper.NameChanged(clone, null, "MyButton") *before*
        // RegisterXamlComponentRecursive's duplicate-name check ever runs, which silently
        // overwrites the document's NamedObjects["MyButton"] entry to point at the clone
        // (last-writer-wins, per NameScopeHelper's own doc comment). The duplicate check then
        // sees a "collision" that is really just the clone finding itself, renames the clone,
        // and NameScopeHelper.NameChanged's unconditional names.Remove(oldName) during that
        // rename evicts the "MyButton" key entirely - leaving the ORIGINAL element (whose XML
        // x:Name attribute still literally says "MyButton") unresolvable by name, even though
        // XamlObject.Name still reads "MyButton" directly off the XML attribute.
        [Test]
        public void CloneDoesNotCorruptOriginalsNameTableEntry()
        {
            WinUITestHost.Run(() => {
                var context = Load(SimpleXaml);
                var grid = (Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component;
                var button = (Microsoft.UI.Xaml.Controls.Button)grid.Children[0];
                var buttonItem = (XamlDesignItem)context.Services.Component.GetDesignItem(button);
                var document = buttonItem.XamlObject.OwnerDocument;

                var clone = (XamlDesignItem)buttonItem.Clone();

                Assert.AreEqual("MyButton", buttonItem.Name,
                    "cloning must not alter the original element's own x:Name attribute");
                Assert.AreNotEqual("MyButton", clone.Name,
                    "the clone must be renamed to avoid a duplicate x:Name in the same document");

                var foundOriginal = NameScopeHelper.FindNamedObject(document, buttonItem.Name);
                Assert.AreSame(buttonItem.XamlObject, foundOriginal,
                    "the original element must remain resolvable by its own name in the document's " +
                    "name table (e.g. for {x:Reference}/ElementName lookups) after a clone is made and renamed");

                var foundClone = NameScopeHelper.FindNamedObject(document, clone.Name);
                Assert.AreSame(clone.XamlObject, foundClone,
                    "the clone must be resolvable by its new, distinct name");

                Assert.AreNotSame(buttonItem.Component, clone.Component,
                    "the clone must be a distinct component instance from the original");
            });
        }
    }
}
