using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Localization;
using ME3TweaksCore.ME3Tweaks.M3Merge.Bio2DATable;
using ME3TweaksCore.Objects;
using ME3TweaksCore.Services;
using ME3TweaksCore.Services.Shared.BasegameFileIdentification;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Diagnostic module for collecting basegame file modification information.
    /// </summary>
    internal class DiagBasegameFiles : DiagModuleBase
    {
        internal override void RunModule(LogUploadPackage package)
        {
            var diag = package.DiagnosticWriter;

            MLog.Information(@"Collecting basegame file changes information");

            diag.AddDiagLine(@"Basegame changes", LogSeverity.DIAGSECTION);

            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingBasegameFileModifications));
            List<string> modifiedFiles = new List<string>();

            void failedCallback(string file)
            {
                modifiedFiles.Add(file);
            }

            var pi = new ProgressInfo();
            pi.OnUpdate = (progressInfo) =>
            {
                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingBasegameFileModifications) + $@" {(int)progressInfo.Value}%");
            };

            var isVanilla = VanillaDatabaseService.ValidateTargetAgainstVanilla(package.DiagnosticTarget, failedCallback, false, pi: pi);
            if (isVanilla)
            {
                diag.AddDiagLine(@"No modified basegame files were found.");
            }
            else
            {
                if (!package.DiagnosticTarget.TextureModded || package.DiagnosticTarget.Game.IsLEGame())
                {
                    var modifiedBGFiles = new List<string>();
                    bool hasAtLeastOneTextureModdedOnlyFile = false;
                    var cookedPath = package.DiagnosticTarget.GetCookedPath();
                    var markerPath = package.DiagnosticTarget.GetTextureMarkerPath();
                    foreach (var mf in modifiedFiles)
                    {
                        if (mf.StartsWith(cookedPath, StringComparison.InvariantCultureIgnoreCase))
                        {
                            var fileName = mf.Substring(cookedPath.Length + 1);
                            if (mf.Equals(markerPath, StringComparison.InvariantCultureIgnoreCase)) continue; //don't report this file
                            var info = BasegameFileIdentificationService.GetBasegameFileSource(package.DiagnosticTarget, mf);
                            var cell = $@"<td>{fileName}</td>";

                            if (info != null)
                            {
                                var strippedSource = info.GetSourceForUI(@"&nbsp;");
                                cell += $@"<td>{strippedSource.Replace("\n", "<br>")}</td>";
                            }
                            else
                            {
                                if (package.DiagnosticTarget.TextureModded)
                                {
                                    // Do not print out texture modded only files
                                    hasAtLeastOneTextureModdedOnlyFile = true;
                                    cell = null;
                                }
                                else
                                {
                                    cell += $@"<td>{fileName}</td>";
                                }
                            }

                            if (cell != null)
                            {
                                modifiedBGFiles.Add($@"<tr>{cell}</tr>");
                            }
                        }
                    }

                    if (hasAtLeastOneTextureModdedOnlyFile)
                    {
                        diag.AddDiagLine(@"Files' whose only modification was from Mass Effect Modder are not shown.");
                    }
                    if (modifiedBGFiles.Any())
                    {
                        diag.AddDiagLine(@"The following basegame files have been modified:");

                        // Make table.
                        var bgtable = $@"
                [HTML]
                <table class=""basegametable"">
                    <thead>
                        <th>Filename</th>
                        <th>Tracked modifications</th>
                    </thead>
                    <tbody>
                        {string.Join("\n", modifiedBGFiles)}
                    </tbody>
                </table>
                [/HTML]";
                        diag.AddDiagLine(string.Join("\n", bgtable.SplitLinesAll(options: StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())));
                    }
                    else
                    {
                        diag.AddDiagLine(@"No modified basegame files were found");
                    }

                }
                else
                {
                    //Check MEMI markers?
                    diag.AddDiagLine(@"Basegame changes check skipped as this installation has been texture modded");
                }
            }
        }
    }
}
