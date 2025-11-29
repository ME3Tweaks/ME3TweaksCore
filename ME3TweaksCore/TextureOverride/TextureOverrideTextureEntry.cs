using LegendaryExplorerCore.Compression;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using LegendaryExplorerCore.Unreal.ObjectInfo;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Hashing;
using System.Linq;

namespace ME3TweaksCore.TextureOverride
{
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

        // SERIALIZATION =========================================================

        /// <summary>
        /// Serializes this texture to the BTP and BTPStream.
        /// </summary>
        /// <param name="compiler">The compiler that holds stats and other transient compile-time info</param>
        /// <param name="btpEntry">The BTP entry for this texture that we will be populating data into</param>
        /// <param name="btpStream">The stream we are serializing mip data to</param>
        /// <param name="sourceFolder">Folder used as base path for package lookups when serializing package data to BTP</param>
        public void Serialize(TextureOverrideCompiler compiler, BTPTextureEntry btpEntry, Stream btpStream, string sourceFolder)
        {
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

            // Set vlues on the btpEntry
            btpEntry.OverridePath = MemoryPath ?? TextureIFP;
            btpEntry.PopulatedMipCount = (byte)numPopulatedMips;
            btpEntry.InternalFormatLODBias = lodBias;
            btpEntry.NeverStream = neverStream;
            btpEntry.bSRGB = srgb;
            // Format should always be set, in game defaults to Unknown if not set
            // this is caught in serialization in debug mode.
            if (Enum.TryParse<BTPPixelFormat>(format.Value.Instanced, out var fmt))
            {
                btpEntry.Format = fmt;
            }

            // Default
            btpEntry.TFC = btpEntry.Owner.TFCTable.GetTFC(null);
            if (tfc != null && tfc.Value.Name != null && tfcGuidProp != null)
            {
                // Fetch table index, add if not there
                btpEntry.TFC = btpEntry.Owner.TFCTable.GetOrAddTFC(tfc.Value.Name, CommonStructs.GetGuid(tfcGuidProp), texture);
            }



            // WRITING TEXTURE MIPS TO BTP STREAM =========================================
            int mipIndex = 0;

            // Write out populated mips data
            // The struct size is 13 mips. We go from largest mip to smallest mip
            // We could up from the smallest mip to the biggest
            for (; mipIndex < 13; mipIndex++)
            {
                if (texBin.Mips.Count == mipIndex)
                    break;

                var sourceMip = texBin.Mips[mipIndex];
                var btpMip = btpEntry.Mips[mipIndex];

                btpMip.UncompressedSize = sourceMip.UncompressedSize;

                // Flag for if we compressed this mip into the BTP
                bool weCompressedMip = false;

                SerializedBTPMip dedupMip = null;
                if (sourceMip.IsLocallyStored && sourceMip.StorageType != StorageTypes.empty)
                {
                    // Mip lookup for duplicates.
                    ulong crc = BitConverter.ToUInt64(Crc64.Hash(sourceMip.Mip));
                    if (compiler.DedupCrcMap.TryGetValue(crc, out var existing))
                    {
                        // We've already serialized identical mip data...
                        dedupMip = existing;
                        compiler.DeduplicationSavings += sourceMip.Mip.Length; // Deduplicating mip
                        // MLog.Information($@"Dedupe occuring for crc {crc}");
                    }

                    if (!sourceMip.IsCompressed)
                    {
                        // BTP mip will set compressed and uncompresed size equal,
                        // this will change if texture gets compressed
                        btpMip.CompressedSize = sourceMip.UncompressedSize;
                    }

                    // Compress textures that would be big for space savings
                    // texture must have > 64x64 size and not already compressed
                    if (dedupMip == null && !sourceMip.IsCompressed)
                    {
                        compiler.InDataSize += sourceMip.Mip.Length; // Stats
                        var area = sourceMip.SizeX * sourceMip.SizeY;
                        if (area >= COMPRESS_SIZE_MIN)
                        {
                            sourceMip.StorageType = StorageTypes.pccOodle;
                            sourceMip.Mip = OodleHelper.Compress(sourceMip.Mip);
                            sourceMip.CompressedSize = sourceMip.Mip.Length;
                            weCompressedMip = true;
                        }
                        compiler.OutDataSize += sourceMip.Mip.Length; // Stats
                    }

                    // Must cache here 
                    var isDuplicateMip = dedupMip != null;

                    // Update dedup map before writing 
                    // if we computed a crc for it and
                    // it's not a dedupe already
                    if (crc != 0 && !isDuplicateMip)
                    {
                        // record crc for future dedupe
                        btpStream.SeekEnd(); // Make sure we're at the end where we're going to append
                        dedupMip = new SerializedBTPMip()
                        {
                            Offset = (ulong)btpStream.Length,
                            CompressedSize = sourceMip.Mip.Length,
                            OodleCompressed = weCompressedMip
                        };
                        compiler.DedupCrcMap[crc] = dedupMip;
                    }

                    // Serialize the mip data (if unique) and update the entry.
                    var offset = (long?)dedupMip?.Offset ?? btpStream.Length;
                    var compressedSize = (int?)(dedupMip?.CompressedSize) ?? sourceMip.CompressedSize;
                    var data = isDuplicateMip ? null : sourceMip.Mip;
                    Debug.WriteLine($@"Serializing {btpEntry.OverridePath} mip {mipIndex} so: {offset}, cs: {compressedSize}, data: {data?.Length}");
                    btpMip.SerializeData(btpStream,
                        offset, // Dedup offset or end of stream
                        compressedSize, // Dedup compressed size or our mip's size
                        data // Only pass mip data if not a dedupe
                    );
                }
                else
                {
                    // It's a TFC offset
                    // Write 64bit version, it gets downcast later
                    btpMip.CompressedOffset = sourceMip.DataOffset;
                }

                btpMip.CompressedSize = dedupMip?.CompressedSize ?? sourceMip.CompressedSize;
                btpMip.Width = (short)sourceMip.SizeX;
                btpMip.Height = (short)sourceMip.SizeY;
                btpMip.Flags = 0;
                if (!sourceMip.IsLocallyStored)
                {
                    // Set mip flag as stored in TFC
                    btpMip.Flags |= BTPMipFlags.External;
                }
                if (weCompressedMip || dedupMip?.OodleCompressed == true)
                {
                    // Custom oodle compressed flag
                    // for the ASI.
                    btpMip.Flags |= BTPMipFlags.OodleCompressed;
                }
            }
        }
    }
}
