using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Packages;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ThreadState = System.Diagnostics.ThreadState;

namespace ME3TweaksCore.Helpers
{
    /// <summary>
    /// Utility class for detecting if Mass Effect games are running
    /// </summary>
    public static class MRunningGameInfo
    {
        private static (bool isRunning, DateTime lastChecked) le1RunningInfo = (false, DateTime.MinValue.AddSeconds(5));
        private static (bool isRunning, DateTime lastChecked) le2RunningInfo = (false, DateTime.MinValue.AddSeconds(5));
        private static (bool isRunning, DateTime lastChecked) le3RunningInfo = (false, DateTime.MinValue.AddSeconds(5));
        private static (bool isRunning, DateTime lastChecked) me1RunningInfo = (false, DateTime.MinValue.AddSeconds(5));
        private static (bool isRunning, DateTime lastChecked) me2RunningInfo = (false, DateTime.MinValue.AddSeconds(5));
        private static (bool isRunning, DateTime lastChecked) me3RunningInfo = (false, DateTime.MinValue.AddSeconds(5));
        private static (bool isRunning, DateTime lastChecked) leLauncherRunningInfo = (false, DateTime.MinValue.AddSeconds(5));


        private static int TIME_BETWEEN_PROCESS_CHECKS = 5;

        private static object gameRunningSyncObjME1 = new object();
        private static object gameRunningSyncObjME2 = new object();
        private static object gameRunningSyncObjME3 = new object();
        private static object gameRunningSyncObjLE1 = new object();
        private static object gameRunningSyncObjLE2 = new object();
        private static object gameRunningSyncObjLE3 = new object();
        private static object gameRunningSyncObjLEL = new object();

        /// <summary>
        /// Returns the object used to enforce concurrency when checking if a game is running
        /// </summary>
        /// <param name="game"></param>
        /// <returns></returns>
        private static object GetSyncObjForGameRunning(MEGame game) => game switch
        {
            MEGame.ME1 => gameRunningSyncObjME1,
            MEGame.ME2 => gameRunningSyncObjME2,
            MEGame.ME3 => gameRunningSyncObjME3,
            MEGame.LE1 => gameRunningSyncObjLE1,
            MEGame.LE2 => gameRunningSyncObjLE2,
            MEGame.LE3 => gameRunningSyncObjLE3,
            MEGame.LELauncher => gameRunningSyncObjLEL,
        };

        /// <summary>
        /// Determines if a specific game is running. This method only updates every 3 seconds due to the huge overhead it has
        /// </summary>
        /// <returns>True if running, false otherwise</returns>
        public static bool IsGameRunning(MEGame gameID, bool forceCheckNow = false)
        {
            (bool isRunning, DateTime lastChecked) runningInfo = (false, DateTime.MinValue);
            lock (GetSyncObjForGameRunning(gameID))
            {
                switch (gameID)
                {
                    case MEGame.ME1:
                        runningInfo = me1RunningInfo;
                        break;
                    case MEGame.LE1:
                        runningInfo = le1RunningInfo;
                        break;
                    case MEGame.LE2:
                        runningInfo = le2RunningInfo;
                        break;
                    case MEGame.ME2:
                        runningInfo = me2RunningInfo;
                        break;
                    case MEGame.LE3:
                        runningInfo = le3RunningInfo;
                        break;
                    case MEGame.ME3:
                        runningInfo = me3RunningInfo;
                        break;
                    case MEGame.LELauncher:
                        runningInfo = leLauncherRunningInfo;
                        break;
                }

                var time = runningInfo.lastChecked.AddSeconds(TIME_BETWEEN_PROCESS_CHECKS);
                //Debug.WriteLine(time + " vs " + DateTime.Now);
                if (!forceCheckNow && time > DateTime.Now)
                {
                    //Debug.WriteLine("CACHED");
                    return runningInfo.isRunning; //cached
                }
            }

            Debug.WriteLine($@"{DateTime.Now} IsGameRunning({gameID}) - stale info, refreshing");

            //Debug.WriteLine("IsRunning: " + gameID);

            var processNames = MEDirectories.ExecutableNames(gameID).Select(Path.GetFileNameWithoutExtension);
            try
            {
                // This is in a try catch, as things in MainModule will throw an error if accessed
                // after the process ends, which it might during the periodic updates of this
                runningInfo.isRunning = Process.GetProcesses().Any(x => processNames.Contains(x.ProcessName) && !IsProcessSuspended(x) &&
                                                                        x.MainModule?.FileVersionInfo.FileMajorPart == (gameID.IsOTGame() ? 1 : 2));
            }
            catch
            {
                // don't really care
            }

            runningInfo.lastChecked = DateTime.Now;
            switch (gameID)
            {
                case MEGame.ME1:
                    me1RunningInfo = runningInfo;
                    break;
                case MEGame.LE1:
                    le1RunningInfo = runningInfo;
                    break;
                case MEGame.ME2:
                    me2RunningInfo = runningInfo;
                    break;
                case MEGame.LE2:
                    le2RunningInfo = runningInfo;
                    break;
                case MEGame.ME3:
                    me3RunningInfo = runningInfo;
                    break;
                case MEGame.LE3:
                    le3RunningInfo = runningInfo;
                    break;
                case MEGame.LELauncher:
                    leLauncherRunningInfo = runningInfo;
                    break;
            }

            return runningInfo.isRunning;
        }

        private static bool IsProcessSuspended(Process proc)
        {
#if DEBUG
            if (proc.Threads.Count == 0) return true; // App is in some weird broken state. I see this in dev machine all the time, requires a restart to fix.
            var isWaiting = proc.Threads[0].ThreadState == ThreadState.Wait;
            if (isWaiting)
            {
                return proc.Threads[0].WaitReason == ThreadWaitReason.Suspended;
            }

            return isWaiting;
#endif
            return proc.Threads[0].ThreadState == ThreadState.Wait;
        }

        /// <summary>
        /// Checks if a process is running. This should not be used for game detection, as it also uses version info.
        /// </summary>
        /// <param name="processName"></param>
        /// <returns></returns>
        public static bool IsProcessRunning(string processName)
        {
            return Process.GetProcesses().Any(x => x.ProcessName.Equals(processName, StringComparison.InvariantCultureIgnoreCase));
        }
    }
}
