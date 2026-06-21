using HarmonyLib;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// Draws the compact performance HUD on top of everything. UIRoot_Play.UIRootOnGUI
    /// runs the whole in-game GUI (including the WindowStack) — a Postfix renders after
    /// it, so the box sits on top. Early-outs before any Find.* access when disabled, and
    /// is suppressed during screenshot capture to match vanilla behaviour.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    public static class Patch_Perf_OverlayDraw
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing) return;
                var s = RimDoctorMod.Instance?.Settings;
                if (s == null || !s.showPerfOverlay) return;
                PerfOverlay.Draw(s);
            }
            catch { }
        }
    }

    /// <summary>
    /// Adds a one-click overlay on/off toggle to the vanilla bottom-right play-settings
    /// strip (same row as "show colonist bar"), matching game conventions.
    /// </summary>
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    public static class Patch_Perf_PlaySettingsToggle
    {
        [HarmonyPostfix]
        public static void Postfix(WidgetRow row, bool worldView)
        {
            try
            {
                if (worldView || row == null) return;
                var s = RimDoctorMod.Instance?.Settings;
                if (s == null) return;
                row.ToggleableIcon(ref s.showPerfOverlay, PerfOverlay.HudIcon,
                    "RimDoctor — show performance overlay", (SoundDef)null, (string)null);
            }
            catch { }
        }
    }
}
