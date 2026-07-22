using AuthenticodeExaminer;
using ME3TweaksCore.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace ME3TweaksCore.Helpers
{
    /// <summary>
    /// Has utility methods for getting build date and signer
    /// </summary>
    public static class BuildHelper
    {
        /// <summary>
        /// Defines an allowed build signer
        /// </summary>
        public class BuildSigner
        {
            /// <summary>
            /// The name on the signature to verify against
            /// </summary>
            public string SigningName { get; set; }

            /// <summary>
            /// The name to use when referencing the signer
            /// </summary>
            public string DisplayName { get; set; }
        }

        public static DateTime BuildDate { get; internal set; }
        public static string BuildDateString { get; internal set; }
        /// <summary>
        /// If the hosting application is signed
        /// </summary>
        public static bool IsSigned { get; internal set; }

        private static bool HasReadBuildInfo { get; set; }

        public static void ReadRuildInfo(BuildSigner[] allowedSigners = null)
        {
            if (HasReadBuildInfo)
                return;

            var info = new FileInspector(MLibraryConsumer.GetExecutablePath());
            var signTime = info.GetSignatures().FirstOrDefault()?.TimestampSignatures.FirstOrDefault()?.TimestampDateTime?.UtcDateTime;

            if (signTime != null)
            {
                // This executable is signed
                BuildDate = signTime.Value;
                BuildDateString = signTime.Value.ToLocalTime().ToString(@"MMMM dd, yyyy @ hh:mmtt");
                var signer = info.GetSignatures().FirstOrDefault()?.SigningCertificate?.GetNameInfo(X509NameType.SimpleName, false);
                if (allowedSigners != null && allowedSigners.Any())
                {
                    if (signer != null && allowedSigners.FirstOrDefault(x=>x.SigningName == signer) != null)
                    {
                        IsSigned = true;
                        MLog.Information($@"Build signed by {allowedSigners.FirstOrDefault(x=>x.SigningName == signer).DisplayName}. Build date: " + BuildDate);
                    }
                    else
                    {
                        MLog.Error($@"Build signed, but not by an authorized signer ({signer})!");
                    }
                }
                else
                {
                    MLog.Warning($@"This build is signed by {signer} - however, no signing names were provided for validation");
                }
            }
            else
            {
                BuildDateString = @"WARNING: This build is not signed by ME3Tweaks";
                var assembly = Assembly.GetEntryAssembly();
                var metadataAttribute = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == @"BuildDateTime");

                if (metadataAttribute != null && DateTime.TryParse(metadataAttribute.Value, out DateTime buildDate))
                {
                    var buildDateStr = buildDate.ToLocalTime().ToString(@"MMMM dd, yyyy @ hh:mmtt");
                    MLog.Information($@"Build date: {buildDateStr}");
                    BuildDateString += $@" | {buildDateStr}";
                }
#if !DEBUG
                MLog.Warning(@"This build is not signed by ME3Tweaks. This may not be an official build.");
#endif
            }
        }
    }
}
