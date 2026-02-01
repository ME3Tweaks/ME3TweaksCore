using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Targets;
using ME3TweaksCore.GameFilesystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace ME3TweaksCore.Services.Symbol
{
    /// <summary>
    /// Service for managing debugging symbol information for game executables
    /// </summary>
    public class SymbolService
    {
        /// <summary>
        /// Database of game symbols indexed by game then by a list of symbol records
        /// </summary>
        private static Dictionary<MEGame, List<SymbolRecord>> Database = new Dictionary<MEGame, List<SymbolRecord>>();

        /// <summary>
        /// If the SymbolService has been initially loaded
        /// </summary>
        public static bool ServiceLoaded { get; private set; }

        /// <summary>
        /// Service name for logging
        /// </summary>
        private const string ServiceLoggingName = @"Symbol Service";

        /// <summary>
        /// Synchronization object for thread-safe operations
        /// </summary>
        private static object syncObj = new object();

        private static string GetLocalServiceCacheFile() => MCoreFilesystem.GetSymbolServiceFile();

        private static void InternalLoadService(JToken serviceData = null)
        {
            lock (syncObj)
            {
                Database = new Dictionary<MEGame, List<SymbolRecord>>();
                LoadDatabase(Database, serviceData);
            }
        }

        private static void LoadDatabase(Dictionary<MEGame, List<SymbolRecord>> database, JToken serviceData = null)
        {
            // First load the local data
            LoadLocalData(database);

            // Then load the server data
            // Online data is merged into local and then committed to disk if updated
            if (serviceData != null)
            {
                try
                {
                    bool updated = false;
                    // Read service data and merge into the local database file
                    var onlineDB = serviceData.ToObject<List<SymbolRecord>>();
                    if (onlineDB == null)
                    {
                        MLog.Error($@"Failed to deserialize online {ServiceLoggingName}: data was null");
                        return;
                    }

                    foreach (var onlineRecord in onlineDB)
                    {
                        if (!database.TryGetValue(onlineRecord.Game, out var gameRecords))
                        {
                            // Use a list of records per game
                            gameRecords = new List<SymbolRecord>();
                            database[onlineRecord.Game] = gameRecords;
                        }

                        // Try find existing record by GameHash (case-insensitive)
                        var key = onlineRecord.GameHash ?? string.Empty;
                        var existingRecord = gameRecords.FirstOrDefault(r => string.Equals(r.GameHash ?? string.Empty, key, StringComparison.OrdinalIgnoreCase));
                        if (existingRecord != null)
                        {
                            // Update if PDB hash changed (case insensitive comparison)
                            if (!string.Equals(existingRecord.PdbHash, onlineRecord.PdbHash, StringComparison.OrdinalIgnoreCase))
                            {
                                var idx = gameRecords.IndexOf(existingRecord);
                                if (idx >= 0)
                                    gameRecords[idx] = onlineRecord;
                                else
                                    gameRecords.Add(onlineRecord);
                                updated = true;
                            }
                        }
                        else
                        {
                            // Add new record
                            gameRecords.Add(onlineRecord);
                            updated = true;
                        }
                    }

                    if (updated)
                    {
                        MLog.Information($@"Merged online {ServiceLoggingName} into local version");
                        CommitDatabaseToDisk();
                    }
                    else
                    {
                        MLog.Information($@"Local {ServiceLoggingName} is up to date with online version");
                    }
                    return;
                }
                catch (Exception ex)
                {
                    MLog.Error($@"Failed to load online {ServiceLoggingName}: {ex.Message}");
                    return;
                }
            }
        }

        private static void LoadLocalData(Dictionary<MEGame, List<SymbolRecord>> database)
        {
            var file = GetLocalServiceCacheFile();
            if (File.Exists(file))
            {
                try
                {
                    var fileText = File.ReadAllText(file);
                    var dbNew = JsonConvert.DeserializeObject<Dictionary<MEGame, List<SymbolRecord>>>(fileText);
                    if (dbNew != null)
                    {
                        database.Clear();
                        foreach (var kvp in dbNew)
                        {
                            database[kvp.Key] = kvp.Value ?? new List<SymbolRecord>();
                        }
                        MLog.Information($@"Loaded local {ServiceLoggingName}");
                        return;
                    }
                }
                catch (Exception e)
                {
                    MLog.Error($@"Error loading local {ServiceLoggingName}: {e.Message}");
                }
            }
            else
            {
                MLog.Information($@"Loaded blank local {ServiceLoggingName}");
                database.Clear();
            }
        }

        private static void CommitDatabaseToDisk()
        {
#if DEBUG
            var outText = JsonConvert.SerializeObject(Database, Formatting.Indented,
                new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });
#else
            var outText = JsonConvert.SerializeObject(Database, 
                new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });
#endif
            try
            {
                File.WriteAllText(GetLocalServiceCacheFile(), outText);
                MLog.Information($@"Updated local {ServiceLoggingName}");
            }
            catch (Exception e)
            {
                MLog.Error($@"Error saving local {ServiceLoggingName}: {e.Message}");
            }
        }

        /// <summary>
        /// Gets symbol records for a specific game
        /// </summary>
        /// <param name="game">The game to get symbol records for</param>
        /// <returns>List of symbol records for the game, or empty list if none found</returns>
        public static List<SymbolRecord> GetSymbolsForGame(MEGame game)
        {
            lock (syncObj)
            {
                if (!ServiceLoaded || Database == null)
                    return new List<SymbolRecord>();

                if (Database.TryGetValue(game, out var records))
                {
                    return new List<SymbolRecord>(records);
                }
                return new List<SymbolRecord>();
            }
        }

        /// <summary>
        /// Loads the symbol service with optional online data
        /// </summary>
        /// <param name="data">The online service data</param>
        /// <returns>True if service was loaded successfully</returns>
        public static bool LoadService(JToken data)
        {
            InternalLoadService(data);
            ServiceLoaded = true;
            return true;
        }

        private static (SymbolRecord match, string exePath, string exeHash) FindMatchingSymbolRecord(GameTarget target)
        {
            if (target == null) return (null, null, null);

            var exePath = M3Directories.GetExecutablePath(target);
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                MLog.Error($@"Executable not found for target: {target.TargetPath}");
                return (null, exePath, null);
            }

            var exeFileInfo = new FileInfo(exePath);
            long exeSize = exeFileInfo.Length;

            // Find if any symbol record matches the executable size first to avoid
            // hashing large files unnecessarily.
            var candidates = GetSymbolsForGame(target.Game);
            if (!candidates.Any(r => r != null && r.GameSize == (int)exeSize))
            {
                // No symbols for game.
                return (null, exePath, null);
            }

            var exeHash = MUtilities.CalculateHash(exePath);

            // Find matching SymbolRecord by size then hash
            SymbolRecord match = null;
            foreach (var rec in candidates)
            {
                if (rec.GameSize != (int)exeSize)
                    continue;
                if (rec.GameHash == exeHash)
                {
                    match = rec;
                    break;
                }
            }

            return (match, exePath, exeHash);
        }


        /// <summary>
        /// Attempts to apply matching PDB symbols for the specified game target by
        /// locating a matching SymbolRecord in the database and copying the matching
        /// PDB from the shared ME3Tweaks cache Symbols folder into the game's
        /// executable folder.
        /// </summary>
        /// <param name="target">Game target to apply symbols for</param>
        public static async Task ApplySymbols(GameTarget target)
        {
            try
            {
                if (target == null) return;
                if (!target.Game.IsLEGame()) return; // Only supported on LE 
                var (match, exePath, exeHash) = FindMatchingSymbolRecord(target);
                if (match == null)
                {
                    return;
                }

                // Check if game already has the correct PDB installed in the executable folder
                var destDir = Path.GetDirectoryName(exePath);
                var destPath = Path.Combine(destDir, Path.GetFileNameWithoutExtension(target.GetExecutableNames()[0]) + @".pdb");
                if (File.Exists(destPath))
                {
                    try
                    {
                        var installedFi = new FileInfo(destPath);
                        if (installedFi.Length == match.PdbSize)
                        {
                            var installedHash = MUtilities.CalculateHash(destPath);
                            if (installedHash == match.PdbHash)
                            {
                                MLog.Information($@"PDB already installed and up to date at {destPath}");
                                return;
                            }
                        }
                        else
                        {
                            MLog.Information($@"Installed PDB size mismatch: {installedFi.Length} != {match.PdbSize}, will attempt to update");
                        }
                    }
                    catch (Exception ex)
                    {
                        MLog.Warning($@"Failed to verify existing PDB at {destPath}: {ex.Message}");
                    }
                }

                var cachedPdbPath = match.GetCachedPath();

                // If the PDB isn't cached locally, try to download it using the SymbolRecord helper
                if (!File.Exists(cachedPdbPath))
                {
                    MLog.Information($@"PDB not found in cache, attempting download for {match.GetStoredPDBName()}");
                    try
                    {
                        var downloaded = await match.DownloadPDBAsync();
                        if (!downloaded)
                        {
                            MLog.Warning($@"Failed to download PDB for {match.GetStoredPDBName()}");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        MLog.Warning($@"Exception downloading PDB for {match.GetStoredPDBName()}: {ex.Message}");
                        return;
                    }
                }

                // Verify PDB size and hash
                var fi = new FileInfo(cachedPdbPath);
                if (fi.Length != match.PdbSize)
                {
                    MLog.Warning($@"Cached PDB size mismatch: {fi.Length} != {match.PdbSize}");
                    return;
                }

                var pdbHash = MUtilities.CalculateHash(cachedPdbPath);
                if (!string.Equals(pdbHash ?? string.Empty, match.PdbHash ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    MLog.Warning($@"Cached PDB hash mismatch: {pdbHash} != {match.PdbHash}");
                    return;
                }

                // Copy to executable folder
                Directory.CreateDirectory(destDir);
                try
                {
                    File.Copy(cachedPdbPath, destPath, true);
                    MLog.Information($@"Copied symbols to {destPath}");
                }
                catch (Exception ex)
                {
                    MLog.Error($@"Failed to copy PDB to game folder: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                MLog.Error($@"Error applying symbols: {ex.Message}");
            }
        }
    }
}
