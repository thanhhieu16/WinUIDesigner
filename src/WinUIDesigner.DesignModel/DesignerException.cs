using System;

namespace WinUIDesigner.DesignModel
{
    public class DesignerException : Exception
    {
        public DesignerException() { }
        public DesignerException(string message) : base(message) { }
        public DesignerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
