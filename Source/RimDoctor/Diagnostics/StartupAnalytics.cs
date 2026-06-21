using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Startup "load weight" analytics: which mods contribute the most defs/assemblies
    /// (the actionable proxy for load cost — RimWorld parses XML in one unified pass, so
    /// true per-mod XML parse time isn't separable, but def/assembly weight is what makes
    /// a mod heavy). Also records the real elapsed span RimDoctor witnessed from its Mod
    /// constructor (early in load) to defs-loaded (StaticConstructorOnStartup).
    /// Collected once at PostLoad; read-only afterwards.
    /// </summary>
    public static class StartupAnalytics
    {
        public class ModWeight { public string mod; public int defs; public int assemblies; }

        public static float LoadSpanSeconds;
        public static int TotalDefs, TotalMods, TotalAssemblies;
        public static List<ModWeight> ByMod = new List<ModWeight>();
        public static bool Ready;

        private static float ctorRealtime = -1f;

        /// <summary>Called as early as possible (RimDoctor Mod ctor) to start the clock.</summary>
        public static void MarkCtor()
        {
            try { if (ctorRealtime < 0f) ctorRealtime = Time.realtimeSinceStartup; } catch { }
        }

        public static void Collect()
        {
            try
            {
                if (ctorRealtime >= 0f) LoadSpanSeconds = Time.realtimeSinceStartup - ctorRealtime;

                var list = new List<ModWeight>();
                int totalDefs = 0, totalAsm = 0;
                var mods = LoadedModManager.RunningModsListForReading;
                if (mods != null)
                {
                    TotalMods = mods.Count;
                    foreach (var m in mods)
                    {
                        if (m == null) continue;
                        int defs = 0;
                        try { foreach (var _ in m.AllDefs) defs++; } catch { }
                        int asm = m.assemblies?.loadedAssemblies?.Count ?? 0;
                        totalDefs += defs; totalAsm += asm;
                        list.Add(new ModWeight { mod = m.Name ?? "(unknown)", defs = defs, assemblies = asm });
                    }
                }
                list.Sort((a, b) => b.defs.CompareTo(a.defs));
                ByMod = list;
                TotalDefs = totalDefs;
                TotalAssemblies = totalAsm;
                Ready = true;
                RDLog.Msg($"Startup analytics: {TotalMods} mods, {TotalDefs} defs, {TotalAssemblies} assemblies, " +
                          $"load span {LoadSpanSeconds:0.0}s (as witnessed by RimDoctor).");
            }
            catch (Exception e) { RDLog.Exception("StartupAnalytics.Collect failed", e); }
        }
    }
}
