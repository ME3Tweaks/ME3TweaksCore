using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Objects;
using ME3TweaksCore.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Diagnostic module for collecting TOC file information.
    /// </summary>
    internal class DiagTOC : DiagModuleBase
    {
        internal override void RunModule(LogUploadPackage package)
        {
           var diag = package.DiagnosticWriter;

            //TOC SIZE CHECK
            if (package.DiagnosticTarget.Game == MEGame.ME3 || package.DiagnosticTarget.Game.IsLEGame())
            {
                MLog.Information(@"Collecting TOC information");

                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingTOCFileInformation));

                diag.AddDiagLine(@"File Table of Contents (TOC) check", LogSeverity.DIAGSECTION);
                diag.AddDiagLine(@"PCConsoleTOC.bin files list all files the game can normally access and stores the values in hash tables for faster lookup.");
                bool hadTocError = false;
                string markerfile = package.DiagnosticTarget.GetTextureMarkerPath();
                var bgTOC = Path.Combine(package.DiagnosticTarget.GetBioGamePath(), @"PCConsoleTOC.bin"); // Basegame
                var isTOCVanilla = VanillaDatabaseService.IsFileVanilla(package.DiagnosticTarget, bgTOC, true);
                if (isTOCVanilla)
                {
                    diag.AddDiagLine($@"Unmodified vanilla TOC: {Path.GetRelativePath(package.DiagnosticTarget.TargetPath, bgTOC)}", LogSeverity.GOOD);
                    diag.AddDiagLine(@"The vanilla shipping game includes references to files that don't exist and incorrect size values for some files; these are normal.");
                    diag.AddDiagLine(@"If you restored from a backup without all localizations, there may be additional missing LOC entries listed here, that is normal.");
                }

                ProgressInfo pi = new ProgressInfo()
                {
                    OnUpdate = (ProgressInfo progressInfo) =>
                    {
                        package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingTOCFileInformation) + $@" {(int)progressInfo.Value}%");
                    },
                    Value = 0
                };

                hadTocError |= CheckTOCFile(package, bgTOC, markerfile, diag.AddDiagLine, isTOCVanilla, pi);

                var dlcs = package.DiagnosticTarget.GetInstalledDLC();
                var dlcTOCs = new List<string>();
                foreach (var v in dlcs)
                {
                    var tocPath = Path.Combine(package.DiagnosticTarget.GetDLCPath(), v, @"PCConsoleTOC.bin");
                    if (File.Exists(tocPath))
                    {
                        dlcTOCs.Add(tocPath);
                    }
                }

                foreach (string toc in dlcTOCs)
                {
                    isTOCVanilla = VanillaDatabaseService.IsFileVanilla(package.DiagnosticTarget, toc, true);
                    if (isTOCVanilla)
                    {
                        diag.AddDiagLine($@"Unmodified vanilla TOC: {Path.GetRelativePath(package.DiagnosticTarget.TargetPath, toc)}", LogSeverity.GOOD);
                    }
                    hadTocError |= CheckTOCFile(package, toc, markerfile, diag.AddDiagLine, isTOCVanilla, pi);
                }

                if (package.DiagnosticTarget.Game.IsOTGame())
                {
                    if (!hadTocError)
                    {
                        diag.AddDiagLine(@"All TOC files passed check. No files have a size larger than the TOC size.");
                    }
                    else
                    {
                        diag.AddDiagLine(@"Some files are larger than the listed TOC size. This typically won't happen unless you manually installed some files or an ALOT installation failed. The game may have issues loading these files.", LogSeverity.ERROR);
                    }
                }
            }
        }


        /// <summary>
        /// Checks the TOC file at the listed path and prints information to the diagnostic for it.
        /// </summary>
        /// <param name="package"></param>
        /// <param name="tocFilePath">TOC file to check</param>
        /// <param name="textureMarkerFilePath"></param>
        /// <param name="addDiagLine">Function to print to diagnostic</param>
        /// <param name="isTocVanilla"></param>
        private static bool CheckTOCFile(LogUploadPackage package, string tocFilePath, string textureMarkerFilePath, Action<string, LogSeverity> addDiagLine, bool isTocVanilla, ProgressInfo pi = null)
        {
            bool hadTocError = false;
            var tocrootPath = package.DiagnosticTarget.TargetPath;
            if (Path.GetFileName(Directory.GetParent(tocFilePath).FullName).StartsWith(@"DLC_"))
            {
                tocrootPath = Directory.GetParent(tocFilePath).FullName;
            }

            MLog.Information($@"Checking TOC file {tocFilePath}");

            TOCBinFile tbf = new TOCBinFile(tocFilePath);
            if (!isTocVanilla)
            {
                // If TOC is vanilla this information is not helpful and just pollutes the output.
                addDiagLine($@" - {tocFilePath.Substring(package.DiagnosticTarget.TargetPath.Length + 1)}: {tbf.GetAllEntries().Count} file entries, {tbf.HashBuckets.Count} hash buckets", LogSeverity.INFO);
            }

            int notPresentOnDiskCount = 0;
            bool isSubbed = false;
            pi?.Value = 0;
            var entries = tbf.GetAllEntries();
            var done = 0;
            foreach (TOCBinFile.Entry ent in entries)
            {
                done++;
                pi?.Value = done * 100.0f / entries.Count;
                pi?.OnUpdate(pi);

                if (ent.name == @"PCConsoleTOC.txt")
                    continue; // This file is not shipped in most games, nor is it used, so we don't care.

                //Console.WriteLine(index + "\t0x" + ent.offset.ToString("X6") + "\t" + ent.size + "\t" + ent.name);

                string filepath = Path.Combine(tocrootPath, ent.name);
                var fileExists = File.Exists(filepath);
                if (fileExists)
                {
                    if (!filepath.Equals(textureMarkerFilePath, StringComparison.InvariantCultureIgnoreCase) && !filepath.ToLower().EndsWith(@"pcconsoletoc.bin"))
                    {
                        FileInfo fi = new FileInfo(filepath);
                        long size = fi.Length;
                        if (ent.size < size && (ent.size == 0 || package.DiagnosticTarget.Game.IsOTGame()))
                        {
                            // Size only matters on OT or if zero on LE
                            addDiagLine($@"   >  {filepath} size is {size}, but TOC lists {ent.size} ({ent.size - size} bytes)", LogSeverity.ERROR);
                            hadTocError = true;
                        }
                    }
                }
                else
                {
                    if (!isSubbed && notPresentOnDiskCount > 10)
                    {
                        isSubbed = true;
                        addDiagLine(@"Click to view more", LogSeverity.SUB);
                    }
                    else
                    {
                        notPresentOnDiskCount++;
                    }

                    addDiagLine($@"   > {filepath} is listed in TOC but is not present on disk", LogSeverity.WARN);
                }
            }

            if (notPresentOnDiskCount > 10)
            {
                addDiagLine(LogShared.END_SUB, LogSeverity.INFO);
            }

            return hadTocError;
        }

    }
}
