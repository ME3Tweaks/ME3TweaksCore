using System.IO;
using System.Linq;
using LegendaryExplorerCore.Coalesced;
using LegendaryExplorerCore.Coalesced.Config;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;

namespace ME3TweaksCore.Config
{
    /// <summary>
    /// M3CD config merge class
    /// </summary>
    public class M3CConfigMerge
    {
        public static void PerformDLCMerge(MEGame game, string dlcFolderRoot, string dlcFolderName)
        {
            var cookedDir = Path.Combine(dlcFolderRoot, dlcFolderName, game.CookedDirName());
            if (!Directory.Exists(cookedDir))
            {
                MLog.Error($@"Cannot ConfigMerge DLC {dlcFolderName}, cooked directory doesn't exist: {cookedDir}");
                return; // Cannot asset merge
            }

            var configBundle = ConfigAssetBundle.FromDLCFolder(game, cookedDir, dlcFolderName);
            if (configBundle == null)
            {
                MLog.Error($@"Cannot ConfigMerge DLC {dlcFolderName}, assets did not load");
                return; // Cannot asset merge
            }

            var m3cds = Directory.GetFiles(cookedDir, @"*" + ConfigMerge.CONFIG_MERGE_EXTENSION,
                    SearchOption.TopDirectoryOnly)
                .Where(x => Path.GetFileName(x).StartsWith(ConfigMerge.CONFIG_MERGE_PREFIX))
                .ToList(); // Find CoalescedMerge-*.m3cd files

            foreach (var m3cd in m3cds)
            {
                MLog.Information($@"Merging M3 Config Delta {m3cd} in {dlcFolderName}");
                var m3cdasset = ConfigFileProxy.LoadIni(m3cd);
                ConfigMerge.PerformMerge(configBundle, m3cdasset);
            }

            if (configBundle.HasChanges)
            {
                configBundle.CommitDLCAssets(); // Commit the final result
            }
        }

        /// <summary>
        /// Converts a configuration bundle object to a version that can be applied as an M3CD. This does not support double typing!
        /// </summary>
        /// <param name="configBundle">Bundle to convert</param>
        /// <returns></returns>
        public static string ConvertBundleToM3CD(ConfigAssetBundle configBundle)
        {
            var ini = new DuplicatingIni();
            foreach (var assetName in configBundle.GetAssetNames())
            {
                var asset = configBundle.GetAsset(assetName);
                foreach (var section in asset.Sections)
                {
                    var sectionHeader = $@"{Path.GetFileNameWithoutExtension(assetName)}.ini {section.Key}";
                    var m3cdSection = ini.GetOrAddSection(sectionHeader);
                    foreach (var uniqueProperty in section.Value)
                    {
                        foreach (var multiProperty in uniqueProperty.Value)
                        {
                            m3cdSection.Entries.Add(new DuplicatingIni.IniEntry($@"{ConfigFileProxy.GetGame2IniDataPrefix(multiProperty.ParseAction)}{uniqueProperty.Key}", multiProperty.Value));
                        }
                    }
                }
            }

            return ini.ToString();
        }
    }
}
