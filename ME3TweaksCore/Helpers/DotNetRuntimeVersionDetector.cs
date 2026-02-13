using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.EventStream;
using ME3TweaksCore.Diagnostics;

// License: Do whatever you want with this.
namespace ME3TweaksCore.Helpers
{
    /// <summary>
    /// Class that can determine if a version of .NET Core is installed
    /// </summary>
    public class DotNetRuntimeVersionDetector
    {
        /// <summary>
        /// This is very windows specific
        /// </summary>
        /// <param name="desktopVersionsOnly">If it needs to filter to Windows Desktop versions only (WPF/Winforms).</param>
        /// <returns>List of versions matching the specified version</returns>
        public static async Task<Version[]> GetInstalledRuntimeVersions(bool desktopVersion)
        {
            try
            {
                var cmd = Cli.Wrap(@"dotnet.exe").WithArguments(@"--list-runtimes").WithValidation(CommandResultValidation.None);
                var runtimes = new List<Version>();
                await foreach (var cmdEvent in cmd.ListenAsync())
                {
                    switch (cmdEvent)
                    {
                        case StartedCommandEvent started:
                            break;
                        case StandardOutputCommandEvent stdOut:
                            if (string.IsNullOrWhiteSpace(stdOut.Text))
                            {
                                continue;
                            }

                            Version v = null;
                            if (stdOut.Text.StartsWith(@"Microsoft.NETCore.App") && !desktopVersion)
                            {
                                v = parseVersion(stdOut.Text);
                            }
                            else if (stdOut.Text.StartsWith(@"Microsoft.WindowsDesktop.App") && desktopVersion)
                            {
                                v = parseVersion(stdOut.Text);
                            }

                            if (v != null)
                            {
                                runtimes.Add(v);
                            }
                            break;
                        case StandardErrorCommandEvent stdErr:
                            break;
                        case ExitedCommandEvent exited:
                            break;
                    }
                }
                return runtimes.ToArray();
            }
            catch (Exception e)
            {
                MLog.Error($@"Error determining installed dotnet runtimes: {e.Message}");
                return Array.Empty<Version>();
            }

        }

        /// <summary>
        /// Parses the version number from the string. If the parse fails, it returns null.
        /// </summary>
        /// <param name="stdOutText"></param>
        /// <returns></returns>
        private static Version parseVersion(string stdOutText)
        {
            var split = stdOutText.Trim().Split(' ');

            // We do not check things like rc- or previews.
            if (Version.TryParse(split[1], out var v))
            {
                return v;
            }

            MLog.Warning($@".NET version string not supported: {split[1]}. It may be that this is not a production version which this code does not support.");
            return null;
        }
    }
}
