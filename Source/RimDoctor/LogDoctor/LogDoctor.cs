using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Verse;

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
        private const int MaxEntries = 2000;  // big modlists legitimately produce many distinct errors
        private const int MaxBenign = 1500;
        private static readonly object gate = new object();
        private static readonly Dictionary<string, LogEntry> byKey = new Dictionary<string, LogEntry>();
        private static readonly List<LogEntry> ordered = new List<LogEntry>(); // newest last — ACTIONABLE only
        // Benign noise is quarantined here so it never buries or evicts real issues.
        private static readonly Dictionary<string, LogEntry> benignByKey = new Dictionary<string, LogEntry>();
        private static readonly List<LogEntry> benignOrdered = new List<LogEntry>();
        private static int frameCounter;
        private static int version;

        /// <summary>Count of ACTIONABLE issues (benign noise excluded).</summary>
        public static int IssueCount { get { lock (gate) return ordered.Count; } }
        /// <summary>Count of distinct benign (auto-classified) messages quarantined.</summary>
        public static int BenignCount { get { lock (gate) return benignOrdered.Count; } }

        /// <summary>
        /// Bumped whenever the captured set changes. UI panels compare this against
        /// their last-built value so they only rebuild their (sorted/filtered) view
        /// when something actually changed — not every frame.
        /// </summary>
        public static int Version { get { lock (gate) return version; } }

        /// <summary>Snapshot of ACTIONABLE entries, newest first.</summary>
        public static List<LogEntry> Snapshot()
        {
            lock (gate)
            {
                var copy = new List<LogEntry>(ordered);
                copy.Reverse();
                return copy;
            }
        }

        /// <summary>Snapshot of quarantined BENIGN entries, newest first.</summary>
        public static List<LogEntry> SnapshotBenign()
        {
            lock (gate)
            {
                var copy = new List<LogEntry>(benignOrdered);
                copy.Reverse();
                return copy;
            }
        }

        /// <summary>
        /// After the SoundPathIndex is built (post-def-load), move any already-
        /// captured "missing texture" entries whose path is actually a sound path
        /// into the benign quarantine. Needed because those probes are logged
        /// during load, before the index exists.
        /// </summary>
        public static void ReclassifySoundProbes()
        {
            try
            {
                lock (gate)
                {
                    for (int i = ordered.Count - 1; i >= 0; i--)
                    {
                        var e = ordered[i];

                        // 1) Move sound-path texture probes into benign.
                        if (e.advice != null && e.advice.attributionHint == "texturePath")
                        {
                            var m = e.advice.TryMatch(e.fullText);
                            if (m != null && m.Groups.Count > 1
                                && SoundPathIndex.IsSoundPath(m.Groups[1].Value.Trim().Trim('\'')))
                            {
                                ordered.RemoveAt(i);
                                byKey.Remove(e.DedupKey);
                                e.isBenign = true;
                                if (!benignByKey.ContainsKey(e.DedupKey))
                                {
                                    benignByKey[e.DedupKey] = e;
                                    benignOrdered.Add(e);
                                }
                                continue;
                            }
                        }

                        // 2) Re-attribute entries captured before defs/index were
                        //    ready: prefer the exact def owner, then assembly/namespace.
                        if (string.IsNullOrEmpty(e.culpritMod))
                            e.culpritMod = ModAttribution.OwnerFromMessageDef(e.fullText)
                                        ?? ModAttribution.GuessOwnerFromText(e.fullText);
                    }
                    version++;
                }
            }
            catch (Exception ex)
            {
                RDLog.Exception("ReclassifySoundProbes failed", ex);
            }
        }

        /// <summary>
        /// Main-thread pass that fills in the culprit mod for any actionable entry
        /// that couldn't be attributed at capture time (the live hook runs off the
        /// main thread, and most texture errors are logged lazily — after the
        /// post-load reclassify). Safe to call repeatedly; cheap when nothing is
        /// missing a culprit. MUST be called from the main thread (UI/report).
        /// </summary>
        public static void EnsureAttributed()
        {
            try
            {
                if (!UnityData.IsInMainThread) return;
                lock (gate)
                {
                    foreach (var e in ordered)
                    {
                        if (!string.IsNullOrEmpty(e.culpritMod) || e.advice == null) continue;
                        if (e.advice.attributionHint == "texturePath")
                        {
                            string owner = ModAttribution.OwnerFromMessageDef(e.fullText);
                            if (owner == null)
                            {
                                var m = e.advice.TryMatch(e.fullText);
                                if (m != null && m.Groups.Count > 1)
                                    owner = ModAttribution.GuessOwnerForTexturePath(m.Groups[1].Value.Trim().Trim('\''));
                            }
                            e.culpritMod = owner ?? ModAttribution.GuessOwnerFromText(e.fullText);
                        }
                        else
                        {
                            e.culpritMod = ModAttribution.GuessOwnerFromText(e.fullText);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RDLog.Exception("EnsureAttributed failed", ex);
            }
        }

        public static void Clear()
        {
            lock (gate)
            {
                byKey.Clear();
                ordered.Clear();
                benignByKey.Clear();
                benignOrdered.Clear();
                version++;
            }
        }

        /// <summary>
        /// Capture a log message. Safe to call from any thread (the Unity hook is
        /// threaded). Does no Unity work — just text + regex + attribution.
        /// Returns true if the message matched a BENIGN rule (so the caller may
        /// suppress it from the game's dev log).
        /// </summary>
        public static bool Capture(LogSeverity severity, string text, string stackTrace = null)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                    return false;

                // Never capture our own messages — avoids recursion + noise.
                if (text.IndexOf(RDLog.Prefix, StringComparison.Ordinal) >= 0)
                    return false;

                var settings = RimDoctorMod.Instance?.Settings;
                if (settings != null && !settings.logDoctorEnabled)
                    return false;

                string full = string.IsNullOrEmpty(stackTrace) ? text : text + "\n" + stackTrace;
                string firstLine = FirstLine(text);
                string key = DedupKey(firstLine);

                lock (gate)
                {
                    // Already seen (either bucket)? bump occurrences, report benign-ness.
                    if (byKey.TryGetValue(key, out var existingA))
                    {
                        existingA.occurrences++;
                        version++;
                        return false;
                    }
                    if (benignByKey.TryGetValue(key, out var existingB))
                    {
                        existingB.occurrences++;
                        version++;
                        return true;
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
                        bool benign = rule.benign;
                        // Sound paths probed as textures are benign engine noise — detect
                        // precisely by checking the captured path against the SoundDef index.
                        if (!benign && rule.attributionHint == "texturePath" && m.Groups.Count > 1)
                        {
                            SoundPathIndex.EnsureReady(); // lazy-build now so we classify at capture time
                            if (SoundPathIndex.IsSoundPath(m.Groups[1].Value.Trim().Trim('\'')))
                                benign = true;
                        }
                        entry.isBenign = benign;
                        if (!benign)
                            entry.culpritMod = Attribute(rule, m, full);
                        break;
                    }

                    // Unmatched Message-level lines are mod status chatter ("...done",
                    // "MVCF applied 52 patches", "CharacterEditor v1.6.3") — informational,
                    // never actionable. Quarantine so they don't fill the list.
                    if (entry.advice == null && severity == LogSeverity.Message)
                        entry.isBenign = true;

                    if (entry.isBenign)
                    {
                        // Quarantine: never touches the actionable list/cap, and we
                        // do NOT spam the persistent session log with it.
                        benignByKey[key] = entry;
                        benignOrdered.Add(entry);
                        if (benignOrdered.Count > MaxBenign)
                        {
                            var rm = benignOrdered[0];
                            benignOrdered.RemoveAt(0);
                            benignByKey.Remove(rm.DedupKey);
                        }
                        version++;
                        return true;
                    }

                    // Actionable: attribute if not already, store, mirror to session log.
                    if (entry.culpritMod == null)
                        entry.culpritMod = ModAttribution.GuessOwnerFromText(full);

                    byKey[key] = entry;
                    ordered.Add(entry);
                    version++;

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
                    return false;
                }
            }
            catch (Exception e)
            {
                // Use the underlying logger directly; never recurse through Capture.
                RDLog.Exception("LogDoctor.Capture failed", e);
                return false;
            }
        }

        private static string Attribute(LogAdviceRule rule, Match m, string full)
        {
            try
            {
                switch (rule.attributionHint)
                {
                    case "texturePath":
                        // Prefer the exact owning mod via "for def 'X'", then fall
                        // back to whoever owns the texture's folder.
                        string byDef = ModAttribution.OwnerFromMessageDef(full);
                        if (byDef != null) return byDef;
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
            EnsureAttributed(); // fill in culprits now that we're on the main thread
            var sb = new StringBuilder();
            sb.AppendLine("=== RimDoctor Log Doctor report ===");
            var snap = Snapshot();
            sb.AppendLine($"{snap.Count} issue(s) to address. {BenignCount} benign message(s) auto-classified and hidden. "
                + $"Texture substitutions this session: {TextureSubstitutionLog.UniquePathCount} path(s), "
                + $"{TextureSubstitutionLog.DrawTimeCatchCount} draw-time catch(es).");
            sb.AppendLine();

            // --- By-mod rollup: turn a wall of lines into an actionable shortlist. ---
            // Group actionable issues by culprit. A mod with many missing-texture
            // errors almost certainly failed to download its Textures folder.
            var byMod = new Dictionary<string, int>();
            var missingTexByMod = new Dictionary<string, int>();
            foreach (var e in snap)
            {
                string mod = string.IsNullOrEmpty(e.culpritMod) ? "(unattributed)" : e.culpritMod;
                byMod[mod] = byMod.TryGetValue(mod, out var c) ? c + e.occurrences : e.occurrences;
                if (e.advice != null && e.advice.attributionHint == "texturePath")
                    missingTexByMod[mod] = missingTexByMod.TryGetValue(mod, out var t) ? t + 1 : 1;
            }
            if (byMod.Count > 0)
            {
                sb.AppendLine("--- Summary by mod (fix these, not the 2000 lines below) ---");
                foreach (var kv in byMod.OrderByDescending(k => k.Value))
                {
                    bool wholeSet = missingTexByMod.TryGetValue(kv.Key, out var mt) && mt >= 15;
                    string flag = wholeSet
                        ? "  ← many missing textures: this mod's Textures folder likely didn't download. Unsubscribe + resubscribe (or re-download)."
                        : "";
                    sb.AppendLine($"  {kv.Value,6}  {kv.Key}{flag}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("--- Full detail ---");
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
