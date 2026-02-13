using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Helpers;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Services;
using ME3TweaksCore.Services.ThirdPartyModIdentification;
using ME3TweaksCore.Targets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Diagnostic module for collecting installed DLC information.
    /// </summary>
    internal class DiagInstalledDLC : DiagModuleBase
    {
        internal override void RunModule(LogUploadPackage package)
        {
            var diag = package.DiagnosticWriter;

            MLog.Information(@"Collecting installed DLC");

            //Get DLCs
            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingDLCInformation));

            var installedDLCs = package.DiagnosticTarget.GetMetaMappedInstalledDLC();

            diag.AddDiagLine(@"Installed DLC", LogSeverity.DIAGSECTION);
            diag.AddDiagLine(@"The following DLC is installed:");

            var mountPriorities = new Dictionary<int, string>();

            var officialDLC = MEDirectories.OfficialDLC(package.DiagnosticTarget.Game);
            List<string> dlcRows = new List<string>();
            foreach (var dlc in installedDLCs)
            {
                var dlcStruct = new InstalledDLCStruct()
                {
                    DLCFolderName = dlc.Key
                };

                string errorLine = null;
                if (!officialDLC.Contains(dlc.Key, StringComparer.InvariantCultureIgnoreCase))
                {
                    var metaMappedDLC = dlc.Value;
                    if (metaMappedDLC != null)
                    {
                        dlcStruct.ModName = metaMappedDLC.ModName;
                        dlcStruct.InstalledBy = metaMappedDLC.InstalledBy;
                        dlcStruct.VersionInstalled = metaMappedDLC.Version;
                        dlcStruct.InstalledOptions = metaMappedDLC.OptionsSelectedAtInstallTime;
                        dlcStruct.NexusUpdateCode = metaMappedDLC.NexusUpdateCode;
                        dlcStruct.InstallTime = metaMappedDLC.InstallTime;
                    }
                    else
                    {

                    }

                    var dlcPath = Path.Combine(package.DiagnosticTarget.GetDLCPath(), dlc.Key);
                    var mount = MELoadedDLC.GetMountPriority(dlcPath, package.DiagnosticTarget.Game);
                    dlcStruct.MountPriority = mount;
                    if (mount != 0)
                    {
                        if (mountPriorities.TryGetValue(mount, out var existingDLC))
                        {
                            dlcStruct.AddError($@"This DLC has the same mount priority ({mount}) as {existingDLC}. This will cause undefined game behavior! Please contact the developers of these mods.");
                        }
                        else
                        {
                            mountPriorities[mount] = dlc.Key;
                        }
                    }
                }
                else
                {
                    dlcStruct.IsOfficialDLC = true;
                    dlcStruct.ModName = TPMIService.GetThirdPartyModInfo(dlc.Key, package.DiagnosticTarget.Game).modname;
                    var dlcPath = Path.Combine(package.DiagnosticTarget.GetDLCPath(), dlc.Key);
                    var mount = MELoadedDLC.GetMountPriority(dlcPath, package.DiagnosticTarget.Game);
                    dlcStruct.MountPriority = mount;
                }

                dlcRows.Add(dlcStruct.GetAsDiagTableRow());
            }

            // Make table.
            var table = $@"
                [HTML]
                <table class=""dlctable"">
                    <thead>
                        <th>DLC Folder Name</th>
                        <th>Mod Name</th>
                        <th>Version</th>
                        <th class=""advanced-only"">Mount</th>
                        <th>Installed By</th>
                        <th>Install Time</th>
                        <th>Options</th>
                        <th>Extra info</th>
                    </thead>
                    <tbody>
                        {string.Join("\n", dlcRows)}
                    </tbody>
                </table>
                [/HTML]";

            // Remove leading whitespace
            diag.AddDiagLine(string.Join("\n", table.SplitLinesAll(options: StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())));

            if (installedDLCs.Any())
            {
                SeeIfIncompatibleDLCIsInstalled(package.DiagnosticTarget, diag.AddDiagLine);
            }

            // 03/13/2022: Supercedance list now lists all DLC files even if they don't supercede anything.
            MLog.Information(@"Collecting supersedance list");
            var supercedanceList = M3Directories.GetFileSupercedances(package.DiagnosticTarget).ToList();
            if (supercedanceList.Any())
            {
                diag.AddDiagLine();
                diag.AddDiagLine(@"DLC mod files", LogSeverity.BOLD);
                diag.AddDiagLine(@"The following DLC mod files are installed, as well as their supercedances. This may mean the mods are incompatible, or that these files are compatibility patches. This information is for developer use only - DO NOT MODIFY YOUR GAME DIRECTORY MANUALLY.");

                bool isFirst = true;
                diag.AddDiagLine(@"Click to view list", LogSeverity.SUB);

                var supercedanceRows = new List<string>();
                foreach (var sl in supercedanceList.OrderBy(x => x.Key))
                {
                    supercedanceRows.Add($@"<tr><td>{sl.Key}</td><td>{string.Join("<br>", sl.Value)}</td></tr>");
                }

                var sltable = $@"
                [HTML]
                <table class=""supercedancetable"">
                    <thead>
                        <th>Filename</th>
                        <th>Supercedances</th>
                    </thead>
                    <tbody>
                        {string.Join("\n", supercedanceRows)}
                    </tbody>
                </table>
                [/HTML]";

                // Remove leading whitespace
                diag.AddDiagLine(string.Join("\n", sltable.SplitLinesAll(options: StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())));

                diag.AddDiagLine(LogShared.END_SUB);
            }
        }

        private static void SeeIfIncompatibleDLCIsInstalled(GameTarget target, Action<string, LogSeverity> addDiagLine)
        {
            var installedDLCMods = VanillaDatabaseService.GetInstalledDLCMods(target);
            var metaFiles = target.GetMetaMappedInstalledDLC(false);

            foreach (var v in metaFiles)
            {
                if (v.Value != null && v.Value.IncompatibleDLC.Any())
                {
                    // See if any DLC is not compatible
                    var installedIncompatDLC = installedDLCMods.Intersect(v.Value.IncompatibleDLC, StringComparer.InvariantCultureIgnoreCase).ToList();
                    foreach (var id in installedIncompatDLC)
                    {
                        var incompatName = TPMIService.GetThirdPartyModInfo(id, target.Game);
                        addDiagLine($@"{v.Value.ModName} is not compatible with {incompatName?.modname ?? id}", LogSeverity.FATAL);
                    }
                }
            }
        }
    }
}
