using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using LegendaryExplorerCore.Unreal.Classes;
using LegendaryExplorerCore.Unreal.ObjectInfo;
using Newtonsoft.Json;

namespace ME3TweaksCore.TextureOverride
{
    /// <summary>
    /// Pixel Format Num for serialization
    /// </summary>
    public enum TOPixelFormat
    {
        PF_Unknown = 0,
        PF_A32B32G32R32F = 1,
        PF_A8R8G8B8 = 2,
        PF_G8 = 3,
        PF_G16 = 4,
        PF_DXT1 = 5,
        PF_DXT3 = 6,
        PF_DXT5 = 7,
        PF_UYVY = 8,
        PF_FloatRGB = 9,
        PF_FloatRGBA = 10,
        PF_DepthStencil = 11,
        PF_ShadowDepth = 12,
        PF_FilteredShadowDepth = 13,
        PF_R32F = 14,
        PF_G16R16 = 15,
        PF_G16R16F = 16,
        PF_G16R16F_FILTER = 17,
        PF_G32R32F = 18,
        PF_A2B10G10R10 = 19,
        PF_A16B16G16R16_UNORM = 20,
        PF_D24 = 21,
        PF_R16F = 22,
        PF_R16F_FILTER = 23,
        PF_BC5 = 24,
        PF_V8U8 = 25,
        PF_A1 = 26,
        PF_NormalMap_LQ = 27,
        PF_NormalMap_HQ = 28,
        PF_A16B16G16R16_FLOAT = 29,
        PF_A16B16G16R16_SNORM = 30,
        PF_FloatR11G11B10 = 31,
        PF_A4R4G4B4 = 32,
        PF_R5G6B5 = 33,
        PF_G8R8 = 34,
        PF_R8_UNORM = 35,
        PF_R8_UINT = 36,
        PF_R8_SINT = 37,
        PF_R16_FLOAT = 38,
        PF_R16_UNORM = 39,
        PF_R16_UINT = 40,
        PF_R16_SINT = 41,
        PF_R8G8_UNORM = 42,
        PF_R8G8_UINT = 43,
        PF_R8G8_SINT = 44,
        PF_R16G16_FLOAT = 45,
        PF_R16G16_UNORM = 46,
        PF_R16G16_UINT = 47,
        PF_R16G16_SINT = 48,
        PF_R32_FLOAT = 49,
        PF_R32_UINT = 50,
        PF_R32_SINT = 51,
        PF_A8 = 52,
        PF_BC7 = 53,
        EPixelFormat_MAX = 54
    };

    public class TextureOverrideTextureEntry
    {
        private const int TFCNameMaxLength = 64 * 2; // unicode
        private const int IFPMaxLength = 256 * 2; // unicode

        /// <summary>
        /// Name of package to find this texture in, in the current folder. Can be relative.
        /// </summary>
        [JsonProperty("sourcepackage")]
        public string CompilingSourcePackage { get; set; }

        /// <summary>
        /// Instanced full path of the texture in the source package
        /// </summary>
        [JsonProperty("textureifp")]
        public string TextureIFP { get; set; }

        /// <summary>
        /// Maps a mip level to 
        /// </summary>
        [JsonIgnore]
        private Dictionary<int, (int manifestPos, int manifestDataPos)> ManifestOffsetMap = new();

        /// <summary>
        /// Serializes to binary form
        /// </summary>
        /// <param name="stream"></param>
        public void Serialize(string sourceFolder, Stream stream, Stream manifestStoredStream)
        {
            var packagePath = Path.Combine(sourceFolder, CompilingSourcePackage);
            if (!File.Exists(packagePath))
            {
                throw new Exception($"sourcepackage does not exist at location {packagePath}");
            }
            using var package = MEPackageHandler.UnsafePartialLoad(packagePath, x => x.InstancedFullPath == TextureIFP);
            var texture = package.FindExport(TextureIFP);
            if (texture == null)
                throw new Exception($"Could not find textureifp {TextureIFP} in package {packagePath}");

            if (!texture.IsA("Texture2D"))
                throw new Exception($"{TextureIFP} is not a texture object in {packagePath} ({texture.ClassName})");

            var texBin = ObjectBinary.From<UTexture2D>(texture); // I think everything serializes from here?
            var tfc = texture.GetProperty<NameProperty>(@"TextureFileCacheName");
            var tfcGuidProp = texture.GetProperty<StructProperty>(@"TFCFileGuid");

            var paddedEndPos = stream.Position + IFPMaxLength;
            stream.WriteStringUnicodeNull(TextureIFP);
            if (paddedEndPos > stream.Position) // Pad to struct size
                stream.WriteZeros((uint)(paddedEndPos - stream.Position));

            paddedEndPos = stream.Position + TFCNameMaxLength;
            stream.WriteStringUnicodeNull(tfc?.Value.Instanced ?? @""); // Empty string = No TFC, package stored.
            if (paddedEndPos > stream.Position) // Pad to struct size
                stream.WriteZeros((uint)(paddedEndPos - stream.Position));
            // Should we check the position is not wrong here? Like too long of IFP


            stream.WriteGuid(tfcGuidProp != null ? CommonStructs.GetGuid(tfcGuidProp) : Guid.Empty); // Zero guid = No TFC, package stored.
            stream.WriteInt32(texBin.Mips.Count);
            int i = 0;
            for(; i < 13; i++)
            {
                if (texBin.Mips.Count == i)
                    break;

                var mip = texBin.Mips[i];
                stream.WriteInt32(mip.UncompressedSize);
                stream.WriteInt32(mip.CompressedSize);
                if (mip.IsLocallyStored)
                {
                    // Store where offset maps to manifest data segment position.
                    ManifestOffsetMap[i] = ((int)stream.Position, (int)manifestStoredStream.Position);
                    stream.WriteInt32(0); // Will be updated later
                    manifestStoredStream.Write(mip.Mip);
                }
                else
                {
                    stream.WriteInt32(mip.DataOffset); // TFC offset
                }
                stream.WriteInt16((short)mip.SizeX);
                stream.WriteInt16((short)mip.SizeY);
                int mipFlag = 0;
                if (!mip.IsLocallyStored)
                {
                    mipFlag |=  1 << 2;
                }
                stream.WriteInt32(mipFlag);
            }

            while (i < 13)
            {
                // Write out blank data.
                stream.WriteZeros(0x14); // null data for undefined mips
                i++;
            }

            // Format should always be set, deafults to Unknown if not set in game
            var format = texture.GetProperty<EnumProperty>("Format");
            if (Enum.TryParse<TOPixelFormat>(format.Value.Instanced, out var fmt))
            {
                stream.WriteInt32((int)fmt);
            }
            else
            {
                throw new Exception("Could not parse format!");
            }
        }

        public void SerializeOffsets(Stream stream, int dataOffset)
        {
            foreach (var entry in ManifestOffsetMap)
            {
                stream.Seek(entry.Value.manifestPos, SeekOrigin.Begin);
                stream.WriteInt32(entry.Value.manifestDataPos + dataOffset);
            }
        }
    }
}
