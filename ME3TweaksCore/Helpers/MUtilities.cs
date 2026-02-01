using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Gammtek.Collections.Generic;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Misc;
using ME3TweaksCore.Objects;
using Microsoft.Win32;
using NickStrupat;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using ThreadState = System.Diagnostics.ThreadState;

namespace ME3TweaksCore.Helpers
{
    /// <summary>
    /// Utility methods for ME3TweaksCore
    /// </summary>
    public static class MUtilities
    {

        //Step 1: https://stackoverflow.com/questions/2435894/net-how-do-i-check-for-illegal-characters-in-a-path
        private static string RemoveSpecialCharactersUsingCustomMethod(this string expression, bool removeSpecialLettersHavingASign = true, bool allowPeriod = false)
        {
            var newCharacterWithSpace = @" ";
            var newCharacter = "";

            // Return carriage handling
            // ASCII LINE-FEED character (LF),
            expression = expression.Replace("\n", newCharacterWithSpace); // do not localize
            // ASCII CARRIAGE-RETURN character (CR)
            expression = expression.Replace("\r", newCharacterWithSpace); // do not localize

            // less than : used to redirect input, allowed in Unix filenames, see Note 1
            expression = expression.Replace(@"<", newCharacter);
            // greater than : used to redirect output, allowed in Unix filenames, see Note 1
            expression = expression.Replace(@">", newCharacter);
            // colon: used to determine the mount point / drive on Windows;
            // used to determine the virtual device or physical device such as a drive on AmigaOS, RT-11 and VMS;
            // used as a pathname separator in classic Mac OS. Doubled after a name on VMS,
            // indicates the DECnet nodename (equivalent to a NetBIOS (Windows networking) hostname preceded by "\\".).
            // Colon is also used in Windows to separate an alternative data stream from the main file.
            expression = expression.Replace(@":", newCharacter);
            // quote : used to mark beginning and end of filenames containing spaces in Windows, see Note 1
            expression = expression.Replace(@"""", newCharacter);
            // slash : used as a path name component separator in Unix-like, Windows, and Amiga systems.
            // (The MS-DOS command.com shell would consume it as a switch character, but Windows itself always accepts it as a separator.[16][vague])
            expression = expression.Replace(@"/", newCharacter);
            // backslash : Also used as a path name component separator in MS-DOS, OS/2 and Windows (where there are few differences between slash and backslash); allowed in Unix filenames, see Note 1
            expression = expression.Replace(@"\", newCharacter);
            // vertical bar or pipe : designates software pipelining in Unix and Windows; allowed in Unix filenames, see Note 1
            expression = expression.Replace(@"|", newCharacter);
            // question mark : used as a wildcard in Unix, Windows and AmigaOS; marks a single character. Allowed in Unix filenames, see Note 1
            expression = expression.Replace(@"?", newCharacter);
            expression = expression.Replace(@"!", newCharacter);
            // asterisk or star : used as a wildcard in Unix, MS-DOS, RT-11, VMS and Windows. Marks any sequence of characters
            // (Unix, Windows, later versions of MS-DOS) or any sequence of characters in either the basename or extension
            // (thus "*.*" in early versions of MS-DOS means "all files". Allowed in Unix filenames, see note 1
            expression = expression.Replace(@"*", newCharacter);
            // percent : used as a wildcard in RT-11; marks a single character.
            expression = expression.Replace(@"%", newCharacter);
            // period or dot : allowed but the last occurrence will be interpreted to be the extension separator in VMS, MS-DOS and Windows.
            // In other OSes, usually considered as part of the filename, and more than one period (full stop) may be allowed.
            // In Unix, a leading period means the file or folder is normally hidden.
            if (!allowPeriod)
            {
                expression = expression.Replace(@".", newCharacter);
            }

            // space : allowed (apart MS-DOS) but the space is also used as a parameter separator in command line applications.
            // This can be solved by quoting, but typing quotes around the name every time is inconvenient.
            //expression = expression.Replace(@"%", " ");
            expression = expression.Replace(@"  ", newCharacter);

            if (removeSpecialLettersHavingASign)
            {
                // Because then issues to zip
                // More at : http://www.thesauruslex.com/typo/eng/enghtml.htm
                expression = expression.Replace(@"ê", @"e");
                expression = expression.Replace(@"ë", @"e");
                expression = expression.Replace(@"ï", @"i");
                expression = expression.Replace(@"œ", @"oe");
            }

            return expression;
        }


        // Step 2: https://stackoverflow.com/questions/2435894/net-how-do-i-check-for-illegal-characters-in-a-path
        public static bool ContainsAnyInvalidPathCharacters(this string path)
        {
            return (!string.IsNullOrEmpty(path) && path.IndexOfAny(Path.GetInvalidPathChars()) >= 0);
        }

        //Step 3: https://stackoverflow.com/questions/2435894/net-how-do-i-check-for-illegal-characters-in-a-path
        public static string RemoveSpecialPathCharactersUsingFrameworkMethod(this string path)
        {
            return Path.GetInvalidFileNameChars().Aggregate(path, (current, c) => current.Replace(c.ToString(), string.Empty));
        }

        /// <summary>
        /// Sanitizes a path by removing disallowed characters
        /// </summary>
        /// <param name="path">Path string</param>
        /// <returns>Sanitized path string</returns>
        public static string SanitizePath(string path, bool allowPeriod = false)
        {
            path = path.RemoveSpecialCharactersUsingCustomMethod(allowPeriod: allowPeriod);
            if (path.ContainsAnyInvalidPathCharacters())
            {
                path = path.RemoveSpecialPathCharactersUsingFrameworkMethod();
            }

            return path;
        }


        // From https://stackoverflow.com/a/40361205
        /// <summary>
        /// Gets a filename from a URL. Returns null if the URI cannot be parsed.
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static string GetFileNameFromUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return null; // Cannot get filename!
            }

            return Path.GetFileName(uri.LocalPath);
        }

        /// <summary>
        /// Calculates the MD5 of the specified file on disk
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="algorithm">Algorithm to use. Use null for MD5.</param>
        /// <param name="progressDelegate">Delegate to invoke during progress. Will slow hashing a bit.</param>
        /// <returns></returns>
        public static string CalculateHash(string filename, string algorithm = null, Action<ProgressInfo> progressDelegate = null)
        {
            try
            {
                using var stream = File.OpenRead(filename);
                return CalculateHash(stream, algorithm, progressDelegate: progressDelegate);
            }
            catch (IOException e)
            {
                MLog.Error($@"I/O ERROR CALCULATING {(algorithm == null ? @"md5" : algorithm.GetType())} OF FILE: " + filename); // do not localize
                MLog.Error(e);
                return "";
            }
        }

        /// <summary>
        /// Calculates the hash of the array.
        /// </summary>
        /// <param name="data">Data to hash</param>
        /// <param name="algorithm">Algorithm to use. Use null for MD5.</param>
        /// <returns></returns>
        public static string CalculateHash(byte[] data, string algorithm = null)
        {
            return CalculateHash(new MemoryStream(data), algorithm);
        }

        /// <summary>
        /// Calculates the hash of the stream from the beginning. Resets the stream to the beginning at the end.
        /// </summary>
        /// <param name="stream">Stream to hash</param>
        /// <param name="algorithm">Algorithm to use. Use null for MD5.</param>
        /// <param name="byteLenToHash">How many bytes to hash. If 0, hash everything in the stream</param>
        /// <param name="percentComplete">Delegate that is invoked with progress. This uses chunked hashing and may be a bit slower.</param>
        /// <returns></returns>
        public static string CalculateHash(Stream stream, string algorithm = null, long byteLenToHash = 0, Action<ProgressInfo> progressDelegate = null)
        {
            HashAlgorithm algo = null;

            try
            {
                switch (algorithm)
                {
                    case @"sha":
                    case @"sha1":
                        algo = SHA1.Create();
                        break;
                    case @"sha256":
                        algo = SHA256.Create();
                        break;
                    default:
                        algo = MD5.Create();
                        break;
                }

                stream.Position = 0;
                Stream streamToHash = byteLenToHash > 0 ? new StreamReadLimitLengthWrapper(stream, byteLenToHash) : stream;
                var hash = progressDelegate != null ? computeHashChunked(streamToHash, algo, progressDelegate) : algo.ComputeHash(streamToHash);
                stream.Position = 0; // reset stream
                return BitConverter.ToString(hash).Replace(@"-", "").ToLowerInvariant();
            }
            catch (Exception e)
            {
                MLog.Error($@"I/O ERROR CALCULATING {algo.GetType()} OF STREAM");
                MLog.Error(e);
                return "";
            }
            finally
            {
                algo.Dispose();
            }
        }

        /// <summary>
        /// Comutes hash in 4KB chunks and reports progress
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="algo"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        private static byte[] computeHashChunked(Stream inputStream, HashAlgorithm algo, Action<ProgressInfo> progressDelegate)
        {
            // Get the total size of the stream for calculating the percentage.
            long totalBytes = inputStream.Length;
            long processedBytes = 0;

            byte[] buffer = new byte[4096]; // Read in chunks of 4KB.
            int bytesRead;

            ProgressInfo pi = new ProgressInfo();
            pi.Indeterminate = false;
            while ((bytesRead = inputStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                // Feed the chunk into the hash algorithm.
                algo.TransformBlock(buffer, 0, bytesRead, null, 0);

                // Update the number of processed bytes and report progress.
                processedBytes += bytesRead;
                if (totalBytes > 0)
                {
                    pi.Value = (double)processedBytes / totalBytes * 100;
                    progressDelegate(pi);
                }
            }

            // Finalize the hash computation.
            algo.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            pi.Value = 100;
            progressDelegate(pi);

            return algo.Hash;
        }

        internal static List<string> GetListOfInstalledAV()
        {
            if (WineWorkarounds.WineDetected)
            {
                // No AV on Wine
                return new List<string>();
            }

            List<string> av = new List<string>();
            // for Windows Vista and above '\root\SecurityCenter2'
            using (var searcher = new ManagementObjectSearcher(@"\\" +
                                                               Environment.MachineName +
                                                               @"\root\SecurityCenter2",
                @"SELECT * FROM AntivirusProduct"))
            {
                var searcherInstance = searcher.Get();
                foreach (var instance in searcherInstance)
                {
                    av.Add(instance[@"displayName"].ToString());
                }
            }

            return av;
        }

        public static Stream GetResourceStream(string assemblyResource, Assembly assembly = null)
        {
            assembly ??= Assembly.GetExecutingAssembly();
#if DEBUG
            // For debugging
            var res = assembly.GetManifestResourceNames(); // Do not remove. Makes debugger way more useful.
            var asset = assembly.GetManifestResourceStream(assemblyResource);
            if (asset == null)
                Debugger.Break();
            return asset;
#endif
            return assembly.GetManifestResourceStream(assemblyResource);
        }

        public static MemoryStream ExtractInternalFileToStream(string internalResourceName, Assembly assembly = null)
        {
            MLog.Information($@"Extracting embedded file: {internalResourceName} to memory");
            assembly ??= Assembly.GetExecutingAssembly();
#if DEBUG
            // This is for inspecting the list of files in debugger
            var resources = assembly.GetManifestResourceNames();
#endif
            using (Stream stream = GetResourceStream(internalResourceName, assembly))
            {
#if AZURE
                if (stream == null)
                {
                    throw new Exception($@"Failed to find internal resource stream: {internalResourceName}");
                }
#endif
                MemoryStream ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }
        }

        /// <summary>
        /// Tests if an embedded resource with the specified name exists.
        /// </summary>
        /// <param name="internalResourceName"></param>
        /// <returns></returns>
        internal static bool DoesEmbeddedAssetExist(string internalResourceName)
        {
            var resources = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            return resources.Any(x => x == internalResourceName);
        }


        internal static void ExtractInternalFile(string internalResourceName, string destination, bool overwrite)
        {
            if (!File.Exists(destination) || overwrite || new FileInfo(destination).Length == 0)
            {
                if (File.Exists(destination))
                {
                    FileInfo fi = new FileInfo(destination);
                    if (fi.IsReadOnly)
                    {
                        fi.IsReadOnly = false; //clear read only. might happen on some binkw32 in archives, maybe
                    }
                }

                MLog.Information($@"Writing internal asset to disk: {internalResourceName} -> {destination}");
                var resource = ExtractInternalFileToStream(internalResourceName);
                resource.WriteToFile(destination);
            }
            else
            {
                //MLog.Warning("File already exists. Not overwriting file.");
            }

        }

        private static (bool isRunning, DateTime lastChecked) le1RunningInfo = (false, DateTime.MinValue.AddSeconds(5));
        private static (bool isRunning, DateTime lastChecked) le2RunningInfo = (false, DateTime.MinValue.AddSeconds(5));
        private static (bool isRunning, DateTime lastChecked) le3RunningInfo = (false, DateTime.MinValue.AddSeconds(5));
        private static (bool isRunning, DateTime lastChecked) me1RunningInfo = (false, DateTime.MinValue.AddSeconds(5));
        private static (bool isRunning, DateTime lastChecked) me2RunningInfo = (false, DateTime.MinValue.AddSeconds(5));
        private static (bool isRunning, DateTime lastChecked) me3RunningInfo = (false, DateTime.MinValue.AddSeconds(5));
        private static (bool isRunning, DateTime lastChecked) leLauncherRunningInfo = (false, DateTime.MinValue.AddSeconds(5));


        private static int TIME_BETWEEN_PROCESS_CHECKS = 5;

        private static object gameRunningSyncObjME1 = new object();
        private static object gameRunningSyncObjME2 = new object();
        private static object gameRunningSyncObjME3 = new object();
        private static object gameRunningSyncObjLE1 = new object();
        private static object gameRunningSyncObjLE2 = new object();
        private static object gameRunningSyncObjLE3 = new object();
        private static object gameRunningSyncObjLEL = new object();

        /// <summary>
        /// Returns the object used to enforce concurrency when checking if a game is running
        /// </summary>
        /// <param name="game"></param>
        /// <returns></returns>
        private static object GetSyncObjForGameRunning(MEGame game) => game switch
        {
            MEGame.ME1 => gameRunningSyncObjME1,
            MEGame.ME2 => gameRunningSyncObjME2,
            MEGame.ME3 => gameRunningSyncObjME3,
            MEGame.LE1 => gameRunningSyncObjLE1,
            MEGame.LE2 => gameRunningSyncObjLE2,
            MEGame.LE3 => gameRunningSyncObjLE3,
            MEGame.LELauncher => gameRunningSyncObjLEL,
        };

        /// <summary>
        /// Determines if a specific game is running. This method only updates every 3 seconds due to the huge overhead it has
        /// </summary>
        /// <returns>True if running, false otherwise</returns>
        public static bool IsGameRunning(MEGame gameID, bool forceCheckNow = false)
        {
            (bool isRunning, DateTime lastChecked) runningInfo = (false, DateTime.MinValue);
            lock (GetSyncObjForGameRunning(gameID))
            {
                switch (gameID)
                {
                    case MEGame.ME1:
                        runningInfo = me1RunningInfo;
                        break;
                    case MEGame.LE1:
                        runningInfo = le1RunningInfo;
                        break;
                    case MEGame.LE2:
                        runningInfo = le2RunningInfo;
                        break;
                    case MEGame.ME2:
                        runningInfo = me2RunningInfo;
                        break;
                    case MEGame.LE3:
                        runningInfo = le3RunningInfo;
                        break;
                    case MEGame.ME3:
                        runningInfo = me3RunningInfo;
                        break;
                    case MEGame.LELauncher:
                        runningInfo = leLauncherRunningInfo;
                        break;
                }

                var time = runningInfo.lastChecked.AddSeconds(TIME_BETWEEN_PROCESS_CHECKS);
                //Debug.WriteLine(time + " vs " + DateTime.Now);
                if (!forceCheckNow && time > DateTime.Now)
                {
                    //Debug.WriteLine("CACHED");
                    return runningInfo.isRunning; //cached
                }
            }

            Debug.WriteLine($@"{DateTime.Now} IsGameRunning({gameID}) - stale info, refreshing");

            //Debug.WriteLine("IsRunning: " + gameID);

            var processNames = MEDirectories.ExecutableNames(gameID).Select(Path.GetFileNameWithoutExtension);
            try
            {
                // This is in a try catch, as things in MainModule will throw an error if accessed
                // after the process ends, which it might during the periodic updates of this
                runningInfo.isRunning = Process.GetProcesses().Any(x => processNames.Contains(x.ProcessName) && !IsProcessSuspended(x) &&
                                                                        x.MainModule?.FileVersionInfo.FileMajorPart == (gameID.IsOTGame() ? 1 : 2));
            }
            catch
            {
                // don't really care
            }

            runningInfo.lastChecked = DateTime.Now;
            switch (gameID)
            {
                case MEGame.ME1:
                    me1RunningInfo = runningInfo;
                    break;
                case MEGame.LE1:
                    le1RunningInfo = runningInfo;
                    break;
                case MEGame.ME2:
                    me2RunningInfo = runningInfo;
                    break;
                case MEGame.LE2:
                    le2RunningInfo = runningInfo;
                    break;
                case MEGame.ME3:
                    me3RunningInfo = runningInfo;
                    break;
                case MEGame.LE3:
                    le3RunningInfo = runningInfo;
                    break;
                case MEGame.LELauncher:
                    leLauncherRunningInfo = runningInfo;
                    break;
            }

            return runningInfo.isRunning;
        }

        private static bool IsProcessSuspended(Process proc)
        {
#if DEBUG
            if (proc.Threads.Count == 0) return true; // App is in some weird broken state. I see this in dev machine all the time, requires a restart to fix.
            var isWaiting = proc.Threads[0].ThreadState == ThreadState.Wait;
            if (isWaiting)
            {
                return proc.Threads[0].WaitReason == ThreadWaitReason.Suspended;
            }

            return isWaiting;
#endif
            return proc.Threads[0].ThreadState == ThreadState.Wait;
        }

        /// <summary>
        /// Checks if a process is running. This should not be used for game detection, as it also uses version info.
        /// </summary>
        /// <param name="processName"></param>
        /// <returns></returns>
        public static bool IsProcessRunning(string processName)
        {
            return Process.GetProcesses().Any(x => x.ProcessName.Equals(processName, StringComparison.InvariantCultureIgnoreCase));
        }


        /// <summary>
        /// Deletes the contents of the specified folder, as well as the directory itself unless deleteDirectoryItself = false
        /// </summary>
        /// <param name="targetDirectory"></param>
        /// <param name="throwOnFailed"></param>
        /// <returns></returns>
        public static bool DeleteFilesAndFoldersRecursively(string targetDirectory, bool throwOnFailed = false, bool deleteDirectoryItself = true)
        {
            if (!Directory.Exists(targetDirectory))
            {
                Debug.WriteLine(@"Directory to delete doesn't exist: " + targetDirectory);
                return true;
            }

            bool result = true;
            foreach (string file in Directory.GetFiles(targetDirectory))
            {
                File.SetAttributes(file, FileAttributes.Normal); //remove read only
                try
                {
                    //Debug.WriteLine("Deleting file: " + file);
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    MLog.Error($@"Unable to delete file: {file}. It may be open still: {e.Message}");
                    if (throwOnFailed)
                    {
                        throw;
                    }

                    return false;
                }
            }

            foreach (string subDir in Directory.GetDirectories(targetDirectory))
            {
                result &= DeleteFilesAndFoldersRecursively(subDir, throwOnFailed, true);
            }

            if (deleteDirectoryItself)
            {
                Thread.Sleep(10); // This makes the difference between whether it works or not. Sleep(0) is not enough.
                try
                {
                    Directory.Delete(targetDirectory);
                }
                catch (Exception e)
                {
                    MLog.Error($@"Unable to delete directory: {targetDirectory}. It may be open still or may not be actually empty: {e.Message}");
                    if (throwOnFailed)
                    {
                        throw;
                    }

                    return false;
                }
            }

            return result;
        }

        /// <summary>
        /// Recursively deletes all empty subdirectories.
        /// </summary>
        /// <param name="startLocation"></param>
        public static void DeleteEmptySubdirectories(string startLocation)
        {
            foreach (var directory in Directory.GetDirectories(startLocation))
            {
                DeleteEmptySubdirectories(directory);
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    MLog.Information(@"Deleting empty directory: " + directory);
                    Directory.Delete(directory, false);
                }
            }
        }

        /// <summary> Checks for write access for the given file.
        /// </summary>
        /// <param name="fileName">The filename.</param>
        /// <returns>true, if write access is allowed, otherwise false</returns>
        public static bool IsDirectoryWritable(string dir)
        {
            try
            {
                System.IO.File.Create(Path.Combine(dir, @"temp_m3.txt")).Close();
                System.IO.File.Delete(Path.Combine(dir, @"temp_m3.txt"));
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (Exception e)
            {
                MLog.Error(@"Error checking permissions to folder: " + dir);
                MLog.Error(@"Directory write test had error that was not UnauthorizedAccess: " + e.Message);
            }

            return false;
        }

        /// <summary>
        /// Reads all lines from a file, attempting to do so even if the file is in use by another process
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string[] WriteSafeReadAllLines(String path)
        {
            using var csv = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(csv);
            List<string> file = new List<string>();
            while (!sr.EndOfStream)
            {
                file.Add(sr.ReadLine());
            }

            return file.ToArray();
        }

        public static MEGame GetGameFromNumber(string gameNum)
        {
            return GetGameFromNumber(int.Parse(gameNum));
        }

        /// <summary>
        /// Converts server game ID to Enum
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        public static MEGame GetGameFromNumber(int number) => number switch
        {
            1 => MEGame.ME1,
            2 => MEGame.ME2,
            3 => MEGame.ME3,
            4 => MEGame.LE1,
            5 => MEGame.LE2,
            6 => MEGame.LE3,
            7 => MEGame.LELauncher,
            _ => MEGame.Unknown
        };

        /// <summary>
        /// Gets an integer percent based on the progress to a maximum. The progress value is rounded to the nearest integer.
        /// </summary>
        /// <param name="downloaded"></param>
        /// <param name="total"></param>
        /// <returns></returns>
        public static int GetPercent(long downloaded, long total)
        {
            return (int)Math.Round(downloaded * 100.0 / total);
        }

        public static long GetSizeOfDirectory(string d, string[] extensionsToCalculate = null)
        {
            return GetSizeOfDirectory(new DirectoryInfo(d), extensionsToCalculate);
        }

        public static long GetSizeOfDirectory(DirectoryInfo d, string[] extensionsToCalculate = null)
        {
            long size = 0;
            // Add file sizes.
            FileInfo[] fis = d.GetFiles();
            foreach (FileInfo fi in fis)
            {
                if (extensionsToCalculate != null)
                {
                    if (extensionsToCalculate.Contains(Path.GetExtension(fi.Name)))
                    {
                        size += fi.Length;
                    }
                }
                else
                {
                    size += fi.Length;
                }
            }

            // Add subdirectory sizes.
            DirectoryInfo[] dis = d.GetDirectories();
            foreach (DirectoryInfo di in dis)
            {
                size += GetSizeOfDirectory(di, extensionsToCalculate);
            }

            return size;
        }

        public static bool IsSubfolder(string parentPath, string childPath)
        {
            var parentUri = new Uri(parentPath);
            var childUri = new DirectoryInfo(childPath).Parent;
            while (childUri != null)
            {
                if (new Uri(childUri.FullName) == parentUri)
                {
                    return true;
                }

                childUri = childUri.Parent;
            }

            return false;
        }

        public static bool CreateDirectoryWithWritePermission(string directoryPath, bool forcePermissions = false)
        {
            if (!forcePermissions && Directory.Exists(Directory.GetParent(directoryPath).FullName) && MUtilities.IsDirectoryWritable(Directory.GetParent(directoryPath).FullName))
            {
                Directory.CreateDirectory(directoryPath);
                return true;
            }

            try
            {
                //try first without admin.
                if (forcePermissions) throw new UnauthorizedAccessException(); //just go to the alternate case.
                Directory.CreateDirectory(directoryPath);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                //Must have admin rights.
                MLog.Information(@"We need admin rights to create this directory");

                // This works because the executable is extracted as part of the published single file package
                // This method would NOT work on Linux single file as it doesn't extract!
                var permissionsGranterExe = MCoreFilesystem.GetCachedExecutable(@"PermissionsGranter.exe");
                if (!File.Exists(permissionsGranterExe))
                {
                    MUtilities.ExtractInternalFile(@"ME3TweaksCore.Assets.PermissionsGranter.exe", permissionsGranterExe, true);
                }

                string args = "\"" + System.Security.Principal.WindowsIdentity.GetCurrent().Name + "\" -create-directory \"" + directoryPath.TrimEnd('\\') + "\""; //do not localize
                try
                {
                    int result = MUtilities.RunProcess(permissionsGranterExe, args, waitForProcess: true, requireAdmin: true, noWindow: true);
                    if (result == 0)
                    {
                        MLog.Information(@"Elevated process returned code 0, restore directory is hopefully writable now.");
                        return true;
                    }
                    else
                    {
                        MLog.Error($@"Elevated process returned code {result}, directory likely is not writable");
                        return false;
                    }
                }
                catch (Exception e)
                {
                    if (e is Win32Exception w32e)
                    {
                        if (w32e.NativeErrorCode == 1223)
                        {
                            //Admin canceled.
                            return false;
                        }
                    }

                    MLog.Error($@"Error creating directory with PermissionsGranter: {e.Message}");
                    return false;

                }
            }
        }

        public static int RunProcess(string exe, string args, bool waitForProcess = false, bool allowReattemptAsAdmin = false, bool requireAdmin = false, bool noWindow = true, bool useShellExecute = true)
        {
            return RunProcess(exe, null, args, waitForProcess: waitForProcess, allowReattemptAsAdmin: allowReattemptAsAdmin, requireAdmin: requireAdmin, noWindow: noWindow, useShellExecute: useShellExecute);
        }

        public static int RunProcess(string exe, List<string> args, bool waitForProcess = false, bool allowReattemptAsAdmin = false, bool requireAdmin = false, bool noWindow = true, bool useShellExecute = true)
        {
            return RunProcess(exe, args, null, waitForProcess: waitForProcess, allowReattemptAsAdmin: allowReattemptAsAdmin, requireAdmin: requireAdmin, noWindow: noWindow, useShellExecute: useShellExecute);
        }

        private static int RunProcess(string exe, List<string> argsL, string argsS, bool waitForProcess, bool allowReattemptAsAdmin, bool requireAdmin, bool noWindow, bool useShellExecute)
        {
            var argsStr = argsS;
            if (argsStr == null && argsL != null)
            {
                argsStr = "";
                foreach (var arg in argsL)
                {
                    if (arg != "") argsStr += @" "; //do not localize
                    if (arg.Contains(" "))
                    {
                        argsStr += $"\"{arg}\"";
                    }
                    else
                    {
                        argsStr += arg;
                    }
                }
            }

            if (requireAdmin)
            {
                MLog.Information($@"Running process as admin: {exe} {argsStr}");
                //requires elevation
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = exe;
                    p.StartInfo.UseShellExecute = useShellExecute;
                    p.StartInfo.WorkingDirectory = Path.GetDirectoryName(exe);
                    p.StartInfo.CreateNoWindow = true;
                    if (noWindow)
                    {
                        p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    }

                    p.StartInfo.Arguments = argsStr;
                    p.StartInfo.Verb = @"runas";
                    p.Start();
                    if (waitForProcess)
                    {
                        p.WaitForExit();
                        return p.ExitCode;
                    }

                    return -1;
                }
            }
            else
            {
                MLog.Information($@"Running process: {exe} {argsStr}");
                try
                {
                    using (Process p = new Process())
                    {
                        p.StartInfo.FileName = exe;
                        p.StartInfo.UseShellExecute = useShellExecute;
                        p.StartInfo.WorkingDirectory = Path.GetDirectoryName(exe);
                        p.StartInfo.CreateNoWindow = noWindow;
                        if (noWindow)
                        {
                            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        }

                        p.StartInfo.Arguments = argsStr;
                        p.Start();
                        if (waitForProcess)
                        {
                            p.WaitForExit();
                            return p.ExitCode;
                        }

                        return -1;
                    }
                }
                catch (Win32Exception w32e)
                {
                    MLog.Warning(@"Win32 exception running process: " + w32e.ToString());
                    if (w32e.NativeErrorCode == 740 && allowReattemptAsAdmin)
                    {
                        MLog.Information(@"Attempting relaunch with administrative rights.");
                        //requires elevation
                        using (Process p = new Process())
                        {
                            p.StartInfo.FileName = exe;
                            p.StartInfo.UseShellExecute = useShellExecute;
                            p.StartInfo.WorkingDirectory = Path.GetDirectoryName(exe);
                            p.StartInfo.CreateNoWindow = noWindow;
                            if (noWindow)
                            {
                                p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                            }

                            p.StartInfo.Arguments = argsStr;
                            p.StartInfo.Verb = @"runas";
                            p.Start();
                            if (waitForProcess)
                            {
                                p.WaitForExit();
                                return p.ExitCode;
                            }

                            return -1;
                        }
                    }
                    else
                    {
                        throw; //rethrow to higher.
                    }
                }
            }
        }

        public static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static double GetInstalledRamAmount()
        {
            return new ComputerInfo().TotalPhysicalMemory;
        }

        /// <summary>
        /// Copies file time data from the specified source file to the specified dest file.
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <param name="destFile"></param>
        public static void CopyTimestamps(string sourceFile, string destFile)
        {
            var bi = new FileInfo(sourceFile);
            File.SetLastWriteTime(destFile, bi.LastWriteTime);
            File.SetCreationTime(destFile, bi.CreationTime);
            File.SetLastAccessTime(destFile, bi.LastAccessTime);
        }

        /// <summary>
        /// Layers a folder of files over the top of a dictionary of loaded files. The directory is assumed to exist already.
        /// </summary>
        /// <param name="loadedFiles">Dictionary of loaded files</param>
        /// <param name="folder">Folder you are layering on top</param>
        public static void LayerFolderOverLoadedFiles(MEGame game, CaseInsensitiveDictionary<string> loadedFiles, string folder, bool includeTFC = false, bool includeAFC = false, string[] additionalExtensions = null)
        {
            foreach (var file in MELoadedFiles.GetCookedFiles(game, folder, includeTFC, includeAFC, additionalExtensions))
            {
                var key = Path.GetFileName(file);
                loadedFiles[key] = file;
            }
        }

        /// <summary>
        /// Windows API function to retrieve disk space information.
        /// </summary>
        /// <param name="lpDirectoryName">A directory on the disk.</param>
        /// <param name="lpFreeBytesAvailable">The total number of free bytes available to the user.</param>
        /// <param name="lpTotalNumberOfBytes">The total number of bytes on the disk.</param>
        /// <param name="lpTotalNumberOfFreeBytes">The total number of free bytes on the disk.</param>
        /// <returns>True if the function succeeds, false otherwise.</returns>
        [DllImport(@"kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDiskFreeSpaceEx(string lpDirectoryName,
            out ulong lpFreeBytesAvailable,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        /// <summary>
        /// Gets the free space available on the drive containing the specified folder.
        /// </summary>
        /// <param name="folderName">The folder path on the drive to check.</param>
        /// <param name="freespace">Output parameter that receives the number of free bytes.</param>
        /// <returns>True if the operation succeeded, false otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown when folderName is null or empty.</exception>
        public static bool DriveFreeBytes(string folderName, out ulong freespace)
        {
            freespace = 0;
            if (string.IsNullOrEmpty(folderName))
            {
                throw new ArgumentNullException(nameof(folderName));
            }

            if (!folderName.EndsWith("\\"))
            {
                folderName += '\\';
            }

            ulong free = 0, dummy1 = 0, dummy2 = 0;

            if (GetDiskFreeSpaceEx(folderName, out free, out dummy1, out dummy2))
            {
                freespace = free;
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}