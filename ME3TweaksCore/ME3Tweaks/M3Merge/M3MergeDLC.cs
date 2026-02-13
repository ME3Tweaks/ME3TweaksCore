using System;
using System.IO;
using LegendaryExplorerCore.Coalesced;
using LegendaryExplorerCore.Coalesced.Config;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.ME3Tweaks.M3Merge.Game2Email;
using ME3TweaksCore.ME3Tweaks.M3Merge.GlobalShader;
using ME3TweaksCore.ME3Tweaks.M3Merge.LE1Config;
using ME3TweaksCore.ME3Tweaks.M3Merge.PlotManager;
using ME3TweaksCore.ME3Tweaks.StarterKit;
using ME3TweaksCore.Targets;

namespace ME3TweaksCore.ME3Tweaks.M3Merge
{
    public static class TargetMergeExtensions
    {
        /// <summary>
        /// If this game supports AutoTOC
        /// </summary>
        /// <param name="game"></param>
        /// <returns></returns>
        public static bool SupportsAutoTOC(this MEGame game) => game == MEGame.ME3 || game.IsLEGame();

        /// <summary>
        /// If this game supports plot manager sync (merge DLC pmu files)
        /// </summary>
        /// <param name="game"></param>
        /// <returns></returns>
        public static bool SupportsPlotManagerSync(this MEGame game) => game.IsGame1() || game.IsGame2();

        /// <summary>
        /// If this game supports plot manager sync (merge DLC pmu files)
        /// </summary>
        /// <param name="game"></param>
        /// <returns></returns>
        public static bool SupportsSquadmateMerge(this MEGame game) => game == MEGame.LE2 || game.IsGame3();

        /// <summary>
        /// If this game supports email merge (merge DLC emm files)
        /// </summary>
        /// <param name="game"></param>
        /// <returns></returns>
        public static bool SupportsEmailMerge(this MEGame game) => game.IsGame2();

    }


    public class M3MergeDLC
    {
        #region INSTANCE
        /// <summary>
        /// Target this merge DLC is for.
        /// </summary>
        public GameTarget Target { get; set; }

        /// <summary>
        /// The location where the MergeDLC should exist for the target this instanced was created with.
        /// </summary>
        public string MergeDLCPath { get; set; }

        /// <summary>
        /// Generates information about a merge DLC for a target. Use the static methods to access a game's information about a merge.
        /// </summary>
        /// <param name="target"></param>
        public M3MergeDLC(GameTarget target)
        {
            Target = target;
            MergeDLCPath = Path.Combine(M3Directories.GetDLCPath(target), MERGE_DLC_FOLDERNAME);
        }
        #endregion

        public const int MERGE_DLC_MOUNT_PRIORITY = 1900000000;
        public const string MERGE_DLC_FOLDERNAME = @"DLC_MOD_M3_MERGE";
        private const string MERGE_DLC_GUID_ATTRIBUTE_NAME = @"MergeDLCGUID";

        /// <summary>
        /// Removes a merge DLC from a target if it exists.
        /// </summary>
        /// <param name="target"></param>
        public static void RemoveMergeDLC(GameTarget target)
        {
            var mergePath = Path.Combine(M3Directories.GetDLCPath(target), MERGE_DLC_FOLDERNAME);
            if (Directory.Exists(mergePath))
            {
                MUtilities.DeleteFilesAndFoldersRecursively(mergePath);
            }
        }

        /// <summary>
        /// Gets the current GUID of the merge DLC. This can be used to track a unique instance of a merge DLC.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static Guid? GetCurrentMergeGuid(GameTarget target)
        {
            var metaPath = Path.Combine(M3Directories.GetDLCPath(target), MERGE_DLC_FOLDERNAME, @"_metacmm.txt");
            if (File.Exists(metaPath))
            {
                MetaCMM m = new MetaCMM(metaPath);
                if (m.ExtendedAttributes.TryGetValue(MERGE_DLC_GUID_ATTRIBUTE_NAME, out var guidStr))
                {
                    try
                    {
                        return new Guid(guidStr);
                    }
                    catch
                    {
                        MLog.Warning($@"Could not convert to M3 Merge Guid: {guidStr}. Will return null guid");
                    }
                }
            }

            return null; // Not found!
        }

        /// <summary>
        /// Generates a new merge DLC folder and assigns it a new Guid.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="guid"></param>
        public void GenerateMergeDLC()
        {
            // Generate M3 DLC Folder

            // Does not work for LE1/ME1!
            var sko = new StarterKitOptions()
            {
                ModGame = Target.Game,
                Target = Target,
                GenerateModdesc = false,
                OutputFolderOverride = M3Directories.GetDLCPath(Target),
                ModDescription = null,
                ModInternalName = @"ME3TweaksCore Merge DLC",
                ModInternalTLKID = 1928304430,
                ModMountFlag = Target.Game.IsGame3() ? new MountFlag(EME3MountFileFlag.LoadsInSingleplayer) : new MountFlag(0, true),
                ModDeveloper = @"ME3TweaksCore",
                ModMountPriority = MERGE_DLC_MOUNT_PRIORITY,
                ModDLCFolderNameSuffix = MERGE_DLC_FOLDERNAME.Substring(@"DLC_MOD_".Length),
                ModModuleNumber = 48955 // GAME 2
            };

            DLCModGenerator.CreateStarterKitMod(Target.GetDLCPath(), sko, null, out _, null); // not generating anything using patch dlc mod so don't need to specify it
            MetaCMM mcmm = new MetaCMM()
            {
                ModName = @"ME3TweaksCore Auto-Generated Merge DLC",
                Version = @"1.0",
                ExtendedAttributes =
                {
                    { MERGE_DLC_GUID_ATTRIBUTE_NAME, Guid.NewGuid().ToString() }// A new GUID is generated
                }
            };
            mcmm.WriteMetaCMM(Path.Combine(M3Directories.GetDLCPath(Target), MERGE_DLC_FOLDERNAME, @"_metacmm.txt"), $@"{Path.GetFileNameWithoutExtension(MLibraryConsumer.GetHostingProcessname())} {MLibraryConsumer.GetAppVersion()}");

            Generated = true;
        }

        /// <summary>
        /// If the merge DLC was generated
        /// </summary>
        public bool Generated { get; set; }

        public const int STARTING_CONDITIONAL = 10000;
        public const int STARTING_TRANSITION = 90000;

        /// <summary>
        /// These can be used by MUST BE INCREMENTED ON USE
        /// </summary>
        public int CurrentConditional = STARTING_CONDITIONAL;
        /// <summary>
        /// These can be used by MUST BE INCREMENTED ON USE
        /// </summary>
        public int CurrentTransition = STARTING_TRANSITION;

        /// <summary>
        /// Adds the plot data into the config folder if necessary
        /// </summary>
        /// <param name="mergeDLC"></param>
        public static void AddPlotDataToConfig(M3MergeDLC mergeDLC)
        {
            var configBundle = ConfigAssetBundle.FromDLCFolder(mergeDLC.Target.Game, Path.Combine(mergeDLC.Target.GetDLCPath(), M3MergeDLC.MERGE_DLC_FOLDERNAME, mergeDLC.Target.Game.CookedDirName()), M3MergeDLC.MERGE_DLC_FOLDERNAME);

            // add startup file
            var bioEngine = configBundle.GetAsset(@"BIOEngine.ini");
            var startupSection = bioEngine.GetOrAddSection(@"Engine.StartupPackages");
            startupSection.AddEntryIfUnique(new CoalesceProperty(@"DLCStartupPackage", new CoalesceValue($@"Startup_{M3MergeDLC.MERGE_DLC_FOLDERNAME}", CoalesceParseAction.AddUnique)));
            if (mergeDLC.Target.Game.IsGame2())
            {
                // Game 3 uses Conditionals.cnd
                // Game 1 merges into basegame directly
                startupSection.AddEntryIfUnique(new CoalesceProperty(@"Package",
                    new CoalesceValue($@"PlotManager{M3MergeDLC.MERGE_DLC_FOLDERNAME}",
                        CoalesceParseAction.AddUnique)));
                startupSection.AddEntryIfUnique(new CoalesceProperty(@"Package",
                    new CoalesceValue($@"PlotManagerAuto{M3MergeDLC.MERGE_DLC_FOLDERNAME}",
                        CoalesceParseAction.AddUnique)));


                // Add conditionals 
                var bioGame = configBundle.GetAsset(@"BIOGame.ini");
                var bioWorldInfoConfig = bioGame.GetOrAddSection(@"SFXGame.BioWorldInfo");
                bioWorldInfoConfig.AddEntryIfUnique(new CoalesceProperty(@"ConditionalClasses",
                    new CoalesceValue($@"PlotManager{M3MergeDLC.MERGE_DLC_FOLDERNAME}.BioAutoConditionals",
                        CoalesceParseAction.AddUnique)));
            }

            configBundle.CommitDLCAssets();
        }

        // Is this run on other projects besides M3?
        /// <summary>
        /// Runs a full merge (via MergeDLC) on the specific target.
        /// </summary>
        /// <param name="target"></param>
        public static void RunCompleteMerge(GameTarget target, Action<string> setStatus = null)
        {
            // This doesn't use MergeDLC but runs at the same time, technically.
            if (target.Game == MEGame.LE1)
            {
                LE1ConfigMerge.RunCoalescedMerge(target, null, null, true); // Todo: Shared settings with M3 for console keybinds.
            }

            // This also does not use the MergeDLC component.
            if ((target.Game.IsGame1() || target.Game.IsGame2()) && PlotManagerMerge.NeedsMerged(target))
            {
                // Todo: Verbose logging pass through.
                PlotManagerMerge.RunPlotManagerMerge(target, false);
            }

            // The following do use the merge DLC component
            var mergeDLC = new M3MergeDLC(target);
            if (target.Game.IsGame2() && ME2EmailMerge.NeedsMergedGame2(target))
            {
                if (!mergeDLC.Generated) mergeDLC.GenerateMergeDLC();
                ME2EmailMerge.RunGame2EmailMerge(mergeDLC, setStatus);
            }

            if ((target.Game == MEGame.LE2 || target.Game.IsGame3()) && SQMOutfitMerge.NeedsMerged(target))
            {
                if (!mergeDLC.Generated) mergeDLC.GenerateMergeDLC();
                SQMOutfitMerge.RunSquadmateOutfitMerge(mergeDLC, setStatus);
            }

            if (target.Game.IsLEGame() && GlobalShaderMerge.NeedsMerged(target))
            {

            }
        }
    }
}
