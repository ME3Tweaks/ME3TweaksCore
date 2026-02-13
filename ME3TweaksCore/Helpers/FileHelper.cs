using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace ME3TweaksCore.Helpers
{
    /// <summary>
    /// Helper class for file operations
    /// </summary>
    public static partial class FileHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public FILETIME CreationTime;
            public FILETIME LastAccessTime;
            public FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        #region From SO - Do not change to LibraryImport
        // Source - https://stackoverflow.com/a/37760332
        // Posted by jdphenix, modified by community. See post 'Timeline' for change history
        // Retrieved 2025-12-13, License - CC BY-SA 3.0

        [DllImport(@"kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(
            IntPtr hFile,
            out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        // Source - https://stackoverflow.com/a/37760332
        // Posted by jdphenix, modified by community. See post 'Timeline' for change history
        // Retrieved 2025-12-13, License - CC BY-SA 3.0

        [DllImport(@"kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateFile(
            [MarshalAs(UnmanagedType.LPTStr)] string filename,
            [MarshalAs(UnmanagedType.U4)] FileAccess access,
            [MarshalAs(UnmanagedType.U4)] FileShare share,
            IntPtr securityAttributes,
            [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
            [MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
            IntPtr templateFile);

        // Source - https://stackoverflow.com/a/37760332
        // Posted by jdphenix, modified by community. See post 'Timeline' for change history
        // Retrieved 2025-12-13, License - CC BY-SA 3.0

        [DllImport(@"kernel32.dll", SetLastError = true)]
        [SuppressUnmanagedCodeSecurity]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        #endregion

        // Source - https://stackoverflow.com/a/37760332
        // Posted by jdphenix, modified by community. See post 'Timeline' for change history
        // Retrieved 2025-12-13, License - CC BY-SA 3.0

        /// <summary>
        /// Determines if two paths are on the same volume. Use this to determine how to handle using File.Move().
        /// If an error occurs in this method, items are treated as on the same volume, calling code should assume
        /// File.Move() is the default for safety. The source path must exist, and the directory at least for the dest must
        /// also exist.
        /// </summary>
        /// <param name="src">The source path.</param>
        /// <param name="dest">The destination path.</param>
        /// <returns>True if both paths are on the same volume; otherwise, false.</returns>
        public static bool IsOnSameVolume(string src, string dest)
        {

            IntPtr srcHandle = IntPtr.Zero, destHandle = IntPtr.Zero;
            bool ensureDeletion = false;

            try
            {
                if (!File.Exists(dest))
                {
                    if (Directory.GetParent(dest).Exists)
                    {
                        ensureDeletion = true;
                        File.Create(dest).Dispose();
                    }
                    else
                    {
                        // Treat as same for safety
                        return true;
                    }
                }

                srcHandle = CreateFile(src, FileAccess.Read, FileShare.ReadWrite, IntPtr.Zero,
                    FileMode.Open, (FileAttributes)0x02000000, IntPtr.Zero);
                destHandle = CreateFile(dest, FileAccess.Read, FileShare.ReadWrite, IntPtr.Zero,
                    FileMode.Open, (FileAttributes)0x02000000, IntPtr.Zero);
                var srcInfo = new BY_HANDLE_FILE_INFORMATION();
                var destInfo = new BY_HANDLE_FILE_INFORMATION();

                if (!GetFileInformationByHandle(srcHandle, out srcInfo))
                {
                    throw new Exception(Marshal.GetLastWin32Error().ToString());
                }

                if (!GetFileInformationByHandle(destHandle, out destInfo))
                {
                    throw new Exception(Marshal.GetLastWin32Error().ToString());
                }

                return srcInfo.VolumeSerialNumber == destInfo.VolumeSerialNumber;
            } catch
            {
                // On error, assume same volume for safety
                return true;
            }
            finally
            {
                if (srcHandle != IntPtr.Zero)
                {
                    CloseHandle(srcHandle);
                }

                if (destHandle != IntPtr.Zero)
                {
                    CloseHandle(destHandle);
                }

                if (ensureDeletion)
                {
                    try
                    {
                        File.Delete(dest);
                    }
                    catch
                    {
                        // Ignore exceptions during cleanup
                    }
                }
            }
        }
    }
}
