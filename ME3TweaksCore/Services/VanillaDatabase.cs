using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Helpers;

namespace ME3TweaksCore.Services
{
    /// <summary>
    /// Hash algorithm the vanilla database uses
    /// </summary>
    public enum VanillaHashAlgorithm : byte
    {
        /// <summary>
        /// Default
        /// </summary>
        MD5 = 0,
        /// <summary>
        /// Milan Slash version
        /// </summary>
        SHA256 = 1,
    }

    /// <summary>
    /// A vanilla database, along with the game it's for and which hash its entries use
    /// </summary>
    public class VanillaDatabase
    {
        public MEGame Game { get; }
        public VanillaHashAlgorithm Algorithm { get; }

        /// <summary>
        /// Relative path to accepted size/hash pairs. The md5 field holds whatever hash <see cref="Algorithm"/> is.
        /// </summary>
        public CaseInsensitiveConcurrentDictionary<List<(int size, string md5)>> Files { get; }

        public VanillaDatabase(MEGame game, VanillaHashAlgorithm algorithm, CaseInsensitiveConcurrentDictionary<List<(int size, string md5)>> files)
        {
            Game = game;
            Algorithm = algorithm;
            Files = files;
        }

        /// <summary>
        /// Name to pass to the hashing algorithm to get a hash comparable to this database's
        /// </summary>
        public string HashAlgorithmName => Algorithm == VanillaHashAlgorithm.SHA256 ? @"sha256" : null;

        /// <summary>
        /// Calculates the hash for the specific file (matching this DB)
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public string CalculateHash(string filePath) => MUtilities.CalculateHash(filePath, HashAlgorithmName);

        /// <summary>
        /// Get info about a specific file
        /// </summary>
        /// <param name="relativePath"></param>
        /// <returns></returns>
        public List<(int size, string md5)> GetFileInfo(string relativePath) => Files.TryGetValue(relativePath, out var info) ? info : null;

        /// <summary>
        /// Checks a file by size and by hash if one is given
        /// </summary>
        public bool IsFileVanilla(string relativePath, long fileSize, string hash = null)
        {
            if (IsUserConfigFile(relativePath))
            {
                return true;
            }

            if (!Files.TryGetValue(relativePath, out var info) || info.All(x => x.size != fileSize))
            {
                return false;
            }

            return hash == null || info.Any(x => string.Equals(x.md5, hash, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Checks if a file on disk is vanilla
        /// </summary>
        public bool IsFileVanilla(string relativePath, FileInfo file, bool hashCheck = false, string hash = null)
        {
            if (IsUserConfigFile(relativePath))
            {
                return true;
            }

            if (!IsFileVanilla(relativePath, file.Length))
            {
                return false;
            }

            if (!hashCheck)
            {
                return true;
            }

            return IsFileVanilla(relativePath, file.Length, hash ?? CalculateHash(file.FullName));
        }

        /// <summary>
        /// Ignored user config files
        /// </summary>
        /// <param name="relativePath"></param>
        /// <returns></returns>
        private bool IsUserConfigFile(string relativePath) => Game.IsLEGame() && relativePath.StartsWith(@"BioGame\Config\", StringComparison.OrdinalIgnoreCase);
    }
}
