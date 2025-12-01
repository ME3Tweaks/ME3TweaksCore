using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.NativeMods;
using ME3TweaksCore.NativeMods.Interfaces;
using ME3TweaksCore.Targets;

namespace ME3TweaksCore.Helpers
{
    /// <summary>
    /// Class that contains generator overrides for specific classes so they can be subclassed (such as WPF extended versions)
    /// </summary>
    public class MExtendedClassGenerators
    {
        public delegate InstalledDLCMod GenerateInstalledDLCModDelegate(string dlcFolderPath, MEGame game, Func<InstalledDLCMod, bool> deleteConfirmationCallback, Action<InstalledDLCMod> notifyDeleted, Action<InstalledDLCMod> notifyToggled, bool modNamePrefersTPMI);
        public static GenerateInstalledDLCModDelegate GenerateInstalledDlcModObject { get; set; } = InternalGenerateInstalledDLCModObject;

        /// <summary>
        /// Used if no delegate override exists. Returns a basic InstalledDLCMod object.
        /// </summary>
        /// <param name="dlcfolderpath"></param>
        /// <param name="game"></param>
        /// <param name="deleteconfirmationcallback"></param>
        /// <param name="notifydeleted"></param>
        /// <param name="notifytoggled"></param>
        /// <param name="modnamepreferstpmi"></param>
        /// <returns></returns>
        private static InstalledDLCMod InternalGenerateInstalledDLCModObject(string dlcfolderpath, MEGame game, Func<InstalledDLCMod, bool> deleteconfirmationcallback, Action<InstalledDLCMod> notifydeleted, Action<InstalledDLCMod> notifytoggled, bool modnamepreferstpmi)
        {
            return new InstalledDLCMod(dlcfolderpath, game, deleteconfirmationcallback, notifydeleted, notifytoggled, modnamepreferstpmi);
        }

        public delegate InstalledExtraFile GenerateInstalledExtraFileDelegate(string filepath, InstalledExtraFile.EFileType type, MEGame game, Action<InstalledExtraFile> notifyDeleted = null);
        public static GenerateInstalledExtraFileDelegate GenerateInstalledExtraFile { get; set; } = InternalGenerateInstalledExtraFile;

        /// <summary>
        /// Used if no delegate override exists. Returns a basic InstalledExtraFile object.
        /// </summary>
        /// <param name="filepath"></param>
        /// <param name="type"></param>
        /// <param name="game"></param>
        /// <param name="notifydeleted"></param>
        /// <returns></returns>
        private static InstalledExtraFile InternalGenerateInstalledExtraFile(string filepath, InstalledExtraFile.EFileType type, MEGame game, Action<InstalledExtraFile> notifydeleted)
        {
            return new InstalledExtraFile(filepath, type, game, notifydeleted);
        }

        public delegate ModifiedFileObject GenerateModifiedFileObjectDelegate(string filePath, GameTarget target, Func<string, bool> restoreBasegamefileConfirmationCallback, Action notifyRestoringFileCallback, Action<object> notifyRestoredCallback, string md5 = null);
        public static GenerateModifiedFileObjectDelegate GenerateModifiedFileObject { get; set; } = InternalGenerateModifiedFileObject;

        /// <summary>
        /// Used if no delegate override exists. Returns a basic ModifiedFileObject.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="target"></param>
        /// <param name="restoreBasegamefileConfirmationCallback"></param>
        /// <param name="notifyRestoringFileCallback"></param>
        /// <param name="notifyRestoredCallback"></param>
        /// <returns></returns>
        private static ModifiedFileObject InternalGenerateModifiedFileObject(string filePath, GameTarget target, Func<string, bool> restoreBasegamefileConfirmationCallback, Action notifyRestoringFileCallback, Action<object> notifyRestoredCallback, string md5 = null)
        {
            // By default we cannot restore texture modded files like this.
            return new ModifiedFileObject(filePath, target, false, restoreBasegamefileConfirmationCallback, notifyRestoringFileCallback, notifyRestoredCallback, md5);
        }

        public delegate SFARObject GenerateSFARObjectDelegate(string file, GameTarget target, Func<string, bool> restoreSFARCallback, Action startingRestoreCallback, Action<object> notifyNoLongerModifiedCallback);
        public static GenerateSFARObjectDelegate GenerateSFARObject { get; set; } = InternalGenerateSFARObject;

        /// <summary>
        /// Used if no delegate override exists. Returns a basic SFARObject.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="target"></param>
        /// <param name="restoresfarcallback"></param>
        /// <param name="startingrestorecallback"></param>
        /// <param name="notifynolongermodifiedcallback"></param>
        /// <returns></returns>
        private static SFARObject InternalGenerateSFARObject(string file, GameTarget target, Func<string, bool> restoresfarcallback, Action startingrestorecallback, Action<object> notifynolongermodifiedcallback)
        {
            return new SFARObject(file, target, restoresfarcallback, startingrestorecallback, notifynolongermodifiedcallback);
        }


        public delegate IKnownInstalledASIMod GenerateKnownInstalledASIModDelegate(string asiFile, string hash, MEGame game, ASIModVersion matchingManifestAsi);
        public static GenerateKnownInstalledASIModDelegate GenerateKnownInstalledASIMod { get; set; } = InternalGenerateKnownInstalledASIMod;

        /// <summary>
        /// Used if no delegate override exists. Returns a basic KnownInstalledASIMod object.
        /// </summary>
        /// <param name="asiFile"></param>
        /// <param name="hash"></param>
        /// <param name="game"></param>
        /// <param name="matchingManifestAsi"></param>
        /// <returns></returns>
        private static KnownInstalledASIMod InternalGenerateKnownInstalledASIMod(string asiFile, string hash, MEGame game, ASIModVersion matchingManifestAsi)
        {
            return new KnownInstalledASIMod(asiFile, hash, game, matchingManifestAsi);
        }


        public delegate IUnknownInstalledASIMod GenerateUnknownInstalledASIModDelegate(string asiFile, string hash, MEGame game);
        public static GenerateUnknownInstalledASIModDelegate GenerateUnknownInstalledASIMod { get; set; } = InternalGenerateUnknownInstalledASIMod;

        /// <summary>
        /// Used if no delegate override exists. Returns a basic UnknownInstalledASIMod object.
        /// </summary>
        /// <param name="asiFile"></param>
        /// <param name="hash"></param>
        /// <param name="game"></param>
        /// <param name="matchingManifestAsi"></param>
        /// <returns></returns>
        private static UnknownInstalledASIMod InternalGenerateUnknownInstalledASIMod(string asiFile, string hash, MEGame game)
        {
            return new UnknownInstalledASIMod(asiFile, hash, game);
        }
    }
}
