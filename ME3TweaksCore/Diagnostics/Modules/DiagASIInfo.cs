using Flurl.Util;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Localization;
using ME3TweaksCore.NativeMods;
using ME3TweaksCore.NativeMods.Interfaces;
using ME3TweaksCore.Targets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ME3TweaksCore.Diagnostics.Modules
{
    internal class DiagASIInfo : DiagModuleBase
    {
        // ASIs began changing over to .log 03/17/2022
        // KismetLogger uses .txt still (we don't care)
        // 01/30/2026 - Kismet logger on new SDK now uses .log extension
        // We will manually filter it out.
        private static readonly string[] asilogExtensions = [@".log"];

#if FALSE
        private void WriteASIInfoOld(LogUploadPackage package)
        {
            var diag = package.DiagnosticWriter;

            #region ASI File Information
            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingASIFileInformation));

            string asidir = M3Directories.GetASIPath(package.DiagnosticTarget);
            diag.AddDiagLine(@"Installed ASI mods", LogSeverity.DIAGSECTION);
            if (Directory.Exists(asidir))
            {
                diag.AddDiagLine(@"The following ASI files are located in the ASI directory:");
                string[] files = Directory.GetFiles(asidir, @"*.asi");
                if (!files.Any())
                {
                    diag.AddDiagLine(@"ASI directory is empty. No ASI mods are installed.");
                }
                else
                {
                    var installedASIs = package.DiagnosticTarget.GetInstalledASIs();
                    var nonUniqueItems = installedASIs.OfType<KnownInstalledASIMod>().SelectMany(
                        x => installedASIs.OfType<IKnownInstalledASIMod>().Where(
                            y => x != y
                                 && x.AssociatedManifestItem.OwningMod ==
                                 y.AssociatedManifestItem.OwningMod)
                        ).Distinct().ToList();

                    foreach (var knownAsiMod in installedASIs.OfType<IKnownInstalledASIMod>().Except(nonUniqueItems))
                    {
                        var str = $@" - {knownAsiMod.AssociatedManifestItem.Name} v{knownAsiMod.AssociatedManifestItem.Version} ({Path.GetFileName(knownAsiMod.InstalledPath)})";
                        if (knownAsiMod.Outdated)
                        {
                            str += @" - Outdated";
                        }
                        diag.AddDiagLine(str, knownAsiMod.Outdated ? LogSeverity.WARN : LogSeverity.GOOD);
                    }

                    foreach (var unknownAsiMod in installedASIs.OfType<IUnknownInstalledASIMod>())
                    {
                        diag.AddDiagLine($@" - {Path.GetFileName(unknownAsiMod.InstalledPath)} - Unknown ASI mod", LogSeverity.WARN);
                    }

                    foreach (var duplicateItem in nonUniqueItems)
                    {
                        var str = $@" - {duplicateItem.AssociatedManifestItem.Name} v{duplicateItem.AssociatedManifestItem.Version} ({Path.GetFileName(duplicateItem.InstalledPath)})";
                        if (duplicateItem.Outdated)
                        {
                            str += @" - Outdated";
                        }

                        str += @" - DUPLICATE ASI";
                        diag.AddDiagLine(str, LogSeverity.FATAL);
                    }

                    diag.AddDiagLine();
                    diag.AddDiagLine(@"Ensure that only one version of an ASI is installed. If multiple copies of the same one are installed, the game may crash on startup.");
                }
            }
            else
            {
                diag.AddDiagLine(@"ASI directory does not exist. No ASI mods are installed.");
            }

            #endregion
        }
#endif

        internal override void RunModule(LogUploadPackage package)
        {
            var diag = package.DiagnosticWriter;

            MLog.Information(@"Collecting ASI mod information");

            #region ASI Mods Table
            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingASIFileInformation));

            string asidir = M3Directories.GetASIPath(package.DiagnosticTarget);
            diag.AddDiagLine(@"Installed ASI mods", LogSeverity.DIAGSECTION);
            if (Directory.Exists(asidir))
            {
                diag.AddDiagLine(@"The following ASI files are located in the ASI directory:");
                string[] files = Directory.GetFiles(asidir, @"*.asi");
                if (!files.Any())
                {
                    diag.AddDiagLine(@"ASI directory is empty. No ASI mods are installed.");
                }
                else
                {

                    var asiRows = new List<string>();

                    // Get all instaleld ASIs.
                    var installedASIs = package.DiagnosticTarget.GetInstalledASIs();

                    // Find list of non-unique ASIs, these will cause big problems
                    var nonUniqueItems = installedASIs.OfType<KnownInstalledASIMod>().SelectMany(
                        x => installedASIs.OfType<IKnownInstalledASIMod>().Where(
                            y => x != y
                                 && x.AssociatedManifestItem.OwningMod ==
                                 y.AssociatedManifestItem.OwningMod)
                        ).Distinct().ToList();

                    // Enumerate list of known ASI mods EXCLUDING duplicates
                    foreach (var knownAsiMod in installedASIs.OfType<IKnownInstalledASIMod>().Except(nonUniqueItems))
                    {
                        var cell = makeKnownAsiModRow(knownAsiMod);
                        // Add the row
                        asiRows.Add($@"<tr>{cell}</tr>");
                    }

                    // Enumerate list of unidentified ASI mods.
                    foreach (var unknownAsiMod in installedASIs.OfType<IUnknownInstalledASIMod>())
                    {
                        // Filename
                        var cell = $@"<td>{Path.GetFileName(unknownAsiMod.InstalledPath)}</td>";

                        // Manifest Name
                        cell += $@"<td>Unknown</td>";

                        // Version
                        var version = @"Unknown";
                        if (unknownAsiMod.DllVersionInfo != null)
                        {
                            version = unknownAsiMod.DllVersionInfo.FileVersion;
                        }
                        cell += $@"<td>{version}</td> ";

                        // The description of the ASI, if available
                        var desc = @"Unknown ASI - use with caution";
                        if (unknownAsiMod.DllVersionInfo != null && !string.IsNullOrWhiteSpace(unknownAsiMod.DllVersionInfo.FileDescription))
                        {
                            desc += $@"<br/>{unknownAsiMod.DllVersionInfo.FileDescription}";
                        }
                        cell += $@"<td>{desc}</td>";

                        // Add the row
                        asiRows.Add($@"<tr class=""unknown-asi"">{cell}</tr>");
                    }

                    // Enumerate list of duplicate ASIs
                    foreach (var duplicateItem in nonUniqueItems)
                    {
                        var cell = makeKnownAsiModRow(duplicateItem, @"DUPLICATE ASI - This is likely to crash the game");
                        // Add the row
                        asiRows.Add($@"<tr class=""duplicate-asi"">{cell}</tr>");
                    }

                    // Make table.
                    var asiTable = $@"
                    [HTML]
                    <table class=""asitable"">
                        <thead>
                            <th>ASI Filename</th>
                            <th>Known name</th>
                            <th>Version</th>
                            <th>Description</th>
                        </thead>
                        <tbody>
                            {string.Join("\n", asiRows)}
                        </tbody>
                    </table>
                    [/HTML]";

                    diag.AddDiagLine(string.Join("\n", asiTable.SplitLinesAll(options: StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())));
                    diag.AddDiagLine(@"Ensure that only one version of an ASI is installed. If multiple copies of the same one are installed, the game may crash on startup.");
                }
            }
            else
            {
                diag.AddDiagLine(@"ASI directory does not exist. No ASI mods are installed.");
            }
            #endregion

            #region ASI Logs
            if (package.DiagnosticTarget.Game.IsLEGame())
            {
                MLog.Information(@"Collecting ASI log files");
                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingASILogFiles));

                var logFiles = GetASILogs(package.DiagnosticTarget);
                diag.AddDiagLine(@"ASI log files", LogSeverity.DIAGSECTION);
                diag.AddDiagLine(@"These are log files from installed ASI mods (within the past day). These are >>highly<< technical; only advanced developers should attempt to interpret these logs.");
                if (logFiles.Any())
                {
                    foreach (var logF in logFiles)
                    {
                        diag.AddDiagLine(logF.Key, LogSeverity.BOLD);
                        diag.AddDiagLine(@"Click to view log", LogSeverity.SUB);
                        diag.AddDiagLine(logF.Value);
                        diag.AddDiagLine(LogShared.END_SUB);
                    }
                }
                else
                {
                    diag.AddDiagLine(@"No recent ASI logs were found.");
                }
            }
            #endregion
        }

        /// <summary>
        /// Shared code for making the row of a known ASI mod
        /// </summary>
        /// <param name="knownAsiMod"></param>
        /// <returns></returns>
        private string makeKnownAsiModRow(IKnownInstalledASIMod knownAsiMod, string descriptionOverride = null)
        {
            // Filename
            var cell = $@"<td>{Path.GetFileName(knownAsiMod.InstalledPath)}</td>";

            // Manifest Name
            cell += $@"<td>{knownAsiMod.AssociatedManifestItem.Name}</td>";

            // Manifest Version and outdated
            var classModifier = @"";
            if (knownAsiMod.Outdated)
            {
                classModifier = @" class=""outdated-asi""";
            }
            cell += $@"<td{classModifier}>v{knownAsiMod.AssociatedManifestItem.Version}</td>";

            // The description of the ASI... not sure how useful this is
            cell += $@"<td>{descriptionOverride ?? knownAsiMod.AssociatedManifestItem.Description}</td>";

            return cell;
        }

        /// <summary>
        /// Gets the contents of log files in the same directory as the game executable. This only returns logs for LE, OT doesn't really have any debug loggers beyond one.
        /// </summary>
        /// <returns>Dictionary of logs, mapped filename to contents. Will return null if not an LE game</returns>
        private static Dictionary<string, string> GetASILogs(GameTarget target)
        {
            if (!target.Game.IsLEGame()) return null;
            var logs = new Dictionary<string, string>();
            // 01/30/2026 - Read logs from Logs directory instead
            var directory = Path.Combine(target.GetExecutableDirectory(), @"Logs");
            if (Directory.Exists(directory))
            {
                foreach (var f in Directory.GetFiles(directory, "*"))
                {
                    try
                    {
                        if (!asilogExtensions.Contains(Path.GetExtension(f)))
                            continue; // Not parsable

                        if (Path.GetFileName(f).Equals(@"KismetLogger.log", StringComparison.OrdinalIgnoreCase))
                            continue; // We don't care about this one.

                        var fi = new FileInfo(f);
                        var timeDelta = DateTime.Now - fi.LastWriteTime;
                        if (timeDelta < TimeSpan.FromDays(1))
                        {
                            // If the log was written within the last day.
                            StringBuilder sb = new StringBuilder();
                            var fileContentsLines = File.ReadAllLines(f);

                            int lastIndexRead = 0;
                            // Read first 30 lines.
                            for (int i = 0; i < 30 && i < fileContentsLines.Length - 1; i++)
                            {
                                sb.AppendLine(fileContentsLines[i]);
                                lastIndexRead = i;
                            }

                            // 02/01/2026
                            // If it's 30 lines we don't insert a ... cause there's nothing else to add to the list.
                            // If exactly 31 lines we don't insert a ... cause its not useful to add a ... that just lets it show
                            // the next line as it would.
                            if (fileContentsLines.Length > 30)
                            {
                                // Read last 30 lines.
                                if (lastIndexRead < fileContentsLines.Length - 1)
                                {
                                    if (fileContentsLines.Length > 31)
                                    {
                                        sb.AppendLine(@"...");
                                    }
                                    var startIndex = Math.Max(lastIndexRead, fileContentsLines.Length - 30);
                                    for (int i = startIndex; i < fileContentsLines.Length - 1; i++)
                                    {
                                        sb.AppendLine(fileContentsLines[i]);
                                    }
                                }
                            }

                            logs[Path.GetFileName(f)] = sb.ToString();
                        }
                        else
                        {
                            MLog.Information($@"Skipping log: {Path.GetFileName(f)}. Last write time was {fi.LastWriteTime}. Only files written within last day are included");
                        }
                    }
                    catch (Exception e)
                    {
                        logs[Path.GetFileName(f)] = e.Message;
                    }
                }
            }

            return logs;
        }

    }
}
