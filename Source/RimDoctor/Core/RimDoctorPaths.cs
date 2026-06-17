using System;
using System.IO;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Resolves on-disk locations RimDoctor needs:
    ///  - the mod's own Data/ folder (bundled rule files),
    ///  - the generated "RimDoctor Overrides" local mod (M5 repairs),
    ///  - the timestamped backups folder (M5).
    ///
    /// All methods are null-tolerant: if a path can't be resolved we return null
    /// and callers degrade gracefully rather than throwing.
    /// </summary>
    public static class RimDoctorPaths
    {
        /// <summary>The RimDoctor mod's root directory on disk.</summary>
        public static string ModRoot
        {
            get
            {
                try { return RimDoctorMod.ContentPack?.RootDir; }
                catch { return null; }
            }
        }

        /// <summary>Full path to a file under the mod's Data/ folder, or null.</summary>
        public static string DataFile(string fileName)
        {
            var root = ModRoot;
            if (string.IsNullOrEmpty(root)) return null;
            return Path.Combine(Path.Combine(root, "Data"), fileName);
        }

        /// <summary>
        /// RimWorld's user config folder (where ModsConfig.xml lives). The Mods
        /// folder and the generated override mod live alongside the game install,
        /// but generated content + backups are safest under the user data dir.
        /// </summary>
        public static string UserDataFolder
        {
            get
            {
                try { return GenFilePaths.ConfigFolderPath != null
                        ? Directory.GetParent(GenFilePaths.ConfigFolderPath)?.FullName
                        : null; }
                catch { return null; }
            }
        }

        /// <summary>Folder where the generated "RimDoctor Overrides" local mod is written (M5).</summary>
        public static string OverrideModFolder
        {
            get
            {
                var data = UserDataFolder;
                if (string.IsNullOrEmpty(data)) return null;
                return Path.Combine(Path.Combine(data, "Mods"), "RimDoctorOverrides");
            }
        }

        /// <summary>Folder where timestamped backup sets are kept (M5).</summary>
        public static string BackupsFolder
        {
            get
            {
                var root = ModRoot;
                if (string.IsNullOrEmpty(root)) return null;
                return Path.Combine(root, "Backups");
            }
        }
    }
}
