using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Nilam
{
    /// <summary>
    /// Current temperature, conditions and the day's range for one place.
    ///
    /// A single dropped request (Wi-Fi blip, DNS hiccup) should not blank the
    /// card for a full cycle: it keeps showing the last good reading and retries
    /// sooner. It also refreshes when the machine wakes or the network returns,
    /// which is when most of those failures used to happen, and it says so when
    /// what it is showing has gone stale.
    /// </summary>
    internal class WeatherCard : Card
    {
        private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(45);
        private static readonly TimeSpan WakeRefreshAfter = TimeSpan.FromMinutes(10);
        private const int Retries = 3;
        private const string Degree = "\u00B0";

        private TextBlock place, icon, temp, desc, range;
        private DispatcherTimer retry;

        private bool busy;
        private bool disposed;
        private int retriesLeft;
        private int failures;                          // consecutive, for the log
        private DateTime lastGood = DateTime.MinValue; // UTC
        private string rangeText = "";

        internal WeatherCard(WidgetApp app) : base(app, "Weather") { }

        protected override string Body { get { return Markup.Weather; } }

        protected override void OnBuilt()
        {
            place = Find<TextBlock>("WxPlace");
            icon = Find<TextBlock>("WxIcon");
            temp = Find<TextBlock>("WxTemp");
            desc = Find<TextBlock>("WxDesc");
            range = Find<TextBlock>("WxRange");

            Find<TextBlock>("WxCredit").MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;       // a link, not the start of a drag
                WidgetApp.Open(Weather.CreditUrl);
            };

            retry = new DispatcherTimer();
            retry.Interval = RetryDelay;
            retry.Tick += delegate(object s, EventArgs e)
            {
                retry.Stop();
                Attempt();
            };

            App.AddTimer(Cycle, Refresh);
            App.AddTimer(TimeSpan.FromMinutes(1), ShowAge);
        }

        internal override void OnShown()
        {
            Refresh();
        }

        internal override void OnWake()
        {
            if (DateTime.UtcNow - lastGood > WakeRefreshAfter) Refresh();
        }

        internal override void Dispose()
        {
            disposed = true;
            if (retry != null) retry.Stop();
            base.Dispose();
        }

        protected override void OnClicked()
        {
            // An unconfigured card is a button: it opens the place to fix it.
            if (App.Store.Settings.Location == null) App.OpenSettings();
        }

        protected override void AddMenuItems(ItemCollection items)
        {
            MenuItem refresh = new MenuItem();
            refresh.Header = "Refresh weather";
            refresh.Click += delegate { Refresh(); };
            items.Add(refresh);

            MenuItem credit = new MenuItem();
            credit.Header = Weather.CreditText;
            credit.Click += delegate { WidgetApp.Open(Weather.CreditUrl); };
            items.Add(credit);
        }

        private void Refresh()
        {
            retriesLeft = Retries;
            retry.Stop();
            Attempt();
        }

        private async void Attempt()
        {
            WeatherLocation where = App.Store.Settings.Location;
            if (where == null)
            {
                place.Text = "WEATHER";
                desc.Text = "Click to set a city";
                return;
            }
            // The name is known before the first answer arrives, and still true
            // if it never does.
            place.Text = CultureInfo.CurrentCulture.TextInfo.ToUpper(where.Name);
            if (busy) return;
            busy = true;
            try
            {
                bool fahrenheit = App.Store.Settings.UseFahrenheit;
                WeatherReading r = await Task.Run(delegate { return Weather.Fetch(where, fahrenheit); });
                if (disposed) return;

                temp.Text = r.Temp + Degree;
                desc.Text = r.Description;
                icon.Text = r.Glyph;
                rangeText = "H:" + r.High + Degree + "   L:" + r.Low + Degree;
                range.Text = rangeText;

                if (failures > 0) Log.Info("weather", "recovered after " + failures + " failed attempt(s)");
                failures = 0;
                lastGood = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                if (disposed) return;
                // One line when a run of failures starts and one when it ends.
                // A laptop that sleeps through the night used to add four lines
                // to an unbounded file for every cycle it missed.
                if (failures == 0) Log.Warn("weather", Log.Describe(ex));
                failures++;

                // Only the very first fetch has nothing to show.
                if (lastGood == DateTime.MinValue) desc.Text = "Weather unavailable";
                if (retriesLeft > 0)
                {
                    retriesLeft--;
                    retry.Stop();
                    retry.Start();
                }
            }
            finally
            {
                busy = false;
            }
        }

        /// <summary>
        /// Swaps the high/low line for the reading's age once it is old enough
        /// to mislead - after a long spell offline the card would otherwise show
        /// this morning's temperature as if it were current.
        /// </summary>
        private void ShowAge()
        {
            if (lastGood == DateTime.MinValue) return;
            TimeSpan age = DateTime.UtcNow - lastGood;
            range.Text = age > StaleAfter ? "UPDATED " + AgeText(age) + " AGO" : rangeText;
        }

        internal static string AgeText(TimeSpan age)
        {
            if (age.TotalMinutes < 90) return Math.Max(1, (int)Math.Round(age.TotalMinutes)) + " MIN";
            if (age.TotalHours < 48) return (int)Math.Round(age.TotalHours) + " H";
            return (int)Math.Round(age.TotalDays) + " D";
        }
    }
}
