using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nilam
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            string profile = ProfileSuffix();
            string exitSignal = AppInfo.Name + ".Exit" + profile;

            // "Nilam.exe --exit" asks the running copy to save and
            // close. The installer and uninstaller use it so that replacing the
            // exe does not mean killing a process that may be mid-write, and so
            // the notification icon is removed properly instead of lingering.
            if (Array.IndexOf(args, "--exit") >= 0)
            {
                EventWaitHandle running;
                if (EventWaitHandle.TryOpenExisting(exitSignal, out running))
                {
                    using (running) running.Set();
                }
                return;
            }

            // Single instance: a second launch (the startup entry plus a manual
            // start, say) would stack a duplicate set of cards on the desktop.
            bool isFirst;
            using (Mutex guard = new Mutex(true, AppInfo.Name + ".SingleInstance" + profile, out isFirst))
            {
                if (!isFirst) return;

                Log.Start(Store.DefaultDir());

                // Nothing here can show an error - there is no window and no
                // console - so anything that escapes is at least written down.
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Crash("unhandled", e.ExceptionObject as Exception);
                };
                TaskScheduler.UnobservedTaskException += delegate(object s, UnobservedTaskExceptionEventArgs e)
                {
                    Log.Error("task", e.Exception);
                    e.SetObserved();
                };

                // Render in software. Hardware rendering maps the Intel GPU driver
                // stack (igc64, media_bin, igddxvacommon, igd12um - about 125 MB of
                // images) into the process, which measured as 81 MB of extra private
                // bytes. A handful of near-static cards do not need a GPU, and this
                // is by far the largest single saving available.
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

                WidgetApp app = new WidgetApp(new Store());
                app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
                {
                    // A failure in one timer tick or click handler is logged and
                    // survived. Letting it through would take every card down
                    // for the sake of one.
                    Log.Crash("ui", e.Exception);
                    e.Handled = true;
                };

                using (EventWaitHandle exit = new EventWaitHandle(false, EventResetMode.AutoReset, exitSignal))
                {
                    // The callback arrives on a thread-pool thread.
                    RegisteredWaitHandle wait = ThreadPool.RegisterWaitForSingleObject(exit,
                        delegate(object state, bool timedOut)
                        {
                            app.Dispatcher.BeginInvoke(new Action(app.Quit));
                        },
                        null, Timeout.Infinite, true);
                    app.Run();
                    wait.Unregister(null);
                }

                GC.KeepAlive(guard);
            }
        }

        /// <summary>
        /// An instance pointed at another state folder is a different profile,
        /// and gets its own lock so it can run beside the installed copy. The
        /// suffix is a hash of the folder because a mutex name cannot hold a path.
        /// </summary>
        private static string ProfileSuffix()
        {
            if (!Store.UsingOverride) return "";
            string dir = Store.DefaultDir().ToUpperInvariant();
            uint h = 2166136261;                    // FNV-1a, 32-bit
            for (int i = 0; i < dir.Length; i++)
            {
                h ^= dir[i];
                h *= 16777619;
            }
            return "." + h.ToString("X8", CultureInfo.InvariantCulture);
        }
    }
}
