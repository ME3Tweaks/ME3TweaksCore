using System;
using System.Diagnostics;
using System.IO;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;

namespace ME3TweaksCore.NativeMods.Interfaces
{
    /// <summary>
    /// Interface for an InstalledASIMod. This exists as the subclasses for Installed/Unknown have to fork for the WPF version.
    /// </summary>
    public interface IInstalledASIMod
    {
        /// <summary>
        /// Game this InstalledASIMod belongs to
        /// </summary>
        public MEGame Game { get; init; }

        /// <summary>
        /// The hash of this ASI
        /// </summary>
        public string Hash { get; init; }

        /// <summary>
        /// The full filepath of the ASI file
        /// </summary>
        public string InstalledPath { get; init; }

        /// <summary>
        /// Deletes the ASI file
        /// </summary>
        /// <returns></returns>
        public bool Uninstall()
        {
            MLog.Information($@"Deleting installed ASI: {InstalledPath}");
            try
            {
                File.Delete(InstalledPath);
                return true;
            }
            catch (Exception e)
            {
                MLog.Error($@"Error uninstalling ASI {InstalledPath}: {e.Message}");
                return false;
            }
        }
    }
}
