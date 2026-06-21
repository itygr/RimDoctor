using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Verse;
using RimWorld.Planet;

namespace RimDoctor
{
    /// <summary>
    /// Per-mod / per-category tick-cost attribution. Fed by the perf patches:
    ///   • NoteComponentTime() — always-on, precise timing of Game/Map/World component
    ///     ticks, attributed to a mod by the component's concrete-type assembly.
    ///   • NoteThingTime()     — OPT-IN, precise timing of Thing.DoTick, attributed by
    ///     def.modContentPack (the dominant sim cost, but a hot path).
    /// Plus an always-on, free COUNT proxy: once/sec we bucket spawned things by owning
    /// mod — the cheap signal shown when per-thing timing is off.
    ///
    /// Raw Stopwatch ticks accumulate in flight (Interlocked / short lock); everything is
    /// converted to ms/sec and snapshotted once per second in Rollup() on the main thread.
    /// The HUD/panel read ONLY the snapshot fields below — never the live accumulators.
    /// </summary>
    public static class TickAttribution
    {
        public const string Vanilla = "Vanilla";
        private static readonly double SwToMs = 1000.0 / Stopwatch.Frequency;

        // in-flight accumulators (raw Stopwatch ticks)
        private static long _thingsSw, _gameCompSw, _mapCompSw, _worldCompSw;
        private static readonly object _modLock = new object();
        private static Dictionary<string, long> _msByMod = new Dictionary<string, long>();

        // assembly/type -> owning mod display name (built once at load)
        private static readonly Dictionary<Assembly, string> _asmToMod = new Dictionary<Assembly, string>();
        private static readonly Dictionary<Type, string> _typeToMod = new Dictionary<Type, string>();

        // ---- snapshot (written in Rollup, read by HUD/panel; all on main thread) ----
        public struct Row { public string mod; public float msPerSec; public int count; public bool measured; }
        public static Row[] TopByTime = Array.Empty<Row>();
        public static Row[] TopByCount = Array.Empty<Row>();
        public static float ThingsMsPerSec, GameCompMsPerSec, MapCompMsPerSec, WorldCompMsPerSec, ComponentsMsPerSec;
        public static bool ThingTimingOn;
        public static string TopLine = "";

        public static void BuildAssemblyMap()
        {
            try
            {
                _asmToMod.Clear(); _typeToMod.Clear();
                var mods = LoadedModManager.RunningModsListForReading;
                if (mods == null) return;
                foreach (var mod in mods)
                {
                    if (mod?.assemblies?.loadedAssemblies == null) continue;
                    string name = (mod.IsCoreMod || mod.IsOfficialMod) ? Vanilla : (mod.Name ?? Vanilla);
                    foreach (var asm in mod.assemblies.loadedAssemblies)
                        if (asm != null && !_asmToMod.ContainsKey(asm)) _asmToMod[asm] = name;
                }
                RDLog.Msg($"Perf: assembly→mod map built for {_asmToMod.Count} mod assemblies.");
            }
            catch (Exception e) { RDLog.Exception("TickAttribution.BuildAssemblyMap failed", e); }
        }

        // ---- hot-path notes ----
        public static void NoteThingTime(ModContentPack mcp, long swTicks)
        {
            Interlocked.Add(ref _thingsSw, swTicks);
            AddMod(ModName(mcp), swTicks);
        }

        public static void NoteComponentTime(object instance, long swTicks)
        {
            if (instance is MapComponent) Interlocked.Add(ref _mapCompSw, swTicks);
            else if (instance is WorldComponent) Interlocked.Add(ref _worldCompSw, swTicks);
            else Interlocked.Add(ref _gameCompSw, swTicks);
            AddMod(ModNameForType(instance?.GetType()), swTicks);
        }

        private static void AddMod(string mod, long swTicks)
        {
            lock (_modLock)
            {
                _msByMod.TryGetValue(mod, out var cur);
                _msByMod[mod] = cur + swTicks;
            }
        }

        private static string ModName(ModContentPack mcp)
        {
            if (mcp == null || mcp.IsCoreMod || mcp.IsOfficialMod) return Vanilla;
            return mcp.Name ?? Vanilla;
        }

        private static string ModNameForType(Type t)
        {
            if (t == null) return Vanilla;
            if (_typeToMod.TryGetValue(t, out var n)) return n;
            string name = Vanilla;
            try { if (_asmToMod.TryGetValue(t.Assembly, out var m)) name = m; } catch { }
            _typeToMod[t] = name;
            return name;
        }

        // ---- once/sec rollup (main thread, from PerfMonitor.Rollup) ----
        public static void Rollup(float dt, bool thingTimingOn)
        {
            ThingTimingOn = thingTimingOn;
            float inv = dt > 0f ? (float)(SwToMs / dt) : 0f;

            ThingsMsPerSec    = Interlocked.Exchange(ref _thingsSw, 0) * inv;
            GameCompMsPerSec  = Interlocked.Exchange(ref _gameCompSw, 0) * inv;
            MapCompMsPerSec   = Interlocked.Exchange(ref _mapCompSw, 0) * inv;
            WorldCompMsPerSec = Interlocked.Exchange(ref _worldCompSw, 0) * inv;
            ComponentsMsPerSec = GameCompMsPerSec + MapCompMsPerSec + WorldCompMsPerSec;

            Dictionary<string, long> snap;
            lock (_modLock) { snap = _msByMod; _msByMod = new Dictionary<string, long>(); }
            var timed = new List<Row>(snap.Count);
            foreach (var kv in snap)
                timed.Add(new Row { mod = kv.Key, msPerSec = kv.Value * inv, measured = true });
            timed.Sort((a, b) => b.msPerSec.CompareTo(a.msPerSec));
            TopByTime = Top(timed, 8);

            RefreshCounts();

            if (TopByTime.Length > 0 && TopByTime[0].msPerSec > 0.05f)
                TopLine = $"{Short(TopByTime[0].mod)} {TopByTime[0].msPerSec:0.0}ms/s";
            else if (TopByCount.Length > 0)
                TopLine = $"{Short(TopByCount[0].mod)} ({TopByCount[0].count})";
            else TopLine = "";
        }

        private static void RefreshCounts()
        {
            try
            {
                var counts = new Dictionary<string, int>();
                var maps = Find.Maps;
                if (maps != null)
                    foreach (var map in maps)
                    {
                        var all = map?.listerThings?.AllThings;
                        if (all == null) continue;
                        for (int i = 0; i < all.Count; i++)
                        {
                            var mcp = all[i]?.def?.modContentPack;
                            if (mcp == null || mcp.IsCoreMod || mcp.IsOfficialMod) continue;
                            string nm = mcp.Name ?? Vanilla;
                            counts.TryGetValue(nm, out var c);
                            counts[nm] = c + 1;
                        }
                    }
                var rows = new List<Row>(counts.Count);
                foreach (var kv in counts) rows.Add(new Row { mod = kv.Key, count = kv.Value, measured = false });
                rows.Sort((a, b) => b.count.CompareTo(a.count));
                TopByCount = Top(rows, 8);
            }
            catch (Exception e) { RDLog.Exception("TickAttribution.RefreshCounts failed", e); }
        }

        private static Row[] Top(List<Row> rows, int n)
            => rows.Count > n ? rows.GetRange(0, n).ToArray() : rows.ToArray();

        private static string Short(string s)
            => string.IsNullOrEmpty(s) ? "?" : (s.Length > 18 ? s.Substring(0, 17) + "…" : s);
    }
}
