using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Nilam
{
    /// <summary>
    /// The clear taskbar: see-through while the desktop is all there is to see,
    /// back to normal as soon as a window, Start, Search or Task View is open.
    ///
    /// Windows 11 draws the taskbar inside Explorer, so the drawing is changed
    /// from in there - by bin\taskbar\NilamTaskbar.dll, which
    /// NilamTaskbarLoader.exe loads into Explorer (taskbar\*.cpp explain how).
    /// This class is the half that stays in Nilam: it watches windows open,
    /// close, minimise and move, decides per monitor whether its taskbar should
    /// be clear, and sends that decision to the agent. The agent puts every
    /// taskbar back by itself when this process ends, however it ends.
    ///
    /// Nothing here runs while the setting is off: no hooks, no timer, nothing
    /// loaded into Explorer.
    /// </summary>
    internal sealed class TaskbarStyle : IDisposable
    {
        // ---- the protocol: keep in step with taskbar\Protocol.h --------------
        private const string AgentClass = "NilamTaskbarAgent.1";
        private const uint Magic = 0x4E4C5442;      // "NLTB"
        private const uint OpSet = 1;
        private const uint OpRestoreAll = 2;
        private const uint FlagClear = 1;
        private const uint FlagHideLine = 2;
        private const int ResultApplied = 1;

        internal const string DllName = "NilamTaskbar.dll";
        internal const string LoaderName = "NilamTaskbarLoader.exe";

        /// <summary>Windows 11 - the first Windows whose taskbar is drawn with XAML.</summary>
        private const int FirstBuild = 22000;

        /// <summary>How long Explorer must have been running before the agent is loaded into it.</summary>
        private static readonly TimeSpan ExplorerSettle = TimeSpan.FromSeconds(45);

        // WinEvent ids. Each pair is one hook covering the range.
        private static readonly uint[,] EventRanges =
        {
            { 0x0003, 0x0003 },     // EVENT_SYSTEM_FOREGROUND
            { 0x000B, 0x000B },     // EVENT_SYSTEM_MOVESIZEEND - dragged to another monitor
            { 0x0016, 0x0017 },     // EVENT_SYSTEM_MINIMIZESTART, MINIMIZEEND
            { 0x8001, 0x8003 },     // EVENT_OBJECT_DESTROY, SHOW, HIDE
            { 0x8017, 0x8018 },     // EVENT_OBJECT_CLOAKED, UNCLOAKED
        };

        private readonly Dispatcher dispatcher;
        // Held in a field: the hooks keep only a native pointer to it, and a
        // collected delegate would be called into freed memory.
        private readonly Native.WinEventProc onEvent;
        private readonly List<IntPtr> hooks = new List<IntPtr>();
        private readonly DispatcherTimer soon;
        private readonly DispatcherTimer backstop;
        private readonly DispatcherTimer reload;
        // The last state each taskbar was successfully given: true for clear.
        private readonly Dictionary<IntPtr, bool> sent = new Dictionary<IntPtr, bool>();
        private readonly uint ownPid;
        private HwndSource window;
        private int taskbarCreated;
        private bool enabled;
        private bool loading;
        private bool gaveUp;
        private int failures;
        private int quickRestarts;
        private DateTime loadedAt = DateTime.MinValue;

        internal TaskbarStyle(Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
            ownPid = (uint)Process.GetCurrentProcess().Id;

            onEvent = OnWinEvent;

            // A burst of window events - an app opening several windows, a
            // drag - becomes one look at the desktop, at most 20 times a second.
            soon = new DispatcherTimer();
            soon.Interval = TimeSpan.FromMilliseconds(50);
            soon.Tick += delegate { soon.Stop(); Evaluate(false); };

            // The safety net, like the cards' own 3-second timer: a window that
            // changed in a way no event reported, an agent that went away.
            backstop = new DispatcherTimer();
            backstop.Interval = TimeSpan.FromSeconds(3);
            backstop.Tick += delegate { Evaluate(false); };

            reload = new DispatcherTimer();
            reload.Tick += delegate { reload.Stop(); Load(); };
        }

        // ------------------------------------------------------- availability
        private static string Folder
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "taskbar"); }
        }

        /// <summary>
        /// Null when the clear taskbar can work here; otherwise a sentence
        /// saying why not, for the settings window.
        /// </summary>
        internal static string Unavailable()
        {
            if (Environment.OSVersion.Version.Major < 10 || Environment.OSVersion.Version.Build < FirstBuild)
                return "Needs Windows 11";
            // Explorer is 64-bit, and the agent is built for Intel and AMD
            // processors. This process is 32-bit, so the machine's own
            // architecture is in PROCESSOR_ARCHITEW6432.
            string arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432");
            if (string.IsNullOrEmpty(arch)) arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
            if (!string.Equals(arch, "AMD64", StringComparison.OrdinalIgnoreCase))
                return "Needs 64-bit Windows on an Intel or AMD processor";
            if (!File.Exists(Path.Combine(Folder, DllName)) || !File.Exists(Path.Combine(Folder, LoaderName)))
                return "Not included in this copy of Nilam - it comes with the published releases";
            return null;
        }

        // ------------------------------------------------------------ on / off
        internal void SetEnabled(bool on)
        {
            if (on && Unavailable() != null) on = false;
            if (on == enabled) return;
            enabled = on;
            if (on) Start();
            else Stop();
        }

        private void Start()
        {
            gaveUp = false;
            failures = 0;
            quickRestarts = 0;
            sent.Clear();

            // A top-level window, because TaskbarCreated - Explorer announcing
            // a new taskbar after it restarted - is broadcast only to those.
            HwndSourceParameters p = new HwndSourceParameters(AppInfo.Name + "Taskbar");
            p.Width = 0;
            p.Height = 0;
            p.WindowStyle = unchecked((int)0x80000000);     // WS_POPUP, never shown
            p.ExtendedWindowStyle = 0x08000080;             // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            window = new HwndSource(p);
            window.AddHook(WndProc);
            taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

            for (int i = 0; i < EventRanges.GetLength(0); i++)
            {
                IntPtr hook = Native.SetWinEventHook(EventRanges[i, 0], EventRanges[i, 1], IntPtr.Zero, onEvent,
                                                     0, 0, 0 /* WINEVENT_OUTOFCONTEXT */);
                if (hook != IntPtr.Zero) hooks.Add(hook);
            }
            if (hooks.Count < EventRanges.GetLength(0))
                Log.Warn("taskbar", "some window events could not be watched; the taskbar may lag by up to three seconds");

            backstop.Start();
            Log.Info("taskbar", "clear taskbar on");
            Evaluate(true);
        }

        private void Stop()
        {
            soon.Stop();
            backstop.Stop();
            reload.Stop();
            foreach (IntPtr hook in hooks) Native.UnhookWinEvent(hook);
            hooks.Clear();

            IntPtr agent = Native.FindMessageWindow(AgentClass);
            if (agent != IntPtr.Zero) Send(agent, OpRestoreAll, IntPtr.Zero, 0, 0);
            sent.Clear();

            if (window != null)
            {
                window.RemoveHook(WndProc);
                window.Dispose();
                window = null;
            }
            Log.Info("taskbar", "clear taskbar off");
        }

        public void Dispose()
        {
            SetEnabled(false);
        }

        // ------------------------------------------------------------- events
        private void OnWinEvent(IntPtr hook, uint eventId, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                // The object events also fire for carets, cursors and every
                // child control; only whole windows matter here.
                if (eventId >= 0x8000)
                {
                    if (idObject != 0 /* OBJID_WINDOW */ || idChild != 0 /* CHILDID_SELF */) return;
                    if (eventId != 0x8001 && Native.GetAncestor(hwnd, Native.GA_ROOT) != hwnd) return;
                }
                if (!soon.IsEnabled) soon.Start();
            }
            catch (Exception ex)
            {
                // Called straight from native code: nothing may escape.
                Log.Error("taskbar", ex);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (taskbarCreated != 0 && msg == taskbarCreated && enabled) OnExplorerRestarted();
            return IntPtr.Zero;
        }

        /// <summary>
        /// Explorer restarted, and the agent went with it. Load it again once
        /// the new taskbar has settled - unless Explorer keeps restarting soon
        /// after a load, which would point at the agent itself.
        /// </summary>
        private void OnExplorerRestarted()
        {
            sent.Clear();
            if (DateTime.UtcNow - loadedAt < TimeSpan.FromSeconds(60)) quickRestarts++;
            else quickRestarts = 0;
            if (quickRestarts >= 2)
            {
                GiveUp("Explorer restarted twice within a minute of loading the taskbar agent");
                return;
            }
            ScheduleLoad(TimeSpan.FromSeconds(3));
        }

        // ------------------------------------------------------------ deciding
        private void Evaluate(bool force)
        {
            if (!enabled) return;
            IntPtr agent = Native.FindMessageWindow(AgentClass);
            if (agent == IntPtr.Zero)
            {
                sent.Clear();
                if (!loading && !reload.IsEnabled) Load();
                return;
            }

            List<IntPtr> bars = Native.Taskbars();
            HashSet<IntPtr> busy = BusyMonitors(bars);

            Dictionary<IntPtr, bool> now = new Dictionary<IntPtr, bool>();
            foreach (IntPtr bar in bars)
            {
                bool clear = !busy.Contains(Native.MonitorFromWindow(bar, Native.MONITOR_DEFAULTTONEAREST));
                bool last;
                if (!force && sent.TryGetValue(bar, out last) && last == clear)
                {
                    now[bar] = clear;
                    continue;
                }
                int result = Send(agent, OpSet, bar, clear ? FlagClear | FlagHideLine : 0, 0x00000000);
                // Anything but "applied" - a taskbar the agent has not found
                // yet, a busy Explorer - is tried again on the next look.
                if (result == ResultApplied) now[bar] = clear;
            }
            sent.Clear();
            foreach (KeyValuePair<IntPtr, bool> kv in now) sent[kv.Key] = kv.Value;
        }

        /// <summary>The monitors that have something open on them.</summary>
        private HashSet<IntPtr> BusyMonitors(List<IntPtr> bars)
        {
            HashSet<IntPtr> busy = new HashSet<IntPtr>();
            HashSet<IntPtr> wanted = new HashSet<IntPtr>();
            foreach (IntPtr bar in bars) wanted.Add(Native.MonitorFromWindow(bar, Native.MONITOR_DEFAULTTONEAREST));

            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground != IntPtr.Zero &&
                TaskbarRules.IsShellSurface(Native.ClassName(foreground), Native.ProcessFileName(foreground)))
            {
                busy.Add(Native.MonitorFromWindow(foreground, Native.MONITOR_DEFAULTTONEAREST));
            }

            Native.EnumWindowsProc visit = delegate(IntPtr hwnd, IntPtr data)
            {
                if (CountsAsOpen(hwnd))
                {
                    IntPtr monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONULL);
                    if (monitor != IntPtr.Zero) busy.Add(monitor);
                }
                // Stop as soon as every taskbar's monitor is known to be busy.
                return !busy.IsSupersetOf(wanted);
            };
            if (!busy.IsSupersetOf(wanted)) Native.EnumWindows(visit, IntPtr.Zero);
            GC.KeepAlive(visit);
            return busy;
        }

        internal static bool CountsAsOpen(IntPtr hwnd)
        {
            // Cheapest tests first: most top-level windows are invisible.
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return false;
            WindowFacts f = new WindowFacts();
            f.Style = Native.GetWindowLong(hwnd, -16);      // GWL_STYLE
            f.ExStyle = Native.GetWindowLong(hwnd, -20);    // GWL_EXSTYLE
            f.ClassName = Native.ClassName(hwnd);
            Native.RECT r;
            if (Native.GetWindowRect(hwnd, out r))
            {
                f.Width = r.Right - r.Left;
                f.Height = r.Bottom - r.Top;
            }
            if (!TaskbarRules.CountsAsOpen(f)) return false;
            // The two checks that cost a call into another component, last.
            return !Native.IsCloaked(hwnd) && !Native.IsFullyTransparent(hwnd, f.ExStyle);
        }

        // ------------------------------------------------------------- loading
        private void ScheduleLoad(TimeSpan delay)
        {
            if (gaveUp) return;
            reload.Stop();
            reload.Interval = delay;
            reload.Start();
        }

        private void GiveUp(string why)
        {
            gaveUp = true;
            reload.Stop();
            Log.Warn("taskbar", why + "; the clear taskbar is off until Nilam starts again");
        }

        private void Load()
        {
            if (!enabled || loading || gaveUp) return;
            if (Native.FindMessageWindow(AgentClass) != IntPtr.Zero)
            {
                Evaluate(true);
                return;
            }

            // For the first while after Explorer starts - at sign-in, or after
            // it restarted - XAML Diagnostics will not let the agent in yet.
            // Measured on Windows 11: still refused 30 seconds after start,
            // accepted at once a minute later. Waiting costs nothing, while
            // trying too early uses up an attempt.
            TimeSpan age = Native.ExplorerAge();
            if (age < ExplorerSettle)
            {
                ScheduleLoad(ExplorerSettle - age);
                return;
            }

            string dll;
            try
            {
                dll = StageDll();
            }
            catch (Exception ex)
            {
                Log.Error("taskbar", ex);
                Failed("the agent could not be copied to the temp folder");
                return;
            }

            Process loader;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Folder, LoaderName), "\"" + dll + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                loader = Process.Start(psi);
            }
            catch (Exception ex)
            {
                // Most likely an antivirus that will not let it run.
                Log.Error("taskbar", ex);
                Failed("the loader could not be started");
                return;
            }

            loading = true;
            loadedAt = DateTime.UtcNow;
            Task.Run(delegate
            {
                int code = -1;
                try
                {
                    if (loader.WaitForExit(60000)) code = loader.ExitCode;
                    else loader.Kill();
                }
                catch (Exception ex)
                {
                    // Reported as a failed load (code -1), which is logged there.
                    Log.Error("taskbar", ex);
                }
                finally { loader.Dispose(); }
                dispatcher.BeginInvoke(new Action(delegate { Loaded(code); }));
            });
        }

        private void Loaded(int code)
        {
            loading = false;
            if (code == 0)
            {
                failures = 0;
                Log.Info("taskbar", "the taskbar agent is running in Explorer");
                if (enabled) Evaluate(true);
                else
                {
                    IntPtr agent = Native.FindMessageWindow(AgentClass);
                    if (agent != IntPtr.Zero) Send(agent, OpRestoreAll, IntPtr.Zero, 0, 0);
                }
                return;
            }
            Failed("the taskbar agent did not start (loader exit code " + code +
                   "; see " + Path.Combine(Path.GetTempPath(), AppInfo.Name, "taskbar-agent.log") + ")");
        }

        private void Failed(string why)
        {
            failures++;
            if (failures >= 3)
            {
                GiveUp(why + ", three times");
                return;
            }
            Log.Warn("taskbar", why + "; trying again shortly");
            ScheduleLoad(TimeSpan.FromSeconds(20));
        }

        /// <summary>
        /// Explorer keeps the DLL open until it restarts, so it is never loaded
        /// from the install folder - an update could not replace it there, and
        /// an uninstall could not delete it. A copy named after its content goes
        /// in the temp folder instead, and copies older versions left behind
        /// are deleted once Explorer has let go of them.
        /// </summary>
        private static string StageDll()
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(Folder, DllName));
            string hash;
            using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes), 0, 8).Replace("-", "");

            string dir = Path.Combine(Path.GetTempPath(), AppInfo.Name);
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "NilamTaskbar-" + hash + ".dll");

            if (!SameBytes(target, bytes))
            {
                string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, bytes);
                // A delete that fails means the file is in use by Explorer;
                // the check below then picks another name, so nothing is lost.
                try { File.Delete(target); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (File.Exists(target))
                {
                    // In use and wrong: leave it, and use a name of our own.
                    target = Path.Combine(dir, "NilamTaskbar-" + hash + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".dll");
                }
                File.Move(temp, target);
            }

            foreach (string old in Directory.GetFiles(dir, "NilamTaskbar-*"))
            {
                if (string.Equals(old, target, StringComparison.OrdinalIgnoreCase)) continue;
                // One that will not go is still loaded in Explorer; it is tried
                // again on the next load, after Explorer has restarted.
                try { File.Delete(old); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return target;
        }

        private static bool SameBytes(string path, byte[] bytes)
        {
            try
            {
                if (!File.Exists(path)) return false;
                byte[] there = File.ReadAllBytes(path);
                if (there.Length != bytes.Length) return false;
                for (int i = 0; i < bytes.Length; i++) if (there[i] != bytes[i]) return false;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // ------------------------------------------------------------ sending
        private int Send(IntPtr agent, uint op, IntPtr taskbar, uint flags, uint color)
        {
            // NilamCommand: five little-endian 32-bit fields.
            byte[] data = new byte[20];
            Put(data, 0, op);
            Put(data, 4, unchecked((uint)taskbar.ToInt32()));
            Put(data, 8, flags);
            Put(data, 12, color);
            Put(data, 16, ownPid);
            IntPtr from = window != null ? window.Handle : IntPtr.Zero;
            return Native.SendCopyData(agent, from, Magic, data, 1000);
        }

        private static void Put(byte[] data, int at, uint value)
        {
            data[at] = (byte)value;
            data[at + 1] = (byte)(value >> 8);
            data[at + 2] = (byte)(value >> 16);
            data[at + 3] = (byte)(value >> 24);
        }
    }

    /// <summary>What CountsAsOpen looks at, gathered so the rules can be tested without real windows.</summary>
    internal struct WindowFacts
    {
        internal int Style;
        internal int ExStyle;
        internal string ClassName;
        internal int Width;
        internal int Height;
    }

    /// <summary>The decisions behind the clear taskbar, kept free of Windows calls so they can be tested.</summary>
    internal static class TaskbarRules
    {
        private const int WS_CHILD = 0x40000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        /// <summary>
        /// Windows that are part of the shell rather than something the user
        /// opened: the desktop itself, the taskbars, and the shell's own
        /// surfaces, which IsShellSurface handles while they are in front.
        /// </summary>
        private static readonly HashSet<string> ShellClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "Progman", "WorkerW",
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
            "Windows.UI.Core.CoreWindow",
            "XamlExplorerHostIslandWindow",
            "TopLevelWindowForOverflowXamlIsland",
            "NotifyIconOverflowWindow",
        };

        /// <summary>
        /// Whether a visible, unminimised, uncloaked top-level window means "the
        /// desktop is not all there is". Tool windows and windows that never
        /// take focus do not count unless they ask for a taskbar button - which
        /// is also what keeps Nilam's own cards from counting.
        /// </summary>
        internal static bool CountsAsOpen(WindowFacts f)
        {
            if ((f.Style & WS_CHILD) != 0) return false;
            bool appWindow = (f.ExStyle & WS_EX_APPWINDOW) != 0;
            if ((f.ExStyle & WS_EX_TOOLWINDOW) != 0 && !appWindow) return false;
            if ((f.ExStyle & WS_EX_NOACTIVATE) != 0 && !appWindow) return false;
            if (f.Width <= 1 || f.Height <= 1) return false;
            if (f.ClassName != null && ShellClasses.Contains(f.ClassName)) return false;
            return true;
        }

        /// <summary>
        /// Start, Search and Task View. While one of them is in front its
        /// taskbar goes back to normal, whatever is behind it.
        /// </summary>
        internal static bool IsShellSurface(string className, string processFile)
        {
            if (className == "XamlExplorerHostIslandWindow") return true;          // Task View
            if (className != "Windows.UI.Core.CoreWindow") return false;
            return string.Equals(processFile, "StartMenuExperienceHost.exe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(processFile, "SearchHost.exe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(processFile, "SearchApp.exe", StringComparison.OrdinalIgnoreCase);
        }
    }
}
