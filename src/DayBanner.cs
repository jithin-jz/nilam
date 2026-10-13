using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nilam
{
    /// <summary>The face the banner is set in, and the spacing tuned for it.</summary>
    internal class BannerFont
    {
        internal FontFamily Family;
        /// <summary>Gap between letters, as a fraction of the font size.</summary>
        internal double TrackingEm;
        /// <summary>Applied to the banner size so different faces occupy a similar width.</summary>
        internal double Scale;

        // 0.4714 em is measured, not taste. In the reference the word THURSDAY
        // occupies an ink box of 190x16 px; Anurati's cap height is 0.8 em, which
        // puts the reference at a 20 px font size, and the width left over after
        // eight glyphs divided by the seven gaps gives this figure.
        private const double AnuratiTracking = 0.4714;

        // Michroma is wider and heavier than Anurati, so it is set a little
        // smaller and tighter to fill about the same line.
        private const double MichromaTracking = 0.30;
        private const double MichromaScale = 0.85;

        /// <summary>
        /// Picks the banner face from the fonts folder beside the exe.
        ///
        /// Michroma ships with the app under the SIL Open Font License. Anurati,
        /// which the banner was first designed around, is licensed for personal
        /// use only and cannot be redistributed - but if the user has put their
        /// own copy in fonts\ it is preferred. Both are loaded by path, so
        /// neither needs installing and neither needs admin rights. With no
        /// usable font the banner falls back to Segoe UI.
        /// </summary>
        internal static BannerFont Resolve()
        {
            BannerFont font = new BannerFont();
            font.Family = new FontFamily("Segoe UI");
            font.TrackingEm = MichromaTracking;
            font.Scale = 1.0;

            try
            {
                string dir = Path.Combine(AppInfo.InstallRoot, "fonts") + Path.DirectorySeparatorChar;
                if (!Directory.Exists(dir)) return font;

                // The folder is handed to WPF as a Uri object. It used to be
                // spliced into the markup as text, where an "&" anywhere in the
                // install path made the XAML unparseable and killed the app at
                // start-up.
                Uri folder = new Uri(dir);
                HashSet<string> present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FontFamily family in Fonts.GetFontFamilies(folder))
                {
                    string source = family.Source;
                    int hash = source.LastIndexOf('#');
                    present.Add(hash >= 0 ? source.Substring(hash + 1) : source);
                }

                if (present.Contains("Anurati"))
                {
                    font.Family = new FontFamily(folder, "./#Anurati, ./#Michroma, Segoe UI");
                    font.TrackingEm = AnuratiTracking;
                }
                else if (present.Contains("Michroma"))
                {
                    font.Family = new FontFamily(folder, "./#Michroma, Segoe UI");
                    font.TrackingEm = MichromaTracking;
                    font.Scale = MichromaScale;
                }
            }
            catch (Exception ex)
            {
                Log.Error("banner", ex);
            }
            return font;
        }
    }

    /// <summary>The weekday, in large tracked capitals across the top of the screen.</summary>
    internal class DayBanner : Card
    {
        private const double BaseSize = 54;

        // The size is tuned against THURSDAY (8 letters); WEDNESDAY (9) runs
        // wider still and can outgrow the space actually free of the card grid.
        // Shrinking the font for the long names keeps every letter clear of the
        // cards, rather than making the reserved area bigger for one word a week.
        private const double MinScale = 0.75;

        // Frozen once at type load: this brush is shared by every letter and
        // never changes, and a frozen Freezable skips WPF's per-assignment
        // change-notification plumbing.
        private static readonly Brush White = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)));

        private StackPanel row;
        private BannerFont font;
        private string shown = "";
        private double maxTextWidth = LayoutPlanner.BannerWidth;

        internal DayBanner(WidgetApp app) : base(app, LayoutPlanner.Banner) { }

        protected override string Body { get { return Markup.DayBanner; } }

        /// <summary>How wide the word may be before it has to shrink. Set from the layout plan.</summary>
        internal double MaxTextWidth
        {
            get { return maxTextWidth; }
            set
            {
                if (value == maxTextWidth) return;
                maxTextWidth = value;
                shown = "";             // redraw at the new limit
                if (row != null) Tick();
            }
        }

        protected override void OnBuilt()
        {
            row = Find<StackPanel>("BannerRow");
            font = BannerFont.Resolve();
            // The word changes once a day; a few seconds late at midnight is fine.
            App.AddTimer(TimeSpan.FromSeconds(5), Tick);
            Tick();
        }

        private void Tick()
        {
            string day = BannerText.DayName(DateTime.Now, CultureInfo.CurrentCulture);
            if (day == shown) return;
            shown = day;

            double size = BaseSize * font.Scale;
            Draw(day, size);
            row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double natural = row.DesiredSize.Width;
            if (natural > maxTextWidth && natural > 0)
            {
                double scale = Math.Max(MinScale, maxTextWidth / natural);
                Draw(day, size * scale);
            }
        }

        // Tracking is applied by laying out one TextBlock per letter and giving
        // each a right margin. WPF has no CharacterSpacing property - that one is
        // UWP only - and the obvious alternative, weaving spacing characters into
        // the string, can only step tracking in whole-glyph widths: the closest a
        // woven thin space got to the reference was 11.25 against a target of
        // 11.875, and no combination of Unicode spaces lands on it. A margin is
        // continuous, so the value is exact.
        //
        // BannerText decides what counts as a letter. For a script that cannot
        // be cut up it returns the whole word as one piece, which is drawn as a
        // single run with no tracking.
        private void Draw(string day, double fontSize)
        {
            bool tracked;
            List<string> pieces = BannerText.Pieces(day, out tracked);
            double gap = tracked ? font.TrackingEm * fontSize : 0;

            row.Children.Clear();
            for (int i = 0; i < pieces.Count; i++)
            {
                TextBlock letter = new TextBlock();
                letter.Text = pieces[i];
                letter.FontFamily = font.Family;
                letter.FontSize = fontSize;
                letter.Foreground = White;
                // Trailing margin is the gap, so the final letter must not carry
                // one or the word sits off-centre by half a gap.
                if (i < pieces.Count - 1) letter.Margin = new Thickness(0, 0, gap, 0);
                row.Children.Add(letter);
            }
        }

        private static Brush Frozen(SolidColorBrush b)
        {
            b.Freeze();
            return b;
        }
    }
}
