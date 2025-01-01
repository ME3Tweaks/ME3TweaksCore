using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.Coalesced;
using LegendaryExplorerCore.Coalesced.Config;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Services.Shared.BasegameFileIdentification;
using ME3TweaksCore.Targets;

namespace ME3TweaksCore.ME3Tweaks.M3Merge.LE1Config
{
    public class ConsoleKeybinding
    {
        /// <summary>
        /// If the value is actually set
        /// </summary>
        public bool IsSetByUser { get; set; }
        /// <summary>
        /// What key is assigned (null means not bound at all)
        /// </summary>
        public string AssignedKey { get; set; }
    }
    public class LE1ConfigMerge
    {
        public static bool RunCoalescedMerge(GameTarget target, ConsoleKeybinding consoleKey, ConsoleKeybinding miniConsoleKey, bool log = false)
        {
            MLog.Information($@"Performing Config Merge for game: {target.TargetPath}");
            var coalescedStream = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.M3Merge.LE1Config.Coalesced_INT.bin");
            var configBundle = ConfigAssetBundle.FromSingleStream(MEGame.LE1, coalescedStream);

            var dlcMountsInOrder = MELoadedDLC.GetDLCNamesInMountOrder(target.Game, target.TargetPath);

            // For BGFIS
            bool mergedAny = false;
            string recordedMergeName = @"";
            void recordMerge(string displayName)
            {
                mergedAny = true;
                recordedMergeName += displayName + "\n"; // do not localize
            }


            foreach (var dlc in dlcMountsInOrder)
            {
                var dlcCookedPath = Path.Combine(target.GetDLCPath(), dlc, target.Game.CookedDirName());

                MLog.Information($@"Looking for ConfigDelta-*.m3cd files in {dlcCookedPath}", log);
                var m3cds = Directory.GetFiles(dlcCookedPath, @"*" + ConfigMerge.CONFIG_MERGE_EXTENSION, SearchOption.TopDirectoryOnly)
                    .Where(x => Path.GetFileName(x).StartsWith(ConfigMerge.CONFIG_MERGE_PREFIX)).ToList(); // Find CoalescedMerge-*.m3cd files
                MLog.Information($@"Found {m3cds.Count} m3cd files to apply", log);

                foreach (var m3cd in m3cds)
                {
                    MLog.Information($@"Merging M3 Config Delta {m3cd}");
                    var m3cdasset = ConfigFileProxy.LoadIni(m3cd);
                    ConfigMerge.PerformMerge(configBundle, m3cdasset);
                    recordMerge($@"{dlc}-{Path.GetFileName(m3cd)}");
                }
            }

            var consolem3Cd = MakeConsoleM3CD(consoleKey, miniConsoleKey);
            if (consolem3Cd != null)
            {
                MLog.Information(@"Merging M3 Config Delta for user chosen keybinds");
                ConfigMerge.PerformMerge(configBundle, consolem3Cd);
                recordMerge(LC.GetString(LC.string_m3ConsoleKeybinds)); // we do want this localized
            }

            var records = new List<BasegameFileRecord>();
            var coalFile = Path.Combine(target.GetCookedPath(), @"Coalesced_INT.bin");
            // Set the BGFIS record name
            if (mergedAny)
            {
                // Serialize the assets
                configBundle.CommitAssets(coalFile, MELocalization.INT);

                // Submit to BGFIS
                records.Add(new BasegameFileRecord(coalFile, target, recordedMergeName.Trim()));
                BasegameFileIdentificationService.AddLocalBasegameIdentificationEntries(records);
            }
            else
            {
                coalescedStream.WriteToFile(coalFile);
            }

            return true;
        }

        /// <summary>
        /// Generates a dynamic M3CD based on user's LE1 console keybinds they last chose
        /// </summary>
        /// <returns></returns>
        private static CoalesceAsset MakeConsoleM3CD(ConsoleKeybinding consoleKey, ConsoleKeybinding miniConsoleKey)
        {
            if ((consoleKey == null || !consoleKey.IsSetByUser) && (miniConsoleKey == null || !miniConsoleKey.IsSetByUser))
            {
                return null;
            }

            DuplicatingIni m3cdIni = new DuplicatingIni();
            var bioInput = m3cdIni.GetOrAddSection(@"BIOInput.ini Engine.Console");
            if (consoleKey != null && consoleKey.IsSetByUser)
            {
                bioInput.SetSingleEntry(@">ConsoleKey", consoleKey.AssignedKey);
            }
            if (miniConsoleKey != null && miniConsoleKey.IsSetByUser)
            {
                bioInput.SetSingleEntry(@">TypeKey", miniConsoleKey.AssignedKey);
            }

            return ConfigFileProxy.ParseIni(m3cdIni.ToString());
        }
    }
}
