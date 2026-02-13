using LegendaryExplorerCore.Packages;
using Newtonsoft.Json;

namespace ME3TweaksCore.Services.Symbol
{
    /// <summary>
    /// Describes symbol information for a game executable
    /// </summary>
    public class SymbolRecord
    {
        /// <summary>
        /// The game this record applies to
        /// </summary>
        [JsonProperty(@"game")]
        public MEGame Game { get; set; }

        /// <summary>
        /// MD5 hash of the target game executable
        /// </summary>
        [JsonProperty(@"gamehash")]
        public string GameHash { get; set; }

        /// <summary>
        /// MD5 hash of the current PDB file for this game
        /// </summary>
        [JsonProperty(@"pdbhash")]
        public string PdbHash { get; set; }
    }
}
