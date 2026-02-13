using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using LegendaryExplorerCore.Gammtek.Extensions;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Localization;
using ME3TweaksCore.NativeMods.Interfaces;
using ME3TweaksCore.Targets;
using PropertyChanged;

namespace ME3TweaksCore.NativeMods
{
    /// <summary>
    /// ASI Manager for single game target
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class ASIGame
    {
        public MEGame Game { get; }
        public ObservableCollectionExtended<GameTarget> GameTargets { get; } = new();
        public ObservableCollectionExtended<object> DisplayedASIMods { get; } = new();
        public GameTarget CurrentGameTarget { get; set; }
        public object SelectedASI { get; set; }
        public string InstallLoaderText { get; set; }

        public string ASILoaderText
        {
            get
            {
                if (LoaderInstalled) return LC.GetString(LC.string_aSILoaderInstalledASIModsWillLoad);
                return LC.GetString(LC.string_aSILoaderNotInstalledASIModsWillNotLoad);
            }
        }

        public bool LoaderInstalled { get; set; }
        public bool IsEnabled { get; set; }
        public string GameName => Game.ToGameName(true);

        public ASIGame(MEGame game, List<GameTarget> targets)
        {
            Game = game;
            GameTargets.ReplaceAll(targets);
            CurrentGameTarget = targets.FirstOrDefault(x => x.RegistryActive);
            IsEnabled = GameTargets.Any();
        }

        /// <summary>
        /// Makes an ASI game for the specific target
        /// </summary>
        /// <param name="target"></param>
        public ASIGame(GameTarget target)
        {
            Game = target.Game;
            GameTargets.ReplaceAll(new[] { target });
            CurrentGameTarget = target;
        }

        protected bool CanInstallLoader() => CurrentGameTarget != null && !LoaderInstalled;


        protected void InstallLoader()
        {
            // Logically we'd find a way to pass this to the consuming application
            // but not sure how you'd do that in a command pattern like this
            CurrentGameTarget.InstallBinkBypass(false); // Catch statement is in here already.
            RefreshBinkStatus();
        }

        protected void RefreshBinkStatus()
        {
            LoaderInstalled = CurrentGameTarget != null && CurrentGameTarget.IsBinkBypassInstalled();
            InstallLoaderText = LoaderInstalled ? LC.GetString(LC.string_loaderInstalled) : LC.GetString(LC.string_installLoader);
        }

        public void RefreshASIStates()
        {
            // Rebuild the list of shown ASIs
            if (CurrentGameTarget != null)
            {
                var selectedObject = SelectedASI;
                var installedASIs = CurrentGameTarget.GetInstalledASIs();
                var installedKnownASIMods = installedASIs.OfType<IKnownInstalledASIMod>();
                var installedUnknownASIMods = installedASIs.OfType<IUnknownInstalledASIMod>();
                var notInstalledASIs = ASIManager.GetASIModsByGame(CurrentGameTarget.Game).Except(installedKnownASIMods.Select(x => x.AssociatedManifestItem.OwningMod));

                // Known
                DisplayedASIMods.ReplaceAll(installedKnownASIMods.OrderBy(x => x.AssociatedManifestItem.Name));

                // Unknown
                DisplayedASIMods.AddRange(installedUnknownASIMods.OrderBy(x => x.UnmappedFilename));

                // Not installed
                DisplayedASIMods.AddRange(notInstalledASIs.Where(x => x.ShouldShowInUI() && x.LatestVersion != null).OrderBy(x => x.LatestVersion.Name));

                // Attempt to re-select the existing object
                if (DisplayedASIMods.Contains(selectedObject))
                {
                    SelectedASI = selectedObject;
                }
                else
                {
                    // Reselect if we updated and the ASI object changed (e.g. v3 to v4)
                    foreach (var v in DisplayedASIMods)
                    {
                        if (v is IKnownInstalledASIMod kim && kim.AssociatedManifestItem.OwningMod == selectedObject)
                        {
                            SelectedASI = v;
                            break;
                        }
                    }
                }
            }
        }

        //Do not delete - fody will link this
        public void OnCurrentGameTargetChanged()
        {
            if (CurrentGameTarget != null)
            {
                RefreshBinkStatus();
                RefreshASIStates();
            }
        }
    }
}