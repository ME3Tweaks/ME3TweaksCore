using System.Linq;
using System.Collections.Generic;
using LegendaryExplorerCore.Packages;
using System.Diagnostics;

namespace ME3TweaksCore.NativeMods
{
    /// <summary>
    /// Represents a single group of ASI mods across versions. This is used to help prevent installation of duplicate ASIs even if the names differ
    /// </summary>
    [DebuggerDisplay(@"ASIMod - {LatestVersionIncludingHidden}")]
    public class ASIMod
    {
        public ASIMod() { }

        /// <summary>
        /// Copy constructor with optional new game.
        /// </summary>
        /// <param name="source"></param>
        public ASIMod(ASIMod source, MEGame? newGame = null, int? newGroupId = null)
        {
            var game = newGame ?? source.Game;
            var groupId = newGroupId ?? source.UpdateGroupId;
            UpdateGroupId = groupId;
            Game = game;
            IsHidden = source.IsHidden;
            Versions = source.Versions.Select(v => new ASIModVersion
            {
                Name = v.Name,
                InstalledPrefix = v.InstalledPrefix,
                Author = v.Author,
                Version = v.Version,
                Description = v.Description,
                Hash = v.Hash,
                SourceCodeLink = v.SourceCodeLink,
                DownloadLink = v.DownloadLink,
                IsBeta = v.IsBeta,
                Hidden = v.Hidden,
                DevModeOnly = v.DevModeOnly,
                Game = game,
                Dependencies = v.Dependencies?.Select(d => new ASIDependency
                {
                    Filename = d.Filename,
                    StorageFilename = d.StorageFilename,
                    Filesize = d.Filesize,
                    Hash = d.Hash,
                    ServerAssetCompressed = d.ServerAssetCompressed,
                    CompressedFilesize = d.CompressedFilesize,
                    CompressedHash = d.CompressedHash,
                }).ToArray() ?? [],
            }).ToList();

            foreach (var v in Versions)
            {
                v.OwningMod = this;
                var sourceVersion = source.Versions.First(sv => sv.Version == v.Version);
                v.OtherGroupsToDeleteOnInstall.AddRange(sourceVersion.OtherGroupsToDeleteOnInstall);
            }
        }

        /// <summary>
        /// Versions of this ASI
        /// </summary>
        public List<ASIModVersion> Versions { get; internal set; }
        /// <summary>
        /// The unique ID of the ASI
        /// </summary>
        public int UpdateGroupId { get; internal set; }
        /// <summary>
        /// The game this ASI is applicable to
        /// </summary>
        public MEGame Game { get; internal set; }
        /// <summary>
        /// If this ASI is not to be shown in a UI, but exists to help catalog and identify if it is installed
        /// </summary>
        public bool IsHidden { get; set; }

        /// <summary>
        /// Gets the latest version of the ASI. Does not include ASIs marked as hidden.
        /// </summary>
        /// <returns></returns>
        public ASIModVersion LatestVersion => Versions.Where(x => !x.Hidden && (ASIManager.Options.BetaMode || !x.IsBeta)).MaxBy(x => x.Version);

        /// <summary>
        /// Gets latest version of ASI even if it's hidden.
        /// </summary>
        public ASIModVersion LatestVersionIncludingHidden => Versions.Where(x => ASIManager.Options.BetaMode || !x.IsBeta).MaxBy(x => x.Version);
        /// <summary>
        /// If any of the versions of this ASI match the given hash
        /// </summary>
        /// <param name="asiHash"></param>
        /// <returns></returns>
        public bool HasMatchingHash(string asiHash)
        {
            return Versions.FirstOrDefault(x => x.Hash == asiHash) != null;
        }

        /// <summary>
        /// If this ASI mod should be shown in a UI. Provide the relevant parameters to produce a result.
        /// </summary>
        /// <param name="devMode"></param>
        /// <returns></returns>
        public bool ShouldShowInUI()
        {
            if (IsHidden)
                return false;

            // List of conditions that make this return false.
            if (!ASIManager.Options.DevMode && Versions.All(x => x.DevModeOnly))
            {
                return false;
            }

            if (!ASIManager.Options.BetaMode && Versions.All(x => x.IsBeta))
            {
                return false;
            }

            // No false conditions occurred
            return true;
        }

        public override string ToString()
        {
            return $@"ASIMod - {LatestVersionIncludingHidden}";
        }
    }
}
