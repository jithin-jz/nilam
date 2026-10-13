using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Nilam
{
    internal class MediaSnapshot
    {
        internal bool HasSession;
        internal string Title;
        internal string Artist;
        internal bool IsPlaying;
        /// <summary>Album art bytes, or null when unchanged or unavailable.</summary>
        internal byte[] Thumbnail;

        // Thumbnail is non-null only on a poll that produced a *different*
        // picture from the one already on screen, so a null Thumbnail normally
        // means "nothing new", not "no art". This flag carries the one case
        // that genuinely means no art, so the UI knows when to drop a cover
        // rather than leaving the previous track's on screen.
        internal bool ArtCleared;
        /// <summary>Identity of the current track, used to avoid re-decoding art.</summary>
        internal string TrackKey;

        /// <summary>False when the player reports no usable timeline, which some
        /// browsers and streams do; the wave then stays unfilled.</summary>
        internal bool HasTimeline;
        internal TimeSpan Position;
        internal TimeSpan Duration;
        /// <summary>
        /// When the player last reported Position. Players update it only
        /// occasionally, so the UI advances it locally from this stamp rather than
        /// polling the session faster.
        /// </summary>
        internal DateTimeOffset PositionAt;

        /// <summary>
        /// Whether the player accepts a seek. It varies by app - Chrome reports
        /// true, some players report false - so the wave only acts as a scrubber
        /// when this is set.
        /// </summary>
        internal bool CanSeek;
    }

    /// <summary>
    /// Reads and drives the system media transport controls, the same source the
    /// Windows volume flyout uses, so it works for any player that reports to
    /// Windows.
    /// </summary>
    internal class MediaMonitor
    {
        private const uint MaxArtBytes = 8 * 1024 * 1024;

        private GlobalSystemMediaTransportControlsSessionManager manager;
        private string lastTrackKey = "";
        private bool failing;

        // Browsers publish their own app icon the instant playback starts and
        // the real cover a beat later, so a single read per track latches the
        // icon for the whole song. Re-reading for a short window after a track
        // change picks up the correction; the window is bounded, so a long track
        // costs a handful of extra reads rather than one every poll forever.
        private static readonly TimeSpan ArtSettleWindow = TimeSpan.FromSeconds(16);

        // -1 is "nothing decided for this track yet", which has to stay distinct
        // from 0, the hash of "this track genuinely has no art".
        private const long ArtUnknown = -1;
        private long lastArtHash = ArtUnknown;
        private DateTime artSettleUntil = DateTime.MinValue;

        internal async Task<MediaSnapshot> ReadAsync()
        {
            MediaSnapshot snap = new MediaSnapshot();
            try
            {
                if (manager == null)
                {
                    manager = await WinRtAsync.ToTask(
                        GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                }

                GlobalSystemMediaTransportControlsSession session =
                    manager == null ? null : manager.GetCurrentSession();

                if (session == null)
                {
                    lastTrackKey = "";
                    failing = false;
                    return snap;   // HasSession stays false
                }

                snap.HasSession = true;

                GlobalSystemMediaTransportControlsSessionMediaProperties props =
                    await WinRtAsync.ToTask(session.TryGetMediaPropertiesAsync());

                if (props != null)
                {
                    snap.Title = string.IsNullOrWhiteSpace(props.Title) ? "Unknown track" : props.Title;
                    snap.Artist = props.Artist == null ? "" : props.Artist;
                    snap.TrackKey = snap.Title + "|" + snap.Artist;

                    if (snap.TrackKey != lastTrackKey)
                    {
                        lastTrackKey = snap.TrackKey;
                        lastArtHash = ArtUnknown;
                        artSettleUntil = DateTime.UtcNow + ArtSettleWindow;
                    }

                    if (DateTime.UtcNow < artSettleUntil)
                    {
                        byte[] art = await ReadThumbnailAsync(props);
                        long hash = ArtHash(art);

                        // Only hand the bytes up when the picture actually
                        // changed, so a steady cover is decoded once rather than
                        // on every poll of the settle window.
                        if (hash != lastArtHash)
                        {
                            lastArtHash = hash;
                            if (art != null) snap.Thumbnail = art;
                            else snap.ArtCleared = true;
                        }
                    }
                }

                GlobalSystemMediaTransportControlsSessionPlaybackInfo info = session.GetPlaybackInfo();
                snap.IsPlaying = info != null &&
                    info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (info != null && info.Controls != null)
                {
                    snap.CanSeek = info.Controls.IsPlaybackPositionEnabled;
                }

                // Timeline is a separate call and is allowed to fail on its own -
                // a player can report a title but no position - so it must not
                // discard the rest of the snapshot.
                try
                {
                    GlobalSystemMediaTransportControlsSessionTimelineProperties tl =
                        session.GetTimelineProperties();
                    if (tl != null)
                    {
                        TimeSpan span = tl.EndTime - tl.StartTime;
                        if (span > TimeSpan.Zero)
                        {
                            snap.Duration = span;
                            snap.Position = tl.Position - tl.StartTime;
                            snap.PositionAt = tl.LastUpdatedTime;
                            snap.HasTimeline = true;
                        }
                    }
                }
                catch (Exception)
                {
                    // No timeline for this player; the wave simply stays unfilled.
                }

                failing = false;
            }
            catch (Exception ex)
            {
                // A player closing mid-call invalidates the manager; rebuild next
                // tick. Forget the track too: the card may fall back to its idle
                // picture on this snapshot, and without a fresh settle window the
                // cover would not be sent again for the rest of the song.
                manager = null;
                lastTrackKey = "";
                if (!failing) Log.Warn("media", Log.Describe(ex));
                failing = true;     // one line per run of failures, not one per poll
            }
            return snap;
        }

        /// <summary>
        /// Asks the current player to jump to a position. Returns false when the
        /// player refuses or advertises no seek support, so the caller can leave
        /// the wave where the player actually is.
        /// </summary>
        internal Task<bool> SeekAsync(TimeSpan position)
        {
            return Command(delegate(GlobalSystemMediaTransportControlsSession s)
            {
                return s.TryChangePlaybackPositionAsync(position.Ticks);
            });
        }

        // The transport buttons go to the session shown on the card. False means
        // there was no session or it refused, and the caller falls back to a
        // media key.
        internal Task<bool> TogglePlayPauseAsync()
        {
            return Command(delegate(GlobalSystemMediaTransportControlsSession s) { return s.TryTogglePlayPauseAsync(); });
        }

        internal Task<bool> NextAsync()
        {
            return Command(delegate(GlobalSystemMediaTransportControlsSession s) { return s.TrySkipNextAsync(); });
        }

        internal Task<bool> PreviousAsync()
        {
            return Command(delegate(GlobalSystemMediaTransportControlsSession s) { return s.TrySkipPreviousAsync(); });
        }

        private async Task<bool> Command(
            Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> send)
        {
            try
            {
                if (manager == null) return false;
                GlobalSystemMediaTransportControlsSession session = manager.GetCurrentSession();
                if (session == null) return false;
                return await WinRtAsync.ToTask(send(session));
            }
            catch (Exception ex)
            {
                Log.Warn("media", Log.Describe(ex));
                return false;
            }
        }

        /// <summary>
        /// FNV-1a over the encoded bytes. This only has to answer "same picture
        /// as last poll?", and the value never leaves the process, so a fast
        /// non-cryptographic hash is the right tool - it costs one pass over
        /// ~25 KB instead of decoding the image to compare it.
        /// </summary>
        private static long ArtHash(byte[] bytes)
        {
            if (bytes == null) return 0;
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < bytes.Length; i++)
            {
                h ^= bytes[i];
                h *= 1099511628211UL;
            }
            long v = (long)(h & 0x7FFFFFFFFFFFFFFFUL);
            // 0 is reserved for "no art" and -1 for "undecided", so a real
            // picture must never hash to either.
            return (v == 0 || v == ArtUnknown) ? 1 : v;
        }

        /// <summary>
        /// Pulls the album art through a WinRT DataReader rather than
        /// AsStreamForRead, which is unavailable for the same metadata reason
        /// described in WinRtAsync.
        /// </summary>
        private static async Task<byte[]> ReadThumbnailAsync(
            GlobalSystemMediaTransportControlsSessionMediaProperties props)
        {
            IRandomAccessStreamWithContentType stream = null;
            DataReader reader = null;
            try
            {
                IRandomAccessStreamReference reference = props.Thumbnail;
                if (reference == null) return null;

                stream = await WinRtAsync.ToTask(reference.OpenReadAsync());
                if (stream == null) return null;

                ulong size = stream.Size;
                if (size == 0 || size > MaxArtBytes) return null;

                reader = new DataReader(stream);
                uint loaded = await WinRtAsync.ToTask(reader.LoadAsync((uint)size));
                if (loaded == 0) return null;

                byte[] bytes = new byte[loaded];
                reader.ReadBytes(bytes);
                return bytes;
            }
            catch (Exception)
            {
                // Art is decoration: a cover that cannot be read is the same as
                // a track that has none.
                return null;
            }
            finally
            {
                // Disposing a WinRT stream whose player has just exited can
                // throw; there is nothing useful to do about it here.
                try { if (reader != null) reader.Dispose(); } catch (Exception) { }
                try { if (stream != null) stream.Dispose(); } catch (Exception) { }
            }
        }
    }
}
