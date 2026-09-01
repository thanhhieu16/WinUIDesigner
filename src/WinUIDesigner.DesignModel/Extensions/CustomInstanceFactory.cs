using System;

namespace WinUIDesigner.DesignModel.Extensions
{
    [ExtensionServer(typeof(NeverApplyExtensionsExtensionServer))]
    public class CustomInstanceFactory : Extension
    {
        public static readonly CustomInstanceFactory DefaultInstanceFactory = new CustomInstanceFactory();

        protected CustomInstanceFactory() { }

        /// <summary>
        /// Creates an instance of the specified type, passing the specified arguments to its constructor.
        /// Unlike the WPF reference, this does not mark the instance as design-time via
        /// DesignerProperties.SetIsInDesignMode - WinUI has no per-instance equivalent
        /// (Windows.ApplicationModel.DesignMode.DesignModeEnabled is process-wide and read-only).
        /// </summary>
        public virtual object CreateInstance(Type type, params object[] arguments)
        {
            return Activator.CreateInstance(type, arguments);
        }
    }
}
