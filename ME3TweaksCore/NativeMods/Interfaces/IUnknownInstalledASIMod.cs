using System.Diagnostics;

namespace ME3TweaksCore.NativeMods.Interfaces
{
    public interface IUnknownInstalledASIMod : IInstalledASIMod
    {
        /// <summary>
        /// The name of the ASI file
        /// </summary>
        public string UnmappedFilename { get; set; }

        /// <summary>
        /// Returns a multi line string blob representation of the dll's version info
        /// </summary>
        public string DllDescription { get; set; }

        /// <summary>
        /// Version information on the Dll
        /// </summary>
        public FileVersionInfo DllVersionInfo { get; init; }
    }
}
