using System;
using System.IO;

namespace WinUIDesigner.Designer.Services
{
    // TEMPORARY walkthrough diagnostic for Cut/Copy/Paste; removed once the cause is found.
    public static class DiagTrace
    {
        public static void Write(string message)
        {
            var path = Environment.GetEnvironmentVariable("WINUIDESIGNER_TRACE");
            if (string.IsNullOrEmpty(path))
                return;
            try { File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine); } catch { }
        }
    }
}
