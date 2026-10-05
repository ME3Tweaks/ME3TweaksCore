using System.Linq;
using ME3TweaksCore.Diagnostics.Support;
using ME3TweaksCore.ME3Tweaks.M3Merge;
using ME3TweaksCore.ME3Tweaks.M3Merge.LinkerPatches;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Diagnostic module for collecting installed linker patch information. Only does anything for games that support linker patch merge.
    /// </summary>
    internal class DiagLinkerPatches : DiagModuleBase
    {
        internal override void RunModule(LogUploadPackage package)
        {
            var target = package.DiagnosticTarget;
            if (!target.Game.SupportsLinkerPatchMerge())
                return;

            var diag = package.DiagnosticWriter;
            MLog.Information(@"Collecting linker patch information");

            diag.AddDiagLine(@"Linker patches", LogSeverity.DIAGSECTION);

            var record = LinkerPatchMerge.LoadBuildRecord(target.Game, target.TargetPath);
            if (LinkerPatchMerge.IsBuildStale(target.Game, target.TargetPath, record))
            {
                diag.AddDiagLine(@"Linker patches are out of date with the installed DLC. The target merge needs to be run to rebuild them.", LogSeverity.ERROR);
            }

            if (record == null || record.Redirects == null || !record.Redirects.Any())
            {
                diag.AddDiagLine(@"No linker patches are installed.");
                return;
            }

            diag.AddDiagLine($@"Linker patches were built at {record.BuildTime}. The following objects are replaced:");
            foreach (var redirect in record.Redirects)
            {
                diag.AddDiagLine($@"{redirect.OriginalObject} -> {redirect.PatchObject} from {redirect.SourceDLC} ({redirect.SourceModName ?? @"No mod name"}) [{redirect.PatchDescription}]");
                if (redirect.Overridden != null)
                {
                    foreach (var overridden in redirect.Overridden)
                    {
                        diag.AddDiagLine($@"    Overrides the patch from {overridden.DLC} ({overridden.ModName ?? @"No mod name"}) [{overridden.PatchDescription}]", LogSeverity.WARN);
                    }
                }
            }
        }
    }
}
