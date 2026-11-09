using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Text;

namespace Nilam
{
    /// <summary>One screen the settings window can offer a slider for.</summary>
    internal abstract class BrightnessDisplay : IDisposable
    {
        internal string Name;
        internal string Detail;
        /// <summary>What the display reported when it was found, 0 to 100.</summary>
        internal int Percent;

        internal virtual bool CanChange { get { return true; } }
        internal abstract void Apply(int percent);
        public virtual void Dispose() { }
    }

    /// <summary>
    /// Finds the connected displays and how to change each one's brightness.
    ///
    /// A laptop's own panel is driven through WMI and an external monitor over
    /// DDC/CI; neither route works for the other kind of screen. WMI is the
    /// documented way to reach the panel and keeps the Windows brightness slider
    /// in step. It is used only while the settings window is open, so it does
    /// not disturb the rule that the cards' periodic work avoids WMI.
    ///
    /// Everything here blocks - a DDC/CI exchange takes tens of milliseconds -
    /// so it runs off the UI thread. See BrightnessQueue.
    /// </summary>
    internal static class Brightness
    {
        private const string WmiNamespace = @"root\wmi";

        /// <summary>
        /// Every connected display. The caller owns the results and disposes
        /// them, since an external monitor's holds a handle.
        /// </summary>
        internal static List<BrightnessDisplay> Discover()
        {
            List<BrightnessDisplay> found = new List<BrightnessDisplay>();
            HashSet<string> panels = new HashSet<string>(StringComparer.Ordinal);
            AddPanels(found, panels);
            Dictionary<string, string> names = FriendlyNames();

            foreach (Native.PhysicalMonitor monitor in Native.PhysicalMonitors())
            {
                string key = BrightnessMath.InstanceKey(monitor.DeviceId);
                if (key.Length > 0 && panels.Contains(key))
                {
                    // Already offered through WMI.
                    Native.ReleasePhysicalMonitor(monitor.Handle);
                    continue;
                }

                string numbered = "Display " + (monitor.Number > 0 ? monitor.Number : found.Count + 1);
                string friendly;
                bool named = names.TryGetValue(key, out friendly);
                string name = named ? friendly : numbered;

                uint current, maximum;
                if (ReadDdc(monitor.Handle, out current, out maximum))
                {
                    found.Add(new DdcDisplay(monitor.Handle, name, named ? numbered : "External monitor",
                                             BrightnessMath.ToPercent(current, maximum), maximum));
                }
                else
                {
                    Native.ReleasePhysicalMonitor(monitor.Handle);
                    found.Add(new FixedDisplay(name));
                }
            }
            return found;
        }

        // The first exchange after a monitor wakes is often dropped, and a
        // monitor that really has no DDC/CI fails all three within a moment.
        private static bool ReadDdc(IntPtr handle, out uint current, out uint maximum)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (Native.GetDdcBrightness(handle, out current, out maximum) && maximum > 0) return true;
                System.Threading.Thread.Sleep(40);
            }
            current = 0;
            maximum = 0;
            return false;
        }

        private static void AddPanels(List<BrightnessDisplay> found, HashSet<string> panels)
        {
            try
            {
                Dictionary<string, ManagementObject> methods = new Dictionary<string, ManagementObject>(StringComparer.OrdinalIgnoreCase);
                using (ManagementObjectSearcher query = new ManagementObjectSearcher(WmiNamespace, "SELECT * FROM WmiMonitorBrightnessMethods"))
                {
                    foreach (ManagementObject item in query.Get()) methods[(string)item["InstanceName"]] = item;
                }

                using (ManagementObjectSearcher query = new ManagementObjectSearcher(WmiNamespace, "SELECT * FROM WmiMonitorBrightness"))
                {
                    foreach (ManagementObject reading in query.Get())
                    {
                        string instance = (string)reading["InstanceName"];
                        ManagementObject method;
                        if (!(reading["Active"] is bool) || !(bool)reading["Active"]) continue;
                        if (!methods.TryGetValue(instance, out method)) continue;

                        byte[] levels = reading["Level"] as byte[];
                        int percent = Convert.ToInt32(reading["CurrentBrightness"], CultureInfo.InvariantCulture);
                        found.Add(new PanelDisplay(method, levels, percent));
                        panels.Add(BrightnessMath.InstanceKey(instance));
                    }
                }
            }
            catch (Exception ex)
            {
                // Normal on a desktop PC, which has no panel: WMI answers "not
                // supported". External monitors are still worth offering.
                Log.Info("brightness", "no built-in panel through WMI: " + Log.Describe(ex));
            }
        }

        /// <summary>Monitor names from their EDID, such as "LEN T24i-30", by InstanceKey.</summary>
        private static Dictionary<string, string> FriendlyNames()
        {
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using (ManagementObjectSearcher query = new ManagementObjectSearcher(WmiNamespace, "SELECT * FROM WmiMonitorID"))
                {
                    foreach (ManagementObject item in query.Get())
                    {
                        string name = BrightnessMath.DecodeName(item["UserFriendlyName"] as ushort[]);
                        if (name.Length > 0) names[BrightnessMath.InstanceKey((string)item["InstanceName"])] = name;
                    }
                }
            }
            catch (Exception ex)
            {
                // Only the label is lost; the row falls back to "Display n".
                Log.Warn("brightness", "could not read monitor names: " + Log.Describe(ex));
            }
            return names;
        }

        private sealed class PanelDisplay : BrightnessDisplay
        {
            private readonly ManagementObject method;
            private readonly byte[] levels;

            internal PanelDisplay(ManagementObject method, byte[] levels, int percent)
            {
                this.method = method;
                this.levels = levels;
                Name = "Built-in display";
                Detail = "Laptop screen";
                Percent = percent;
            }

            internal override void Apply(int percent)
            {
                // Arguments: seconds allowed for the change, then the level.
                method.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, BrightnessMath.Nearest(levels, percent) });
            }

            public override void Dispose()
            {
                method.Dispose();
            }
        }

        private sealed class DdcDisplay : BrightnessDisplay
        {
            private readonly IntPtr handle;
            private readonly uint maximum;

            internal DdcDisplay(IntPtr handle, string name, string detail, int percent, uint maximum)
            {
                this.handle = handle;
                this.maximum = maximum;
                Name = name;
                Detail = detail;
                Percent = percent;
            }

            internal override void Apply(int percent)
            {
                if (!Native.SetDdcBrightness(handle, BrightnessMath.ToLevel(percent, maximum)))
                {
                    throw new InvalidOperationException("the monitor did not accept the DDC/CI command");
                }
            }

            public override void Dispose()
            {
                Native.ReleasePhysicalMonitor(handle);
            }
        }

        /// <summary>A display Windows can see but cannot dim, shown so its absence is explained.</summary>
        private sealed class FixedDisplay : BrightnessDisplay
        {
            internal FixedDisplay(string name)
            {
                Name = name;
                Detail = "Can't be changed from Windows. If the monitor's own menu has DDC/CI, turn it on.";
            }

            internal override bool CanChange { get { return false; } }
            internal override void Apply(int percent) { }
        }
    }

    /// <summary>The arithmetic and name handling, kept apart from the hardware so it can be tested.</summary>
    internal static class BrightnessMath
    {
        internal static int ToPercent(uint current, uint maximum)
        {
            if (maximum == 0) return 0;
            return (int)Math.Min(100, Math.Round(current * 100.0 / maximum));
        }

        internal static uint ToLevel(int percent, uint maximum)
        {
            int clamped = Math.Max(0, Math.Min(100, percent));
            return (uint)Math.Round(clamped * maximum / 100.0);
        }

        /// <summary>
        /// The supported level closest to a percentage. Many panels offer only
        /// a handful of steps, and asking for one in between is an error on some.
        /// </summary>
        internal static byte Nearest(byte[] levels, int percent)
        {
            int clamped = Math.Max(0, Math.Min(100, percent));
            if (levels == null || levels.Length == 0) return (byte)clamped;
            byte best = levels[0];
            foreach (byte level in levels)
            {
                if (Math.Abs(level - clamped) < Math.Abs(best - clamped)) best = level;
            }
            return best;
        }

        /// <summary>
        /// A form both spellings of one monitor share, so a window handle can
        /// be matched to its WMI entry: "DISPLAY\LEN68F1\4&amp;3a&amp;0&amp;UID1_0" from WMI and
        /// "\\?\DISPLAY#LEN68F1#4&amp;3a&amp;0&amp;UID1#{guid}" from the display APIs.
        /// </summary>
        internal static string InstanceKey(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string text = id.Replace('#', '\\').ToUpperInvariant();
            int start = text.IndexOf(@"DISPLAY\", StringComparison.Ordinal);
            if (start < 0) return "";
            text = text.Substring(start + 8);

            int guid = text.IndexOf(@"\{", StringComparison.Ordinal);
            if (guid >= 0) text = text.Substring(0, guid);

            int suffix = text.LastIndexOf('_');
            if (suffix > 0 && IsDigits(text, suffix + 1)) text = text.Substring(0, suffix);
            return text;
        }

        private static bool IsDigits(string text, int from)
        {
            if (from >= text.Length) return false;
            for (int i = from; i < text.Length; i++)
            {
                if (!char.IsDigit(text[i])) return false;
            }
            return true;
        }

        /// <summary>WMI hands names back as character codes padded with zeros.</summary>
        internal static string DecodeName(ushort[] codes)
        {
            if (codes == null) return "";
            StringBuilder text = new StringBuilder(codes.Length);
            foreach (ushort code in codes)
            {
                if (code == 0) break;
                text.Append((char)code);
            }
            return text.ToString().Trim();
        }
    }

    /// <summary>
    /// Applies brightness changes one at a time, off the UI thread, keeping only
    /// the newest value per display. A slider drag raises far more changes than
    /// a monitor can take at DDC/CI speed; queueing them all would leave the
    /// screen crawling through old values long after the drag ended.
    /// </summary>
    internal class BrightnessQueue
    {
        private readonly object gate = new object();
        private readonly Action<Action> start;
        private readonly Dictionary<BrightnessDisplay, int> pending = new Dictionary<BrightnessDisplay, int>();
        private List<BrightnessDisplay> toRelease;
        private bool draining;

        /// <param name="start">Runs the drain loop; a thread-pool task, except in the tests.</param>
        internal BrightnessQueue(Action<Action> start)
        {
            this.start = start;
        }

        internal void Set(BrightnessDisplay display, int percent)
        {
            bool begin;
            lock (gate)
            {
                pending[display] = percent;
                begin = !draining;
                draining = true;
            }
            if (begin) start(Drain);
        }

        /// <summary>Disposes the displays once every change asked for so far has been applied.</summary>
        internal void Finish(List<BrightnessDisplay> displays)
        {
            bool begin;
            lock (gate)
            {
                toRelease = displays;
                begin = !draining;
                draining = true;
            }
            if (begin) start(Drain);
        }

        private void Drain()
        {
            while (true)
            {
                BrightnessDisplay display = null;
                int percent = 0;
                List<BrightnessDisplay> release = null;
                lock (gate)
                {
                    foreach (KeyValuePair<BrightnessDisplay, int> next in pending)
                    {
                        display = next.Key;
                        percent = next.Value;
                        break;
                    }
                    if (display != null)
                    {
                        pending.Remove(display);
                    }
                    else
                    {
                        release = toRelease;
                        toRelease = null;
                        draining = false;
                    }
                }

                if (display == null)
                {
                    if (release != null)
                    {
                        foreach (BrightnessDisplay d in release) Release(d);
                    }
                    return;
                }

                try { display.Apply(percent); }
                catch (Exception ex)
                {
                    Log.Warn("brightness", "could not set " + display.Name + ": " + Log.Describe(ex));
                }
            }
        }

        private static void Release(BrightnessDisplay display)
        {
            try { display.Dispose(); }
            catch (Exception ex)
            {
                Log.Warn("brightness", "could not release " + display.Name + ": " + Log.Describe(ex));
            }
        }
    }
}
