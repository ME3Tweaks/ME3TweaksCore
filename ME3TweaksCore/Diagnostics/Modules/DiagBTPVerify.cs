using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Textures;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.Classes;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Objects;
using ME3TweaksCore.TextureOverride;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Cache for handling a bunch of filestreams.
    /// </summary>
    class FileStreamCache : IDisposable
    {
        private CaseInsensitiveDictionary<FileStream> Streams = new();

        /// <summary>
        /// Gets a filestream for the given path.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public FileStream GetStream(string filePath)
        {
            if (Streams.TryGetValue(filePath, out var s))
            {
                return s;
            }

            s = File.OpenRead(filePath);
            Streams[filePath] = s;
            return s;
        }

        /// <summary>
        /// Disposes all streams
        /// </summary>
        public void Dispose()
        {
            foreach (var fs in Streams.Values)
            {
                fs.Dispose();
            }
            Streams = null;
        }
    }

    /// <summary>
    /// Diagnostic module for verifying BTP files - testing decompression, metadata files, referenced offsets are within bounds, etc.
    /// </summary>
    internal class DiagBTPVerify : DiagModuleBase
    {
        internal override void RunModule(LogUploadPackage package)
        {
            if (!package.DiagnosticTarget.Game.IsLEGame())
            {
                // Target doesn't support BTPs
                return;
            }

            var diag = package.DiagnosticWriter;
            diag.AddDiagLine(@"Texture Overrides via BTP", LogSeverity.DIAGSECTION);
            diag.AddDiagLine(@"Binary Texture Packages override textures at runtime via the Texture Override ASI.");
            diag.AddDiagLine(@"This check is best effort; issues may occur at runtime if the targeted textures are different than what BTP files were built against.");

            bool foundBTP = false;
            bool allBTPsOK = true;

            var dlcFoldersNames = package.DiagnosticTarget.GetInstalledDLC();
            using var tfcCache = new FileStreamCache();
            foreach (var name in dlcFoldersNames)
            {
                var dlcfolder = Path.Combine(package.DiagnosticTarget.GetDLCPath(), name);
                var btpPath = M3CTextureOverrideMerge.GetCombinedTexturePackagePath(package.DiagnosticTarget, name);

                if (!File.Exists(btpPath))
                {
                    // No BTP in this DLC.
                    continue;
                }

                // There is a BTP here...
                foundBTP = true;

                MLog.Information($@"BTPVerify: Checking {btpPath}");
                var btpRelPath = btpPath.Substring(package.DiagnosticTarget.GetDLCPath().Length + 1);

                using var btpStream = File.OpenRead(btpPath);
                BinaryTexturePackage btp = null;

                void onUpdate(ProgressInfo lpi)
                {
                    package.UpdateStatusCallback?.Invoke($@"Checking {name} BTP" + $@" {lpi.Value:F0}%");
                }

                ProgressInfo pi = new ProgressInfo();
                pi.OnUpdate = onUpdate;

                // Initial read and locally stored mip data
                try
                {
                    btp = new BinaryTexturePackage(btpStream, loadMips: true, verify: true, pi: pi);
                }
                catch (Exception e)
                {
                    MLog.Exception(e, $@"Error reading tables in BTP: ");
                    diag.AddDiagLine($@"Error reading {btpRelPath}: {e.Message}", LogSeverity.ERROR);
                    allBTPsOK = false;
                    continue; // No more on this BTP
                }

                // Now read the external mips
                var tfcs = package.DiagnosticTarget.GetFilesLoadedInGame(includeTFCs: true);
                foreach (var texture in btp.TextureOverrides.Where(x => x.TFC.TFCName != @"None"))
                {
                    var tfcMips = texture.Mips.Where(x => (x.Flags & BTPMipFlags.External) != 0).ToList();
                    if (!tfcMips.Any())
                    {
                        continue; // This texture doesn't actually use external mips
                    }

                    var tfcName = texture.TFC.TFCName + @".tfc";
                    if (!tfcs.TryGetValue(tfcName, out var tfcPath))
                    {
                        // This is not an error, as this can be expected if you have a BTP with TFC textures that
                        // may not be installed. This may be indicative of an error, or it may not be
                        // It'll be listed as a warning in log, since you probably shouldn't be doing overrides
                        // using TFCs you don't control.
                        MLog.Warning($@"Texture override {texture.OverridePath} references TFC not in game: {tfcName}");
                        diag.AddDiagLine($@"Texture override {texture.OverridePath} references TFC not in game: {tfcName} - If override is applied, higher resolution mips will be black", LogSeverity.WARN);
                        continue; // No more on this texture, since they will all have this problem.
                    }

                    var tfcStream = tfcCache.GetStream(tfcPath);
                    foreach (var mip in tfcMips)
                    {
                        if (mip.CompressedOffset >= tfcStream.Length)
                        {
                            // Fatal OOB read!
                            MLog.Fatal($@"Texture override {texture.OverridePath} mip {mip.Width}x{mip.Height} offset in TFC {tfcName} is out of bounds, this will cause a game crash!");
                            diag.AddDiagLine($@"Texture override {texture.OverridePath} mip {mip.Width}x{mip.Height} offset in TFC {tfcName} is out of bounds, this will cause a game crash!", LogSeverity.FATAL);
                            continue;
                        }

                        if ((mip.CompressedOffset + mip.CompressedSize) > tfcStream.Length)
                        {
                            // Fatal OOB when size is added!
                            MLog.Fatal($@"Texture override {texture.OverridePath} mip {mip.Width}x{mip.Height} offset is in bounds of TFC {tfcName} but will overread when accessed, this will cause a game crash!");
                            diag.AddDiagLine($@"Texture override {texture.OverridePath} mip {mip.Width}x{mip.Height} offset is in bounds of TFC {tfcName} but will overread when accessed, this will cause a game crash!", LogSeverity.FATAL);
                            continue;
                        }

                        // It looks like we're going to be OK on the read.
                        // Decompress the texture. If it dies here,
                        // something is wrong externally.
                        try
                        {
                            tfcStream.Seek(mip.CompressedOffset, SeekOrigin.Begin);
                            byte[] temp = new byte[mip.UncompressedSize];
                            TextureCompression.DecompressTexture(temp, tfcStream, StorageTypes.extOodle, mip.UncompressedSize, mip.CompressedSize);
                        }
                        catch (Exception e)
                        {
                            MLog.Error($@"Texture override {texture.OverridePath} mip {mip.Width}x{mip.Height} could not decompress: {e.Message}");
                            diag.AddDiagLine($@"Texture override {texture.OverridePath} mip {mip.Width}x{mip.Height} decompression failed", LogSeverity.ERROR);
                        }
                        // Decompressed OK.
                        // We don't really care about converting it further, so 
                        // we're going to consider this OK.
                    }
                }

                // Also check CRC for metadata.
                var directory = Directory.GetParent(btpPath).FullName;
                var testMetadataFile = Path.Combine(directory, @"BTPMetadata.btm");

                if (!File.Exists(testMetadataFile))
                {
                    MLog.Error($@"BTP metadata file is missing: {testMetadataFile}");
                    diag.AddDiagLine($@"BTP metadata file is missing: {testMetadataFile.Substring(package.DiagnosticTarget.GetDLCPath().Length + 1)}", LogSeverity.ERROR);
                    allBTPsOK = false;
                    continue;
                }

                var hash = Crc32.HashToUInt32(File.ReadAllBytes(testMetadataFile));
                if (btp.Header.MetadataCRC != hash)
                {
                    MLog.Error($@"This BTP has a mismatched metadata file! CRC we got: {testMetadataFile}, CRC we expected: {btp.Header.MetadataCRC}");
                    diag.AddDiagLine($@"BTP metadata file is invalid: {testMetadataFile.Substring(package.DiagnosticTarget.GetDLCPath().Length + 1)}", LogSeverity.ERROR);
                    allBTPsOK = false;
                    continue;
                }

                diag.AddDiagLine($@"{btpRelPath} passed verification", LogSeverity.GOOD);
            }

            // Set message if there would be none.
            if (!foundBTP)
            {
                diag.AddDiagLine($@"This installation doesn't have any Binary Texture Overrides (BTP) installed");
            }
        }
    }
}
