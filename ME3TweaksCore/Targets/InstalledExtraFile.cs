using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Helpers;
using Serilog;

namespace ME3TweaksCore.Targets
{
    public class InstalledExtraFile
    {
        private Action<InstalledExtraFile> notifyDeleted;
        private MEGame game;
        public string DisplayName { get; }
        public enum EFileType
        {
            DLL
        }

        public EFileType FileType { get; }
        public InstalledExtraFile(string filepath, EFileType type, MEGame game, Action<InstalledExtraFile> notifyDeleted = null)
        {
            this.game = game;
            this.notifyDeleted = notifyDeleted;
            FilePath = filepath;
            FileName = Path.GetFileName(filepath);
            FileType = type;
            DisplayName = FileName;
            switch (type)
            {
                case EFileType.DLL:
                    var info = FileVersionInfo.GetVersionInfo(FilePath);
                    var desc = "";
                    if (!string.IsNullOrWhiteSpace(info.ProductName))
                    {
                        if (info.ProductName.Trim().Length > desc.Length)
                        {
                            desc = info.ProductName.Trim();
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(info.FileDescription))
                    {
                        if (info.FileDescription.Trim().Length > desc.Length)
                        {
                            desc = info.FileDescription.Trim();
                        }
                    }

                    if (!string.IsNullOrEmpty(desc))
                    {
                        DisplayName += $@" ({desc})";
                    }


                    break;
            }
        }

        public bool CanDeleteFile() => !MRunningGameInfo.IsGameRunning(game);

        public void DeleteExtraFile()
        {
            if (!MRunningGameInfo.IsGameRunning(game))
            {
                try
                {
                    File.Delete(FilePath);
                    notifyDeleted?.Invoke(this);
                }
                catch (Exception e)
                {
                    MLog.Error($@"Error deleting extra file {FilePath}: {e.Message}");
                }
            }
        }

        public string FileName { get; set; }

        public string FilePath { get; set; }
    }
}
