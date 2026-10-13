using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Nilam
{
    /// <summary>
    /// The notification-area icon. Until this existed the only way to reach the
    /// app was to right-click a card - so with the cards off screen, hidden, or
    /// all switched off, there was no handle on it at all short of Task Manager.
    ///
    /// Done with Shell_NotifyIcon directly rather than Windows Forms' NotifyIcon,
    /// which would load two more assemblies into a process that is small on
    /// purpose.
    /// </summary>
    internal class Tray : IDisposable
    {
        private const int CallbackMessage = 0x8000 + 1;     // WM_APP + 1
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int IconId = 1;

        private readonly string tip;
        private readonly Func<ContextMenu> buildMenu;
        private readonly Action onClick;
        private readonly int taskbarCreated;

        private HwndSource source;
        private IntPtr icon = IntPtr.Zero;
        private ContextMenu openMenu;       // held so it is not collected while open

        internal Tray(string tip, Func<ContextMenu> buildMenu, Action onClick)
        {
            this.tip = tip;
            this.buildMenu = buildMenu;
            this.onClick = onClick;

            // A hidden top-level window: it receives the icon's mouse messages,
            // and it is what takes the foreground before a menu is shown.
            HwndSourceParameters p = new HwndSourceParameters(AppInfo.Name + "Tray");
            p.Width = 0;
            p.Height = 0;
            p.WindowStyle = unchecked((int)0x80000000);     // WS_POPUP, never shown
            p.ExtendedWindowStyle = 0x00000080;             // WS_EX_TOOLWINDOW
            source = new HwndSource(p);
            source.AddHook(WndProc);

            // Explorer broadcasts this when the taskbar is (re)created; every
            // icon is gone at that point and has to be added again.
            taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

            IntPtr large, small;
            if (Native.ExtractIconEx(AppInfo.ExePath, 0, out large, out small, 1) > 0)
            {
                icon = small != IntPtr.Zero ? small : large;
                if (small != IntPtr.Zero && large != IntPtr.Zero) Native.DestroyIcon(large);
            }
            Add();
        }

        /// <summary>The hidden window, for Native.SetForegroundWindow.</summary>
        internal IntPtr Handle
        {
            get { return source == null ? IntPtr.Zero : source.Handle; }
        }

        private Native.NOTIFYICONDATA Data()
        {
            Native.NOTIFYICONDATA d = new Native.NOTIFYICONDATA();
            d.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.NOTIFYICONDATA));
            d.hWnd = source.Handle;
            d.uID = IconId;
            d.uFlags = Native.NIF_MESSAGE | Native.NIF_TIP | (icon != IntPtr.Zero ? Native.NIF_ICON : 0);
            d.uCallbackMessage = CallbackMessage;
            d.hIcon = icon;
            d.szTip = tip;
            d.szInfo = "";
            d.szInfoTitle = "";
            return d;
        }

        private void Add()
        {
            Native.NOTIFYICONDATA d = Data();
            if (!Native.Shell_NotifyIcon(Native.NIM_ADD, ref d))
            {
                // Not fatal: at logon the taskbar may not exist yet, and
                // TaskbarCreated will bring us back here when it does.
                Log.Warn("tray", "the notification icon could not be added yet");
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == CallbackMessage)
            {
                int mouse = lParam.ToInt32() & 0xFFFF;
                if (mouse == WM_RBUTTONUP) ShowMenu();
                else if (mouse == WM_LBUTTONUP) onClick();
                handled = true;
            }
            else if (taskbarCreated != 0 && msg == taskbarCreated)
            {
                Add();
            }
            return IntPtr.Zero;
        }

        private void ShowMenu()
        {
            // Without this the menu would not close when the user clicks away:
            // a menu only sees an outside click if its thread is the foreground.
            Native.SetForegroundWindow(source.Handle);
            openMenu = buildMenu();
            openMenu.Placement = PlacementMode.MousePoint;
            openMenu.IsOpen = true;
        }

        public void Dispose()
        {
            if (source != null)
            {
                Native.NOTIFYICONDATA d = Data();
                Native.Shell_NotifyIcon(Native.NIM_DELETE, ref d);
                source.RemoveHook(WndProc);
                source.Dispose();
                source = null;
            }
            if (icon != IntPtr.Zero)
            {
                Native.DestroyIcon(icon);
                icon = IntPtr.Zero;
            }
        }
    }
}
