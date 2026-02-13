using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using LegendaryExplorerCore.Compression;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Localization;
using Octokit;
using Serilog;

namespace ME3TweaksCore.Helpers.MEM
{
    /// <summary>
    /// Handles updates to MEM (NoGui)
    /// </summary>
    public class MEMNoGuiUpdater
    {
        /// <summary>
        /// Soak gates for MEM updates on stable channel
        /// </summary>
        private static int[] SoakThresholds = { 25, 75, 150, 350, 650 };
        /// <summary>
        /// Highest supported version of MEM (that is not soak testing)
        /// </summary>
        public static int HighestSupportedMEMVersion { get; set; } = 999; //Default to very high number
        /// <summary>
        /// The date the soak test version of MEM was started
        /// </summary>
        public static DateTime SoakStartDate { get; set; }

        /// <summary>
        /// Version of MEM That is currently being targeted for soak testing. After the amount of days (as indexes) has passed for SoakThreshholds, this will effectively become the main version.
        /// </summary>
        public static int SoakTestingMEMVersion { get; set; }

        /// <summary>
        /// Checks for and updates mem if necessary. This method is blocking.
        /// </summary>
        /// <returns>If MEM is now available after this call. Returns false if there's errors updating.</returns>
        public static bool UpdateMEM(bool classicMEM, bool bypassSoakGate, Action<long, long> downloadProgressChanged = null, Action<Exception> exceptionUpdating = null, Action<string> statusMessageUpdate = null, bool forceOnlineUpdate = false)
        {
            int memVersion = 0;
            var mempath = MCoreFilesystem.GetMEMNoGuiPath(classicMEM);
            var localMemExecutableExists = File.Exists(mempath);
            if (localMemExecutableExists)
            {
                // File exists
                memVersion = MEMIPCHandler.GetMemVersion(classicMEM);
            }

            try
            {
                MLog.Information(@"Checking for updates to MassEffectModderNoGui. The local version is " + memVersion);
                if (bypassSoakGate)
                {
                    MLog.Information(@"Beta mode enabled, will include prerelease builds");
                }
                var client = new GitHubClient(new ProductHeaderValue(@"METweaksCore"));
                var releases = client.Repository.Release.GetAll(@"MassEffectModder", @"MassEffectModder").Result;
                MLog.Information(@"Fetched MEMNOGui releases from github...");
                if (classicMEM && memVersion >= 500)
                {
                    // Force downgrade
                    MLog.Warning(@"The local MEMNoGui version is higher than the supported version. We are forcibly downgrading this client.");
                    memVersion = 0;
                }

                Release latestReleaseWithApplicableAsset = null;
                if (releases.Any())
                {
                    //The release we want to check is always the latest, so [0]
                    foreach (Release r in releases)
                    {
                        if (!bypassSoakGate && r.Prerelease)
                        {
                            // Beta only release
                            continue;
                        }

                        if (r.Assets.Count == 0)
                        {
                            // Release has no assets
                            continue;
                        }

                        int releaseNameInt = Convert.ToInt32(r.TagName);
                        if (ReleaseIsForGame(releaseNameInt, classicMEM)) // >= 500 is LE only
                        {
                            if (releaseNameInt > memVersion && getApplicableAssetForPlatform(r) != null)
                            {
                                ReleaseAsset applicableAsset = getApplicableAssetForPlatform(r);
                                // This is an update...
                                if (bypassSoakGate)
                                {
                                    // Use this update
                                    latestReleaseWithApplicableAsset = r;
                                    break;
                                }

                                // Check if this is the soak testing build
                                if (releaseNameInt == SoakTestingMEMVersion)
                                {
                                    var comparisonAge = SoakStartDate == default ? DateTime.Now - r.PublishedAt.Value : DateTime.Now - SoakStartDate;
                                    int soakTestReleaseAge = (comparisonAge).Days;
                                    if (soakTestReleaseAge >= SoakThresholds.Length)
                                    {
                                        MLog.Information(@"New MassEffectModderNoGui update is past soak period, accepting this release as an update");
                                        latestReleaseWithApplicableAsset = r;
                                        break;
                                    }

                                    int soakThreshold = SoakThresholds[soakTestReleaseAge];

                                    //Soak gating
                                    if (applicableAsset.DownloadCount > soakThreshold)
                                    {
                                        MLog.Information($@"New MassEffectModderNoGui update is soak testing and has reached the daily soak threshold of {soakThreshold}. This update is not applicable to us today, threshold will expand tomorrow.");
                                        continue;
                                    }
                                    else
                                    {
                                        MLog.Information(@"New MassEffectModderNoGui update is available and soaking, this client will participate in this soak test.");
                                        latestReleaseWithApplicableAsset = r;
                                        break;
                                    }
                                }

                                // Check if this build is approved for stable
                                if (!bypassSoakGate && releaseNameInt > HighestSupportedMEMVersion)
                                {
                                    MLog.Information(@"New MassEffectModderNoGui update is available, but is not yet approved for stable channel: " + releaseNameInt);
                                    continue;
                                }

                                if (releaseNameInt > memVersion)
                                {
                                    MLog.Information($@"New MassEffectModderNoGui update is available: {releaseNameInt}");
                                    latestReleaseWithApplicableAsset = r;
                                    break;
                                }
                            }
                            else if (releaseNameInt <= memVersion)
                            {
                                MLog.Information(@"Latest release that is available and has been approved for use is v" + releaseNameInt + @" - no update available for us");
                                break;
                            }
                        }
                    }

                    //No local version, no latest, but we have asset available somehwere
                    if (memVersion == 0 && latestReleaseWithApplicableAsset == null)
                    {
                        MLog.Information(@"MassEffectModderNoGui does not exist locally, and no applicable version can be found, force pulling latest from github");
                        latestReleaseWithApplicableAsset = releases.FirstOrDefault(x => getApplicableAssetForPlatform(x) != null);
                    }
                    else if (memVersion == 0 && latestReleaseWithApplicableAsset == null)
                    {
                        //No local version, and we have no server version
                        MLog.Error(@"Cannot pull a copy of MassEffectModderNoGui from server, could not find one with assets. ME3TweaksCore may not work properly!");
                        return false;
                    }
                    else if (memVersion == 0)
                    {
                        MLog.Information(@"MassEffectModderNoGui does not exist locally. Pulling a copy from Github.");
                    }

                    if (latestReleaseWithApplicableAsset != null)
                    {
                        ReleaseAsset asset = getApplicableAssetForPlatform(latestReleaseWithApplicableAsset);
                        MLog.Information(@"MassEffectModderNoGui update available: " + latestReleaseWithApplicableAsset.TagName);
                        //there's an update
                        var downloadClient = new WebClient();
                        downloadClient.Headers[@"Accept"] = @"application/vnd.github.v3+json";
                        downloadClient.Headers[@"user-agent"] = MCoreFilesystem.AppDataFolderName; // Use the appdata folder name as the user agent
                        string downloadPath = Path.Combine(MCoreFilesystem.GetTempDirectory(), @"MEM_Update" + Path.GetExtension(asset.BrowserDownloadUrl));
                        DownloadHelper.DownloadFile(new Uri(asset.BrowserDownloadUrl), downloadPath, (bytesReceived, totalBytes) =>
                        {
                            downloadProgressChanged?.Invoke(bytesReceived, totalBytes);
                        });

                        // Handle unzip code here.
                        statusMessageUpdate?.Invoke(LC.GetString(LC.string_extractingMassEffectModderNoGui));
                        if (Path.GetExtension(downloadPath) == @".7z")
                        {
                            var res = LZMA.ExtractSevenZipArchive(downloadPath, MCoreFilesystem.GetTempDirectory(), true);
                            if (!res)
                            {
                                MLog.Error(@"ERROR EXTRACTING 7z MASSEFFECMODDERNOGUI!!");
                                return false;
                            }

                            // Copy into place. 
                            var sourceFile = Path.Combine(MCoreFilesystem.GetTempDirectory(), @"MassEffectModderNoGui.exe");
                            if (File.Exists(MCoreFilesystem.GetMEMNoGuiPath(classicMEM))) File.Delete(MCoreFilesystem.GetMEMNoGuiPath(classicMEM));
                            File.Move(sourceFile, MCoreFilesystem.GetMEMNoGuiPath(classicMEM));
                        }
                        else if (Path.GetExtension(downloadPath) == @".zip")
                        {
                            var zf = ZipFile.OpenRead(downloadPath);
                            var zipEntry = zf.Entries.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.FullName) == @"MassEffectModderNoGui");
                            if (zipEntry != null)
                            {
                                if (File.Exists(MCoreFilesystem.GetMEMNoGuiPath(classicMEM))) File.Delete(MCoreFilesystem.GetMEMNoGuiPath(classicMEM));
                                using (var fs = File.OpenWrite(MCoreFilesystem.GetMEMNoGuiPath(classicMEM)))
                                {
                                    zipEntry.Open().CopyTo(fs);
                                }
#if LINUX
                                Utilities.MakeFileExecutable(Locations.MEMPath());
#endif
                                MLog.Information($@"Updated MassEffectModderNoGui to version {MEMIPCHandler.GetMemVersion(true)}");
                            }
                            else
                            {
                                MLog.Error(@"MassEffectModderNoGui file was not found in the archive!");
                                return false;
                            }
                        }
                    }
                    else
                    {
                        //up to date
                        MLog.Information(@"No updates for MassEffectModderNoGui are available");
                    }
                }
            }
            catch (Exception e)
            {
                MLog.Exception(e, @"An error occurred running MassEffectModderNoGui updater: ");
                if (forceOnlineUpdate || !localMemExecutableExists)
                {
                    exceptionUpdating?.Invoke(e);
                    return false;
                }

                // Just return if a local copy exists
                return localMemExecutableExists;
            }

            // OK
            return true;
        }

        /// <summary>
        /// MEM < 500 is for OT, >= 500 is LE.
        /// </summary>
        /// <param name="version"></param>
        /// <param name="classicMem"></param>
        /// <returns></returns>
        private static bool ReleaseIsForGame(int version, bool classicMem)
        {
            if (classicMem)
            {
                return version < 500;
            }
            else
            {
                return version >= 500;
            }
        }

        private static ReleaseAsset getApplicableAssetForPlatform(Release r)
        {
            foreach (var a in r.Assets)
            {
#if WINDOWS
                if (a.Name.StartsWith(@"MassEffectModderNoGui-v")) return a;
#elif LINUX
                if (a.Name.StartsWith(@"MassEffectModderNoGui-Linux-v")) return a;
#elif MACOS
                if (a.Name.StartsWith(@"MassEffectModderNoGui-macOS-v")) return a;
#endif
            }

            return null; //no asset for platform
        }

        private static void unzipMEMUpdate(object sender, AsyncCompletedEventArgs e)
        {

        }
    }
}
