#if ALOT
using System.IO;
using ME3TweaksCore.Helpers;

namespace ME3TweaksCore.Diagnostics
{
    /// <summary>
    /// Helper class for enabling debug features - USED BY ALOT INSTALLER DO NOT REMOVE
    /// </summary>
    public static class QuickFixHelper
    {
        public enum QuickFixName
        {
            /// <summary>
            /// Skip installing markers in install step
            /// </summary>
            skipmarkers,
            /// <summary>
            /// Do not cleanup staging (cleans up initially)
            /// </summary>
            nocleanstaging,
            /// <summary>
            /// Forces log to also save locally after upload
            /// </summary>
            ForceSavingLogLocally
        }


        /// <summary>
        /// Checks if a local file, starting with an underscore, exists next to the executable. If it does, the fix is enabled. If it isn't, the fix is not enabled.
        /// </summary>
        /// <param name="fixname">Filename, WITHOUT THE UNDERSCORE</param>
        /// <returns></returns>
        public static bool IsQuickFixEnabled(QuickFixName fixname) => File.Exists(Path.Combine(MLibraryConsumer.GetExecutingAssemblyFolder(), $@"_{fixname}"));
    }
}
#endif