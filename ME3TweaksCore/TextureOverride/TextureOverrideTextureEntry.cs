using CommunityToolkit.HighPerformance.Helpers;
using LegendaryExplorerCore.Compression;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Packages.CloningImportingAndRelinking;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using LegendaryExplorerCore.Unreal.ObjectInfo;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace ME3TweaksCore.TextureOverride
{
    public class TextureOverrideTextureEntry
    {
        /// <summary>
        /// Minimum area of a texture before we oodle compress it in the package
        /// </summary>
        public const int BTP_COMPRESS_SIZE_MIN = 64 * 64;

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
        /// <param name="metadataPackage">Optional package for storing the texture metadata when shipping a btp only.</param>
        public void Serialize(TextureOverrideCompiler compiler, BTPTextureEntry btpEntry, Stream btpStream, IMEPackage package, IMEPackage metadataPackage = null)
        {
            // Find texture package
            //var packagePath = Path.Combine(sourceFolder, CompilingSourcePackage);
            //if (!File.Exists(packagePath))
            //{
            //    throw new Exception($"sourcepackage does not exist at location {packagePath}");
            //}

            // Load package and find texture
            // using var package = MEPackageHandler.UnsafePartialLoad(packagePath, x => x.InstancedFullPath.CaseInsensitiveEquals(TextureIFP));
            var texture = package.FindExport(TextureIFP);
            if (texture == null)
            {
                throw new Exception($"Could not find textureifp {TextureIFP} in package {package.FilePath}");
            }

            // Make sure it's Texture2D
            if (!texture.IsA(@"Texture2D"))
            {
                throw new Exception($"{TextureIFP} is not a texture object in {package.FilePath} ({texture.ClassName})");
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

            // Set values on the btpEntry
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
            compiler.serializedMipInfo.TryGetValue(TextureIFP, out var smi);

            // Write out populated mips data
            // The struct size is 13 mips. We go from largest mip to smallest mip
            // We could up from the smallest mip to the biggest
            for (int mipIndex = 0; mipIndex < texBin.Mips.Count; mipIndex++)
            {
                if (texBin.Mips.Count == mipIndex)
                    break;

                var sourceMip = texBin.Mips[mipIndex];
                var btpMip = btpEntry.Mips[mipIndex];

                // See if we already preprocessed this mip
                // in the texture compression step
                SerializedBTPMip serializedInfo = null;
                if (smi != null)
                {
                    smi.TryGetValue(mipIndex, out serializedInfo);
                }

                // Uncompressed size doesn't change from source mip
                btpMip.UncompressedSize = sourceMip.UncompressedSize;

                if (sourceMip.IsLocallyStored && !sourceMip.IsEmpty)
                {
                    // Will be prepared already so we must set matching size.
                    if (!sourceMip.IsCompressed)
                    {
                        // BTP mip will set compressed and uncompresed size equal,
                        // this will change if texture was oodle compressed
                        btpMip.CompressedSize = sourceMip.UncompressedSize;
                    }

                    // Are we a dedup mip?
                    bool isDedupMip = false;
                    if (serializedInfo == null)
                    {
                        serializedInfo = new SerializedBTPMip(sourceMip
#if DEBUG
                            ,
                            package,
                            texture,
                            mipIndex
#endif
                            );

                    }
                    else
                    {

                        if (serializedInfo.CompressedSize != sourceMip.Mip.Length)
                        {
                            serializedInfo = new SerializedBTPMip(sourceMip
#if DEBUG
                            ,
                            package,
                            texture,
                            mipIndex
#endif
                            );
                        }
                    }

                    if (serializedInfo.Offset == 0)
                    {
                        // We haven't serialized to BTP yet
                        if (serializedInfo.Crc == 0)
                        {
                            serializedInfo.Crc = BitConverter.ToUInt64(Crc64.Hash(sourceMip.Mip));
                            if (serializedInfo.Crc == 0)
                            {
                                // Mip was too small to properly crc
                                serializedInfo.Crc = ulong.MaxValue;
                            }
                            else
                            {
                                // Try global dedup map to see if we already serialized this mip then.
                                if (compiler.DedupCrcMap.TryGetValue(serializedInfo.Crc, out var test))
                                {
                                    // This is a duplicate mip
                                    isDedupMip = true;
                                    serializedInfo = test;
                                }
                            }
                        }

                        if (!isDedupMip)
                        {
                            // we're going to write to end of the btp stream
                            serializedInfo.Offset = (ulong)btpStream.Length;
                        }
                    }
                    else
                    {
                        // offset was previously set; we're already serialized into btp
                        isDedupMip = true;
                    }


                    if (serializedInfo.Crc == 0)
                        Debugger.Break();

                    // record crc for future dedupe, but it must be 4x4 or larger since tiny textures have crc collisions
                    if (sourceMip.SizeX > 4 || sourceMip.SizeY > 4)
                    {
                        compiler.DedupCrcMap[serializedInfo.Crc] = serializedInfo;
                    }

                    btpMip.CompressedSize = serializedInfo.CompressedSize;

                    // Serialize the mip data (if unique) and update the entry.
                    var data = isDedupMip ? null : sourceMip.Mip;
                    // Debug.WriteLine($@"Serializing {btpEntry.OverridePath} mip {mipIndex} so: {offset}, cs: {compressedSize}, data: {data?.Length}");
                    btpMip.SerializeData(btpStream,
                        (long)serializedInfo.Offset, // Dedup offset or end of stream
                        serializedInfo.CompressedSize, // Dedup compressed size or our mip's size
                        data // Only pass mip data if not a dedupe
                    );
                }
                else
                {
                    // It's a TFC offset
                    // Write 64bit version, it gets downcast later
                    btpMip.CompressedOffset = sourceMip.DataOffset;
                }

                btpMip.CompressedSize = serializedInfo?.CompressedSize ?? sourceMip.CompressedSize;
                btpMip.Width = (short)sourceMip.SizeX;
                btpMip.Height = (short)sourceMip.SizeY;
                btpMip.Flags = 0;
                if (!sourceMip.IsLocallyStored)
                {
                    // Set mip flag as stored in TFC
                    btpMip.Flags |= BTPMipFlags.External;
                }
                if (serializedInfo?.OodleCompressed == true)
                {
                    // Custom oodle compressed flag
                    // for the ASI.
                    btpMip.Flags |= BTPMipFlags.OodleCompressed;
                }
            }


            if (metadataPackage != null)
            {
                // Write out metadata - texture export, stubbed
                texture.WriteBinary([]); // Hope this works...
                EntryExporter.ExportExportToPackage(texture, package, out _);
            }
        }
    }
}
