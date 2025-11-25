using LegendaryExplorerCore.Compression;
using LegendaryExplorerCore.Gammtek.IO;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
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
    /// <summary>
    /// Contains information about a mip stored in BTP. We only need the offset and how big the chunk of data is at that offset. This is used for deduplication.
    /// </summary>
    public class SerializedBTPMip
    {
        public ulong Offset;
        public int CompressedSize;
    }

    /// <summary>
    /// Class for serializing FNV1 hashes. Very basic.
    /// </summary>
    class FNV1
    {
        private const uint OFFSET = 0x811c9dc5;
        private const uint PRIME = 0x01000193;

        /// <summary>
        /// Computes the FNV1 hash of the given byte array and returns it.
        /// </summary>
        /// <param name="array"></param>
        /// <returns></returns>
        public static uint Compute(byte[] array)
        {
            uint value = OFFSET;

            foreach (var b in array)
            {
                value = (value * PRIME) ^ b;
            }

            return value;
        }
    }

    /// <summary>
    /// Pixel Format for serialization in BTP
    /// </summary>
    public enum BTPPixelFormat
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

    /// <summary>
    /// The flags for a mip about how it should be handled in the ASI
    /// </summary>
    [Flags]
    public enum BTPMipFlags
    {
        /// <summary>
        /// Mip should not be modified
        /// </summary>
        Original = 1 << 1,
        /// <summary>
        /// Mip is located in an external texture file cache
        /// </summary>
        External = 1 << 2,
        /// <summary>
        /// Mip was compressed with Oodle during serialization and must be decompressed to access
        /// the mip data
        /// </summary>
        OodleCompressed = 1 << 3,
    }

    /// <summary>
    /// Class for handling Binary Texture Package (BTP) files, which are used by the Texture Override ASI.
    /// </summary>
    public class BinaryTexturePackage
    {
        // BTP - Binary Texture Package (Manifest)

        /// <summary>
        /// The current serialization version. This must be changed in sync with the ASI!
        /// </summary>
        public const string EXTENSION_TEXTURE_OVERRIDE_BINARY = @".btp";

        /// <summary>
        /// The current version when serializing
        /// </summary>
        public const ushort CURRENT_VERSION = 2;

        /// <summary>
        /// Flags to indicate that mips should be loaded on deserialization
        /// </summary>
        public bool LoadMips { get; internal set; }

        /// <summary>
        /// If we are performing final serialization
        /// </summary>
        public bool IsFinalSerialize { get; internal set; }


        // LAYOUT ==================
        internal BTPHeader Header;
        internal List<BTPTextureEntry> TextureOverrides;
        // Mips are referenced by TextureOverrides
        internal BTPTFCTable TFCTable;

        /// <summary>
        /// Constructs a new Binary Texture Package object, optionally deserializing from the given stream, if any
        /// </summary>
        /// <param name="btpStream"></param>
        /// <param name="loadMips">If mip data should be also loaded. This can use a lot of memory.</param>
        public BinaryTexturePackage(Stream btpStream, bool loadMips = false)
        {
            if (btpStream != null)
            {
                LoadMips = loadMips;
                Deserialize(btpStream);
            }
            else
            {
                // Setup blank object
                Header = new BTPHeader(this, null);
                TextureOverrides = new();
                TFCTable = new BTPTFCTable(this, null);
            }
        }

        /// <summary>
        /// Serializes this entire BTP object to the given stream. The given stream should already have been serialized,
        /// as this will write the TFC table at the end and serialize all table data.
        /// </summary>
        /// <param name="btpStream">The existing stream to finalize</param>
        public void FinalSerialize(Stream btpStream)
        {
            IsFinalSerialize = true;

            // Append TFC table, set offset in header
            TFCTable.Serialize(btpStream);
            Header.TFCTableOffset = TFCTable.TableOffset;

            // Now rewrite everything that isn't data
            btpStream.Seek(0, SeekOrigin.Begin);
            Header.Serialize(btpStream);
            foreach (var tex in TextureOverrides)
            {
                tex.Serialize(btpStream);
            }

            IsFinalSerialize = false;
            // And we should be done now
        }


        /// <summary>
        /// Generates BTP object from BTP stream. Without source data, this isn't that useful, but is good for verification
        /// </summary>
        /// <param name="btpStream">Input stream to construct BTP object from</param>
        private void Deserialize(Stream btpStream)
        {
            Header = new BTPHeader(this, btpStream);
            var textureTableStart = btpStream.Position;

            // First we must deserialize tfc table/map
            btpStream.Seek((long)Header.TFCTableOffset, SeekOrigin.Begin);
            TFCTable = new BTPTFCTable(this, btpStream);

            // Deserialize texture table
            btpStream.Seek(textureTableStart, SeekOrigin.Begin);
            TextureOverrides = new List<BTPTextureEntry>((int)Header.TextureCount);
            for (var i = 0; i < Header.TextureCount; i++)
            {
                var btpOverride = new BTPTextureEntry(this, btpStream);
                TextureOverrides.Add(btpOverride);
            }
        }
    }


    /// <summary>
    /// Header for BTP file
    /// </summary>
    class BTPHeader
    {
        /// <summary>
        /// Magic for BTP
        /// </summary>
        private static string MANIFEST_HEADER => "LETEXM"; // Must be ASCII

        /// <summary>
        /// BTP owner of this header
        /// </summary>
        public BinaryTexturePackage Owner { get; }

        /// <summary>
        /// Magic string for header
        /// </summary>
        public string Magic { get; set; }

        /// <summary>
        /// Version of serialization for this BTP
        /// </summary>
        public ushort Version { get; set; }

        /// <summary>
        /// FNV1 hash of the DLC folder this BTP is for, combined with the game id
        /// </summary>
        public uint TargetHash { get; set; }

        /// <summary>
        /// Number of texture override entries. Only use during serialization/deserialization.
        /// </summary>
        public uint TextureCount { get; set; }

        /// <summary>
        /// Number of items in the TFC Table. Only use during serialization/deserialization.
        /// </summary>
        public uint TFCTableCount { get; set; }

        /// <summary>
        /// Offset for TFC table
        /// </summary>
        public ulong TFCTableOffset { get; set; }

        // 4 bytes reserved currently


        public BTPHeader(BinaryTexturePackage owner, Stream btpStream)
        {
            this.Owner = owner;
            if (btpStream != null)
            {
                Deserialize(btpStream);
            }
            else
            {
                // Setup blank object
                Magic = MANIFEST_HEADER;
                Version = BinaryTexturePackage.CURRENT_VERSION;
            }
        }

        /// <summary>
        /// Writes the header out to the given stream.
        /// </summary>
        /// <param name="btpStream">Stream to write to</param>
        public void Serialize(Stream btpStream)
        {
            var headerStartPos = btpStream.Position;
            btpStream.WriteStringASCII(Magic); // Is this null terminated? We dont want that.
            btpStream.WriteUInt16(Version);
            btpStream.WriteUInt32(TargetHash);
            btpStream.WriteUInt32(TextureCount);
            btpStream.WriteUInt32(TFCTableCount);
            btpStream.WriteUInt64(TFCTableOffset);
            btpStream.WriteInt32(0); // Unused for now

#if DEBUG
            var entrySize = btpStream.Position - headerStartPos;
            if (entrySize != 32)
            {
                throw new Exception(@"Serializer for header produced the wrong size!");
            }
#endif
        }

        private void Deserialize(Stream btpStream)
        {
            Magic = btpStream.ReadStringASCII(6);
            Version = btpStream.ReadUInt16();
            TargetHash = btpStream.ReadUInt32();
            TextureCount = btpStream.ReadUInt32();
            TFCTableCount = btpStream.ReadUInt32();
            TFCTableOffset = btpStream.ReadUInt64();
            btpStream.ReadInt32(); // Reserved - 4 bytes
        }
    }

    /// <summary>
    /// Information about a single texture override in the BTP
    /// </summary>
    public class BTPTextureEntry
    {
        /// <summary>
        /// Owner of this entry
        /// </summary>
        public BinaryTexturePackage Owner { get; }

        /// <summary>
        /// The memory path to override
        /// </summary>
        public string OverridePath { get; set; }

        /// <summary>
        /// Reference to the BTP TFC Entry
        /// </summary>
        public BTPTFCEntry TFC { get; set; }

        /// <summary>
        /// Texture format for this entry
        /// </summary>
        public BTPPixelFormat Format { get; set; }

        /// <summary>
        /// If texture is sRGB
        /// </summary>
        public bool bSRGB { get; set; }

        /// <summary>
        /// LOD bias to allow higher mips to load
        /// </summary>
        public int InternalFormatLODBias { get; set; } // Stored as byte

        /// <summary>
        /// If texture should never be streamed, package stored must set this
        /// </summary>
        public bool NeverStream { get; set; }

        /// <summary>
        /// Number of actual mips over override is using
        /// </summary>
        public byte PopulatedMipCount { get; set; }

        /// <summary>
        /// List of BTP mips that are populated
        /// </summary>
        public List<BTPMipEntry> Mips { get; } = new(13);

        // Constants
        private const int OVERRIDE_PATH_MAX_CHARS = 255; // +1 for null terminator


        // SERIALIZATION ONLY
        /// <summary>
        /// Where the entry data begins.
        /// </summary>
        private long EntryOffset;

        /// <summary>
        /// Seeks the stream to the start of the entry where it was serialized
        /// </summary>
        /// <param name="btpStream"></param>
        /// <exception cref="Exception"></exception>
        public void SeekTo(Stream btpStream)
        {
            if (EntryOffset == 0)
            {
                throw new Exception(@"BTPStream was incorrectly setup; the offset was never set for an entry!");
            }
            btpStream.Seek(EntryOffset, SeekOrigin.Begin);
        }

        /// <summary>
        /// Constructs a BTP entry with the given owner and deserializes if BTP stream is not null.
        /// </summary>
        /// <param name="owner"></param>
        /// <param name="btpStream"></param>
        public BTPTextureEntry(BinaryTexturePackage owner, Stream btpStream)
        {
            this.Owner = owner;
            if (btpStream != null)
            {
                Deserialize(btpStream);
            }
            else
            {
                // TFC on initialization is always set to None, so that we have a valid reference.
                TFC = Owner.TFCTable.GetTFC(@"None");
                // Allocate mips
                for (int i = 0; i < 13; i++)
                {
                    Mips.Add(new BTPMipEntry(this, null));
                }
            }
        }

        /// <summary>
        /// Serializes the texture entry to the given stream. This does NOT serialize mip data! 
        /// </summary>
        /// <param name="btpStream"></param>
        public void Serialize(Stream btpStream)
        {
            EntryOffset = btpStream.Position;
            btpStream.WritePaddedStringUnicodeNull(OverridePath ?? @"", (OVERRIDE_PATH_MAX_CHARS + 1) * 2); // If not set, we just write a blank string.
            btpStream.WriteInt32(TFC.TableIndex);
            btpStream.WriteInt32((int)Format);
            btpStream.WriteBoolByte(bSRGB);
            btpStream.WriteByte((byte)InternalFormatLODBias);
            btpStream.WriteBoolByte(NeverStream);
            btpStream.WriteByte(PopulatedMipCount);

            // Write out all 13 mip header structs now, from largest to smallest
            foreach (var mip in Mips)
            {
                mip.Serialize(btpStream);
            }

#if DEBUG
            var entrySize = btpStream.Position - EntryOffset;
            if (entrySize != 836)
            {
                throw new Exception(@"Serializer for texture produced the wrong size!");
            }

            if (Owner.IsFinalSerialize)
            {
                if (Format == BTPPixelFormat.PF_Unknown)
                {
                    throw new Exception(@"Serializer for texture reports no format!");
                }

                if (PopulatedMipCount == 0)
                {
                    throw new Exception(@"Serializer for texture reports 0 mips!");
                }
            }
#endif
        }


        /// <summary>
        /// Populates this object from the given stream
        /// </summary>
        /// <param name="btpStream">Stream to read from</param>
        private void Deserialize(Stream btpStream)
        {
            EntryOffset = btpStream.Position;
            OverridePath = btpStream.ReadStringUnicodeNull();
            btpStream.Seek(EntryOffset + ((OVERRIDE_PATH_MAX_CHARS + 1) * 2), SeekOrigin.Begin); // +1 for null terminator
            var tfcTableIndex = btpStream.ReadInt32();
            TFC = Owner.TFCTable.TFCTable.Values.First(x => x.TableIndex == tfcTableIndex);
            Format = (BTPPixelFormat)btpStream.ReadInt32();
            bSRGB = btpStream.ReadBoolByte();
            InternalFormatLODBias = btpStream.ReadByte();
            NeverStream = btpStream.ReadBoolByte();

            // Read mip structs, even unpopulated
            PopulatedMipCount = (byte)btpStream.ReadByte();
            for (var i = 0; i < 13; i++)
            {
                // If 2 items in list, 0,1 are used, index 2 is invalid
                // so >=
                var mip = new BTPMipEntry(this, btpStream, i >= PopulatedMipCount);
                Mips.Add(mip);
            }

#if DEBUG
            var entrySize = btpStream.Position - EntryOffset;
            if (entrySize != 836)
            {
                throw new Exception(@"Deserializer for texture produced the wrong size!");
            }
#endif
        }
    }

    /// <summary>
    /// Information about a mip in the BTP
    /// </summary>
    public class BTPMipEntry
    {
        /// <summary>
        /// Texture override owner of this mip
        /// </summary>
        private BTPTextureEntry Owner { get; }

        /// <summary>
        /// Size of data uncompressed
        /// </summary>
        public int UncompressedSize { get; set; }
        /// <summary>
        /// Size of compressed data (if compressed)
        /// </summary>
        public int CompressedSize { get; set; }
        /// <summary>
        /// Offset to data, either in the TFC or in the BTP if this is package stored in flags
        /// </summary>
        public long CompressedOffset { get; set; }

        /// <summary>
        /// Width of the mip
        /// </summary>
        public short Width { get; set; }
        /// <summary>
        /// Height of the mip
        /// </summary>
        public short Height { get; set; }

        /// <summary>
        /// Flags of the mip - storage type, etc
        /// </summary>
        public BTPMipFlags Flags { get; set; }

        /// <summary>
        /// Data for the mip. This will be null the owner has LoadMips = false.
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// Offset of the entry when serialized/deserialized
        /// </summary>
        private long EntryOffset { get; set; }

        /// <summary>
        /// Seeks the stream to the start of the entry where it was serialized
        /// </summary>
        /// <param name="btpStream"></param>
        /// <exception cref="Exception"></exception>
        public void SeekTo(Stream btpStream)
        {
            if (EntryOffset == 0)
            {
                throw new Exception(@"BTPStream was incorrectly setup; the offset was never set for a mip entry!");
            }
            btpStream.Seek(EntryOffset, SeekOrigin.Begin);
        }

        public BTPMipEntry(BTPTextureEntry owner, Stream btpStream, bool isUnused = false)
        {
            Owner = owner;
            if (btpStream != null)
            {
                Deserialize(btpStream, isUnused);
            }
        }

        /// <summary>
        /// Serializes the mip header to the stream. This does NOT serialize data!
        /// </summary>
        /// <param name="btpStream">Stream to serialize to</param>
        public void Serialize(Stream btpStream, bool noDataChecks = false)
        {
            EntryOffset = btpStream.Position;
            btpStream.WriteInt32(UncompressedSize);
            btpStream.WriteInt32(CompressedSize);
            btpStream.WriteInt64(CompressedOffset);
            btpStream.WriteInt16(Width);
            btpStream.WriteInt16(Height);
            btpStream.WriteInt32((int)Flags);

#if DEBUG
            var entrySize = btpStream.Position - EntryOffset;
            if (entrySize != 24)
            {
                throw new Exception(@"Serializer for mip produced the wrong size!");
            }

            if (Owner.Owner.IsFinalSerialize && (Width > 0 || Height > 0))
            {
                if (UncompressedSize == 0)
                {
                    throw new Exception(@"Uncompressed size is 0 on final serialize!");
                }

                if (CompressedSize == 0)
                {
                    throw new Exception(@"Compressed size is 0 on final serialize!");
                }

                if (CompressedOffset == 0)
                {
                    throw new Exception(@"Compressed offset is 0 on final serialize!");
                }
            }
#endif
        }

        /// <summary>
        /// Serializes the data of this mip to the stream, as well as updating the stream's data for offset and sizing. 
        /// This also updates the BTP object to reflect the new data.
        /// </summary>
        /// <param name="btpStream">Stream to serialize to.</param>
        /// <param name="offset">The offset of the data. If data is present, it will be written at this position. If new data, this should be at the end of the stream</param>
        /// <param name="data">If null, this data is already in the stream, and we shouldn't write it again.</param>
        public void SerializeData(Stream btpStream, long offset, int compressedSize, byte[] data = null)
        {
            // We may be writing a deduplicated mip;
            // in this case we only set the offset
            // and compressed size, as everything else
            // should be identical to whatever we are
            // pointing at (since it's a duplicate).

            // Write package stored data to BTP
            if (data != null)
            {
                btpStream.Seek(offset, SeekOrigin.Begin);
                btpStream.Write(data);
                CompressedSize = compressedSize;
            }

#if DEBUG
            if (compressedSize == 0)
            {
                throw new Exception(@"Compressed size is 0!");
            }

            // Offset in TFC and package should never be zero since GUID in TFC and BTP header
            if (offset == 0)
            {
                throw new Exception(@"Offset is 0!");
            }
#endif

            // The offset where data resides (in BTP or in TFC)
            CompressedOffset = offset;
            CompressedSize = compressedSize;

            // Return to entry
            SeekTo(btpStream);

            // Rewrite entry data with updated information.
            Serialize(btpStream);
        }

        /// <summary>
        /// Populates this object from the given stream
        /// </summary>
        /// <param name="btpStream">STream to read from</param>
        private void Deserialize(Stream btpStream, bool isUnused)
        {
            EntryOffset = btpStream.Position;
            UncompressedSize = btpStream.ReadInt32();
            CompressedSize = btpStream.ReadInt32();
            CompressedOffset = btpStream.ReadInt64();
            Width = btpStream.ReadInt16();
            Height = btpStream.ReadInt16();
            Flags = (BTPMipFlags)btpStream.ReadInt32();

#if DEBUG
            var entrySize = btpStream.Position - EntryOffset;
            if (entrySize != 24)
            {
                throw new Exception(@"Deserializer for mip produced the wrong size!");
            }

            if (!isUnused)
            {
                if (CompressedSize == 0)
                {
                    throw new Exception(@"Deserializer for mip reports 0 compressed size!");
                }

                if (UncompressedSize == 0)
                {
                    throw new Exception(@"Deserializer for mip reports 0 uncompressed size!");
                }

                if (Width == 0 || Height == 0)
                {
                    throw new Exception(@"Deserializer for mip reports 0 Width/Height!");
                }
            }
#endif

            if (!isUnused && Owner.Owner.LoadMips && (Flags & BTPMipFlags.External) == 0)
            {
                // Local stored mip
                // To use it's data as a texture
                // you must check for oodle compression flag.
                var mipEntryEnd = btpStream.Position;
                btpStream.Seek(CompressedOffset, SeekOrigin.Begin);
                var data = Data = btpStream.ReadToBuffer(CompressedSize);

                // Verify decompression
                byte[] decompressedData = null;
                if ((Flags & BTPMipFlags.OodleCompressed) != 0)
                {
                    decompressedData = new byte[UncompressedSize];
                    var res = OodleHelper.Decompress(data, decompressedData);
                    if (res == 0)
                    {
                        Debug.WriteLine("darn");
                    }
                    else
                    {
                        Debug.WriteLine("hi");
                    }
                }

                var magic = BitConverter.ToUInt32(decompressedData ?? data);
                if (magic != 0x9E2A83C1) {
                    Debug.WriteLine("oof");
                }

                // Return
                btpStream.Seek(mipEntryEnd, SeekOrigin.Begin);
            }
        }
    }

    /// <summary>
    /// Information about a TFC in the BTP's TFC table
    /// </summary>
    public class BTPTFCEntry
    {
        /// <summary>
        /// Table owner of this TFC Entry
        /// </summary>
        private BTPTFCTable Owner;

        /// <summary>
        /// Name for no TFC association.
        /// </summary>
        public const string NO_TFC_NAME = @"None";
        /// <summary>
        /// Maximum length of a TFC name string.
        /// </summary>
        public const int TFC_NAME_MAX_SIZE = 63; // 128 bytes reserved, stored as 2byte unicode per character, so 64 chars -1 for null terminator.

        /// <summary>
        /// Constructor for BTPTFCEntry
        /// </summary>
        /// <param name="owner">Owning BTP</param>
        public BTPTFCEntry(BTPTFCTable owner, Stream btpStream)
        {
            Owner = owner;
            if (btpStream != null)
            {
                Deserialize(btpStream);
            }
        }

        /// <summary>
        /// Name of the TFC. "No TFC" uses the name 'None'.
        /// </summary>
        public string TFCName { get; internal set; } // None if no TFC

        /// <summary>
        /// Guid of the TFC. "No TFC" uses the Guid of all zero's.
        /// </summary>
        public Guid TFCGuid { get; internal set; } // 0 if no TFC

        /// <summary>
        /// Index into BTP table.
        /// </summary>
        public int TableIndex { get; internal set; }

        /// <summary>
        /// Offset of this entry in the stream when serialized/deserialized
        /// </summary>
        private long EntryOffset;

        /// <summary>
        /// Serializes this TFC entry to the given stream.
        /// </summary>
        /// <param name="btpStream"></param>
        internal void Serialize(Stream btpStream)
        {
            EntryOffset = btpStream.Position;
            btpStream.WritePaddedStringUnicodeNull(TFCName, (TFC_NAME_MAX_SIZE + 1) * 2);
            btpStream.WriteGuid(TFCGuid);

#if DEBUG
            var entrySize = btpStream.Position - EntryOffset;
            if (entrySize != 144)
            {
                throw new Exception(@"Serialize for TFC entry produced the wrong size!");
            }
#endif
        }


        /// <summary>
        /// Populates this object from the given stream.
        /// </summary>
        /// <param name="btpStream"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void Deserialize(Stream btpStream)
        {
            EntryOffset = btpStream.Position;
            TFCName = btpStream.ReadStringUnicodeNull();
            btpStream.Seek(EntryOffset + (TFC_NAME_MAX_SIZE + 1) * 2, SeekOrigin.Begin);
            TFCGuid = btpStream.ReadGuid();
            // Index must be assigned by the caller
        }
    }

    public class BTPTFCTable
    {
        /// <summary>
        /// Owner of this BTP TFC Table
        /// </summary>
        public BinaryTexturePackage Owner { get; }

        /// <summary>
        /// Location of the table in the stream
        /// </summary>
        internal ulong TableOffset { get; set; }

        /// <summary>
        /// Next TFC Index to assign in the table when adding another TFC table item
        /// </summary>
        int NextTFCIndex = 0;

        /// <summary>
        /// Map of TFC name to their entries. Used for performance.
        /// </summary>
        internal CaseInsensitiveDictionary<BTPTFCEntry> TFCTable;

        public BTPTFCTable(BinaryTexturePackage owner, Stream btpStream)
        {
            Owner = owner;
            if (btpStream != null)
            {
                Deserialize(btpStream);
            }
            else
            {
                TFCTable = new();
            }
        }

        /// <summary>
        /// Serializes the TFC table to the given stream, at the end.
        /// </summary>
        /// <param name="btpStream"></param>
        internal void Serialize(Stream btpStream)
        {
            // Align to 16 bytes
            btpStream.SeekEnd();
            while (btpStream.Position % 16 != 0)
                btpStream.WriteByte(0);

            // Store table offset and write the entries
            TableOffset = (ulong)btpStream.Position;
            foreach (var btpTFC in TFCTable.Values.OrderBy(x => x.TableIndex))
            {
                btpTFC.Serialize(btpStream);
            }
        }

        private void Deserialize(Stream btpStream)
        {
            TableOffset = (ulong)btpStream.Position;
            TFCTable = new();
            for (int i = 0; i < Owner.Header.TFCTableCount; i++)
            {
                var entry = new BTPTFCEntry(this, btpStream);
                entry.TableIndex = i;
                TFCTable[entry.TFCName] = entry;
                NextTFCIndex++; // Increment to make sure it's accurate if we edit this
            }
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
            if (tfc.Length > BTPTFCEntry.TFC_NAME_MAX_SIZE)
            {
                MLog.Error($@"TFC name is too big to serialize to BTP, aborting: {tfc}");
                throw new Exception($"TFC name is too long to use for the M3 Texture Override system: {tfc}. The maximum length supported is {BTPTFCEntry.TFC_NAME_MAX_SIZE} characters.");
            }

            tfcInfo = new BTPTFCEntry(this, null)
            {
                TableIndex = NextTFCIndex++,
                TFCGuid = guid,
                TFCName = tfc
            };

            TFCTable[tfc] = tfcInfo;
            Owner.Header.TFCTableCount++;
            return tfcInfo.TableIndex;
        }

        /// <summary>
        /// Returns the TFC entry for the given TFC name.
        /// </summary>
        /// <param name="tfcName"></param>
        /// <returns></returns>
        internal BTPTFCEntry GetTFC(string tfcName)
        {
            return TFCTable[tfcName];
        }
    }
}
