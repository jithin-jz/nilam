using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace Nilam
{
    /// <summary>
    /// Win32 entry points. Everything the widgets need from the OS is read
    /// through P/Invoke rather than WMI or Windows Forms, which keeps the
    /// process small and avoids waking the WMI provider host every few seconds.
    /// </summary>
    internal static class Native
    {
        // ---- window styles and z-order -----------------------------------
        // The 32-bit Get/SetWindowLong are correct here because the build is
        // pinned to /platform:x86. A 64-bit build would need the Ptr variants.
        [DllImport("user32.dll")]
        internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                                int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>Lets another process bring its window to the front; see SettingsHost.Open.</summary>
        [DllImport("user32.dll")]
        internal static extern bool AllowSetForegroundWindow(int processId);

        internal const int ASFW_ANY = -1;

        [DllImport("user32.dll")]
        internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        internal const byte VK_MEDIA_NEXT = 0xB0;
        internal const byte VK_MEDIA_PREV = 0xB1;
        internal const byte VK_MEDIA_PLAY = 0xB3;

        /// <summary>Non-interactive widget: never takes focus, never in Alt+Tab.</summary>
        internal static void MakeDesktopWidget(IntPtr hwnd)
        {
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        /// <summary>Interactive widget (Notes): can take focus, still hidden from Alt+Tab.</summary>
        internal static void MakeToolWindowOnly(IntPtr hwnd)
        {
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
        }

        /// <summary>
        /// Park the widget at the bottom of the z-order so every app window draws
        /// over it. The widget then lives on the desktop the way macOS widgets do
        /// instead of floating above everything like the taskbar.
        /// </summary>
        internal static void PinToBottom(IntPtr hwnd)
        {
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// Lift the widget to the front of the ordinary (non-topmost) windows,
        /// without activating it. Only used while Show Desktop is on, when the
        /// desktop itself has been raised above everything else.
        /// </summary>
        internal static void BringToTop(IntPtr hwnd)
        {
            SetWindowPos(hwnd, IntPtr.Zero /* HWND_TOP */, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // ---- the desktop window and z-order queries ---------------------------
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

        private const uint GW_HWNDNEXT = 2;

        /// <summary>
        /// The top-level window that holds the desktop icons. That is Progman on
        /// current Windows; on some older builds the icon view is moved into a
        /// WorkerW instead, so both are looked at. Zero if Explorer is not
        /// running.
        /// </summary>
        internal static IntPtr FindDesktopIconHost()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero &&
                FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return progman;

            IntPtr worker = IntPtr.Zero;
            for (int guard = 0; guard < 256; guard++)
            {
                worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                if (worker == IntPtr.Zero) break;
                if (FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return worker;
            }
            return progman;
        }

        /// <summary>True when <paramref name="upper"/> is in front of <paramref name="lower"/> in the z-order.</summary>
        internal static bool IsAbove(IntPtr upper, IntPtr lower)
        {
            if (upper == IntPtr.Zero || lower == IntPtr.Zero) return false;
            IntPtr h = upper;
            // Walk towards the back from the upper window. The guard is only
            // there so a window list changing underneath us cannot loop.
            for (int guard = 0; guard < 4096; guard++)
            {
                h = GetWindow(h, GW_HWNDNEXT);
                if (h == IntPtr.Zero) return false;
                if (h == lower) return true;
            }
            return false;
        }

        internal delegate void WinEventProc(IntPtr hook, uint eventId, IntPtr hwnd, int idObject,
                                            int idChild, uint thread, uint time);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
                                                      WinEventProc callback, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        internal static extern bool UnhookWinEvent(IntPtr hook);

        internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

        /// <summary>
        /// Presses a media key. Only the fallback now: the media card talks to
        /// the player's own session first, because an injected key goes to
        /// whichever app Windows routes media keys to - not necessarily the one
        /// on the card - and is dropped altogether while an elevated window has
        /// the focus.
        /// </summary>
        internal static void SendMediaKey(byte vk)
        {
            keybd_event(vk, 0, 0x0001, UIntPtr.Zero);
            keybd_event(vk, 0, 0x0001 | 0x0002, UIntPtr.Zero);
        }

        // ---- monitors -----------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MONITORINFO
        {
            internal int cbSize;
            internal RECT rcMonitor;
            internal RECT rcWork;
            internal uint dwFlags;
        }

        internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int index);

        private const int LOGPIXELSX = 88;
        private const uint MONITORINFOF_PRIMARY = 1;

        /// <summary>Pixels per WPF unit. The process is system-DPI aware, so one factor covers every monitor.</summary>
        internal static double SystemScale()
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return 1.0;
            try
            {
                int dpi = GetDeviceCaps(dc, LOGPIXELSX);
                return dpi > 0 ? dpi / 96.0 : 1.0;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }
        }

        /// <summary>
        /// The work area of every monitor, in WPF units, primary first.
        /// SystemParameters.WorkArea only knows about the primary monitor, which
        /// is why a card dragged to a second screen used to jump back on every
        /// start.
        /// </summary>
        internal static List<Rect> WorkAreas()
        {
            List<Rect> areas = new List<Rect>();
            double scale = SystemScale();
            MonitorEnumProc proc = delegate(IntPtr monitor, IntPtr hdc, ref RECT clip, IntPtr data)
            {
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(monitor, ref mi))
                {
                    Rect wa = new Rect(mi.rcWork.Left / scale, mi.rcWork.Top / scale,
                                       Math.Max(0, mi.rcWork.Right - mi.rcWork.Left) / scale,
                                       Math.Max(0, mi.rcWork.Bottom - mi.rcWork.Top) / scale);
                    if ((mi.dwFlags & MONITORINFOF_PRIMARY) != 0) areas.Insert(0, wa);
                    else areas.Add(wa);
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);
            return areas;
        }

        // ---- notification area --------------------------------------------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct NOTIFYICONDATA
        {
            internal int cbSize;
            internal IntPtr hWnd;
            internal int uID;
            internal int uFlags;
            internal int uCallbackMessage;
            internal IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            internal string szTip;
            internal int dwState;
            internal int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            internal string szInfo;
            internal int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            internal string szInfoTitle;
            internal int dwInfoFlags;
            internal Guid guidItem;
            internal IntPtr hBalloonIcon;
        }

        internal const int NIM_ADD = 0;
        internal const int NIM_DELETE = 2;
        internal const int NIF_MESSAGE = 1;
        internal const int NIF_ICON = 2;
        internal const int NIF_TIP = 4;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int RegisterWindowMessage(string name);

        // ---- title bar ----------------------------------------------------
        // Lets an ordinary window's title bar follow the dark theme and take the
        // colour of the window below it. Windows versions that predate either
        // attribute refuse it quietly and keep the standard bar.
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        internal static void StyleTitleBar(IntPtr hwnd, bool dark, byte r, byte g, byte b)
        {
            int on = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4);
            int colorref = r | (g << 8) | (b << 16);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorref, 4);
        }

        // ---- the taskbar ---------------------------------------------------
        // What TaskbarStyle needs to tell an open window from an empty desktop,
        // and to reach the agent inside Explorer. See TaskbarStyle.
        internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        internal const uint GA_ROOT = 2;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int capacity);

        internal static string ClassName(IntPtr hwnd)
        {
            System.Text.StringBuilder name = new System.Text.StringBuilder(256);
            return GetClassName(hwnd, name, name.Capacity) > 0 ? name.ToString() : "";
        }

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        internal const uint MONITOR_DEFAULTTONULL = 0;
        internal const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out uint flags);

        /// <summary>A layered window set to be fully see-through, which shows nothing however visible it claims to be.</summary>
        internal static bool IsFullyTransparent(IntPtr hwnd, int exStyle)
        {
            const int WS_EX_LAYERED = 0x00080000;
            const uint LWA_ALPHA = 0x2;
            if ((exStyle & WS_EX_LAYERED) == 0) return false;
            uint key, flags;
            byte alpha;
            return GetLayeredWindowAttributes(hwnd, out key, out alpha, out flags) && (flags & LWA_ALPHA) != 0 && alpha == 0;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        private const int DWMWA_CLOAKED = 14;

        /// <summary>
        /// Hidden by the window manager while still "visible": a suspended Store
        /// app, a window on another virtual desktop, a shell surface that is
        /// closed.
        /// </summary>
        internal static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked;
            return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0;
        }

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, System.Text.StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>The file name of the program that owns a window, such as "SearchHost.exe"; empty if it cannot be read.</summary>
        internal static string ProcessFileName(IntPtr hwnd)
        {
            const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return "";
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return "";
            try
            {
                System.Text.StringBuilder name = new System.Text.StringBuilder(1024);
                int size = name.Capacity;
                if (!QueryFullProcessImageName(process, 0, name, ref size)) return "";
                return System.IO.Path.GetFileName(name.ToString());
            }
            finally
            {
                CloseHandle(process);
            }
        }

        /// <summary>The main taskbar and one per extra monitor that shows a taskbar.</summary>
        internal static List<IntPtr> Taskbars()
        {
            List<IntPtr> bars = new List<IntPtr>();
            IntPtr main = FindWindow("Shell_TrayWnd", null);
            if (main != IntPtr.Zero) bars.Add(main);
            IntPtr secondary = IntPtr.Zero;
            for (int guard = 0; guard < 64; guard++)
            {
                secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null);
                if (secondary == IntPtr.Zero) break;
                bars.Add(secondary);
            }
            return bars;
        }

        /// <summary>How long the Explorer that owns the taskbar has been running; zero if that cannot be read.</summary>
        internal static TimeSpan ExplorerAge()
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return TimeSpan.Zero;
            uint pid;
            GetWindowThreadProcessId(tray, out pid);
            try
            {
                using (System.Diagnostics.Process explorer = System.Diagnostics.Process.GetProcessById((int)pid))
                {
                    TimeSpan age = DateTime.Now - explorer.StartTime;
                    return age < TimeSpan.Zero ? TimeSpan.Zero : age;
                }
            }
            catch (Exception)
            {
                return TimeSpan.Zero;
            }
        }

        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        internal static IntPtr FindMessageWindow(string className)
        {
            return FindWindowEx(HWND_MESSAGE, IntPtr.Zero, className, null);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct COPYDATASTRUCT
        {
            internal IntPtr dwData;
            internal int cbData;
            internal IntPtr lpData;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(IntPtr hwnd, int message, IntPtr wParam, ref COPYDATASTRUCT data,
                                                        uint flags, uint timeout, out IntPtr result);

        /// <summary>
        /// Sends a block of data to a window in another process. Windows copies
        /// it across, including from this 32-bit process to a 64-bit one.
        /// Returns the window's answer, or -1 if it did not answer in time.
        /// </summary>
        internal static int SendCopyData(IntPtr target, IntPtr from, uint tag, byte[] data, uint timeoutMs)
        {
            const int WM_COPYDATA = 0x004A;
            const uint SMTO_ABORTIFHUNG = 0x0002;
            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                COPYDATASTRUCT cds = new COPYDATASTRUCT();
                cds.dwData = new IntPtr(unchecked((int)tag));
                cds.cbData = data.Length;
                cds.lpData = pin.AddrOfPinnedObject();
                IntPtr result;
                if (SendMessageTimeout(target, WM_COPYDATA, from, ref cds, SMTO_ABORTIFHUNG, timeoutMs, out result) == IntPtr.Zero)
                    return -1;
                return result.ToInt32();
            }
            finally
            {
                pin.Free();
            }
        }

        // ---- display brightness -------------------------------------------
        // DDC/CI, the channel to an external monitor. See Brightness.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            internal int cbSize;
            internal RECT rcMonitor;
            internal RECT rcWork;
            internal uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            internal int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceString;
            internal int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
        private struct PHYSICAL_MONITOR
        {
            internal IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string szPhysicalMonitorDescription;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
        private static extern bool GetMonitorInfoEx(IntPtr hMonitor, ref MONITORINFOEX info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE info, uint flags);

        private const uint EDD_GET_DEVICE_INTERFACE_NAME = 1;

        [DllImport("dxva2.dll")]
        private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

        [DllImport("dxva2.dll")]
        private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll")]
        private static extern bool DestroyPhysicalMonitor(IntPtr physicalMonitor);

        [DllImport("dxva2.dll")]
        private static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr physicalMonitor, byte code, IntPtr type,
                                                                   out uint current, out uint maximum);

        [DllImport("dxva2.dll")]
        private static extern bool SetVCPFeature(IntPtr physicalMonitor, byte code, uint value);

        // The MCCS code for luminance. The low-level VCP calls are used rather
        // than GetMonitorBrightness because that one first demands a capabilities
        // string, which plenty of monitors that do obey this code never send.
        private const byte VCP_BRIGHTNESS = 0x10;

        /// <summary>A physical screen. The handle must go to ReleasePhysicalMonitor.</summary>
        internal class PhysicalMonitor
        {
            internal IntPtr Handle;
            /// <summary>The n of \\.\DISPLAYn - what Windows calls "Display n".</summary>
            internal int Number;
            internal string DeviceId;
        }

        internal static List<PhysicalMonitor> PhysicalMonitors()
        {
            List<PhysicalMonitor> found = new List<PhysicalMonitor>();
            MonitorEnumProc proc = delegate(IntPtr monitor, IntPtr hdc, ref RECT clip, IntPtr data)
            {
                MONITORINFOEX mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (!GetMonitorInfoEx(monitor, ref mi)) return true;

                uint count;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out count) || count == 0) return true;
                PHYSICAL_MONITOR[] screens = new PHYSICAL_MONITOR[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, screens)) return true;

                for (uint i = 0; i < count; i++)
                {
                    PhysicalMonitor p = new PhysicalMonitor();
                    p.Handle = screens[i].hPhysicalMonitor;
                    p.Number = DisplayNumber(mi.szDevice);
                    DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
                    dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    p.DeviceId = EnumDisplayDevices(mi.szDevice, i, ref dd, EDD_GET_DEVICE_INTERFACE_NAME) ? dd.DeviceID : "";
                    found.Add(p);
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);
            return found;
        }

        /// <summary>2 for "\\.\DISPLAY2"; 0 when the name has no number on the end.</summary>
        internal static int DisplayNumber(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return 0;
            int i = deviceName.Length;
            while (i > 0 && char.IsDigit(deviceName[i - 1])) i--;
            int n;
            return int.TryParse(deviceName.Substring(i), out n) ? n : 0;
        }

        internal static bool GetDdcBrightness(IntPtr physicalMonitor, out uint current, out uint maximum)
        {
            return GetVCPFeatureAndVCPFeatureReply(physicalMonitor, VCP_BRIGHTNESS, IntPtr.Zero, out current, out maximum);
        }

        internal static bool SetDdcBrightness(IntPtr physicalMonitor, uint value)
        {
            return SetVCPFeature(physicalMonitor, VCP_BRIGHTNESS, value);
        }

        internal static void ReleasePhysicalMonitor(IntPtr physicalMonitor)
        {
            DestroyPhysicalMonitor(physicalMonitor);
        }

        // ---- system stats -------------------------------------------------
        [DllImport("kernel32.dll")]
        internal static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

        [StructLayout(LayoutKind.Sequential)]
        internal struct MEMORYSTATUSEX
        {
            internal uint dwLength;
            internal uint dwMemoryLoad;
            internal ulong ullTotalPhys;
            internal ulong ullAvailPhys;
            internal ulong ullTotalPageFile;
            internal ulong ullAvailPageFile;
            internal ulong ullTotalVirtual;
            internal ulong ullAvailVirtual;
            internal ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern bool GetDiskFreeSpaceEx(string directoryName,
                                                       out ulong freeBytesAvailable,
                                                       out ulong totalNumberOfBytes,
                                                       out ulong totalNumberOfFreeBytes);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SYSTEM_POWER_STATUS
        {
            internal byte ACLineStatus;
            internal byte BatteryFlag;
            internal byte BatteryLifePercent;
            internal byte SystemStatusFlag;
            internal uint BatteryLifeTime;
            internal uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        internal static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        /// <summary>BatteryFlag bit 7 means "no system battery".</summary>
        internal static bool HasBattery()
        {
            SYSTEM_POWER_STATUS s;
            if (!GetSystemPowerStatus(out s)) return false;
            return (s.BatteryFlag & 128) == 0;
        }
    }
}
