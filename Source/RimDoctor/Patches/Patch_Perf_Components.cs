using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld.Planet;

namespace RimDoctor
{
    /// <summary>
    /// Always-on precise timing of GameComponent / MapComponent / WorldComponent ticks,
    /// attributed per-mod by the component's concrete-type assembly.
    ///
    /// The *Tick() methods are newslot-virtual, so patching the BASE method would never
    /// fire for subclass overrides (virtual dispatch skips the base body). We instead
    /// enumerate every concrete override at load and patch each one. Components number in
    /// the tens, so the per-tick cost of these stopwatch pairs is negligible — safe to
    /// leave on always. NOT marked [HarmonyPatch]: applied programmatically from startup.
    /// </summary>
    public static class Patch_Perf_Components
    {
        private static bool patched;

        public static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();

        public static void Postfix(object __instance, long __state)
        {
            try { TickAttribution.NoteComponentTime(__instance, Stopwatch.GetTimestamp() - __state); } catch { }
        }

        public static void PatchAll(Harmony h)
        {
            if (patched || h == null) return;
            patched = true;
            try
            {
                var pre = new HarmonyMethod(typeof(Patch_Perf_Components).GetMethod(nameof(Prefix)));
                var post = new HarmonyMethod(typeof(Patch_Perf_Components).GetMethod(nameof(Postfix)));
                int n = 0;
                n += PatchOverrides(h, typeof(GameComponent), "GameComponentTick", pre, post);
                n += PatchOverrides(h, typeof(MapComponent), "MapComponentTick", pre, post);
                n += PatchOverrides(h, typeof(WorldComponent), "WorldComponentTick", pre, post);
                RDLog.Msg($"Perf: timing {n} component-tick override(s) for per-mod attribution.");
            }
            catch (Exception e) { RDLog.Exception("Patch_Perf_Components.PatchAll failed", e); }
        }

        private static int PatchOverrides(Harmony h, Type baseType, string method, HarmonyMethod pre, HarmonyMethod post)
        {
            int count = 0;
            foreach (var t in GenTypes.AllTypes)
            {
                if (t == null || t.IsAbstract || t == baseType || !baseType.IsAssignableFrom(t)) continue;
                MethodInfo mi;
                try
                {
                    mi = t.GetMethod(method,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                        null, Type.EmptyTypes, null);
                }
                catch { continue; }
                if (mi == null || mi.IsAbstract || mi.DeclaringType != t) continue;
                try { h.Patch(mi, prefix: pre, postfix: post); count++; }
                catch (Exception e) { RDLog.Exception($"Perf: patch {t.Name}.{method} failed", e); }
            }
            return count;
        }
    }
}
