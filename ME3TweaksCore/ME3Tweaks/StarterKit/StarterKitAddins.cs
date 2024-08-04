﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.Coalesced;
using LegendaryExplorerCore.Coalesced.Xml;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Gammtek.Extensions;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Packages.CloningImportingAndRelinking;
using LegendaryExplorerCore.Textures;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.Classes;
using LegendaryExplorerCore.Unreal.ObjectInfo;
using LegendaryExplorerCore.UnrealScript;
using LegendaryExplorerCore.UnrealScript.Compiling.Errors;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.ME3Tweaks.M3Merge;
using ME3TweaksCore.ME3Tweaks.M3Merge.Bio2DATable;
using ME3TweaksCore.ME3Tweaks.ModManager.Interfaces;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Objects;
using ME3TweaksCore.Services.Backup;
using ME3TweaksCore.Targets;
using Newtonsoft.Json;
using static LegendaryExplorerCore.Unreal.CNDFile;

namespace ME3TweaksCore.ME3Tweaks.StarterKit
{
    /// <summary>
    /// Info about what to add to a coalesced file
    /// </summary>
    internal class CoalescedEntryInfo
    {
        public string File { get; set; }
        public string Section { get; set; }
        public CoalesceProperty Property { get; set; }
    }
    /// <summary>
    /// Adds addition features to a DLC mod
    /// </summary>
    public class StarterKitAddins
    {
        #region RESOURCES
        private const string LE1ModSettingsClassTextAsset = @"ME3TweaksCore.ME3Tweaks.StarterKit.LE1.Classes.ModSettingsSubmenu.uc";
        private const string LE3ModSettingsClassTextAsset = @"ME3TweaksCore.ME3Tweaks.StarterKit.LE3.Classes.SFXGUIData_ModSettings.uc";
        #endregion

        #region STARTUP FILE
        /// <summary>
        /// Generates a startup file for the specified game
        /// </summary>
        /// <param name="game">Game to generate for. Cannot generate ME1 startup files</param>
        /// <param name="dlcFolderPath">The path to the root of the DLC folder</param>
        public static void AddStartupFile(MEGame game, string dlcFolderPath)
        {
            if (game == MEGame.ME1)
            {
                MLog.Error(@"Cannot add startup file to ME1.");
                return;
            }
            MLog.Information($@"Adding startup file to {dlcFolderPath}. Game: {game}");
            var dlcName = Path.GetFileName(dlcFolderPath);
            var cookedPath = Path.Combine(dlcFolderPath, game.CookedDirName());
            var startupFName = GetStartupFilename(game, dlcName);

            var startupPackagePath = Path.Combine(cookedPath, startupFName);
            if (File.Exists(startupPackagePath))
            {
                MLog.Warning($@"A startup file already exists: {startupPackagePath}. Not regenerating.");
                return;
            }

            using var package = MEPackageHandler.CreateAndOpenPackage(startupPackagePath, game, true);
            CreateObjectReferencer(package, true);
            package.Save();

            if (game == MEGame.LE1)
            {
                // Add to autoload
                AddAutoloadReferenceGame1(dlcFolderPath, @"Packages", @"GlobalPackage", Path.GetFileNameWithoutExtension(startupFName));
            }
            else
            {
                // Add it to coalesced so it gets used
                AddCoalescedReference(game, dlcName, cookedPath, @"BioEngine", @"Engine.StartupPackages", @"DLCStartupPackage", Path.GetFileNameWithoutExtension(startupFName).StripUnrealLocalization(), CoalesceParseAction.AddUnique);
            }
        }

        private static string GetStartupFilename(MEGame game, string dlcName)
        {
            var startupFName = $@"Startup{dlcName.Substring(3)}.pcc"; // "DLC|_"
            if (game.IsGame3())
            {
                startupFName = $@"Startup{dlcName.Substring(3)}_INT.pcc"; // "DLC|_" // Required for plot manager to work properly for some reason.
            }
            return startupFName;
        }


        /// <summary>
        /// Adds an entry to Autoload.ini if it doesn't already exist
        /// </summary>
        /// <param name="dlcRootPath">The path of the DLC dir</param>
        private static void AddAutoloadReferenceGame1(string dlcRootPath, string section, string key, string value, bool isIndexed = true)
        {
            // Load autoload
            var autoload = Path.Combine(dlcRootPath, @"Autoload.ini");
            var ini = DuplicatingIni.LoadIni(autoload);

            var packageHeading = ini.GetOrAddSection(section);
            if (!isIndexed)
            {
                packageHeading.SetSingleEntry(key, value);
            }
            else
            {
                // Loop to find entry
                int i = 0;
                string indexedKey;
                while (true)
                {
                    // Loop to find existing value or if not found just add it

                    i++;
                    indexedKey = $@"{key}{i}";
                    var foundVal = packageHeading.GetValue(indexedKey);
                    if (foundVal == null)
                    {
                        // Add it
                        packageHeading.SetSingleEntry(indexedKey, value);
                        break;
                    }
                    else
                    {
                        // Check it
                        if (foundVal.Value == value)
                            return; // Doesn't need added
                    }

                }
            }

            // Reserialize
            File.WriteAllText(autoload, ini.ToString());
        }
        #endregion

        #region Squadmate Merge

        /// <summary>
        /// Generates squadmate outfit merge files for the specified henchmen
        /// </summary>
        /// <param name="game">The game to generate for</param>
        /// <param name="henchName">The internal name of the henchman</param>
        /// <param name="dlcFolderPath">The path to the DLC folder root to modify</param>
        /// <param name="outfits">The list of outfits to append to</param>
        /// <param name="getGamePatchModFolder">Delegate that is invoked when trying to layer the main patch mod for the game's files over the top of BioWares, as that is preferable</param>
        /// <returns>Error if failed, null if OK</returns>
        public static string GenerateSquadmateMergeFiles(MEGame game, string henchName, string dlcFolderPath, SQMOutfitMerge.SquadmateMergeInfo outfits, Func<MEGame, string> getGamePatchModFolder)
        {
            // Setup
            var dlcName = Path.GetFileName(dlcFolderPath);
            var henchHumanName = GetHumanName(henchName);
            var cookedPath = Path.Combine(dlcFolderPath, game.CookedDirName());
            var sourcefiles = new List<string>();
            var sourceBaseDir = BackupService.GetGameBackupPath(game);
            if (sourceBaseDir == null || !Directory.Exists(sourceBaseDir))
            {
                MLog.Warning($@"No backup available for {game}");
                return LC.GetString(LC.string_interp_sk_sqmNoBackup, game);
            }
            var sourceBaseFiles = MELoadedFiles.GetFilesLoadedInGame(game, true, gameRootOverride: sourceBaseDir);

            // Try to source from game patch if possible.
            if (getGamePatchModFolder != null)
            {
                var gamePatchModFolder = getGamePatchModFolder(game);
                if (gamePatchModFolder != null)
                {
                    MLog.Information($@"Layering patch DLC mod files over backup files for source priority: {gamePatchModFolder}");
                    MUtilities.LayerFolderOverLoadedFiles(game, sourceBaseFiles, gamePatchModFolder);
                }
            }
#if DEBUG
            var filesDebug = sourceBaseFiles.Where(x => x.Key.StartsWith(@"BioH_")).Select(x => x.Key).ToList();
#endif

            // File list
            // Main
            if (game == MEGame.LE2)
            {
                // We use 01 as they are subclassed for unrealscript type checking safety against the base class.
                sourcefiles.Add($@"BioH_{henchName}_01.pcc"); // Used everywhere but SM
                sourcefiles.Add($@"BioH_END_{henchName}_01.pcc"); // Used at suicide mission
            }
            else if (game.IsGame3())
            {
                sourcefiles.Add($@"BioH_{henchName}_00.pcc");
                sourcefiles.Add($@"BioH_{henchName}_00_Explore.pcc");
            }

            // Localizations
            foreach (var f in sourcefiles.ToList()) // To list for concurrent modification exception
            {
                foreach (var lang in GameLanguage.GetVOLanguagesForGame(game))
                {
                    sourcefiles.Add($@"{Path.GetFileNameWithoutExtension(f)}_LOC_{lang.FileCode}.pcc");
                }
            }

            // Step 1: Verify files

            foreach (var f in sourcefiles)
            {
                if (!sourceBaseFiles.TryGetValue(f, out var _))
                {
                    MLog.Warning($@"Required file for squadmate merge not available in backup: {f}");
                    return LC.GetString(LC.string_interp_sk_sqmBackupMissingRequiredFile, f);
                }
            }

            // LE2 doesn't have these. We will generate it ourselves
            var isourcefname = $@"SFXHenchImages{henchHumanName}0.pcc";
            if (game.IsGame3())
            {
                if (!sourceBaseFiles.TryGetValue(isourcefname, out var _))
                {
                    MLog.Warning($@"Required file for squadmate merge not available in backup: {isourcefname}");
                    return LC.GetString(LC.string_interp_sk_sqmBackupMissingRequiredFile, isourcefname);
                }
            }

            MLog.Information(@"Squadmate merge generator: all required source files found in backup or patch");

            var newHenchIndex = StarterKitAddins.GetNumOutfitsForHenchInDLC(henchName, dlcName, cookedPath);
            var dlcNameHenchIndex = GetIndexedHenchString(dlcName, newHenchIndex); ;
            // Step 2: Copy files
            foreach (var f in sourcefiles)
            {
                var path = sourceBaseFiles[f];
                var destFName = Path.GetFileName(f);
                if (game.IsGame3())
                {
                    if (newHenchIndex > 0)
                    {
                        // we use 1 based indexing. We are based on the _01 files - strip that off for our naming.
                        destFName = destFName.Replace(@"_00", $@"_{newHenchIndex.ToString().PadLeft(2, '0')}");
                    }
                    else
                    {
                        // First instance of this hench is not indexed
                        destFName = destFName.Replace(@"_00", @"");
                    }
                }
                else if (game == MEGame.LE2)
                {
                    if (newHenchIndex > 0)
                    {
                        // we use 1 based indexing. We are based on the _01 files - strip that off for our naming.
                        destFName = destFName.Replace(@"_01", $@"_{newHenchIndex.ToString().PadLeft(2, '0')}");
                    }
                    else
                    {
                        // First instance of this hench is not indexed
                        destFName = destFName.Replace(@"_01", @"");
                    }
                }
                destFName = destFName.Replace(henchName, $@"{henchName}_{dlcName}");

                var destpath = Path.Combine(cookedPath, destFName);

                MLog.Information($@"Building squadmate merge asset using source file {path}");
                using var package = MEPackageHandler.OpenMEPackage(path);
                if (package.Localization == MELocalization.None)
                {
                    if (game.IsGame3())
                    {
                        // Todo: Update indexing for multi-outfit
                        ReplaceNameIfExists(package, $@"BioH_{henchName}_00", $@"BioH_{henchName}_{dlcNameHenchIndex}");
                        ReplaceNameIfExists(package, $@"BioH_{henchName}_00_Explore", $@"BioH_{henchName}_{dlcNameHenchIndex}_Explore");
                        ReplaceNameIfExists(package, @"VariantA", $@"Variant{dlcName}");
                        ReplaceNameIfExists(package, $@"{henchHumanName}A_Combat", $@"{henchHumanName}{dlcNameHenchIndex}_Combat");
                        ReplaceNameIfExists(package, $@"{henchHumanName}A_EX_Combat", $@"{henchHumanName}{dlcNameHenchIndex}_EX_Combat");
                        ReplaceNameIfExists(package, $@"{henchHumanName}A_Conversation", $@"{henchHumanName}{dlcNameHenchIndex}_Conversation");
                    }
                    else if (game == MEGame.LE2)
                    {
                        var actors = package.GetLevelActors();

                        var pawn = actors.FirstOrDefault(x => x.IsA(@"SFXPawn"));
                        var pawnClass = pawn.Class as ExportEntry;
                        var pawnClassDefaults = pawnClass.GetDefaults();
                        var actorType = pawnClassDefaults.GetProperty<ObjectProperty>(@"ActorType").ResolveToEntry(package) as ExportEntry;

                        // Replace class and default names
                        ReplaceNameIfExists(package, pawnClass.ObjectName, $@"{pawnClass.ObjectName.Name.Replace(@"_01", "")}_{dlcNameHenchIndex}"); // do not localize
                        ReplaceNameIfExists(package, pawnClassDefaults.ObjectName, $@"{pawnClassDefaults.ObjectName.Name.Replace(@"_01", "")}_{dlcNameHenchIndex}"); // do not localize
                        actorType.ObjectName = $@"{actorType.ObjectName.Name.Replace(@"_01", "")}_{dlcNameHenchIndex.ToLower()}"; // do not localize

                        // Replace package export name.
                        ReplaceNameIfExists(package, package.FileNameNoExtension, $@"{package.FileNameNoExtension[..^3]}_{dlcNameHenchIndex}");
                        ReplaceNameIfExists(package, @"SFXGamePawns", $@"SFXGamePawns_{dlcName}");
                    }
                }

                package.Save(destpath);
            }

            // Step 3: Add hench images package

            if (game.IsGame3())
            {
                InstallGame3Images(game, cookedPath, henchHumanName, dlcName, sourceBaseFiles, isourcefname, newHenchIndex);
            }
            else if (game == MEGame.LE2)
            {
                InstallLE2Images(game, cookedPath, henchHumanName, dlcName, sourceBaseFiles, isourcefname, newHenchIndex);
            }

            // Step 4: Add squadmate outfit merge to the list
            var outfit = new SquadmateInfoSingle();
            outfit.HenchName = henchName;
            outfit.HenchPackage = $@"BioH_{henchName}_{dlcNameHenchIndex}";

            if (game.IsGame3())
            {
                outfit.HighlightImage = GetIndexedHenchString($@"GUI_Henchmen_Images_{dlcName}.{henchHumanName}Glow", newHenchIndex);
                outfit.AvailableImage = GetIndexedHenchString($@"GUI_Henchmen_Images_{dlcName}.{henchHumanName}", newHenchIndex);
                outfit.SilhouetteImage = GetIndexedHenchString($@"GUI_Henchmen_Images_{dlcName}.{henchHumanName}_locked", newHenchIndex);
                outfit.DescriptionText0 = 0;
                outfit.CustomToken0 = 0;
            }
            else if (game == MEGame.LE2)
            {
                outfit.HighlightImage = GetIndexedHenchString($@"{henchHumanName}Glow", newHenchIndex);
                outfit.AvailableImage = GetIndexedHenchString(henchHumanName, newHenchIndex);
            }

            outfits.Outfits.Add(outfit);

            if (outfits.Outfits.Any())
            {
                // Write the .sqm file
                var sqmPath = Path.Combine(dlcFolderPath, game.CookedDirName(), SQMOutfitMerge.SQUADMATE_MERGE_MANIFEST_FILE);
                var sqmText = JsonConvert.SerializeObject(outfits, Formatting.Indented, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore });
                File.WriteAllText(sqmPath, sqmText);
            }

            return null;
        }

        /// <summary>
        /// Gets number of henchmen outfits, assuming they follow standard rule: BioH_Hench_DLCName.pcc, then following BioH_Hench_DLCName_01, 02... 
        /// </summary>
        /// <param name="henchName"></param>
        /// <param name="dlcName"></param>
        /// <param name="cookedPath"></param>
        /// <returns></returns>
        public static int GetNumOutfitsForHenchInDLC(string henchName, string dlcName, string cookedPath)
        {
            var baseFile = $@"BioH_{henchName}_{dlcName}";
            var testPath = Path.Combine(cookedPath, baseFile + @".pcc");
            int henchCount = 0;
            if (File.Exists(testPath))
            {
                // Find next available index.
                henchCount = 1;
                while (henchCount < 32)
                {
                    // We cap at 32 even in Game3
                    testPath = Path.Combine(cookedPath, baseFile + @"_" + henchCount.ToString(@"00") + @".pcc");
                    if (!File.Exists(testPath))
                        break;
                    henchCount++;
                }
            }

            return henchCount;
        }

        /// <summary>
        /// Adds _XX on the end of names if index is above 0.
        /// </summary>
        /// <param name="inStr"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        private static string GetIndexedHenchString(string inStr, int index)
        {
            if (index == 0)
                return inStr;

            return $@"{inStr}_{index:00}";
        }

        private static void InstallLE2Images(MEGame game, string cookedPath, string henchHumanName, string dlcName, CaseInsensitiveDictionary<string> sourceBaseFiles, string isourcefname, int newHenchIndex)
        {
            var idestpath = Path.Combine(cookedPath, $@"SFXHenchImages_{dlcName}.pcc");
            if (File.Exists(idestpath))
            {
                // Edit existing package
                using var ipackage = MEPackageHandler.OpenMEPackage(idestpath);
                var texToClone = ipackage.Exports.FirstOrDefault(x => x.ClassName == @"Texture2D");

                // Available
                var exp = EntryCloner.CloneEntry(texToClone);
                exp.ObjectName = new NameReference(GetIndexedHenchString(henchHumanName, newHenchIndex), 0);
                var t2d = new Texture2D(exp);
                var imageBytes = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.StarterKit.LE2.HenchImages.placeholder_unselected.png").GetBuffer();
                t2d.Replace(Image.LoadFromFileMemory(imageBytes, 2, PixelFormat.DXT5), exp.GetProperties(), isPackageStored: true);

                // Chosen
                exp = EntryCloner.CloneEntry(texToClone);
                exp.ObjectName = new NameReference(GetIndexedHenchString($@"{henchHumanName}Glow", newHenchIndex), 0);
                t2d = new Texture2D(exp);
                imageBytes = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.StarterKit.LE2.HenchImages.placeholder_selected.png").GetBuffer();
                t2d.Replace(Image.LoadFromFileMemory(imageBytes, 2, PixelFormat.DXT5), exp.GetProperties(), isPackageStored: true);
                ipackage.Save();
            }
            else
            {
                // Generate new package
                using var ipackage = MEPackageHandler.CreateAndOpenPackage(idestpath, MEGame.LE2);

                // Setup the first texture from nothing.
                ExportEntry exp = Texture2D.CreateTexture(ipackage, GetIndexedHenchString(henchHumanName, newHenchIndex), 8, 16, PixelFormat.DXT5, false); // We just specify correct aspect ratio as the replacement code will properly do it for us.

                // Available
                var t2d = new Texture2D(exp);
                var imageBytes = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.StarterKit.LE2.HenchImages.placeholder_unselected.png").GetBuffer();
                t2d.Replace(Image.LoadFromFileMemory(imageBytes, 2, PixelFormat.DXT5), exp.GetProperties(), isPackageStored: true);
                exp.WriteProperty(new BoolProperty(false, @"sRGB"));

                // Chosen
                exp = Texture2D.CreateTexture(ipackage, GetIndexedHenchString($@"{henchHumanName}Glow", newHenchIndex), 8, 16, PixelFormat.DXT5, false); // We just specify correct aspect ratio as the replacement code will properly do it for us.
                t2d = new Texture2D(exp);
                imageBytes = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.StarterKit.LE2.HenchImages.placeholder_selected.png").GetBuffer();
                t2d.Replace(Image.LoadFromFileMemory(imageBytes, 2, PixelFormat.DXT5), exp.GetProperties(), isPackageStored: true);
                exp.WriteProperty(new BoolProperty(false, @"sRGB"));

                ipackage.Save(idestpath);
            }
        }

        private static void InstallGame3Images(MEGame game, string cookedPath, string henchHumanName, string dlcName,
            CaseInsensitiveDictionary<string> sourceBaseFiles, string isourcefname, int newHenchIndex)
        {
            var idestpath = Path.Combine(cookedPath, $@"SFXHenchImages_{dlcName}.pcc");
            if (File.Exists(idestpath))
            {
                // Edit existing package
                using var ipackage = MEPackageHandler.OpenMEPackage(idestpath);
                var texToClone = ipackage.Exports.FirstOrDefault(x => x.ClassName == @"Texture2D");

                // Available
                var exp = EntryCloner.CloneEntry(texToClone);
                AddToObjectReferencer(exp);
                exp.ObjectName = new NameReference(GetIndexedHenchString($@"{henchHumanName}", newHenchIndex), 0);
                var t2d = new Texture2D(exp);
                var imageBytes = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.StarterKit.LE3.HenchImages.placeholder_available.png").GetBuffer();
                t2d.Replace(Image.LoadFromFileMemory(imageBytes, 2, PixelFormat.ARGB), exp.GetProperties(), isPackageStored: true);

                // Silouette
                exp = EntryCloner.CloneEntry(texToClone);
                AddToObjectReferencer(exp);
                exp.ObjectName = new NameReference(GetIndexedHenchString($@"{henchHumanName}_locked", newHenchIndex), 0);
                t2d = new Texture2D(exp);
                imageBytes = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.StarterKit.LE3.HenchImages.placeholder_silo.png").GetBuffer();
                t2d.Replace(Image.LoadFromFileMemory(imageBytes, 2, PixelFormat.ARGB), exp.GetProperties(), isPackageStored: true);

                // Chosen
                exp = EntryCloner.CloneEntry(texToClone);
                AddToObjectReferencer(exp);
                exp.ObjectName = new NameReference(GetIndexedHenchString($@"{henchHumanName}Glow", newHenchIndex), 0);
                t2d = new Texture2D(exp);
                imageBytes = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.StarterKit.LE3.HenchImages.placeholder_chosen.png").GetBuffer();
                t2d.Replace(Image.LoadFromFileMemory(imageBytes, 2, PixelFormat.ARGB), exp.GetProperties(), isPackageStored: true);

                ipackage.Save();
            }
            else
            {
                // If new package we don't need to use our indexing code for hench names.

                // Generate new package
                using var ipackage = MEPackageHandler.OpenMEPackage(sourceBaseFiles[isourcefname]);

                // Available
                var exp = ipackage.FindExport($@"GUI_Henchmen_Images.{henchHumanName}0");
                var t2d = new Texture2D(exp);
                var imageBytes = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.StarterKit.LE3.HenchImages.placeholder_available.png").GetBuffer();
                t2d.Replace(Image.LoadFromFileMemory(imageBytes, 2, PixelFormat.ARGB), exp.GetProperties(), isPackageStored: true);
                exp.ObjectName = $@"{henchHumanName}";

                // Silouette
                exp = ipackage.FindExport($@"GUI_Henchmen_Images.{henchHumanName}0_locked");
                t2d = new Texture2D(exp);
                imageBytes = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.StarterKit.LE3.HenchImages.placeholder_silo.png").GetBuffer();
                t2d.Replace(Image.LoadFromFileMemory(imageBytes, 2, PixelFormat.ARGB), exp.GetProperties(), isPackageStored: true);
                exp.ObjectName = $@"{henchHumanName}_locked";

                // Chosen
                exp = ipackage.FindExport($@"GUI_Henchmen_Images.{henchHumanName}0Glow");
                t2d = new Texture2D(exp);
                imageBytes = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.StarterKit.LE3.HenchImages.placeholder_chosen.png").GetBuffer();
                t2d.Replace(Image.LoadFromFileMemory(imageBytes, 2, PixelFormat.ARGB), exp.GetProperties(), isPackageStored: true);
                exp.ObjectName = $@"{henchHumanName}Glow";

                ReplaceNameIfExists(ipackage, $@"GUI_Henchmen_Images", $@"GUI_Henchmen_Images_{dlcName}");
                ReplaceNameIfExists(ipackage, $@"SFXHenchImages{henchHumanName}0", $@"SFXHenchImages_{dlcName}");
                ipackage.Save(idestpath);
            }
        }

        public static string GetHumanName(string henchName)
        {
            // Game3
            if (henchName == @"Marine") return @"James";

            // LE2
            if (henchName == @"Vixen") return @"Miranda";
            if (henchName == @"Leading") return @"Jacob";
            if (henchName == @"Professor") return @"Mordin";
            if (henchName == @"Convict") return @"Jack";
            if (henchName == @"Mystic") return @"Samara";
            if (henchName == @"Assassin") return @"Thane";
            if (henchName == @"Geth") return @"Legion";
            if (henchName == @"Thief") return @"Kasumi";
            if (henchName == @"Veteran") return @"Zaeed";

            return henchName;
        }

        private static void ReplaceNameIfExists(IMEPackage package, string originalName, string newName)
        {
            var idx = package.findName(originalName);
            if (idx >= 0)
            {
                package.replaceName(idx, newName);
            }
        }
        #endregion

        #region Plot Manager 
        public static void GeneratePlotData(GameTarget target, string dlcFolderPath)
        {
            // Startup file
            var dlcName = Path.GetFileName(dlcFolderPath);
            var cookedPath = Path.Combine(dlcFolderPath, target.Game.CookedDirName());

            #region GAME 1
            if (target.Game.IsGame1())
            {
                // Plot Manager
                var plotManName = $@"PlotManager{dlcName}";
                var plotManF = Path.Combine(dlcFolderPath, target.Game.CookedDirName(), $@"{plotManName}{target.Game.PCPackageFileExtension()}");
                if (File.Exists(plotManF))
                {
                    MLog.Warning($@"PlotManager file {plotManF} already exists - not generating file or updating autoload");
                }
                else
                {
                    MEPackageHandler.CreateAndSavePackage(plotManF, target.Game);
                    // PlotManager needs added since it forces it into memory (in vanilla) for long enough to be referenced
                    AddAutoloadReferenceGame1(dlcFolderPath, @"Packages", @"GlobalPackage", plotManName);
                    AddAutoloadReferenceGame1(dlcFolderPath, @"Packages", @"PlotManagerConditionals", $@"{AddConditionalsClass(target, plotManF, dlcName)}.BioAutoConditionals");
                }

                // Plot Manager Auto
                var plotManAutoName = $@"PlotManagerAuto{dlcName}";
                var plotManAutoF = Path.Combine(dlcFolderPath, target.Game.CookedDirName(), $@"{plotManAutoName}{target.Game.PCPackageFileExtension()}");
                if (File.Exists(plotManAutoF))
                {
                    MLog.Warning($@"PlotManager file {plotManF} already exists - not generating file or updating autoload");
                }
                else
                {
                    MEPackageHandler.CreateAndSavePackage(plotManAutoF, target.Game);
                    AddPlotManagerAuto(plotManAutoF, dlcName);
                    AddAutoloadReferenceGame1(dlcFolderPath, @"Packages", @"PlotManagerStateTransitionMap", $@"{plotManAutoName}.StateTransitionMap");
                    AddAutoloadReferenceGame1(dlcFolderPath, @"Packages", @"PlotManagerConsequenceMap", $@"{plotManAutoName}.ConsequenceMap");
                    AddAutoloadReferenceGame1(dlcFolderPath, @"Packages", @"PlotManagerOutcomeMap", $@"{plotManAutoName}.OutcomeMap");
                    AddAutoloadReferenceGame1(dlcFolderPath, @"Packages", @"PlotManagerQuestMap", $@"{plotManAutoName}.QuestMap");
                    AddAutoloadReferenceGame1(dlcFolderPath, @"Packages", @"PlotManagerCodexMap", $@"{plotManAutoName}.DataCodexMap");
                }
            }
            #endregion

            #region GAME 2 and GAME 3
            if (target.Game.IsGame2() || target.Game.IsGame3())
            {
                var startupFName = GetStartupFilename(target.Game, dlcName);
                var startupPackagePath = Path.Combine(cookedPath, startupFName);
                if (!File.Exists(startupPackagePath))
                {
                    // Generate startup file (it's required)
                    AddStartupFile(target.Game, dlcFolderPath);
                }

                if (target.Game.IsGame2())
                {
                    // We need to add the conditionals
                    var plotManagerPackageName = AddConditionalsClass(target, startupPackagePath, dlcName);
                    AddCoalescedReference(target.Game, dlcName, cookedPath, @"BioEngine", @"Engine.StartupPackages", @"Package", plotManagerPackageName, CoalesceParseAction.AddUnique);

                    var bio2daPackageName = AddBio2DAGame2(target.Game, dlcName, startupPackagePath);
                    AddCoalescedReference(target.Game, dlcName, cookedPath, @"BioEngine", @"Engine.StartupPackages", @"Package", bio2daPackageName, CoalesceParseAction.AddUnique);


                    // Must also add to biogame
                    AddCoalescedReference(target.Game, dlcName, cookedPath, @"BioGame", @"SFXGame.BioWorldInfo", @"ConditionalClasses", $@"{plotManagerPackageName}.BioAutoConditionals", CoalesceParseAction.AddUnique);
                }

                // Generate the maps
                var plotAutoPackageName = AddPlotManagerAuto(startupPackagePath, dlcName);

                // Add to Coalesced
                AddCoalescedReference(target.Game, dlcName, cookedPath, @"BioEngine", @"Engine.StartupPackages", @"Package", plotAutoPackageName, CoalesceParseAction.AddUnique);

                if (target.Game.IsGame3())
                {
                    // Additional coalesced entry
                    // Should include localization of _INT
                    AddCoalescedReference(target.Game, dlcName, cookedPath, @"BioEngine", @"Engine.StartupPackages", @"dlcstartuppackagename", Path.GetFileNameWithoutExtension(startupPackagePath), CoalesceParseAction.AddUnique);

                    // Conditionals file
                    CNDFile c = new CNDFile();
                    c.ConditionalEntries = new List<ConditionalEntry>();
                    c.ToFile(Path.Combine(dlcFolderPath, target.Game.CookedDirName(), $@"Conditionals{Path.GetFileName(dlcFolderPath)}.cnd"));
                }
            }
            #endregion
        }

        private static string AddBio2DAGame2(MEGame game, string dlcName, string startupPackagePath)
        {
            using var startupFile = MEPackageHandler.OpenMEPackage(startupPackagePath);
            var plotPackageName = $@"BIOG_2DA_{dlcName.Substring(4)}_PlotManager_X"; // DLC_|<stuff>
            var plotPackageExport = startupFile.FindExport(plotPackageName);
            if (plotPackageExport == null)
            {
                // Create the export
                plotPackageExport = ExportCreator.CreatePackageExport(startupFile, plotPackageName);
            }

            var rand = new Random();
            var cols = new[] { @"Id", @"Credits", @"Eezo", @"Palladium", @"Platinum", @"Iridium", @"Description" };
            Create2DA(plotPackageExport, new NameReference(@"Plot_Treasure_Resources_part", rand.Next(100000) + 1000), cols, true);

            cols = new[] { @"nmLevel", @"nmTreasure", @"nmTech", @"nmResource", @"nPrice", @"nmRequiredTech", @"nRequiredTechLevel", @"nDiscoverTechLevel", @"nNoAnimation", @"nMultiLevel" };
            Create2DA(plotPackageExport, new NameReference(@"Plot_Treasure_Treasure_part", rand.Next(100000) + 1000), cols, false);

            if (startupFile.IsModified)
                startupFile.Save();
            return plotPackageName;
        }

        private static ExportEntry Create2DA(IEntry parent, NameReference objectName, string[] columns, bool isStandard2DA)
        {
            // Test if exists first - do not overwrite
            var fulltest = $@"{parent.InstancedFullPath}.{objectName}";
            var exp = parent.FileRef.FindExport(fulltest);
            if (exp != null)
                return exp; // Return existing

            // Generate it 
            var className = isStandard2DA ? @"Bio2DA" : @"Bio2DANumberedRows";
            var rop = new RelinkerOptionsPackage { ImportExportDependencies = true };
            exp = new ExportEntry(parent.FileRef, parent, objectName)
            { Class = EntryImporter.EnsureClassIsInFile(parent.FileRef, className, rop) };

            exp.ObjectFlags |= UnrealFlags.EObjectFlags.Public | UnrealFlags.EObjectFlags.LoadForClient | UnrealFlags.EObjectFlags.LoadForServer | UnrealFlags.EObjectFlags.Standalone;
            exp.ExportFlags |= UnrealFlags.EExportFlags.ForcedExport;
            // Since table is blank we don't need to care about the column names property
            Bio2DA bio2DA = new Bio2DA();
            bio2DA.Cells = new Bio2DACell[0, 0];
            foreach (var c in columns)
            {
                bio2DA.AddColumn(c);
            }
            bio2DA.Write2DAToExport(exp);

            parent.FileRef.AddExport(exp);
            return exp;
        }


        /// <summary>
        /// Adds BioAutoConditionals class
        /// </summary>
        /// <param name="startupPackagePath"></param>
        /// <param name="dlcName"></param>
        /// <returns>Name of package export 'PlotManager[DLCNAME]' that is added to coalesced</returns>
        private static string AddConditionalsClass(GameTarget target, string startupPackagePath, string dlcName)
        {
            using var startupFile = MEPackageHandler.OpenMEPackage(startupPackagePath);
            var sfPlotExportName = $@"PlotManager{dlcName}";

            ExportEntry sfPlotExport = null;
            if (!startupFile.Game.IsGame1())
            {
                // Game 1 uses package root
                sfPlotExport = startupFile.FindExport(sfPlotExportName);
                if (sfPlotExport == null)
                {
                    // Create the export
                    sfPlotExport = ExportCreator.CreatePackageExport(startupFile, sfPlotExportName);
                }
            }

            var usop = new UnrealScriptOptionsPackage()
            {
                Cache = new TargetPackageCache { RootPath = target.GetBioGamePath() },
                GamePathOverride = target.TargetPath
            };

            var lib = new FileLib(startupFile);
            lib.Initialize(usop);
            var scriptText = @"Class BioAutoConditionals extends BioConditionals; public function bool FTemplateFunction(BioWorldInfo bioWorld, int Argument){ local BioGlobalVariableTable gv; gv = bioWorld.GetGlobalVariables(); return TRUE; } defaultproperties { }";
            UnrealScriptCompiler.CompileClass(startupFile, scriptText, lib, usop, parent: sfPlotExport);

            if (startupFile.Game.IsGame1())
            {
                // Remove forcedexport flag on class and defaults
                var classExp = startupFile.FindExport(@"BioAutoConditionals");
                classExp.ExportFlags &= ~UnrealFlags.EExportFlags.ForcedExport;

                classExp = startupFile.FindExport(@"Default__BioAutoConditionals");
                classExp.ExportFlags &= ~UnrealFlags.EExportFlags.ForcedExport;
            }

            if (startupFile.IsModified)
                startupFile.Save();

            return sfPlotExportName;
        }

        /// <summary>
        /// Adds plot manager maps (codex, consequence, journal, etc)
        /// </summary>
        /// <param name="startupPackagePath"></param>
        /// <param name="dlcName"></param>
        /// <returns>'PlotManagerAuto[DLCName]' for adding to startup packages</returns>
        private static string AddPlotManagerAuto(string startupPackagePath, string dlcName)
        {
            using var packageFile = MEPackageHandler.OpenMEPackage(startupPackagePath);
            CreateObjectReferencer(packageFile, true); // Ensures there is an object referencer available for use
            ExportEntry sfPlotExport = null;
            if (!packageFile.Game.IsGame1())
            {
                // In game 1 we use non-forced export and just make it a package file instead. At least thats how ME1 did it and Pinnacle Station for LE1
                var sfPlotExportName = $@"PlotManagerAuto{dlcName}";
                sfPlotExport = packageFile.FindExport(sfPlotExportName);
                if (sfPlotExport == null)
                {
                    // Create the export
                    sfPlotExport = ExportCreator.CreatePackageExport(packageFile, sfPlotExportName);
                }
            }
            // Generate the map exports
            AddToObjectReferencer(GeneratePlotManagerAutoExport(packageFile, sfPlotExport, @"DataCodexMap", @"BioCodexMap", 2));
            var consequenceMapClass = packageFile.Game.IsGame1() ? @"BioStateEventMap" : @"BioConsequenceMap";
            AddToObjectReferencer(GeneratePlotManagerAutoExport(packageFile, sfPlotExport, @"ConsequenceMap", consequenceMapClass, 1));
            AddToObjectReferencer(GeneratePlotManagerAutoExport(packageFile, sfPlotExport, @"OutcomeMap", @"BioOutcomeMap", 1));
            AddToObjectReferencer(GeneratePlotManagerAutoExport(packageFile, sfPlotExport, @"QuestMap", @"BioQuestMap", 4)); // Journal
            AddToObjectReferencer(GeneratePlotManagerAutoExport(packageFile, sfPlotExport, @"StateTransitionMap", @"BioStateEventMap", 1));
            packageFile.Save();

            return sfPlotExport?.ObjectName.Name ?? Path.GetFileNameWithoutExtension(startupPackagePath);
        }


        /// <summary>
        /// Generates one of the plot manager auto exports
        /// </summary>
        /// <param name="parent">The plot manager auto package export or null if at root (Game 1)</param>
        /// <param name="objectName">The name of the export</param>
        /// <param name="className">Name of class of export to generate</param>
        /// <param name="numZerosBinary">Number of 4 byte 0s to add as binary data (for empty binary)</param>
        /// <returns>The generated export</returns>
        private static ExportEntry GeneratePlotManagerAutoExport(IMEPackage package, ExportEntry parent, string objectName, string className, int numZerosBinary)
        {
            // Test if exists first - do not overwrite
            string fulltest;
            if (parent == null)
            {
                fulltest = objectName;
            }
            else
            {
                fulltest = $@"{parent.InstancedFullPath}.{objectName}";
            }

            var exp = package.FindExport(fulltest);
            if (exp != null)
                return exp; // Return existing


            // Generate it 
            var rop = new RelinkerOptionsPackage { ImportExportDependencies = true };
            exp = new ExportEntry(package, parent, objectName)
            { Class = EntryImporter.EnsureClassIsInFile(package, className, rop) };
            exp.ObjectFlags |= UnrealFlags.EObjectFlags.Public | UnrealFlags.EObjectFlags.LoadForClient | UnrealFlags.EObjectFlags.LoadForServer | UnrealFlags.EObjectFlags.Standalone;

            if (package.Game.IsGame1())
            {
                // Do not set as forced export
                exp.ExportFlags &= ~UnrealFlags.EExportFlags.ForcedExport;
            }
            else
            {
                exp.ExportFlags |= UnrealFlags.EExportFlags.ForcedExport;
            }

            exp.WriteBinary(new byte[4 * numZerosBinary]); // Blank data
            package.AddExport(exp);
            return exp;
        }
        #endregion

        #region 2DA
        public static void GenerateBlank2DAs(MEGame game, string dlcFolderPath, List<Bio2DAOption> blank2DAsToGenerate)
        {
            var dlcName = Path.GetFileName(dlcFolderPath);
            var bPath = BackupService.GetGameBackupPath(game);
            string cookedPath = null;
            if (bPath != null)
            {
                cookedPath = MEDirectories.GetCookedPath(game, bPath);
                if (!Directory.Exists(cookedPath))
                {
                    MLog.Error($@"Backup directory doesn't exist, can't generate 2DAs: {cookedPath}");
                    return;
                }
            }
            else
            {
                MLog.Error($@"Backup directory doesn't exist, can't generate 2DAs");
                return;
            }

            var newPackageName = $@"BIOG_2DA_{dlcName}";
            var newPackagePath = Path.Combine(dlcFolderPath, game.CookedDirName(), $@"{newPackageName}{game.PCPackageFileExtension()}");
            IMEPackage twoDAPackage;
            if (!File.Exists(newPackagePath))
            {
                twoDAPackage = MEPackageHandler.CreateAndOpenPackage(newPackagePath, game);
            }
            else
            {
                twoDAPackage = MEPackageHandler.OpenMEPackage(newPackagePath);
            }

            foreach (var twoDARef in blank2DAsToGenerate)
            {
                // Open the source package, table only
                MLog.Information($@"Generating blank 2DA for {twoDARef.TemplateTable.EntryPath} from {Path.GetFileName(twoDARef.TemplateTable.FilePath)}");
                var sourcePackage = MEPackageHandler.UnsafePartialLoad(twoDARef.TemplateTable.FilePath, x => x.UIndex == twoDARef.TemplateTable.EntryUIndex);
                var sourceTable = sourcePackage.GetUExport(twoDARef.TemplateTable.EntryUIndex);

                var gen = twoDARef.GenerateBlank2DA(sourceTable, twoDAPackage);
                if (gen != null)
                {
                    twoDARef.InstalledInstancedFullPath = gen.InstancedFullPath;
                    if (game == MEGame.ME1)
                    {
                        AddAutoloadReferenceGame1(dlcFolderPath, @"Packages", @"2DA", newPackageName);
                    }
                }
            }

            if (twoDAPackage.IsModified)
            {
                if (game == MEGame.LE1)
                {
                    GenerateM3DA(dlcFolderPath, blank2DAsToGenerate, twoDAPackage);
                }
                twoDAPackage.Save();
            }
        }

        private static void GenerateM3DA(string dlcFolderPath, List<Bio2DAOption> tables, IMEPackage twoDAPackage)
        {
            var m3daPath = Path.Combine(dlcFolderPath, twoDAPackage.Game.CookedDirName(), $@"{Path.GetFileName(dlcFolderPath)}-2DAs.m3da");
            var mml = new List<Bio2DAMergeManifest>();
            var mapping = new Dictionary<string, Bio2DAMergeManifest>();
            foreach (var b in tables)
            {
                if (!mapping.TryGetValue(Path.GetFileName(b.TemplateTable.FilePath), out var mm))
                {
                    mm = new Bio2DAMergeManifest()
                    {
                        Comment = @"Starter kit generated table merge",
                        ModPackageFile = twoDAPackage.FileNameNoExtension + @".pcc",
                        GamePackageFile = Path.GetFileName(b.TemplateTable.FilePath),
                        ModTables = new List<string>(),
                    };
                    mapping[Path.GetFileName(b.TemplateTable.FilePath)] = mm;
                    mml.Add(mm);
                }

                mm.ModTables.Add(b.InstalledInstancedFullPath);
            }
            File.WriteAllText(m3daPath, JsonConvert.SerializeObject(mml, Formatting.Indented));
        }

        #endregion

        #region Utility 

        /// <summary>
        /// Adds an item to Coalesced
        /// </summary>
        /// <param name="game">Game to add to</param>
        /// <param name="dlcName">The name of the DLC</param>
        /// <param name="cookedPath">The path of the CookedPCConsole folder</param>
        /// <param name="configFilename">The name of the config file</param>
        /// <param name="sectionName">The section name in the config file</param>
        /// <param name="key">The key of the property (property name)</param>
        /// <param name="value">The value of the property</param>
        /// <param name="parseAction">How the property should be applied</param>
        private static void AddCoalescedReference(MEGame game, string dlcName, string cookedPath, string configFilename, string sectionName, string key, string value, CoalesceParseAction parseAction)
        {
            var prop = new CoalesceProperty(key, new CoalesceValue(value, parseAction));
            var info = new CoalescedEntryInfo() { File = configFilename, Section = sectionName, Property = prop };
            if (game.IsGame2())
            {
                AddCoalescedEntryGame2(dlcName, cookedPath, info);
            }
            else if (game.IsGame3())
            {
                AddCoalescedEntryGame3(dlcName, cookedPath, info);
            }
        }

        /// <summary>
        /// Adds game 2 coalesced entry
        /// </summary>
        /// <param name="dlcName">Name of DLC</param>
        /// <param name="cookedPath">Directory of CookedPCConsole</param>
        /// <param name="info">Info about what to add</param>
        private static void AddCoalescedEntryGame2(string dlcName, string cookedPath, CoalescedEntryInfo info)
        {
            // Add to the coalesced
            var actualFileName = $@"{Path.GetFileNameWithoutExtension(info.File)}.ini";
            var iniFile = Path.Combine(cookedPath, actualFileName);
            CoalesceAsset configIni;
            if (!File.Exists(iniFile))
            {
                // No contents.
                configIni = ConfigFileProxy.ParseIni(@"");
            }
            else
            {
                configIni = ConfigFileProxy.LoadIni(iniFile);
            }

            var sp = configIni.GetOrAddSection(info.Section);
            sp.AddEntryIfUnique(info.Property);
            File.WriteAllText(iniFile, configIni.GetGame2IniText());
        }

        /// <summary>
        /// Applies a change to a Game 3 coalesced file
        /// </summary>
        /// <param name="dlcName"></param>
        /// <param name="cookedPath"></param>
        /// <param name="startupInfo"></param>
        private static void AddCoalescedEntryGame3(string dlcName, string cookedPath, CoalescedEntryInfo startupInfo)
        {
            // Todo: Non-saving mode to improve performance

            // Load coalesced
            var coalFile = $@"Default_{dlcName}.bin";
            var coalPath = Path.Combine(cookedPath, coalFile);
            var decompiled = CoalescedConverter.DecompileGame3ToMemory(new MemoryStream(File.ReadAllBytes(coalPath)));
            var iniFiles = new SortedDictionary<string, CoalesceAsset>(); // For recomp
            foreach (var f in decompiled)
            {
                iniFiles[f.Key] = XmlCoalesceAsset.LoadFromMemory(f.Value);
            }

            // Add entry
            var file = iniFiles[$@"{Path.GetFileNameWithoutExtension(startupInfo.File)}.xml"]; // Ensure we don't use extension in provided file.
            var section = file.GetOrAddSection(startupInfo.Section);
            section.AddEntryIfUnique(startupInfo.Property);

            // Reserialize
            var assetTexts = new Dictionary<string, string>();
            foreach (var asset in iniFiles)
            {
                assetTexts[asset.Key] = asset.Value.ToXmlString();
            }

            var outBin = CoalescedConverter.CompileFromMemory(assetTexts);
            outBin.WriteToFile(coalPath);
        }

        /// <summary>
        /// Creates an empty ObjectReferencer if none exists - if one exists, it returns that instead
        /// </summary>
        /// <param name="package">Package to operate on</param>
        /// <returns>Export of an export referencer</returns>
        public static ExportEntry CreateObjectReferencer(IMEPackage package, bool isStartupPackage)
        {
            var referencer = package.Exports.FirstOrDefault(x => x.ClassName == @"ObjectReferencer");
            if (referencer != null) return referencer;

            var rop = new RelinkerOptionsPackage() { Cache = new PackageCache() };
            if (package.Game.IsGame2())
            {
                // 2 just uses objectreferencer
                referencer = new ExportEntry(package, 0, package.GetNextIndexedName(@"ObjectReferencer"), properties: new PropertyCollection() { new ArrayProperty<ObjectProperty>(@"ReferencedObjects") })
                {
                    Class = EntryImporter.EnsureClassIsInFile(package, @"ObjectReferencer", rop)
                };
            }
            else
            {
                // 3 uses both ObjectReferencer for normal packages and CombinedStartupReferencer for startup files
                // Startup files do not work if they use ObjectReferencer
                referencer = new ExportEntry(package, 0, package.GetNextIndexedName(isStartupPackage ? @"CombinedStartupReferencer" : @"ObjectReferencer"), properties: new PropertyCollection() { new ArrayProperty<ObjectProperty>(@"ReferencedObjects") })
                {
                    Class = EntryImporter.EnsureClassIsInFile(package, @"ObjectReferencer", rop)
                };
                if (isStartupPackage)
                {
                    referencer.indexValue = 0;
                }
            }

            referencer.WriteProperty(new ArrayProperty<ObjectProperty>(@"ReferencedObjects"));
            package.AddExport(referencer);
            return referencer;
        }

        /// <summary>
        /// Adds the specified entry to the object referencer in the package. If there is no object referencer already added then this does nothing.
        /// </summary>
        /// <param name="entry">The entry to add. It is not checked if it is already in the list</param>
        /// <returns>If object reference was added</returns>
        public static bool AddToObjectReferencer(IEntry entry)
        {
            var referencer = entry.FileRef.Exports.FirstOrDefault(x => x.ClassName == @"ObjectReferencer");
            if (referencer == null) return false;
            var refs = referencer.GetProperty<ArrayProperty<ObjectProperty>>(@"ReferencedObjects") ?? new ArrayProperty<ObjectProperty>(@"ReferencedObjects");
            refs.Add(new ObjectProperty(entry));
            referencer.WriteProperty(refs);
            return true;
        }


        #endregion

        #region LE3 Mod Settings Menu
        public static void AddLE3ModSettingsMenu(IM3Mod mod, GameTarget target, string dlcFolderPath, List<Action<DuplicatingIni>> moddescAddinDelegates, Func<List<string>> getModDLCRequirements = null)
        {
            if (target.Game != MEGame.LE3)
                return;

            var dlcName = Path.GetFileName(dlcFolderPath);
            var cookedPath = Path.Combine(dlcFolderPath, target.Game.CookedDirName());

            // GuiData contains the class that mod settings menu will dynamic load
            var guiDataFName = $@"SFXGUIData_{dlcName}.pcc";
            var guiDataPackagePath = Path.Combine(cookedPath, guiDataFName);
            if (!File.Exists(guiDataPackagePath))
            {
                // Generate GuiData package
                using var package = MEPackageHandler.CreateAndOpenPackage(guiDataPackagePath, target.Game, true);
                CreateObjectReferencer(package, false);
                package.Save();
            }

            // Create package (this is not in above in case it already exists for some reason...)
            var className = $@"SFXGUIData_ModSettings_{dlcName}";
            string modSettingsClassPath = $@"SFXGameContent.{className}";
            using var guiDataPackage = MEPackageHandler.OpenMEPackage(guiDataPackagePath);
            var testPath = guiDataPackage.FindExport(modSettingsClassPath);
            if (testPath == null)
            {
                // Does not contain the export, we need to create it.

                var container = guiDataPackage.FindExport(@"SFXGameContent");
                if (container != null && container.ClassName == @"Package")
                {
                    MLog.Information(@"Found existing SFXGameContent in GUIData package, using that as parent");
                }
                else if (container != null)
                {
                    MLog.Error($@"We found SFXGameContent in {guiDataPackagePath} but it is not a Package export! This is not supported");
                    return;
                }
                else
                {
                    container = ExportCreator.CreatePackageExport(guiDataPackage, @"SFXGameContent");
                }

                var settingsData = guiDataPackage.FindExport(@"Settings_Data");
                if (settingsData != null && settingsData.ClassName == @"Package")
                {
                    MLog.Information(@"Found existing Settings_Data in GUIData package, using that as parent");
                }
                else if (settingsData != null)
                {
                    MLog.Error($@"We found Settings_Data in {guiDataPackagePath} but it is not a Package export! This is not supported");
                    return;
                }
                else
                {
                    settingsData = ExportCreator.CreatePackageExport(guiDataPackage, @"Settings_Data");
                }


                // Compile the classes
                var usop = new UnrealScriptOptionsPackage()
                {
                    Cache = new TargetPackageCache { RootPath = target.GetBioGamePath() },
                    GamePathOverride = target.TargetPath
                };

                var fileLib = new FileLib(guiDataPackage);
                if (!fileLib.Initialize(usop))
                {
                    MLog.Error($@"Error intitializing filelib for sfxguidata package: {fileLib.InitializationLog.AllErrors.Select(msg => msg.ToString())}");
                    return;
                }

                // 1. Parent class
                (_, MessageLog log1) = UnrealScriptCompiler.CompileClass(guiDataPackage, new StreamReader(MUtilities.ExtractInternalFileToStream(LE3ModSettingsClassTextAsset)).ReadToEnd(), fileLib, usop, parent: settingsData);
                if (log1.HasErrors)
                {
                    MLog.Error($@"Failed to compile SFXGUIData_ModSettings for sfxguidata package: {log1.AllErrors.Select(msg => msg.ToString())}");
                    return;
                }

                // 2. Our custom class
                (_, MessageLog log2) = UnrealScriptCompiler.CompileClass(guiDataPackage, GetCustomLE3ModSettingsClassText(dlcName), fileLib, usop, parent: container);
                if (log2.HasErrors)
                {
                    MLog.Error($@"Failed to compile {className} for sfxguidata package: {log2.AllErrors.Select(msg => msg.ToString())}");
                    return;
                }

                guiDataPackage.Save();
            }

            MountFile mf = new MountFile(Path.Combine(dlcFolderPath, target.Game.CookedDirName(), @"mount.dlc"));
            // Add the dynamic load mapping for our class.
            Dictionary<string, string> dlm = new CaseInsensitiveDictionary<string>
                {
                    { @"ObjectName", modSettingsClassPath },
                    { @"SeekFreePackageName", Path.GetFileNameWithoutExtension(guiDataFName) }
                };
            AddCoalescedReference(target.Game, dlcName, cookedPath, @"BioEngine", @"sfxgame.sfxengine",
                @"dynamicloadmapping",
                StringStructParser.BuildCommaSeparatedSplitValueList(dlm, dlm.Keys.ToArray()),
                CoalesceParseAction.AddUnique);

            // Add BioUI references so our menu loads
            AddCoalescedReference(target.Game, dlcName, cookedPath, @"BioUI", modSettingsClassPath,
                @"confirmationmessageatextoverride", @"247370", CoalesceParseAction.New);
            AddCoalescedReference(target.Game, dlcName, cookedPath, @"BioUI", modSettingsClassPath, @"m_sratext",
                @"247370", CoalesceParseAction.New);
            AddCoalescedReference(target.Game, dlcName, cookedPath, @"BioUI", modSettingsClassPath, @"m_srbtext",
                @"576055", CoalesceParseAction.New);
            AddCoalescedReference(target.Game, dlcName, cookedPath, @"BioUI", modSettingsClassPath, @"m_srtitle",
                @"3248043", CoalesceParseAction.New); // Point to mod TLK ID?

            // Add root menu reference to our menu
            Dictionary<string, string> msmr = new CaseInsensitiveDictionary<string>
                {
                    { @"SubMenuClassName", modSettingsClassPath },
                    {
                        @"ChoiceEntry", StringStructParser.BuildCommaSeparatedSplitValueList(
                            new CaseInsensitiveDictionary<string>()
                            {
                                { @"srChoiceName", mf.TLKID.ToString() }, // These strings need added to the TLK
                                { @"srChoiceDescription", @"3248042" }, // These strings need added to the TLK
                            })
                    },
                    { @"Images[0]", modSettingsClassPath },
                };

            // Todo: Change to config bundle
            AddCoalescedReference(target.Game, dlcName, cookedPath, @"BioUI",
                @"sfxgamecontent.sfxguidata_modsettings_root", @"modsettingitemarray",
                StringStructParser.BuildCommaSeparatedSplitValueList(msmr, @"SubMenuClassName", @"Images[0]"),
                CoalesceParseAction.AddUnique);


            // Add DLC requirements
            // If mod is null, we haven't generated mod yet, so it will say there is no dependency on this yet.
            bool requiresLE3Patch = mod != null && mod.RequiredDLC.Any(x => x.DLCFolderName.Key.CaseInsensitiveEquals(@"DLC_MOD_LE3Patch"));
            bool requiresLE3Framework = mod != null && mod.RequiredDLC.Any(x => x.DLCFolderName.Key.CaseInsensitiveEquals(@"DLC_MOD_Framework"));
            if (!requiresLE3Framework || !requiresLE3Patch)
            {
                // Mod has a dependency on LE3 Comm Patch so we add that to the moddesc
                moddescAddinDelegates.Add(x =>
                {
                    var reqDlc = x[@"ModInfo"][@"requireddlc"];
                    if (!requiresLE3Patch)
                    {
                        if (!string.IsNullOrWhiteSpace(reqDlc.Value)) reqDlc.Value += @";";
                        reqDlc.Value += @"DLC_MOD_LE3Patch";
                    }

                    if (!requiresLE3Framework)
                    {
                        if (!string.IsNullOrWhiteSpace(reqDlc.Value)) reqDlc.Value += @";";
                        reqDlc.Value += @"DLC_MOD_Framework";
                    }

                    x[@"ModInfo"][@"requireddlc"] = reqDlc;
                });
            }
        }

        /// <summary>
        /// The class text for the custom menu class, split to its own func for clarity
        /// </summary>
        /// <param name="dlcName"></param>
        /// <returns></returns>
        private static string GetCustomLE3ModSettingsClassText(string dlcName)
        {
            return $@"Class SFXGUIData_ModSettings_{dlcName} extends SFXGUIData_ModSettings editinlinenew perobjectconfig config(UI); defaultproperties {{ }}";
        }
        #endregion

        #region LE1 Mod Settings Menu
        // Documentation:

        /// <summary>
        /// The class text for the custom menu class, split to its own func for clarity
        /// </summary>
        /// <param name="dlcName"></param>
        /// <returns></returns>
        private static string GetCustomLE1ModSettingsClassText(string dlcName)
        {
            return $@"Class ModSettingsSubmenu_{dlcName} extends ModSettingsSubmenu config(UI); defaultproperties {{ }}";
        }

        public static void AddLE1ModSettingsMenu(IM3Mod mod, GameTarget target, string dlcFolderPath,
            List<Action<DuplicatingIni>> moddescAddinDelegates, Func<List<string>> getModDLCRequirements = null)
        {
            if (target.Game != MEGame.LE1)
                return;

            var dlcName = Path.GetFileName(dlcFolderPath);
            var cookedPath = Path.Combine(dlcFolderPath, target.Game.CookedDirName());

            #region Menu class

            {
                // MSM contains the class that mod settings menu will dynamic load
                var msmDataFName = $@"{dlcName}_ModSettingsSubmenus.pcc";
                var msmPackageFilePath = Path.Combine(cookedPath, msmDataFName);
                if (!File.Exists(msmPackageFilePath))
                {
                    // Generate MSM package. We will re-open it.
                    using var package = MEPackageHandler.CreateAndOpenPackage(msmPackageFilePath, target.Game, true);
                    // CreateObjectReferencer(package, false); // Don't think this is needed...
                    package.Save();
                }

                // Create package (this is not in above in case it already exists for some reason...)
                var className = $@"ModSettingsSubmenu_{dlcName}";
                string modSettingsClassPath = $@"ModSettingsMenu.ModSettingsSubmenu"; // Base class
                using var msmDataPackage = MEPackageHandler.OpenMEPackage(msmPackageFilePath);
                var testPath = msmDataPackage.FindExport(modSettingsClassPath);
                if (testPath == null)
                {
                    // Does not contain the export, we need to create it.

                    var container = msmDataPackage.FindExport(@"ModSettingsMenu", @"Package");
                    if (container != null)
                    {
                        MLog.Information(@"Found existing ModSettingsMenu in MSM package, using that as parent");
                    }
                    else
                    {
                        container = ExportCreator.CreatePackageExport(msmDataPackage, @"ModSettingsMenu");
                    }

                    // Compile the classes
                    var usop = new UnrealScriptOptionsPackage()
                    {
                        Cache = new TargetPackageCache { RootPath = target.GetBioGamePath() },
                        GamePathOverride = target.TargetPath
                    };

                    var fileLib = new FileLib(msmDataPackage);
                    if (!fileLib.Initialize(usop))
                    {
                        var errors = string.Join(@", ", fileLib.InitializationLog.AllErrors.Select(msg => msg.ToString()));
                        MLog.Error($@"Error initializing filelib for msmdata package: {errors}");
                        throw new Exception(LC.GetString(LC.string_skai_fileLibInitFailedMSM, errors));
                    }

                    // 1. Parent class
                    (_, MessageLog log0) = UnrealScriptCompiler.CompileClass(msmDataPackage,
                        @"Class ModSettingsSubmenu;",
                        fileLib, usop, parent: container); // works around self-referencing issues.
                    (_, MessageLog log1) = UnrealScriptCompiler.CompileClass(msmDataPackage,
                        new StreamReader(MUtilities.ExtractInternalFileToStream(LE1ModSettingsClassTextAsset))
                            .ReadToEnd(),
                        fileLib, 
                        usop,
                        export: msmDataPackage.FindExport(modSettingsClassPath, @"Class"));
                    if (log1.HasErrors)
                    {
                        var errors = log1.AllErrors.Select(msg => msg.ToString());
                        MLog.Error($@"Failed to compile ModSettingsSubmenu for msmdata package: {errors}");
                        throw new Exception(LC.GetString(LC.string_skai_failedToCompileParentClassMSM, errors));
                    }

                    // 2. Our custom class
                    (_, MessageLog log2) = UnrealScriptCompiler.CompileClass(msmDataPackage,
                        GetCustomLE1ModSettingsClassText(dlcName), fileLib, usop);
                    if (log2.HasErrors)
                    {
                        var errors = log2.AllErrors.Select(msg => msg.ToString());
                        MLog.Error($@"Failed to compile {className} for msmdata package: {errors}");
                        throw new Exception(LC.GetString(LC.string_skai_failedToCompileClassMSM, className, errors));
                    }

                    msmDataPackage.Save();
                }
            }

            #endregion

            #region Base UI image
            var baseImageMemoryPath = $@"GUI_Images_{dlcName}.MenuRootImage";
            {
                // Add UI image
                // MSM contains the class that mod settings menu will dynamic load
                var guiImagesFName = $@"GUI_Images_{dlcName}.pcc";
                var guiImagesPackageFilePath = Path.Combine(cookedPath, guiImagesFName);
                if (!File.Exists(guiImagesPackageFilePath))
                {
                    // Generate GUI images package. We will re-open it.
                    using var package = MEPackageHandler.CreateAndOpenPackage(guiImagesPackageFilePath, target.Game, true);
                    package.Save();
                }

                // Create package (this is not in above in case it already exists for some reason...)
                string modBaseImagePath = @"MenuRootImage_I1";
                using var guiImagePackage = MEPackageHandler.OpenMEPackage(guiImagesPackageFilePath);
                var testPath = guiImagePackage.FindExport(modBaseImagePath, @"Texture2D");
                if (testPath == null)
                {
                    var createdImage = Texture2D.CreateTexture(guiImagePackage, modBaseImagePath, 2048, 1024, PixelFormat.DXT5, false);
                    Texture2D.CreateSWFForTexture(createdImage);

                    var t2d = new Texture2D(createdImage);
                    var imageBytes = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.StarterKit.LE1.Images.msm_placeholder.jpg").ToArray();
                    var image = Image.LoadFromFileMemory(imageBytes, 2, PixelFormat.DXT5);
                    t2d.Replace(image, createdImage.GetProperties(), isPackageStored: true);
                    guiImagePackage.Save();
                }
            }

            #endregion


            var autoload = Path.Combine(dlcFolderPath, @"Autoload.ini");

            int strId = 157152; // You should not be messing with things you don't understand.
            if (File.Exists(autoload))
            {
                var autoLoadIni = DuplicatingIni.LoadIni(autoload);
                var gui = autoLoadIni.GetSection(@"GUI");
                if (gui != null)
                {
                    var nameStrRef = gui.GetValue(@"NameStrRef");
                    if (nameStrRef.HasValue)
                        int.TryParse(nameStrRef.Value, out strId); // Parse to mod name.
                }
            }

            // Add root menu reference to our menu
            Dictionary<string, string> msmr = new CaseInsensitiveDictionary<string>
                {
                    { @"srCenterText", strId.ToString() },
                    { @"srDescriptionTitleText", strId.ToString() },
                    { @"srDescriptionText", @"181072" }, // "Settings"
                    { @"Images", $"(\"{baseImageMemoryPath}\")" }, // do not localize
                };


            var m3cdData = $@"[BioUI.ini ModSettings_Submenus_LE1CP.ModSettingsSubmenu_Root]" + Environment.NewLine;
            m3cdData += $@"+menuitems={StringStructParser.BuildCommaSeparatedSplitValueList(msmr, @"SubmenuClassName")}"; // do not localize
            var m3cdPath = Path.Combine(cookedPath, @"ConfigDelta-ModSettingsMenu.m3cd");
            File.WriteAllText(m3cdPath, m3cdData);



            // Add DLC requirements
            // If mod is null, we haven't generated mod yet, so it will say there is no dependency on this yet.
            bool requiresLE1Patch = mod != null && mod.RequiredDLC.Any(x => x.DLCFolderName.Key.CaseInsensitiveEquals(@"DLC_MOD_LE1CP"));
            if (!requiresLE1Patch)
            {
                // Mod has a dependency on LE1 Comm Patch so we add that to the moddesc
                moddescAddinDelegates.Add(x =>
                {
                    var reqDlc = x[@"ModInfo"][@"requireddlc"];
                    if (!string.IsNullOrWhiteSpace(reqDlc.Value)) reqDlc.Value += @";";
                    reqDlc.Value += @"DLC_MOD_LE1CP";
                    x[@"ModInfo"][@"requireddlc"] = reqDlc;
                });
            }
        }

        #endregion
    }
}
