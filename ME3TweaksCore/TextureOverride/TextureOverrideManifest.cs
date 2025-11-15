using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ME3TweaksCore.TextureOverride
{
    public class TextureOverrideManifest
    {
        [JsonProperty("textures")]
        public List<TextureOverrideTextureEntry> Textures { get; set; }
    }
}
