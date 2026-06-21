using System.Diagnostics;
using System.Threading;
using UnityEngine;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Lightweight live performance sampler. Harmony patches feed it three signals:
    ///   • NoteFrame() — once per rendered frame (Root.Update)        → FPS
    ///   • NoteTick()  — once per simulation tick  (TickManager.DoSingleTick) → TPS + ms/tick
    ///   • NotePath()  — once per path solve       (PathFinder.FindPathNow)   → pathfinding cost
    /// Counters accumulate (thread-safe; pathfinding can run off the main thread),
    /// and are rolled up into a per-second snapshot inside NoteFrame on the main
    /// thread. The UI reads the snapshot fields; it never touches the raw counters.
    /// </summary>
    public static class PerfMonitor
    {
        // raw accumulators for the current 1-second window
        private static long _tickCount, _tickSwTicks, _pathCount, _pathSwTicks;
        private static int  _frameCount;
        private static float _windowStart = -1f;

        // rolling history (oldest..newest via _histHead ring)
        private const int HistLen = 120;
        private static readonly float[] _tpsHist = new float[HistLen];
        private static readonly float[] _msHist  = new float[HistLen];
        private static int _histHead, _histCount;

        // latest per-second snapshot (written + read on the main thread)
        public static float Fps, Tps, MsPerTick, PathMsPerSec, PathCallsPerSec;
        public static int   TargetTps;
        public static bool  Paused, HasData;

        // memory / GC (GC churn = stutter)
        public static float HeapMB;          // managed heap size
        public static float Gc0PerSec;       // gen-0 collections per second
        private static int  _lastGc0 = -1;

        private static readonly double SwToMs = 1000.0 / Stopwatch.Frequency;

        public static void NoteTick(long swTicks)
        {
            Interlocked.Increment(ref _tickCount);
            Interlocked.Add(ref _tickSwTicks, swTicks);
        }

        public static void NotePath(long swTicks)
        {
            Interlocked.Increment(ref _pathCount);
            Interlocked.Add(ref _pathSwTicks, swTicks);
        }

        /// <summary>Called every frame on the main thread; rolls up a window each second.</summary>
        public static void NoteFrame()
        {
            _frameCount++;
            float now = Time.realtimeSinceStartup;
            if (_windowStart < 0f) { _windowStart = now; return; }
            float dt = now - _windowStart;
            if (dt < 1f) return;
            Rollup(dt);
            _windowStart = now;
        }

        private static void Rollup(float dt)
        {
            long ticks  = Interlocked.Exchange(ref _tickCount, 0);
            long tickSw = Interlocked.Exchange(ref _tickSwTicks, 0);
            long paths  = Interlocked.Exchange(ref _pathCount, 0);
            long pathSw = Interlocked.Exchange(ref _pathSwTicks, 0);
            int frames = _frameCount; _frameCount = 0;

            Fps = frames / dt;
            Tps = ticks / dt;
            MsPerTick = ticks > 0 ? (float)(tickSw * SwToMs / ticks) : 0f;
            PathMsPerSec = (float)(pathSw * SwToMs / dt);
            PathCallsPerSec = paths / dt;

            var tm = Find.TickManager;
            Paused = tm == null || tm.Paused;
            TargetTps = Paused ? 0 : Mathf.RoundToInt(60f * (tm?.TickRateMultiplier ?? 1f));
            HasData = true;

            // memory / GC sampling (cheap: counters + heap size, no forced collection)
            try
            {
                HeapMB = System.GC.GetTotalMemory(false) / (1024f * 1024f);
                int g0 = System.GC.CollectionCount(0);
                if (_lastGc0 >= 0) Gc0PerSec = (g0 - _lastGc0) / dt;
                _lastGc0 = g0;
            }
            catch { }

            _tpsHist[_histHead] = Tps;
            _msHist[_histHead]  = MsPerTick;
            _histHead = (_histHead + 1) % HistLen;
            if (_histCount < HistLen) _histCount++;

            // roll up per-mod / per-category tick attribution for the same window
            try { TickAttribution.Rollup(dt, RimDoctorMod.Instance?.Settings?.detailedThingTiming ?? false); }
            catch { }
        }

        /// <summary>Returns history oldest→newest. ms=true for ms/tick, else TPS.</summary>
        public static float[] History(bool ms)
        {
            int n = _histCount;
            var outArr = new float[n];
            for (int i = 0; i < n; i++)
            {
                int idx = ((_histHead - n + i) % HistLen + HistLen) % HistLen;
                outArr[i] = ms ? _msHist[idx] : _tpsHist[idx];
            }
            return outArr;
        }

        /// <summary>Fraction of the per-tick time budget consumed (1.0 = fully saturated).</summary>
        public static float BudgetUsed
        {
            get
            {
                if (Paused || TargetTps <= 0) return 0f;
                float budgetMs = 1000f / TargetTps;
                return budgetMs > 0f ? MsPerTick / budgetMs : 0f;
            }
        }
    }
}
