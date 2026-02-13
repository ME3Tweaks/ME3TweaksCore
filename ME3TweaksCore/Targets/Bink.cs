using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using System;
using System.Diagnostics;
using System.IO;

namespace ME3TweaksCore.Targets
{
    /// <summary>
    /// Utility stuff for bink
    /// </summary>
    public static class Bink
    {
        // List of bink ASI loader hashes this version of ME3TweaksCore can use.
        internal const string ME1ASILoaderHash = @"30660f25ab7f7435b9f3e1a08422411a";
        internal const string ME2ASILoaderHash = @"a5318e756893f6232284202c1196da13";
        internal const string ME3ASILoaderHash = @"1acccbdae34e29ca7a50951999ed80d5";
        internal const string LEASILoaderHash = @"2511b99b964b1f9302f5cb3cb4bff273"; // bink 2.0.0.14 by ME3Tweaks 02/01/2026


        /// <summary>
        /// Determines if the enhanced bink video library file is installed
        /// </summary>
        /// <returns>True if the specific version is found; false otherwise</returns>
        public static bool IsEnhancedBinkInstalled(this GameTarget target)
        {
            if (target.Game.IsOTGame()) return false; // Enhanced bink is only for LE.
            try
            {
                string binkPath = target.GetOriginalProxiedBinkPath();
                if (!File.Exists(binkPath))
                    return false;

                var finfo = FileVersionInfo.GetVersionInfo(binkPath);
                return Version.TryParse(finfo.FileVersion, out var binkVer) && binkVer >= new Version(@"2022.05");
            }
            catch (Exception e)
            {
                // File is in use by another process perhaps
                MLog.Exception(e, @"Unable to determine if enhanced bink is installed:");
            }

            return false;
        }

        /// <summary>
        /// Determines if the bink ASI loader/bypass is installed (both OT and LE)
        /// </summary>
        /// <returns></returns>
        public static bool IsBinkBypassInstalled(this GameTarget target)
        {
            try
            {
                string binkPath = target.GetVanillaBinkPath();
                string expectedHash = null;
                if (target.Game == MEGame.ME1) expectedHash = Bink.ME1ASILoaderHash;
                else if (target.Game == MEGame.ME2) expectedHash = Bink.ME2ASILoaderHash;
                else if (target.Game == MEGame.ME3) expectedHash = Bink.ME3ASILoaderHash;
                else if (target.Game.IsLEGame()) expectedHash = Bink.LEASILoaderHash;

                if (File.Exists(binkPath))
                {
                    return MUtilities.CalculateHash(binkPath) == expectedHash;
                }
            }
            catch (Exception e)
            {
                // File is in use by another process perhaps
                MLog.Exception(e, @"Unable to hash bink dll:");
            }

            return false;
        }

        /// <summary>
        /// Installs the Bink ASI loader to this target.
        /// </summary>
        /// <returns></returns>
        public static bool InstallBinkBypass(this GameTarget target, bool throwError)
        {
            var destPath = target.GetVanillaBinkPath();
            var parent = Directory.GetParent(destPath).FullName;
            if (!Directory.Exists(parent))
            {
                // Nothing to install to!
                MLog.Warning($@"Bink install folder doesn't exist, skipping: {parent}");
                return false;
            }

            MLog.Information($@"Installing Bink bypass for {target.Game} to {destPath}");
            try
            {
                var obinkPath = target.GetOriginalProxiedBinkPath();

                if (target.Game.IsOTGame())
                {
                    MUtilities.ExtractInternalFile($@"ME3TweaksCore.GameFilesystem.Bink._32.{target.Game.ToString().ToLower()}.binkw32.dll", destPath, true);
                    MUtilities.ExtractInternalFile($@"ME3TweaksCore.GameFilesystem.Bink._32.{target.Game.ToString().ToLower()}.binkw23.dll", obinkPath, true);
                }
                else if (target.Game.IsLEGame() || target.Game == MEGame.LELauncher)
                {
                    MUtilities.ExtractInternalFile(@"ME3TweaksCore.GameFilesystem.Bink._64.bink2w64.dll", destPath, true); // Bypass proxy / ASI loader
                    MUtilities.ExtractInternalFile(@"ME3TweaksCore.GameFilesystem.Bink._64.bink2w64_enhanced.dll", obinkPath, true); // The original dll (enhanced version)
                }
                else
                {
                    MLog.Error($@"Unknown game for gametarget (InstallBinkBypass): {target.Game}");
                    return false;
                }

                MLog.Information($@"Installed Bink bypass for {target.Game}");
                return true;
            }
            catch (Exception e)
            {
                MLog.Exception(e, @"Error installing bink bypass:");
                if (throwError)
                    throw;
            }

            return false;
        }

        /// <summary>
        /// Gets the path to the proxied version of the bink dll
        /// </summary>
        /// <returns></returns>
        internal static string GetOriginalProxiedBinkPath(this GameTarget target)
        {
            if (target.Game == MEGame.ME1 || target.Game == MEGame.ME2) return Path.Combine(target.TargetPath, @"Binaries", @"binkw23.dll");
            if (target.Game == MEGame.ME3) return Path.Combine(target.TargetPath, @"Binaries", @"win32", @"binkw23.dll");
            if (target.Game.IsLEGame()) return Path.Combine(target.TargetPath, @"Binaries", @"Win64", @"bink2w64_original.dll");
            if (target.Game == MEGame.LELauncher) return Path.Combine(target.TargetPath, @"bink2w64_original.dll");
            return null;
        }

        /// <summary>
        /// Uninstalls the Bink ASI loader from this target (does not do anything to Legendary Edition Launcher)
        /// </summary>
        public static void UninstallBinkBypass(this GameTarget target)
        {
            var binkPath = target.GetVanillaBinkPath();
            var obinkPath = target.GetOriginalProxiedBinkPath();
            if (target.Game == MEGame.ME1)
            {
                if (File.Exists(obinkPath))
                    File.Delete(obinkPath);
                MUtilities.ExtractInternalFile(@"ME3TweaksCore.GameFilesystem.Bink._32.me1.binkw23.dll", binkPath, true);
            }
            else if (target.Game == MEGame.ME2)
            {
                if (File.Exists(obinkPath))
                    File.Delete(obinkPath);
                MUtilities.ExtractInternalFile(@"ME3TweaksCore.GameFilesystem.Bink._32.me2.binkw23.dll", binkPath, true);
            }
            else if (target.Game == MEGame.ME3)
            {
                if (File.Exists(obinkPath))
                    File.Delete(obinkPath);
                MUtilities.ExtractInternalFile(@"ME3TweaksCore.GameFilesystem.Bink._32.me3.binkw23.dll", binkPath, true);
            }
            else if (target.Game.IsLEGame())
            {
                if (File.Exists(obinkPath))
                    File.Delete(obinkPath);
                MUtilities.ExtractInternalFile(@"ME3TweaksCore.GameFilesystem.Bink._64.bink2w64_original.dll", binkPath, true);
            }
        }

        /// <summary>
        /// Gets the path where the original, vanilla bink dll should be (the one that is proxied)
        /// </summary>
        /// <returns></returns>
        public static string GetVanillaBinkPath(this GameTarget target)
        {
            if (target.Game == MEGame.ME1 || target.Game == MEGame.ME2) return Path.Combine(target.TargetPath, @"Binaries", @"binkw32.dll");
            if (target.Game == MEGame.ME3) return Path.Combine(target.TargetPath, @"Binaries", @"win32", @"binkw32.dll");
            if (target.Game.IsLEGame()) return Path.Combine(target.TargetPath, @"Binaries", @"Win64", @"bink2w64.dll");
            if (target.Game == MEGame.LELauncher) return Path.Combine(target.TargetPath, @"bink2w64.dll");
            return null;
        }
    }
}
