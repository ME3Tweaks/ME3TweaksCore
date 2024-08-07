﻿using System.Linq;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Packages.CloningImportingAndRelinking;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.Classes;
using PropertyChanged;

namespace ME3TweaksCore.ME3Tweaks.StarterKit
{
    /// <summary>
    /// UI bound object for choosing which 2DAs to generate in starter kit
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class Bio2DAOption
    {
        /// <summary>
        /// If option is chosen for generation
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// The title text of the option
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// The template table path to use for reading the columns and data type when generating
        /// </summary>
        public LEXOpenable TemplateTable { get; set; }

        /// <summary>
        /// Installed IFP, this variable is transient.
        /// </summary>
        public string InstalledInstancedFullPath { get; set; }

        public Bio2DAOption(string title, LEXOpenable templateTable)
        {
            Title = title;
            TemplateTable = templateTable;
        }

        /// <summary>
        /// Generates a blank 2DA with info from this object at the specified path
        /// </summary>
        public ExportEntry GenerateBlank2DA(ExportEntry sourceTable, IMEPackage p)
        {
            var newObjectName = $@"{sourceTable.ObjectName}_part";
            var index = 1;
            var nameRef = new NameReference(newObjectName, index);

            // We don't support indexing 
            if (p.Exports.Any(x => x.ObjectName == nameRef))
                return null; // Already exists

            EntryImporter.ImportAndRelinkEntries(EntryImporter.PortingOption.CloneAllDependencies, sourceTable, p, null, true, new RelinkerOptionsPackage(), out var v);
            if (v is ExportEntry newEntry)
            {
                if (newEntry.Parent == null)
                {
                    newEntry.ExportFlags &= ~UnrealFlags.EExportFlags.ForcedExport; // It is not ForcedExport in these seek free files.
                }
                else
                {
                    newEntry.ExportFlags |= UnrealFlags.EExportFlags.ForcedExport; // It is ForcedExport if under a package
                }

                var twoDA = new Bio2DA(newEntry);
                twoDA.ClearRows();
                twoDA.Write2DAToExport();
                newEntry.ObjectName = nameRef;
                var objRef = StarterKitAddins.CreateObjectReferencer(p, false);
                StarterKitAddins.AddToObjectReferencer(objRef);
                return newEntry;
            }

            return null;
        }
    }
}
