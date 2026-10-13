using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Nilam
{
    /// <summary>
    /// The application: builds the cards, owns what they share (state, timers,
    /// menus, the notification icon) and reacts to the machine changing under
    /// them. What each card shows lives in that card's own class.
    /// </summary>
    internal class WidgetApp : Application
    {
        internal readonly Store Store;
        internal readonly Stats Stats = new Stats();
        internal readonly MediaMonitor Player = new MediaMonitor();
        internal Theme Theme;
        internal DesktopLayer Desktop;

        private readonly List<Card> cards = new List<Card>();
        private readonly List<DispatcherTimer> timers = new List<DispatcherTimer>();
        private readonly DispatcherTimer layoutSave;
        private readonly DispatcherTimer settle;
        private readonly DispatcherTimer wake;
        private Tray tray;
        private SettingsWindow settingsWindow;
        private bool hooked;
        private bool quitting;

        internal WidgetApp(Store store)
        {
            Store = store;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Each of these runs once, a short while after the last time it was
            // poked, so a burst of events turns into one piece of work.
            layoutSave = OneShot(TimeSpan.FromMilliseconds(400), delegate { Store.SaveLayout(); });
            settle = OneShot(TimeSpan.FromMilliseconds(800), OnEnvironmentSettled);
            wake = OneShot(TimeSpan.FromSeconds(5), delegate
            {
                foreach (Card card in cards.ToArray()) card.OnWake();
            });

            Startup += OnStartup;
            SessionEnding += delegate { FlushAll(); };
            Exit += delegate { Unhook(); };
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            try
            {
                Store.LoadLayout();
                Store.LoadSettings();
                tray = new Tray(AppInfo.DisplayName, BuildTrayMenu, OpenSettings);
                Desktop = new DesktopLayer(CardWindows);
                BuildCards();
                ShowCards();
                Store.SaveLayout();
                Hook();
                Log.Info("app", "started " + AppInfo.Version + ", " + cards.Count + " card(s)");
            }
            catch (Exception ex)
            {
                // Without this a start-up failure was invisible: no window, no
                // console, nothing in Task Manager a moment later.
                Log.Crash("startup", ex);
                MessageBox.Show(
                    AppInfo.DisplayName + " could not start." + Environment.NewLine + Environment.NewLine +
                    ex.Message + Environment.NewLine + Environment.NewLine +
                    "Details were written to:" + Environment.NewLine + Log.FilePath,
                    AppInfo.DisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        // ---------------------------------------------------------------- build
        private List<string> WantedCards()
        {
            List<string> wanted = new List<string>();
            bool battery = Native.HasBattery();
            foreach (string name in Settings.CardNames)
            {
                if (!Store.Settings.IsCardEnabled(name)) continue;
                if (name == "Battery" && !battery) continue;
                wanted.Add(name);
            }
            return wanted;
        }

        private static List<Rect> WorkAreas()
        {
            List<Rect> areas = Native.WorkAreas();
            if (areas.Count == 0) areas.Add(SystemParameters.WorkArea);
            return areas;
        }

        private void BuildCards()
        {
            Theme = Theme.From(Store.Settings, SystemParameters.HighContrast);

            List<string> wanted = WantedCards();
            LayoutPlan plan = LayoutPlanner.Plan(SystemParameters.WorkArea, wanted);
            List<Rect> areas = WorkAreas();

            foreach (string name in wanted)
            {
                Card card = Create(name);
                DayBanner banner = card as DayBanner;
                if (banner != null) banner.MaxTextWidth = plan.BannerMaxTextWidth;
                card.Build(plan.Cards[name], areas);
                cards.Add(card);
            }

            AddTimer(TimeSpan.FromSeconds(3), ReadStats);
            AddTimer(TimeSpan.FromSeconds(3), Desktop.Sync);     // see DesktopLayer

            ReadStats();
        }

        private Card Create(string name)
        {
            switch (name)
            {
                case "Clock": return new ClockCard(this);
                case "Weather": return new WeatherCard(this);
                case "Media": return new MediaCard(this);
                case "System": return new SystemCard(this);
                case "Battery": return new BatteryCard(this);
                case "Notes": return new NotesCard(this);
                case LayoutPlanner.Banner: return new DayBanner(this);
                default: throw new ArgumentException("There is no card called '" + name + "'.");
            }
        }

        private void ShowCards()
        {
            foreach (Card card in cards)
            {
                Window w = card.Window;
                w.Opacity = 0;
                w.Show();
                Desktop.Place(w);
                DoubleAnimation fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(260)));
                w.BeginAnimation(Window.OpacityProperty, fade);
                card.OnShown();
            }
        }

        private void CloseCards()
        {
            foreach (DispatcherTimer t in timers) t.Stop();
            timers.Clear();
            foreach (Card card in cards)
            {
                card.Dispose();
                card.Window.Close();
            }
            cards.Clear();
        }

        /// <summary>Tears every card down and builds them again from the current settings.</summary>
        private void Rebuild()
        {
            Store.SaveLayout();
            CloseCards();
            BuildCards();
            ShowCards();
            Store.SaveLayout();
        }

        private IList<Window> CardWindows()
        {
            List<Window> windows = new List<Window>(cards.Count);
            foreach (Card card in cards) windows.Add(card.Window);
            return windows;
        }

        private void ReadStats()
        {
            StatsSnapshot s = Stats.Read();
            foreach (Card card in cards) card.OnStats(s);
        }

        // ---------------------------------------------------------------- timers
        /// <summary>A repeating timer that lives as long as the current set of cards.</summary>
        internal void AddTimer(TimeSpan interval, Action tick)
        {
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = interval;
            t.Tick += delegate(object s, EventArgs e) { tick(); };
            t.Start();
            timers.Add(t);
        }

        private static DispatcherTimer OneShot(TimeSpan delay, Action action)
        {
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = delay;
            t.Tick += delegate(object s, EventArgs e)
            {
                ((DispatcherTimer)s).Stop();
                action();
            };
            return t;
        }

        private static void Poke(DispatcherTimer timer)
        {
            timer.Stop();
            timer.Start();
        }

        internal void SaveLayoutSoon()
        {
            Poke(layoutSave);
        }

        private void FlushAll()
        {
            layoutSave.Stop();
            Store.SaveLayout();
            foreach (Card card in cards) card.Flush();
        }

        // ------------------------------------------------------ the machine changes
        private void Hook()
        {
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
            SystemEvents.PowerModeChanged += OnPowerChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
            hooked = true;
        }

        private void Unhook()
        {
            if (!hooked) return;
            hooked = false;
            // These are static events; a handler left on one keeps the app
            // object alive and can fire into it after the dispatcher has gone.
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
            SystemEvents.PowerModeChanged -= OnPowerChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        }

        private void OnDisplayChanged(object sender, EventArgs e)
        {
            Poke(settle);
        }

        private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // .NET caches the culture; without this a change to the region's
            // time format would not reach the clock until the next start.
            if (e.Category == UserPreferenceCategory.Locale) CultureInfo.CurrentCulture.ClearCachedData();
            // The taskbar moving or resizing changes the work area, and a
            // contrast theme changes the colours; both arrive through here.
            Poke(settle);
        }

        private void OnPowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) Poke(wake);
        }

        private void OnNetworkChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            // Raised on a thread-pool thread.
            if (!e.IsAvailable) return;
            Dispatcher.BeginInvoke(new Action(delegate { Poke(wake); }));
        }

        /// <summary>
        /// Monitors were added, removed or rearranged, the taskbar moved, or a
        /// contrast theme was switched. Positions used to be checked only at
        /// launch, so a card on a monitor that was then unplugged stayed
        /// stranded off screen until the next start.
        /// </summary>
        private void OnEnvironmentSettled()
        {
            if (Theme != null && SystemParameters.HighContrast != Theme.HighContrast)
            {
                Rebuild();
                return;
            }
            Replan();
            List<Rect> areas = WorkAreas();
            foreach (Card card in cards)
            {
                Window w = card.Window;
                Rect now = new Rect(w.Left, w.Top, card.Home.Width, card.Home.Height);
                Rect fit = LayoutPlanner.Fit(now, card.Home, areas);
                if (fit.Left != w.Left) w.Left = fit.Left;
                if (fit.Top != w.Top) w.Top = fit.Top;
            }
        }

        /// <summary>Recomputes every card's default spot for the screen as it is now.</summary>
        private void Replan()
        {
            LayoutPlan plan = LayoutPlanner.Plan(SystemParameters.WorkArea, WantedCards());
            foreach (Card card in cards)
            {
                Rect home;
                if (plan.Cards.TryGetValue(card.Name, out home)) card.Home = home;
                DayBanner banner = card as DayBanner;
                if (banner != null) banner.MaxTextWidth = plan.BannerMaxTextWidth;
            }
        }

        // ---------------------------------------------------------------- menus
        /// <summary>The items every card's menu and the notification icon's menu share.</summary>
        internal void AddSharedMenuItems(ItemCollection items)
        {
            MenuItem settings = new MenuItem();
            settings.Header = "Settings...";
            settings.Click += delegate { OpenSettings(); };

            MenuItem lockItem = new MenuItem();
            lockItem.Header = "Lock positions";
            lockItem.IsCheckable = true;
            lockItem.IsChecked = Store.Locked;
            lockItem.Click += delegate(object s, RoutedEventArgs e)
            {
                Store.Locked = ((MenuItem)s).IsChecked;
                Store.SaveLayout();
            };

            MenuItem reset = new MenuItem();
            reset.Header = "Reset positions";
            reset.Click += delegate { ResetPositions(); };

            MenuItem exit = new MenuItem();
            exit.Header = "Exit widgets";
            exit.Click += delegate { Quit(); };

            items.Add(settings);
            items.Add(lockItem);
            items.Add(reset);
            items.Add(new Separator());
            items.Add(exit);
        }

        private ContextMenu BuildTrayMenu()
        {
            ContextMenu menu = new ContextMenu();
            AddSharedMenuItems(menu.Items);
            return menu;
        }

        /// <summary>See Card.OnMenuOpening for why a menu needs this first.</summary>
        internal void TakeForeground()
        {
            if (tray != null) Native.SetForegroundWindow(tray.Handle);
        }

        internal void OpenSettings()
        {
            if (settingsWindow != null)
            {
                settingsWindow.Activate();
                return;
            }
            settingsWindow = new SettingsWindow(this);
            settingsWindow.Closed += delegate { settingsWindow = null; };
            settingsWindow.Show();
        }

        internal void ApplySettings(Settings updated)
        {
            Store.Settings = updated;
            Store.SaveSettings();
            Rebuild();
        }

        private void ResetPositions()
        {
            Replan();
            foreach (Card card in cards)
            {
                card.Window.Left = card.Home.Left;
                card.Window.Top = card.Home.Top;
                Store.Layout[card.Name] = new Point2(card.Home.Left, card.Home.Top);
            }
            layoutSave.Stop();
            Store.SaveLayout();
        }

        /// <summary>Saves everything, removes the notification icon and ends the process.</summary>
        internal void Quit()
        {
            if (quitting) return;       // the menu and an --exit signal can both arrive
            quitting = true;
            FlushAll();
            if (settingsWindow != null) settingsWindow.Close();
            CloseCards();
            if (Desktop != null) Desktop.Dispose();
            if (tray != null)
            {
                tray.Dispose();
                tray = null;
            }
            Log.Info("app", "exit");
            Shutdown();
        }

        // ---------------------------------------------------------------- helpers
        /// <summary>Opens a web address or a folder in whatever Windows has registered for it.</summary>
        internal static void Open(string target)
        {
            try
            {
                Process.Start(target);
            }
            catch (Exception ex)
            {
                Log.Error("app", ex);
            }
        }
    }
}
