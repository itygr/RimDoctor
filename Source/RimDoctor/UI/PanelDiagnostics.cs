using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// Diagnostics panel: the deep, actionable view. Two sub-views:
    ///   • Action items — every subsystem's findings, prioritized, with fixes.
    ///   • Live log — tail of RimDoctor's persistent diagnostic log.
    /// Plus copy/save full report and open-log-folder.
    /// </summary>
    public class PanelDiagnostics : IRimDoctorPanel
    {
        public string Label => "RimDoctor.Tab.Diag".TranslateSafe("Diagnostics");

        private enum View { Actions, Log }
        private View view = View.Actions;

        private List<ActionItem> items;
        private HarmonyReport harmony;
        private Vector2 scroll;
        private Vector2 logScroll;
        private readonly HashSet<int> expanded = new HashSet<int>();
        private ActionSeverity minSeverity = ActionSeverity.Info;

        public void OnSelected()
        {
            // Auto-populate the action list the first time the panel opens: run a
            // health scan + a sort if they haven't run yet, so every source is fed.
            try
            {
                if (ContentHealthScanner.LastResult == null)
                    ContentHealthScanner.Scan();
                if (LoadOrderSorter.Last == null)
                {
                    CommunityRules.LoadOrReload();
                    LoadOrderSorter.Sort();
                }
            }
            catch (System.Exception e)
            {
                RDLog.Exception("Diagnostics auto-populate failed", e);
            }
            Refresh();
        }

        private void Refresh()
        {
            harmony = HarmonyInsight.Collect();
            items = DiagnosticsAggregator.BuildActionItems(harmony);
            DiagnosticLog.Info("Diagnostics", $"Refreshed: {items.Count} action item(s), {harmony.conflicts.Count} harmony conflict(s).");
        }

        public void Draw(Rect rect)
        {
            // Toolbar
            var bar = new Rect(rect.x, rect.y, rect.width, 30f);
            float x = bar.x;
            if (Widgets.ButtonText(new Rect(x, bar.y, 110f, 28f),
                    "RimDoctor.Diag.Refresh".TranslateSafe("Refresh"))) Refresh();
            x += 116f;
            if (Widgets.ButtonText(new Rect(x, bar.y, 130f, 28f),
                    "RimDoctor.Diag.Copy".TranslateSafe("Copy report")))
            {
                GUIUtility.systemCopyBuffer = ReportBuilder.Build();
                Messages.Message("RimDoctor.Diag.Copied".TranslateSafe("Full diagnostic report copied to clipboard."),
                    MessageTypeDefOf.TaskCompletion, false);
            }
            x += 136f;
            if (Widgets.ButtonText(new Rect(x, bar.y, 130f, 28f),
                    "RimDoctor.Diag.Save".TranslateSafe("Save report")))
            {
                var p = ReportBuilder.Save();
                Messages.Message(p != null
                    ? "RimDoctor.Diag.Saved".TranslateSafe("Saved report to " + p)
                    : "RimDoctor.Diag.SaveFail".TranslateSafe("Save failed (see log)."),
                    MessageTypeDefOf.TaskCompletion, false);
            }
            x += 136f;
            if (Widgets.ButtonText(new Rect(x, bar.y, 130f, 28f),
                    "RimDoctor.Diag.OpenFolder".TranslateSafe("Open log folder")))
                DiagnosticLog.OpenFolder();

            // View toggle (right)
            if (Widgets.ButtonText(new Rect(rect.xMax - 200f, bar.y, 95f, 28f),
                    view == View.Actions ? "● Actions" : "Actions")) view = View.Actions;
            if (Widgets.ButtonText(new Rect(rect.xMax - 100f, bar.y, 95f, 28f),
                    view == View.Log ? "● Live log" : "Live log")) view = View.Log;

            var body = new Rect(rect.x, bar.yMax + 6f, rect.width, rect.yMax - (bar.yMax + 6f));
            if (view == View.Actions) DrawActions(body);
            else DrawLog(body);
        }

        private void DrawActions(Rect rect)
        {
            if (items == null) Refresh();

            // Severity filter chips
            var fbar = new Rect(rect.x, rect.y, rect.width, 26f);
            float fx = fbar.x;
            Widgets.Label(new Rect(fx, fbar.y + 2f, 64f, 24f), "Min:");
            fx += 56f;
            foreach (ActionSeverity sev in new[] { ActionSeverity.Info, ActionSeverity.Medium, ActionSeverity.High, ActionSeverity.Critical })
            {
                var r = new Rect(fx, fbar.y, 86f, 24f);
                if (minSeverity == sev) Widgets.DrawHighlightSelected(r);
                if (Widgets.ButtonText(r, sev.ToString())) minSeverity = sev;
                fx += 90f;
            }

            // Counts
            int crit = 0, high = 0, med = 0, info = 0;
            foreach (var it in items)
                switch (it.severity)
                {
                    case ActionSeverity.Critical: crit++; break;
                    case ActionSeverity.High: high++; break;
                    case ActionSeverity.Medium: med++; break;
                    default: info++; break;
                }
            var sumRect = new Rect(rect.x, fbar.yMax + 2f, rect.width, 20f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(sumRect, $"⛔ {crit} critical   ⚠ {high} high   ◆ {med} medium   • {info} info   "
                + $"(Harmony: {harmony?.patchedMethods ?? 0} methods, {harmony?.conflicts.Count ?? 0} multi-mod)");
            Text.Font = GameFont.Small;

            var listArea = new Rect(rect.x, sumRect.yMax + 4f, rect.width, rect.yMax - (sumRect.yMax + 4f));

            var shown = new List<ActionItem>();
            foreach (var it in items)
                if ((int)it.severity >= (int)minSeverity) shown.Add(it);

            float viewH = 0f;
            for (int i = 0; i < shown.Count; i++) viewH += RowHeight(shown[i], i);
            var v = new Rect(0, 0, listArea.width - 18f, Mathf.Max(viewH, listArea.height));
            Widgets.BeginScrollView(listArea, ref scroll, v);
            float y = 0f;
            for (int i = 0; i < shown.Count; i++)
            {
                float h = RowHeight(shown[i], i);
                DrawActionRow(new Rect(0, y, v.width, h), shown[i], i);
                y += h;
            }
            if (shown.Count == 0)
                Widgets.Label(new Rect(0, 0, v.width, 40f),
                    "RimDoctor.Diag.Clean".TranslateSafe("Nothing at this severity. Run a Health scan + Sort for the fullest picture."));
            Widgets.EndScrollView();
        }

        private float RowHeight(ActionItem it, int i)
        {
            float h = 28f;
            if (expanded.Contains(i))
            {
                if (!string.IsNullOrEmpty(it.detail)) h += Text.CalcHeight(it.detail, 920f);
                if (!string.IsNullOrEmpty(it.suggestion)) h += 22f;
                if (!string.IsNullOrEmpty(it.culpritMod)) h += 22f;
                h += 6f;
            }
            return h + 4f;
        }

        private void DrawActionRow(Rect r, ActionItem it, int i)
        {
            Widgets.DrawHighlightIfMouseover(r);
            var header = new Rect(r.x + 4f, r.y, r.width - 8f, 26f);
            if (Widgets.ButtonInvisible(header))
            {
                if (!expanded.Remove(i)) expanded.Add(i);
            }

            GUI.color = SevColor(it.severity);
            string caret = expanded.Contains(i) ? "▾" : "▸";
            Widgets.Label(header, $"{caret} [{it.SeverityLabel}] ({it.source}) {it.title}");
            GUI.color = Color.white;

            float y = r.y + 28f;
            if (expanded.Contains(i))
            {
                if (!string.IsNullOrEmpty(it.culpritMod))
                {
                    GUI.color = new Color(1f, 0.85f, 0.5f);
                    Widgets.Label(new Rect(r.x + 22f, y, r.width - 26f, 22f), "Likely culprit: " + it.culpritMod);
                    GUI.color = Color.white; y += 22f;
                }
                if (!string.IsNullOrEmpty(it.detail))
                {
                    float dh = Text.CalcHeight(it.detail, r.width - 26f);
                    Widgets.Label(new Rect(r.x + 22f, y, r.width - 26f, dh), it.detail);
                    y += dh;
                }
                if (!string.IsNullOrEmpty(it.suggestion))
                {
                    GUI.color = new Color(0.7f, 1f, 0.7f);
                    Widgets.Label(new Rect(r.x + 22f, y, r.width - 26f, 22f), "→ " + it.suggestion);
                    GUI.color = Color.white; y += 22f;
                }
            }

            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawLineHorizontal(r.x, r.yMax - 1f, r.width);
            GUI.color = Color.white;
        }

        private void DrawLog(Rect rect)
        {
            var lines = DiagnosticLog.Snapshot();
            var v = new Rect(0, 0, rect.width - 18f, Mathf.Max(lines.Count * 18f, rect.height));
            Widgets.BeginScrollView(rect, ref logScroll, v);
            Text.Font = GameFont.Tiny;
            float y = 0f;
            foreach (var l in lines)
            {
                GUI.color = LevelColor(l.level);
                Widgets.Label(new Rect(0, y, v.width, 18f), $"{l.stamp} [{l.category}] {l.message}");
                y += 18f;
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.EndScrollView();
        }

        private static Color SevColor(ActionSeverity s)
        {
            switch (s)
            {
                case ActionSeverity.Critical: return new Color(1f, 0.4f, 0.4f);
                case ActionSeverity.High: return new Color(1f, 0.6f, 0.45f);
                case ActionSeverity.Medium: return new Color(1f, 0.85f, 0.45f);
                default: return new Color(0.7f, 0.85f, 1f);
            }
        }

        private static Color LevelColor(DiagLevel l)
        {
            switch (l)
            {
                case DiagLevel.Error: return new Color(1f, 0.5f, 0.5f);
                case DiagLevel.Warn: return new Color(1f, 0.85f, 0.45f);
                case DiagLevel.Trace: return new Color(0.6f, 0.6f, 0.6f);
                default: return new Color(0.85f, 0.9f, 1f);
            }
        }
    }
}
