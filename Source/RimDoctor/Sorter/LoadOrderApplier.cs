using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Applies a proposed load order and handles import/export of mod lists.
    ///
    /// Applying load order is inherently a write-config-then-restart operation:
    /// RimWorld reads the load order once at startup. We back up ModsConfig.xml
    /// first, write the new active list, save, then restart the game.
    /// </summary>
    public static class LoadOrderApplier
    {
        /// <summary>Path to the live ModsConfig.xml, or null.</summary>
        public static string ModsConfigPath
        {
            get
            {
                try { return Path.Combine(GenFilePaths.ConfigFolderPath, "ModsConfig.xml"); }
                catch { return null; }
            }
        }

        /// <summary>
        /// Back up ModsConfig.xml, set the active list to the proposed order, save,
        /// and restart. Returns false if anything before the restart failed (in
        /// which case nothing was changed past the backup).
        /// </summary>
        public static bool ApplyAndRestart(SortResult result)
        {
            // DISABLED. Auto-applying a sorted order called ModsConfig.SetActiveToList,
            // which OVERWROTE the user's entire load order (reordered everything and
            // dropped any mod the sorter couldn't place). That behaviour has been removed.
            // Kept as a safe no-op so nothing in RimDoctor can ever rewrite ModsConfig.xml.
            _ = result;
            RDLog.Msg("Load-order apply is disabled in RimDoctor — ModsConfig.xml left untouched.");
            return false;
        }

        public static string ExportPath
        {
            get
            {
                var data = RimDoctorPaths.UserDataFolder;
                return data == null ? null : Path.Combine(data, "RimDoctor_modlist.xml");
            }
        }

        /// <summary>Export an ordered list of packageIds to a simple XML file.</summary>
        public static string Export(List<string> packageIds)
        {
            try
            {
                string path = ExportPath;
                if (path == null) return null;
                var doc = new XDocument(new XElement("modList",
                    new XElement("meta", new XElement("source", "RimDoctor"), new XElement("count", packageIds.Count)),
                    new XElement("mods", packageIds.Select(id => new XElement("li", id)))));
                doc.Save(path);
                RDLog.Msg($"Exported {packageIds.Count} mods to {path}");
                return path;
            }
            catch (Exception e)
            {
                RDLog.Exception("Mod list export failed", e);
                return null;
            }
        }

        /// <summary>Import an ordered list of packageIds from the export file, or null.</summary>
        public static List<string> Import()
        {
            try
            {
                string path = ExportPath;
                if (path == null || !File.Exists(path)) return null;
                var doc = XDocument.Load(path);
                var ids = doc.Root?.Element("mods")?.Elements("li").Select(e => e.Value.Trim()).ToList();
                RDLog.Msg($"Imported {ids?.Count ?? 0} mods from {path}");
                return ids;
            }
            catch (Exception e)
            {
                RDLog.Exception("Mod list import failed", e);
                return null;
            }
        }
    }
}
