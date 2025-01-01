using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Misc;
using ME3TweaksCore.NativeMods;
using ME3TweaksCore.Services;
using Serilog;

namespace ME3TweaksCore
{
    /// <summary>
    /// Class containing variables and callbacks that can be assigned to to pass to the ME3TweaksCore InitLib method.
    /// </summary>
    public class ME3TweaksCoreLibInitPackage
    {
        /// <summary>
        /// Specifies if auxiliary services such as BasegameFileIdentificationService, ThirdPartyIdentificationService, etc, should initialize with the library. Setting this to false
        /// means that the consuming library will manually initialize them.
        /// </summary>
        public bool LoadAuxiliaryServices { get; set; } = true;

        /// <summary>
        /// Indicates that the BuildHelper class should get information about the hosting executable when the library is loaded
        /// </summary>
        public bool LoadBuildInfo { get; set; } = true;

        /// <summary>
        /// Defines the list of authorized names on authenticode signatures to treat this build as genuine
        /// </summary>
        public BuildHelper.BuildSigner[] AllowedSigners { get; set; }

        /// <summary>
        /// The run on ui thread delegate. If you are not using WPF, this should just return a thread.
        /// </summary>
        [DisallowNull]
        public Action<Action> RunOnUiThreadDelegate { get; init; }

        /// <summary>
        /// Delegate to create a logger. Used when a log collection takes place and the logger must be restarted.
        /// </summary>
        public Func<ILogger> CreateLogger { get; init; }

        /// <summary>
        /// Delegate to create fetch the wrapping application's logger. Used when initializing logger objects.
        /// </summary>
        public Func<ILogger> GetLogger { get; init; }

        /// <summary>
        /// Function to invoke to test if we can fetch online content. This method should be throttled (for example, once per day) to prevent server overload or having the web host blacklist the client IP.
        /// </summary>
        public Func<bool> CanFetchContentThrottleCheck { get; init; }


        // TELEMETRY CALLBACKS

        /// <summary>
        /// Delegate to call when the internal library wants to track that an event has occurred.
        /// </summary>
        public Action<string, Dictionary<string, string>> TrackEventCallback { get; init; }
        /// <summary>
        /// Delegate to call when the internal library wants to track that an error has occurred.
        /// </summary>
        public Action<Exception, Dictionary<string, string>> TrackErrorCallback { get; init; }
        /// <summary>
        /// Delegate to call when the internal library wants to track that an error has occurred and a log should also be included.
        /// </summary>
        public Action<Exception, Dictionary<string, string>> UploadErrorLogCallback { get; init; }
        /// <summary>
        /// Called by LegendaryExplorerCore when a package fails to save.
        /// </summary>
        public Action<string> LECPackageSaveFailedCallback { get; init; }

        // CLASS EXTENSIONS (FOR GAMETARGET)
        // THESE ARE OPTIONAL
        /// <summary>
        /// Delegate that will be called when an InstalledDLCMod object is generated. You can supply your own extended version such as a WPF version or your own implementation
        /// </summary>
        public MExtendedClassGenerators.GenerateInstalledDLCModDelegate GenerateInstalledDlcModDelegate { get; init; }

        /// <summary>
        /// Delegate that will be called when an InstalledExtraFile object is generated. You can supply your own extended version such as a WPF version or your own implementation
        /// </summary>
        public MExtendedClassGenerators.GenerateInstalledExtraFileDelegate GenerateInstalledExtraFileDelegate { get; init; }

        /// <summary>
        /// Delegate that will be called when a ModifiedFileObject is generated. You can supply your own extended version such as a WPF version or your own implementation
        /// </summary>
        public MExtendedClassGenerators.GenerateModifiedFileObjectDelegate GenerateModifiedFileObjectDelegate { get; init; }

        /// <summary>
        /// Delegate that will be called when an SFARObject is generated. You can supply your own extended version such as a WPF version or your own implementation
        /// </summary>
        public MExtendedClassGenerators.GenerateSFARObjectDelegate GenerateSFARObjectDelegate { get; init; }

        /// <summary>
        /// Delegate that will be called when a KnownInstalledASIMod is generated. You can supply your own extended version such as a WPF version or your own implementation
        /// </summary>
        public MExtendedClassGenerators.GenerateKnownInstalledASIModDelegate GenerateKnownInstalledASIModDelegate { get; init; }

        /// <summary>
        /// Delegate that will be called when a UnknownInstalledASIMod is generated. You can supply your own extended version such as a WPF version or your own implementation
        /// </summary>
        public MExtendedClassGenerators.GenerateUnknownInstalledASIModDelegate GenerateUnknownInstalledASIModDelegate { get; init; }

        /// <summary>
        /// The list of endpoints to use for loading online services on initial boot. If this is not set, auxillary services will not be loaded. This is not used if LoadAuxiliaryServices is false.
        /// </summary>
        public FallbackLink AuxiliaryCombinedOnlineServicesEndpoint { get; init; }

        /// <summary>
        /// If beta features should be enabled 
        /// </summary>
        public bool BetaMode { get; init; }

        /// <summary>
        /// The localization language to load after loading the INT one to initially set the strings
        /// </summary>
        public string InitialLanguage { get; init; }

        /// <summary>
        /// The list of property databases to load when Legendary Explorer Core loads. Default is all ME games
        /// </summary>
        public MEGame[] PropertyDatabasesToLoad { get; set; } = new[] { MEGame.ME1, MEGame.ME2, MEGame.ME3, MEGame.LE1, MEGame.LE2, MEGame.LE3 };

        /// <summary>
        /// Installs the callbacks specified in this package into ME3TweaksCore.
        /// </summary>
        internal void InstallInitPackage()
        {
            CheckOptions();
            LogCollector.CreateLogger = CreateLogger;
            Log.Logger ??= LogCollector.CreateLogger?.Invoke();

            if (LoadBuildInfo)
            {
                BuildHelper.ReadRuildInfo(AllowedSigners);
            }


            ME3TweaksCoreLib.RunOnUIThread = RunOnUiThreadDelegate;

            if (CanFetchContentThrottleCheck != null)
            {
                MOnlineContent.CanFetchContentThrottleCheck = CanFetchContentThrottleCheck;
            }

            TelemetryInterposer.SetErrorCallback(TrackErrorCallback);
            TelemetryInterposer.SetUploadErrorLogCallback(UploadErrorLogCallback);
            TelemetryInterposer.SetEventCallback(TrackEventCallback);

            // EXTENSION CLASSES
            if (GenerateInstalledDlcModDelegate != null)
                MExtendedClassGenerators.GenerateInstalledDlcModObject = GenerateInstalledDlcModDelegate;
            if (GenerateInstalledExtraFileDelegate != null)
                MExtendedClassGenerators.GenerateInstalledExtraFile = GenerateInstalledExtraFileDelegate;
            if (GenerateModifiedFileObjectDelegate != null)
                MExtendedClassGenerators.GenerateModifiedFileObject = GenerateModifiedFileObjectDelegate;
            if (GenerateSFARObjectDelegate != null)
                MExtendedClassGenerators.GenerateSFARObject = GenerateSFARObjectDelegate;
            if (GenerateSFARObjectDelegate != null)
                MExtendedClassGenerators.GenerateKnownInstalledASIMod = GenerateKnownInstalledASIModDelegate;
            if (GenerateSFARObjectDelegate != null)
                MExtendedClassGenerators.GenerateUnknownInstalledASIMod = GenerateUnknownInstalledASIModDelegate;

            // BETA FEATURES - These will require a reboot of the consuming app to properly fully work if changed during runtime
            ASIManager.Options.BetaMode = BetaMode;

            // Load strings
            LC.InternalSetLanguage(@"int"); // Load INT as it is the default language. Non-INT can be loaded later over the top of this
            if (InitialLanguage != null && InitialLanguage != @"int")
            {
                LC.InternalSetLanguage(InitialLanguage);
            }
        }

        [Conditional(@"DEBUG")]
        private void CheckOptions()
        {
            OptionNotSetCheck(RunOnUiThreadDelegate, nameof(RunOnUiThreadDelegate));
            OptionNotSetCheck(CreateLogger, nameof(CreateLogger));
            OptionNotSetCheck(CanFetchContentThrottleCheck, nameof(CanFetchContentThrottleCheck));
            OptionNotSetCheck(TrackEventCallback, nameof(TrackEventCallback));
            OptionNotSetCheck(TrackErrorCallback, nameof(TrackErrorCallback));
            OptionNotSetCheck(UploadErrorLogCallback, nameof(UploadErrorLogCallback));
            OptionNotSetCheck(LECPackageSaveFailedCallback, nameof(LECPackageSaveFailedCallback));
            OptionNotSetCheck(GenerateInstalledDlcModDelegate, nameof(GenerateInstalledDlcModDelegate));
            OptionNotSetCheck(GenerateInstalledExtraFileDelegate, nameof(GenerateInstalledExtraFileDelegate));
            OptionNotSetCheck(GenerateModifiedFileObjectDelegate, nameof(GenerateModifiedFileObjectDelegate));
            OptionNotSetCheck(GenerateSFARObjectDelegate, nameof(GenerateSFARObjectDelegate));
            OptionNotSetCheck(GenerateKnownInstalledASIModDelegate, nameof(GenerateKnownInstalledASIModDelegate));
            OptionNotSetCheck(GenerateUnknownInstalledASIModDelegate, nameof(GenerateUnknownInstalledASIModDelegate));
            if (LoadAuxiliaryServices)
                OptionNotSetCheck(AuxiliaryCombinedOnlineServicesEndpoint, nameof(AuxiliaryCombinedOnlineServicesEndpoint));
        }

        [Conditional(@"DEBUG")]
        private void OptionNotSetCheck(object obj, string optionName)
        {
            if (obj == null)
                MLog.Warning($@"DEBUG INFO: ME3TweaksCoreLibInitPackage option not set: {optionName}");
        }
    }
}
