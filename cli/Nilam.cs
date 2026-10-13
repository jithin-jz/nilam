using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Nilam
{
    // The `nilam` command. Deliberately a separate exe from the widget app: it
    // needs to keep running (to launch the installer) even while the thing it
    // manages is being stopped and overwritten.
    //
    // It only ever installs a published release. Its predecessor, dwx, used to
    // download and run whatever install.ps1 was on the main branch at that
    // moment, which made every push an instant, unreviewed update for everyone
    // who ran it.
    internal static class Cli
    {
        private const string Repo = "jithin-jz/nilam";
        private const string Site = "https://github.com/" + Repo;
        private const string InstallerName = "install.ps1";
        private const string ChecksumsName = "SHA256SUMS.txt";

        private enum Lookup { Found, NoRelease, Unreachable }

        /// <summary>
        /// The folder this copy manages. The command is installed to the install
        /// folder's bin\cli\, so the install is two folders up - which keeps
        /// working when Nilam was installed somewhere other than the default. A
        /// stray copy elsewhere falls back to the default location.
        /// </summary>
        private static string InstallDir
        {
            get
            {
                try
                {
                    string above = Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..", ".."));
                    if (File.Exists(Path.Combine(above, "VERSION"))) return above;
                }
                catch (Exception) { }   // an unusual path: use the default below
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "Nilam");
            }
        }

        private static int Main(string[] args)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (NotSupportedException) { }

            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "version";
            switch (cmd)
            {
                case "version": case "-v": case "--version": return CmdVersion();
                // install and update run the same installer, which is safe to
                // run over an existing copy. The difference is that update
                // stops when there is nothing newer, and install (a repair)
                // carries on regardless.
                case "install": return CmdInstall(true);
                case "update": return CmdInstall(false);
                default: return CmdHelp();
            }
        }

        private static int CmdVersion()
        {
            string local = ReadLocalVersion();
            Console.WriteLine("Nilam");
            Console.WriteLine("  installed: " + (local ?? "not installed"));

            string tag;
            Lookup found = LatestRelease(out tag);
            if (found == Lookup.Unreachable)
            {
                Console.WriteLine("  latest   : could not check (no network / GitHub unreachable)");
                return 0;
            }
            if (found == Lookup.NoRelease)
            {
                Console.WriteLine("  latest   : no release has been published yet");
                return 0;
            }

            string latest = SemVer.FromTag(tag);
            Console.WriteLine("  latest   : " + latest);
            Console.WriteLine();
            if (local == null) Console.WriteLine("Not installed yet. Run: nilam install");
            else if (SemVer.IsNewer(latest, local))
            {
                Console.WriteLine("Update available: " + local + " -> " + latest);
                Console.WriteLine("Run: nilam update");
            }
            else Console.WriteLine("Up to date.");
            return 0;
        }

        private static int CmdInstall(bool evenIfCurrent)
        {
            string tag;
            Lookup found = LatestRelease(out tag);
            if (found == Lookup.Unreachable)
            {
                Console.WriteLine("Could not reach GitHub to look for the latest release. Check the connection and try again.");
                return 1;
            }
            if (found == Lookup.NoRelease)
            {
                Console.WriteLine("No release has been published yet, so there is nothing to install from.");
                Console.WriteLine("To run a development copy, clone " + Site + " and run install.ps1 there.");
                return 1;
            }

            string local = ReadLocalVersion();
            string latest = SemVer.FromTag(tag);
            if (!evenIfCurrent && local != null && !SemVer.IsNewer(latest, local))
            {
                Console.WriteLine("Already up to date (" + local + "). Use `nilam install` to reinstall anyway.");
                return 0;
            }

            Console.WriteLine((local == null ? "Installing" : "Updating to") + " Nilam " + latest + " ...");

            // One fixed folder, overwritten each time, so repeated updates do
            // not leave a trail of installer copies in %TEMP%.
            string work = Path.Combine(Path.GetTempPath(), "Nilam-update");
            string script = Path.Combine(work, InstallerName);
            string sums = Path.Combine(work, ChecksumsName);
            string from = Site + "/releases/download/" + tag + "/";
            try
            {
                Directory.CreateDirectory(work);
                Download(from + InstallerName, script);
                Download(from + ChecksumsName, sums);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not download the installer for " + tag + ": " + ex.Message);
                return 1;
            }

            // The script is about to be run with the execution policy bypassed,
            // so check it is the file the release was published with. This
            // catches a truncated or corrupted download; it is not a
            // signature, and does not protect against a release that was
            // itself published by someone who should not have been able to.
            string expected = ExpectedHash(sums, InstallerName);
            string actual = Sha256(script);
            if (expected == null || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("The downloaded installer does not match the checksum published with " + tag + ".");
                Console.WriteLine("  expected: " + (expected ?? "(no entry for " + InstallerName + ")"));
                Console.WriteLine("  actual  : " + actual);
                Console.WriteLine("Nothing was installed. Try again; if it keeps happening, report it at " + Site + "/issues");
                return 1;
            }

            Console.WriteLine("A new window will show installer progress.");

            // install.ps1 is run from a file with -File rather than piped into
            // -Command "irm ... | iex". That in-memory pipe-and-execute shape is
            // exactly what Windows Defender's ML heuristics flag as
            // Trojan:Win32/Commando.A!ml when it comes from an unsigned exe -
            // confirmed by testing, not theoretical.
            //
            // Fire-and-forget: the installer replaces this very exe, and
            // waiting here would keep its file locked against that.
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -InstallDir \"" + InstallDir + "\"",
                UseShellExecute = true
            };
            try
            {
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not start the installer: " + ex.Message);
                return 1;
            }
            return 0;
        }

        private static int CmdHelp()
        {
            Console.WriteLine("nilam - command-line tool for Nilam, desktop widgets for Windows");
            Console.WriteLine();
            Console.WriteLine("  nilam version   Show the installed version and the latest release");
            Console.WriteLine("  nilam update    Install the latest release if it is newer");
            Console.WriteLine("  nilam install   Install or reinstall the latest release");
            Console.WriteLine();
            Console.WriteLine("To open the widgets themselves, find Nilam in the Start menu.");
            return 0;
        }

        private static string ReadLocalVersion()
        {
            try
            {
                string path = Path.Combine(InstallDir, "VERSION");
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>
        /// Finds the newest release by asking where /releases/latest redirects:
        /// to /releases/tag/vX.Y.Z when there is one, and to plain /releases
        /// when there is none. Unlike the REST API this needs no JSON parser
        /// and has no hourly request limit.
        /// </summary>
        private static Lookup LatestRelease(out string tag)
        {
            tag = null;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Site + "/releases/latest");
                req.AllowAutoRedirect = false;
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.UserAgent = "nilam-cli";
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    string location = resp.Headers["Location"];
                    const string marker = "/releases/tag/";
                    int at = location == null ? -1 : location.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) return Lookup.NoRelease;
                    tag = Uri.UnescapeDataString(location.Substring(at + marker.Length)).Trim('/');
                    return tag.Length == 0 ? Lookup.NoRelease : Lookup.Found;
                }
            }
            catch (WebException ex)
            {
                // GitHub answered, and the answer was "no such page": the
                // repository has no releases page to redirect from. That is a
                // different thing from not being able to reach GitHub at all.
                HttpWebResponse answer = ex.Response as HttpWebResponse;
                if (answer != null && answer.StatusCode == HttpStatusCode.NotFound) return Lookup.NoRelease;
                return Lookup.Unreachable;
            }
        }

        private static void Download(string url, string path)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.UserAgent = "nilam-cli";
            using (WebResponse resp = req.GetResponse())
            using (Stream rs = resp.GetResponseStream())
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                rs.CopyTo(fs);
            }
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Reads one entry from a sha256sum-style file: the hash, whitespace, then the file name.</summary>
        private static string ExpectedHash(string sumsPath, string fileName)
        {
            foreach (string line in File.ReadAllLines(sumsPath))
            {
                string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                string name = parts[parts.Length - 1].TrimStart('*');
                if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) return parts[0];
            }
            return null;
        }
    }
}
