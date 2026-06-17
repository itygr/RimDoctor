using System;
using HarmonyLib;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Redirects the vanilla debug-log window to RimDoctor's plain-language window.
    ///
    /// Target: Verse.Log.TryOpenLogWindow() — what RimWorld calls when the red error
    /// box is clicked. Prefix returns false (skip the vanilla window) when the
    /// 'useRimDoctorLogWindow' setting is on, opening RimDoctor's window instead.
    ///
    /// On any error we return true so the vanilla window still opens — the user is
    /// never left without a log.
    /// </summary>
    [HarmonyPatch(typeof(Log), nameof(Log.TryOpenLogWindow))]
    public static class Patch_Log_OpenWindow
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            try
            {
                var s = RimDoctorMod.Instance?.Settings;
                if (s != null && s.useRimDoctorLogWindow)
                {
                    Dialog_RimDoctorLog.OpenOrFocus();
                    return false; // handled — don't open the raw vanilla log
                }
            }
            catch (Exception e)
            {
                RDLog.Exception("Log window redirect failed — falling back to vanilla", e);
            }
            return true;
        }
    }
}
