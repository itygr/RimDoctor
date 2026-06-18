using System;
using HarmonyLib;
using LudeonTK;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Catch-all redirect for RimWorld's raw debug log window.
    ///
    /// Patch_Log_OpenWindow only covers the red-error-box click (Log.TryOpenLogWindow).
    /// RimWorld opens Verse.EditWindow_Log through other paths too (dev-mode auto-open,
    /// the dev palette button). This prefix on WindowStack.Add intercepts ANY attempt
    /// to add an EditWindow_Log and swaps in RimDoctor's plain-language window instead,
    /// when the setting is on.
    ///
    /// SuppressRedirectOnce lets RimDoctor's own "Open raw game log" button bypass this
    /// for a single open, so power users can still reach the vanilla view on demand.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    public static class Patch_WindowStack_AddLog
    {
        /// <summary>Set true to let the very next EditWindow_Log through unredirected.</summary>
        public static bool SuppressRedirectOnce;

        [HarmonyPrefix]
        public static bool Prefix(Window window)
        {
            try
            {
                if (!(window is EditWindow_Log))
                    return true;

                if (SuppressRedirectOnce)
                {
                    SuppressRedirectOnce = false; // consume the one-shot bypass
                    return true;                  // allow the raw log this once
                }

                var s = RimDoctorMod.Instance?.Settings;
                if (s != null && s.useRimDoctorLogWindow)
                {
                    Dialog_RimDoctorLog.OpenOrFocus();
                    return false; // swallow the vanilla log window
                }
            }
            catch (Exception e)
            {
                RDLog.Exception("WindowStack.Add log redirect failed", e);
            }
            return true;
        }
    }
}
