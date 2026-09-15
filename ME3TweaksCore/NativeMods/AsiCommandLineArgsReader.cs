using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ME3TweaksCore.Diagnostics;
using Newtonsoft.Json;

namespace ME3TweaksCore.NativeMods
{
    /// <summary>
    /// A command line switch discovered in an installed ASI mod, along with which file it came from.
    /// </summary>
    public class AsiCommandLineSwitchInfo
    {
        /// <summary>
        /// The filename (not full path) of the ASI that declared this switch.
        /// </summary>
        public string SourceAsiFileName { get; set; }

        /// <summary>
        /// The switch, as embedded by the ASI.
        /// </summary>
        public AsiCommandLineSwitch Switch { get; set; }
    }

    /// <summary>
    /// Reads the "ME3TWEAKS_CMDLINE_ARGS" RCDATA resource that ASI mods may embed to self-document
    /// the command line switches they support. The containing DLL is mapped as a data file only
    /// (LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE) - it is never loaded as code, so this
    /// works regardless of the ASI's architecture or whether its other dependencies (the game SDK, etc)
    /// are resolvable in this process.
    /// </summary>
    public static class AsiCommandLineArgsReader
    {
        private const string ResourceName = @"ME3TWEAKS_CMDLINE_ARGS";
        private const string ResourceTypeRcData = @"#10"; // RT_RCDATA

        private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
        private const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;

        [DllImport(@"kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

        [DllImport(@"kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindResource(IntPtr hModule, string lpName, string lpType);

        [DllImport(@"kernel32.dll")]
        private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

        [DllImport(@"kernel32.dll")]
        private static extern IntPtr LockResource(IntPtr hResData);

        [DllImport(@"kernel32.dll")]
        private static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

        [DllImport(@"kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        /// <summary>
        /// Scans every .asi file in the given directory and returns the command line switches
        /// declared by any of them. ASIs with no embedded resource, or an unparseable one, are skipped.
        /// </summary>
        /// <param name="asiDirectory">Directory to scan for .asi files</param>
        public static List<AsiCommandLineSwitchInfo> ScanDirectoryForCommandLineSwitches(string asiDirectory)
        {
            var results = new List<AsiCommandLineSwitchInfo>();
            if (string.IsNullOrEmpty(asiDirectory) || !Directory.Exists(asiDirectory))
                return results;

            foreach (var asiFile in Directory.GetFiles(asiDirectory, @"*.asi"))
            {
                var embedded = ReadEmbeddedCommandLineArgs(asiFile);
                if (embedded?.Commands == null)
                    continue;

                var fileName = Path.GetFileName(asiFile);
                foreach (var sw in embedded.Commands)
                {
                    if (string.IsNullOrWhiteSpace(sw?.Name))
                        continue;
                    results.Add(new AsiCommandLineSwitchInfo { SourceAsiFileName = fileName, Switch = sw });
                }
            }

            return results;
        }

        /// <summary>
        /// Reads and parses the embedded command line switch resource from a single ASI file.
        /// Returns null if the file has no such resource, or it could not be read/parsed.
        /// </summary>
        public static AsiEmbeddedCommandLineArgs ReadEmbeddedCommandLineArgs(string asiFilePath)
        {
            var json = ReadRcDataAsUtf8String(asiFilePath, ResourceName);
            if (json == null)
                return null;

            try
            {
                return JsonConvert.DeserializeObject<AsiEmbeddedCommandLineArgs>(json);
            }
            catch (Exception e)
            {
                MLog.Error($@"Failed to parse embedded command line args resource in {asiFilePath}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads a named RCDATA resource out of a native module without loading it as code.
        /// </summary>
        private static string ReadRcDataAsUtf8String(string modulePath, string resourceName)
        {
            var hModule = LoadLibraryEx(modulePath, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
            if (hModule == IntPtr.Zero)
                return null;

            try
            {
                var hResInfo = FindResource(hModule, resourceName, ResourceTypeRcData);
                if (hResInfo == IntPtr.Zero)
                    return null;

                var hResData = LoadResource(hModule, hResInfo);
                if (hResData == IntPtr.Zero)
                    return null;

                var pResData = LockResource(hResData);
                if (pResData == IntPtr.Zero)
                    return null;

                var size = SizeofResource(hModule, hResInfo);
                if (size == 0)
                    return null;

                var bytes = new byte[size];
                Marshal.Copy(pResData, bytes, 0, (int)size);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch (Exception e)
            {
                MLog.Error($@"Failed to read embedded command line args resource from {modulePath}: {e.Message}");
                return null;
            }
            finally
            {
                FreeLibrary(hModule);
            }
        }
    }
}
