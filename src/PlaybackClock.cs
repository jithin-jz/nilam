using System;

namespace Nilam
{
    /// <summary>What the media session reported on the last poll.</summary>
    internal struct PlaybackState
    {
        internal bool HasTimeline;
        internal double DurationSeconds;
        internal double PositionSeconds;
        /// <summary>When the player stamped PositionSeconds, in UTC.</summary>
        internal DateTime PositionAtUtc;
        internal bool Playing;
        internal string TrackKey;
    }

    /// <summary>
    /// The media card's own playback clock.
    ///
    /// Needed because a player is allowed to report a position once and never
    /// refresh it: Spotify stamps Position=0 at track start and leaves it there
    /// for the whole track. Extrapolating from that fixed anchor is right only
    /// while playback is running, so the elapsed time is accumulated here
    /// instead, advancing only while Playing. It has no dependency on WPF or the
    /// media session, which is what lets the tests drive it with a fake clock.
    /// </summary>
    internal class PlaybackClock
    {
        private string trackKey = "";
        private double seconds;
        private double anchorPos = -1;
        private DateTime lastTick = DateTime.MinValue;

        /// <summary>Elapsed seconds as this clock currently believes them.</summary>
        internal double Seconds { get { return seconds; } }

        /// <summary>
        /// Advances to <paramref name="utcNow"/> and returns progress through the
        /// track, 0 to 1. Returns 0 when the player reports no usable timeline.
        /// </summary>
        internal double Advance(DateTime utcNow, PlaybackState s)
        {
            double fraction = 0;

            if (!s.HasTimeline || s.DurationSeconds <= 0)
            {
                trackKey = "";
                anchorPos = -1;
                seconds = 0;
            }
            else
            {
                double reported = s.PositionSeconds;
                double drift = (utcNow - s.PositionAtUtc).TotalSeconds;
                if (drift < 0) drift = 0;
                string key = s.TrackKey == null ? "" : s.TrackKey;

                if (key != trackKey)
                {
                    // A different track: take the player's word for it once.
                    trackKey = key;
                    anchorPos = reported;
                    seconds = reported + (s.Playing ? drift : 0);
                }
                else if (Math.Abs(reported - anchorPos) > 0.75)
                {
                    // The reported position moved, so this player does keep it
                    // current - or the user seeked. Either way, believe it.
                    anchorPos = reported;
                    seconds = reported + (s.Playing ? drift : 0);
                }
                else if (s.Playing && lastTick != DateTime.MinValue)
                {
                    // The player is not refreshing its position, so run our own
                    // clock. Advancing only while Playing is what makes a pause
                    // hold the wave still rather than collapse or race ahead.
                    seconds += (utcNow - lastTick).TotalSeconds;
                }

                if (seconds < 0) seconds = 0;
                if (seconds > s.DurationSeconds) seconds = s.DurationSeconds;
                fraction = seconds / s.DurationSeconds;
            }

            lastTick = utcNow;
            return fraction;
        }

        /// <summary>
        /// Marks time as observed without advancing. Called while the user is
        /// scrubbing, so the seconds spent dragging are not added on release.
        /// </summary>
        internal void Touch(DateTime utcNow)
        {
            lastTick = utcNow;
        }

        /// <summary>
        /// Holds the clock at a requested position until the player reports
        /// where it actually landed, rather than snapping back to the stale
        /// position while the player catches up.
        /// </summary>
        internal void SeekTo(double targetSeconds)
        {
            seconds = targetSeconds < 0 ? 0 : targetSeconds;
        }
    }
}
