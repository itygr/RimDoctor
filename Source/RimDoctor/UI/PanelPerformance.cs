using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// Performance panel: live TPS/FPS, a history sparkline, a plain-language verdict
    /// (is it simulation lag or graphics lag?), and a "where the time goes" breakdown
    /// that names the biggest levers — measured pathfinding cost plus the object counts
    /// that drive per-tick work.
    /// </summary>
    public class PanelPerformance : IRimDoctorPanel
    {
        public string Label => "RimDoctor.Tab.Perf".TranslateSafe("Performance");

        // throttled world counts
        private int mapCount, pawnsTotal, colonists, animals, hostiles;
        private int things, items, plants, filth, buildings, worldPawns;
        private float lastCount = -999f;

        public void OnSelected() { RecomputeCounts(); }

        public void Draw(Rect rect)
        {
            if (Time.realtimeSinceStartup - lastCount > 1.25f) RecomputeCounts();

            float y = rect.y;

            // ---- headline stat cards ----
            float cardW = (rect.width - 4 * 8f) / 5f, cardH = 64f;
            DrawStat(new Rect(rect.x + 0 * (cardW + 8f), y, cardW, cardH),
                "Ticks / sec", PerfMonitor.HasData ? PerfMonitor.Tps.ToString("0") : "—",
                PerfMonitor.Paused ? "paused" : ("target " + PerfMonitor.TargetTps), TpsColor());
            DrawStat(new Rect(rect.x + 1 * (cardW + 8f), y, cardW, cardH),
                "Frames / sec", PerfMonitor.HasData ? PerfMonitor.Fps.ToString("0") : "—",
                PerfMonitor.Fps >= 30 ? "smooth" : "low", FpsColor());
            DrawStat(new Rect(rect.x + 2 * (cardW + 8f), y, cardW, cardH),
                "Game speed", SpeedName(), "", new Color(0.8f, 0.85f, 1f));
            DrawStat(new Rect(rect.x + 3 * (cardW + 8f), y, cardW, cardH),
                "ms / tick", PerfMonitor.HasData ? PerfMonitor.MsPerTick.ToString("0.0") : "—",
                "sim cost", new Color(0.85f, 0.85f, 0.9f));
            DrawStat(new Rect(rect.x + 4 * (cardW + 8f), y, cardW, cardH),
                "Tick budget", PerfMonitor.Paused ? "—" : (PerfMonitor.BudgetUsed * 100f).ToString("0") + "%",
                "of available", BudgetColor());
            y += cardH + 10f;

            // ---- sparkline ----
            var spark = new Rect(rect.x, y, rect.width, 54f);
            DrawSparkline(spark);
            y = spark.yMax + 8f;

            // memory / GC line (GC churn = stutter)
            Text.Font = GameFont.Tiny;
            bool gcHot = PerfMonitor.Gc0PerSec >= 3f;
            GUI.color = gcHot ? new Color(1f, 0.85f, 0.45f) : new Color(0.72f, 0.72f, 0.72f);
            Widgets.Label(new Rect(rect.x, y, rect.width, 16f),
                $"Memory: {PerfMonitor.HeapMB:0} MB managed heap   ·   GC gen-0: {PerfMonitor.Gc0PerSec:0.0}/s"
                + (gcHot ? "   ⚠ frequent GC — can cause micro-stutter (a mod allocating heavily each tick)" : ""));
            GUI.color = Color.white; Text.Font = GameFont.Small;
            y += 18f;

            // ---- verdict ----
            string verdict; Color vcol;
            BuildVerdict(out verdict, out vcol);
            float vh = Text.CalcHeight(verdict, rect.width - 16f) + 12f;
            var vr = new Rect(rect.x, y, rect.width, vh);
            Widgets.DrawBoxSolid(vr, new Color(vcol.r, vcol.g, vcol.b, 0.12f));
            GUI.color = vcol;
            Widgets.Label(new Rect(vr.x + 8f, vr.y + 6f, vr.width - 16f, vh - 8f), verdict);
            GUI.color = Color.white;
            y = vr.yMax + 10f;

            // ---- where the time goes ----
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(rect.x, y, rect.width, 16f), "WHERE THE TIME GOES");
            GUI.color = Color.white; Text.Font = GameFont.Small;
            y += 18f;

            foreach (var c in Contributors())
            {
                var r = new Rect(rect.x, y, rect.width, 38f);
                Widgets.DrawBoxSolid(new Rect(r.x, r.y, 6f, 34f), c.color);
                Widgets.Label(new Rect(r.x + 14f, r.y, rect.width - 16f, 20f), $"<b>{c.title}</b>   {c.value}");
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.75f, 0.75f, 0.75f);
                Widgets.Label(new Rect(r.x + 14f, r.y + 18f, rect.width - 16f, 18f), c.note);
                GUI.color = Color.white; Text.Font = GameFont.Small;
                y += 40f;
            }

            // ---- BY MOD: tick cost ----
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(rect.x, y, rect.width - 220f, 16f),
                TickAttribution.ThingTimingOn
                    ? "BY MOD — MEASURED TICK COST (ms/s)"
                    : "BY MOD — BY OBJECT COUNT (enable detailed timing for ms)");
            GUI.color = Color.white; Text.Font = GameFont.Small;

            var st = RimDoctorMod.Instance?.Settings;
            if (st != null)
            {
                string tlabel = st.detailedThingTiming ? "Detailed timing: ON" : "Detailed timing: OFF";
                if (Widgets.ButtonText(new Rect(rect.xMax - 210f, y - 3f, 210f, 22f), tlabel))
                {
                    st.detailedThingTiming = !st.detailedThingTiming;
                    if (st.detailedThingTiming) Patch_Perf_ThingTick.Enable();
                    else Patch_Perf_ThingTick.Disable();
                }
            }
            y += 18f;

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 6f, y, rect.width - 12f, 16f),
                $"Components (always measured): {TickAttribution.ComponentsMsPerSec:0.0} ms/s   —   "
                + $"Game {TickAttribution.GameCompMsPerSec:0.0} · Map {TickAttribution.MapCompMsPerSec:0.0} · World {TickAttribution.WorldCompMsPerSec:0.0}");
            y += 16f;

            var rows = (TickAttribution.ThingTimingOn && TickAttribution.TopByTime.Length > 0)
                ? TickAttribution.TopByTime : TickAttribution.TopByCount;
            int shown = 0;
            foreach (var row in rows)
            {
                if (shown >= 6) break;
                string val = row.measured ? $"{row.msPerSec:0.0} ms/s" : $"{row.count} ticking things";
                Widgets.Label(new Rect(rect.x + 6f, y, rect.width - 12f, 16f), $"{shown + 1}. {row.mod}  —  {val}");
                y += 16f; shown++;
            }
            if (shown == 0)
                Widgets.Label(new Rect(rect.x + 6f, y, rect.width, 16f),
                    "(no data yet — unpause and play a few seconds)");
            Text.Font = GameFont.Small;

            // copy button (bottom-right)
            if (Widgets.ButtonText(new Rect(rect.xMax - 150f, rect.yMax - 30f, 150f, 28f),
                    "RimDoctor.Perf.Copy".TranslateSafe("Copy perf report")))
            {
                GUIUtility.systemCopyBuffer = BuildReport();
                Messages.Message("RimDoctor.Perf.Copied".TranslateSafe("Performance report copied."),
                    MessageTypeDefOf.TaskCompletion, false);
            }
        }

        // ---- pieces -----------------------------------------------------------
        private struct Contrib { public string title, value, note; public Color color; }

        private List<Contrib> Contributors()
        {
            var list = new List<Contrib>();
            float tickMsPerSec = PerfMonitor.MsPerTick * PerfMonitor.Tps;
            float pathShare = tickMsPerSec > 0.01f ? PerfMonitor.PathMsPerSec / tickMsPerSec : 0f;
            bool simBound = !PerfMonitor.Paused && PerfMonitor.TargetTps > 0
                            && PerfMonitor.Tps < PerfMonitor.TargetTps * 0.92f;

            Color hot = new Color(1f, 0.55f, 0.45f), warm = new Color(1f, 0.85f, 0.5f), cool = new Color(0.7f, 0.85f, 1f);

            list.Add(new Contrib {
                title = "Pathfinding",
                value = PerfMonitor.PathCallsPerSec > 0
                    ? $"{PerfMonitor.PathCallsPerSec:0}/s · {PerfMonitor.PathMsPerSec:0.0} ms/s ({pathShare * 100f:0}% of sim)"
                    : "n/a",
                note = "Movers solving routes. Spikes from big/maze maps, large raids & herds, blocked paths, door/region churn.",
                color = (simBound && pathShare > 0.30f) ? hot : cool
            });
            list.Add(new Contrib {
                title = "Pawns",
                value = $"{pawnsTotal} total · {colonists} colonists · {animals} animals · {hostiles} hostile",
                note = "Every pawn runs AI each tick. Big raids and animal herds are the usual #1 cost.",
                color = (simBound && (hostiles > 40 || pawnsTotal > 120)) ? hot : (pawnsTotal > 80 ? warm : cool)
            });
            list.Add(new Contrib {
                title = "Maps active",
                value = mapCount.ToString(),
                note = "Each map simulates fully even when off-screen. Abandon stale caravan/temporary maps.",
                color = (mapCount > 3) ? warm : cool
            });
            list.Add(new Contrib {
                title = "Things on maps",
                value = $"{things} total · {items} items · {plants} plants · {filth} filth",
                note = "Loose items, filth and plants all tick. Haul/clean, avoid stockpile hoarding, clear blight.",
                color = (simBound && things > 40000) ? hot : (things > 25000 ? warm : cool)
            });
            list.Add(new Contrib {
                title = "World pawns",
                value = worldPawns.ToString(),
                note = "Off-map characters tracked by the world. Some mods inflate this; very high counts add background cost.",
                color = (worldPawns > 1500) ? warm : cool
            });
            return list;
        }

        private void BuildVerdict(out string text, out Color color)
        {
            if (!PerfMonitor.HasData)
            { text = "Gathering data… give it a couple of seconds of play (unpause)."; color = new Color(0.7f, 0.7f, 0.7f); return; }
            if (PerfMonitor.Paused)
            { text = "⏸ Paused — no simulation is running, so TPS is 0. Unpause to measure lag."; color = new Color(0.7f, 0.7f, 0.7f); return; }

            bool simOk = PerfMonitor.Tps >= PerfMonitor.TargetTps * 0.92f;
            if (simOk)
            {
                if (PerfMonitor.Fps < 30f)
                { text = $"🖥 Simulation is fine ({PerfMonitor.Tps:0}/{PerfMonitor.TargetTps} TPS) but FPS is low ({PerfMonitor.Fps:0}). "
                       + "This is GRAPHICS lag, not simulation — too much being drawn or GPU-bound. Try fewer visual mods, lower resolution, or uncap framerate.";
                  color = new Color(0.7f, 0.85f, 1f); return; }
                text = $"✅ Running smoothly — {PerfMonitor.Tps:0}/{PerfMonitor.TargetTps} TPS, {PerfMonitor.Fps:0} FPS. No simulation lag at this speed.";
                color = new Color(0.6f, 1f, 0.6f); return;
            }

            text = $"⚠ Simulation can't keep up: {PerfMonitor.Tps:0} TPS vs {PerfMonitor.TargetTps} target "
                 + $"({PerfMonitor.BudgetUsed * 100f:0}% of the per-tick CPU budget used). This is SIMULATION lag — "
                 + "the breakdown below shows the biggest levers (red = likely culprit).";
            color = new Color(1f, 0.6f, 0.45f);
        }

        private void RecomputeCounts()
        {
            lastCount = Time.realtimeSinceStartup;
            mapCount = pawnsTotal = colonists = animals = hostiles = 0;
            things = items = plants = filth = buildings = 0;
            try
            {
                var player = Faction.OfPlayerSilentFail;
                var maps = Find.Maps;
                if (maps != null)
                {
                    mapCount = maps.Count;
                    foreach (var map in maps)
                    {
                        var spawned = map?.mapPawns?.AllPawnsSpawned;
                        if (spawned != null)
                            foreach (var p in spawned)
                            {
                                if (p == null) continue;
                                pawnsTotal++;
                                if (p.IsColonist) colonists++;
                                if (p.RaceProps != null && p.RaceProps.Animal) animals++;
                                if (player != null && p.HostileTo(player)) hostiles++;
                            }
                        var all = map?.listerThings?.AllThings;
                        if (all != null)
                        {
                            things += all.Count;
                            foreach (var t in all)
                            {
                                switch (t?.def?.category)
                                {
                                    case ThingCategory.Item: items++; break;
                                    case ThingCategory.Plant: plants++; break;
                                    case ThingCategory.Filth: filth++; break;
                                    case ThingCategory.Building: buildings++; break;
                                }
                            }
                        }
                    }
                }
                worldPawns = Find.WorldPawns?.AllPawnsAlive?.Count ?? 0;
            }
            catch (System.Exception e) { RDLog.Exception("Perf counts failed", e); }
        }

        private string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== RimDoctor performance report ===");
            sb.AppendLine($"TPS {PerfMonitor.Tps:0.0} / target {PerfMonitor.TargetTps}   FPS {PerfMonitor.Fps:0.0}   "
                        + $"ms/tick {PerfMonitor.MsPerTick:0.0}   budget {PerfMonitor.BudgetUsed * 100f:0}%   speed {SpeedName()}");
            sb.AppendLine($"Pathfinding: {PerfMonitor.PathCallsPerSec:0}/s, {PerfMonitor.PathMsPerSec:0.0} ms/s");
            sb.AppendLine($"Memory: {PerfMonitor.HeapMB:0} MB heap, GC gen-0 {PerfMonitor.Gc0PerSec:0.0}/s");
            sb.AppendLine($"Maps {mapCount}; Pawns {pawnsTotal} ({colonists} colonists, {animals} animals, {hostiles} hostile)");
            sb.AppendLine($"Things {things} ({items} items, {plants} plants, {filth} filth, {buildings} buildings); World pawns {worldPawns}");
            sb.AppendLine($"Components measured: {TickAttribution.ComponentsMsPerSec:0.0} ms/s "
                + $"(Game {TickAttribution.GameCompMsPerSec:0.0}, Map {TickAttribution.MapCompMsPerSec:0.0}, World {TickAttribution.WorldCompMsPerSec:0.0})");
            sb.AppendLine(TickAttribution.ThingTimingOn ? "By mod (measured ms/s):" : "By mod (object count):");
            var rep = (TickAttribution.ThingTimingOn && TickAttribution.TopByTime.Length > 0)
                ? TickAttribution.TopByTime : TickAttribution.TopByCount;
            foreach (var row in rep)
                sb.AppendLine(row.measured ? $"  {row.mod}: {row.msPerSec:0.0} ms/s" : $"  {row.mod}: {row.count} things");
            BuildVerdict(out var v, out _);
            sb.AppendLine("Verdict: " + v);
            return sb.ToString();
        }

        // ---- drawing helpers --------------------------------------------------
        private void DrawStat(Rect r, string label, string value, string sub, Color valColor)
        {
            Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.04f));
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(r.x + 8f, r.y + 4f, r.width - 10f, 16f), label);
            GUI.color = Color.white;
            Text.Font = GameFont.Medium;
            GUI.color = valColor;
            Widgets.Label(new Rect(r.x + 8f, r.y + 18f, r.width - 10f, 28f), value);
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(r.x + 8f, r.y + 44f, r.width - 10f, 16f), sub);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawSparkline(Rect r)
        {
            Widgets.DrawBoxSolid(r, new Color(0f, 0f, 0f, 0.10f));
            var hist = PerfMonitor.History(false);
            if (hist.Length < 2)
            {
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(r.x + 8f, r.y + 4f, r.width, 16f), "TPS history (fills as you play)");
                Text.Font = GameFont.Small;
                return;
            }
            float scale = Mathf.Max(PerfMonitor.TargetTps, 60f);
            foreach (var v in hist) if (v > scale) scale = v;

            int n = hist.Length;
            float bw = (r.width - 4f) / n;
            for (int i = 0; i < n; i++)
            {
                float frac = Mathf.Clamp01(hist[i] / scale);
                float h = frac * (r.height - 6f);
                var br = new Rect(r.x + 2f + i * bw, r.yMax - 3f - h, Mathf.Max(bw - 1f, 1f), h);
                Color c = hist[i] >= PerfMonitor.TargetTps * 0.92f ? new Color(0.45f, 0.9f, 0.5f)
                        : hist[i] >= PerfMonitor.TargetTps * 0.6f ? new Color(1f, 0.85f, 0.45f)
                        : new Color(1f, 0.5f, 0.45f);
                Widgets.DrawBoxSolid(br, c);
            }
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(r.x + 6f, r.y + 2f, 200f, 16f), $"TPS history (max {scale:0})");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private Color TpsColor()
        {
            if (PerfMonitor.Paused || !PerfMonitor.HasData) return new Color(0.7f, 0.7f, 0.7f);
            if (PerfMonitor.Tps >= PerfMonitor.TargetTps * 0.92f) return new Color(0.55f, 1f, 0.55f);
            if (PerfMonitor.Tps >= PerfMonitor.TargetTps * 0.6f) return new Color(1f, 0.85f, 0.45f);
            return new Color(1f, 0.5f, 0.45f);
        }
        private Color FpsColor()
        {
            if (!PerfMonitor.HasData) return new Color(0.7f, 0.7f, 0.7f);
            return PerfMonitor.Fps >= 30f ? new Color(0.55f, 1f, 0.55f) : new Color(1f, 0.85f, 0.45f);
        }
        private Color BudgetColor()
        {
            if (PerfMonitor.Paused) return new Color(0.7f, 0.7f, 0.7f);
            float b = PerfMonitor.BudgetUsed;
            if (b < 0.8f) return new Color(0.55f, 1f, 0.55f);
            if (b < 1f) return new Color(1f, 0.85f, 0.45f);
            return new Color(1f, 0.5f, 0.45f);
        }

        private static string SpeedName()
        {
            var tm = Find.TickManager;
            if (tm == null) return "—";
            if (tm.Paused) return "Paused";
            switch (tm.CurTimeSpeed)
            {
                case TimeSpeed.Normal: return "Normal";
                case TimeSpeed.Fast: return "Fast";
                case TimeSpeed.Superfast: return "Superfast";
                case TimeSpeed.Ultrafast: return "Ultra";
                default: return tm.CurTimeSpeed.ToString();
            }
        }
    }
}
