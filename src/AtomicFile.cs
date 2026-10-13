using System.IO;
using System.Text;

namespace Nilam
{
    internal static class AtomicFile
    {
        /// <summary>
        /// Replaces a file's contents so that a reader - or the next start after
        /// a crash - sees either the whole old text or the whole new text.
        ///
        /// File.WriteAllText truncates first and writes second. The installer
        /// stops the widgets with a hard kill, and a kill landing between those
        /// two steps left an empty notes file. Here the text goes to a sibling
        /// file, is flushed to disk, and is then swapped in with one rename.
        /// </summary>
        internal static void WriteAllText(string path, string text)
        {
            string temp = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(text == null ? "" : text);
            using (FileStream fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }

            try
            {
                File.Replace(temp, path, null);
            }
            catch (IOException)
            {
                // File.Replace is not available on every file system (FAT, some
                // network shares and redirected folders). Fall back to a copy,
                // which is not atomic but still never leaves the file empty for
                // longer than the copy takes.
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
        }
    }
}
