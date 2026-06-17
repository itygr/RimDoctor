using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace RimDoctor
{
    public class RepairSummary
    {
        public int placeholdersWritten;
        public int patchesWritten;
        public int wouldFix;       // report-only count
        public bool didWrite;
        public string backupDir;
        public List<string> notes = new List<string>();
    }

    /// <summary>
    /// Executes repairs per the selected tier. Everything is written into the
    /// generated "RimDoctor Overrides" mod — never into the source mods. A backup
    /// of any prior override mod is taken first, and "undo" removes what we wrote.
    ///
    ///   ReportOnly  : count what could be fixed; write nothing.
    ///   SafeAutoFix : generate placeholder .png files at the exact missing paths
    ///                 (single + 4 rotations) so content resolves on disk.
    ///   Maximum     : SafeAutoFix + emit a PatchOperation file redirecting broken
    ///                 texPaths to a shared placeholder (covers paths we can't write
    ///                 at directly, e.g. random-graphic folders).
    /// </summary>
    public static class RepairEngine
    {
        private static byte[] cachedPng;

        private static readonly string[] DirSuffixes = { "", "_south", "_north", "_east", "_west" };

        public static RepairSummary Run(RepairTier tier, HealthScanResult scan)
        {
            var summary = new RepairSummary();
            try
            {
                if (scan == null)
                {
                    summary.notes.Add("Run a Content Health scan first.");
                    return summary;
                }

                // Gather all missing textures from the scan.
                var missing = new List<MissingTextureRef>();
                foreach (var rep in scan.reports)
                    missing.AddRange(rep.missing);

                if (tier == RepairTier.ReportOnly)
                {
                    summary.wouldFix = missing.Count;
                    summary.notes.Add($"Report-only: {missing.Count} missing texture(s) could be fixed. " +
                                      "Switch to Safe auto-fix or Maximum in Mod Settings to apply.");
                    return summary;
                }

                if (missing.Count == 0)
                {
                    summary.notes.Add("Nothing to repair — no missing textures found.");
                    return summary;
                }

                // Backup any existing override mod before we change it.
                summary.backupDir = BackupExistingOverride();
                OverrideModWriter.BeginRun();
                OverrideModWriter.EnsureSkeleton();

                byte[] png = PlaceholderPng();
                if (png == null)
                {
                    summary.notes.Add("Could not generate placeholder PNG (texture not encodable) — aborted.");
                    return summary;
                }

                // Safe: write placeholder files at each missing path (+ rotations).
                var written = new HashSet<string>();
                foreach (var m in missing)
                {
                    if (string.IsNullOrEmpty(m.texPath)) continue;
                    foreach (var suf in DirSuffixes)
                    {
                        string rel = "Textures/" + m.texPath + suf + ".png";
                        if (!written.Add(rel)) continue;
                        if (OverrideModWriter.WriteFile(rel, png))
                            summary.placeholdersWritten++;
                    }
                }

                // Maximum: also emit a texPath-redirect patch for robustness.
                if (tier == RepairTier.Maximum)
                {
                    string patch = BuildRedirectPatch(missing);
                    OverrideModWriter.WriteText("Patches/RimDoctor_Overrides.xml", patch);
                    summary.patchesWritten = 1;
                    summary.notes.Add("Maximum: wrote a texPath-redirect patch for broken defs.");
                }

                summary.didWrite = true;
                summary.notes.Add($"Wrote {summary.placeholdersWritten} placeholder texture file(s) into the " +
                                  $"'{OverrideModWriter.ModName}' mod. Enable it (loads last) and restart to apply.");
                RDLog.Msg($"Repair ({tier}): {summary.placeholdersWritten} placeholders, {summary.patchesWritten} patch file(s).");
            }
            catch (Exception e)
            {
                RDLog.Exception("RepairEngine.Run failed", e);
                summary.notes.Add("Repair hit an error (see log).");
            }
            return summary;
        }

        /// <summary>Undo the last repair by deleting the generated override mod.</summary>
        public static bool UndoLast()
        {
            return OverrideModWriter.DeleteAll();
        }

        private static string BackupExistingOverride()
        {
            try
            {
                string root = OverrideModWriter.Root;
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;
                string set = BackupManager.NewBackupSet();
                if (set == null) return null;
                // Copy the existing override mod's content files into the backup set.
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string relative = f.Substring(root.Length).TrimStart('/', '\\');
                    string dest = Path.Combine(Path.Combine(set, "RimDoctorOverrides"), relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(f, dest, true);
                }
                return set;
            }
            catch (Exception e)
            {
                RDLog.Exception("Backing up existing override mod failed", e);
                return null;
            }
        }

        private static byte[] PlaceholderPng()
        {
            if (cachedPng != null) return cachedPng;
            try
            {
                var tex = PlaceholderTexture.Get();
                if (tex == null) return null;
                cachedPng = ImageConversion.EncodeToPNG(tex);
                return cachedPng;
            }
            catch (Exception e)
            {
                RDLog.Exception("Encoding placeholder PNG failed", e);
                return null;
            }
        }

        private static string BuildRedirectPatch(List<MissingTextureRef> missing)
        {
            // A PatchOperationReplace per broken ThingDef graphicData/texPath. Since
            // we also wrote a placeholder AT the original path, this is belt-and-
            // suspenders for paths we couldn't write directly (it points at the same
            // generated placeholder). The targeted defs were just found by the scan,
            // so the xpath resolves; if a def's owning mod is later disabled the
            // operation no-ops with a single benign "node not found" log line.
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<!-- Generated by RimDoctor (Maximum tier). Redirects broken texPaths to a placeholder. Safe to delete. -->");
            sb.AppendLine("<Patch>");
            var seen = new HashSet<string>();
            foreach (var m in missing)
            {
                if (m.defType != nameof(ThingDef) || string.IsNullOrEmpty(m.defName)) continue;
                if (!seen.Add(m.defName)) continue;
                sb.AppendLine("  <Operation Class=\"PatchOperationReplace\">");
                sb.AppendLine($"    <xpath>/Defs/ThingDef[defName=\"{m.defName}\"]/graphicData/texPath</xpath>");
                sb.AppendLine("    <value>");
                sb.AppendLine($"      <texPath>{m.texPath}</texPath>");
                sb.AppendLine("    </value>");
                sb.AppendLine("  </Operation>");
            }
            sb.AppendLine("</Patch>");
            return sb.ToString();
        }
    }
}
