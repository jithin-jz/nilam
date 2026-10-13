using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Nilam
{
    internal class ClockCard : Card
    {
        private TextBlock time, ampm, day, date;

        internal ClockCard(WidgetApp app) : base(app, "Clock") { }

        protected override string Body { get { return Markup.Clock; } }

        protected override void OnBuilt()
        {
            time = Find<TextBlock>("TimeText");
            ampm = Find<TextBlock>("AmPmText");
            day = Find<TextBlock>("DayText");
            date = Find<TextBlock>("DateText");
            App.AddTimer(TimeSpan.FromSeconds(1), Tick);
            Tick();
        }

        private void Tick()
        {
            DateTime now = DateTime.Now;
            CultureInfo culture = CultureInfo.CurrentCulture;
            ClockReading r = TimeText.Format(now, App.Store.Settings.Clock, culture);
            time.Text = r.Time;
            ampm.Text = r.AmPm;
            day.Text = now.ToString("dddd", culture);
            date.Text = culture.TextInfo.ToUpper(now.ToString("dd MMM yyyy", culture));
        }
    }

    internal class SystemCard : Card
    {
        private TextBlock cpuText, ramText, diskText;
        private Border cpuBar, ramBar;

        internal SystemCard(WidgetApp app) : base(app, "System") { }

        protected override string Body { get { return Markup.SysStats; } }

        protected override void OnBuilt()
        {
            cpuText = Find<TextBlock>("CpuText");
            cpuBar = Find<Border>("CpuBar");
            ramText = Find<TextBlock>("RamText");
            ramBar = Find<Border>("RamBar");
            diskText = Find<TextBlock>("DiskText");
        }

        internal override void OnStats(StatsSnapshot s)
        {
            if (s.CpuPercent >= 0)
            {
                cpuText.Text = s.CpuPercent + "%";
                SetBar(cpuBar, s.CpuPercent);
            }
            if (s.RamPercent >= 0)
            {
                ramText.Text = s.RamPercent + "%";
                SetBar(ramBar, s.RamPercent);
            }
            diskText.Text = s.DiskFreeGB >= 0
                ? Math.Round(s.DiskFreeGB).ToString(CultureInfo.CurrentCulture) + " GB FREE"
                : "";
        }
    }

    internal class BatteryCard : Card
    {
        private TextBlock text, status;
        private Border bar;

        internal BatteryCard(WidgetApp app) : base(app, "Battery") { }

        protected override string Body { get { return Markup.Battery; } }

        protected override void OnBuilt()
        {
            text = Find<TextBlock>("BattText");
            status = Find<TextBlock>("BattStatus");
            bar = Find<Border>("BattBar");
        }

        internal override void OnStats(StatsSnapshot s)
        {
            if (s.BatteryPercent < 0)
            {
                text.Text = "n/a";
                return;
            }
            text.Text = s.BatteryPercent + "%";
            status.Text = Stats.BatteryStatus(s);
            SetBar(bar, s.BatteryPercent);
        }
    }

    internal class NotesCard : Card
    {
        // Long enough that a burst of typing is one write, short enough that a
        // kill straight after a pause loses nothing a person would notice.
        private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

        private TextBox box;
        private DispatcherTimer saveTimer;
        private bool dirty;
        private bool loading;

        internal NotesCard(WidgetApp app) : base(app, "Notes") { }

        protected override string Body { get { return Markup.Notes; } }

        // The one card created as interactive, so it can be typed in. It drops
        // back to the desktop layer as soon as focus leaves.
        protected override bool TakesFocus { get { return true; } }

        protected override void OnBuilt()
        {
            box = Find<TextBox>("NotesBox");

            string text;
            if (!App.Store.TryLoadNotes(out text))
            {
                // The file is there but could not be read. Saving now would
                // replace it with whatever gets typed into an empty box, so
                // nothing is saved until a later start reads it successfully.
                box.IsReadOnly = true;
                box.Text = "Your notes could not be opened just now. They are untouched on disk - "
                         + "restart the widgets to try again.";
                return;
            }

            loading = true;
            box.Text = text;
            loading = false;

            saveTimer = new DispatcherTimer();
            saveTimer.Interval = SaveDelay;
            saveTimer.Tick += delegate(object s, EventArgs e) { Flush(); };

            box.TextChanged += delegate(object s, TextChangedEventArgs e)
            {
                if (loading) return;
                dirty = true;
                saveTimer.Stop();       // restart the countdown on every keystroke
                saveTimer.Start();
            };
        }

        internal override void Flush()
        {
            if (saveTimer != null) saveTimer.Stop();
            if (!dirty) return;
            dirty = false;
            App.Store.SaveNotes(box.Text);
        }
    }
}
