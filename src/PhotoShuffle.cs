using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Nilam
{
    /// <summary>
    /// Random pictures from one folder, shown in the media card's art tile while
    /// nothing is playing. Read-only: files are opened for reading and never
    /// modified, and nothing leaves the machine. The folder and the on/off
    /// switch are in the settings window.
    /// </summary>
    internal class PhotoShuffle
    {
        private static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

        // A runaway folder (a synced drive, say) must not turn a scan into a
        // long walk; a few thousand candidates is plenty to pick from.
        private const int MaxFiles = 5000;
        private static readonly TimeSpan RescanAfter = TimeSpan.FromMinutes(10);

        // The tile is 96px. Decoding straight to 288px on the short side covers
        // up to 300% scaling, and keeps a 20 MB photo at a few hundred KB in
        // memory instead of the ~48 MB a full-size decode of 12 megapixels costs.
        internal const int DecodeSize = 288;

        private readonly string folder;
        private readonly Random rng = new Random();
        private List<string> files = new List<string>();
        private DateTime scannedAt = DateTime.MinValue;
        private string lastPick;

        /// <param name="folder">
        /// The one folder to draw from. By default this is a subfolder of
        /// Pictures rather than the whole library: the rest is mostly
        /// screenshots, which make poor ambient art.
        /// </param>
        internal PhotoShuffle(string folder)
        {
            this.folder = folder;
        }

        /// <summary>
        /// Scans and decodes on the thread pool; the result is frozen, so the UI
        /// thread can use it directly. Null when there is nothing to show.
        /// </summary>
        internal Task<BitmapSource> NextAsync()
        {
            return Task.Run(new Func<BitmapSource>(Next));
        }

        private BitmapSource Next()
        {
            if (DateTime.UtcNow - scannedAt > RescanAfter)
            {
                files = Scan(folder);
                scannedAt = DateTime.UtcNow;
            }

            // A few tries: a file can be deleted or turn out to be undecodable
            // between the scan and the pick.
            for (int attempt = 0; attempt < 5 && files.Count > 0; attempt++)
            {
                string path = files[rng.Next(files.Count)];
                if (files.Count > 1 && path == lastPick) continue;   // never the same one twice running

                BitmapSource bmp = Load(path);
                if (bmp != null)
                {
                    lastPick = path;
                    return bmp;
                }
                files.Remove(path);
            }
            return null;
        }

        private static List<string> Scan(string root)
        {
            List<string> found = new List<string>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return found;

            // Walked by hand rather than with SearchOption.AllDirectories, which
            // abandons the whole scan on the first folder it is denied.
            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0 && found.Count < MaxFiles)
            {
                string dir = pending.Pop();
                try
                {
                    foreach (string f in Directory.EnumerateFiles(dir))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (Array.IndexOf(Extensions, ext) >= 0) found.Add(f);
                        if (found.Count >= MaxFiles) break;
                    }
                    foreach (string sub in Directory.EnumerateDirectories(dir))
                    {
                        // Skip junctions and symlinks, which can loop back on
                        // themselves or lead somewhere far outside the folder.
                        FileAttributes a = File.GetAttributes(sub);
                        if ((a & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) == 0)
                            pending.Push(sub);
                    }
                }
                catch (UnauthorizedAccessException) { }   // a folder we may not read: skip it
                catch (IOException) { }                   // vanished or unreadable mid-scan: skip it
            }
            return found;
        }

        private static BitmapSource Load(string path)
        {
            try
            {
                Rotation turn;
                bool wide;
                Peek(path, out turn, out wide);
                BitmapImage bmp = new BitmapImage();
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;    // decode now, release the file
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    // The tile is square and fills UniformToFill, so it is the
                    // short side that must reach DecodeSize. Pinning the width
                    // instead would leave a 16:9 wallpaper only 162px tall.
                    if (wide) bmp.DecodePixelHeight = DecodeSize;
                    else bmp.DecodePixelWidth = DecodeSize;
                    bmp.Rotation = turn;
                    bmp.StreamSource = fs;
                    bmp.EndInit();
                }
                bmp.Freeze();
                return bmp;
            }
            catch (Exception)
            {
                // Not a picture WPF can decode, or gone since the scan. The
                // caller drops it from the list and tries another.
                return null;
            }
        }

        /// <summary>
        /// Reads the header only (DelayCreation skips the pixels) for the stored
        /// shape and the EXIF orientation. Phone cameras store the sensor image
        /// unrotated and record the real orientation in EXIF; WPF ignores that
        /// tag, so without it a portrait photo comes out lying on its side.
        /// </summary>
        private static void Peek(string path, out Rotation turn, out bool wide)
        {
            turn = Rotation.Rotate0;
            wide = false;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    BitmapFrame frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    // Stored, pre-rotation axes. Should DecodePixel* instead act
                    // on the rotated axes for a 90/270 EXIF turn, only one side
                    // is ever pinned, so the cost is a softer tile, not a
                    // stretched one.
                    wide = frame.PixelWidth > frame.PixelHeight;

                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext != ".jpg" && ext != ".jpeg") return;
                    BitmapMetadata meta = frame.Metadata as BitmapMetadata;
                    object tag = meta == null ? null : meta.GetQuery("System.Photo.Orientation");
                    if (tag == null) return;
                    switch (Convert.ToInt32(tag))
                    {
                        case 3: turn = Rotation.Rotate180; break;
                        case 6: turn = Rotation.Rotate90; break;
                        case 8: turn = Rotation.Rotate270; break;
                    }
                }
            }
            catch (Exception)
            {
                // No readable header or metadata: decode unrotated, width-pinned.
            }
        }
    }
}
