using System;
using System.Reflection;

namespace WinUIDesigner.Designer.Tests
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			return new NUnitLite.AutoRun(Assembly.GetExecutingAssembly()).Execute(args);
		}
	}
}
