using System;
using System.IO;

namespace CodingBackProp
{
    /// <summary>
    /// Finds a data file without any hard-coded path, so the project runs on any machine.
    /// Search order:
    ///   1. next to the running .exe (bin\Debug)   <- works when the CSV is set to "Copy to Output Directory"
    ///   2. the folders above the .exe (up to 6 levels) - finds the CSV in the project / repo folder
    ///   3. the current working directory
    /// Returns null if nothing is found.
    /// </summary>
    public static class DataPaths
    {
        public static string FindFile(string fileName)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int level = 0; level < 7 && !string.IsNullOrEmpty(dir); level++)
            {
                string candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;

                DirectoryInfo parent = Directory.GetParent(dir);
                dir = parent == null ? null : parent.FullName;
            }

            string cwd = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            return File.Exists(cwd) ? cwd : null;
        }

        /// <summary>Folder where generated files (clean CSV, logs) are written. Always writable.</summary>
        public static string OutputFolder
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }
    }
}
