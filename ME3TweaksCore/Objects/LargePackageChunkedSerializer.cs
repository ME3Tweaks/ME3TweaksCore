using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Packages.CloningImportingAndRelinking;
using ME3TweaksCore.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ME3TweaksCore.Objects
{
    /// <summary>
    /// Class for handling serializing large amounts of data to. Only tracks single export transfers at a time. Splits files once they reach a specirfic size..
    /// </summary>
    public class LargePackageChunkedSerializer
    {
        /// <summary>
        /// Amount of data we've recorded to current package exports
        /// </summary>
        private long currentDataSize = 0;

        /// <summary>
        /// Current package that is being serialized to
        /// </summary>
        private IMEPackage currentPackage;

        /// <summary>
        /// The current index of the package we are serializing
        /// </summary>
        int currentPackageIndex = 0;

        /// <summary>
        /// Where serialized data gets written out to when packages are saved
        /// </summary>
        public string baseSavePath { private get; init; }

        /// <summary>
        /// Game packages are for
        /// </summary>
        public MEGame game { private get; init; }

        /// <summary>
        /// Name of packages to roll through
        /// </summary>
        public string basePackageName { private get; init; }

        /// <summary>
        /// List of packages that were saved by this serializer
        /// </summary>

        private List<string> packagePaths = new();

        /// <summary>
        /// Saved package paths list
        /// </summary>
        public IReadOnlyList<string> PackagePaths => packagePaths;

        /// <summary>
        /// Delegate to invoke before save occurs
        /// </summary>
        public Action<IMEPackage> OnSave { get; init; }

        public IEntry ExportInto(ExportEntry source, PackageCache cache)
        {
            if (currentPackage == null || (source.DataSize + currentDataSize) > MaxSize)
            {
                Rollover();
            }

            // Export the texture to the new package
            currentDataSize += source.DataSize;
            EntryExporter.ExportExportToPackage(source, currentPackage, out var portedEntry, cache, new RelinkerOptionsPackage() { CheckImportsWhenExportingToPackage = false });
            return portedEntry;
        }

        /// <summary>
        /// Saves current package, resets, and starts next package
        /// </summary>
        private void Rollover()
        {
            if (currentPackage != null)
            {
                MLog.Information($@"Large data serializer - saving package...");
                OnSave?.Invoke(currentPackage);
                currentPackage.Save();
                packagePaths.Add(currentPackage.FilePath);
            }

            currentPackageIndex++;
            currentDataSize = 0;

            var name = currentPackageIndex == 1 ? basePackageName : basePackageName + currentPackageIndex;
            var path = Path.Combine(baseSavePath, $@"{name}.pcc");
            MLog.Information($@"Large data serializer - rolling new package {path}, max content size: {FileSize.FormatSize(MaxSize)}");
            currentPackage = MEPackageHandler.CreateAndOpenPackage(path, game);
        }

        /// <summary>
        /// Saves package and resets serializer
        /// </summary>
        public void Finish()
        {
            MLog.Information($@"Large data serializer - finalizing");
            if (currentPackage != null)
            {
                OnSave?.Invoke(currentPackage);
                currentPackage.Save();
                packagePaths.Add(currentPackage.FilePath);
            }
            currentPackageIndex = 0;
            currentDataSize = 0;
        }

        // 256MiB
        private int _maxSize = 256 * 1024 * 1024;

        /// <summary>
        /// Max size of package "data" when uncompressed - due to how serialization 
        /// works it may be larger, but we buffer small enough it should be fine.
        /// </summary>
        public int MaxSize
        {
            get
            {
                return _maxSize;
            }
            set
            { 
                if (value != _maxSize)
                {
                    MLog.Information($@"Large data serializer - changing max package size from {FileSize.FormatSize(_maxSize)} to {FileSize.FormatSize(value)}");
                    _maxSize = value;
                }
            }
        } 
    }
}
