using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Objects;
using System.Collections.Generic;

namespace ME3TweaksCore.ME3Tweaks.ModManager.Interfaces
{
    /// <summary>
    /// Basic interface for M3 mods that allows access outside of the implementation of the mod class.
    /// </summary>
    public interface IM3Mod
    {
        // only things that need to be easily accessible are added here. There is not really a need otherwise.

        /// <summary>
        /// List of DLC required for this mod to be installed
        /// </summary>
        public List<DLCRequirement> RequiredDLC { get; set; }
    }

    /// <summary>
    /// Interface for displaying a mod in a list
    /// </summary>
    public interface IDisplayableMod
    {
        /// <summary>
        /// Name that can be displayed in the user interface
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Game this mod is for
        /// </summary>
        public MEGame Game { get; }
    }
}
