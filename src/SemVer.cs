using System;

namespace Nilam
{
    /// <summary>
    /// Release-number comparison, shared by the widget exe, the nilam command and
    /// the tests. Kept free of every other type so cli\build.cmd can compile it
    /// into nilam.exe on its own.
    /// </summary>
    internal static class SemVer
    {
        /// <summary>Turns a git tag such as "v2.0.0" into the bare "2.0.0".</summary>
        internal static string FromTag(string tag)
        {
            if (tag == null) return null;
            tag = tag.Trim();
            if (tag.Length > 1 && (tag[0] == 'v' || tag[0] == 'V') && char.IsDigit(tag[1])) tag = tag.Substring(1);
            return tag;
        }

        /// <summary>
        /// Numeric, dot-separated comparison, so 1.10.0 is newer than 1.2.0. A
        /// part that is missing or not a number counts as zero, which makes
        /// "2.0" and "2.0.0" equal and keeps a malformed value from ever being
        /// reported as an update.
        /// </summary>
        internal static bool IsNewer(string candidate, string current)
        {
            string[] a = (FromTag(candidate) ?? "").Split('.');
            string[] b = (FromTag(current) ?? "").Split('.');
            int len = Math.Max(a.Length, b.Length);
            for (int i = 0; i < len; i++)
            {
                int av = Part(a, i);
                int bv = Part(b, i);
                if (av != bv) return av > bv;
            }
            return false;
        }

        private static int Part(string[] parts, int i)
        {
            if (i >= parts.Length) return 0;
            int v;
            return int.TryParse(parts[i], out v) && v > 0 ? v : 0;
        }
    }
}
