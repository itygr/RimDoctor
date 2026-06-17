using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Builds the comprehensive, shareable RimDoctor diagnostic report (Markdown):
    /// environment, prioritized action items, full active-mod load order, Harmony
    /// conflict map, health summary, and texture-substitution log. Can be copied to
    /// the clipboard or saved to disk.
    /// </summary>
    public static class ReportBuilder
    {
        public static string Build()
        {
            var sb = new StringBuilder();
            try
            {
                var harmony = HarmonyInsight.Collect();
                var items = DiagnosticsAggregator.BuildActionItems(harmony);

                sb.AppendLine("# RimDoctor Diagnostic Report");
                sb.AppendLine($"_Generated {DateTime.Now}_");
                sb.AppendLine();

                sb.AppendLine("## Environment");
                sb.AppendLine(EnvironmentInfo.AsText());

                // Action items grouped by severity
                sb.AppendLine("## Things to address");
                if (items.Count == 0)
                    sb.AppendLine("Nothing flagged. (Run a Health scan and Sort for a fuller picture.)");
                else
                {
                    var counts = items.GroupBy(i => i.severity)
                        .OrderByDescending(g => (int)g.Key)
                        .Select(g => $"{g.Count()} {g.Key}");
                    sb.AppendLine($"_{string.Join(", ", counts)}_");
                    sb.AppendLine();
                    int n = 1;
                    foreach (var it in items)
                    {
                        sb.AppendLine($"### [{it.SeverityLabel}] {it.title}");
                        sb.AppendLine($"- Source: {it.source}");
                        if (!string.IsNullOrEmpty(it.culpritMod)) sb.AppendLine($"- Likely culprit: **{it.culpritMod}**");
                        if (!string.IsNullOrEmpty(it.detail)) sb.AppendLine($"- Detail: {it.detail}");
                        if (!string.IsNullOrEmpty(it.suggestion)) sb.AppendLine($"- Suggested fix: {it.suggestion}");
                        sb.AppendLine();
                        n++;
                    }
                }

                // Harmony conflict map
                sb.AppendLine("## Harmony patch map");
                sb.AppendLine($"- Patched methods: {harmony.patchedMethods}");
                sb.AppendLine($"- Total patches: {harmony.totalPatches}");
                sb.AppendLine($"- Methods patched by >1 mod: {harmony.conflicts.Count}");
                sb.AppendLine();
                foreach (var c in harmony.conflicts.Take(60))
                    sb.AppendLine($"- {(c.HighRisk ? "⚠ " : "")}`{c.method}` — {c.owners.Count} owners " +
                                  $"[{string.Join(", ", c.owners)}] (pre {c.prefixes}/post {c.postfixes}/trans {c.transpilers})");
                sb.AppendLine();

                // Health summary
                var scan = ContentHealthScanner.LastResult;
                sb.AppendLine("## Content health");
                if (scan == null) sb.AppendLine("_(no scan run this session)_");
                else
                {
                    sb.AppendLine($"- Defs scanned: {scan.defsScanned}");
                    sb.AppendLine($"- Clean mods: {scan.CleanMods} | Missing textures: {scan.MissingMods} | Likely broken: {scan.BrokenMods}");
                }
                sb.AppendLine();

                // Texture substitutions
                sb.AppendLine("## Texture substitutions this session");
                sb.AppendLine($"- Unique missing paths: {TextureSubstitutionLog.UniquePathCount}");
                sb.AppendLine($"- Draw-time null catches: {TextureSubstitutionLog.DrawTimeCatchCount}");
                foreach (var e in TextureSubstitutionLog.Entries.OrderByDescending(x => x.hits).Take(50))
                    sb.AppendLine($"  - {e.path} (x{e.hits})");
                sb.AppendLine();

                // Full load order
                sb.AppendLine("## Active load order");
                try
                {
                    int i = 1;
                    foreach (var m in ModsConfig.ActiveModsInLoadOrder.Where(m => m != null))
                        sb.AppendLine($"{i++}. {m.Name}  `{m.PackageId}`");
                }
                catch (Exception e) { sb.AppendLine($"_(failed to read load order: {e.Message})_"); }
            }
            catch (Exception e)
            {
                RDLog.Exception("ReportBuilder.Build failed", e);
                sb.AppendLine($"\n_(report generation hit an error: {e.Message})_");
            }
            return sb.ToString();
        }

        /// <summary>Save the report to the logs folder; returns the path or null.</summary>
        public static string Save()
        {
            try
            {
                string folder = RimDoctorPaths.LogsFolder;
                if (string.IsNullOrEmpty(folder)) return null;
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"rimdoctor_report_{DateTime.Now:yyyyMMdd_HHmmss}.md");
                File.WriteAllText(path, Build());
                RDLog.Msg($"Saved diagnostic report to {path}");
                return path;
            }
            catch (Exception e)
            {
                RDLog.Exception("Saving report failed", e);
                return null;
            }
        }
    }
}
