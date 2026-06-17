using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Best-effort attribution: given a (missing) texture path, guess which mod it
    /// belongs to. A missing texture is by definition in no mod's loaded content,
    /// so we can't look it up directly. Instead we find the mod that owns the
    /// DIRECTORY the texture should live in — i.e. the mod that has other loaded
    /// textures under the same folder. That's almost always the real owner.
    ///
    /// Expanded in Milestone 4 to also attribute from stack-trace assembly names.
    /// </summary>
    public static class ModAttribution
    {
        /// <summary>
        /// Returns the display name of the mod most likely to own <paramref name="texturePath"/>,
        /// or null if no confident guess. Never throws.
        /// </summary>
        public static string GuessOwnerForTexturePath(string texturePath)
        {
            try
            {
                if (string.IsNullOrEmpty(texturePath))
                    return null;

                // Reading mod content holders is not thread-safe. The Unity log hook
                // fires off the main thread, so bail out to a safe null there.
                if (!UnityData.IsInMainThread)
                    return null;

                var mods = LoadedModManager.RunningModsListForReading;
                if (mods == null)
                    return null;

                // Walk up the directory chain: "A/B/C/tex" -> "A/B/C" -> "A/B" -> "A".
                // The deepest folder with a loaded sibling texture wins.
                string dir = DirectoryOf(texturePath);
                while (!string.IsNullOrEmpty(dir))
                {
                    foreach (var mod in mods)
                    {
                        if (mod == null)
                            continue;
                        if (ModHasTextureUnder(mod, dir))
                            return mod.Name;
                    }
                    dir = ParentOf(dir);
                }
            }
            catch (Exception e)
            {
                RDLog.Exception("GuessOwnerForTexturePath failed", e);
            }
            return null;
        }

        private static bool ModHasTextureUnder(ModContentPack mod, string dir)
        {
            try
            {
                var holder = mod.GetContentHolder<Texture2D>();
                if (holder == null)
                    return false;
                IEnumerable<Texture2D> under = holder.GetAllUnderPath(dir);
                return under != null && under.Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Best-effort attribution from free text (a log message / stack trace):
        /// returns the name of a running mod whose name or packageId appears in the
        /// text, or null. Used by the Log Doctor when a rule has no path capture.
        /// Prefers the longest match to avoid short-name false positives.
        /// </summary>
        public static string GuessOwnerFromText(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                    return null;

                // Strongest signal: a mod's assembly/namespace appearing in a stack
                // trace (this is thread-safe — it reads a prebuilt index, not mod state).
                string byCode = ModAssemblyIndex.IdentifyFromText(text);
                if (byCode != null)
                    return byCode;

                if (!UnityData.IsInMainThread)
                    return null; // threaded log hook — avoid touching mod state off-thread
                var mods = LoadedModManager.RunningModsListForReading;
                if (mods == null)
                    return null;

                string best = null;
                int bestLen = 0;
                foreach (var mod in mods)
                {
                    if (mod == null) continue;
                    // Skip core/official + RimDoctor itself to reduce noise.
                    if (mod.IsCoreMod || mod.IsOfficialMod) continue;

                    if (TextMentions(text, mod.Name) && mod.Name.Length > bestLen)
                    {
                        best = mod.Name; bestLen = mod.Name.Length;
                    }
                    string pid = mod.PackageId;
                    if (!string.IsNullOrEmpty(pid) && pid.Length > bestLen &&
                        text.IndexOf(pid, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        best = mod.Name; bestLen = pid.Length;
                    }
                }
                return best;
            }
            catch (Exception e)
            {
                RDLog.Exception("GuessOwnerFromText failed", e);
                return null;
            }
        }

        private static bool TextMentions(string text, string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 4)
                return false; // too short to be a confident match
            return text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DirectoryOf(string path)
        {
            int i = path.LastIndexOf('/');
            return i <= 0 ? "" : path.Substring(0, i);
        }

        private static string ParentOf(string dir)
        {
            int i = dir.LastIndexOf('/');
            return i <= 0 ? "" : dir.Substring(0, i);
        }
    }
}
