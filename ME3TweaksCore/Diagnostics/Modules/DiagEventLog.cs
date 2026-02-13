using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Gammtek.Extensions;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Misc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Diagnostic module for collecting event log crash information.
    /// </summary>
    internal class DiagEventLog : DiagModuleBase
    {
        internal override void RunModule(LogUploadPackage package)
        {
            if (WineWorkarounds.WineDetected)
            {
                return; // Do nothing on Linux for this
            }

            var diag = package.DiagnosticWriter;

            //EVENT LOGS
            MLog.Information(@"Collecting event logs");
            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingEventLogs));
            StringBuilder crashLogs = new StringBuilder();
            var sevenDaysAgo = DateTime.Now.AddDays(-3);

            //Get event logs
            EventLog ev = new EventLog(@"Application");
            List<EventLogEntry> entries = ev.Entries
                .Cast<EventLogEntry>()
                .Where(z => z.InstanceId == 1001 && z.TimeGenerated > sevenDaysAgo && (GenerateEventLogString(z).ContainsAny(MEDirectories.ExecutableNames(package.DiagnosticTarget.Game), StringComparison.InvariantCultureIgnoreCase)))
                .ToList();

            diag.AddDiagLine($@"{package.DiagnosticTarget.Game.ToGameName()} crash logs found in Event Viewer", LogSeverity.DIAGSECTION);
            if (entries.Any())
            {
                diag.AddDiagLine($@"Crash event logs are often not useful except for determining if the executable or a library crashed the game");
                diag.AddDiagLine(@"Click to view events", LogSeverity.SUB);
                foreach (var entry in entries)
                {
                    string str = string.Join("\n", GenerateEventLogString(entry).Split('\n').ToList().Take(17).ToList()); //do not localize
                    diag.AddDiagLine($"{package.DiagnosticTarget.Game.ToGameName()} Event {entry.TimeGenerated}\n{str}"); //do not localize
                }
                diag.AddDiagLine(LogShared.END_SUB);
            }
            else
            {
                diag.AddDiagLine(@"No crash events found in Event Viewer");
            }
        }

        /// <summary>
        /// Formats an event log string
        /// </summary>
        /// <param name="entry"></param>
        /// <returns></returns>
        private static string GenerateEventLogString(EventLogEntry entry) =>
            $"Event type: {entry.EntryType}\nEvent Message: {entry.Message + entry}\nEvent Time: {entry.TimeGenerated.ToShortTimeString()}\nEvent {entry.UserName}\n"; //do not localize
    }
}
