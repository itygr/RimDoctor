using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Times local-map pathfinding. In 1.6 the solver is PathFinder.FindPathNow
    /// (multiple overloads). We patch them all via TargetMethods so the cost shows up
    /// regardless of which overload a caller uses. If the API ever changes the class
    /// processor just disables this one feature (path timing reads as 0).
    /// </summary>
    [HarmonyPatch]
    public static class Patch_Perf_Path
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static
                                     | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var m in typeof(PathFinder).GetMethods(flags))
                if (m.Name == "FindPathNow")
                    yield return m;
        }

        [HarmonyPrefix]
        public static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();

        [HarmonyPostfix]
        public static void Postfix(long __state)
        {
            try { PerfMonitor.NotePath(Stopwatch.GetTimestamp() - __state); } catch { }
        }
    }
}
