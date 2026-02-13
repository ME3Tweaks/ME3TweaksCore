using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Diagnostic module for collecting TFC file information.
    /// </summary>
    internal class DiagTFCInfo : DiagModuleBase
    {
        internal override void RunModule(LogUploadPackage package)
        {
            var diag = package.DiagnosticWriter;
            if (package.DiagnosticTarget.Game > MEGame.ME1)
            {
                MLog.Information(@"Getting list of TFCs");
                package.UpdateStatusCallback?.Invoke(LC.GetString(LC.string_collectingTFCFileInformation));

                diag.AddDiagLine(@"Texture File Cache (TFC) files", LogSeverity.DIAGSECTION);
                diag.AddDiagLine(@"The following TFC files are present in the game directory.");
                var bgPath = M3Directories.GetBioGamePath(package.DiagnosticTarget);
                var tfcFiles = package.DiagnosticTarget.GetFilesLoadedInGame(includeTFCs: true).Where(x => Path.GetExtension(x.Key) == @".tfc").Select(x => x.Value).ToList();
                if (tfcFiles.Any())
                {
                    List<string> tfcs = new List<string>();
                    foreach (string tfc in tfcFiles)
                    {
                        FileInfo fi = new FileInfo(tfc);
                        long tfcSize = fi.Length;
                        string tfcPath = tfc.Substring(bgPath.Length + 1);

                        var pathChunks = tfcPath.Split(Path.DirectorySeparatorChar);
                        var container = @"Unknown";
                        if (pathChunks[0].CaseInsensitiveEquals(@"CookedPCConsole"))
                        {
                            container = @"Basegame";
                        }
                        else if (pathChunks.Length > 1)
                        {
                            container = pathChunks[1];
                        }

                        tfcs.Add($@"<tr><td>{container}</td><td>{Path.GetFileName(tfc)}</td><td>{FileSize.FormatSize(tfcSize)}</td></tr>");
                    }

                    // Make table.
                    var tfctable = $@"
                [HTML]
                <table class=""tfctable"">
                    <thead>
                        <th>Location</th>
                        <th>TFC Name</th>
                        <th>Size</th>
                    </thead>                        
                    <tbody>
                        {string.Join("\n", tfcs)}
                    </tbody>
                </table>
                [/HTML]";
                    diag.AddDiagLine(string.Join("\n", tfctable.SplitLinesAll(options: StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())));
                }
                else
                {
                    diag.AddDiagLine(@"No TFC files were found - is this installation broken?", LogSeverity.ERROR);
                }
            }
        }
    }
}
