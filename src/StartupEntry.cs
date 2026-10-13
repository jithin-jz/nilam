using System;
using System.IO;
using Microsoft.Win32;

namespace Nilam
{
    /// <summary>
    /// "Start with Windows". A per-user Run value pointing straight at the exe.
    ///
    /// Releases before 2.0 - when the app was still called Desktop Widgets - used
    /// a Startup-folder shortcut that ran a VBScript launcher through
    /// wscript.exe. VBScript is deprecated and is due to be switched off by
    /// default, and the exe never needed it: it is built as a windowless
    /// program, so nothing flashes when it starts.
    /// </summary>
    internal static class StartupEntry
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // The old shortcut keeps its old name; this is not AppInfo.Name.
        private const string LegacyShortcutName = "DesktopWidgets.lnk";

        private static string LegacyShortcut
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                                    LegacyShortcutName);
            }
        }

        internal static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (key != null && key.GetValue(AppInfo.Name) != null) return true;
                }
                return File.Exists(LegacyShortcut);
            }
            catch (Exception ex)
            {
                Log.Error("startup", ex);
                return false;
            }
        }

        internal static void Set(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled) key.SetValue(AppInfo.Name, "\"" + AppInfo.ExePath + "\"");
                    else key.DeleteValue(AppInfo.Name, false);
                }
                // Either way the old shortcut goes: left in place it would start
                // a second copy, or keep starting one the user just turned off.
                if (File.Exists(LegacyShortcut)) File.Delete(LegacyShortcut);
            }
            catch (Exception ex)
            {
                Log.Error("startup", ex);
            }
        }
    }
}
