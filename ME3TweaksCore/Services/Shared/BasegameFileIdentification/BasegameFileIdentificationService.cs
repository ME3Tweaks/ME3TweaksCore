using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LegendaryExplorerCore.Gammtek.Extensions.Collections.Generic;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Targets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ME3TweaksCore.Services.Shared.BasegameFileIdentification
{
    /// <summary>
    /// Service for identifying basegame file changes based on a shared database
    /// </summary>
    public class BasegameFileIdentificationService
    {
        /// <summary>
        /// Database of locally installed files. The mapping is as follows:
        /// [GameNameAsString] -> Dictionary of relative file paths -> List of records about that file. E.g. Size, hash, name.
        /// </summary>
        private static Dictionary<string, CaseInsensitiveDictionary<List<BasegameFileRecord>>> Database;

        /// <summary>
        /// If the BasegameFileIdentificationService has been initially loaded
        /// </summary>
        public static bool ServiceLoaded { get; private set; }

        /// <summary>
        /// Service name for logging
        /// </summary>
        private const string ServiceLoggingName = @"Basegame File Identification Service";

        /// <summary>
        /// Loads the shared Basegame File Identification Service database from disk
        /// </summary>
        private static void LoadSharedBasegameIdentificationService()
        {
            if (Database != null) return;
            Database = new CaseInsensitiveDictionary<CaseInsensitiveDictionary<List<BasegameFileRecord>>>();
            LoadDatabase(Database);
        }

        /// <summary>
        /// Loads a Basegame File Identification Service database from disk
        /// </summary>
        /// <param name="database"></param>
        /// <param name="serverData"></param>
        private static void LoadDatabase(Dictionary<string, CaseInsensitiveDictionary<List<BasegameFileRecord>>> database, JToken serverData = null)
        {
            var file = MCoreFilesystem.GetSharedBasegameIdentificationServiceFile();
            if (File.Exists(file))
            {
                var attemptsLeft = 3;

                while (attemptsLeft > 0)
                {
                    attemptsLeft--;

                    try
                    {
                        var db = JsonConvert.DeserializeObject<Dictionary<string, CaseInsensitiveDictionary<List<BasegameFileRecord>>>>(File.ReadAllText(file));
                        database.ReplaceAll(db);

                        // LEGACY APP-SPECIFIC DB
                        var legacyFilePath = MCoreFilesystem.GetAppSpecificBasegameFileIdentificationServiceFile();
                        if (File.Exists(legacyFilePath))
                        {
                            // merge in legacy file
                            var db2 = JsonConvert.DeserializeObject<Dictionary<string, CaseInsensitiveDictionary<List<BasegameFileRecord>>>>(File.ReadAllText(legacyFilePath));
                            mergeDatabase(db2, database);
                        }
                        MLog.Information($@"Loaded {ServiceLoggingName}");
                        break;
                    }
                    catch (Exception e)
                    {
                        MLog.Error($@"Error loading {ServiceLoggingName}: {e.Message}");
                        var db = getBlankBGFIDB();
                        database.ReplaceAll(db);
                        Thread.Sleep(1000); // This should be more than enough time for the record to resave
                    }
                }
            }
            else
            {
                MLog.Information($@"Loaded blank {ServiceLoggingName}");
                var db = getBlankBGFIDB();
                database.ReplaceAll(db);
            }
        }

        /// <summary>
        /// Merges 2 basegame file identification service databases
        /// </summary>
        /// <param name="incoming">Database to merge</param>
        /// <param name="database">Database to merge into</param>
        private static void mergeDatabase(Dictionary<string, CaseInsensitiveDictionary<List<BasegameFileRecord>>> incoming, Dictionary<string, CaseInsensitiveDictionary<List<BasegameFileRecord>>> target)
        {
            MLog.Information(@"Merging basegame file identification databases (in memory)");
            foreach (var game in incoming) // For each game
            {
                // Find target
                if (target.TryGetValue(game.Key, out var matchingGame))
                {
                    foreach (var entry in game.Value) // For each file in game
                    {
                        if (matchingGame.TryGetValue(entry.Key, out var matchingInstances))
                        {
                            foreach (var instanceEntry in entry.Value) // For each instance in file
                            {
                                if (!matchingInstances.Contains(instanceEntry))
                                    matchingInstances.Add(instanceEntry); // Merge single instance
                            }
                        }
                        else
                        {
                            // Merge whole entry
                            matchingGame[entry.Key] = entry.Value;
                        }
                    }
                }
                else
                {
                    target[game.Key] = game.Value; // Merge whole game
                }
            }
        }

        public static void AddLocalBasegameIdentificationEntries(List<BasegameFileRecord> entries)
        {
            LoadSharedBasegameIdentificationService();

            bool updated = false;
            // Update the DB
            foreach (var entry in entries)
            {
                string gameKey = entry.game == @"0"
                    ? @"LELAUNCHER"
                    : MUtilities.GetGameFromNumber(entry.game).ToString();
                if (Database.TryGetValue(gameKey, out var gameDB))
                {
                    List<BasegameFileRecord> existingInfos;
                    if (!gameDB.TryGetValue(entry.file, out existingInfos))
                    {
                        existingInfos = new List<BasegameFileRecord>();
                        gameDB[entry.file] = existingInfos;
                    }

                    if (existingInfos.All(x => x.hash != entry.hash))
                    {
                        // new info
                        entry.file = null; // Do not serialize this
                        entry.game = null; // Do not serialize this
                        existingInfos.Add(entry);
                        updated = true;
                    }
                }
            }

            // Serialize it back to disk
            if (updated)
            {
                CommitDatabaseToDisk();
            }
            else
            {
                MLog.Information($@"Local {ServiceLoggingName} did not need updating");
            }
        }

        private static void CommitDatabaseToDisk()
        {

#if DEBUG
            var outText = JsonConvert.SerializeObject(Database, Formatting.Indented,
                new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });
#else
                var outText =
 JsonConvert.SerializeObject(Database, new JsonSerializerSettings() { NullValueHandling =
 NullValueHandling.Ignore });
#endif
            var attemptsLeft = 6;
            while (attemptsLeft > 0)
            {
                attemptsLeft--;
                try
                {
                    MLog.Information($@"Updating shared {ServiceLoggingName}");
                    File.WriteAllText(MCoreFilesystem.GetSharedBasegameIdentificationServiceFile(), outText);
                    MLog.Information($@"Updated shared {ServiceLoggingName}");
                    break;

                }
                catch (Exception e)
                {
                    // bwomp bwomp
                    MLog.Error($@"Error saving shared BGFIS: {e.Message}");
                }
                Thread.Sleep(500);
            }
        }

        /// <summary>
        /// Looks up information about a basegame file using the BasegameFileIdentificationService
        /// </summary>
        /// <param name="target"></param>
        /// <param name="fullfilepath"></param>
        /// <returns></returns>
        public static BasegameFileRecord GetBasegameFileSource(GameTarget target, string fullfilepath, string md5 = null)
        {
            return GetBasegameFileSource(target.Game, target.TargetPath, fullfilepath, md5);
        }

        /// <summary>
        /// Looks up information about a basegame file using the BasegameFileIdentificationService
        /// </summary>
        /// <returns></returns>
        public static BasegameFileRecord GetBasegameFileSource(MEGame game, string rootPath, string fullfilepath, string md5 = null)
        {
            LoadSharedBasegameIdentificationService();
            if (Database.TryGetValue(game.ToString(), out var infosForGameL))
            {
                var relativeFilename = fullfilepath.Substring(rootPath.Length + 1).ToUpper();

                if (infosForGameL.TryGetValue(relativeFilename, out var items))
                {
                    md5 ??= MUtilities.CalculateHash(fullfilepath);
                    var match = items.FirstOrDefault(x => x.hash == md5);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns a blank Basegame Identification Database
        /// </summary>
        /// <returns></returns>
        private static Dictionary<string, CaseInsensitiveDictionary<List<BasegameFileRecord>>> getBlankBGFIDB()
        {
            return new Dictionary<string, CaseInsensitiveDictionary<List<BasegameFileRecord>>>
            {
                [@"ME1"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
                [@"ME2"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
                [@"ME3"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
                [@"LE1"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
                [@"LE2"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
                [@"LE3"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
                [@"LELauncher"] = new CaseInsensitiveDictionary<List<BasegameFileRecord>>(),
            };
        }

        // This service doesn't take any data but uses the service loader model.
        public static bool LoadService(JToken data)
        {
            LoadSharedBasegameIdentificationService();
            ServiceLoaded = true;
            return true;
        }

        private static object syncObj = new object();

        /// <summary>
        /// Purges all entries for the specified game and commits the file back to disk.
        /// </summary>
        /// <param name="game">The game to purge entries for</param>
        public static void PurgeEntriesForGame(MEGame game)
        {
            lock (syncObj)
            {
                if (Database.TryGetValue(game.ToString(), out var infosForGameL))
                {
                    MLog.Information($@"Clearing basegame filedatabase entries for {game}");
                    infosForGameL.Clear();
                    CommitDatabaseToDisk();
                }

                // Delete legacy file for consistency on older code.s
                try
                {
                    var legacyFile = MCoreFilesystem.GetAppSpecificBasegameFileIdentificationServiceFile();
                    if (File.Exists(legacyFile))
                    {
                        File.Delete(legacyFile);
                    }
                }
                catch
                {
                    // Do not care.
                }
            }
        }

        /// <summary>
        /// Returns all entries for a given game
        /// </summary>
        /// <param name="game">The game to get entries for</param>
        /// <returns>Dictionary mapping for the game</returns>
        public static CaseInsensitiveDictionary<List<BasegameFileRecord>> GetEntriesForGame(MEGame game)
        {
            if (Database.TryGetValue(game.ToString(), out var gameEntries))
            {
                return gameEntries;
            }

            return new CaseInsensitiveDictionary<List<BasegameFileRecord>>(0); // Return nothing.
        }

        /// <summary>
        /// Returns all entries for a given list of files for a game
        /// </summary>
        /// <param name="game"></param>
        /// <param name="files"></param>
        /// <returns></returns>
        public static CaseInsensitiveDictionary<List<BasegameFileRecord>> GetEntriesForFiles(MEGame game, List<string> files)
        {
            CaseInsensitiveDictionary<List<BasegameFileRecord>> records = new(files.Count);
            if (Database.TryGetValue(game.ToString(), out var gameEntries))
            {
                foreach (var f in files)
                {
                    if (gameEntries.TryGetValue(f, out var list))
                    {
                        records[f] = list;
                    }
                }
            }

            return records;
        }
    }
}