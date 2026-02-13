using LegendaryExplorerCore.Helpers;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using Microsoft.Win32;
using NickStrupat;
using System;
using System.Globalization;
using System.Management;
using System.Text;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Diagnostic module for collecting system information.
    /// </summary>
    internal class DiagSystemInfo : DiagModuleBase
    {
        internal override void RunModule(LogUploadPackage package)
        {
            var diag = package.DiagnosticWriter;

            MLog.Information(@"Collecting system information");
            var computerInfo = new ComputerInfo();


            package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingSystemInformation));

            diag.AddDiagLine(@"System information", LogSeverity.DIAGSECTION);
            OperatingSystem os = Environment.OSVersion;
            Version osBuildVersion = os.Version;

            //Windows 10 only
            string verLine = computerInfo.OSFullName;


            // Todo: Implement OS via new M3SupportedOS
            if (os.Version < ME3TweaksCoreLib.MIN_SUPPORTED_OS)
            {
                diag.AddDiagLine(@"This operating system is not supported", LogSeverity.FATAL);
                diag.AddDiagLine(@"Upgrade to a supported operating system if you want support", LogSeverity.FATAL);
            }
            else if (!computerInfo.ActuallyPlatform)
            {
                // Is this actually Windows? Probably not
                diag.AddDiagLine(@"This software environment is not supported", LogSeverity.FATAL);
            }
            // End supported OS change req

            diag.AddDiagLine(verLine, os.Version < ME3TweaksCoreLib.MIN_SUPPORTED_OS ? LogSeverity.ERROR : LogSeverity.INFO);
            if (!WineWorkarounds.WineDetected)
            {
                diag.AddDiagLine($@"Version " + osBuildVersion, os.Version < ME3TweaksCoreLib.MIN_SUPPORTED_OS ? LogSeverity.ERROR : LogSeverity.INFO);
            } else
            {
                diag.AddDiagLine(@"Host OS version: " + computerInfo.OSVersion);
                diag.AddDiagLine($@"Windows Version reported by Wine: " + osBuildVersion, os.Version < ME3TweaksCoreLib.MIN_SUPPORTED_OS ? LogSeverity.ERROR : LogSeverity.INFO);

            }
                diag.AddDiagLine($@"System culture: {CultureInfo.InstalledUICulture.Name}");

            diag.AddDiagLine();
            MLog.Information(@"Collecting memory information");

            diag.AddDiagLine(@"System Memory", LogSeverity.BOLD);
            long ramInBytes = (long)computerInfo.TotalPhysicalMemory; // Should work on Linux.
            diag.AddDiagLine($@"Total memory available: {FileSize.FormatSize(ramInBytes)}");
            if (!WineWorkarounds.WineDetected)
            {
                var memSpeed = computerInfo.MemorySpeed;
                if (memSpeed > 0)
                {
                    diag.AddDiagLine($@"Memory speed: {memSpeed}Mhz");
                }
                else
                {
                    diag.AddDiagLine($@"Could not get memory speed", LogSeverity.WARN);
                }
            }

            diag.AddDiagLine(@"Processors", LogSeverity.BOLD);
            MLog.Information(@"Collecting processor information");

            // Windows
            diag.AddDiagLine(GetProcessorInformationForDiag());

            if (ramInBytes == 0)
            {
                diag.AddDiagLine(@"Unable to get the read amount of physically installed ram.", LogSeverity.WARN);
            }

            if (!WineWorkarounds.WineDetected)
            {
                MLog.Information(@"Collecting video card information");
                // Enumerate the Display Adapters registry key
                int vidCardIndex = 1;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (key != null)
                    {
                        var subNames = key.GetSubKeyNames();
                        foreach (string adapterRegistryIndex in subNames)
                        {
                            if (!int.TryParse(adapterRegistryIndex, out _)) continue; // Only go into numerical ones
                            try
                            {
                                var videoKey = key.OpenSubKey(adapterRegistryIndex);

                                // Get memory. If memory is not populated then this is not active right now (I think)
                                long vidCardSizeInBytes = (long)videoKey.GetValue(@"HardwareInformation.qwMemorySize", -1L);
                                ulong vidCardSizeInBytesIntegrated = 1uL;
                                if (vidCardSizeInBytes == -1)
                                {
                                    var memSize = videoKey.GetValue(@"HardwareInformation.MemorySize", 1uL); // We use 1 because 2-4GB range is realistic. But a 1 byte video card?
                                    if (memSize is byte[] whyWouldYouPutThisInBytes)
                                    {
                                        vidCardSizeInBytesIntegrated = BitConverter.ToUInt32(whyWouldYouPutThisInBytes);
                                    }
                                    else if (memSize is long l)
                                    {
                                        vidCardSizeInBytesIntegrated = (ulong)l;
                                    }
                                }
                                if (vidCardSizeInBytes == -1 && vidCardSizeInBytesIntegrated == 1) continue; // Not defined

                                string vidCardName = @"Unknown name";

                                // Try 1: Use DriverDesc
                                var vidCardNameReg = videoKey.GetValue(@"DriverDesc");
                                if (vidCardNameReg is string str)
                                {
                                    vidCardName = str;
                                }
                                else
                                {
                                    vidCardNameReg = null; // ensure null for flow control below
                                }

                                if (vidCardNameReg == null)
                                {
                                    // Try 2: Read AdapterString
                                    vidCardNameReg = videoKey.GetValue(@"HardwareInformation.AdapterString",
                                        @"Unable to get adapter name");
                                    if (vidCardNameReg is byte[] bytes)
                                    {
                                        // AMD Radeon 6700XT on eGPU writes REG_BINARY for some reason
                                        vidCardName =
                                            Encoding.Unicode.GetString(bytes)
                                                .Trim(); // During upload step we have to strip \0 or it'll break the log viewer due to how lzma-js works
                                    }
                                    else if (vidCardNameReg is string str2)
                                    {
                                        vidCardName = str2;
                                    }
                                }

                                string vidDriverVersion = (string)videoKey.GetValue(@"DriverVersion", @"Unable to get driver version");
                                string vidDriverDate = (string)videoKey.GetValue(@"DriverDate", @"Unable to get driver date");

                                diag.AddDiagLine();
                                diag.AddDiagLine($@"Video Card {(vidCardIndex++)}", LogSeverity.BOLD);
                                diag.AddDiagLine($@"Name: {vidCardName}");
                                if (vidCardSizeInBytesIntegrated == 1 && vidCardSizeInBytes == -1)
                                {
                                    diag.AddDiagLine($@"Memory: (System Shared)");
                                }
                                else if (vidCardSizeInBytes > 0)
                                {
                                    diag.AddDiagLine($@"Memory: {FileSize.FormatSize(vidCardSizeInBytes)}");
                                }
                                else
                                {
                                    diag.AddDiagLine($@"Memory: {FileSize.FormatSize(vidCardSizeInBytesIntegrated)}");
                                }
                                diag.AddDiagLine($@"Driver Version: {vidDriverVersion}");
                                diag.AddDiagLine($@"Driver Date: {vidDriverDate}");
                            }
                            catch (Exception ex)
                            {
                                diag.AddDiagLine($@"Error getting video card information: {ex.Message}", LogSeverity.WARN);
                            }
                        }
                    }
                }

                // Antivirus
                var avs = MUtilities.GetListOfInstalledAV();
                diag.AddDiagLine(@"Antivirus products", LogSeverity.BOLD);
                diag.AddDiagLine(@"The following antivirus products were detected:");
                foreach (var av in avs)
                {
                    diag.AddDiagLine($@"- {av}");
                }
            }
        }


        /// <summary>
        /// Fetches processor information for diagnostic logging.
        /// </summary>
        /// <returns></returns>
        public static string GetProcessorInformationForDiag()
        {
            if (WineWorkarounds.WineDetected)
            {
                return GetProcessorInformationForDiagWine();
            }
            else
            {
                return GetProcessorInformationForDiagWindows();
            }
        }

        private static string GetProcessorInformationForDiagWine()
        {
            return new ComputerInfo().CPUName;
        }

#pragma warning disable CA1416 // Validate platform compatibility
        private static string GetProcessorInformationForDiagWindows()
        {
            string str = "";
            try
            {
                ManagementObjectSearcher mosProcessor = new ManagementObjectSearcher(@"SELECT * FROM Win32_Processor");

                foreach (ManagementObject moProcessor in mosProcessor.Get())
                {
                    if (str != "")
                    {
                        str += "\n"; //do not localize
                    }

                    if (moProcessor[@"name"] != null)
                    {
                        str += moProcessor[@"name"].ToString();
                        str += "\n"; //do not localize
                    }
                    if (moProcessor[@"maxclockspeed"] != null)
                    {
                        str += @"Maximum reported clock speed: ";
                        str += moProcessor[@"maxclockspeed"].ToString();
                        str += " Mhz\n"; //do not localize
                    }
                    if (moProcessor[@"numberofcores"] != null)
                    {
                        str += @"Cores: ";

                        str += moProcessor[@"numberofcores"].ToString();
                        str += "\n"; //do not localize
                    }
                    if (moProcessor[@"numberoflogicalprocessors"] != null)
                    {
                        str += @"Logical processors: ";
                        str += moProcessor[@"numberoflogicalprocessors"].ToString();
                        str += "\n"; //do not localize
                    }

                }
                return str
                   // 01/30/2026 - Remove these as 
                   // it breaks output in logs
                   //.Replace(@"(TM)", @"™")
                   //.Replace(@"(tm)", @"™")
                   //.Replace(@"(R)", @"®")
                   //.Replace(@"(r)", @"®")
                   //.Replace(@"(C)", @"©")
                   //.Replace(@"(c)", @"©")
                   .Replace(@"    ", @" ")
                   .Replace(@"  ", @" ").Trim();
            }
            catch (Exception e)
            {
                MLog.Error($@"Error getting processor information: {e.Message}");
                return $"Error getting processor information: {e.Message}\n"; //do not localize
            }
        }
#pragma warning restore CA1416 // Validate platform compatibility
    }
}