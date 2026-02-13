using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ME3TweaksCore.Helpers
{
    /// <summary>
    /// Contains filesystem methods to get and create directories and files used by ME3TweaksCore. Data is stored in the AppDataFolderName directory.
    /// </summary>
    public static class MCoreFilesystem
    {
        /// <summary>
        /// The appdata folder for ME3TweaksCore. Change this if you want to segregate your consuming application's data from other instances. CHANGE THIS BEFORE BOOTING THE LIBRARY.
        /// </summary>
        public static string AppDataFolderName { get; set; } = @"ME3TweaksCore";

        /// <summary>
        /// The delegate declaration for GetAppDataFolder.
        /// </summary>
        /// <param name="createIfMissing"></param>
        /// <returns></returns>
        public delegate string GetAppDataFolderDelegate(bool createIfMissing = true);


        /// <summary>
        /// Gets the appdata directory for app data storage. The default implementation's folder name can be changed via AppDataFolderName. This method can be overridden by assigning it a delegate.
        /// </summary>
        /// <param name="createIfMissing"></param>
        /// <returns></returns>
        public static string GetSharedME3TweaksDataFolder(bool createIfMissing = true)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ME3Tweaks");
            if (createIfMissing && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            return folder;
        }

        /// <summary>
        /// Gets the appdata directory for app data storage. The default implementation's folder name can be changed via AppDataFolderName. This method can be overridden by assigning it a delegate.
        /// </summary>
        /// <param name="createIfMissing"></param>
        /// <returns></returns>
        public static GetAppDataFolderDelegate GetAppDataFolder = createIfMissing =>
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDataFolderName);
            if (createIfMissing && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            return folder;
        };

        /// <summary>
        /// Gets the directory that holds cached native dlls.
        /// </summary>
        /// <returns></returns>
        internal static string GetDllDirectory()
        {
            return Directory.CreateDirectory(Path.Combine(GetAppDataFolder(), @"dlls")).FullName;
        }

        /// <summary>
        /// Returns the path to where the cached online service responses reside on disk.
        /// </summary>
        /// <returns></returns>
        public static string GetME3TweaksServicesCache()
        {
            return Directory.CreateDirectory(Path.Combine(GetAppDataFolder(), @"ME3TweaksServicesCache")).FullName;
        }

        /// <summary>
        /// Returns the path to where the cached online service responses reside on disk.
        /// </summary>
        /// <returns></returns>
        public static string GetSharedME3TweaksServicesCache()
        {
            return Directory.CreateDirectory(Path.Combine(GetSharedME3TweaksDataFolder(), @"ServicesCache")).FullName;
        }

        /// <summary>
        /// Returns the path to where the cached local BasegameIdentificationService file resides on disk.
        /// </summary>
        /// <returns></returns>
        public static string GetSharedBasegameIdentificationServiceFile()
        {
            return Path.Combine(GetSharedME3TweaksServicesCache(), @"basegamefileidentificationservice.json");
        }

        /// <summary>
        /// Returns the path to where the cached local BasegameIdentificationService file resides on disk. This should only be used for legacy reasons!
        /// </summary>
        /// <returns></returns>
        internal static string GetAppSpecificBasegameFileIdentificationServiceFile()
        {
            return Path.Combine(GetME3TweaksServicesCache(), @"localbasegamefileidentificationservice.json");
        }

        /// <summary>
        /// Returns the path to where the local FileSourceService file resides on disk.
        /// </summary>
        /// <returns></returns>
        public static string GetFileSourceServiceFile()
        {
            return Path.Combine(GetME3TweaksServicesCache(), @"filesourceservice.json");
        }

        public static string GetThirdPartyIdentificationCachedFile()
        {
            return Path.Combine(GetME3TweaksServicesCache(), @"thirdpartyidentificationservice.json");
        }

        /// <summary>
        /// Returns the path to where the local SymbolService file resides on disk.
        /// </summary>
        /// <returns></returns>
        public static string GetSymbolServiceFile()
        {
            return Path.Combine(GetME3TweaksServicesCache(), @"symbolservice.json");
        }

        /// <summary>
        /// Directory that contains MEM executables
        /// </summary>
        /// <returns></returns>
        public static string GetMEMDir()
        {
            return Directory.CreateDirectory(Path.Combine(GetAppDataFolder(), @"MassEffectModder")).FullName;
        }

        /// <summary>
        /// Gets path to where the MEM executable should reside
        /// </summary>
        /// <param name="classicMem"></param>
        /// <returns></returns>
        public static string GetMEMNoGuiPath(bool classicMem)
        {
            return Path.Combine(GetMEMDir(), GetMEMEXEName(classicMem));
        }

        /// <summary>
        /// Temp directory. This is cleaned on every boot of the library
        /// </summary>
        /// <returns></returns>
        public static string GetTempDirectory()
        {
            return Directory.CreateDirectory(Path.Combine(GetAppDataFolder(), @"Temp")).FullName;
        }

        /// <summary>
        /// Gets directory where executables that must be run are stored (PermissionsGranter.exe for example)
        /// </summary>
        /// <returns></returns>
        public static string GetCachedExecutablesDirectory()
        {
            return Directory.CreateDirectory(Path.Combine(GetAppDataFolder(), @"CachedExecutables")).FullName;
        }

        /// <summary>
        /// Gets filepath where a cached executable would reside. This does not mean the file exists.
        /// </summary>
        /// <param name="executableName">Full executable name including the extension.</param>
        /// <returns></returns>
        public static string GetCachedExecutable(string executableName)
        {
            return Path.Combine(GetCachedExecutablesDirectory(), executableName);
        }

        /// <summary>
        /// Gets the log directory for the application.
        /// </summary>
        /// <returns></returns>
        public static string GetLogDir()
        {
            return Directory.CreateDirectory(Path.Combine(GetAppDataFolder(), @"logs")).FullName;
        }
        
        /// <summary>
        /// Gets the MassEffectModderNoGui executable name for the specified game.
        /// </summary>
        /// <param name="classicMem"></param>
        /// <returns></returns>
        internal static string GetMEMEXEName(bool classicMem)
        {
            if (classicMem)
                return @"MassEffectModderNoGui.exe";
            return @"MassEffectModderNoGui_LE.exe";
        }
    }
}
