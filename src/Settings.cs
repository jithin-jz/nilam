using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Nilam
{
    internal enum UnitChoice { Auto, Celsius, Fahrenheit }
    internal enum ClockChoice { Auto, Hour12, Hour24 }

    internal class WeatherLocation
    {
        internal string Name;
        internal double Lat;
        internal double Lon;

        internal bool IsValid
        {
            get { return Lat >= -90 && Lat <= 90 && Lon >= -180 && Lon <= 180; }
        }
    }

    /// <summary>
    /// What the user can change, as stored in settings.json. Everything has a
    /// default that reproduces how the widgets behaved before there were any
    /// settings, so an absent or partly filled file is always usable.
    /// </summary>
    internal class Settings
    {
        internal const int SchemaVersion = 1;

        // 80% is what the cards have always looked like. The markup used to say
        // 71% with a drop shadow underneath; the shadow was clipped at the
        // window's edge but still showed through the translucent card, and the
        // two together measured 80% in the middle of a card.
        internal const int MinOpacity = 40;
        internal const int MaxOpacity = 100;
        internal const int DefaultOpacity = 80;

        /// <summary>Subfolder of Pictures used when no folder is chosen.</summary>
        internal const string DefaultPhotoFolder = "wallpaper";

        /// <summary>Every card, in the order they are laid out.</summary>
        internal static readonly string[] CardNames =
            { "Clock", "Weather", "Media", "System", "Battery", "Notes", "DayBanner" };

        internal UnitChoice Units = UnitChoice.Auto;
        internal ClockChoice Clock = ClockChoice.Auto;
        internal int CardOpacity = DefaultOpacity;
        internal bool IdlePhotos = true;
        /// <summary>Empty means Pictures\wallpaper.</summary>
        internal string PhotoFolder = "";
        /// <summary>Null until a city has been chosen.</summary>
        internal WeatherLocation Location;

        private readonly Dictionary<string, bool> cards = new Dictionary<string, bool>();

        internal bool IsCardEnabled(string name)
        {
            bool on;
            return !cards.TryGetValue(name, out on) || on;
        }

        internal void SetCardEnabled(string name, bool on)
        {
            cards[name] = on;
        }

        /// <summary>
        /// "Automatic" follows the Windows region: Fahrenheit where the region
        /// is not metric, which in practice is the United States.
        /// </summary>
        internal bool UseFahrenheit
        {
            get
            {
                if (Units == UnitChoice.Fahrenheit) return true;
                if (Units == UnitChoice.Celsius) return false;
                try { return !RegionInfo.CurrentRegion.IsMetric; }
                catch (ArgumentException) { return false; }
            }
        }

        internal static string DefaultPhotoPath
        {
            get
            {
                // Resolved against the Pictures known folder so a redirected
                // (OneDrive, say) library still works.
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                                    DefaultPhotoFolder);
            }
        }

        internal string ResolvedPhotoFolder
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PhotoFolder)) return DefaultPhotoPath;
                return Environment.ExpandEnvironmentVariables(PhotoFolder.Trim());
            }
        }

        // ---- json ---------------------------------------------------------
        internal string ToJson()
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["version"] = SchemaVersion;
            root["units"] = Units == UnitChoice.Celsius ? "c" : Units == UnitChoice.Fahrenheit ? "f" : "auto";
            root["clock"] = Clock == ClockChoice.Hour12 ? "12" : Clock == ClockChoice.Hour24 ? "24" : "auto";
            root["cardOpacity"] = CardOpacity;
            root["idlePhotos"] = IdlePhotos;
            root["photoFolder"] = PhotoFolder == null ? "" : PhotoFolder;

            Dictionary<string, object> shown = new Dictionary<string, object>();
            foreach (string name in CardNames) shown[name] = IsCardEnabled(name);
            root["cards"] = shown;

            if (Location != null)
            {
                Dictionary<string, object> where = new Dictionary<string, object>();
                where["name"] = Location.Name == null ? "" : Location.Name;
                where["lat"] = Location.Lat;
                where["lon"] = Location.Lon;
                root["weather"] = where;
            }
            return Json.Serialize(root);
        }

        /// <summary>
        /// Throws on text that is not JSON at all; tolerates everything else,
        /// falling back to the default for any value it cannot use.
        /// </summary>
        internal static Settings FromJson(string text)
        {
            Settings s = new Settings();
            Dictionary<string, object> root = Json.ParseObject(text);
            if (root == null) return s;

            string units = Json.String(root, "units");
            if (units == "c") s.Units = UnitChoice.Celsius;
            else if (units == "f") s.Units = UnitChoice.Fahrenheit;

            string clock = Json.String(root, "clock");
            if (clock == "12") s.Clock = ClockChoice.Hour12;
            else if (clock == "24") s.Clock = ClockChoice.Hour24;

            double opacity = Json.Number(root, "cardOpacity", DefaultOpacity);
            s.CardOpacity = (int)Math.Round(Math.Max(MinOpacity, Math.Min(MaxOpacity, opacity)));
            s.IdlePhotos = Json.Bool(root, "idlePhotos", true);
            string folder = Json.String(root, "photoFolder");
            s.PhotoFolder = folder == null ? "" : folder;

            Dictionary<string, object> shown = Json.Object(root, "cards");
            if (shown != null)
            {
                foreach (string name in CardNames) s.cards[name] = Json.Bool(shown, name, true);
            }

            s.Location = LocationFrom(Json.Object(root, "weather"));
            return s;
        }

        /// <summary>Reads { name, lat, lon }; null unless both coordinates are usable.</summary>
        internal static WeatherLocation LocationFrom(Dictionary<string, object> where)
        {
            if (where == null || !Json.Has(where, "lat") || !Json.Has(where, "lon")) return null;
            WeatherLocation loc = new WeatherLocation();
            loc.Lat = Json.Number(where, "lat", double.NaN);
            loc.Lon = Json.Number(where, "lon", double.NaN);
            if (double.IsNaN(loc.Lat) || double.IsNaN(loc.Lon) || !loc.IsValid) return null;
            string name = Json.String(where, "name");
            loc.Name = string.IsNullOrWhiteSpace(name) ? "Weather" : name;
            return loc;
        }

        internal Settings Clone()
        {
            return FromJson(ToJson());
        }
    }
}
