using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
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
                throw new Exception($"Cannot use texture override manifests for different games: {Game} <-> {target.Game}");
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

        // BTP - Binary Texture Package (Manifest)
        public const string EXTENSION_TEXTURE_OVERRIDE_BINARY = @".btp";
        public const ushort CURRENT_VERSION = 1;

        private static string MANIFEST_HEADER => "LETEXM"; // Must be ASCII

        /// <summary>
        /// Serializes this manifest object to its binary form for use by the game.
        /// </summary>
        /// <param name="sourceFolder">The base folder. For DLC this will be DLC_MOD_NAME/CookedPCConsole/</param>
        /// <param name="destFile">Where files are serialized to. For how ASI expects it, it should be DLC_MOD_NAME/TheFile</param>
        public void CompileBinaryTexturePackage(string sourceFolder, string destFile)
        {
            MLog.Information($@"Compiling Texture Override binary package to {destFile} with {Textures.Count} textures");
            using var outStream = File.Open(destFile, FileMode.Create, FileAccess.ReadWrite);
            using var dataSegment = new MemoryStream();

            // Write Header.
            outStream.WriteStringASCII(MANIFEST_HEADER);
            outStream.WriteUInt16(CURRENT_VERSION);
            outStream.WriteUInt32(uint.MaxValue); // Not entirely sure what to put here.
            outStream.WriteInt32(Textures.Count);
            outStream.WriteZeros(16); // Reserved

            // Write metadata segment
            foreach (var texture in Textures)
            {
                texture.Serialize(sourceFolder, outStream, dataSegment);
            }

            outStream.SeekEnd();

            // Write out data segment
            var dataSegmentStart = (int)outStream.Position;
            dataSegment.SeekBegin();
            dataSegment.CopyTo(outStream); // Append

            // Go in and update the offsets
            foreach (var texture in Textures)
            {
                texture.SerializeOffsets(outStream, dataSegmentStart);
            }
        }
    }
}