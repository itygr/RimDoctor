namespace RimDoctor
{
    /// <summary>
    /// REMOVED in v1.0.4 — this patch is intentionally gone.
    ///
    /// It used to Harmony-patch Verse.ContentFinder&lt;UnityEngine.Texture2D&gt;.Get
    /// to substitute a placeholder for missing textures at LOAD time (the old,
    /// off-by-default "aggressive" fallback).
    ///
    /// WHY IT WAS REMOVED — it silently broke ALL game audio:
    /// On Mono, generic methods over reference types share a single compiled
    /// implementation ("generic code sharing"). Detouring
    /// ContentFinder&lt;Texture2D&gt;.Get therefore ALSO intercepted
    /// ContentFinder&lt;AudioClip&gt;.Get, so every sound clip resolved through the
    /// texture code path, came back null, and every SoundDef ended up with zero
    /// resolved grains. Symptoms: no music, missing SFX, and the log flooded with
    /// "SubSustainer ... could not resolve any grains" / "Getting random element
    /// from empty collection". The detour was installed unconditionally (the
    /// on/off setting was only checked INSIDE the postfix), so it affected every
    /// player by default — not just those who enabled the feature.
    ///
    /// The class itself is intentionally left with NO Harmony attributes so the
    /// startup auto-patch loop never installs anything here.
    ///
    /// The SAFE missing-texture protection still ships: Patch_GUI_DrawTexture
    /// substitutes a placeholder only for a texture actually about to be rendered,
    /// always on the main thread, and never touches the content-load path. That is
    /// the real anti-freeze feature; this load-time variant was redundant and
    /// fundamentally unsound, so it is gone for good.
    /// </summary>
    internal static class Patch_ContentFinder_Texture
    {
        // Intentionally empty. No [HarmonyPatch] — see summary above.
    }
}
