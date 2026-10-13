using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Xml;

namespace Nilam
{
    /// <summary>
    /// One widget: a borderless, transparent window parked on the desktop layer.
    /// This base class owns everything the cards have in common - building the
    /// window from markup, placing it, dragging, the right-click menu - and each
    /// card adds only what it shows and how it refreshes.
    /// </summary>
    internal abstract class Card
    {
        /// <summary>Inner width of a small card: the full length of a stat bar.</summary>
        protected const double BarMax = 124.0;

        protected readonly WidgetApp App;
        internal readonly string Name;
        internal Window Window;
        /// <summary>The default spot, where "Reset positions" puts the card.</summary>
        internal Rect Home;

        protected Card(WidgetApp app, string name)
        {
            App = app;
            Name = name;
        }

        /// <summary>The card's markup, from Markup.cs.</summary>
        protected abstract string Body { get; }

        /// <summary>
        /// True for a card that can be typed into. Every other card never takes
        /// focus, so clicking one does not pull the keyboard away from the app
        /// in use.
        /// </summary>
        protected virtual bool TakesFocus { get { return false; } }

        /// <summary>The window exists but is not shown: find elements, wire events, start timers.</summary>
        protected virtual void OnBuilt() { }

        /// <summary>The window is on screen. First refresh that needs the network goes here.</summary>
        internal virtual void OnShown() { }

        /// <summary>A fresh system-stats reading, every three seconds.</summary>
        internal virtual void OnStats(StatsSnapshot stats) { }

        /// <summary>The machine woke from sleep or the network came back.</summary>
        internal virtual void OnWake() { }

        /// <summary>A click that did not turn into a drag.</summary>
        protected virtual void OnClicked() { }

        /// <summary>Items for this card's right-click menu, above the shared ones.</summary>
        protected virtual void AddMenuItems(ItemCollection items) { }

        /// <summary>Write anything not yet on disk. Called on deactivate, sign-out and exit.</summary>
        internal virtual void Flush() { }

        /// <summary>The window is about to close: stop anything still running.</summary>
        internal virtual void Dispose()
        {
            Flush();
        }

        internal void Build(Rect home, IList<Rect> workAreas)
        {
            Home = home;

            Window w;
            using (StringReader sr = new StringReader(Markup.Window(Body, App.Theme)))
            using (XmlReader xr = XmlReader.Create(sr))
            {
                w = (Window)XamlReader.Load(xr);
            }
            w.Tag = Name;
            Window = w;

            Rect spot = home;
            Point2 saved;
            if (App.Store.Layout.TryGetValue(Name, out saved))
            {
                spot = new Rect(saved.Left, saved.Top, home.Width, home.Height);
            }
            spot = LayoutPlanner.Fit(spot, home, workAreas);
            w.Left = spot.Left;
            w.Top = spot.Top;
            App.Store.Layout[Name] = new Point2(w.Left, w.Top);

            bool takesFocus = TakesFocus;
            w.SourceInitialized += delegate(object s, EventArgs e)
            {
                IntPtr h = new WindowInteropHelper((Window)s).Handle;
                if (takesFocus) Native.MakeToolWindowOnly(h);
                else Native.MakeDesktopWidget(h);
                Native.PinToBottom(h);
            };

            // A card that can take focus rises to the front when it does, so
            // drop it back onto the desktop layer as soon as focus leaves.
            w.Deactivated += delegate(object s, EventArgs e)
            {
                Flush();
                App.Desktop.Place((Window)s);
            };

            w.MouseLeftButtonDown += OnMouseDown;
            w.LocationChanged += OnMoved;
            w.ContextMenu = new ContextMenu();
            w.ContextMenuOpening += OnMenuOpening;

            OnBuilt();
        }

        protected T Find<T>(string name) where T : class
        {
            T found = Window.FindName(name) as T;
            // Fail at start-up, loudly, if the markup and the code disagree -
            // the old lookup returned null and the card just stayed blank.
            if (found == null) throw new InvalidOperationException(
                "The " + Name + " card's markup has no " + typeof(T).Name + " named '" + name + "'.");
            return found;
        }

        protected static void SetBar(Border bar, double percent)
        {
            double w = BarMax * percent / 100.0;
            if (w < 0) w = 0;
            if (w > BarMax) w = BarMax;
            bar.Width = w;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            Window w = Window;
            double left = w.Left, top = w.Top;
            if (!App.Store.Locked)
            {
                try
                {
                    w.DragMove();
                    w.Left = LayoutPlanner.Snap(w.Left);    // snap to an 8px grid
                    w.Top = LayoutPlanner.Snap(w.Top);
                }
                catch (InvalidOperationException)
                {
                    // DragMove refuses if the button was released before it
                    // started. That is just a click.
                }
            }
            App.Desktop.Place(w);
            if (w.Left == left && w.Top == top) OnClicked();
        }

        private void OnMoved(object sender, EventArgs e)
        {
            App.Store.Layout[Name] = new Point2(Window.Left, Window.Top);
            // LocationChanged fires for every pixel of a drag. The file is
            // written once the card has been still for a moment.
            App.SaveLayoutSoon();
        }

        private void OnMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // The notes box brings its own cut/copy/paste menu; leave it alone.
            if (e.Source is TextBoxBase) return;

            // Built fresh each time so "Lock positions" always shows the current state.
            ItemCollection items = Window.ContextMenu.Items;
            items.Clear();
            AddMenuItems(items);
            if (items.Count > 0) items.Add(new Separator());
            App.AddSharedMenuItems(items);

            // A card never becomes the active window, and a menu opened from a
            // thread that is not in the foreground does not see the click that
            // should dismiss it. Taking the foreground first - on a hidden
            // window, so no card changes its place in the z-order - is the same
            // thing a notification-area icon has to do before showing its menu.
            if (!Window.IsActive) App.TakeForeground();
        }
    }
}
