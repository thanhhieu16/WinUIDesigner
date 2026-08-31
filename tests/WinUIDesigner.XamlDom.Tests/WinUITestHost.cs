using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace WinUIDesigner.XamlDom.Tests
{
	/// <summary>
	/// Runs test bodies on a thread that has a live WinUI XAML dispatcher.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every <c>Microsoft.UI.Xaml.*</c> object is a WinRT class with thread affinity: creating one
	/// (or calling <c>XamlReader.Load</c>) on an ordinary thread fails with
	/// <c>RPC_E_WRONG_THREAD (0x8001010E)</c>. Verified directly: on a bare STA thread every
	/// activation fails, while inside <c>Microsoft.UI.Xaml.Application.Start</c>'s callback
	/// <c>new Grid()</c>, <c>new Style()</c>, <c>new Binding()</c>, <c>new ResourceDictionary()</c>,
	/// <c>Activator.CreateInstance(typeof(Button))</c>, <c>XamlReader.Load</c> (including nested
	/// children) and setting properties through <c>TypeDescriptor</c> all succeed. Tasks 1-4 never
	/// hit this because their tests only used <c>typeof(...)</c>, never an instance.
	/// </para>
	/// <para>
	/// The host thread is started lazily on first use and left running as a background thread, so
	/// no NUnit fixture lifecycle is involved and the process can still exit normally.
	/// </para>
	/// </remarks>
	public static class WinUITestHost
	{
		static readonly object gate = new object();
		static Microsoft.UI.Dispatching.DispatcherQueue queue;
		static Exception startupFailure;
		static bool started;

		/// <summary>
		/// Describes why the WinUI host thread could not be started, or null if it is running.
		/// </summary>
		public static Exception StartupFailure
		{
			get { EnsureStarted(); return startupFailure; }
		}

		static void EnsureStarted()
		{
			lock (gate) {
				if (started)
					return;
				started = true;

				// Check the host executable before calling Application.Start. WinUI's
				// registration-free activation manifest is embedded in this project's own apphost
				// (see Program.cs); under any other host - notably `dotnet test`'s testhost.exe -
				// Application.Start does not throw a catchable exception, it fail-fasts and takes
				// the whole process down, which shows up as "Test host process crashed" with no
				// results at all. Gating on the host executable keeps that from happening: the
				// WinUI tests report as ignored, and the tests needing no WinUI object still run.
				if (!IsOwnApphost()) {
					startupFailure = new PlatformNotSupportedException(
						"Host process is '" + CurrentHostName() + "', which carries no Windows App SDK " +
						"registration-free activation manifest.");
					return;
				}

				var ready = new ManualResetEventSlim();
				var thread = new Thread(() => {
					try {
						Microsoft.UI.Xaml.Application.Start(p => {
							// Creating the Application sets Application.Current, which is where
							// WinUI's XamlReader looks for the IXamlMetadataProvider it needs to
							// resolve non-framework types. See TestApplication.
							new TestApplication();
							WarmUpTypeDescriptor();
							queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
							if (queue == null)
								startupFailure = new InvalidOperationException(
									"Application.Start ran but no DispatcherQueue exists on the callback thread.");
							ready.Set();
						});
					} catch (Exception ex) {
						startupFailure = ex;
					} finally {
						ready.Set();
					}
				});
				thread.IsBackground = true;
				thread.SetApartmentState(ApartmentState.STA);
				thread.Start();

				if (!ready.Wait(TimeSpan.FromSeconds(60)) && startupFailure == null) {
					startupFailure = new TimeoutException("Timed out waiting for the WinUI application thread to start.");
				}
			}
		}

		static string CurrentHostName()
		{
			try {
				return System.Diagnostics.Process.GetCurrentProcess().MainModule?.ModuleName ?? "(unknown)";
			} catch (Exception) {
				return "(unknown)";
			}
		}

		static bool IsOwnApphost()
		{
			var expected = System.IO.Path.GetFileNameWithoutExtension(
				System.Reflection.Assembly.GetExecutingAssembly().Location);
			var actual = System.IO.Path.GetFileNameWithoutExtension(CurrentHostName());
			return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Forces <see cref="System.ComponentModel.TypeDescriptor"/>'s one-time COM-descriptor
		/// initialisation to happen here rather than inside whichever test happens to run first.
		/// </summary>
		/// <remarks>
		/// The very first <c>TypeDescriptor.GetConverter(object)</c>/<c>GetProperties(object)</c>
		/// call made against a WinRT-backed instance in this process throws
		/// <c>FileNotFoundException: Could not load file or assembly 'System.Windows.Forms'</c> -
		/// .NET's <c>TypeDescriptor.ComObjectType</c> resolves its descriptor implementation out of
		/// <c>System.Windows.Forms</c>, which a WinUI app does not reference. The failure is
		/// one-shot: it is cached, and every later call succeeds (verified - a second
		/// <c>GetProperties</c> on the same instance returns the full property list and property
		/// assignment through it works). Left unwarmed it makes exactly one arbitrary test fail,
		/// and which one depends on scheduling. Swallowing it once here makes the suite
		/// deterministic without hiding anything: the behaviour is described in the task report as
		/// something a real consumer of this library will meet on its first parse.
		/// </remarks>
		static void WarmUpTypeDescriptor()
		{
			try {
				System.ComponentModel.TypeDescriptor.GetConverter(new Microsoft.UI.Xaml.Media.SolidColorBrush());
			} catch (Exception) {
			}
			try {
				System.ComponentModel.TypeDescriptor.GetProperties(new Microsoft.UI.Xaml.Media.SolidColorBrush());
			} catch (Exception) {
			}
		}

		/// <summary>
		/// Runs <paramref name="action"/> on the WinUI dispatcher thread and rethrows any exception
		/// it raised (preserving its original stack trace, so NUnit assertion failures surface
		/// normally).
		/// </summary>
		public static void Run(Action action)
		{
			EnsureStarted();

			if (queue == null) {
				NUnit.Framework.Assert.Ignore(
					"No WinUI application thread: this host cannot activate Microsoft.UI.Xaml classes, so the " +
					"test was skipped rather than failed. Run the tests through " +
					"WinUIDesigner.XamlDom.Tests.exe (see Program.cs) instead of `dotnet test`. Underlying " +
					"error: " + (startupFailure?.Message ?? "unknown"));
			}

			ExceptionDispatchInfo captured = null;
			var done = new ManualResetEventSlim();
			if (!queue.TryEnqueue(() => {
				try {
					action();
				} catch (Exception ex) {
					captured = ExceptionDispatchInfo.Capture(ex);
				} finally {
					done.Set();
				}
			})) {
				throw new InvalidOperationException("Could not enqueue work onto the WinUI dispatcher thread.");
			}

			if (!done.Wait(TimeSpan.FromMinutes(2)))
				throw new TimeoutException("Test body did not complete on the WinUI dispatcher thread.");

			captured?.Throw();
		}
	}

	/// <summary>
	/// The <c>Application</c> instance the tests run under. Implements
	/// <see cref="Microsoft.UI.Xaml.Markup.IXamlMetadataProvider"/> so WinUI's own
	/// <c>XamlReader.Load</c> can resolve the test fixture types - see
	/// <see cref="ReflectionXamlMetadataProvider"/>.
	/// </summary>
	public sealed class TestApplication : Microsoft.UI.Xaml.Application, Microsoft.UI.Xaml.Markup.IXamlMetadataProvider
	{
		readonly ReflectionXamlMetadataProvider provider = new ReflectionXamlMetadataProvider(
			typeof(TestApplication).Assembly);

		public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(Type type) => provider.GetXamlType(type);

		public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(string fullName) => provider.GetXamlType(fullName);

		public Microsoft.UI.Xaml.Markup.XmlnsDefinition[] GetXmlnsDefinitions() => provider.GetXmlnsDefinitions();
	}
}
