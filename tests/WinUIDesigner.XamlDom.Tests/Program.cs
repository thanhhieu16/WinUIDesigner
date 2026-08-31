using System;
using System.Reflection;

namespace WinUIDesigner.XamlDom.Tests
{
	/// <summary>
	/// NUnitLite entry point.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This project builds as an executable and hosts NUnit itself instead of relying only on
	/// <c>dotnet test</c>'s <c>testhost.exe</c>. That is not a stylistic choice: WinUI's
	/// registration-free WinRT activation manifest (the thing that makes
	/// <c>WindowsAppSDKSelfContained</c> work without an installed Windows App SDK runtime) is
	/// <b>embedded into the project's own apphost executable</b> at build time - verified: the
	/// build produces <c>obj\...\Manifests\app.manifest</c> listing every activatable class, and it
	/// is linked into the .exe. A class-library test project has no apphost, so
	/// <c>testhost.exe</c> - which ships pre-built with an embedded manifest of its own and
	/// therefore ignores a side-by-side <c>testhost.exe.manifest</c> (confirmed: dropping one next
	/// to it changes nothing) - cannot activate any <c>Microsoft.UI.Xaml</c> class and the test
	/// host process crashes outright.
	/// </para>
	/// <para>
	/// Running the tests through this executable gives them a host that does carry the manifest.
	/// </para>
	/// </remarks>
	public static class Program
	{
		public static int Main(string[] args)
		{
			return new NUnitLite.AutoRun(Assembly.GetExecutingAssembly()).Execute(args);
		}
	}
}
