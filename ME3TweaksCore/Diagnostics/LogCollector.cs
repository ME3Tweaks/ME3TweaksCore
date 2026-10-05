using Flurl.Http;
using LegendaryExplorerCore.Compression;
using LegendaryExplorerCore.Gammtek.Extensions;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics.Modules;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.ME3Tweaks.M3Merge.Bio2DATable;
using ME3TweaksCore.Misc;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ME3TweaksCore.Diagnostics
{
    /// <summary>
    /// Provides functionality for collecting application logs and game diagnostic information for upload to the ME3Tweaks Log Viewing service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The LogCollector class is responsible for:
    /// <list type="bullet">
    /// <item><description>Discovering and collecting application log files from the file system</description></item>
    /// <item><description>Gathering comprehensive game diagnostic information through modular diagnostic modules</description></item>
    /// <item><description>Compressing log data using LZMA compression for efficient transmission</description></item>
    /// <item><description>Uploading logs and diagnostics to ME3Tweaks servers for remote viewing and analysis</description></item>
    /// <item><description>Managing the Serilog logger lifecycle to safely access locked log files</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Thread Safety:</strong> This class is not thread-safe. Concurrent calls to log collection
    /// methods may result in race conditions, especially when managing the Serilog logger state through
    /// <see cref="Log.CloseAndFlush"/> and <see cref="CreateLogger"/>.
    /// </para>
    /// <para>
    /// <strong>Logger Management:</strong> Methods that collect logs (<see cref="CollectLogs"/> and 
    /// <see cref="CollectLatestLog"/>) temporarily close the active Serilog logger to release file locks,
    /// then recreate it using the <see cref="CreateLogger"/> delegate. The consuming application must
    /// initialize this delegate before calling any log collection methods.
    /// </para>
    /// <para>
    /// <strong>Diagnostic Modules:</strong> The <see cref="PerformDiagnostic"/> method executes a series of
    /// diagnostic modules in a predefined order to collect comprehensive game state information including:
    /// game info, system info, ASI mods, basegame files, installed DLC, texture files, TOC files, event logs,
    /// and more. Each module runs with exception handling to ensure partial diagnostics can still be collected
    /// if individual modules fail.
    /// </para>
    /// <para>
    /// <strong>Upload Service:</strong> Log uploads are sent to the ME3Tweaks unified log service endpoint. The service returns a URL for viewing
    /// uploaded logs. Data is compressed using LZMA before transmission to reduce bandwidth requirements.
    /// </para>
    /// </remarks>
    public class LogCollector
    {
        /// <summary>
        /// Gets a list of available log files in the configured log directory.
        /// </summary>
        /// <param name="activeLogPath">Optional. The full path to the currently active log file. If provided, the corresponding LogItem will have its <see cref="LogItem.IsActiveLog"/> property set to true.</param>
        /// <returns>A list of <see cref="LogItem"/> objects representing all .txt files found in the log directory.</returns>
        /// <remarks>
        /// This method searches for all files with a .txt extension in the directory returned by <see cref="MCoreFilesystem.GetLogDir"/>.
        /// The active log file (if specified) will be marked for special handling in the UI.
        /// </remarks>
        /// <exception cref="DirectoryNotFoundException">Thrown if the log directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown if the caller does not have permission to read the log directory.</exception>
        public static List<LogItem> GetLogsList(string activeLogPath = null)
        {
            var logs = Directory.GetFiles(MCoreFilesystem.GetLogDir(), @"*.txt");
            return logs.Select(x => new LogItem(x)
            {
                IsActiveLog = x == activeLogPath
            }).ToList();
        }

        /// <summary>
        /// Collects the contents of a specific application log file by temporarily closing the logger, reading the file, and then reopening the logger.
        /// </summary>
        /// <param name="logfile">The full path to the log file to collect.</param>
        /// <returns>The full text content of the log file, or null if an error occurred during reading.</returns>
        /// <remarks>
        /// <para>
        /// This method performs the following steps:
        /// <list type="number">
        /// <item><description>Logs a message indicating the logger is shutting down</description></item>
        /// <item><description>Closes and flushes the Serilog logger using <see cref="Log.CloseAndFlush"/></description></item>
        /// <item><description>Reads the entire contents of the specified log file</description></item>
        /// <item><description>Recreates the logger using the <see cref="CreateLogger"/> delegate</description></item>
        /// <item><description>Logs any errors that occurred during file reading</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// The logger is always recreated in the finally block, ensuring it is reinitialized even if an exception occurs.
        /// Any errors during file reading are logged after the logger is recreated.
        /// </para>
        /// <para>
        /// <strong>Important:</strong> The <see cref="CreateLogger"/> delegate must be initialized before calling this method,
        /// otherwise the logger will not be properly recreated.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown if logfile is null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown if the specified log file does not exist.</exception>
        /// <exception cref="IOException">Thrown if an I/O error occurs while reading the file.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown if the caller does not have permission to read the file.</exception>
        public static string CollectLogs(string logfile)
        {
            MLog.Information(@"Shutting down logger to allow application to pull log file.");
            Log.CloseAndFlush();
            string errorText = null;
            try
            {
                string log = File.ReadAllText(logfile);
                CreateLogger?.Invoke();
                return log;
            }
            catch (Exception e)
            {
                errorText = @"Could not read log file! " + e.Message;
                return null;
            }
            finally
            {
                CreateLogger?.Invoke();
                if (errorText != null)
                {
                    MLog.Error(errorText);
                }
            }
        }

        /// <summary>
        /// Gets or sets the delegate used to recreate the Serilog logger after it has been closed for log collection.
        /// </summary>
        /// <value>
        /// A function that returns an <see cref="ILogger"/> instance. This delegate is invoked by <see cref="CollectLogs"/>
        /// and <see cref="CollectLatestLog"/> after closing the logger to release file locks.
        /// </value>
        /// <remarks>
        /// <para>
        /// The consuming application must initialize this delegate during application startup before any log collection
        /// operations are performed. The delegate should create and configure a new Serilog logger instance.
        /// </para>
        /// <para>
        /// Example implementation:
        /// <code>
        /// LogCollector.CreateLogger = () => {
        ///     return new LoggerConfiguration()
        ///         .WriteTo.File(
        ///             Path.Combine(logDir, "app.log"),
        ///             rollingInterval: RollingInterval.Day,
        ///             retainedFileCountLimit: 14,
        ///             fileSizeLimitBytes: 10 * 1024 * 1024)
        ///         .CreateLogger();
        /// };
        /// </code>
        /// </para>
        /// </remarks>
        internal static Func<ILogger> CreateLogger { get; set; }




        /// <summary>
        /// Collects the most recent log file from the specified directory by closing the logger, reading the latest file by modification time, and optionally reopening the logger.
        /// </summary>
        /// <param name="logdir">The directory path containing log files to search.</param>
        /// <param name="restartLogger">If true, the logger will be recreated after reading the log file. If false, the logger remains closed.</param>
        /// <returns>The full text content of the most recent log file, or null if no log files exist or an error occurred during reading.</returns>
        /// <remarks>
        /// <para>
        /// This method:
        /// <list type="number">
        /// <item><description>Closes and flushes the Serilog logger</description></item>
        /// <item><description>Searches for all .txt files in the specified directory</description></item>
        /// <item><description>Identifies the most recent file based on <see cref="FileInfo.LastWriteTime"/></description></item>
        /// <item><description>Reads the entire contents of that file</description></item>
        /// <item><description>Optionally recreates the logger based on the restartLogger parameter</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// If an exception occurs while reading the log file, a fatal-level log message is written (after the logger is recreated)
        /// and null is returned. This allows the application to continue even if log collection fails.
        /// </para>
        /// <para>
        /// The restartLogger parameter is useful when collecting logs during application shutdown, where logger recreation
        /// may not be necessary or desired.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown if logdir is null or empty.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown if the specified directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown if the caller does not have permission to access the directory.</exception>
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

        /// <summary>
        /// Performs comprehensive diagnostic collection for a game installation and returns the diagnostic report as a formatted string.
        /// </summary>
        /// <param name="package">The <see cref="LogUploadPackage"/> containing the game target to diagnose and callbacks for progress updates.</param>
        /// <returns>A formatted diagnostic report string with all null terminators removed for server compatibility.</returns>
        /// <remarks>
        /// <para>
        /// This method executes a series of diagnostic modules in a specific order to collect various game information that is useful for troubleshooting.
        /// Each diagnostic module is executed with exception handling. If a module fails, the error is logged
        /// and added to the diagnostic output, but processing continues with remaining modules to ensure
        /// partial diagnostics can still be collected.
        /// </para>
        /// <para>
        /// Progress callbacks in the package are invoked throughout the diagnostic process to update the UI
        /// with current status information and taskbar progress state.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if package, package.DiagnosticWriter, or package.DiagnosticTarget is null.</exception>
        public static string PerformDiagnostic(LogUploadPackage package)
        {
            var diag = package.DiagnosticWriter;

            MLog.Information($@"Collecting diagnostics for target {package.DiagnosticTarget.TargetPath}");
            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_preparingToCollectDiagnosticInfo));
            package.UpdateTaskbarProgressStateCallback?.Invoke(MTaskbarState.Indeterminate);

            #region Diagnostic setup and diag header
            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingGameInformation));

            MLog.Information(@"Beginning to build diagnostic output");

            diag.AddDiagLine(package.DiagnosticTarget.Game.ToGameNum().ToString(), LogSeverity.GAMEID);
            if (package.SelectedSaveFilePath != null && File.Exists(package.SelectedSaveFilePath))
            {
                // This will allow server to locate the save file that is uploaded and tell user what it is 
                diag.AddDiagLine(MUtilities.CalculateHash(package.SelectedSaveFilePath) + @"|" + Path.GetFileName(package.SelectedSaveFilePath), LogSeverity.SAVE_FILE_HASH_NAME);
            }
            diag.AddDiagLine($@"{MLibraryConsumer.GetHostingProcessname()} {MLibraryConsumer.GetAppVersion()} Game Diagnostic");
            diag.AddDiagLine($@"Build date: {MLibraryConsumer.GetSigningDate()}");
            diag.AddDiagLine($@"ME3TweaksCore version: {MLibraryConsumer.GetLibraryVersion()}");
            diag.AddDiagLine($@"Diagnostic for {package.DiagnosticTarget.Game.ToGameName()}");
            diag.AddDiagLine($@"Diagnostic generated at {DateTime.Now.ToShortDateString()} {DateTime.Now.ToShortTimeString()}");
            #endregion

            // List of modules to run for diagnostics, in the order they appear in the report.
            var modules = new List<DiagModuleBase>()
                {
                    new DiagGameInfo(),
                    new DiagSystemInfo(),
                    new DiagASIInfo(),
                    new DiagBasegameFiles(),
                    new DiagInstalledDLC(),
                    new DiagLinkerPatches(),
                    new DiagBTPVerify(),
                    new DiagTFCInfo(),
                    new DiagMEM(), // This also checks for extended in it
                    new DiagTOC(),
                    new DiagEventLog(),
                    new DiagME1Logs(),
                    new DiagME3Logger(),
                    new DiagExtended(),
                };

            foreach (var module in modules)
            {
                try
                {
                    module.RunModule(package);
                }
                catch (Exception ex)
                {
                    MLog.Error($@"An exception occurred running diagnostic module {module.GetType().Name}: {ex.Message}");
                    diag.AddDiagLine($@"An exception occurred running diagnostic module {module.GetType().Name}: {ex.Message}", LogSeverity.ERROR);
                }
                finally
                {
                    // Run cleanup, always
                    module.PostRunModule(package);
                }
            }

            // We have to strip any null terminators or it will bork it on the server log viewer
            return diag.GetDiagnosticText().Replace("\0", @""); // do not localize
        }

        /// <summary>
        /// Gets the standard session start marker string used to differentiate application sessions in log files.
        /// </summary>
        /// <value>
        /// A string constant containing the session start marker. This value should not be modified as it is
        /// used by the server-side log viewer to identify and separate different application sessions.
        /// </value>
        /// <remarks>
        /// <para>
        /// This string should be written as the very first line when starting a new logging session in the application.
        /// The ME3Tweaks log viewer service uses this marker to parse and display logs by session, allowing users
        /// to navigate between different application runs within a single log file.
        /// </para>
        /// <para>
        /// <strong>Warning:</strong> Changing this value will break compatibility with the server-side log viewer's
        /// session detection logic.
        /// </para>
        /// </remarks>
        public static string SessionStartString { get; } = @"============================SESSION START============================";

        /// <summary>
        /// Asynchronously submits a diagnostic log package to the ME3Tweaks Log Viewing service for remote viewing and analysis.
        /// </summary>
        /// <param name="package">The <see cref="LogUploadPackage"/> containing diagnostic data, application logs, save files, and other attachments to upload.</param>
        /// <returns>A task that represents the asynchronous upload operation. The task result contains the same <see cref="LogUploadPackage"/> with the <see cref="LogUploadPackage.Response"/> property populated with either the viewing URL or an error message.</returns>
        /// <remarks>
        /// <para>
        /// The method handles various exception types:
        /// <list type="bullet">
        /// <item><description><see cref="AggregateException"/>: Unwraps the inner exception and provides its message</description></item>
        /// <item><description><see cref="FlurlHttpTimeoutException"/>: Provides a timeout-specific error message</description></item>
        /// <item><description>General exceptions: Provides the exception message, stripping verbose HTTP request body information</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if package is null.</exception>
        public static async Task<LogUploadPackage> SubmitDiagnosticLogAsync(LogUploadPackage package)
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

            package.FullLogText = logUploadText.ToString();

            if (package.UseLocalLogViewer)
            {
                MLog.Information(@"Returning log package without uploading.");
                return package;
            }

            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_compressingForUpload));
            var lzmalog = LZMA.CompressToLZMAFile(Encoding.UTF8.GetBytes(package.FullLogText));
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
                string responseString = await @"https://me3tweaks.com/modmanager/logservice/shared/logupload"
                    .PostUrlEncodedAsync(dictionary).ReceiveString();
                Uri uriResult;
                bool result = Uri.TryCreate(responseString, UriKind.Absolute, out uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
                if (result)
                {
                    //should be valid URL.
                    MLog.Information(@"Result from server for log upload: " + responseString);
                    package.Response = responseString;
                }
                else
                {
                    MLog.Error(@"Error uploading log. The server responded with: " + responseString);
                    package.Response = LC.GetString(LC.string_interp_serverRejectedTheUpload, responseString);
                }
            }
            catch (AggregateException e)
            {
                Exception ex = e.InnerException;
                string exmessage = ex.Message;
                package.Response = LC.GetString(LC.string_interp_logWasUnableToUpload, exmessage);
            }
            catch (FlurlHttpTimeoutException)
            {
                // FlurlHttpTimeoutException derives from FlurlHttpException; catch here only
                // if you want to handle timeouts as a special case
                MLog.Error(@"Request timed out while uploading log.");
                package.Response = LC.GetString(LC.string_interp_requestTimedOutUploading);

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

                package.Response = LC.GetString(LC.string_interp_logWasUnableToUpload, exmessage);
            }

            return package;
        }
    }
}

