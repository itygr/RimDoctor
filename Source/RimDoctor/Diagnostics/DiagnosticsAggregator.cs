using System;
using System.Collections.Generic;
using System.Linq;

namespace RimDoctor
{
    /// <summary>
    /// Pulls findings from every RimDoctor subsystem (Log Doctor, health scan,
    /// sorter, texture substitutions, Harmony conflicts) into a single prioritized
    /// "things to address" list. This is the heart of the Diagnostics panel.
    ///
    /// It does NOT trigger heavy work itself — it reads each subsystem's latest
    /// state. Run a health scan / sort first to populate those sources.
    /// </summary>
    public static class DiagnosticsAggregator
    {
        public static List<ActionItem> BuildActionItems(HarmonyReport harmony = null)
        {
            var items = new List<ActionItem>();
            try
            {
                // --- Log Doctor: captured errors/warnings ---
                foreach (var e in LogDoctor.Snapshot())
                {
                    var sev = e.severity == LogSeverity.Error ? ActionSeverity.High
                            : e.severity == LogSeverity.Warning ? ActionSeverity.Medium
                            : ActionSeverity.Info;
                    // A high-volume repeated error is more urgent.
                    if (e.severity == LogSeverity.Error && e.occurrences > 50)
                        sev = ActionSeverity.Critical;

                    items.Add(new ActionItem
                    {
                        severity = sev,
                        source = "Log Doctor",
                        title = Trim(e.rawMessage, 140) + (e.occurrences > 1 ? $"  (x{e.occurrences})" : ""),
                        detail = e.HasAdvice ? e.advice.meaning + " — " + e.advice.likelyCause : null,
                        culpritMod = e.culpritMod,
                        suggestion = e.HasAdvice ? e.advice.suggestedFix : null
                    });
                }

                // --- Health scan: missing textures + broken mods ---
                var scan = ContentHealthScanner.LastResult;
                if (scan != null)
                {
                    foreach (var rep in scan.reports)
                    {
                        if (rep.status == HealthStatus.LikelyBroken)
                            items.Add(new ActionItem
                            {
                                severity = ActionSeverity.High,
                                source = "Health",
                                title = $"Likely incomplete download: {rep.modName}",
                                detail = rep.note,
                                culpritMod = rep.modName,
                                suggestion = "Unsubscribe & re-subscribe, or Verify Integrity of Game Files."
                            });
                        else if (rep.status == HealthStatus.MissingTextures)
                            items.Add(new ActionItem
                            {
                                severity = ActionSeverity.Medium,
                                source = "Health",
                                title = $"{rep.MissingCount} missing texture(s): {rep.modName}",
                                detail = rep.missing.Take(5).Select(m => m.texPath).Aggregate("", (a, b) => a + "\n   • " + b),
                                culpritMod = rep.modName,
                                suggestion = "Re-subscribe the mod, or use RimDoctor's Safe/Maximum repair to write placeholders."
                            });
                    }
                }

                // --- Texture substitutions this session ---
                if (TextureSubstitutionLog.UniquePathCount > 0)
                    items.Add(new ActionItem
                    {
                        severity = ActionSeverity.Medium,
                        source = "Textures",
                        title = $"{TextureSubstitutionLog.UniquePathCount} texture(s) substituted at runtime this session",
                        detail = TextureSubstitutionLog.Entries.Take(8)
                            .Select(en => $"{en.path} (x{en.hits})")
                            .Aggregate("", (a, b) => a + "\n   • " + b),
                        suggestion = "These were missing on disk; fallback kept the game running. Run a Health scan + repair to fix permanently."
                    });
                if (TextureSubstitutionLog.DrawTimeCatchCount > 0)
                    items.Add(new ActionItem
                    {
                        severity = ActionSeverity.Medium,
                        source = "Textures",
                        title = $"{TextureSubstitutionLog.DrawTimeCatchCount} null-texture draw call(s) caught",
                        detail = "A UI tried to draw a null texture — without the fallback this is the per-frame freeze.",
                        suggestion = "Keep texture fallback ON. If a specific screen misbehaves, identify the owning mod via the Log Doctor."
                    });

                // --- Sorter warnings ---
                var sort = LoadOrderSorter.Last;
                if (sort != null)
                {
                    foreach (var w in sort.warnings)
                    {
                        var sev = w.kind == WarningKind.Cycle ? ActionSeverity.High
                                : w.kind == WarningKind.MissingDependency ? ActionSeverity.High
                                : ActionSeverity.Medium;
                        items.Add(new ActionItem
                        {
                            severity = sev,
                            source = "Sorter",
                            title = w.kind.ToString(),
                            detail = w.text,
                            suggestion = w.kind == WarningKind.MissingDependency
                                ? "Subscribe/enable the missing mod."
                                : "Review the conflicting load rules; the sorter shows placement reasons."
                        });
                    }
                    if (sort.Changed)
                        items.Add(new ActionItem
                        {
                            severity = ActionSeverity.Medium,
                            source = "Sorter",
                            title = "Your load order differs from the recommended order",
                            detail = "RimDoctor computed a different order based on dependencies + community rules.",
                            suggestion = "Open the Load Order panel and Apply & Restart to fix it."
                        });
                }

                // --- Harmony conflicts ---
                if (harmony != null)
                {
                    foreach (var c in harmony.conflicts.Take(40))
                        items.Add(new ActionItem
                        {
                            severity = c.HighRisk ? ActionSeverity.High : ActionSeverity.Info,
                            source = "Harmony",
                            title = $"{c.owners.Count} mods patch {Trim(c.method, 90)}",
                            detail = $"Owners: {string.Join(", ", c.owners)}\n" +
                                     $"prefixes:{c.prefixes} postfixes:{c.postfixes} transpilers:{c.transpilers}"
                                     + (c.HighRisk ? "\n⚠ Multiple transpilers/owners — high conflict risk." : ""),
                            suggestion = c.HighRisk
                                ? "If you see odd behaviour in this area, test by disabling one of these mods."
                                : "Usually fine — overlapping patches are common. Noted for reference."
                        });
                }

                // Sort: most severe first, then by source.
                items.Sort((a, b) =>
                {
                    int s = ((int)b.severity).CompareTo((int)a.severity);
                    if (s != 0) return s;
                    return string.Compare(a.source, b.source, StringComparison.Ordinal);
                });
            }
            catch (Exception e)
            {
                RDLog.Exception("DiagnosticsAggregator failed", e);
            }
            return items;
        }

        private static string Trim(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }
    }
}
