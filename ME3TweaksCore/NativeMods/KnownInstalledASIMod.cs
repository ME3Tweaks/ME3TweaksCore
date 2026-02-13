using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Localization;
using ME3TweaksCore.NativeMods.Interfaces;
using ME3TweaksCore.Targets;

namespace ME3TweaksCore.NativeMods
{
    /// <summary>
    /// ASI file that is mapped to a known ASI file
    /// </summary>
    public class KnownInstalledASIMod : InstalledASIMod, IKnownInstalledASIMod
    {
        public KnownInstalledASIMod(string filepath, string hash, MEGame game, ASIModVersion mappedVersion) : base(filepath, hash, game)
        {
            AssociatedManifestItem = mappedVersion;
        }

        public bool VerifyDependencies(GameTarget target)
        {
            return ASIManager.VerifyDependenciesShared(AssociatedManifestItem, target);
        }

        /// <summary>
        /// The manifest version information about this installed ASI mod
        /// </summary>
        public ASIModVersion AssociatedManifestItem { get; set; }

        /// <summary>
        /// If this installed ASI mod is outdated
        /// </summary>
        public bool Outdated
        {
            get
            {
                if (AssociatedManifestItem.OwningMod.LatestVersion == null)
                {
                    // When we conduct check make sure it is not null.
                    return AssociatedManifestItem.OwningMod.LatestVersionIncludingHidden.Version > AssociatedManifestItem.Version;
                }
                return AssociatedManifestItem.OwningMod.LatestVersionIncludingHidden.Version > AssociatedManifestItem.Version;
            }
        }

        /// <summary>
        /// The installation status string for this ASI
        /// </summary>
        public string InstallStatus => Outdated ? LC.GetString(LC.string_outdatedVersionInstalled) : LC.GetString(LC.string_installed);
    }
}
