using ME3TweaksCore.Diagnostics;
using Microsoft.Win32;
using NickStrupat;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ME3TweaksCore.Helpers
{
    /// <summary>
    /// Contains information about Wine detection and version information for running on Linux/Unix systems via Wine compatibility layer
    /// </summary>
    [Localizable(false)]
    public static class WineWorkarounds
    {
        /// <summary>
        /// Indicates whether the application is running under Wine
        /// </summary>
        public static bool WineDetected { get; set; }

        /// <summary>
        /// The detected version of Wine, if running under Wine
        /// </summary>
        public static Version WineDetectedVersion { get; set; }

        /// <summary>
        /// The name of the host operating system kernel
        /// </summary>
        public static string WineHostKernelName { get; set; }

        /// <summary>
        /// The version of the host operating system kernel
        /// </summary>
        public static Version WineHostKernelVersion { get; set; }


        /// <summary>
        /// Checks if Wine is present
        /// </summary>
        /// <returns>True if Wine is detected, false otherwise</returns>
        private static bool IsWineDetected()
        {
            // these values are normally set whenever in a wine prefix
            using (RegistryKey WineDbgCrashDialog = Registry.CurrentUser.OpenSubKey(@"Software\Wine\WineDbg"))
            using (RegistryKey DebugRelayExclude = Registry.CurrentUser.OpenSubKey(@"Software\Wine\Debug"))
            {
                Version WineVersion = WineGetVersion();
                // None of these should be set if running in a real Windows environment
                return WineVersion != null || WineDbgCrashDialog != null || DebugRelayExclude != null;
            }
        }

        [DllImport(@"ntdll.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr wine_get_version();

        [DllImport(@"kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport(@"kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport(@"kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        /// <summary>
        /// Get Wine version
        /// </summary>
        /// <returns>Version if available, otherwise null</returns>
        public static Version WineGetVersion()
        {
            try
            {
#if DEBUG
                // This is in if debug so it doesn't breakpoint in debug builds on Windows 
                // First check if the wine_get_version function exists in ntdll.dll
                IntPtr ntdllModule = LoadLibrary(@"ntdll.dll");
                if (ntdllModule == IntPtr.Zero)
                    return null;
#endif
                try
                {
#if DEBUG
                    IntPtr procAddress = GetProcAddress(ntdllModule, @"wine_get_version");
                    if (procAddress == IntPtr.Zero)
                    {
                        // Function doesn't exist (not running under Wine)
                        return null;
                    }
#endif
                    var v = new Version(Marshal.PtrToStringAnsi(wine_get_version()));
                    return v;
                }
                finally
                {
#if DEBUG
                    FreeLibrary(ntdllModule);
#endif
                }
            }
            catch
            {
                return null;
            }
        }


        [DllImport(@"ntdll.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern void wine_get_host_version(out IntPtr sysname, out IntPtr release);

        /// <summary>
        /// Gets both host kernel name and version
        /// <para>
        ///     Parameters will be set to null if function is not available.<br />
        ///     This generally means the host is Windows, or Wine is hiding its version.
        /// </para>
        /// </summary>
        /// <param name="sysname">"Linux" if host is Linux, "Darwin" if host is MacOS.</param>
        /// <param name="release">Kernel version if host is Linux, untested for MacOS.</param>
        private static void WineGetHostVersion(out string sysname, out Version release)
        {
            try
            {
                wine_get_host_version(out IntPtr systemName, out IntPtr releaseName);
                sysname = Marshal.PtrToStringAnsi(systemName);
                release = new Version(Marshal.PtrToStringAnsi(releaseName));
            }
            catch
            {
                sysname = null;
                release = null;
            }
        }

        /// <summary>
        /// Initializes Wine detection and related version information for the current environment.
        /// </summary>
        /// <remarks>This method sets static properties indicating whether Wine is present, the detected
        /// Wine version, and the host kernel information if Wine is detected. It should be called before accessing
        /// Wine-related properties to ensure accurate detection results. This method is thread-safe and can be called
        /// multiple times; subsequent calls will update the detection state.</remarks>
        public static void Init()
        {
            WineDetected = IsWineDetected();
            if (WineDetected)
            {
                WineDetectedVersion = WineGetVersion();
                WineGetHostVersion(out string HostKernelName, out Version HostKernelVersion);
                WineHostKernelName = HostKernelName;
                WineHostKernelVersion = HostKernelVersion;

                // Needs changed to Mac if that can be determined (darwin?)
                ComputerInfo.ForcePlatform(EOSPlatform.Linux);
            }
        }

        /// <summary>
        /// Logs information about Wine if detected
        /// </summary>
        public static void LogWineInfo()
        {
            if (WineDetected)
            {
                MLog.Information(@"Wine detected, running under Linux or MacOS");
                if (WineDetectedVersion != null)
                {
                    MLog.Information($@"Wine version: {WineDetectedVersion}");
                    MLog.Information($@"Host Kernel: {WineHostKernelName} {WineHostKernelVersion}");
                }
            }
        }
    }
}
