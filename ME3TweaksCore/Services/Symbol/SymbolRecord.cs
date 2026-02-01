using LegendaryExplorerCore.Compression;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Objects;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ME3TweaksCore.Services.Symbol
{
    /// <summary>
    /// Describes symbol information for a game executable
    /// </summary>
    public class SymbolRecord
    {
        /// <summary>
        /// The game this record applies to
        /// </summary>
        [JsonProperty(@"game")]
        public MEGame Game { get; set; }

        /// <summary>
        /// MD5 hash of the target game executable
        /// </summary>
        [JsonProperty(@"executablehash")]
        public string GameHash { get; set; }

        /// <summary>
        /// Size in bytes of the executable
        /// </summary>
        [JsonProperty(@"executablesize")]
        public int GameSize { get; set; }

        /// <summary>
        /// MD5 hash of the current PDB file for this game
        /// </summary>
        [JsonProperty(@"pdbhash")]
        public string PdbHash { get; set; }

        /// <summary>
        /// Size of the PDB file in bytes
        /// </summary>
        [JsonProperty(@"pdbsize")]
        public int PdbSize { get; set; }

        /// <summary>
        /// Size in bytes of the lzma compressed PDB file
        /// </summary>
        [JsonProperty(@"pdbcompressedsize")]
        public int PdbCompressedSize { get; set; }

        /// <summary>
        /// MD5 of the lzma compressed PDB file
        /// </summary>
        [JsonProperty(@"pdbcompressedhash")]
        public string PdbCompressedHash { get; set; }

        /// <summary>
        /// Gets the name of this PDB file as stored in the ME3Tweaks shared symbols folder and on a server
        /// </summary>
        /// <returns></returns>
        internal string GetStoredPDBName()
        {
            return $@"{Game}-{GameHash}.pdb";
        }

        /// <summary>
        /// Returns the path at which this symbol's PDB file would be cached
        /// </summary>
        /// <returns></returns>
        internal string GetCachedPath()
        {
            var sharedSymbolsFolder = Path.Combine(MCoreFilesystem.GetSharedME3TweaksDataFolder(), "Symbols");
            return Path.Combine(sharedSymbolsFolder, GetStoredPDBName());
        }

        /// <summary>
        /// Downloads the PDB file from a fallback link, verifies it in memory, and saves it to the cache if valid.
        /// </summary>
        /// <param name="progressInfo">Optional progress reporting object</param>
        /// <returns>True if download and verification succeeded, false otherwise</returns>
        internal async Task<bool> DownloadPDBAsync(ProgressInfo progressInfo = null)
        {
            // Generate fallback download URLs (placeholders) with .lzma extension
            var fallbackLink = new FallbackLink
            {
                MainURL = $@"https://github.com/ME3Tweaks/ME3TweaksAssets/releases/download/symbols/{GetStoredPDBName()}.lzma",
                FallbackURL = $@"https://me3tweaks.com/modmanager/services/symbol/{GetStoredPDBName()}.lzma",
                LoadBalancing = false
            };

            var urls = fallbackLink.GetAllLinks();

            // Try each URL in sequence
            foreach (var url in urls)
            {
                try
                {
                    if (progressInfo != null)
                    {
                        progressInfo.Status = $"Downloading PDB";
                        progressInfo.Value = 0;
                        progressInfo.Indeterminate = false;
                        progressInfo.OnUpdate?.Invoke(progressInfo);
                    }

                    // Download the compressed file to memory
                    byte[] compressedData;
                    using (var client = new ShortTimeoutWebClient())
                    {
                        // Set up progress reporting
                        if (progressInfo != null)
                        {
                            client.DownloadProgressChanged += (s, e) =>
                            {
                                if (progressInfo != null)
                                {
                                    progressInfo.Value = e.ProgressPercentage;
                                    progressInfo.Indeterminate = false;
                                    progressInfo.OnUpdate?.Invoke(progressInfo);
                                }
                            };
                        }

                        compressedData = await client.DownloadDataTaskAsync(url);
                    }

                    if (progressInfo != null)
                    {
                        progressInfo.Status = "Verifying PDB";
                        progressInfo.Indeterminate = true;
                        progressInfo.OnUpdate?.Invoke(progressInfo);
                    }

                    // Verify compressed size
                    if (compressedData.Length != PdbCompressedSize)
                    {
                        MLog.Warning($@"Downloaded compressed PDB size mismatch: {compressedData.Length} != {PdbCompressedSize}");
                        continue; // Try next URL
                    }

                    // Verify compressed MD5 hash
                    string compressedHash;
                    using (var md5 = MD5.Create())
                    {
                        var hashBytes = md5.ComputeHash(compressedData);
                        compressedHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    }

                    if (!string.Equals(compressedHash, PdbCompressedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        MLog.Warning($@"Downloaded compressed PDB hash mismatch: {compressedHash} != {PdbCompressedHash}");
                        continue; // Try next URL
                    }

                    progressInfo?.OnUpdate?.Invoke(new ProgressInfo
                    {
                        Status = "Decompressing PDB",
                        Indeterminate = true
                    });

                    // Decompress the data
                    byte[] decompressedData;
                    try
                    {
                        decompressedData = LZMA.DecompressLZMAFile(compressedData);
                        if (decompressedData == null || decompressedData.Length == 0)
                        {
                            MLog.Warning($@"Failed to decompress PDB file");
                            continue; // Try next URL
                        }
                    }
                    catch (Exception ex)
                    {
                        MLog.Warning($@"Error decompressing PDB: {ex.Message}");
                        continue; // Try next URL
                    }

                    // Verify decompressed size
                    if (decompressedData.Length != PdbSize)
                    {
                        MLog.Warning($@"Decompressed PDB size mismatch: {decompressedData.Length} != {PdbSize}");
                        continue; // Try next URL
                    }

                    // Verify decompressed MD5 hash
                    var decompressedHash = MUtilities.CalculateHash(decompressedData);
                    if (decompressedHash != PdbHash)
                    {
                        MLog.Warning($@"Decompressed PDB hash mismatch: {decompressedHash} != {PdbHash}");
                        continue; // Try next URL
                    }

                    // All verification passed, write decompressed data to cache
                    var cachedPath = GetCachedPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(cachedPath));
                    await File.WriteAllBytesAsync(cachedPath, decompressedData);

                    if (progressInfo != null)
                    {
                        progressInfo.Indeterminate = false;
                        progressInfo.Value = 100;
                        progressInfo?.OnUpdate?.Invoke(progressInfo);
                    }

                    MLog.Information($@"Successfully downloaded and cached {GetStoredPDBName()}");
                    return true;
                }
                catch (Exception ex)
                {
                    MLog.Warning($@"Failed to download or verify PDB from {url}: {ex.Message}");
                }
            }

            return false;
        }
    }
}
