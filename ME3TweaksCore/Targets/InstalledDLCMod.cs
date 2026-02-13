using System;
using System.IO;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Services.ThirdPartyModIdentification;
using PropertyChanged;

namespace ME3TweaksCore.Targets
{
    [AddINotifyPropertyChangedInterface]
    public class InstalledDLCMod
    {
        protected string dlcFolderPath;
        /// <summary>
        /// If DLC is enabled
        /// </summary>
        public bool IsEnabled => DLCFolderName.StartsWith(@"DLC_");
        public string EnableDisableText => IsEnabled ? LC.GetString(LC.string_disable) : LC.GetString(LC.string_enable);
        public string DeleteText { get; set; } = LC.GetString(LC.string_delete);
        public string EnableDisableTooltip { get; set; }
        public string ModName { get; private set; }
        public string DLCFolderName { get; private set; }
        public string DLCFolderNameString { get; private set; }
        public string InstalledBy { get; private set; }
        public string Version { get; private set; }
        public string InstallerInstanceBuild { get; private set; }

        /// <summary>
        /// If DLC is being toggled or deleted
        /// </summary>
        public bool IsBeingOperatedOn { get; set; }


        public ObservableCollectionExtended<string> IncompatibleDLC { get; } = new ObservableCollectionExtended<string>();
        public ObservableCollectionExtended<string> ChosenInstallOptions { get; } = new ObservableCollectionExtended<string>();

        private MEGame game;

        protected Func<InstalledDLCMod, bool> deleteConfirmationCallback;

        /// <summary>
        /// Listener to invoke when this item has been deleted
        /// </summary>
        protected Action<InstalledDLCMod> notifyDeleted;

        /// <summary>
        /// Listener to invoke when enabling/disabling
        /// </summary>
        private Action<InstalledDLCMod> notifyToggled;

        /// <summary>
        /// Indicates that this mod was installed by ALOT Installer or Mod Manager.
        /// </summary>
        public bool InstalledByManagedSolution { get; private set; }
        public virtual void OnDLCFolderNameChanged()
        {
            dlcFolderPath = Path.Combine(Directory.GetParent(dlcFolderPath).FullName, DLCFolderName);
            parseMetaCmm(DLCFolderName.StartsWith('x'), false);
        }

        public InstalledDLCMod(string dlcFolderPath, MEGame game, Func<InstalledDLCMod, bool> deleteConfirmationCallback, Action<InstalledDLCMod> notifyDeleted, Action<InstalledDLCMod> notifyToggled, bool modNamePrefersTPMI)
        {
            this.dlcFolderPath = dlcFolderPath;
            this.game = game;
            var dlcFolderName = DLCFolderNameString = Path.GetFileName(dlcFolderPath);
            if (TPMIService.TryGetModInfo(game, dlcFolderName.TrimStart('x'), out var tpmi))
            {
                ModName = tpmi.modname;
            }
            else
            {
                ModName = dlcFolderName;
            }

            DLCFolderName = dlcFolderName;
            this.deleteConfirmationCallback = deleteConfirmationCallback;
            this.notifyDeleted = notifyDeleted;
            this.notifyToggled = notifyToggled;
        }

        private void parseMetaCmm(bool disabled, bool modNamePrefersTPMI)
        {
            // Get name of DLC folder, removing beginning x if any.
            DLCFolderNameString = DLCFolderName.TrimStart('x');
            var metaFile = Path.Combine(dlcFolderPath, @"_metacmm.txt");
            if (File.Exists(metaFile))
            {
                InstalledByManagedSolution = true;
                InstalledBy = LC.GetString(LC.string_installedByModManager); //Default value when finding metacmm.
                MetaCMM mcmm = new MetaCMM(metaFile);
                if (DLCFolderNameString != ModName && mcmm.ModName != ModName)
                {
                    DLCFolderNameString += $@" ({ModName})";
                    if (!modNamePrefersTPMI || ModName == null)
                    {
                        ModName = mcmm.ModName;
                    }
                }

                ModName = mcmm.ModName;
                Version = mcmm.Version;
                InstallerInstanceBuild = mcmm.InstalledBy;
                if (int.TryParse(InstallerInstanceBuild, out var _))
                {
                    InstalledBy = LC.GetString(LC.string_installedByModManager) + @" Build " + InstallerInstanceBuild; // Installed by Mod Manager Build X. Doens't need localized
                }
                else
                {
                    InstalledBy = LC.GetString(LC.string_interp_installedByX, InstallerInstanceBuild);
                }

                // MetaCMM Extended
                if (mcmm.OptionsSelectedAtInstallTime != null)
                {
                    ChosenInstallOptions.ReplaceAll(mcmm.OptionsSelectedAtInstallTime);
                }
                if (mcmm.IncompatibleDLC != null)
                {
                    IncompatibleDLC.ReplaceAll(mcmm.IncompatibleDLC);
                }
            }
            else
            {
                InstalledBy = LC.GetString(LC.string_notInstalledByModManager);
            }
            if (disabled)
            {
                DLCFolderNameString += @" - " + LC.GetString(LC.string_disabled);
            }
        }

        public void ToggleDLC()
        {
            var source = dlcFolderPath;
            var dlcdir = Directory.GetParent(dlcFolderPath).FullName;
            var isBecomingDisabled = DLCFolderName.StartsWith(@"DLC"); //about to change to xDLC, so it's becoming disabled
            var newdlcname = DLCFolderName.StartsWith(@"xDLC") ? DLCFolderName.TrimStart('x') : @"x" + DLCFolderName;
            var target = Path.Combine(dlcdir, newdlcname);
            try
            {
                Directory.Move(source, target);
                DLCFolderName = newdlcname;
                dlcFolderPath = target;
                EnableDisableTooltip = LC.GetString(isBecomingDisabled ? LC.string_tooltip_enableDLC : LC.string_tooltip_disableDLC);
                notifyToggled?.Invoke(this);
            }
            catch (Exception e)
            {
                MLog.Error(@"Unable to toggle DLC: " + e.Message);
            }
        }

        public bool CanToggleDLC() => (game is MEGame.ME3 || game.IsLEGame() || DLCFolderName.StartsWith('x')) && !MRunningGameInfo.IsGameRunning(game);

        public bool EnableDisableVisible => game is MEGame.ME3 || game.IsLEGame() || DLCFolderName.StartsWith('x');
        protected bool CanDeleteDLCMod() => !MRunningGameInfo.IsGameRunning(game);

        protected virtual void DeleteDLCMod()
        {
            var confirmDelete = deleteConfirmationCallback?.Invoke(this);
            if (confirmDelete.HasValue && confirmDelete.Value)
            {
                IsBeingOperatedOn = true;
                MLog.Information(@"Deleting DLC mod from target: " + dlcFolderPath);
                try
                {
                    MUtilities.DeleteFilesAndFoldersRecursively(dlcFolderPath);
                    notifyDeleted?.Invoke(this);
                }
                catch (Exception e)
                {
                    MLog.Error($@"Error deleting DLC mod: {e.Message}");
                    // Todo: Show a dialog to the user
                }
                IsBeingOperatedOn = false;
            }
        }

        public void ClearHandlers()
        {
            deleteConfirmationCallback = null;
            notifyDeleted = null;
        }
    }
}
