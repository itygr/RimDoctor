using UnityEngine;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// Load Order panel — READ-ONLY. Computes a suggested load order and shows it
    /// diffed against your current order, with placement reasons and warnings
    /// (missing dependencies, incompatibilities, cycles). It is purely advisory:
    /// RimDoctor never writes ModsConfig.xml and never reorders your list. Use it
    /// as a guide and make changes yourself in the vanilla Mods screen.
    ///
    /// NOTE: the old "Apply &amp; Restart" auto-sort was removed — it called
    /// ModsConfig.SetActiveToList, which overwrote the entire active list and
    /// dropped any mod it couldn't place (that's what reset modlists). The
    /// suggestions stay; the destructive one-click apply is gone for good.
    /// </summary>
    public class PanelSorter : IRimDoctorPanel
    {
        public string Label => "RimDoctor.Tab.Sorter".TranslateSafe("Load Order");

        private SortResult result;
        private Vector2 scroll;
        private Vector2 warnScroll;

        public void OnSelected()
        {
            if (result == null) Recompute();
        }

        private void Recompute()
        {
            CommunityRules.LoadOrReload();
            result = LoadOrderSorter.Sort();
        }

        public void Draw(Rect rect)
        {
            // Toolbar — read-only actions only (no apply, nothing touches ModsConfig).
            var bar = new Rect(rect.x, rect.y, rect.width, 30f);
            float x = bar.x;
            if (Widgets.ButtonText(new Rect(x, bar.y, 150f, 28f),
                    "RimDoctor.Sorter.Sort".TranslateSafe("Check load order"))) Recompute();
            x += 156f;

            if (Widgets.ButtonText(new Rect(x, bar.y, 150f, 28f),
                    "RimDoctor.Sorter.Export".TranslateSafe("Export suggestion")) && result != null)
            {
                var p = LoadOrderApplier.Export(result.proposedPackageIds);
                Messages.Message(p != null
                    ? "RimDoctor.Sorter.Exported".TranslateSafe("Exported suggested mod list to " + p)
                    : "RimDoctor.Sorter.ExportFail".TranslateSafe("Export failed (see log)."),
                    MessageTypeDefOf.TaskCompletion, false);
            }
            x += 156f;
            if (Widgets.ButtonText(new Rect(x, bar.y, 130f, 28f),
                    "RimDoctor.Sorter.RefreshRules".TranslateSafe("Refresh rules")))
            {
                CommunityRules.LoadOrReload();
                Recompute();
                Messages.Message("RimDoctor.Sorter.RulesReloaded".TranslateSafe(
                    $"Reloaded {CommunityRules.RuleCount} community rule(s)."), MessageTypeDefOf.TaskCompletion, false);
            }

            if (result == null)
            {
                Widgets.Label(new Rect(rect.x, bar.yMax + 8f, rect.width, 40f),
                    "RimDoctor.Sorter.Prompt".TranslateSafe("Press 'Check load order' to compute a suggested order."));
                return;
            }

            // Read-only reassurance note.
            var note = new Rect(rect.x, bar.yMax + 4f, rect.width, 22f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.65f, 0.78f, 0.9f);
            Widgets.Label(note, "RimDoctor.Sorter.ReadOnly".TranslateSafe(
                "Suggestions only — RimDoctor never changes your mod list. Reorder it yourself in the Mods screen."));
            GUI.color = Color.white;

            // Status line
            bool changed = result.Changed;
            var status = new Rect(rect.x, note.yMax + 2f, rect.width, 22f);
            GUI.color = changed ? new Color(1f, 0.9f, 0.5f) : new Color(0.6f, 1f, 0.6f);
            Widgets.Label(status, changed
                ? "RimDoctor.Sorter.Diff".TranslateSafe($"Suggested order differs from your current order. {result.warnings.Count} warning(s).")
                : "RimDoctor.Sorter.Same".TranslateSafe("Your current order already matches the suggestion. ✅"));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            float topY = status.yMax + 4f;

            // Warnings panel (top, if any)
            float warnH = 0f;
            if (result.warnings.Count > 0)
            {
                warnH = Mathf.Min(120f, 8f + result.warnings.Count * 22f);
                var warnRect = new Rect(rect.x, topY, rect.width, warnH);
                Widgets.DrawBoxSolid(warnRect, new Color(0.25f, 0.15f, 0.15f, 0.5f));
                var wv = new Rect(0, 0, warnRect.width - 18f, result.warnings.Count * 22f);
                Widgets.BeginScrollView(warnRect, ref warnScroll, wv);
                float wy = 0f;
                foreach (var w in result.warnings)
                {
                    GUI.color = WarnColor(w.kind);
                    Widgets.Label(new Rect(4f, wy, wv.width - 8f, 22f), "⚠ " + w.text);
                    wy += 22f;
                }
                GUI.color = Color.white;
                Widgets.EndScrollView();
                topY += warnH + 6f;
            }

            // Diff list (proposed order vs current, with placement reasons)
            var listArea = new Rect(rect.x, topY, rect.width, rect.yMax - topY);
            var proposed = result.proposedOrder;
            const float rowH = 24f;
            var view = new Rect(0, 0, listArea.width - 18f, Mathf.Max(proposed.Count * rowH, listArea.height));
            Widgets.BeginScrollView(listArea, ref scroll, view);
            UiUtil.VisibleRange(scroll.y, listArea.height, rowH, proposed.Count, out int first, out int last);
            for (int i = first; i <= last; i++)
            {
                var mod = proposed[i];
                var row = new Rect(0, i * rowH, view.width, rowH);
                if (i % 2 == 0) Widgets.DrawLightHighlight(row);

                int curIdx = result.currentPackageIds.IndexOf(mod.PackageId);
                bool moved = curIdx != i;

                // index
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(row.x + 2f, row.y, 38f, 24f), (i + 1).ToString());
                GUI.color = Color.white;

                // moved indicator (how far this mod would move from where it is now)
                if (moved)
                {
                    int delta = curIdx - i;
                    GUI.color = delta > 0 ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.8f, 0.5f);
                    string arrow = delta > 0 ? "▲" : "▼";
                    Widgets.Label(new Rect(row.x + 40f, row.y, 50f, 24f), $"{arrow}{Mathf.Abs(delta)}");
                    GUI.color = Color.white;
                }

                Widgets.Label(new Rect(row.x + 94f, row.y, row.width * 0.45f, 24f), mod.Name);

                // reason
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Text.Font = GameFont.Tiny;
                var rr = new Rect(row.x + 94f + row.width * 0.45f + 6f, row.y + 2f, row.width * 0.4f, 22f);
                Widgets.Label(rr, result.ReasonFor(mod.PackageId) ?? "");
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                if (Mouse.IsOver(row))
                {
                    TooltipHandler.TipRegion(row, $"{mod.Name}\n{mod.PackageId}\n{result.ReasonFor(mod.PackageId)}");
                    Widgets.DrawHighlight(row);
                }
            }
            Widgets.EndScrollView();
        }

        private static Color WarnColor(WarningKind k)
        {
            switch (k)
            {
                case WarningKind.Cycle: return new Color(1f, 0.5f, 0.5f);
                case WarningKind.Incompatible: return new Color(1f, 0.6f, 0.6f);
                default: return new Color(1f, 0.85f, 0.5f);
            }
        }
    }
}
