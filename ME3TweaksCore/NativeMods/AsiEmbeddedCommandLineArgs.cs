using System.Collections.Generic;
using Newtonsoft.Json;

namespace ME3TweaksCore.NativeMods
{
    /// <summary>
    /// Deserialization target for the JSON payload ASI mods embed in a "ME3TWEAKS_CMDLINE_ARGS" RCDATA
    /// resource to self-document the command line switches they support.
    /// </summary>
    public class AsiEmbeddedCommandLineArgs
    {
        [JsonProperty(@"schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty(@"commands")]
        public List<AsiCommandLineSwitch> Commands { get; set; } = new();
    }

    /// <summary>
    /// A single command line switch an ASI mod supports, as embedded by the ASI itself.
    /// </summary>
    public class AsiCommandLineSwitch
    {
        [JsonProperty(@"name")]
        public string Name { get; set; }

        [JsonProperty(@"description")]
        public string Description { get; set; }
    }
}
