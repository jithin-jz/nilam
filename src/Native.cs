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
