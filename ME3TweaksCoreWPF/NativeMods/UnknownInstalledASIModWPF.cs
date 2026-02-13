using System.Diagnostics;
using System;
using System.IO;
using System.Windows.Media;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.NativeMods;
using ME3TweaksCore.NativeMods.Interfaces;

namespace ME3TweaksCoreWPF.NativeMods
{
    /// <summary>
    /// WPF-specific version of UnknownInstalledASIMod.
    /// </summary>
    public class UnknownInstalledASIModWPF : InstalledASIModWPF, IUnknownInstalledASIMod
    {
        /// <summary>
        /// These are M3 specific, they should probably be moved to an M3 subclass...
        /// </summary>
        private static Brush brush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0x10, 0x10));
        public override Brush BackgroundColor => brush;
        public FileVersionInfo DllVersionInfo { get; init; }

        public UnknownInstalledASIModWPF(string filepath, string hash, MEGame game) : base(filepath, hash, game)
        {
            DllVersionInfo = FileVersionInfo.GetVersionInfo(filepath);
            UnmappedFilename = Path.GetFileNameWithoutExtension(filepath);
            DllDescription = UnknownInstalledASIMod.ReadDllDescription(DllVersionInfo);
        }

        /// <summary>
        /// Name of the ASI file
        /// </summary>
        public string UnmappedFilename { get; set; }

        public string DllDescription { get; set; }

        /// <summary>
        /// Static constructor for use with delegates
        /// </summary>
        /// <param name="filepath"></param>
        /// <param name="hash"></param>
        /// <param name="game"></param>
        /// <returns></returns>
        public static UnknownInstalledASIModWPF GenerateUnknownInstalledASIModWPF(string filepath, string hash, MEGame game)
        {
            return new UnknownInstalledASIModWPF(filepath, hash, game);
        }
    }
}
