using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RimDoctor
{
    public enum DiagLevel { Trace, Info, Warn, Error }

    public class DiagLine
    {
        public DiagLevel level;
        public string category;   // e.g. "TextureFallback", "Sorter", "Repair", "Patch"
        public string message;
        public string stamp;      // wall-clock HH:mm:ss
        public int seq;
    }

    /// <summary>
    /// RimDoctor's own deep diagnostic log. Distinct from the Log Doctor (which
    /// interprets the GAME's log): this records everything RIMDOCTOR does —
    /// substitutions, scans, sort decisions, repairs, patch results — to a
    /// persistent per-session file AND an in-memory ring buffer the Diagnostics
    /// panel tails live.
    ///
    /// Low-volume by design (no per-frame writes). Every write is guarded; the
    /// logger can never throw into the game.
    /// </summary>
    public static class DiagnosticLog
    {
        private const int RingCapacity = 2000;
        private static readonly object gate = new object();
        private static readonly Queue<DiagLine> ring = new Queue<DiagLine>(RingCapacity);
        private static string filePath;
        private static int seq;
        private static bool fileReady;

        public static string FilePath => filePath;

        /// <summary>Bumped on every write so the live-log panel only rebuilds its view when there's new output.</summary>
        public static int Version { get { lock (gate) return seq; } }

        public static void Init()
        {
            try
            {
                string folder = RimDoctorPaths.LogsFolder;
                if (string.IsNullOrEmpty(folder)) return;
                Directory.CreateDirectory(folder);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                filePath = Path.Combine(folder, $"rimdoctor_session_{stamp}.log");
                File.WriteAllText(filePath,
                    $"# RimDoctor diagnostic log — session started {DateTime.Now}{Environment.NewLine}");
                fileReady = true;
                // Flush anything buffered before the file existed.
                lock (gate)
                {
                    var sb = new StringBuilder();
                    foreach (var l in ring) sb.AppendLine(Format(l));
                    if (sb.Length > 0) File.AppendAllText(filePath, sb.ToString());
                }
                PruneOldLogs(folder);
                Info("Diagnostics", $"Diagnostic log started at {filePath}");
            }
            catch (Exception e)
            {
                // Use Verse.Log directly — never recurse into RDLog/DiagnosticLog.
                Verse.Log.Warning($"{RDLog.Prefix} DiagnosticLog.Init failed: {e.Message}");
            }
        }

        public static void Trace(string category, string message) => Write(DiagLevel.Trace, category, message);
        public static void Info(string category, string message) => Write(DiagLevel.Info, category, message);
        public static void Warn(string category, string message) => Write(DiagLevel.Warn, category, message);
        public static void Error(string category, string message) => Write(DiagLevel.Error, category, message);

        public static void Write(DiagLevel level, string category, string message)
        {
            try
            {
                var line = new DiagLine
                {
                    level = level,
                    category = category ?? "-",
                    message = message ?? "",
                    stamp = DateTime.Now.ToString("HH:mm:ss"),
                    seq = ++seq
                };
                lock (gate)
                {
                    ring.Enqueue(line);
                    while (ring.Count > RingCapacity) ring.Dequeue();
                    if (fileReady)
                    {
                        try { File.AppendAllText(filePath, Format(line) + Environment.NewLine); }
                        catch { /* disk hiccup — keep the ring buffer regardless */ }
                    }
                }
            }
            catch { /* never throw */ }
        }

        public static List<DiagLine> Snapshot()
        {
            lock (gate) return new List<DiagLine>(ring);
        }

        public static void OpenFolder()
        {
            try
            {
                string folder = RimDoctorPaths.LogsFolder;
                if (!string.IsNullOrEmpty(folder))
                    Application.OpenURL("file://" + folder);
            }
            catch (Exception e)
            {
                Verse.Log.Warning($"{RDLog.Prefix} OpenFolder failed: {e.Message}");
            }
        }

        private static string Format(DiagLine l) =>
            $"{l.stamp} [{l.level.ToString().ToUpperInvariant()[0]}] {l.category,-16} {l.message}";

        private static void PruneOldLogs(string folder)
        {
            try
            {
                var files = Directory.GetFiles(folder, "rimdoctor_session_*.log");
                if (files.Length <= 15) return;
                Array.Sort(files);
                for (int i = 0; i < files.Length - 15; i++)
                    File.Delete(files[i]);
            }
            catch { /* best effort */ }
        }
    }
}
