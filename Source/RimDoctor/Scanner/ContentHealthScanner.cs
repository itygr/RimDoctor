using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Scans loaded content for problems:
    ///   1. Missing textures — every ThingDef.graphicData.texPath resolved via the
    ///      same logic RimWorld uses (single/multi/random).
    ///   2. Likely-broken mods — About.xml present but no usable content anywhere
    ///      under the mod folder (the classic "incomplete Steam download" case).
    ///
    /// Runs on demand from the Health panel. Results are grouped per mod.
    /// </summary>
    public static class ContentHealthScanner
    {
        public static HealthScanResult LastResult { get; private set; }

        public static HealthScanResult Scan()
        {
            var result = new HealthScanResult();
            try
            {
                var byMod = new Dictionary<string, ModHealthReport>();

                ModHealthReport ReportFor(ModContentPack mcp)
                {
                    string id = mcp?.PackageId ?? "(unknown)";
                    if (!byMod.TryGetValue(id, out var rep))
                    {
                        rep = new ModHealthReport
                        {
                            modName = mcp?.Name ?? "(unknown)",
                            packageId = id
                        };
                        byMod[id] = rep;
                        result.reports.Add(rep);
                    }
                    return rep;
                }

                // Ensure every running mod has a report (so clean mods show as clean).
                var running = LoadedModManager.RunningModsListForReading;
                if (running != null)
                    foreach (var mcp in running)
                        ReportFor(mcp);

                // --- 1. Missing textures across ThingDefs ---
                foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (def?.graphicData == null) continue;
                    string texPath = def.graphicData.texPath;
                    if (string.IsNullOrEmpty(texPath)) continue;

                    result.defsScanned++;
                    result.texturesChecked++;

                    if (!TextureResolver.Exists(texPath))
                    {
                        var rep = ReportFor(def.modContentPack);
                        rep.missing.Add(new MissingTextureRef
                        {
                            defName = def.defName,
                            defType = nameof(ThingDef),
                            texPath = texPath
                        });
                        if (rep.status == HealthStatus.Clean)
                            rep.status = HealthStatus.MissingTextures;
                    }
                }

                // --- 2. Likely-broken (incomplete download) mods ---
                if (running != null)
                {
                    foreach (var mcp in running)
                    {
                        var rep = ReportFor(mcp);
                        // Don't override a missing-textures finding; broken is rarer + checked only if otherwise clean.
                        if (rep.status != HealthStatus.Clean) continue;
                        if (mcp.IsCoreMod || mcp.IsOfficialMod) continue;

                        if (LooksLikeIncompleteDownload(mcp, out string why))
                        {
                            rep.status = HealthStatus.LikelyBroken;
                            rep.note = why;
                        }
                    }
                }

                result.completed = true;
            }
            catch (Exception e)
            {
                RDLog.Exception("Content health scan failed", e);
            }

            // Sort: broken first, then most-missing, then clean.
            result.reports.Sort((a, b) =>
            {
                int s = StatusRank(b.status).CompareTo(StatusRank(a.status));
                if (s != 0) return s;
                return b.MissingCount.CompareTo(a.MissingCount);
            });

            LastResult = result;
            return result;
        }

        private static int StatusRank(HealthStatus s)
        {
            switch (s)
            {
                case HealthStatus.LikelyBroken: return 2;
                case HealthStatus.MissingTextures: return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// A mod looks like an incomplete download if its folder has About.xml but
        /// contains no .dll, no Defs xml, no Patches xml, and no texture files
        /// anywhere beneath it. Pure-translation / pure-Patches mods are allowed
        /// (they have xml), so this only fires on genuinely empty shells.
        /// </summary>
        private static bool LooksLikeIncompleteDownload(ModContentPack mcp, out string why)
        {
            why = null;
            try
            {
                string root = mcp?.RootDir;
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    return false;

                bool hasDll = AnyFile(root, "*.dll");
                bool hasXml = AnyFile(root, "*.xml", excludeAboutOnly: true);
                bool hasTex = AnyFile(root, "*.png") || AnyFile(root, "*.jpg");

                if (!hasDll && !hasXml && !hasTex)
                {
                    why = "About.xml is present but the folder has no assemblies, no XML defs/patches, " +
                          "and no textures — this is almost always an incomplete Steam download. " +
                          "Unsubscribe and re-subscribe, or Verify Integrity of Game Files.";
                    return true;
                }
            }
            catch (Exception e)
            {
                RDLog.Exception($"Incomplete-download check failed for {mcp?.Name}", e);
            }
            return false;
        }

        private static bool AnyFile(string root, string pattern, bool excludeAboutOnly = false)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
                {
                    if (excludeAboutOnly)
                    {
                        // Ignore the About folder's own xml (About.xml, loadFolders.xml, etc.)
                        string dir = Path.GetFileName(Path.GetDirectoryName(f) ?? "");
                        if (string.Equals(dir, "About", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    return true;
                }
            }
            catch { /* permission/IO — treat as "can't tell", don't flag */ return true; }
            return false;
        }

        /// <summary>Plain-text export of the last scan.</summary>
        public static string BuildReport(HealthScanResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== RimDoctor Content Health report ===");
            if (result == null) { sb.AppendLine("(no scan run yet)"); return sb.ToString(); }
            sb.AppendLine($"Scanned {result.defsScanned} defs / {result.texturesChecked} textures across {result.reports.Count} mods.");
            sb.AppendLine($"Clean: {result.CleanMods}   Missing textures: {result.MissingMods}   Likely broken: {result.BrokenMods}");
            sb.AppendLine();
            foreach (var rep in result.reports)
            {
                if (rep.status == HealthStatus.Clean) continue;
                string tag = rep.status == HealthStatus.LikelyBroken ? "BROKEN" : "MISSING TEXTURES";
                sb.AppendLine($"[{tag}] {rep.modName}  ({rep.packageId})");
                if (!string.IsNullOrEmpty(rep.note))
                    sb.AppendLine($"    {rep.note}");
                foreach (var m in rep.missing)
                    sb.AppendLine($"    • {m.defType} '{m.defName}' → {m.texPath}");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
