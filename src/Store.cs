using System;
using System.Collections.Generic;
using System.IO;

namespace Nilam
{
    /// <summary>
    /// Reads and writes the on-disk state: card positions, the notes, and the
    /// settings. Every write goes through AtomicFile, and every failure is
    /// logged rather than swallowed.
    /// </summary>
    internal class Store
    {
        internal readonly string Dir;
        private readonly string layoutPath;
        private readonly string notesPath;
        private readonly string settingsPath;
        private readonly string legacyWeatherPath;

        internal readonly Dictionary<string, Point2> Layout = new Dictionary<string, Point2>();
        internal bool Locked;
        internal Settings Settings = new Settings();

        internal Store() : this(DefaultDir(), UsingOverride ? new string[0] : LegacyDirs())
        {
        }

        /// <param name="dir">The state folder.</param>
        /// <param name="legacyDirs">Folders earlier releases wrote to, to copy from once.</param>
        internal Store(string dir, IEnumerable<string> legacyDirs)
        {
            Dir = dir;
            layoutPath = Path.Combine(Dir, "layout.json");
            notesPath = Path.Combine(Dir, "notes.txt");
            settingsPath = Path.Combine(Dir, "settings.json");
            legacyWeatherPath = Path.Combine(Dir, "weather.json");
            try { Directory.CreateDirectory(Dir); }
            catch (Exception ex) { Log.Error("store", ex); }
            ImportLegacy(legacyDirs);
        }

        // ---- location -----------------------------------------------------
        internal static bool UsingOverride
        {
            get { return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppInfo.StateDirVariable)); }
        }

        /// <summary>
        /// %APPDATA%\Nilam, unless the override variable is set. The
        /// install folder is %LOCALAPPDATA%\Nilam and is deleted on
        /// uninstall, so the state cannot live inside it.
        /// </summary>
        internal static string DefaultDir()
        {
            string over = Environment.GetEnvironmentVariable(AppInfo.StateDirVariable);
            if (!string.IsNullOrWhiteSpace(over)) return Path.GetFullPath(over.Trim());
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);
        }

        internal static string[] LegacyDirs()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] dirs = new string[AppInfo.LegacyStateFolders.Length];
            for (int i = 0; i < dirs.Length; i++) dirs[i] = Path.Combine(local, AppInfo.LegacyStateFolders[i]);
            return dirs;
        }

        /// <summary>
        /// Carries positions, notes and the weather location over from a folder
        /// an earlier release used. The files are copied, not moved: an older
        /// copy of the app that is still installed keeps working, and nothing
        /// is lost if this run fails part-way.
        /// </summary>
        private void ImportLegacy(IEnumerable<string> legacyDirs)
        {
            if (File.Exists(layoutPath) || File.Exists(notesPath) || File.Exists(settingsPath)) return;
            foreach (string old in legacyDirs)
            {
                try
                {
                    if (!Directory.Exists(old)) continue;
                    int copied = 0;
                    foreach (string name in new string[] { "layout.json", "notes.txt", "weather.json" })
                    {
                        string from = Path.Combine(old, name);
                        if (!File.Exists(from)) continue;
                        File.Copy(from, Path.Combine(Dir, name), false);
                        copied++;
                    }
                    if (copied == 0) continue;

                    File.WriteAllText(Path.Combine(old, "MOVED.txt"),
                        "Nilam (formerly Desktop Widgets) now keeps its layout, notes and settings in:" + Environment.NewLine +
                        "    " + Dir + Environment.NewLine + Environment.NewLine +
                        "This folder was copied there on " + DateTime.Now.ToString("yyyy-MM-dd") +
                        " and is no longer read. It is safe to delete." + Environment.NewLine);
                    Log.Info("store", "copied " + copied + " file(s) from " + old);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error("store", ex);
                }
            }
        }

        // ---- layout -------------------------------------------------------
        internal void LoadLayout()
        {
            try
            {
                if (!File.Exists(layoutPath)) return;
                Dictionary<string, object> root = Json.ParseObject(File.ReadAllText(layoutPath));
                if (root == null) return;
                Locked = Json.Bool(root, "_locked", false);
                foreach (KeyValuePair<string, object> kv in root)
                {
                    if (kv.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                    Dictionary<string, object> pos = kv.Value as Dictionary<string, object>;
                    if (pos == null) continue;
                    double left = Json.Number(pos, "Left", double.NaN);
                    double top = Json.Number(pos, "Top", double.NaN);
                    if (double.IsNaN(left) || double.IsNaN(top)) continue;
                    Layout[kv.Key] = new Point2(left, top);
                }
            }
            catch (Exception ex)
            {
                // An unreadable layout only costs the positions: the cards go
                // back to their default spots.
                Log.Error("layout", ex);
            }
        }

        internal void SaveLayout()
        {
            try
            {
                Dictionary<string, object> root = new Dictionary<string, object>();
                foreach (KeyValuePair<string, Point2> kv in Layout)
                {
                    Dictionary<string, object> pos = new Dictionary<string, object>();
                    pos["Left"] = kv.Value.Left;
                    pos["Top"] = kv.Value.Top;
                    root[kv.Key] = pos;
                }
                root["_locked"] = Locked;
                AtomicFile.WriteAllText(layoutPath, Json.Serialize(root));
            }
            catch (Exception ex)
            {
                Log.Error("layout", ex);
            }
        }

        // ---- notes --------------------------------------------------------
        /// <summary>
        /// False when the notes file exists but could not be read - locked by a
        /// sync client or a scanner, say. The caller must not save in that
        /// case: the old code returned an empty string here, and the first
        /// keystroke then overwrote the real notes with it.
        /// </summary>
        internal bool TryLoadNotes(out string text)
        {
            text = "";
            try
            {
                if (File.Exists(notesPath)) text = File.ReadAllText(notesPath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("notes", ex);
                return false;
            }
        }

        internal void SaveNotes(string text)
        {
            try { AtomicFile.WriteAllText(notesPath, text); }
            catch (Exception ex) { Log.Error("notes", ex); }
        }

        // ---- settings -----------------------------------------------------
        internal void LoadSettings()
        {
            Settings = new Settings();
            try
            {
                if (File.Exists(settingsPath)) Settings = Settings.FromJson(File.ReadAllText(settingsPath));
            }
            catch (Exception ex)
            {
                // Keep the unreadable file for a person to look at; the next
                // save would otherwise replace it with defaults.
                Log.Error("settings", ex);
                try { File.Copy(settingsPath, settingsPath + ".bad", true); }
                catch (Exception copyError) { Log.Error("settings", copyError); }
                Settings = new Settings();
            }

            if (Settings.Location == null) Settings.Location = LoadLegacyWeather();
        }

        internal void SaveSettings()
        {
            try { AtomicFile.WriteAllText(settingsPath, Settings.ToJson()); }
            catch (Exception ex) { Log.Error("settings", ex); }
        }

        /// <summary>
        /// weather.json is where releases before 2.0 kept the location, written
        /// by the installer as { "name": "City", "lat": 0.0, "lon": 0.0 }.
        /// </summary>
        private WeatherLocation LoadLegacyWeather()
        {
            try
            {
                if (!File.Exists(legacyWeatherPath)) return null;
                return Settings.LocationFrom(Json.ParseObject(File.ReadAllText(legacyWeatherPath)));
            }
            catch (Exception ex)
            {
                Log.Error("settings", ex);
                return null;
            }
        }
    }

    internal struct Point2
    {
        internal double Left;
        internal double Top;
        internal Point2(double left, double top) { Left = left; Top = top; }
    }
}
