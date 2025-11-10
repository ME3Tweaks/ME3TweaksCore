using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Services.Backup;
using ME3TweaksCore.Services.Shared.BasegameFileIdentification;
using ME3TweaksCore.Targets;

namespace ME3TweaksCore.ME3Tweaks.M3Merge.GlobalShader
{
    /// <summary>
    /// Handles merging global shader cache for LE games.
    /// </summary>
    public class GlobalShaderMerge
    {
        private static string SHADER_MERGE_PATTERN = @"GlobalShader-*.m3gs";

        public static bool RunShaderMerge(GameTarget target, bool log)
        {
            MLog.Information($@"Performing Shader Merge for game: {target.TargetPath}");
            var backup = BackupService.GetGameBackupPath(target.Game);
            if (backup == null)
            {
                MLog.Warning("Backup not available; cannot perform GlobalShaderCache merge. Skipping.");
                return false;
            }

            var globalShaderCacheF = Path.Combine(backup, "BioGame", "CookedPCConsole", "GlobalShaderCache-PC-D3D-SM5.bin");
            using var fs = File.OpenRead(globalShaderCacheF);
            var globalShaderCache = GlobalShaderCache.ReadGlobalShaderCache(fs, target.Game);
            var shaders = globalShaderCache.Shaders.Values.ToList();

            var dlcMountsInOrder = MELoadedDLC.GetDLCNamesInMountOrder(target.Game, target.TargetPath);

            // For BGFIS
            bool mergedAny = false;
            string recordedMergeName = @"";
            void recordMerge(string displayName)
            {
                mergedAny = true;
                recordedMergeName += displayName + "\n"; // do not localize
            }

            foreach (var dlc in dlcMountsInOrder)
            {
                var dlcCookedPath = Path.Combine(target.GetDLCPath(), dlc, target.Game.CookedDirName());

                MLog.Information($@"Looking for GlobalShader-*.m3gs files in {dlcCookedPath}", log);
                var globalShaders = Directory.GetFiles(dlcCookedPath, GlobalShaderMerge.SHADER_MERGE_PATTERN, SearchOption.TopDirectoryOnly)
                    .ToList();
                MLog.Information($@"Found {globalShaders.Count} m3gs files to apply", log);

                foreach (var gs in globalShaders)
                {
                    var extractedShaderIdx = ExtractShaderIndex(gs, out var shaderIndex);
                    if (extractedShaderIdx && shaderIndex >= 0 && shaderIndex < shaders.Count)
                    {
                        MLog.Information($@"Merging M3 Global Shader {gs}");
                        shaders[shaderIndex].ShaderByteCode = File.ReadAllBytes(gs);
                        recordMerge($@"{dlc}-{Path.GetFileName(gs)}");
                        mergedAny = true;
                    }
                    else
                    {
                        MLog.Error($@"Invalid filename for global shader: {Path.GetFileName(gs)}. Must be in the form: GlobalShader-INDEX[...].m3gs and between 0 and {shaders.Count}. Skipping.");
                    }
                }
            }

            var records = new List<BasegameFileRecord>();
            var outF = Path.Combine(target.GetCookedPath(), "GlobalShaderCache-PC-D3D-SM5.bin");

            // Set the BGFIS record name
            if (mergedAny)
            {
                // Serialize the assets
                var ms = new MemoryStream();
                var gscsc = new PackagelessSerializingContainer(ms, null) { Game = target.Game };
                globalShaderCache.WriteTo(gscsc);
                ms.WriteByte(0); // This forces size change, which lets us tell it's been modified on size check.
                ms.WriteToFile(outF);

                // Submit to BGFIS
                records.Add(new BasegameFileRecord(outF, target, recordedMergeName.Trim()));
                BasegameFileIdentificationService.AddLocalBasegameIdentificationEntries(records);
            }
            else
            {
                File.Copy(globalShaderCacheF, outF, true);
            }

            return true;
        }

        private static bool ExtractShaderIndex(string filepath, out int shaderIndex)
        {
            shaderIndex = -1;
            var filename = Path.GetFileName(filepath);
            filename = filename.Substring(filename.IndexOf('-') + 1);
            var nextDash = filename.IndexOf('-');
            if (nextDash > 0 && int.TryParse(filename.Substring(0, nextDash), out shaderIndex))
            {
                return true;
            }

            return false;
        }

        public static bool NeedsMerged(GameTarget target)
        {
            return true;
            var supercedances = target.GetFileSupercedances([".m3gs"]); //, viaTOC: true);
        }
    }
}