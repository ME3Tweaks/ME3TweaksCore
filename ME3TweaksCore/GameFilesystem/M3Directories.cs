using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Services;
using ME3TweaksCore.Targets;
using Serilog;

namespace ME3TweaksCore.GameFilesystem
{
    /// <summary>
    /// Interposer for GameTarget -> MEDirectories, some convenience methods
    /// </summary>
    public static class M3Directories
    {
        #region INTERPOSERS
        public static string GetBioGamePath(this GameTarget target) => MEDirectories.GetBioGamePath(target.Game, target.TargetPath);
        public static string GetDLCPath(this GameTarget target) => MEDirectories.GetDLCPath(target.Game, target.TargetPath);
        public static string GetCookedPath(this GameTarget target) => MEDirectories.GetCookedPath(target.Game, target.TargetPath);

        /// <summary>
        /// Gets executable path
        /// </summary>
        /// <param name="target"></param>
        /// <param name="preferRealGameExe">Prefer ME2 game exe (ME2Game.exe) vs MassEffect2.exe</param>
        /// <returns></returns>
        public static string GetExecutablePath(this GameTarget target, bool preferRealGameExe = false)
        {
            if (target.Game == MEGame.ME2 && preferRealGameExe)
            {
                // Prefer ME2Game.exe if it exists
                var executableFolder = GetExecutableDirectory(target);
                var exeReal = Path.Combine(executableFolder, @"ME2Game.exe");
                if (File.Exists(exeReal))
                {
                    return exeReal;
                }
            }
            else if (target.Game == MEGame.LELauncher)
            {
                // LE LAUNCHER
                return Path.Combine(target.TargetPath, @"MassEffectLauncher.exe");
            }
            return MEDirectories.GetExecutablePath(target.Game, target.TargetPath);
        }

        public static string GetExecutableDirectory(this GameTarget target)
        {
            if (target.Game == MEGame.LELauncher) return target.TargetPath; // LELauncher
            return MEDirectories.GetExecutableFolderPath(target.Game, target.TargetPath);
        }
        public static string GetLODConfigFile(this GameTarget target) => MEDirectories.GetLODConfigFile(target.Game, target.TargetPath);
        public static string GetTextureMarkerPath(this GameTarget target) => MEDirectories.GetTextureModMarkerPath(target.Game, target.TargetPath);
        public static string GetASIPath(this GameTarget target) => MEDirectories.GetASIPath(target.Game, target.TargetPath);
        public static string GetTestPatchSFARPath(this GameTarget target)
        {
            if (target.Game != MEGame.ME3) throw new Exception(@"Cannot fetch TestPatch SFAR for games that are not ME3");
            return ME3Directory.GetTestPatchSFARPath(target.TargetPath);
        }
        public static ReadOnlyCollection<string> GetExecutableNames(this GameTarget target) => MEDirectories.ExecutableNames(target.Game);

        /// <summary>
        /// Fetches the INT coalesced file path from the target. ME1 is not supported as it does not have a single file path
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static string GetCoalescedPath(this GameTarget target)
        {
            if (target.Game == MEGame.ME2)
                return Path.Combine(GetBioGamePath(target), @"Config", @"PC", @"Cooked", @"Coalesced.ini");
            if (target.Game is MEGame.LE1 or MEGame.LE2)
                return Path.Combine(GetCookedPath(target), @"Coalesced_INT.bin");
            if (target.Game.IsGame3())
                return Path.Combine(GetCookedPath(target), @"Coalesced.bin");

            throw new Exception($@"Cannot fetch combined Coalesced path for unsupported game: {target.Game}");
        }
        public static bool IsInBasegame(string file, GameTarget target) => MEDirectories.IsInBasegame(file, target.Game, target.TargetPath);
        public static bool IsInOfficialDLC(string file, GameTarget target) => MEDirectories.IsInOfficialDLC(file, target.Game, target.TargetPath);
        public static List<string> EnumerateGameFiles(this GameTarget validationTarget, Predicate<string> predicate = null)
        {
            return MEDirectories.EnumerateGameFiles(validationTarget.Game, validationTarget.TargetPath, predicate: predicate);
        }


        public static CaseInsensitiveDictionary<string> GetFilesLoadedInGame(this GameTarget target, bool forceReload = false, bool includeTFCs = false, bool includeAFCs = false, bool forceUseCached = false)
        {
            return MELoadedFiles.GetFilesLoadedInGame(target.Game, forceReload, includeTFCs, includeAFCs, target.TargetPath, forceUseCached);
        }
        #endregion

        public static Dictionary<string, int> GetMountPriorities(this GameTarget selectedTarget)
        {
            //make dictionary from basegame files
            var dlcmods = VanillaDatabaseService.GetInstalledDLCMods(selectedTarget);
            var mountMapping = new Dictionary<string, int>();
            foreach (var dlc in dlcmods)
            {
                var mountpath = Path.Combine(M3Directories.GetDLCPath(selectedTarget), dlc);
                try
                {
                    mountMapping[dlc] = MELoadedDLC.GetMountPriority(mountpath, selectedTarget.Game);
                }
                catch (Exception e)
                {
                    MLog.Error($@"Exception getting mount priority from file: {mountpath}: {e.Message}");
                }
            }

            return mountMapping;
        }

        /// <summary>
        /// Gets a list of superceding package files from the DLC of the game. Only files in DLC mods are returned
        /// </summary>
        /// <param name="target">Target to get supercedances for</param>
        /// <returns>Dictionary mapping filename to list of DLCs that contain that file, in order of highest priority to lowest</returns>
        public static Dictionary<string, List<string>> GetFileSupercedances(this GameTarget target, string[] additionalExtensionsToInclude = null)
        {
            //make dictionary from basegame files
            var fileListMapping = new CaseInsensitiveDictionary<List<string>>();
            var directories = MELoadedDLC.GetEnabledDLCFolders(target.Game, target.TargetPath).OrderBy(dir => MELoadedDLC.GetMountPriority(dir, target.Game)).ToList();
            foreach (string directory in directories)
            {
                var dlc = Path.GetFileName(directory);
                if (MEDirectories.OfficialDLC(target.Game).Contains(dlc)) continue; //skip
                foreach (string filePath in MELoadedFiles.GetCookedFiles(target.Game, directory, false, additionalExtensions: additionalExtensionsToInclude))
                {
                    string fileName = Path.GetFileName(filePath);
                    if (fileName != null && fileName.RepresentsPackageFilePath() || (additionalExtensionsToInclude != null && additionalExtensionsToInclude.Contains(Path.GetExtension(fileName))))
                    {
                        if (fileListMapping.TryGetValue(fileName, out var supercedingList))
                        {
                            supercedingList.Insert(0, dlc);
                        }
                        else
                        {
                            fileListMapping[fileName] = new List<string>(new[] { dlc });
                        }
                    }
                }
            }

            return fileListMapping;
        }

        /// <summary>
        /// Given a game and executable path, returns the basepath of the installation.
        /// </summary>
        /// <param name="game">What game this exe is for</param>
        /// <param name="exe">Executable path</param>
        /// <returns></returns>
        public static string GetGamePathFromExe(MEGame game, string exe)
        {
            string result = Path.GetDirectoryName(exe);
            if (game == MEGame.LELauncher)
                return result;
            result = Path.GetDirectoryName(result); //binaries, <GAME>
            if (game == MEGame.ME3 || game.IsLEGame())
                result = Path.GetDirectoryName(result); //up one more because of win32/win64 directory.
            return result;
        }

        // Needs moved into a M3 specific class. Probably subclass of GameTarget, like M3GameTarget
        //internal static bool IsOfficialDLCInstalled(ModJob.JobHeader header, GameTarget gameTarget)
        //{
        //    if (header == ModJob.JobHeader.BALANCE_CHANGES) return true; //Don't check balance changes
        //    if (header == ModJob.JobHeader.ME2_RCWMOD) return true; //Don't check
        //    if (header == ModJob.JobHeader.ME1_CONFIG) return true; //Don't check
        //    if (header == ModJob.JobHeader.BASEGAME) return true; //Don't check basegame
        //    if (header == ModJob.JobHeader.CUSTOMDLC) return true; //Don't check custom dlc
        //    if (header == ModJob.JobHeader.LOCALIZATION) return true; //Don't check localization
        //    if (header == ModJob.JobHeader.LELAUNCHER) return true; //Don't check launcher
        //    if (header == ModJob.JobHeader.GAME1_EMBEDDED_TLK) return true; //Don't check launcher

        //    if (header == ModJob.JobHeader.TESTPATCH)
        //    {
        //        return File.Exists(GetTestPatchSFARPath(gameTarget));
        //    }
        //    else
        //    {
        //        return M3Directories.GetInstalledDLC(gameTarget).Contains(ModJob.GetHeadersToDLCNamesMap(gameTarget.Game)[header]);
        //    }
        //}
    }
}
