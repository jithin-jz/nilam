using System;
using System.IO;
using System.Text;

namespace Nilam
{
    /// <summary>
    /// One small log file beside the settings.
    ///
    /// The app has no console and no window of its own, so before this existed a
    /// failure simply vanished: errors were swallowed where they happened and a
    /// crash left nothing behind. Everything that goes wrong now leaves a line
    /// here, and the file is capped so it cannot grow without limit on a
    /// machine that spends its life offline.
    /// </summary>
    internal static class Log
    {
        // Two files of this size at most (widgets.log and widgets.log.1).
        internal const long MaxBytes = 256 * 1024;

        private static readonly object Gate = new object();
        private static string path;

        internal static string FilePath { get { return path; } }

        internal static void Start(string directory)
        {
            // Nothing can be reported if the log itself cannot start, and the
            // widgets are still worth running without it.
            try { Directory.CreateDirectory(directory); } catch { }
            path = Path.Combine(directory, "widgets.log");
        }

        internal static void Info(string area, string message) { Write("INFO ", area, message); }
        internal static void Warn(string area, string message) { Write("WARN ", area, message); }
        internal static void Error(string area, Exception ex) { Write("ERROR", area, Describe(ex)); }

        /// <summary>An error nobody handled. Logged with its stack trace.</summary>
        internal static void Crash(string area, Exception ex)
        {
            Write("CRASH", area, ex == null ? "unknown error" : ex.ToString());
        }

        /// <summary>Exception type and message, with the inner chain, on one line.</summary>
        internal static string Describe(Exception ex)
        {
            if (ex == null) return "unknown error";
            StringBuilder sb = new StringBuilder();
            int depth = 0;
            for (Exception e = ex; e != null && depth < 4; e = e.InnerException)
            {
                if (depth > 0) sb.Append("  --> ");
                sb.Append(e.GetType().Name).Append(": ").Append(FirstLine(e.Message));
                depth++;
            }
            return sb.ToString();
        }

        private static string FirstLine(string text)
        {
            if (text == null) return "";
            int cut = text.IndexOfAny(new char[] { '\r', '\n' });
            return cut < 0 ? text : text.Substring(0, cut);
        }

        private static void Write(string level, string area, string message)
        {
            if (path == null) return;
            string line = DateTime.Now.ToString("s") + "  " + level + "  " + area + ": " + message;
            try
            {
                lock (Gate)
                {
                    FileInfo f = new FileInfo(path);
                    if (f.Exists && f.Length > MaxBytes)
                    {
                        string old = path + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch
            {
                // The log is the last resort; it must never be the thing that
                // takes the widgets down.
            }
        }
    }
}
