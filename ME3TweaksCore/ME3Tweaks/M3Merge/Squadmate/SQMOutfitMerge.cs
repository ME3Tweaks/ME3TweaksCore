﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegendaryExplorerCore.Coalesced;
using LegendaryExplorerCore.Coalesced.Config;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Gammtek.Extensions.Collections.Generic;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Packages.CloningImportingAndRelinking;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.UnrealScript;
using LegendaryExplorerCore.UnrealScript.Compiling.Errors;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.ME3Tweaks.StarterKit.LE2;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Targets;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ME3TweaksCore.ME3Tweaks.M3Merge
{
    public class SQMOutfitMerge
    {
        public const string SQUADMATE_MERGE_MANIFEST_V2_EXTENSION = @".sqm2";

        public const string SQUADMATE_MERGE_MANIFEST_FILE = @"SquadmateMergeInfo.sqm";
        private const string SUICIDE_MISSION_STREAMING_PACKAGE_NAME = @"BioP_EndGm_StuntHench.pcc";
        public class SquadmateMergeInfo
        {
            [JsonConverter(typeof(StringEnumConverter))]
            [JsonProperty(@"game")]
            public MEGame Game { get; set; }

            [JsonProperty(@"outfits")]
            public List<SquadmateInfoSingle> Outfits { get; set; }

            public bool Validate(string dlcName, GameTarget target, CaseInsensitiveDictionary<string> loadedFiles)
            {
                foreach (var outfit in Outfits)
                {
                    // Check packages
                    if (!loadedFiles.ContainsKey($@"{outfit.HenchPackage}.pcc"))
                    {
                        MLog.Error($@"SquadmateMergeInfo failed validation: {outfit.HenchPackage}.pcc not found in game");
                        return false;
                    }

                    if (Game.IsGame3())
                    {
                        if (!loadedFiles.ContainsKey($@"{outfit.HenchPackage}_Explore.pcc"))
                        {
                            MLog.Error($@"SquadmateMergeInfo failed validation: {outfit.HenchPackage}_Explore.pcc not found in game");
                            return false;
                        }
                    }

                    if (!loadedFiles.ContainsKey($@"SFXHenchImages_{dlcName}.pcc"))
                    {
                        MLog.Error($@"SquadmateMergeInfo failed validation: SFXHenchImages_{dlcName}.pcc not found in game");
                        return false;
                    }
                }

                return true;
            }
        }


        /// <summary>
        /// Loads the sqm file from the given DLC directory. Loads a blank if none.
        /// </summary>
        /// <param name="game"></param>
        /// <param name="contentDirectory"></param>
        /// <returns></returns>
        public static SquadmateMergeInfo LoadSquadmateMergeInfo(MEGame game, string contentDirectory)
        {
            var sqmPath = Path.Combine(contentDirectory, game.CookedDirName(), SQMOutfitMerge.SQUADMATE_MERGE_MANIFEST_FILE);
            if (File.Exists(sqmPath))
            {
                // Load existing
                return JsonConvert.DeserializeObject<SQMOutfitMerge.SquadmateMergeInfo>(File.ReadAllText(sqmPath));
            }
            else
            {
                // Create new.
                return new SquadmateMergeInfo() { Game = game, Outfits = [] };
            }
        }

        private static StructProperty GeneratePlotStreamingElement(string packageName, int conditionalNum)
        {
            PropertyCollection pc = new PropertyCollection();
            pc.AddOrReplaceProp(new NameProperty(packageName, @"ChunkName"));
            pc.AddOrReplaceProp(new IntProperty(conditionalNum, @"Conditional"));
            pc.AddOrReplaceProp(new BoolProperty(false, @"bFallback"));
            pc.AddOrReplaceProp(new NoneProperty());

            return new StructProperty(@"PlotStreamingElement", pc);
        }

        private static int GetSquadmateOutfitInt(string squadmateName, MEGame game)
        {
            MLog.Information($@"SQMMERGE: Generating outfit int for {game} {squadmateName}");
            if (game.IsGame2())
            {
                switch (squadmateName)
                {
                    case @"Convict": return 314;
                    case @"Garrus": return 318;
                    case @"Geth": return 315;
                    case @"Grunt": return 322;
                    case @"Leading": return 313;
                    case @"Mystic": return 323;
                    case @"Professor": return 321;
                    case @"Tali": return 320;
                    case @"Thief": return 317;
                    case @"Veteran": return 324;
                    case @"Vixen": return 312;
                    case @"Assassin": return 319;

                        // LOTSB doesn't use plot streaming for liara
                        // case @"Liara": return 312;
                }
            }
            else if (game.IsGame3())
            {
                switch (squadmateName)
                {
                    case @"Liara": return 10152;
                    case @"Kaidan": return 10153;
                    case @"Ashley": return 10154;
                    case @"Garrus": return 10155;
                    case @"EDI": return 10156;
                    case @"Prothean": return 10157;
                    case @"Marine": return 10158;
                    case @"Tali": return 10214;
                        // case @"Wrex": return ??; Wrex outfit can't be changed, its hardcoded to 13, which is TRUE
                }
            }

            throw new Exception(LC.GetString(LC.string_interp_invalidHenchNameSquadmateNameValueIsCaseSensitive, squadmateName));
        }

        /// <summary>
        /// Returns if the specified target has any squadmate outfit merge files.
        /// </summary>
        /// <param name="target"></param>
        public static bool NeedsMerged(GameTarget target)
        {
            if (!target.Game.IsGame3() && target.Game != MEGame.LE2) return false;
            var sqmSupercedances = M3Directories.GetFileSupercedances(target, new[] { @".sqm" });
            return sqmSupercedances.TryGetValue(SQUADMATE_MERGE_MANIFEST_FILE, out var infoList) && infoList.Count > 0;
        }

        /// <summary>
        /// Generates squadmate outfit information for Game 3 and LE2. The merge DLC must be already generated.
        /// </summary>
        /// <param name="mergeDLC"></param>
        /// <exception cref="Exception"></exception>
        public static string RunSquadmateOutfitMerge(M3MergeDLC mergeDLC, Action<string> updateUIText)
        {
            if (!mergeDLC.Generated)
                return null; // Do not run on non-generated. It may be that a prior check determined this merge was not necessary 
            Stopwatch sw = Stopwatch.StartNew();
            string result = null;
            var loadedFiles = MELoadedFiles.GetFilesLoadedInGame(mergeDLC.Target.Game, gameRootOverride: mergeDLC.Target.TargetPath);
            //var mergeFiles = loadedFiles.Where(x =>
            //    x.Key.StartsWith(@"BioH_") && x.Key.Contains(@"_DLC_MOD_") && x.Key.EndsWith(@".pcc") && !x.Key.Contains(@"_LOC_") && !x.Key.Contains(@"_Explore."));

            MLog.Information(@"SQMMERGE: Building BioP_Global");
            var appearanceInfo = new CaseInsensitiveDictionary<List<SquadmateInfoSingle>>();

            int appearanceId = mergeDLC.Target.Game.IsGame3() ? 255 : 3; // starting // LE2 is 0-8, LE3 does not care

            // Scan squadmate merge files
            var sqmSupercedances = M3Directories.GetFileSupercedances(mergeDLC.Target, new[] { @".sqm" });
            var squadmateImageInfosLE2 = new List<LE2SquadmateImageInfo>();

            if (sqmSupercedances.TryGetValue(SQUADMATE_MERGE_MANIFEST_FILE, out var infoList))
            {
                infoList.Reverse();
                foreach (var dlc in infoList)
                {
                    MLog.Information($@"SQMMERGE: Processing {dlc}");

                    var jsonFile = Path.Combine(M3Directories.GetDLCPath(mergeDLC.Target), dlc, mergeDLC.Target.Game.CookedDirName(), SQUADMATE_MERGE_MANIFEST_FILE);
                    SquadmateMergeInfo infoPackage = null;
                    try
                    {
                        infoPackage = JsonConvert.DeserializeObject<SquadmateMergeInfo>(File.ReadAllText(jsonFile));
                    }
                    catch (Exception ex)
                    {
                        result = LC.GetString(LC.string_errorReadingSquadmateOutfitManifestFileSeeLogs);
                        MLog.Exception(ex, $@"Error reading squadmate merge manifest: {jsonFile}. This DLC will not be squadmate merged");
                    }

                    if (infoPackage == null || !infoPackage.Validate(dlc, mergeDLC.Target, loadedFiles))
                    {
                        continue; // skip this
                    }

                    IMEPackage imagePackage = null; // Not used for LE3
                    if (mergeDLC.Target.Game == MEGame.LE2)
                    {
                        var henchImagesP = Path.Combine(mergeDLC.Target.GetDLCPath(), dlc, mergeDLC.Target.Game.CookedDirName(), $@"SFXHenchImages_{dlc}.pcc");
                        imagePackage = MEPackageHandler.OpenMEPackage(henchImagesP);
                    }

                    // Enumerate all outfits listed for a single squadmate
                    foreach (var outfit in infoPackage.Outfits)
                    {
                        List<SquadmateInfoSingle> list;

                        // See if we already have an outfit list for this squadmate, maybe from another mod...
                        if (!appearanceInfo.TryGetValue(outfit.HenchName, out list))
                        {
                            list = new List<SquadmateInfoSingle>();
                            appearanceInfo[outfit.HenchName] = list;
                        }

                        outfit.ConditionalIndex = mergeDLC.CurrentConditional++; // This is always incremented, so it might appear out of order in game files depending on how mod order is processed, that should be okay though.

                        if (mergeDLC.Target.Game.IsGame2())
                        {
                            // This is the 'slot' of the outfit for this squadmate
                            outfit.AppearanceId = list.Any() ? (list.MaxBy(x => x.AppearanceId).AppearanceId + 1) : GetFirstAvailableSquadmateAppearanceIndexLE2(outfit.HenchName); // Get first unused slot

                            // 06/17/2024 - Update to 32 with changes by Nanuke
                            // Todo: If higher than 31 (0 index) we have too many outfits!!!!
                            if (outfit.AppearanceId > 31)
                            {
                                MLog.Error(@"Squadmate outfit merge for LE2 only supports 32 outfits per character currently!");
                                MLog.Error($@"This outfit for {outfit.HenchName} will be skipped.");
                                result = LC.GetString(LC.string_someSquadmateOutfitsWereNotMergedSeeLogs);
                                continue;
                            }

                            var availableImage = imagePackage.FindExport(outfit.AvailableImage);
                            if (availableImage == null)
                            {
                                MLog.Error($@"Available image {outfit.AvailableImage} not found in package: {imagePackage.FilePath}. This outfit will be skipped");
                                result = LC.GetString(LC.string_someSquadmateOutfitsWereNotMergedSeeLogs);
                                continue;
                            }

                            var selectedImage = imagePackage.FindExport(outfit.HighlightImage);
                            if (selectedImage == null)
                            {
                                MLog.Error($@"Selected image {outfit.HighlightImage} not found in package: {imagePackage.FilePath}. This outfit will be skipped");
                                result = LC.GetString(LC.string_someSquadmateOutfitsWereNotMergedSeeLogs);
                                continue;
                            }

                            // Add the source exports to the porting list
                            squadmateImageInfosLE2.Add(new LE2SquadmateImageInfo()
                            {
                                SourceExport = availableImage,
                                DestinationTextureName = GetTextureExportNameForSquadmateLE2(outfit.HenchName, outfit.AppearanceId, false)
                            });

                            squadmateImageInfosLE2.Add(new LE2SquadmateImageInfo()
                            {
                                SourceExport = selectedImage,
                                DestinationTextureName = GetTextureExportNameForSquadmateLE2(outfit.HenchName, outfit.AppearanceId, true)
                            });
                        }
                        else if (mergeDLC.Target.Game.IsGame3())
                        {
                            // Must be fully unique
                            outfit.AppearanceId = appearanceId++; // may need adjusted
                        }
                        outfit.DLCName = dlc;
                        list.Add(outfit);
                        MLog.Information($@"SQMMERGE: ConditionalIndex for {outfit.HenchName} appearanceid {outfit.AppearanceId}: {outfit.ConditionalIndex}");
                    }

                    //Debug.WriteLine("hi");
                }
            }

            if (appearanceInfo.Any())
            {
                var cookedDir = Path.Combine(M3Directories.GetDLCPath(mergeDLC.Target),
                    M3MergeDLC.MERGE_DLC_FOLDERNAME, mergeDLC.Target.Game.CookedDirName());

                var packagesToPatch = new List<string>();
                packagesToPatch.Add(@"BioP_Global.pcc"); // Game 2 and 3 both use this file as the primary hench streaming
                if (mergeDLC.Target.Game.IsGame2())
                {
                    // Game 2 has an extra global file for suicide mission
                    packagesToPatch.Add(SUICIDE_MISSION_STREAMING_PACKAGE_NAME);
                }

                foreach (var package in packagesToPatch)
                {
                    var streamingPackage = MEPackageHandler.OpenMEPackage(loadedFiles[package]);
                    var lsk = streamingPackage.Exports.FirstOrDefault(x => x.ClassName == @"LevelStreamingKismet");

                    // Clone LevelStreamingKismets
                    foreach (var sqm in appearanceInfo.Values)
                    {
                        foreach (var outfit in sqm)
                        {
                            var fName = outfit.HenchPackage;
                            if (package == SUICIDE_MISSION_STREAMING_PACKAGE_NAME)
                            {
                                // We use END packages since they don't add to party
                                fName = @"BioH_END_" + fName.Substring(5); // Take the original text after BioH_
                            }

                            var newLSK = EntryCloner.CloneEntry(lsk);
                            newLSK.WriteProperty(new NameProperty(fName, @"PackageName"));
                            if (mergeDLC.Target.Game.IsGame3())
                            {
                                // Game 3 has _Explore files too
                                fName += @"_Explore";
                                newLSK = EntryCloner.CloneEntry(lsk);
                                newLSK.WriteProperty(new NameProperty(fName, @"PackageName"));
                            }
                        }
                    }

                    // Update BioWorldInfo
                    // Doesn't have consistent number so we can't find it by instanced full path
                    var bioWorldInfo = streamingPackage.Exports.FirstOrDefault(x => x.ClassName == @"BioWorldInfo");

                    var props = bioWorldInfo.GetProperties();

                    // Update Plot Streaming
                    var plotStreaming = props.GetProp<ArrayProperty<StructProperty>>(@"PlotStreaming");
                    foreach (var sqm in appearanceInfo.Values)
                    {
                        foreach (var outfit in sqm)
                        {
                            // find item to add to
                            // Use END if game 2 and we are suicide mission package
                            buildPlotElementObject(plotStreaming, outfit, mergeDLC.Target.Game, mergeDLC.Target.Game.IsGame2() && package == SUICIDE_MISSION_STREAMING_PACKAGE_NAME);
                            if (mergeDLC.Target.Game.IsGame3())
                            {
                                // Add EXPLORE too
                                buildPlotElementObject(plotStreaming, outfit, mergeDLC.Target.Game, true);
                            }
                        }
                    }


                    // Update StreamingLevels
                    var streamingLevels = props.GetProp<ArrayProperty<ObjectProperty>>(@"StreamingLevels");
                    streamingLevels.ReplaceAll(streamingPackage.Exports
                        .Where(x => x.ClassName == @"LevelStreamingKismet").Select(x => new ObjectProperty(x)));

                    bioWorldInfo.WriteProperties(props);

                    // Save plot streaming controller package into DLC

                    var outP = Path.Combine(cookedDir, package);
                    streamingPackage.Save(outP);
                }

                // Generate conditionals file
                if (mergeDLC.Target.Game.IsGame3())
                {
                    // ME3/LE3
                    CNDFile cnd = new CNDFile();
                    cnd.ConditionalEntries = new List<CNDFile.ConditionalEntry>();

                    foreach (var sqm in appearanceInfo.Values)
                    {
                        foreach (var outfit in sqm)
                        {
                            var scText = $@"(plot.ints[{GetSquadmateOutfitInt(outfit.HenchName, mergeDLC.Target.Game)}] == i{outfit.MemberAppearanceValue})";
                            var compiled = ME3ConditionalsCompiler.Compile(scText);
                            cnd.ConditionalEntries.Add(new CNDFile.ConditionalEntry()
                            { Data = compiled, ID = outfit.ConditionalIndex });
                        }
                    }

                    cnd.ToFile(Path.Combine(cookedDir, $@"Conditionals{M3MergeDLC.MERGE_DLC_FOLDERNAME}.cnd"));
                }
                else if (mergeDLC.Target.Game.IsGame2())
                {
                    // LE2
                    var startupF = Path.Combine(cookedDir, $@"Startup_{M3MergeDLC.MERGE_DLC_FOLDERNAME}.pcc");
                    var startup = MEPackageHandler.OpenMEPackageFromStream(MUtilities.GetResourceStream($@"ME3TweaksCore.ME3Tweaks.M3Merge.Startup.{mergeDLC.Target.Game}.Startup_{M3MergeDLC.MERGE_DLC_FOLDERNAME}.pcc"), $@"Startup_{M3MergeDLC.MERGE_DLC_FOLDERNAME}.pcc");
                    var conditionalClass = startup.FindExport($@"PlotManager{M3MergeDLC.MERGE_DLC_FOLDERNAME}.BioAutoConditionals");

                    // Add Conditional Functions
                    var packageCache = new TargetPackageCache() { RootPath = mergeDLC.Target.GetBioGamePath() };
                    UnrealScriptOptionsPackage usop = new UnrealScriptOptionsPackage() { Cache = packageCache, GamePathOverride = mergeDLC.Target.TargetPath };
                    FileLib fl = new FileLib(startup);
                    bool initialized = fl.Initialize(usop);
                    if (!initialized)
                    {
                        throw new Exception(@"FileLib for script update could not initialize, cannot install conditionals");
                    }


                    var scTextOrig = new StreamReader(MUtilities.GetResourceStream($@"ME3TweaksCore.ME3Tweaks.M3Merge.Squadmate.{mergeDLC.Target.Game}.HasOutfitOnConditional.uc"))
                        .ReadToEnd();
                    foreach (var sqm in appearanceInfo.Values)
                    {
                        foreach (var outfit in sqm)
                        {
                            var scText = scTextOrig.Replace(@"%CONDITIONALNUM%", outfit.ConditionalIndex.ToString());
                            scText = scText.Replace(@"%SQUADMATEOUTFITPLOTINT%", GetSquadmateOutfitInt(outfit.HenchName, MEGame.LE2).ToString());
                            scText = scText.Replace(@"%OUTFITINDEX%", outfit.MemberAppearanceValue.ToString());

                            MessageLog log = UnrealScriptCompiler.AddOrReplaceInClass(conditionalClass, scText, fl, usop);
                            if (log.AllErrors.Any())
                            {
                                MLog.Error($@"Error compiling function F{outfit.ConditionalIndex}:");
                                foreach (var l in log.AllErrors)
                                {
                                    MLog.Error(l.Message);
                                }
                                throw new Exception(LC.GetString(LC.string_interp_errorCompilingConditionalFunction, $@"F{outfit.ConditionalIndex}", string.Join('\n', log.AllErrors.Select(x => x.Message))));
                            }
                        }
                    }


                    // Relink the conditionals chain
                    //UClass uc = ObjectBinary.From<UClass>(conditionalClass);
                    //uc.UpdateLocalFunctions();
                    //uc.UpdateChildrenChain();
                    //conditionalClass.WriteBinary(uc);

                    startup.Save(startupF);
                }


                // Add startup package, member appearances
                if (mergeDLC.Target.Game.IsGame2())
                {
                    M3MergeDLC.AddPlotDataToConfig(mergeDLC);

                    // Add appearances to list
                    var configBundle = ConfigAssetBundle.FromDLCFolder(mergeDLC.Target.Game, cookedDir, M3MergeDLC.MERGE_DLC_FOLDERNAME);
                    var bioUi = configBundle.GetAsset(@"BIOUI.ini");
                    var partySelectionSection = bioUi.GetOrAddSection(@"SFXGame.BioSFHandler_PartySelection");

                    foreach (var sqm in appearanceInfo.Values)
                    {
                        foreach (var outfit in sqm)
                        {
                            //lstAppearances = (Tag = hench_tali, AddAppearance = 3, PlotFlag = -1);
                            var properties = new Dictionary<string, string>();
                            properties[@"Tag"] = $@"hench_{outfit.HenchName.ToLower()}";
                            properties[@"AddAppearance"] = outfit.AppearanceId.ToString();
                            properties[@"PlotFlag"] = outfit.PlotFlag.ToString();
                            var appearanceStruct = StringStructParser.BuildCommaSeparatedSplitValueList(properties);
                            partySelectionSection.AddEntry(new CoalesceProperty(@"lstAppearances",
                                new CoalesceValue(appearanceStruct, CoalesceParseAction.AddUnique)));
                        }
                    }

                    configBundle.CommitDLCAssets();

                    // Update squadmate images
                    // Create and patch BioH_SelectGUI for more squadmate images

                    // Lvl2/3/4 are LOTSB
                    int numDone = 0;
                    int numToDo = 5;
                    updateUIText?.Invoke(LC.GetString(LC.string_synchronizingSquadmateOutfits) + @" 0%");
                    var packagesToInjectInto = new[]
                        { @"BioH_SelectGUI.pcc", @"BioP_Exp1Lvl2.pcc", @"BioP_Exp1Lvl3.pcc", @"BioP_Exp1Lvl4.pcc" };
                    using var swfStream = MUtilities.ExtractInternalFileToStream($@"ME3TweaksCore.ME3Tweaks.M3Merge.Squadmate.{mergeDLC.Target.Game}.TeamSelect.swf");
                    var swfData = swfStream.ToArray();
                    Parallel.ForEach(packagesToInjectInto, package =>
                    {

                        //foreach (var package in packagesToInjectInto)
                        //{
                        var packageF = loadedFiles[package];
                        using var packageP = MEPackageHandler.OpenMEPackage(packageF);

                        // Inject extended SWF
                        var swf = packageP.FindExport(@"GUI_SF_TeamSelect.TeamSelect");
                        var rawData = swf.GetProperty<ImmutableByteArrayProperty>(@"RawData");
                        rawData.Bytes = swfData;
                        swf.WriteProperty(rawData);

                        // Inject images
                        var teamSelect = packageP.FindExport(@"GUI_SF_TeamSelect.TeamSelect");
                        var teamSelectRefs = teamSelect.GetProperty<ArrayProperty<ObjectProperty>>(@"References");
                        foreach (var squadmateImage in squadmateImageInfosLE2)
                        {
                            squadmateImage.InjectSquadmateImageIntoPackage(packageP, teamSelectRefs);
                        }
                        teamSelect.WriteProperty(teamSelectRefs);

                        var time = Stopwatch.StartNew();
                        var teamSelectPackagePath = Path.Combine(cookedDir, package);
                        packageP.Save(teamSelectPackagePath); // Save into merge DLC
                        time.Stop();
                        MLog.Information($@"Saved teamselect package {teamSelectPackagePath} in {time.ElapsedMilliseconds}ms");

                        var count = Interlocked.Increment(ref numDone);
                        updateUIText?.Invoke(LC.GetString(LC.string_synchronizingSquadmateOutfits) + $@" {(int)(count * 100.0f / numToDo)}%");

                    });
                    //}

                    // Patch Zaeed's loyalty mission, as it waits specifically for his 00 outfit to load
                    var zaeedLoyF = loadedFiles[@"BioD_ZyaVTL_110Jungle.pcc"];
                    using var zaeedLoyP = MEPackageHandler.OpenMEPackage(zaeedLoyF);

                    // We could edit the name and it'd be easier, but we want to ensure compatibility
                    // So we have to change the value.
                    var wait = zaeedLoyP.FindExport(@"TheWorld.PersistentLevel.Main_Sequence.Level_Startup.SeqAct_WaitForLevelsVisible_0");
                    var levelNames = wait.GetProperty<ArrayProperty<NameProperty>>(@"LevelNames");
                    levelNames[0] = new NameProperty(@"BioH_Veteran"); // We do not do _00, we use virtual
                    wait.WriteProperty(levelNames);
                    var savePath = Path.Combine(cookedDir, Path.GetFileName(zaeedLoyF));
                    zaeedLoyP.Save(savePath); // Save into merge DLC
                    var count = Interlocked.Increment(ref numDone);
                    updateUIText?.Invoke(LC.GetString(LC.string_synchronizingSquadmateOutfits) + $@" {(int)(count * 100.0f / numToDo)}%");

                }
                else if (mergeDLC.Target.Game.IsGame3())
                {
                    // Change to config bundle
                    var configBundle = ConfigAssetBundle.FromDLCFolder(mergeDLC.Target.Game, cookedDir, M3MergeDLC.MERGE_DLC_FOLDERNAME);

                    // Member appearances
                    var bioUI = configBundle.GetAsset(@"BioUI");
                    var teamselect = bioUI.GetOrAddSection(@"sfxgame.sfxguidata_teamselect");

                    foreach (var sqm in appearanceInfo.Values)
                    {
                        foreach (var outfit in sqm)
                        {
                            teamselect.AddEntry(new CoalesceProperty(@"selectappearances", new CoalesceValue(StringStructParser.BuildCommaSeparatedSplitValueList(outfit.ToPropertyDictionary(), @"AvailableImage", @"HighlightImage", @"DeadImage", @"SilhouetteImage"), CoalesceParseAction.AddUnique)));
                        }
                    }

                    // Dynamic load mapping
                    var bioEngine = configBundle.GetAsset(@"BioEngine");
                    var sfxEngine = bioEngine.GetOrAddSection(@"sfxgame.sfxengine");

                    foreach (var sqm in appearanceInfo.Values)
                    {
                        foreach (var outfit in sqm)
                        {
                            // * <Section name="sfxgame.sfxengine">
                            // <Property name="dynamicloadmapping">
                            // <Value type="3">(ObjectName="BIOG_GesturesConfigDLC.RuntimeData",SeekFreePackageName="GesturesConfigDLC")</Value>
                            sfxEngine.AddEntry(new CoalesceProperty(@"dynamicloadmapping", new CoalesceValue($"(ObjectName=\"{outfit.AvailableImage}\",SeekFreePackageName=\"SFXHenchImages_{outfit.DLCName}\")", CoalesceParseAction.AddUnique))); // do not localize
                            sfxEngine.AddEntry(new CoalesceProperty(@"dynamicloadmapping", new CoalesceValue($"(ObjectName=\"{outfit.HighlightImage}\",SeekFreePackageName=\"SFXHenchImages_{outfit.DLCName}\")", CoalesceParseAction.AddUnique))); // do not localize
                        }
                    }

                    configBundle.CommitDLCAssets();
                }
            }

            sw.Stop();
            MLog.Information($@"Ran SQMOutfitMerge in {sw.ElapsedMilliseconds}ms");
            return result;
        }

        private static void buildPlotElementObject(ArrayProperty<StructProperty> plotStreaming, SquadmateInfoSingle sqm, MEGame game, bool isSpecial)
        {
            var fName = sqm.HenchPackage;
            var virtualChunk = $@"BioH_{sqm.HenchName}";
            if (game.IsGame2() && isSpecial)
            {
                fName = @"BioH_END_" + fName.Substring(5); // Take the original text after BioH_
                virtualChunk = $@"BioH_END_{sqm.HenchName}";
            }
            else if (game.IsGame3() && isSpecial)
            {
                fName += @"_Explore";
                virtualChunk += @"_Explore";
            }

            var element = plotStreaming.FirstOrDefault(x => x.GetProp<NameProperty>(@"VirtualChunkName").Value == virtualChunk);
            if (element != null)
            {
                var elem = element.GetProp<ArrayProperty<StructProperty>>(@"Elements");
                sqm.MemberAppearanceValue = elem.Count;
                elem.Add(GeneratePlotStreamingElement(fName, sqm.ConditionalIndex));
            }
        }

        /// <summary>
        /// LE2 has hardcoded squadmate images that load from the package that holds the SWF
        /// naNuke built a swf with more slots that reference the below names based on the hex
        /// representation of the ID of the sprite in the swf
        /// </summary>
        /// <param name="henchname"></param>
        /// <returns></returns>
        private static string GetTextureExportNameForSquadmateLE2(string henchname, int appearanceNumber, bool isGlow)
        {
            // Appearance indexes are 1-9
            var baseSpriteId = GetBaseSpriteIdForSquadmateImage(henchname, appearanceNumber);
            if (isGlow) baseSpriteId += 3;
            return $@"TeamSelect_I{baseSpriteId:X}";
        }

        private static int GetBaseSpriteIdForSquadmateImage(string henchname, int appearanceIndex)
        {
            // New slots use a fixed base number (increments of 500) and all start at the (appearance index * 10 + 1)
            // glow is offset by 3 and then 7 to the next non-glow for a skip of 10.
            // We add one to the base index number when calculating to account for the offsets being indexed at 1 and not at 0 (see the texture sheet)
            var lowerHench = henchname.ToLowerInvariant();
            switch (lowerHench)
            {
                case @"vixen":
                    {
                        if (appearanceIndex == 0) return 0x4C; // Default
                        if (appearanceIndex == 1) return 0xB4; // Loyalty
                        if (appearanceIndex == 2) return 0x109; // DLC
                        return 1500 + ((appearanceIndex + 1) * 10) + 1;
                    }
                case @"garrus":
                    {
                        if (appearanceIndex == 0) return 0x61; // Default
                        if (appearanceIndex == 1) return 0xBC; // Loyalty
                        if (appearanceIndex == 2) return 0x111; // DLC
                        return 2000 + ((appearanceIndex + 1) * 10) + 1;
                    }
                case @"mystic":
                    {
                        if (appearanceIndex == 0) return 0x68; // Default
                        if (appearanceIndex == 1) return 0xC3; // Loyalty
                        return 2500 + ((appearanceIndex + 1) * 10) + 1;
                    }
                case @"grunt":
                    {
                        if (appearanceIndex == 0) return 0x6F; // Default
                        if (appearanceIndex == 1) return 0xCA; // Loyalty
                        if (appearanceIndex == 2) return 0x118; // DLC
                        return 3000 + ((appearanceIndex + 1) * 10) + 1;
                    }
                case @"leading":
                    {
                        if (appearanceIndex == 0) return 0x78; // Default
                        if (appearanceIndex == 1) return 0xD1; // Loyalty
                        return 3500 + ((appearanceIndex + 1) * 10) + 1;
                    }
                case @"tali":
                    {
                        if (appearanceIndex == 0) return 0x7F; // Default
                        if (appearanceIndex == 1) return 0xD8; // Loyalty
                        if (appearanceIndex == 2) return 0x11F; // DLC
                        return 4000 + ((appearanceIndex + 1) * 10) + 1;
                    }
                case @"convict":
                    {
                        if (appearanceIndex == 0) return 0x866; // Default
                        if (appearanceIndex == 1) return 0xDF; // Loyalty
                        if (appearanceIndex == 2) return 0x126; // DLC
                        return 4500 + ((appearanceIndex + 1) * 10) + 1;
                    }
                case @"geth":
                    {
                        if (appearanceIndex == 0) return 0x8D; // Default
                        if (appearanceIndex == 1) return 0xE6; // Loyalty
                        return 5000 + ((appearanceIndex + 1) * 10) + 1;
                    }
                case @"thief":
                    {
                        if (appearanceIndex == 0) return 0x96; // Default
                        if (appearanceIndex == 1) return 0xED; // Loyalty
                        return 5500 + ((appearanceIndex + 1) * 10) + 1;
                    }
                case @"assassin":
                    {
                        if (appearanceIndex == 0) return 0x9D; // Default
                        if (appearanceIndex == 1) return 0xF4; // Loyalty
                        if (appearanceIndex == 2) return 0x12D; // DLC
                        return 6000 + ((appearanceIndex + 1) * 10) + 1;
                    }
                case @"professor":
                    {
                        if (appearanceIndex == 0) return 0xA6; // Default
                        if (appearanceIndex == 1) return 0xFB; // Loyalty
                        return 6500 + ((appearanceIndex + 1) * 10) + 1;
                    }
                case @"veteran":
                    {
                        if (appearanceIndex == 0) return 0xAD; // Default
                        if (appearanceIndex == 1) return 0x102; // Loyalty
                        return 7000 + ((appearanceIndex + 1) * 10) + 1;
                    }
            }

            // The custom slot, not sure how we will implement this. I just say it's 'custom' which won't return anything in the next func but the 1 value
            return 7500 + ((appearanceIndex + 1) * 10) + 1;
        }

        private static int GetFirstAvailableSquadmateAppearanceIndexLE2(string henchname)
        {
            henchname = henchname.ToLowerInvariant();
            if (henchname == @"vixen") return 3;
            if (henchname == @"garrus") return 3;
            if (henchname == @"mystic") return 2;
            if (henchname == @"grunt") return 3;
            if (henchname == @"leading") return 2;
            if (henchname == @"tali") return 3;
            if (henchname == @"geth") return 2;
            if (henchname == @"convict") return 3;
            if (henchname == @"thief") return 2;
            if (henchname == @"assassin") return 3;
            if (henchname == @"professor") return 2;
            if (henchname == @"veteran") return 2;

            return 1; // 13th slot is custom and begins at 1. 0 is done via the member info struct
        }
    }
}