using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ME3TweaksCore.ME3Tweaks.M3Merge.LinkerPatches
{
    /// <summary>
    /// Record of the last linker patch merge, written next to the merged output. This is the source of truth for which linker patches are active.
    /// </summary>
    public class LinkerPatchBuildRecord
    {
        /// <summary>
        /// When the merge was run
        /// </summary>
        [JsonProperty(@"buildTime")]
        public DateTime BuildTime { get; set; }

        /// <summary>
        /// The manifests that contributed to this build. Used to detect when the build is out of date.
        /// </summary>
        [JsonProperty(@"sourceManifests")]
        public List<LinkerPatchSourceManifestRecord> SourceManifests { get; set; } = new();

        /// <summary>
        /// The redirects that were written into the index file
        /// </summary>
        [JsonProperty(@"redirects")]
        public List<LinkerPatchRedirectRecord> Redirects { get; set; } = new();

        /// <summary>
        /// Loads a build record from disk. Returns null if it does not exist.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static LinkerPatchBuildRecord Load(string path)
        {
            if (!File.Exists(path))
                return null;
            return JsonConvert.DeserializeObject<LinkerPatchBuildRecord>(File.ReadAllText(path));
        }

        /// <summary>
        /// Writes this build record to disk
        /// </summary>
        /// <param name="path"></param>
        public void Save(string path)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }

    /// <summary>
    /// A manifest that contributed to a linker patch build
    /// </summary>
    public class LinkerPatchSourceManifestRecord
    {
        /// <summary>
        /// DLC folder name the manifest is in
        /// </summary>
        [JsonProperty(@"dlc")]
        public string DLC { get; set; }

        /// <summary>
        /// Path of the manifest, relative to the DLC folder
        /// </summary>
        [JsonProperty(@"manifestRelPath")]
        public string ManifestRelPath { get; set; }

        /// <summary>
        /// MD5 of the manifest at build time
        /// </summary>
        [JsonProperty(@"md5")]
        public string MD5 { get; set; }
    }

    /// <summary>
    /// A redirect written into the linker patch index
    /// </summary>
    public class LinkerPatchRedirectRecord
    {
        /// <summary>
        /// MemoryFullPath of the object being replaced
        /// </summary>
        [JsonProperty(@"originalObject")]
        public string OriginalObject { get; set; }

        /// <summary>
        /// Package name of the installed patch package (the PatchObject value in the index)
        /// </summary>
        [JsonProperty(@"patchObject")]
        public string PatchObject { get; set; }

        /// <summary>
        /// Filename of the patch package in the source DLC's LinkerPatches folder
        /// </summary>
        [JsonProperty(@"patchPackageFile")]
        public string PatchPackageFile { get; set; }

        /// <summary>
        /// DLC folder the winning patch came from
        /// </summary>
        [JsonProperty(@"sourceDLC")]
        public string SourceDLC { get; set; }

        /// <summary>
        /// Mod name of the DLC the winning patch came from, from _metacmm.txt
        /// </summary>
        [JsonProperty(@"sourceModName")]
        public string SourceModName { get; set; }

        /// <summary>
        /// Filename of the manifest that declared this patch
        /// </summary>
        [JsonProperty(@"manifestFile")]
        public string ManifestFile { get; set; }

        /// <summary>
        /// Description of the manifest that declared this patch
        /// </summary>
        [JsonProperty(@"manifestDescription")]
        public string ManifestDescription { get; set; }

        /// <summary>
        /// Description of this patch
        /// </summary>
        [JsonProperty(@"patchDescription")]
        public string PatchDescription { get; set; }

        /// <summary>
        /// Lower mount priority patches of the same object that this redirect overrode
        /// </summary>
        [JsonProperty(@"overridden")]
        public List<LinkerPatchOverrideRecord> Overridden { get; set; } = new();
    }

    /// <summary>
    /// A patch that was overridden by a higher mount priority DLC
    /// </summary>
    public class LinkerPatchOverrideRecord
    {
        /// <summary>
        /// DLC folder name of the overridden patch
        /// </summary>
        [JsonProperty(@"dlc")]
        public string DLC { get; set; }

        /// <summary>
        /// Mod name of the overridden patch's DLC
        /// </summary>
        [JsonProperty(@"modName")]
        public string ModName { get; set; }

        /// <summary>
        /// Filename of the manifest that declared the overridden patch
        /// </summary>
        [JsonProperty(@"manifestFile")]
        public string ManifestFile { get; set; }

        /// <summary>
        /// Description of the overridden patch
        /// </summary>
        [JsonProperty(@"patchDescription")]
        public string PatchDescription { get; set; }
    }
}
