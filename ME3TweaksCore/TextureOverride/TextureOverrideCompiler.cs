using LegendaryExplorerCore.Helpers;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Objects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

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
        /// Converts a texture override manifest and its supporting data into a Binary Texture Package
        /// </summary>
        /// <param name="tom">Manifest to convert</param>
        /// <param name="sourceFolder">Source folder that contains the packages. This is the DLC cooked directory.</param>
        /// <param name="btpStream">The destination stream to write to. This should be the start of a stream.</param>
        /// <param name="pi">Progress interop</param>
        public void BuildBTPFromTO(TextureOverrideManifest tom, string sourceFolder, Stream btpStream, string dlcName, ProgressInfo pi)
        {
            // Setup variables!
            DLCName = dlcName;
            Progress = pi;

            //debug
            tom.Textures = tom.Textures.Where(x => x.TextureIFP == "BIOG_V_Env_Hologram_Z.Textures.Holomod_07_Tex").ToList();

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
            var textureEntryTableStart = btpStream.Position;
            BTP.TextureOverrides = new(total);
            for(var i = 0; i < total; i++)
            {
                BTP.TextureOverrides.Add(new BTPTextureEntry(BTP, null));
            }

            foreach(var to in BTP.TextureOverrides)
            {
                // Write out blank placeholders for now so the data is allocated in the stream
                done++;
                to.Serialize(btpStream);
                BTP.Header.TextureCount++;
            }

            // Where data for mips begins being added
            var dataSegmentStart = btpStream.Position;

            // Serialize texture entries and mip data
            try
            {
                done = 0;
                for(done = 0; done < BTP.TextureOverrides.Count; done++)
                {
                    // Update progress to user
                    if (total > 0)
                    {
                        pi?.Value = (100.0 * done + 1) / total;
                        pi?.OnUpdate(pi);
                    }

                    var btpEntry = BTP.TextureOverrides[done];
                    var texture = tom.Textures[done];

                    // Serialize the texture
                    texture.Serialize(this, btpEntry, btpStream, sourceFolder);
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

            BTP.FinalSerialize(btpStream);

            // Done

#if DEBUG
            btpStream.SeekBegin();
            var verifyBTP = new BinaryTexturePackage(btpStream, true);
#endif

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
    }
}
