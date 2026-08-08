using LegendaryExplorerCore.Gammtek.Extensions;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Services.Backup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// Contains the Linux side of Game Restore.

namespace ME3TweaksCore.Services.Restore
{
    public partial class GameRestore
    {
        private void ExecuteRestoreUsingRsync(string backupPath, string destinationPath, GameBackupStatus backupStatus, bool logEachFileCopied)
        {
            var rsyncPath = GetRSyncPath();
            if (rsyncPath == null)
            {
                MLog.Error($@"Could not locate rsync on host system - cannot run backup.");
                throw new Exception(LC.GetString(LC.string_cannotLocateRsync));
            }

            var rsyncSource = GetRsyncCompatiblePath(backupPath);
            var rsyncDestination = GetRsyncCompatiblePath(destinationPath);

            MLog.Information($@"Beginning rsync restore: {rsyncPath} {rsyncSource} -> {rsyncDestination}");
            var exitCode = ExecuteRestoreUsingRsyncViaShell(rsyncSource, rsyncDestination, backupStatus, logEachFileCopied);

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

        private string GetRSyncPath()
        {
            if (File.Exists($@"Z:/run/host/usr/bin/rsync"))
            {
                return $@"/run/host/usr/bin/rsync";
            }

            if (File.Exists($@"Z:/run/current-system/sw/bin/rsync"))
            {
                return $@"/run/current-system/sw/bin/rsync";
            }

            // Not found
            return null;
        }

        /// <summary>
        /// Creates a shell script that restores the game's backup.
        /// Script takes a single argument, a path where to output the stdout.
        /// </summary>
        /// <param name="outputPath">Script will be written to this path</param>
        /// <param name="backupPath">Path to the game backup</param>
        /// <param name="destinationPath">Path to the game installtion to be restored</param>
        /// <returns></returns>
        private static void CreateRsyncScript(string outputPath, string backupPath, string destinationPath)
        {
            var shebang = @"#!/usr/bin/env bash";
            var shortFlags = @"-av"; // a Archive v Verbose
            var longFlags = @"--delete"; // delete extra files from destination
            var format = $@"--out-format={QuoteCommandArgument("%t %i %o %n")}";
            var excludesOption = @"--exclude cmm_vanilla";

            var script = $"""
                {shebang}

                set -o pipefail

                echo "Starting restore" >> "$1"
                # Nixos
                if [ -d "/run/current-system/sw/bin/" ]; then
                  RSYNC_PATH="/run/current-system/sw/bin/rsync"
                else
                  export LD_PRELOAD="$(find /run/host/usr/ -name libpopt.so.0 -print -quit 2>/dev/null)" # gets first libpopt.so.0 and exits
                  RSYNC_PATH="/run/host/usr/bin/rsync"
                fi
                
                $RSYNC_PATH {shortFlags} {longFlags} {format} {excludesOption} \
                "{backupPath}/" \
                "{destinationPath}/" 2>&1 \
                >> "$1"
                echo "$RSYNC_PATH" >> "$1"
                echo "--- RESTORE COMPLETE (exit code $?) ---" >> "$1"
                """;
            File.WriteAllText(outputPath, script.Replace("\r", "").ToCharArray()); // do not localize
        }

        private int ExecuteRestoreUsingRsyncViaShell(string backupPath, string destinationPath, GameBackupStatus backupStatus, bool logEachFileCopied)
        {
            var guid = Guid.NewGuid();
            var logFile = $@"/dev/shm/binm3-rsync-{guid}.log";
            var scriptFile = $@"/dev/shm/restore-rsync-{guid}.sh";
            CreateRsyncScript(scriptFile, backupPath, destinationPath);
            var shellArguments = $@"{scriptFile} {QuoteCommandArgument(logFile)}";
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

            MLog.Information($@"Running {shellPath} {shellArguments}");
            TryStartAndMonitorRsyncShell(shellPath, shellArguments, logFile, backupStatus, logEachFileCopied, out var exitCode);

            try
            {
                if (File.Exists(logFile))
                {
                    // dev/shm should clear up automatically over time
                    //MLog.Information($@"Skipping deletion of {logFile}");
                    //MLog.Information($@"Skipping deletion of {scriptFile}");
                    //File.Delete(logFile);
                    //File.Delete(scriptFile);
                }
            }
            catch (Exception e)
            {
                MLog.Warning($@"Unable to cleanup after restore: {e.Message}");
            }

            return exitCode;
        }

        private bool TryStartAndMonitorRsyncShell(string shellExecutable, string shellArguments, string logFile, GameBackupStatus backupStatus, bool logEachFileCopied, out int exitCode)
        {
            exitCode = -1;
            MLog.Information("TryStartAndMonitorRsyncShell");
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = shellExecutable,
                    Arguments = shellArguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Start();

                long filePosition = 0;
                bool done = false;
                while (!done)
                {
                    done = DrainRsyncLogFile(logFile, ref filePosition, backupStatus, logEachFileCopied, out exitCode);
                    System.Threading.Thread.Sleep(50);
                }

                return true;
            }
            catch (Exception e)
            {
                exitCode = -3;
                MLog.Warning($@"Unable to start shell wrapper '{shellExecutable}': {e.Message}");
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
                    MLog.Error($"Restore failed: Restore log {logFile} not created after {200 * counter}.");
                    exitCode = -2;
                    return true;
                }
                else
                {
                    MLog.Information($"Restore log at {logFile} doesn't exist yet.");
                    System.Threading.Thread.Sleep(200);
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
                if (line.StartsWith("--- RESTORE COMPLETE"))
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

            if (outputLine.StartsWith(@"Starting restore") || outputLine.StartsWith(@"sent ") || outputLine.StartsWith(@"total size is ") || outputLine.StartsWith(@"sending incremental file list") || outputLine.StartsWith(@"--- RESTORE COMPLETE") )
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
                MLog.Debug($@"Rsync {outputLine}");
            }

            // 1 date
            // 2 attribute updates
            // 3 action del./send/recv
            // 4 path to file
            var lineMatch = Regex.Match(outputLine, @"(\d*\/\S*\s\S*)\s*(\S*)\s*(\S*)\s(.*$)");
            DateTime date = DateTime.MinValue;
            string affectedFileAttrs = "";
            string rsyncAction = "unknown";
            string affectedFilePath = outputLine;
            if (lineMatch.Success)
            {
                date = lineMatch.Groups[1].Value.ToDateTime();
                affectedFileAttrs = lineMatch.Groups[2].Value;
                rsyncAction = lineMatch.Groups[3].Value;
                affectedFilePath = lineMatch.Groups[4].Value;
            }

            backupStatus.BackupLocationStatus = LC.GetString(LC.string_interp_copyingX, affectedFilePath);
            UpdateStatusCallback?.Invoke(backupStatus.BackupLocationStatus);
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
                    Arguments = $@"-u {QuoteCommandArgument(path)}",
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

            return $"\"{argument.Replace("\"", "\\\"")}\""; // do not localize
        }
    }
}
