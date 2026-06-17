using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// PRIMARY texture-fallback patch (Milestone 3 headline feature).
    ///
    /// Target: Verse.ContentFinder&lt;UnityEngine.Texture2D&gt;.Get(string itemPath, bool reportFailure)
    ///         (static, returns Texture2D — confirmed against RimWorld 1.6.4850)
    ///
    /// WARNING — OFF BY DEFAULT. This was the original "headline" approach but it is
    /// fundamentally unsound: reportFailure == true does NOT mean "this texture must
    /// exist". RimWorld (and many mods) call Get(path, true) for things that are
    /// legitimately absent (song art, ambience, optional effects) and handle the
    /// null. Substituting a placeholder breaks that null-handling and crashes on
    /// load (observed: 477 expected-null probes turned into placeholders → crash).
    /// It also risks creating a Texture2D off the main thread during async loading.
    ///
    /// The SAFE anti-freeze protection lives at the draw layer (Patch_GUI_DrawTexture),
    /// which only substitutes a texture that is actually about to be rendered and
    /// always runs on the main thread. This patch is therefore gated behind the
    /// explicit, off-by-default 'aggressiveLoadFallback' setting and is a no-op
    /// unless the user knowingly enables it.
    ///
    /// reportFailure == false is always left untouched — that's how the M2 scanner
    /// probes for existence.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_ContentFinder_Texture
    {
        // Target the CLOSED generic ContentFinder<Texture2D>.Get — Harmony resolves
        // closed generics fine when given the concrete type.
        [HarmonyTargetMethod]
        public static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ContentFinder<Texture2D>), "Get",
                new[] { typeof(string), typeof(bool) });
        }

        [HarmonyPostfix]
        public static void Postfix(ref Texture2D __result, string itemPath, bool reportFailure)
        {
            try
            {
                if (__result != null)
                    return;

                // Respect probes: a caller asking with reportFailure == false wants
                // to know the truth (e.g. the health scanner). Don't lie to them.
                if (!reportFailure)
                    return;

                // OFF by default. Without the explicit opt-in this is a complete
                // no-op — the safe draw-layer fallback handles the real freeze case.
                var settings = RimDoctorMod.Instance?.Settings;
                if (settings == null || !settings.aggressiveLoadFallback)
                    return;

                // Never create a Texture2D off the main thread (async asset loading).
                if (!UnityData.IsInMainThread)
                    return;

                var placeholder = PlaceholderTexture.Get();
                if (placeholder == null)
                    return; // couldn't build one — leave the original null behaviour

                __result = placeholder;

                bool firstSeen = TextureSubstitutionLog.RecordPath(itemPath);
                if (firstSeen && settings.logEachSubstitution)
                {
                    string owner = ModAttribution.GuessOwnerForTexturePath(itemPath);
                    RDLog.Warn(
                        $"Missing texture substituted with placeholder: '{itemPath}'" +
                        (owner != null ? $"  (likely owner: {owner})" : "") +
                        ". This would otherwise have returned null and could freeze/crash a screen.");
                }
            }
            catch (Exception e)
            {
                RDLog.Exception("ContentFinder<Texture2D>.Get postfix failed", e);
            }
        }
    }
}
