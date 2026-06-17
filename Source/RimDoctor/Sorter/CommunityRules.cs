using System;
using System.Collections.Generic;
using System.IO;
using RimDoctor.Util;

namespace RimDoctor
{
    /// <summary>
    /// Loads RimDoctor's community sorting rules (Data/communityRules.json) and,
    /// when refreshed from a URL, tolerates both RimDoctor's own schema and the
    /// RimSort/RimPy nested shape. Hot-reloadable.
    /// </summary>
    public static class CommunityRules
    {
        public class Rule
        {
            public readonly List<string> loadAfter = new List<string>();
            public readonly List<string> loadBefore = new List<string>();
        }

        // packageId (lowercase) -> rule
        private static Dictionary<string, Rule> rules = new Dictionary<string, Rule>();
        public static int RuleCount => rules.Count;

        public static Rule For(string packageId)
        {
            if (packageId != null && rules.TryGetValue(packageId.ToLowerInvariant(), out var r))
                return r;
            return null;
        }

        public static void LoadOrReload()
        {
            try
            {
                string path = RimDoctorPaths.DataFile("communityRules.json");
                if (path == null || !File.Exists(path))
                {
                    RDLog.Warn("communityRules.json not found — sorter uses About.xml + tiers only.");
                    rules = new Dictionary<string, Rule>();
                    return;
                }
                rules = Parse(File.ReadAllText(path));
                RDLog.Msg($"Sorter: loaded {rules.Count} community rule(s).");
            }
            catch (Exception e)
            {
                RDLog.Exception("Failed to load communityRules.json", e);
            }
        }

        /// <summary>Parse either RimDoctor schema or RimSort-style. Returns a fresh map.</summary>
        public static Dictionary<string, Rule> Parse(string text)
        {
            var map = new Dictionary<string, Rule>();
            var root = Json.AsObject(Json.Deserialize(text));
            if (root == null) return map;

            // RimDoctor schema: { "rules": { id: { loadAfter:[], loadBefore:[] } } }
            var rulesObj = Json.AsObject(root.TryGetValue("rules", out var ro) ? ro : null);
            // RimSort schema: top-level is the id map directly.
            var idMap = rulesObj ?? root;

            foreach (var kv in idMap)
            {
                if (string.Equals(kv.Key, "version", StringComparison.OrdinalIgnoreCase)) continue;
                if (kv.Key.StartsWith("_")) continue;
                var entry = Json.AsObject(kv.Value);
                if (entry == null) continue;

                var rule = new Rule();
                CollectIds(entry, "loadAfter", rule.loadAfter);
                CollectIds(entry, "loadBefore", rule.loadBefore);
                // RimSort capitalization variants
                CollectIds(entry, "loadBottom", rule.loadAfter); // loadBottom ~ load very late
                map[kv.Key.ToLowerInvariant()] = rule;
            }
            return map;
        }

        // Handles both ["id", ...] and RimSort's { "value": ["id", ...] }.
        private static void CollectIds(Dictionary<string, object> entry, string key, List<string> into)
        {
            if (!entry.TryGetValue(key, out var v) || v == null) return;
            var list = Json.AsList(v);
            if (list != null)
            {
                foreach (var s in Json.StrList(v))
                    into.Add(s.ToLowerInvariant());
                return;
            }
            var obj = Json.AsObject(v);
            if (obj != null && obj.TryGetValue("value", out var inner))
                foreach (var s in Json.StrList(inner))
                    into.Add(s.ToLowerInvariant());
        }
    }
}
