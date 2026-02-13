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
using ME3TweaksCore.ME3Tweaks.ModManager;

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
        /// Returns if the specified target has any plot manager merge files.
        /// </summary>
        /// <param name="target"></param>
        public static bool NeedsMerged(GameTarget target)
        {
            if (!target.Game.IsGame1() && !target.Game.IsGame2()) return false;
            try
            {
                var pmuSupercedances = target.GetFileSupercedances(new[] { PLOT_MANAGER_UPDATE_EXTENSION });
                // Check V1
                var needsMerged = pmuSupercedances.TryGetValue(PLOT_MANAGER_UPDATE_FILENAME, out var infoList) &&
                       infoList.Count > 0;
                if (!needsMerged)
                {
                    // Check V2
                    needsMerged = pmuSupercedances.Any(x => Path.GetExtension(x.Key) == PLOT_MANAGER_UPDATE_EXTENSION);
                }

                return needsMerged;
            }
            catch (Exception e)
            {
                MLog.Exception(e, @"Error getting file supercedences:");
            }

            return false;
        }

        /// <summary>
        /// Runs plot manager merge. Throws exceptions on errors!
        /// </summary>
        /// <param name="target"></param>
        /// <param name="verboseLogging"></param>
        /// <returns>True on success. Does not return false.</returns>
        /// <exception cref="Exception">If invalid function names are found, file lib fails, or export compilation fails</exception>
        public static bool RunPlotManagerMerge(GameTarget target, bool verboseLogging = false)
        {
            MLog.Information($@"Updating PlotManager for game: {target.TargetPath}");
            Dictionary<string, string> funcMap = new();
            Dictionary<string, string> sourceMap = new();
            List<string> combinedNames = new List<string>();

            var dlcs = target.GetInstalledDLCByMountPriority();
            dlcs.Reverse();

            foreach (var dlc in dlcs)
            {
                var dlcCookedPath = Path.Combine(target.GetDLCPath(), dlc, target.Game.CookedDirName());
                if (!Directory.Exists(dlcCookedPath))
                    continue;

                var dlcFiles = Directory.GetFiles(dlcCookedPath, @"*", SearchOption.TopDirectoryOnly);
                var metacmm = target.GetMetaCMMForDLC(dlc);
                var manifestFiles = new List<string>();

                // Find manifest files in the DLC files list
                foreach (var f in dlcFiles)
                {
                    var fname = Path.GetFileName(f);
                    if (fname == PLOT_MANAGER_UPDATE_FILENAME)
                    {
                        // This is a manifest file (v1)
                        manifestFiles.Add(f);
                        continue;
                    }

                    if (metacmm != null && metacmm.ModDescFeatureLevel >= ModDescConsts.MODDESC_VERSION_9_2)
                    {
                        // 9.2 and above: Support extra pmu files.
                        if (Path.GetExtension(fname) == PLOT_MANAGER_UPDATE_EXTENSION)
                        {
                            manifestFiles.Add(f);
                            continue;
                        }
                    }
                }

                foreach (var manifestFile in manifestFiles)
                {
                    MLog.Information($@"PlotSync: Processing {manifestFile}");
                    ProcessManifest(target, dlc, manifestFile, funcMap, sourceMap, combinedNames);
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
                MLog.Information($@"Initializing plot manager path with relative package cache path {target.GetBioGamePath()}");

                var usop = new UnrealScriptOptionsPackage()
                {
                    GamePathOverride = target.TargetPath,
                    Cache = new TargetPackageCache() { RootPath = M3Directories.GetBioGamePath(target) }
                };
                var fl = new FileLib(plotManager);
                bool initialized = fl.Initialize(usop, canUseBinaryCache: false);
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

                    (_, MessageLog log) = UnrealScriptCompiler.CompileFunction(exp, v.Value, fl, usop);
                    if (log.AllErrors.Any())
                    {
                        MLog.Error($@"Error compiling function {exp.InstancedFullPath}:");
                        foreach (var l in log.AllErrors)
                        {
                            MLog.Error(l.Message);
                        }


                        var source = $@"{v.Key} from {sourceMap[v.Key]}";
                        throw new Exception(LC.GetString(LC.string_interp_errorCompilingFunctionReason, source,
                            string.Join('\n', log.AllErrors.Select(x => x.Message))));
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

        /// <summary>
        /// Processes a single PMU manifest file and adds its functions to the funcMap and sourceMap.
        /// </summary>
        /// <param name="target">The game target</param>
        /// <param name="dlc">The DLC name</param>
        /// <param name="manifestFile">The full path to the manifest file</param>
        /// <param name="funcMap">Dictionary mapping function numbers to their source code</param>
        /// <param name="sourceMap">Dictionary mapping function numbers to their source DLC</param>
        /// <param name="combinedNames">List of UI names for all processed DLCs</param>
        /// <exception cref="Exception">If invalid function names are found</exception>
        private static void ProcessManifest(GameTarget target, string dlc, string manifestFile,
            Dictionary<string, string> funcMap, Dictionary<string, string> sourceMap, List<string> combinedNames)
        {
            var metaMaps = target.GetMetaMappedInstalledDLC(false);
            var uiName = metaMaps[dlc]?.ModName ??
                         TPMIService.GetThirdPartyModInfo(dlc, target.Game)?.modname ?? dlc;

            if (!combinedNames.Contains(uiName))
            {
                combinedNames.Add(uiName);
            }

            var text = File.ReadAllLines(manifestFile);
            StringBuilder sb = null;
            string currentFuncNum = null;

            foreach (var line in text)
            {
                if (line.StartsWith(@"public function bool F"))
                {
                    if (sb != null)
                    {
                        funcMap[currentFuncNum] = sb.ToString();
                        sourceMap[currentFuncNum] = dlc;
                        MLog.Information($@"PlotSync: Adding function {currentFuncNum} from {dlc}");
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
                            MLog.Error($@"Skipping plot manager update: Conditional {num} is not a valid number for use. Values must be greater than 0 and less than 2 billion.");
                            TelemetryInterposer.TrackEvent(@"Bad plot manager function",
                                new Dictionary<string, string>()
                                {
                                    { @"FunctionName", $@"F{currentFuncNum}" },
                                    { @"DLCName", dlc }
                                });
                            sb = null;
                            throw new Exception(LC.GetString(LC.string_dialog_invalidConditionalNumberSourceMod, num, uiName));
                        }

                        if (num.ToString().Length != currentFuncNum.Length)
                        {
                            MLog.Error($@"Skipping plot manager update: Conditional {currentFuncNum} is not a valid number for use. Values must not contain leading zeros");
                            TelemetryInterposer.TrackEvent(@"Bad plot manager function",
                                new Dictionary<string, string>()
                                {
                                    { @"FunctionName", $@"F{currentFuncNum}" },
                                    { @"DLCName", dlc }
                                });

                            sb = null;
                            throw new Exception(LC.GetString(LC.string_dialog_invalidConditionalNumberSourceMod, num, uiName));
                        }
                    }
                    else
                    {
                        // Not an integer somehow
                        MLog.Error(
                            $@"Skipping plot manager update: Conditional {currentFuncNum} is not a valid number for use. Values must be greater than 0 and less than 2 billion.");
                        TelemetryInterposer.TrackEvent(@"Bad plot manager function",
                            new Dictionary<string, string>()
                            {
                                { @"FunctionName", $@"F{currentFuncNum}" },
                                { @"DLCName", dlc }
                            });
                        sb = null;
                        throw new Exception(LC.GetString(LC.string_dialog_invalidConditionalNumberSourceMod, num, uiName));
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
                sourceMap[currentFuncNum] = dlc;
                MLog.Information($@"PlotSync: Adding function {currentFuncNum} from {dlc}");
            }
        }
    }
}
