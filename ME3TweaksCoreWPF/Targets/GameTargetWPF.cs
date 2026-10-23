using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Input;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore;
using ME3TweaksCore.Localization;
using ME3TweaksCore.Targets;
using ME3TweaksCoreWPF.UI;
using PropertyChanged;
using System.Linq;

namespace ME3TweaksCoreWPF.Targets
{
    /// <summary>
    /// WPF extension class to the ME3TweaksCore GameTarget class that provides information about an installation of a game.
    /// </summary>
    [DebuggerDisplay(@"GameTargetWPF {Game} {TargetPath}")]
    [AddINotifyPropertyChangedInterface]
    public class GameTargetWPF : GameTarget
    {
        public GameTargetWPF(MEGame game, string targetRootPath, bool currentRegistryActive, bool isCustomOption = false, bool isTest = false, bool skipInit = false) : base(game, targetRootPath, currentRegistryActive, isCustomOption, isTest, skipInit)
        {
            LoadCommands();
        }

        private void LoadCommands()
        {
            EnableAllDLCModsCommand = new GenericCommand(EnableAllDLCMods, AnyDLCModsDisabled);
            DisableAllDLCModsCommand = new GenericCommand(DisableAllDLCMods, AnyDLCModsEnabled);
        }

        private void DisableAllDLCMods()
        {
            foreach (var mod in UIInstalledDLCMods.Where(x => x.IsEnabled && x.CanToggleDLC()))
            {
                mod.ToggleDLC();
            }
        }

        private void EnableAllDLCMods()
        {
            foreach (var mod in UIInstalledDLCMods.Where(x=>!x.IsEnabled && x.CanToggleDLC()))
            {
                mod.ToggleDLC();
            }
        }

        private bool AnyDLCModsDisabled()
        {
            return UIInstalledDLCMods.Any(x => !x.IsEnabled && x.CanToggleDLC());
        }

        private bool AnyDLCModsEnabled()
        {
            return UIInstalledDLCMods.Any(x => x.IsEnabled && x.CanToggleDLC());
        }

        public ICommand EnableAllDLCModsCommand { get; set; }
        public ICommand DisableAllDLCModsCommand { get; set; }

        private ListCollectionView _modifiedBasegameFilesView;

        /// <summary>
        /// View for filtering modified basegame files. This is an owned view rather than the default view, as WPF purges default
        /// views that have no subscribers (e.g. when Installation Information is closed), which nulls their SourceCollection. If a
        /// cross thread change is still queued for a purged view, WPF crashes processing it. Must first be accessed on the UI thread.
        /// </summary>
        public ICollectionView ModifiedBasegameFilesView => _modifiedBasegameFilesView ??= new ListCollectionView(ModifiedBasegameFiles);

        /// <summary>
        /// Determines if this gametarget can be chosen in dropdowns
        /// </summary>
        public bool Selectable { get; set; } = true;

        public string TargetBootIcon
        {
            get
            {
                if (IsCustomOption) return null; // No icon as this is not actual game
                if (GameSource == null) return @"/images/unknown.png";
                if (GameSource.Contains(GameTarget.GAME_SOURCE_STEAM)) return @"/images/steam.png"; //higher priority than Origin in icon will make the Steam/Origin mix work
                if (GameSource.Contains(GameTarget.GAME_SOURCE_EA_APP)) return @"/images/ea.png";
                if (GameSource.Contains(GameTarget.GAME_SOURCE_DVD)) return @"/images/dvd.png";
                return @"/images/unknown.png";
            }
        }

        public string RemoveTargetTooltipText
        {
            get
            {
                if (RegistryActive) return LC.GetString(LC.string_dialog_cannotRemoveActiveTarget);
                return LC.GetString(LC.string_tooltip_removeTargetFromM3);
            }
        }

        /// <summary>
        /// Call this when the modified object lists are no longer necessary. In WPF this needs to be overridden to run on dispatcher
        /// </summary>
        public override void DumpModifiedFilesFromMemory()
        {
            //Some commands are made from a background thread, which means this might not be called from dispatcher
            ME3TweaksCoreLib.RunOnUIThread(() =>
            {
                ModifiedBasegameFiles.ClearEx();
                ModifiedSFARFiles.ClearEx();
                foreach (var uiInstalledDlcMod in UIInstalledDLCMods)
                {
                    uiInstalledDlcMod.ClearHandlers();
                }

                UIInstalledDLCMods.ClearEx();
            });
        }

        public override string ValidateTarget(bool ignoreCmmVanilla = false)
        {
            if (!Selectable)
                return null; // Don't bother with this
            return base.ValidateTarget(ignoreCmmVanilla);
        }

        public override void ReloadGameTarget(bool logInfo = true, bool forceLodUpdate = false, bool reverseME1Executable = true, bool skipInit = false)
        {
            base.ReloadGameTarget(logInfo, forceLodUpdate, reverseME1Executable, skipInit);
            OnPropertyChanged(nameof(TargetBootIcon));
        }
    }
}