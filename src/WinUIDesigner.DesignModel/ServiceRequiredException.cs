using System;

namespace WinUIDesigner.DesignModel
{
    public class ServiceRequiredException : DesignerException
    {
        public Type ServiceType { get; private set; }

        public ServiceRequiredException(Type serviceType)
            : base("Service " + serviceType.FullName + " is required.")
        {
            this.ServiceType = serviceType;
        }

        public ServiceRequiredException() { }
        public ServiceRequiredException(string message) : base(message) { }
        public ServiceRequiredException(string message, Exception innerException) : base(message, innerException) { }
    }
}
