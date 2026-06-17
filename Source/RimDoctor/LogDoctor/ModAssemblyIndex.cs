using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Maps loaded mod assemblies + their root namespaces back to the owning mod, so
    /// an exception stack trace (e.g. "at CombatExtended.Foo.Bar ()") can be
    /// attributed to a mod even though the namespace ("CombatExtended") never equals
    /// the display name ("Combat Extended"). This is how reliable culprit attribution
    /// works — by code, not by name-in-text.
    ///
    /// Built once after defs/assemblies load. Read-only afterwards.
    /// </summary>
    public static class ModAssemblyIndex
    {
        // namespace-root (lowercased) -> mod display name
        private static readonly Dictionary<string, string> nsToMod =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // assembly simple name (lowercased) -> mod display name
        private static readonly Dictionary<string, string> asmToMod =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static bool Ready { get; private set; }

        // Framework/engine roots that must never be attributed to a mod.
        private static readonly HashSet<string> Ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Verse", "RimWorld", "UnityEngine", "Unity", "HarmonyLib", "Mono",
            "Microsoft", "mscorlib", "Newtonsoft", "Steamworks", "Ionic", "RuntimeAudioClipLoader",
            "TMPro", "Verse.AI", "LudeonTK", "0Harmony", "Assembly-CSharp", "RimDoctor"
        };

        public static void Build()
        {
            try
            {
                nsToMod.Clear();
                asmToMod.Clear();
                var mods = LoadedModManager.RunningModsListForReading;
                if (mods == null) { Ready = true; return; }

                foreach (var mod in mods)
                {
                    if (mod?.assemblies?.loadedAssemblies == null) continue;
                    if (mod.IsCoreMod || mod.IsOfficialMod) continue;
                    string modName = mod.Name;

                    foreach (var asm in mod.assemblies.loadedAssemblies)
                    {
                        if (asm == null) continue;
                        string asmName = asm.GetName().Name;
                        if (!string.IsNullOrEmpty(asmName) && !Ignore.Contains(asmName) && !asmToMod.ContainsKey(asmName))
                            asmToMod[asmName] = modName;

                        foreach (var t in SafeGetTypes(asm))
                        {
                            string ns = t?.Namespace;
                            if (string.IsNullOrEmpty(ns)) continue;
                            int dot = ns.IndexOf('.');
                            string root = dot > 0 ? ns.Substring(0, dot) : ns;
                            if (root.Length < 3 || Ignore.Contains(root)) continue;
                            if (!nsToMod.ContainsKey(root))
                                nsToMod[root] = modName;
                        }
                    }
                }
                Ready = true;
                RDLog.Msg($"Indexed namespaces/assemblies for {nsToMod.Count} namespace(s) across mods (culprit attribution).");
            }
            catch (Exception e)
            {
                RDLog.Exception("ModAssemblyIndex.Build failed", e);
                Ready = true;
            }
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types ?? Array.Empty<Type>(); }
            catch { return Array.Empty<Type>(); }
        }

        /// <summary>
        /// Scan a stack trace / message for a code token that belongs to a mod.
        /// Returns the mod display name or null. Checks namespace roots first
        /// (strongest signal), then assembly names.
        /// </summary>
        public static string IdentifyFromText(string text)
        {
            if (string.IsNullOrEmpty(text) || (nsToMod.Count == 0 && asmToMod.Count == 0))
                return null;
            try
            {
                // Stack-trace lines look like "at <Namespace>.<Type>.<Method> (...)".
                // Pull dotted tokens and test their leading segment against the index.
                foreach (var token in TokenizeDotted(text))
                {
                    int dot = token.IndexOf('.');
                    string root = dot > 0 ? token.Substring(0, dot) : token;
                    if (nsToMod.TryGetValue(root, out var mod))
                        return mod;
                }
                foreach (var kv in asmToMod)
                    if (text.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return kv.Value;
            }
            catch (Exception e)
            {
                RDLog.Exception("ModAssemblyIndex.IdentifyFromText failed", e);
            }
            return null;
        }

        private static IEnumerable<string> TokenizeDotted(string text)
        {
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.')) i++;
                    string tok = text.Substring(start, i - start);
                    if (tok.IndexOf('.') > 0) yield return tok;
                }
                else i++;
            }
        }
    }
}
