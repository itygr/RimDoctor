using System;
using System.IO;
using System.Linq;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Writes timestamped backups before any destructive action and prunes old
    /// sets to the configured retention. A "set" is a folder Backups/&lt;stamp&gt;/.
    ///
    /// Timestamps are formed from RimWorld's tick/real time indirectly — we can't
    /// call DateTime.Now-style helpers reliably across resume contexts, so we use
    /// DateTime for the folder name here (this runs only on explicit user action,
    /// never during a workflow replay).
    /// </summary>
    public static class BackupManager
    {
        public static string LastBackupDir { get; private set; }

        /// <summary>Create a new timestamped backup folder and return its path (or null).</summary>
        public static string NewBackupSet()
        {
            try
            {
                string root = RimDoctorPaths.BackupsFolder;
                if (string.IsNullOrEmpty(root)) return null;
                Directory.CreateDirectory(root);

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string dir = Path.Combine(root, stamp);
                // Disambiguate if two happen in the same second.
                int n = 1;
                string baseDir = dir;
                while (Directory.Exists(dir)) dir = baseDir + "_" + (n++);
                Directory.CreateDirectory(dir);

                LastBackupDir = dir;
                PruneOld(root);
                return dir;
            }
            catch (Exception e)
            {
                RDLog.Exception("Failed to create backup set", e);
                return null;
            }
        }

        /// <summary>Copy a file into the given backup set, preserving its name.</summary>
        public static bool BackupFile(string backupDir, string sourceFile)
        {
            try
            {
                if (string.IsNullOrEmpty(backupDir) || !File.Exists(sourceFile)) return false;
                string dest = Path.Combine(backupDir, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, dest, overwrite: true);
                return true;
            }
            catch (Exception e)
            {
                RDLog.Exception($"Failed to back up {sourceFile}", e);
                return false;
            }
        }

        private static void PruneOld(string root)
        {
            try
            {
                int keep = RimDoctorMod.Instance?.Settings?.backupRetention ?? 10;
                if (keep <= 0) return;
                var sets = Directory.GetDirectories(root)
                    .OrderByDescending(d => d) // timestamp names sort chronologically
                    .ToList();
                for (int i = keep; i < sets.Count; i++)
                    Directory.Delete(sets[i], recursive: true);
            }
            catch (Exception e)
            {
                RDLog.Exception("Backup prune failed", e);
            }
        }
    }
}
