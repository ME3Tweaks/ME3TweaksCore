using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.EventStream;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Gammtek.Extensions;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Targets;

namespace ME3TweaksCore.Helpers.MEM
{

    /// <summary>
    /// Flags for specifying Level of Detail (LOD) settings for textures in Mass Effect (OT) games.
    /// Can be combined using bitwise operations.
    /// </summary>
    [Flags]
    public enum LodSetting
    {
        /// <summary>
        /// Vanilla game LOD settings (no modifications)
        /// </summary>
        Vanilla = 0,
        /// <summary>
        /// 2K texture resolution limit
        /// </summary>
        TwoK = 1,
        /// <summary>
        /// 4K texture resolution (no limit)
        /// </summary>
        FourK = 2,
        /// <summary>
        /// Enable soft shadows mode
        /// </summary>
        SoftShadows = 4,
    }


    /// <summary>
    /// Utility class for interacting with MassEffectModderNoGui (MEM). 
    /// Provides methods for texture modding operations including installation, verification, and LOD management.
    /// All calls must be run on a background thread.
    /// </summary>
    public static class MEMIPCHandler
    {
        #region Static Property Changed

        /// <summary>
        /// Event raised when a static property value changes
        /// </summary>
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

        /// <summary>
        /// Gets or sets the version number of MassEffectModderNoGui for Original Trilogy games.
        /// Returns -1 if the version has not been retrieved yet.
        /// </summary>
        public static short MassEffectModderNoGuiVersionOT
        {
            get => _memNoGuiVersionOT;
            set => SetProperty(ref _memNoGuiVersionOT, value);
        }

        private static short _memNoGuiVersionLE = -1;

        /// <summary>
        /// Gets or sets the version number of MassEffectModderNoGui for Legendary Edition games.
        /// Returns -1 if the version has not been retrieved yet.
        /// </summary>
        public static short MassEffectModderNoGuiVersionLE
        {
            get => _memNoGuiVersionLE;
            set => SetProperty(ref _memNoGuiVersionLE, value);
        }

        /// <summary>
        /// Returns the version number for MEM, or 0 if it couldn't be retrieved
        /// </summary>
        /// <param name="classicMEM">True for Original Trilogy MEM, false for Legendary Edition MEM</param>
        /// <returns>The version number as a short, or 0 if retrieval failed</returns>
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

        /// <summary>
        /// Runs MassEffectModderNoGui with specified arguments and waits for it to exit.
        /// Handles IPC communication and process lifecycle management.
        /// </summary>
        /// <param name="classicMEM">True for Original Trilogy MEM, false for Legendary Edition MEM</param>
        /// <param name="arguments">Command-line arguments to pass to MEM</param>
        /// <param name="shouldWaitforExit">Whether the call should wait for MEM to exit</param>
        /// <param name="reasonCannotBeSafelyTerminated">Reason why this process cannot be safely terminated (if applicable)</param>
        /// <param name="applicationStarted">Callback invoked when the application starts, receives process ID</param>
        /// <param name="ipcCallback">Callback for handling IPC messages, receives command and parameter</param>
        /// <param name="applicationStdErr">Callback for standard error output</param>
        /// <param name="applicationExited">Callback invoked when application exits, receives exit code</param>
        /// <param name="setMEMCrashLog">Callback to receive crash log content if MEM crashes</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
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
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            void appStart(int processID)
            {
                MLog.Information($@"MassEffectModderNoGui launched, process ID: {processID}");
                applicationStarted?.Invoke(processID);
                try
                {
                    MEMProcessHandler.AddProcess(Process.GetProcessById(processID), shouldWaitforExit, reasonCannotBeSafelyTerminated);
                }
                catch (Exception e)
                {
                    // Couldn't add process, may have aleady existed
                    MLog.Warning($@"Couldn't add process to tracker - it may have already terminated: {e.Message}");
                }
            }

            void appExited(int code)
            {
                // We will log the start and stops.
                if (code == 0)
                    MLog.Information(@"MassEffectModderNoGui exited normally with code 0");
                else
                    MLog.Error($@"MassEffectModderNoGui exited abnormally with code {code}");

                applicationExited?.Invoke(code);
                tcs.TrySetResult(true);
            }

            StringBuilder crashLogBuilder = new StringBuilder();
            object crashLogLock = new object();

            void memCrashLogOutput(string str)
            {
                lock (crashLogLock)
                {
                    crashLogBuilder.AppendLine(str);
                }
            }

            // Run MEM
            var memTask = MEMIPCHandler.RunMEMIPC(classicMEM, arguments, appStart, ipcCallback, applicationStdErr, appExited,
                memCrashLogOutput,
                cancellationToken);

            // Wait until exit
            tcs.Task.Wait(cancellationToken);

            lock (crashLogLock)
            {
                if (crashLogBuilder.Length > 0)
                {
                    setMEMCrashLog?.Invoke(crashLogBuilder.ToString().Trim());
                }
            }
        }

        /// <summary>
        /// Runs MassEffectModderNoGui asynchronously with IPC communication enabled.
        /// Processes standard output, standard error, and IPC commands.
        /// </summary>
        /// <param name="classicMEM">True for Original Trilogy MEM, false for Legendary Edition MEM</param>
        /// <param name="arguments">Command-line arguments to pass to MEM</param>
        /// <param name="applicationStarted">Callback invoked when the application starts</param>
        /// <param name="ipcCallback">Callback for handling IPC messages</param>
        /// <param name="applicationStdErr">Callback for standard error output</param>
        /// <param name="applicationExited">Callback invoked when application exits</param>
        /// <param name="memCrashLine">Callback for individual crash log lines</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>A task representing the asynchronous operation</returns>
        private static async Task RunMEMIPC(bool classicMEM, string arguments, Action<int> applicationStarted = null,
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
        /// Converts MEM IPC output to command and parameter for handling. 
        /// This method assumes string starts with [IPC] always.
        /// </summary>
        /// <param name="str">The IPC string to parse, must start with [IPC]</param>
        /// <returns>A tuple containing the command name and its parameter value</returns>
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
        /// <param name="classicMEM">True for Original Trilogy MEM, false for Legendary Edition MEM</param>
        /// <param name="targetGame">The game to set the path for</param>
        /// <param name="targetPath">The filesystem path to the game installation</param>
        /// <returns>True if exit code is zero (success), false otherwise</returns>
        public static bool SetGamePath(bool classicMEM, MEGame targetGame, string targetPath)
        {
            // This doesn't work very well with LE on Windows as of version 533
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
        /// <param name="target">The game target containing game type and installation path</param>
        /// <returns>True if the game path was set successfully, false otherwise</returns>
        public static bool SetGamePath(GameTarget target)
        {
            return SetGamePath(target.Game.IsOTGame(), target.Game, target.TargetPath);
        }

        /// <summary>
        /// Sets the LODs (Level of Detail) as specified in the setting bitmask with MEM for the specified game.
        /// Only works for Original Trilogy games.
        /// </summary>
        /// <param name="game">The game to apply LOD settings to</param>
        /// <param name="setting">The LOD settings flags to apply</param>
        /// <returns>True if LODs were set successfully, false otherwise</returns>
        public static bool SetLODs(MEGame game, LodSetting setting)
        {
            if (game.IsLEGame())
            {
                MLog.Error(@"Cannot set LODs for LE games! This call shouldn't have been made, this is a bug.");
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
        /// Gets list of files in a compressed archive
        /// </summary>
        /// <param name="file">Path to the archive file</param>
        /// <returns>List of file paths contained in the archive</returns>
        public static List<string> GetFileListing(string file)
        {
            // Used by Linux
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
                MLog.Error($@"MassEffectModderNoGui had error getting file listing of archive {file}, exit code {exitcode}");
            }

            return fileListing;
        }

        /// <summary>
        /// Fetches the list of LODs (Level of Detail settings) for the specified game
        /// </summary>
        /// <param name="game">The game to retrieve LOD information for</param>
        /// <returns>Dictionary mapping LOD identifiers to their values, or null if an error occurred</returns>
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
        /// Enumeration used to pass game directory data back to ALOT Installer Core. 
        /// DO NOT CHANGE VALUES AS THEY ARE INDIRECTLY REFERENCED
        /// </summary>
        public enum GameDirPath
        {
            /// <summary>Mass Effect 1 game installation path</summary>
            ME1GamePath,
            /// <summary>Mass Effect 1 configuration path</summary>
            ME1ConfigPath,
            /// <summary>Mass Effect 2 game installation path</summary>
            ME2GamePath,
            /// <summary>Mass Effect 2 configuration path</summary>
            ME2ConfigPath,
            /// <summary>Mass Effect 3 game installation path</summary>
            ME3GamePath,
            /// <summary>Mass Effect 3 configuration path</summary>
            ME3ConfigPath,
        }

        /// <summary>
        /// Returns location of the game and config paths (on Linux) as defined by MEM, or null if game can't be found.
        /// </summary>
        /// <param name="originalTrilogy">True for Original Trilogy, false for Legendary Edition</param>
        /// <returns>Dictionary mapping GameDirPath enums to their filesystem paths</returns>
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

        // This was when this project could run on Linux. Leaving for reference.
#if ALOT && !WINDOWS
        /// <summary>
        /// Sets the configuration path for a game (Linux only).
        /// Only works on Linux builds of MEM.
        /// </summary>
        /// <param name="game">The game to set the config path for</param>
        /// <param name="itemValue">The configuration directory path</param>
        /// <returns>True if the config path was set successfully, false otherwise</returns>
        public static bool SetConfigPath(MEGame game, string itemValue)
        {
            int exitcode = 0;
            string args = $"--set-game-user-path --gameid {game.ToGameNum()} --path \"{itemValue}\""; //do not localize
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
        /// <returns>MEMSessionResult containing installation results, errors, and exit code</returns>
        public static MEMSessionResult InstallMEMFiles(GameTarget target, string memFileListFile, Action<string> currentActionCallback = null, Action<int> progressCallback = null, bool setGamePath = true, bool skipMarkers = false)
        {
            MEMSessionResult result = new MEMSessionResult(); // Install session flag is set during stage context switching
            if (setGamePath)
            {
                MEMIPCHandler.SetGamePath(target);
            }

            currentActionCallback?.Invoke(LC.GetString(LC.string_preparingToInstallTextures));
            var cmdParams = $@"--install-mods --gameid {target.Game.ToMEMGameNum()} --input ""{memFileListFile}"" --verify --ipc";

            if (skipMarkers)
            {
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
        /// Checks a target for texture install markers, and returns a list of packages containing texture markers on them. 
        /// This only is run on a game target that is not texture modded.
        /// </summary>
        /// <param name="target">The game target to check for markers</param>
        /// <param name="currentActionCallback">Callback to update UI with current action text</param>
        /// <param name="progressCallback">Callback to report progress percentage</param>
        /// <param name="setGamePath">Whether to set the game path before checking</param>
        /// <returns>MEMSessionResult containing found markers in the Errors list, or null if target is already texture modded</returns>
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
        /// Checks the texture map for consistency to the current game state (added/removed and replaced files). 
        /// This is run in two stages and only is run on games that are already texture modded.
        /// </summary>
        /// <param name="target">The game target to check texture map consistency for</param>
        /// <param name="currentActionCallback">Callback to update UI with current action text</param>
        /// <param name="progressCallback">Callback to report progress percentage</param>
        /// <param name="setGamePath">Whether to set the game path before checking</param>
        /// <returns>Object containing all texture map desynchronizations in the errors list, or null if target is not texture modded</returns>
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
        /// Determines if a file should be ignored by texture consistency check
        /// </summary>
        /// <param name="s">The file path to check</param>
        /// <returns>True if the file should be ignored, false otherwise</returns>
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

        /// <summary>
        /// Converts a Legendary Edition relative path to a shorter display path by removing the /Game/MEX/ prefix
        /// </summary>
        /// <param name="LERelativePath">The full relative path from LE</param>
        /// <returns>Shortened path with prefix removed, or original path if too short or null</returns>
        private static string GetShortPath(string LERelativePath)
        {
            if (string.IsNullOrWhiteSpace(LERelativePath) || LERelativePath.Length < 11) return LERelativePath;
            return LERelativePath.Substring(10); // Remove /Game/MEX/

        }
    }
}
