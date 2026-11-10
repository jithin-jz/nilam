using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nilam
{
    /// <summary>
    /// The settings window runs in a process of its own - "Nilam.exe --settings" -
    /// which ends when the window closes.
    ///
    /// Measured on Windows 11 at 150%: opening the settings window inside the
    /// widgets' process added about 7 MB to its private working set, and none of
    /// it came back after the window closed - not even after a full garbage
    /// collection, with the window object itself collected. WPF's software
    /// renderer keeps the largest drawing buffer it has needed (the whole window,
    /// 998 x 1000 pixels: 3.8 MB), and the heap keeps the room it grew into. The
    /// widgets alone need about 20 MB, so one look at the settings cost them a
    /// third more for the rest of the session. In a process of its own, all of
    /// that goes back to Windows when the window closes.
    ///
    /// The two processes share the settings file and two named events: one
    /// brings an open settings window to the front, the other tells the widgets
    /// to reload the settings after a save.
    /// </summary>
    internal static class SettingsHost
    {
        internal const string Argument = "--settings";

        /// <summary>Set by the settings process after a save; the widgets reload when it is.</summary>
        internal static string ReloadSignal(string profile)
        {
            return AppInfo.Name + ".Reload" + profile;
        }

        private static string ShowSignal(string profile)
        {
            return AppInfo.Name + ".Settings.Show" + profile;
        }

        /// <summary>
        /// In the widgets' process: opens the settings window, or brings the one
        /// already open to the front. The new process inherits this one's
        /// environment, so a scratch profile (NILAM_STATE_DIR) opens its own
        /// settings.
        /// </summary>
        internal static void Open()
        {
            try
            {
                // The click that led here lets this process put a window in
                // front. The window will belong to another process - the new
                // one, or a settings process already open - so pass that on.
                Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                ProcessStartInfo psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, Argument);
                psi.UseShellExecute = false;
                using (Process.Start(psi)) { }
            }
            catch (Exception ex)
            {
                Log.Error("settings", ex);
            }
        }

        /// <summary>The whole of a --settings process. Returns when the window has closed.</summary>
        internal static void Run(string profile)
        {
            bool isFirst;
            using (Mutex guard = new Mutex(true, AppInfo.Name + ".Settings" + profile, out isFirst))
            {
                if (!isFirst)
                {
                    // Already open: bring that one forward rather than open a second.
                    EventWaitHandle show;
                    if (EventWaitHandle.TryOpenExisting(ShowSignal(profile), out show))
                    {
                        using (show) show.Set();
                    }
                    return;
                }

                Log.Start(Store.DefaultDir());
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Crash("settings", e.ExceptionObject as Exception);
                };
                // As in the widgets' process; see Program.
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

                Store store = new Store();
                store.LoadSettings();

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
                {
                    Log.Crash("settings", e.Exception);
                    e.Handled = true;
                };

                SettingsWindow window = null;
                using (EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignal(profile)))
                {
                    // Called on a thread-pool thread.
                    RegisteredWaitHandle wait = ThreadPool.RegisterWaitForSingleObject(show,
                        delegate(object state, bool timedOut)
                        {
                            app.Dispatcher.BeginInvoke(new Action(delegate { if (window != null) window.Activate(); }));
                        },
                        null, Timeout.Infinite, false);

                    app.Startup += delegate
                    {
                        window = new SettingsWindow(store, delegate(Settings saved) { Save(store, saved, profile); });
                        window.Closed += delegate { app.Shutdown(); };
                        window.Show();
                    };
                    app.Run();
                    wait.Unregister(null);
                }
                GC.KeepAlive(guard);
            }
        }

        private static void Save(Store store, Settings saved, string profile)
        {
            store.Settings = saved;
            store.SaveSettings();
            // With no widgets running there is nobody to tell; they read the
            // file when they start.
            EventWaitHandle reload;
            if (EventWaitHandle.TryOpenExisting(ReloadSignal(profile), out reload))
            {
                using (reload) reload.Set();
            }
        }
    }
}
