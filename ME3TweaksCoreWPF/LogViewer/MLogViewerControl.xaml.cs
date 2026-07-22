using LegendaryExplorerCore.Helpers;
using ME3TweaksCore.Helpers;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace ME3TweaksCoreWPF.LogViewer
{
    /// <summary>
    /// A WPF user control that displays log text in a WebView2-based log viewer interface.
    /// The control loads a local HTML-based log viewer.
    /// </summary>
    public partial class MLogViewerControl : UserControl
    {
        /// <summary>
        /// The text of the log/diagnostic to display
        /// </summary>
        private string logText;

        /// <summary>
        /// Local log viewer URI
        /// </summary>
        private const string LOG_VIEW_LOCAL_URI = @"https://me3tweaks.com/modmanager/logservice/logviewer.html?manualLoad=true";

        /// <summary>
        /// URL path prefixes that should be fetched from the live site instead of the virtual host mapping.
        /// </summary>
        private static readonly string[] LIVE_SITE_URL_PREFIXES =
        {
            "/modmanager/services/thirdpartyidentificationservice",
            "/modmanager/mods/updatecheck"
        };

        /// <summary>
        /// Gets the file system path to the temporary directory used for log viewer assets.
        /// </summary>
        /// <returns>The full path to the temporary log viewer asset directory if it can be created; otherwise, null.</returns>
        private string GetLogViewerAssetPath()
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), @"ME3TweaksLogViewer");
                Directory.CreateDirectory(tempDir);
                return tempDir;
            }
            catch
            {
                // Couldn't extract. It's not going to work. Oh well.
                return null;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MLogViewerControl"/> class.
        /// Extracts embedded web assets and prepares the WebView2 control for displaying the log.
        /// </summary>
        /// <param name="logText">The log text to display in the viewer.</param>
        public MLogViewerControl(string logText)
        {
            var tempDir = GetLogViewerAssetPath();
            if (tempDir != null)
            {
                var zipStream = MUtilities.ExtractInternalFileToStream(@"ME3TweaksCoreWPF.LogViewer.Web.zip", Assembly.GetExecutingAssembly());
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    archive.ExtractToDirectory(tempDir, overwriteFiles: true);
                }
            }

            InitializeComponent();
            this.logText = logText;
            InitializeAsync();
        }

        /// <summary>
        /// Asynchronously initializes the WebView2 control, sets up virtual host mapping for local assets,
        /// and navigates to the log viewer HTML.
        /// </summary>
        async void InitializeAsync()
        {
            await webView.EnsureCoreWebView2Async(null);
            webView.NavigationStarting += CoreWebView2_NavigationStarting;
            webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

            // NOTE: no SetVirtualHostNameToFolderMapping anymore — we serve local files
            // ourselves in WebResourceRequested so we retain full control per-request.
            webView.CoreWebView2.AddWebResourceRequestedFilter("https://me3tweaks.com/*", CoreWebView2WebResourceContext.All);
            webView.CoreWebView2.AddWebResourceRequestedFilter("https://www.me3tweaks.com/*", CoreWebView2WebResourceContext.All);
            webView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

            webView.CoreWebView2.Navigate(LOG_VIEW_LOCAL_URI);
        }

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [@".html"] = @"text/html",
            [@".htm"] = @"text/html",
            [@".js"] = @"application/javascript",
            [@".css"] = @"text/css",
            [@".png"] = @"image/png",
            [@".jpg"] = @"image/jpeg",
            [@".svg"] = @"image/svg+xml",
            [@".json"] = @"application/json",
        };

        private static readonly HashSet<string> DisallowedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            @"Transfer-Encoding", @"Content-Encoding", @"Content-Length",
            @"Connection", @"Keep-Alive", @"Content-Security-Policy", @"X-Frame-Options"
        };

        private async void CoreWebView2_WebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var requestUri))
                return;

            bool isMe3TweaksHost = requestUri.Host.Equals(@"me3tweaks.com", StringComparison.OrdinalIgnoreCase)
                                 || requestUri.Host.Equals(@"www.me3tweaks.com", StringComparison.OrdinalIgnoreCase);
            if (!isMe3TweaksHost)
                return; // other domains (CDNs, etc.) pass straight through to the internet

            bool shouldProxyLive = LIVE_SITE_URL_PREFIXES.Any(prefix =>
                requestUri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            if (shouldProxyLive)
            {
                var deferral = e.GetDeferral();
                try
                {
                    using var httpReq = new HttpRequestMessage(new HttpMethod(e.Request.Method), requestUri);
                    using var response = await _http.SendAsync(httpReq);
                    var bytes = await response.Content.ReadAsByteArrayAsync();

                    var headerLines = string.Join("\n",
                        response.Headers.Concat(response.Content.Headers)
                            .Where(h => !DisallowedResponseHeaders.Contains(h.Key))
                            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
                    headerLines += "\nCache-Control: no-store"; // do not localize

                    e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                        new MemoryStream(bytes), (int)response.StatusCode, response.ReasonPhrase ?? "OK", headerLines);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($@"[WRR] live proxy failed: {ex}");
                    e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                        null, 502, @"Bad Gateway", @"Cache-Control: no-store");
                }
                finally
                {
                    deferral.Complete();
                }
                return;
            }

            // Otherwise, serve the local asset ourselves.
            var assetDir = GetLogViewerAssetPath();
            if (assetDir == null)
            {
                e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }

            var relativePath = requestUri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(assetDir, relativePath));

            // Guard against path traversal now that we've dropped the built-in
            // safety checks that SetVirtualHostNameToFolderMapping used to provide.
            if (!fullPath.StartsWith(Path.GetFullPath(assetDir), StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, @"Not Found", "");
                return;
            }

            var bytes2 = File.ReadAllBytes(fullPath);
            var contentType = MimeTypes.TryGetValue(Path.GetExtension(fullPath), out var ct) ? ct : @"application/octet-stream";
            e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                new MemoryStream(bytes2), 200, @"OK", $@"Content-Type: {contentType}");
        }

        /// <summary>
        /// Handles new window requests from the WebView2 control by opening the URL in the default browser
        /// instead of creating a new window within the WebView.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">Event arguments containing the requested URI.</param>
        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        /// <summary>
        /// Handles navigation events in the WebView2 control. When navigating to the log viewer page,
        /// it injects the log text as a base64-encoded string. For external URLs, it cancels navigation
        /// and opens the link in the default browser instead.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">Event arguments containing navigation information.</param>
        private async void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri == LOG_VIEW_LOCAL_URI)
            {
                await Task.Run(async () =>
                {
                    Thread.Sleep(1000);
                }).ContinueWithOnUIThread(async x =>
                {
                    var base64Log = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(logText));
                    var result = await webView.ExecuteScriptAsync($@"handleBase64Log(""{base64Log}"");");
                    Debug.WriteLine(result);
                });
            }
            else
            {
                e.Cancel = true;
                try
                {
                    Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
                }
                catch
                {
                }
            }
        }
    }
}