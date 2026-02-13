using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.NativeMods.Interfaces;
using Serilog;

namespace ME3TweaksCore.NativeMods
{

    /// <summary>
    /// Object describing an installed ASI mod. Subclasses determine if this is known or unknown due to fun data binding issues in WPF
    /// </summary>
    public abstract class InstalledASIMod : IInstalledASIMod
    {
        public MEGame Game { get; init; }
        public string Hash { get; init; }
        public string InstalledPath { get; init; }
        protected InstalledASIMod(string asiFile, string hash, MEGame game)
        {
            Game = game;
            InstalledPath = asiFile;
            Hash = hash;
        }

        ///// <summary>
        ///// Deletes the backing file for this ASI
        ///// </summary>
        //public bool Uninstall()
        //{
        //    MLog.Information($@"Deleting installed ASI: {InstalledPath}");
        //    try
        //    {
        //        File.Delete(InstalledPath);
        //        return true;
        //    }
        //    catch (Exception e)
        //    {
        //        MLog.Error($@"Error uninstalling ASI {InstalledPath}: {e.Message}");
        //        return false;
        //    }
        //}
    }
}
