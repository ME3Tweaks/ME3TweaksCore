using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Packages.CloningImportingAndRelinking;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.Classes;
using LegendaryExplorerCore.Unreal.ObjectInfo;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Services;
using ME3TweaksCore.Services.Shared.BasegameFileIdentification;
using ME3TweaksCore.Targets;
using Newtonsoft.Json;

namespace ME3TweaksCore.ME3Tweaks.M3Merge.Bio2DATable
{
    /// <summary>
    /// Handles the Bio2DA merge feature.
    /// </summary>
    public class Bio2DAMerge
    {
        private const string BIO2DA_MERGE_FILE_SUFFIX = @".m3da";
        public const string BIO2DA_BGFIS_DATA_BLOCK = @"BGFIS-Bio2DAMerge";

        private static readonly string[] Mergable2DAFiles = new[]
        {
            @"Engine.pcc",
            @"SFXGame.pcc",
            @"EntryMenu.pcc",

            // Bring Down The Sky
            @"BIOG_2DA_UNC_AreaMap_X.pcc",
            @"BIOG_2DA_UNC_GalaxyMap_X.pcc",
            @"BIOG_2DA_UNC_GamerProfile_X.pcc",
            @"BIOG_2DA_UNC_Movement_X.pcc",
            @"BIOG_2DA_UNC_Music_X.pcc",
            @"BIOG_2DA_UNC_Talents_X.pcc",
            @"BIOG_2DA_UNC_TreasureTables_X.pcc",
            @"BIOG_2DA_UNC_UI_X.pcc",
        };

#if DEBUG
        public static void BuildVanillaTables(GameTarget target)
        {
            var vPackage = MEPackageHandler.CreateAndOpenPackage(@"B:\UserProfile\source\repos\ME3Tweaks\MassEffectModManager\submodules\ME3TweaksCore\ME3TweaksCore\ME3Tweaks\M3Merge\Bio2DATable\VanillaTables.pcc", MEGame.LE1);
            var loadedFiles = target.GetFilesLoadedInGame();
            foreach (var file in Mergable2DAFiles)
            {
                if (loadedFiles.TryGetValue(file, out var filepath))
                {
                    var package = MEPackageHandler.OpenMEPackage(filepath);
                    foreach (var exp in package.Exports.Where(x => !x.IsDefaultObject && x.IsA(@"Bio2DA")))
                    {
                        EntryExporter.ExportExportToPackage(exp, vPackage, out _);
                    }
                }
            }

            vPackage.Save();
        }
#endif

        public static bool RunBio2DAMerge(GameTarget target)
        {
            MLog.Information($@"Performing Bio2DA Merge for game: {target.TargetPath}");

            //var coalescedStream = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.M3Merge.LE1Config.Coalesced_INT.bin");
            //var configBundle = ConfigAssetBundle.FromSingleStream(MEGame.LE1, coalescedStream);

            var dlcMountsInOrder = MELoadedDLC.GetDLCNamesInMountOrder(target.Game, target.TargetPath);

            // For BGFIS
            bool mergedAny = false;
            string recordedMergeName = @"";


            // Map: Filepath -> list of applied m3da filenames
            var recordedApplications = new CaseInsensitiveDictionary<List<string>>();
            void recordM3DAApplication(IMEPackage package, string displayName)
            {
                if (!recordedApplications.TryGetValue(package.FilePath, out var list))
                {
                    list = new List<string>();
                    recordedApplications[package.FilePath] = list;
                }

                if (!list.Contains(displayName, StringComparer.InvariantCultureIgnoreCase))
                {
                    list.Add(displayName);
                }
            }


            // Step 1: Load all modifiable packages
            var loadedFiles = target.GetFilesLoadedInGame();
            var packageContainer = new Bio2DAMergePackageContainer();
            foreach (var file in Mergable2DAFiles)
            {
                if (loadedFiles.TryGetValue(file, out var filepath))
                {
                    // Hash the files before we open them so we can pull the information from Basegame File Identification Service.
                    var packageData = MEPackageHandler.ReadAllFileBytesIntoMemoryStream(filepath);
                    packageContainer.OriginalHashes[target.GetRelativePath(filepath)] =
                        MUtilities.CalculateHash(packageData);

                    var package = MEPackageHandler.OpenMEPackageFromStream(packageData, filepath);
                    packageContainer.InsertTargetPackage(package);
                }
            }

            // Step 2: Reset all tables
            var vanillaTables = MEPackageHandler.OpenMEPackageFromStream(MUtilities.ExtractInternalFileToStream(@"ME3TweaksCore.ME3Tweaks.M3Merge.Bio2DATable.VanillaTables.pcc"));
            var vanilla2DAs = vanillaTables.Exports.Where(x => x.IsA(@"Bio2DA")).ToList();
            foreach (var file in packageContainer.GetTargetablePackages())
            {
                foreach (var exp in vanilla2DAs)
                {
                    var matchingExp = file.FindExport(exp.InstancedFullPath);
                    if (matchingExp != null)
                    {
                        // Reset the 2DA to prepare for changes
                        packageContainer.VanillaTableNames ??= new List<string>();
                        packageContainer.VanillaTableNames.Add(matchingExp.ObjectName.Instanced);
                        EntryImporter.ImportAndRelinkEntries(EntryImporter.PortingOption.ReplaceSingularWithRelink, exp,
                            file, matchingExp, true, new RelinkerOptionsPackage(), out _);

                        if (matchingExp.DataChanged)
                        {
                            Debug.WriteLine($@"Reset table: {matchingExp.InstancedFullPath} in {file.FileNameNoExtension}");
                        }
                    }
                }
            }


            foreach (var dlc in dlcMountsInOrder)
            {
                var dlcCookedPath = Path.Combine(target.GetDLCPath(), dlc, target.Game.CookedDirName());

                MLog.Information($@"Looking for {BIO2DA_MERGE_FILE_SUFFIX} files in {dlcCookedPath}");
                var m3das = Directory
                    .GetFiles(dlcCookedPath, $@"{dlc}-*" + BIO2DA_MERGE_FILE_SUFFIX, SearchOption.AllDirectories)
                    .ToList(); // Find all M3DA files
                MLog.Information($@"Found {m3das.Count} m3da files to parse");

                foreach (var m3daF in m3das)
                {
                    MLog.Information($@"Merging M3 Bio2DA Merge Manifest {m3daF}");
                    var result = MergeManifest(dlcCookedPath, m3daF, target, recordM3DAApplication, packageContainer);
                    if (!result)
                    {
                        // Merge failed. // Todo: Hook up to the UI in M3 via the params
                    }
                }


            }

            var records = new List<BasegameFileRecord>();
            foreach (var file in packageContainer.GetTargetablePackages())
            {
                // Todo: Record merges for BGFIS
                if (file.IsModified)
                {
                    MLog.Information($@"Saving 2DA merged package {file.FilePath}");
                    var outStream = file.SaveToStream(true); // We only support LE1 so its always true

                    recordedApplications.TryGetValue(file.FilePath, out var recordedMergesForFile);
                    var record = CreateRecord(target, packageContainer, file, outStream, recordedMergesForFile, false, out var savedVanilla);
                    if (!savedVanilla)
                    {
                        outStream.WriteToFile(file.FilePath); // Save to disk
                    }

                    // Create record
                    records.Add(record);
                }
            }

            // Set the BGFIS record name
            if (records.Any())
            {
                // Submit to BGFIS
                BasegameFileIdentificationService.AddLocalBasegameIdentificationEntries(records);
                return true;
            }

            return false;
        }

        private static BasegameFileRecord CreateRecord(GameTarget target, Bio2DAMergePackageContainer packageContainer, IMEPackage finalPackage, MemoryStream finalPackageStream, List<string> recordedMerges, bool localize, out bool savedVanilla)
        {
            savedVanilla = false;

            // We are going to check if this is the vanilla package. We must strip off the LECL tag. MEM marker will not be here since it was saved with LEC.
            finalPackageStream.Seek(-8, SeekOrigin.End);
            var tagSize = finalPackageStream.ReadInt32();
            finalPackageStream.Seek(tagSize - 8, SeekOrigin.Current);

            var lecllessSize = (int)finalPackageStream.Position;
            finalPackageStream.Position = 0;
            var lecllessMd5 = MUtilities.CalculateHash(finalPackageStream, byteLenToHash: lecllessSize);
            var isVanilla = VanillaDatabaseService.IsFileVanilla(target.Game, target.GetRelativePath(finalPackage.FilePath), false, lecllessSize, lecllessMd5);

            if (isVanilla)
            {
                savedVanilla = true;
                // If this file had not been saved with LECLData, it would be vanilla. We are going to truncate it here.
                finalPackageStream.SetLength(lecllessSize);
                finalPackageStream.WriteToFile(finalPackage.FilePath);
                return null;
            }

            // It is not vanilla.
            var finalHash = MUtilities.CalculateHash(finalPackageStream); // The saved package.
            var originalHash = packageContainer.OriginalHashes[target.GetRelativePath(finalPackage.FilePath)];
            var originalInfo = BasegameFileIdentificationService.GetBasegameFileSource(target, finalPackage.FilePath, originalHash);
            var newInfoString = @"";

            if (originalInfo != null)
            {
                newInfoString = originalInfo.GetWithoutBlock(BIO2DA_BGFIS_DATA_BLOCK);
            }

            if (recordedMerges != null && recordedMerges.Any())
            {
                if (!string.IsNullOrWhiteSpace(newInfoString))
                {
                    newInfoString += "\n"; // do not localize
                }
                newInfoString += BasegameFileRecord.CreateBlock(BIO2DA_BGFIS_DATA_BLOCK, string.Join(BasegameFileRecord.BLOCK_SEPARATOR, recordedMerges));
            }

            if (recordedMerges == null && string.IsNullOrWhiteSpace(newInfoString))
            {
                // Edge case: Names were added to the name table for our custom merged 2DA.
                // Unfortunately we have no way to reset this because we have no idea what names were 
                // added unless we compared to something else and figured out if any were
                // still in use, and that would be slow. So that's not really helpful here...
                if (localize)
                {
                    newInfoString = "(Vanilla - all M3DAs reverted)";
                }
                else
                {
                    newInfoString = @"(Vanilla - all M3DAs reverted)"; // This is not localized as it will show in diagnostics.
                }
            }

            return new BasegameFileRecord(target.GetRelativePath(finalPackage.FilePath), (int)finalPackageStream.Length, target.Game, newInfoString, finalHash);

        }

        /// <summary>
        /// Merges a single manifest file (can contain multiple files to merge)
        /// </summary>
        /// <param name="dlcCookedPath"></param>
        /// <param name="mergeFilePath"></param>
        /// <param name="target"></param>
        /// <param name="recordMerge"></param>
        /// <param name="packageContainer"></param>
        /// <exception cref="Exception">When there's an error in input. Error applying data itself will not throw.</exception>
        /// <returns></returns>
        private static bool MergeManifest(string dlcCookedPath, string mergeFilePath, GameTarget target, Action<IMEPackage, string> recordMerge, Bio2DAMergePackageContainer packageContainer)
        {
            var mergeData = File.ReadAllText(mergeFilePath);
            var mergeObject = JsonConvert.DeserializeObject<List<Bio2DAMergeManifest>>(mergeData);
            var mergedResult = false;

            foreach (var obj in mergeObject)
            {
                var destPackage = packageContainer.GetTargetPackage(Path.Combine(target.GetCookedPath(), obj.GamePackageFile));
                if (destPackage == null)
                {
                    MLog.Error($@"Bio2DA merge 'packagefile' is invalid: {obj.GamePackageFile} - cannot merge into non-basegame/Bring Down The Sky 2DA files");
                    MLog.Information(@"Packages in the 2DA cache:");
                    foreach (var package in packageContainer.GetTargetablePackages())
                    {
                        MLog.Information($"  {package.FilePath}");
                    }
                    throw new Exception(LC.GetString(LC.string_interp_2damerge_invalidTargetFile, obj.GamePackageFile));
                }

                var basePackagePath = Path.Combine(target.GetCookedPath(), obj.GamePackageFile);
                if (!File.Exists(basePackagePath))
                {
                    MLog.Error($@"Bio2DA merge 'packagefile' is invalid: {obj.GamePackageFile} - could not find in basegame CookedPCConsole folder of target");
                    throw new Exception(LC.GetString(LC.string_interp_2damerge_couldNotFindTarget, obj.GamePackageFile));
                }

                var modPackagePath = Directory.GetFiles(dlcCookedPath, obj.ModPackageFile, SearchOption.AllDirectories).FirstOrDefault();
                if (modPackagePath == null)
                {
                    MLog.Error($@"Bio2DA merge 'mergepackagefile' is invalid: {obj.ModPackageFile} - could not find in CookedPCConsole folder of mod");
                    throw new Exception(LC.GetString(LC.string_interp_2damerge_couldNotFindSourcePackage, obj.ModPackageFile));
                }

                var baseFile = packageContainer.GetTargetPackage(basePackagePath);
                var modFile = packageContainer.GetModPackage(modPackagePath);
                if (modFile == null)
                {
                    // Needs opened and cached
                    modFile = MEPackageHandler.OpenMEPackage(modPackagePath);
                    packageContainer.InsertModPackage(modFile);
                }
                foreach (var table in obj.ModTables)
                {
                    string objNameStr = table;
                    int dotIdx = objNameStr.LastIndexOf('.');
                    if (dotIdx > 0)
                    {
                        objNameStr = objNameStr[(dotIdx + 1)..];
                    }

                    var objName = NameReference.FromInstancedString(objNameStr);
                    if (!objName.Name.EndsWith(@"_part"))
                    {
                        MLog.Error($@"Bio2DA merge 'mergetables' value is invalid: {table} - base name of object does not end with _part");
                        throw new Exception(LC.GetString(LC.string_interp_2damerge_invalidTableNameMissingPart, table));
                        return false;
                    }

                    var tableName = objName.Name.Substring(0, objName.Name.Length - 5); // Remove _part. The table name should not be indexed... probably

                    var modTable = modFile.FindExport(table); // Find by IFP.
                    if (modTable == null)
                    {
                        MLog.Error($@"Bio2DA merge 'mergetables' value is invalid: {table} - could not find table with that instanced full path in package '{modPackagePath}'");
                        throw new Exception(LC.GetString(LC.string_interp_2damerge_invalidCouldNotFindSourceTableExport, table, modPackagePath));
                        return false;
                    }

                    if (!modTable.IsA(@"Bio2DA"))
                    {
                        MLog.Error($@"Bio2DA merge 'mergetables' value is invalid: {table} - export is not a Bio2DA or subclass. It was: {modTable.ClassName}");
                        throw new Exception(LC.GetString(LC.string_interp_2damerge_invalidSourceObjectIsNot2DA, table, modTable.ClassName));
                        return false;
                    }

                    var baseTable = baseFile.Exports.FirstOrDefault(x => !x.IsDefaultObject && x.IsA(@"Bio2DA") &&
                                                                         (x.ObjectName.Instanced.CaseInsensitiveEquals(tableName) || // Direct name
                                                                          //10/31/2024 - Fix targetting _part tables in BDTS tables that we reset
                                                                         (x.ObjectName.Name.Length > 5 && x.ObjectName.Name.StartsWith(tableName, StringComparison.CurrentCultureIgnoreCase) && x.ObjectName.Name.EndsWith("_part")) // Targetting _part table in BDTS tables
                                                                         ));
                    if (baseTable == null)
                    {
                        MLog.Error($@"Bio2DA merge 'mergetables' value is invalid: {table} - could not find basegame table with base name '{tableName}' name in package '{basePackagePath}'");
                        throw new Exception(LC.GetString(LC.string_interp_2damerge_invalidCouldNotFindTargetTable, table, tableName, basePackagePath));
                    }

                    // Check basetable is actually a vanilla table
                    // 10/31/2024 - Strip _part so we can successfully target BDTS tables
                    var baseTableName = baseTable.ObjectName.Name.EndsWith("_part") ? baseTable.ObjectName.Name[..^5] : baseTable.ObjectName.Instanced;
                    if (!packageContainer.VanillaTableNames.Contains(baseTableName, StringComparer.InvariantCultureIgnoreCase))
                    {
                        MLog.Error($@"Bio2DA merge 'mergetables' value is invalid: {table} - this is not a vanilla table. Bio2DA merge does not work with non-vanilla tables.");
                        throw new Exception(LC.GetString(LC.string_interp_2damerge_invalidNotAVanillaTable, table));
                    }

                    Bio2DA mod2DA = new Bio2DA(modTable);
                    Bio2DA base2DA = new Bio2DA(baseTable);
                    var mergedCount = mod2DA.MergeInto(base2DA, out var result);
                    if (result == Bio2DAMergeResult.OK)
                    {
                        MLog.Information($@"Bio2DA merged {mergedCount.Count} rows from {table} in {modTable.FileRef.FilePath} into {base2DA.Export.ObjectName.Instanced} in {baseTable.FileRef.FileNameNoExtension}");
                        mergedResult |= mergedCount.Any();
                        base2DA.Write2DAToExport();
                        recordMerge(baseFile, Path.GetFileName(mergeFilePath)); // Record we applied this m3cd to this package
                    }
                    else
                    {
                        MLog.Error($@"Bio2DA merge into {tableName} from {table} failed with result {result}");
                        // We will not throw an exception here
                        TelemetryInterposer.TrackError(new Exception(@"Bio2DA Merge Failed"), new Dictionary<string, string>()
                        {
                            {@"Table name", baseTable.InstancedFullPath},
                            {@"Result", result.ToString()},
                            {@"Mod Table", modTable.InstancedFullPath},
                            {@"Mod Package", modPackagePath}
                        });
                        return false;
                    }
                }
            }

            return mergedResult;
        }

        /// <summary>
        /// Gets a list of Bio2DA merges into the given basegame file record
        /// </summary>
        /// <returns></returns>
        internal static List<string> GetMergedFilenames(BasegameFileRecord info)
        {
            List<string> merges = new List<string>(0);
            var blockText = info.GetBlock(BIO2DA_BGFIS_DATA_BLOCK);

            if (blockText != null)
            {
                merges = blockText.Split(BasegameFileRecord.BLOCK_SEPARATOR).ToList();
            }

            return merges;
        }
    }
}
