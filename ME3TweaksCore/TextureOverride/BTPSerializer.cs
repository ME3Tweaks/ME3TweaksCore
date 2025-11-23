using Flurl.Http.Configuration;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Objects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ME3TweaksCore.TextureOverride
{
    /// <summary>
    /// Contains information about a mip stored in BTP. We only need the offset and how big the chunk of data is at that offset.
    /// </summary>
    public class SerializedBTPMip
    {
        public ulong Offset;
        public int CompressedSize;
    }

    /// <summary>
    /// Entry in the BTP table about a TFC
    /// </summary>
    public class SerializedBTPTFCPair
    {
        public string TFCName { get; internal set; } // None if no TFC
        public Guid TFCGuid { get; internal set; } // 0 if no TFC

        /// <summary>
        /// Index into BTP table
        /// </summary>
        public int TableIndex { get; internal set; }
    }

    /// <summary>
    /// Handles serializing a TextureOverrideManifest to BTP file format
    /// </summary>
    public class BTPSerializer : IDisposable
    {
        // BTP - Binary Texture Package (Manifest)

        /// <summary>
        /// The current serialization version. This must be changed in sync with the ASI!
        /// </summary>
        public const string EXTENSION_TEXTURE_OVERRIDE_BINARY = @".btp";
        public const ushort CURRENT_VERSION = 1;
        public const uint TEXTURE_STRUCT_SIZE = 836;
        public const uint TFC_DEF_STRUCT_SIZE = 144;
        public const int TFC_NAME_MAX_SIZE = 63; // 128 bytes reserved, stored as 2byte unicode per character, so 64 chars -1 for null terminator.
        private static string MANIFEST_HEADER => "LETEXM"; // Must be ASCII

        /// <summary>
        /// The current serialization stream
        /// </summary>
        internal FileStream btpStream;

        // DEDUPLICATION ========================
        /// <summary>
        /// Maps the CRC of a mip to its offset in the CRC map.
        /// </summary>

        internal Dictionary<ulong, SerializedBTPMip> OffsetCrcMap;

        // PROGRESS ============================
        /// <summary>
        /// Current progress info object. Can be null
        /// </summary>
        internal ProgressInfo Progress;


        // TFC TABLE ============================
        /// <summary>
        /// The TFC table
        /// </summary>
        internal CaseInsensitiveDictionary<SerializedBTPTFCPair> TFCTable;

        /// <summary>
        /// Next TFC Index to assign in the table
        /// </summary>
        internal int NextTFCIndex = 0;


        // Statistics ===========================

        // Total amount of all uncompressed data added to btp
        internal long InDataSize = 0;
        // Size of BTP data segment
        internal long OutDataSize = 0;
        // Amount of data that was deduplicated in BTP
        internal long DeduplicationSavings = 0;

        internal void Serialize(TextureOverrideManifest tom, string sourceFolder, string destFile, ProgressInfo pi)
        {
            OffsetCrcMap = new();
            Progress = pi;
            TFCTable = new();

            // Add No-TFC to TFC table at index 0
            GetTFCTableIndex("None", Guid.Empty, null);

            btpStream = File.Open(destFile, FileMode.Create, FileAccess.ReadWrite);

            // Write Header.
            btpStream.WriteStringASCII(MANIFEST_HEADER); // MAGIC
            btpStream.WriteUInt16(CURRENT_VERSION);      // VERSION
            btpStream.WriteUInt32(uint.MaxValue);        // FNV-1 of GAMEDLCFOLDERNAME
            btpStream.WriteUInt32((uint)tom.Textures.Count); // Number of texture entries
            var tfcTableHeaderPos = btpStream.Position;
            btpStream.WriteInt32(0);                     // Number of TFC table entries - updated later
            btpStream.WriteUInt64(0);                    // Offset of TFC table - updated later
            btpStream.WriteZeros(4);                     // Reserved

            // TEXTURE ENTRIES =======================
            var total = tom.Textures.Count;
            var done = 0;

            pi?.Status = $"Preallocating texture override";
            pi?.OnUpdate(pi);

            // Preallocate space for texture entries
            var textureEntryTableStart = btpStream.Position;
            btpStream.WriteZeros(total * (int)TEXTURE_STRUCT_SIZE);

            // Where data for mips begins being added
            var dataSegmentStart = btpStream.Position;

            // Serialize texture entries and mip data
            try
            {
                done = 0;
                foreach (var texture in tom.Textures)
                {
                    // Seek to the texture entry position
                    btpStream.Seek(done * TEXTURE_STRUCT_SIZE, SeekOrigin.Begin);

                    // Update progress to user
                    done++;
                    if (total > 0)
                    {
                        pi?.Value = 100.0 * done / total;
                        pi?.OnUpdate(pi);
                    }

                    // Serialize the texture
                    texture.Serialize(this, sourceFolder);
#if DEBUG
                    pi?.Status = $"Serializing In: {FileSize.FormatSize(InDataSize)} Out: {FileSize.FormatSize(OutDataSize)} Dedup: -{FileSize.FormatSize(DeduplicationSavings)} {pi.Value:0.00}%";
#endif
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("hi");
            }

            // Log statistics
            var ratio = InDataSize > 0 ? (OutDataSize * 100.0 / InDataSize).ToString() : @"N/A";
            MLog.Information($@"Mip serialization complete: Stats: Input data size: {FileSize.FormatSize(InDataSize)} Output data size: {FileSize.FormatSize(OutDataSize)}, Deduplicated: {FileSize.FormatSize(DeduplicationSavings)}, compression ratio: {ratio}%");

            // Serialize the TFC table
            btpStream.SeekEnd();
            while (btpStream.Position % 16 != 0)
                btpStream.WriteByte(0); // Align to 16 bytes

            var btpTFCTableOffset = btpStream.Position;
            foreach(var tfcEntry in TFCTable.Values.OrderBy(x=> x.TableIndex))
            {
                var tfcEntryOffset = btpStream.Position;
                // Allocate space
                btpStream.WriteZeros(TFC_DEF_STRUCT_SIZE);

                // Write TFC Name
                btpStream.Seek(tfcEntryOffset, SeekOrigin.Begin);
                btpStream.WriteStringUnicodeNull(tfcEntry.TFCName);

                // Write GUID
                btpStream.Seek(tfcEntryOffset + 128, SeekOrigin.Begin);
                btpStream.WriteGuid(tfcEntry.TFCGuid);
            }

            // Now update BTP header for this table
            btpStream.Seek(tfcTableHeaderPos, SeekOrigin.Begin);
            btpStream.WriteInt32(TFCTable.Count);                     // Number of TFC table entries
            btpStream.WriteUInt64((ulong)btpTFCTableOffset);          // Offset of TFC table

            // Done
            btpStream.Close();
            MLog.Information($@"BTP serialization completed");

            //// Go in and update the offsets
            //pi.Status = "Finalizing texture override package";
            //done = 0;
            //foreach (var texture in tom.Textures)
            //{
            //    done++;
            //    if (total > 0)
            //    {
            //        pi?.Value = 100.0 * done / total;
            //        pi?.OnUpdate(pi);
            //    }
            //    try
            //    {
            //        texture.SerializeOffsets(btpStream, dataSegmentStart);
            //    }
            //    catch (Exception ex)
            //    {
            //        Debug.WriteLine("hi");
            //    }
            //}
        }

        /// <summary>
        /// Updates the building TFC table
        /// </summary>
        /// <param name="tfc">TFC name</param>
        /// <param name="guid">TFC Guid</param>
        /// <returns></returns>
        internal int GetTFCTableIndex(string tfc, Guid guid, ExportEntry export)
        {
            if (TFCTable.TryGetValue(tfc, out var tfcInfo))
            {
                if (tfcInfo.TFCGuid != guid)
                {
                    MLog.Warning($@"Detected GUID mismatch during serialization! This may to lead to problems in game. {export.InstancedFullPath} in {export.FileRef.FileNameNoExtension} has TFC guid that doesn't match previously seen on for {tfcInfo.TFCName}. Export: {guid} Previous: {tfcInfo.TFCGuid}");
                }

                return tfcInfo.TableIndex;
            }

            // Insert new one
            if (tfc.Length > TFC_NAME_MAX_SIZE)
            {
                MLog.Error($@"TFC name is too big to serialize to BTP, aborting: {tfc}");
                throw new Exception($"TFC name is too long to use for the M3 Texture Override system: {tfc}. The maximum length supported is {TFC_NAME_MAX_SIZE} characters.");
            }

            tfcInfo = new SerializedBTPTFCPair()
            {
                TableIndex = NextTFCIndex++,
                TFCGuid = guid,
                TFCName = tfc
            };

            TFCTable[tfc] = tfcInfo;
            return tfcInfo.TableIndex;
        }

        public void Dispose()
        {
            btpStream?.Dispose();
        }
    }
}
