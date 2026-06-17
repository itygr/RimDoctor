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
    /// Behaviour: if the load returns null AND the caller wanted a failure reported
    /// (reportFailure == true), we substitute a generated placeholder instead of
    /// null. Returning null here is what crashes/freezes screens in big modlists.
    ///
    /// We deliberately do NOT substitute when reportFailure == false: that path is
    /// how callers (and RimDoctor's own M2 scanner) probe whether a texture exists.
    /// Substituting there would make every texture look present.
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

                var settings = RimDoctorMod.Instance?.Settings;
                if (settings == null || !settings.textureFallbackEnabled)
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
