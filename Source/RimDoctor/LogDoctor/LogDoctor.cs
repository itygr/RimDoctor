using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RimDoctor
{
    /// <summary>
    /// The Log Doctor capture + interpretation engine. Holds the deduped list of
    /// captured issues, matches each against the advice DB, and attributes a
    /// likely culprit mod. Fed by Patch_Log_Capture (Verse.Log.*) and the Unity
    /// logMessageReceivedThreaded hook.
    /// </summary>
    public static class LogDoctor
    {
        private const int MaxEntries = 500;
        private static readonly object gate = new object();
        private static readonly Dictionary<string, LogEntry> byKey = new Dictionary<string, LogEntry>();
        private static readonly List<LogEntry> ordered = new List<LogEntry>(); // newest last
        private static int frameCounter;
        private static int version;

        public static int IssueCount { get { lock (gate) return ordered.Count; } }

        /// <summary>
        /// Bumped whenever the captured set changes. UI panels compare this against
        /// their last-built value so they only rebuild their (sorted/filtered) view
        /// when something actually changed — not every frame.
        /// </summary>
        public static int Version { get { lock (gate) return version; } }

        /// <summary>Snapshot of entries, newest first.</summary>
        public static List<LogEntry> Snapshot()
        {
            lock (gate)
            {
                var copy = new List<LogEntry>(ordered);
                copy.Reverse();
                return copy;
            }
        }

        public static void Clear()
        {
            lock (gate)
            {
                byKey.Clear();
                ordered.Clear();
                version++;
            }
        }

        /// <summary>
        /// Capture a log message. Safe to call from any thread (the Unity hook is
        /// threaded). Does no Unity work — just text + regex + attribution.
        /// </summary>
        public static void Capture(LogSeverity severity, string text, string stackTrace = null)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                    return;

                // Never capture our own messages — avoids recursion + noise.
                if (text.IndexOf(RDLog.Prefix, StringComparison.Ordinal) >= 0)
                    return;

                var settings = RimDoctorMod.Instance?.Settings;
                if (settings != null && !settings.logDoctorEnabled)
                    return;

                string full = string.IsNullOrEmpty(stackTrace) ? text : text + "\n" + stackTrace;
                string firstLine = FirstLine(text);
                string key = DedupKey(firstLine);

                lock (gate)
                {
                    if (byKey.TryGetValue(key, out var existing))
                    {
                        existing.occurrences++;
                        version++; // (xN) count changed
                        return;
                    }

                    var entry = new LogEntry
                    {
                        severity = severity,
                        rawMessage = firstLine,
                        fullText = full,
                        firstSeenFrame = frameCounter++,
                        DedupKey = key
                    };

                    // Match advice (first rule wins) + attribute.
                    foreach (var rule in LogAdviceDatabase.Rules)
                    {
                        var m = rule.TryMatch(full);
                        if (m == null) continue;
                        entry.advice = rule;
                        entry.culpritMod = Attribute(rule, m, full);
                        break;
                    }
                    // If no rule attributed it, still try a text-based guess.
                    if (entry.culpritMod == null)
                        entry.culpritMod = ModAttribution.GuessOwnerFromText(full);

                    byKey[key] = entry;
                    ordered.Add(entry);
                    version++;

                    // Mirror each NEW unique game-log issue into RimDoctor's persistent
                    // session log so the on-disk log captures game errors too (not just
                    // RimDoctor's own events). Deduped, so this stays low-volume.
                    DiagLevel dl = severity == LogSeverity.Error ? DiagLevel.Error
                                 : severity == LogSeverity.Warning ? DiagLevel.Warn
                                 : DiagLevel.Info;
                    string suffix = entry.culpritMod != null ? $"  [culprit: {entry.culpritMod}]" : "";
                    DiagnosticLog.Write(dl, "GameLog", firstLine + suffix);

                    if (ordered.Count > MaxEntries)
                    {
                        var removed = ordered[0];
                        ordered.RemoveAt(0);
                        byKey.Remove(removed.DedupKey);
                    }
                }
            }
            catch (Exception e)
            {
                // Use the underlying logger directly; never recurse through Capture.
                RDLog.Exception("LogDoctor.Capture failed", e);
            }
        }

        private static string Attribute(LogAdviceRule rule, Match m, string full)
        {
            try
            {
                switch (rule.attributionHint)
                {
                    case "texturePath":
                        if (m.Groups.Count > 1)
                            return ModAttribution.GuessOwnerForTexturePath(m.Groups[1].Value.Trim());
                        return null;
                    case "modName":
                        if (m.Groups.Count > 1)
                            return m.Groups[1].Value.Trim();
                        return null;
                    default:
                        return ModAttribution.GuessOwnerFromText(full);
                }
            }
            catch { return null; }
        }

        // Collapse numbers/hashes so "X at 0x1a2b" and "X at 0x9f" dedupe together.
        private static readonly Regex Volatile = new Regex(@"0x[0-9a-fA-F]+|\d{2,}", RegexOptions.Compiled);
        private static string DedupKey(string firstLine)
        {
            return Volatile.Replace(firstLine ?? "", "#");
        }

        private static string FirstLine(string text)
        {
            int nl = text.IndexOf('\n');
            string line = nl >= 0 ? text.Substring(0, nl) : text;
            return line.Length > 300 ? line.Substring(0, 300) : line;
        }

        /// <summary>Builds a shareable plain-text report of all captured issues.</summary>
        public static string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== RimDoctor Log Doctor report ===");
            var snap = Snapshot();
            sb.AppendLine($"{snap.Count} unique issue(s) captured. Texture substitutions this session: "
                + $"{TextureSubstitutionLog.UniquePathCount} path(s), {TextureSubstitutionLog.DrawTimeCatchCount} draw-time catch(es).");
            sb.AppendLine();
            int n = 1;
            foreach (var e in snap)
            {
                sb.AppendLine($"[{n++}] {e.severity}  (x{e.occurrences})");
                sb.AppendLine($"    {e.rawMessage}");
                if (e.HasAdvice)
                {
                    sb.AppendLine($"    Meaning : {e.advice.meaning}");
                    sb.AppendLine($"    Cause   : {e.advice.likelyCause}");
                    sb.AppendLine($"    Fix     : {e.advice.suggestedFix}");
                }
                if (!string.IsNullOrEmpty(e.culpritMod))
                    sb.AppendLine($"    Likely culprit: {e.culpritMod}");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
