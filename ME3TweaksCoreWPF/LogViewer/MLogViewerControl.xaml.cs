using LegendaryExplorerCore.Helpers;
using ME3TweaksCore.Helpers;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
        /// injects jQuery into the page, and navigates to the log viewer HTML.
        /// </summary>
        async void InitializeAsync()
        {
            await webView.EnsureCoreWebView2Async(null);
            webView.NavigationStarting += CoreWebView2_NavigationStarting;
            webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            var assetDir = GetLogViewerAssetPath();
            if (assetDir != null)
            {
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(@"me3tweaks.com", GetLogViewerAssetPath(), CoreWebView2HostResourceAccessKind.Allow);
                string script = await File.ReadAllTextAsync(Path.Combine(assetDir, @"modmanager\logservice\shared\jquery-3.5.0.min.js"));
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
            }
         
            webView.CoreWebView2.Navigate(LOG_VIEW_LOCAL_URI);
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