using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Objects;
using ME3TweaksCore.Targets;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ME3TweaksCore.TextureOverride
{
    /// <summary>
    /// JSON manifest for texture override
    /// </summary>
    public class TextureOverrideManifest
    {
        /// <summary>
        /// DLC-folder extension for texture override manifests files
        /// </summary>
        public const string EXTENSION_TEXTURE_OVERRIDE_MANIFEST = @".m3to";
        public const string PREFIX_TEXTURE_OVERRIDE_MANIFEST = @"TextureOverride-";


        /// <summary>
        /// Metadata-only comment. Not used by the compiler but kept here for serialization purposes.
        /// </summary>
        //[JsonProperty(@"comment")]
        // public string Comment { get; set; }

        /// <summary>
        /// What game this manifest is for
        /// </summary>
        [JsonProperty(@"game")]
        [JsonConverter(typeof(StringEnumConverter))]
        public MEGame Game { get; set; }

        /// <summary>
        /// Textures to override
        /// </summary>
        [JsonProperty(@"textures")]
        public List<TextureOverrideTextureEntry> Textures { get; set; }

        /// <summary>
        /// Merges this manifest into the target
        /// </summary>
        /// <param name="target"></param>
        internal bool MergeInto(TextureOverrideManifest target)
        {
            if (target.Game != Game)
            {
                MLog.Error($@"Cannot override textures from different games. {Game} <-> {target.Game}");
                throw new Exception(LC.GetString(LC.string_interp_toManifestGameMismatch1, Game, target.Game));
            }

            if (this.Textures != null)
            {
                if (target.Textures == null)
                {
                    target.Textures = new List<TextureOverrideTextureEntry>();
                }

                // Merge and replace duplicate textures
                target.Textures = Textures.Concat(target.Textures)
                                             .GroupBy(item => item.TextureIFP, StringComparer.OrdinalIgnoreCase)
                                             .Select(group => group.First()) // Take the first element as we concatenated existing list to ours
                                             .ToList();
            }

            return true;
        }

        // SERIALIZATION TO BINARY =======================================================

        /// <summary>
        /// Serializes this manifest object to its binary form for use by the game.
        /// </summary>
        /// <param name="sourceFolder">The base folder. For DLC this will be DLC_MOD_NAME/CookedPCConsole/</param>
        /// <param name="destFile">Where files are serialized to. For how ASI expects it, it should be DLC_MOD_NAME/TheFile</param>
        public void CompileBinaryTexturePackage(GameTarget target, string dlcName, ProgressInfo pi = null)
        {
            var btpDest = M3CTextureOverrideMerge.GetCombinedTexturePackagePath(target, dlcName);
            MLog.Information($@"Compiling Texture Override binary package to {btpDest} with {Textures.Count} textures");

            var metadataPackage = MEPackageHandler.CreateAndOpenPackage(M3CTextureOverrideMerge.GetBTPMetadataPath(target, dlcName), Game);
            metadataPackage.FindNameOrAdd(dlcName); // DLC name is first in name table.
            using var btpStream = new FileStream(btpDest, FileMode.Create);
            var compiler = new TextureOverrideCompiler();
            var sourceFolder = Path.Combine(target.GetDLCPath(), dlcName, target.Game.CookedDirName());
            compiler.BuildBTPFromTO(this, sourceFolder, btpStream, dlcName, pi, metadataPackage);
        }

        /// <summary>
        /// Does verification checks that this manifest adheres to the required standards
        /// </summary>
        /// <param name="filePath">Path for logging/showing in exceptions</param>
        /// <param name="targetGame">Target game</param>
        /// <param name="throwIfFailed">Set to true to make this method throw localized exceptions on failure.</param>
        /// <returns></returns>
        /// <exception cref="Exception">When verification fails and throwIfFailed is true</exception>
        internal bool Verify(string filePath, MEGame targetGame, bool throwIfFailed = false)
        {
            if (Game != targetGame)
            {
                MLog.Error($@"Texture Override manifest {filePath} game mismatch in {filePath} (file targets {Game}, we are loading for {targetGame}), skipping this file.");
                if (throwIfFailed)
                {
                    throw new Exception(LC.GetString(LC.string_interp_toManifestGameMismatch1, filePath, Game, targetGame));
                }
                return false;
            }

            foreach (var entry in Textures)
            {
                var fileName = Path.GetFileName(entry.CompilingSourcePackage);
                if (!fileName.StartsWith(@"TO_"))
                {
                    MLog.Error($@"Texture Override manifest {filePath} has invalid source package name: {fileName} - files MUST start with 'TO_', skipping this file.");
                    if (throwIfFailed)
                    {
                        throw new Exception(LC.GetString(LC.string_interp_toManifestGameMismatch2, filePath, fileName));
                    }
                    return false;
                }
            }


            return true;
        }
    }
}
