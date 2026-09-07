using System.Reflection;

namespace WinUIDesigner.Shell.Tests
{
    /// <summary>
    /// NUnitLite entry point. See WinUIDesigner.Designer.Tests/Program.cs for why this project
    /// hosts NUnit itself instead of relying on dotnet test.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            return new NUnitLite.AutoRun(Assembly.GetExecutingAssembly()).Execute(args);
        }
    }
}
