using ME3TweaksCore.Diagnostics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ME3TweaksCore.Helpers
{
    /// <summary>
    /// Information about a process that has a file open
    /// </summary>
    /// <param name="ProcessId">The process id</param>
    /// <param name="ApplicationName">The name Windows uses for the application, such as its window title or service display name. May be empty.</param>
    /// <param name="ProcessName">The name of the process executable, without extension. Null if it could not be determined.</param>
    public record LockingProcessInfo(int ProcessId, string ApplicationName, string ProcessName);

    /// <summary>
    /// Determines which processes have a file open, using the Windows Restart Manager. Windows does not report this when a file cannot be accessed.
    /// </summary>
    [Localizable(false)]
    public static class FileLockHelper
    {
        private const int ERROR_SHARING_VIOLATION = 32;
        private const int ERROR_LOCK_VIOLATION = 33;
        private const int ERROR_MORE_DATA = 234;
        private const int CCH_RM_SESSION_KEY = 32; // sizeof(GUID) * 2
        private const int CCH_RM_MAX_APP_NAME = 255;
        private const int CCH_RM_MAX_SVC_NAME = 63;

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        private enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public string strServiceShortName;

            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;

            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport(@"rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

        [DllImport(@"rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport(@"rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames, uint nApplications, RM_UNIQUE_PROCESS[] rgApplications, uint nServices, string[] rgsServiceNames);

        [DllImport(@"rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

        /// <summary>
        /// Gets the processes that currently have the specified file open. This never throws; an empty list is returned if the processes
        /// cannot be determined, such as when running under Wine.
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <returns>Processes that have the file open</returns>
        public static List<LockingProcessInfo> GetProcessesLockingFile(string path)
        {
            var result = new List<LockingProcessInfo>();
            if (string.IsNullOrWhiteSpace(path) || WineWorkarounds.WineDetected || !OperatingSystem.IsWindows())
                return result;

            uint sessionHandle = 0;
            bool sessionStarted = false;
            try
            {
                var sessionKey = new StringBuilder(CCH_RM_SESSION_KEY + 1);
                var res = RmStartSession(out sessionHandle, 0, sessionKey);
                if (res != 0)
                {
                    MLog.Warning($@"Could not start Restart Manager session to determine what is using {path}: error {res}");
                    return result;
                }

                sessionStarted = true;
                res = RmRegisterResources(sessionHandle, 1, new[] { Path.GetFullPath(path) }, 0, null, 0, null);
                if (res != 0)
                {
                    MLog.Warning($@"Could not register {path} with Restart Manager: error {res}");
                    return result;
                }

                RM_PROCESS_INFO[] processInfo = null;
                uint processInfoCount = 0;
                uint rebootReasons = 0;

                // The number of processes can change between calls, so retry a few times
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    res = RmGetList(sessionHandle, out var processInfoNeeded, ref processInfoCount, processInfo, ref rebootReasons);
                    if (res == 0)
                        break;

                    if (res != ERROR_MORE_DATA)
                    {
                        MLog.Warning($@"Could not get list of processes using {path} from Restart Manager: error {res}");
                        return result;
                    }

                    processInfo = new RM_PROCESS_INFO[processInfoNeeded];
                    processInfoCount = processInfoNeeded;
                }

                if (res != 0 || processInfo == null)
                    return result; // Nothing has the file open, or the list kept changing

                for (int i = 0; i < processInfoCount; i++)
                {
                    var pid = processInfo[i].Process.dwProcessId;
                    string processName = null;
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        processName = process.ProcessName;
                    }
                    catch
                    {
                        // Process may have exited, or we may not have access to it
                    }

                    result.Add(new LockingProcessInfo(pid, processInfo[i].strAppName, processName));
                }
            }
            catch (Exception e)
            {
                // E.g. rstrtmgr.dll not available
                MLog.Warning($@"Could not determine what is using {path}: {e.Message}");
            }
            finally
            {
                if (sessionStarted)
                {
                    RmEndSession(sessionHandle);
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a readable list of the processes that have the specified file open, such as 'Mass Effect Legendary Edition (MassEffect3.exe, PID 1234)'.
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <returns>Description of the processes, or null if none could be found</returns>
        public static string DescribeLockingProcesses(string path)
        {
            var processes = GetProcessesLockingFile(path);
            if (processes.Count == 0)
                return null;

            return string.Join(@", ", processes.Select(DescribeProcess));
        }

        private static string DescribeProcess(LockingProcessInfo info)
        {
            var exeName = info.ProcessName != null ? $@"{info.ProcessName}.exe" : null;
            if (string.IsNullOrWhiteSpace(info.ApplicationName) || string.Equals(info.ApplicationName, info.ProcessName, StringComparison.OrdinalIgnoreCase) || string.Equals(info.ApplicationName, exeName, StringComparison.OrdinalIgnoreCase))
            {
                return $@"{exeName ?? info.ApplicationName ?? @"Unknown process"} (PID {info.ProcessId})";
            }

            return exeName != null
                ? $@"{info.ApplicationName} ({exeName}, PID {info.ProcessId})"
                : $@"{info.ApplicationName} (PID {info.ProcessId})";
        }

        /// <summary>
        /// Determines if the exception is from a file being in use by another process.
        /// </summary>
        /// <param name="e">Exception to check</param>
        /// <returns>True if the file was in use</returns>
        public static bool IsFileInUseException(Exception e)
        {
            if (e is not IOException)
                return false;

            var errorCode = e.HResult & 0xFFFF;
            return errorCode is ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION;
        }
    }
}
