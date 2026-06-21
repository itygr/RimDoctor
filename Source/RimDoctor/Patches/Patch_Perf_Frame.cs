using HarmonyLib;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Counts rendered frames (for FPS) and drives the per-second rollup. Root.Update
    /// runs once per frame in both the menu and in-game.
    /// </summary>
    [HarmonyPatch(typeof(Root), "Update")]
    public static class Patch_Perf_Frame
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { PerfMonitor.NoteFrame(); } catch { }
        }
    }
}
