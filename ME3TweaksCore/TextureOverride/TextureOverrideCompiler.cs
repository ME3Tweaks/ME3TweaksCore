using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LegendaryExplorerCore.Helpers;
using Newtonsoft.Json;

namespace ME3TweaksCore.TextureOverride
{
    public static class TextureOverrideCompiler
    {
        public const string EXTENSION_TEXTURE_OVERRIDE_BINARY = @".letexm";
        public const ushort CURRENT_VERSION = 1;

        private static string MANIFEST_HEADER => "LETEXM"; // Must be ASCII

        public static void CompileLETEXM(string inputFile)
        {
            var manifest = JsonConvert.DeserializeObject<TextureOverrideManifest>(File.ReadAllText(inputFile));
            var sourceFolder = Directory.GetParent(inputFile).FullName;

            using var outStream = File.Open(Path.Combine(sourceFolder, $"{Path.GetFileNameWithoutExtension(inputFile)}{EXTENSION_TEXTURE_OVERRIDE_BINARY}"), FileMode.Create, FileAccess.ReadWrite);
            using var dataSegment = new MemoryStream();
            // Write Header.
            outStream.WriteStringASCII(MANIFEST_HEADER);
            outStream.WriteUInt16(CURRENT_VERSION);
            outStream.WriteUInt32(uint.MaxValue); // Not entirely sure what to put here.
            outStream.WriteInt32(manifest.Textures.Count);
            outStream.WriteZeros(16); // Reserved

            // Write entries.
            foreach (var texture in manifest.Textures)
            {
                texture.Serialize(sourceFolder, outStream, dataSegment);
            }

            outStream.SeekEnd();
            var dataSegmentStart = (int) outStream.Position;
            dataSegment.SeekBegin();
            dataSegment.CopyTo(outStream); // Append

            foreach (var texture in manifest.Textures)
            {
                texture.SerializeOffsets(outStream, dataSegmentStart);
            }
        }
    }
}
