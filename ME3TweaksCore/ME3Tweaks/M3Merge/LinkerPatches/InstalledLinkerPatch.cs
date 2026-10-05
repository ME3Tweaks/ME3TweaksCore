using System.Collections.Generic;
using System.Linq;

namespace ME3TweaksCore.ME3Tweaks.M3Merge.LinkerPatches
{
    /// <summary>
    /// UI model for a linker patch redirect that is installed into a target
    /// </summary>
    public class InstalledLinkerPatch
    {
        public string OriginalObject { get; }
        public string PatchObject { get; }
        public string PatchPackageFile { get; }
        public string SourceDLC { get; }
        public string SourceModName { get; }
        public string ManifestFile { get; }
        public string ManifestDescription { get; }
        public string PatchDescription { get; }

        /// <summary>
        /// Patches of the same object from lower mount priority DLC that this one overrode
        /// </summary>
        public List<LinkerPatchOverrideRecord> Overridden { get; }

        public InstalledLinkerPatch(LinkerPatchRedirectRecord record)
        {
            OriginalObject = record.OriginalObject;
            PatchObject = record.PatchObject;
            PatchPackageFile = record.PatchPackageFile;
            SourceDLC = record.SourceDLC;
            SourceModName = record.SourceModName;
            ManifestFile = record.ManifestFile;
            ManifestDescription = record.ManifestDescription;
            PatchDescription = record.PatchDescription;
            Overridden = record.Overridden ?? new List<LinkerPatchOverrideRecord>();
        }

        /// <summary>
        /// Names of the mods this patch overrode, or null if it overrode nothing
        /// </summary>
        public string UIOverriddenText => Overridden.Any() ? string.Join(@", ", Overridden.Select(x => string.IsNullOrWhiteSpace(x.ModName) ? x.DLC : x.ModName)) : null;

        /// <summary>
        /// Text to show for this patch. Uses the object path if the mod did not describe the patch
        /// </summary>
        public string UIDisplayName => string.IsNullOrWhiteSpace(PatchDescription) ? OriginalObject : PatchDescription;

        /// <summary>
        /// Text used to group patches by the mod they came from
        /// </summary>
        public string UIGroupHeader => string.IsNullOrWhiteSpace(SourceModName) ? SourceDLC : $@"{SourceModName} ({SourceDLC})";
    }
}
