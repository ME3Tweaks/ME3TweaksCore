using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
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
        // One consideration: Delete the texture packages in the DLC after merge is complete
        // This way we don't store redundant data in the DLC cooked folder as these aren't used by game

        public static void PerformDLCMerge(MEGame game, string dlcFolderRoot, string dlcFolderName)
        {
            var cookedDir = Path.Combine(dlcFolderRoot, dlcFolderName, game.CookedDirName());
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
                Game = game,
                Textures = new List<TextureOverrideTextureEntry>()
            };

            foreach (var m3to in m3tos)
            {
                MLog.Information($@"Merging M3 Texture Override {m3to} in {dlcFolderName}");
                var manifestText = File.ReadAllText(m3to);
                var manifest = JsonConvert.DeserializeObject<TextureOverrideManifest>(manifestText);
                if (manifest.Game != game)
                {
                    MLog.Error($@"Texture Override manifest game mismatch in {m3to} (file targets {manifest.Game}, we are merging {game}), skipping this file.");
                    continue;
                }
                var errorText = manifest.MergeInto(combinedManifest);
            }

            // Generate the binary package
            if (combinedManifest.Textures.Count > 0)
            {
                var binPath = Path.Combine(dlcFolderRoot, dlcFolderName, $@"CombinedTextureOverrides{TextureOverrideManifest.EXTENSION_TEXTURE_OVERRIDE_BINARY}");
                MLog.Information($@"Compiling M3 Texture Override binary package {binPath} for DLC {dlcFolderName}");
                combinedManifest.CompileBinaryTexturePackage(cookedDir, binPath);
            }
        }
    }
}
