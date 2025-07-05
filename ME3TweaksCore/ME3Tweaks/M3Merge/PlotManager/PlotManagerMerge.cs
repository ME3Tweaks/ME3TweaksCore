using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Services.Shared.BasegameFileIdentification;
using ME3TweaksCore.Services.ThirdPartyModIdentification;
using ME3TweaksCore.Targets;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System;
using System.Linq;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Packages.CloningImportingAndRelinking;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using LegendaryExplorerCore.UnrealScript;
using LegendaryExplorerCore.UnrealScript.Compiling.Errors;
using LegendaryExplorerCore.Helpers;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Localization;

namespace ME3TweaksCore.ME3Tweaks.M3Merge.PlotManager
{
    public static class PlotManagerMerge
    {
        /// <summary>
        /// Extension used for Plot Manager Sync.
        /// </summary>
        public const string PLOT_MANAGER_UPDATE_EXTENSION = @".pmu";

        /// <summary>
        /// The fixed filename that your mod must use for PMU files.
        /// </summary>
        public const string PLOT_MANAGER_UPDATE_FILENAME = @"PlotManagerUpdate.pmu";

        /// <summary>
        /// Returns if the specified target has any email merge files.
        /// </summary>
        /// <param name="target"></param>
        public static bool NeedsMerged(GameTarget target)
        {
            if (!target.Game.IsGame1() && !target.Game.IsGame2()) return false;
            try
            {
                var emailSupercedances = target.GetFileSupercedances(new[] { PLOT_MANAGER_UPDATE_EXTENSION });
                return emailSupercedances.TryGetValue(PLOT_MANAGER_UPDATE_FILENAME, out var infoList) &&
                       infoList.Count > 0;
            }
            catch (Exception e)
            {
                MLog.Exception(e, @"Error getting file supercedences:");
            }

            return false;
        }

        public static bool RunPlotManagerMerge(GameTarget target, bool verboseLogging = false)
        {
            MLog.Information($@"Updating PlotManager for game: {target.TargetPath}");
            var allSupercedances = M3Directories.GetFileSupercedances(target, new[] { PLOT_MANAGER_UPDATE_EXTENSION });
            Dictionary<string, string> funcMap = new();
            List<string> combinedNames = new List<string>();

            // Todo: Change this to allow multiple PMU files.
            if (allSupercedances.TryGetValue(@"PlotManagerUpdate.pmu", out var pmuSupercedances))
            {
                pmuSupercedances.Reverse(); // list goes from highest to lowest. We want to build in lowest to highest
                StringBuilder sb = null;
                string currentFuncNum = null;
                var metaMaps = target.GetMetaMappedInstalledDLC(false);
                foreach (var pmuDLCName in pmuSupercedances)
                {
                    var uiName = metaMaps[pmuDLCName]?.ModName ??
                                 TPMIService.GetThirdPartyModInfo(pmuDLCName, target.Game)?.modname ?? pmuDLCName;
                    combinedNames.Add(uiName);
                    var text = File.ReadAllLines(Path.Combine(M3Directories.GetDLCPath(target), pmuDLCName,
                        target.Game.CookedDirName(), PLOT_MANAGER_UPDATE_FILENAME));
                    foreach (var line in text)
                    {
                        if (line.StartsWith(@"public function bool F"))
                        {
                            if (sb != null)
                            {
                                funcMap[currentFuncNum] = sb.ToString();
                                MLog.Information($@"PlotSync: Adding function {currentFuncNum} from {pmuDLCName}");
                                currentFuncNum = null;
                            }

                            sb = new StringBuilder();
                            sb.AppendLine(line);

                            // Method name
                            currentFuncNum = line.Substring(22);
                            currentFuncNum = currentFuncNum.Substring(0, currentFuncNum.IndexOf('('));
                            if (int.TryParse(currentFuncNum, out var num))
                            {
                                if (num <= 0)
                                {
                                    MLog.Error(
                                        $@"Skipping plot manager update: Conditional {num} is not a valid number for use. Values must be greater than 0 and less than 2 billion.");
                                    TelemetryInterposer.TrackEvent(@"Bad plot manager function",
                                        new Dictionary<string, string>()
                                        {
                                            { @"FunctionName", $@"F{currentFuncNum}" },
                                            { @"DLCName", pmuDLCName }
                                        });
                                    sb = null;
                                    return false;
                                }

                                if (num.ToString().Length != currentFuncNum.Length)
                                {
                                    MLog.Error(
                                        $@"Skipping plot manager update: Conditional {currentFuncNum} is not a valid number for use. Values must not contain leading zeros");
                                    TelemetryInterposer.TrackEvent(@"Bad plot manager function",
                                        new Dictionary<string, string>()
                                        {
                                            { @"FunctionName", $@"F{currentFuncNum}" },
                                            { @"DLCName", pmuDLCName }
                                        });
                                    sb = null;
                                    return false;
                                }
                            }
                            else
                            {
                                MLog.Error(
                                    $@"Skipping plot manager update: Conditional {currentFuncNum} is not a valid number for use. Values must be greater than 0 and less than 2 billion.");
                                TelemetryInterposer.TrackEvent(@"Bad plot manager function",
                                    new Dictionary<string, string>()
                                    {
                                        { @"FunctionName", $@"F{currentFuncNum}" },
                                        { @"DLCName", pmuDLCName }
                                    });
                                sb = null;
                                return false;
                            }
                        }
                        else
                        {
                            sb?.AppendLine(line);
                        }
                    }

                    // Add final, if any was found
                    if (sb != null)
                    {
                        funcMap[currentFuncNum] = sb.ToString();
                        MLog.Information($@"PlotSync: Adding function {currentFuncNum} from {pmuDLCName}");
                    }
                }
            }

            var extension = target.Game == MEGame.ME1 ? @"u" : @"pcc";
            var pmPath = Path.Combine(target.GetCookedPath(), $@"PlotManager.{extension}");
            var vpm = MUtilities.ExtractInternalFileToStream(
                $@"ME3TweaksCore.ME3Tweaks.M3Merge.PlotManager.{target.Game}.PlotManager.{extension}"); // do not localize
            if (funcMap.Any())
            {
                var plotManager = MEPackageHandler.OpenMEPackageFromStream(vpm,
                    $@"PlotManager.{(target.Game == MEGame.ME1 ? @"u" : @"pcc")}"); // do not localize
                var clonableFunction = plotManager.Exports.FirstOrDefault(x => x.ClassName == @"Function");

                // STEP 1: ADD ALL NEW FUNCTIONS BEFORE WE INITIALIZE THE FILELIB.
                foreach (var v in funcMap)
                {
                    var pmKey = $@"BioAutoConditionals.F{v.Key}";
                    var exp = plotManager.FindExport(pmKey);
                    if (exp == null)
                    {
                        // Adding a new conditional
                        exp = EntryCloner.CloneEntry(clonableFunction);
                        exp.ObjectName = new NameReference($@"F{v.Key}", 0);
                        exp.FileRef.InvalidateLookupTable(); // We changed the name.

                        // Reduces trash
                        UFunction uf = ObjectBinary.From<UFunction>(exp);
                        uf.Children = 0;
                        uf.ScriptBytes = Array.Empty<byte>(); // No script data
                        exp.WriteBinary(uf);
                        MLog.Information(
                            $@"Generated new blank conditional function export: {exp.UIndex} {exp.InstancedFullPath}",
                            verboseLogging);
                    }
                }

                // Relink child chain
                UClass uc = ObjectBinary.From<UClass>(plotManager.FindExport(@"BioAutoConditionals"));
                uc.UpdateChildrenChain();
                uc.UpdateLocalFunctions();
                uc.Export.WriteBinary(uc);


                // STEP 2: UPDATE FUNCTIONS
                Stopwatch sw = Stopwatch.StartNew();
                MLog.Information($@"Initializing plot manager path with relative package cache path {M3Directories.GetBioGamePath(target)}");
                var fl = new FileLib(plotManager);
                bool initialized =
                    fl.Initialize(new UnrealScriptOptionsPackage()
                    {
                        GamePathOverride = target.TargetPath,
                        Cache = new TargetPackageCache() { RootPath = M3Directories.GetBioGamePath(target) }
                    }
                    , canUseBinaryCache: false);
                if (!initialized)
                {
                    MLog.Error(@"Error initializing FileLib for plot manager sync:");
                    foreach (var v in fl.InitializationLog.AllErrors) MLog.Error(v.Message);
                    throw new Exception(LC.GetString(LC.string_interp_fileLibInitFailedPlotManager,
                        string.Join(Environment.NewLine,
                            fl.InitializationLog.AllErrors.Select(x => x.Message)))); //force localize
                }

                sw.Stop();
                Debug.WriteLine($@"Took {sw.ElapsedMilliseconds}ms to load filelib");

                bool relinkChain = false;
                foreach (var v in funcMap)
                {
                    var pmKey = $@"BioAutoConditionals.F{v.Key}";
                    MLog.Information($@"Updating conditional entry: {pmKey}", verboseLogging);
                    var exp = plotManager.FindExport(pmKey);

                    (_, MessageLog log) = UnrealScriptCompiler.CompileFunction(exp, v.Value, fl, new UnrealScriptOptionsPackage());
                    if (log.AllErrors.Any())
                    {
                        MLog.Error($@"Error compiling function {exp.InstancedFullPath}:");
                        foreach (var l in log.AllErrors)
                        {
                            MLog.Error(l.Message);
                        }

                        throw new Exception(LC.GetString(LC.string_interp_errorCompilingFunctionReason, exp,
                            string.Join('\n', log.AllErrors.Select(x => x.Message))));
                        return false;
                    }
                }

                if (plotManager.IsModified)
                {
                    plotManager.Save(pmPath, true);
                    // Update local file DB
                    var bgfe = new BasegameFileRecord(pmPath.Substring(target.TargetPath.Length + 1),
                        (int)new FileInfo(pmPath).Length, target.Game,
                        LC.GetString(LC.string_interp_plotManagerSyncForX, string.Join(@", ", combinedNames)),
                        MUtilities.CalculateHash(pmPath));
                    BasegameFileIdentificationService.AddLocalBasegameIdentificationEntries(
                        new List<BasegameFileRecord>(new[] { bgfe }));
                }
            }
            else
            {
                // Just write out vanilla.
                vpm.WriteToFile(pmPath);
            }

            return true;
        }
    }
}
