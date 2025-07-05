using System;
using System.Net;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Services;

namespace ME3TweaksCore.Misc
{
    public class ShortTimeoutWebClient : WebClient
    {
        public ShortTimeoutWebClient() : base()
        {
            // Set the user agent
            Headers.Add(@"user-agent", $@"{MLibraryConsumer.GetHostingProcessname()} {MLibraryConsumer.GetAppVersion()}");
        }

        protected override WebRequest GetWebRequest(Uri uri)
        {
            var w = base.GetWebRequest(uri) as HttpWebRequest;
            w.Headers.Add(HttpRequestHeader.AcceptEncoding, @"gzip,deflate"); // We accept gzip, deflate
            w.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
            w.Timeout = MSharedSettings.WebClientTimeout * 1000;
            return w;
        }
    }
}
