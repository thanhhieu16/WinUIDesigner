using System;
using System.IO;

namespace WinUIDesigner.Designer
{
    /// <summary>
    /// TEMPORARY walkthrough diagnostics: appends a line to the file named by the
    /// WINUIDESIGNER_TRACE environment variable, and does nothing when it is unset.
    /// </summary>
    public static class DiagTrace
    {
        static readonly string path = Environment.GetEnvironmentVariable("WINUIDESIGNER_TRACE");

        public static void Write(string message)
        {
            if (path == null)
                return;
            try { File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine); }
            catch (IOException) { }
        }
    }
}
