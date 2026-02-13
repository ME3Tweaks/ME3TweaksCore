using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Misc;
using Serilog;

namespace ME3TweaksCore.Services
{
    /// <summary>
    /// Class containing online content features and helper methods.
    /// </summary>
    public partial class MOnlineContent
    {
        /// <summary>
        /// Checks if we can perform an online content fetch. Library consumers should sets this value for an appropriate throttle.
        /// </summary>
        /// <returns></returns>
        public static Func<bool> CanFetchContentThrottleCheck { get; internal set; } = () =>
        {
            var lastContentCheck = MSharedSettings.LastContentCheck;
            var timeNow = DateTime.Now;
            return (timeNow - lastContentCheck).TotalDays > 1;
        };

        /// <summary>
        /// Fetches a remote string, aware of its encoding.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="authorizationToken"></param>
        /// <returns></returns>
        public static string FetchRemoteString(string url, string authorizationToken = null)
        {
            try
            {
                using var wc = new ShortTimeoutWebClient();
                if (authorizationToken != null)
                {
                    wc.Headers.Add(@"Authorization", authorizationToken);
                }
                return wc.DownloadStringAwareOfEncoding(url);
            }
            catch (Exception e)
            {
                MLog.Error(@"Error downloading string: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Fetches a remote string, aware of its encoding.
        /// </summary>
        /// <param name="url">FallbackLink to fetch data on</param>
        /// <param name="authorizationToken"></param>
        /// <returns></returns>
        public static string FetchRemoteString(FallbackLink url, string authorizationToken = null)
        {
            foreach (var fbUrl in url.GetAllLinks())
            {
                var result = FetchRemoteString(fbUrl, authorizationToken);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        /// <summary>
        /// Downloads from a URL to memory. This is a blocking call and must be done on a background thread.
        /// </summary>
        /// <param name="url">URL to download from</param>
        /// <param name="progressCallback">Progress information clalback</param>
        /// <param name="hash">Hash check value (md5). Leave null if no hash check</param>
        /// <returns></returns>

        public static (MemoryStream result, string errorMessage) DownloadToMemory(string url,
            Action<long, long> progressCallback = null,
            string hash = null,
            bool logDownload = false,
            CancellationTokenSource cancellationTokenSource = null)
        {
            MemoryStream responseStream = new MemoryStream();
            string downloadError = null;

            using var wc = new HttpClientDownloadWithProgress(url, responseStream, cancellationTokenSource?.Token ?? default);
            wc.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
            {
                progressCallback?.Invoke(totalBytesDownloaded, totalFileSize ?? 0);
            };

            if (logDownload)
            {
                MLog.Information(@"Downloading to memory: " + url);
            }
            else
            {
                Debug.WriteLine(@"Downloading to memory: " + url);
            }

            try
            {
                wc.StartDownload().Wait();
            }
            catch (Exception e)
            {
                // Error downloading
                downloadError = e.Message;
                return (responseStream, downloadError);
            }

            if (cancellationTokenSource != null && cancellationTokenSource.Token.IsCancellationRequested)
            {
                return (null, null);
            }

            if (hash == null) return (responseStream, downloadError);
            var md5 = MUtilities.CalculateHash(responseStream);
            responseStream.Position = 0;
            if (md5 != hash)
            {
                responseStream = null;
                downloadError = LC.GetString(LC.string_interp_onlineContentHashWrong, url, hash, md5);
            }

            return (responseStream, downloadError);
        }
    }
}
