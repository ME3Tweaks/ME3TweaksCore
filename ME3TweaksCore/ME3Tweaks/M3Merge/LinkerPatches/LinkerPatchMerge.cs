using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.Coalesced;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Exceptions;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.ME3Tweaks.ModManager;
using ME3TweaksCore.Targets;

namespace ME3TweaksCore.ME3Tweaks.M3Merge.LinkerPatches
{
    /// <summary>
    /// Merges the linker patches declared by DLC mods (.m3lp manifests in DLC_MOD_X\LinkerPatches) into the global BioGame\LinkerPatches folder that MilanHost loads.
    /// The output is rebuilt from scratch every time this merge runs.
    /// </summary>
    public static class LinkerPatchMerge
    {
        /// <summary>
        /// Extension of linker patch manifests
        /// </summary>
        public const string LINKER_PATCH_MANIFEST_EXTENSION = @".m3lp";

        /// <summary>
        /// Name of the folder in a DLC mod (sibling of CookedPCConsole) that holds manifests and patch packages
        /// </summary>
        public const string LINKER_PATCH_DLC_FOLDERNAME = @"LinkerPatches";

        /// <summary>
        /// Name of the output folder under BioGame
        /// </summary>
        public const string LINKER_PATCH_OUTPUT_FOLDERNAME = @"LinkerPatches";

        /// <summary>
        /// Required prefix of patch package filenames. Case sensitive.
        /// </summary>
        public const string LINKER_PATCH_PACKAGE_PREFIX = @"Patch_";

        /// <summary>
        /// Filename of the coalesced index that MilanHost reads the redirect table from
        /// </summary>
        public const string LINKER_PATCH_INDEX_FILENAME = @"Default_LinkerPatches.bin";

        /// <summary>
        /// Filename of the build record, in the root of the output folder
        /// </summary>
        public const string LINKER_PATCH_BUILD_RECORD_FILENAME = @"_m3linkerpatches.json";

        // Index (coalesced) layout. This is a contract with MilanHost's LinkerPatcher.
        internal const string INDEX_ASSET_NAME = @"bioengine.ini";
        internal const string INDEX_ASSET_SOURCE = @"..\..\biogame\config\bioengine.ini";
        internal const string INDEX_SECTION_NAME = @"linkerpatchobjects";
        internal const string INDEX_PROPERTY_NAME = @"redirects";

        /// <summary>
        /// A manifest that contributes to the merge
        /// </summary>
        /// <param name="DLCFolderName">DLC folder the manifest is in</param>
        /// <param name="ManifestPath">Full path to the manifest</param>
        /// <param name="ManifestRelPath">Path of the manifest relative to the DLC folder</param>
        /// <param name="MetaCMM">The _metacmm.txt of the DLC</param>
        public record LinkerPatchManifestSource(string DLCFolderName, string ManifestPath, string ManifestRelPath, MetaCMM MetaCMM);

        /// <summary>
        /// A redirect being built, with the info needed to copy its package
        /// </summary>
        private class PendingRedirect
        {
            public LinkerPatchRedirectRecord Record { get; init; }
            public string SourcePackagePath { get; init; }
        }

        /// <summary>
        /// An original object that must be checked against the game files
        /// </summary>
        private record PendingOriginalValidation(string OriginalObject, string PatchClassName, string PatchObjectPath, string DLCFolderName, string ManifestFile, string OriginalPackageHint);

        #region Paths
        /// <summary>
        /// Gets the BioGame\LinkerPatches folder of a game
        /// </summary>
        public static string GetLinkerPatchesRootPath(MEGame game, string gameRoot) => Path.Combine(MEDirectories.GetBioGamePath(game, gameRoot), LINKER_PATCH_OUTPUT_FOLDERNAME);

        /// <summary>
        /// Gets the BioGame\LinkerPatches\CookedPCConsole folder of a game, which holds the index and patch packages
        /// </summary>
        public static string GetLinkerPatchesCookedPath(MEGame game, string gameRoot) => Path.Combine(GetLinkerPatchesRootPath(game, gameRoot), game.CookedDirName());

        /// <summary>
        /// Gets the path of the build record of a game
        /// </summary>
        public static string GetBuildRecordPath(MEGame game, string gameRoot) => Path.Combine(GetLinkerPatchesRootPath(game, gameRoot), LINKER_PATCH_BUILD_RECORD_FILENAME);
        #endregion

        /// <summary>
        /// Returns if a filename is a valid patch package name: it must start with Patch_ (case sensitive) and end with .pcc, and must not be a path.
        /// </summary>
        /// <param name="packageFileName"></param>
        /// <returns></returns>
        public static bool IsValidPatchPackageName(string packageFileName)
        {
            if (string.IsNullOrWhiteSpace(packageFileName)) return false;
            if (packageFileName.IndexOfAny(new[] { '\\', '/', ':' }) >= 0) return false;
            if (!packageFileName.StartsWith(LINKER_PATCH_PACKAGE_PREFIX, StringComparison.Ordinal)) return false;
            if (!packageFileName.EndsWith(@".pcc", StringComparison.OrdinalIgnoreCase)) return false;
            return packageFileName.Length > LINKER_PATCH_PACKAGE_PREFIX.Length + 4; // Must have something between Patch_ and .pcc
        }

        /// <summary>
        /// Gets the package name (without extension) a patch package is installed as. Patch_X.pcc from DLC_MOD_Foo becomes Patch_DLC_MOD_Foo_X.
        /// This is the PatchObject value in the index.
        /// </summary>
        /// <param name="dlcFolderName">Source DLC folder name</param>
        /// <param name="patchPackageFileName">Source patch package filename</param>
        /// <returns></returns>
        public static string GetOutputPackageName(string dlcFolderName, string patchPackageFileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(patchPackageFileName).Substring(LINKER_PATCH_PACKAGE_PREFIX.Length);
            return $@"{LINKER_PATCH_PACKAGE_PREFIX}{dlcFolderName}_{baseName}";
        }

        /// <summary>
        /// If the object path is inside a level (TheWorld or below). Level objects cannot be linker patched.
        /// </summary>
        public static bool IsLevelObjectPath(string objectPath) => objectPath.Split('.').Any(x => x.Equals(@"TheWorld", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// If the export is a World or Level, or is inside one
        /// </summary>
        private static bool IsInLevel(IEntry entry)
        {
            for (var e = entry; e != null; e = e.Parent)
            {
                if (e.ClassName is @"World" or @"Level")
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Enumerates the manifests that contribute to the merge, in mount order (lowest priority first). A DLC only contributes if it was installed
        /// from a moddesc with feature level 10.0 or higher. This is the only place manifests are enumerated, so <see cref="NeedsMerged(GameTarget)"/>,
        /// the merge, and staleness detection always agree.
        /// </summary>
        /// <param name="target">Target to enumerate</param>
        /// <param name="logSkipped">If DLC that have manifests but are skipped should be logged</param>
        /// <returns></returns>
        public static List<LinkerPatchManifestSource> GetContributingManifests(GameTarget target, bool logSkipped = false) => GetContributingManifests(target.Game, target.TargetPath, logSkipped);

        /// <summary>
        /// Enumerates the manifests that contribute to the merge for the game at the specified root. See <see cref="GetContributingManifests(GameTarget, bool)"/>.
        /// </summary>
        public static List<LinkerPatchManifestSource> GetContributingManifests(MEGame game, string gameRoot, bool logSkipped = false)
        {
            var result = new List<LinkerPatchManifestSource>();
            if (!game.SupportsLinkerPatchMerge())
                return result;

            var dlcPath = MEDirectories.GetDLCPath(game, gameRoot);
            // Same as GameTarget.GetInstalledDLCByMountPriority() - lowest priority first, enabled only
            foreach (var dlc in MELoadedDLC.GetDLCNamesInMountOrder(game, gameRoot))
            {
                if (dlc == M3MergeDLC.MERGE_DLC_FOLDERNAME)
                    continue; // Not used

                var linkerPatchDir = Path.Combine(dlcPath, dlc, LINKER_PATCH_DLC_FOLDERNAME);
                if (!Directory.Exists(linkerPatchDir))
                    continue;

                var manifests = Directory.GetFiles(linkerPatchDir, @"*" + LINKER_PATCH_MANIFEST_EXTENSION, SearchOption.TopDirectoryOnly)
                    .Where(x => Path.GetExtension(x).Equals(LINKER_PATCH_MANIFEST_EXTENSION, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (manifests.Count == 0)
                    continue;

                // Same as GameTarget.GetMetaCMMForDLC()
                var metaPath = Path.Combine(dlcPath, dlc, @"_metacmm.txt");
                var metacmm = File.Exists(metaPath) ? new MetaCMM(metaPath) : null;
                if (metacmm == null || metacmm.ModDescFeatureLevel < ModDescConsts.MODDESC_VERSION_10_0)
                {
                    // Linker patches are a moddesc 10.0 feature
                    MLog.Warning($@"LINKERPATCH: {dlc} has {LINKER_PATCH_MANIFEST_EXTENSION} files but was not installed with moddesc 10.0 or higher. Its linker patches will not be used", logSkipped);
                    continue;
                }

                foreach (var manifest in manifests)
                {
                    result.Add(new LinkerPatchManifestSource(dlc, manifest, Path.Combine(LINKER_PATCH_DLC_FOLDERNAME, Path.GetFileName(manifest)), metacmm));
                }
            }

            return result;
        }

        /// <summary>
        /// Returns if the linker patch merge should run on the target: either a DLC contributes manifests, or there is existing output that must be cleaned up.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static bool NeedsMerged(GameTarget target) => NeedsMerged(target.Game, target.TargetPath);

        /// <summary>
        /// Returns if the linker patch merge should run on the game at the specified root. See <see cref="NeedsMerged(GameTarget)"/>.
        /// </summary>
        public static bool NeedsMerged(MEGame game, string gameRoot)
        {
            if (!game.SupportsLinkerPatchMerge()) return false;
            return GetContributingManifests(game, gameRoot).Any() || Directory.Exists(GetLinkerPatchesRootPath(game, gameRoot));
        }

        /// <summary>
        /// Loads the build record of the target. Returns null if there is none or it cannot be read.
        /// </summary>
        public static LinkerPatchBuildRecord LoadBuildRecord(MEGame game, string gameRoot)
        {
            var recordPath = GetBuildRecordPath(game, gameRoot);
            try
            {
                return LinkerPatchBuildRecord.Load(recordPath);
            }
            catch (Exception e)
            {
                MLog.Exception(e, $@"Could not read linker patch build record {recordPath}:");
                return null;
            }
        }

        /// <summary>
        /// Returns if the installed linker patches no longer match the manifests of the enabled DLC, and the merge needs to be run again.
        /// </summary>
        /// <param name="game">Game</param>
        /// <param name="gameRoot">Root of the game</param>
        /// <param name="record">The current build record, may be null</param>
        /// <returns></returns>
        public static bool IsBuildStale(MEGame game, string gameRoot, LinkerPatchBuildRecord record)
        {
            if (!game.SupportsLinkerPatchMerge()) return false;
            var current = GetContributingManifests(game, gameRoot);
            if (record == null)
            {
                // Manifests that were never built, or leftover output that has no record
                return current.Any() || Directory.Exists(GetLinkerPatchesRootPath(game, gameRoot));
            }

            var recorded = record.SourceManifests ?? new List<LinkerPatchSourceManifestRecord>();
            if (recorded.Count != current.Count)
                return true;

            var recordedHashes = new CaseInsensitiveDictionary<string>();
            foreach (var r in recorded)
            {
                recordedHashes[Path.Combine(r.DLC ?? @"", r.ManifestRelPath ?? @"")] = r.MD5;
            }

            foreach (var c in current)
            {
                if (!recordedHashes.TryGetValue(Path.Combine(c.DLCFolderName, c.ManifestRelPath), out var md5) || !string.Equals(md5, MUtilities.CalculateHash(c.ManifestPath), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Output was removed out from under the record
            if (record.Redirects != null && record.Redirects.Any() && !File.Exists(Path.Combine(GetLinkerPatchesCookedPath(game, gameRoot), LINKER_PATCH_INDEX_FILENAME)))
                return true;

            return false;
        }

        /// <summary>
        /// Rebuilds BioGame\LinkerPatches from the manifests of the enabled DLC mods of the target. On failure, BioGame\LinkerPatches is deleted and the exception is rethrown.
        /// </summary>
        /// <param name="target">Target to merge</param>
        /// <param name="log">Optional callback that receives each redirect log line</param>
        public static void RunLinkerPatchMerge(GameTarget target, Action<string> log = null)
        {
            RunLinkerPatchMerge(target.Game, target.TargetPath, log, true);
        }

        /// <summary>
        /// Rebuilds BioGame\LinkerPatches for the game at the specified root. See <see cref="RunLinkerPatchMerge(GameTarget, Action{string})"/>.
        /// </summary>
        /// <param name="game">Game</param>
        /// <param name="gameRoot">Root of the game</param>
        /// <param name="log">Optional callback that receives each redirect log line</param>
        /// <param name="validateOriginalsAgainstGame">If each original object must be found in the game's files with the same class as its patch. Only tests should turn this off.</param>
        public static void RunLinkerPatchMerge(MEGame game, string gameRoot, Action<string> log, bool validateOriginalsAgainstGame)
        {
            void logLine(string line)
            {
                MLog.Information(line);
                log?.Invoke(line);
            }

            var outputRoot = GetLinkerPatchesRootPath(game, gameRoot);
            var sw = Stopwatch.StartNew();
            MLog.Information($@"LINKERPATCH: Running linker patch merge for {gameRoot}");
            try
            {
                var manifests = GetContributingManifests(game, gameRoot, true);
                var loadedFiles = MELoadedFiles.GetFilesLoadedInGame(game, gameRootOverride: gameRoot);
                var dlcPath = MEDirectories.GetDLCPath(game, gameRoot);

                // Original object -> winning redirect. Later (higher mount) DLC overwrite earlier ones.
                var winners = new CaseInsensitiveDictionary<PendingRedirect>();
                var sourceRecords = new List<LinkerPatchSourceManifestRecord>();
                var originalValidations = new List<PendingOriginalValidation>();

                foreach (var dlcManifests in manifests.GroupBy(x => x.DLCFolderName)) // GroupBy keeps mount order
                {
                    var dlc = dlcManifests.Key;
                    var modName = dlcManifests.First().MetaCMM?.ModName;
                    var dlcOriginals = new CaseInsensitiveDictionary<string>(); // Original object -> manifest that declared it
                    var openedPackages = new CaseInsensitiveDictionary<IMEPackage>();
                    try
                    {
                        foreach (var source in dlcManifests)
                        {
                            var manifestFile = Path.GetFileName(source.ManifestPath);
                            sourceRecords.Add(new LinkerPatchSourceManifestRecord()
                            {
                                DLC = dlc,
                                ManifestRelPath = source.ManifestRelPath,
                                MD5 = MUtilities.CalculateHash(source.ManifestPath)
                            });

                            LinkerPatchManifest manifest;
                            try
                            {
                                manifest = LinkerPatchManifest.Load(source.ManifestPath);
                            }
                            catch (Exception e)
                            {
                                MLog.Exception(e, $@"LINKERPATCH: Could not read manifest {source.ManifestPath}:");
                                throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_invalidManifest, manifestFile, dlc, e.Message));
                            }

                            if (manifest == null)
                                throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_invalidManifest, manifestFile, dlc, @"null"));
                            if (manifest.Game != game)
                                throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_manifestWrongGame, manifestFile, dlc, manifest.Game, game));

                            MLog.Information($@"LINKERPATCH: Processing {dlc}\{source.ManifestRelPath} ({manifest.Patches?.Count ?? 0} patches): {manifest.Description ?? @"(no description)"}");
                            var patches = manifest.Patches ?? new List<LinkerPatchManifestEntry>();
                            for (int i = 0; i < patches.Count; i++)
                            {
                                var entry = patches[i];
                                if (entry == null || string.IsNullOrWhiteSpace(entry.OriginalObject) || string.IsNullOrWhiteSpace(entry.PatchPackage)
                                    || entry.OriginalObject.Contains('"') || entry.OriginalObject.Split('.').Any(string.IsNullOrWhiteSpace))
                                {
                                    throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_invalidEntry, manifestFile, dlc, i + 1));
                                }

                                var original = entry.OriginalObject.Trim();
                                var packageFile = entry.PatchPackage.Trim();

                                // Level objects are not supported: worlds unload, and a rooted patch object would keep the level alive
                                if (IsLevelObjectPath(original))
                                    throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_levelObjectNotAllowed, original, dlc, manifestFile));
                                if (!IsValidPatchPackageName(packageFile))
                                    throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_invalidPackageName, packageFile, dlc, manifestFile));

                                if (dlcOriginals.TryGetValue(original, out var firstManifest))
                                    throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_duplicateInDLC, dlc, original, firstManifest, manifestFile));
                                dlcOriginals[original] = manifestFile;

                                // The runtime looks up <PatchObject>.<OriginalObject>: the patch object mirrors the original's path inside the patch package.
                                // MilanHost un-forces the tree it is in when loading the patch package, so it does not collide with the original in memory.
                                var packagePath = Path.Combine(dlcPath, dlc, LINKER_PATCH_DLC_FOLDERNAME, packageFile);
                                var package = OpenPatchPackage(game, dlc, manifestFile, packageFile, packagePath, openedPackages);
                                var patchExport = package.Exports.FirstOrDefault(x => x.InstancedFullPath.Equals(original, StringComparison.OrdinalIgnoreCase));
                                if (patchExport == null)
                                    throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_missingPatchObject, packageFile, dlc, original));

                                originalValidations.Add(new PendingOriginalValidation(original, patchExport.ClassName, $@"{packageFile}: {patchExport.InstancedFullPath}", dlc, manifestFile, string.IsNullOrWhiteSpace(entry.OriginalPackage) ? null : Path.GetFileName(entry.OriginalPackage.Trim())));

                                var pending = new PendingRedirect()
                                {
                                    SourcePackagePath = packagePath,
                                    Record = new LinkerPatchRedirectRecord()
                                    {
                                        OriginalObject = original,
                                        PatchObject = GetOutputPackageName(dlc, packageFile),
                                        PatchPackageFile = packageFile,
                                        SourceDLC = dlc,
                                        SourceModName = modName,
                                        ManifestFile = manifestFile,
                                        ManifestDescription = manifest.Description,
                                        PatchDescription = entry.Description
                                    }
                                };

                                if (winners.TryGetValue(original, out var existing))
                                {
                                    // Lower mount priority DLC patched this object already. This DLC wins.
                                    logLine($@"LINKERPATCH: {original} from {dlc} ({modName}) overrides {existing.Record.SourceDLC} ({existing.Record.SourceModName})");
                                    pending.Record.Overridden.AddRange(existing.Record.Overridden);
                                    pending.Record.Overridden.Add(new LinkerPatchOverrideRecord()
                                    {
                                        DLC = existing.Record.SourceDLC,
                                        ModName = existing.Record.SourceModName,
                                        ManifestFile = existing.Record.ManifestFile,
                                        PatchDescription = existing.Record.PatchDescription
                                    });
                                }

                                winners[original] = pending;
                            }
                        }
                    }
                    finally
                    {
                        foreach (var p in openedPackages.Values)
                        {
                            p.Dispose();
                        }
                    }
                }

                if (validateOriginalsAgainstGame && originalValidations.Any())
                {
                    ValidateOriginalsAgainstGame(loadedFiles, originalValidations);
                }

                // Determine output packages. Only packages that still have a winning redirect are installed.
                var outputPackages = new CaseInsensitiveDictionary<(string sourcePath, string dlc)>(); // Output filename -> source
                foreach (var winner in winners.Values)
                {
                    var outputFile = winner.Record.PatchObject + @".pcc";
                    if (outputPackages.TryGetValue(outputFile, out var existingOutput))
                    {
                        if (!existingOutput.sourcePath.Equals(winner.SourcePackagePath, StringComparison.OrdinalIgnoreCase))
                            throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_outputNameCollision, outputFile, existingOutput.dlc, winner.Record.SourceDLC));
                        continue;
                    }

                    // Real game packages always win package lookup, so the patch package would never load
                    if (loadedFiles.TryGetValue(outputFile, out var gameFile))
                        throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_collidesWithGamePackage, outputFile, winner.Record.SourceDLC, gameFile));

                    outputPackages[outputFile] = (winner.SourcePackagePath, winner.Record.SourceDLC);
                }

                // Rebuild output from scratch
                DeleteOutput(outputRoot, true);
                if (!sourceRecords.Any())
                {
                    MLog.Information(@"LINKERPATCH: No DLC contribute linker patches, output has been removed");
                    return;
                }

                var redirects = winners.Values.Select(x => x.Record).OrderBy(x => x.OriginalObject, StringComparer.OrdinalIgnoreCase).ToList();
                if (redirects.Any())
                {
                    var cookedPath = GetLinkerPatchesCookedPath(game, gameRoot);
                    Directory.CreateDirectory(cookedPath);
                    foreach (var outputPackage in outputPackages)
                    {
                        File.Copy(outputPackage.Value.sourcePath, Path.Combine(cookedPath, outputPackage.Key));
                    }

                    File.WriteAllBytes(Path.Combine(cookedPath, LINKER_PATCH_INDEX_FILENAME), BuildIndex(redirects).ToArray());
                }
                else
                {
                    // Manifests exist but declare nothing. Only the build record is written, so staleness detection still knows these manifests were built.
                    Directory.CreateDirectory(outputRoot);
                    MLog.Information(@"LINKERPATCH: Contributing manifests do not declare any patches");
                }

                foreach (var redirect in redirects)
                {
                    logLine($@"LINKERPATCH: {redirect.OriginalObject} -> {redirect.PatchObject} from {redirect.SourceDLC} ({redirect.SourceModName}) [{redirect.PatchDescription}]");
                }

                var record = new LinkerPatchBuildRecord()
                {
                    BuildTime = DateTime.Now,
                    SourceManifests = sourceRecords,
                    Redirects = redirects
                };
                record.Save(GetBuildRecordPath(game, gameRoot));

                sw.Stop();
                MLog.Information($@"LINKERPATCH: Installed {redirects.Count} linker patches from {outputPackages.Count} packages in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception e)
            {
                MLog.Exception(e, @"LINKERPATCH: Linker patch merge failed, removing linker patches:");
                DeleteOutput(outputRoot, false);
                throw;
            }
        }

        /// <summary>
        /// Builds the coalesced index that MilanHost loads
        /// </summary>
        /// <param name="redirects">Redirects to write</param>
        /// <returns>Stream of the LE3 coalesced file</returns>
        internal static MemoryStream BuildIndex(IEnumerable<LinkerPatchRedirectRecord> redirects)
        {
            var asset = new CoalesceAsset(INDEX_ASSET_NAME) { Source = INDEX_ASSET_SOURCE };
            var section = asset.GetOrAddSection(INDEX_SECTION_NAME);
            foreach (var redirect in redirects)
            {
                section.AddEntry(new CoalesceProperty(INDEX_PROPERTY_NAME, new CoalesceValue($"(OriginalObject=\"{redirect.OriginalObject}\",PatchObject=\"{redirect.PatchObject}\")", CoalesceParseAction.AddUnique))); // do not localize
            }

            return CoalescedConverter.CompileFromMemory(new Dictionary<string, string>() { { @"BioEngine.xml", asset.ToXmlString() } });
        }

        /// <summary>
        /// Opens (or gets the already opened) patch package, validating it
        /// </summary>
        private static IMEPackage OpenPatchPackage(MEGame game, string dlc, string manifestFile, string packageFile, string packagePath, CaseInsensitiveDictionary<IMEPackage> openedPackages)
        {
            if (openedPackages.TryGetValue(packageFile, out var package))
                return package;

            if (!File.Exists(packagePath))
                throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_packageNotFound, packageFile, dlc, manifestFile));

            try
            {
                package = MEPackageHandler.OpenMEPackage(packagePath, forceLoadFromDisk: true);
            }
            catch (Exception e)
            {
                MLog.Exception(e, $@"LINKERPATCH: Could not open patch package {packagePath}:");
                throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_packageCouldNotBeOpened, packageFile, dlc, e.Message));
            }

            openedPackages[packageFile] = package;
            if (package.Game != game)
                throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_packageWrongGame, packageFile, dlc, package.Game, game));

            // The engine raises a fatal mislinked error when a redirected export's patch object is not in a cooked package
            if (!package.Flags.HasFlag(UnrealFlags.EPackageFlags.Cooked))
                throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_packageNotCooked, packageFile, dlc));

            // Patch packages are renamed on install, so imports rooted at any patch package's source name (including its own) would no longer resolve
            var patchImport = package.Imports.FirstOrDefault(x => x.idxLink == 0 && x.ObjectName.Instanced.StartsWith(LINKER_PATCH_PACKAGE_PREFIX, StringComparison.Ordinal));
            if (patchImport != null)
                throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_importsPatchPackage, packageFile, dlc, patchImport.InstancedFullPath));

            return package;
        }

        /// <summary>
        /// Ensures each original object exists in the game and has the same class as its patch, as MilanHost does not check class when swapping objects.
        /// </summary>
        private static void ValidateOriginalsAgainstGame(CaseInsensitiveDictionary<string> loadedFiles, List<PendingOriginalValidation> validations)
        {
            var sw = Stopwatch.StartNew();
            var originalClasses = new CaseInsensitiveDictionary<string>(); // Original object -> class name
            var levelOriginals = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Originals that are inside a level under a name other than TheWorld
            var unresolved = new HashSet<string>(validations.Select(x => x.OriginalObject), StringComparer.OrdinalIgnoreCase);
            var scannedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void scanFile(string filePath)
            {
                if (unresolved.Count == 0 || !scannedFiles.Add(filePath))
                    return;
                using var package = MEPackageHandler.UnsafePartialLoad(filePath, _ => false); // Only tables are needed
                foreach (var exp in package.Exports)
                {
                    var memoryPath = exp.MemoryFullPath;
                    if (unresolved.Remove(memoryPath))
                    {
                        originalClasses[memoryPath] = exp.ClassName;
                        if (IsInLevel(exp))
                            levelOriginals.Add(memoryPath);
                        if (unresolved.Count == 0)
                            return;
                    }
                }
            }

            // Manifest hints are searched first. They are not authoritative, and may point to a file from a mod that isn't installed.
            foreach (var validation in validations.Where(x => x.OriginalPackageHint != null))
            {
                if (unresolved.Contains(validation.OriginalObject) && loadedFiles.TryGetValue(validation.OriginalPackageHint, out var hintPath))
                {
                    scanFile(hintPath);
                }
            }

            // Non forced exports start with the name of the file they are in
            foreach (var original in unresolved.ToList())
            {
                var firstSegment = original.Substring(0, original.IndexOf('.') is var idx and >= 0 ? idx : original.Length);
                if (loadedFiles.TryGetValue(firstSegment + @".pcc", out var filePath))
                {
                    scanFile(filePath);
                }
            }

            // Forced exports can be in any file. Startup files are the most likely place, so they are searched first.
            if (unresolved.Count > 0)
            {
                MLog.Information($@"LINKERPATCH: Searching all game packages for {unresolved.Count} original objects: {string.Join(@", ", unresolved)}");
                var packageFiles = loadedFiles.Values.Where(x => x.EndsWith(@".pcc", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => Path.GetFileName(x).StartsWith(@"Startup", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
                foreach (var packageFile in packageFiles)
                {
                    if (unresolved.Count == 0)
                        break;
                    scanFile(packageFile);
                }
            }

            sw.Stop();
            MLog.Information($@"LINKERPATCH: Validated original objects against {scannedFiles.Count} game packages in {sw.ElapsedMilliseconds}ms");

            foreach (var validation in validations)
            {
                if (!originalClasses.TryGetValue(validation.OriginalObject, out var originalClass))
                {
                    // The hinted package isn't in the game, so the original likely belongs to a mod that isn't installed. The redirect does nothing until it is.
                    if (validation.OriginalPackageHint != null && !loadedFiles.ContainsKey(validation.OriginalPackageHint))
                    {
                        MLog.Warning($@"LINKERPATCH: Cannot validate {validation.OriginalObject} from {validation.DLCFolderName} ({validation.ManifestFile}): hinted package {validation.OriginalPackageHint} is not in the game. Installing the patch without validation.");
                        continue;
                    }
                    throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_originalNotFound, validation.OriginalObject, validation.DLCFolderName, validation.ManifestFile));
                }
                if (levelOriginals.Contains(validation.OriginalObject))
                    throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_levelObjectNotAllowed, validation.OriginalObject, validation.DLCFolderName, validation.ManifestFile));
                if (!originalClass.Equals(validation.PatchClassName, StringComparison.OrdinalIgnoreCase))
                    throw new LinkerPatchMergeException(LC.GetString(LC.string_interp_linkerPatch_classMismatch, validation.OriginalObject, originalClass, validation.PatchObjectPath, validation.PatchClassName, validation.DLCFolderName));
            }
        }

        /// <summary>
        /// Deletes BioGame\LinkerPatches
        /// </summary>
        private static void DeleteOutput(string outputRoot, bool throwOnFailed)
        {
            if (!Directory.Exists(outputRoot))
                return;
            MLog.Information($@"LINKERPATCH: Deleting {outputRoot}");
            try
            {
                MUtilities.DeleteFilesAndFoldersRecursively(outputRoot, throwOnFailed);
            }
            catch (Exception e) when (!throwOnFailed)
            {
                MLog.Exception(e, $@"LINKERPATCH: Could not delete {outputRoot}:");
            }
        }
    }
}
