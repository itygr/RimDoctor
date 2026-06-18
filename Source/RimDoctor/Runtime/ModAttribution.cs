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

        /// <summary>
        /// Many texture errors include "for def 'X'". Resolve that def to its owning
        /// mod — precise attribution for missing mod textures that have no stack trace.
        /// </summary>
        public static string OwnerFromMessageDef(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text) || !UnityData.IsInMainThread) return null;
                var m = DefRefRegex.Match(text);
                if (!m.Success) return null;
                string defName = m.Groups[1].Value;

                // Most are ThingDefs (weapons/apparel/buildings); check it then a few others.
                var td = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (td?.modContentPack != null) return td.modContentPack.Name;
                var terr = DefDatabase<TerrainDef>.GetNamedSilentFail(defName);
                if (terr?.modContentPack != null) return terr.modContentPack.Name;
            }
            catch (Exception e)
            {
                RDLog.Exception("OwnerFromMessageDef failed", e);
            }
            return null;
        }

        private static readonly System.Text.RegularExpressions.Regex DefRefRegex =
            new System.Text.RegularExpressions.Regex("for def '([^']+)'",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // Maps a def-name prefix (token before the first '_', e.g. "CE", "GR", "VFEP")
        // to the mod that owns most defs with that prefix. Built once, lazily, on the
        // main thread. Lets us attribute a texture error for a def that FAILED to
        // register, via its many sibling defs that did.
        private static Dictionary<string, string> prefixToMod;

        /// <summary>
        /// Attribution of last resort: pull "for def 'X_Foo'" out of the text, take the
        /// "X" prefix, and return whichever running mod owns the most defs sharing it.
        /// </summary>
        public static string OwnerFromDefNamePrefix(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text) || !UnityData.IsInMainThread) return null;
                var m = DefRefRegex.Match(text);
                if (!m.Success) return null;
                string defName = m.Groups[1].Value;
                int us = defName.IndexOf('_');
                if (us <= 0) return null;
                string prefix = defName.Substring(0, us);

                EnsurePrefixIndex();
                return prefixToMod != null && prefixToMod.TryGetValue(prefix, out var mod) ? mod : null;
            }
            catch (Exception e)
            {
                RDLog.Exception("OwnerFromDefNamePrefix failed", e);
                return null;
            }
        }

        private static void EnsurePrefixIndex()
        {
            if (prefixToMod != null) return;
            var counts = new Dictionary<string, Dictionary<string, int>>();
            // ThingDefs cover the vast majority of textured content; TerrainDefs add floors.
            TallyPrefixes(DefDatabase<ThingDef>.AllDefsListForReading, counts);
            TallyPrefixes(DefDatabase<TerrainDef>.AllDefsListForReading, counts);

            var result = new Dictionary<string, string>();
            foreach (var kv in counts)
            {
                string bestMod = null; int best = 0;
                foreach (var mc in kv.Value)
                    if (mc.Value > best) { best = mc.Value; bestMod = mc.Key; }
                if (bestMod != null) result[kv.Key] = bestMod;
            }
            prefixToMod = result;
        }

        private static void TallyPrefixes<T>(List<T> defs, Dictionary<string, Dictionary<string, int>> counts) where T : Def
        {
            if (defs == null) return;
            foreach (var d in defs)
            {
                if (d?.defName == null || d.modContentPack == null) continue;
                if (d.modContentPack.IsCoreMod || d.modContentPack.IsOfficialMod) continue;
                int us = d.defName.IndexOf('_');
                if (us <= 0) continue;
                string prefix = d.defName.Substring(0, us);
                if (prefix.Length < 2) continue; // too short to be a confident mod tag
                if (!counts.TryGetValue(prefix, out var byMod))
                    counts[prefix] = byMod = new Dictionary<string, int>();
                byMod.TryGetValue(d.modContentPack.Name, out var c);
                byMod[d.modContentPack.Name] = c + 1;
            }
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
