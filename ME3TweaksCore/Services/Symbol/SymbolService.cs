using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ME3TweaksCore.Services.Symbol
{
    /// <summary>
    /// Service for managing debugging symbol information for game executables
    /// </summary>
    public class SymbolService
    {
        /// <summary>
        /// Database of game symbols indexed by game
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
                            gameRecords = new List<SymbolRecord>();
                            database[onlineRecord.Game] = gameRecords;
                        }

                        // Check if this record already exists (matching game and game hash - case insensitive)
                        var existingRecord = gameRecords.FirstOrDefault(r => 
                            string.Equals(r.GameHash, onlineRecord.GameHash, StringComparison.OrdinalIgnoreCase));

                        if (existingRecord != null)
                        {
                            // Update if PDB hash changed (case insensitive comparison)
                            if (!string.Equals(existingRecord.PdbHash, onlineRecord.PdbHash, StringComparison.OrdinalIgnoreCase))
                            {
                                // Remove old record and add updated one to maintain immutability
                                gameRecords.Remove(existingRecord);
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
                    var db = JsonConvert.DeserializeObject<Dictionary<MEGame, List<SymbolRecord>>>(File.ReadAllText(file));
                    if (db != null)
                    {
                        database.Clear();
                        foreach (var kvp in db)
                        {
                            database[kvp.Key] = kvp.Value;
                        }
                        MLog.Information($@"Loaded local {ServiceLoggingName}");
                    }
                    else
                    {
                        MLog.Warning($@"Local {ServiceLoggingName} file deserialized to null");
                        database.Clear();
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

                return Database.TryGetValue(game, out var records) 
                    ? new List<SymbolRecord>(records) 
                    : new List<SymbolRecord>();
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
    }
}
