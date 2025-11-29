using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Objects;
using ME3TweaksCore.Targets;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ME3TweaksCore.TextureOverride
{
    /// <summary>
    /// Install-time DLC merge for Texture override system
    /// </summary>
    public class M3CTextureOverrideMerge
    {
        /// <summary>
        /// Gets path to BTP for given target and DLC name
        /// </summary>
        /// <param name="target"></param>
        /// <param name="dlcFolderName"></param>
        /// <returns></returns>
        public static string GetCombinedTexturePackagePath(GameTarget target, string dlcFolderName)
        {
            return Path.Combine(target.GetDLCPath(), dlcFolderName, $@"CombinedTextureOverrides{BinaryTexturePackage.EXTENSION_TEXTURE_OVERRIDE_BINARY}");
        }


        // One consideration: Delete the texture packages in the DLC after merge is complete
        // This way we don't store redundant data in the DLC cooked folder as these aren't used by game
        // Probably will require some changes to installer or something... ugh

        /// <summary>
        /// Performs a texture merge on the given game's DLC folder, on the given DLC
        /// </summary>
        /// <param name="target">Target we are merging</param>
        /// <param name="dlcFolderName">Name of the DLC folder we are merging on</param>
        public static void PerformDLCMerge(GameTarget target, string dlcFolderName, ProgressInfo pi = null)
        {
            var cookedDir = Path.Combine(target.GetDLCPath(), dlcFolderName, target.Game.CookedDirName());
            if (!Directory.Exists(cookedDir))
            {
                MLog.Error($@"Cannot TextureOverride DLC merge {dlcFolderName}, cooked directory doesn't exist: {cookedDir}");
                return; // Cannot asset merge
            }

            var m3tos = Directory.GetFiles(cookedDir, @"*" + TextureOverrideManifest.EXTENSION_TEXTURE_OVERRIDE_MANIFEST, SearchOption.TopDirectoryOnly)
                .Where(x => Path.GetFileName(x).StartsWith(TextureOverrideManifest.PREFIX_TEXTURE_OVERRIDE_MANIFEST))
                .ToList(); // Find TextureOverride-*.m3to files

            // Generate combined/override list in order of found files.
            var combinedManifest = new TextureOverrideManifest
            {
                Game = target.Game,
                Textures = new List<TextureOverrideTextureEntry>()
            };

            foreach (var m3to in m3tos)
            {
                MLog.Information($@"Merging M3 Texture Override {m3to} in {dlcFolderName}");
                var manifestText = File.ReadAllText(m3to);
                var manifest = JsonConvert.DeserializeObject<TextureOverrideManifest>(manifestText);
                if (manifest.Game != target.Game)
                {
                    MLog.Error($@"Texture Override manifest game mismatch in {m3to} (file targets {manifest.Game}, we are merging {target.Game}), skipping this file.");
                    continue;
                }
                var errorText = manifest.MergeInto(combinedManifest);
            }

            // Generate the binary package
            if (combinedManifest.Textures.Count > 0)
            {
                pi?.Status = "Building texture override package";
                pi?.Value = 0;
                pi?.OnUpdate(pi);
                var binPath = GetCombinedTexturePackagePath(target, dlcFolderName);
                MLog.Information($@"Compiling M3 Texture Override binary package {binPath} for DLC {dlcFolderName}");
                combinedManifest.CompileBinaryTexturePackage(cookedDir, binPath, dlcFolderName, pi);
            }
        }
    }
}
