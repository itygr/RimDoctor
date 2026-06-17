using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Resets a single mod's saved settings — the cure for the classic
    /// "a mod's corrupt config crashes a screen / the main menu" problem (e.g. the
    /// Vanilla Backgrounds Expanded GetBGRect crash). Settings live in
    /// Config/Mod_&lt;folderId&gt;_&lt;class&gt;.xml; we back them up first, then delete
    /// so the mod regenerates clean defaults on next launch.
    /// </summary>
    public static class ModSettingsRepair
    {
        public class ModSettingsEntry
        {
            public string modName;
            public List<string> files = new List<string>();
        }

        /// <summary>Active mods that have a settings file on disk.</summary>
        public static List<ModSettingsEntry> ModsWithSettings()
        {
            var list = new List<ModSettingsEntry>();
            try
            {
                string cfg = GenFilePaths.ConfigFolderPath;
                if (string.IsNullOrEmpty(cfg) || !Directory.Exists(cfg)) return list;
                var allSettingFiles = Directory.GetFiles(cfg, "Mod_*.xml");

                foreach (var mod in ModsConfig.ActiveModsInLoadOrder.Where(m => m != null))
                {
                    string folder = mod.FolderName;
                    if (string.IsNullOrEmpty(folder)) continue;
                    string prefix = "Mod_" + folder + "_";
                    var matches = allSettingFiles
                        .Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (matches.Count > 0)
                        list.Add(new ModSettingsEntry { modName = mod.Name, files = matches });
                }
            }
            catch (Exception e)
            {
                RDLog.Exception("ModsWithSettings failed", e);
            }
            return list.OrderBy(e => e.modName).ToList();
        }

        /// <summary>Back up then delete the given settings files. Returns count reset.</summary>
        public static int Reset(ModSettingsEntry entry)
        {
            int n = 0;
            try
            {
                if (entry?.files == null) return 0;
                string set = BackupManager.NewBackupSet();
                foreach (var f in entry.files)
                {
                    if (set != null) BackupManager.BackupFile(set, f);
                    File.Delete(f);
                    n++;
                }
                RDLog.Msg($"Reset settings for '{entry.modName}' ({n} file(s); backup: {set ?? "none"}). Restart to apply.");
            }
            catch (Exception e)
            {
                RDLog.Exception($"Reset settings for {entry?.modName} failed", e);
            }
            return n;
        }
    }
}
