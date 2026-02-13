using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.ME3Tweaks.ModManager;
using ME3TweaksCore.Services.Backup;
using ME3TweaksCore.Services.Shared.BasegameFileIdentification;
using ME3TweaksCore.Targets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ME3TweaksCore.ME3Tweaks.M3Merge.GlobalShader
{
    /// <summary>
    /// Handles merging global shader cache for LE games.
    /// </summary>
    public class GlobalShaderMerge
    {
        public static string SHADER_MERGE_PATTERN = @"GlobalShader-*.m3gs";
        private record GSMFileInfo(string hash, uint size);

        /// <summary>
        /// Map of information about the global shader cache for each game
        /// </summary>
        private static Dictionary<MEGame, GSMFileInfo> ShaderFileMap = new() {
            { MEGame.LE1, new GSMFileInfo(@"29be3e69c5b1f5139b88e3764f018af6", 1506125) },
            { MEGame.LE2, new GSMFileInfo(@"69de7c7b796bd9e376325195914ebfb4", 1526420) },
            { MEGame.LE3, new GSMFileInfo(@"997970d72635b082f7c039802f39ef27", 2212817) },
        };

        /// <summary>
        /// Runs the shader merge process for the specified game target. Scans DLC_MOD folders for .m3gs files
        /// and merges them into the GlobalShaderCache-PC-D3D-SM5.bin file in mount order.
        /// </summary>
        /// <param name="target">The game target to perform shader merge on</param>
        /// <param name="log">Whether to enable detailed logging</param>
        /// <returns>True if the merge completed successfully, false if a vanilla shader cache could not be sourced</returns>
        public static bool RunShaderMerge(GameTarget target, bool log)
        {
            MLog.Information($@"Performing Shader Merge for game: {target.TargetPath}");
            var globalShaderCacheF = GetVanillaGlobalShaderCache(target);
            if (globalShaderCacheF == null)
            {
                MLog.Warning($@"Could not source a vanilla copy of the Global Shader Cache. Cannot perform Shader Merge, skipping.");
                return false;
            }

            using var fs = File.OpenRead(globalShaderCacheF);
            var globalShaderCache = GlobalShaderCache.ReadGlobalShaderCache(fs, target.Game);
            var shaders = globalShaderCache.Shaders.Values.ToList();

            var dlcMountsInOrder = MELoadedDLC.GetDLCNamesInMountOrder(target.Game, target.TargetPath);

            // For BGFIS
            bool mergedAny = false;
            string recordedMergeName = @"";
            void recordMerge(string displayName)
            {
                mergedAny = true;
                recordedMergeName += displayName + "\n"; // do not localize
            }

            foreach (var dlc in dlcMountsInOrder)
            {
                if (!dlc.StartsWith(@"DLC_MOD_"))
                {
                    // Do not shader merge non-mod folders
                    continue;
                }

                var metaCmm = target.GetMetaCMMForDLC(dlc);
                if (metaCmm == null || metaCmm.ModDescFeatureLevel < ModDescConsts.MODDESC_VERSION_9_2)
                {
                    // Requires 9.2 moddesc
                    continue;
                }

                var dlcCookedPath = Path.Combine(target.GetDLCPath(), dlc, target.Game.CookedDirName());

                MLog.Debug($@"Looking for {SHADER_MERGE_PATTERN} files in {dlcCookedPath}", log);
                var globalShaders = Directory.GetFiles(dlcCookedPath, GlobalShaderMerge.SHADER_MERGE_PATTERN, SearchOption.TopDirectoryOnly)
                    .ToList();
                if (globalShaders.Count > 0)
                {
                    MLog.Information($@"Found {globalShaders.Count} m3gs files to apply", log);
                }

                foreach (var gs in globalShaders)
                {
                    var extractedShaderIdx = ExtractShaderIndex(gs, out var shaderIndex);
                    if (extractedShaderIdx && shaderIndex >= 0 && shaderIndex < shaders.Count)
                    {
                        var relPath = Path.GetRelativePath(target.TargetPath, gs);
                        MLog.Information($@"Merging M3 Global Shader {relPath}");
                        var shaderCode = File.ReadAllBytes(gs);

                        if (shaderCode.Length > 4)
                        {
                            var magic = BitConverter.ToInt32(shaderCode, 0);
                            if (magic != 0x43425844)
                            {
                                MLog.Error($@"Invalid m3gs file: File doesn't appear to be a compiled shader file");
                                throw new Exception(LC.GetString(LC.string_interp_gsInvalidHeader, relPath));
                            }
                        }
                        else
                        {
                            MLog.Error($@"Invalid m3gs file: File is not a valid compiled shader");
                            throw new Exception(LC.GetString(LC.string_interp_gsNotValidShader, relPath));
                        }
                        shaders[shaderIndex].ShaderByteCode = shaderCode;
                        recordMerge($@"{dlc}-{Path.GetFileName(gs)}");
                        mergedAny = true;
                    }
                    else
                    {
                        MLog.Error($@"Invalid filename for global shader: {Path.GetFileName(gs)}. Must be in the form: GlobalShader-INDEX-[...].m3gs and between 0 and {shaders.Count}. Skipping.");
                    }
                }
            }

            var records = new List<BasegameFileRecord>();
            var outF = Path.Combine(target.GetCookedPath(), @"GlobalShaderCache-PC-D3D-SM5.bin");

            // Set the BGFIS record name
            if (mergedAny)
            {
                // Serialize the assets
                var ms = new MemoryStream();
                var gscsc = new PackagelessSerializingContainer(ms, null);
                gscsc.SetGame(target.Game);
                globalShaderCache.WriteTo(gscsc);
                ms.WriteByte(0); // This forces size change, which lets us tell it's been modified on size check.
                ms.WriteToFile(outF);

                // Submit to BGFIS
                records.Add(new BasegameFileRecord(outF, target, recordedMergeName.Trim()));
                BasegameFileIdentificationService.AddLocalBasegameIdentificationEntries(records);
            }
            else
            {
                // Just copy the file instead.
                File.Copy(globalShaderCacheF, outF, true);
            }

            return true;
        }

        /// <summary>
        /// Attempts to source a vanilla global shader cache file from multiple locations in priority order:
        /// 1. ME3Tweaks shared backup folder
        /// 2. ME3Tweaks game backup
        /// 3. Current game installation (if vanilla)
        /// </summary>
        /// <param name="target">The game target to source the shader cache for</param>
        /// <returns>Path to a vanilla GlobalShaderCache-PC-D3D-SM5.bin file, or null if none could be found or validated</returns>
        private static string GetVanillaGlobalShaderCache(GameTarget target)
        {
            var shaderCacheBackupFolder = Path.Combine(MCoreFilesystem.GetSharedME3TweaksDataFolder(), @"GlobalShaderCacheBackup");
            if (!Directory.Exists(shaderCacheBackupFolder))
            {
                Directory.CreateDirectory(shaderCacheBackupFolder);
            }

            bool hasShaderCacheBackup = false;


            // Use shared version so user doesn't need a game backup for this single feature.
            var sharedBackupShaderCachePath = Path.Combine(shaderCacheBackupFolder, $@"{target.Game}.bin");
            #region Check ME3Tweaks shared backup
            {
                if (File.Exists(sharedBackupShaderCachePath))
                {
                    // Check size.
                    bool isValidBackup = true;
                    var fi = new FileInfo(sharedBackupShaderCachePath);
                    var info = ShaderFileMap[target.Game];
                    if (fi.Length != info.size)
                    {
                        // Cannot use this file.
                        isValidBackup = false;
                        File.Delete(sharedBackupShaderCachePath); // Invalid
                    }

                    if (isValidBackup && MUtilities.CalculateHash(sharedBackupShaderCachePath) != info.hash)
                    {
                        // Cannot use this file.
                        isValidBackup = false;
                        File.Delete(sharedBackupShaderCachePath); // Invalid
                    }

                    // File is valid.
                    hasShaderCacheBackup = isValidBackup;
                }
            }
            #endregion

            #region ME3Tweaks Backup
            if (!hasShaderCacheBackup)
            {
                // Find it in ME3Tweaks Backup
                var backup = BackupService.GetGameBackupPath(target.Game);
                if (backup != null)
                {
                    var globalShaderCacheF = Path.Combine(backup, @"BioGame", @"CookedPCConsole", @"GlobalShaderCache-PC-D3D-SM5.bin");
                    if (File.Exists(globalShaderCacheF))
                    {
                        // Copy to backup
                        File.Copy(globalShaderCacheF, sharedBackupShaderCachePath, true);
                        MLog.Information($@"Copied global shader cache from game backup for {target.Game}.");
                        hasShaderCacheBackup = true;
                    }
                }
            }
            #endregion

            #region Game version
            if (!hasShaderCacheBackup)
            {
                var gameShaderCache = Path.Combine(target.GetCookedPath(), @"GlobalShaderCache-PC-D3D-SM5.bin");
                if (File.Exists(gameShaderCache))
                {
                    // Check file is vanilla.
                    bool isVanilla = true;
                    var fi = new FileInfo(gameShaderCache);
                    var info = ShaderFileMap[target.Game];
                    if (fi.Length != info.size)
                    {
                        // Cannot use this file.
                        isVanilla = false;
                    }

                    if (isVanilla && MUtilities.CalculateHash(gameShaderCache) != info.hash)
                    {
                        // Cannot use this file.
                        isVanilla = false;
                    }

                    if (isVanilla)
                    {
                        File.Copy(gameShaderCache, sharedBackupShaderCachePath, true);
                        MLog.Information($@"Backed up global shader cache from installation");
                        hasShaderCacheBackup = true;
                    }
                }
            }
            #endregion

            return hasShaderCacheBackup ? sharedBackupShaderCachePath : null;
        }


        /// <summary>
        /// Extracts the shader index number from a global shader merge filename.
        /// Expected format: GlobalShader-INDEX-[...].m3gs
        /// </summary>
        /// <param name="filepath">The full path to the shader file</param>
        /// <param name="shaderIndex">The extracted shader index, or -1 if extraction failed</param>
        /// <returns>True if the index was successfully extracted and parsed, false otherwise</returns>
        private static bool ExtractShaderIndex(string filepath, out int shaderIndex)
        {
            shaderIndex = -1;
            var filename = Path.GetFileName(filepath);
            filename = filename.Substring(filename.IndexOf('-') + 1);
            var nextDash = filename.IndexOf('-');
            if (nextDash > 0 && int.TryParse(filename.Substring(0, nextDash), out shaderIndex))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if the specified game target requires shader merging by scanning for .m3gs files in DLC_MOD folders.
        /// </summary>
        /// <param name="target">The game target to check</param>
        /// <returns>True if shader merging is needed</returns>
        public static bool NeedsMerged(GameTarget target)
        {
            return true;
            // var supercedances = target.GetFileSupercedances([".m3gs"]); //, viaTOC: true);
        }
    }
}