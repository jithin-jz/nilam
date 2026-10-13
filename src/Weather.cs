using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;

namespace Nilam
{
    internal class WeatherReading
    {
        /// <summary>In whichever unit was asked for.</summary>
        internal int Temp;
        internal int High;
        internal int Low;
        internal string Description;
        internal string Glyph;
        internal bool IsDay;
    }

    /// <summary>One match from the city search.</summary>
    internal class GeoPlace
    {
        internal string Name;
        internal string Region;
        internal string Country;
        internal double Lat;
        internal double Lon;

        /// <summary>"Kozhikode, Kerala, India" - enough to tell two Springfields apart.</summary>
        internal string Label
        {
            get
            {
                string label = Name;
                if (!string.IsNullOrEmpty(Region) && Region != Name) label += ", " + Region;
                if (!string.IsNullOrEmpty(Country)) label += ", " + Country;
                return label;
            }
        }
    }

    /// <summary>
    /// Current conditions and city search from Open-Meteo, which needs no API
    /// key. Its free tier is for non-commercial use and its data is CC BY 4.0,
    /// which is why the weather card and the settings window carry a credit.
    /// </summary>
    internal static class Weather
    {
        internal const string CreditText = "Weather data by Open-Meteo.com";
        internal const string CreditUrl = "https://open-meteo.com/";

        private const int TimeoutMs = 8000;

        private static readonly Dictionary<int, string> Descriptions = BuildDescriptions();
        private static readonly Dictionary<int, string> Glyphs = BuildGlyphs();

        static Weather()
        {
            // Open-Meteo is HTTPS only; be explicit so this does not depend on the
            // machine-wide default protocol setting.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (NotSupportedException) { }
        }

        /// <summary>Blocking fetch - call it off the UI thread. Throws on any failure.</summary>
        internal static WeatherReading Fetch(WeatherLocation where, bool fahrenheit)
        {
            string url = "https://api.open-meteo.com/v1/forecast"
                + "?latitude=" + where.Lat.ToString(CultureInfo.InvariantCulture)
                + "&longitude=" + where.Lon.ToString(CultureInfo.InvariantCulture)
                + "&current=temperature_2m,weather_code,is_day"
                + "&daily=temperature_2m_max,temperature_2m_min"
                + "&timezone=auto&forecast_days=1"
                + (fahrenheit ? "&temperature_unit=fahrenheit" : "");
            return Parse(Get(url));
        }

        /// <summary>Turns a forecast response into a reading. Throws FormatException on an unexpected shape.</summary>
        internal static WeatherReading Parse(string json)
        {
            Dictionary<string, object> root = Json.ParseObject(json);
            Dictionary<string, object> current = Json.Object(root, "current");
            Dictionary<string, object> daily = Json.Object(root, "daily");
            double temp = Json.Number(current, "temperature_2m", double.NaN);
            double code = Json.Number(current, "weather_code", double.NaN);
            if (current == null || daily == null || double.IsNaN(temp) || double.IsNaN(code))
            {
                throw new FormatException("The weather service sent a response this version does not understand.");
            }

            WeatherReading r = new WeatherReading();
            r.Temp = (int)Math.Round(temp);
            r.High = (int)Math.Round(First(daily, "temperature_2m_max", temp));
            r.Low = (int)Math.Round(First(daily, "temperature_2m_min", temp));
            r.IsDay = Json.Number(current, "is_day", 1) != 0;

            int wmo = (int)Math.Round(code);
            r.Description = Descriptions.ContainsKey(wmo) ? Descriptions[wmo] : "Weather";
            r.Glyph = GlyphFor(wmo, r.IsDay);
            return r;
        }

        /// <summary>
        /// Looks a place up by name, so nobody has to find and type coordinates.
        /// Blocking; throws on a network failure. An empty list means no match.
        /// </summary>
        internal static List<GeoPlace> Search(string query, string language)
        {
            string url = "https://geocoding-api.open-meteo.com/v1/search"
                + "?name=" + Uri.EscapeDataString(query)
                + "&count=8&format=json"
                + "&language=" + Uri.EscapeDataString(string.IsNullOrEmpty(language) ? "en" : language);
            return ParseSearch(Get(url));
        }

        internal static List<GeoPlace> ParseSearch(string json)
        {
            List<GeoPlace> places = new List<GeoPlace>();
            object[] results = Json.Array(Json.ParseObject(json), "results");
            if (results == null) return places;     // the service omits "results" when nothing matches

            foreach (object item in results)
            {
                Dictionary<string, object> d = item as Dictionary<string, object>;
                string name = Json.String(d, "name");
                double lat = Json.Number(d, "latitude", double.NaN);
                double lon = Json.Number(d, "longitude", double.NaN);
                if (string.IsNullOrEmpty(name) || double.IsNaN(lat) || double.IsNaN(lon)) continue;

                GeoPlace p = new GeoPlace();
                p.Name = name;
                p.Region = Json.String(d, "admin1");
                p.Country = Json.String(d, "country");
                p.Lat = lat;
                p.Lon = lon;
                places.Add(p);
            }
            return places;
        }

        private static string Get(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.UserAgent = AppInfo.UserAgent;

            using (WebResponse resp = req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
            {
                return sr.ReadToEnd();
            }
        }

        private static double First(Dictionary<string, object> daily, string key, double fallback)
        {
            object[] items = Json.Array(daily, key);
            double d;
            return items != null && items.Length > 0 && Json.TryDouble(items[0], out d) ? d : fallback;
        }

        private static string GlyphFor(int wmo, bool isDay)
        {
            // A sun at midnight reads as a bug. Only the two clear-sky codes
            // have a day/night difference; cloud, rain and snow look the same.
            if (!isDay && (wmo == 0 || wmo == 1)) return "\uE708";
            return Glyphs.ContainsKey(wmo) ? Glyphs[wmo] : "\uE753";
        }

        private static Dictionary<int, string> BuildDescriptions()
        {
            Dictionary<int, string> d = new Dictionary<int, string>();
            d[0] = "Clear"; d[1] = "Mainly clear"; d[2] = "Partly cloudy"; d[3] = "Overcast";
            d[45] = "Fog"; d[48] = "Freezing fog";
            d[51] = "Light drizzle"; d[53] = "Drizzle"; d[55] = "Heavy drizzle";
            d[56] = "Freezing drizzle"; d[57] = "Freezing drizzle";
            d[61] = "Light rain"; d[63] = "Rain"; d[65] = "Heavy rain";
            d[66] = "Freezing rain"; d[67] = "Freezing rain";
            d[71] = "Light snow"; d[73] = "Snow"; d[75] = "Heavy snow"; d[77] = "Snow grains";
            d[80] = "Rain showers"; d[81] = "Rain showers"; d[82] = "Heavy showers";
            d[85] = "Snow showers"; d[86] = "Snow showers";
            d[95] = "Thunderstorm"; d[96] = "Thunderstorm, hail"; d[99] = "Thunderstorm, hail";
            return d;
        }

        private static Dictionary<int, string> BuildGlyphs()
        {
            Dictionary<int, string> g = new Dictionary<int, string>();
            // Every codepoint below - and U+E708, the moon used at night - was
            // verified present in both Segoe Fluent Icons
            // (C:\Windows\Fonts\SegoeIcons.ttf) and Segoe MDL2 Assets by reading
            // the fonts' own glyph maps, not copied from an icon chart. That
            // check matters: six of the eight codes this table used at first
            // were in neither font and rendered as empty tofu boxes, and a
            // seventh - U+E9CA, used as the "cloud" and as the fallback - does
            // exist but draws a thermometer.
            //
            // The font ships no sun-behind-cloud glyph, so "partly cloudy" shares
            // the plain cloud with "overcast", and it has a single precipitation
            // glyph, so drizzle and rain share it too. The description text under
            // the icon is what tells those pairs apart.
            string sun = "\uE706", partly = "\uE753", cloud = "\uE753", fog = "\uEDA8";
            string drizzle = "\uEA91", rain = "\uEA91", snow = "\uEDAD", storm = "\uE945";
            g[0] = sun; g[1] = sun; g[2] = partly; g[3] = cloud;
            g[45] = fog; g[48] = fog;
            g[51] = drizzle; g[53] = drizzle; g[55] = drizzle; g[56] = drizzle; g[57] = drizzle;
            g[61] = rain; g[63] = rain; g[65] = rain; g[66] = rain; g[67] = rain;
            g[71] = snow; g[73] = snow; g[75] = snow; g[77] = snow;
            g[80] = rain; g[81] = rain; g[82] = rain;
            g[85] = snow; g[86] = snow;
            g[95] = storm; g[96] = storm; g[99] = storm;
            return g;
        }
    }
}
