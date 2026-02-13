using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Misc;
using ME3TweaksCore.NativeMods;
using ME3TweaksCore.Services;
using ME3TweaksCore.Services.FileSource;
using ME3TweaksCore.Services.Shared.BasegameFileIdentification;
using ME3TweaksCore.Services.Symbol;
using ME3TweaksCore.Services.ThirdPartyModIdentification;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ME3TweaksCore.ME3Tweaks.Online
{
    /// <summary>
    /// Class for handling the various online service fetches (ME3TweaksCore)
    /// </summary>
    public static class MCoreServiceLoader
    {
        /// <summary>
        /// Delegate that defines a service loader definition
        /// </summary>
        /// <param name="data">The online content data that was loaded from the online service</param>
        /// <returns></returns>
        public delegate bool OnlineServiceLoader(JToken data);

        // SERVICE KEYS

        /// <summary>
        /// Key for combined fetch
        /// </summary>
        public const string ASI_MANIFEST_KEY = @"asimanifest";
        public const string BGFI_SERVICE_KEY = @"basegamefileidentificationservice";
        public const string TPMI_SERVICE_KEY = @"thirdpartyidentificationservice";
        public const string FS_SERVICE_KEY   = @"filesourceservice";
        public const string SYMBOL_SERVICE_KEY = @"symbolservice";

        /// <summary>
        /// If this is the first content check for this session, we will check throttling for online fetch. If we are not able to use online, we will use cached instead
        /// </summary>
        private static bool FirstContentCheck = true;

        // ME3TweaksCore service loaders
        private static Dictionary<string, OnlineServiceLoader> ServiceLoaders = new()
        {
            // Identifies mods by DLC name per game
            { TPMI_SERVICE_KEY, TPMIService.LoadService },
            { BGFI_SERVICE_KEY, BasegameFileIdentificationService.LoadService },
            { FS_SERVICE_KEY, FileSourceService.LoadService },
            { ASI_MANIFEST_KEY, ASIManager.LoadService },
            { SYMBOL_SERVICE_KEY, SymbolService.LoadService },
        };

        /// <summary>
        /// Loads ME3Tweaks services that depend on the ME3Tweaks server
        /// <param name="endpoints">The list of endpoints to load from</param>
        /// <returns>The parsed online content if the manifest was successfully fetched; null otherwise</returns>
        /// </summary>
        public static JToken LoadServices(FallbackLink endpoints, bool useCachedContent = false)
        {
            // We cache this here in the event that there's some exception.
            useCachedContent = useCachedContent || (FirstContentCheck && !MOnlineContent.CanFetchContentThrottleCheck());
            FirstContentCheck = false; // Ensure this is false after the initial usage

            string serviceData = null;
            if (!useCachedContent)
            {
                foreach (var staticurl in endpoints.GetAllLinks())
                {
                    Uri myUri = new Uri(staticurl);
                    string host = myUri.Host;

                    var fetchUrl = staticurl;

                    try
                    {
                        using var wc = new ShortTimeoutWebClient();
                        Stopwatch sw = Stopwatch.StartNew();
                        
                        serviceData = wc.DownloadStringAwareOfEncoding(fetchUrl);
                        sw.Stop();
                        MLog.Information($@"Fetched combined services data from endpoint {host} in {sw.ElapsedMilliseconds}ms");
                        break;
                    }
                    catch (Exception e)
                    {
                        MLog.Error($@"Unable to fetch combined services data from endpoint {host}: {e.Message}");
                    }
                }

                if (serviceData == null)
                {
                    MLog.Error(@"Unable to fetch combined services data, services will use cached data instead");
                }
            }

            var combinedServicesManifest = serviceData != null ? JsonConvert.DeserializeObject<JToken>(serviceData) : null;

            foreach (var serviceLoader in ServiceLoaders)
            {
                if (combinedServicesManifest != null)
                {
                    // if service is not defined in combined manifest, this just returns null
                    serviceLoader.Value.Invoke(combinedServicesManifest[serviceLoader.Key]);
                }
                else
                {
                    serviceLoader.Value.Invoke(null);
                }
            }

            return combinedServicesManifest;
        }
    }
}
