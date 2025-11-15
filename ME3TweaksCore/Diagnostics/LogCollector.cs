using AuthenticodeExaminer;
using Flurl.Http;
using LegendaryExplorerCore.Compression;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Gammtek.Extensions;
using LegendaryExplorerCore.Gammtek.Paths;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.ME1.Unreal.UnhoodBytecode;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Helpers.ME1;
using ME3TweaksCore.Helpers.MEM;
using ME3TweaksCore.Localization;
using ME3TweaksCore.ME3Tweaks.M3Merge.Bio2DATable;
using ME3TweaksCore.Misc;
using ME3TweaksCore.NativeMods;
using ME3TweaksCore.NativeMods.Interfaces;
using ME3TweaksCore.Objects;
using ME3TweaksCore.Services;
using ME3TweaksCore.Services.Shared.BasegameFileIdentification;
using ME3TweaksCore.Services.ThirdPartyModIdentification;
using ME3TweaksCore.Targets;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using NickStrupat;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text;
using System.Threading;

namespace ME3TweaksCore.Diagnostics
{
    /// <summary>
    /// Contains classes and information used by the ME3Tweaks Log Viewer service on the website
    /// </summary>
    internal class ME3TweaksLogViewer
    {
        /// <summary>
        /// Used to colorize the log
        /// </summary>
        public enum LogSeverity
        {
            INFO,
            WARN,
            ERROR,
            FATAL,
            GOOD,
            DIAGSECTION,
            BOLD,
            DLC,
            GAMEID,
            OFFICIALDLC,
            TPMI,
            SUB,
            BOLDBLUE,
            SUPERCEDANCE_FILE,
            SAVE_FILE_HASH_NAME,
            NOPRE
        }
        public class InstalledDLCStruct
        {
            // Used to tell log viewer which version we have to parse
            private const int SERVERCODE_VER = 4;

            /// <summary>
            /// MetaCMM name
            /// </summary>
            public string ModName { get; set; }
            public string DLCFolderName { get; set; }
            public int NexusUpdateCode { get; set; }
            public int MountPriority { get; set; }
            public string InstalledBy { get; set; }
            public string VersionInstalled { get; set; }
            public DateTime? InstallTime { get; set; }
            public IEnumerable<string> InstalledOptions { get; set; }
            public bool IsOfficialDLC { get; set; }

            private List<string> errors = new List<string>();

            public void PrintToDiag(Action<string, LogSeverity> printToDiagFunc)
            {
                StringBuilder sb = new StringBuilder();
                LogSeverity severity;
                if (IsOfficialDLC)
                {
                    severity = LogSeverity.OFFICIALDLC;
                    sb.Append(DLCFolderName);
                    printToDiagFunc(sb.ToString(), severity);
                }
                else
                {
                    severity = LogSeverity.DLC;
                    sb.Append(SERVERCODE_VER);
                    sb.Append(@";;");
                    sb.Append(DLCFolderName);
                    sb.Append(@";;");
                    sb.Append(ModName); // Useful if not found in TPMI
                    // Mod Version
                    sb.Append(@";;");
                    if (VersionInstalled != null)
                    {
                        sb.Append(VersionInstalled);
                    }
                    else
                    {
                        sb.Append(@"0.0");
                    }

                    // Installed By
                    sb.Append(@";;");

                    // It's a modded DLC
                    string installTime = InstallTime == null ? @"" : $@" on {InstallTime.ToString()}";
                    if (string.IsNullOrWhiteSpace(InstalledBy))
                    {
                        sb.Append($@"Not installed by managed installer{installTime}"); // Invalid metacmm or not present
                    }
                    else if (int.TryParse(InstalledBy, out var _))
                    {
                        sb.Append($@"Installed by Mod Manager Build {InstalledBy}{installTime}"); // Legacy (and M3) - only list build number
                    }
                    else
                    {
                        sb.Append($@"Installed by {InstalledBy}{installTime}"); // The metacmm lists the string
                    }

                    // Nexus Update Code
                    sb.Append(@";;");
                    sb.Append(NexusUpdateCode);
                    printToDiagFunc(sb.ToString(), severity);

                    // SELECTED OPTIONS
                    if (InstalledOptions != null && InstalledOptions.Any())
                    {
                        severity = LogSeverity.INFO;
                        foreach (var o in InstalledOptions)
                        {
                            printToDiagFunc($@"   > {o}", severity);
                        }
                    }
                }
            }

            /// <summary>
            /// Prints this struct to the diagnostic as a table row.
            /// </summary>
            /// <param name="printToDiagFunc"></param>
            public string GetAsDiagTableRow()
            {
                StringBuilder sb = new StringBuilder();

                List<string> modifiers = new List<string>();

                // Adds a data-<attr> attribute so it can be fetched with javascript
                void addDataAttr(string attrName, object attrValue) {
                    modifiers.Add($@"data-{attrName}=""{attrValue}""");
                }

                void addCell(string value, bool advancedOnly = false)
                {
                    if (advancedOnly)
                    {
                        sb.Append($@"<td class=""advanced-only"">{value}</td>");
                    }
                    else
                    {
                        sb.Append($@"<td>{value}</td>");
                    }
                }

                // Only table cells is added, row is set after
                string rowClass = "row-mod";
                if (IsOfficialDLC)
                {
                    sb.Append(DLCFolderName);
                    rowClass = "row-officialdlc";
                }
                else
                {
                    addCell(DLCFolderName);
                    addCell(ModName); // Useful if not found in TPMI
                    addCell(VersionInstalled != null ? VersionInstalled : @"");
                    addCell(MountPriority.ToString(), true);

                    // Install source
                    string installedBy = null;
                    // It's a modded DLC
                    if (string.IsNullOrWhiteSpace(InstalledBy))
                    {
                        installedBy = @"Unknown"; // Invalid metacmm or not present
                    }
                    else if (int.TryParse(InstalledBy, out var _))
                    {
                        installedBy = $@"Mod Manager Build {InstalledBy}"; // Legacy (and M3) - only list build number
                    }
                    else
                    {
                        installedBy = InstalledBy; // The metacmm lists the string
                    }

                    addCell(installedBy);

                    // Install date
                    addCell(InstallTime?.ToString());

                    // Add some extra useful info
                    addDataAttr(@"nexus-update-code", NexusUpdateCode);

                    // SELECTED OPTIONS
                    var options = InstalledOptions != null ? string.Join(@"", InstalledOptions.Select(x=> $@"<p class=""install-option"">{x}</p>")) : @"";
                    addCell(options);

                    // Extra info - can be populated by server
                    addCell(string.Join("\n", errors));
                }

                if (errors.Any())
                {
                    // Mark as error
                    rowClass = @"row-error";
                }

                var result = $"<tr class=\"{rowClass}\" {string.Join(@" ", modifiers)}>{sb.ToString()}</tr>"; // do not localize
                return result;
            }

            internal void AddError(string message)
            {
                errors.Add(message);
            }
        }
    }

    public class LogCollector
    {
        /// <summary>
        /// Closing tag for a collapsable subsection
        /// </summary>
        private const string END_SUB = @"[/SUB]";
        public static List<LogItem> GetLogsList(string activeLogPath = null)
        {
            var logs = Directory.GetFiles(MCoreFilesystem.GetLogDir(), @"*.txt");
            return logs.Select(x => new LogItem(x)
            {
                IsActiveLog = x == activeLogPath
            }).ToList();
        }

        public static string CollectLogs(string logfile)
        {
            MLog.Information(@"Shutting down logger to allow application to pull log file.");
            Log.CloseAndFlush();
            try
            {
                string log = File.ReadAllText(logfile);
                CreateLogger();
                return log;
            }
            catch (Exception e)
            {
                CreateLogger();
                MLog.Error(@"Could not read log file! " + e.Message);
                return null;
            }
        }
        /// <summary>
        /// ILogger creation delegate that is invoked when reopening the logger after collecting logs.
        /// </summary>
        internal static Func<ILogger> CreateLogger { get; set; }

        // Following is an example CreateLogger call that can be used in consuming applications.
        //        internal static void CreateLogger()
        //        {
        //            Log.Logger = new LoggerConfiguration().WriteTo.SizeRollingFile(Path.Combine(App.LogDir, @"modmanagerlog.txt"),
        //                                    retainedFileDurationLimit: TimeSpan.FromDays(14),
        //                                    fileSizeLimitBytes: 1024 * 1024 * 10) // 10MB  
        //#if DEBUG
        //                .WriteTo.Debug()
        //#endif
        //                .CreateLogger();
        //        }


        /// <summary>
        /// Collects the latest log file and reopens the logger when complete (if specified)
        /// </summary>
        /// <param name="logdir"></param>
        /// <param name="restartLogger"></param>
        /// <returns></returns>
        public static string CollectLatestLog(string logdir, bool restartLogger)
        {
            MLog.Information(@"Closing application log to allow application to read log file");
            Log.CloseAndFlush();
            var logFile = new DirectoryInfo(logdir)
                                             .GetFiles(@"*.txt")
                                             .OrderByDescending(f => f.LastWriteTime)
                                             .FirstOrDefault();
            string logText = null;
            if (logFile != null && File.Exists(logFile.FullName))
            {
                try
                {
                    logText = File.ReadAllText(logFile.FullName);
                }
                catch (Exception e)
                {
                    MLog.Fatal($@"UNABLE TO READ LOG FILE {logFile.FullName}: {e.Message}");
                }
            }

            if (restartLogger)
            {
                CreateLogger?.Invoke();
            }
            return logText;
        }




        //private static void runMassEffectModderNoGuiIPC(string operationName, string exe, string args, object lockObject, Action<string, string> exceptionOccuredCallback, Action<int?> setExitCodeCallback = null, Action<string, string> ipcCallback = null)
        //{
        //    MLog.Information($@"Running Mass Effect Modder No GUI w/ IPC: {exe} {args}");
        //    var memProcess = new ConsoleApp(exe, args);
        //    bool hasExceptionOccured = false;
        //    memProcess.ConsoleOutput += (o, args2) =>
        //    {
        //        string str = args2.Line;
        //        if (hasExceptionOccured)
        //        {
        //            Log.Fatal(@"MassEffectModderNoGui.exe: " + str);
        //        }
        //        if (str.StartsWith(@"[IPC]", StringComparison.Ordinal))
        //        {
        //            string command = str.Substring(5);
        //            int endOfCommand = command.IndexOf(' ');
        //            if (endOfCommand >= 0)
        //            {
        //                command = command.Substring(0, endOfCommand);
        //            }

        //            string param = str.Substring(endOfCommand + 5).Trim();
        //            if (command == @"EXCEPTION_OCCURRED")
        //            {
        //                hasExceptionOccured = true;
        //                exceptionOccuredCallback?.Invoke(operationName, param);
        //                return; //don't process this command further, nothing handles it.
        //            }

        //            ipcCallback?.Invoke(command, param);
        //        }
        //        //Debug.WriteLine(args2.Line);
        //    };
        //    memProcess.Exited += (a, b) =>
        //    {
        //        setExitCodeCallback?.Invoke(memProcess.ExitCode);
        //        lock (lockObject)
        //        {
        //            Monitor.Pulse(lockObject);
        //        }
        //    };
        //    memProcess.Run();
        //}

        public static string PerformDiagnostic(LogUploadPackage package)
        {
            MLog.Information($@"Collecting diagnostics for target {package.DiagnosticTarget.TargetPath}");
            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_preparingToCollectDiagnosticInfo));
            package.UpdateTaskbarProgressStateCallback?.Invoke(MTaskbarState.Indeterminate);

            #region MEM No Gui Fetch
            object memEnsuredSignaler = new object();
            // It is here we say a little prayer
            // to keep the bugs away from this monsterous code
            //    /_/\/\
            //    \_\  /
            //    /_/  \
            //    \_\/\ \
            //      \_\/

            bool hasMEM = false;
            #region MEM Fetch Callbacks
            //void readyToLaunch(string exe)
            //{
            //    Thread.Sleep(100); //try to stop deadlock
            //    hasMEM = true;
            //    mempath = exe;
            //    lock (memEnsuredSignaler)
            //    {
            //        Monitor.Pulse(memEnsuredSignaler);
            //    }
            //};

            void failedToExtractMEM(Exception e)
            {
                Thread.Sleep(100); //try to stop deadlock
                hasMEM = false;
            }
            void currentTaskCallback(string s) => package.UpdateStatusCallback?.Invoke(s);
            void setPercentDone(long downloaded, long total) => package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_preparingMEMNoGUIX, MUtilities.GetPercent(downloaded, total)));

            #endregion
            // Ensure MEM NOGUI
            if (package.DiagnosticTarget != null)
            {
                hasMEM = MEMNoGuiUpdater.UpdateMEM(package.DiagnosticTarget.Game.IsOTGame(), false, setPercentDone, failedToExtractMEM, currentTaskCallback, false);
            }

            //wait for tool fetch
            //if (!hasMEM)
            //{
            //    lock (memEnsuredSignaler)
            //    {
            //        Monitor.Wait(memEnsuredSignaler, new TimeSpan(0, 0, 25));
            //    }
            //}
            #endregion
            MLog.Information(@"Completed MEM fetch task");

            #region Diagnostic setup and diag header
            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingGameInformation));
            var diagStringBuilder = new StringBuilder();

            void addDiagLines(IEnumerable<string> strings, ME3TweaksLogViewer.LogSeverity sev = ME3TweaksLogViewer.LogSeverity.INFO)
            {
                foreach (var s in strings)
                {
                    addDiagLine(s, sev);
                }
            }

            void addDiagLine(string message = "", ME3TweaksLogViewer.LogSeverity sev = ME3TweaksLogViewer.LogSeverity.INFO)
            {
                if (diagStringBuilder == null)
                {
                    diagStringBuilder = new StringBuilder();
                }

                switch (sev)
                {
                    case ME3TweaksLogViewer.LogSeverity.INFO:
                        diagStringBuilder.Append(message);
                        break;
                    case ME3TweaksLogViewer.LogSeverity.WARN:
                        diagStringBuilder.Append($@"[WARN]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.ERROR:
                        diagStringBuilder.Append($@"[ERROR]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.FATAL:
                        diagStringBuilder.Append($@"[FATAL]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.DIAGSECTION:
                        diagStringBuilder.Append($@"[DIAGSECTION]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.GOOD:
                        diagStringBuilder.Append($@"[GREEN]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.BOLD:
                        diagStringBuilder.Append($@"[BOLD]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.BOLDBLUE:
                        diagStringBuilder.Append($@"[BOLDBLUE]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.DLC:
                        diagStringBuilder.Append($@"[DLC]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.OFFICIALDLC:
                        diagStringBuilder.Append($@"[OFFICIALDLC]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.GAMEID:
                        diagStringBuilder.Append($@"[GAMEID]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.TPMI:
                        diagStringBuilder.Append($@"[TPMI]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.SUB:
                        diagStringBuilder.Append($@"[SUB]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.SUPERCEDANCE_FILE:
                        diagStringBuilder.Append($@"[SSF]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.SAVE_FILE_HASH_NAME:
                        diagStringBuilder.Append($@"[SF]{message}");
                        break;
                    case ME3TweaksLogViewer.LogSeverity.NOPRE:
                        // Server will not use <pre>
                        // Do it for each line so we don't have to do blocks on server
                        foreach (var line in message.SplitLinesAll(options: StringSplitOptions.RemoveEmptyEntries))
                        {
                            diagStringBuilder.Append($@"[NOPRE]{message}");
                        }
                        break;
                    default:
                        Debugger.Break();
                        break;
                }
                diagStringBuilder.Append("\n"); //do not localize
            }


            string gamePath = package.DiagnosticTarget.TargetPath;
            var gameID = package.DiagnosticTarget.Game.ToMEMGameNum().ToString();
            MLog.Information(@"Beginning to build diagnostic output");

            addDiagLine(package.DiagnosticTarget.Game.ToGameNum().ToString(), ME3TweaksLogViewer.LogSeverity.GAMEID);
            if (package.SelectedSaveFilePath != null && File.Exists(package.SelectedSaveFilePath))
            {
                // This will allow server to locate the save file that is uploaded and tell user what it is 
                addDiagLine(MUtilities.CalculateHash(package.SelectedSaveFilePath) + @"|" + Path.GetFileName(package.SelectedSaveFilePath), ME3TweaksLogViewer.LogSeverity.SAVE_FILE_HASH_NAME);
            }
            addDiagLine($@"{MLibraryConsumer.GetHostingProcessname()} {MLibraryConsumer.GetAppVersion()} Game Diagnostic");
            addDiagLine($@"Build date: {MLibraryConsumer.GetSigningDate()}");
            addDiagLine($@"ME3TweaksCore version: {MLibraryConsumer.GetLibraryVersion()}");
            addDiagLine($@"Diagnostic for {package.DiagnosticTarget.Game.ToGameName()}");
            addDiagLine($@"Diagnostic generated at {DateTime.Now.ToShortDateString()} {DateTime.Now.ToShortTimeString()}");
            #endregion

            #region MEM Setup
            //vars
            string args = null;
            int exitcode = -1;

            //paths
            string oldMemGamePath = null;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"MassEffectModder");
            string _iniPath = Path.Combine(path, package.DiagnosticTarget.Game.IsLEGame() ? @"MassEffectModderLE.ini" : @"MassEffectModder.ini");
            if (hasMEM)
            {
                // Set INI path to target

                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                if (!File.Exists(_iniPath))
                {
                    File.Create(_iniPath).Close();
                }

                // TODO: DUPLICATING INI NEEDS LATEST VERSION.
                DuplicatingIni ini = DuplicatingIni.LoadIni(_iniPath);

                if (package.DiagnosticTarget.Game.IsLEGame())
                {
                    oldMemGamePath = ini[@"GameDataPath"][@"MELE"].Value;
                    var rootPath = Directory.GetParent(gamePath);
                    if (rootPath != null)
                        rootPath = Directory.GetParent(rootPath.FullName);
                    if (rootPath != null)
                    {
                        ini[@"GameDataPath"][@"MELE"].Value = rootPath.FullName;
                    }
                    else
                    {
                        MLog.Error($@"Invalid game directory: {gamePath} is not part of an overall LE install");
                        addDiagLine($@"MEM diagnostics skipped: Game directory is not part of an overall LE install", ME3TweaksLogViewer.LogSeverity.ERROR);
                        hasMEM = false;
                    }
                }
                else
                {
                    oldMemGamePath = ini[@"GameDataPath"][package.DiagnosticTarget.Game.ToString()]?.Value;
                    ini[@"GameDataPath"][package.DiagnosticTarget.Game.ToString()].Value = gamePath;
                }

                if (hasMEM)
                {
                    File.WriteAllText(_iniPath, ini.ToString());
                    var versInfo = FileVersionInfo.GetVersionInfo(MCoreFilesystem.GetMEMNoGuiPath(package.DiagnosticTarget.Game.IsOTGame()));
                    int fileVersion = versInfo.FileMajorPart;
                    addDiagLine($@"Diagnostic MassEffectModderNoGui version: {fileVersion}");
                }
            }
            else
            {
                addDiagLine(@"Mass Effect Modder No Gui was not available for use when this diagnostic was generated.", ME3TweaksLogViewer.LogSeverity.ERROR);
            }
            #endregion

            try
            {
                #region Game Information
                MLog.Information(@"Collecting basic game information");

                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingGameInformation));
                addDiagLine(@"Basic game information", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                addDiagLine($@"Game is installed at {gamePath}");

                MLog.Information(@"Reloading target for most up to date information");
                package.DiagnosticTarget.ReloadGameTarget(false); //reload vars
                TextureModInstallationInfo avi = package.DiagnosticTarget.GetInstalledALOTInfo();

                string exePath = M3Directories.GetExecutablePath(package.DiagnosticTarget);
                if (File.Exists(exePath))
                {
                    MLog.Information(@"Getting game version");
                    var versInfo = FileVersionInfo.GetVersionInfo(exePath);
                    addDiagLine($@"Version: {versInfo.FileMajorPart}.{versInfo.FileMinorPart}.{versInfo.FileBuildPart}.{versInfo.FilePrivatePart}");

                    //Disk type
                    string pathroot = Path.GetPathRoot(gamePath);
                    pathroot = pathroot.Substring(0, 1);
                    if (pathroot == @"\")
                    {
                        addDiagLine(@"Installation appears to be on a network drive (first character in path is \)", ME3TweaksLogViewer.LogSeverity.WARN);
                    }
                    else
                    {
                        if (MUtilities.IsWindows10OrNewer())
                        {
                            int backingType = GetPartitionDiskBackingType(pathroot);
                            string type = @"Unknown type";
                            switch (backingType)
                            {
                                case 3:
                                    type = @"Hard disk drive";
                                    break;
                                case 4:
                                    type = @"Solid state drive";
                                    break;
                                default:
                                    type += @": " + backingType;
                                    break;
                            }

                            addDiagLine(@"Installed on disk type: " + type);
                        }
                    }

                    if (package.DiagnosticTarget.Supported)
                    {
                        addDiagLine($@"Game source: {package.DiagnosticTarget.GameSource}", ME3TweaksLogViewer.LogSeverity.GOOD);
                    }
                    else
                    {
                        addDiagLine($@"Game source: Unknown/Unsupported - {package.DiagnosticTarget.ExecutableHash}", ME3TweaksLogViewer.LogSeverity.FATAL);
                    }

                    if (package.DiagnosticTarget.Game == MEGame.ME1)
                    {
                        MLog.Information(@"Getting additional ME1 executable information");
                        var exeInfo = ME1ExecutableInfo.GetExecutableInfo(M3Directories.GetExecutablePath(package.DiagnosticTarget), false);
                        if (avi != null)
                        {
                            addDiagLine($@"Large Address Aware: {exeInfo.HasLAAApplied}", exeInfo.HasLAAApplied ? ME3TweaksLogViewer.LogSeverity.GOOD : ME3TweaksLogViewer.LogSeverity.FATAL);
                            addDiagLine($@"No-admin patched: {exeInfo.HasLAAApplied}", exeInfo.HasProductNameChanged ? ME3TweaksLogViewer.LogSeverity.GOOD : ME3TweaksLogViewer.LogSeverity.WARN);
                            addDiagLine($@"enableLocalPhysXCore patched: {exeInfo.HasPhysXCoreChanged}", exeInfo.HasLAAApplied ? ME3TweaksLogViewer.LogSeverity.GOOD : ME3TweaksLogViewer.LogSeverity.WARN);
                        }
                        else
                        {
                            addDiagLine($@"Large Address Aware: {exeInfo.HasLAAApplied}");
                            addDiagLine($@"No-admin patched: {exeInfo.HasLAAApplied}");
                            addDiagLine($@"enableLocalPhysXCore patched: {exeInfo.HasLAAApplied}");
                        }
                    }

                    //Executable signatures
                    MLog.Information(@"Checking executable signature");

                    var info = new FileInspector(exePath);
                    var certOK = info.Validate();
                    if (certOK == SignatureCheckResult.NoSignature)
                    {
                        addDiagLine(@"This executable is not signed", ME3TweaksLogViewer.LogSeverity.ERROR);
                    }
                    else
                    {
                        if (certOK == SignatureCheckResult.BadDigest)
                        {
                            if (package.DiagnosticTarget.Game == MEGame.ME1 && versInfo.ProductName == @"Mass_Effect")
                            {
                                //Check if this Mass_Effect
                                addDiagLine(@"Signature check for this executable was skipped as MEM modified this exe");
                            }
                            else
                            {
                                addDiagLine(@"The signature for this executable is not valid. The executable has been modified", ME3TweaksLogViewer.LogSeverity.ERROR);
                                diagPrintSignatures(info, addDiagLine);
                            }
                        }
                        else
                        {
                            addDiagLine(@"Signature check for this executable: " + certOK.ToString());
                            diagPrintSignatures(info, addDiagLine);
                        }
                    }

                    //BINK
                    MLog.Information(@"Checking if Bink ASI loader is installed");

                    if (package.DiagnosticTarget.IsBinkBypassInstalled())
                    {
                        if (package.DiagnosticTarget.Game.IsOTGame())
                        {
                            addDiagLine(@"binkw32 ASI bypass is installed");
                        }
                        else
                        {
                            addDiagLine(@"bink2w64 ASI loader is installed");
                        }
                    }
                    else
                    {
                        if (package.DiagnosticTarget.Game.IsOTGame())
                        {
                            addDiagLine(@"binkw32 ASI bypass is not installed. ASI mods, DLC mods, and modified DLC will not load", ME3TweaksLogViewer.LogSeverity.WARN);
                        }
                        else
                        {
                            addDiagLine(@"bink2w64 ASI loader is not installed. ASI mods will not load", ME3TweaksLogViewer.LogSeverity.WARN);
                        }
                    }

                    bool enhancedBinkInstalled = false;
                    if (package.DiagnosticTarget.Game.IsLEGame() || package.DiagnosticTarget.Game == MEGame.LELauncher)
                    {
                        // ME3Tweakscore 8.1.0 for LE: Enhanced bink2 encoder
                        enhancedBinkInstalled = package.DiagnosticTarget.IsEnhancedBinkInstalled();
                        if (enhancedBinkInstalled)
                        {
                            addDiagLine(@"Enhanced bink dll is installed", ME3TweaksLogViewer.LogSeverity.GOOD);
                        }
                        else
                        {
                            addDiagLine(@"Standard bink dll is installed");
                        }
                    }


                    if (package.DiagnosticTarget.Game == MEGame.ME1)
                    {
                        // Check for patched PhysX
                        if (ME1PhysXTools.IsPhysXLoaderPatchedLocalOnly(package.DiagnosticTarget))
                        {
                            addDiagLine(@"PhysXLoader.dll is patched to force local PhysXCore.dll", ME3TweaksLogViewer.LogSeverity.GOOD);
                        }
                        else if (certOK == SignatureCheckResult.BadDigest)
                        {
                            addDiagLine(@"PhysXLoader.dll is not patched to force local PhysXCore.dll. Game may not boot", ME3TweaksLogViewer.LogSeverity.WARN);
                        }
                        else if (certOK == SignatureCheckResult.Valid)
                        {
                            addDiagLine(@"PhysXLoader.dll is not patched, but executable is still signed", ME3TweaksLogViewer.LogSeverity.GOOD);
                        }
                        else
                        {
                            addDiagLine(@"PhysXLoader.dll status could not be checked", ME3TweaksLogViewer.LogSeverity.WARN);
                        }
                    }

                    package.DiagnosticTarget.PopulateExtras();
                    if (package.DiagnosticTarget.ExtraFiles.Any())
                    {
                        addDiagLine(@"Additional dll files found in game executable directory:", ME3TweaksLogViewer.LogSeverity.WARN);
                        foreach (var extra in package.DiagnosticTarget.ExtraFiles)
                        {
                            addDiagLine(@" > " + extra.DisplayName);
                        }
                    }
                }

                #endregion

                #region System Information
                MLog.Information(@"Collecting system information");
                var computerInfo = new ComputerInfo();


                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingSystemInformation));

                addDiagLine(@"System information", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                OperatingSystem os = Environment.OSVersion;
                Version osBuildVersion = os.Version;

                //Windows 10 only
                string verLine = computerInfo.OSFullName;

                if (os.Version < ME3TweaksCoreLib.MIN_SUPPORTED_OS)
                {
                    addDiagLine(@"This operating system is not supported", ME3TweaksLogViewer.LogSeverity.FATAL);
                    addDiagLine(@"Upgrade to a supported operating system if you want support", ME3TweaksLogViewer.LogSeverity.FATAL);
                }
                else if (!computerInfo.ActuallyPlatform)
                {
                    // Is this actually Windows? Probably not
                    // Do not waste time supporting these, just give user a message saying it's not supported
                    addDiagLine(@"This software environment is not supported", ME3TweaksLogViewer.LogSeverity.FATAL);
                    addDiagLine(@"Only actual Windows platforms are supported; do not report issues with this installation, they will not be fixed", ME3TweaksLogViewer.LogSeverity.FATAL);
                }

                addDiagLine(verLine, os.Version < ME3TweaksCoreLib.MIN_SUPPORTED_OS ? ME3TweaksLogViewer.LogSeverity.ERROR : ME3TweaksLogViewer.LogSeverity.INFO);
                addDiagLine(@"Version " + osBuildVersion, os.Version < ME3TweaksCoreLib.MIN_SUPPORTED_OS ? ME3TweaksLogViewer.LogSeverity.ERROR : ME3TweaksLogViewer.LogSeverity.INFO);
                addDiagLine($@"System culture: {CultureInfo.InstalledUICulture.Name}");

                addDiagLine();
                MLog.Information(@"Collecting memory information");

                addDiagLine(@"System Memory", ME3TweaksLogViewer.LogSeverity.BOLD);
                long ramInBytes = (long)computerInfo.TotalPhysicalMemory;
                addDiagLine($@"Total memory available: {FileSize.FormatSize(ramInBytes)}");
                var memSpeed = computerInfo.MemorySpeed;
                if (memSpeed > 0)
                {
                    addDiagLine($@"Memory speed: {memSpeed}Mhz");
                }
                else
                {
                    addDiagLine($@"Could not get memory speed", ME3TweaksLogViewer.LogSeverity.WARN);
                }


                addDiagLine(@"Processors", ME3TweaksLogViewer.LogSeverity.BOLD);
                MLog.Information(@"Collecting processor information");

                addDiagLine(GetProcessorInformationForDiag());
                if (ramInBytes == 0)
                {
                    addDiagLine(@"Unable to get the read amount of physically installed ram. This may be a sign of impending hardware failure in the SMBIOS", ME3TweaksLogViewer.LogSeverity.WARN);
                }

                MLog.Information(@"Collecting video card information");

                /* OLD CODE HERE
                                ManagementObjectSearcher objvide = new ManagementObjectSearcher(@"select * from Win32_VideoController");
                int vidCardIndex = 1;
                foreach (ManagementObject obj in objvide.Get())
                {
                    addDiagLine();
                    addDiagLine(@"Video Card " + vidCardIndex, LogSeverity.BOLD);
                    addDiagLine(@"Name: " + obj[@"Name"]);

                    //Get Memory
                    string vidKey = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\";
                    vidKey += (vidCardIndex - 1).ToString().PadLeft(4, '0');
                    object returnvalue = null;
                    try
                    {
                        returnvalue = Registry.GetValue(vidKey, @"HardwareInformation.qwMemorySize", 0L);
                    }
                    catch (Exception ex)
                    {
                        addDiagLine($@"Unable to read memory size from registry. Reading from WMI instead ({ex.GetType()})", LogSeverity.WARN);
                    }

                    string displayVal;
                    if (returnvalue is long size && size != 0)
                    {
                        displayVal = FileSize.FormatSize(size);
                    }
                    else
                    {
                        try
                        {
                            UInt32 wmiValue = (UInt32)obj[@"AdapterRam"];
                            var numBytes = (long)wmiValue;

                            // TODO: UPDATE THIS FOR FILESIZE. NEEDS TESTING
                            displayVal = FileSize.FormatSize(numBytes);
                            if (numBytes == uint.MaxValue)
                            {
                                displayVal += @" (possibly more, variable is 32-bit unsigned)";
                            }
                        }
                        catch (Exception)
                        {
                            displayVal = @"Unable to read value from registry/WMI";
                        }
                    }

                    addDiagLine(@"Memory: " + displayVal);
                    addDiagLine(@"DriverVersion: " + obj[@"DriverVersion"]);
                    vidCardIndex++;
                }*/

                // Enumerate the Display Adapters registry key
                int vidCardIndex = 1;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (key != null)
                    {
                        var subNames = key.GetSubKeyNames();
                        foreach (string adapterRegistryIndex in subNames)
                        {
                            if (!int.TryParse(adapterRegistryIndex, out _)) continue; // Only go into numerical ones
                            try
                            {
                                var videoKey = key.OpenSubKey(adapterRegistryIndex);

                                // Get memory. If memory is not populated then this is not active right now (I think)
                                long vidCardSizeInBytes = (long)videoKey.GetValue(@"HardwareInformation.qwMemorySize", -1L);
                                ulong vidCardSizeInBytesIntegrated = 1uL;
                                if (vidCardSizeInBytes == -1)
                                {
                                    var memSize = videoKey.GetValue(@"HardwareInformation.MemorySize", 1uL); // We use 1 because 2-4GB range is realistic. But a 1 byte video card?
                                    if (memSize is byte[] whyWouldYouPutThisInBytes)
                                    {
                                        vidCardSizeInBytesIntegrated = BitConverter.ToUInt32(whyWouldYouPutThisInBytes);
                                    }
                                    else if (memSize is long l)
                                    {
                                        vidCardSizeInBytesIntegrated = (ulong)l;
                                    }
                                }
                                if (vidCardSizeInBytes == -1 && vidCardSizeInBytesIntegrated == 1) continue; // Not defined

                                string vidCardName = @"Unknown name";

                                // Try 1: Use DriverDesc
                                var vidCardNameReg = videoKey.GetValue(@"DriverDesc");
                                if (vidCardNameReg is string str)
                                {
                                    vidCardName = str;
                                }
                                else
                                {
                                    vidCardNameReg = null; // ensure null for flow control below
                                }

                                if (vidCardNameReg == null)
                                {
                                    // Try 2: Read AdapterString
                                    vidCardNameReg = videoKey.GetValue(@"HardwareInformation.AdapterString",
                                        @"Unable to get adapter name");
                                    if (vidCardNameReg is byte[] bytes)
                                    {
                                        // AMD Radeon 6700XT on eGPU writes REG_BINARY for some reason
                                        vidCardName =
                                            Encoding.Unicode.GetString(bytes)
                                                .Trim(); // During upload step we have to strip \0 or it'll break the log viewer due to how lzma-js works
                                    }
                                    else if (vidCardNameReg is string str2)
                                    {
                                        vidCardName = str2;
                                    }
                                }

                                string vidDriverVersion = (string)videoKey.GetValue(@"DriverVersion", @"Unable to get driver version");
                                string vidDriverDate = (string)videoKey.GetValue(@"DriverDate", @"Unable to get driver date");

                                addDiagLine();
                                addDiagLine($@"Video Card {(vidCardIndex++)}", ME3TweaksLogViewer.LogSeverity.BOLD);
                                addDiagLine($@"Name: {vidCardName}");
                                if (vidCardSizeInBytesIntegrated == 1 && vidCardSizeInBytes == -1)
                                {
                                    addDiagLine($@"Memory: (System Shared)");
                                }
                                else if (vidCardSizeInBytes > 0)
                                {
                                    addDiagLine($@"Memory: {FileSize.FormatSize(vidCardSizeInBytes)}");
                                }
                                else
                                {
                                    addDiagLine($@"Memory: {FileSize.FormatSize(vidCardSizeInBytesIntegrated)}");
                                }
                                addDiagLine($@"Driver Version: {vidDriverVersion}");
                                addDiagLine($@"Driver Date: {vidDriverDate}");
                            }
                            catch (Exception ex)
                            {
                                addDiagLine($@"Error getting video card information: {ex.Message}", ME3TweaksLogViewer.LogSeverity.WARN);
                            }
                        }
                    }
                }

                // Antivirus
                var avs = MUtilities.GetListOfInstalledAV();
                addDiagLine(@"Antivirus products", ME3TweaksLogViewer.LogSeverity.BOLD);
                addDiagLine(@"The following antivirus products were detected:");
                foreach (var av in avs)
                {
                    addDiagLine($@"- {av}");
                }
                #endregion

                #region Texture mod information

                MLog.Information(@"Getting texture mod installation info");
                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_gettingTextureInfo));
                addDiagLine(@"Current texture mod information", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);

                var textureHistory = package.DiagnosticTarget.GetTextureModInstallationHistory();
                if (!textureHistory.Any())
                {
                    addDiagLine(
                        @"The texture mod installation marker was not detected. No texture mods appear to be installed");
                }
                else
                {
                    var latestInstall = textureHistory[0];
                    if (latestInstall.ALOTVER > 0 || latestInstall.MEUITMVER > 0)
                    {
                        addDiagLine($@"ALOT version: {latestInstall.ALOTVER}.{latestInstall.ALOTUPDATEVER}.{latestInstall.ALOTHOTFIXVER}");
                        if (latestInstall.MEUITMVER != 0)
                        {
                            var meuitmName = package.DiagnosticTarget.Game == MEGame.ME1 ? @"MEUITM" : $@"MEUITM{package.DiagnosticTarget.Game.ToGameNum()}";
                            addDiagLine($@"{meuitmName} version: {latestInstall.MEUITMVER}");
                        }
                    }
                    else if (package.DiagnosticTarget.Game.IsOTGame())
                    {
                        addDiagLine(@"This installation has been texture modded, but ALOT and/or MEUITM has not been installed");
                    }
                    else if (package.DiagnosticTarget.Game.IsLEGame())
                    {
                        addDiagLine(@"This installation has been texture modded with MassEffectModder");
                    }

                    if (latestInstall.MarkerExtendedVersion >= TextureModInstallationInfo.FIRST_EXTENDED_MARKER_VERSION && !string.IsNullOrWhiteSpace(latestInstall.InstallerVersionFullName))
                    {
                        addDiagLine($@"Latest installation was from performed by {latestInstall.InstallerVersionFullName}");
                    }
                    else if (latestInstall.ALOT_INSTALLER_VERSION_USED > 0)
                    {
                        addDiagLine($@"Latest installation was from installer v{latestInstall.ALOT_INSTALLER_VERSION_USED}");
                    }

                    addDiagLine($@"Latest installation used MEM v{latestInstall.MEM_VERSION_USED}");
                    addDiagLine(@"Texture mod installation history", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                    addDiagLine(@"The history of texture mods installed into this game is as follows (from latest install to first install):");

                    addDiagLine(@"Click to view list", ME3TweaksLogViewer.LogSeverity.SUB);
                    bool isFirst = true;
                    foreach (var tmii in textureHistory)
                    {
                        if (isFirst)
                            isFirst = false;
                        else
                            addDiagLine();

                        if (tmii.MarkerExtendedVersion >= TextureModInstallationInfo.FIRST_EXTENDED_MARKER_VERSION)
                        {
                            addDiagLine($@"Texture install on {tmii.InstallationTimestamp:yyyy MMMM dd h:mm:ss tt zz}", ME3TweaksLogViewer.LogSeverity.BOLDBLUE);
                        }
                        else
                        {
                            addDiagLine(@"Texture install", ME3TweaksLogViewer.LogSeverity.BOLDBLUE);
                        }

                        addDiagLine($@"Marker version {tmii.MarkerExtendedVersion}");
                        addDiagLine(tmii.ToString());
                        if (tmii.MarkerExtendedVersion >= 3 && !string.IsNullOrWhiteSpace(tmii.InstallerVersionFullName))
                        {
                            addDiagLine($@"Installation was from performed by {tmii.InstallerVersionFullName}");
                        }
                        else if (tmii.ALOT_INSTALLER_VERSION_USED > 0)
                        {
                            addDiagLine($@"Installation was performed by installer v{tmii.ALOT_INSTALLER_VERSION_USED}");
                        }

                        addDiagLine($@"Installed used MEM v{tmii.MEM_VERSION_USED}");

                        if (tmii.InstalledTextureMods.Any())
                        {
                            addDiagLine(@"Files installed in session:");
                            foreach (var fi in tmii.InstalledTextureMods)
                            {
                                var modStr = @" - ";
                                if (fi.ModType == InstalledTextureMod.InstalledTextureModType.USERFILE)
                                {
                                    modStr += @"[USERFILE] ";
                                }

                                modStr += fi.ModName;
                                if (!string.IsNullOrWhiteSpace(fi.AuthorName))
                                {
                                    modStr += $@" by {fi.AuthorName}";
                                }

                                addDiagLine(modStr, fi.ModType == InstalledTextureMod.InstalledTextureModType.USERFILE ? ME3TweaksLogViewer.LogSeverity.WARN : ME3TweaksLogViewer.LogSeverity.GOOD);
                                if (fi.ChosenOptions.Any())
                                {
                                    addDiagLine(@"   Chosen options for install:");
                                    foreach (var c in fi.ChosenOptions)
                                    {
                                        addDiagLine($@"      {c}");
                                    }
                                }
                            }
                        }
                    }

                    addDiagLine(END_SUB);
                }

                #endregion

                #region Basegame file changes
                MLog.Information(@"Collecting basegame file changes information");

                addDiagLine(@"Basegame changes", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);

                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingBasegameFileModifications));
                List<string> modifiedFiles = new List<string>();

                void failedCallback(string file)
                {
                    modifiedFiles.Add(file);
                }

                var isVanilla = VanillaDatabaseService.ValidateTargetAgainstVanilla(package.DiagnosticTarget, failedCallback, false);
                if (isVanilla)
                {
                    addDiagLine(@"No modified basegame files were found.");
                }
                else
                {
                    if (!package.DiagnosticTarget.TextureModded || package.DiagnosticTarget.Game.IsLEGame())
                    {
                        var modifiedBGFiles = new List<string>();
                        bool hasAtLeastOneTextureModdedOnlyFile = false;
                        var cookedPath = package.DiagnosticTarget.GetCookedPath();
                        var markerPath = package.DiagnosticTarget.GetTextureMarkerPath();
                        foreach (var mf in modifiedFiles)
                        {
                            if (mf.StartsWith(cookedPath, StringComparison.InvariantCultureIgnoreCase))
                            {
                                var fileName = mf.Substring(cookedPath.Length + 1);
                                if (mf.Equals(markerPath, StringComparison.InvariantCultureIgnoreCase)) continue; //don't report this file
                                var info = BasegameFileIdentificationService.GetBasegameFileSource(package.DiagnosticTarget, mf);
                                var cell = $@"<td>{fileName}</td>";

                                if (info != null)
                                {
                                    var source = info.source;
                                    // Strip BGFIS Bio2DA block and parse it, then add the BGFIS block for Bio2DA merge
                                    var strippedSource = info.GetWithoutBlock(Bio2DAMerge.BIO2DA_BGFIS_DATA_BLOCK).Trim();
                                    var twoDAMerge = Bio2DAMerge.GetMergedFilenames(info);
                                    if (twoDAMerge.Count > 0)
                                    {
                                        strippedSource += '\n' + string.Join('\n', twoDAMerge);
                                    }
                                    cell += $@"<td>{strippedSource.Replace("\n", "<br>")}</td>";
                                }
                                else
                                {
                                    if (package.DiagnosticTarget.TextureModded)
                                    {
                                        // Do not print out texture modded only files
                                        hasAtLeastOneTextureModdedOnlyFile = true;
                                        cell = null;
                                    }
                                    else
                                    {
                                        cell += $@"<td>{fileName}</td>";
                                    }
                                }

                                if (cell != null)
                                {
                                    modifiedBGFiles.Add($@"<tr>{cell}</tr>");
                                }
                            }
                        }

                        if (hasAtLeastOneTextureModdedOnlyFile)
                        {
                            addDiagLine(@"Files' whose only modification was from Mass Effect Modder are not shown.");
                        }
                        if (modifiedBGFiles.Any())
                        {
                            addDiagLine(@"The following basegame files have been modified:");

                            // Make table.
                            var bgtable = $@"
                [HTML]
                <table class=""basegametable"">
                    <thead>
                        <th>Filename</th>
                        <th>Tracked modifications</th>
                    </thead>
                    <tbody>
                        {string.Join("\n", modifiedBGFiles)}
                    </tbody>
                </table>
                [/HTML]";
                            addDiagLine(string.Join("\n", bgtable.SplitLinesAll(options: StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())));
                        }
                        else
                        {
                            addDiagLine(@"No modified basegame files were found");
                        }

                    }
                    else
                    {
                        //Check MEMI markers?
                        addDiagLine(@"Basegame changes check skipped as this installation has been texture modded");
                    }
                }

                #endregion

                #region Blacklisted mods check
                MLog.Information(@"Checking for mods that are known to cause problems in the scene");

                void memExceptionOccured(string operation)
                {
                    addDiagLine($@"An exception occurred performing an operation: {operation}", ME3TweaksLogViewer.LogSeverity.ERROR);
                    addDiagLine(@"Check the Mod Manager application log for more information.", ME3TweaksLogViewer.LogSeverity.ERROR);
                    addDiagLine(@"Report this on the ME3Tweaks Discord for further assistance.", ME3TweaksLogViewer.LogSeverity.ERROR);
                }

                if (hasMEM)
                {
                    package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_checkingForBlacklistedModsMEM));
                    args = $@"--detect-bad-mods --gameid {gameID} --ipc";
                    var blacklistedMods = new List<string>();
                    MEMIPCHandler.RunMEMIPCUntilExit(package.DiagnosticTarget.Game.IsOTGame(), args, false, setMEMCrashLog: memExceptionOccured, ipcCallback: (string command, string param) =>
                    {
                        switch (command)
                        {
                            case @"ERROR":
                                blacklistedMods.Add(param);
                                break;
                            default:
                                Debug.WriteLine(@"oof?");
                                break;
                        }
                    }, applicationExited: x => exitcode = x);

                    if (exitcode != 0)
                    {
                        addDiagLine(
                            $@"MassEffectModderNoGuiexited exited incompatible mod detection check with code {exitcode}",
                            ME3TweaksLogViewer.LogSeverity.ERROR);
                    }

                    if (blacklistedMods.Any())
                    {
                        addDiagLine(@"The following blacklisted mods were found:", ME3TweaksLogViewer.LogSeverity.ERROR);
                        foreach (var str in blacklistedMods)
                        {
                            addDiagLine(@" - " + str);
                        }

                        addDiagLine(@"These mods have been blacklisted by modding tools because of known issues they cause. Do not use these mods", ME3TweaksLogViewer.LogSeverity.ERROR);
                    }
                    else
                    {
                        addDiagLine(@"No blacklisted mods were found installed");
                    }
                }
                else
                {
                    addDiagLine(@"MEM not available, skipped blacklisted mods check", ME3TweaksLogViewer.LogSeverity.WARN);

                }

                #endregion

                #region Installed DLCs
                MLog.Information(@"Collecting installed DLC");

                //Get DLCs
                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingDLCInformation));

                var installedDLCs = package.DiagnosticTarget.GetMetaMappedInstalledDLC();

                addDiagLine(@"Installed DLC", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                addDiagLine(@"The following DLC is installed:");

                var mountPriorities = new Dictionary<int, string>();

                var officialDLC = MEDirectories.OfficialDLC(package.DiagnosticTarget.Game);
                List<string> dlcRows = new List<string>();
                foreach (var dlc in installedDLCs)
                {
                    ME3TweaksLogViewer.InstalledDLCStruct dlcStruct = new ME3TweaksLogViewer.InstalledDLCStruct()
                    {
                        DLCFolderName = dlc.Key
                    };

                    string errorLine = null;
                    if (!officialDLC.Contains(dlc.Key, StringComparer.InvariantCultureIgnoreCase))
                    {
                        var metaMappedDLC = dlc.Value;
                        if (metaMappedDLC != null)
                        {
                            dlcStruct.ModName = metaMappedDLC.ModName;
                            dlcStruct.InstalledBy = metaMappedDLC.InstalledBy;
                            dlcStruct.VersionInstalled = metaMappedDLC.Version;
                            dlcStruct.InstalledOptions = metaMappedDLC.OptionsSelectedAtInstallTime;
                            dlcStruct.NexusUpdateCode = metaMappedDLC.NexusUpdateCode;
                            dlcStruct.InstallTime = metaMappedDLC.InstallTime;
                        }
                        else
                        {

                        }

                        var dlcPath = Path.Combine(package.DiagnosticTarget.GetDLCPath(), dlc.Key);
                        var mount = MELoadedDLC.GetMountPriority(dlcPath, package.DiagnosticTarget.Game);
                        dlcStruct.MountPriority = mount;
                        if (mount != 0)
                        {
                            if (mountPriorities.TryGetValue(mount, out var existingDLC))
                            {
                                dlcStruct.AddError($@"This DLC has the same mount priority ({mount}) as {existingDLC}. This will cause undefined game behavior! Please contact the developers of these mods.");
                            }
                            else
                            {
                                mountPriorities[mount] = dlc.Key;
                            }
                        }
                    }
                    else
                    {
                        dlcStruct.IsOfficialDLC = true;
                    }

                    dlcRows.Add(dlcStruct.GetAsDiagTableRow());
                }

                // Make table.
                var table = $@"
                [HTML]
                <table class=""dlctable"">
                    <thead>
                        <th>DLC Folder Name</th>
                        <th>Mod Name</th>
                        <th>Version</th>
                        <th class=""advanced-only"">Mount</th>
                        <th>Installed By</th>
                        <th>Install Time</th>
                        <th>Options</th>
                        <th>Extra info</th>
                    </thead>
                    <tbody>
                        {string.Join("\n", dlcRows)}
                    </tbody>
                </table>
                [/HTML]";

                // Remove leading whitespace
                addDiagLine(string.Join("\n", table.SplitLinesAll(options: StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())));

                if (installedDLCs.Any())
                {
                    SeeIfIncompatibleDLCIsInstalled(package.DiagnosticTarget, addDiagLine);
                }

                // 03/13/2022: Supercedance list now lists all DLC files even if they don't supercede anything.
                MLog.Information(@"Collecting supersedance list");
                var supercedanceList = M3Directories.GetFileSupercedances(package.DiagnosticTarget).ToList();
                if (supercedanceList.Any())
                {
                    addDiagLine();
                    addDiagLine(@"DLC mod files", ME3TweaksLogViewer.LogSeverity.BOLD);
                    addDiagLine(@"The following DLC mod files are installed, as well as their supercedances. This may mean the mods are incompatible, or that these files are compatibility patches. This information is for developer use only - DO NOT MODIFY YOUR GAME DIRECTORY MANUALLY.");

                    bool isFirst = true;
                    addDiagLine(@"Click to view list", ME3TweaksLogViewer.LogSeverity.SUB);

                    var supercedanceRows = new List<string>();
                    foreach (var sl in supercedanceList.OrderBy(x => x.Key))
                    {
                        supercedanceRows.Add($@"<tr><td>{sl.Key}</td><td>{string.Join("<br>", sl.Value)}</td></tr>");
                    }

                    var sltable = $@"
                [HTML]
                <table class=""supercedancetable"">
                    <thead>
                        <th>Filename</th>
                        <th>Supercedances</th>
                    </thead>
                    <tbody>
                        {string.Join("\n", supercedanceRows)}
                    </tbody>
                </table>
                [/HTML]"; 

                    // Remove leading whitespace
                    addDiagLine(string.Join("\n", sltable.SplitLinesAll(options: StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())));

                    addDiagLine(END_SUB);
                }
                #endregion

                #region Get list of TFCs

                if (package.DiagnosticTarget.Game > MEGame.ME1)
                {
                    MLog.Information(@"Getting list of TFCs");
                    package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingTFCFileInformation));

                    addDiagLine(@"Texture File Cache (TFC) files", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                    addDiagLine(@"The following TFC files are present in the game directory.");
                    var bgPath = M3Directories.GetBioGamePath(package.DiagnosticTarget);
                    string[] tfcFiles = Directory.GetFiles(bgPath, @"*.tfc", SearchOption.AllDirectories);
                    if (tfcFiles.Any())
                    {
                        List<string> tfcs = new List<string>();
                        foreach (string tfc in tfcFiles)
                        {
                            FileInfo fi = new FileInfo(tfc);
                            long tfcSize = fi.Length;
                            string tfcPath = tfc.Substring(bgPath.Length + 1);

                            var pathChunks = tfcPath.Split(Path.DirectorySeparatorChar);
                            var container = @"Unknown";
                            if (pathChunks[0].CaseInsensitiveEquals(@"CookedPCConsole"))
                            {
                                container = "Basegame";
                            } 
                            else if (pathChunks.Length > 1)
                            {
                                container = pathChunks[1];
                            }

                            tfcs.Add($@"<tr><td>{container}</td><td>{Path.GetFileName(tfc)}</td><td>{FileSize.FormatSize(tfcSize)}</td></tr>");
                        }

                        // Make table.
                        var tfctable = $@"
                [HTML]
                <table class=""tfctable"">
                    <thead>
                        <th>Location</th>
                        <th>TFC Name</th>
                        <th>Size</th>
                    </thead>                        
                    <tbody>
                        {string.Join("\n", tfcs)}
                    </tbody>
                </table>
                [/HTML]";
                        addDiagLine(string.Join("\n", tfctable.SplitLinesAll(options: StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())));



                    }
                    else
                    {
                        addDiagLine(@"No TFC files were found - is this installation broken?", ME3TweaksLogViewer.LogSeverity.ERROR);
                    }
                }

                #endregion

                if (hasMEM)
                {
                    #region Files added or removed after texture install
                    MLog.Information(@"Finding files that have been added/replaced/removed after textures were installed");

                    args = $@"--check-game-data-mismatch --gameid {gameID} --ipc";
                    if (package.DiagnosticTarget.TextureModded)
                    {
                        // Is this correct on linux?
                        MLog.Information(@"Checking texture map is in sync with game state");

                        var mapName = $@"me{gameID}map";
                        if (package.DiagnosticTarget.Game.IsLEGame())
                        {
                            mapName = $@"mele{gameID}map"; // LE has different name
                        }

                        bool textureMapFileExists = File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + $@"\MassEffectModder\{mapName}.bin");
                        addDiagLine(@"Files added or removed after texture mods were installed", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);

                        if (textureMapFileExists)
                        {
                            // check for replaced files (file size changes)
                            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_checkingTextureMapGameConsistency));
                            List<string> removedFiles = new List<string>();
                            List<string> addedFiles = new List<string>();
                            List<string> replacedFiles = new List<string>();
                            MEMIPCHandler.RunMEMIPCUntilExit(package.DiagnosticTarget.Game.IsOTGame(), args, false, setMEMCrashLog: memExceptionOccured, ipcCallback: (string command, string param) =>
                            {
                                switch (command)
                                {
                                    case @"ERROR_REMOVED_FILE":
                                        //.Add($" - File removed after textures were installed: {param}");
                                        removedFiles.Add(param);
                                        break;
                                    case @"ERROR_ADDED_FILE":
                                        //addedFiles.Add($"File was added after textures were installed" + param + " " + File.GetCreationTimeUtc(Path.Combine(gamePath, param));
                                        addedFiles.Add(param);
                                        break;
                                    case @"ERROR_VANILLA_MOD_FILE":
                                        if (!addedFiles.Contains(param))
                                        {
                                            replacedFiles.Add(param);
                                        }
                                        break;
                                    default:
                                        Debug.WriteLine(@"oof?");
                                        break;
                                }
                            },
                            applicationExited: i => exitcode = i);
                            if (exitcode != 0)
                            {
                                addDiagLine(
                                    $@"MassEffectModderNoGuiexited exited texture map consistency check with code {exitcode}",
                                    ME3TweaksLogViewer.LogSeverity.ERROR);
                            }

                            if (removedFiles.Any())
                            {
                                addDiagLine(@"The following problems were detected checking game consistency with the texture map file:", ME3TweaksLogViewer.LogSeverity.ERROR);
                                foreach (var error in removedFiles)
                                {
                                    addDiagLine(@" - " + error, ME3TweaksLogViewer.LogSeverity.ERROR);
                                }
                            }

                            if (addedFiles.Any())
                            {
                                addDiagLine(@"The following files were added after textures were installed:", ME3TweaksLogViewer.LogSeverity.ERROR);
                                foreach (var error in addedFiles)
                                {
                                    addDiagLine(@" - " + error, ME3TweaksLogViewer.LogSeverity.ERROR);
                                }
                            }

                            if (replacedFiles.Any())
                            {
                                addDiagLine(@"The following files were replaced after textures were installed:", ME3TweaksLogViewer.LogSeverity.ERROR);
                                foreach (var error in replacedFiles)
                                {
                                    addDiagLine(@" - " + error, ME3TweaksLogViewer.LogSeverity.ERROR);
                                }
                            }

                            if (replacedFiles.Any() || addedFiles.Any() || removedFiles.Any())
                            {
                                addDiagLine(@"Diagnostic detected that some files were added, removed or replaced after textures were installed.", ME3TweaksLogViewer.LogSeverity.ERROR);
                                addDiagLine(@"Package files cannot be installed after a texture mod is installed - the texture pointers will be wrong.", ME3TweaksLogViewer.LogSeverity.ERROR);
                            }
                            else
                            {
                                addDiagLine(@"Diagnostic reports no files appear to have been added or removed since texture scan took place.");
                            }

                        }
                        else
                        {
                            addDiagLine($@"Texture map file is missing: {mapName}.bin - was game migrated to new system or are you running this tool on a different user account than textures were installed with?");
                        }
                    }

                    #endregion

                    #region Textures - full check
                    //FULL CHECK
                    if (package.PerformFullTexturesCheck)
                    {
                        MLog.Information(@"Performing full texture check");
                        var param = 0;
                        package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_performingFullTexturesCheckX, param)); //done this way to save a string in localization
                        addDiagLine(@"Full Textures Check", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                        args = $@"--check-game-data-textures --gameid {gameID} --ipc";
                        var emptyMipsNotRemoved = new List<string>();
                        var badTFCReferences = new List<string>();
                        var scanErrors = new List<string>();
                        string lastMissingTFC = null;
                        package.UpdateProgressCallback?.Invoke(0);
                        package.UpdateTaskbarProgressStateCallback?.Invoke(MTaskbarState.Progressing);

                        string currentProcessingFile = null;
                        void handleIPC(string command, string param)
                        {
                            switch (command)
                            {
                                case @"ERROR_MIPMAPS_NOT_REMOVED":
                                    if (package.DiagnosticTarget.TextureModded)
                                    {
                                        //only matters when game is texture modded
                                        emptyMipsNotRemoved.Add(param);
                                    }
                                    break;
                                case @"TASK_PROGRESS":
                                    if (int.TryParse(param, out var progress))
                                    {
                                        package.UpdateProgressCallback?.Invoke(progress);
                                    }
                                    package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_performingFullTexturesCheckX, param));
                                    break;
                                case @"PROCESSING_FILE":
                                    // Print this out if MEM dies
                                    currentProcessingFile = param;
                                    break;
                                case @"ERROR_REFERENCED_TFC_NOT_FOUND":
                                    //badTFCReferences.Add(param);
                                    lastMissingTFC = param;
                                    break;
                                case @"ERROR_TEXTURE_SCAN_DIAGNOSTIC":
                                    if (lastMissingTFC != null)
                                    {
                                        if (lastMissingTFC.StartsWith(@"Textures_"))
                                        {
                                            var foldername = Path.GetFileNameWithoutExtension(lastMissingTFC).Substring(@"Textures_".Length);
                                            if (MEDirectories.OfficialDLC(package.DiagnosticTarget.Game)
                                                .Contains(foldername))
                                            {
                                                break; //dlc is packed still
                                            }
                                        }
                                        badTFCReferences.Add(lastMissingTFC + @", " + param);
                                    }
                                    else
                                    {
                                        scanErrors.Add(param);
                                    }
                                    lastMissingTFC = null; //reset
                                    break;
                                default:
                                    Debug.WriteLine($@"{command} {param}");
                                    break;
                            }
                        }

                        string memCrashText = null;
                        MEMIPCHandler.RunMEMIPCUntilExit(package.DiagnosticTarget.Game.IsOTGame(),
                            args,
                            false,
                            ipcCallback: handleIPC,
                            applicationExited: x => exitcode = x,
                            setMEMCrashLog: x => memCrashText = x
                        );

                        if (exitcode != 0)
                        {
                            addDiagLine($@"MassEffectModderNoGui exited full textures check with code {exitcode}", ME3TweaksLogViewer.LogSeverity.ERROR);
                            if (currentProcessingFile != null)
                            {
                                addDiagLine($@"The last file processed by MassEffectModder was: {currentProcessingFile}", ME3TweaksLogViewer.LogSeverity.ERROR);
                            }
                            addDiagLine();
                        }

                        package.UpdateProgressCallback?.Invoke(0);
                        package.UpdateTaskbarProgressStateCallback?.Invoke(MTaskbarState.Indeterminate);


                        if (emptyMipsNotRemoved.Any() || badTFCReferences.Any() || scanErrors.Any())
                        {
                            addDiagLine(@"Texture check reported errors", ME3TweaksLogViewer.LogSeverity.ERROR);
                            if (emptyMipsNotRemoved.Any())
                            {
                                addDiagLine();
                                addDiagLine(@"The following textures contain empty mips, which typically means files were installed after texture mods were installed.:", ME3TweaksLogViewer.LogSeverity.ERROR);
                                foreach (var em in emptyMipsNotRemoved)
                                {
                                    addDiagLine(@" - " + em, ME3TweaksLogViewer.LogSeverity.ERROR);
                                }
                            }

                            if (badTFCReferences.Any())
                            {
                                addDiagLine();
                                addDiagLine(@"The following textures have bad TFC references, which means the mods were built wrong, dependent DLC is missing, or the mod was installed wrong:", ME3TweaksLogViewer.LogSeverity.ERROR);
                                foreach (var br in badTFCReferences)
                                {
                                    addDiagLine(@" - " + br, ME3TweaksLogViewer.LogSeverity.ERROR);
                                }
                            }

                            if (scanErrors.Any())
                            {
                                addDiagLine();
                                addDiagLine(@"The following textures failed to scan:", ME3TweaksLogViewer.LogSeverity.ERROR);
                                foreach (var fts in scanErrors)
                                {
                                    addDiagLine(@" - " + fts, ME3TweaksLogViewer.LogSeverity.ERROR);
                                }
                            }
                        }
                        else if (exitcode != 0)
                        {
                            addDiagLine(@"Texture check failed");
                            if (memCrashText != null)
                            {
                                addDiagLine(@"MassEffectModder crashed with info:");
                                addDiagLines(memCrashText.Split("\n"), ME3TweaksLogViewer.LogSeverity.ERROR); //do not localize
                            }
                        }
                        else
                        {
                            // Is this right?? We skipped check. We can't just print this
                            addDiagLine(@"Texture check did not find any texture issues in this installation");
                        }
                    }
                    #endregion

                    #region Texture LODs
                    MLog.Information(@"Collecting texture LODs");

                    package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingLODSettings));
                    var lods = MEMIPCHandler.GetLODs(package.DiagnosticTarget.Game);
                    if (lods != null)
                    {
                        addLODStatusToDiag(package.DiagnosticTarget, lods, addDiagLine);
                    }
                    else
                    {
                        addDiagLine(@"MassEffectModderNoGui exited --print-lods with error. See application log for more info.", ME3TweaksLogViewer.LogSeverity.ERROR);
                    }

                    #endregion
                }
                else
                {
                    MLog.Warning(@"MEM not available. Multiple collections were skipped");

                    addDiagLine(@"Texture checks skipped", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                    addDiagLine(@"Mass Effect Modder No Gui was not available for use when this diagnostic was run.", ME3TweaksLogViewer.LogSeverity.WARN);
                    addDiagLine(@"The following checks were skipped:", ME3TweaksLogViewer.LogSeverity.WARN);
                    addDiagLine(@" - Files added or removed after texture install", ME3TweaksLogViewer.LogSeverity.WARN);
                    addDiagLine(@" - Blacklisted mods check", ME3TweaksLogViewer.LogSeverity.WARN);
                    addDiagLine(@" - Textures check", ME3TweaksLogViewer.LogSeverity.WARN);
                    addDiagLine(@" - Texture LODs check", ME3TweaksLogViewer.LogSeverity.WARN);
                }

                #region ASI mods
                MLog.Information(@"Collecting ASI mod information");

                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingASIFileInformation));

                string asidir = M3Directories.GetASIPath(package.DiagnosticTarget);
                addDiagLine(@"Installed ASI mods", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                if (Directory.Exists(asidir))
                {
                    addDiagLine(@"The following ASI files are located in the ASI directory:");
                    string[] files = Directory.GetFiles(asidir, @"*.asi");
                    if (!files.Any())
                    {
                        addDiagLine(@"ASI directory is empty. No ASI mods are installed.");
                    }
                    else
                    {
                        var installedASIs = package.DiagnosticTarget.GetInstalledASIs();
                        var nonUniqueItems = installedASIs.OfType<KnownInstalledASIMod>().SelectMany(
                            x => installedASIs.OfType<IKnownInstalledASIMod>().Where(
                                y => x != y
                                     && x.AssociatedManifestItem.OwningMod ==
                                     y.AssociatedManifestItem.OwningMod)
                            ).Distinct().ToList();

                        foreach (var knownAsiMod in installedASIs.OfType<IKnownInstalledASIMod>().Except(nonUniqueItems))
                        {
                            var str = $@" - {knownAsiMod.AssociatedManifestItem.Name} v{knownAsiMod.AssociatedManifestItem.Version} ({Path.GetFileName(knownAsiMod.InstalledPath)})";
                            if (knownAsiMod.Outdated)
                            {
                                str += @" - Outdated";
                            }
                            addDiagLine(str, knownAsiMod.Outdated ? ME3TweaksLogViewer.LogSeverity.WARN : ME3TweaksLogViewer.LogSeverity.GOOD);
                        }

                        foreach (var unknownAsiMod in installedASIs.OfType<IUnknownInstalledASIMod>())
                        {
                            addDiagLine($@" - {Path.GetFileName(unknownAsiMod.InstalledPath)} - Unknown ASI mod", ME3TweaksLogViewer.LogSeverity.WARN);
                        }

                        foreach (var duplicateItem in nonUniqueItems)
                        {
                            var str = $@" - {duplicateItem.AssociatedManifestItem.Name} v{duplicateItem.AssociatedManifestItem.Version} ({Path.GetFileName(duplicateItem.InstalledPath)})";
                            if (duplicateItem.Outdated)
                            {
                                str += @" - Outdated";
                            }

                            str += @" - DUPLICATE ASI";
                            addDiagLine(str, ME3TweaksLogViewer.LogSeverity.FATAL);
                        }

                        addDiagLine();
                        addDiagLine(@"Ensure that only one version of an ASI is installed. If multiple copies of the same one are installed, the game may crash on startup.");
                    }
                }
                else
                {
                    addDiagLine(@"ASI directory does not exist. No ASI mods are installed.");
                }

                #endregion
                #region ASI Logs
                if (package.DiagnosticTarget.Game.IsLEGame())
                {
                    MLog.Information(@"Collecting ASI log files");
                    package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingASILogFiles));

                    var logFiles = GetASILogs(package.DiagnosticTarget);
                    addDiagLine(@"ASI log files", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                    addDiagLine(@"These are log files from installed ASI mods (within the past day). These are >>highly<< technical; only advanced developers should attempt to interpret these logs.");
                    if (logFiles.Any())
                    {
                        foreach (var logF in logFiles)
                        {
                            addDiagLine(logF.Key, ME3TweaksLogViewer.LogSeverity.BOLD);
                            addDiagLine(@"Click to view log", ME3TweaksLogViewer.LogSeverity.SUB);
                            addDiagLine(logF.Value);
                            addDiagLine(END_SUB);
                        }
                    }
                    else
                    {
                        addDiagLine(@"No recent ASI logs were found.");
                    }
                }
                #endregion

                #region ME3/LE: TOC check

                //TOC SIZE CHECK
                if (package.DiagnosticTarget.Game == MEGame.ME3 || package.DiagnosticTarget.Game.IsLEGame())
                {
                    MLog.Information(@"Collecting TOC information");

                    package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingTOCFileInformation));

                    addDiagLine(@"File Table of Contents (TOC) check", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                    addDiagLine(@"PCConsoleTOC.bin files list all files the game can normally access and stores the values in hash tables for faster lookup.");
                    bool hadTocError = false;
                    string markerfile = package.DiagnosticTarget.GetTextureMarkerPath();
                    var bgTOC = Path.Combine(package.DiagnosticTarget.GetBioGamePath(), @"PCConsoleTOC.bin"); // Basegame
                    var isTOCVanilla = VanillaDatabaseService.IsFileVanilla(package.DiagnosticTarget, bgTOC, true);
                    if (isTOCVanilla)
                    {
                        addDiagLine($@"Unmodified vanilla TOC: {Path.GetRelativePath(package.DiagnosticTarget.TargetPath, bgTOC)}", ME3TweaksLogViewer.LogSeverity.GOOD);
                        addDiagLine(@"The vanilla shipping game includes references to files that don't exist and incorrect size values for some files; these are normal.");
                        addDiagLine(@"If you restored from a backup without all localizations, there may be additional missing LOC entries listed here, that is normal.");
                    }

                    hadTocError |= CheckTOCFile(package, bgTOC, markerfile, addDiagLine, isTOCVanilla);

                    var dlcs = package.DiagnosticTarget.GetInstalledDLC();
                    var dlcTOCs = new List<string>();
                    foreach (var v in dlcs)
                    {
                        var tocPath = Path.Combine(package.DiagnosticTarget.GetDLCPath(), v, @"PCConsoleTOC.bin");
                        if (File.Exists(tocPath))
                        {
                            dlcTOCs.Add(tocPath);
                        }
                    }

                    foreach (string toc in dlcTOCs)
                    {
                        isTOCVanilla = VanillaDatabaseService.IsFileVanilla(package.DiagnosticTarget, toc, true);
                        if (isTOCVanilla)
                        {
                            addDiagLine($@"Unmodified vanilla TOC: {Path.GetRelativePath(package.DiagnosticTarget.TargetPath, toc)}", ME3TweaksLogViewer.LogSeverity.GOOD);
                        }
                        hadTocError |= CheckTOCFile(package, toc, markerfile, addDiagLine, isTOCVanilla);
                    }

                    if (package.DiagnosticTarget.Game.IsOTGame())
                    {
                        if (!hadTocError)
                        {
                            addDiagLine(@"All TOC files passed check. No files have a size larger than the TOC size.");
                        }
                        else
                        {
                            addDiagLine(@"Some files are larger than the listed TOC size. This typically won't happen unless you manually installed some files or an ALOT installation failed. The game may have issues loading these files.", ME3TweaksLogViewer.LogSeverity.ERROR);
                        }
                    }
                }

                #endregion

                #region Mass Effect (1) log files

                //ME1: LOGS
                if (package.DiagnosticTarget.Game == MEGame.ME1)
                {
                    MLog.Information(@"Collecting ME1 crash logs");

                    package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingME1ApplicationLogs));

                    //GET LOGS
                    string logsdir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), @"BioWare\Mass Effect\Logs");
                    if (Directory.Exists(logsdir))
                    {
                        DirectoryInfo info = new DirectoryInfo(logsdir);
                        FileInfo[] files = info.GetFiles().Where(f => f.LastWriteTime > DateTime.Now.AddDays(-3)).OrderByDescending(p => p.LastWriteTime).ToArray();
                        DateTime threeDaysAgo = DateTime.Now.AddDays(-3);
                        foreach (FileInfo file in files)
                        {
                            //Console.WriteLine(file.Name + " " + file.LastWriteTime);
                            var logLines = File.ReadAllLines(file.FullName);
                            int crashLineNumber = -1;
                            int currentLineNumber = -1;
                            string reason = "";
                            foreach (string line in logLines)
                            {
                                if (line.Contains(@"Critical: appError called"))
                                {
                                    crashLineNumber = currentLineNumber;
                                    reason = @"Log file indicates crash occurred";
                                    MLog.Information(@"Found crash in ME1 log " + file.Name + @" on line " + currentLineNumber);
                                    break;
                                }

                                currentLineNumber++;
                            }

                            if (crashLineNumber >= 0)
                            {
                                crashLineNumber = Math.Max(0, crashLineNumber - 10); //show last 10 lines of log leading up to the crash
                                                                                     //this log has a crash
                                addDiagLine(@"Mass Effect game log " + file.Name, ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                                if (reason != "") addDiagLine(reason);
                                if (crashLineNumber > 0)
                                {
                                    addDiagLine(@"[CRASHLOG]...");
                                }

                                for (int i = crashLineNumber; i < logLines.Length; i++)
                                {
                                    addDiagLine(@"[CRASHLOG]" + logLines[i]);
                                }
                            }
                        }
                    }
                }

                #endregion

                #region Event logs for crashes

                //EVENT LOGS
                MLog.Information(@"Collecting event logs");
                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingEventLogs));
                StringBuilder crashLogs = new StringBuilder();
                var sevenDaysAgo = DateTime.Now.AddDays(-3);

                //Get event logs
                EventLog ev = new EventLog(@"Application");
                List<EventLogEntry> entries = ev.Entries
                    .Cast<EventLogEntry>()
                    .Where(z => z.InstanceId == 1001 && z.TimeGenerated > sevenDaysAgo && (GenerateEventLogString(z).ContainsAny(MEDirectories.ExecutableNames(package.DiagnosticTarget.Game), StringComparison.InvariantCultureIgnoreCase)))
                    .ToList();

                addDiagLine($@"{package.DiagnosticTarget.Game.ToGameName()} crash logs found in Event Viewer", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                if (entries.Any())
                {
                    addDiagLine($@"Crash event logs are often not useful except for determining if the executable or a library crashed the game");
                    addDiagLine(@"Click to view events", ME3TweaksLogViewer.LogSeverity.SUB);
                    foreach (var entry in entries)
                    {
                        string str = string.Join("\n", GenerateEventLogString(entry).Split('\n').ToList().Take(17).ToList()); //do not localize
                        addDiagLine($"{package.DiagnosticTarget.Game.ToGameName()} Event {entry.TimeGenerated}\n{str}"); //do not localize
                    }
                    addDiagLine(END_SUB);
                }
                else
                {
                    addDiagLine(@"No crash events found in Event Viewer");
                }

                #endregion

                #region ME3Logger

                if (package.DiagnosticTarget.Game == MEGame.ME3)
                {
                    MLog.Information(@"Collecting ME3Logger session log");
                    package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingME3SessionLog));
                    string me3logfilepath = Path.Combine(Directory.GetParent(M3Directories.GetExecutablePath(package.DiagnosticTarget)).FullName, @"me3log.txt");
                    if (File.Exists(me3logfilepath))
                    {
                        FileInfo fi = new FileInfo(me3logfilepath);
                        addDiagLine(@"Mass Effect 3 last session log", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
                        addDiagLine(@"Last session log has modification date of " + fi.LastWriteTimeUtc.ToShortDateString());
                        addDiagLine(@"Note that messages from this log can be highly misleading as they are context dependent!");
                        addDiagLine();
                        var log = MUtilities.WriteSafeReadAllLines(me3logfilepath); //try catch needed?
                        int lineNum = 0;
                        foreach (string line in log)
                        {
                            addDiagLine(line, line.Contains(@"I/O failure", StringComparison.InvariantCultureIgnoreCase) ? ME3TweaksLogViewer.LogSeverity.FATAL : ME3TweaksLogViewer.LogSeverity.INFO);
                            lineNum++;
                            if (lineNum > 100)
                            {
                                break;
                            }
                        }

                        if (lineNum > 200)
                        {
                            addDiagLine(@"... log truncated ...");
                        }
                    }
                }

                #endregion

                // LE logs are collected by ASI logs above.
            }
            catch (Exception ex)
            {
                addDiagLine(@"Exception occurred while running diagnostic.", ME3TweaksLogViewer.LogSeverity.ERROR);
                addDiagLine(ex.FlattenException(), ME3TweaksLogViewer.LogSeverity.ERROR);
                return diagStringBuilder.ToString();
            }
            finally
            {
                //restore MEM setting
                // This is M3 specific
                if (hasMEM)
                {
                    if (File.Exists(_iniPath))
                    {
                        DuplicatingIni ini = DuplicatingIni.LoadIni(_iniPath);
                        ini[@"GameDataPath"][package.DiagnosticTarget.Game.ToString()].Value = oldMemGamePath;
                        File.WriteAllText(_iniPath, ini.ToString());
                    }
                }
            }

            // We have to strip any null terminators or it will bork it on the server log viewer
            return diagStringBuilder.ToString().Replace("\0", @""); // do not localize
        }

        /// <summary>
        /// Checks the TOC file at the listed path and prints information to the diagnostic for it.
        /// </summary>
        /// <param name="package"></param>
        /// <param name="tocFilePath">TOC file to check</param>
        /// <param name="textureMarkerFilePath"></param>
        /// <param name="addDiagLine">Function to print to diagnostic</param>
        /// <param name="isTocVanilla"></param>
        private static bool CheckTOCFile(LogUploadPackage package, string tocFilePath, string textureMarkerFilePath, Action<string, ME3TweaksLogViewer.LogSeverity> addDiagLine, bool isTocVanilla)
        {
            bool hadTocError = false;
            var tocrootPath = package.DiagnosticTarget.TargetPath;
            if (Path.GetFileName(Directory.GetParent(tocFilePath).FullName).StartsWith(@"DLC_"))
            {
                tocrootPath = Directory.GetParent(tocFilePath).FullName;
            }

            MLog.Information($@"Checking TOC file {tocFilePath}");

            TOCBinFile tbf = new TOCBinFile(tocFilePath);
            if (!isTocVanilla)
            {
                // If TOC is vanilla this information is not helpful and just pollutes the output.
                addDiagLine($@" - {tocFilePath.Substring(package.DiagnosticTarget.TargetPath.Length + 1)}: {tbf.GetAllEntries().Count} file entries, {tbf.HashBuckets.Count} hash buckets", ME3TweaksLogViewer.LogSeverity.INFO);
            }

            int notPresentOnDiskCount = 0;
            bool isSubbed = false;
            foreach (TOCBinFile.Entry ent in tbf.GetAllEntries())
            {
                if (ent.name == "PCConsoleTOC.txt")
                    continue; // This file is not shipped in most games, nor is it used, so we don't care.

                //Console.WriteLine(index + "\t0x" + ent.offset.ToString("X6") + "\t" + ent.size + "\t" + ent.name);

                string filepath = Path.Combine(tocrootPath, ent.name);
                var fileExists = File.Exists(filepath);
                if (fileExists)
                {
                    if (!filepath.Equals(textureMarkerFilePath, StringComparison.InvariantCultureIgnoreCase) && !filepath.ToLower().EndsWith(@"pcconsoletoc.bin"))
                    {
                        FileInfo fi = new FileInfo(filepath);
                        long size = fi.Length;
                        if (ent.size < size && (ent.size == 0 || package.DiagnosticTarget.Game.IsOTGame()))
                        {
                            // Size only matters on OT or if zero on LE
                            addDiagLine($@"   >  {filepath} size is {size}, but TOC lists {ent.size} ({ent.size - size} bytes)", ME3TweaksLogViewer.LogSeverity.ERROR);
                            hadTocError = true;
                        }
                    }
                }
                else
                {
                    if (!isSubbed && notPresentOnDiskCount > 10)
                    {
                        isSubbed = true;
                        addDiagLine("Click to view more", ME3TweaksLogViewer.LogSeverity.SUB);
                    }
                    else
                    {
                        notPresentOnDiskCount++;
                    }

                    addDiagLine($@"   > {filepath} is listed in TOC but is not present on disk", ME3TweaksLogViewer.LogSeverity.WARN);
                }
            }

            if (notPresentOnDiskCount > 10)
            {
                addDiagLine(END_SUB, ME3TweaksLogViewer.LogSeverity.INFO);
            }

            return hadTocError;
        }

        private static void SeeIfIncompatibleDLCIsInstalled(GameTarget target, Action<string, ME3TweaksLogViewer.LogSeverity> addDiagLine)
        {
            var installedDLCMods = VanillaDatabaseService.GetInstalledDLCMods(target);
            var metaFiles = target.GetMetaMappedInstalledDLC(false);

            foreach (var v in metaFiles)
            {
                if (v.Value != null && v.Value.IncompatibleDLC.Any())
                {
                    // See if any DLC is not compatible
                    var installedIncompatDLC = installedDLCMods.Intersect(v.Value.IncompatibleDLC, StringComparer.InvariantCultureIgnoreCase).ToList();
                    foreach (var id in installedIncompatDLC)
                    {
                        var incompatName = TPMIService.GetThirdPartyModInfo(id, target.Game);
                        addDiagLine($@"{v.Value.ModName} is not compatible with {incompatName?.modname ?? id}", ME3TweaksLogViewer.LogSeverity.FATAL);
                    }
                }
            }
        }

        private static void addLODStatusToDiag(GameTarget selectedDiagnosticTarget, Dictionary<string, string> lods, Action<string, ME3TweaksLogViewer.LogSeverity> addDiagLine)
        {
            addDiagLine(@"Texture Level of Detail (LOD) settings", ME3TweaksLogViewer.LogSeverity.DIAGSECTION);
            if (selectedDiagnosticTarget.Game.IsLEGame())
            {
                addDiagLine(@"These should always be blank for Legendary Edition games. Legendary Edition modding does not modify LODs due to engine changes.", ME3TweaksLogViewer.LogSeverity.INFO);
            }

            string iniPath = M3Directories.GetLODConfigFile(selectedDiagnosticTarget);
            if (!File.Exists(iniPath))
            {
                addDiagLine($@"Game config file is missing - has game been run once?: {iniPath}", ME3TweaksLogViewer.LogSeverity.WARN);
                return;
            }

            foreach (KeyValuePair<string, string> kvp in lods)
            {
                addDiagLine($@"{kvp.Key}={kvp.Value}", ME3TweaksLogViewer.LogSeverity.INFO);
            }

            var textureChar1024 = lods.FirstOrDefault(x => x.Key == @"TEXTUREGROUP_Character_1024");
            if (string.IsNullOrWhiteSpace(textureChar1024.Key)) //does this work for ME2/ME3??
            {
                //not found
                addDiagLine(@"Could not find TEXTUREGROUP_Character_1024 in config file for checking LOD settings", ME3TweaksLogViewer.LogSeverity.ERROR);
                return;
            }

            try
            {
                int maxLodSize = 0;
                if (!string.IsNullOrWhiteSpace(textureChar1024.Value))
                {
                    //ME2,3 default to blank
                    maxLodSize = int.Parse(StringStructParser.GetCommaSplitValues(textureChar1024.Value)[selectedDiagnosticTarget.Game == MEGame.ME1 ? @"MinLODSize" : @"MaxLODSize"]);
                }

                // Texture mod installed, missing HQ LODs
                var HQSettingsMissingLine = @"High quality texture LOD settings appear to be missing, but a high resolution texture mod appears to be installed.\n[ERROR]The game will not use these new high quality assets - config file was probably deleted or texture quality settings were changed in game"; //do not localize

                // No texture mod, no HQ LODs
                var HQVanillaLine = @"High quality LOD settings are not set and no high quality texture mod is installed";
                switch (selectedDiagnosticTarget.Game)
                {
                    case MEGame.ME1:
                        if (maxLodSize != 1024) //ME1 Default
                        {
                            //LODS MODIFIED!
                            if (maxLodSize == 4096)
                            {
                                addDiagLine(@"LOD quality settings: 4K textures", ME3TweaksLogViewer.LogSeverity.INFO);
                            }
                            else if (maxLodSize == 2048)
                            {
                                addDiagLine(@"LOD quality settings: 2K textures", ME3TweaksLogViewer.LogSeverity.INFO);
                            }

                            //Not Default
                            if (selectedDiagnosticTarget.TextureModded)
                            {
                                addDiagLine(@"This installation appears to have a texture mod installed, so unused/empty mips are already removed", ME3TweaksLogViewer.LogSeverity.INFO);
                            }
                            else if (maxLodSize > 1024)
                            {
                                addDiagLine(@"Texture LOD settings appear to have been raised, but this installation has not been texture modded - game will likely have unused mip crashes.", ME3TweaksLogViewer.LogSeverity.FATAL);
                            }
                        }
                        else
                        {
                            //Default ME1 LODs
                            if (selectedDiagnosticTarget.TextureModded && selectedDiagnosticTarget.HasALOTOrMEUITM())
                            {
                                addDiagLine(HQSettingsMissingLine, ME3TweaksLogViewer.LogSeverity.ERROR);
                            }
                            else
                            {
                                addDiagLine(HQVanillaLine, ME3TweaksLogViewer.LogSeverity.INFO);
                            }
                        }

                        break;
                    case MEGame.ME2:
                    case MEGame.ME3:
                        if (maxLodSize != 0)
                        {
                            //Not vanilla, alot/meuitm
                            if (selectedDiagnosticTarget.TextureModded && selectedDiagnosticTarget.HasALOTOrMEUITM())
                            {
                                //addDiagLine(HQVanillaLine, LogSeverity.INFO);
                                if (maxLodSize == 4096)
                                {
                                    addDiagLine(@"LOD quality settings: 4K textures", ME3TweaksLogViewer.LogSeverity.INFO);
                                }
                                else if (maxLodSize == 2048)
                                {
                                    addDiagLine(@"LOD quality settings: 2K textures", ME3TweaksLogViewer.LogSeverity.INFO);
                                }
                            }
                            else
                            {
                                //else if (selectedDiagnosticTarget.TextureModded) //not vanilla, but no MEM/MEUITM
                                //{
                                if (maxLodSize == 4096)
                                {
                                    addDiagLine(@"LOD quality settings: 4K textures (no high res mod installed)", ME3TweaksLogViewer.LogSeverity.WARN);
                                }
                                else if (maxLodSize == 2048)
                                {
                                    addDiagLine(@"LOD quality settings: 2K textures (no high res mod installed)", ME3TweaksLogViewer.LogSeverity.INFO);
                                }

                                //}
                                if (!selectedDiagnosticTarget.TextureModded)
                                {
                                    //no texture mod, but has set LODs
                                    addDiagLine(@"LODs have been explicitly set, but a texture mod is not installed - game may have black textures as empty mips may not be removed", ME3TweaksLogViewer.LogSeverity.WARN);
                                }
                            }
                        }
                        else //default
                        {
                            //alot/meuitm, but vanilla settings.
                            if (selectedDiagnosticTarget.TextureModded &&
                                selectedDiagnosticTarget.HasALOTOrMEUITM())
                            {
                                addDiagLine(HQSettingsMissingLine, ME3TweaksLogViewer.LogSeverity.ERROR);
                            }
                            else //no alot/meuitm, vanilla setting.
                            {
                                addDiagLine(HQVanillaLine, ME3TweaksLogViewer.LogSeverity.INFO);
                            }
                        }

                        break;
                }
            }
            catch (Exception e)
            {
                MLog.Error(@"Error checking LOD settings: " + e.Message);
                addDiagLine($@"Error checking LOD settings: {e.Message}", ME3TweaksLogViewer.LogSeverity.INFO);
            }
        }

        private static string getSignerSubject(string subject)
        {
            // Get Common Name (CN)
            var props = StringStructParser.GetCommaSplitValues($"({subject})"); // do not localize
            return props[@"CN"];
        }

        private static void diagPrintSignatures(FileInspector info, Action<string, ME3TweaksLogViewer.LogSeverity> addDiagLine)
        {
            foreach (var sig in info.GetSignatures())
            {
                var signingTime = sig.TimestampSignatures.FirstOrDefault()?.TimestampDateTime?.UtcDateTime;
                addDiagLine(@" > Signed on " + signingTime, ME3TweaksLogViewer.LogSeverity.INFO);

                bool isFirst = true;
                foreach (var signChain in sig.AdditionalCertificates)
                {
                    try
                    {
                        addDiagLine($@" >> {(isFirst ? @"Signed" : @"Countersigned")} by {getSignerSubject(signChain.Subject)}", ME3TweaksLogViewer.LogSeverity.INFO); // do not localize
                    }
                    catch
                    {
                        addDiagLine($@"  >> {(isFirst ? "Signed" : "Countersigned")} by " + signChain.Subject, ME3TweaksLogViewer.LogSeverity.INFO); // do not localize
                    }

                    isFirst = false;
                }
            }
        }

        private static string GenerateEventLogString(EventLogEntry entry) =>
            $"Event type: {entry.EntryType}\nEvent Message: {entry.Message + entry}\nEvent Time: {entry.TimeGenerated.ToShortTimeString()}\nEvent {entry.UserName}\n"; //do not localize

        private static int GetPartitionDiskBackingType(string partitionLetter)
        {
            using (var partitionSearcher = new ManagementObjectSearcher(
                @"\\localhost\ROOT\Microsoft\Windows\Storage",
                $@"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter='{partitionLetter}'"))
            {
                try
                {
                    var partition = partitionSearcher.Get().Cast<ManagementBaseObject>().Single();
                    using (var physicalDiskSearcher = new ManagementObjectSearcher(
                        @"\\localhost\ROOT\Microsoft\Windows\Storage",
                        $@"SELECT Size, Model, MediaType FROM MSFT_PhysicalDisk WHERE DeviceID='{partition[@"DiskNumber"]}'")) //do not localize
                    {
                        var physicalDisk = physicalDiskSearcher.Get().Cast<ManagementBaseObject>().Single();
                        return (UInt16)physicalDisk[@"MediaType"];
                    }
                }
                catch (Exception e)
                {
                    MLog.Warning($@"Error reading partition type on {partitionLetter}: {e.Message}. This may be an expected error due to how WMI works");
                    return -1;
                }
            }
        }

        // ASIs began changing over to .log 03/17/2022
        // KismetLogger uses .txt still (we don't care)
        private static string[] asilogExtensions = new[] { @".log" };

        /// <summary>
        /// Gets the contents of log files in the same directory as the game executable. This only returns logs for LE, OT doesn't really have any debug loggers beyond one.
        /// </summary>
        /// <returns>Dictionary of logs, mapped filename to contents. Will return null if not an LE game</returns>
        private static Dictionary<string, string> GetASILogs(GameTarget target)
        {
            if (!target.Game.IsLEGame()) return null;
            var logs = new Dictionary<string, string>();
            var directory = target.GetExecutableDirectory();
            if (Directory.Exists(directory))
            {
                foreach (var f in Directory.GetFiles(directory, "*"))
                {
                    try
                    {
                        if (!asilogExtensions.Contains(Path.GetExtension(f)))
                            continue; // Not parsable

                        var fi = new FileInfo(f);
                        var timeDelta = DateTime.Now - fi.LastWriteTime;
                        if (timeDelta < TimeSpan.FromDays(1))
                        {
                            // If the log was written within the last day.
                            StringBuilder sb = new StringBuilder();
                            var fileContentsLines = File.ReadAllLines(f);

                            int lastIndexRead = 0;
                            // Read first 30 lines.
                            for (int i = 0; i < 30 && i < fileContentsLines.Length - 1; i++)
                            {
                                sb.AppendLine(fileContentsLines[i]);
                                lastIndexRead = i;
                            }

                            // Read last 30 lines.
                            if (lastIndexRead < fileContentsLines.Length - 1)
                            {
                                sb.AppendLine(@"...");
                                var startIndex = Math.Max(lastIndexRead, fileContentsLines.Length - 30);
                                for (int i = startIndex; i < fileContentsLines.Length - 1; i++)
                                {
                                    sb.AppendLine(fileContentsLines[i]);
                                }
                            }

                            logs[Path.GetFileName(f)] = sb.ToString();
                        }
                        else
                        {
                            MLog.Information($@"Skipping log: {Path.GetFileName(f)}. Last write time was {fi.LastWriteTime}. Only files written within last day are included");
                        }
                    }
                    catch (Exception e)
                    {
                        logs[Path.GetFileName(f)] = e.Message;
                    }
                }
            }

            return logs;
        }

        public static string GetProcessorInformationForDiag()
        {
            string str = "";
            try
            {
                ManagementObjectSearcher mosProcessor = new ManagementObjectSearcher(@"SELECT * FROM Win32_Processor");

                foreach (ManagementObject moProcessor in mosProcessor.Get())
                {
                    if (str != "")
                    {
                        str += "\n"; //do not localize
                    }

                    if (moProcessor[@"name"] != null)
                    {
                        str += moProcessor[@"name"].ToString();
                        str += "\n"; //do not localize
                    }
                    if (moProcessor[@"maxclockspeed"] != null)
                    {
                        str += @"Maximum reported clock speed: ";
                        str += moProcessor[@"maxclockspeed"].ToString();
                        str += " Mhz\n"; //do not localize
                    }
                    if (moProcessor[@"numberofcores"] != null)
                    {
                        str += @"Cores: ";

                        str += moProcessor[@"numberofcores"].ToString();
                        str += "\n"; //do not localize
                    }
                    if (moProcessor[@"numberoflogicalprocessors"] != null)
                    {
                        str += @"Logical processors: ";
                        str += moProcessor[@"numberoflogicalprocessors"].ToString();
                        str += "\n"; //do not localize
                    }

                }
                return str
                   .Replace(@"(TM)", @"™")
                   .Replace(@"(tm)", @"™")
                   .Replace(@"(R)", @"®")
                   .Replace(@"(r)", @"®")
                   .Replace(@"(C)", @"©")
                   .Replace(@"(c)", @"©")
                   .Replace(@"    ", @" ")
                   .Replace(@"  ", @" ").Trim();
            }
            catch (Exception e)
            {
                MLog.Error($@"Error getting processor information: {e.Message}");
                return $"Error getting processor information: {e.Message}\n"; //do not localize
            }
        }

        /// <summary>
        /// Log session divider. Should always be the very first line of a new session
        /// </summary>
        public static string SessionStartString { get; } = @"============================SESSION START============================";

        /// <summary>
        /// Creates a document ready for upload to ME3Tweaks Log Viewing Service.
        /// </summary>
        /// <param name="selectedDiagnosticTarget"></param>
        /// <param name="selectedLog"></param>
        /// <param name="textureCheck"></param>
        /// <param name="updateStatusCallback"></param>
        /// <param name="updateProgressCallback"></param>
        /// <param name="updateTaskbarProgressStateCallback"></param>
        /// <returns></returns>
        public static string SubmitDiagnosticLog(LogUploadPackage package)
        {

            StringBuilder logUploadText = new StringBuilder();
            if (package.DiagnosticTarget != null && !package.DiagnosticTarget.IsCustomOption && (package.DiagnosticTarget.Game.IsOTGame() || package.DiagnosticTarget.Game.IsLEGame()))
            {
                Debug.WriteLine(@"Selected game target: " + package.DiagnosticTarget.TargetPath);
                logUploadText.Append("[MODE]diagnostics\n"); //do not localize
                logUploadText.Append(LogCollector.PerformDiagnostic(package));
                logUploadText.Append("\n"); //do not localize
            }

            if (package.SelectedLog != null && package.SelectedLog.Selectable)
            {
                Debug.WriteLine(@"Selected log: " + package.SelectedLog.filepath);
                logUploadText.Append("[MODE]logs\n"); //do not localize
                logUploadText.AppendLine(LogCollector.CollectLogs(package.SelectedLog.filepath));
                logUploadText.Append("\n"); //do not localize
            }

            var logtext = logUploadText.ToString();
            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_compressingForUpload));
            var lzmalog = LZMA.CompressToLZMAFile(Encoding.UTF8.GetBytes(logtext));
            try
            {
                //this doesn't need to technically be async, but library doesn't have non-async method.
                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_uploadingToME3Tweaks));

                dynamic data = new ExpandoObject();
                IDictionary<string, object> dictionary = (IDictionary<string, object>)data;

                dictionary.Add(@"LogData", Convert.ToBase64String(lzmalog));
                if (package.Attachments != null)
                {
                    foreach (var attachment in package.Attachments)
                    {
                        // For save files this will be a filename ending with .pcsav
                        dictionary.Add(attachment.Key, Convert.ToBase64String(attachment.Value));
                    }
                }
                dictionary.Add(@"ToolName", MLibraryConsumer.GetHostingProcessname());
                dictionary.Add(@"ToolVersion", MLibraryConsumer.GetAppVersion());

                //10/22/2023 - Change to unified endpoint
                string responseString = @"https://me3tweaks.com/modmanager/logservice/shared/logupload".PostUrlEncodedAsync(dictionary)

                    //new
                    //{
                    //    LogData = Convert.ToBase64String(lzmalog),
                    //    Attachments = package.Attachments,
                    //    ToolName = MLibraryConsumer.GetHostingProcessname(),
                    //    ToolVersion = MLibraryConsumer.GetAppVersion()
                    //})
                    .ReceiveString().Result;
                Uri uriResult;
                bool result = Uri.TryCreate(responseString, UriKind.Absolute, out uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
                if (result)
                {
                    //should be valid URL.
                    //diagnosticsWorker.ReportProgress(0, new ThreadCommand(SET_DIAGTASK_ICON_GREEN, Image_Upload));
                    //e.Result = responseString;
                    MLog.Information(@"Result from server for log upload: " + responseString);
                    return responseString;
                }
                else
                {
                    MLog.Error(@"Error uploading log. The server responded with: " + responseString);
                    return LC.GetString(LC.string_interp_serverRejectedTheUpload, responseString);
                }
            }
            catch (AggregateException e)
            {
                Exception ex = e.InnerException;
                string exmessage = ex.Message;
                return LC.GetString(LC.string_interp_logWasUnableToUpload, exmessage);
            }
            catch (FlurlHttpTimeoutException)
            {
                // FlurlHttpTimeoutException derives from FlurlHttpException; catch here only
                // if you want to handle timeouts as a special case
                MLog.Error(@"Request timed out while uploading log.");
                return LC.GetString(LC.string_interp_requestTimedOutUploading);

            }
            catch (Exception ex)
            {
                // ex.Message contains rich details, including the URL, verb, response status,
                // and request and response bodies (if available)
                MLog.Exception(ex, @"Handled error uploading log: ");
                string exmessage = ex.Message;
                var index = exmessage.IndexOf(@"Request body:");
                if (index > 0)
                {
                    exmessage = exmessage.Substring(0, index);
                }

                return LC.GetString(LC.string_interp_logWasUnableToUpload, exmessage);
            }
        }
    }

    /// <summary>
    /// Package of options for what to collect in a diagnostic/log upload.
    /// </summary>
    public class LogUploadPackage
    {
        // 10/22/2023 - Change to unified/abstracted log system serverside
        ///// <summary>
        ///// Endpoint for uploading to. This is required.
        ///// </summary>
        //public string UploadEndpoint { get; set; }

        /// <summary>
        /// Target to perform diagnostic on (can be null)
        /// </summary>
        public GameTarget DiagnosticTarget { get; set; }

        /// <summary>
        /// Application log to upload (can be null)
        /// </summary>
        public LogItem SelectedLog { get; set; }

        /// <summary>
        /// The save file to upload, if any
        /// </summary>
        public string SelectedSaveFilePath { get; set; }

        /// <summary>
        /// If a full texture check should be performed. This only occurs if DiagnosticTarget is not null.
        /// </summary>
        public bool PerformFullTexturesCheck { get; set; }

        /// <summary>
        /// Invoked when the text status of the log collection should be updated.
        /// </summary>
        public Action<string> UpdateStatusCallback { get; set; }

        /// <summary>
        /// Invoked when a progress bar of some kind should be updated
        /// </summary>
        public Action<int> UpdateProgressCallback { get; set; }

        /// <summary>
        /// Invoked when a taskbar state should be updated (or progressbar)
        /// </summary>
        public Action<MTaskbarState> UpdateTaskbarProgressStateCallback { get; set; }

        /// <summary>
        /// Mapping of any attachments that are also included in the upload
        /// </summary>
        public Dictionary<string, byte[]> Attachments { get; set; }
    }
}
