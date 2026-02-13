using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using LegendaryExplorerCore.Compression;
using LegendaryExplorerCore.Helpers;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;

namespace ME3TweaksCore.Diagnostics
{
    public static class LogUploader
    {
        //12/14/2025
        // Is this used anymore? Was this for ALOT Installer? MER doesn't use it
        
        /// <summary>
        /// Uploads a log to the specified endpoint, using the lzma upload method. 
        /// The receiver must accept and return a link to the diagnostic, or an error reason which will be returned to the caller. 
        /// This method is synchronous and should not be run on a UI thread.
        /// </summary>
        /// <param name="logtext">The log text to upload</param>
        /// <param name="endpoint">The endpoint to upload to</param>
        /// <param name="attachments">Files to upload along with the log</param>
        /// <returns></returns>
        public static (bool uploaded, string result) UploadLog(string logtext, string endpoint, Dictionary<string, string> attachments = null)
        {
            var lzmalog = LZMA.CompressToLZMAFile(Encoding.UTF8.GetBytes(logtext));
            var lzmamd5 = MUtilities.CalculateHash(new MemoryStream(lzmalog));
            try
            {
                // examples of converting both Stream and byte [] to HttpContent objects
                // representing input type file
                HttpContent bytesContent = new ByteArrayContent(lzmalog);

                // Submit the form using HttpClient and 
                // create form data as Multipart (enctype="multipart/form-data")

                using var client = new HttpClient();
                using var formData = new MultipartFormDataContent();
                // <input type="file" name="file2" />
                formData.Add(new StringContent(MLibraryConsumer.GetAppVersion().ToString()), @"toolversion");
                formData.Add(new StringContent(MLibraryConsumer.GetHostingProcessname()), @"tool");
                formData.Add(new StringContent(lzmamd5), @"lzmamd5");
                formData.Add(bytesContent, @"lzmafile", @"lzmafile.lzma");

                if (attachments != null)
                {
                    foreach (var attachment in attachments)
                    {
                        var file = attachment.Key;
                        if (File.Exists(file) && file.Length < (3 * FileSize.MebiByte))
                        {
                            HttpContent fileContent = new ByteArrayContent(File.ReadAllBytes(file));
                            formData.Add(fileContent, attachment.Value);
                        }
                    }
                }

                // Invoke the request to the server

                // equivalent to pressing the submit button on
                // a form with attributes (action="{url}" method="post")
                var response = client.PostAsync(endpoint, formData).Result;

                // ensure the request was a success
                if (!response.IsSuccessStatusCode)
                {
                    return (false, LC.GetString(LC.string_errorUploadingLogResponse, response.StatusCode.ToString()));
                }
                var resultStream = response.Content.ReadAsStreamAsync().Result;
                var responseString = new StreamReader(resultStream).ReadToEnd();

                Uri uriResult;
                bool result = Uri.TryCreate(responseString, UriKind.Absolute, out uriResult)
                              && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
                if (result)
                {
                    //should be valid URL.
                    //diagnosticsWorker.ReportProgress(0, new ThreadCommand(SET_DIAGTASK_ICON_GREEN, Image_Upload));
                    //e.Result = responseString;
                    MLog.Information(@"Result from server for log upload: " + responseString);
                    return (true, responseString);
                }
                MLog.Error(@"Error uploading log. The server responded with: " + responseString);
                return (false, LC.GetString(LC.string_interp_serverRejectedLogUpload, responseString));
            }
            catch (Exception ex)
            {
                // ex.Message contains rich details, including the URL, verb, response status,
                // and request and response bodies (if available)
                MLog.Exception(ex, @"Handled error uploading log");
                return (false, LC.GetString(LC.string_interp_errorUploadingLog, ex.Message));
            }
        }
    }
}
