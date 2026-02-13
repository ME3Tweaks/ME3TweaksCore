using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Text;
using LegendaryExplorerCore.Gammtek.Collections.ObjectModel;
using LegendaryExplorerCore.Gammtek.Extensions.Collections.Generic;
using LegendaryExplorerCore.Misc;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.ME3Tweaks.ModManager;
using ME3TweaksCore.Objects;

namespace ME3TweaksCore.GameFilesystem
{
    /// <summary>
    /// Class that represents data in _metacmm.txt files - files that describe the installed mod
    /// </summary>
    public class MetaCMM
    {
        #region Info Prefixes
        public static readonly string PrefixOptionsSelectedOnInstall = @"[INSTALLOPTIONS]";
        public static readonly string PrefixOptionKeysSelectedOnInstall = @"[OPTIONKEYS]";
        public static readonly string PrefixIncompatibleDLC = @"[INCOMPATIBLEDLC]";
        public static readonly string PrefixRequiredDLC = @"[REQUIREDDLC]";
        public static readonly string PrefixExtendedAttributes = @"[EXTENDEDATTRIBUTE]";
        public static readonly string PrefixModDescPath = @"[SOURCEMODDESC]";
        public static readonly string PrefixModDescHash = @"[SOURCEMODDESCHASH]";
        public static readonly string PrefixNexusUpdateCode = @"[NEXUSUPDATECODE]";
        public static readonly string PrefixUsesEnhancedBink = @"[USESENHANCEDBINK]";
        public static readonly string PrefixInstallTime = @"[INSTALLTIME]";
        public static readonly string PrefixModDescFeatureLevel = @"[MDFEATURELEVEL]";
        #endregion

        public string ModName { get; set; }
        public string Version { get; set; }
        public string InstalledBy { get; set; }
        public string InstallerInstanceGUID { get; set; }

        /// <summary>
        /// Path of moddesc.ini that was used to generate this (only used by M3)
        /// </summary>
        public string ModdescSourcePath { get; set; }

        /// <summary>
        /// The cmmver of the moddesc that was used to install this mod.
        /// </summary>
        public double ModDescFeatureLevel { get; set; }
        /// <summary>
        /// List of DLC that this one is not compatible with
        /// </summary>
        public ObservableCollectionExtended<string> IncompatibleDLC { get; } = new ObservableCollectionExtended<string>();
        /// <summary>
        /// List of DLC that this one is not compatible with
        /// </summary>
        public ObservableCollectionExtended<DLCRequirement> RequiredDLC { get; } = new ObservableCollectionExtended<DLCRequirement>();
        /// <summary>
        /// List of selected install-time options (human readable)
        /// </summary>
        public ObservableCollectionExtended<string> OptionsSelectedAtInstallTime { get; } = new ObservableCollectionExtended<string>();
        /// <summary>
        /// List of extended attributes that don't have a hardcoded variable in tooling.
        /// </summary>
        public CaseInsensitiveDictionary<string> ExtendedAttributes { get; } = new();

        /// <summary>
        /// The code used to check for nexus updates - this is not for updating the mod, but just checking the installed version against the server version, mostly for logging.
        /// </summary>
        public int NexusUpdateCode { get; set; }

        /// <summary>
        /// If this mod makes use of the enhanced 2022.5 bink encoder (LE only)
        /// </summary>
        public bool RequiresEnhancedBink { get; set; }

        /// <summary>
        /// When this DLC mod was installed
        /// </summary>
        public DateTime? InstallTime { get; set; }


        /// <summary>
        /// List of option keys that were installed. This is similar to <see cref="OptionsSelectedAtInstallTime"/> but this is for programatic access.
        /// </summary>
        public List<string> OptionKeysSelectedAtInstallTime { get; set; } = new();


        /// <summary>
        /// Hash of the moddesc that installed this mod
        /// </summary>
        public string ModdescSourceHash { get; set; }

        /// <summary>
        /// Writes the metacmm file to the specified filepath.
        /// </summary>
        /// <param name="path">Filepath to write to</param>
        /// <param name="installingAppName">The managed installer name that is writing this metacmm file. An integer indicates a Mod Manager build number</param>
        public void WriteMetaCMM(string path, string installingAppName)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(ModName);
            sb.AppendLine(Version);
            sb.AppendLine(InstalledBy ?? installingAppName);
            sb.AppendLine(InstallerInstanceGUID);

            // MetaCMM Extended
            if (OptionsSelectedAtInstallTime.Any())
            {
                sb.AppendLine($@"{PrefixOptionsSelectedOnInstall}{string.Join(';', OptionsSelectedAtInstallTime)}");
            }

            // Mod Manager 9: track option keys used during install
            if (OptionKeysSelectedAtInstallTime.Any())
            {
                sb.AppendLine($@"{PrefixOptionKeysSelectedOnInstall}{string.Join(';', OptionKeysSelectedAtInstallTime)}");
            }
            if (IncompatibleDLC.Any())
            {
                sb.AppendLine($@"{PrefixIncompatibleDLC}{string.Join(';', IncompatibleDLC)}");
            }

            // Mod Manager 9.1: track feature level of moddesc which is used to determine required dlc parsing
            if (ModDescFeatureLevel > 0)
            {
                sb.AppendLine($@"{PrefixModDescFeatureLevel}{ModDescFeatureLevel}");
            }

            // Mod Manager 8: Write the source moddesc.ini path so we can potentially use it to track 
            // which library mod was used to install it in the event we ever track mods in the library
            if (ModdescSourcePath != null)
            {
                sb.AppendLine($@"{PrefixModDescPath}{ModdescSourcePath}");
            }

            // Mod Manager 9: Instead, use the moddesc hash so we can also track archives.
            if (ModdescSourceHash != null)
            {
                sb.AppendLine($@"{PrefixModDescHash}{ModdescSourceHash}");
            }

            if (RequiredDLC.Any())
            {
                sb.AppendLine($@"{PrefixRequiredDLC}{string.Join(';', RequiredDLC)}");
            }

            // Mod Manager 8: Write nexus update code so we can check for updates in diags on the serverside
            if (NexusUpdateCode != 0)
            {
                sb.AppendLine($@"{PrefixNexusUpdateCode}{NexusUpdateCode}");
            }

            if (RequiresEnhancedBink)
            {
                sb.AppendLine($@"{PrefixUsesEnhancedBink}true");
            }

            if (InstallTime != null)
            {
                sb.AppendLine($@"{PrefixInstallTime}{InstallTime.Value.Ticks}");
            }



            File.WriteAllText(path, sb.ToString());
        }

        public MetaCMM() { }

        /// <summary>
        /// Loads a metaCMM file from disk
        /// </summary>
        /// <param name="metaFile"></param>
        public MetaCMM(string metaFile)
        {
            var lines = MUtilities.WriteSafeReadAllLines(metaFile).ToList();
            int i = 0;
            foreach (var line in lines)
            {
                switch (i)
                {
                    case 0:
                        ModName = line;
                        break;
                    case 1:
                        Version = line;
                        break;
                    case 2:
                        InstalledBy = line;
                        break;
                    case 3:
                        InstallerInstanceGUID = line;
                        break;
                    default:
                        // MetaCMM Extended
                        if (line.StartsWith(PrefixOptionsSelectedOnInstall))
                        {
                            var parsedline = line.Substring(PrefixOptionsSelectedOnInstall.Length);
                            OptionsSelectedAtInstallTime.ReplaceAll(StringStructParser.GetSemicolonSplitList(parsedline));
                        }
                        else if (line.StartsWith(PrefixOptionKeysSelectedOnInstall))
                        {
                            var parsedline = line.Substring(PrefixOptionKeysSelectedOnInstall.Length);
                            OptionKeysSelectedAtInstallTime.ReplaceAll(StringStructParser.GetSemicolonSplitList(parsedline));
                        }
                        else if (line.StartsWith(PrefixIncompatibleDLC))
                        {
                            var parsedline = line.Substring(PrefixIncompatibleDLC.Length);
                            IncompatibleDLC.ReplaceAll(StringStructParser.GetSemicolonSplitList(parsedline));
                        }
                        else if (line.StartsWith(PrefixExtendedAttributes))
                        {
                            var parsedline = line.Substring(PrefixExtendedAttributes.Length);
                            var entry = new DuplicatingIni.IniEntry(parsedline);
                            ExtendedAttributes.Add(entry.Key, entry.Value);
                        }
                        else if (line.StartsWith(PrefixModDescPath))
                        {
                            var parsedline = line.Substring(PrefixModDescPath.Length);
                            ModdescSourcePath = parsedline;
                        }
                        else if (line.StartsWith(PrefixRequiredDLC))
                        {
                            var parsedline = line.Substring(PrefixRequiredDLC.Length);
                            var split = parsedline.Split(';');
                            foreach (var s in split)
                            {
                                try
                                {
                                    // MetaCMM does not write things beyond version. I hope to not regret this decision
                                    // Someday i'll look at this and be like,
                                    // why did I not make this a static method for parsing a requirement? Why must I replace this 5 times?
                                    // Future Mgamerz note: The real issue is that I never deserialized ModDescFeatureLevel until 9.2
                                    // even though this was added in 9.1
                                    var testreq = ModDescFeatureLevel >= ModDescConsts.MODDESC_VERSION_9_0
                                        ? DLCRequirement.ParseRequirementKeyed(s, ModDescFeatureLevel)
                                        : DLCRequirement.ParseRequirement(s, false, ModDescFeatureLevel >= ModDescConsts.MODDESC_VERSION_6_3);
                                    RequiredDLC.Add(testreq);
                                }
                                catch (Exception ex1)
                                {
                                    MLog.Warning($@"Failed to read DLC requirement: {s} in metacmm file {metaFile}");
                                }
                            }
                        }
                        else if (line.StartsWith(PrefixNexusUpdateCode))
                        {
                            var parsedline = line.Substring(PrefixNexusUpdateCode.Length);
                            if (int.TryParse(parsedline, out var nc))
                            {
                                NexusUpdateCode = nc;
                            }
                            else
                            {
                                MLog.Warning($@"Failed to read NexusUpdateCode: Invalid value: {parsedline}");
                            }
                        }
                        else if (line.StartsWith(PrefixUsesEnhancedBink))
                        {
                            var parsedline = line.Substring(PrefixUsesEnhancedBink.Length);
                            if (bool.TryParse(parsedline, out var nc))
                            {
                                RequiresEnhancedBink = nc;
                            }
                            else
                            {
                                MLog.Warning($@"Failed to read UsesEnhancedBink: Invalid value: {parsedline}");
                            }
                        }
                        else if (line.StartsWith(PrefixInstallTime))
                        {
                            var parsedline = line.Substring(PrefixInstallTime.Length);
                            if (long.TryParse(parsedline, out var ticks))
                            {
                                try
                                {
                                    InstallTime = new DateTime(ticks);
                                }
                                catch (Exception ex)
                                {
                                    MLog.Warning($@"Failed to read install time, value {parsedline}: {ex.Message}");
                                }
                            }
                        }
                        else if (line.StartsWith(PrefixModDescHash))
                        {
                            ModdescSourceHash = line.Substring(PrefixModDescHash.Length);
                        }
                        else if (line.StartsWith(PrefixModDescFeatureLevel))
                        {
                            ModDescFeatureLevel = ModFileFormats.NormalizeModescVersion(double.Parse(line.Substring(PrefixModDescFeatureLevel.Length)));
                        }
                        break;
                }
                i++;
            }
        }
    }
}
