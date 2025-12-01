using LegendaryExplorerCore.Compression;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Objects;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ME3TweaksCore.TextureOverride
{
    public class TextureOverrideCompiler
    {
        internal string DLCName;

        // DEDUPLICATION ========================
        /// <summary>
        /// Maps the CRC of a mip to its offset in the CRC map.
        /// </summary>
        internal Dictionary<ulong, SerializedBTPMip> DedupCrcMap = new();


        // PROGRESS ============================
        /// <summary>
        /// Current progress info object. Can be null
        /// </summary>
        internal ProgressInfo Progress;

        // Statistics ===========================
        // SERIALIZATION ONLY
        // Total amount of all uncompressed data added to btp
        internal long InDataSize = 0;
        // Size of BTP data segment
        internal long OutDataSize = 0;
        // Amount of data that was deduplicated in BTP
        internal long DeduplicationSavings = 0;


        /// <summary>
        /// Contains information about IFP -> serialized information. Maps IFP to dictionary of mip indices that were serialized to the BTP data block.
        /// </summary>
        internal ConcurrentDictionary<string, Dictionary<int, SerializedBTPMip>> serializedMipInfo = new();

        /// <summary>
        /// Converts a texture override manifest and its supporting data into a Binary Texture Package
        /// </summary>
        /// <param name="tom">Manifest to convert</param>
        /// <param name="sourceFolder">Source folder that contains the packages. This is the DLC cooked directory.</param>
        /// <param name="btpStream">The destination stream to write to. This should be the start of a stream.</param>
        /// <param name="pi">Progress interop</param>
        public void BuildBTPFromTO(TextureOverrideManifest tom, string sourceFolder, Stream btpStream, string dlcName, ProgressInfo pi, IMEPackage metadataPackage = null)
        {
            MLog.Information($@"BTP build: Initializing");

            // Setup variables!
            DLCName = dlcName;
            Progress = pi;

            // DEBUG ONLY
            // Filter for testing
            // tom.Textures = tom.Textures.Where(x => x.TextureIFP.Contains(@"BIOG_HMM_HED_PROMorph.Eye.EYE_Diff", StringComparison.OrdinalIgnoreCase)).ToList(); // == "BIOG_Humanoid_MASTER_MTR_R.Eye.HMM_EYE_MASTER_Diffuse").ToList();

            // Prepare for performance by serializing in-order of source package
            tom.Textures = tom.Textures.OrderBy(t => t.CompilingSourcePackage).ToList();

            MLog.Information($@"BTP build: Building for {dlcName} with {tom.Textures.Count} overrides");

            // Start serialization
            // We use BTP object only for transient data storage,
            // it does not handle actual serialization as we would have to
            // load mips into it, which could use huge amounts of memory.
            // This is effectively a lazy serializer
            var BTP = new BinaryTexturePackage(null);

            // Add No-TFC to TFC table at index 0
            BTP.TFCTable.GetTFCTableIndex("None", Guid.Empty, null);

            // Setup header (first pass)
            var fnvInput = $@"{tom.Game}{DLCName}";
            BTP.Header.TargetHash = FNV1.Compute(Encoding.Unicode.GetBytes(fnvInput));
            BTP.Header.Serialize(btpStream);

            // TEXTURE OVERRIDE SERIALIZATION =======================
            var total = tom.Textures.Count;
            var done = 0;

            pi?.Value = 0;
            pi?.Status = $"Building texture override package";
            pi?.OnUpdate(pi);

            // Preallocate space for texture entries
            MLog.Information($@"BTP build: Preallocating texture entry table");

            var textureEntryTableStart = btpStream.Position;
            BTP.TextureOverrides = new(total);
            for (var i = 0; i < total; i++)
            {
                BTP.TextureOverrides.Add(new BTPTextureEntry(BTP, null));
            }

            foreach (var to in BTP.TextureOverrides)
            {
                // Write out blank placeholders for now so the data is allocated in the stream
                done++;
                to.Serialize(btpStream);
                BTP.Header.TextureCount++;
            }

            // Where data for mips begins being added
            var dataSegmentStart = btpStream.Position;

            // Serialize texture entries and mip data
            IMEPackage currentSourcePackage = null;
            done = 0;
            MLog.Information($@"BTP build: Beginning texture override serializations");

            for (done = 0; done < BTP.TextureOverrides.Count; done++)
            {
                // Update progress to user
                if (total > 0)
                {
                    pi?.Value = (100.0 * done + 1) / total;
                    pi?.OnUpdate(pi);
                }

                var btpEntry = BTP.TextureOverrides[done];
                var texture = tom.Textures[done];

                if (currentSourcePackage == null || !currentSourcePackage.FilePath.EndsWith(texture.CompilingSourcePackage, StringComparison.OrdinalIgnoreCase))
                {
                    // Load new source package
                    var newPath = Path.Combine(sourceFolder, texture.CompilingSourcePackage);
                    MLog.Information($@"BTP build: Switching to new source package {newPath}");

                    currentSourcePackage = MEPackageHandler.UnsafePartialLoad(newPath, x =>
                    {
                        var ifp = x.MemoryFullPath;
                        // Load textures that are in the manifest only
                        return tom.Textures.Any(entry =>
                        {
                            if (entry.MemoryPath != null)
                            {
                                return entry.MemoryPath.CaseInsensitiveEquals(ifp);
                            }
                            return entry.TextureIFP.CaseInsensitiveEquals(ifp);
                        });
                    });

                    // Dump old package.
                    GC.Collect();

                    // Now compress textures in the package in parallel to speed things along.
                    MLog.Information($@"BTP build: Compressing package stored mips in parallel");
                    PrepareTextureCompression(currentSourcePackage);
                    MLog.Information($@"BTP build: Serializing {currentSourcePackage.Exports.Count(x=>x.IsDataLoaded())} package stored texture overrides");
                }

                // Serialize the texture
                texture.Serialize(this, btpEntry, btpStream, currentSourcePackage, metadataPackage);
                currentSourcePackage.FindExport(btpEntry.OverridePath)?.Data = Array.Empty<byte>(); // This will allow deallocation of memory as we progress

#if DEBUG
                // pi?.Status = $"Serializing In: {FileSize.FormatSize(InDataSize)} Out: {FileSize.FormatSize(OutDataSize)} Dedup: -{FileSize.FormatSize(DeduplicationSavings)} {pi.Value:0.00}%";
#endif
            }

            // Lose the reference
            currentSourcePackage = null;

            // Log statistics
            var ratio = InDataSize > 0 ? (OutDataSize * 100.0 / InDataSize).ToString() : @"N/A";
            MLog.Information($@"BTP build: Mip data serialization complete: Stats: Input data size: {FileSize.FormatSize(InDataSize)} Output data size: {FileSize.FormatSize(OutDataSize)}, Deduplicated: {FileSize.FormatSize(DeduplicationSavings)}, compression ratio: {ratio:F2}%");
            MLog.Information($@"BTP build: Performing final serialization");
            BTP.FinalSerialize(btpStream);
            MLog.Information($@"BTP build: Completed");

            // Done
#if DEBUG
            MLog.Information($@"BTP build: Beginning deserialization verification");
            btpStream.SeekBegin();

            pi?.Status = @"Verifying BTP";
            pi?.Value = 0;
            pi?.OnUpdate(pi);
            var verifyBTP = new BinaryTexturePackage(btpStream, true, pi);
#endif

            btpStream.Close();
        }

        /// <summary>
        /// Sets information about how to serialize mip data
        /// </summary>
        /// <param name="instancedFullPath"></param>
        /// <param name="i">Mip index from the source export</param>
        /// <param name="serializationInfo"></param>
        private void SetSerializationInfo(string instancedFullPath, int i, SerializedBTPMip serializationInfo)
        {
            if (!serializedMipInfo.TryGetValue(instancedFullPath, out var existingMap))
            {
                existingMap = new(6);
                serializedMipInfo[instancedFullPath] = existingMap;
            }

            existingMap[i] = serializationInfo;
        }

        private void PrepareTextureCompression(IMEPackage currentSourcePackage)
        {
            // For every loaded object...
            var loadedItems = currentSourcePackage.Exports.Where(x => x.IsDataLoaded());
            Parallel.ForEach(loadedItems, new ParallelOptions() { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 2, 1, 6) }, texture =>
            {
                var compressedAny = false;
                var texBin = ObjectBinary.From<UTexture2D>(texture);
                for (int i = 0; i < texBin.Mips.Count; i++)
                {
                    var sourceMip = texBin.Mips[i];
                    if (!sourceMip.IsLocallyStored || sourceMip.StorageType == StorageTypes.empty)
                        continue; // Nothing to see here

                    // Mip lookup for duplicates.
                    ulong crc = BitConverter.ToUInt64(Crc64.Hash(sourceMip.Mip));

                    // Small textures can have a 0 crc
                    if (crc != 0)
                    {
                        if (DedupCrcMap.TryGetValue(crc, out var existing))
                        {
                            // We've already serialized identical mip data...
                            DeduplicationSavings += sourceMip.Mip.Length; // Deduplicating mip
                            SetSerializationInfo(texture.InstancedFullPath, i, existing);
                            continue; // nothing left to do
                        }
                    }
                    else
                    {
                        crc = ulong.MaxValue;
                    }

                    var serializationInfo = new SerializedBTPMip();
                    serializationInfo.Crc = crc;
                    serializationInfo.CompressedSize = sourceMip.CompressedSize; // Copy original value

                    // Compress textures that would be big for space savings
                    // texture must have >= 64x64 size and not already compressed
                    if (!sourceMip.IsCompressed)
                    {
                        InDataSize += sourceMip.Mip.Length; // Stats
                        var area = sourceMip.SizeX * sourceMip.SizeY;
                        if (area >= TextureOverrideTextureEntry.BTP_COMPRESS_SIZE_MIN)
                        {
                            sourceMip.StorageType = StorageTypes.pccOodle;
                            sourceMip.Mip = OodleHelper.Compress(sourceMip.Mip); // compress mip and store it back
                            serializationInfo.CompressedSize = sourceMip.CompressedSize = sourceMip.Mip.Length; // Now set the new compressed size so we can use it later
                            serializationInfo.OodleCompressed = true; // Flag that makes it think its compressed.
#if DEBUG
                            serializationInfo.DebugSource = $@"{currentSourcePackage.FilePath} {texture.InstancedFullPath} mip {i}";
#endif
                            SetSerializationInfo(texture.InstancedFullPath, i, serializationInfo); // cache the serialization info for compression
                            compressedAny = true;
                        }
                        OutDataSize += sourceMip.Mip.Length; // Stats
                    }

                }

                if (compressedAny)
                {
                    texture.WriteBinary(texBin);
                }
            });
        }
    }
}
