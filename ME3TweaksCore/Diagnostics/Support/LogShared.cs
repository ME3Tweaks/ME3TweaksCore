using ME3TweaksCore.Misc;
using ME3TweaksCore.Services.ThirdPartyModIdentification;
using ME3TweaksCore.Targets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ME3TweaksCore.Diagnostics.Support
{
    public static class LogShared
    {
        /// <summary>
        /// Closing tag for a collapsable subsection
        /// </summary>
        public const string END_SUB = @"[/SUB]";

    }
    /// <summary>
    /// Package of options for what to collect in a diagnostic/log upload.
    /// </summary>
    public class LogUploadPackage
    {
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
        /// If the advanced diagnostics module should be run or not.
        /// </summary>
        public bool AdvancedDiagnosticsEnabled { get; set; }

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

        /// <summary>
        /// Writer for the game diagnostic.
        /// </summary>
        public DiagWriter DiagnosticWriter { get; } = new DiagWriter();

        /// <summary>
        /// The response from the server - will either be a URL starting with https://, or an error message.
        /// </summary>
        public string Response { get; internal set; }

        /// <summary>
        /// The full generated log text
        /// </summary>
        public string FullLogText { get; internal set; }

        /// <summary>
        /// If diag/log should be compiled but not uploaded
        /// </summary>
        public bool UseLocalLogViewer { get; set; }
    }

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


    /// <summary>
    /// Contains information about an installed DLC
    /// </summary>
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
            void addDataAttr(string attrName, object attrValue)
            {
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
            string rowClass = @"row-mod";
            if (IsOfficialDLC)
            {
                rowClass = @"row-officialdlc";
                addCell(DLCFolderName);
                addCell(ModName); // Set when loading the struct object
                addCell(@""); // Version
                addCell(MountPriority.ToString(), true);
                addCell(@"BioWare");
                addCell(@"");
                addCell(@"");
                addCell(@"");
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
                var options = InstalledOptions != null ? string.Join(@"", InstalledOptions.Select(x => $@"<p class=""install-option"">{x}</p>")) : @"";
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