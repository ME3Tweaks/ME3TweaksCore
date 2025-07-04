using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using LegendaryExplorerCore.Compression;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Misc;
using ME3TweaksCore.NativeMods;
using ME3TweaksCore.NativeMods.Interfaces;
using ME3TweaksCore.Objects;
using ME3TweaksCore.Services;
using ME3TweaksCore.Services.Backup;
using ME3TweaksCore.Services.Shared.BasegameFileIdentification;

namespace ME3TweaksCore.Targets
{
    /// <summary>
    /// GameTarget is the main class for interacting with an installation of a Mass Effect game.
    /// </summary>
    [DebuggerDisplay(@"GameTarget {Game} {TargetPath}")]
    public class GameTarget : IEqualityComparer<GameTarget>, INotifyPropertyChanged
    {
        public const uint MEMI_TAG = 0x494D454D;

        // Used in UIs determining game source
        public const string GAME_SOURCE_EA_APP = @"EA App";
        public const string GAME_SOURCE_STEAM = @"Steam";
        public const string GAME_SOURCE_DVD = @"DVD";

        public MEGame Game { get; }
        public string TargetPath { get; }
        public bool RegistryActive { get; set; }
        public string GameSource { get; private set; }
        public string ExecutableHash { get; private set; }

        public string ASILoaderName
        {
            get
            {
                if (Game == MEGame.ME1) return LC.GetString(LC.string_binkw32ASILoader);
                if (Game is MEGame.ME2 or MEGame.ME3) return LC.GetString(LC.string_binkw32ASIBypass);
                if (Game.IsLEGame()) return LC.GetString(LC.string_bink2w64ASILoader);
                return $@"UNKNOWN GAME FOR ASI LOADER: {Game}";
            }
        }
        public bool Supported => GameSource != null;
        /// <summary>
        /// If this is the polish build of ME1
        /// </summary>
        public bool IsPolishME1 { get; private set; }


        public string ALOTVersion { get; private set; }
        /// <summary>
        /// Indicates that this is a custom, abnormal game object. It may be used only for UI purposes, but it depends on the context.
        /// </summary>
        public bool IsCustomOption { get; set; } = false;

        /// <summary>
        /// Initializes a game target
        /// </summary>
        /// <param name="game">Game this target represents</param>
        /// <param name="targetRootPath">The root path of the target</param>
        /// <param name="currentRegistryActive">If this path is the current 'active' registry target</param>
        /// <param name="isCustomOption">If this is a custom fake game target but is used in a target list</param>
        /// <param name="isTest">If this is a test object</param>
        /// <param name="skipInit">If we should skip initialization when loading the target data</param>
        public GameTarget(MEGame game, string targetRootPath, bool currentRegistryActive, bool isCustomOption = false, bool isTest = false, bool skipInit = false)
        {
            //if (!currentRegistryActive)
            //    Debug.WriteLine("hi");
            this.Game = game;
            this.RegistryActive = currentRegistryActive;
            this.IsCustomOption = isCustomOption;
            this.TargetPath = targetRootPath.TrimEnd('\\');
            MemoryAnalyzer.AddTrackedMemoryItem($@"{game} GameTarget {TargetPath} - IsCustomOption: {isCustomOption}", new WeakReference(this));
            ReloadGameTarget(!isTest, skipInit: skipInit);

            if (Game.IsLEGame())
            {
                OodleHelper.EnsureOodleDll(TargetPath, MCoreFilesystem.GetDllDirectory());
            }
        }


        public virtual void ReloadGameTarget(bool logInfo = true, bool forceLodUpdate = false, bool reverseME1Executable = true, bool skipInit = false)
        {
            if (!IsCustomOption && !skipInit)
            {
                if (Directory.Exists(TargetPath))
                {
                    MLog.Information(@"Getting game source for target " + TargetPath, logInfo);
                    var hashCheckResult = VanillaDatabaseService.GetGameSource(this, reverseME1Executable);

                    GameSource = hashCheckResult.result;
                    ExecutableHash = hashCheckResult.hash;

                    if (ExecutableHash.Length != 32)
                    {
                        MLog.Error($@"Issue getting game source: {ExecutableHash}", shouldLog: logInfo);
                    }
                    else
                    {

                        if (GameSource == null)
                        {
                            // No source is listed
                            MLog.Error(@"Unknown source or illegitimate installation: " + hashCheckResult.hash,
                                shouldLog: logInfo);
                        }
                        else
                        {
                            if (GameSource.Contains(GAME_SOURCE_EA_APP) && (Game is MEGame.ME3 or MEGame.LELauncher || Game.IsLEGame()))
                            {
                                // Check for steam
                                var testPath = Game == MEGame.ME3 ? TargetPath : Directory.GetParent(TargetPath).FullName;
                                if (Game != MEGame.ME3)
                                {
                                    // This looks for __overlay folder; LE games use it in Legendary Edition root, above game target paths, so we go up folder
                                    var parent = Directory.GetParent(testPath);
                                    if (parent != null)
                                    {
                                        testPath = parent.FullName;
                                    }
                                    else
                                    {
                                        MLog.Error(@"Executable is not in the correct filesystem hierarchy (parent folder that should exist does not); this is not a valid installation", shouldLog: logInfo);
                                        IsValid = false;
                                        return;
                                    }
                                }
                                if (Directory.Exists(Path.Combine(testPath, @"__overlay")))
                                {
                                    GameSource += @" (Steam version)";
                                }
                            }

                            MLog.Information(@"Source: " + GameSource, shouldLog: logInfo);
                        }
                    }

                    if (Game != MEGame.LELauncher)
                    {
                        // Actual game
                        var oldTMOption = TextureModded;
                        var alotInfo = GetInstalledALOTInfo(allowCached: false);
                        if (alotInfo != null)
                        {
                            TextureModded = true;
                            ALOTVersion = alotInfo.ToString();
                            if (alotInfo.MEUITMVER > 0)
                            {
                                MEUITMInstalled = true;
                                MEUITMVersion = alotInfo.MEUITMVER;
                            }
                        }
                        else
                        {
                            TextureModded = false;
                            ALOTVersion = null;
                            MEUITMInstalled = false;
                            MEUITMVersion = 0;
                        }

                        IsPolishME1 = Game == MEGame.ME1 && File.Exists(Path.Combine(TargetPath, @"BioGame", @"CookedPC", @"Movies", @"niebieska_pl.bik"));
                        if (IsPolishME1)
                        {
                            MLog.Information(@"ME1 Polish Edition detected", logInfo);
                        }

                        /* This feature is only in Mod Manager
                        if (RegistryActive && (Settings.AutoUpdateLODs2K || Settings.AutoUpdateLODs4K) &&
                            oldTMOption != TextureModded && forceLodUpdate)
                        {
                            UpdateLODs(Settings.AutoUpdateLODs2K);
                        }*/
                    }
                    else
                    {
                        // LELAUNCHER
                        IsValid = true; //set to false if target becomes invalid
                    }
                }
                else
                {
                    MLog.Error($@"Target is invalid: {TargetPath} does not exist (or is not accessible)");
                    IsValid = false;
                }
            }
            else
            {
                // Custom Option
                IsValid = true;
            }
        }

        public void UpdateLODs(bool twoK)
        {
            if (Game.IsLEGame())
                return; // Do not update LE LODs for now.

            if (!TextureModded)
            {
                // Reset LODs
                MTextureLODSetter.SetLODs(this, false, false, false);
            }
            else
            {
                if (Game == MEGame.ME1)
                {
                    if (MEUITMInstalled)
                    {
                        //detect soft shadows/meuitm
                        var branchingPCFCommon =
                            Path.Combine(TargetPath, @"Engine", @"Shaders", @"BranchingPCFCommon.usf");
                        if (File.Exists(branchingPCFCommon))
                        {
                            var md5 = MUtilities.CalculateHash(branchingPCFCommon);
                            MTextureLODSetter.SetLODs(this, true, twoK, md5 == @"10db76cb98c21d3e90d4f0ffed55d424");
                        }
                    }
                }
                else if (Game.IsOTGame())
                {
                    //me2/3
                    MTextureLODSetter.SetLODs(this, true, twoK, false);
                }
            }
        }

        public bool Equals(GameTarget x, GameTarget y)
        {
            return x.TargetPath == y.TargetPath && x.Game == y.Game;
        }

        public int GetHashCode(GameTarget obj)
        {
            return obj.TargetPath.GetHashCode();
        }

        public bool TextureModded { get; private set; }


        private TextureModInstallationInfo cachedTextureModInfo;

        /// <summary>
        /// Gets the installed texture mod info. If startpos is not defined (<0) the latest version is used from the end of the file.
        /// </summary>
        /// <param name="startpos"></param>
        /// <returns></returns>
        public TextureModInstallationInfo GetInstalledALOTInfo(int startPos = -1, bool allowCached = true)
        {
            if (allowCached && cachedTextureModInfo != null && startPos == -1) return cachedTextureModInfo;

            string markerPath = M3Directories.GetTextureMarkerPath(this);
            if (markerPath != null && File.Exists(markerPath))
            {
                try
                {
                    using FileStream fs = new FileStream(markerPath, System.IO.FileMode.Open, FileAccess.Read);
                    if (startPos < 0)
                    {
                        fs.SeekEnd();
                    }
                    else
                    {
                        fs.Seek(startPos, SeekOrigin.Begin);
                    }

                    long endPos = fs.Position;
                    fs.Position = endPos - 4;
                    uint memi = fs.ReadUInt32();

                    if (memi == MEMI_TAG)
                    {
                        long markerStartOffset = fs.Position;
                        //MEM has been run on this installation
                        fs.Position = endPos - 8;
                        short installerVersionUsed = fs.ReadInt16();
                        short memVersionUsed = fs.ReadInt16();
                        fs.Position -= 4; //roll back so we can read this whole thing as 4 bytes
                        int preMemi4Bytes = fs.ReadInt32();
                        int perGameFinal4Bytes = -20;
                        switch (Game)
                        {
                            case MEGame.ME1:
                                perGameFinal4Bytes = 0;
                                break;
                            case MEGame.ME2:
                                perGameFinal4Bytes = 4352;
                                break;
                            case MEGame.ME3:
                                perGameFinal4Bytes = 16777472;
                                break;
                        }

                        // Note: If MEMI v1 is written after any other MEMI marker, it will not work as we cannot differentiate v1 to v2+
                        // v1 was never used with LE
                        if (Game.IsLEGame() || preMemi4Bytes != perGameFinal4Bytes) //default bytes before 178 MEMI Format (MEMI v1)
                        {
                            // MEMI v3 (and technically also v2 but values will be wrong)
                            fs.Position = endPos - 12;
                            short ALOTVER = fs.ReadInt16();
                            byte ALOTUPDATEVER = (byte)fs.ReadByte();
                            byte ALOTHOTFIXVER = (byte)fs.ReadByte();

                            //unused for now
                            fs.Position = endPos - 16;

                            markerStartOffset = fs.Position;
                            int MEUITMVER = fs.ReadInt32();

                            var tmii = new TextureModInstallationInfo(ALOTVER, ALOTUPDATEVER, ALOTHOTFIXVER, MEUITMVER, memVersionUsed, installerVersionUsed);
                            tmii.MarkerExtendedVersion = 0x01;
                            tmii.MarkerStartPosition = (int)markerStartOffset;

                            // MEMI v4 DETECTION
                            fs.Position = endPos - 20;
                            var markerMagic = fs.ReadUInt32();
                            if (markerMagic == TextureModInstallationInfo.TEXTURE_MOD_MARKER_VERSIONING_MAGIC)
                            {
                                // It's MEMI v4 (or higher)
                                var memiExtendedEndPos = endPos - 24; // Sanity check should make reading end here
                                fs.Position = memiExtendedEndPos;
                                fs.Position -= fs.ReadInt32(); // Go to start of MEMI extended marker
                                tmii.MarkerStartPosition = (int)fs.Position;
                                tmii.MarkerExtendedVersion = fs.ReadInt32();
                                // Extensions to memi format go here
                                if (Game.IsLEGame() && tmii.MarkerExtendedVersion == 0x02)
                                {
                                    // MEM LE
                                    tmii.InstallerVersionFullName = fs.ReadStringUnicodeNull();
                                    tmii.InstallationTimestamp = new DateTime(1970, 1, 1).ToLocalTime().AddSeconds(fs.ReadUInt64());
                                    var fileCount = fs.ReadInt32();
                                    for (int i = 0; i < fileCount; i++)
                                    {
                                        tmii.InstalledTextureMods.Add(new InstalledTextureMod(fs, tmii.MarkerExtendedVersion, Game));
                                    }
                                }
                                else if (tmii.MarkerExtendedVersion == 0x04)
                                {
                                    // This is done by OT ALOT Installer
                                    tmii.InstallerVersionFullName = fs.ReadUnrealString();
                                    tmii.InstallationTimestamp = DateTime.FromBinary(fs.ReadInt64());
                                    var fileCount = fs.ReadInt32();
                                    for (int i = 0; i < fileCount; i++)
                                    {
                                        tmii.InstalledTextureMods.Add(new InstalledTextureMod(fs, tmii.MarkerExtendedVersion, Game));
                                    }
                                }

                                if (fs.Position != memiExtendedEndPos)
                                {
                                    MLog.Warning($@"Sanity check for MEMI extended marker failed. We did not read data until the marker info offset. Should be at 0x{memiExtendedEndPos:X6}, but ended at 0x{fs.Position:X6}");
                                }
                            }
                            if (startPos == -1) cachedTextureModInfo = tmii;
                            return tmii;
                        }

                        var info = new TextureModInstallationInfo(0, 0, 0, 0)
                        {
                            MarkerStartPosition = (int)markerStartOffset,
                            MarkerExtendedVersion = 0x01
                        }; //MEMI tag but no info we know of

                        if (startPos == -1) cachedTextureModInfo = info;
                        return info;
                    }
                }
                catch (Exception e)
                {
                    MLog.Error($@"Error reading texture mod marker file for {Game}. Installed info will be returned as null (nothing installed). " + e.Message);
                    return null;
                }
            }

            return null;
            //Debug. Force ALOT always on
            //return new ALOTVersionInfo(9, 0, 0, 0); //MEMI tag but no info we know of
        }

        public ObservableCollectionExtended<ModifiedFileObject> ModifiedBasegameFiles { get; } = new();
        public ObservableCollectionExtended<SFARObject> ModifiedSFARFiles { get; } = new();

        public void PopulateModifiedBasegameFiles(Func<string, bool> restoreBasegamefileConfirmationCallback = null,
            Func<string, bool> restoreSfarConfirmationCallback = null,
            Action notifySFARRestoringCallback = null,
            Action notifyFileRestoringCallback = null,
            Action<object> notifyRestoredCallback = null)
        {
            ModifiedBasegameFiles.ClearEx();
            ModifiedSFARFiles.ClearEx();

            List<string> modifiedSfars = new List<string>();
            List<string> modifiedFiles = new List<string>();
            var trackedBasegameFilesTextureModded = Game.IsLEGame() && TextureModded ? BasegameFileIdentificationService.GetEntriesForGame(Game) : null;

            void failedCallback(string file)
            {
                if (Game == MEGame.ME3 && Path.GetExtension(file).Equals(@".sfar", StringComparison.InvariantCultureIgnoreCase))
                {
                    modifiedSfars.Add(file);
                    return;
                }
                if (Path.GetFileName(file).Equals(@"PCConsoleTOC.bin", StringComparison.InvariantCultureIgnoreCase))
                {
                    return; // Do not report this file as modified
                }
                if (this.Game != MEGame.LELauncher && file == this.GetTextureMarkerPath())
                {
                    return; //Do not report this file as modified or user will desync game state with texture state
                }

                if (Game.IsLEGame() && TextureModded && file.RepresentsPackageFilePath())
                {
                    var size = new FileInfo(file).Length;
                    var relative = file.Substring(TargetPath.Length) + 1;
                    if (trackedBasegameFilesTextureModded.TryGetValue(relative, out var tracked) && tracked.All(x => x.size != size))
                    {
                        // There is no way we will have a match for this
                        return;
                    }
                }
                modifiedFiles.Add(file);
            }

            // Todo: Maybe add callback to allow skipping texture modded files we know are not tracked
            VanillaDatabaseService.ValidateTargetAgainstVanilla(this, failedCallback, false);

            // SFAR is ME3 only
            if (Game == MEGame.ME3)
            {
                List<string> inconsistentDLC = new List<string>();
                VanillaDatabaseService.ValidateTargetDLCConsistency(this, x => inconsistentDLC.Add(x));

                modifiedSfars.AddRange(inconsistentDLC.Select(x => Path.Combine(x, @"CookedPCConsole", @"Default.sfar")));
                modifiedSfars = modifiedSfars.Distinct().ToList(); //filter out if modified + inconsistent

                ModifiedSFARFiles.AddRange(modifiedSfars.Select(file => MExtendedClassGenerators.GenerateSFARObject(file, this, restoreSfarConfirmationCallback, notifySFARRestoringCallback, notifyRestoredCallback)));
            }

            // Filter out packages and TFCs if game is texture modded
            List<ModifiedFileObject> modifiedBasegameFiles;
            if (TextureModded && Game.IsLEGame())
            {
                // Non package files initially.
                modifiedBasegameFiles = modifiedFiles.Where(x => !x.RepresentsPackageFilePath()).Select(file => MExtendedClassGenerators.GenerateModifiedFileObject(
                    file.Substring(TargetPath.Length + 1), this,
                    restoreBasegamefileConfirmationCallback,
                    notifyFileRestoringCallback,
                    notifyRestoredCallback)).ToList();

                // Then look for only tracked package files
                foreach (var trackedFile in BasegameFileIdentificationService.GetEntriesForGame(Game).Where(x => x.Key.RepresentsPackageFilePath()))
                {
                    var path = Path.Combine(TargetPath, trackedFile.Key);
                    var hash = MUtilities.CalculateHash(path);
                    var info = trackedFile.Value.FirstOrDefault(x => x.hash == hash);
                    if (info != null)
                    {
                        // Should we allow restoring these...? Seems kinda dangerous
                        modifiedBasegameFiles.Add(
                            MExtendedClassGenerators.GenerateModifiedFileObject(trackedFile.Key, this, null, null, null, md5: hash));
                    }
                }
            }
            else
            {
                // Not texture modded
                modifiedBasegameFiles = modifiedFiles.Where(x =>
                        // This line is for OT as it doesn't support post-texture mod tracking
                        !TextureModded || (!x.RepresentsPackageFilePath() && Path.GetExtension(x) != @".tfc"))

                    .Select(file => MExtendedClassGenerators.GenerateModifiedFileObject(
                        file.Substring(TargetPath.Length + 1), this,
                        restoreBasegamefileConfirmationCallback,
                        notifyFileRestoringCallback,
                        notifyRestoredCallback)).ToList();
            }

            ModifiedBasegameFiles.AddRange(modifiedBasegameFiles);
        }

        /// <summary>
        /// Call this when the modified object lists are no longer necessary. In WPF this needs to be overridden to run on dispatcher
        /// </summary>
        public virtual void DumpModifiedFilesFromMemory()
        {
            ModifiedBasegameFiles.ClearEx();
            ModifiedSFARFiles.ClearEx();
            foreach (var uiInstalledDlcMod in UIInstalledDLCMods)
            {
                uiInstalledDlcMod.ClearHandlers();
            }

            UIInstalledDLCMods.ClearEx();
        }

        public ObservableCollectionExtended<InstalledDLCMod> UIInstalledDLCMods { get; } = new();
        public ObservableCollectionExtended<InstalledOfficialDLC> UIInstalledOfficialDLC { get; } = new();

        /// <summary>
        /// Populates the list of installed mods and official DLCs.
        /// </summary>
        /// <param name="includeDisabled"></param>
        /// <param name="deleteConfirmationCallback"></param>
        /// <param name="notifyDeleted"></param>
        /// <param name="notifyToggled"></param>
        /// <param name="modNamePrefersTPMI"></param>
        public virtual void PopulateDLCMods(bool includeDisabled, Func<InstalledDLCMod, bool> deleteConfirmationCallback = null, Action notifyDeleted = null, Action notifyToggled = null, bool modNamePrefersTPMI = false)
        {
            if (Game == MEGame.LELauncher) return; // LE Launcher doesn't have DLC mods
            var dlcDir = M3Directories.GetDLCPath(this);
            var allOfficialDLCforGame = MEDirectories.OfficialDLC(Game);
            var installedDLC = GetInstalledDLC(includeDisabled);
            var installedMods = installedDLC.Where(x => !allOfficialDLCforGame.Contains(x.TrimStart('x'), StringComparer.InvariantCultureIgnoreCase));

            // Also populate official DLC
            var installedOfficialDLC = installedDLC.Where(x => allOfficialDLCforGame.Contains(x, StringComparer.InvariantCultureIgnoreCase));
            var notInstalledOfficialDLC = allOfficialDLCforGame.Where(x => !installedOfficialDLC.Contains(x));

            var officialDLC = installedOfficialDLC.Select(x => new InstalledOfficialDLC(x, true, Game)).ToList();
            officialDLC.AddRange(notInstalledOfficialDLC.Select(x => new InstalledOfficialDLC(x, false, Game)));
            officialDLC = officialDLC.OrderBy(x => x.HumanName).ToList();

            //Must run on UI thread (if this library is being used in a UI project)
            ME3TweaksCoreLib.RunOnUIThread(() =>
            {
                UIInstalledDLCMods.ReplaceAll(installedMods.Select(x => MExtendedClassGenerators.GenerateInstalledDlcModObject(Path.Combine(dlcDir, x), Game, deleteConfirmationCallback, notifyDeleted, notifyToggled, modNamePrefersTPMI)).ToList().OrderBy(x => x.ModName));
                UIInstalledOfficialDLC.ReplaceAll(officialDLC);
            });
        }

        private InstalledDLCMod InternalGenerateDLCModObject()
        {
            // This is for superclass I think?
            throw new NotImplementedException();
        }

        public bool IsTargetWritable()
        {
            if (Game == MEGame.LELauncher)
            {
                return MUtilities.IsDirectoryWritable(TargetPath) && MUtilities.IsDirectoryWritable(Path.Combine(TargetPath, @"Content"));
            }
            return MUtilities.IsDirectoryWritable(TargetPath) && MUtilities.IsDirectoryWritable(Path.Combine(TargetPath, @"Binaries"));
        }

        public bool IsValid { get; set; }

        /// <summary>
        /// Validates a game directory by checking for multiple things that should be present in a working game.
        /// </summary>
        /// <param name="ignoreCmmVanilla">Ignore the check for a cmm_vanilla file. Presence of this file will cause validation to fail</param>
        /// <returns>String of failure reason, null if OK</returns>
        public virtual string ValidateTarget(bool ignoreCmmVanilla = false)
        {
            IsValid = false; //set to invalid at first/s
            string[] validationFiles = null;
            switch (Game)
            {
                case MEGame.ME1:
                    validationFiles = new[]
                    {
                        Path.Combine(TargetPath, @"Binaries", @"MassEffect.exe"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPC", @"Maps", @"EntryMenu.SFM"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPC", @"BIOC_Base.u"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPC", @"Packages", @"Textures", @"BIOA_GLO_00_A_Opening_FlyBy_T.upk"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPC", @"Maps", @"WAR", @"LAY", @"BIOA_WAR20_05_LAY.SFM"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPC", @"Movies", @"MEvisionSEQ3.bik")
                    };
                    break;
                case MEGame.ME2:
                    validationFiles = new[]
                    {
                        Path.Combine(TargetPath, @"Binaries", @"MassEffect2.exe"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPC", @"BioA_BchLmL.pcc"),
                        Path.Combine(TargetPath, @"BioGame", @"Config", @"PC", @"Cooked", @"Coalesced.ini"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPC", @"Wwise_Jack_Loy_Music.afc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPC", @"WwiseAudio.pcc"),
                        Path.Combine(TargetPath, @"BioGame", @"Movies", @"Crit03_CollectArrive_Part2_1.bik")
                    };
                    break;
                case MEGame.ME3:
                    validationFiles = new[]
                    {
                        Path.Combine(TargetPath, @"Binaries", @"Win32", @"MassEffect3.exe"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"Textures.tfc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"Startup.pcc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"Coalesced.bin"),
                        Path.Combine(TargetPath, @"BioGame", @"Patches", @"PCConsole", @"Patch_001.sfar"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"Textures.tfc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"citwrd_rp1_bailey_m_D_Int.afc")
                    };
                    break;
                case MEGame.LE1:
                    validationFiles = new[]
                    {
                        Path.Combine(TargetPath, @"Binaries", @"Win64", @"MassEffect1.exe"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"Textures5.tfc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"Startup_INT.pcc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"Coalesced_INT.bin"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"Textures.tfc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"PlotManagerAutoDLC_UNC.pcc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"SFXTest.pcc") // This file is expected to exist as lots of code depends on it
                    };
                    break;
                case MEGame.LE2:
                    validationFiles = new[]
                    {
                        Path.Combine(TargetPath, @"Binaries", @"Win64", @"MassEffect2.exe"),
                        Path.Combine(TargetPath, @"BioGame", @"PCConsoleTOC.bin"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"Startup_INT.pcc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"Coalesced_INT.bin"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"BioD_QuaTlL_505LifeBoat_LOC_INT.pcc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"cithub_ad_low_a_S_INT.afc"),
                        Path.Combine(TargetPath, @"BioGame", @"DLC", @"DLC_METR_Patch01", @"CookedPCConsole", @"BioA_Nor_103aGalaxyMap.pcc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"SFXTest.pcc") // This file is expected to exist as lots of code depends on it
                    };
                    break;
                case MEGame.LE3:
                    validationFiles = new[]
                    {
                        Path.Combine(TargetPath, @"Binaries", @"Win64", @"MassEffect3.exe"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"Textures1.tfc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"Startup.pcc"),
                        Path.Combine(TargetPath, @"BioGame", @"DLC", @"DLC_CON_PRO3", @"CookedPCConsole", @"DLC_CON_PRO3_INT.tlk"),
                        Path.Combine(TargetPath, @"BioGame", @"DLC", @"DLC_CON_END", @"CookedPCConsole", @"BioD_End001_910RaceToConduit.pcc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"citwrd_rp1_bailey_m_D_Int.afc"),
                        Path.Combine(TargetPath, @"BioGame", @"CookedPCConsole", @"SFXTest.pcc") // This file is expected to exist as lots of code depends on it
                    };
                    break;
                case MEGame.LELauncher: // LELAUNCHER
                    validationFiles = new[]
                    {
                        Path.Combine(TargetPath, @"MassEffectLauncher.exe"),
                        Path.Combine(TargetPath, @"Content", @"EulaUI.swf"),
                        Path.Combine(TargetPath, @"Content", @"click.wav"),
                        Path.Combine(TargetPath, @"Content", @"LauncherUI.swf"),
                        Path.Combine(TargetPath, @"Content", @"Xbox_ControllerIcons.swf"),
                        Path.Combine(TargetPath, @"Content", @"Sounds", @"mus_gui_menu_looping_quad.wav"),
                    };
                    break;
            }

            if (validationFiles == null) return null; //Invalid game.
            MLog.Information($@"Validating game target: {TargetPath}");
            foreach (var f in validationFiles)
            {
                if (!File.Exists(f))
                {
                    return LC.GetString(LC.string_interp_invalidTargetMissingFile, Path.GetFileName(f));
                }
            }

            // Check exe on first file
            var exeInfo = FileVersionInfo.GetVersionInfo(validationFiles[0]);
            switch (Game)
            {
                /*
                MassEffect.exe 1.2.20608.0
                MassEffect2.exe 1.2.1604.0 (File Version)
                ME2Game.exe is the same
                MassEffect3.exe 1.5.5427.124
                */
                case MEGame.ME1:
                    if (exeInfo.FileVersion != @"1.2.20608.0")
                    {
                        // NOT SUPPORTED
                        return LC.GetString(LC.string_interp_unsupportedME1Version, exeInfo.FileVersion);
                    }
                    break;
                case MEGame.ME2:
                    if (exeInfo.FileVersion != @"1.2.1604.0" && exeInfo.FileVersion != @"01604.00") // Steam and Origin exes have different FileVersion for some reason
                    {
                        // NOT SUPPORTED
                        return LC.GetString(LC.string_interp_unsupportedME2Version, exeInfo.FileVersion);
                    }
                    break;
                case MEGame.ME3:
                    if (exeInfo.FileVersion != @"05427.124") // not really sure what's going on here
                    {
                        // NOT SUPPORTED
                        return LC.GetString(LC.string_interp_unsupportedME3Version, exeInfo.FileVersion);
                    }
                    break;

                    // No check for Legendary Edition games right now until patch cycle ends
            }

            if (!ignoreCmmVanilla)
            {
                if (File.Exists(Path.Combine(TargetPath, @"cmm_vanilla"))) return LC.GetString(LC.string_invalidTargetProtectedByCmmvanilla);
            }

            IsValid = true;
            return null;
        }

        protected bool Equals(GameTarget other)
        {
            return TargetPath == other.TargetPath;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((GameTarget)obj);
        }

        public bool MEUITMInstalled { get; private set; }
        public int MEUITMVersion { get; private set; }

        public override int GetHashCode()
        {
            return (TargetPath != null ? TargetPath.GetHashCode() : 0);
        }

        private Queue<SFARObject> SFARRestoreQueue = new Queue<SFARObject>();

        public ObservableCollectionExtended<TextureModInstallationInfo> TextureInstallHistory { get; } = new ObservableCollectionExtended<TextureModInstallationInfo>();

        public void PopulateTextureInstallHistory()
        {
            TextureInstallHistory.ReplaceAll(GetTextureModInstallationHistory());
        }

        public List<TextureModInstallationInfo> GetTextureModInstallationHistory()
        {
            var alotInfos = new List<TextureModInstallationInfo>();
            if (Game == MEGame.LELauncher) return alotInfos;
            int startPos = -1;
            while (GetInstalledALOTInfo(startPos, false) != null)
            {
                var info = GetInstalledALOTInfo(startPos, false);
                alotInfos.Add(info);
                startPos = info.MarkerStartPosition;
            }

            return alotInfos;
        }

        public void StampDebugALOTInfo()
        {
#if DEBUG
            // Writes a MEMI v4 marker
            Random r = new Random();
            TextureModInstallationInfo tmii = new TextureModInstallationInfo((short)(r.Next(10) + 1), 1, 0, 3);
            tmii.MarkerExtendedVersion = TextureModInstallationInfo.LATEST_TEXTURE_MOD_MARKER_VERSION;
            tmii.InstallationTimestamp = DateTime.Now;
            var ran = new Random();
            int i = 10;
            var fileset = new List<InstalledTextureMod>();
            string[] authors = { @"Mgamerz", @"Scottina", @"Sil", @"Audemus", @"Jack", @"ThisGuy", @"KitFisto" };
            string[] modnames =
            {
                @"Spicy Italian Meatballs", @"HD window textures", @"Zebra Stripes", @"Everything is an advertisement",
                @"Todd Howard's Face", @"Kai Lame", @"Downscaled then upscaled", @"1990s phones", @"Are these even texture mods?", @"Dusty countertops",
                @"Dirty shoes", @"Cotton Candy Clothes", @"4K glowy things", @"Christmas in August", @"Cyber warfare", @"Shibuya but it's really hot all the time",
                @"HD Manhole covers", @"Priority Earth but retextured to look like Detroit"
            };
            while (i > 0)
            {
                fileset.Add(new InstalledTextureMod()
                {
                    ModName = modnames.RandomElement(),
                    AuthorName = authors.RandomElement(),
                    ModType = r.Next(6) == 0 ? InstalledTextureMod.InstalledTextureModType.USERFILE : InstalledTextureMod.InstalledTextureModType.MANIFESTFILE
                });
                i--;
            }
            tmii.InstalledTextureMods.AddRange(fileset);
            StampTextureModificationInfo(tmii);
#endif
        }

        /// <summary>
        /// Stamps the TextureModInstallationInfo object into the game. This method only works in Debug mode
        /// as M3 is not a texture installer
        /// </summary>
        /// <param name="tmii"></param>
        public void StampTextureModificationInfo(TextureModInstallationInfo tmii)
        {
#if DEBUG
            var markerPath = M3Directories.GetTextureMarkerPath(this);
            try
            {
                using (FileStream fs = new FileStream(markerPath, FileMode.Open, FileAccess.ReadWrite))
                {
                    // MARKER FILE FORMAT
                    // When writing marker, the end of the file is appended with the following data.
                    // Programs that read this marker must read the file IN REVERSE as the MEMI marker
                    // file is appended to prevent data corruption of an existing game file

                    // MEMI v1 - ALOT support
                    // This version only indicated that a texture mod (alot) had been installed
                    // BYTE "MEMI" ASCII

                    // MEMI v2 - MEUITM support (2018):
                    // This version supported versioning of main ALOT and MEUITM. On ME2/3, the MEUITM field would be 0
                    // INT MEUITM VERSION
                    // INT ALOT VERSION (major only)
                    // SHORT MEM VERSION USED
                    // SHORT INSTALLER VERSION USED
                    // <MEMI v1>

                    // MEMI v3 - ALOT subversioning support (2018):
                    // This version split the ALOT int into a short and 2 bytes. The size did not change.
                    // As a result it is not possible to distinguish v2 and v3, and code should just assume v3.
                    // INT MEUITM Version
                    // SHORT ALOT Version
                    // BYTE ALOT Update Version
                    // BYTE ALOT Hotfix Version (not used)
                    // SHORT MEM VERSION USED
                    // SHORT INSTALLER VERSION USED
                    // <MEMI v1>

                    // MEMI v4 - Extended (2020+):
                    // INT MEMI EXTENDED VERSION                         <---------------------------------------------
                    // UNREALSTRING Installer Version Info Extended                                                   |
                    // LONG BINARY DATESTAMP OF STAMPING TIME                                                         |
                    // INT INSTALLED FILE COUNT - ONLY COUNTS TEXTURE MODS, PREINSTALL MODS ARE NOT COUNTED           |
                    // FOR <INSTALLED FILE COUNT>                                                                     |
                    //     BYTE INSTALLED FILE TYPE                                                                   |
                    //         0 = USER FILE                                                                          |
                    //         1 = MANIFEST FILE                                                                      |
                    //     UNREALSTRING Installed File Name (INT LEN (negative for unicode), STR DATA)                |
                    //     [IF MANIFESTFILE] UNREALSTRING Author Name                                                 |
                    // INT MEMI Extended Marker Data Start Offset -----------------------------------------------------
                    // INT MEMI Extended Magic (0xDEADBEEF)
                    // <MEMI v3>

                    fs.SeekEnd();

                    // Write MEMI v4 - Installer full name, date, List of installed files
                    var memiExtensionStartPos = fs.Position;
                    fs.WriteInt32(TextureModInstallationInfo.LATEST_TEXTURE_MOD_MARKER_VERSION); // THIS MUST BE INCREMENTED EVERY TIME MARKER FORMAT CHANGES!! OR IT WILL BREAK OTHER APPS
                    fs.WriteUnrealStringUnicode($@"ME3Tweaks Installer DEBUG TEST");
                    fs.WriteInt64(DateTime.Now.ToBinary()); // DATESTAMP
                    fs.WriteInt32(tmii.InstalledTextureMods.Count); // NUMBER OF FILE ENTRIES TO FOLLOW. Count must be here
                    foreach (var fi in tmii.InstalledTextureMods)
                    {
                        fi.WriteToMarker(fs);
                    }
                    fs.WriteInt32((int)memiExtensionStartPos); // Start of memi extended data
                    fs.WriteUInt32(TextureModInstallationInfo.TEXTURE_MOD_MARKER_VERSIONING_MAGIC); // Magic that can be used to tell if this has the v3 extended marker offset preceding it

                    // Write MEMI v3
                    fs.WriteInt32(tmii.MEUITMVER); //meuitm
                    fs.WriteInt16(tmii.ALOTVER); //major
                    fs.WriteByte(tmii.ALOTUPDATEVER); //minor
                    fs.WriteByte(tmii.ALOTHOTFIXVER); //hotfix

                    // MEMI v2 is not used

                    // Write MEMI v1
                    fs.WriteInt16(tmii.ALOT_INSTALLER_VERSION_USED); //Installer Version (Build)
                    fs.WriteInt16(tmii.MEM_VERSION_USED); //Backend MEM version
                    fs.WriteUInt32(MEMI_TAG);
                }

            }
            catch (Exception e)
            {
                MLog.Error($@"Error writing debug texture mod installation marker file: {e.Message}");
            }
#endif
        }

        public void StripALOTInfo()
        {
#if DEBUG
            var markerPath = M3Directories.GetTextureMarkerPath(this);

            try
            {
                using (FileStream fs = new FileStream(markerPath, System.IO.FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.SeekEnd();
                    fs.WriteUInt32(1234); //erase memi tag
                }
                MLog.Information(@"Changed MEMI Tag for game to 1234.");
            }
            catch (Exception e)
            {
                MLog.Error($@"Error stripping debug ALOT marker file for {Game}. {e.Message}");
            }
#endif
        }

        public bool HasALOTOrMEUITM()
        {
            var alotInfo = GetInstalledALOTInfo();
            return alotInfo != null && (alotInfo.ALOTVER > 0 || alotInfo.MEUITMVER > 0);
        }

        private bool RestoringSFAR;

        private void SignalSFARRestore()
        {
            if (SFARRestoreQueue.TryDequeue(out var sfar))
            {
                RestoringSFAR = true;
                sfar.RestoreSFAR(true, SFARRestoreCompleted);
            }
        }

        private void SFARRestoreCompleted()
        {
            RestoringSFAR = false;
            SignalSFARRestore(); //try next item
        }

        /// <summary>
        /// Queues an sfar for restoration.
        /// </summary>
        /// <param name="sfar">SFAR to restore</param>
        /// <param name="batchMode">If this is part of a batch mode, and thus should not show dialogs</param>
        public void RestoreSFAR(SFARObject sfar, bool batchMode)
        {
            sfar.Restoring = true;
            sfar.RestoreButtonContent = LC.GetString(LC.string_restoring);
            SFARRestoreQueue.Enqueue(sfar);
            if (!RestoringSFAR)
            {
                SignalSFARRestore();
            }
        }

        // We set it to false on RestoringSFAR because ModifiedSFARFiles will be modified. A race condition may occur.
        public bool HasModifiedMPSFAR() => !RestoringSFAR && ModifiedSFARFiles.Any(x => x.IsMPSFAR);
        public bool HasModifiedSPSFAR() => !RestoringSFAR && ModifiedSFARFiles.Any(x => x.IsSPSFAR);

        public ObservableCollectionExtended<InstalledExtraFile> ExtraFiles { get; } = new ObservableCollectionExtended<InstalledExtraFile>();
        /// <summary>
        /// Populates list of 'extra' items for the game. This includes things like dlls, and for ME1, config files
        /// </summary>
        public void PopulateExtras()
        {
            try
            {
                var exeDir = M3Directories.GetExecutableDirectory(this);
                var dlls = Directory.GetFiles(exeDir, @"*.dll").Select(x => Path.GetFileName(x));
                var expectedDlls = MEDirectories.VanillaDlls(this.Game);
                var extraDlls = dlls.Except(expectedDlls, StringComparer.InvariantCultureIgnoreCase);

                void notifyExtraFileDeleted(InstalledExtraFile ief)
                {
                    ExtraFiles.Remove(ief);
                }

                ExtraFiles.ReplaceAll(extraDlls.Select(x => MExtendedClassGenerators.GenerateInstalledExtraFile(Path.Combine(exeDir, x), InstalledExtraFile.EFileType.DLL, Game, notifyExtraFileDeleted)));
            }
            catch (Exception e)
            {
                MLog.Error($@"Error populating extras for target {TargetPath}: " + e.Message);
            }
        }

        public string NumASIModsInstalledText { get; private set; }
        public void PopulateASIInfo()
        {
            if (Game == MEGame.LELauncher) return;
            var installedASIs = GetInstalledASIs();
            if (installedASIs.Any())
            {
                NumASIModsInstalledText = LC.GetString(LC.string_interp_asiStatus, installedASIs.Count);
            }
            else
            {
                NumASIModsInstalledText = LC.GetString(LC.string_thisInstallationHasNoASIModsInstalled);
            }
        }

        public string Binkw32StatusText { get; private set; }
        public void PopulateBinkInfo()
        {
            if (Game is MEGame.ME2 or MEGame.ME3)
            {
                Binkw32StatusText = IsBinkBypassInstalled() ? LC.GetString(LC.string_bypassInstalledASIAndDLCModsWillBeAbleToLoad) : LC.GetString(LC.string_bypassNotInstalledASIAndDLCModsWillBeUnableToLoad);
            }
            else if (Game is MEGame.ME1 || Game.IsLEGame())
            {
                Binkw32StatusText = IsBinkBypassInstalled() ? LC.GetString(LC.string_bypassInstalledASIModsWillBeAbleToLoad) : LC.GetString(LC.string_bypassNotInstalledASIModsWillBeUnableToLoad);
            }
        }

        public List<IInstalledASIMod> GetInstalledASIs()
        {
            var installedASIs = new List<IInstalledASIMod>();
            try
            {
                string asiDirectory = M3Directories.GetASIPath(this);
                if (asiDirectory != null && Directory.Exists(TargetPath))
                {
                    if (!Directory.Exists(asiDirectory))
                    {
                        Directory.CreateDirectory(asiDirectory); //Create it, but we don't need it
                        return installedASIs; //It won't have anything in it if we are creating it
                    }

                    var asiFiles = Directory.GetFiles(asiDirectory, @"*.asi");
                    foreach (var asiFile in asiFiles)
                    {
                        var hash = MUtilities.CalculateHash(asiFile);
                        var matchingManifestASI = ASIManager.GetASIVersionByHash(hash, Game);
                        if (matchingManifestASI != null)
                        {
                            installedASIs.Add(MExtendedClassGenerators.GenerateKnownInstalledASIMod(asiFile, hash, Game, matchingManifestASI));
                        }
                        else
                        {
                            installedASIs.Add(MExtendedClassGenerators.GenerateUnknownInstalledASIMod(asiFile, hash, Game));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MLog.Error(@"Error fetching list of installed ASIs: " + e.Message);
            }

            return installedASIs;
        }

        public bool SupportsLODUpdates() => Game.IsOTGame();

        /// <summary>
        /// Gets all installed and enabled DLC foldernames. Includes disabled if indicated.
        /// </summary>
        /// <param name="includeDisabled"></param>
        /// <returns></returns>
        public List<string> GetInstalledDLC(bool includeDisabled = false)
        {
            var dlcDirectory = M3Directories.GetDLCPath(this);
            if (Directory.Exists(dlcDirectory))
            {
                return Directory.GetDirectories(dlcDirectory).Where(x => Path.GetFileName(x).StartsWith(@"DLC_") || (includeDisabled && Path.GetFileName(x).StartsWith(@"xDLC_"))).Select(x => Path.GetFileName(x)).ToList();
            }

            return new List<string>();
        }

        /// <summary>
        /// Gets a list of installed DLC, sorted by mount priority from lowest to highest. Does not include disabled DLC.
        /// </summary>
        /// <param name="includeDisabled"></param>
        /// <returns></returns>
        public List<string> GetInstalledDLCByMountPriority(bool includeDisabled = false)
        {
            return MELoadedDLC.GetDLCNamesInMountOrder(Game, TargetPath);
        }

        /// <summary>
        /// Gets list of DLC directory names that are not made by BioWare. Does not include disabled DLC.
        /// </summary>
        /// <param name="target">Target to get mods from</param>
        /// <returns>List of DLC foldernames</returns>
        public List<string> GetInstalledDLCMods()
        {
            return GetInstalledDLC().Where(x => !MEDirectories.OfficialDLC(Game).Contains(x, StringComparer.InvariantCultureIgnoreCase)).ToList();
        }

        /// <summary>
        /// Maps each DLC folder to it's MetaCMM file, if one exists. Otherwise it is mapped to null
        /// </summary>
        /// <param name="target">Target to get data from</param>
        /// <param name="installedDLC">The list of DLC in the target, to prevent multiple filesystem enumeration if done externally to method</param>
        /// <returns></returns>
        public CaseInsensitiveDictionary<MetaCMM> GetMetaMappedInstalledDLC(bool includeOfficial = true, List<string> installedDLC = null)
        {
            installedDLC ??= GetInstalledDLC();
            var metamap = new CaseInsensitiveDictionary<MetaCMM>();
            var dlcpath = M3Directories.GetDLCPath(this);
            foreach (var v in installedDLC)
            {
                if (!includeOfficial && MEDirectories.OfficialDLC(Game).Contains(v)) continue; // This is not a mod
                var meta = Path.Combine(dlcpath, v, @"_metacmm.txt");
                MetaCMM mf = null;
                if (File.Exists(meta))
                {
                    mf = new MetaCMM(meta);
                }

                metamap[v] = mf;
            }

            return metamap;
        }

        private const string ME1ASILoaderHash = @"30660f25ab7f7435b9f3e1a08422411a";
        private const string ME2ASILoaderHash = @"a5318e756893f6232284202c1196da13";
        private const string ME3ASILoaderHash = @"1acccbdae34e29ca7a50951999ed80d5";
        private const string LEASILoaderHash = @"19d444d944d89b173a81339859ec3e93"; // bink 2.0.0.12 by ME3Tweaks 06/02/2024

        /// <summary>
        /// Determines if the enhanced bink video library file is installed
        /// </summary>
        /// <returns>True if the specific version is found; false otherwise</returns>
        public bool IsEnhancedBinkInstalled()
        {
            if (Game.IsOTGame()) return false; // Enhanced bink is only for LE.
            try
            {
                string binkPath = GetOriginalProxiedBinkPath();
                if (!File.Exists(binkPath))
                    return false;

                var finfo = FileVersionInfo.GetVersionInfo(binkPath);
                return Version.TryParse(finfo.FileVersion, out var binkVer) && binkVer >= new Version(@"2022.05");
            }
            catch (Exception e)
            {
                // File is in use by another process perhaps
                MLog.Exception(e, @"Unable to determine if enhanced bink is installed:");
            }

            return false;
        }

        /// <summary>
        /// Determines if the bink ASI loader/bypass is installed (both OT and LE)
        /// </summary>
        /// <returns></returns>
        public bool IsBinkBypassInstalled()
        {
            try
            {
                string binkPath = GetVanillaBinkPath();
                string expectedHash = null;
                if (Game == MEGame.ME1) expectedHash = ME1ASILoaderHash;
                else if (Game == MEGame.ME2) expectedHash = ME2ASILoaderHash;
                else if (Game == MEGame.ME3) expectedHash = ME3ASILoaderHash;
                else if (Game.IsLEGame()) expectedHash = LEASILoaderHash;

                if (File.Exists(binkPath))
                {
                    return MUtilities.CalculateHash(binkPath) == expectedHash;
                }
            }
            catch (Exception e)
            {
                // File is in use by another process perhaps
                MLog.Exception(e, @"Unable to hash bink dll:");
            }

            return false;
        }

        /// <summary>
        /// Installs the Bink ASI loader to this target.
        /// </summary>
        /// <returns></returns>
        public bool InstallBinkBypass(bool throwError)
        {
            var destPath = GetVanillaBinkPath();
            MLog.Information($@"Installing Bink bypass for {Game} to {destPath}");
            try
            {
                var obinkPath = GetOriginalProxiedBinkPath();

                if (Game.IsOTGame())
                {
                    MUtilities.ExtractInternalFile($@"ME3TweaksCore.GameFilesystem.Bink._32.{Game.ToString().ToLower()}.binkw32.dll", destPath, true);
                    MUtilities.ExtractInternalFile($@"ME3TweaksCore.GameFilesystem.Bink._32.{Game.ToString().ToLower()}.binkw23.dll", obinkPath, true);
                }
                else if (Game.IsLEGame() || Game == MEGame.LELauncher)
                {
                    MUtilities.ExtractInternalFile(@"ME3TweaksCore.GameFilesystem.Bink._64.bink2w64.dll", destPath, true); // Bypass proxy / ASI loader
                    MUtilities.ExtractInternalFile(@"ME3TweaksCore.GameFilesystem.Bink._64.bink2w64_enhanced.dll", obinkPath, true); // The original dll (enhanced version)
                }
                else
                {
                    MLog.Error($@"Unknown game for gametarget (InstallBinkBypass): {Game}");
                    return false;
                }

                MLog.Information($@"Installed Bink bypass for {Game}");
                return true;
            }
            catch (Exception e)
            {
                MLog.Exception(e, @"Error installing bink bypass:");
                if (throwError)
                    throw;
            }

            return false;
        }

        /// <summary>
        /// Uninstalls the Bink ASI loader from this target (does not do anything to Legendary Edition Launcher)
        /// </summary>
        public void UninstallBinkBypass()
        {
            var binkPath = GetVanillaBinkPath();
            var obinkPath = GetOriginalProxiedBinkPath();
            if (Game == MEGame.ME1)
            {
                if (File.Exists(obinkPath))
                    File.Delete(obinkPath);
                MUtilities.ExtractInternalFile(@"ME3TweaksCore.GameFilesystem.Bink._32.me1.binkw23.dll", binkPath, true);
            }
            else if (Game == MEGame.ME2)
            {
                if (File.Exists(obinkPath))
                    File.Delete(obinkPath);
                MUtilities.ExtractInternalFile(@"ME3TweaksCore.GameFilesystem.Bink._32.me2.binkw23.dll", binkPath, true);
            }
            else if (Game == MEGame.ME3)
            {
                if (File.Exists(obinkPath))
                    File.Delete(obinkPath);
                MUtilities.ExtractInternalFile(@"ME3TweaksCore.GameFilesystem.Bink._32.me3.binkw23.dll", binkPath, true);
            }
            else if (Game.IsLEGame())
            {
                if (File.Exists(obinkPath))
                    File.Delete(obinkPath);
                MUtilities.ExtractInternalFile(@"ME3TweaksCore.GameFilesystem.Bink._64.bink2w64_original.dll", binkPath, true);
            }
        }

        /// <summary>
        /// Gets the path where the original, vanilla bink dll should be (the one that is proxied)
        /// </summary>
        /// <returns></returns>
        private string GetVanillaBinkPath()
        {
            if (Game == MEGame.ME1 || Game == MEGame.ME2) return Path.Combine(TargetPath, @"Binaries", @"binkw32.dll");
            if (Game == MEGame.ME3) return Path.Combine(TargetPath, @"Binaries", @"win32", @"binkw32.dll");
            if (Game.IsLEGame()) return Path.Combine(TargetPath, @"Binaries", @"Win64", @"bink2w64.dll");
            if (Game == MEGame.LELauncher) return Path.Combine(TargetPath, @"bink2w64.dll");
            return null;
        }

        /// <summary>
        /// Removes the cmm_vanilla file from this target, if present.
        /// </summary>
        public void StripCmmVanilla()
        {
            var cmmVanilla = Path.Combine(TargetPath, BackupService.CMM_VANILLA_FILENAME);
            if (File.Exists(cmmVanilla))
            {
                MLog.Information($@"Deleting {BackupService.CMM_VANILLA_FILENAME} file: {cmmVanilla}");
                File.Delete(cmmVanilla);
            }
        }

        /// <summary>
        /// Gets the path to the proxied version of the bink dll
        /// </summary>
        /// <returns></returns>
        private string GetOriginalProxiedBinkPath()
        {
            if (Game == MEGame.ME1 || Game == MEGame.ME2) return Path.Combine(TargetPath, @"Binaries", @"binkw23.dll");
            if (Game == MEGame.ME3) return Path.Combine(TargetPath, @"Binaries", @"win32", @"binkw23.dll");
            if (Game.IsLEGame()) return Path.Combine(TargetPath, @"Binaries", @"Win64", @"bink2w64_original.dll");
            if (Game == MEGame.LELauncher) return Path.Combine(TargetPath, @"bink2w64_original.dll");
            return null;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
