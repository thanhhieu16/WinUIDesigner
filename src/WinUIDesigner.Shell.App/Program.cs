using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The entry point, replacing the one the XAML compiler generates (DISABLE_XAML_GENERATED_MAIN
    /// in the project file) so the UI thread is guaranteed to be single-threaded.
    /// </summary>
    /// <remarks>
    /// Measured on the walkthrough runner with the generated Main: the UI thread was in the
    /// multithreaded apartment (CoGetApartmentType type 1, managed ApartmentState MTA). OLE needs a
    /// single-threaded apartment, so everything built on it failed - the WinRT clipboard threw
    /// CO_E_NOTINITIALIZED, and drag-and-drop goes through the same OLE layer. So Main checks the
    /// apartment it was given and, if it is not single-threaded, runs the application on a thread
    /// that is: an apartment cannot be changed once a thread has joined one. The body is the
    /// generated Main's, unchanged.
    /// </remarks>
    public static class Program
    {
        [DllImport("Microsoft.ui.xaml.dll")]
        static extern void XamlCheckProcessRequirements();

        [STAThread]
        static void Main(string[] args)
        {
            App.Trace("Main: managed apartment=" + Thread.CurrentThread.GetApartmentState());
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                Run();
                return;
            }

            var ui = new Thread(Run);
            ui.SetApartmentState(ApartmentState.STA);
            ui.Start();
            ui.Join();
        }

        static void Run()
        {
            XamlCheckProcessRequirements();
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
    }
}
