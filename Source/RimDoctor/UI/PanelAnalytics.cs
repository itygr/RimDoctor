using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// Analytics panel: static/structural game analytics distinct from the live
    /// Performance tab. Two views:
    ///   • Load weight — per-mod def/assembly counts (what makes startup heavy) + the
    ///     real RimDoctor-witnessed load span.
    ///   • Save &amp; world — in-memory bloat: things by mod, items/filth/plants, world
    ///     pawns by race-mod, maps, factions, and the newest save file's size on disk.
    /// </summary>
    public class PanelAnalytics : IRimDoctorPanel
    {
        public string Label => "RimDoctor.Tab.Analytics".TranslateSafe("Analytics");

        private enum View { Load, Save }
        private View view = View.Load;
        private Vector2 loadScroll, saveScroll;
        private float lastSave = -999f;

        public void OnSelected()
        {
            if (!StartupAnalytics.Ready) StartupAnalytics.Collect();
            CollectSaveThrottled(true);
        }

        private void CollectSaveThrottled(bool force)
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            if (force || Time.realtimeSinceStartup - lastSave > 3f)
            {
                lastSave = Time.realtimeSinceStartup;
                SaveAnalytics.Collect();
            }
        }

        public void Draw(Rect rect)
        {
            var bar = new Rect(rect.x, rect.y, rect.width, 30f);
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 130f, 28f), view == View.Load ? "● Load weight" : "Load weight"))
                view = View.Load;
            if (Widgets.ButtonText(new Rect(bar.x + 136f, bar.y, 130f, 28f), view == View.Save ? "● Save & world" : "Save & world"))
            { view = View.Save; CollectSaveThrottled(true); }

            if (Widgets.ButtonText(new Rect(rect.xMax - 150f, bar.y, 150f, 28f),
                    "RimDoctor.Analytics.Copy".TranslateSafe("Copy analytics")))
            {
                GUIUtility.systemCopyBuffer = BuildReport();
                Messages.Message("RimDoctor.Analytics.Copied".TranslateSafe("Analytics copied to clipboard."),
                    MessageTypeDefOf.TaskCompletion, false);
            }

            var body = new Rect(rect.x, bar.yMax + 6f, rect.width, rect.yMax - (bar.yMax + 6f));
            if (view == View.Load) DrawLoad(body);
            else DrawSave(body);
        }

        private void DrawLoad(Rect rect)
        {
            if (!StartupAnalytics.Ready) StartupAnalytics.Collect();

            var head = new Rect(rect.x, rect.y, rect.width, 40f);
            Text.Font = GameFont.Small;
            Widgets.Label(head, $"<b>{StartupAnalytics.TotalMods}</b> mods   ·   <b>{StartupAnalytics.TotalDefs:n0}</b> defs   ·   "
                + $"<b>{StartupAnalytics.TotalAssemblies}</b> C# assemblies   ·   load span "
                + $"<b>{StartupAnalytics.LoadSpanSeconds:0.0}s</b> (as RimDoctor saw it)");
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(rect.x, head.yMax - 16f, rect.width, 16f),
                "Mods ranked by def count — the actionable proxy for XML/startup load weight. C# assemblies add JIT + patch cost.");
            GUI.color = Color.white; Text.Font = GameFont.Small;

            var list = StartupAnalytics.ByMod;
            var area = new Rect(rect.x, head.yMax + 4f, rect.width, rect.yMax - (head.yMax + 4f));
            const float rowH = 22f;
            int max = list?.Count ?? 0;
            int maxDefs = (max > 0) ? Mathf.Max(1, list[0].defs) : 1;
            var v = new Rect(0, 0, area.width - 18f, Mathf.Max(max * rowH, area.height));
            Widgets.BeginScrollView(area, ref loadScroll, v);
            UiUtil.VisibleRange(loadScroll.y, area.height, rowH, max, out int first, out int last);
            for (int i = first; i <= last && i < max; i++)
            {
                var m = list[i];
                var r = new Rect(0, i * rowH, v.width, rowH);
                if (i % 2 == 0) Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.03f));
                // weight bar
                float frac = (float)m.defs / maxDefs;
                Widgets.DrawBoxSolid(new Rect(r.x, r.y + 3f, frac * 120f, rowH - 6f), new Color(0.35f, 0.7f, 0.9f, 0.5f));
                Widgets.Label(new Rect(r.x + 6f, r.y, 130f, rowH), $"{m.defs:n0} defs");
                Widgets.Label(new Rect(r.x + 140f, r.y, 70f, rowH), m.assemblies > 0 ? $"{m.assemblies} asm" : "");
                Widgets.Label(new Rect(r.x + 214f, r.y, v.width - 218f, rowH), m.mod);
            }
            Widgets.EndScrollView();
        }

        private void DrawSave(Rect rect)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                Widgets.Label(rect, "Load a colony to see save & world analytics.");
                return;
            }
            CollectSaveThrottled(false);

            var head = new Rect(rect.x, rect.y, rect.width, 56f);
            Text.Font = GameFont.Small;
            string size = SaveAnalytics.SaveFileBytes > 0
                ? $"{SaveAnalytics.SaveFileBytes / (1024f * 1024f):0.0} MB ({SaveAnalytics.SaveFileName})"
                : "—";
            Widgets.Label(new Rect(head.x, head.y, head.width, 22f),
                $"<b>{SaveAnalytics.TotalThings:n0}</b> things   ·   <b>{SaveAnalytics.WorldPawns:n0}</b> world pawns   ·   "
                + $"<b>{SaveAnalytics.Maps}</b> maps   ·   <b>{SaveAnalytics.Factions}</b> factions   ·   newest save <b>{size}</b>");
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.72f, 0.72f);
            Widgets.Label(new Rect(head.x, head.y + 24f, head.width, 16f),
                $"items {SaveAnalytics.Items:n0} · filth {SaveAnalytics.Filth:n0} · plants {SaveAnalytics.Plants:n0} · "
                + $"buildings {SaveAnalytics.Buildings:n0} · pawns {SaveAnalytics.Pawns:n0}"
                + (SaveAnalytics.Filth > 3000 || SaveAnalytics.Items > 20000 || SaveAnalytics.WorldPawns > 1500
                    ? "   ⚠ high — clean filth / haul or forbid loose items / cull world pawns to shrink the save" : ""));
            GUI.color = Color.white; Text.Font = GameFont.Small;

            // two columns: things-by-mod, world-pawns-by-mod
            var area = new Rect(rect.x, head.yMax + 4f, rect.width, rect.yMax - (head.yMax + 4f));
            float colW = (area.width - 12f) / 2f;
            DrawCountList(new Rect(area.x, area.y, colW, area.height), "Things by mod", SaveAnalytics.ThingsByMod, ref saveScroll);
            DrawCountList(new Rect(area.x + colW + 12f, area.y, colW, area.height), "World pawns by source", SaveAnalytics.WorldPawnsByMod, ref saveScroll2);
        }

        private Vector2 saveScroll2;

        private void DrawCountList(Rect rect, string title, System.Collections.Generic.List<SaveAnalytics.ModCount> list, ref Vector2 scroll)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 16f), title.ToUpperInvariant());
            GUI.color = Color.white; Text.Font = GameFont.Small;

            var inner = new Rect(rect.x, rect.y + 18f, rect.width, rect.height - 18f);
            int max = list?.Count ?? 0;
            int top = (max > 0) ? Mathf.Max(1, list[0].count) : 1;
            const float rowH = 22f;
            var v = new Rect(0, 0, inner.width - 18f, Mathf.Max(max * rowH, inner.height));
            Widgets.BeginScrollView(inner, ref scroll, v);
            for (int i = 0; i < max; i++)
            {
                var m = list[i];
                var r = new Rect(0, i * rowH, v.width, rowH);
                float frac = (float)m.count / top;
                Widgets.DrawBoxSolid(new Rect(r.x, r.y + 3f, frac * (v.width * 0.45f), rowH - 6f), new Color(0.9f, 0.7f, 0.35f, 0.45f));
                Widgets.Label(new Rect(r.x + 6f, r.y, 80f, rowH), $"{m.count:n0}");
                Widgets.Label(new Rect(r.x + 90f, r.y, v.width - 94f, rowH), m.mod);
            }
            if (max == 0)
                Widgets.Label(new Rect(0, 0, v.width, 22f), "(none)");
            Widgets.EndScrollView();
        }

        private string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== RimDoctor analytics ===");
            sb.AppendLine($"Load: {StartupAnalytics.TotalMods} mods, {StartupAnalytics.TotalDefs} defs, "
                + $"{StartupAnalytics.TotalAssemblies} assemblies, span {StartupAnalytics.LoadSpanSeconds:0.0}s");
            sb.AppendLine("Heaviest mods by def count:");
            var bm = StartupAnalytics.ByMod;
            if (bm != null)
                for (int i = 0; i < bm.Count && i < 15; i++)
                    sb.AppendLine($"  {bm[i].defs,6} defs  {(bm[i].assemblies > 0 ? bm[i].assemblies + " asm  " : "       ")}{bm[i].mod}");
            if (Current.ProgramState == ProgramState.Playing)
            {
                sb.AppendLine($"Save/world: {SaveAnalytics.TotalThings} things ({SaveAnalytics.Items} items, "
                    + $"{SaveAnalytics.Filth} filth, {SaveAnalytics.Plants} plants), {SaveAnalytics.WorldPawns} world pawns, "
                    + $"{SaveAnalytics.Maps} maps, {SaveAnalytics.Factions} factions; newest save {SaveAnalytics.SaveFileBytes / (1024f * 1024f):0.0} MB");
                sb.AppendLine("Things by mod:");
                foreach (var m in SaveAnalytics.ThingsByMod) sb.AppendLine($"  {m.count,6}  {m.mod}");
            }
            return sb.ToString();
        }
    }
}
