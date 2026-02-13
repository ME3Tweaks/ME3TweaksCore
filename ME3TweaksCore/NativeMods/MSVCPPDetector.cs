#pragma warning disable CA1416 // Validate platform compatibility

using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Objects;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ME3TweaksCore.NativeMods
{
    /// <summary>
    /// Provides methods to detect and install Microsoft Visual C++ Redistributables
    /// </summary>
    public class MSVCPP
    {
        private const string MSVC_REGISTRY_BASE = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes";
        public const string MSVC_DOWNLOAD_URL = @"https://aka.ms/vc14/vc_redist.x64.exe";
        private const int REQUIRED_MAJOR_VERSION = 14; // v145 corresponds to version 14.x
        private const int MINIMUM_REQUIRED_MINOR_VERSION = 50; // 50 is VS 2026 v145

        /// <summary>
        /// Detects if Microsoft Visual C++ 2015-2026 Redistributable (v145 or higher) x64 is installed
        /// </summary>
        /// <returns>True if v145 or higher is installed, false otherwise</returns>
        public static bool IsVCRedist2015To2026x64Installed()
        {
            try
            {
                MLog.Information(@"Checking for Microsoft Visual C++ 2015-2026 Redistributable (x64)");
                // Check both 64-bit and 32-bit registry views for x64 version
                var registryPaths = new[]
                {
                    RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
                    RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                };

                foreach (var baseKey in registryPaths)
                {
                    using (baseKey)
                    {
                        using var runtimeKey = baseKey.OpenSubKey(MSVC_REGISTRY_BASE);
                        if (runtimeKey != null)
                        {
                            // Check x64 version
                            using var x64Key = runtimeKey.OpenSubKey(@"x64");
                            if (x64Key != null)
                            {
                                var installed = x64Key.GetValue(@"Installed");
                                var version = x64Key.GetValue(@"Version");
                                var major = x64Key.GetValue(@"Major");
                                var minor = x64Key.GetValue(@"Minor");

                                if (installed != null && (int)installed == 1)
                                {
                                    if (major != null && minor != null)
                                    {
                                        int majorVersion = (int)major;
                                        int minorVersion = (int)minor;
                                        MLog.Information($@"Found MSVC++ x64 version: Major={majorVersion}, Minor={minorVersion}, Version={version}");

                                        if (majorVersion == REQUIRED_MAJOR_VERSION && minorVersion >= MINIMUM_REQUIRED_MINOR_VERSION)
                                        {
                                            MLog.Information($@"MSVC++ x64 v{majorVersion} is installed (meets v145/14.50 requirement)");
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                MLog.Warning(@"Microsoft Visual C++ 2015-2026 Redistributable (x64) v145 or higher is not installed");
                return false;
            }
            catch (Exception ex)
            {
                MLog.Error($@"Error checking for MSVC++ installation: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Downloads, installs, and verifies the latest Microsoft Visual C++ 2015-2026 Redistributable (x64).
        /// This is a blocking operation and should be called from a background thread.
        /// </summary>
        /// <param name="progressInfo">Optional ProgressInfo object for progress updates</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>True if installation was successful, false otherwise</returns>
        public static async Task<bool> DownloadAndInstallVCRedistx64Async(
            ProgressInfo progressInfo = null,
            CancellationToken cancellationToken = default)
        {
            string tempInstallerPath = null;

            try
            {
                MLog.Information(@"Starting download and installation of Microsoft Visual C++ 2015-2026 Redistributable (x64)");

                // Check if already installed
                if (IsVCRedist2015To2026x64Installed())
                {
                    MLog.Information(@"MSVC++ x64 is already installed at required version or higher");
                    return true;
                }

                // Create temp file path for installer
                tempInstallerPath = Path.Combine(Path.GetTempPath(), $@"vc_redist_x64_{Guid.NewGuid()}.exe");

                // Download the installer
                MLog.Information($@"Downloading MSVC++ redistributable from {MSVC_DOWNLOAD_URL}");
                var downloadSuccess = await DownloadInstallerAsync(MSVC_DOWNLOAD_URL, tempInstallerPath, progressInfo, cancellationToken);

                if (!downloadSuccess)
                {
                    MLog.Error(@"Failed to download MSVC++ redistributable installer");
                    return false;
                }

                if (!File.Exists(tempInstallerPath))
                {
                    MLog.Error(@"Downloaded installer file does not exist");
                    return false;
                }

                MLog.Information($@"Downloaded installer to {tempInstallerPath}");

                // Install silently with admin privileges
                MLog.Information(@"Installing MSVC++ redistributable (requires administrator privileges)");
                progressInfo.Value = 0;
                progressInfo.Indeterminate = true;
                progressInfo.Status = LC.GetString(LC.string_installingMicrosoftVisualCPPRedistributable);
                progressInfo.OnUpdate?.Invoke(progressInfo);
                var installSuccess = InstallVCRedist(tempInstallerPath);

                if (!installSuccess)
                {
                    MLog.Error(@"Failed to install MSVC++ redistributable");
                    return false;
                }

                // Verify installation
                MLog.Information(@"Verifying MSVC++ installation");
                if (IsVCRedist2015To2026x64Installed())
                {
                    MLog.Information(@"MSVC++ redistributable installed and verified successfully");
                    return true;
                }
                else
                {
                    MLog.Warning(@"MSVC++ installation completed but verification failed - it may require a system restart");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                MLog.Warning(@"MSVC++ installation was cancelled");
                return false;
            }
            catch (Exception ex)
            {
                MLog.Error($@"Error during MSVC++ installation: {ex.Message}");
                return false;
            }
            finally
            {
                // Clean up temp installer
                if (tempInstallerPath != null && File.Exists(tempInstallerPath))
                {
                    try
                    {
                        File.Delete(tempInstallerPath);
                        MLog.Information(@"Cleaned up temporary installer file");
                    }
                    catch (Exception ex)
                    {
                        MLog.Warning($@"Failed to delete temporary installer: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Downloads the installer from the specified URL to the destination path
        /// </summary>
        private static async Task<bool> DownloadInstallerAsync(
            string url,
            string destinationPath,
            ProgressInfo progressInfo,
            CancellationToken cancellationToken)
        {
            try
            {
                progressInfo.Status = LC.GetString(LC.string_downloadingMicrosoftVisualCPPRedistributable);
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;

                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                var totalBytesRead = 0L;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalBytesRead += bytesRead;

                    if (progressInfo != null && totalBytes.HasValue)
                    {
                        progressInfo.Value = (double)totalBytesRead / totalBytes.Value * 100.0f;
                        progressInfo.Indeterminate = false;
                        progressInfo.OnUpdate?.Invoke(progressInfo);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                MLog.Error($@"Error downloading MSVC++ installer: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Installs the MSVC++ redistributable using the downloaded installer
        /// </summary>
        private static bool InstallVCRedist(string installerPath)
        {

            try
            {
                // Arguments for silent installation:
                // /install = install mode
                // /quiet = no UI
                // /norestart = don't restart automatically
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = @"/install /quiet /norestart",
                    UseShellExecute = true,
                    Verb = @"runas", // Run as administrator
                    CreateNoWindow = true
                };


                MLog.Information($@"Starting installer: {installerPath} {startInfo.Arguments}");

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    MLog.Error(@"Failed to start installer process");
                    return false;
                }

                process.WaitForExit();

                var exitCode = process.ExitCode;
                MLog.Information($@"Installer exited with code: {exitCode}");

                // Exit codes:
                // 0 = success
                // 3010 = success, but restart required
                // 1638 = newer version already installed
                // 5100 = does not meet system requirements
                if (exitCode == 0 || exitCode == 3010 || exitCode == 1638)
                {
                    if (exitCode == 3010)
                    {
                        MLog.Warning(@"Installation successful but system restart is required");
                    }
                    else if (exitCode == 1638)
                    {
                        MLog.Information(@"A newer or same version is already installed");
                    }
                    return true;
                }

                MLog.Error($@"Installer failed with exit code: {exitCode}");
                return false;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // User cancelled UAC prompt or elevation was denied
                if (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
                {
                    MLog.Warning(@"Installation cancelled by user (UAC prompt declined)");
                }
                else
                {
                    MLog.Error($@"Error starting installer: {ex.Message}");
                }
                return false;
            }
            catch (Exception ex)
            {
                MLog.Error($@"Error running installer: {ex.Message}");
                return false;
            }
        }
    }
}
#pragma warning restore CA1416 // Validate platform compatibility
