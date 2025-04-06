using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Localization;

namespace ME3TweaksCore.Helpers
{

    public static class ModFileFormats
    {

        public static MEGame GetGameMEMFileIsFor(string file)
        {
            if (!File.Exists(file))
                return MEGame.Unknown; // We don't know what file this game is for because it doesn't exist!

            try
            {
                MEGame game = MEGame.Unknown;
                using var memFile = File.OpenRead(file);
                return GetGameMEMFileIsFor(memFile);
            }
            catch (Exception e)
            {
                MLog.Exception(e, $@"Unable to determine game MEM file {file} is for");
                return MEGame.Unknown;
            }
        }

        /// <summary>
        /// Reads the mem file stream and determines the game it is for
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static MEGame GetGameMEMFileIsFor(Stream stream)
        {
            var magic = stream.ReadStringASCII(4);
            if (magic != @"TMOD")
            {
                return MEGame.Unknown;
            }
            var version = stream.ReadInt32(); //3 = LE
            var gameIdOffset = stream.ReadInt64();
            stream.Position = gameIdOffset;
            var gameId = stream.ReadInt32();

            if (gameId == 1) return version < 3 ? MEGame.ME1 : MEGame.LE1;
            if (gameId == 2) return version < 3 ? MEGame.ME2 : MEGame.LE2;
            if (gameId == 3) return version < 3 ? MEGame.ME3 : MEGame.LE3;
            return MEGame.Unknown;
        }

        public static List<string> GetFileListForMEMFile(string file)
        {
            try
            {
                using var memFile = File.OpenRead(file);
                return GetFileListForMEMFile(memFile);
            }
            catch (Exception e)
            {
                MLog.Exception(e, $@"Unable to determine game MEM file {file} is for");
            }
            return new List<string>();

        }

        private static List<string> GetFileListForMEMFile(Stream memFile)
        {
            var files = new List<string>();
            var magic = memFile.ReadStringASCII(4);
            if (magic != @"TMOD")
            {
                return files;
            }
            var version = memFile.ReadInt32(); //3 = LE
            var gameIdOffset = memFile.ReadInt64();
            memFile.Position = gameIdOffset;
            var gameId = memFile.ReadInt32();

            var numFiles = memFile.ReadInt32();
            for (int i = 0; i < numFiles; i++)
            {
                var tag = memFile.ReadInt32();
                var name = memFile.ReadStringASCIINull();
                if (string.IsNullOrWhiteSpace(name)) name = LC.GetString(LC.string_nameNotListedInMemBrackets);
                var offset = memFile.ReadUInt64();
                var size = memFile.ReadUInt64();

                // 04/04/2025 - It looks like MEM Legacy did not write this value
                // as shown here https://github.com/MassEffectModder/MassEffectModderLegacy/blob/d7ce737cfc63e3c99c4b64be8d0f953ed3bc6943/MassEffectModder/Misc.cs#L1983
                if (version >= 3)
                {
                    var flags = memFile.ReadUInt64();
                }

                files.Add(name);
            }

            return files;
        }

        // Mod files are NOT supported in M3
#if ALOT
        public static ModFileInfo GetGameForMod(string file)
        {
            try
            {
                using var modFile = File.OpenRead(file);
                var len = modFile.ReadInt32(); //first 4 bytes
                var version = modFile.ReadStringASCIINull();
                modFile.SeekBegin();
                if (version.Length >= 5) // "modern" .mod
                {
                    //Re-read the version length
                    version = modFile.ReadUnrealString();
                }
                var numEntries = modFile.ReadUInt32();
                string desc = modFile.ReadUnrealString();
                var script = modFile.ReadUnrealString().Split("\n").Select(x => x.Trim()).ToList(); // do not localize
                ApplicableGame game = ApplicableGame.None;
                if (script.Any(x => x.StartsWith(@"using ME1Explorer")))
                {
                    game |= ApplicableGame.ME1;
                }
                else if (script.Any(x => x.StartsWith(@"using ME2Explorer")))
                {
                    game |= ApplicableGame.ME2;
                }
                else if (script.Any(x => x.StartsWith(@"using ME3Explorer")))
                {
                    game |= ApplicableGame.ME3;
                }

                var target = Locations.GetTarget(game.ApplicableGameToMEGame());
                if (target == null)
                {
                    return new ModFileInfo()
                    {
                        ApplicableGames = ApplicableGame.None,
                        Description = LC.GetString(LC.string_interp_targetGameXnotInstalled, game.ApplicableGameToMEGame()),
                        Usable = false
                    };
                }

                var biogame = M3Directories.GetBioGamePath(target);
                foreach (var pcc in script.Where(x => x.StartsWith(@"pccs.Add(")))  // do not localize
                {
                    var subBioPath = pcc.Substring("pccs.Add(\"".Length); // do not localize
                    subBioPath = subBioPath.Substring(0, subBioPath.Length - 3);
                    var targetFile = Path.Combine(biogame, subBioPath);
                    if (!File.Exists(targetFile))
                    {
                        return new ModFileInfo()
                        {
                            ApplicableGames = ApplicableGame.None,
                            Description = LC.GetString(LC.string_interp_targetFileDoesntExistX, subBioPath),
                            Usable = false
                        };
                    }
                }

                return new ModFileInfo()
                {
                    ApplicableGames = game,
                    Description = desc,
                    Usable = true
                };
            }
            catch (Exception e)
            {
                return new ModFileInfo()
                {
                    ApplicableGames = ApplicableGame.None,
                    Description = e.Message,
                    Usable = false
                };
            }


            //string path = "";
            //if (desc.Contains("Binary Replacement"))
            //{
            //    try
            //    {
            //        ParseME3xBinaryScriptMod(scriptLegacy, ref package, ref mod.exportId, ref path);
            //        if (mod.exportId == -1 || package == "" || path == "")
            //        {
            //            // NOT COMPATIBLE
            //            return ApplicableGame.None;
            //        }
            //    }
            //    catch
            //    {
            //        // NOT COMPATIBLE
            //        return ApplicableGame.None;
            //    }
            //    mod.packagePath = Path.Combine(path, package);
            //    mod.binaryModType = 1;
            //    len = modFile.ReadInt32();
            //    mod.data = modFile.ReadToBuffer(len);
            //}
            //else
            //{
            //    modFile.SeekBegin();
            //    len = modFile.ReadInt32();
            //    version = modFile.ReadStringASCII(len); // version
            //}

        }
#endif

    }
}
