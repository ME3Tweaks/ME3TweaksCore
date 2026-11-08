using LegendaryExplorerCore.Gammtek.Extensions;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc.ME3Tweaks;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.NativeMods.Interfaces;
using ME3TweaksCore.Services;
using ME3TweaksCore.Targets;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ME3TweaksCore.NativeMods
{
    /// <summary>
    /// Backend for ASI Management
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public static class ASIManager
    {
        public static readonly string CachedASIsFolder = Directory.CreateDirectory(Path.Combine(MCoreFilesystem.GetSharedME3TweaksDataFolder(), @"CachedASIs")).FullName;

        public static readonly string ManifestLocation = Path.Combine(CachedASIsFolder, @"manifest.xml");
        public static readonly string StagedManifestLocation = Path.Combine(CachedASIsFolder, @"manifest_staged.xml");

        public static readonly List<ASIMod> MasterME1ASIUpdateGroups = new List<ASIMod>();
        public static readonly List<ASIMod> MasterME2ASIUpdateGroups = new List<ASIMod>();
        public static readonly List<ASIMod> MasterME3ASIUpdateGroups = new List<ASIMod>();

        public static readonly List<ASIMod> MasterLE1ASIUpdateGroups = new List<ASIMod>();
        public static readonly List<ASIMod> MasterLE2ASIUpdateGroups = new List<ASIMod>();
        public static readonly List<ASIMod> MasterLE3ASIUpdateGroups = new List<ASIMod>();
        public static readonly List<ASIMod> MasterLMPASIUpdateGroups = new List<ASIMod>();
        public static readonly List<ASIMod> MasterLELauncherASIUpdateGroups = new List<ASIMod>();

        /// <summary>
        /// Returns an enumeration of all master update groups for all games
        /// </summary>
        public static IEnumerable<ASIMod> AllASIMods => MasterLE1ASIUpdateGroups.Concat(MasterLE2ASIUpdateGroups).Concat(MasterLE3ASIUpdateGroups).Concat(MasterLMPASIUpdateGroups).Concat(MasterLELauncherASIUpdateGroups).Concat(MasterME1ASIUpdateGroups).Concat(MasterME2ASIUpdateGroups).Concat(MasterME3ASIUpdateGroups);

        /// <summary>
        /// ASI Manager 
        /// </summary>
        public static readonly ASIManagerOptions Options = new ASIManagerOptions();

        /// <summary>
        /// Loads the ASI manifest. This should only be done at startup or when the online manifest is refreshed. ForceLocal only works if there is local ASI manifest present
        /// </summary>
        /// <param name="forceLocal">If the manifest should be force-loaded from the local cache</param>
        /// <param name="overrideThrottling">If the manifest should be updated regardless of content check throttle</param>
        /// <param name="preloadedManifestData">Preloaded data if it was loaded from something else, such as a combined manifest</param>
        public static void LoadManifest(bool forceLocal = false, bool overrideThrottling = false, string preloadedManifestData = null)
        {
            MLog.Information($@"Loading ASI manifest.");
            try
            {
                internalLoadManifest(forceLocal, overrideThrottling, preloadedManifestData);
            }
            catch (Exception e)
            {
                MLog.Error($@"Error loading ASI manifest: {e.Message}");
            }
        }

        private static void internalLoadManifest(bool forceLocal = false, bool overrideThrottling = false, string preloadedManifestData = null)
        {
            if (!forceLocal && preloadedManifestData != null)
            {
                var onlineManifest = preloadedManifestData.Trim();
                // Online in this case means that it was preloaded by combined
                // services which is only sourced from online.
                MLog.Information(@"Using online ASI manifest");
                try
                {
                    File.WriteAllText(StagedManifestLocation, onlineManifest);
                }
                catch (Exception e)
                {
                    MLog.Error(@"Error writing cached ASI manifest to disk: " + e.Message);
                }

                try
                {
                    ParseManifest(onlineManifest, true);
                    MLog.Information(@"Loaded online ASI manifest");
                    logManifestInfo();
                    return;
                }
                catch (Exception e)
                {
                    MLog.Error(@"Error parsing online ASI manifest: " + e.Message);
                    MLog.Warning(@"Falling back to cached ASI manifest");
                }
            }
            else if (forceLocal)
            {
                MLog.Information(@"Force local ASI manifest requested; skipping preloaded data");
            }
            else
            {
                MLog.Warning(@"Online ASI manifest not available; falling back to cached manifest");
            }

            if (File.Exists(ManifestLocation))
            {
                MLog.Information(@"Loading cached ASI manifest");
                try
                {
                    LoadManifestFromDisk(ManifestLocation, false);
                    MLog.Information(@"Loaded cached ASI manifest");
                    logManifestInfo();
                    return;
                }
                catch (Exception e)
                {
                    MLog.Exception(e, @"Error loading cached manifest: ");
                }
            }
            else
            {
                MLog.Warning(@"Cached ASI manifest is not available");
            }

            LoadEmbeddedManifest();
            logManifestInfo();
        }

        private static void LoadEmbeddedManifest()
        {
            MLog.Warning(@"Loading embedded ASI manifest as no on-disk or network based version could be used");
            var resource = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.NativeMods.CachedASI.asimanifest.xml");
            var embeddedManifest = new StreamReader(resource).ReadToEnd();
            ParseManifest(embeddedManifest, false);
        }

        private static void logManifestInfo()
        {
            MLog.Information(@"Loaded ASI information:");
            var masterGroups = new[]
            {
                MasterME1ASIUpdateGroups, MasterME2ASIUpdateGroups, MasterME3ASIUpdateGroups, MasterLE1ASIUpdateGroups,
                MasterLE2ASIUpdateGroups, MasterLE3ASIUpdateGroups, MasterLMPASIUpdateGroups, MasterLELauncherASIUpdateGroups
            };

            for (int i = 0; i < masterGroups.Length; i++)
            {
                MLog.Information($@"{GameNumConversion.FromGameNum(i + 1).ToGameName(true)} has ASI groups {string.Join(',', masterGroups[i].Select(x => x.UpdateGroupId))} available");
#if DEBUG
                foreach (var asiversions in masterGroups[i])
                {
                    MLog.Debug($@"ASI Update Group {asiversions.UpdateGroupId} IsHidden: {asiversions.IsHidden} ---------------");
                    foreach (var asi in asiversions.Versions)
                    {
                        MLog.Debug($@"   {asi.Name} v{asi.Version} IsBetaOnly: {asi.IsBeta}");
                    }
                }
#endif
            }
        }

        /// <summary>
        /// Loads the data using a ServiceLoader system. This is treated like an online fetch.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static bool LoadService(JToken data)
        {
            // The data is just the xml string.
            LoadManifest(overrideThrottling: data != null, preloadedManifestData: data?.ToObject<string>());
            return true; // ??
        }

        ///// <summary>
        ///// Extracts the default ASI assets from this assembly so there is a default set of cached assets that are alway required for proper program functionality
        ///// </summary>
        //public static void ExtractDefaultASIResources()
        //{
        //    string[] defaultResources = { @"BalanceChangesReplacer-v3.0.asi", @"ME1-DLC-ModEnabler-v1.0.asi", @"ME3Logger_truncating-v1.0.asi", @"AutoTOC_LE-v2.0.asi", @"LE1AutoloadEnabler-v1.0.asi", @"manifest.xml" };
        //    foreach (var file in defaultResources)
        //    {
        //        var outfile = Path.Combine(CachedASIsFolder, file);
        //        if (!File.Exists(outfile))
        //        {
        //            MUtilities.ExtractInternalFile(@"ME3TweaksModManager.modmanager.asi." + file, outfile, true);
        //        }
        //    }
        //}

        /// <summary>
        /// Calls ParseManifest() on the given file path.
        /// </summary>
        /// <param name="manifestPath"></param>
        /// <param name="games"></param>
        /// <param name="isStaged">If file is staged for copying the cached location</param>
        /// <param name="selectionStateUpdateCallback"></param>
        private static void LoadManifestFromDisk(string manifestPath, bool isStaged = false)
        {
            MLog.Information($@"Using ASI manifest from disk: {manifestPath}");
            ParseManifest(File.ReadAllText(manifestPath), isStaged);
        }

        /// <summary>
        /// Converts integer to MEGame
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        private static MEGame intToGame(int i)
        {
            switch (i)
            {
                case 1:
                    return MEGame.ME1;
                case 2:
                    return MEGame.ME2;
                case 3:
                    return MEGame.ME3;
                case 4:
                    return MEGame.LE1;
                case 5:
                    return MEGame.LE2;
                case 6:
                    return MEGame.LE3;
                case 7:
                    return MEGame.LELauncher;
                case 8:
                    return MEGame.LMP;
                default:
                    throw new Exception(LC.GetString(LC.string_interp_unsupportedGameIdInAsiManifest, i));
            }
        }

        /// <summary>
        /// Fetches the specified ASI by it's hash for the specified game
        /// </summary>
        /// <param name="hash">The MD5 to lookup</param>
        /// <param name="game">The game the ASI belongs to</param>
        /// <returns>The versioned ASI mod if found, null otherwise</returns>
        public static ASIModVersion GetASIVersionByHash(string hash, MEGame game)
        {
            List<ASIMod> relevantGroups = null;
            switch (game)
            {
                case MEGame.ME1:
                    relevantGroups = MasterME1ASIUpdateGroups;
                    break;
                case MEGame.ME2:
                    relevantGroups = MasterME2ASIUpdateGroups;
                    break;
                case MEGame.ME3:
                    relevantGroups = MasterME3ASIUpdateGroups;
                    break;
                case MEGame.LE1:
                    relevantGroups = MasterLE1ASIUpdateGroups;
                    break;
                case MEGame.LE2:
                    relevantGroups = MasterLE2ASIUpdateGroups;
                    break;
                case MEGame.LE3:
                    relevantGroups = MasterLE3ASIUpdateGroups;
                    break;
                case MEGame.LMP:
                    relevantGroups = MasterLMPASIUpdateGroups;
                    break;
                case MEGame.LELauncher:
                    relevantGroups = MasterLELauncherASIUpdateGroups;
                    break;
                default:
                    return null;
            }

            if (relevantGroups.Any())
            {
                return relevantGroups.FirstOrDefault(x => x.HasMatchingHash(hash))?.Versions.First(x => x.Hash == hash);
            }

            return null;
        }


        /// <summary>
        /// Fetches the specific ASI version by game.
        /// </summary>
        /// <param name="updateGroup">The update group of the ASI.</param>
        /// <param name="version">The version to fetch of the ASI</param>
        /// <param name="game">The game the ASI belongs to</param>
        /// <returns>The versioned ASI mod object, or null if not found.</returns>
        public static ASIModVersion GetASIVersion(int updateGroup, int version, MEGame game)
        {
            List<ASIMod> relevantGroups = null;
            switch (game)
            {
                case MEGame.ME1:
                    relevantGroups = MasterME1ASIUpdateGroups;
                    break;
                case MEGame.ME2:
                    relevantGroups = MasterME2ASIUpdateGroups;
                    break;
                case MEGame.ME3:
                    relevantGroups = MasterME3ASIUpdateGroups;
                    break;
                case MEGame.LE1:
                    relevantGroups = MasterLE1ASIUpdateGroups;
                    break;
                case MEGame.LE2:
                    relevantGroups = MasterLE2ASIUpdateGroups;
                    break;
                case MEGame.LE3:
                    relevantGroups = MasterLE3ASIUpdateGroups;
                    break;
                case MEGame.LMP:
                    relevantGroups = MasterLMPASIUpdateGroups;
                    break;
                case MEGame.LELauncher:
                    relevantGroups = MasterLELauncherASIUpdateGroups;
                    break;
                default:
                    return null;
            }

            if (relevantGroups.Any())
            {
                var group = relevantGroups.FirstOrDefault(x => x.UpdateGroupId == updateGroup);
                if (group != null)
                {
                    return group.Versions.FirstOrDefault(x => x.Version == version);
                }
            }

            return null;
        }

        /// <summary>
        /// Parses a string (xml) into an ASI manifest.
        /// </summary>
        private static void ParseManifest(string xmlText, bool isStaged = false)
        {
            bool reloadOnError = true;
            try
            {
                MasterME1ASIUpdateGroups.Clear();
                MasterME2ASIUpdateGroups.Clear();
                MasterME3ASIUpdateGroups.Clear();

                MasterLE1ASIUpdateGroups.Clear();
                MasterLE2ASIUpdateGroups.Clear();
                MasterLE3ASIUpdateGroups.Clear();
                MasterLMPASIUpdateGroups.Clear();
                MasterLELauncherASIUpdateGroups.Clear();
                XElement rootElement = XElement.Parse(xmlText.Trim());
                // Debug.WriteLine(rootElement.ToString());


                //I Love Linq
                var updateGroups = (from ugroup in rootElement.Elements(@"updategroup")
                                    select new ASIMod
                                    {
                                        UpdateGroupId = (int)ugroup.Attribute(@"groupid"),
                                        Game = intToGame((int)ugroup.Attribute(@"game")),
                                        Versions = ugroup.Elements(@"asimod").Select(version => new ASIModVersion
                                        {
                                            Name = (string)version.Element(@"name"),
                                            InstalledPrefix = (string)version.Element(@"installedname"),
                                            Author = (string)version.Element(@"author"),
                                            Version = TryConvert.ToInt32(version.Element(@"version")?.Value, 1), // This could hide errors pretty easily if something is wrong with ASI manifest!
                                            Description = (string)version.Element(@"description"),
                                            Hash = (string)version.Element(@"hash"),
                                            SourceCodeLink = (string)version.Element(@"sourcecode"),
                                            DownloadLink = (string)version.Element(@"downloadlink"),
                                            IsBeta = TryConvert.ToBoolFromInt(version.Element(@"beta")?.Value),
                                            Hidden = TryConvert.ToBoolFromInt(version.Element(@"hidden")?.Value),
                                            DevModeOnly = TryConvert.ToBoolFromInt(version.Element(@"devsonly")?.Value),
                                            Dependencies = version.Element(@"dependencies") != null ?
                                                (from dep in version.Element(@"dependencies").Elements(@"dependency")
                                                 select new ASIDependency
                                                 {
                                                     Filename = (string)dep.Element(@"filename"),
                                                     StorageFilename = (string)dep.Element(@"storagefilename"),
                                                     Filesize = TryConvert.ToInt32(dep.Element(@"size")?.Value, -1), // -1 will ensure validation always fails
                                                     Hash = (string)dep.Element(@"hash"),

                                                     ServerAssetCompressed = TryConvert.ToBoolFromInt(dep.Element(@"serverassetcompressed")?.Value), // If data is .lzma on server
                                                     CompressedFilesize = TryConvert.ToInt32(dep.Element(@"compressedsize")?.Value, -1), // -1 will ensure validation always fails
                                                     CompressedHash = (string)dep.Element(@"compressedhash"), // will be null if ServerAssetCompressed is false
                                                 }).ToArray()
                                                : new ASIDependency[] { },
                                            _otherGroupsToDeleteOnInstallInternal = version.Element(@"autoremovegroups")?.Value,

                                            Game = intToGame((int)ugroup.Attribute(@"game")), // use ugroup element to pull from outer group
                                        }).OrderBy(x => x.Version).ToList()
                                    }).ToList();

                // Add some transient ASIs for now
                // Todo: Put into server manifest
                // once we have unveiled things.

                // ME3 - Add Profile Dumper
                updateGroups.Add(new ASIMod()
                {
                    UpdateGroupId = 499,
                    Game = MEGame.ME3,
                    Versions = [
                        // V1
                        new ASIModVersion {
                            Name = @"ME3MPProfileDump",
                            InstalledPrefix="ME3MPProfileDump",
                            Author = "ME3Tweaks",
                            Version = 1,
                            Description = "ME3MPProfileDump is an ASI used by ME3Tweaks Mod Manager to convert your ME3 online profile to LMP's local multiplayer profile.",
                            Hash = "fbd140acfdb0c52de338cce41d12bced",
                            SourceCodeLink = "https://github.com/ME3Tweaks/ME3-ASI-Plugins/ME3ProfileDump",
                            DownloadLink = "https://me3tweaks.com/mods/asi/ME3ProfileDump-v1.0.asi",
                            IsBeta = false,
                            Hidden = true,
                            DevModeOnly = false,
                            Game = MEGame.ME3
                        },
                        // V2
                        new ASIModVersion {
                            Name = @"ME3MPProfileDump",
                            InstalledPrefix="ME3MPProfileDump",
                            Author = "ME3Tweaks",
                            Version = 2,
                            Description = "ME3MPProfileDump is an ASI used by ME3Tweaks Mod Manager to convert your ME3 online profile to LMP's local multiplayer profile.",
                            Hash = "0340c109608663f7bc048d00b3668a24",
                            SourceCodeLink = "https://github.com/ME3Tweaks/ME3-ASI-Plugins/ME3ProfileDump",
                            DownloadLink = "https://me3tweaks.com/mods/asi/ME3ProfileDump-v2.0.asi",
                            IsBeta = false,
                            Hidden = true,
                            DevModeOnly = false,
                            Game = MEGame.ME3
                        },
                    ]
                });


                // LELauncher - Add MilanLauncher
                updateGroups.Add(new ASIMod()
                {
                    UpdateGroupId = (int)ASIModUpdateGroupID.LEL_MilanLauncher,
                    Game = MEGame.LELauncher,
                    Versions = [
                        // V2
                        new ASIModVersion {
                            Name = @"Milan Launcher",
                            InstalledPrefix="MilanLauncher",
                            Author = "ME3Tweaks",
                            Version = 2,
                            Description = "Milan Launcher enables the Project Milan Launcher mod to detect and launch the LMP game installation that is generated by Project Milan.",
                            Hash = "65978d57b47cb8e521cdb413342eedb7",
                            SourceCodeLink = "https://github.com/ME3Tweaks/LexASIs/MilanLauncher",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LELMilanLauncher-v2.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LELauncher
                        },
                    ]
                });

                // Add LE3 ASIs that we want
                // This prevents duplication into the manifest
                // Once public we can have new flag for LMP cloning.
                updateGroups.Add(new ASIMod(updateGroups.Find(x => x.UpdateGroupId == ASIModIDs.LE3_LINKER_PRINTER), MEGame.LMP, 1202));
                updateGroups.Add(new ASIMod(updateGroups.Find(x => x.UpdateGroupId == ASIModIDs.LE3_STREAMING_LEVELS_HUD), MEGame.LMP, 1203));
                updateGroups.Add(new ASIMod(updateGroups.Find(x => x.UpdateGroupId == ASIModIDs.LE3_DEBUGLOGGER_DEV), MEGame.LMP, 1204));
                updateGroups.Add(new ASIMod(updateGroups.Find(x => x.UpdateGroupId == ASIModIDs.LE3_FUNCTION_LOGGER), MEGame.LMP, 1205));

                // Add MilanHost
                updateGroups.Add(new ASIMod()
                {
                    UpdateGroupId = (int)ASIModUpdateGroupID.LMP_MilanHost,
                    Game = MEGame.LMP,
                    Versions = [
                        // V1
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 1,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "9c8b72297757886998a21ab29194eccf",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/MilanHost.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V2 -8/12/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 2,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "9e8e5f91fcd28cc1f02b7e015c7c8ff2",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v2.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V3 -8/26/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 3,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "10ce2c82cbd7f014b8927b38f9b6a3ef",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v3.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V4 - 09/19/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 4,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "ae075215434a285e50d0a19c2235888e",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v4.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V5 - 09/26/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 5,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "f548550034bd111d17447ffaca95c67c",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v5.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V6 - 09/27/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 6,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "95c26ebdcded227455ad733dc025e555",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v6.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V7 - 10/04/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 7,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "4ffcbdcd226c5b184e564fc8778eaa75",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v7.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V8 - 10/09/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 8,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "1a00e15a6c0fd98cd184f59d45f6d4ff",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v8.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V9 - 10/13/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 9,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "7b892ab348bcce5d70880cc7f2430089",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v9.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V10 - 10/15/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 10,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "2fbc41c30d8bc75f74975219a88c757a",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v10.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V11 - 10/17/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 11,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "4ca0764cf8aa4bf7deb00385e510b817",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v11.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V12 - 10/18/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 12,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "2b99a6857b40b1a8a597b3202b95e049",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v12.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V13 - 10/21/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 13,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "f95f37a0c0aa0c7a9aac0f2782af52ec",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v13.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V14 - 10/26/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 14,
                            Description = "MilanHost adds required functionality to the game to allow Project Milan to work properly.",
                            Hash = "a3450024422bc00b3e6260a4c381c88e",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v14.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // Crash Fix
                        // V15 - 10/28/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 15,
                            Description = "MilanHost adds required functionality to the game to allow Legendary Edition Multiplayer to work properly.",
                            Hash = "8077327aaee26084990d7d5b54baedbd",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v15.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // MenuScreens, FINAL build mode
                        // V16 - 10/31/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 16,
                            Description = "MilanHost adds required functionality to the game to allow Legendary Edition Multiplayer to work properly. LMP will not boot without this ASI.",
                            Hash = "fa889645f0f31251d28a69e9b23a88d3",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v16.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V17 - 11/04/2026
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 17,
                            Description = "MilanHost adds required functionality to the game to allow Legendary Edition Multiplayer to work properly. LMP will not boot without this ASI.",
                            Hash = "99aee0742571caf0e5b312056de435f5",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v17.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V18 - 11/08/2026
                        // Origin name fix, Crash fix for FOV native extension
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 18,
                            Description = "MilanHost adds required functionality to the game to allow Legendary Edition Multiplayer to work properly. LMP will not boot without this ASI.",
                            Hash = "84ccdd8f385d4194294582b46243fa0d",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v18.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                        // V18 - 11/08/2026
                        // Persisted matchmaking settings, joining improvements
                        new ASIModVersion {
                            Name = @"MilanHost",
                            InstalledPrefix="MilanHost",
                            Author = "ME3Tweaks",
                            Version = 19,
                            Description = "MilanHost adds required functionality to the game to allow Legendary Edition Multiplayer to work properly. LMP will not boot without this ASI.",
                            Hash = "01c03389c4e0f23c725760f99f793367",
                            SourceCodeLink = "https://github.com/llamathings/MilanClientServer",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPMilanHost-v19.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        },
                    ]
                });

                // Add AutoTOC v3
                updateGroups.Add(new ASIMod()
                {
                    UpdateGroupId = (int)ASIModUpdateGroupID.LMP_AutoTOC,
                    Game = MEGame.LMP,
                    Versions = [
                        // V3 - 8/26/2026
                        new ASIModVersion {
                            Name = @"AutoTOC",
                            InstalledPrefix="AutoTOC",
                            Author = "ME3Tweaks",
                            Version = 3,
                            Description = "Autotoc description here.",
                            Hash = "3e06aa2c71fcf093c658cd321e9d606a",
                            SourceCodeLink = "https://github.com/ME3Tweaks/blahblahblah",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LEAutoTOC-v3.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        }
                    ]
                });

                // Add LMP Texture Override
                updateGroups.Add(new ASIMod()
                {
                    UpdateGroupId = (int)ASIModUpdateGroupID.LMP_TextureOverride,
                    Game = MEGame.LMP,
                    Versions = [
                        // V5 - 10/18/2026
                        new ASIModVersion {
                            Name = @"TextureOverride",
                            InstalledPrefix="TextureOverride",
                            Author = "ME3Tweaks",
                            Version = 5,
                            Description = "Texture Override description here.",
                            Hash = "016e6685ddd664bf89dcf4064f2c315e",
                            SourceCodeLink = "https://github.com/ME3Tweaks/LExASIs/tree/main/TextureOverride",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPTextureOverride-v5.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP
                        }
                    ]
                });

                // Add Discord Integration. Mod Manager installs it when Discord asks it to start
                // LMP for a join (me3tweaksmodmanager://lmplaunch).
                updateGroups.Add(new ASIMod()
                {
                    UpdateGroupId = (int)ASIModUpdateGroupID.LMP_DiscordIntegration,
                    Game = MEGame.LMP,
                    Versions = [
                        new ASIModVersion {
                            Name = @"Discord Integration",
                            InstalledPrefix = "LMPDiscordIntegration",
                            Author = "ME3Tweaks",
                            Version = 2,
                            Description = "Discord Integration shows your Legendary Multiplayer lobby on your Discord profile and lets friends join it through Discord.",
                            Hash = "5e6e21fc2afc44cad7afd74878cb623b",
                            SourceCodeLink = "https://github.com/ME3Tweaks/LExASIs/tree/main/DiscordIntegration",
                            DownloadLink = "https://me3tweaks.com/mods/asi/lmp/sdfa6sd9f89a7sdf897sg7gfsd4/LMPDiscordIntegration-v2.0.asi",
                            IsBeta = false,
                            Hidden = false,
                            DevModeOnly = false,
                            Game = MEGame.LMP,
                            Dependencies = [
                                new ASIDependency
                                {
                                    Filename = @"discord_partner_sdk.dll",
                                    StorageFilename = @"discord_partner_sdk_v1.dll",
                                    Filesize = 9602488,
                                    Hash = @"cd4513eec1296329f96834c69a5930d0",
                                    ServerAssetCompressed = true,
                                    CompressedFilesize = 3144437,
                                    CompressedHash = "b4916fc2aa84c5120577cf1fc897f766"
                                }
                            ]
                        },
                    ]
                });

                // Add DebugLogger
                //MasterLMPASIUpdateGroups.Add(new ASIMod()
                //{
                //    UpdateGroupId = 500,
                //    Game = MEGame.LMP,
                //    Versions = [new ASIModVersion {

                //    }]
                //});


                foreach (var v in updateGroups)
                {
                    if (v.LatestVersionIncludingHidden == null)
                        continue; // Group has no available ASIs - maybe new beta asi
#if DEBUG
                    Debug.WriteLine($@"Read {v.Game} ASI group {v.UpdateGroupId}: {v.LatestVersionIncludingHidden}. Beta: {v.LatestVersionIncludingHidden.IsBeta}");
#endif
                    // If all ASIs are hidden mark entire group as hidden.
                    // Should this be moved to ShouldShowInUI?
                    if (v.Versions.All(x => x.Hidden))
                    {
                        v.IsHidden = true;
                    }

                    switch (v.Game)
                    {
                        case MEGame.ME1:
                            MasterME1ASIUpdateGroups.Add(v);
                            break;
                        case MEGame.ME2:
                            MasterME2ASIUpdateGroups.Add(v);
                            break;
                        case MEGame.ME3:
                            MasterME3ASIUpdateGroups.Add(v);
                            break;
                        case MEGame.LE1:
                            MasterLE1ASIUpdateGroups.Add(v);
                            break;
                        case MEGame.LE2:
                            MasterLE2ASIUpdateGroups.Add(v);
                            break;
                        case MEGame.LE3:
                            MasterLE3ASIUpdateGroups.Add(v);
                            break;
                        case MEGame.LMP:
                            MasterLMPASIUpdateGroups.Add(v);
                            break;
                        case MEGame.LELauncher:
                            MasterLELauncherASIUpdateGroups.Add(v);
                            break;
                    }

                    // Linq (get it?) versions to parents
                    foreach (var m in v.Versions)
                    {
                        m.OwningMod = v;
                        m.OtherGroupsToDeleteOnInstall.Remove(v.UpdateGroupId); // Ensure we don't delete ourself on install
                    }
                }

                reloadOnError = false;
                if (isStaged)
                {
                    File.Copy(StagedManifestLocation, ManifestLocation, true); //this will make sure cached manifest is parsable.
                }
            }
            catch (Exception e)
            {
                if (isStaged && File.Exists(ManifestLocation) && reloadOnError)
                {
                    //try cached instead
                    LoadManifestFromDisk(ManifestLocation, false);
                    return;
                }
                if (!reloadOnError)
                {
                    return; //Don't rethrow exception as we did load the manifest still
                }

                throw new Exception(@"Error parsing the ASI Manifest: " + e.Message);
            }

        }

        /// <summary>
        /// Gets the full paths of the support files ASIs need that are present in the specified target, outside of the ASI folder.
        /// These are installed alongside ASIs, such as the VC++ runtime dlls that are required under Wine.
        /// </summary>
        /// <param name="target">Target to check</param>
        /// <returns>Full paths of support files that exist in the target. Empty if there are none.</returns>
        public static List<string> GetASISupportFiles(GameTarget target)
        {
            if (!WineWorkarounds.WineDetected)
            {
                return new List<string>();
            }

            return ASIWine.GetVC145TargetFilePaths(target).Where(File.Exists).ToList();
        }

        /// <summary>
        /// Installs the specific version of an ASI to the specified target
        /// </summary>
        /// <param name="target"></param>
        /// <param name="forceSource">Null to let application choose the source, true to force online, false to force local cache. This parameter is used for testing</param>
        /// <returns></returns>
        public static async Task<bool> InstallASIToTarget(ASIModVersion asi, GameTarget target, bool? forceSource = null)
        {
            if (asi.Game != target.Game) throw new Exception($@"ASI {asi.Name} cannot be installed to game {target.Game}");
            MLog.Information($@"Processing ASI installation request: {asi.Name} v{asi.Version} -> {target.TargetPath}");
            string destinationFilename = $@"{asi.InstalledPrefix}-v{asi.Version}.asi";
            string cachedPath = Path.Combine(CachedASIsFolder, destinationFilename);
            string destinationDirectory = M3Directories.GetASIPath(target);
            if (!Directory.Exists(destinationDirectory))
            {
                MLog.Information(@"Creating ASI directory in game: " + destinationDirectory);
                Directory.CreateDirectory(destinationDirectory);
            }
            string finalPath = Path.Combine(destinationDirectory, destinationFilename);

            // 02/07/2026 - Install VC++ 14.50 dlls if on linux
            if (WineWorkarounds.WineDetected)
            {
                if (await ASIWine.EnsureVC145ZipDownloadedAsync())
                {
                    await ASIWine.ExtractVC145ToTargetAsync(target);
                }
                else
                {
                    // Required dlls are missing for ASIs that do logging.........
                    MLog.Error($@"Required dlls for ASIs to work could not be downloaded. The file should be downloaded from {ASIWine.VC145_DOWNLOAD_URL} and placed at {ASIWine.GetVC145ZipPath()}");
                }
            }

            var installedASIs = target.GetInstalledASIs();
            // Delete existing ASIs from the same group to ensure we don't install the same mod
            var existingSameGroupMods = installedASIs.OfType<IKnownInstalledASIMod>().Where(x => x.AssociatedManifestItem.OwningMod == asi.OwningMod).ToList();
            bool hasExistingVersionOfModInstalled = false;
            if (existingSameGroupMods.Any())
            {
                foreach (var v in existingSameGroupMods)
                {
                    if (v.Hash == asi.Hash && !forceSource.HasValue && !hasExistingVersionOfModInstalled) //If we are forcing a source, we should always install. Delete duplicates past the first one
                    {
                        MLog.Information($@"{v.AssociatedManifestItem.Name} is already installed. We will not remove the existing correct installed ASI for this install request");
                        hasExistingVersionOfModInstalled = true;
                        continue; //Don't delete this one. We are already installed. There is no reason to install it again.
                    }
                    MLog.Information($@"Deleting existing ASI from same group: {v.InstalledPath}");
                    v.Uninstall();
                    installedASIs.Remove(v);
                }
            }

            // Remove any conflicting ASIs
            foreach (var v in installedASIs.OfType<IKnownInstalledASIMod>())
            {
                if (asi.OtherGroupsToDeleteOnInstall.Contains(v.AssociatedManifestItem.OwningMod.UpdateGroupId))
                {
                    // Delete tis other ASI
                    v.Uninstall();
                }
            }

            if (hasExistingVersionOfModInstalled)
            {
                // The ASI was already installed. Its dependencies may still be missing or replaced; this only copies ones that are.
                await asi.InstallDependencies(target);
                return true;
            }

            if (asi.DownloadLink == null)
            {
                // Listed before it is uploaded, so there is nothing to install yet.
                MLog.Error($@"ASI {asi.Name} v{asi.Version} has no download link yet; it cannot be installed");
                return false;
            }

            // Install the ASI
            //if (forceSource == null || forceSource.Value == false)
            //{
            //    Debug.WriteLine("Hit me");
            //}
            string md5;
            bool useLocal = forceSource == false;
            if (!useLocal && !forceSource.HasValue)
            {
                // Do not use local was set, but no preference of where source was set, so we will see if it exists in cache
                useLocal = File.Exists(cachedPath);
            }
            if (useLocal)
            {
                //Check hash first
                md5 = MUtilities.CalculateHash(cachedPath);
                if (md5 == asi.Hash)
                {
                    MLog.Information($@"Copying ASI from cached library to destination: {cachedPath} -> {finalPath}");

                    File.Copy(cachedPath, finalPath, true);
                    MLog.Information($@"Installed ASI to {finalPath}");
                    TelemetryInterposer.TrackEvent(@"Installed ASI", new Dictionary<string, string>() {
                        { @"Filename", Path.GetFileNameWithoutExtension(finalPath)}
                    });

                    await asi.InstallDependencies(target);

                    return true;
                }
            }

            if (!forceSource.HasValue || forceSource.Value)
            {
                var usingEmbedded = false;
                var downloadStream = new MemoryStream();


                // Is this a locally embedded ASI?
                var fname = MUtilities.GetFileNameFromUrl(asi.DownloadLink);
                if (fname != null)
                {
                    var embeddedAssetPath = $@"ME3TweaksCore.NativeMods.CachedASI.{asi.Game}.{destinationFilename}";

                    if (asi.OwningMod.UpdateGroupId is 29 or 30 or 31) // AutoTOC for LE is the same across all games 
                    {
                        embeddedAssetPath = $@"ME3TweaksCore.NativeMods.CachedASI.{destinationFilename}";
                    }

                    var existsEmbedded = MUtilities.DoesEmbeddedAssetExist(embeddedAssetPath);
                    if (existsEmbedded)
                    {
                        downloadStream = MUtilities.ExtractInternalFileToStream(embeddedAssetPath);

                        if (MUtilities.CalculateHash(downloadStream) != asi.Hash)
                        {
                            MLog.Error(@"Embedded ASI hash does not match manifest, we will discard the embedded ASI data");
                            downloadStream = new MemoryStream();
                        }
                        else
                        {
                            usingEmbedded = true;
                        }
                    }
                }

                try
                {
                    if (!usingEmbedded)
                    {
                        // Online download
                        MLog.Information(@"Fetching remote ASI from server");
                        var request = WebRequest.Create(asi.DownloadLink);

                        using WebResponse response = request.GetResponse();
                        response.GetResponseStream().CopyTo(downloadStream);
                    }

                    // Online download security check
                    if (!usingEmbedded)
                    {
                        md5 = MUtilities.CalculateHash(downloadStream);
                        if (md5 != asi.Hash)
                        {
                            //ERROR!
                            MLog.Error(@"Downloaded ASI did not match the manifest! It has the wrong hash.");
#if DEBUG
                            MLog.Warning($@"Downloaded hash: {md5} Manifest hash: {asi.Hash}");
#endif
                            return false;
                        }

                        MLog.Information(@"Fetched remote ASI from server.");
                    }

                    MLog.Information($@"Installing ASI to {finalPath}");

                    downloadStream.WriteToFile(finalPath);
                    MLog.Information(@"ASI successfully installed.");
                    TelemetryInterposer.TrackEvent(@"Installed ASI", new Dictionary<string, string>()
                    {
                        {@"Filename", Path.GetFileNameWithoutExtension(finalPath)}
                    });

                    //Cache ASI
                    if (!Directory.Exists(CachedASIsFolder))
                    {
                        MLog.Information(@"Creating cached ASIs folder");
                        Directory.CreateDirectory(CachedASIsFolder);
                    }

                    MLog.Information(@"Caching ASI to local ASI library: " + cachedPath);
                    downloadStream.WriteToFile(cachedPath);

                    await asi.InstallDependencies(target);

                    return true;
                }
                catch (Exception e)
                {
                    MLog.Error($@"Error downloading ASI from {asi.DownloadLink}: {e.Message}");
                }
            }

            // We could not install the ASI
            return false;
        }

        /// <summary>
        /// Installs an ASI to the specified target. If there is an existing version installed, it is updated. If no version is specified, the latest version is installed.
        /// </summary>
        /// <param name="mod">The ASI mod to install</param>
        /// <param name="target">The target to install to</param>
        /// <param name="version">The version to install. The default is 0, which means the latest</param>
        /// <param name="includeHiddenASIVersions">If hidden ASI mod versions should be included.</param>
        /// <returns></returns>
        public static async Task<bool> InstallASIToTarget(ASIMod mod, GameTarget target, int version = 0, bool includeHiddenASIVersions = false)
        {
            ASIModVersion modV = null;
            if (version == 0)
            {
                modV = includeHiddenASIVersions ? mod.LatestVersionIncludingHidden : mod.LatestVersion;
            }
            else
            {
                modV = mod.Versions.FirstOrDefault(x => x.Version == version);
            }

            if (modV == null)
                return false; // Did not install

            return await InstallASIToTarget(modV, target);
        }

        /// <summary>
        /// Installs the specified ASI (by update group ID) to the target. If a version is not specified, the latest version is installed.
        /// </summary>
        /// <param name="updateGroup"></param>
        /// <param name="nameForLogging"></param>
        /// <param name="gameTarget"></param>
        /// <param name="version">The versio nto install. The default is 0, which means the latest</param>
        public static async Task<bool> InstallASIToTargetByGroupID(ASIModUpdateGroupID updateGroup, string nameForLogging, GameTarget gameTarget, int version = 0, bool includeHiddenASIs = false)
        {
            return await InstallASIToTargetByGroupID((int)updateGroup, nameForLogging, gameTarget, version, includeHiddenASIs);
        }

        /// <summary>
        /// Installs the specified ASI (by update group ID) to the target. If a version is not specified, the latest version is installed.
        /// </summary>
        /// <param name="updateGroup"></param>
        /// <param name="nameForLogging"></param>
        /// <param name="gameTarget"></param>
        /// <param name="version">The versio nto install. The default is 0, which means the latest</param>
        public static async Task<bool> InstallASIToTargetByGroupID(int updateGroup, string nameForLogging, GameTarget gameTarget, int version = 0, bool includeHiddenASIs = false)
        {
            var group = GetASIModsByGame(gameTarget.Game).FirstOrDefault(x => x.UpdateGroupId == updateGroup);
            if (group == null)
            {
                // Cannot find ASI!
                MLog.Error($@"Cannot find ASI ({nameForLogging}) with update group ID {updateGroup} for game {gameTarget.Game}");
                return false;
            }

            return await InstallASIToTarget(group, gameTarget, version, includeHiddenASIs);
        }

        /// <summary>
        /// Gets the list of ASI mods by game from the manifest
        /// </summary>
        /// <param name="game"></param>
        /// <returns></returns>
        public static List<ASIMod> GetASIModsByGame(MEGame game)
        {
            switch (game)
            {
                case MEGame.ME1:
                    return MasterME1ASIUpdateGroups;
                case MEGame.ME2:
                    return MasterME2ASIUpdateGroups;
                case MEGame.ME3:
                    return MasterME3ASIUpdateGroups;
                case MEGame.LE1:
                    return MasterLE1ASIUpdateGroups;
                case MEGame.LE2:
                    return MasterLE2ASIUpdateGroups;
                case MEGame.LE3:
                    return MasterLE3ASIUpdateGroups;
                case MEGame.LMP:
                    return MasterLMPASIUpdateGroups;
                case MEGame.LELauncher:
                    return MasterLELauncherASIUpdateGroups;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Retreives an ASI mod version via the game, group, and optionally the version.
        /// </summary>
        /// <param name="game"></param>
        /// <param name="updateGroup"></param>
        /// <param name="asiModVersion"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static ASIModVersion GetASIModVersion(MEGame game, int updateGroup, int? asiModVersion = null, bool includeHidden = false)
        {
            var mods = GetASIModsByGame(game);
            var group = mods.FirstOrDefault(x => x.UpdateGroupId == updateGroup);
            if (group == null)
            {
                MLog.Warning($@"Unable to find requested ASI mod updategroup: Game: {game}, UpdateGroup: {updateGroup}");
                return null;
            }

            ASIModVersion result = null;
            if (asiModVersion != null)
            {
                result = group.Versions.FirstOrDefault(x => x.Version == asiModVersion);
            }
            else
            {
                result = includeHidden ? group.LatestVersionIncludingHidden : group.LatestVersion;
            }
            if (result == null)
            {
                MLog.Warning($@"Unable to find requested ASI mod version in updategroup: Game: {group}, UpdateGroup: {updateGroup}, Version: {asiModVersion?.ToString() ?? "(Latest)"}");
            }
            return result;

        }

        /// <summary>
        /// Shared method for verifying dependencies of a known ASI mod
        /// </summary>
        /// <param name="associatedManifestItem"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static bool VerifyDependenciesShared(ASIModVersion associatedManifestItem, GameTarget target)
        {
            if (associatedManifestItem != null && associatedManifestItem.Dependencies != null && associatedManifestItem.Dependencies.Length > 0)
            {
                foreach (var a in associatedManifestItem.Dependencies)
                {
                    if (!a.IsInstalled(target))
                    {
                        // Dependency is missing.
                        MLog.Warning($@"ASI {associatedManifestItem.Name} v{associatedManifestItem.Version} is installed but is missing/has incorrect dependency {a.Filename}");
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
