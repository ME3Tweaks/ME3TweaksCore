using LegendaryExplorerCore.Compression;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Targets;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ME3TweaksCore.NativeMods
{
    /// <summary>
    /// Represents a dependency required by an ASI mod, such as a DLL library.
    /// Handles caching, downloading, and verification of dependency files.
    /// </summary>
    public class ASIDependency
    {
        /// <summary>
        /// The filename of the dependency (as installed)
        /// </summary>
        public string Filename { get; set; }

        /// <summary>
        /// The filename of the dependency (as on storage/cache)
        /// </summary>
        public string StorageFilename { get; set; }

        /// <summary>
        /// The expected size of the dependency file in bytes
        /// </summary>
        public int Filesize { get; set; }

        /// <summary>
        /// The MD5 hash of the dependency file used for integrity verification
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// If the server asset is compressed and will need decompression after download
        /// </summary>
        public bool ServerAssetCompressed { get; set; }

        /// <summary>
        /// Filesize of downloaded asset (if compressed)
        /// </summary>
        public int CompressedFilesize { get; set; }

        /// <summary>
        /// Hash of the downloaded asset (if compressed)
        /// </summary>
        public string CompressedHash { get; set; }

        /// <summary>
        /// Gets the full path to where this dependency is stored in the local cache
        /// </summary>
        /// <returns>The full path to the cached dependency file</returns>
        public string GetCachedPath()
        {
            return Path.Combine(ASIManager.CachedASIsFolder, @"dependencies", StorageFilename);
        }

        /// <summary>
        /// Checks if this dependency exists in the ME3Tweaks cache and verifies its integrity
        /// </summary>
        /// <returns>True if the file exists in cache with correct size and hash; otherwise false</returns>
        public bool IsInME3TweaksCache()
        {
            var cachePath = GetCachedPath();

            if (!File.Exists(cachePath))
                return false;

            var fileInfo = new FileInfo(cachePath);
            if (fileInfo.Length != Filesize)
                return false;

            var calculatedHash = MUtilities.CalculateHash(cachePath);
            return calculatedHash.Equals(Hash, StringComparison.InvariantCultureIgnoreCase);
        }

        /// <summary>
        /// Downloads this dependency from the an endpoint to the local cache.
        /// Verifies file size and hash before saving.
        /// </summary>
        /// <returns>True if the download and verification succeeded; otherwise false</returns>
        public async Task<bool> DownloadToCache()
        {
            try
            {
                using (var client = new ShortTimeoutWebClient())
                {
                    var links = DownloadLink.GetAllLinks();

                    // Try each link until one succeeds
                    foreach (var link in links)
                    {
                        try
                        {
                            MLog.Information($@"Downloading ASI dependency from {link}...");
                            var downloadedData = await client.DownloadDataTaskAsync(link);

                            if (ServerAssetCompressed)
                            {
                                // Verify compressed data size
                                if (downloadedData.Length != CompressedFilesize)
                                {
                                    MLog.Warning($@"Rejecting invalid compressed size on downloaded ASI dependency {StorageFilename}: expected {CompressedFilesize}, got {downloadedData.Length}");
                                    continue; // Try next link
                                }

                                // Verify compressed data hash
                                using (var compressedStream = new MemoryStream(downloadedData))
                                {
                                    var calculatedCompressedHash = MUtilities.CalculateHash(compressedStream);
                                    if (!calculatedCompressedHash.Equals(CompressedHash, StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        MLog.Warning($@"Rejecting invalid compressed hash on downloaded ASI dependency {StorageFilename}");
                                        continue; // Try next link
                                    }

                                    // Decompress using LZMA
                                    compressedStream.Seek(0, SeekOrigin.Begin);
                                    MLog.Information($@"Decompressing ASI dependency {StorageFilename}...");
                                    using (var decompressedStream = new MemoryStream())
                                    {
                                        LZMA.DecompressLZMAStream(compressedStream, decompressedStream);
                                        downloadedData = decompressedStream.ToArray();
                                    }
                                }
                            }

                            // Check size
                            if (downloadedData.Length != Filesize)
                            {
                                MLog.Warning($@"Rejecting invalid size on downloaded ASI dependency {StorageFilename}");
                                continue; // Try next link
                            }

                            // Check hash in memory using MemoryStream
                            using (var memoryStream = new MemoryStream(downloadedData))
                            {
                                var calculatedHash = MUtilities.CalculateHash(memoryStream);
                                if (!calculatedHash.Equals(Hash, StringComparison.InvariantCultureIgnoreCase))
                                {
                                    MLog.Warning($@"Rejecting invalid hash on downloaded ASI dependency {StorageFilename}");
                                    continue; // Try next link
                                }
                            }

                            // Write to cache
                            var cachePath = GetCachedPath();
                            var directory = Path.GetDirectoryName(cachePath);
                            if (!Directory.Exists(directory))
                                Directory.CreateDirectory(directory);

                            // Delete existing file if present
                            if (File.Exists(cachePath))
                                File.Delete(cachePath);

                            MLog.Information($@"Saving downloaded ASI dependency {StorageFilename}");
                            await File.WriteAllBytesAsync(cachePath, downloadedData);
                            return true;
                        }
                        catch
                        {
                            // Try next link
                            continue;
                        }
                    }

                    // All links failed
                    return false;
                }
            }
            catch
            {
                MLog.Error($@"Failed to download ASI dependency {StorageFilename}, all endpoints failed");
                return false;
            }
        }

        /// <summary>
        /// Checks if this dependency is installed in the specified game target and verifies its integrity
        /// </summary>
        /// <param name="target">The game target to check for the dependency installation</param>
        /// <returns>True if the file exists in the game directory with correct size and hash; otherwise false</returns>
        public bool IsInstalled(GameTarget target)
        {
            var binaryDir = target.GetExecutableDirectory();
            var installedPath = Path.Combine(binaryDir, Filename);

            if (!File.Exists(installedPath))
                return false;

            var fileInfo = new FileInfo(installedPath);
            if (fileInfo.Length != Filesize)
                return false;

            var calculatedHash = MUtilities.CalculateHash(installedPath);
            return calculatedHash.Equals(Hash, StringComparison.InvariantCultureIgnoreCase);
        }

        internal async Task InstallToTarget(GameTarget target)
        {
            // Check if already installed
            if (IsInstalled(target))
            {
                MLog.Information($@"ASI dependency {Filename} is already installed");
                return;
            }

            MLog.Information($@"Installing ASI dependency {Filename}");

            // Check if dependency is in cache
            if (!IsInME3TweaksCache())
            {
                MLog.Information($@"ASI dependency {Filename} not found in cache, downloading...");
                bool downloadSuccess = await DownloadToCache();

                if (!downloadSuccess)
                {
                    throw new Exception(LC.GetString(LC.string_interp_failedToDownloadASIDependency, Filename));
                }

                // Verify it's now in cache
                if (!IsInME3TweaksCache())
                {
                    throw new Exception(LC.GetString(LC.string_interp_asiDependencyNotAvailable, Filename));
                }
            }

            // Copy from cache to target
            var cachePath = GetCachedPath();
            var binaryDir = target.GetExecutableDirectory();
            var destinationPath = Path.Combine(binaryDir, Filename);

            File.Copy(cachePath, destinationPath, true);

            MLog.Information($@"Successfully installed ASI dependency {Filename}");
        }

        /// <summary>
        /// Gets the download links for this dependency with load balancing between main and fallback URLs
        /// </summary>
        public FallbackLink DownloadLink
        {
            get
            {
                var suffix = ServerAssetCompressed ? @".lzma" : @"";
                return new FallbackLink()
                {
                    LoadBalancing = true,
                    MainURL = $@"https://me3tweaks.com/mods/asi/dependencies/{StorageFilename}{suffix}",
                    FallbackURL = $@"https://github.com/ME3Tweaks/LExASIs/releases/download/Dependencies/{StorageFilename}{suffix}"
                };
            }
        }
    }
}
