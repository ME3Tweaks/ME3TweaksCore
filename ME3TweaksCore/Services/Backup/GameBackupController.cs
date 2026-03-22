using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Gammtek.Extensions;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Objects;
using ME3TweaksCore.Services.ThirdPartyModIdentification;
using ME3TweaksCore.Targets;
using PropertyChanged;
using Serilog;

namespace ME3TweaksCore.Services.Backup
{
    /// <summary>
    /// Class that handles backing up a game.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class GameBackup
    {
        public string GameName => Game.ToGameName();
        public MEGame Game { get; }

        /// <summary>
        /// Reports the current progress of the backup
        /// </summary>
        public Action<long, long> BackupProgressCallback { get; set; }
        /// <summary>
        /// Called when there is a blocking action, such as game running
        /// </summary>
        public Action<string, string> BlockingActionCallback { get; set; }
        /// <summary>
        /// Called when there is a warning that needs a yes/no answer
        /// </summary>
        public Func<string, string, bool, string, string, bool> WarningActionYesNoCallback { get; set; }
        /// <summary> 
        /// Called when the user must select a game executable (for backup). Return null to indicate the user aborted the prompt.
        /// </summary>
        public Func<MEGame, string> SelectGameExecutableCallback { get; set; }

        /// <summary>
        /// Called when the user must select a backup folder destination. Return null to indicate user aborted the prompt.
        /// </summary>
        public Func<string, string> SelectGameBackupFolderDestination { get; set; }
        /// <summary>
        /// Called when the backup thread has completed.
        /// </summary>
        public Action NotifyBackupThreadCompleted { get; set; }
        /// <summary>
        /// Called when there is a warning that has a potentially long list of items in it, with a title, top and bottom message, as well as a list of strings. These items should be placed in a scrolling mechanism
        /// </summary>
        public Func<string, string, string, List<string>, string, string, bool> WarningListCallback { get; set; }

        /// <summary>
        /// Called when there is a blcoking error that has a potentially long list of items in it, with a title, top and bottom message, as well as a list of strings. These items should be placed in a scrolling mechanism
        /// </summary>
        public Action<string, string, List<string>> BlockingListCallback { get; set; }

        /// <summary>
        /// Called when there is a new status message that should be displayed, such as what is being backed up.
        /// </summary>
        public Action<string> UpdateStatusCallback { get; set; }
        /// <summary>
        /// Sets the progressbar (if any) with this backup operation to indeterminate or not.
        /// </summary>
        public Action<bool> SetProgressIndeterminateCallback { get; set; }

        /// <summary>
        /// Invoked when the user should be prompted to select which languages to include in the backup. Parameters: Title, Message
        /// </summary>
        public Func<string, string, GameLanguage[]> SelectGameLanguagesCallback { get; set; }

        /// <summary>
        /// The backup status for this game, which can be accessed via BackupService.
        /// </summary>
        private GameBackupStatus BackupStatus { get; init; }

        public GameBackup(MEGame game, IEnumerable<GameTarget> availableBackupSources)
        {
            this.Game = game;
            BackupStatus = BackupService.GetBackupStatus(game);
        }


        /// <summary>
        /// Performs a backup for the specified backup target. The application targets list is checked to make sure we aren't backing up into another game's directory.
        /// </summary>
        /// <param name="backupSourceTarget"></param>
        /// <param name="applicationTargets"></param>
        /// <returns></returns>
        public bool PerformBackup(GameTarget targetToBackup, List<GameTarget> applicationTargets)
        {
            if (targetToBackup == null)
            {
                throw new Exception(@"Cannot call PerformBackup() with a null target!");
            }

            GameLanguage[] allGameLangauges = targetToBackup.Game != MEGame.LELauncher ? GameLanguage.GetLanguagesForGame(targetToBackup.Game) : null;
            GameLanguage[] selectedLanguages = null;

            if (!targetToBackup.IsCustomOption)
            {
                MLog.Information($@"PerformBackup() on {targetToBackup.TargetPath}");
                // Backup target
                if (MRunningGameInfo.IsGameRunning(targetToBackup.Game))
                {
                    MLog.Error(@"PerformBackup: Cannot backup game while game is running");
                    EndBackup();
                    BlockingActionCallback?.Invoke(LC.GetString(LC.string_cannotBackupGame), LC.GetString(LC.string_interp_cannotBackupGameIsRunning, targetToBackup.Game.ToGameName()));
                    return false;
                }
                else
                {
                    MLog.Information(@"PerformBackup: Game is not running");
                }

                // Language selection
                if (Game != MEGame.LELauncher)
                {
                    selectedLanguages = SelectGameLanguagesCallback?.Invoke(LC.GetString(LC.string_selectLanguages), LC.GetString(LC.string_dialog_selectWhichLanguagesToIncludeInBackup));
                }
            }
            else
            {
                // Point to existing game installation
                MLog.Information(@"PerformBackup() with IsCustomOption.");
                var linkOK = WarningActionYesNoCallback?.Invoke(LC.GetString(LC.string_ensureCorrectGameChosen), LC.GetString(LC.string_warningLinkedTargetWillNotLoad),
                    false, LC.GetString(LC.string_iUnderstand), LC.GetString(LC.string_abortLinking));
                if (!linkOK.HasValue || !linkOK.Value)
                {
                    MLog.Information(@"User aborted linking due to dialog");
                    EndBackup();
                    return false;
                }

                MLog.Information(@"Prompting user to select executable of link target");

                var gameExecutable = SelectGameExecutableCallback?.Invoke(Game);

                if (gameExecutable == null)
                {
                    MLog.Warning(@"User did not choose game executable to link as backup. Aborting");
                    EndBackup();
                    return false;
                }

                // TODO: Check version number of executable for LE/OT
                if (Game.IsGame2() || Game.IsGame3())
                {
                    var version = FileVersionInfo.GetVersionInfo(gameExecutable);
                    if (version.FileMajorPart == 2 && Game.IsOTGame())
                    {
                        MLog.Error($@"The selected executable is the Legendary Edition of the game, but target for backup is {Game}.");
                        EndBackup();
                        BlockingActionCallback?.Invoke(LC.GetString(LC.string_cannotBackupGame), LC.GetString(LC.string_cannotLinkTargetWrongGenerationLEneedOT));
                        return false;
                    }
                    else if (version.FileMajorPart == 1 && Game.IsLEGame())
                    {
                        MLog.Error($@"The selected executable is the Original Trilogy of the game, but target for backup is {Game}.");
                        EndBackup();
                        BlockingActionCallback?.Invoke(LC.GetString(LC.string_cannotBackupGame), LC.GetString(LC.string_cannotLinkTargetWrongGenerationOTneedLE));
                        return false;
                    }
                }

                // Initialize the executable target
                targetToBackup = new GameTarget(Game, M3Directories.GetGamePathFromExe(Game, gameExecutable), false, true);

                if (applicationTargets != null && applicationTargets.Any(x => x.TargetPath.Equals(targetToBackup.TargetPath, StringComparison.InvariantCultureIgnoreCase)))
                {
                    // Can't point to an existing modding target
                    MLog.Error(@"This target is not valid to point to as a backup: It is listed a modding target already, it must be removed as a target first");
                    EndBackup();
                    BlockingActionCallback?.Invoke(LC.GetString(LC.string_cannotBackupGame), LC.GetString(LC.string_interp_cannotBackupThisIsTheCurrentGamePath));
                    return false;
                }

                // Validate it to ensure we can use it for checking
                var validationFailureReason = targetToBackup.ValidateTarget(ignoreCmmVanilla: true);
                if (!targetToBackup.IsValid)
                {
                    MLog.Error(@"This installation is not valid to point to as a backup: " + validationFailureReason);
                    EndBackup();
                    BlockingActionCallback?.Invoke(LC.GetString(LC.string_cannotBackupGame), LC.GetString(LC.string_interp_cannotBackupTargetReason, validationFailureReason));
                    return false;
                }
            }


            MLog.Information(@"Starting the backup thread. Checking path: " + targetToBackup.TargetPath);
            BackupInProgress = true;

            List<string> nonVanillaFiles = new List<string>();

            void nonVanillaFileFoundCallback(string filepath)
            {
                MLog.Error($@"Non-vanilla file found: {filepath}");
                nonVanillaFiles.Add(filepath.Substring(targetToBackup.TargetPath.Length + 1)); //Oh goody i'm sure this won't cause issues
            }

            List<string> inconsistentDLC = new List<string>();

            void inconsistentDLCFoundCallback(string filepath)
            {
                if (targetToBackup.Supported)
                {
                    MLog.Error($@"DLC is in an inconsistent state: {filepath}");
                    inconsistentDLC.Add(filepath);
                }
                else
                {
                    MLog.Error(@"Detected an inconsistent DLC, likely due to an unofficial copy of the game");
                }
            }

            UpdateStatusCallback?.Invoke(LC.GetString(LC.string_validatingBackupSource));
            BackupStatus.BackupStatus = LC.GetString(LC.string_validatingBackupSource);
            SetProgressIndeterminateCallback?.Invoke(true);
            MLog.Information(@"Checking target is vanilla");
            bool isVanilla = VanillaDatabaseService.ValidateTargetAgainstVanilla(targetToBackup, nonVanillaFileFoundCallback, false);

            List<string> dlcModsInstalled = null;

            if (Game != MEGame.LELauncher)
            {
                MLog.Information(@"Checking DLC consistency");
                bool isDLCConsistent = VanillaDatabaseService.ValidateTargetDLCConsistency(targetToBackup, inconsistentDLCCallback: inconsistentDLCFoundCallback);

                MLog.Information(@"Checking only vanilla DLC is installed");
                dlcModsInstalled = VanillaDatabaseService.GetInstalledDLCMods(targetToBackup).Select(x =>
                {
                    var tpmi = TPMIService.GetThirdPartyModInfo(x, targetToBackup.Game);
                    if (tpmi != null) return $@"{x} ({tpmi.modname})";
                    return x;
                }).ToList();
                List<string> installedDLC = VanillaDatabaseService.GetInstalledOfficialDLC(targetToBackup);
                List<string> allOfficialDLC = Game == MEGame.LELauncher ? new List<string>() : MEDirectories.OfficialDLC(targetToBackup.Game).ToList();

                MLog.Information(@"Checking for TexturesMEM TFCs");
                var memTextures = Directory.GetFiles(targetToBackup.TargetPath, @"TexturesMEM*.tfc", SearchOption.AllDirectories);

                if (installedDLC.Count() < allOfficialDLC.Count())
                {
                    var dlcList = string.Join("\n - ", allOfficialDLC.Except(installedDLC).Select(x => $@"{MEDirectories.OfficialDLCNames(targetToBackup.Game)[x]} ({x})")); //do not localize
                    dlcList = @" - " + dlcList;
                    MLog.Information(@"The following dlc will be missing in the backup if user continues: " + dlcList);
                    string message = LC.GetString(LC.string_dialog_notAllDLCInstalled, dlcList);
                    var okToBackup = WarningActionYesNoCallback?.Invoke(LC.GetString(LC.string_someDlcNotInstalled), message, false, LC.GetString(LC.string_continueBackingUp), LC.GetString(LC.string_abortBackup));
                    if (!okToBackup.HasValue || !okToBackup.Value)
                    {
                        MLog.Information(@"User canceled backup due to some missing data");
                        EndBackup();
                        return false;
                    }
                }

                if (memTextures.Any())
                {
                    MLog.Information($@"Cannot backup: Game contains TexturesMEM TFC files ({memTextures.Length})");
                    EndBackup();
                    BlockingActionCallback?.Invoke(LC.GetString(LC.string_leftoverTextureFilesFound), LC.GetString(LC.string_dialog_foundLeftoverTextureFiles));
                    return false;
                }

                if (!isDLCConsistent)
                {
                    MLog.Information(@"Cannot backup: Game contains inconsistent DLC");
                    EndBackup();
                    if (targetToBackup.Supported)
                    {
                        BlockingListCallback?.Invoke(LC.GetString(LC.string_inconsistentDLCDetected), LC.GetString(LC.string_dialogTheFollowingDLCAreInAnInconsistentState), inconsistentDLC);
                    }
                    else
                    {
                        BlockingListCallback?.Invoke(LC.GetString(LC.string_inconsistentDLCDetected), LC.GetString(LC.string_inconsistentDLCDetectedUnofficialGame), inconsistentDLC);
                    }

                    return false;
                }

                // Todo: Maybe find a way to skip these?
                if (dlcModsInstalled.Any())
                {
                    MLog.Information(@"Cannot backup: Game contains modified game files");
                    EndBackup();
                    BlockingListCallback?.Invoke(LC.GetString(LC.string_cannotBackupModifiedGame), LC.GetString(LC.string_dialogDLCModsWereDetectedCannotBackup), dlcModsInstalled);
                    return false;
                }
            }

            if (!isVanilla)
            {
                // Cannot backup a non-vanilla game
                MLog.Information(@"Cannot backup: Game is modified");
                EndBackup();
                BlockingListCallback?.Invoke(LC.GetString(LC.string_cannotBackupModifiedGame), LC.GetString(LC.string_followingFilesDoNotMatchTheVanillaDatabase), nonVanillaFiles);
                return false;
            }

            BackupStatus.BackupStatus = LC.GetString(LC.string_waitingForUserInput);

            string backupPath = null;
            if (!targetToBackup.IsCustomOption)
            {
                // Creating a new backup
                MLog.Information(@"Prompting user to select backup destination");
                backupPath = SelectGameBackupFolderDestination?.Invoke(LC.GetString(LC.string_selectEmptyBackupDestinationDirectory));
                if (backupPath != null && Directory.Exists(backupPath))
                {
                    MLog.Information(@"Backup path chosen: " + backupPath);
                    bool okToBackup = validateBackupPath(backupPath, targetToBackup, applicationTargets);
                    if (!okToBackup)
                    {
                        EndBackup();
                        return false;
                    }
                }
                else
                {
                    MLog.Information(@"User canceled making a backup"); // also could be dir doesn't exist anymore
                    EndBackup();
                    return false;
                }
            }
            else
            {
                // Linking existing backup
                MLog.Information(@"Linking existing backup at " + targetToBackup.TargetPath);
                backupPath = targetToBackup.TargetPath;
                bool okToBackup = validateBackupPath(targetToBackup.TargetPath, targetToBackup, applicationTargets);
                if (!okToBackup)
                {
                    EndBackup();
                    return false;
                }
            }

            if (!targetToBackup.IsCustomOption)
            {
                MLog.Information(@"Preparing to backup");

                // Making new backup
                #region callbacks and copy code

                // Todo: Maybe uninstall bink before copying data? So it's more 'vanilla'

                // Copy to new backup
                void fileCopiedCallback()
                {
                    ProgressValue++;
                    BackupProgressCallback?.Invoke(ProgressValue, ProgressMax);
                }

                string dlcFolderpath = null;
                int dlcSubStringLen = 0;
                CaseInsensitiveDictionary<string> officialDLCNames = null;

                if (Game != MEGame.LELauncher)
                {
                    // Launch doesn't set these paths so we cannot access them on Launcher backup.
                    dlcFolderpath = M3Directories.GetDLCPath(targetToBackup) + Path.DirectorySeparatorChar;
                    dlcSubStringLen = dlcFolderpath.Length;
                    officialDLCNames = MEDirectories.OfficialDLCNames(targetToBackup.Game);
                }

                // Used for showing to the user what the last file processed was.
                string currentProcessingFile = null;
                bool aboutToCopyCallbackLauncher(string file)
                {
                    currentProcessingFile = file;
                    UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_backingUpX, Path.GetFileName(file)));
                    BackupStatus.BackupLocationStatus = LC.GetString(LC.string_interp_backingUpX, Path.GetFileName(file));
                    return true;
                }

                bool aboutToCopyCallbackMainGame(string file)
                {
                    currentProcessingFile = file;
                    try
                    {
                        // TODO: MAYBE WAY TO SKIP DLC MODS? So we can just backup even if user installed ONLY DLC mods

                        if (file.Contains(@"\cmmbackup\")) return false; //do not copy cmmbackup files - these are leftovers from ME3CMM's old backup routines

                        if (selectedLanguages != null)
                        {
                            // Check language of file
                            var fileName = Path.GetFileNameWithoutExtension(file);
                            if (fileName != null && fileName.LastIndexOf("_", StringComparison.InvariantCultureIgnoreCase) > 0)
                            {
                                var suffix = fileName.Substring(fileName.LastIndexOf("_", StringComparison.InvariantCultureIgnoreCase) + 1); // INT, ESN, PLPC
                                if (allGameLangauges != null && allGameLangauges.Any(x => x.FileCode.Equals(suffix, StringComparison.InvariantCultureIgnoreCase)) && !selectedLanguages.Any(x => x.FileCode.Equals(suffix, StringComparison.InvariantCultureIgnoreCase)))
                                {
                                    Debug.WriteLine($@"Skipping non-selected localized file for backup: {file}");
                                    return false; // Do not back up this file
                                }
                            }
                        }


                        if (dlcFolderpath != null && file.StartsWith(dlcFolderpath, StringComparison.InvariantCultureIgnoreCase))
                        {
                            //It's a DLC!
                            string dlcname = file.Substring(dlcSubStringLen);
                            var dlcFolderNameEndPos = dlcname.IndexOf(Path.DirectorySeparatorChar);
                            if (dlcFolderNameEndPos > 0)
                            {
                                dlcname = dlcname.Substring(0, dlcFolderNameEndPos);
                                if (officialDLCNames.TryGetValue(dlcname, out var hrName))
                                {
                                    UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_backingUpX, hrName));
                                    BackupStatus.BackupLocationStatus = LC.GetString(LC.string_interp_backingUpX, hrName);
                                }
                                else
                                {
                                    UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_backingUpX, dlcname));
                                    BackupStatus.BackupLocationStatus = LC.GetString(LC.string_interp_backingUpX, dlcname);
                                }
                            }
                            else
                            {
                                // Loose files in the DLC folder
                                UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_backingUpX, LC.GetString(LC.string_basegame)));
                                BackupStatus.BackupLocationStatus = LC.GetString(LC.string_interp_backingUpX, LC.GetString(LC.string_basegame));
                            }
                        }
                        else
                        {
                            //It's basegame
                            if (file.EndsWith(@".bik"))
                            {
                                UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_backingUpX, LC.GetString(LC.string_movies)));
                                BackupStatus.BackupLocationStatus = LC.GetString(LC.string_interp_backingUpX, LC.GetString(LC.string_movies));
                            }
                            else if (new FileInfo(file).Length > 52428800)
                            {
                                UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_backingUpX, Path.GetFileName(file)));
                                BackupStatus.BackupLocationStatus = LC.GetString(LC.string_interp_backingUpX, Path.GetFileName(file));
                            }
                            else
                            {
                                UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_backingUpX, LC.GetString(LC.string_basegame)));
                                BackupStatus.BackupLocationStatus = LC.GetString(LC.string_interp_backingUpX, LC.GetString(LC.string_basegame));
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        MLog.Error($@"Error about to copy file: {e.Message}");
                        TelemetryInterposer.TrackError(e);
                    }

                    return true;
                }

                void bigFileProgressCallback(string fileBeingCopied, long dataCopied, long totalDataToCopy)
                {
                    if (dlcFolderpath == null)
                    {
                        // If DLC folder path is not set (maybe launcher has big file?) this prevents crash
                        UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_backingUpSinglePercentage, Path.GetFileName(fileBeingCopied), (int)(dataCopied * 100d / totalDataToCopy)));
                        return;
                    }

                    if (fileBeingCopied.StartsWith(dlcFolderpath, StringComparison.InvariantCultureIgnoreCase))
                    {
                        //It's a DLC!
                        string dlcname = fileBeingCopied.Substring(dlcSubStringLen);
                        int index = dlcname.IndexOf(Path.DirectorySeparatorChar);
                        try
                        {
                            string postFix = "";
                            dlcname = dlcname.Substring(0, index);
                            if (officialDLCNames.TryGetValue(dlcname, out var hrName))
                            {
                                postFix = hrName;
                            }
                            else
                            {
                                postFix = dlcname;
                            }

                            UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_backingUpX, $@"{postFix} {(int)(dataCopied * 100d / totalDataToCopy)}%"));
                            BackupStatus.BackupLocationStatus = LC.GetString(LC.string_interp_backingUpX, $@"{postFix} {(int)(dataCopied * 100d / totalDataToCopy)}%");
                        }
                        catch
                        {

                        }
                    }
                    else
                    {
                        UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_backingUpSinglePercentage, Path.GetFileName(fileBeingCopied), (int)(dataCopied * 100d / totalDataToCopy)));
                    }
                }


                void totalFilesToCopyCallback(int total)
                {
                    ProgressValue = 0;
                    ProgressIndeterminate = false;
                    ProgressMax = total;
                }

                BackupStatus.BackupStatus = LC.GetString(LC.string_creatingBackup);
                MLog.Information($@"Backing up {targetToBackup.TargetPath} to {backupPath}");

                try
                {
                    CopyTools.CopyAll_ProgressBar(new DirectoryInfo(targetToBackup.TargetPath),
                        new DirectoryInfo(backupPath),
                        totalItemsToCopyCallback: totalFilesToCopyCallback,
                        aboutToCopyCallback: Game == MEGame.LELauncher
                            ? aboutToCopyCallbackLauncher
                            : aboutToCopyCallbackMainGame,
                        fileCopiedCallback: fileCopiedCallback,
                        ignoredExtensions: new[] { @"*.pdf", @"*.mp3", @"*.wav" },
                        bigFileProgressCallback: bigFileProgressCallback,
                        copyTimestamps: true);
                }
                catch (Exception e)
                {
                    MLog.Exception(e, @"Exception backing up game:");
                    EndBackup();
                    BlockingActionCallback?.Invoke(LC.GetString(LC.string_errorCreatingBackup), LC.GetString(LC.string_interp_errorCreatingBackupX, e.Message, currentProcessingFile));
                    return false;
                }

                #endregion
            }

            // Write location
            SetBackupPath(Game, backupPath);

            // Write vanilla marker
            if (isVanilla && dlcModsInstalled == null || !dlcModsInstalled.Any())
            {
                var cmmvanilla = Path.Combine(backupPath, @"cmm_vanilla");
                if (!File.Exists(cmmvanilla))
                {
                    MLog.Information(@"Writing cmm_vanilla to " + cmmvanilla);
                    File.Create(cmmvanilla).Close();
                }
            }
            else
            {
                MLog.Information(@"Not writing vanilla marker as this is not a vanilla backup");
            }

            MLog.Information(@"Backup completed");
            TelemetryInterposer.TrackEvent(@"Created a backup", new Dictionary<string, string>()
                        {
                                {@"game", Game.ToString()},
                                {@"Result", @"Success"},
                                {@"Type", targetToBackup.IsCustomOption ? @"Linked" : @"Copy"}
                        });
            EndBackup();
            return true;
        }

        private bool validateBackupPath(string backupPath, GameTarget targetToBackup, List<GameTarget> applicableTargets)
        {
            //Check empty
            if (!targetToBackup.IsCustomOption && Directory.Exists(backupPath))
            {
                var files = Directory.GetFiles(backupPath);
                var dirs = Directory.GetDirectories(backupPath);
                if (files.Length > 0 || dirs.Length > 0)
                {
                    //Directory not empty
                    MLog.Error($@"Selected backup directory {backupPath} is not empty: {files.Length} files, {dirs.Length} directories");
                    if (files.Length > 0)
                    {
                        MLog.Error($@"First file found: {files.FirstOrDefault()}");
                    }

                    if (dirs.Length > 0)
                    {
                        MLog.Error($@"First directory found: {dirs.FirstOrDefault()}");
                    }

                    EndBackup();
                    BlockingActionCallback?.Invoke(LC.GetString(LC.string_directoryNotEmpty), LC.GetString(LC.string_directoryIsNotEmptyMustBeEmpty));
                    return false;
                }
            }
            if (!targetToBackup.IsCustomOption)
            {

                //Check space
                if (!MUtilities.DriveFreeBytes(backupPath, out ulong freespace))
                {
                    MLog.Error($@"Failed to get free space for backup path: {backupPath}");
                    EndBackup();
                    BlockingActionCallback?.Invoke(LC.GetString(LC.string_cannotCreateBackup), LC.GetString(LC.string_dialog_backupUnableToDetermineFreeSpace, backupPath));
                    return false;
                }

                var requiredSpace = (long)(MUtilities.GetSizeOfDirectory(new DirectoryInfo(targetToBackup.TargetPath)) * 1.1); //10% buffer
                MLog.Information($@"Backup space check. Backup size required: {FileSize.FormatSize(requiredSpace)}, free space: {FileSize.FormatSize((long)freespace)}");
                if ((long)freespace < requiredSpace)
                {
                    //Not enough space.
                    MLog.Error($@"Not enough disk space to create backup at {backupPath}");
                    EndBackup();
                    BlockingActionCallback?.Invoke(LC.GetString(LC.string_insufficientDiskSpace), LC.GetString(LC.string_dialogInsufficientDiskSpace, Path.GetPathRoot(backupPath), FileSize.FormatSize((long)freespace), FileSize.FormatSize(requiredSpace)));
                    return false;
                }

                //Check writable
                var writable = MUtilities.IsDirectoryWritable(backupPath);
                if (!writable)
                {
                    //Not enough space.
                    MLog.Error($@"Backup destination selected is not writable.");
                    EndBackup();
                    BlockingActionCallback?.Invoke(LC.GetString(LC.string_cannotCreateBackup), LC.GetString(LC.string_dialog_userAccountDoesntHaveWritePermissionsBackup));
                    return false;
                }
            }
            //Check is Documents folder
            var docsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"BioWare", targetToBackup.Game.ToGameName());
            if (backupPath.Equals(docsPath, StringComparison.InvariantCultureIgnoreCase) || backupPath.IsSubPathOf(docsPath))
            {
                MLog.Error(@"User chose path in or around the documents path for the game - not allowed as game can load files from here.");
                BlockingActionCallback?.Invoke(LC.GetString(LC.string_locationNotAllowedForBackup), LC.GetString(LC.string_interp_dialog_linkFailedSubdirectoryOfGameDocumentsFolder));
                return false;
            }


            //Check it is not subdirectory of a game (we might want to check its not subdir of a target)
            if (applicableTargets != null)
            {
                foreach (var target in applicableTargets)
                {
                    if (backupPath.IsSubPathOf(target.TargetPath))
                    {
                        //Not enough space.
                        MLog.Error($@"A backup cannot be created in a subdirectory of a game. {backupPath} is a subdir of {targetToBackup.TargetPath}");
                        BlockingActionCallback?.Invoke(LC.GetString(LC.string_cannotCreateBackup), LC.GetString(LC.string_dialogBackupCannotBeSubdirectoryOfGame, backupPath, targetToBackup.TargetPath));
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// This must be called before showing a dialog if canceling a backup - not entirely sure why.
        /// </summary>
        private void EndBackup()
        {
            ResetBackupStatus();
            ProgressIndeterminate = false;
            ProgressVisible = false;
            BackupInProgress = false;
        }

        private void ResetBackupStatus()
        {
            BackupService.UpdateBackupStatus(Game);
        }

        public int ProgressMax { get; set; } = 100;
        public int ProgressValue { get; set; } = 0;
        public bool ProgressIndeterminate { get; set; } = true;
        public bool ProgressVisible { get; set; } = false;
        public bool BackupInProgress { get; set; }

        /// <summary>
        /// Sets the path for the backup in the registry
        /// </summary>
        /// <param name="game"></param>
        /// <param name="path"></param>
        public static void SetBackupPath(MEGame game, string path)
        {
            MSharedSettings.WriteSettingString($@"{game}VanillaBackupLocation", path);
        }
    }
}
