using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Objects;
using ME3TweaksCore.Targets;
using Newtonsoft.Json;
using System;
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
        /// Name of BTP file in a DLC mod that is loaded by the ASI
        /// </summary>
        public const string COMBINED_BTP_FILENAME = $@"CombinedTextureOverrides{BinaryTexturePackage.EXTENSION_TEXTURE_OVERRIDE_BINARY}";

        /// <summary>
        /// Gets path to BTP for given target and DLC name
        /// </summary>
        /// <param name="target"></param>
        /// <param name="dlcFolderName"></param>
        /// <returns></returns>
        public static string GetCombinedTexturePackagePath(GameTarget target, string dlcFolderName)
        {
            return Path.Combine(target.GetDLCPath(), dlcFolderName, COMBINED_BTP_FILENAME);
        }

        /// <summary>
        /// Name of BTP metadata file in a DLC mod
        /// </summary>
        public const string BTP_METADATA_FILENAME = $@"BTPMetadata.btm";


        /// <summary>
        /// Gets path to BTP metadata for given target and DLC name
        /// </summary>
        /// <param name="target"></param>
        /// <param name="dlcFolderName"></param>
        /// <returns></returns>
        public static string GetBTPMetadataPath(GameTarget target, string dlcFolderName)
        {
            return Path.Combine(target.GetDLCPath(), dlcFolderName, BTP_METADATA_FILENAME);
        }

        /// <summary>
        /// Performs a texture merge on the given game's DLC folder, on the given DLC
        /// </summary>
        /// <param name="target">Target we are merging</param>
        /// <param name="dlcFolderName">Name of the DLC folder we are merging on</param>
        public static string PerformDLCMerge(GameTarget target, string dlcFolderName, bool deleteTOFiles, ProgressInfo pi = null)
        {
            var cookedDir = Path.Combine(target.GetDLCPath(), dlcFolderName, target.Game.CookedDirName());
            if (!Directory.Exists(cookedDir))
            {
                MLog.Error($@"Cannot TextureOverride DLC merge {dlcFolderName}, cooked directory doesn't exist: {cookedDir}");
                return null; // Cannot merge, just ignore this folder
            }

            var m3tos = Directory.GetFiles(cookedDir, @"*" + TextureOverrideManifest.EXTENSION_TEXTURE_OVERRIDE_MANIFEST, SearchOption.TopDirectoryOnly);
            var matchingOverrides = m3tos.Where(x => Path.GetFileName(x).StartsWith(TextureOverrideManifest.PREFIX_TEXTURE_OVERRIDE_MANIFEST))
                .ToList(); // Find TextureOverride-*.m3to files

            // Generate combined/override list in order of found files.
            var combinedManifest = new TextureOverrideManifest
            {
                Game = target.Game,
                Textures = new List<TextureOverrideTextureEntry>()
            };

            foreach (var m3to in matchingOverrides)
            {
                MLog.Information($@"Merging M3 Texture Override {m3to} in {dlcFolderName}");
                var manifestText = File.ReadAllText(m3to);

                if (string.IsNullOrEmpty(manifestText))
                {
                    MLog.Warning($@"Skipping empty manifest file {m3to}");
                    continue;
                }
                var manifest = JsonConvert.DeserializeObject<TextureOverrideManifest>(manifestText);

                try
                {
                    if (!manifest.Verify(Path.GetFileName(m3to), target.Game, true))
                    {
                        // no throw but false is skipped.
                        continue;
                    }
                    manifest.MergeInto(combinedManifest);
                }
                catch (Exception ex)
                {
                    // Bubble up the error message
                    return ex.Message;
                }
            }

            // Generate the binary package
            if (combinedManifest.Textures.Count > 0)
            {
                pi?.Status = LC.GetString(LC.string_buildingTextureOverridePackage);
                pi?.Value = 0;
                pi?.OnUpdate(pi);
                try
                {
                    combinedManifest.CompileBinaryTexturePackage(target, dlcFolderName, pi);
                }
                catch (Exception ex)
                {
                    // Remove this file cause it could crash game if left around
                    var binPath = GetCombinedTexturePackagePath(target, dlcFolderName);
                    if (File.Exists(binPath))
                    {
                        File.Delete(binPath);
                    }

                    var metadataPath = GetBTPMetadataPath(target, dlcFolderName);
                    if (File.Exists(metadataPath))
                    {
                        File.Delete(metadataPath);
                    }

                    // Bubble up the error message
                    return ex.Message;
                }
                // Now delete TO_ packages, as we don't want them clogging up the game.
                if (deleteTOFiles)
                {
                    var toFiles = Directory.GetFiles(cookedDir, @"TO_*.pcc", SearchOption.AllDirectories);
                    foreach (var tof in toFiles)
                    {
                        try
                        {
                            MLog.Information($@"Deleting texture override file from game: {tof}");
                            File.Delete(tof);
                        }
                        catch (Exception e)
                        {
                            MLog.Error($@"Unable to delete TO_ file after merge: {tof}, {e.Message}");
                        }
                    }
                }
            }

            return null;
        }
    }
}