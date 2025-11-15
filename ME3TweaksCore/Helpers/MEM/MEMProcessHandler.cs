using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ME3TweaksCore.Diagnostics;

namespace ME3TweaksCore.Helpers.MEM
{
    /// <summary>
    /// Describes a running MassEffectModderNoGui process
    /// </summary>
    public class MEMProcess
    {
        /// <summary>
        /// The running process. It may have already exited.
        /// </summary>
        public Process RunningProcess { get; init; }

        /// <summary>
        /// If this process should not be unexpectedly terminated
        /// </summary>
        public bool ShouldWaitForExit { get; init; }
        /// <summary>
        /// Reason this process should not be terminated
        /// </summary>
        public string WaitReason { get; set; }
    }

    /// <summary>
    /// Handles running processes and allows safe exit or termination
    /// </summary>
    public static class MEMProcessHandler
    {
        private static readonly object syncObj = new();
        public static readonly List<MEMProcess> Processes = new();
        private static void ClearRunningProcesses()
        {
            foreach (var f in Processes.ToList())
            {
                if (f.RunningProcess.HasExited)
                    Processes.Remove(f);
            }
        }

        /// <summary>
        /// If there are any processes that are marked not safe to exit
        /// </summary>
        /// <returns></returns>
        public static bool CanTerminate()
        {
            lock (syncObj)
            {
                foreach (var f in Processes.ToList())
                {
                    if (!f.RunningProcess.HasExited && f.ShouldWaitForExit)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Just kill everything. Even things unsafely.
        /// </summary>
        public static void TerminateAll()
        {
            lock (syncObj)
            {
                foreach (var f in Processes.Where(x => !x.RunningProcess.HasExited).ToList())
                {
                    MLog.Information($@"Killing MassEffectModderNoGui process {f.RunningProcess.Id}");
                    try
                    {
                        f.RunningProcess.Kill();
                    }
                    catch
                    {
                        // We really don't care.
                    }
                }

                ClearRunningProcesses();
            }
        }

        /// <summary>
        /// Adds a tracked MassEffectModderNoGui process
        /// </summary>
        /// <param name="process">The MEM process</param>
        /// <param name="shouldWaitForExit">If process cannot be safely terminated (e.g. installing), set this to true</param>
        /// <param name="reasonShouldWaitForExit">Can be null if shouldWaitForExit is false</param>
        public static void AddProcess(Process process, bool shouldWaitForExit, string reasonShouldWaitForExit)
        {
            lock (syncObj)
            {
                var mp = new MEMProcess()
                {
                    RunningProcess = process,
                    ShouldWaitForExit = shouldWaitForExit,
                    WaitReason = reasonShouldWaitForExit
                };
                Processes.Add(mp);
            }
        }

        public static string GetReasonShouldNotTerminate()
        {
            lock (syncObj)
            {
                ClearRunningProcesses();
                return Processes.FirstOrDefault(x => x.ShouldWaitForExit && x.WaitReason != null)?.WaitReason;
            }
        }

        /// <summary>
        /// Returns if there are any MEM processes run from this application session
        /// </summary>
        /// <returns></returns>
        public static bool IsMEMRunning()
        {
            lock (syncObj)
            {
                return Processes.Any();
            }
        }
    }
}
