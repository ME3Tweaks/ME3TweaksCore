using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Diagnostic module for reading the ME3Logger file and parsing out useful messages we could find.
    /// </summary>
    internal class DiagME3Logger : DiagModuleBase
    {
        internal override void RunModule(LogUploadPackage package)
        {
            var diag = package.DiagnosticWriter;

            if (package.DiagnosticTarget.Game == MEGame.ME3)
            {
                MLog.Information(@"Collecting ME3Logger session log");
                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingME3SessionLog));
                string me3logfilepath = Path.Combine(Directory.GetParent(M3Directories.GetExecutablePath(package.DiagnosticTarget)).FullName, @"me3log.txt");
                if (File.Exists(me3logfilepath))
                {
                    FileInfo fi = new FileInfo(me3logfilepath);
                    diag.AddDiagLine(@"Mass Effect 3 last session log", LogSeverity.DIAGSECTION);
                    diag.AddDiagLine(@"Last session log has modification date of " + fi.LastWriteTimeUtc.ToShortDateString());
                    diag.AddDiagLine(@"Note that messages from this log can be highly misleading as they are context dependent!");
                    diag.AddDiagLine();
                    var log = MUtilities.WriteSafeReadAllLines(me3logfilepath); //try catch needed?
                    int lineNum = 0;
                    foreach (string line in log)
                    {
                        diag.AddDiagLine(line, line.Contains(@"I/O failure", StringComparison.InvariantCultureIgnoreCase) ? LogSeverity.FATAL : LogSeverity.INFO);
                        lineNum++;
                        if (lineNum > 100)
                        {
                            break;
                        }
                    }

                    if (lineNum > 200)
                    {
                        diag.AddDiagLine(@"... log truncated ...");
                    }
                }
            }
        }
    }
}
