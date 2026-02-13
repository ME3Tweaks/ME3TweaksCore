using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Localization;
using ME3TweaksCore.NativeMods;
using ME3TweaksCore.NativeMods.Interfaces;
using ME3TweaksCore.Targets;

namespace ME3TweaksCoreWPF.NativeMods
{
    /// <summary>
    /// ASI file that is mapped to a known ASI file (WPF extensions). This class mirrors the non WPF version but does NOT subclass its subclasses!
    /// </summary>
    public class KnownInstalledASIModWPF : InstalledASIModWPF, IKnownInstalledASIMod
    {
        private static Brush installedBrush = new SolidColorBrush(Color.FromArgb(0x33, 0, 0xFF, 0));
        private static Brush outdatedBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0));

        public KnownInstalledASIModWPF(string filepath, string hash, MEGame game, ASIModVersion mappedVersion) : base(filepath, hash, game)
        {
            AssociatedManifestItem = mappedVersion;
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

        public override Brush BackgroundColor => Outdated ? outdatedBrush : installedBrush;

        /// <summary>
        /// The installation status of the ASI
        /// </summary>
        public string InstallStatus => Outdated ? LC.GetString(LC.string_outdatedVersionInstalled) : LC.GetString(LC.string_installed);


        /// <summary>
        /// Static constructor which can be used as a delegate
        /// </summary>
        /// <param name="asiFile"></param>
        /// <param name="hash"></param>
        /// <param name="game"></param>
        /// <param name="mappedVersion"></param>
        /// <returns></returns>
        public static KnownInstalledASIModWPF GenerateKnownInstalledASIModWPF(string asiFile, string hash, MEGame game, ASIModVersion mappedVersion)
        {
            return new KnownInstalledASIModWPF(asiFile, hash, game, mappedVersion);
        }

        public bool VerifyDependencies(GameTarget target)
        {
            return ASIManager.VerifyDependenciesShared(AssociatedManifestItem, target);
        }
    }
}
