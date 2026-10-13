using System;
using System.Collections.Generic;
using System.Globalization;

namespace Nilam
{
    /// <summary>
    /// Decides how the weekday banner's word is cut up for letter tracking.
    ///
    /// The banner draws one TextBlock per letter and spaces them with a margin.
    /// That is right for an alphabet whose letters stand alone, and wrong
    /// everywhere else: cutting by UTF-16 unit strands combining marks in their
    /// own block (Hindi, Malayalam, Tamil, Thai), breaks the joins in Arabic and
    /// lays a right-to-left word out left-to-right. Those scripts are handed
    /// back whole, to be drawn as one run with no tracking.
    /// </summary>
    internal static class BannerText
    {
        internal static string DayName(DateTime now, CultureInfo culture)
        {
            // The culture's own upper-casing: ToUpperInvariant would leave a
            // Turkish dotted i without its dot.
            return culture.TextInfo.ToUpper(now.ToString("dddd", culture));
        }

        /// <summary>
        /// The pieces to draw. <paramref name="tracked"/> is false when the word
        /// came back as a single piece that must not be letter-spaced.
        /// </summary>
        internal static List<string> Pieces(string word, out bool tracked)
        {
            List<string> pieces = new List<string>();
            tracked = true;
            if (string.IsNullOrEmpty(word)) return pieces;

            // Text elements, not chars: a surrogate pair or a base letter plus
            // its marks is one element.
            TextElementEnumerator e = StringInfo.GetTextElementEnumerator(word);
            while (e.MoveNext())
            {
                string element = (string)e.Current;
                if (!StandsAlone(element)) tracked = false;
                pieces.Add(element);
            }

            if (!tracked)
            {
                pieces.Clear();
                pieces.Add(word);
            }
            return pieces;
        }

        private static bool StandsAlone(string element)
        {
            if (element.Length != 1) return false;      // a cluster or a surrogate pair
            char c = element[0];
            if (c < 0x0590) return true;                 // Latin, Greek, Cyrillic, Armenian
            if (c >= 0x10A0 && c <= 0x10FF) return true; // Georgian
            if (c >= 0x3040 && c <= 0x30FF) return true; // kana
            if (c >= 0x4E00 && c <= 0x9FFF) return true; // CJK ideographs
            if (c >= 0xAC00 && c <= 0xD7A3) return true; // Hangul syllables
            return false;                                // Hebrew, Arabic, Indic, Thai and the rest
        }
    }
}
