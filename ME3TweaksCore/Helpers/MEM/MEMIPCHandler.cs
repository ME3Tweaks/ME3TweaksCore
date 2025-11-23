using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using CliWrap;
using CliWrap.EventStream;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Targets;

namespace ME3TweaksCore.Helpers.MEM
{

    [Flags]
    public enum LodSetting
    {
        Vanilla = 0,
        TwoK = 1,
        FourK = 2,
        SoftShadows = 4,
    }


    /// <summary>
    /// Utility class for interacting with MEM. Calls must be run on a background thread of
    /// </summary>
    public static class MEMIPCHandler
    {
        #region Static Property Changed

        public static event PropertyChangedEventHandler StaticPropertyChanged;

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

        private static short _memNoGuiVersionOT = -1;

        public static short MassEffectModderNoGuiVersionOT
        {
            get => _memNoGuiVersionOT;
            set => SetProperty(ref _memNoGuiVersionOT, value);
        }

        private static short _memNoGuiVersionLE = -1;

        public static short MassEffectModderNoGuiVersionLE
        {
            get => _memNoGuiVersionLE;
            set => SetProperty(ref _memNoGuiVersionLE, value);
        }

        /// <summary>
        /// Returns the version number for MEM, or 0 if it couldn't be retreived
        /// </summary>
        /// <returns></returns>
        public static short GetMemVersion(bool classicMEM)
        {
            // If the current version doesn't support the --version --ipc, we just assume it is 0.
            MEMIPCHandler.RunMEMIPCUntilExit(classicMEM, @"--version --ipc", false, ipcCallback: (command, param) =>
            {
                if (command == @"VERSION")
                {
                    if (classicMEM)
                    {
                        MassEffectModderNoGuiVersionOT = short.Parse(param);
                    }
                    else
                    {
                        MassEffectModderNoGuiVersionLE = short.Parse(param);
                    }
                }
            });

            return classicMEM ? MassEffectModderNoGuiVersionOT : MassEffectModderNoGuiVersionLE;
        }

        public static void RunMEMIPCUntilExit(bool classicMEM,
            string arguments,
            bool shouldWaitforExit,
            string reasonCannotBeSafelyTerminated = null,
            Action<int> applicationStarted = null,
            Action<string, string> ipcCallback = null,
            Action<string> applicationStdErr = null,
            Action<int> applicationExited = null,
            Action<string> setMEMCrashLog = null,
            CancellationToken cancellationToken = default)
        {

            object lockObject = new object();

            void appStart(int processID)
            {
                MLog.Information($@"MassEffectModderNoGui launched, process ID: {processID}");
                applicationStarted?.Invoke(processID);
                MEMProcessHandler.AddProcess(Process.GetProcessById(processID), shouldWaitforExit, reasonCannotBeSafelyTerminated);
            }

            void appExited(int code)
            {
                // We will log the start and stops.
                if (code == 0)
                    MLog.Information(@"MassEffectModderNoGui exited normally with code 0");
                else
                    MLog.Error($@"MassEffectModderNoGui exited abnormally with code {code}");

                applicationExited?.Invoke(code);
                lock (lockObject)
                {
                    Monitor.Pulse(lockObject);
                }
            }

            StringBuilder crashLogBuilder = new StringBuilder();

            void memCrashLogOutput(string str)
            {
                crashLogBuilder.AppendLine(str);
            }

            // Run MEM
            MEMIPCHandler.RunMEMIPC(classicMEM, arguments, appStart, ipcCallback, applicationStdErr, appExited,
                memCrashLogOutput,
                cancellationToken);

            // Wait until exit
            lock (lockObject)
            {
                Monitor.Wait(lockObject);
            }

            if (crashLogBuilder.Length > 0)
            {
                setMEMCrashLog?.Invoke(crashLogBuilder.ToString().Trim());
            }
        }

        private static async void RunMEMIPC(bool classicMEM, string arguments, Action<int> applicationStarted = null,
            Action<string, string> ipcCallback = null, Action<string> applicationStdErr = null,
            Action<int> applicationExited = null, Action<string> memCrashLine = null,
            CancellationToken cancellationToken = default)
        {
            bool exceptionOcurred = false;
            DateTime lastCacheoutput = DateTime.Now;

            void internalHandleIPC(string command, string parm)
            {
                switch (command)
                {
                    case @"CACHE_USAGE":
                        if (DateTime.Now > (lastCacheoutput.AddSeconds(10)))
                        {
                            MLog.Information($@"MEM cache usage: {FileSize.FormatSize(long.Parse(parm))}");
                            lastCacheoutput = DateTime.Now;
                        }

                        break;
                    case @"EXCEPTION_OCCURRED": //An exception has occurred and MEM is going to crash
                        exceptionOcurred = true;
                        ipcCallback?.Invoke(command, parm);
                        break;
                    default:
                        ipcCallback?.Invoke(command, parm);
                        break;
                }
            }

            // No validation. Make sure exit code is checked in the calling process.
            var memPath = MCoreFilesystem.GetMEMNoGuiPath(classicMEM);
            string lastProcessedFile = null;

            var cmd = Cli.Wrap(memPath).WithArguments(arguments).WithValidation(CommandResultValidation.None);
            Debug.WriteLine($@"Launching process: {memPath} {arguments}");

            // GET MEM ENCODING
            FileVersionInfo mvi = FileVersionInfo.GetVersionInfo(memPath);
            Encoding encoding = mvi.FileMajorPart > 421 ? Encoding.Unicode : Encoding.UTF8; //? Is UTF8 the default for windows console

            await foreach (var cmdEvent in cmd.ListenAsync(encoding, cancellationToken))

            {
                switch (cmdEvent)
                {
                    case StartedCommandEvent started:
                        applicationStarted?.Invoke(started.ProcessId);
                        break;
                    case StandardOutputCommandEvent stdOut:
#if DEBUG
                        if (!stdOut.Text.StartsWith(@"[IPC]CACHE_USAGE"))
                        {
                            Debug.WriteLine(stdOut.Text);
                        }
#endif
                        if (stdOut.Text.StartsWith(@"[IPC]"))
                        {
                            var ipc = breakdownIPC(stdOut.Text);
                            if (ipc.command == @"PROCESSING_FILE" && ipc.param != null)
                            {
                                lastProcessedFile = ipc.param;
                            }
                            internalHandleIPC(ipc.command, ipc.param);
                        }
                        else
                        {
                            if (exceptionOcurred)
                            {
                                if (lastProcessedFile != null)
                                {
                                    MLog.Fatal($@"MEM crashed - last processed file was {lastProcessedFile}");
                                    lastProcessedFile = null; // don't print again
                                }
                                MLog.Fatal($@"{stdOut.Text}");
                                memCrashLine?.Invoke(stdOut.Text);
                            }
                        }

                        break;
                    case StandardErrorCommandEvent stdErr:
                        Debug.WriteLine(@"STDERR " + stdErr.Text);
                        if (exceptionOcurred)
                        {
                            MLog.Fatal($@"{stdErr.Text}");
                        }
                        else
                        {
                            applicationStdErr?.Invoke(stdErr.Text);
                        }

                        break;
                    case ExitedCommandEvent exited:
                        applicationExited?.Invoke(exited.ExitCode);
                        break;
                }
            }
        }

        /// <summary>
        /// Converts MEM IPC output to command, param for handling. This method assumes string starts with [IPC] always.
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        private static (string command, string param) breakdownIPC(string str)
        {
            string command = str.Substring(5);
            int endOfCommand = command.IndexOf(' ');
            if (endOfCommand >= 0)
            {
                command = command.Substring(0, endOfCommand);
            }

            string param = str.Substring(endOfCommand + 5).Trim();
            return (command, param);
        }

        /// <summary>
        /// Sets the path MEM will use for the specified game
        /// </summary>
        /// <param name="targetGame"></param>
        /// <param name="targetPath"></param>
        /// <returns>True if exit code is zero</returns>
        public static bool SetGamePath(bool classicMEM, MEGame targetGame, string targetPath)
        {
            int exitcode = 0;

            string args = $@"--set-game-data-path --gameid {targetGame.ToMEMGameNum()} --path ""{targetPath}""";
            MEMIPCHandler.RunMEMIPCUntilExit(classicMEM, args, false, applicationExited: x => exitcode = x);
            if (exitcode != 0)
            {
                MLog.Error($@"Non-zero MassEffectModderNoGui exit code setting game path: {exitcode}");
            }

            return exitcode == 0;
        }


        /// <summary>
        /// Sets MEM up to use the specified target for texture modding
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static bool SetGamePath(GameTarget target)
        {
            return SetGamePath(target.Game.IsOTGame(), target.Game, target.TargetPath);
        }

        /// <summary>
        /// Sets the LODs as specified in the setting bitmask with MEM for the specified game
        /// </summary>
        /// <param name="game"></param>
        /// <param name="setting"></param>
        /// <returns></returns>
        public static bool SetLODs(MEGame game, LodSetting setting)
        {
            if (game.IsLEGame())
            {
                MLog.Error(@"Cannot set LODs for LE games! This is a bug.");
                return false;
            }

            string args = $@"--apply-lods-gfx --gameid {game.ToMEMGameNum()}";
            if (setting.HasFlag(LodSetting.SoftShadows))
            {
                args += @" --soft-shadows-mode --meuitm-mode";
            }

            if (setting.HasFlag(LodSetting.TwoK))
            {
                args += @" --limit-2k";
            }
            else if (setting.HasFlag(LodSetting.FourK))
            {
                // Nothing
            }
            else if (setting == LodSetting.Vanilla)
            {
                // Remove LODs
                args = $@"--remove-lods --gameid {game.ToMEMGameNum()}";
            }

            int exitcode = -1;
            // We don't care about IPC on this
            MEMIPCHandler.RunMEMIPCUntilExit(true, args,
                false, null, null, null,
                x => MLog.Error($@"StdError setting LODs: {x}"),
                x => exitcode = x); //Change to catch exit code of non zero.        
            if (exitcode != 0)
            {
                MLog.Error($@"MassEffectModderNoGui had error setting LODs, exited with code {exitcode}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets list of files in an archive
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static List<string> GetFileListing(string file)
        {
            string args = $"--list-archive --input \"{file}\" --ipc"; //do not localize
            List<string> fileListing = new List<string>();

            int exitcode = -1;
            MEMIPCHandler.RunMEMIPCUntilExit(false, args,
                false, null,
                ipcCallback: (command, param) =>
                {
                    if (command == @"FILENAME")
                    {
                        fileListing.Add(param);
                    }
                },
                applicationStdErr: x => MLog.Error($@"StdError getting file listing for file {file}: {x}"),
                applicationExited: x => exitcode = x); //Change to catch exit code of non zero.        
            if (exitcode != 0)
            {
                MLog.Error(
                    $@"MassEffectModderNoGui had error getting file listing of archive {file}, exit code {exitcode}");
            }

            return fileListing;
        }

        /// <summary>
        /// Fetches the list of LODs for the specified game
        /// </summary>
        /// <param name="game"></param>
        /// <returns></returns>
        public static Dictionary<string, string> GetLODs(MEGame game)
        {
            Dictionary<string, string> lods = new Dictionary<string, string>();
            var args = $@"--print-lods --gameid {game.ToMEMGameNum()} --ipc";
            int exitcode = -1;
            MEMIPCHandler.RunMEMIPCUntilExit(game.IsOTGame(), args, false, ipcCallback: (command, param) =>
            {
                switch (command)
                {
                    case @"LODLINE":
                        var lodSplit = param.Split(@"=");
                        try
                        {
                            lods[lodSplit[0]] = param.Substring(lodSplit[0].Length + 1);
                        }
                        catch (Exception e)
                        {
                            MLog.Error($@"Error reading LOD line output from MEM: {param}, {e.Message}");
                        }

                        break;
                    default:
                        //Debug.WriteLine(@"oof?");
                        break;
                }
            },
                applicationExited: x => exitcode = x
            );
            if (exitcode != 0)
            {
                MLog.Error($@"Error fetching LODs for {game}, exit code {exitcode}");
                return null; // Error getting LODs
            }

            return lods;
        }

        /// <summary>
        /// Used to pass data back to installer core. DO NOT CHANGE VALUES AS
        /// THEY ARE INDIRECTLY REFERENCED
        /// </summary>
        public enum GameDirPath
        {
            ME1GamePath,
            ME1ConfigPath,
            ME2GamePath,
            ME2ConfigPath,
            ME3GamePath,
            ME3ConfigPath,
        }

        /// <summary>
        /// Returns location of the game and config paths (on linux) as defined by MEM, or null if game can't be found.
        /// </summary>
        /// <param name="game"></param>
        /// <returns></returns>
        public static Dictionary<GameDirPath, string> GetGameLocations(bool originalTrilogy)
        {
            Dictionary<GameDirPath, string> result = new Dictionary<GameDirPath, string>();
            MEMIPCHandler.RunMEMIPCUntilExit(originalTrilogy, $@"--get-game-paths --ipc",
                false,
                ipcCallback: (command, param) =>
                {
                    // THIS CODE ONLY WORKS ON OT
                    // LE REPORTS DIFFERENTLY
                    var spitIndex = param.IndexOf(' ');
                    if (spitIndex < 0) return; // This is nothing
                    var gameId = param.Substring(0, spitIndex);
                    var path = Path.GetFullPath(param.Substring(spitIndex + 1, param.Length - (spitIndex + 1)));
                    switch (command)
                    {
                        case @"GAMEPATH":
                            {
                                var keyname = Enum.Parse<GameDirPath>($@"ME{gameId}GamePath");
                                if (param.Length > 1)
                                {
                                    result[keyname] = path;
                                }
                                else
                                {
                                    result[keyname] = null;
                                }

                                break;
                            }
                        case @"GAMECONFIGPATH":
                            {
                                var keyname = Enum.Parse<GameDirPath>($@"ME{gameId}ConfigPath");
                                if (param.Length > 1)
                                {
                                    result[keyname] = path;
                                }
                                else
                                {
                                    result[keyname] = null;
                                }

                                break;
                            }
                    }
                });
            return result;
        }

#if !WINDOWS
                // Only works on Linux builds of MEM
                public static bool SetConfigPath(MEGame game, string itemValue)
                {
                    int exitcode = 0;
                    string args =
 $"--set-game-user-path --gameid {game.ToGameNum()} --path \"{itemValue}\""; //do not localize
                    MEMIPCHandler.RunMEMIPCUntilExit(args, applicationExited: x => exitcode = x);
                    if (exitcode != 0)
                    {
                        MLog.Error($@"Non-zero MassEffectModderNoGui exit code setting game config path: {exitcode}");
                    }
                    return exitcode == 0;
                }
#endif



        /// <summary>
        /// Installs a MEM List File (MFL) to the game specified. This call does NOT ensure MEM exists.
        /// </summary>
        /// <param name="target">Target to install textures to</param>
        /// <param name="memFileListFile">The path to the MFL file that MEM will use to install</param>
        /// <param name="currentActionCallback">A delegate to set UI text to inform the user of what is occurring</param>
        /// <param name="progressCallback">Percentage-based progress indicator for the current stage</param>
        /// <param name="setGamePath">If the game path should be set. Setting to false can save a bit of time if you know the path is already correct.</param>
        /// <param name="skipMarkers">If the markers step should be skipped in install</param>
        public static MEMSessionResult InstallMEMFiles(GameTarget target, string memFileListFile, Action<string> currentActionCallback = null, Action<int> progressCallback = null, bool setGamePath = true, bool skipMarkers = false)
        {
            MEMSessionResult result = new MEMSessionResult(); // Install session flag is set during stage context switching
            if (setGamePath)
            {
                MEMIPCHandler.SetGamePath(target);
            }

            currentActionCallback?.Invoke(LC.GetString(LC.string_preparingToInstallTextures));
            var cmdParams = $@"--install-mods --gameid {target.Game.ToMEMGameNum()} --input ""{memFileListFile}"" --verify --ipc";

            if (skipMarkers) {
                cmdParams += $@" --skip-markers";
            }

            MEMIPCHandler.RunMEMIPCUntilExit(target.Game.IsOTGame(), cmdParams,
                true,
                LC.GetString(LC.string_dialog_memRunningCloseAttempt),
                applicationExited: code => { result.ExitCode = code; },
                applicationStarted: pid =>
                {
                    MLog.Information($@"MassEffectModder process started with PID {pid}");
                    result.ProcessID = pid;
                },
                setMEMCrashLog: crashMsg =>
                {
                    result.AddError(LC.GetString(LC.string_interp_lastFileBeingProcessedWasX, result.CurrentFile));
                    result.AddError(crashMsg);
                    MLog.Fatal(crashMsg); // MEM died
                },
                ipcCallback: (command, param) =>
                {
                    switch (command)
                    {
                        // Stage context switch
                        case @"STAGE_CONTEXT":
                            {
                                MLog.Information($@"MEM stage context switch to: {param}");
                                progressCallback?.Invoke(0); // Reset progress to 0
                                switch (param)
                                {
                                    // OT-ME3 ONLY - DLC is unpacked for use
                                    case @"STAGE_UNPACKDLC":
                                        result.IsInstallSession = true; // Once we move to this stage, we now are modifying the game. This is not used in any game but ME3.
                                        currentActionCallback?.Invoke(LC.GetString(LC.string_unpackingDLC));
                                        break;
                                    // The game file sizes are compared against the precomputed texture map
                                    case @"STAGE_PRESCAN":
                                        currentActionCallback?.Invoke(LC.GetString(LC.string_checkingGameData));
                                        break;
                                    // The files that differ from precomputed texture map are inspected and merged into the used texture map
                                    case @"STAGE_SCAN":
                                        currentActionCallback?.Invoke(LC.GetString(LC.string_scanningGameTextures));
                                        break;
                                    // Package files are updated and data is stored in them for the lower mips
                                    case @"STAGE_INSTALLTEXTURES":
                                        result.IsInstallSession = true; // Once we move to this stage, we now are modifying the game.
                                        currentActionCallback?.Invoke(LC.GetString(LC.string_installingTextures));
                                        break;
                                    // Textures that were installed are checked for correct magic numbers
                                    case @"STAGE_VERIFYTEXTURES":
                                        currentActionCallback?.Invoke(LC.GetString(LC.string_verifyingTextures));
                                        break;
                                    // Non-texture modded files are tagged as belonging to a texture mod installation so they cannot be moved across installs
                                    case @"STAGE_MARKERS":
                                        currentActionCallback?.Invoke(LC.GetString(LC.string_installingMarkers));
                                        break;
                                    default:
                                        // REPACK - that's for OT only?

                                        break;

                                }
                            }
                            break;
                        case @"PROCESSING_FILE":
                            MLog.Information($@"MEM processing file: {param}");
                            result.CurrentFile = GetShortPath(param);
                            break;
                        case @"ERROR_REFERENCED_TFC_NOT_FOUND":
                            MLog.Error($@"MEM: Texture references a TFC that was not found in game: {param}");
                            result.AddError(LC.GetString(LC.string_interp_textureReferencesTFCNotFoundY, param));
                            break;
                        case @"ERROR_FILE_NOT_COMPATIBLE":
                            MLog.Error($@"MEM: This file is not listed as compatible with {target.Game}: {param}");
                            result.AddError(LC.GetString(LC.string_interp_XIsNotCompatibleWithY, param, target.Game));
                            break;
                        case @"ERROR":
                            MLog.Error($@"MEM: Error occurred: {param}");
                            result.AddError(LC.GetString(LC.string_interp_anErrorOccurredDuringInstallationX, param));
                            break;
                        case @"TASK_PROGRESS":
                            {
                                progressCallback?.Invoke(int.Parse(param));
                                break;
                            }
                        default:
                            Debug.WriteLine($@"{command}: {param}");
                            break;
                    }
                });
            return result;
        }

        /// <summary>
        /// Checks a target for texture install markers, and returns a list of packages containing texture markers on them. This only is run on a game target that is not texture modded.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="currentActionCallback"></param>
        /// <param name="progressCallback"></param>
        public static MEMSessionResult CheckForMarkers(GameTarget target, Action<string> currentActionCallback = null, Action<int> progressCallback = null, bool setGamePath = true)
        {
            if (target.TextureModded) return null;
            if (setGamePath)
            {
                MEMIPCHandler.SetGamePath(target);
            }

            // If not texture modded, we check for presense of MEM marker on files
            // which tells us this was part of a different texture installation
            // and can easily break stuff in the game

            // Markers will be stored in the 'Errors' variable.
            MEMSessionResult result = new MEMSessionResult();
            if (target.TextureModded)
            {
                MLog.Information(@"Checking for missing texture markers with MEM");
                currentActionCallback?.Invoke(LC.GetString(LC.string_checkingCurrentInstallation));
            }
            else
            {
                MLog.Information(@"Checking for existing texture markers with MEM");
                currentActionCallback?.Invoke(LC.GetString(LC.string_checkingForExistingMarkers));
            }

            MEMIPCHandler.RunMEMIPCUntilExit(target.Game.IsOTGame(), $@"--check-for-markers --gameid {target.Game.ToMEMGameNum()} --ipc",
                false,
                applicationExited: code => result.ExitCode = code,
                applicationStarted: pid =>
                {
                    MLog.Information($@"MassEffectModder process started with PID {pid}");
                    result.ProcessID = pid;
                },
                setMEMCrashLog: crashMsg =>
                {
                    MLog.Fatal(crashMsg); // MEM died
                },
                ipcCallback: (command, param) =>
                {
                    switch (command)
                    {
                        case @"TASK_PROGRESS":
                            if (int.TryParse(param, out var percent))
                            {
                                progressCallback?.Invoke(percent);
                            }
                            break;
                        case @"FILENAME":
                            // Not sure what's going on here...
                            // Debug.WriteLine(param);
                            break;
                        case @"ERROR_FILEMARKER_FOUND":
                            if (!target.TextureModded)
                            {
                                // If not texture modded, we found file part of a different install
                                //  MLog.Error($"Package file was part of a different texture installation: {param}");
                                result.AddError(param);
                            }
                            break;
                        default:
                            Debug.WriteLine($@"{command}: {param}");
                            break;
                    }
                });

            return result;
        }

        /// <summary>
        /// Checks the texture map for consistency to the current game state (added/removed and replaced). This is run in two stages and only is run on games that are not already texture modded.
        /// </summary>
        /// <returns>Object containing all texture map desynchronizations in the errors list.</returns>
        public static MEMSessionResult CheckTextureMapConsistencyAddedRemoved(GameTarget target, Action<string> currentActionCallback = null, Action<int> progressCallback = null, bool setGamePath = true)
        {
            if (!target.TextureModded) return null; // We have nothing to check

            if (setGamePath)
            {
                MEMIPCHandler.SetGamePath(target);
            }

            var result = new MEMSessionResult();
            MLog.Information(@"Checking texture map consistency with MEM");
            currentActionCallback?.Invoke(LC.GetString(LC.string_checkingTextureMapConsistency));

            int stageMultiplier = 0;
            // This is the list of added files.
            // We use this to suppress duplicates when a vanilla file is found
            // e.g. new mod is installed, it will not have marker
            // and it will also not be in texture map.
            var addedFiles = new List<string>();

            string[] argsToRun = new[]
            {
                $@"--check-game-data-mismatch --gameid {target.Game.ToMEMGameNum()} --ipc", // Added/Removed
                $@"--check-game-data-after --gameid {target.Game.ToMEMGameNum()} --ipc", // Replaced
            };

            foreach (var args in argsToRun)
            {
                stageMultiplier++;
                MEMIPCHandler.RunMEMIPCUntilExit(target.Game.IsOTGame(), args,
                    false,
                    applicationExited: code => result.ExitCode = code,
                    applicationStarted: pid =>
                    {
                        MLog.Information($@"MassEffectModder process started with PID {pid}");
                        result.ProcessID = pid;
                    },
                    setMEMCrashLog: crashMsg =>
                    {
                        MLog.Fatal(crashMsg); // MEM died
                    },
                    ipcCallback: (command, param) =>
                    {
                        switch (command)
                        {
                            case @"TASK_PROGRESS":
                                if (int.TryParse(param, out var percent))
                                {
                                    // Two stages so we divide by two and then multiply by the result
                                    progressCallback?.Invoke((percent / 2 * stageMultiplier));
                                }

                                break;
                            case @"ERROR_REMOVED_FILE":
                                MLog.Error($@"MEM: File was removed from game after texture scan took place: {GetShortPath(param)}");
                                result.AddError(LC.GetString(LC.string_interp_fileWasRemovedX, GetShortPath(param)));
                                break;
                            case @"ERROR_ADDED_FILE":
                                MLog.Error($@"MEM: File was added to game after texture scan took place: {GetShortPath(param)}");
                                result.AddError(LC.GetString(LC.string_interp_fileWasAddedX, GetShortPath(param)));
                                addedFiles.Add(param); //Used to suppress vanilla mod file
                                break;
                            case @"ERROR_VANILLA_MOD_FILE":
                                if (!addedFiles.Contains(param, StringComparer.InvariantCultureIgnoreCase) && !IsIgnoredFile(param))
                                {
                                    MLog.Error($@"MEM: File was replaced in game after texture scan took place: {GetShortPath(param)}");
                                    result.AddError(LC.GetString(LC.string_interp_fileWasReplacedX, GetShortPath(param)));
                                }
                                break;
                            default:
                                Debug.WriteLine($@"{command}: {param}");
                                break;
                        }
                    });
            }

            return result;
        }

        /// <summary>
        /// Determines if file is ignored by texture consistency check
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private static bool IsIgnoredFile(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false; // ???
            var name = Path.GetFileName(s).ToLower();
            switch (name)
            {
                case @"sfxtest.pcc":
                case @"plotmanager.pcc":
                    return true;
                default:
                    return false;
            }
        }

        private static string GetShortPath(string LERelativePath)
        {
            if (string.IsNullOrWhiteSpace(LERelativePath) || LERelativePath.Length < 11) return LERelativePath;
            return LERelativePath.Substring(10); // Remove /Game/MEX/

        }
    }
}
