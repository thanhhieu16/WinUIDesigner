using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace WinUIDesigner.TestHost
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
							// The TypeDescriptor/System.Windows.Forms first-call quirk that used to be
							// warmed up here (see git history for the removed WarmUpTypeDescriptor
							// method) is now absorbed by the library itself, once per process, in
							// WinUIDesigner.XamlDom.XamlParser.EnsureTypeDescriptorInitialized - every
							// test in this suite reaches TypeDescriptor only through XamlParser.Parse,
							// so no test-side warm-up is needed any more. Verified by running the full
							// suite after removing this call: still deterministic, no
							// FileNotFoundException for System.Windows.Forms anywhere.
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
				System.Reflection.Assembly.GetEntryAssembly()?.Location ?? System.Reflection.Assembly.GetExecutingAssembly().Location);
			var actual = System.IO.Path.GetFileNameWithoutExtension(CurrentHostName());
			return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
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

		/// <summary>
		/// Like <see cref="Run"/>, for a body that awaits WinRT async operations (such as
		/// <c>RenderTargetBitmap.RenderAsync</c>). The body starts on the dispatcher thread, its
		/// continuations are posted back to that thread's queue, and the caller blocks until the
		/// returned task completes. Blocking inside <see cref="Run"/> instead would deadlock, since
		/// the continuation needs the very thread the body would be holding.
		/// </summary>
		public static void RunAsync(Func<System.Threading.Tasks.Task> body)
		{
			EnsureStarted();

			if (queue == null) {
				NUnit.Framework.Assert.Ignore(
					"No WinUI application thread: this host cannot activate Microsoft.UI.Xaml classes, so the " +
					"test was skipped rather than failed. Underlying error: " + (startupFailure?.Message ?? "unknown"));
			}

			ExceptionDispatchInfo captured = null;
			var done = new ManualResetEventSlim();
			if (!queue.TryEnqueue(async () => {
				try {
					if (!(System.Threading.SynchronizationContext.Current is Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext))
						System.Threading.SynchronizationContext.SetSynchronizationContext(
							new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(queue));
					await body();
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

		/// <summary>
		/// <see cref="RunAsync"/> with the shared live window, whose <c>Content</c> is reset to null
		/// afterwards, as <see cref="RunWithWindow"/> does.
		/// </summary>
		public static void RunWithWindowAsync(Func<Microsoft.UI.Xaml.Window, System.Threading.Tasks.Task> body)
		{
			var window = EnsureWindow();
			try {
				RunAsync(() => body(window));
			} finally {
				Run(() => window.Content = null);
			}
		}

		/// <summary>
		/// Runs <paramref name="action"/> on the WinUI dispatcher thread with a real, activated
		/// <see cref="Microsoft.UI.Xaml.Window"/> whose XAML island is already live - needed for
		/// any test that depends on a real layout pass (ActualWidth/ActualHeight,
		/// TransformToVisual, VisualTreeHelper.FindElementsInHostCoordinates all require an
		/// element to be connected to a live XamlRoot). Assigning <c>window.Content</c> inside
		/// <paramref name="action"/> attaches the tree synchronously, so the usual
		/// <c>window.Content = root; root.UpdateLayout();</c> then assert works.
		/// <see cref="Microsoft.UI.Xaml.Window.Content"/> is reset to null afterwards, even if
		/// <paramref name="action"/> throws.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Verified empirically (Phase 3, Task 1) on Windows App SDK 1.7.250606001, unpackaged +
		/// registration-free activation. The naive version of this method - create a
		/// <c>Window</c>, <c>Activate()</c> it and run the test body in the same dispatcher
		/// turn - does NOT work, for two independent reasons, both confirmed by direct
		/// measurement rather than inference:
		/// </para>
		/// <para>
		/// 1. The host is fine. <c>WindowNative.GetWindowHandle</c> returns a real HWND
		/// (class <c>WinUIDesktopWin32WindowClass</c>) that <c>IsWindow</c>/<c>IsWindowVisible</c>
		/// confirm, <c>Window.Compositor</c> and <c>Window.AppWindow</c> are non-null and
		/// <c>CompositionTarget.Rendering</c> ticks at ~60Hz. What is asynchronous is the
		/// *first* <c>Window.Content</c> assignment: XAML connects the content to the window's
		/// island on a later turn of the message loop, so <c>XamlRoot</c> stays null (and
		/// <c>ActualWidth</c> zero, and <c>Loaded</c> unraised) for the whole turn that set it.
		/// No amount of <c>UpdateLayout</c> helps; only returning to the loop does. Hence the
		/// one-time warm-up below, which pays that cost once per process. Every later
		/// <c>Content</c> assignment - including the ones the test bodies make - then attaches
		/// synchronously, because the island and XamlRoot already exist.
		/// </para>
		/// <para>
		/// 2. The window must never be closed. Closing the process's last live WinUI
		/// <c>Window</c> ends <c>Application.Start</c>'s message loop, which tears down the
		/// shared DispatcherQueue: every later <see cref="Run"/> then fails with "Could not
		/// enqueue work onto the WinUI dispatcher thread", the host thread exits and NUnitLite
		/// dies before printing any results (silent exit code 0, no output at all). So one
		/// window is created per process, reused by every call, and left open - the process
		/// exiting is what disposes of it.
		/// </para>
		/// <para>
		/// Note for callers: <c>FindElementsInHostCoordinates</c> wants the point in *island*
		/// coordinates, which is what <c>element.TransformToVisual(null)</c> produces.
		/// <c>TransformToVisual(XamlRoot.Content)</c> is NOT the same thing and silently returns
		/// no hits whenever the root content is not itself at the island origin - which is the
		/// normal case here, since a <c>Grid</c> with an explicit Width/Height gets centred in
		/// the window (measured: ActualOffset &lt;562,260&gt; in a 1424x720 island).
		/// </para>
		/// <para>
		/// Verified only on an interactive desktop session with a live window station (WinSta0) and
		/// DWM running. Headless, session-0, or otherwise disconnected sessions are unconfirmed -
		/// whoever wires up CI for this suite should re-verify this mechanism there first.
		/// </para>
		/// </remarks>
		public static void RunWithWindow(Action<Microsoft.UI.Xaml.Window> action)
		{
			var window = EnsureWindow();
			try {
				Run(() => action(window));
			} finally {
				// Drop the test's tree so it cannot leak into the next test. Run() rather than a
				// bare assignment: Content has thread affinity like everything else here.
				Run(() => window.Content = null);
			}
		}

		/// <summary>
		/// Like <see cref="RunWithWindow"/>, but leaves <c>Window.Content</c> attached afterwards, so a
		/// fixture can build one tree in its setup and keep driving that same live tree across several
		/// tests. Callers own the cleanup: whatever they attach stays attached until another call
		/// replaces it, so a fixture that uses this must not leave a tree behind that a later fixture
		/// would inherit.
		/// </summary>
		/// <remarks>
		/// Needed because detaching and re-attaching a tree between tests does not reliably re-run
		/// layout on it: measured on a re-attached MenuBar, ActualHeight stayed 0 for the whole turn
		/// even after an explicit UpdateLayout().
		/// </remarks>
		public static void RunOnWindow(Action<Microsoft.UI.Xaml.Window> action)
		{
			var window = EnsureWindow();
			Run(() => action(window));
		}

		static readonly object windowGate = new object();
		static Microsoft.UI.Xaml.Window sharedWindow;

		/// <summary>
		/// Creates the process-wide test window on first use and pumps the message loop until its
		/// XAML island is live, so callers can rely on <c>Window.Content</c> attaching
		/// synchronously. See <see cref="RunWithWindow"/> for why this is needed.
		/// </summary>
		static Microsoft.UI.Xaml.Window EnsureWindow()
		{
			lock (windowGate) {
				if (sharedWindow != null)
					return sharedWindow;

				Microsoft.UI.Xaml.Window created = null;
				// Run() (not this method) is what reports an unusable host, via Assert.Ignore.
				Run(() => {
					created = new Microsoft.UI.Xaml.Window();
					// Any content will do; assigning it is what makes XAML build the island.
					created.Content = new Microsoft.UI.Xaml.Controls.Grid();
					created.Activate();
				});

				// The island is connected on a later turn of the loop, so give the loop turns:
				// each Run() round-trips through it. Measured: ready after two round-trips.
				var deadline = DateTime.UtcNow.AddSeconds(30);
				var live = false;
				while (!live && DateTime.UtcNow < deadline) {
					Run(() => live = ((Microsoft.UI.Xaml.FrameworkElement)created.Content).XamlRoot != null);
					if (!live)
						Thread.Sleep(10);
				}
				if (!live)
					throw new TimeoutException(
						"The test window's XAML island never became live: Window.Content.XamlRoot stayed null " +
						"for 30 seconds after Activate(). Live-layout tests cannot run in this session.");

				// Park it off-screen now that the island is established, so a test run does not
				// leave a window sitting on top of the desktop holding the foreground. Verified
				// to change nothing about layout or hit-testing (XamlRoot.IsHostVisible stays
				// true and FindElementsInHostCoordinates still resolves).
				Run(() => {
					try {
						created.AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
					} catch (Exception) {
						// Purely cosmetic - an on-screen window works just as well.
					}
				});

				sharedWindow = created;
				return sharedWindow;
			}
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
			Assembly.GetEntryAssembly() ?? typeof(TestApplication).Assembly,
			typeof(TestApplication).Assembly);

		public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(Type type) => provider.GetXamlType(type);

		public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(string fullName) => provider.GetXamlType(fullName);

		public Microsoft.UI.Xaml.Markup.XmlnsDefinition[] GetXmlnsDefinitions() => provider.GetXmlnsDefinitions();
	}
}
