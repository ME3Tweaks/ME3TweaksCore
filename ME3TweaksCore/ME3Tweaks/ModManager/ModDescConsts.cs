namespace ME3TweaksCore.ME3Tweaks.ModManager
{
    // You can view references to these to determine feature level changes
    /// <summary>
    /// Contains constants for different versions of moddesc. This allows the editor tools to more easily find features that only apply to certain
    /// versions of moddesc.
    /// </summary>
    public class ModDescConsts
    {
        /// <summary>
        /// ModDesc 1.0 - Legacy Mod Manager 1.0/1.1 (2012) - Only supports ME3 basegame coalesced
        /// </summary>
        public const double MODDESC_VERSION_1_0 = 1.0;

        /// <summary>
        /// ModDesc 2.0 - Mod Manager 2 (2013) - Supports newfiles/replacefiles, modcoal flag
        /// </summary>
        public const double MODDESC_VERSION_2_0 = 2.0;

        /// <summary>
        /// ModDesc 3.0 - Mod Manager 3 (2014) - Supports TESTPATCH header
        /// </summary>
        public const double MODDESC_VERSION_3_0 = 3.0;

        /// <summary>
        /// ModDesc 3.1 - Mod Manager 3.1 (2014) - Supports CUSTOMDLC
        /// </summary>
        public const double MODDESC_VERSION_3_1 = 3.1;

        /// <summary>
        /// FAKE VERSION - Remaps to 3.1 due to version changes at the time.
        /// </summary>
        public const double MODDESC_VERSION_4_0 = 4.0;

        /// <summary>
        /// ModDesc 4.1 - Supports addfiles/addfilestargets
        /// </summary>
        public const double MODDESC_VERSION_4_1 = 4.1;

        /// <summary>
        /// ModDesc 4.2 - Supports altfiles
        /// </summary>
        public const double MODDESC_VERSION_4_2 = 4.2;

        /// <summary>
        /// ModDesc 4.3 - Supports BALANCE_CHANGES header
        /// </summary>
        public const double MODDESC_VERSION_4_3 = 4.3;

        /// <summary>
        /// ModDesc 4.4 - Supports altdlc, outdatedcustomdlc
        /// </summary>
        public const double MODDESC_VERSION_4_4 = 4.4;

        /// <summary>
        /// ModDesc 4.5 - Adds support for altfiles
        /// </summary>
        public const double MODDESC_VERSION_4_5 = 4.5;

        /// <summary>
        /// ModDesc 5.0 - Supports requireddlc
        /// </summary>
        public const double MODDESC_VERSION_5_0 = 5.0;

        /// <summary>
        /// ModDesc 5.1 - Supports additionaldeploymentfolders
        /// </summary>
        public const double MODDESC_VERSION_5_1 = 5.1;

        /// <summary>
        /// ModDesc 6.0 - Supports game descriptor, gamedirectorystructure, incompatibledlc, multilists, additionaldeploymentfiles, ME1/ME2 support
        /// </summary>
        public const double MODDESC_VERSION_6_0 = 6.0;

        /// <summary>
        /// ModDesc 6.1 - Supports LOCALIZATION header (ME2/3)
        /// </summary>
        public const double MODDESC_VERSION_6_1 = 6.1;

        /// <summary>
        /// ModDesc 6.2 - Supports bannerimagename, optional single required DLC (? prefix)
        /// </summary>
        public const double MODDESC_VERSION_6_2 = 6.2;

        /// <summary>
        /// ModDesc 6.3 - Support +/- in DLC requirements,
        /// </summary>
        public const double MODDESC_VERSION_6_3 = 6.3;

        /// <summary>
        /// ModDesc 7.0 - Supports Legendary Edition games, mergemods, GAME1_EMBEDDED_TLK
        /// </summary>
        public const double MODDESC_VERSION_7_0 = 7.0;

        /// <summary>
        /// ModDesc 8.0 - Supports LE vanilla DLC requirement restrictions, sortalternates, alternate dependencies, sortindex, compressed TLK data
        /// </summary>
        public const double MODDESC_VERSION_8_0 = 8.0;

        /// <summary>
        /// ModDesc 8.1 - Supports TEXTUREMODS, HEADMORPHS, requiresenhancedbink, basegame mergemods validation
        /// </summary>
        public const double MODDESC_VERSION_8_1 = 8.1;

        /// <summary>
        /// ModDesc 9.0 - Supports ASIMODS, batchinstallreversesort, keyed DLC requirements, TLK option keys (subdirectories)
        /// </summary>
        public const double MODDESC_VERSION_9_0 = 9.0;

        /// <summary>
        /// ModDesc 9.1 - Fixes for (), [] in struct parsing for alternates
        /// </summary>
        public const double MODDESC_VERSION_9_1 = 9.1;

        /// <summary>
        /// ModDesc 9.2 - Supports M3GS, M3TO, changes to DependsOnKeys selection logic
        /// </summary>
        public const double MODDESC_VERSION_9_2 = 9.2;
    }
}
