using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Nilam
{
    /// <summary>
    /// The modern Windows folder picker - the same Explorer-style dialog File
    /// Explorer uses - through its COM interface. WPF on .NET Framework has no
    /// folder dialog, and Windows Forms' FolderBrowserDialog shows the old tree
    /// view and would pull a whole UI framework into the process for one button.
    /// </summary>
    internal static class FolderPicker
    {
        /// <summary>Returns the chosen folder, or null if the person cancelled.</summary>
        internal static string Pick(IntPtr owner, string title, string startFolder)
        {
            IFileDialog dialog = (IFileDialog)new FileOpenDialogClass();
            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                dialog.SetTitle(title);
                return Show(dialog, owner, startFolder);
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        private static string Show(IFileDialog dialog, IntPtr owner, string startFolder)
        {
            if (!string.IsNullOrEmpty(startFolder) && Directory.Exists(startFolder))
            {
                IShellItem start;
                Guid iid = typeof(IShellItem).GUID;
                if (SHCreateItemFromParsingName(startFolder, IntPtr.Zero, ref iid, out start) == 0)
                {
                    dialog.SetFolder(start);
                    Marshal.ReleaseComObject(start);
                }
            }

            // Any failure, cancelling included, comes back as an HRESULT.
            if (dialog.Show(owner) != 0) return null;

            IShellItem result;
            dialog.GetResult(out result);
            try
            {
                IntPtr path;
                result.GetDisplayName(SIGDN_FILESYSPATH, out path);
                try { return Marshal.PtrToStringUni(path); }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            finally { Marshal.ReleaseComObject(result); }
        }

        private const uint FOS_PICKFOLDERS = 0x20;
        private const uint FOS_FORCEFILESYSTEM = 0x40;
        private const uint FOS_PATHMUSTEXIST = 0x800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
                                                              [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogClass { }

        // The methods must stay in exactly this order: COM calls them by their
        // position in the interface, not by name. Unused ones are declared only
        // to hold their place.
        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr filterSpec);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder);
            void SetFolder(IShellItem folder);
            void GetFolder(out IShellItem folder);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName(out IntPtr name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int placement);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint form, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
