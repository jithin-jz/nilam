using System;
using System.Globalization;
using System.Text;

namespace Nilam
{
    internal struct ClockReading
    {
        internal string Time;
        /// <summary>Empty on a 24-hour clock.</summary>
        internal string AmPm;
    }

    /// <summary>
    /// Formats the clock card's time. The card used to be hard-wired to "hh:mm"
    /// plus the culture's AM/PM text. Most 24-hour cultures have no AM/PM text at
    /// all, so on a German or French Windows 20:23 was shown as a bare "08:23".
    /// </summary>
    internal static class TimeText
    {
        internal static bool Uses24Hour(ClockChoice choice, CultureInfo culture)
        {
            if (choice == ClockChoice.Hour24) return true;
            if (choice == ClockChoice.Hour12) return false;
            // 'H' is the 24-hour specifier and 'h' the 12-hour one; nothing else
            // in a time pattern uses either letter once quoted text is removed.
            return Unquoted(culture.DateTimeFormat.ShortTimePattern).IndexOf('H') >= 0;
        }

        internal static ClockReading Format(DateTime now, ClockChoice choice, CultureInfo culture)
        {
            bool h24 = Uses24Hour(choice, culture);
            string pattern = Unquoted(culture.DateTimeFormat.ShortTimePattern);

            // Follow the user's own pattern for the leading zero when it is
            // already in the hour style being shown ("h:mm tt" gives 8:23,
            // "HH:mm" gives 08:23). When the style was forced the other way the
            // pattern has nothing to say, so use two digits, which is what the
            // card's width was designed around.
            bool patternMatches = pattern.IndexOf(h24 ? 'H' : 'h') >= 0;
            bool twoDigit = !patternMatches || pattern.IndexOf(h24 ? "HH" : "hh", StringComparison.Ordinal) >= 0;

            string hour = h24 ? (twoDigit ? "HH" : "H") : (twoDigit ? "hh" : "h");
            ClockReading r;
            // ':' in a custom format is the culture's own time separator, so
            // en-IN still gets its "08.23".
            r.Time = now.ToString(hour + ":mm", culture);
            r.AmPm = "";
            if (!h24)
            {
                DateTimeFormatInfo f = culture.DateTimeFormat;
                string designator = now.Hour < 12 ? f.AMDesignator : f.PMDesignator;
                // A 12-hour clock forced onto a culture with no designators
                // would be ambiguous without something here.
                if (string.IsNullOrEmpty(designator)) designator = now.Hour < 12 ? "AM" : "PM";
                r.AmPm = culture.TextInfo.ToUpper(designator);
            }
            return r;
        }

        /// <summary>Drops 'quoted literals' and \escaped characters from a format pattern.</summary>
        private static string Unquoted(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return "";
            StringBuilder sb = new StringBuilder(pattern.Length);
            char quote = '\0';
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '\'' || c == '"') { quote = c; continue; }
                if (c == '\\') { i++; continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
