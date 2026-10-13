using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Xml;

// Built by `build.cmd tests` into bin\Tests.exe, together with the app's own
// sources, using the same in-box compiler - so there is no test framework to
// install. A test is any parameterless static method in a static class whose
// name ends in "Tests". The exit code is the number of failures.
namespace Nilam.Tests
{
    internal static class TestRunner
    {
        [STAThread]     // the markup tests create WPF objects
        private static int Main()
        {
            int passed = 0, failed = 0;
            foreach (Type type in typeof(TestRunner).Assembly.GetTypes())
            {
                if (type.Namespace != "Nilam.Tests" || !type.Name.EndsWith("Tests", StringComparison.Ordinal)) continue;
                foreach (MethodInfo m in type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.GetParameters().Length != 0 || m.ReturnType != typeof(void) || m.Name.StartsWith("<", StringComparison.Ordinal)) continue;
                    try
                    {
                        m.Invoke(null, null);
                        passed++;
                    }
                    catch (TargetInvocationException ex)
                    {
                        failed++;
                        Exception inner = ex.InnerException ?? ex;
                        Console.WriteLine("FAIL  " + type.Name + "." + m.Name);
                        Console.WriteLine("      " + (inner is CheckFailed ? inner.Message : inner.ToString()));
                    }
                }
            }
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed;
        }
    }

    internal class CheckFailed : Exception
    {
        internal CheckFailed(string message) : base(message) { }
    }

    internal static class Check
    {
        internal static void True(bool condition, string what)
        {
            if (!condition) throw new CheckFailed(what);
        }

        internal static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new CheckFailed(what + ": expected <" + expected + "> but was <" + actual + ">");
        }

        internal static void Near(double expected, double actual, string what)
        {
            if (Math.Abs(expected - actual) > 0.0001)
                throw new CheckFailed(what + ": expected " + expected + " but was " + actual);
        }

        internal static void Throws(Action action, string what)
        {
            try { action(); }
            catch (Exception) { return; }
            throw new CheckFailed(what + ": expected an exception");
        }
    }

    /// <summary>A scratch folder that is removed when the test is done.</summary>
    internal sealed class TempDir : IDisposable
    {
        internal readonly string Path;

        internal TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dw-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string File(string name) { return System.IO.Path.Combine(Path, name); }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static class SemVerTests
    {
        private static void ComparesPartsAsNumbers()
        {
            Check.True(SemVer.IsNewer("1.10.0", "1.2.0"), "1.10.0 is newer than 1.2.0");
            Check.True(!SemVer.IsNewer("1.2.0", "1.10.0"), "1.2.0 is not newer than 1.10.0");
            Check.True(SemVer.IsNewer("2.0.0", "1.4.2"), "2.0.0 is newer than 1.4.2");
        }

        private static void EqualVersionsAreNotAnUpdate()
        {
            Check.True(!SemVer.IsNewer("2.0.0", "2.0.0"), "same version");
            Check.True(!SemVer.IsNewer("2.0", "2.0.0"), "a missing part counts as zero");
        }

        private static void AcceptsGitTags()
        {
            Check.Equal("2.0.0", SemVer.FromTag("v2.0.0"), "tag prefix");
            Check.Equal("vnext", SemVer.FromTag("vnext"), "a v that is part of a word stays");
            Check.True(SemVer.IsNewer("v2.0.1", "2.0.0"), "tag against bare version");
        }

        private static void GarbageIsNeverAnUpdate()
        {
            Check.True(!SemVer.IsNewer("banana", "1.0.0"), "not a version");
            Check.True(!SemVer.IsNewer(null, "1.0.0"), "null");
            Check.True(!SemVer.IsNewer("<html>404</html>", "1.0.0"), "an error page");
        }
    }

    internal static class TimeTextTests
    {
        private static readonly DateTime Evening = new DateTime(2026, 10, 13, 20, 23, 0);
        private static readonly DateTime Morning = new DateTime(2026, 10, 13, 8, 5, 0);

        // useUserOverride: false, so the result does not depend on how the
        // machine running the tests has its own region set up.
        private static CultureInfo Culture(string name) { return new CultureInfo(name, false); }

        private static void TwentyFourHourCultureShowsTheRealHour()
        {
            // The bug: this used to come out as "08:23" with nothing after it.
            ClockReading r = TimeText.Format(Evening, ClockChoice.Auto, Culture("de-DE"));
            Check.Equal("20:23", r.Time, "German time");
            Check.Equal("", r.AmPm, "no AM/PM on a 24-hour clock");
        }

        private static void TwelveHourCultureFollowsItsOwnPattern()
        {
            ClockReading r = TimeText.Format(Evening, ClockChoice.Auto, Culture("en-US"));
            Check.Equal("8:23", r.Time, "US time has no leading zero");
            Check.Equal("PM", r.AmPm, "US designator");
            Check.Equal("AM", TimeText.Format(Morning, ClockChoice.Auto, Culture("en-US")).AmPm, "morning");
        }

        private static void UsesTheCulturesTimeSeparator()
        {
            CultureInfo dotted = (CultureInfo)Culture("en-US").Clone();
            dotted.DateTimeFormat.TimeSeparator = ".";
            dotted.DateTimeFormat.ShortTimePattern = "hh.mm tt";
            ClockReading r = TimeText.Format(Evening, ClockChoice.Auto, dotted);
            Check.Equal("08.23", r.Time, "dotted separator and two-digit hour");
            Check.Equal("PM", r.AmPm, "designator");
        }

        private static void ForcedTwelveHourAlwaysHasADesignator()
        {
            CultureInfo german = Culture("de-DE");
            ClockReading r = TimeText.Format(Evening, ClockChoice.Hour12, german);
            Check.Equal("08:23", r.Time, "forced 12-hour");
            string own = german.DateTimeFormat.PMDesignator;
            Check.Equal(string.IsNullOrEmpty(own) ? "PM" : german.TextInfo.ToUpper(own), r.AmPm, "never blank");
        }

        private static void ForcedTwentyFourHour()
        {
            ClockReading r = TimeText.Format(Evening, ClockChoice.Hour24, Culture("en-US"));
            Check.Equal("20:23", r.Time, "forced 24-hour");
            Check.Equal("", r.AmPm, "no designator");
        }

        private static void QuotedLettersInAPatternAreIgnored()
        {
            CultureInfo c = (CultureInfo)Culture("en-US").Clone();
            c.DateTimeFormat.ShortTimePattern = "h 'H' mm tt";
            Check.True(!TimeText.Uses24Hour(ClockChoice.Auto, c), "a quoted H is a literal, not a 24-hour hour");
            c.DateTimeFormat.ShortTimePattern = "HH'h'mm";
            Check.True(TimeText.Uses24Hour(ClockChoice.Auto, c), "a quoted h is a literal");
        }
    }

    internal static class BannerTextTests
    {
        private static readonly DateTime Tuesday = new DateTime(2026, 10, 13);

        private static List<string> Pieces(string culture, out bool tracked, out string word)
        {
            word = BannerText.DayName(Tuesday, new CultureInfo(culture, false));
            return BannerText.Pieces(word, out tracked);
        }

        private static void LatinIsTrackedLetterByLetter()
        {
            bool tracked; string word;
            List<string> p = Pieces("en-US", out tracked, out word);
            Check.Equal("TUESDAY", word, "upper-cased day");
            Check.True(tracked, "tracked");
            Check.Equal(7, p.Count, "one piece per letter");
        }

        private static void CyrillicAndCjkAreTracked()
        {
            bool tracked; string word;
            List<string> p = Pieces("ru-RU", out tracked, out word);
            Check.True(tracked && p.Count == word.Length, "Cyrillic letters stand alone");
            p = Pieces("ja-JP", out tracked, out word);
            Check.True(tracked && p.Count == word.Length, "CJK characters stand alone");
        }

        private static void ScriptsThatCannotBeCutStayWhole()
        {
            foreach (string culture in new string[] { "hi-IN", "ml-IN", "ta-IN", "th-TH", "ar-SA", "he-IL" })
            {
                bool tracked; string word;
                List<string> p = Pieces(culture, out tracked, out word);
                Check.True(!tracked, culture + " must not be letter-spaced");
                Check.Equal(1, p.Count, culture + " is one piece");
                Check.Equal(word, p[0], culture + " keeps the whole word");
            }
        }

        private static void UpperCasesWithTheCulturesRules()
        {
            // Monday in Turkish is "Pazartesi"; its last letter must become a dotted capital I.
            string word = BannerText.DayName(new DateTime(2026, 10, 12), new CultureInfo("tr-TR", false));
            Check.True(word.IndexOf('\u0130') >= 0, "Turkish dotted capital I, got " + word);
        }

        private static void EmptyWordHasNoPieces()
        {
            bool tracked;
            Check.Equal(0, BannerText.Pieces("", out tracked).Count, "empty");
        }
    }

    internal static class LayoutTests
    {
        private static readonly string[] All = Settings.CardNames;

        private static bool Overlap(Rect a, Rect b)
        {
            Rect i = Rect.Intersect(a, b);
            return !i.IsEmpty && i.Width > 0.01 && i.Height > 0.01;
        }

        private static void TallScreenKeepsTheOriginalPositions()
        {
            // 1920x1200 at 150%: the layout this app was designed on.
            LayoutPlan plan = LayoutPlanner.Plan(new Rect(0, 0, 1280, 752), All);
            Check.Equal(new Rect(904, 32, 160, 160), plan.Cards["Clock"], "clock");
            Check.Equal(new Rect(1080, 32, 160, 160), plan.Cards["Weather"], "weather");
            Check.Equal(new Rect(904, 208, 336, 160), plan.Cards["Media"], "media");
            Check.Equal(new Rect(904, 384, 160, 160), plan.Cards["System"], "system");
            Check.Equal(new Rect(1080, 384, 160, 160), plan.Cards["Battery"], "battery");
            Check.Equal(new Rect(904, 560, 336, 160), plan.Cards["Notes"], "notes");
            Check.Equal(new Rect(256, 32, 760, 92), plan.Cards["DayBanner"], "banner");
            Check.Near(496, plan.BannerMaxTextWidth, "banner stays clear of the grid");
        }

        private static void NothingOverlapsOnAnyCommonScreen()
        {
            // Work areas in WPF units. 1280x672 is 1080p at 150% - where the
            // notes card used to sit 8 units over the row above it - and
            // 1097x569 is 1080p at 175%, where it covered most of that row.
            double[][] screens =
            {
                new double[] { 1280, 752 }, new double[] { 1280, 672 }, new double[] { 1097.14, 569.14 },
                new double[] { 1152, 672 }, new double[] { 1366, 720 }, new double[] { 1920, 1032 },
                new double[] { 1536, 816 }, new double[] { 1024, 552 }, new double[] { 800, 552 },
                new double[] { 3840, 2112 }, new double[] { 1280, 400 }
            };
            foreach (double[] s in screens)
            {
                Rect wa = new Rect(0, 0, s[0], s[1]);
                string label = s[0] + "x" + s[1];
                LayoutPlan plan = LayoutPlanner.Plan(wa, All);
                Check.Equal(All.Length, plan.Cards.Count, label + ": every card placed");

                // The banner's window is mostly transparent and may lie over a
                // card; what has to stay clear is the band its word can occupy.
                Rect window = plan.Cards[LayoutPlanner.Banner];
                double centre = window.Left + (window.Width / 2);
                Rect word = new Rect(centre - (plan.BannerMaxTextWidth / 2), window.Top, plan.BannerMaxTextWidth, window.Height);
                Check.True(wa.Contains(window), label + ": banner is on screen at " + window);

                List<KeyValuePair<string, Rect>> placed = new List<KeyValuePair<string, Rect>>();
                foreach (KeyValuePair<string, Rect> kv in plan.Cards)
                {
                    if (kv.Key != LayoutPlanner.Banner) placed.Add(kv);
                }
                for (int i = 0; i < placed.Count; i++)
                {
                    Check.True(wa.Contains(placed[i].Value), label + ": " + placed[i].Key + " is on screen at " + placed[i].Value);
                    Check.True(!Overlap(placed[i].Value, word), label + ": the banner's word can reach " + placed[i].Key);
                    for (int j = i + 1; j < placed.Count; j++)
                    {
                        Check.True(!Overlap(placed[i].Value, placed[j].Value),
                                   label + ": " + placed[i].Key + " overlaps " + placed[j].Key);
                    }
                }
            }
        }

        private static void RespectsATaskbarOnTheTopOrLeft()
        {
            Rect wa = new Rect(60, 48, 1220, 704);
            LayoutPlan plan = LayoutPlanner.Plan(wa, All);
            foreach (KeyValuePair<string, Rect> kv in plan.Cards)
            {
                Check.True(wa.Contains(kv.Value), kv.Key + " is inside the offset work area");
            }
        }

        private static void WithoutABatteryTheSystemCardStandsAlone()
        {
            List<string> cards = new List<string>(All);
            cards.Remove("Battery");
            LayoutPlan plan = LayoutPlanner.Plan(new Rect(0, 0, 1280, 752), cards);
            Check.True(!plan.Cards.ContainsKey("Battery"), "no battery card");
            Check.Equal(new Rect(904, 384, 160, 160), plan.Cards["System"], "system keeps its cell");
            Check.Equal(new Rect(904, 560, 336, 160), plan.Cards["Notes"], "notes keeps its row");
        }

        private static void DisabledCardsCloseTheGap()
        {
            LayoutPlan plan = LayoutPlanner.Plan(new Rect(0, 0, 1280, 752), new string[] { "Clock", "Notes" });
            Check.Equal(2, plan.Cards.Count, "two cards");
            Check.Near(plan.Cards["Clock"].Bottom + LayoutPlanner.Gutter, plan.Cards["Notes"].Top, "notes directly under clock");
        }

        private static void ACardOnASecondMonitorStaysThere()
        {
            // The bug: positions were checked against the primary monitor only,
            // so this card was pulled back to its default spot on every start.
            List<Rect> areas = new List<Rect> { new Rect(0, 0, 1280, 752), new Rect(1280, 0, 1920, 1040) };
            Rect saved = new Rect(2000, 104, 160, 160);
            Check.Equal(saved, LayoutPlanner.Fit(saved, new Rect(904, 32, 160, 160), areas), "left where the user put it");

            List<Rect> leftOf = new List<Rect> { new Rect(0, 0, 1280, 752), new Rect(-1920, 0, 1920, 1040) };
            saved = new Rect(-800, 200, 160, 160);
            Check.Equal(saved, LayoutPlanner.Fit(saved, new Rect(904, 32, 160, 160), leftOf), "negative coordinates are fine");
        }

        private static void AStrandedCardGoesHome()
        {
            List<Rect> areas = new List<Rect> { new Rect(0, 0, 1280, 752) };
            Rect home = new Rect(904, 32, 160, 160);
            Check.Equal(home, LayoutPlanner.Fit(new Rect(5000, 100, 160, 160), home, areas), "off every screen");
            Check.Equal(home, LayoutPlanner.Fit(new Rect(1250, 100, 160, 160), home, areas), "mostly off screen");
        }

        private static void AMostlyVisibleCardIsNudgedOnScreen()
        {
            List<Rect> areas = new List<Rect> { new Rect(0, 0, 1280, 752) };
            Rect fit = LayoutPlanner.Fit(new Rect(1180, 100, 160, 160), new Rect(904, 32, 160, 160), areas);
            Check.Equal(new Rect(1120, 100, 160, 160), fit, "moved the minimum distance");
        }

        private static void NoMonitorsAtAllFallsBackToHome()
        {
            Rect home = new Rect(904, 32, 160, 160);
            Check.Equal(home, LayoutPlanner.Fit(new Rect(10, 10, 160, 160), home, new List<Rect>()), "home");
        }
    }

    internal static class PlaybackClockTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static PlaybackState Track(string key, double position, DateTime stampedAt, bool playing)
        {
            PlaybackState s = new PlaybackState();
            s.HasTimeline = true;
            s.DurationSeconds = 200;
            s.PositionSeconds = position;
            s.PositionAtUtc = stampedAt;
            s.Playing = playing;
            s.TrackKey = key;
            return s;
        }

        private static void RunsItsOwnClockWhenThePlayerNeverUpdates()
        {
            // Spotify's behaviour: position 0 stamped at track start, then silence.
            PlaybackClock c = new PlaybackClock();
            PlaybackState s = Track("a", 0, T0, true);
            Check.Near(0, c.Advance(T0, s), "start");
            Check.Near(0.05, c.Advance(T0.AddSeconds(10), s), "ten seconds in");
            Check.Near(0.10, c.Advance(T0.AddSeconds(20), s), "twenty seconds in");
        }

        private static void APauseHoldsTheWaveStill()
        {
            PlaybackClock c = new PlaybackClock();
            c.Advance(T0, Track("a", 0, T0, true));
            c.Advance(T0.AddSeconds(10), Track("a", 0, T0, true));
            Check.Near(0.05, c.Advance(T0.AddSeconds(60), Track("a", 0, T0, false)), "paused: no advance, no collapse");
            Check.Near(0.10, c.Advance(T0.AddSeconds(70), Track("a", 0, T0, true)), "resumed from where it stopped");
        }

        private static void BelievesAPlayerThatReportsANewPosition()
        {
            PlaybackClock c = new PlaybackClock();
            c.Advance(T0, Track("a", 0, T0, true));
            DateTime stamp = T0.AddSeconds(30);
            Check.Near(101.0 / 200, c.Advance(stamp.AddSeconds(1), Track("a", 100, stamp, true)), "re-anchored, plus drift");
        }

        private static void ANewTrackStartsOver()
        {
            PlaybackClock c = new PlaybackClock();
            c.Advance(T0, Track("a", 0, T0, true));
            c.Advance(T0.AddSeconds(50), Track("a", 0, T0, true));
            DateTime stamp = T0.AddSeconds(55);
            Check.Near(5.0 / 200, c.Advance(stamp.AddSeconds(5), Track("b", 0, stamp, true)), "new track, five seconds old");
        }

        private static void NoTimelineMeansAnEmptyWave()
        {
            PlaybackClock c = new PlaybackClock();
            c.Advance(T0, Track("a", 50, T0, true));
            PlaybackState none = new PlaybackState();
            Check.Near(0, c.Advance(T0.AddSeconds(1), none), "no timeline");
        }

        private static void NeverRunsPastTheEnd()
        {
            PlaybackClock c = new PlaybackClock();
            c.Advance(T0, Track("a", 0, T0, true));
            Check.Near(1, c.Advance(T0.AddSeconds(900), Track("a", 0, T0, true)), "clamped to the track length");
        }

        private static void ASeekHoldsUntilThePlayerCatchesUp()
        {
            PlaybackClock c = new PlaybackClock();
            PlaybackState s = Track("a", 0, T0, true);
            c.Advance(T0, s);
            c.SeekTo(150);
            Check.Near(151.0 / 200, c.Advance(T0.AddSeconds(1), s), "stays at the requested spot and keeps running");
        }

        private static void TimeSpentScrubbingIsNotAdded()
        {
            PlaybackClock c = new PlaybackClock();
            PlaybackState s = Track("a", 0, T0, true);
            c.Advance(T0, s);
            c.Touch(T0.AddSeconds(30));     // the user dragged for thirty seconds
            Check.Near(1.0 / 200, c.Advance(T0.AddSeconds(31), s), "only the second after the drag counts");
        }
    }

    internal static class WeatherTests
    {
        // A real response, trimmed.
        private const string Forecast =
            "{\"latitude\":11.28,\"longitude\":75.83,\"timezone\":\"Asia/Kolkata\"," +
            "\"current\":{\"time\":\"2026-10-13T21:00\",\"interval\":900,\"temperature_2m\":82.9,\"weather_code\":3,\"is_day\":0}," +
            "\"daily\":{\"time\":[\"2026-10-13\"],\"temperature_2m_max\":[92.1],\"temperature_2m_min\":[77.4]}}";

        private static string Clear(int isDay)
        {
            return "{\"current\":{\"temperature_2m\":20,\"weather_code\":0,\"is_day\":" + isDay + "}," +
                   "\"daily\":{\"temperature_2m_max\":[25],\"temperature_2m_min\":[15]}}";
        }

        private static void ParsesAForecast()
        {
            WeatherReading r = Weather.Parse(Forecast);
            Check.Equal(83, r.Temp, "temperature rounds");
            Check.Equal(92, r.High, "high");
            Check.Equal(77, r.Low, "low");
            Check.Equal("Overcast", r.Description, "description");
            Check.True(!r.IsDay, "night");
            Check.Equal("\uE753", r.Glyph, "cloud glyph");
        }

        private static void AClearNightShowsAMoonNotASun()
        {
            Check.Equal("\uE706", Weather.Parse(Clear(1)).Glyph, "sun by day");
            Check.Equal("\uE708", Weather.Parse(Clear(0)).Glyph, "moon by night");
        }

        private static void AnUnexpectedResponseIsAnErrorNotAZero()
        {
            Check.Throws(delegate { Weather.Parse("{}"); }, "empty object");
            Check.Throws(delegate { Weather.Parse("{\"error\":true,\"reason\":\"Latitude must be in range of -90 to 90\"}"); }, "API error body");
            Check.Throws(delegate { Weather.Parse("<html>captive portal</html>"); }, "not JSON");
        }

        private static void ParsesACitySearch()
        {
            List<GeoPlace> places = Weather.ParseSearch(
                "{\"results\":[{\"id\":1265873,\"name\":\"Kozhikode\",\"latitude\":11.24802,\"longitude\":75.7804," +
                "\"country\":\"India\",\"admin1\":\"Kerala\"},{\"name\":\"Nowhere\"}],\"generationtime_ms\":0.5}");
            Check.Equal(1, places.Count, "the entry without coordinates is skipped");
            Check.Equal("Kozhikode, Kerala, India", places[0].Label, "label");
            Check.Near(11.24802, places[0].Lat, "latitude");
            Check.Near(75.7804, places[0].Lon, "longitude");
        }

        private static void NoMatchIsAnEmptyList()
        {
            Check.Equal(0, Weather.ParseSearch("{\"generationtime_ms\":0.53}").Count, "no results key");
        }

        private static void DescribesHowOldAReadingIs()
        {
            Check.Equal("50 MIN", WeatherCard.AgeText(TimeSpan.FromMinutes(50)), "minutes");
            Check.Equal("3 H", WeatherCard.AgeText(TimeSpan.FromHours(3)), "hours");
            Check.Equal("3 D", WeatherCard.AgeText(TimeSpan.FromDays(3)), "days");
        }
    }

    internal static class SettingsTests
    {
        private static void DefaultsMatchTheOriginalBehaviour()
        {
            Settings s = new Settings();
            foreach (string name in Settings.CardNames) Check.True(s.IsCardEnabled(name), name + " on by default");
            Check.Equal(80, s.CardOpacity, "opacity");
            Check.Equal(UnitChoice.Auto, s.Units, "units");
            Check.Equal(ClockChoice.Auto, s.Clock, "clock");
            Check.True(s.IdlePhotos, "idle photos");
            Check.True(s.Location == null, "no location");
            Check.True(s.ResolvedPhotoFolder.EndsWith("wallpaper", StringComparison.OrdinalIgnoreCase), "default folder");
        }

        private static void RoundTripsThroughJson()
        {
            Settings s = new Settings();
            s.Units = UnitChoice.Fahrenheit;
            s.Clock = ClockChoice.Hour24;
            s.CardOpacity = 65;
            s.IdlePhotos = false;
            s.PhotoFolder = @"D:\Art & Photos";
            s.SetCardEnabled("Notes", false);
            s.Location = new WeatherLocation();
            s.Location.Name = "Kozhikode";
            s.Location.Lat = 11.2448;
            s.Location.Lon = 75.7721;

            Settings back = Settings.FromJson(s.ToJson());
            Check.Equal(UnitChoice.Fahrenheit, back.Units, "units");
            Check.Equal(ClockChoice.Hour24, back.Clock, "clock");
            Check.Equal(65, back.CardOpacity, "opacity");
            Check.True(!back.IdlePhotos, "idle photos");
            Check.Equal(@"D:\Art & Photos", back.PhotoFolder, "folder");
            Check.True(!back.IsCardEnabled("Notes") && back.IsCardEnabled("Clock"), "cards");
            Check.Equal("Kozhikode", back.Location.Name, "place");
            Check.Near(11.2448, back.Location.Lat, "lat");
            Check.Near(75.7721, back.Location.Lon, "lon");
        }

        private static void WrongTypesFallBackToDefaults()
        {
            Settings s = Settings.FromJson(
                "{\"units\":5,\"clock\":true,\"cardOpacity\":\"lots\",\"idlePhotos\":\"yes\",\"cards\":\"none\",\"weather\":{\"lat\":\"north\"}}");
            Check.Equal(UnitChoice.Auto, s.Units, "units");
            Check.Equal(ClockChoice.Auto, s.Clock, "clock");
            Check.Equal(80, s.CardOpacity, "opacity");
            Check.True(s.IdlePhotos, "idle photos");
            Check.True(s.IsCardEnabled("Clock"), "cards");
            Check.True(s.Location == null, "location");
        }

        private static void OpacityIsClamped()
        {
            Check.Equal(40, Settings.FromJson("{\"cardOpacity\":5}").CardOpacity, "too low");
            Check.Equal(100, Settings.FromJson("{\"cardOpacity\":500}").CardOpacity, "too high");
        }

        private static void ImpossibleCoordinatesAreRejected()
        {
            // 112448 is what "11.2448" turned into when the old installer parsed
            // it on a German, Spanish or Brazilian Windows.
            Check.True(Settings.FromJson("{\"weather\":{\"name\":\"X\",\"lat\":112448,\"lon\":75.7}}").Location == null, "latitude out of range");
            Check.True(Settings.FromJson("{\"weather\":{\"name\":\"X\",\"lat\":11.2,\"lon\":757721}}").Location == null, "longitude out of range");
        }

        private static void ToleratesAByteOrderMarkAndEmptyText()
        {
            Check.Equal(UnitChoice.Celsius, Settings.FromJson("\uFEFF{\"units\":\"c\"}").Units, "BOM");
            Check.Equal(UnitChoice.Auto, Settings.FromJson("").Units, "empty");
            Check.Throws(delegate { Settings.FromJson("{{{ not json"); }, "malformed");
        }
    }

    internal static class StoreTests
    {
        private static readonly string[] NoLegacy = new string[0];

        private static void LayoutRoundTrips()
        {
            using (TempDir dir = new TempDir())
            {
                Store a = new Store(dir.Path, NoLegacy);
                a.Layout["Clock"] = new Point2(904, 32);
                a.Layout["Notes"] = new Point2(-800.5, 560);
                a.Locked = true;
                a.SaveLayout();

                Store b = new Store(dir.Path, NoLegacy);
                b.LoadLayout();
                Check.True(b.Locked, "lock flag");
                Check.Near(904, b.Layout["Clock"].Left, "clock left");
                Check.Near(-800.5, b.Layout["Notes"].Left, "negative, fractional left");
                Check.True(!File.Exists(dir.File("layout.json.tmp")), "no temp file left behind");
            }
        }

        private static void ReadsALayoutWrittenByTheOldVersion()
        {
            using (TempDir dir = new TempDir())
            {
                File.WriteAllText(dir.File("layout.json"),
                    "\uFEFF{\"Clock\":{\"Left\":904,\"Top\":32},\"Weather\":{\"Left\":1080.0,\"Top\":32.0},\"_locked\":false}");
                Store s = new Store(dir.Path, NoLegacy);
                s.LoadLayout();
                Check.Equal(2, s.Layout.Count, "two cards");
                Check.Near(1080, s.Layout["Weather"].Left, "weather left");
            }
        }

        private static void ACorruptLayoutIsSurvived()
        {
            using (TempDir dir = new TempDir())
            {
                File.WriteAllText(dir.File("layout.json"), "{\"Clock\":{\"Left\":90");     // truncated mid-write
                Store s = new Store(dir.Path, NoLegacy);
                s.LoadLayout();
                Check.Equal(0, s.Layout.Count, "falls back to defaults");
            }
        }

        private static void NotesRoundTrip()
        {
            using (TempDir dir = new TempDir())
            {
                Store a = new Store(dir.Path, NoLegacy);
                a.SaveNotes("first");
                a.SaveNotes("second line\r\nwith unicode \u00E9\u4E2D");
                string text;
                Check.True(new Store(dir.Path, NoLegacy).TryLoadNotes(out text), "readable");
                Check.Equal("second line\r\nwith unicode \u00E9\u4E2D", text, "latest text");
                Check.True(!File.Exists(dir.File("notes.txt.tmp")), "no temp file left behind");
            }
        }

        private static void MissingNotesAreEmptyButReadable()
        {
            using (TempDir dir = new TempDir())
            {
                string text;
                Check.True(new Store(dir.Path, NoLegacy).TryLoadNotes(out text), "a missing file is not a failure");
                Check.Equal("", text, "empty");
            }
        }

        private static void UnreadableNotesAreReportedNotReturnedAsEmpty()
        {
            // The data-loss bug: a failed read came back as "", and the first
            // keystroke then saved that over the real file.
            using (TempDir dir = new TempDir())
            {
                File.WriteAllText(dir.File("notes.txt"), "precious");
                Store s = new Store(dir.Path, NoLegacy);
                using (new FileStream(dir.File("notes.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    string text;
                    Check.True(!s.TryLoadNotes(out text), "a locked file must report failure");
                }
                Check.Equal("precious", File.ReadAllText(dir.File("notes.txt")), "file untouched");
            }
        }

        private static void CopiesStateFromTheOldFolderOnce()
        {
            using (TempDir old = new TempDir())
            using (TempDir dir = new TempDir())
            {
                File.WriteAllText(old.File("layout.json"), "{\"Clock\":{\"Left\":904,\"Top\":32},\"_locked\":true}");
                File.WriteAllText(old.File("notes.txt"), "buy milk");
                // As the old installer wrote it: Windows PowerShell adds a BOM.
                File.WriteAllText(old.File("weather.json"), "\uFEFF{\"name\":\"Kozhikode\",\"lat\":11.2448,\"lon\":75.7721}");

                Store s = new Store(dir.Path, new string[] { old.Path });
                s.LoadLayout();
                s.LoadSettings();
                string notes;
                Check.True(s.TryLoadNotes(out notes), "notes readable");
                Check.Equal("buy milk", notes, "notes carried over");
                Check.True(s.Locked && s.Layout.ContainsKey("Clock"), "layout carried over");
                Check.Equal("Kozhikode", s.Settings.Location.Name, "weather location carried over");
                Check.True(File.Exists(old.File("notes.txt")), "the old folder is copied, not emptied");
                Check.True(File.Exists(old.File("MOVED.txt")), "the old folder says where things went");
            }
        }

        private static void NeverImportsOverExistingState()
        {
            using (TempDir old = new TempDir())
            using (TempDir dir = new TempDir())
            {
                File.WriteAllText(old.File("notes.txt"), "old notes");
                File.WriteAllText(dir.File("notes.txt"), "current notes");
                Store s = new Store(dir.Path, new string[] { old.Path });
                string notes;
                s.TryLoadNotes(out notes);
                Check.Equal("current notes", notes, "existing notes win");
                Check.True(!File.Exists(old.File("MOVED.txt")), "nothing was imported");
            }
        }

        private static void CorruptSettingsAreKeptAsideAndDefaultsUsed()
        {
            using (TempDir dir = new TempDir())
            {
                File.WriteAllText(dir.File("settings.json"), "{{{ not json");
                Store s = new Store(dir.Path, NoLegacy);
                s.LoadSettings();
                Check.Equal(80, s.Settings.CardOpacity, "defaults");
                Check.True(File.Exists(dir.File("settings.json.bad")), "the unreadable file is kept for inspection");
            }
        }

        private static void SettingsSaveAndLoad()
        {
            using (TempDir dir = new TempDir())
            {
                Store a = new Store(dir.Path, NoLegacy);
                a.Settings.Clock = ClockChoice.Hour24;
                a.SaveSettings();
                Store b = new Store(dir.Path, NoLegacy);
                b.LoadSettings();
                Check.Equal(ClockChoice.Hour24, b.Settings.Clock, "clock choice");
            }
        }
    }

    internal static class LogTests
    {
        private static void RotatesInsteadOfGrowingForever()
        {
            using (TempDir dir = new TempDir())
            {
                Log.Start(dir.Path);
                string filler = new string('x', 1000);
                for (int i = 0; i < 600; i++) Log.Warn("test", filler);     // about 600 KB in all
                Log.Start(System.IO.Path.GetTempPath());                      // let go of the folder

                long current = new FileInfo(dir.File("widgets.log")).Length;
                Check.True(File.Exists(dir.File("widgets.log.1")), "an older file exists");
                Check.True(current <= Log.MaxBytes + 2048, "the live file stays near the cap, was " + current);
                Check.True(Directory.GetFiles(dir.Path).Length == 2, "never more than two files");
            }
        }

        private static void DescribesTheWholeExceptionChainOnOneLine()
        {
            Exception ex = new InvalidOperationException("outer\r\nsecond line", new IOException("inner"));
            Check.Equal("InvalidOperationException: outer  --> IOException: inner", Log.Describe(ex), "chain");
        }
    }

    internal static class StatsTests
    {
        private static void ReadsSaneValues()
        {
            Stats stats = new Stats();
            stats.Read();
            StatsSnapshot s = stats.Read();
            Check.True(s.CpuPercent >= 0 && s.CpuPercent <= 100, "cpu " + s.CpuPercent);
            Check.True(s.RamPercent > 0 && s.RamPercent <= 100, "ram " + s.RamPercent);
            Check.True(s.DiskFreeGB > 0, "free space on the system drive " + s.DiskFreeGB);
        }

        private static void PluggedInIsNotTheSameAsCharging()
        {
            StatsSnapshot s = new StatsSnapshot();
            s.BatteryPercent = 100; s.OnMains = true; s.Charging = false;
            Check.Equal("Fully charged", Stats.BatteryStatus(s), "full on mains");
            s.BatteryPercent = 80;
            Check.Equal("Plugged in", Stats.BatteryStatus(s), "held below full on mains");
            s.Charging = true;
            Check.Equal("Charging", Stats.BatteryStatus(s), "charging");
            s.OnMains = false; s.Charging = false;
            Check.Equal("On battery", Stats.BatteryStatus(s), "on battery");
        }
    }

    internal static class MarkupTests
    {
        private static Window Load(string xaml)
        {
            using (StringReader sr = new StringReader(xaml))
            using (XmlReader xr = XmlReader.Create(sr))
            {
                return (Window)XamlReader.Load(xr);
            }
        }

        private static void Has(Window w, string card, params string[] names)
        {
            foreach (string name in names)
            {
                Check.True(w.FindName(name) != null, card + " markup defines " + name);
            }
        }

        private static void EveryCardParsesAndDefinesItsNamedElements()
        {
            Theme theme = Theme.From(new Settings(), false);
            Has(Load(Markup.Window(Markup.Clock, theme)), "Clock", "TimeText", "AmPmText", "DayText", "DateText");
            Has(Load(Markup.Window(Markup.Weather, theme)), "Weather", "WxPlace", "WxIcon", "WxTemp", "WxDesc", "WxRange", "WxCredit");
            Has(Load(Markup.Window(Markup.Media, theme)), "Media", "MediaTitle", "MediaArtist", "BtnPrev", "BtnPlay", "BtnNext",
                "ArtBorder", "ArtBeat", "Beat1", "Beat2", "Beat3", "Beat4", "WaveDim", "WaveLit", "WaveHost");
            Has(Load(Markup.Window(Markup.SysStats, theme)), "System", "CpuText", "CpuBar", "RamText", "RamBar", "DiskText");
            Has(Load(Markup.Window(Markup.Battery, theme)), "Battery", "BattText", "BattStatus", "BattBar");
            Has(Load(Markup.Window(Markup.Notes, theme)), "Notes", "NotesBox");
            Has(Load(Markup.Window(Markup.DayBanner, theme)), "DayBanner", "BannerRow");
        }

        private static void ContrastThemeParsesToo()
        {
            Theme theme = Theme.From(new Settings(), true);
            Check.Equal("#FF000000", theme.CardBackground, "opaque card");
            Load(Markup.Window(Markup.Weather, theme));
        }

        private static void NoTokenIsLeftUnfilled()
        {
            Theme theme = Theme.From(new Settings(), false);
            foreach (string body in new string[] { Markup.Clock, Markup.Weather, Markup.Media, Markup.SysStats,
                                                   Markup.Battery, Markup.Notes, Markup.DayBanner })
            {
                Check.True(Markup.Window(body, theme).IndexOf("__", StringComparison.Ordinal) < 0, "an unfilled __TOKEN__ remains");
            }
        }

        private static void TheButtonsHaveNamesAScreenReaderCanSay()
        {
            Window w = Load(Markup.Window(Markup.Media, Theme.From(new Settings(), false)));
            foreach (string name in new string[] { "BtnPrev", "BtnPlay", "BtnNext" })
            {
                string spoken = System.Windows.Automation.AutomationProperties.GetName((DependencyObject)w.FindName(name));
                Check.True(!string.IsNullOrEmpty(spoken), name + " has an accessible name");
            }
        }

        private static void CardOpacityBecomesTheBackgroundAlpha()
        {
            Settings s = new Settings();
            Check.Equal("#CC121215", Theme.From(s, false).CardBackground, "the default is the measured original look");
            s.CardOpacity = 100;
            Check.Equal("#FF121215", Theme.From(s, false).CardBackground, "fully opaque");
        }

        private static void SettingsWindowParsesAndDefinesItsControls()
        {
            Window w = Load(SettingsWindow.Xaml);
            Has(w, "Settings", "PlaceText", "CityBox", "SearchButton", "SearchStatus", "CityList",
                "UnitAuto", "UnitC", "UnitF", "ClockAuto", "Clock12", "Clock24", "IdlePhotos", "PhotoFolder",
                "OpacitySlider", "OpacityText", "StartupBox", "SaveButton", "CancelButton",
                "VersionRun", "CreditLink", "FolderLink");
            foreach (string name in Settings.CardNames) Has(w, "Settings", "Card" + name);
        }
    }
}
