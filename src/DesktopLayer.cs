using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Nilam
{
    /// <summary>
    /// Keeps the cards on the desktop layer: behind every app window, yet still
    /// in view when the desktop itself is brought to the front.
    ///
    /// Parking a window at the bottom of the z-order is enough for the first
    /// half. It is exactly wrong for the second: Show Desktop (Win+D, or the
    /// sliver at the end of the taskbar) works by raising the desktop window
    /// above every window that cannot be minimised - and a card cannot be. So
    /// the one moment someone asks to look at their desktop, the widgets on it
    /// vanished, and stayed gone until another window was activated.
    ///
    /// The desktop window normally sits below everything, including a window
    /// parked at the bottom. So an invisible marker window is kept parked
    /// there and never moved for any other reason. If the desktop is ever in
    /// front of the marker, Show Desktop is on: the cards are lifted above the
    /// desktop for as long as that lasts and dropped back when it ends. The
    /// marker is what makes this stable - the cards themselves cannot be used
    /// for the test, because lifting them would make it read "off" again.
    /// </summary>
    internal class DesktopLayer : IDisposable
    {
        private readonly Func<IList<Window>> cards;
        // Held in a field: the hook only keeps a native pointer to it, and a
        // collected delegate would be called into freed memory.
        private readonly Native.WinEventProc onForeground;
        private readonly DispatcherTimer settle;
        private HwndSource marker;
        private IntPtr hook = IntPtr.Zero;
        private bool desktopRaised;

        internal DesktopLayer(Func<IList<Window>> cards)
        {
            this.cards = cards;

            HwndSourceParameters p = new HwndSourceParameters(AppInfo.Name + "Marker");
            p.Width = 0;
            p.Height = 0;
            p.WindowStyle = unchecked((int)0x80000000);     // WS_POPUP, never shown
            p.ExtendedWindowStyle = 0x08000080;             // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            marker = new HwndSource(p);
            Native.PinToBottom(marker.Handle);

            // The z-order is not always final at the instant the foreground
            // changes, so look once more a moment later.
            settle = new DispatcherTimer();
            settle.Interval = TimeSpan.FromMilliseconds(250);
            settle.Tick += delegate(object s, EventArgs e)
            {
                settle.Stop();
                Sync();
            };

            // Entering and leaving Show Desktop both change the foreground
            // window, so this event is the cue to look. The callback arrives on
            // this thread, through the message loop.
            onForeground = delegate(IntPtr h, uint eventId, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
            {
                try
                {
                    Sync();
                    settle.Stop();
                    settle.Start();
                }
                catch (Exception ex)
                {
                    // Called straight from native code: nothing may escape.
                    Log.Error("desktop", ex);
                }
            };
            hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                                          IntPtr.Zero, onForeground, 0, 0, 0 /* WINEVENT_OUTOFCONTEXT */);
            if (hook == IntPtr.Zero) Log.Warn("desktop", "could not watch for Show Desktop; the cards will lag it by up to three seconds");
        }

        /// <summary>
        /// Puts every card where it belongs for the current state. Also the
        /// safety net run every three seconds: Explorer restarts, wallpaper
        /// changes and display switches can all shuffle the z-order.
        /// </summary>
        internal void Sync()
        {
            if (marker == null) return;
            desktopRaised = Native.IsAbove(Native.FindDesktopIconHost(), marker.Handle);
            foreach (Window w in cards())
            {
                // A card being typed in is in front on purpose.
                if (!w.IsActive) Place(w);
            }
            if (!desktopRaised) Native.PinToBottom(marker.Handle);
        }

        /// <summary>Returns one card to the layer after it was moved, clicked or deactivated.</summary>
        internal void Place(Window w)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            if (desktopRaised) Native.BringToTop(h);
            else Native.PinToBottom(h);
        }

        public void Dispose()
        {
            settle.Stop();
            if (hook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(hook);
                hook = IntPtr.Zero;
            }
            if (marker != null)
            {
                marker.Dispose();
                marker = null;
            }
        }
    }
}
