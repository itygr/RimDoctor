using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RimDoctor
{
    public class PatchConflict
    {
        public string method;
        public List<string> owners = new List<string>();
        public int prefixes, postfixes, transpilers;
        /// <summary>Transpilers from multiple mods are the highest-risk overlap.</summary>
        public bool HighRisk => transpilers > 1 || (transpilers >= 1 && owners.Count > 1);
    }

    public class HarmonyReport
    {
        public int patchedMethods;
        public int totalPatches;
        public readonly List<PatchConflict> conflicts = new List<PatchConflict>(); // methods with >1 owner
    }

    /// <summary>
    /// Introspects ALL Harmony patches in the game (not just RimDoctor's) to find
    /// methods patched by multiple mods — the classic cause of subtle, hard-to-
    /// trace conflicts. Pure read-only reflection over Harmony's registry.
    /// </summary>
    public static class HarmonyInsight
    {
        public static HarmonyReport Collect()
        {
            var report = new HarmonyReport();
            try
            {
                var methods = Harmony.GetAllPatchedMethods()?.ToList() ?? new List<MethodBase>();
                report.patchedMethods = methods.Count;

                foreach (var m in methods)
                {
                    Patches info;
                    try { info = Harmony.GetPatchInfo(m); }
                    catch { continue; }
                    if (info == null) continue;

                    int pre = info.Prefixes?.Count ?? 0;
                    int post = info.Postfixes?.Count ?? 0;
                    int trans = info.Transpilers?.Count ?? 0;
                    report.totalPatches += pre + post + trans;

                    var owners = info.Owners?.Distinct().ToList() ?? new List<string>();
                    if (owners.Count > 1)
                    {
                        report.conflicts.Add(new PatchConflict
                        {
                            method = DescribeMethod(m),
                            owners = owners,
                            prefixes = pre,
                            postfixes = post,
                            transpilers = trans
                        });
                    }
                }

                // Highest-risk first, then most owners.
                report.conflicts.Sort((a, b) =>
                {
                    int r = b.HighRisk.CompareTo(a.HighRisk);
                    if (r != 0) return r;
                    return b.owners.Count.CompareTo(a.owners.Count);
                });
            }
            catch (Exception e)
            {
                RDLog.Exception("HarmonyInsight.Collect failed", e);
            }
            return report;
        }

        private static string DescribeMethod(MethodBase m)
        {
            try { return $"{m.DeclaringType?.FullName}.{m.Name}"; }
            catch { return m?.Name ?? "(unknown)"; }
        }
    }
}
