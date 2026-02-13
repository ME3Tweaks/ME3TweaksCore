using AuthenticodeExaminer;
using ME3TweaksCore.Localization;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ME3TweaksCore.Helpers
{
    /// <summary>
    /// Class that gets information about the executable that is consuming this library
    /// </summary>
    public class MLibraryConsumer
    {

        /// <summary>
        /// Returns the running application version information
        /// </summary>
        /// <returns></returns>
        public static Version GetAppVersion() => System.Reflection.Assembly.GetEntryAssembly().GetName().Version;

        /// <summary>
        /// Gets the executable path that is hosting this library.
        /// </summary>
        /// <returns></returns>
        public static string GetExecutablePath() => Process.GetCurrentProcess().MainModule.FileName;

        /// <summary>
        /// Gets the folder of the current program that is running this library.
        /// </summary>
        /// <returns></returns>
        public static string GetExecutingAssemblyFolder() => Path.GetDirectoryName(GetExecutablePath());

        /// <summary>
        /// Gets the version information for the ALOT Installer Core Library.
        /// </summary>
        /// <returns></returns>
        public static Version GetLibraryVersion() => Assembly.GetExecutingAssembly().GetName().Version;

        /// <summary>
        /// Returns the hosting processes' name, without extension
        /// </summary>
        /// <returns></returns>
#if (DEBUG)
        // running process will be 'dotnet' in this mode
        public static string GetHostingProcessname() => @"ME3TweaksCore";
#else
        public static string GetHostingProcessname() => Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.ModuleName);
#endif

        /// <summary>
        /// Gets the signing date of the executable. Depends on it being signed
        /// </summary>
        /// <returns></returns>
        internal static string GetSigningDate()
        {
            var info = new FileInspector(GetExecutablePath());
            var signTime = info.GetSignatures().FirstOrDefault()?.TimestampSignatures.FirstOrDefault()?.TimestampDateTime?.UtcDateTime;

            if (signTime != null)
            {
                return signTime.Value.ToLocalTime().ToString(@"MMMM dd, yyyy @ hh:mm");
            }

            return LC.GetString(LC.string_buildNotSigned);
        }
    }
}
