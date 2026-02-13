using LegendaryExplorerCore.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace ME3TweaksCore.Diagnostics.Support
{
    /// <summary>
    /// Class that stores the diagnostic as it is being built. This doesn't include the application log.
    /// </summary>
    public class DiagWriter
    {
        /// <summary>
        /// The string builder we are writing to
        /// </summary>
        private StringBuilder diagStringBuilder;

        private Lock writeLock = new Lock();

        public DiagWriter()
        {
            diagStringBuilder = new StringBuilder();
        }

        /// <summary>
        /// Gets the output of the diagnostic writer.
        /// </summary>
        /// <returns></returns>
        public string GetDiagnosticText() => diagStringBuilder.ToString();

        /// <summary>
        /// Adds a new diagnostic line to this builder. This method is thread safe.
        /// </summary>
        /// <param name="message">The message to add</param>
        /// <param name="sev">The severity code.</param>
        public void AddDiagLine(string message = "", LogSeverity sev = LogSeverity.INFO)
        {
            lock (writeLock)
            {
                switch (sev)
                {
                    case LogSeverity.INFO:
                        diagStringBuilder.Append(message);
                        break;
                    case LogSeverity.WARN:
                        diagStringBuilder.Append($@"[WARN]{message}");
                        break;
                    case LogSeverity.ERROR:
                        diagStringBuilder.Append($@"[ERROR]{message}");
                        break;
                    case LogSeverity.FATAL:
                        diagStringBuilder.Append($@"[FATAL]{message}");
                        break;
                    case LogSeverity.DIAGSECTION:
                        diagStringBuilder.Append($@"[DIAGSECTION]{message}");
                        break;
                    case LogSeverity.GOOD:
                        diagStringBuilder.Append($@"[GREEN]{message}");
                        break;
                    case LogSeverity.BOLD:
                        diagStringBuilder.Append($@"[BOLD]{message}");
                        break;
                    case LogSeverity.BOLDBLUE:
                        diagStringBuilder.Append($@"[BOLDBLUE]{message}");
                        break;
                    case LogSeverity.DLC:
                        diagStringBuilder.Append($@"[DLC]{message}");
                        break;
                    case LogSeverity.OFFICIALDLC:
                        diagStringBuilder.Append($@"[OFFICIALDLC]{message}");
                        break;
                    case LogSeverity.GAMEID:
                        diagStringBuilder.Append($@"[GAMEID]{message}");
                        break;
                    case LogSeverity.TPMI:
                        diagStringBuilder.Append($@"[TPMI]{message}");
                        break;
                    case LogSeverity.SUB:
                        diagStringBuilder.Append($@"[SUB]{message}");
                        break;
                    case LogSeverity.SUPERCEDANCE_FILE:
                        diagStringBuilder.Append($@"[SSF]{message}");
                        break;
                    case LogSeverity.SAVE_FILE_HASH_NAME:
                        diagStringBuilder.Append($@"[SF]{message}");
                        break;
                    case LogSeverity.NOPRE:
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
        }

        /// <summary>
        /// Adds multiple diagnostic lines all with the same severity code.
        /// </summary>
        /// <param name="strings"></param>
        /// <param name="sev"></param>
        public void AddDiagLines(IEnumerable<string> strings, LogSeverity sev = LogSeverity.INFO)
        {
            foreach (var s in strings)
            {
                AddDiagLine(s, sev);
            }
        }
    }
}
