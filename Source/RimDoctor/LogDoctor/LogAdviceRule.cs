using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using RimDoctor.Util;

namespace RimDoctor
{
    /// <summary>One advice rule: a regex pattern plus the plain-language explanation.</summary>
    public class LogAdviceRule
    {
        public string id;
        public string severity;       // informational label only
        public string meaning;
        public string likelyCause;
        public string suggestedFix;
        public string attributionHint; // "texturePath" | "modName" | "none"
        public bool benign;            // known-harmless noise: quarantined + optionally suppressed

        private readonly Regex regex;
        public bool Valid => regex != null;

        public LogAdviceRule(string id, string pattern, string severity, string meaning,
            string likelyCause, string suggestedFix, string attributionHint, bool benign)
        {
            this.id = id;
            this.severity = severity;
            this.meaning = meaning;
            this.likelyCause = likelyCause;
            this.suggestedFix = suggestedFix;
            this.attributionHint = attributionHint;
            this.benign = benign;
            try
            {
                regex = new Regex(pattern,
                    RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
            }
            catch (Exception e)
            {
                RDLog.Exception($"Bad regex in logAdvice rule '{id}'", e);
                regex = null;
            }
        }

        /// <summary>Returns the regex Match if this rule applies to the message, else null.</summary>
        public Match TryMatch(string message)
        {
            if (regex == null || string.IsNullOrEmpty(message))
                return null;
            try
            {
                var m = regex.Match(message);
                return m.Success ? m : null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Loads + hot-reloads the advice rules from Data/logAdvice.json.</summary>
    public static class LogAdviceDatabase
    {
        private static List<LogAdviceRule> rules = new List<LogAdviceRule>();
        public static IReadOnlyList<LogAdviceRule> Rules => rules;
        public static int RuleCount => rules.Count;

        /// <summary>First rule whose pattern matches the text, or null. First match wins.</summary>
        public static LogAdviceRule Match(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var snapshot = rules; // local ref; LoadOrReload swaps the whole list atomically
            for (int i = 0; i < snapshot.Count; i++)
                if (snapshot[i].TryMatch(text) != null)
                    return snapshot[i];
            return null;
        }

        public static void LoadOrReload()
        {
            try
            {
                string path = RimDoctorPaths.DataFile("logAdvice.json");
                if (path == null || !File.Exists(path))
                {
                    RDLog.Warn($"logAdvice.json not found at {path ?? "(unknown)"} — Log Doctor has no rules.");
                    rules = new List<LogAdviceRule>();
                    return;
                }

                var root = Json.AsObject(Json.Deserialize(File.ReadAllText(path)));
                var parsed = new List<LogAdviceRule>();
                if (root != null && root.TryGetValue("rules", out var rulesObj))
                {
                    foreach (var item in Json.AsList(rulesObj) ?? new List<object>())
                    {
                        var o = Json.AsObject(item);
                        if (o == null) continue;
                        string pattern = Json.Str(o, "pattern");
                        if (string.IsNullOrEmpty(pattern)) continue;
                        string severity = Json.Str(o, "severity", "Warning");
                        // benign defaults true for Message-severity rules unless overridden.
                        bool benign = string.Equals(severity, "Message", System.StringComparison.OrdinalIgnoreCase);
                        if (o.TryGetValue("benign", out var bv) && bv is bool bb) benign = bb;
                        var rule = new LogAdviceRule(
                            Json.Str(o, "id", "(unnamed)"),
                            pattern,
                            severity,
                            Json.Str(o, "meaning", ""),
                            Json.Str(o, "likelyCause", ""),
                            Json.Str(o, "suggestedFix", ""),
                            Json.Str(o, "attributionHint", "none"),
                            benign);
                        if (rule.Valid)
                            parsed.Add(rule);
                    }
                }
                rules = parsed;
                RDLog.Msg($"Log Doctor: loaded {rules.Count} advice rule(s).");
            }
            catch (Exception e)
            {
                RDLog.Exception("Failed to load logAdvice.json", e);
            }
        }
    }
}
