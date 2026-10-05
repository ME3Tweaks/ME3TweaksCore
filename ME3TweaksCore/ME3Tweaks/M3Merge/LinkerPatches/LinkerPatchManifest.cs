using System.Collections.Generic;
using System.IO;
using LegendaryExplorerCore.Packages;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ME3TweaksCore.ME3Tweaks.M3Merge.LinkerPatches
{
    /// <summary>
    /// A .m3lp manifest, shipped by a DLC mod in its LinkerPatches folder. Describes which in-memory objects are replaced by which patch packages.
    /// </summary>
    public class LinkerPatchManifest
    {
        /// <summary>
        /// The game this manifest is for
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty(@"game")]
        public MEGame Game { get; set; }

        /// <summary>
        /// Optional description of this set of patches. Shown in Installation Information and written to logs.
        /// </summary>
        [JsonProperty(@"description")]
        public string Description { get; set; }

        /// <summary>
        /// The patches this manifest declares
        /// </summary>
        [JsonProperty(@"patches")]
        public List<LinkerPatchManifestEntry> Patches { get; set; } = new();

        /// <summary>
        /// Loads a manifest from disk. Throws if the file is not valid JSON.
        /// </summary>
        /// <param name="path">Path to the .m3lp file</param>
        /// <returns></returns>
        public static LinkerPatchManifest Load(string path)
        {
            return JsonConvert.DeserializeObject<LinkerPatchManifest>(File.ReadAllText(path));
        }
    }

    /// <summary>
    /// A single object redirect declared by a .m3lp manifest
    /// </summary>
    public class LinkerPatchManifestEntry
    {
        /// <summary>
        /// The MemoryFullPath of the object to replace, as Legendary Explorer shows it
        /// </summary>
        [JsonProperty(@"originalObject")]
        public string OriginalObject { get; set; }

        /// <summary>
        /// Filename of the patch package in the DLC's LinkerPatches folder. Must start with Patch_ (case sensitive) and end with .pcc.
        /// The patch object must be at the same path in the package as the original object (e.g. GUI_SF_Splash.Splash_I2); MilanHost loads that tree as part of the patch package.
        /// </summary>
        [JsonProperty(@"patchPackage")]
        public string PatchPackage { get; set; }

        /// <summary>
        /// Optional, non-authoritative hint of the package filename (e.g. SFXGame.pcc) that contains the original object. It is searched first when validating the original.
        /// The file may belong to another mod that is not installed, in which case the original cannot be validated and the patch is still installed.
        /// </summary>
        [JsonProperty(@"originalPackage")]
        public string OriginalPackage { get; set; }

        /// <summary>
        /// Optional description of what this patch does
        /// </summary>
        [JsonProperty(@"description")]
        public string Description { get; set; }
    }
}
