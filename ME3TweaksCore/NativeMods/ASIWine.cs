using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Services;
using ME3TweaksCore.Targets;

namespace ME3TweaksCore.NativeMods
{
    internal static class ASIWine
    {
        private const string VC145_ZIP_FILENAME = @"vc145.zip";
        private const string VC145_MD5_HASH = @"b80dfe7a90dee0bb31af9f7f2173a6f3";
        internal const string VC145_DOWNLOAD_URL = @"https://github.com/ME3Tweaks/ME3TweaksAssets/releases/download/msvc-v145/vc145.zip";

        /// <summary>
        /// Tracks if the VC145 zip file has been hash-verified during this session
        /// </summary>
        private static bool _vc145HashVerifiedThisSession = false;

        /// <summary>
        /// Builds the filepath for vc145.zip in CachedASIs/Linux folder
        /// </summary>
        /// <returns>Full path to vc145.zip</returns>
        internal static string GetVC145ZipPath()
        {
            var cachedASIsFolder = Path.Combine(MCoreFilesystem.GetSharedME3TweaksDataFolder(), @"CachedASIs", @"Linux");
            Directory.CreateDirectory(cachedASIsFolder);
            return Path.Combine(cachedASIsFolder, VC145_ZIP_FILENAME);
        }

        /// <summary>
        /// Ensures the vc145.zip file is downloaded and verified with the correct MD5 hash.
        /// On first check of an existing file, verifies the MD5. Subsequent checks during the same session skip hash verification.
        /// If the file doesn't exist or hash is incorrect, downloads it from the server.
        /// </summary>
        /// <returns>True if the file is available and verified; false otherwise</returns>
        public static async Task<bool> EnsureVC145ZipDownloadedAsync()
        {
            var zipPath = GetVC145ZipPath();

            // If file exists and we've already verified it this session, return true
            if (File.Exists(zipPath) && _vc145HashVerifiedThisSession)
            {
                MLog.Information($@"VC145 zip already verified this session: {zipPath}");
                return true;
            }

            // If file exists but not yet verified this session, verify the hash
            if (File.Exists(zipPath))
            {
                MLog.Information($@"Verifying VC145 zip hash: {zipPath}");
                var existingHash = await Task.Run(() => MUtilities.CalculateHash(zipPath));
                
                if (existingHash == VC145_MD5_HASH)
                {
                    MLog.Information(@"VC145 zip hash verified successfully");
                    _vc145HashVerifiedThisSession = true;
                    return true;
                }

                MLog.Warning($@"VC145 zip hash mismatch. Expected: {VC145_MD5_HASH}, Got: {existingHash}. Will re-download.");
                
                // Delete the corrupted file
                try
                {
                    File.Delete(zipPath);
                }
                catch (Exception ex)
                {
                    MLog.Error($@"Failed to delete corrupted VC145 zip: {ex.Message}");
                    return false;
                }
            }

            // Download the file
            MLog.Information($@"Downloading VC145 zip from {VC145_DOWNLOAD_URL}");
            var downloadResult = await Task.Run(() => MOnlineContent.DownloadToMemory(
                VC145_DOWNLOAD_URL,
                hash: VC145_MD5_HASH,
                logDownload: true
            ));

            if (downloadResult.errorMessage != null || downloadResult.result == null)
            {
                MLog.Error($@"Failed to download VC145 zip: {downloadResult.errorMessage ?? "Unknown error"}");
                return false;
            }

            // Save the downloaded file
            try
            {
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                {
                    downloadResult.result.Position = 0;
                    await downloadResult.result.CopyToAsync(fileStream);
                }

                MLog.Information($@"VC145 zip downloaded and saved successfully: {zipPath}");
                _vc145HashVerifiedThisSession = true;
                return true;
            }
            catch (Exception ex)
            {
                MLog.Error($@"Failed to save VC145 zip: {ex.Message}");
                return false;
            }
            finally
            {
                downloadResult.result?.Dispose();
            }
        }

        /// <summary>
        /// Extracts the VC145 zip file contents to the executable directory of the specified game target.
        /// The zip file must already be downloaded via EnsureVC145ZipDownloadedAsync.
        /// Skips extraction if all files already exist with matching sizes.
        /// </summary>
        /// <param name="target">The game target whose executable directory will receive the extracted files</param>
        /// <param name="overwriteExisting">If true, overwrites existing files; otherwise skips them</param>
        /// <returns>True if extraction succeeded or files already exist; false otherwise</returns>
        public static async Task<bool> ExtractVC145ToTargetAsync(GameTarget target, bool overwriteExisting = true)
        {
            if (target == null)
            {
                return false;
            }

            var zipPath = GetVC145ZipPath();
            if (!File.Exists(zipPath))
            {
                MLog.Error($@"Cannot extract VC145: zip file not found at {zipPath}. Call EnsureVC145ZipDownloadedAsync first.");
                return false;
            }

            var executableDir = M3Directories.GetExecutableDirectory(target);
            if (!Directory.Exists(executableDir))
            {
                MLog.Error($@"Cannot extract VC145: executable directory does not exist: {executableDir}");
                return false;
            }

            try
            {
                // Check if all files already exist with correct sizes
                bool allFilesExist = await Task.Run(() =>
                {
                    using (var archive = ZipFile.OpenRead(zipPath))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            // Skip directories
                            if (string.IsNullOrEmpty(entry.Name))
                                continue;

                            var destinationPath = Path.Combine(executableDir, entry.FullName);
                            
                            // Check if file exists with matching size
                            if (!File.Exists(destinationPath))
                            {
                                return false;
                            }

                            var fileInfo = new FileInfo(destinationPath);
                            if (fileInfo.Length != entry.Length)
                            {
                                return false;
                            }
                        }
                        return true;
                    }
                });

                if (allFilesExist)
                {
                    MLog.Information($@"VC145 dlls already present in target executable directory");
                    return true;
                }

                MLog.Information($@"Extracting VC145 zip to: {executableDir}");
                await Task.Run(() =>
                {
                    using (var archive = ZipFile.OpenRead(zipPath))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            // Skip directories
                            if (string.IsNullOrEmpty(entry.Name))
                                continue;

                            var destinationPath = Path.Combine(executableDir, entry.FullName);
                            
                            // Check if file exists
                            if (File.Exists(destinationPath))
                            {
                                if (!overwriteExisting)
                                {
                                    MLog.Information($@"Skipping existing file: {entry.FullName}");
                                    continue;
                                }

                                MLog.Information($@"Overwriting existing file: {entry.FullName}");
                            }

                            // Extract the file
                            entry.ExtractToFile(destinationPath, overwriteExisting);
                            MLog.Information($@"Extracted: {entry.FullName}");
                        }
                    }
                });

                MLog.Information($@"VC145 extraction completed successfully to: {executableDir}");
                return true;
            }
            catch (Exception ex)
            {
                MLog.Error($@"Failed to extract VC145 zip: {ex.Message}");
                return false;
            }
        }
    }
}
