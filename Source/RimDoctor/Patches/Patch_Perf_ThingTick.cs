using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// OPT-IN per-mod timing of the simulation's dominant cost: Verse.Thing.DoTick
    /// (non-virtual; TickList calls it per-thing for every rate bucket). Thousands of
    /// calls per tick, so it is NEVER auto-applied — Enable()/Disable() dynamically
    /// patch/unpatch it, leaving the default build's hot tick path completely pristine.
    /// __state is a stack local (re-entrancy-safe under nested ThingOwner.DoTick).
    /// </summary>
    public static class Patch_Perf_ThingTick
    {
        private static bool active;
        public static bool Active => active;

        public static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();

        public static void Postfix(Thing __instance, long __state)
        {
            try { TickAttribution.NoteThingTime(__instance?.def?.modContentPack, Stopwatch.GetTimestamp() - __state); } catch { }
        }

        private static MethodInfo Target() => typeof(Thing).GetMethod("DoTick",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        public static void Enable()
        {
            if (active) return;
            var h = RimDoctorMod.HarmonyInstance; var m = Target();
            if (h == null || m == null) { RDLog.Msg("Perf: cannot enable per-thing timing (Harmony/method missing)."); return; }
            try
            {
                h.Patch(m,
                    prefix: new HarmonyMethod(typeof(Patch_Perf_ThingTick).GetMethod(nameof(Prefix))),
                    postfix: new HarmonyMethod(typeof(Patch_Perf_ThingTick).GetMethod(nameof(Postfix))));
                active = true;
                RDLog.Msg("Perf: per-thing tick timing ENABLED (Thing.DoTick patched).");
            }
            catch (Exception e) { RDLog.Exception("Perf: enabling per-thing timing failed", e); }
        }

        public static void Disable()
        {
            if (!active) return;
            var h = RimDoctorMod.HarmonyInstance; var m = Target();
            try
            {
                if (h != null && m != null)
                {
                    h.Unpatch(m, typeof(Patch_Perf_ThingTick).GetMethod(nameof(Prefix)));
                    h.Unpatch(m, typeof(Patch_Perf_ThingTick).GetMethod(nameof(Postfix)));
                }
            }
            catch (Exception e) { RDLog.Exception("Perf: disabling per-thing timing failed", e); }
            active = false;
            RDLog.Msg("Perf: per-thing tick timing disabled.");
        }
    }
}
