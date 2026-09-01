using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using WinUIDesigner.XamlDom;
using WinUIDesigner.DesignModel.Services;

namespace WinUIDesigner.DesignModel.Xaml
{
    public sealed class XamlDesignContext : DesignContext
    {
        readonly XamlDocument _doc;
        readonly XamlDesignItem _rootItem;
        readonly XamlParserSettings _parserSettings;
        internal readonly XamlComponentService _componentService;

        internal XamlDocument Document {
            get { return _doc; }
        }

        public string ClassName {
            get { return _doc.RootElement.GetXamlAttribute("Class"); }
        }

        public XamlDesignContext(XmlReader xamlReader, XamlLoadSettings loadSettings)
        {
            if (xamlReader == null)
                throw new ArgumentNullException("xamlReader");
            if (loadSettings == null)
                throw new ArgumentNullException("loadSettings");

            this.Services.AddService(typeof(IComponentPropertyService), new ComponentPropertyService());
            this.Services.AddService(typeof(UndoService), new UndoService());

            var xamlErrorService = new XamlErrorService();
            this.Services.AddService(typeof(XamlErrorService), xamlErrorService);
            this.Services.AddService(typeof(IXamlErrorSink), xamlErrorService);

            _componentService = new XamlComponentService(this);
            this.Services.AddService(typeof(IComponentService), _componentService);

            foreach (Action<XamlDesignContext> action in loadSettings.CustomServiceRegisterFunctions) {
                action(this);
            }

            foreach (Assembly designerAssembly in loadSettings.DesignerAssemblies) {
                this.Services.ExtensionManager.RegisterAssembly(designerAssembly);
            }

            _parserSettings = new XamlParserSettings();
            _parserSettings.TypeFinder = loadSettings.TypeFinder;
            _parserSettings.CurrentProjectAssemblyName = loadSettings.CurrentProjectAssemblyName;
            _parserSettings.CreateInstanceCallback = this.Services.ExtensionManager.CreateInstanceWithCustomInstanceFactory;
            _parserSettings.ServiceProvider = this.Services;
            _doc = XamlParser.Parse(xamlReader, _parserSettings);

            loadSettings.ReportErrors(xamlErrorService);

            if (_doc == null) {
                string message;
                if (xamlErrorService != null && xamlErrorService.Errors.Count > 0)
                    message = xamlErrorService.Errors[0].Message;
                else
                    message = "Could not load document.";
                throw new XamlLoadException(message);
            }

            _rootItem = _componentService.RegisterXamlComponentRecursive(_doc.RootElement);
        }

        public override void Save(XmlWriter writer)
        {
            _doc.Save(writer);
        }

        public override DesignItem RootItem {
            get { return _rootItem; }
        }

        public XamlParserSettings ParserSettings {
            get { return _parserSettings; }
        }

        public override ChangeGroup OpenGroup(string changeGroupTitle, ICollection<DesignItem> affectedItems)
        {
            if (affectedItems == null)
                throw new ArgumentNullException("affectedItems");

            UndoService undoService = this.Services.GetRequiredService<UndoService>();
            var g = undoService.StartTransaction(affectedItems);
            g.Title = changeGroupTitle;
            return g;
        }
    }

    /// <summary>
    /// Minimal IComponentPropertyService: reflects System.ComponentModel.TypeDescriptor
    /// properties/events for the design item's component type. No WPF-specific dependency
    /// in the reference implementation this is modelled on beyond TypeDescriptor itself.
    /// </summary>
    sealed class ComponentPropertyService : IComponentPropertyService
    {
        public IEnumerable<System.ComponentModel.MemberDescriptor> GetAvailableProperties(DesignItem designItem)
        {
            foreach (System.ComponentModel.PropertyDescriptor p in System.ComponentModel.TypeDescriptor.GetProperties(designItem.Component))
                yield return p;
        }

        public IEnumerable<System.ComponentModel.MemberDescriptor> GetAvailableEvents(DesignItem designItem)
        {
            foreach (System.ComponentModel.EventDescriptor e in System.ComponentModel.TypeDescriptor.GetEvents(designItem.Component))
                yield return e;
        }

        public IEnumerable<System.ComponentModel.MemberDescriptor> GetCommonAvailableProperties(IEnumerable<DesignItem> designItems)
        {
            System.Collections.Generic.HashSet<string> commonNames = null;
            var all = new List<System.ComponentModel.PropertyDescriptor>();
            foreach (var item in designItems) {
                var props = System.ComponentModel.TypeDescriptor.GetProperties(item.Component);
                var names = new System.Collections.Generic.HashSet<string>();
                foreach (System.ComponentModel.PropertyDescriptor p in props) {
                    names.Add(p.Name);
                    all.Add(p);
                }
                commonNames = commonNames == null ? names : new System.Collections.Generic.HashSet<string>(System.Linq.Enumerable.Where(commonNames, n => names.Contains(n)));
            }
            if (commonNames == null)
                yield break;
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var p in all) {
                if (commonNames.Contains(p.Name) && seen.Add(p.Name))
                    yield return p;
            }
        }
    }
}
