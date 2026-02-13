using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Diagnostic module for collecting the ME1 logs.
    /// </summary>
    internal class DiagME1Logs : DiagModuleBase
    {
        internal override void RunModule(LogUploadPackage package)
        {
            var diag = package.DiagnosticWriter;

            //ME1: LOGS
            if (package.DiagnosticTarget.Game == MEGame.ME1)
            {
                MLog.Information(@"Collecting ME1 crash logs");

                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingME1ApplicationLogs));

                //GET LOGS
                string logsdir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), @"BioWare\Mass Effect\Logs");
                if (Directory.Exists(logsdir))
                {
                    DirectoryInfo info = new DirectoryInfo(logsdir);
                    FileInfo[] files = info.GetFiles().Where(f => f.LastWriteTime > DateTime.Now.AddDays(-3)).OrderByDescending(p => p.LastWriteTime).ToArray();
                    DateTime threeDaysAgo = DateTime.Now.AddDays(-3);
                    foreach (FileInfo file in files)
                    {
                        var logLines = File.ReadAllLines(file.FullName);
                        int crashLineNumber = -1;
                        int currentLineNumber = -1;
                        string reason = "";
                        foreach (string line in logLines)
                        {
                            if (line.Contains(@"Critical: appError called"))
                            {
                                crashLineNumber = currentLineNumber;
                                reason = @"Log file indicates crash occurred";
                                MLog.Information(@"Found crash in ME1 log " + file.Name + @" on line " + currentLineNumber);
                                break;
                            }

                            currentLineNumber++;
                        }

                        if (crashLineNumber >= 0)
                        {
                            crashLineNumber = Math.Max(0, crashLineNumber - 10); //show last 10 lines of log leading up to the crash
                                                                                 //this log has a crash
                            diag.AddDiagLine(@"Mass Effect game log " + file.Name, LogSeverity.DIAGSECTION);
                            if (reason != "") diag.AddDiagLine(reason);
                            if (crashLineNumber > 0)
                            {
                                diag.AddDiagLine(@"[CRASHLOG]...");
                            }

                            for (int i = crashLineNumber; i < logLines.Length; i++)
                            {
                                diag.AddDiagLine(@"[CRASHLOG]" + logLines[i]);
                            }
                        }
                    }
                }
            }
        }
    }
}