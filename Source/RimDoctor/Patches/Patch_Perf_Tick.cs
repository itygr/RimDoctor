using System.Diagnostics;
using HarmonyLib;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Times every simulation tick. TickManager.DoSingleTick runs once per game tick
    /// (many times per frame at fast speed), so counting calls gives true TPS and the
    /// stopwatch gives average ms spent simulating one tick.
    /// </summary>
    [HarmonyPatch(typeof(TickManager), "DoSingleTick")]
    public static class Patch_Perf_Tick
    {
        [HarmonyPrefix]
        public static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();

        [HarmonyPostfix]
        public static void Postfix(long __state)
        {
            try { PerfMonitor.NoteTick(Stopwatch.GetTimestamp() - __state); } catch { }
        }
    }
}
