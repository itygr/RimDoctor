using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// Load Order panel: computes a proposed order, shows it diffed against the
    /// current order with placement reasons + warnings, and applies via
    /// ModsConfig + restart (after confirmation and a backup).
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
            // Toolbar
            var bar = new Rect(rect.x, rect.y, rect.width, 30f);
            float x = bar.x;
            if (Widgets.ButtonText(new Rect(x, bar.y, 130f, 28f),
                    "RimDoctor.Sorter.Sort".TranslateSafe("Sort now"))) Recompute();
            x += 136f;

            bool changed = result != null && result.Changed;
            GUI.color = changed ? new Color(0.6f, 1f, 0.6f) : Color.gray;
            if (Widgets.ButtonText(new Rect(x, bar.y, 170f, 28f),
                    "RimDoctor.Sorter.Apply".TranslateSafe("Apply & Restart")) && changed)
                ConfirmApply();
            GUI.color = Color.white;
            x += 176f;

            if (Widgets.ButtonText(new Rect(x, bar.y, 110f, 28f),
                    "RimDoctor.Sorter.Export".TranslateSafe("Export")) && result != null)
            {
                var p = LoadOrderApplier.Export(result.proposedPackageIds);
                Messages.Message(p != null
                    ? "RimDoctor.Sorter.Exported".TranslateSafe("Exported mod list to " + p)
                    : "RimDoctor.Sorter.ExportFail".TranslateSafe("Export failed (see log)."),
                    MessageTypeDefOf.TaskCompletion, false);
            }
            x += 116f;
            if (Widgets.ButtonText(new Rect(x, bar.y, 110f, 28f),
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
                    "RimDoctor.Sorter.Prompt".TranslateSafe("Press 'Sort now' to compute a proposed load order."));
                return;
            }

            // Status line
            var status = new Rect(rect.x, bar.yMax + 4f, rect.width, 22f);
            Text.Font = GameFont.Tiny;
            GUI.color = changed ? new Color(1f, 0.9f, 0.5f) : new Color(0.6f, 1f, 0.6f);
            Widgets.Label(status, changed
                ? "RimDoctor.Sorter.Diff".TranslateSafe($"Proposed order differs from current. {result.warnings.Count} warning(s).")
                : "RimDoctor.Sorter.Same".TranslateSafe("Your current order already matches the proposed order. ✅"));
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

            // Diff list
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

                // moved indicator
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

        private void ConfirmApply()
        {
            string msg = "RimDoctor.Sorter.Confirm".TranslateSafe(
                "Apply the proposed load order and restart RimWorld now?\n\n" +
                "• ModsConfig.xml will be backed up first.\n" +
                "• The game will close and relaunch with the new order.\n" +
                "• Save your game before continuing if needed.");
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(msg, () =>
            {
                if (!LoadOrderApplier.ApplyAndRestart(result))
                    Messages.Message("RimDoctor.Sorter.ApplyFail".TranslateSafe(
                        "Apply failed — load order unchanged (see log)."), MessageTypeDefOf.RejectInput, false);
            }, destructive: false));
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
