using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Timers;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Gammtek.Extensions;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Services.Backup;
using ME3TweaksCore.Targets;
using PropertyChanged;
using RoboSharp;

namespace ME3TweaksCore.Services.Restore
{
    #region RESTORE

    /// <summary>
    /// Object that contains the logic for performing the restoration of a game.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class GameRestore
    {
        public MEGame Game { get; }

        public long ProgressValue { get; set; }
        public long ProgressMax { get; set; }
        //public bool ProgressIndeterminate { get; set; }
        /// <summary>
        /// Callback for when there is a blocking error and the restore cannot be performed. The first parameter is the title, the second is the message
        /// </summary>
        public Action<string, string> BlockingErrorCallback { get; set; }
        /// <summary>
        /// Callback for when there is an error during the restore. This may mean you need to keep UI target available still so user can try again without losing the target
        /// </summary>
        public Action<string, string> RestoreErrorCallback { get; set; }
        /// <summary>
        /// Callback to select a directory for custom restore location
        /// </summary>
        public Func<string, string, string> SelectDestinationDirectoryCallback { get; set; }
        /// <summary>
        /// Callback to confirm restoration over existing game
        /// </summary>
        public Func<string, string, bool> ConfirmationCallback { get; set; }
        /// <summary>
        /// Callback when the status string on the UI should be updated
        /// </summary>
        public Action<string> UpdateStatusCallback { get; set; }
        /// <summary>
        /// Callback when there is a progress update for the UI
        /// </summary>
        public Action<long, long> UpdateProgressCallback { get; set; }
        /// <summary>
        /// Callback when the progressbar should change indeterminate states
        /// </summary>
        public Action<bool> SetProgressIndeterminateCallback { get; set; }
        /// <summary>
            /// Value indicating if a restore operation is currently in progress
        /// </summary>
        public bool RestoreInProgress { get; private set; }

        /// <summary>
        /// The function that retreives a string for the restore-everything prompt, to allow tool-specific text
        /// </summary>
        public Func<MEGame, string> GetRestoreEverythingString { get; set; } = RestoreEverythingDefault;

        /// <summary>
        /// If optimized texture restore method should be used. If false, it's skipped
        /// </summary>
        public Func<bool> UseOptimizedTextureRestore { get; set; } = UseOptimizedTextureRestoreDefault;

        /// <summary>
        /// If each file about to be copied should be logged. This is a debugging feature
        /// </summary>
        public Func<bool> ShouldLogEveryCopiedFile { get; set; } = ShouldLogEveryCopiedFileDefault;

        /// <summary>
        /// Timer that periodically calls SystemSleepManager to keep the system awake during restore
        /// </summary>
        private Timer _keepAwakeTimer = new Timer(30 * 1000); // 30s interval

        #region Delegate defaults
        /// <summary>
        /// If texture modded, scan for marker and strip it and reset datestamp instead of copy
        /// </summary>
        /// <returns></returns>
        private static bool UseOptimizedTextureRestoreDefault()
        {
            return true;
        }

        /// <summary>
        /// Do not log all files by default
        /// </summary>
        /// <returns></returns>
        private static bool ShouldLogEveryCopiedFileDefault()
        {
            return false;
        }


        /// <summary>
        /// The default text for when everything is being restored via a backup.
        /// </summary>
        /// <param name="game"></param>
        /// <returns></returns>
        private static string RestoreEverythingDefault(MEGame game)
        {
            return LC.GetString(LC.string_interp_restoringWillDeleteEverythingMessage, game.ToGameName());
        }

        /// <summary>
        /// Default: we don't use legacy full wipe method of restore
        /// </summary>
        /// <returns></returns>
        private static bool UseLegacyFullCopyDefault()
        {
            return false;
        }
        #endregion

        public GameRestore(MEGame game)
        {
            this.Game = game;
        }

        /// <summary>
        /// Restores the game to the specified directory (game location). Pass in null if you wish to restore to a custom location. Refreshes the target on completion. This call is blocking, it should be run on a background thread.
        /// </summary>
        /// <param name="destinationDirectory">Game directory that will be replaced with backup. If null, it will prompt for a custom directory.</param>
        /// <returns></returns>
        public bool PerformRestore(GameTarget restoreTarget, string destinationDirectory)
        {
            if (MRunningGameInfo.IsGameRunning(Game))
            {
                BlockingErrorCallback?.Invoke(LC.GetString(LC.string_cannotRestoreGame), LC.GetString(LC.string_interp_cannotRestoreGameToGameWhileRunning, Game.ToGameName()));
                return false;
            }

            bool restore = destinationDirectory == null; // Restore to custom location
            if (!restore)
            {
                var confirmDeletion = ConfirmationCallback?.Invoke(LC.GetString(LC.string_interp_restoringWillDeleteEverythingTitle, Game.ToGameName()), GetRestoreEverythingString(Game));
                restore |= confirmDeletion.HasValue && confirmDeletion.Value;
            }

            var backupStatus = BackupService.GetBackupStatus(Game);

            if (restore)
            {
                RestoreInProgress = true;
                // We will set values on backupStatus.
                string backupPath = BackupService.GetGameBackupPath(Game);

                if (destinationDirectory == null)
                {
                    destinationDirectory = SelectDestinationDirectoryCallback?.Invoke(LC.GetString(LC.string_selectDestinationLocationForRestore), LC.GetString(LC.string_selectADirectoryToRestoreTheGameToThisDirectoryMustBeEmpty));
                    if (destinationDirectory != null)
                    {
                        //Check empty
                        if (Directory.Exists(destinationDirectory))
                        {
                            if (Directory.GetFiles(destinationDirectory).Length > 0 || Directory.GetDirectories(destinationDirectory).Length > 0)
                            {
                                //Directory not empty
                                BlockingErrorCallback?.Invoke(LC.GetString(LC.string_cannotRestoreGame), LC.GetString(LC.string_restoreDestinationNotEmpty));
                                return false;
                            }

                            //TODO: PREVENT RESTORING TO DOCUMENTS/BIOWARE
                        }

                        TelemetryInterposer.TrackEvent(@"Chose to restore game to custom location", new Dictionary<string, string>() { { @"Game", Game.ToString() } });
                    }
                    else
                    {
                        MLog.Warning(@"User declined to choose destination directory");
                        return false;
                    }
                }

                SetProgressIndeterminateCallback?.Invoke(true);

                backupStatus.BackupLocationStatus = LC.GetString(LC.string_preparingGameDirectory);
                var created = MUtilities.CreateDirectoryWithWritePermission(destinationDirectory);
                if (!created)
                {
                    RestoreErrorCallback?.Invoke(LC.GetString(LC.string_errorCreatingGameDirectory), LC.GetString(LC.string_interp_couldNotCreateGameDirectoryNoPermission, Game.ToGameName()));
                    //b.Result = RestoreResult.ERROR_COULD_NOT_CREATE_DIRECTORY;
                    return false;
                }

                RestoreUsingRoboCopy(backupPath, restoreTarget, backupStatus, destinationDirectory);


                //Check for cmmvanilla file and remove it present

                string cmmVanilla = Path.Combine(destinationDirectory, @"cmm_vanilla");
                if (File.Exists(cmmVanilla))
                {
                    MLog.Information(@"Removing cmm_vanilla file");
                    File.Delete(cmmVanilla);
                }

                MLog.Information(@"Restore thread wrapping up");
                RestoreInProgress = false;

                BackupService.RefreshBackupStatus(game: Game);
                restoreTarget?.ReloadGameTarget(); // Reload target if we were passed in one.
                return true;
            }

            BackupService.RefreshBackupStatus(game: Game);
            RestoreInProgress = false;
            return false;
        }

        /// <summary>
        /// External setter for RestoreInProgress - used when errors may occur that stall this variable from being reset
        /// </summary>
        /// <param name="inProgress"></param>
        public void SetRestoreInProgress(bool inProgress)
        {
            RestoreInProgress = inProgress;
        }
        private void RestoreUsingRoboCopy(string backupPath, GameTarget destTarget, GameBackupStatus backupStatus, string destinationPathOverride = null)
        {
            SetSleepPrevention(true);
            var useTextureOptimized = UseOptimizedTextureRestore();
            var logEachFileCopied = ShouldLogEveryCopiedFile();
            if (destTarget != null && useTextureOptimized && destTarget.TextureModded)
            {
                MLog.Information(@"Using texture-modded restore method");
                backupStatus.BackupLocationStatus = LC.GetString(LC.string_analyzingGameFiles);
                SetProgressIndeterminateCallback?.Invoke(true);

                // Game is texture modded.
                var packagesToCheck = new List<string>();

                void addNonVanillaFile(string failedItem)
                {
                    if (failedItem.RepresentsPackageFilePath())
                        packagesToCheck.Add(failedItem);
                }

                backupStatus.BackupLocationStatus = LC.GetString(LC.string_comparingGameAgainstVanillaDatabase);
                UpdateStatusCallback?.Invoke(backupStatus.BackupLocationStatus);

                VanillaDatabaseService.ValidateTargetAgainstVanilla(destTarget, addNonVanillaFile, false);

                // For each package that failed validation, we should check the size.
                backupStatus.BackupLocationStatus = LC.GetString(LC.string_checkingTexturetaggedPackages);
                UpdateStatusCallback?.Invoke(backupStatus.BackupLocationStatus);
                int numOnlyTexTagged = 0;
                SetProgressIndeterminateCallback?.Invoke(false);
                ProgressValue = 0;
                ProgressMax = packagesToCheck.Count;
                foreach (var fullPath in packagesToCheck)
                {
                    var relativePath = fullPath.Substring(destTarget.TargetPath.Length + 1);
                    var fi = new FileInfo(fullPath);
                    bool resetDate = false;
                    var vanillaInfos = VanillaDatabaseService.GetVanillaFileInfo(destTarget, relativePath);
                    if (vanillaInfos != null && vanillaInfos.Any(x => x.size == fi.Length - 24))
                    {
                        // Might just be MEMI tagged
                        using var fileStream = File.Open(fullPath, FileMode.Open);
                        fileStream.SeekEnd();
                        fileStream.Seek(-24, SeekOrigin.Current);
                        var tag = fileStream.ReadStringASCII(24);
                        if (tag == @"ThisIsMEMEndOfFileMarker")
                        {
                            fileStream.SetLength(fileStream.Length - 24); // Truncate
                            resetDate = true;
                        }
                    }

                    // This is done outside of the previous block to make the filestream be closed so it doesn't interfere with our operation
                    if (resetDate)
                    {
                        // Copy data over from backup so robocopy doesn't copy it.
                        MUtilities.CopyTimestamps(Path.Combine(backupPath, relativePath), fullPath);
                        numOnlyTexTagged++;
                    }
                    ProgressValue++;
                    UpdateProgressCallback?.Invoke(ProgressValue, ProgressMax);
                }
                MLog.Information(@"Texture-modded pre-restore has completed");

                Debug.WriteLine($@"Files only texture tagged: {numOnlyTexTagged}");
            }

            backupStatus.BackupStatus = LC.GetString(LC.string_restoringFromBackup);
            UpdateStatusCallback?.Invoke(backupStatus.BackupStatus);

            string gamerSettings = null;
            if (destTarget != null && destTarget.Game.IsLEGame())
            {
                var gamerSettingsF = MEDirectories.GetLODConfigFile(destTarget.Game, destTarget.TargetPath);
                if (File.Exists(gamerSettingsF))
                {
                    gamerSettings = File.ReadAllText(gamerSettingsF);
                    MLog.Information(@"Cached gamersettings.ini in memory");
                }
            }

            var destinationPath = destinationPathOverride ?? destTarget?.TargetPath;
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                MLog.Error(@"Restore destination path was not specified.");
                SetSleepPrevention(false);
                return;
            }

            if (WineWorkarounds.WineDetected)
            {
                ExecuteRestoreUsingRsync(backupPath, destinationPath, backupStatus, logEachFileCopied);
            }
            else
            {
                ExecuteRestoreUsingRobocopy(backupPath, destinationPath, backupStatus, logEachFileCopied);
            }

            SetSleepPrevention(false);

            if (gamerSettings != null)
            {
                var gamerSettingsF = MEDirectories.GetLODConfigFile(destTarget.Game, destTarget.TargetPath);
                Directory.GetParent(gamerSettingsF).Create();
                File.WriteAllText(gamerSettingsF, gamerSettings);
                MLog.Information(@"Restored gamersettings.ini");
            }
        }

        private void ExecuteRestoreUsingRobocopy(string backupPath, string destinationPath, GameBackupStatus backupStatus, bool logEachFileCopied)
        {
            string currentRoboCopyFile = null;
            RoboCommand rc = new RoboCommand();
            rc.CopyOptions.Destination = destinationPath;
            rc.CopyOptions.Source = backupPath;
            rc.CopyOptions.Mirror = true;
            rc.CopyOptions.MultiThreadedCopiesCount = 2;
            rc.OnCopyProgressChanged += (sender, args) =>
            {
                SetProgressIndeterminateCallback?.Invoke(false);
                ProgressValue = (int)args.CurrentFileProgress;
                ProgressMax = 100;
                UpdateProgressCallback?.Invoke(ProgressValue, ProgressMax);
            };
            rc.OnFileProcessed += (sender, args) =>
            {
                if (args.ProcessedFile.Name.StartsWith(backupPath) && args.ProcessedFile.Name.Length > backupPath.Length)
                {
                    currentRoboCopyFile = args.ProcessedFile.Name.Substring(backupPath.Length + 1);
                    if (logEachFileCopied)
                    {
                        MLog.Debug($@"Robocopying {currentRoboCopyFile}");
                    }
                    backupStatus.BackupLocationStatus = LC.GetString(LC.string_interp_copyingX, currentRoboCopyFile);
                    UpdateStatusCallback?.Invoke(backupStatus.BackupLocationStatus);
                }
            };

            MLog.Information($@"Beginning robocopy restore: {backupPath} -> {destinationPath}");
            rc.Start().Wait();
            MLog.Information(@"Robocopy restore has completed");
        }

        private void ExecuteRestoreUsingRsync(string backupPath, string destinationPath, GameBackupStatus backupStatus, bool logEachFileCopied)
        {
            var rsyncSource = GetRsyncCompatiblePath(backupPath);
            var rsyncDestination = GetRsyncCompatiblePath(destinationPath);
            string rsyncArgs = BuildRsyncArgs(rsyncSource, rsyncDestination);

            MLog.Information($@"Beginning rsync restore: {rsyncSource} -> {rsyncDestination}");
            var started = TryExecuteRsyncProcess(@"rsync", rsyncArgs, backupStatus, logEachFileCopied, out var exitCode);
            if (!started)
            {
                MLog.Warning(@"Direct rsync invocation failed to start. Falling back to bash wrapper for Wine.");
                exitCode = ExecuteRestoreUsingRsyncViaBash(rsyncSource, rsyncDestination, backupStatus, logEachFileCopied);
            }

            if (exitCode != 0)
            {
                MLog.Warning($@"rsync restore finished with exit code {exitCode}");
                throw new Exception($@"rsync restore failed with exit code {exitCode}");
            }
            else
            {
                MLog.Information(@"Rsync restore has completed");
            }
        }

        private bool TryExecuteRsyncProcess(string executableName, string arguments, GameBackupStatus backupStatus, bool logEachFileCopied, out int exitCode)
        {
            exitCode = -1;
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executableName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                process.OutputDataReceived += (sender, args) => HandleRsyncOutputLine(args.Data, backupStatus, logEachFileCopied);
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        HandleRsyncOutputLine(args.Data, backupStatus, logEachFileCopied);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                exitCode = process.ExitCode;
                return true;
            }
            catch (Exception e)
            {
                MLog.Warning($@"Unable to start rsync process '{executableName}': {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Creates a bash script that restores the game's backup.
        /// Script takes a single argument, a path where to output the stdout.
        /// </summary>
        /// <param name="outputPath">Script will be written to this path</param>
        /// <param name="backupPath">Path to the game backup</param>
        /// <param name="destinationPath">Path to the game installtion to be restored</param>
        /// <returns></returns>
        private static void CreateRsyncScript(string outputPath, string backupPath, string destinationPath)
        {
            var shebang = @"#/usr/bin/env bash";
            var shortFlags = @"-av"; // a Archive v Verbose
            var longFlags = @"--delete"; // delete extra files from destination
            var excludesOption = "--exclude cmm_vanilla";

            var script = $"""
                {shebang}

                set -o pipefail

                # Nixos
                if [ -d "/run/current-system/sw/bin/" ]; then
                  RSYNC_PATH="/run/current-system/sw/bin/rsync"
                else
                  LD_LIBRARY_PATH="/run/host/usr/lib/x86_64-linux-gnu/"
                  RSYNC_PATH="/run/host/usr/bin/rsync"
                fi
                
                $RSYNC_PATH {shortFlags} {longFlags} {excludesOption} \
                "{backupPath}/" \
                "{destinationPath}/" 2>&1 \
                | tee "$1"

                echo "--- RESTORE COMPLETE (exit code $?) ---" >> "$1"
                """;
            File.WriteAllText(outputPath, script.Replace("\r", "").ToCharArray());
        }

        private int ExecuteRestoreUsingRsyncViaBash(string backupPath, string destinationPath, GameBackupStatus backupStatus, bool logEachFileCopied)
        {
            var logFile = $"/dev/shm/binm3-rsync-{Guid.NewGuid():N}.log";
            var scriptFile = $"/dev/shm/restore-rsync-{Guid.NewGuid():N}.sh";
            CreateRsyncScript(scriptFile, backupPath, destinationPath);
            var shellArguments = $"{scriptFile} {QuoteCommandArgument(logFile)}";
            var shellPath = @"/bin/sh";

            // Start cmd for mildly easier testing
            //using var process = new Process();
            //process.StartInfo = new ProcessStartInfo
            //{
            //    FileName = "cmd.exe",
            //    UseShellExecute = false,
            //    CreateNoWindow = false
            //};
            //process.Start();

            MLog.Information($"Running {shellPath} {shellArguments}");
            TryStartAndMonitorRsyncBash(shellPath, shellArguments, logFile, backupStatus, logEachFileCopied, out var exitCode);

            try
            {
                if (File.Exists(logFile))
                {
                    MLog.Information($@"Skipping deletion of {logFile}");
                    MLog.Information($@"Skipping deletion of {scriptFile}");
                    //File.Delete(logFile);
                    //File.Delete(scriptFile);
                }
            }
            catch (Exception e)
            {
                MLog.Warning($@"Unable to cleanup after restore: {e.Message}");
                //MLog.Warning($@"Unable to delete rsync temp log file: {e.Message}");
            }

            return exitCode;
        }

        private bool TryStartAndMonitorRsyncBash(string bashExecutable, string bashArguments, string logFile, GameBackupStatus backupStatus, bool logEachFileCopied, out int exitCode)
        {
            exitCode = -1;
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = bashExecutable,
                    Arguments = bashArguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (!process.Start())
                {
                    return false;
                }

                long filePosition = 0;
                bool done = false;
                while (!done)
                {
                    done = DrainRsyncLogFile(logFile, ref filePosition, backupStatus, logEachFileCopied, out exitCode);
                    System.Threading.Thread.Sleep(50);
                }

                process.WaitForExit();
                DrainRsyncLogFile(logFile, ref filePosition, backupStatus, logEachFileCopied, out exitCode);
                return true;
            }
            catch (Exception e)
            {
                MLog.Warning($@"Unable to start bash wrapper '{bashExecutable}': {e.Message}");
                return false;
            }
        }

        // Returns true if reached the exit phrase
        private bool DrainRsyncLogFile(string logFile, ref long filePosition, GameBackupStatus backupStatus, bool logEachFileCopied, out int exitCode)
        {
            // Check multiple times if logs exists
            int counter = 0;
            while (!File.Exists(logFile))
            {
                if (counter >= 5)
                {
                    MLog.Error($"Restore failed: Restore log {logFile} not created after {100*counter}.");
                    exitCode = -2;
                    return true;
                } else
                {
                    MLog.Information($"Restore log at {logFile} doesn't exist yet.");
                    System.Threading.Thread.Sleep(100);
                    counter++;
                }
            }

            using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(filePosition, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                HandleRsyncOutputLine(line, backupStatus, logEachFileCopied);
                if (line.Contains("--- RESTORE COMPLETE"))
                {
                    exitCode = Regex.Match(line, @"\(exit code (-?\d*)\)").Groups[1].Value.ToInt32();
                    MLog.Information($"Restore complete, exit code {exitCode}");
                    return true;
                }
            }

            filePosition = stream.Position;
            exitCode = 0;
            return false;
        }

        private void HandleRsyncOutputLine(string outputLine, GameBackupStatus backupStatus, bool logEachFileCopied)
        {
            if (string.IsNullOrWhiteSpace(outputLine))
            {
                return;
            }

            var progressMatch = Regex.Match(outputLine, @"(\d{1,3})%");
            if (progressMatch.Success && int.TryParse(progressMatch.Groups[1].Value, out var percentProgress))
            {
                SetProgressIndeterminateCallback?.Invoke(false);
                ProgressValue = percentProgress;
                ProgressMax = 100;
                UpdateProgressCallback?.Invoke(ProgressValue, ProgressMax);
                return;
            }

            if (outputLine.StartsWith(@"sent ") || outputLine.StartsWith(@"total size is "))
            {
                return;
            }

            if (outputLine.StartsWith(@"rsync:"))
            {
                MLog.Warning($@"rsync: {outputLine}");
                return;
            }

            if (logEachFileCopied)
            {
                MLog.Debug($@"Rsync copying {outputLine}");
            }

            backupStatus.BackupLocationStatus = LC.GetString(LC.string_interp_copyingX, outputLine);
            UpdateStatusCallback?.Invoke(backupStatus.BackupLocationStatus);
        }

        private static string BuildRsyncArgs(string rsyncSource, string rsyncDestination)
        {
            return $"-a --delete --info=progress2 --out-format=\"%n\" -- {QuoteCommandArgument(rsyncSource)} {QuoteCommandArgument(rsyncDestination)}";
        }

        private static string GetRsyncCompatiblePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var convertedPath = TryConvertWinePathToUnix(path);
            if (!string.IsNullOrWhiteSpace(convertedPath))
            {
                return convertedPath;
            }

            if (path.Length > 2 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            {
                var driveLetter = char.ToLowerInvariant(path[0]);
                var normalizedPath = path.Substring(2).Replace('\\', '/');
                if (driveLetter == 'z')
                {
                    return normalizedPath;
                }

                return $"/{driveLetter}{normalizedPath}";
            }

            return path.Replace('\\', '/');
        }

        private static string TryConvertWinePathToUnix(string path)
        {
            try
            {
                using var winePathProcess = new Process();
                winePathProcess.StartInfo = new ProcessStartInfo
                {
                    FileName = @"winepath",
                    Arguments = $"-u {QuoteCommandArgument(path)}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                winePathProcess.Start();
                var output = winePathProcess.StandardOutput.ReadToEnd();
                winePathProcess.WaitForExit();

                if (winePathProcess.ExitCode == 0)
                {
                    return output.Trim();
                }
            }
            catch (Exception e)
            {
                MLog.Warning($@"Unable to convert path via winepath: {e.Message}");
            }

            return null;
        }

        private static string QuoteCommandArgument(string argument)
        {
            if (argument == null)
            {
                return "\"\"";
            }

            return $"\"{argument.Replace("\"", "\\\"")}\"";
        }

        private static string EscapeBashArgument(string argument)
        {
            if (argument == null)
            {
                return "''";
            }

            return $"'{argument.Replace("'", "'\\''")}'";
        }


        /// <summary>
        /// Prevents the system from going to sleep during the restore operation
        /// </summary>
        /// <param name="keepAwake">True to prevent sleep, false to allow sleep</param>
        private void SetSleepPrevention(bool keepAwake)
        {
            if (keepAwake)
            {
                if (_keepAwakeTimer != null)
                {
                    _keepAwakeTimer.Elapsed += keepSystemAwake;
                    _keepAwakeTimer.Start();
                }
            }
            else
            {
                if (_keepAwakeTimer != null)
                {
                    _keepAwakeTimer.Stop();
                    _keepAwakeTimer.Elapsed -= keepSystemAwake;
                    _keepAwakeTimer.Dispose();
                    _keepAwakeTimer = null;
                }
                SystemSleepManager.AllowSleep();
            }
        }

        /// <summary>
        /// Timer callback that keeps the system awake during restore
        /// </summary>
        private void keepSystemAwake(object sender, ElapsedEventArgs e)
        {
            SystemSleepManager.PreventSleep(@"GameRestore");
        }
    }

    #endregion
}
