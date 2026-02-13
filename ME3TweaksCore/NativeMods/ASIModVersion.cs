using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Targets;
using PropertyChanged;

namespace ME3TweaksCore.NativeMods
{
    /// <summary>
    /// Object containing information about a single version of an ASI mod in the ASI mod manifest
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    [DebuggerDisplay(@"{Name} - v{Version}")]
    public class ASIModVersion
    {
        /// <summary>
        /// The direct download link to the ASI
        /// </summary>
        public string DownloadLink { get; internal set; }
        /// <summary>
        /// The link to the source code of the ASI
        /// </summary>
        public string SourceCodeLink { get; internal set; }

        /// <summary>
        /// MD5 of the ASI
        /// </summary>
        public string Hash { get; internal set; }
        /// <summary>
        /// Version of this ASI mod
        /// </summary>
        public int Version { get; internal set; }
        /// <summary>
        /// Developer of the ASI
        /// </summary>
        public string Author { get; internal set; }
        /// <summary>
        /// The preferred prefix for the ASI, used when installing
        /// </summary>
        public string InstalledPrefix { get; internal set; }
        /// <summary>
        /// The displayable name of the ASI
        /// </summary>
        public string Name { get; internal set; }
        /// <summary>
        /// The game this ASI is applicable to
        /// </summary>
        public MEGame Game { get; set; }
        /// <summary>
        /// The description of this ASI
        /// </summary>
        public string Description { get; internal set; }

        /// <summary>
        /// Dependencies required by this ASI - extra dlls, essentially
        /// </summary>
        public ASIDependency[] Dependencies { get; internal set; }

        /// <summary>
        /// Description but with the newlines \\n replaced with \n.
        /// </summary>
        public string DescriptionFormatted
        {
            get
            {
                if (Description == null)
                {
                    return @"";
                }

                // Convert actual \n to newlines.
                return Description.Replace(@"\n", "\n");

            }
        }

        /// <summary>
        /// If this ASI version is marked as not being production ready - users of the library will need to filter this as appropriate
        /// </summary>
        public bool IsBeta { get; set; }
        /// <summary>
        /// If this ASI version should not be shown to user for new installs, but around for tracking
        /// </summary>
        public bool Hidden { get; set; }
        /// <summary>
        /// If this ASI version should not be shown to non-developer users
        /// </summary>
        public bool DevModeOnly { get; set; }
        /// <summary>
        /// The owning mod of this version
        /// </summary>
        public ASIMod OwningMod { get; set; }
        /// <summary>
        /// List of other groups to delete on install. This is for ASIs that have multiple differing update groups (like autoload)
        /// </summary>
        public List<int> OtherGroupsToDeleteOnInstall = new();

        /// <summary>
        /// Internal hack to generate the OtherGroupsToDeleteOnInstall variable from text.
        /// </summary>
        [SuppressPropertyChangedWarnings]
        public string _otherGroupsToDeleteOnInstallInternal
        {
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    OtherGroupsToDeleteOnInstall.Clear();
                    var split = value.Split(',');
                    foreach (var v in split)
                    {
                        if (int.TryParse(v, out var asigroupid))
                        {
                            OtherGroupsToDeleteOnInstall.Add(asigroupid);
                        }
                    }
                }
            }
        }

        public override string ToString()
        {
            return $@"{Name} - v{Version}";
        }

        internal async Task InstallDependencies(GameTarget target)
        {
            if (Dependencies == null || Dependencies.Length == 0)
                return;

            MLog.Information($@"Installing dependencies for ASI {Name} v{Version}...");
            foreach (var dependency in Dependencies)
            {
                await dependency.InstallToTarget(target);
            }
        }
    }
}
