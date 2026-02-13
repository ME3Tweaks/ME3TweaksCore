using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Timers;
using ME3TweaksCore.Diagnostics;
using Timer = System.Timers.Timer;

// From ALOTInstallerCore
namespace ME3TweaksCore.Helpers
{
    /// <summary>
    /// Manages the system being allowed to go to sleep or not.
    /// </summary>
    public static class SystemSleepManager
    {
        private static object syncObj = new object();

        private static Timer _wakeTimer;

        // If we have not received a new call to PreventSleep() in 4 minutes we will stop keeping the system awake.
        // This is a failsafe.
        private static readonly int MinutesPerWake = 4;

        private static int RemainingMinutesAwake = MinutesPerWake;

        /// <summary>
        /// The last call to PreventSleep() will set this.
        /// </summary>
        private static string AwakeReason;

        /// <summary>
        /// Prevents the system from entering sleep mode. Calls to this will extend sleep prevention by a defined amount of time, see <see cref="MinutesPerWake"/>. Use only on long running tasks and be sure to call <see cref="AllowSleep"/> when done.
        /// </summary>
        public static void PreventSleep(string reason)
        {
            // We do not log this call as it might happen a lot.

            // Not sure if we should cache this somewhere for use as this probably won't work on SteamDeck
            // if (OperatingSystemSupported) {
            SetThreadExecutionState(ExecutionState.EsContinuous | ExecutionState.EsSystemRequired);
            lock (syncObj)
            {
                AwakeReason = reason;
                RemainingMinutesAwake = MinutesPerWake; // Reset the counter
                if (_wakeTimer == null)
                {
                    MLog.Information($@"The system is being prevented from going to sleep per application request: {reason}");
                    _wakeTimer = new Timer(60 * 1000); //1 minute
                    _wakeTimer.Elapsed += keepAwakeTimerTick;
                    _wakeTimer.Start();
                }
            }

            // }
        }

        private static void keepAwakeTimerTick(object sender, ElapsedEventArgs e)
        {
            RemainingMinutesAwake--;
            if (RemainingMinutesAwake < 0)
            {
                // Whoever is keeping us awake might have forgotten
                MLog.Error($@"FAILSAFE: The application has not requested sleep prevention for {MinutesPerWake} minutes. This may be a bug; long running tasks should periodically issue PreventSleep() and always call AllowSleep() when done. We are releasing the wakelock. The last specified reason was {AwakeReason}");
                AllowSleep();
            }
            else
            {
                // Keep it awake, do not renew though.
                MLog.Information($@"The system is not allowed to go to sleep for another {RemainingMinutesAwake + 1} minutes due to previous request by {AwakeReason}");
                SetThreadExecutionState(ExecutionState.EsContinuous | ExecutionState.EsSystemRequired);
            }
        }

        /// <summary>
        /// Allows the system to go to sleep again, canceling the failsafe timer as well.
        /// </summary>
        public static void AllowSleep()
        {
            lock (syncObj)
            {
                if (_wakeTimer == null)
                    return; // The system was already allowed to go to sleep.

                MLog.Information(@"The system is being allowed to go to sleep again");
                // Not sure if we should cache this somewhere for use as this probably won't work on SteamDeck
                // if (OperatingSystemSupported) {
                SetThreadExecutionState(ExecutionState.EsContinuous);
                // }

                if (_wakeTimer != null)
                {
                    _wakeTimer.Stop();
                    _wakeTimer.Elapsed -= keepAwakeTimerTick;
                    _wakeTimer.Dispose(); // 08/19/2025 - dispose of timer
                    _wakeTimer = null;
                }
            }
        }

        [DllImport(@"kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

        [Flags]
        private enum ExecutionState : uint
        {
            EsAwaymodeRequired = 0x00000040,
            EsContinuous = 0x80000000,
            EsDisplayRequired = 0x00000002,
            EsSystemRequired = 0x00000001
        }
    }
}
