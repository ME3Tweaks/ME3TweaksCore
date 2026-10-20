namespace ME3TweaksCore.NativeMods
{

    /// <summary>
    /// Enumeration of ASI Manager Update Groups.
    /// </summary>
    public enum ASIModUpdateGroupID
    {
        ME1_DLCModEnabler = 16,

        ME2_KismetLogger = 14,
        ME2_FunctionLogger = 15,
        ME2_AceSlammer = 18,
        ME2_SplashEnabler = 20,
        ME2_KasumiCrashFix = 23,
        ME2_StreamingLevelsHUD = 25,
        ME2_DeathCounterLS = 26,
        ME2_SeqAct_LogEnabler = 27,
        ME2_CombolutionSupport = 28,

        ME3_MouseDisabler = 0,
        ME3_OriginMPStatus = 1,
        ME3_ME3ClientMessageExposer = 2,
        ME3_OriginUnlinker = 4,
        ME3_BalanceChangesReplacer = 5,
        ME3_LiveTLKReplacer = 6,
        ME3_ME3Parallelization = 7,
        ME3_ME3Logger = 8,
        ME3_AutoTOC = 9,
        ME3_KismetLogger = 10,
        ME3_RetaliationBugfix = 11,
        ME3_FullGAW = 12,
        ME3_POCCCMovingSpawnPoints = 13,
        ME3_DocumentsRedirector = 17,
        ME3_SeqAct_LogEnabler = 19,
        ME3_SplashEnabler = 21,
        ME3_GarbageCollectionForcer = 22,
        ME3_StreamingLevelsHUD = 24,
        ME3_ControllerInputTester = 33,

        LE1_AutoTOCLE = 29, // All LE uses the same ASI. But ASI Manager design forces a new ID for a different game.
        LE1_AutoloadEnabler = 32,
        LE1_AutoloadEnablerDebug = 34,
        LE1_StreamingLevelsHUD = 35,
        LE1_KismetLogger = 36,
        LE1_SeqAct_LogEnabler = 37,
        LE1_LEXInterop = 42,
        LE1_2DAPrinter = 43,
        LE1_PNGScreenShots = 44,
        LE1_LinkerPrinter = 47,
        LE1_DebugLogger = 70,
        LE1_ConsoleExtension = 75,
        LE1_ScriptDebugger = 82,
        LE1_FunctionLogger = 83,

        LE2_AutoTOCLE = 30, // All LE uses the same ASI. But ASI Manager design forces a new ID for a different game.
        LE2_StreamingLevelsHUD = 38,
        LE2_KismetLogger = 41,
        LE2_PNGScreenShots = 45,
        LE2_LinkerPrinter = 48,
        LE2_DebugLogger = 71,
        LE2_SeqAct_LogEnabler = 73,
        LE2_ConsoleExtension = 76,
        LE2_HotReload = 78,
        LE2_LEXInterop = 79,
        LE2_ScriptDebugger = 81,
        LE2_FunctionLogger = 84,
        
        LE3_AutoTOCLE = 31, // All LE uses the same ASI. But ASI Manager design forces a new ID for a different game.
        LE3_StreamingLevelsHUD = 39,
        LE3_KismetLogger = 40,
        LE3_PNGScreenShots = 46,
        LE3_LinkerPrinter = 49,
        LE3_DebugLogger = 72,
        LE3_SeqAct_LogEnabler = 74,
        LE3_ConsoleExtension = 77,
        LE3_LEXInterop = 80,
        LE3_FunctionLogger = 85,


        LMP_MilanHost = 501,
        LMP_AutoTOC = 502,
        LMP_TextureOverride = 503,

        LEL_MilanLauncher = 510,

        LMP_DiscordIntegration = 1207
    }
}
