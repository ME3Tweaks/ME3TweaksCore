using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Gammtek.Extensions;
using LegendaryExplorerCore.Gammtek.Paths;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Helpers.MEM;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Objects;
using ME3TweaksCore.Targets;
using Octokit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Diagnostic module for textures via Mass Effect Modder (MEM) 
    /// </summary>
    internal class DiagMEM : DiagModuleBase
    {
        /// <summary>
        /// Set to true if MEM was prepared for use
        /// </summary>
        private bool hasMEM = false;

        /// <summary>
        /// Path to the used MEM Ini file
        /// </summary>
        private string memIniPath = null;

        /// <summary>
        /// The cached MEM game path before we swapped it.
        /// </summary>
        private string oldMEMGamePath = null;

        internal override void RunModule(LogUploadPackage package)
        {
            var diag = package.DiagnosticWriter;
            var gameID = package.DiagnosticTarget.Game.ToMEMGameNum().ToString();

            // It is here we say a little prayer
            // to keep the bugs away from this monsterous code
            // This used to be in LogCollector when everything was in one place
            // so it makes less sense here
            // but I don't want to lose this treasure
            //    /_/\/\
            //    \_\  /
            //    /_/  \
            //    \_\/\ \
            //      \_\/


            // Prepare MEM
            var memEnsuredSignaler = new object();

            #region Callbacks
            void currentTaskCallback(string s) => package.UpdateStatusCallback?.Invoke(s);
            void setPercentDone(long downloaded, long total) => package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_preparingMEMNoGUIX, MUtilities.GetPercent(downloaded, total)));
            void failedToExtractMEM(Exception e)
            {
                Thread.Sleep(100); //try to stop deadlock
                hasMEM = false;
            }
            void memExceptionOccured(string operation)
            {
                diag.AddDiagLine($@"An exception occurred performing an operation: {operation}", LogSeverity.ERROR);
                diag.AddDiagLine(@"Check the Mod Manager application log for more information.", LogSeverity.ERROR);
                diag.AddDiagLine(@"Report this on the ME3Tweaks Discord for further assistance.", LogSeverity.ERROR);
            }
            #endregion


            // Texture mod information doesn't require MEM to read
            #region Texture mod information
            MLog.Information(@"Getting texture mod installation info");
            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_gettingTextureInfo));
            diag.AddDiagLine(@"Current texture mod information", LogSeverity.DIAGSECTION);

            var textureHistory = package.DiagnosticTarget.GetTextureModInstallationHistory();
            if (!textureHistory.Any())
            {
                diag.AddDiagLine(
                    @"The texture mod installation marker was not detected. No texture mods appear to be installed");
            }
            else
            {
                var latestInstall = textureHistory[0];
                if (latestInstall.ALOTVER > 0 || latestInstall.MEUITMVER > 0)
                {
                    diag.AddDiagLine($@"ALOT version: {latestInstall.ALOTVER}.{latestInstall.ALOTUPDATEVER}.{latestInstall.ALOTHOTFIXVER}");
                    if (latestInstall.MEUITMVER != 0)
                    {
                        var meuitmName = package.DiagnosticTarget.Game == MEGame.ME1 ? @"MEUITM" : $@"MEUITM{package.DiagnosticTarget.Game.ToGameNum()}";
                        diag.AddDiagLine($@"{meuitmName} version: {latestInstall.MEUITMVER}");
                    }
                }
                else if (package.DiagnosticTarget.Game.IsOTGame())
                {
                    diag.AddDiagLine(@"This installation has been texture modded, but ALOT and/or MEUITM has not been installed");
                }
                else if (package.DiagnosticTarget.Game.IsLEGame())
                {
                    diag.AddDiagLine(@"This installation has been texture modded with MassEffectModder");
                }

                if (latestInstall.MarkerExtendedVersion >= TextureModInstallationInfo.FIRST_EXTENDED_MARKER_VERSION && !string.IsNullOrWhiteSpace(latestInstall.InstallerVersionFullName))
                {
                    diag.AddDiagLine($@"Latest installation was from performed by {latestInstall.InstallerVersionFullName}");
                }
                else if (latestInstall.ALOT_INSTALLER_VERSION_USED > 0)
                {
                    diag.AddDiagLine($@"Latest installation was from installer v{latestInstall.ALOT_INSTALLER_VERSION_USED}");
                }

                diag.AddDiagLine($@"Latest installation used MEM v{latestInstall.MEM_VERSION_USED}");
                diag.AddDiagLine(@"Texture mod installation history", LogSeverity.DIAGSECTION);
                diag.AddDiagLine(@"The history of texture mods installed into this game is as follows (from latest install to first install):");

                diag.AddDiagLine(@"Click to view list", LogSeverity.SUB);
                bool isFirst = true;
                foreach (var tmii in textureHistory)
                {
                    if (isFirst)
                        isFirst = false;
                    else
                        diag.AddDiagLine();

                    if (tmii.MarkerExtendedVersion >= TextureModInstallationInfo.FIRST_EXTENDED_MARKER_VERSION)
                    {
                        diag.AddDiagLine($@"Texture install on {tmii.InstallationTimestamp:yyyy MMMM dd h:mm:ss tt zz}", LogSeverity.BOLDBLUE);
                    }
                    else
                    {
                        diag.AddDiagLine(@"Texture install", LogSeverity.BOLDBLUE);
                    }

                    diag.AddDiagLine($@"Marker version {tmii.MarkerExtendedVersion}");
                    diag.AddDiagLine(tmii.ToString());
                    if (tmii.MarkerExtendedVersion >= 3 && !string.IsNullOrWhiteSpace(tmii.InstallerVersionFullName))
                    {
                        diag.AddDiagLine($@"Installation was from performed by {tmii.InstallerVersionFullName}");
                    }
                    else if (tmii.ALOT_INSTALLER_VERSION_USED > 0)
                    {
                        diag.AddDiagLine($@"Installation was performed by installer v{tmii.ALOT_INSTALLER_VERSION_USED}");
                    }

                    diag.AddDiagLine($@"Installed used MEM v{tmii.MEM_VERSION_USED}");

                    if (tmii.InstalledTextureMods.Any())
                    {
                        diag.AddDiagLine(@"Files installed in session:");
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

                            diag.AddDiagLine(modStr, fi.ModType == InstalledTextureMod.InstalledTextureModType.USERFILE ? LogSeverity.WARN : LogSeverity.GOOD);
                            if (fi.ChosenOptions.Any())
                            {
                                diag.AddDiagLine(@"   Chosen options for install:");
                                foreach (var c in fi.ChosenOptions)
                                {
                                    diag.AddDiagLine($@"      {c}");
                                }
                            }
                        }
                    }
                }

                diag.AddDiagLine(LogShared.END_SUB);
            }

            #endregion

            // Now setup MEM.

            #region MEM Setup
            string args = null;
            int exitcode = -1;

            // Ensure MEM NOGUI
            if (package.DiagnosticTarget != null)
            {
                hasMEM = MEMNoGuiUpdater.UpdateMEM(package.DiagnosticTarget.Game.IsOTGame(), false, setPercentDone, failedToExtractMEM, currentTaskCallback, false);
            }

            MLog.Information(@"Completed MEM fetch task");

            if (hasMEM)
            {
                // This can change hasMEM variable so it must be kept separate from the other if branch
                setMemGamePathForDiag(package);
            }
            #endregion

            // The following checks require MEM to be available.
            if (hasMEM)
            {
                #region Blacklisted mods check
                if (package.DiagnosticTarget.Game.IsOTGame())
                {
                    MLog.Information(@"Checking for mods that are known to cause problems in the scene");

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
                        diag.AddDiagLine(
                            $@"MassEffectModderNoGuiexited exited incompatible mod detection check with code {exitcode}",
                            LogSeverity.ERROR);
                    }

                    if (blacklistedMods.Any())
                    {
                        diag.AddDiagLine(@"The following blacklisted mods were found:", LogSeverity.ERROR);
                        foreach (var str in blacklistedMods)
                        {
                            diag.AddDiagLine(@" - " + str);
                        }

                        diag.AddDiagLine(@"These mods have been blacklisted by modding tools because of known issues they cause. Do not use these mods", LogSeverity.ERROR);
                    }
                    else
                    {
                        diag.AddDiagLine(@"No blacklisted mods were found installed");
                    }
                }
                #endregion

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
                    diag.AddDiagLine(@"Files added or removed after texture mods were installed", LogSeverity.DIAGSECTION);

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
                            diag.AddDiagLine(
                                $@"MassEffectModderNoGuiexited exited texture map consistency check with code {exitcode}",
                                LogSeverity.ERROR);
                        }

                        if (removedFiles.Any())
                        {
                            diag.AddDiagLine(@"The following problems were detected checking game consistency with the texture map file:", LogSeverity.ERROR);
                            foreach (var error in removedFiles)
                            {
                                diag.AddDiagLine(@" - " + error, LogSeverity.ERROR);
                            }
                        }

                        if (addedFiles.Any())
                        {
                            diag.AddDiagLine(@"The following files were added after textures were installed:", LogSeverity.ERROR);
                            foreach (var error in addedFiles)
                            {
                                diag.AddDiagLine(@" - " + error, LogSeverity.ERROR);
                            }
                        }

                        if (replacedFiles.Any())
                        {
                            diag.AddDiagLine(@"The following files were replaced after textures were installed:", LogSeverity.ERROR);
                            foreach (var error in replacedFiles)
                            {
                                diag.AddDiagLine(@" - " + error, LogSeverity.ERROR);
                            }
                        }

                        if (replacedFiles.Any() || addedFiles.Any() || removedFiles.Any())
                        {
                            diag.AddDiagLine(@"Diagnostic detected that some files were added, removed or replaced after textures were installed.", LogSeverity.ERROR);
                            diag.AddDiagLine(@"Package files cannot be installed after a texture mod is installed - the texture pointers will be wrong.", LogSeverity.ERROR);
                        }
                        else
                        {
                            diag.AddDiagLine(@"Diagnostic reports no files appear to have been added or removed since texture scan took place.");
                        }

                    }
                    else
                    {
                        diag.AddDiagLine($@"Texture map file is missing: {mapName}.bin - was game migrated to new system or are you running this tool on a different user account than textures were installed with?");
                    }
                }

                #endregion

                #region Textures - full check
                // FULL CHECK
                // LE only checks if texture modded, OT always checks
                if (package.AdvancedDiagnosticsEnabled && (package.DiagnosticTarget.TextureModded || package.DiagnosticTarget.Game.IsOTGame()))
                {
                    MLog.Information(@"Performing full texture check");
                    var param = 0;
                    package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_interp_performingFullTexturesCheckX, param)); //done this way to save a string in localization
                    diag.AddDiagLine(@"Full Textures Check", LogSeverity.DIAGSECTION);
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
                        diag.AddDiagLine($@"MassEffectModderNoGui exited full textures check with code {exitcode}", LogSeverity.ERROR);
                        if (currentProcessingFile != null)
                        {
                            diag.AddDiagLine($@"The last file processed by MassEffectModder was: {currentProcessingFile}", LogSeverity.ERROR);
                        }
                        diag.AddDiagLine();
                    }

                    package.UpdateProgressCallback?.Invoke(0);
                    package.UpdateTaskbarProgressStateCallback?.Invoke(MTaskbarState.Indeterminate);


                    if (emptyMipsNotRemoved.Any() || badTFCReferences.Any() || scanErrors.Any())
                    {
                        diag.AddDiagLine(@"Texture check reported errors", LogSeverity.ERROR);
                        if (emptyMipsNotRemoved.Any())
                        {
                            diag.AddDiagLine();
                            diag.AddDiagLine(@"The following textures contain empty mips, which typically means files were installed after texture mods were installed.:", LogSeverity.ERROR);
                            foreach (var em in emptyMipsNotRemoved)
                            {
                                diag.AddDiagLine(@" - " + em, LogSeverity.ERROR);
                            }
                        }

                        if (badTFCReferences.Any())
                        {
                            diag.AddDiagLine();
                            diag.AddDiagLine(@"The following textures have bad TFC references, which means the mods were built wrong, dependent DLC is missing, or the mod was installed wrong:", LogSeverity.ERROR);
                            foreach (var br in badTFCReferences)
                            {
                                diag.AddDiagLine(@" - " + br, LogSeverity.ERROR);
                            }
                        }

                        if (scanErrors.Any())
                        {
                            diag.AddDiagLine();
                            diag.AddDiagLine(@"The following textures failed to scan:", LogSeverity.ERROR);
                            foreach (var fts in scanErrors)
                            {
                                diag.AddDiagLine(@" - " + fts, LogSeverity.ERROR);
                            }
                        }
                    }
                    else if (exitcode != 0)
                    {
                        diag.AddDiagLine(@"Texture check failed");
                        if (memCrashText != null)
                        {
                            diag.AddDiagLine(@"MassEffectModder crashed with info:");
                            diag.AddDiagLines(memCrashText.Split("\n"), LogSeverity.ERROR); //do not localize
                        }
                    }
                    else
                    {
                        // Is this right?? We skipped check. We can't just print this
                        diag.AddDiagLine(@"Texture check did not find any texture issues in this installation");
                    }
                }
                #endregion

                #region Texture LODs
                MLog.Information(@"Collecting texture LODs");

                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingLODSettings));
                var lods = MEMIPCHandler.GetLODs(package.DiagnosticTarget.Game);
                if (lods != null)
                {
                    addLODStatusToDiag(package.DiagnosticTarget, lods, diag.AddDiagLine);
                }
                else
                {
                    diag.AddDiagLine(@"MassEffectModderNoGui exited --print-lods with error. See application log for more info.", LogSeverity.ERROR);
                }
                #endregion
            }
            else
            {
                // MEM not available.
                MLog.Warning(@"MEM not available. Multiple checks were skipped.");

                diag.AddDiagLine(@"Texture checks skipped", LogSeverity.DIAGSECTION);
                diag.AddDiagLine(@"Mass Effect Modder No Gui was not available for use when this diagnostic was run.", LogSeverity.WARN);
                diag.AddDiagLine(@"The following checks were skipped:", LogSeverity.WARN);
                diag.AddDiagLine(@" - Files replaced, added or removed after .mem texture install", LogSeverity.WARN);
                if (package.DiagnosticTarget.Game.IsOTGame())
                {
                    diag.AddDiagLine(@" - Blacklisted mods check", LogSeverity.WARN);
                }
                diag.AddDiagLine(@" - Textures check", LogSeverity.WARN);
                diag.AddDiagLine(@" - Texture LODs check", LogSeverity.WARN);
            }
        }

        /// <summary>
        /// Sets the ini to make mem run on our target. Saves old path for restoration later. This can set hasMEM to false if invalid game path is detected.
        /// </summary>
        /// <param name="package"></param>
        private void setMemGamePathForDiag(LogUploadPackage package)
        {
            var diag = package.DiagnosticWriter;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"MassEffectModder");
            memIniPath = Path.Combine(path, package.DiagnosticTarget.Game.IsLEGame() ? @"MassEffectModderLE.ini" : @"MassEffectModder.ini");

            // Set INI path to target

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            if (!File.Exists(memIniPath))
            {
                File.Create(memIniPath).Close();
            }

            var ini = DuplicatingIni.LoadIni(memIniPath);

            if (package.DiagnosticTarget.Game.IsLEGame())
            {
                oldMEMGamePath = ini[@"GameDataPath"][@"MELE"].Value;
                var rootPath = Directory.GetParent(package.DiagnosticTarget.TargetPath);
                if (rootPath != null)
                    rootPath = Directory.GetParent(rootPath.FullName);
                if (rootPath != null)
                {
                    ini[@"GameDataPath"][@"MELE"].Value = rootPath.FullName;
                }
                else
                {
                    MLog.Error($@"Invalid game directory: {package.DiagnosticTarget.TargetPath} is not part of an overall LE install");
                    diag.AddDiagLine($@"MEM diagnostics skipped: Game directory is not part of an overall LE install", LogSeverity.ERROR);
                    hasMEM = false;
                }
            }
            else
            {
                oldMEMGamePath = ini[@"GameDataPath"][package.DiagnosticTarget.Game.ToString()]?.Value;
                ini[@"GameDataPath"][package.DiagnosticTarget.Game.ToString()].Value = package.DiagnosticTarget.TargetPath;
            }

            File.WriteAllText(memIniPath, ini.ToString());
            var versInfo = FileVersionInfo.GetVersionInfo(MCoreFilesystem.GetMEMNoGuiPath(package.DiagnosticTarget.Game.IsOTGame()));
            int fileVersion = versInfo.FileMajorPart;
            diag.AddDiagLine($@"Diagnostic MassEffectModderNoGui version: {fileVersion}");

        }

        internal override void PostRunModule(LogUploadPackage package)
        {
            // Reset MEM INI
            resetMEMGamePath(package);
        }

        /// <summary>
        /// Restores the MEM game target path to its original value
        /// </summary>
        private void resetMEMGamePath(LogUploadPackage package)
        {
            if (hasMEM)
            {
                if (File.Exists(memIniPath))
                {
                    MLog.Information(@"Restoring MEM INI game path");
                    DuplicatingIni ini = DuplicatingIni.LoadIni(memIniPath);
                    ini[@"GameDataPath"][package.DiagnosticTarget.Game.ToString()].Value = oldMEMGamePath;
                    File.WriteAllText(memIniPath, ini.ToString());
                }
            }
        }

        private static void addLODStatusToDiag(GameTarget selectedDiagnosticTarget, Dictionary<string, string> lods, Action<string, LogSeverity> addDiagLine)
        {
            addDiagLine(@"Texture Level of Detail (LOD) settings", LogSeverity.DIAGSECTION);

            string iniPath = M3Directories.GetLODConfigFile(selectedDiagnosticTarget);
            if (!File.Exists(iniPath))
            {
                if (selectedDiagnosticTarget.Game.IsOTGame())
                {
                    addDiagLine($@"Game config file is missing - has game been run once?: {iniPath}", LogSeverity.WARN);
                }
                else
                {
                    addDiagLine($@"LODs are not modified (and should not be)", LogSeverity.GOOD);
                }
                return;
            }

            bool leInvalidLodsFound = false;
            foreach (KeyValuePair<string, string> kvp in lods)
            {
                if (selectedDiagnosticTarget.Game.IsLEGame())
                {
                    if (!leInvalidLodsFound && !string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        leInvalidLodsFound = true; // So we don't print multiple times
                        addDiagLine(@"Detected LOD settings configured in the LOD files - do not set these in Legendary Edition!", LogSeverity.FATAL);
                    }
                }
                else
                {
                    addDiagLine($@"{kvp.Key}={kvp.Value}", LogSeverity.INFO);
                }
            }

            if (selectedDiagnosticTarget.Game.IsOTGame())
            {
                var textureChar1024 = lods.FirstOrDefault(x => x.Key == @"TEXTUREGROUP_Character_1024");
                if (string.IsNullOrWhiteSpace(textureChar1024.Key)) //does this work for ME2/ME3??
                {
                    //not found
                    addDiagLine(@"Could not find TEXTUREGROUP_Character_1024 in config file for checking LOD settings", LogSeverity.ERROR);
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
                                    addDiagLine(@"LOD quality settings: 4K textures", LogSeverity.INFO);
                                }
                                else if (maxLodSize == 2048)
                                {
                                    addDiagLine(@"LOD quality settings: 2K textures", LogSeverity.INFO);
                                }

                                //Not Default
                                if (selectedDiagnosticTarget.TextureModded)
                                {
                                    addDiagLine(@"This installation appears to have a texture mod installed, so unused/empty mips are already removed", LogSeverity.INFO);
                                }
                                else if (maxLodSize > 1024)
                                {
                                    addDiagLine(@"Texture LOD settings appear to have been raised, but this installation has not been texture modded - game will likely have unused mip crashes.", LogSeverity.FATAL);
                                }
                            }
                            else
                            {
                                //Default ME1 LODs
                                if (selectedDiagnosticTarget.TextureModded && selectedDiagnosticTarget.HasALOTOrMEUITM())
                                {
                                    addDiagLine(HQSettingsMissingLine, LogSeverity.ERROR);
                                }
                                else
                                {
                                    addDiagLine(HQVanillaLine, LogSeverity.INFO);
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
                                        addDiagLine(@"LOD quality settings: 4K textures", LogSeverity.INFO);
                                    }
                                    else if (maxLodSize == 2048)
                                    {
                                        addDiagLine(@"LOD quality settings: 2K textures", LogSeverity.INFO);
                                    }
                                }
                                else
                                {
                                    //else if (selectedDiagnosticTarget.TextureModded) //not vanilla, but no MEM/MEUITM
                                    //{
                                    if (maxLodSize == 4096)
                                    {
                                        addDiagLine(@"LOD quality settings: 4K textures (no high res mod installed)", LogSeverity.WARN);
                                    }
                                    else if (maxLodSize == 2048)
                                    {
                                        addDiagLine(@"LOD quality settings: 2K textures (no high res mod installed)", LogSeverity.INFO);
                                    }

                                    //}
                                    if (!selectedDiagnosticTarget.TextureModded)
                                    {
                                        //no texture mod, but has set LODs
                                        addDiagLine(@"LODs have been explicitly set, but a texture mod is not installed - game may have black textures as empty mips may not be removed", LogSeverity.WARN);
                                    }
                                }
                            }
                            else //default
                            {
                                //alot/meuitm, but vanilla settings.
                                if (selectedDiagnosticTarget.TextureModded &&
                                    selectedDiagnosticTarget.HasALOTOrMEUITM())
                                {
                                    addDiagLine(HQSettingsMissingLine, LogSeverity.ERROR);
                                }
                                else //no alot/meuitm, vanilla setting.
                                {
                                    addDiagLine(HQVanillaLine, LogSeverity.INFO);
                                }
                            }

                            break;
                    }
                }
                catch (Exception e)
                {
                    MLog.Error(@"Error checking LOD settings: " + e.Message);
                    addDiagLine($@"Error checking LOD settings: {e.Message}", LogSeverity.INFO);
                }
            }
            else if (!leInvalidLodsFound)
            {
                addDiagLine($@"LODs are not modified (and should not be)", LogSeverity.GOOD);
            }
        }
    }
}