using LegendaryExplorerCore.Compression;
using LegendaryExplorerCore.Gammtek.Extensions.IO;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using LegendaryExplorerCore.Unreal.ObjectInfo;
using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Hashing;
using System.Linq;


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
        /// <summary>
        /// Minimum area of a texture before we oodle compress it in the package
        /// </summary>
        private const int COMPRESS_SIZE_MIN = 64 * 64;

        /// <summary>
        /// Maximum length of a name of a TFC in bytes.
        /// </summary>
        private const int TFCNameMaxLength = 64 * 2; // unicode
        /// <summary>
        /// Maximum length of a texture IFP in bytes.
        /// </summary>
        private const int IFPMaxLength = 256 * 2; // unicode

        // SHOULD ONLY CONTAIN TEXTURES!!
        // Examples: TO_BlueOutfit.pcc
        //           TO_A_BaseComponent.pcc
        //           TO_A_Armour_HeavyTextures.pcc
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
        /// OPTIONAL - The path of the texture in memory, in the event it has a different path than the package (due to non-seek free).
        /// </summary>
        [JsonProperty("memorypath", NullValueHandling = NullValueHandling.Ignore)]
        public string MemoryPath { get; set; }

        /// <summary>
        /// Maps a mip level's metadata to its position in the manifest stream, and the actual data position in the TFC or BTP data segment
        /// </summary>
        //[JsonIgnore]
        //private Dictionary<int, (int manifestPos, ulong manifestDataPos)> ManifestOffsetMap = new();

        /// <summary>
        /// Serializes to binary form
        /// </summary>
        /// <param name="sourceFolder">Folder used as base path for package lookups when serializing package data to BTP</param>
        public void Serialize(BTPSerializer serializer, string sourceFolder)
        {
            // Position for our texture entry
            var entryPosition = serializer.btpStream.Position;

            // Find texture package
            var packagePath = Path.Combine(sourceFolder, CompilingSourcePackage);
            if (!File.Exists(packagePath))
            {
                throw new Exception($"sourcepackage does not exist at location {packagePath}");
            }

            // Load package and find texture
            using var package = MEPackageHandler.UnsafePartialLoad(packagePath, x => x.InstancedFullPath.CaseInsensitiveEquals(TextureIFP));
            var texture = package.FindExport(TextureIFP);
            if (texture == null)
            {
                throw new Exception($"Could not find textureifp {TextureIFP} in package {packagePath}");
            }

            // Make sure it's Texture2D
            if (!texture.IsA(@"Texture2D"))
            {
                throw new Exception($"{TextureIFP} is not a texture object in {packagePath} ({texture.ClassName})");
            }

            // Read metadata about texture.
            var texBin = ObjectBinary.From<UTexture2D>(texture); // I think everything serializes from here?
            var numPopulatedMips = texBin.Mips.Count(x => x.StorageType != StorageTypes.empty);
            var numEmptyMips = texBin.Mips.Count(x => x.StorageType == StorageTypes.empty);

            var props = texture.GetProperties();
            var tfc = props.GetProp<NameProperty>(@"TextureFileCacheName");
            var tfcGuidProp = props.GetProp<StructProperty>(@"TFCFileGuid");
            var format = props.GetProp<EnumProperty>("Format"); // Default would be PF_Unknown according to enum
            var lodBias = props.GetProp<IntProperty>(@"InternalFormatLODBias")?.Value ?? 0;
            var neverStream = props.GetProp<BoolProperty>(@"NeverStream")?.Value ?? false;
            var srgb = props.GetProp<BoolProperty>(@"sRGB")?.Value ?? true; // Default is true on Texture class

            var tfcTableIndex = 0; // "None" is on index 0
            if (tfc != null && tfc.Value.Name != null && tfcGuidProp != null)
            {
                // Fetch table index, add if not there
                tfcTableIndex = serializer.GetTFCTableIndex(tfc.Value.Name, CommonStructs.GetGuid(tfcGuidProp), texture);
            }


            // WRITING TEXTURE ENTRY =========================================

            // We store as fixed-length strings
            // Write TextureIFP and then pad to fill the remaining space
            var paddedEndPos = serializer.btpStream.Position + IFPMaxLength;
            serializer.btpStream.WriteStringUnicodeNull(MemoryPath ?? TextureIFP);
            if (paddedEndPos > serializer.btpStream.Position)
            {
                // Pad to struct size
                serializer.btpStream.WriteZeros((uint)(paddedEndPos - serializer.btpStream.Position));
            }

            // Write TFC index for the table
            serializer.btpStream.WriteInt32(tfcTableIndex);

            // Write the number of populated mips.
            serializer.btpStream.WriteInt32(texBin.Mips.Count);
            int i = 0;

            // Write out populated mips data
            // The struct size is 13 mips. We go from largest mip to smallest mip
            // We could up from the smallest mip to the biggest
            for (; i < 13; i++)
            {
                if (texBin.Mips.Count == i)
                    break;

                var mip = texBin.Mips[i];

                // Oodle compress?
                bool weCompressedMip = false;

                // Mip lookup for duplicates.
                SerializedBTPMip sm = null;
                ulong crc = 0L;
                if (mip.IsLocallyStored && mip.StorageType != StorageTypes.empty)
                {
                    crc = BitConverter.ToUInt64(Crc64.Hash(mip.Mip));
                    if (serializer.OffsetCrcMap.TryGetValue(crc, out var existing))
                    {
                        // We've already serialized identical mip data...
                        sm = existing;
                        serializer.DeduplicationSavings += mip.Mip.Length; // Deduplicating mip
                        // MLog.Information($@"Dedupe occuring for crc {crc}");
                    }

                    if (sm == null && !mip.IsCompressed)
                    {
                        serializer.InDataSize += mip.Mip.Length;
                        var area = mip.SizeX * mip.SizeY;
                        if (area >= COMPRESS_SIZE_MIN) // Compress textures that would be big.
                        {
                            mip.StorageType = StorageTypes.pccOodle;
                            mip.Mip = OodleHelper.Compress(mip.Mip);
                            mip.CompressedSize = mip.Mip.Length;
                            weCompressedMip = true;
                        }
                        serializer.OutDataSize += mip.Mip.Length;
                    }
                }

                // Write sizes
                serializer.btpStream.WriteInt32(mip.UncompressedSize);
                serializer.btpStream.WriteInt32(sm?.CompressedSize ?? mip.CompressedSize); // use known compressed size if this is a dedupe

                if (mip.IsLocallyStored)
                {
                    // Locally stored puts them into the override file itself.
                    // This is non-TFC textures and lower mips of TFC textures
                    // Store where offset maps to manifest data segment position.

                    var mipDataOffsetPos = serializer.btpStream.Position;
                    serializer.btpStream.WriteUInt64(sm?.Offset ?? (ulong)serializer.btpStream.Length);// use known offset if this is a dedupe

                    if (sm == null)
                    {
                        // Write mip, this is not a dedupe
                        if (crc != 0)
                        {
                            // record crc for dedupe
                            sm = new SerializedBTPMip()
                            {
                                Offset = (ulong)serializer.btpStream.Length,
                                CompressedSize = mip.Mip.Length
                            };
                            serializer.OffsetCrcMap[crc] = sm;
                        }

                        // Write mip data and then rewind again.
                        var oldPos = serializer.btpStream.Position;
                        serializer.btpStream.Write(mip.Mip);
                        serializer.btpStream.Seek(oldPos, SeekOrigin.Begin);
                    }
                }
                else
                {
                    // It's a TFC offset
                    // Write 64bit version, it gets downcast later
                    serializer.btpStream.WriteUInt64((ulong)mip.DataOffset); // TFC offset
                }
                serializer.btpStream.WriteInt16((short)mip.SizeX);
                serializer.btpStream.WriteInt16((short)mip.SizeY);
                int mipFlag = 0;
                if (!mip.IsLocallyStored)
                {
                    // Set mip flag as stored in TFC
                    mipFlag |= 1 << 2;
                }
                if (weCompressedMip)
                {
                    // Oodle compressed flag.
                    mipFlag |= 1 << 3;
                }

                serializer.btpStream.WriteInt32(mipFlag);
            }

            // Write out remaining blanks to fill struct
            while (i < 13)
            {
                // Write out blank data.
                serializer.btpStream.WriteZeros(0x14); // null data for undefined mips
                i++;
            }

            // Format should always be set, in game defaults to Unknown if not set
            if (Enum.TryParse<TOPixelFormat>(format.Value.Instanced, out var fmt))
            {
                // Write Format Byte
                serializer.btpStream.WriteInt32((int)fmt);
            }
            else
            {
                throw new Exception($"'Format' property missing on texture {TextureIFP}");
            }

            // Internal LOD Bias
            serializer.btpStream.WriteInt32(lodBias);

            // NeverStream
            serializer.btpStream.WriteBoolInt(neverStream);

            //SRGB
            serializer.btpStream.WriteBoolInt(srgb);
        }
    }
}
