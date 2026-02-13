using ME3TweaksCore.Localization;
using ME3TweaksCore.Targets;
using System.Linq;

namespace ME3TweaksCore.NativeMods.Interfaces
{
    /// <summary>
    /// Interface for all KnownInstalledASI implementations (as they have to fork for subclassing)
    /// </summary>
    public interface IKnownInstalledASIMod : IInstalledASIMod
    {
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
        /// The installation status string of the ASI
        /// </summary>
        public string InstallStatus => Outdated ? LC.GetString(LC.string_outdatedVersionInstalled) : LC.GetString(LC.string_installed);

        /// <summary>
        /// Verifies dependencies on this ASI mod. If they are not met the ASI is not considered installed as it will simply fail to load in game.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public bool VerifyDependencies(GameTarget target);
    }
}
