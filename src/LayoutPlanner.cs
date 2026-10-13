using System;
using System.Collections.Generic;
using System.Windows;

namespace Nilam
{
    internal class LayoutPlan
    {
        /// <summary>The default spot for each card, keyed by card name.</summary>
        internal readonly Dictionary<string, Rect> Cards = new Dictionary<string, Rect>();

        /// <summary>How wide the banner's word may be before it must shrink.</summary>
        internal double BannerMaxTextWidth;
    }

    /// <summary>
    /// Where the cards go. Pure arithmetic over rectangles - no windows, no
    /// monitors - so every screen shape can be checked in the tests instead of
    /// by borrowing laptops.
    ///
    /// All units are WPF device-independent pixels.
    /// </summary>
    internal static class LayoutPlanner
    {
        // macOS grid: a 160px unit with 16px gutters. Small cards are 160x160,
        // medium cards span two units (336x160). The cards form rows two units
        // wide, stacked into a block parked on the right of the work area.
        internal const int Unit = 160;
        internal const int Gutter = 16;
        internal const int Pitch = Unit + Gutter;              // 176
        internal const int BlockWidth = (Unit * 2) + Gutter;   // 336
        internal const int Edge = 8;                           // minimum gap to the work area's edge
        internal const int RightMargin = 40;

        // The day banner deliberately ignores the card grid: it is a bare line
        // of text centred at the top of the work area. Its window has a fixed
        // size so the position is computable before anything is measured.
        internal const string Banner = "DayBanner";
        internal const int BannerWidth = 760;
        internal const int BannerHeight = 92;
        internal const int BannerTop = 28;                     // gap below the top of the work area

        private static readonly string[] WideCards = { "Media", "Notes" };

        /// <summary>Everything sits on an 8px grid.</summary>
        internal static double Snap(double v)
        {
            return Math.Round(v / 8) * 8;
        }

        internal static Size SizeOf(string name)
        {
            if (name == Banner) return new Size(BannerWidth, BannerHeight);
            return new Size(Array.IndexOf(WideCards, name) >= 0 ? BlockWidth : Unit, Unit);
        }

        /// <summary>
        /// Default positions for the given cards on a work area. On a screen
        /// tall enough for every row this is one block, vertically centred, at
        /// the right edge - exactly where the cards have always been. On a
        /// shorter screen (1080p at 150% leaves 672px, the block needs 688) the
        /// rows are split evenly across blocks placed side by side, below the
        /// banner, instead of being squeezed until they overlap.
        /// </summary>
        internal static LayoutPlan Plan(Rect wa, IList<string> names)
        {
            LayoutPlan plan = new LayoutPlan();
            List<List<string>> rows = Rows(names);
            bool banner = names.Contains(Banner);

            double minX = Snap(wa.Left + Edge);
            double minY = Snap(wa.Top + Edge);
            double bannerX = Math.Max(minX, Snap(wa.Left + ((wa.Width - BannerWidth) / 2)));
            double bannerY = Snap(wa.Top + BannerTop);

            int fits = (int)Math.Floor((wa.Height - (2 * Edge) + Gutter) / Pitch);
            if (fits < 1) fits = 1;
            int blocks = rows.Count <= fits ? 1 : (int)Math.Ceiling(rows.Count / (double)fits);
            int perBlock = blocks == 1 ? rows.Count : (int)Math.Ceiling(rows.Count / (double)blocks);
            double blockHeight = (perBlock * Unit) + (Math.Max(0, perBlock - 1) * Gutter);

            // Anchored to the work area rather than the screen, so a taskbar on
            // any edge is respected.
            double right = Snap(wa.Right - RightMargin - BlockWidth);
            double top = Snap(wa.Top + ((wa.Height - blockHeight) / 2));

            if (blocks > 1)
            {
                // Several blocks reach in towards the middle of the screen,
                // where the banner is, so they start below it.
                if (banner) top = Math.Max(top, Snap(bannerY + BannerHeight + Gutter));
                double lowest = Math.Floor((wa.Bottom - Edge - blockHeight) / 8) * 8;
                if (top > lowest) top = lowest;
            }

            double left = right - ((blocks - 1) * (BlockWidth + Gutter));
            if (left < minX)
            {
                right += minX - left;
                left = minX;
            }
            if (top < minY) top = minY;

            for (int r = 0; r < rows.Count; r++)
            {
                int block = r / perBlock;
                double x = left + (block * (BlockWidth + Gutter));
                double y = top + ((r % perBlock) * Pitch);
                List<string> row = rows[r];
                for (int c = 0; c < row.Count; c++)
                {
                    Size size = SizeOf(row[c]);
                    plan.Cards[row[c]] = new Rect(x + (c * Pitch), y, size.Width, size.Height);
                }
            }

            double centre = wa.Left + (wa.Width / 2.0);
            double maxText = Math.Min(BannerWidth, wa.Width - (2 * Edge));
            if (rows.Count > 0 && top < bannerY + BannerHeight + Gutter)
            {
                // The grid shares the banner's band of the screen. BannerWidth
                // is only a design ceiling: the true limit for a centred word
                // is twice its distance from the screen centre to the grid's
                // left edge, less a gutter.
                maxText = Math.Min(maxText, (left - Gutter - centre) * 2);
            }
            plan.BannerMaxTextWidth = Math.Max(0, maxText);

            if (banner) plan.Cards[Banner] = new Rect(bannerX, bannerY, BannerWidth, BannerHeight);
            return plan;
        }

        /// <summary>
        /// Lays the cards out in rows two units wide, in the order given: small
        /// cards pair up, a wide card takes a row to itself.
        /// </summary>
        private static List<List<string>> Rows(IList<string> names)
        {
            List<List<string>> rows = new List<List<string>>();
            List<string> open = null;
            foreach (string name in names)
            {
                if (name == Banner) continue;
                if (Array.IndexOf(WideCards, name) >= 0)
                {
                    open = null;
                    List<string> wide = new List<string>();
                    wide.Add(name);
                    rows.Add(wide);
                    continue;
                }
                if (open == null)
                {
                    open = new List<string>();
                    rows.Add(open);
                }
                open.Add(name);
                if (open.Count == 2) open = null;
            }
            return rows;
        }

        /// <summary>
        /// Decides where a card with a saved position actually goes.
        ///
        /// A saved position is a point in Windows' virtual desktop space, which
        /// shifts whenever a monitor is added, removed or rearranged. If at
        /// least half the card still lands on one of the current work areas it
        /// is kept there, nudged fully on screen - on whichever monitor that
        /// is, not only the primary one. Otherwise it goes back to its default
        /// spot rather than being left stranded off screen.
        /// </summary>
        internal static Rect Fit(Rect card, Rect home, IList<Rect> workAreas)
        {
            Rect best = Rect.Empty;
            double bestArea = 0;
            foreach (Rect wa in workAreas)
            {
                Rect overlap = Rect.Intersect(card, wa);
                double area = overlap.IsEmpty ? 0 : overlap.Width * overlap.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = wa;
                }
            }

            if (bestArea >= card.Width * card.Height * 0.5) return Inside(card, best);
            return workAreas.Count > 0 ? Inside(home, workAreas[0]) : home;
        }

        private static Rect Inside(Rect r, Rect wa)
        {
            double x = Math.Max(wa.Left, Math.Min(r.Left, wa.Right - r.Width));
            double y = Math.Max(wa.Top, Math.Min(r.Top, wa.Bottom - r.Height));
            return new Rect(x, y, r.Width, r.Height);
        }
    }
}
