using System;
using System.Reflection;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.ME3Tweaks.Online;
using ME3TweaksCore.NativeMods;
using ME3TweaksCore.Services.Backup;
using Serilog;

namespace ME3TweaksCore
{
    /// <summary>
    /// Boot class for the ME3TweaksCore library. You must call Initialize() before using this library to ensure dependencies are loaded.
    /// </summary>
    public static class ME3TweaksCoreLib
    {
        /// <summary>
        /// If the library has already been initialized.
        /// </summary>
        public static bool Initialized { get; private set; }

        public static Action<Action> RunOnUIThread;

        public static Version MIN_SUPPORTED_OS => new Version();

        /// <summary>
        /// The CoreLibVersion version
        /// </summary>
        public static Version CoreLibVersion => Assembly.GetExecutingAssembly().GetName().Version;

        /// <summary>
        /// The CoreLibrary version, in Human Readable form.
        /// </summary>
        public static string CoreLibVersionHR => $@"ME3TweaksCore {CoreLibVersion}"; // Needs checked this outputs proper string.

        /// <summary>
        /// Initial initialization function for the library. You must call this function before using the library, otherwise it may not reliably work.
        /// </summary>
        /// <param name="createLogger"></param>
        public static void Initialize(ME3TweaksCoreLibInitPackage package)
        {
            if (Initialized)
            {
                return; // Already initialized.
            }

            if (package == null)
            {
                throw new Exception(@"The ME3TweaksCoreLibInitPackage object was null! This object is required to initialize the library.");
            }

            if (Log.Logger == Serilog.Core.Logger.None && package.GetLogger != null)
            {
                // Attach to hosting logger
                Log.Logger = package.GetLogger.Invoke();
                MLog.Information(@"------------------------------------");
            }

            MLog.Information($@"Initializing ME3TweaksCore library {MLibraryConsumer.GetLibraryVersion()}");
            package.InstallInitPackage();


            // Load Legendary Explorer Core as we depend on it
            MEPackageHandler.GlobalSharedCacheEnabled = false; // ME3Tweaks tools (non LEX) do not use the global package cache
            LegendaryExplorerCoreLib.InitLib(null, logger: package.GetLogger?.Invoke(), 
                packageSavingFailed: package.LECPackageSaveFailedCallback, 
                // objectDBsToLoad: package.PropertyDatabasesToLoad, // Use lazy loader now
                usePropertyDBLazyLoad: true);

            try
            {
                MUtilities.DeleteFilesAndFoldersRecursively(MCoreFilesystem.GetTempDirectory(), deleteDirectoryItself: false); // Clear temp but don't delete the directory itself
            }
            catch (Exception e)
            {
                MLog.Error($@"Error deleting temp files: {e.Message}");
            }

            BackupService.InitBackupService(RunOnUIThread, logPaths: true);

            if (package.LoadAuxiliaryServices)
            {
                if (package.AuxiliaryCombinedOnlineServicesEndpoint != null)
                {
                    MCoreServiceLoader.LoadServices(package.AuxiliaryCombinedOnlineServicesEndpoint);
                }
                else
                {
                    MLog.Warning(@"ME3TweaksCoreLib.Initialize() was called with LoadAuxiliaryServices but did not specify a AuxiliaryCombinedOnlineServicesEndpoint! Some services were not loaded.");
                }
            }

            MLog.Information(@"ME3TweaksCore has initialized");
            Initialized = true;
        }
    }
}
