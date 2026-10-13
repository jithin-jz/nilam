using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;   // System.IO.Path is also in scope

namespace Nilam
{
    /// <summary>
    /// Now playing: title, artist, cover art, transport buttons, and a sine-wave
    /// progress bar that doubles as a scrubber.
    /// </summary>
    internal class MediaCard : Card
    {
        private const double WaveAmplitude = 5;
        private const int WaveCycles = 7;
        private const double WaveStep = 2;        // sample spacing in px

        private const int PhotoSeconds = 20;

        private const string PlayGlyph = "\uE768";
        private const string PauseGlyph = "\uE769";

        private TextBlock title, artist;
        private Button play;
        private Border art;
        private StackPanel beat;
        private Panel waveHost;
        private RectangleGeometry waveClip;
        private readonly Border[] beatBars = new Border[4];

        private readonly PlaybackClock clock = new PlaybackClock();
        private PlaybackState state;
        private PhotoShuffle photos;
        private DispatcherTimer refreshSoon;

        private bool busy;
        private bool playing;
        private bool canSeek;
        private bool idle;
        private bool photoBusy;
        private bool dragging;
        private double dragFraction;
        private int artVersion;
        private bool disposed;

        internal MediaCard(WidgetApp app) : base(app, "Media") { }

        protected override string Body { get { return Markup.Media; } }

        protected override void OnBuilt()
        {
            title = Find<TextBlock>("MediaTitle");
            artist = Find<TextBlock>("MediaArtist");
            play = Find<Button>("BtnPlay");
            art = Find<Border>("ArtBorder");
            beat = Find<StackPanel>("ArtBeat");
            waveHost = Find<Panel>("WaveHost");
            for (int i = 0; i < beatBars.Length; i++) beatBars[i] = Find<Border>("Beat" + (i + 1));

            Find<Button>("BtnPrev").Click += delegate { Send(App.Player.PreviousAsync, Native.VK_MEDIA_PREV); };
            play.Click += delegate { Send(App.Player.TogglePlayPauseAsync, Native.VK_MEDIA_PLAY); };
            Find<Button>("BtnNext").Click += delegate { Send(App.Player.NextAsync, Native.VK_MEDIA_NEXT); };

            Settings settings = App.Store.Settings;
            if (settings.IdlePhotos) photos = new PhotoShuffle(settings.ResolvedPhotoFolder);

            BuildWave();
            WireUpWave();

            refreshSoon = new DispatcherTimer();
            refreshSoon.Interval = TimeSpan.FromMilliseconds(350);
            refreshSoon.Tick += delegate(object s, EventArgs e)
            {
                refreshSoon.Stop();
                Refresh();
            };

            // Timers are deliberately coarse: the session is polled every two
            // seconds and the wave moves once a second.
            App.AddTimer(TimeSpan.FromSeconds(2), Refresh);
            App.AddTimer(TimeSpan.FromSeconds(1), UpdateWave);
            App.AddTimer(TimeSpan.FromSeconds(PhotoSeconds), delegate { if (idle) ShowRandomPhoto(); });
        }

        internal override void OnShown()
        {
            Refresh();
        }

        internal override void Dispose()
        {
            disposed = true;
            if (refreshSoon != null) refreshSoon.Stop();
            SetBeat(false);
            base.Dispose();
        }

        // ------------------------------------------------------------ transport
        /// <summary>
        /// Sends a transport command to the session shown on the card, and only
        /// falls back to a global media key when there is no session to ask -
        /// which is what lets Play wake a player that has gone quiet.
        /// </summary>
        private async void Send(Func<Task<bool>> viaSession, byte fallbackKey)
        {
            bool done = false;
            try { done = await viaSession(); }
            catch (Exception ex) { Log.Error("media", ex); }
            if (disposed) return;
            if (!done) Native.SendMediaKey(fallbackKey);

            // Pick up the new state now rather than at the next two-second poll,
            // so the play/pause glyph answers the click.
            refreshSoon.Stop();
            refreshSoon.Start();
        }

        private async void Refresh()
        {
            if (busy) return;      // a stalled player must not queue up calls
            busy = true;
            try
            {
                MediaSnapshot snap = await App.Player.ReadAsync();
                if (!disposed) Apply(snap);
            }
            catch (Exception ex)
            {
                Log.Error("media", ex);
            }
            finally
            {
                busy = false;
            }
        }

        private void Apply(MediaSnapshot snap)
        {
            playing = snap.IsPlaying;
            canSeek = snap.CanSeek;

            state.TrackKey = snap.TrackKey == null ? "" : snap.TrackKey;
            state.HasTimeline = snap.HasTimeline;
            state.PositionSeconds = snap.Position.TotalSeconds;
            state.DurationSeconds = snap.Duration.TotalSeconds;
            state.PositionAtUtc = snap.PositionAt.UtcDateTime;
            state.Playing = snap.IsPlaying;

            // Only advertise the wave as grabbable when the player will honour a
            // seek, so a dead click is never invited.
            waveHost.Cursor = (canSeek && snap.HasTimeline) ? Cursors.Hand : Cursors.Arrow;

            if (!snap.HasSession)
            {
                title.Text = "";
                artist.Text = "";
                SetPlayGlyph(false);
                state.HasTimeline = false;
                UpdateWave();
                // Idle: a picture instead of the equaliser - the bars would
                // suggest something is loaded when nothing is. Set up once on
                // the way in; repeating it every poll would wipe the picture
                // each time.
                if (!idle)
                {
                    idle = true;
                    ClearArt();
                    beat.Visibility = Visibility.Collapsed;
                    ShowRandomPhoto();
                }
                return;
            }

            if (idle)
            {
                // Drop the slideshow picture, or it would pass for the new
                // track's cover until the real art arrives.
                idle = false;
                ClearArt();
            }

            if (snap.Title != null) title.Text = snap.Title;
            if (snap.Artist != null) artist.Text = snap.Artist;
            SetPlayGlyph(snap.IsPlaying);
            // Three distinct cases, and conflating any two of them shows the
            // wrong thing:
            //   new picture   -> paint it
            //   art cleared   -> this track has none, run the equaliser
            //   nothing new   -> leave whatever is on screen alone
            if (snap.Thumbnail != null) SetArt(snap.Thumbnail);
            else if (snap.ArtCleared) ClearArt();

            UpdateWave();
        }

        private void SetPlayGlyph(bool isPlaying)
        {
            play.Content = isPlaying ? PauseGlyph : PlayGlyph;
            AutomationProperties.SetName(play, isPlaying ? "Pause" : "Play");
        }

        // ------------------------------------------------------------ idle photos
        private async void ShowRandomPhoto()
        {
            if (photos == null || photoBusy) return;
            photoBusy = true;
            try
            {
                BitmapSource bmp = await photos.NextAsync();
                // Null means the folder had nothing usable: keep the empty tile.
                // A track may also have started while this one was decoding.
                if (bmp != null && idle && !disposed) PaintArt(bmp);
            }
            catch (Exception ex)
            {
                Log.Error("photos", ex);
            }
            finally
            {
                photoBusy = false;
            }
        }

        // ----------------------------------------------------------- art fallback
        // Four bars, each with its own period and peak. The periods are chosen
        // not to be multiples of one another: whole-number ratios make the bars
        // resynchronise into a single block every few seconds, which is exactly
        // what gives a fake equaliser away.
        private static readonly double[] BeatPeriods = { 0.62, 0.47, 0.73, 0.55 };
        private static readonly double[] BeatPeaks = { 0.85, 1.00, 0.62, 0.92 };
        private const double BeatRest = 0.28;
        private const int BeatFrameRate = 15;

        /// <summary>
        /// Runs the equaliser while something is actually playing, and parks the
        /// bars at a flat resting height when it is not - a paused track that
        /// keeps dancing reads as a bug.
        /// </summary>
        private void SetBeat(bool running)
        {
            for (int i = 0; i < beatBars.Length; i++)
            {
                Border bar = beatBars[i];
                if (bar == null) continue;
                ScaleTransform st = bar.RenderTransform as ScaleTransform;
                if (st == null) continue;

                if (!running)
                {
                    // Passing null hands the property back to its local value.
                    // Without it the bar freezes at whatever height the animation
                    // happened to be at when it stopped.
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    st.ScaleY = BeatRest;
                    continue;
                }

                DoubleAnimation a = new DoubleAnimation();
                a.From = BeatRest;
                a.To = BeatPeaks[i];
                a.Duration = new Duration(TimeSpan.FromSeconds(BeatPeriods[i]));
                a.AutoReverse = true;
                a.RepeatBehavior = RepeatBehavior.Forever;
                SineEase ease = new SineEase();
                ease.EasingMode = EasingMode.EaseInOut;
                a.EasingFunction = ease;
                // Every widget window sets AllowsTransparency=True, which puts
                // WPF into software rendering - there is no GPU compositing for
                // a layered window - so each animation frame repaints the card
                // on the CPU. An equaliser does not need anywhere near the
                // default 60 of them a second.
                Timeline.SetDesiredFrameRate(a, BeatFrameRate);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        }

        private void ClearArt()
        {
            artVersion++;       // a cover still decoding must not land after this
            art.Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
            beat.Visibility = Visibility.Visible;
            SetBeat(playing);
        }

        /// <summary>
        /// Decodes cover art off the UI thread. A player may hand over anything
        /// up to 8 MB - a 3000px cover embedded in a local file, say - and
        /// decoding that at full size on the UI thread stalled every card.
        /// </summary>
        private async void SetArt(byte[] bytes)
        {
            int version = ++artVersion;
            BitmapSource bmp = null;
            try
            {
                bmp = await Task.Run(new Func<BitmapSource>(delegate { return DecodeTile(bytes); }));
            }
            catch (Exception ex)
            {
                Log.Error("media", ex);
            }
            if (disposed || version != artVersion) return;     // a newer cover, or a clear, won
            if (bmp == null) ClearArt();
            else PaintArt(bmp);
        }

        private static BitmapSource DecodeTile(byte[] bytes)
        {
            try
            {
                bool wide = false, large = false;
                using (MemoryStream peek = new MemoryStream(bytes))
                {
                    // Header only: DelayCreation skips the pixels.
                    BitmapFrame frame = BitmapFrame.Create(peek, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    wide = frame.PixelWidth > frame.PixelHeight;
                    large = Math.Min(frame.PixelWidth, frame.PixelHeight) > PhotoShuffle.DecodeSize;
                }

                BitmapImage bmp = new BitmapImage();
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;   // decode now, release the stream
                    // Only ever scale down: pinning the short side keeps the
                    // square tile filled, and small art is left at its own size.
                    if (large)
                    {
                        if (wide) bmp.DecodePixelHeight = PhotoShuffle.DecodeSize;
                        else bmp.DecodePixelWidth = PhotoShuffle.DecodeSize;
                    }
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }
                bmp.Freeze();
                return bmp;
            }
            catch (Exception)
            {
                // Bytes that are not a picture WPF can decode: treat the track
                // as having no art.
                return null;
            }
        }

        /// <summary>Fills the art tile with a picture - cover art or an idle photo.</summary>
        private void PaintArt(BitmapSource bmp)
        {
            ImageBrush brush = new ImageBrush(bmp);
            brush.Stretch = Stretch.UniformToFill;
            art.Background = brush;
            beat.Visibility = Visibility.Collapsed;
            // Stop the equaliser once it is hidden: an animation on a
            // collapsed element still ticks the composition clock forever.
            SetBeat(false);
        }

        // ------------------------------------------------------------------ wave
        // The progress indicator is a sine wave drawn twice: a dim copy spanning
        // the whole track, and a lit copy on top revealed left to right.
        //
        // The lit copy is uncovered by moving a clip rectangle rather than by
        // rebuilding its path, so the geometry is built once and frozen and a
        // progress update costs one Rect assignment instead of re-sampling 150
        // points every second.
        private void BuildWave()
        {
            ShapePath dim = Find<ShapePath>("WaveDim");
            ShapePath lit = Find<ShapePath>("WaveLit");

            Geometry wave = MakeWaveGeometry();
            dim.Data = wave;
            lit.Data = wave;          // one frozen geometry, referenced twice

            waveClip = new RectangleGeometry(new Rect(0, 0, 0, Markup.WaveHeight));
            lit.Clip = waveClip;
        }

        private static Geometry MakeWaveGeometry()
        {
            double mid = Markup.WaveHeight / 2;
            PathFigure figure = new PathFigure();
            figure.StartPoint = new Point(0, mid);

            PolyLineSegment segment = new PolyLineSegment();
            for (double x = WaveStep; x <= Markup.WaveWidth; x += WaveStep)
            {
                double y = mid + (WaveAmplitude *
                    Math.Sin(2 * Math.PI * WaveCycles * x / Markup.WaveWidth));
                segment.Points.Add(new Point(x, y));
            }
            figure.Segments.Add(segment);

            PathGeometry geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            return geometry;
        }

        private void UpdateWave()
        {
            if (waveClip == null) return;

            DateTime now = DateTime.UtcNow;
            double fraction;
            if (dragging)
            {
                // While a scrub is in progress the pointer owns the wave: showing
                // the player's position instead would fight the finger.
                fraction = dragFraction;
                clock.Touch(now);
            }
            else
            {
                fraction = clock.Advance(now, state);
            }
            waveClip.Rect = new Rect(0, 0, Markup.WaveWidth * fraction, Markup.WaveHeight);
        }

        // --------------------------------------------------------------- scrub
        private void WireUpWave()
        {
            Panel host = waveHost;

            host.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                // Marking this handled is what stops the window-wide drag handler
                // from picking the card up instead of seeking.
                e.Handled = true;
                if (!canSeek || state.DurationSeconds <= 0) return;

                dragging = true;
                dragFraction = FractionFromPoint(host, e.GetPosition(host));
                host.CaptureMouse();
                UpdateWave();
            };

            host.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!dragging) return;
                dragFraction = FractionFromPoint(host, e.GetPosition(host));
                UpdateWave();
            };

            host.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!dragging) return;
                e.Handled = true;
                dragging = false;
                host.ReleaseMouseCapture();

                // One seek, on release. Seeking per mouse-move would fire dozens
                // of requests across a single drag, which players handle badly.
                CommitSeek(dragFraction);
            };

            // Losing capture (Explorer restart, display change) must not leave the
            // wave stuck under a drag that is no longer happening.
            host.LostMouseCapture += delegate { dragging = false; };
        }

        private static double FractionFromPoint(FrameworkElement host, Point p)
        {
            double width = host.ActualWidth > 0 ? host.ActualWidth : Markup.WaveWidth;
            double fraction = p.X / width;
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            return fraction;
        }

        private async void CommitSeek(double fraction)
        {
            TimeSpan target = TimeSpan.FromSeconds(state.DurationSeconds * fraction);

            // Hold the wave at the requested spot rather than snapping back to the
            // stale position while the player catches up. The clock re-anchors
            // once the player reports the new spot.
            clock.SeekTo(target.TotalSeconds);
            UpdateWave();

            try { await App.Player.SeekAsync(target); }
            catch (Exception ex) { Log.Error("media", ex); }
        }
    }
}
