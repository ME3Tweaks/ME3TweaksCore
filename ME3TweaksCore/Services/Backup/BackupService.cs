using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using LegendaryExplorerCore.Gammtek.Extensions;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Targets;
using Microsoft.Win32;

namespace ME3TweaksCore.Services.Backup
{
    /// <summary>
    /// Contains methods and bindable variables for accessing and displaying info about game backups 
    /// </summary>
    public static class BackupService
    {
        #region Static Property Changed

        public static event PropertyChangedEventHandler StaticPropertyChanged;

        /// <summary>
        /// Called when a game backup state changes between BackedUp and not BackedUp.
        /// </summary>
        public static event EventHandler StaticBackupStateChanged;

        /// <summary>
        /// Sets given property and notifies listeners of its change. IGNORES setting the property to same value.
        /// Should be called in property setters.
        /// </summary>
        /// <typeparam name="T">Type of given property.</typeparam>
        /// <param name="field">Backing field to update.</param>
        /// <param name="value">New value of property.</param>
        /// <param name="propertyName">Name of property.</param>
        /// <returns>True if success, false if backing field and new value aren't compatible.</returns>
        private static bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
            return true;
        }
        #endregion

        public static ObservableCollectionExtended<GameBackupStatus> GameBackupStatuses { get; } = new ObservableCollectionExtended<GameBackupStatus>();

        /// <summary>
        /// Vanilla backup marker filename
        /// </summary>
        public const string CMM_VANILLA_FILENAME = @"cmm_vanilla";

        /// <summary>
        /// Initializes the backup service.
        /// </summary>
        public static void InitBackupService(Action<Action> runCodeOnUIThreadCallback, bool refreshStatuses = true, bool logPaths = false)
        {
            MLog.Information($@"Initializing backup service");

            void runOnUiThread()
            {
                GameBackupStatuses.Add(new GameBackupStatus(MEGame.ME1));
                GameBackupStatuses.Add(new GameBackupStatus(MEGame.ME2));
                GameBackupStatuses.Add(new GameBackupStatus(MEGame.ME3));
                GameBackupStatuses.Add(new GameBackupStatus(MEGame.LE1));
                GameBackupStatuses.Add(new GameBackupStatus(MEGame.LE2));
                GameBackupStatuses.Add(new GameBackupStatus(MEGame.LE3));
                GameBackupStatuses.Add(new GameBackupStatus(MEGame.LMP));
                GameBackupStatuses.Add(new GameBackupStatus(MEGame.LELauncher));
            }

            runCodeOnUIThreadCallback.Invoke(runOnUiThread);

            foreach (var v in GameBackupStatuses)
            {
                v.OnBackupStateChanged += InternalOnBackupStateChanged;
            }

            if (refreshStatuses)
                RefreshBackupStatus(null, log: true);
        }

        /// <summary>
        /// Called when any of the backup status states change.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void InternalOnBackupStateChanged(object sender, EventArgs e)
        {
            StaticBackupStateChanged?.Invoke(sender, EventArgs.Empty);;
        }


        public static GameBackupStatus GetBackupStatus(MEGame game)
        {
            return GameBackupStatuses.FirstOrDefault(x => x.Game == game);
        }

        /// <summary>
        /// Refreshes the backup status of the listed game, or all if none is specified.
        /// </summary>
        /// <param name="allTargets">List of targets to determine if the game is installed or not. Passing null will assume the game is installed</param>
        /// <param name="forceCmmVanilla">If the backups will be forced to have the cmmVanilla file to be considered valid</param>
        /// <param name="game">What game to refresh. Set to unknown to refresh all.</param>
        public static void RefreshBackupStatus(List<GameTarget> allTargets = null, MEGame game = MEGame.Unknown, bool log = false)
        {
            foreach (var v in GameBackupStatuses)
            {
                if (v.Game == game || game == MEGame.Unknown)
                {
                    v.RefreshBackupStatus(allTargets == null || allTargets.Any(x => x.Game == game), log);
                }
            }
        }

        /// <summary>
        /// Sets the status of a backup.
        /// </summary>
        /// <param name="game"></param>
        /// <param name="checkingBackup"></param>
        /// <param name="pleaseWait"></param>
        public static void SetStatus(MEGame game, string status, string tooltip)
        {
            //switch (game)
            //{
            //    case MEGame.ME1:
            //        ME1BackupStatus = status;
            //        ME1BackupStatusTooltip = tooltip;
            //        break;
            //    case MEGame.ME2:
            //        ME2BackupStatus = status;
            //        ME2BackupStatusTooltip = tooltip;
            //        break;
            //    case MEGame.ME3:
            //        ME3BackupStatus = status;
            //        ME3BackupStatusTooltip = tooltip;
            //        break;
            //}
        }

        //public static void SetActivity(MEGame game, bool p1)
        //{
        //    switch (game)
        //    {
        //        case MEGame.ME1:
        //            ME1BackupActivity = p1;
        //            break;
        //        case MEGame.ME2:
        //            ME2BackupActivity = p1;
        //            break;
        //        case MEGame.ME3:
        //            ME3BackupActivity = p1;
        //            break;
        //    }
        //}


        /// <summary>
        /// Makes ME3Tweaks software not know about a backup.
        /// </summary>
        /// <param name="meGame"></param>
        public static void UnlinkBackup(MEGame meGame)
        {
            MLog.Information($@"Unlinking backup for {meGame}");
            var gbPath = BackupService.GetGameBackupPath(meGame, forceReturnPath: true);
            if (gbPath != null)
            {
                var cmmVanilla = Path.Combine(gbPath, CMM_VANILLA_FILENAME);
                if (File.Exists(cmmVanilla))
                {
                    MLog.Information($@"Deleting {CMM_VANILLA_FILENAME} file: {cmmVanilla}");
                    File.Delete(cmmVanilla);
                }
            }
            BackupService.RemoveBackupPath(meGame);
            RefreshBackupStatus(game: meGame);
        }

        /// <summary>
        /// Deletes the registry key used to store the backup location
        /// </summary>
        /// <param name="game"></param>
        private static void RemoveBackupPath(MEGame game)
        {
            MSharedSettings.DeleteSetting(game + @"VanillaBackupLocation");

            // Strip pre 7.0
            if (game == MEGame.ME3)
            {
                RegistryHandler.DeleteRegistryKey(Registry.CurrentUser, @"Software\Mass Effect 3 Mod Manager", @"VanillaCopyLocation");
            }
            else if (game == MEGame.ME1 || game == MEGame.ME2)
            {
                RegistryHandler.DeleteRegistryKey(Registry.CurrentUser, @"Software\ALOTAddon", game + @"VanillaBackupLocation");
            }
        }

        public static string GetGameBackupPath(MEGame game, bool logReturnedPath = false, bool forceReturnPath = false, bool refresh = false)
        {
            // Todo: Compare with M3 build 123 as it has a refresh variable. Maybe to cache lookups?

            var path = MSharedSettings.GetSettingString($@"{game}VanillaBackupLocation");

            if (forceReturnPath)
            {
                return path; // do not check it
            }

            if (logReturnedPath)
            {
                MLog.Information($@" >> Backup path lookup for {game} returned: {path}");
            }

            if (path == null || !Directory.Exists(path))
            {
                if (logReturnedPath)
                {
                    MLog.Information(@" >> Path is null or directory doesn't exist.");
                }

                return null;
            }

            //Super basic validation
            if (game != MEGame.LELauncher && (!Directory.Exists(Path.Combine(path, @"BIOGame")) || !Directory.Exists(Path.Combine(path, @"Binaries"))))
            {
                if (logReturnedPath)
                {
                    MLog.Warning($@" >> {path} is missing BioGame/Binaries subdirectory, invalid backup");
                }

                return null;
            }
            else if (game == MEGame.LELauncher && !Directory.Exists(Path.Combine(path, @"Content")))
            {
                if (logReturnedPath)
                {
                    MLog.Warning($@" >> {path} is missing Content directory, invalid backup");
                }

                return null;
            }

            var hasVanillaMarker = File.Exists(Path.Combine(path, @"cmm_vanilla"));

            if (!hasVanillaMarker)
            {
                if (logReturnedPath)
                {
                    MLog.Warning($@" >> {path} is not marked as a vanilla backup. ME3TweaksCore does not consider this a vanilla backup and will not use it");
                }

                return null;
            }

            if (logReturnedPath)
            {
                MLog.Information($@" >> {path} is considered a valid backup path");
            }

            return path;
        }

        //public static void SetBackedUp(MEGame game, bool b)
        //{
        //    switch (game)
        //    {
        //        case MEGame.ME1:
        //            ME1BackedUp = b;
        //            break;
        //        case MEGame.ME2:
        //            ME2BackedUp = b;
        //            break;
        //        case MEGame.ME3:
        //            ME3BackedUp = b;
        //            break;
        //    }
        //    StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(AnyGameMissingBackup)));
        //    StaticBackupStateChanged?.Invoke(null, null);
        //}

        //public static void SetInstallStatuses(ObservableCollectionExtended<GameTarget> installationTargets)
        //{
        //    ME1Installed = installationTargets.Any(x => x.Game == MEGame.ME1);
        //    ME2Installed = installationTargets.Any(x => x.Game == MEGame.ME2);
        //    ME3Installed = installationTargets.Any(x => x.Game == MEGame.ME3);
        //    StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(AnyGameMissingBackup)));
        //    StaticBackupStateChanged?.Invoke(null, null);
        //}

        public static bool HasGameEverBeenBackedUp(MEGame game)
        {
            return MSharedSettings.GetSettingString($@"{game}VanillaBackupLocation") != null;
        }

        public static void UpdateBackupStatus(MEGame game)
        {
            GameBackupStatuses.FirstOrDefault(x => x.Game == game)?.RefreshBackupStatus(true, false);
        }

        /// <summary>
        /// ME3Tweaks Shared Registry Key
        /// </summary>
        internal const string REGISTRY_KEY_ME3TWEAKS = @"Software\ME3Tweaks";

        /// <summary>
        /// Copies ME1/ME2/ME3 backup paths to the new location if they are not defined there.
        /// </summary>
        public static void MigrateBackupPaths()
        {
#if WINDOWS
            if (GetGameBackupPath(MEGame.ME1, false) == null)
            {
                var storedPath = RegistryHandler.GetRegistryString($@"HKEY_CURRENT_USER\Software\ALOTAddon", @"ME1VanillaBackupLocation"); // what a disaster this is
                if (storedPath != null)
                {
                    MLog.Information(@"Migrating ALOT key backup location for ME1");
                    MSharedSettings.WriteSettingString(@"ME1VanillaBackupLocation", storedPath);
                }
            }
            if (GetGameBackupPath(MEGame.ME2, false) == null)
            {
                var storedPath = RegistryHandler.GetRegistryString($@"HKEY_CURRENT_USER\Software\ALOTAddon", @"ME2VanillaBackupLocation"); // what a disaster this is
                if (storedPath != null)
                {
                    MLog.Information(@"Migrating ALOT key backup location for ME2");
                    MSharedSettings.WriteSettingString(@"ME2VanillaBackupLocation", storedPath);
                }
            }
            if (GetGameBackupPath(MEGame.ME3, false) == null)
            {
                var storedPath = RegistryHandler.GetRegistryString($@"HKEY_CURRENT_USER\Software\Mass Effect 3 Mod Manager", @"VanillaCopyLocation"); // what a disaster this is
                if (storedPath != null)
                {
                    MLog.Information(@"Migrating ME3CMM key backup location for ME3");
                    MSharedSettings.WriteSettingString(@"ME3VanillaBackupLocation", storedPath);
                }
            }
#endif
        }

        /// <summary>
        /// Signal that backup activity is taking place
        /// </summary>
        /// <param name="game"></param>
        /// <param name="isActive"></param>
        public static void SetActivity(MEGame game, bool isActive)
        {
            var status = GetBackupStatus(game);
            status.BackupActivity = isActive;
        }

        public static bool AnyGameMissingBackup(params MEGame[] gamesToCheck)
        {
            foreach (var g in gamesToCheck)
            {
                var bs = GetBackupStatus(g);
                if (bs == null || !bs.BackedUp)
                    return false;
            }
            return false;
        }
    }
}
