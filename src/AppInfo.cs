using System;
using System.IO;
using System.Reflection;

namespace Nilam
{
    /// <summary>
    /// Every name the app shows to the outside world - folders, the mutex, the
    /// registry value, the HTTP user agent - lives here, so renaming the product
    /// is a change to this file rather than a hunt through the sources.
    /// </summary>
    internal static class AppInfo
    {
        /// <summary>Folder, registry and mutex name. No spaces.</summary>
        internal const string Name = "Nilam";

        /// <summary>The name a person reads.</summary>
        internal const string DisplayName = "Nilam";

        internal const string Repo = "jithin-jz/nilam";
        internal const string RepoUrl = "https://github.com/" + Repo;

        /// <summary>
        /// Set this environment variable to run against a different state folder
        /// - a scratch profile for development or tests - without touching the
        /// real layout and notes. Such an instance also gets its own
        /// single-instance lock, so it can run beside the installed copy.
        /// </summary>
        internal const string StateDirVariable = "NILAM_STATE_DIR";

        // State folders written by earlier releases, relative to %LOCALAPPDATA%.
        // Store copies the first one it finds into the current folder on first
        // run. If the product is ever renamed again, add the old name here.
        internal static readonly string[] LegacyStateFolders = { "KiroDesktopWidgets" };

        private static string version;

        /// <summary>The release this exe was built from, as written in VERSION.</summary>
        internal static string Version
        {
            get
            {
                if (version == null)
                {
                    AssemblyInformationalVersionAttribute a = (AssemblyInformationalVersionAttribute)
                        Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(),
                                                     typeof(AssemblyInformationalVersionAttribute));
                    version = a == null ? "0.0.0" : a.InformationalVersion;
                }
                return version;
            }
        }

        internal static string UserAgent
        {
            get { return Name + "/" + Version + " (+" + RepoUrl + ")"; }
        }

        internal static string ExePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        /// <summary>The folder above bin\, where fonts\ sits.</summary>
        internal static string InstallRoot
        {
            get { return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..")); }
        }
    }
}
