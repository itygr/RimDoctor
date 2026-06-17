using System;
using HarmonyLib;
using UnityEngine;

namespace RimDoctor
{
    /// <summary>
    /// SECONDARY defensive texture-fallback patch (Milestone 3).
    ///
    /// Target: UnityEngine.GUI.DrawTexture(Rect position, Texture image)
    ///         (the most common overload; confirmed in UnityEngine.IMGUIModule)
    ///
    /// Why: catches a null Texture that reaches the draw call WITHOUT going through
    /// ContentFinder — e.g. a mod cached a null in a static field, or built a
    /// Graphic whose texture failed to resolve. A null here makes Unity log
    /// "null texture passed to GUI.DrawTexture" every frame, which is the exact
    /// per-frame spam that freezes menus/screens in big modlists.
    ///
    /// We swap the null for the placeholder (so the broken UI stays visible and
    /// usable) instead of skipping the draw. Single null-check per call — cheap.
    /// </summary>
    [HarmonyPatch(typeof(GUI), nameof(GUI.DrawTexture), new[] { typeof(Rect), typeof(Texture) })]
    public static class Patch_GUI_DrawTexture
    {
        [HarmonyPrefix]
        public static void Prefix(ref Texture image)
        {
            try
            {
                if (image != null)
                    return;

                var settings = RimDoctorMod.Instance?.Settings;
                if (settings == null || !settings.textureFallbackEnabled)
                    return;

                var placeholder = PlaceholderTexture.Get();
                if (placeholder == null)
                    return;

                image = placeholder;

                if (TextureSubstitutionLog.RecordDrawTimeCatch() && settings.logEachSubstitution)
                {
                    RDLog.Warn(
                        "Caught a null texture at GUI.DrawTexture and substituted a placeholder. " +
                        "This is the per-frame 'null texture passed to GUI.DrawTexture' freeze — " +
                        "prevented. (Further catches this session are not logged individually.)");
                }
            }
            catch (Exception e)
            {
                RDLog.Exception("GUI.DrawTexture prefix failed", e);
            }
        }
    }
}
