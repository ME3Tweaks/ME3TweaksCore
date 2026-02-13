using LegendaryExplorerCore.Misc;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Targets;
using System;
using System.Collections.Generic;
using System.Linq;
using LegendaryExplorerCore.Helpers;
using ME3TweaksCore.Services.ThirdPartyModIdentification;

namespace ME3TweaksCore.Objects
{
    public abstract class DLCRequirementBase
    {
        public const string REQKEY_MINVERSION = @"minversion";
        public const string REQKEY_DLCOPTIONKEY = @"optionkey";
        public const string REQKEY_MAXVERSION = @"maxversion"; // I highly doubt this will be used, but maybe some new mod will make people unhappy and they will try to use old version. DLCRequirements does not support this as it doesn't really make sense to only allow install against older version

        /// <summary>
        /// The DLC foldername to test for. This is PlusMinus because of COND_DLC_SPECIFIC_SETUP
        /// </summary>
        public PlusMinusKey DLCFolderName { get; init; }

        /// <summary>
        /// The minimum version for this DLC requirement to be met - CAN BE NULL to specify no min version
        /// </summary>
        public Version MinVersion { get; init; }

        /// <summary>
        /// Maximum version of a mod allowed for this DLC requirement to be met - CAN BE NULL to specify no max version
        /// </summary>
        public Version MaxVersion { get; set; }

        /// <summary>
        /// Chosen option requirements for DLC - CAN BE NULL to specify no options 
        /// </summary>
        public List<DLCOptionKey> DLCOptionKeys { get; init; }

        /// <summary>
        /// Cached failure reason from the last call to IsRequirementMet()
        /// </summary>
        public string LastFailedReason { get; set; }

        /// <summary>
        /// Empty constructor
        /// </summary>
        public DLCRequirementBase() { }
        public DLCRequirementBase(string inputString, double featureLevel, bool folderNameIsPlusMinus, char openingChar = '[', char closingChar = ']')
        {
            inputString = inputString.Trim();

            //if (inputString.Contains('(') || inputString.Contains(')'))
            //{
            //    throw new Exception("DLCRequirementBase cannot contain ( or ) due to parser issues that would potentially break backwards compatibility if fixed. Use [ ] instead for conditions.");
            //}

            var structStart = inputString.IndexOf(openingChar);
            var structEnd = inputString.LastIndexOf(closingChar);

            // No conditions.
            if (structStart == -1 && structEnd == -1)
            {
                DLCFolderName = folderNameIsPlusMinus ? new PlusMinusKey(inputString) : new PlusMinusKey(null, inputString);
                return;
            }

            // Backwards ][.
            if (structStart == -1 || structEnd == -1 || structStart > structEnd)
            {
                // Todo: UPDATE LOCALIZATION FOR THIS
                throw new Exception(LC.GetString(LC.string_dlcRequirementInvalidParenthesis));
            }

            string attributeStruct = inputString.Substring(structStart + 1, structEnd - (structStart + 1));
            var keyMap = StringStructParser.GetSplitMultiMapValues(attributeStruct, canBeCaseInsensitive: true, openChar: openingChar, closeChar: closingChar);
            if (keyMap.Count == 0)
                throw new Exception(LC.GetString(LC.string_interp_drb_malformedDR, openingChar, closingChar, inputString));

            // Read values
            foreach (var key in keyMap)
            {
                // Conditions.
                switch (key.Key)
                {
                    case DLCRequirement.REQKEY_MINVERSION:
                        if (!Version.TryParse(key.Value[0], out var minVer))
                        {
                            throw new Exception(LC.GetString(LC.string_interp_dlcRequirementInvalidBadVersion, key.Value));
                        }

                        MinVersion = minVer;
                        break;
                    case DLCRequirement.REQKEY_MAXVERSION:
                        if (!Version.TryParse(key.Value[0], out var maxVer))
                        {
                            throw new Exception(LC.GetString(LC.string_interp_dlcRequirementInvalidBadVersion, key.Value));
                        }

                        MaxVersion = maxVer;
                        break;
                    case DLCRequirement.REQKEY_DLCOPTIONKEY:
                        foreach (var value in key.Value)
                        {
                            // + = Must be chosen
                            // Blank = Any blank can be chosen to satisfy
                            // - = Must not be chosen

                            DLCOptionKeys ??= new List<DLCOptionKey>();
                            DLCOptionKeys.Add(new DLCOptionKey(value, featureLevel));
                        }

                        break;
                    default:
                        throw new Exception(LC.GetString(LC.string_interp_drb_unknownDescriptor, key.Key));
                }
            }

            var fname = inputString.Substring(0, structStart);
            DLCFolderName = folderNameIsPlusMinus ? new PlusMinusKey(fname) : new PlusMinusKey(null, fname);
        }

        /// <summary>
        /// ToString() for this. This is what gets serialized into ModDesc!
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            if (!HasConditions())
                return DLCFolderName.ToString();

            CaseInsensitiveDictionary<List<string>> keyMap = new CaseInsensitiveDictionary<List<string>>();
            if (MinVersion != null)
                keyMap[REQKEY_MINVERSION] = [MinVersion.ToString()];
            if (MaxVersion != null)
                keyMap[REQKEY_MAXVERSION] = [MaxVersion.ToString()];
            if (DLCOptionKeys != null)
                keyMap[REQKEY_DLCOPTIONKEY] = DLCOptionKeys.Select(x => x.ToString()).ToList();

            return $@"{DLCFolderName}{StringStructParser.BuildCommaSeparatedSplitMultiValueList(keyMap, '[', ']', quoteValues: false)}";
        }

        public bool HasConditions()
        {
            return MinVersion != null || MaxVersion != null || DLCOptionKeys != null;
        }

        /// <summary>
        /// Tests if this requirement is met against the given target
        /// </summary>
        /// <param name="target">Can be null if metas is populated.</param>
        /// <returns></returns>
        public bool IsRequirementMet(GameTarget target, CaseInsensitiveDictionary<MetaCMM> metas = null, bool checkOptionKeys = true)
        {
            metas ??= target.GetMetaMappedInstalledDLC();
            if (DLCFolderName.IsPlus == false)
            {
                // Checking if DLC is not present.
                return !metas.ContainsKey(DLCFolderName.Key);
            }

            // Checking for existence.
            if (metas.TryGetValue(DLCFolderName.Key, out var meta))
            {
                // Check if there is anything beyond DLC present
                if (!HasConditions())
                    return true; // No min version

                // if no metacmm file is found it will be mapped to null.
                if (meta != null)
                {
                    // MINIMUM VERSION
                    if (MinVersion != null)
                    {
                        if (Version.TryParse(meta.Version, out var version))
                        {
                            if (ProperVersion.IsLessThan(version, MinVersion))
                            {
                                // Does not meet minimum version requirement
                                MLog.Error($@"DLCRequirement not met: {version} is less than MinVersion {MinVersion}");
                                return false;
                            }

                            // Version DLC Requirement Met
                        }
                        else
                        {
                            MLog.Error($@"DLCRequirement not met: Could not read a semantic mod version of {DLCFolderName} from its _metacmm file. Cannot conduct minimum version requirement. Value: {meta.Version}");
                            return false;
                        }
                    }

                    // MAXIMUM VERSION
                    if (MaxVersion != null)
                    {
                        if (Version.TryParse(meta.Version, out var version))
                        {
                            if (ProperVersion.IsGreaterThan(version, MaxVersion))
                            {
                                // Does not meet minimum version requirement
                                MLog.Error($@"DLCRequirement not met: {version} is greater than MaxVersion {MaxVersion}");
                                return false;
                            }

                            // Version DLC Requirement Met
                        }
                        else
                        {
                            MLog.Error($@"DLCRequirement not met: Could not read a semantic mod version of {DLCFolderName} from its _metacmm file. Cannot conduct maximum version requirement. Value: {meta.Version}");
                            return false;
                        }
                    }

                    // OPTION KEYS
                    if (checkOptionKeys && DLCOptionKeys != null)
                    {
                        // Hard yes/no on all reqs
                        foreach (var key in DLCOptionKeys.Where(x => x.OptionKey.IsPlus != null))
                        {
                            if (!IsHardDLCOptionKeyRequirementMet(meta, key))
                            {
                                if (key.OptionKey.IsPlus == true)
                                {
                                    //MLog.Error($@"DLCRequirement not met: DLC Option Key requirement is not met: {DLCFolderName} is not installed with optionkey value specified: {key}");
                                    LastFailedReason = LC.GetString(LC.string_interp_drb_notInstalledWithOptionX, key.UIString ?? key.OptionKey.Key);
                                }
                                else
                                {
                                    //MLog.Error($@"DLCRequirement not met: DLC Option Key requirement is not met: {DLCFolderName} is not installed with optionkey value specified: {key}");
                                    LastFailedReason = LC.GetString(LC.string_interp_drb_installedWithIncompatibleOptionX, key.UIString ?? key.OptionKey.Key);
                                }
                                return false;
                            }
                        }

                        // Soft, shared check
                        var shared = DLCOptionKeys.Where(x => x.OptionKey.IsPlus == null).ToList();
                        if (shared.Any() && !IsAnyDLCOptionKeyRequirementMet(meta, shared))
                        {
                            var optionsStr = string.Join(',', shared.Select(x => x.UIString ?? x.OptionKey.Key));
                            LastFailedReason = LC.GetString(LC.string_interp_drb_notInstalledWithAtLeastX, optionsStr);
                            return false;
                        }
                    }
                }
                else
                {
                    MLog.Error($@"DLCRequirementBase condition not met: Information about the mod of the DLC mod {DLCFolderName} could not be determined; there is no mod manager metadata file with the DLC mod. Manually installed mods are not supported for extended DLC requirements.");
                    return false;
                }

                return true; // DLC was found
            }

            // DLC folder was not found
            return false;
        }

        /// <summary>
        /// Checks if this alternate meets the conditional DLC requirements for option keys in other mods.
        /// </summary>
        /// <param name="meta">Information about the installed target mod</param>
        /// <param name="optionKey">Option key to test for</param>
        /// <returns></returns>
        private bool IsHardDLCOptionKeyRequirementMet(MetaCMM meta, DLCOptionKey optionKey)
        {
            if (optionKey.OptionKey.IsPlus == true && !meta.OptionKeysSelectedAtInstallTime.Contains(optionKey.OptionKey.Key, StringComparer.InvariantCultureIgnoreCase))
            {
                // Option that must have been chosen was not.
                return false;
            }
            if (optionKey.OptionKey.IsPlus == false && meta.OptionKeysSelectedAtInstallTime.Contains(optionKey.OptionKey.Key, StringComparer.InvariantCultureIgnoreCase))
            {
                // Option that must not have been chosen in another mod was chosen.
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks for option keys that use blanks - the 'any of installed' variant.
        /// </summary>
        /// <param name="meta">Information about the installed target mod</param>
        /// <param name="optionKeys">List of any option key to test for</param>
        /// <returns></returns>
        private bool IsAnyDLCOptionKeyRequirementMet(MetaCMM meta, List<DLCOptionKey> optionKeys)
        {
            // Are any of the specified option keys installed into the target mod?
            return meta.OptionKeysSelectedAtInstallTime.Any(x => optionKeys.Any(y => y.OptionKey.Key.CaseInsensitiveEquals(x)));
        }

        /// <summary>
        /// Converts this requirement to a UI readable string
        /// </summary>
        /// <param name="info">Optional TMPI info about the mod foldername</param>
        /// <returns></returns>
        public string ToUIString(ThirdPartyModInfo info, bool showDLCModName = true)
        {
            string dlcText = "";
            if (info != null)
            {
                // We assume this is for installed DLC
                dlcText += $@"{info.modname}";
                if (showDLCModName)
                {
                    dlcText += $@" ({DLCFolderName.Key})";
                }
            }
            else
            {
                dlcText += DLCFolderName.Key;
            }

            if (MinVersion != null)
            {
                dlcText += @" " + LC.GetString(LC.string_interp_minVersionAppend, MinVersion);
            }

            if (MaxVersion != null)
            {
                dlcText += @" " + LC.GetString(LC.string_interp_maxVersionAppend, MaxVersion);
            }

            if (DLCOptionKeys != null)
            {
                foreach (var f in DLCOptionKeys.Where(x => x.OptionKey.IsPlus != null))
                {
                    dlcText += @" " + f.ToUIString();
                }

                var softDeps = DLCOptionKeys.Where(x => x.OptionKey.IsPlus == null).ToList();
                if (softDeps.Any())
                {
                    dlcText += @" " + LC.GetString(LC.string_interp_drb_withAtLeastOneOfTheFollowingX, string.Join(',', softDeps.Select(x => x.ToUIString())));
                }
            }

            return dlcText;
        }
    }
}
