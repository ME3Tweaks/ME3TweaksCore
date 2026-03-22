using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LegendaryExplorerCore.Coalesced;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.TLK;
using LegendaryExplorerCore.TLK.ME1;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.ME3Tweaks.M3Merge;
using ME3TweaksCore.Objects;

namespace ME3TweaksCore.ME3Tweaks.StarterKit
{
    /// <summary>
    /// Generates a blank DLC mod folder (does not include moddesc.ini)
    /// </summary>
    public class DLCModGenerator
    {
        public static string CreateStarterKitMod(string destFolder, StarterKitOptions skOption, Action<string> UITextCallback, out List<Action<DuplicatingIni>> moddescAddinDelegates, Func<MEGame, string> getGamePatchModDirectory)
        {
            //NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"StarterKitThread");
            //nbw.DoWork += (sender, args) =>
            //{
            var dlcFolderName = $@"DLC_MOD_{skOption.ModDLCFolderNameSuffix}";
            var modPath = skOption.OutputFolderOverride ?? Path.Combine(destFolder, MUtilities.SanitizePath(skOption.ModName));
            if (skOption.OutputFolderOverride == null && Directory.Exists(modPath))
            {
                MUtilities.DeleteFilesAndFoldersRecursively(modPath);
            }

            Directory.CreateDirectory(modPath);

            //Creating DLC directories
            MLog.Information(@"Creating starter kit folders");
            var contentDirectory = Path.Combine(modPath, dlcFolderName);

            if (skOption.OutputFolderOverride != null && Directory.Exists(contentDirectory))
            {
                // Wipe out DLC folder target
                MUtilities.DeleteFilesAndFoldersRecursively(contentDirectory);
            }

            Directory.CreateDirectory(contentDirectory);

            var cookedDir = Directory.CreateDirectory(Path.Combine(contentDirectory, skOption.ModGame.CookedDirName())).FullName;
            if (skOption.ModGame.IsGame1())
            {
                // AutoLoad.ini using AutoloadIni representation
                var autoload = new AutoloadIni();
                // Add GlobalTalkTable entry
                autoload.GlobalTalkTables.Add($@"{dlcFolderName}_GlobalTlk.GlobalTlk_tlk");

                // GUI NameStrRef
                autoload.DLCModNameStrRef = skOption.ModInternalTLKID;

                // ME1 DLC mount info
                autoload.ModName = skOption.ModName;
                autoload.ModMount = skOption.ModMountPriority;

                MLog.Information($@"Saving autoload.ini for {skOption.ModGame} mod");
                File.WriteAllText(Path.Combine(contentDirectory, @"AutoLoad.ini"), autoload.ToString(), new UTF8Encoding(false));

                //TLK
                var dialogdir = skOption.ModGame == MEGame.ME1
                    ? Directory.CreateDirectory(Path.Combine(cookedDir, @"Packages", @"Dialog")).FullName
                    : cookedDir;
                var tlkGlobalFile = Path.Combine(dialogdir, $@"{dlcFolderName}_GlobalTlk");
                var extension = skOption.ModGame == MEGame.ME1 ? @"upk" : @"pcc";
                foreach (var lang in GameLanguage.GetLanguagesForGame(skOption.ModGame))
                {
                    var langExt = lang.FileCode == @"INT" ? "" : $@"_{lang.FileCode}"; // do not localize
                    var tlkPath = $@"{tlkGlobalFile}{langExt}.{extension}";
                    MUtilities.ExtractInternalFile($@"ME3TweaksCore.ME3Tweaks.StarterKit.{skOption.ModGame}.BlankTlkFile.{extension}", tlkPath, true);

                    var tlkFile = MEPackageHandler.OpenMEPackage(tlkPath);
                    var tlk1 = new ME1TalkFile(tlkFile.GetUExport(1));
                    var tlk2 = new ME1TalkFile(tlkFile.GetUExport(2));

                    tlk1.StringRefs[0].StringID = skOption.ModInternalTLKID;
                    tlk2.StringRefs[0].StringID = skOption.ModInternalTLKID;

                    tlk1.StringRefs[0].Data = skOption.ModInternalName;
                    tlk2.StringRefs[0].Data = skOption.ModInternalName;

                    var huff = new HuffmanCompression();
                    huff.LoadInputData(tlk1.StringRefs.ToList());
                    huff.SerializeTalkfileToExport(tlkFile.GetUExport(1));

                    huff = new HuffmanCompression();
                    huff.LoadInputData(tlk2.StringRefs.ToList());
                    huff.SerializeTalkfileToExport(tlkFile.GetUExport(2));
                    MLog.Information($@"Saving {tlkPath} TLK package");
                    tlkFile.Save();
                }
            }
            else
            {
                //ME2, ME3
                MountFile mf = new MountFile();
                mf.Game = skOption.ModGame;
                mf.MountFlags = skOption.ModMountFlag;
                mf.ME2Only_DLCFolderName = dlcFolderName;
                mf.ME2Only_DLCHumanName = skOption.ModName;
                mf.MountPriority = (ushort)skOption.ModMountPriority;
                mf.TLKID = skOption.ModInternalTLKID;
                MLog.Information(@"Saving mount.dlc file for mod");
                mf.WriteMountFile(Path.Combine(cookedDir, @"Mount.dlc"));

                if (skOption.ModGame.IsGame3())
                {
                    if (skOption.ModGame == MEGame.ME3)
                    {
                        //Extract Default.Sfar
                        MUtilities.ExtractInternalFile(@"ME3TweaksCore.ME3Tweaks.StarterKit.ME3.Default.sfar", Path.Combine(cookedDir, @"Default.sfar"), true);
                    }

                    //Generate Coalesced.bin for mod
                    var memory = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.StarterKit.Game3.Default_DLC_MOD_StarterKit.bin");
                    var files = CoalescedConverter.DecompileGame3ToMemory(memory);
                    //Modify coal files for this mod.
                    files[@"BioEngine.xml"] = files[@"BioEngine.xml"].Replace(@"StarterKit", skOption.ModDLCFolderNameSuffix); //update bioengine

                    var newMemory = CoalescedConverter.CompileFromMemory(files);
                    var outpath = Path.Combine(cookedDir, $@"Default_{dlcFolderName}.bin");
                    MLog.Information(@"Saving new starterkit coalesced file");
                    File.WriteAllBytes(outpath, newMemory.ToArray());
                }
                else
                {
                    //ME2, LE2
                    DuplicatingIni bioEngineIni = new DuplicatingIni();
                    //bioEngineIni.Configuration.AssigmentSpacer = ""; //no spacer.
                    bioEngineIni[@"Core.System"][@"!CookPaths"].Value = @"CLEAR";
                    bioEngineIni[@"Core.System"][@"+SeekFreePCPaths"].Value = $@"..\BIOGame\DLC\{dlcFolderName}\CookedPC"; // Is still CookedPC on LE
                    bioEngineIni[@"Engine.DLCModules"][dlcFolderName].Value = skOption.ModModuleNumber.ToString();
                    bioEngineIni[@"DLCInfo"][@"Version"].Value = 0.ToString(); //unknown
                    bioEngineIni[@"DLCInfo"][@"Flags"].Value = ((int)skOption.ModMountFlag.FlagValue).ToString();
                    bioEngineIni[@"DLCInfo"][@"Name"].Value = skOption.ModInternalTLKID.ToString();
                    MLog.Information(@"Saving BioEngine file");
                    bioEngineIni.WriteToFile(Path.Combine(cookedDir, @"BIOEngine.ini"), new UTF8Encoding(false));
                }

                var tlkFilePrefix = skOption.ModGame.IsGame3() ? dlcFolderName : $@"DLC_{skOption.ModModuleNumber}";

                var languages = GameLanguage.GetLanguagesForGame(skOption.ModGame);
                foreach (var lang in languages)
                {
                    List<TLKStringRef> strs = new List<TLKStringRef>();
                    strs.Add(new TLKStringRef(skOption.ModInternalTLKID, skOption.ModInternalName));
                    if (skOption.ModGame.IsGame2())
                    {
                        strs.Add(new TLKStringRef(skOption.ModInternalTLKID + 1, @"DLC_" + skOption.ModModuleNumber));
                    }
                    else
                    {
                        strs.Add(new TLKStringRef(skOption.ModInternalTLKID + 1, @"DLC_MOD_" + skOption.ModDLCFolderNameSuffix));
                    }

                    strs.Add(new TLKStringRef(skOption.ModInternalTLKID + 2, lang.LanguageCode.ToString()));
                    strs.Add(new TLKStringRef(skOption.ModInternalTLKID + 3, @"Male"));
                    strs.Add(new TLKStringRef(skOption.ModInternalTLKID + 3, @"Female"));

                    foreach (var str in strs)
                    {
                        str.Data += '\0';
                    }

                    var tlk = Path.Combine(cookedDir, $@"{tlkFilePrefix}_{lang.FileCode}.tlk");
                    MLog.Information(@"Saving TLK file: " + tlk);
                    LegendaryExplorerCore.TLK.ME2ME3.HuffmanCompression.SaveToTlkFile(tlk, strs);
                }
            }

            // ADDINS
            moddescAddinDelegates = new List<Action<DuplicatingIni>>();
            if (skOption.AddStartupFile)
            {
                UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Startup file");
                StarterKitAddins.AddStartupFile(skOption.ModGame, contentDirectory);
            }
            if (skOption.AddPlotManagerData)
            {
                UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - PlotManager data");
                StarterKitAddins.GeneratePlotData(skOption.Target, contentDirectory);
            }

            if (skOption.AddModSettingsMenu)
            {
                UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Mod Settings Menu");
                StarterKitAddins.AddLE3ModSettingsMenu(null, skOption.Target, contentDirectory, moddescAddinDelegates);
            }

            if (skOption.Blank2DAsToGenerate.Any())
            {
                UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - 2DAs");
                StarterKitAddins.GenerateBlank2DAs(skOption.ModGame, contentDirectory, skOption.Blank2DAsToGenerate);
            }

            // Generator needs to accept multiple outfit dictionaries
            var outfits = SQMOutfitMerge.LoadSquadmateMergeInfo(skOption.ModGame, contentDirectory);
            
            string errorMessage = null;
            if (skOption.ModGame.IsGame3())
            {

                if (skOption.AddAshleySQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Ashley SQM");
                    errorMessage =
                        StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Ashley", contentDirectory,
                            outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddEDISQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - EDI SQM");
                    errorMessage =
                        StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"EDI", contentDirectory,
                            outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddGarrusSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Garrus SQM");
                    errorMessage =
                        StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Garrus", contentDirectory,
                            outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddKaidanSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Kaidan SQM");
                    errorMessage =
                        StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Kaidan", contentDirectory,
                            outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddJamesSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - James SQM");
                    errorMessage =
                        StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Marine", contentDirectory,
                            outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddJavikSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Javik SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Prothean",
                        contentDirectory, outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddLiaraSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Liara SQM");
                    errorMessage =
                        StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Liara", contentDirectory,
                            outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddTaliSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Tali SQM");
                    errorMessage =
                        StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Tali", contentDirectory,
                            outfits, getGamePatchModDirectory);
                }
            }
            else if (skOption.ModGame == MEGame.LE2)
            {
                if (skOption.AddMirandaSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Miranda SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Vixen", contentDirectory, outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddJacobSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Jacob SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Leading", contentDirectory, outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddMordinSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Mordin SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Professor", contentDirectory, outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddGarrusSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Garrus SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Garrus", contentDirectory, outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddJackSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Jack SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Convict", contentDirectory, outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddGruntSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Grunt SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Grunt", contentDirectory, outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddTaliSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Tali SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Tali", contentDirectory, outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddSamaraSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Samara SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Mystic", contentDirectory, outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddThaneSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Thane SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Assassin", contentDirectory, outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddLegionSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Legion SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Geth", contentDirectory, outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddKasumiSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Kasumi SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Thief", contentDirectory, outfits, getGamePatchModDirectory);
                }

                if (errorMessage == null && skOption.AddZaeedSQM)
                {
                    UITextCallback?.Invoke($@"{LC.GetString(LC.string_generatingMod)} - Zaeed SQM");
                    errorMessage = StarterKitAddins.GenerateSquadmateMergeFiles(skOption.ModGame, @"Veteran", contentDirectory, outfits, getGamePatchModDirectory);
                }
            }
            if (errorMessage != null)
            {
                throw new Exception(errorMessage);
            }

            return modPath;
        }
    }
}
