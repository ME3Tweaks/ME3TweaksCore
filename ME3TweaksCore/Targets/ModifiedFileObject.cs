using System;
using System.IO;
using LegendaryExplorerCore.Helpers;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Services.Backup;
using ME3TweaksCore.Services.Shared.BasegameFileIdentification;
using PropertyChanged;

namespace ME3TweaksCore.Targets
{
    [AddINotifyPropertyChangedInterface]
    public class ModifiedFileObject(
        string filePath,
        GameTarget target,
        bool canRestoreTextureModded,
        Func<string, bool> restoreBasegamefileConfirmationCallback,
        Action notifyRestoringFileCallback,
        Action<object> notifyRestoredCallback,
        string md5 = null)
    {
        private bool canRestoreFile;
        private bool checkedForBackupFile;
        public string FilePath { get; } = filePath;

        [AlsoNotifyFor(nameof(RestoreButtonText))]
        public bool Restoring { get; set; }

        /// <summary>
        /// Precalculated MD5 for performance
        /// </summary>
        public string MD5 { get; set; } = md5;

        /// <summary>
        /// String denoting what triggered this modification of the file
        /// </summary>
        public string ModificationSource { get; set; }

        /// <summary>
        /// Uses the basegame file DB to attempt to determine the source mod of this file. This method should be run on a background thread.
        /// </summary>
        public void DetermineSource()
        {
            var fullpath = Path.Combine(target.TargetPath, FilePath);
            if (File.Exists(fullpath))
            {
                //if (FilePath.Equals(Utilities.Getth, StringComparison.InvariantCultureIgnoreCase)) return; //don't report this file
                var info = BasegameFileIdentificationService.GetBasegameFileSource(target, fullpath, MD5);
                if (info != null)
                {
                    var modSource =  info.GetSourceForUI();
                    if (!string.IsNullOrWhiteSpace(modSource))
                    {
                        // Don't set value unless actually something
                        ModificationSource = modSource;
                    }
                }
#if DEBUG
                //else
                //{
                //    ModificationSource = MUtilities.CalculateHash(fullpath);
                //}
#endif
            }
        }

        public void RestoreFileWrapper()
        {
            RestoreFile(false);
        }

        public void RestoreFile(bool batchRestore)
        {
            bool? restore = batchRestore;
            if (!restore.Value) restore = restoreBasegamefileConfirmationCallback?.Invoke(FilePath);
            if (restore.HasValue && restore.Value && internalCanRestoreFile(batchRestore))
            {
                //Todo: Background thread this maybe?
                var backupPath = BackupService.GetGameBackupPath(target.Game);
                var backupFile = Path.Combine(backupPath, FilePath);
                var targetFile = Path.Combine(target.TargetPath, FilePath);
                try
                {
                    Restoring = true;
                    MLog.Information(@"Restoring basegame file: " + targetFile);
                    notifyRestoringFileCallback?.Invoke();
                    var tfi = new FileInfo(targetFile);
                    if (tfi.IsReadOnly)
                    {
                        tfi.IsReadOnly = false;
                    }
                    File.Copy(backupFile, targetFile, true);
                    notifyRestoredCallback?.Invoke(this);
                }
                catch (Exception e)
                {
                    Restoring = false;
                    notifyRestoredCallback?.Invoke(this);
                    MLog.Error($@"Error restoring file {targetFile}: " + e.Message);
                }
            }
        }

        //might need to make this more efficient...
        public string RestoreButtonText
        {
            get
            {
                if (Restoring)
                    return LC.GetString(LC.string_restoring);

                if (CanRestoreFile())
                    return LC.GetString(LC.string_restore);

                // Can't restore. Determine reason

                // Unsure how to get a check on this type of modification.
                if (target.TextureModded)
                    return LC.GetString(LC.string_cannotRestore);

                return LC.GetString(LC.string_noBackup);
            }
        }

        public bool CanRestoreFile()
        {
            return internalCanRestoreFile(false);
        }

        private bool internalCanRestoreFile(bool batchMode)
        {
            if (Restoring && !batchMode) return false;
            if (checkedForBackupFile) return canRestoreFile;

            // Check in backup
            var backupPath = BackupService.GetGameBackupPath(target.Game);
            canRestoreFile = backupPath != null && File.Exists(Path.Combine(backupPath, FilePath));
            checkedForBackupFile = true; // cache result

            if (canRestoreFile && !canRestoreTextureModded && target.TextureModded)
            {
                // Not allowed
                if (FilePath.RepresentsPackageFilePath())
                {
                    canRestoreFile = false;
                }
            }

            return canRestoreFile;
        }
    }
}
