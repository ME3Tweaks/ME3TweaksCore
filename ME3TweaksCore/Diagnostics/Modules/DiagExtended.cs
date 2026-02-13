using System;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Localization;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Diagnostic module for extended diagnostics - these typically take longer so they must be chosen by the user to run
    /// </summary>
    internal class DiagExtended : DiagModuleBase
    {
        internal override void RunModule(LogUploadPackage package)
        {
            if (!package.AdvancedDiagnosticsEnabled)
            {
                return; // Nothing to do here
            }

            // Verify all packages can be decompressed
            TestPackageDecompression(package);
        }

        private static void LogAdvancedTool(string tool)
        {
            MLog.Information($@"DiagExtended: Running {tool}");
        }


        /// <summary>
        /// Opens all used package files in the game and verifies they can be opened by LEC. This will catch things such as compression errors.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="progressCallback"></param>
        /// <returns></returns>
        private static void TestPackageDecompression(LogUploadPackage package)
        {
            var diag = package.DiagnosticWriter;
            LogAdvancedTool(@"TestPackageDecompression");

            diag.AddDiagLine(@"Package decompression test", LogSeverity.DIAGSECTION);
            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_testingPackageDecompression));

            var packageList = package.DiagnosticTarget.EnumerateGameFiles(x => x.RepresentsPackageFilePath());

            bool foundError = false;
            int done = 0;
#if DEBUG
            var sw = new Stopwatch();
            sw.Start();
#endif
            Parallel.ForEach(packageList, new ParallelOptions { MaxDegreeOfParallelism = 1 }, packPath =>
            {
                try
                {
                    var filename = Path.GetFileName(packPath);
                    if (!filename.Contains(@"RefShaderCache"))
                    {
                        // Use lazy load and enumerate the exports. This way we don't allocate as much memory at once.
                        using var fs = File.OpenRead(packPath);
                        var testPackage = MEPackageHandler.UnsafeLazyLoadFromStream(fs, packPath);
                        foreach (var ex in testPackage.Exports)
                        {
                            // Load and unload exports
                            testPackage.LoadExport(ex);
                            testPackage.UnloadExport(ex);

                        }
                    }
                }
                catch (Exception e)
                {
                    foundError = true;
                    MLog.Exception(e, @"Error opening/decompressing package file: ");
                    package.DiagnosticWriter.AddDiagLine(LC.GetString(LC.string_interp_failedToLoadPackageXY, packPath, e.FlattenException())); // Fat stack is probably more useful as it can trace where code failed.
                }

                Interlocked.Increment(ref done);
                var progress = (int)(done * 100.0 / packageList.Count);
                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_testingPackageDecompression) + $@" {progress}%");
                package.UpdateProgressCallback?.Invoke(progress);
            });

#if DEBUG
            sw.Stop();
            MLog.Information($@"Decompression test took {sw.ElapsedMilliseconds}ms");
#endif

            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_testingPackageDecompression) + $@" 100%");

            if (!foundError)
            {
                diag.AddDiagLine(@"All package files opened and decompressed successfully with Legendary Explorer Core.", LogSeverity.GOOD);
            }
        }
    }
}
