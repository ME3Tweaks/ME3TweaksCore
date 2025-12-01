using System;
using System.Windows.Input;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Targets;
using ME3TweaksCoreWPF.UI;
using PropertyChanged;
using Serilog;

namespace ME3TweaksCoreWPF.Targets
{
    [AddINotifyPropertyChangedInterface]
    public class InstalledDLCModWPF : InstalledDLCMod
    {
        public GenericCommand EnableDisableCommand { get; set; }
        public GenericCommand DeleteCommand { get; set; }


        public InstalledDLCModWPF(string dlcFolderPath, MEGame game, Func<InstalledDLCMod, bool> deleteConfirmationCallback, Action<InstalledDLCMod> notifyDeleted, Action<InstalledDLCMod> notifyToggled, bool modNamePrefersTPMI) 
            : base(dlcFolderPath, game, deleteConfirmationCallback, notifyDeleted, notifyToggled, modNamePrefersTPMI)
        {
            DeleteCommand = new GenericCommand(DeleteDLCMod, CanDeleteDLCMod);
            EnableDisableCommand = new GenericCommand(ToggleDLC, CanToggleDLC);
        }

        /// <summary>
        /// WPF implementation of DeleteDLCMod()
        /// </summary>
        protected override void DeleteDLCMod()
        {
            bool? holdingShift = Keyboard.Modifiers == ModifierKeys.Shift;
            if (!holdingShift.Value) holdingShift = null;
            var confirmDelete = holdingShift ?? deleteConfirmationCallback?.Invoke(this);
            if (confirmDelete.HasValue && confirmDelete.Value)
            {
                IsBeingOperatedOn = true;
                MLog.Information(@"Deleting DLC mod from target: " + dlcFolderPath);
                try
                {
                    DeleteText = LC.GetString(LC.string_deleting);
                    MUtilities.DeleteFilesAndFoldersRecursively(dlcFolderPath);
                    notifyDeleted?.Invoke(this);
                }
                catch (Exception e)
                {
                    DeleteText = LC.GetString(LC.string_delete);
                    MLog.Error($@"Error deleting DLC mod: {e.Message}");
                    // Todo: Show a dialog to the user
                }
                IsBeingOperatedOn = false;
            }
        }


        /// <summary>
        /// Generates the WPF version of an InstalledDLCModObject.
        /// </summary>
        /// <param name="dlcfolderpath"></param>
        /// <param name="game"></param>
        /// <param name="deleteconfirmationcallback"></param>
        /// <param name="notifydeleted"></param>
        /// <param name="notifytoggled"></param>
        /// <param name="modnamepreferstpmi"></param>
        /// <returns></returns>
        private static InstalledDLCMod GenerateInstalledDLCModObject(string dlcfolderpath, MEGame game, Func<InstalledDLCMod, bool> deleteconfirmationcallback, Action<InstalledDLCMod> notifydeleted, Action<InstalledDLCMod> notifytoggled, bool modnamepreferstpmi)
        {
            return new InstalledDLCModWPF(dlcfolderpath, game, deleteconfirmationcallback, notifydeleted, notifytoggled, modnamepreferstpmi);
        }
    }
}
