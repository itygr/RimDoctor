using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// Log Doctor panel: lists captured issues newest-first with plain-language
    /// meaning / cause / fix, the likely culprit mod, and a "copy report" button.
    /// </summary>
    public class PanelLogDoctor : IRimDoctorPanel
    {
        public string Label => "RimDoctor.Tab.LogDoctor".TranslateSafe("Log Doctor");

        private Vector2 scroll;
        private string filter = "";
        private bool onlyWithAdvice;

        // Cached view — rebuilt only when the captured set, filter, flags, or width
        // change (not every frame). IMGUI repaints continuously, so copying +
        // filtering the whole list each frame would be pure GC churn.
        private List<LogEntry> cachedEntries;
        private float cachedViewHeight;
        private int cacheVersion = -1;
        private string cacheFilter;
        private bool cacheOnlyAdvice;
        private float cacheWidth = -1f;

        public void OnSelected() { }

        private void EnsureCache(float contentWidth)
        {
            int v = LogDoctor.Version;
            if (cachedEntries != null && v == cacheVersion && cacheOnlyAdvice == onlyWithAdvice
                && cacheFilter == filter && Mathf.Approximately(cacheWidth, contentWidth))
                return;

            cachedEntries = Filtered(LogDoctor.Snapshot());
            cachedViewHeight = 0f;
            foreach (var e in cachedEntries) cachedViewHeight += RowHeight(e, contentWidth);

            cacheVersion = v;
            cacheFilter = filter;
            cacheOnlyAdvice = onlyWithAdvice;
            cacheWidth = contentWidth;
        }

        public void Draw(Rect rect)
        {
            // Toolbar
            var bar = new Rect(rect.x, rect.y, rect.width, 30f);
            float x = bar.x;
            if (Widgets.ButtonText(new Rect(x, bar.y, 130f, 28f),
                    "RimDoctor.LogDoctor.Copy".TranslateSafe("Copy report")))
            {
                GUIUtility.systemCopyBuffer = LogDoctor.BuildReport();
                Messages.Message("RimDoctor.LogDoctor.Copied".TranslateSafe("Log Doctor report copied to clipboard."),
                    MessageTypeDefOf.TaskCompletion, false);
            }
            x += 136f;
            if (Widgets.ButtonText(new Rect(x, bar.y, 130f, 28f),
                    "RimDoctor.LogDoctor.Reload".TranslateSafe("Reload rules")))
            {
                LogAdviceDatabase.LoadOrReload();
                Messages.Message("RimDoctor.LogDoctor.Reloaded".TranslateSafe(
                    $"Reloaded {LogAdviceDatabase.RuleCount} advice rule(s)."), MessageTypeDefOf.TaskCompletion, false);
            }
            x += 136f;
            if (Widgets.ButtonText(new Rect(x, bar.y, 100f, 28f),
                    "RimDoctor.LogDoctor.Clear".TranslateSafe("Clear")))
            {
                LogDoctor.Clear();
            }
            x += 110f;
            Widgets.CheckboxLabeled(new Rect(x, bar.y, 180f, 28f),
                "RimDoctor.LogDoctor.OnlyAdvice".TranslateSafe("Only explained"), ref onlyWithAdvice);

            // Filter field
            var filterRect = new Rect(rect.xMax - 230f, bar.y, 230f, 28f);
            filter = Widgets.TextField(filterRect, filter ?? "");

            // Summary line
            var summary = new Rect(rect.x, bar.yMax + 4f, rect.width, 22f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(summary, "RimDoctor.LogDoctor.Summary".TranslateSafe(
                $"{LogDoctor.IssueCount} unique issue(s) • {LogAdviceDatabase.RuleCount} advice rule(s) • "
                + $"{TextureSubstitutionLog.UniquePathCount} texture(s) substituted, "
                + $"{TextureSubstitutionLog.DrawTimeCatchCount} draw-time null(s) caught"));
            Text.Font = GameFont.Small;

            // List
            var listArea = new Rect(rect.x, summary.yMax + 4f, rect.width, rect.yMax - (summary.yMax + 4f));
            float contentWidth = listArea.width - 18f;
            EnsureCache(contentWidth);
            var entries = cachedEntries;
            var view = new Rect(0, 0, contentWidth, Mathf.Max(cachedViewHeight, listArea.height));

            Widgets.BeginScrollView(listArea, ref scroll, view);
            float y = 0f;
            foreach (var e in entries)
            {
                float rowH = RowHeight(e, contentWidth);
                // Cull: only draw rows intersecting the viewport (vanilla pattern).
                if (UiUtil.RowVisible(y, rowH, scroll.y, listArea.height))
                    DrawRow(new Rect(0, y, view.width, rowH), e);
                y += rowH;
            }
            if (entries.Count == 0)
            {
                Widgets.Label(new Rect(0, 0, view.width, 40f),
                    "RimDoctor.LogDoctor.None".TranslateSafe("No issues captured yet. Errors and warnings will appear here as they occur."));
            }
            Widgets.EndScrollView();
        }

        private List<LogEntry> Filtered(List<LogEntry> all)
        {
            var result = new List<LogEntry>();
            foreach (var e in all)
            {
                if (onlyWithAdvice && !e.HasAdvice) continue;
                if (!string.IsNullOrEmpty(filter))
                {
                    var f = filter.ToLowerInvariant();
                    bool hit = (e.rawMessage?.ToLowerInvariant().Contains(f) ?? false)
                               || (e.culpritMod?.ToLowerInvariant().Contains(f) ?? false)
                               || (e.advice?.meaning?.ToLowerInvariant().Contains(f) ?? false);
                    if (!hit) continue;
                }
                result.Add(e);
            }
            return result;
        }

        private float RowHeight(LogEntry e, float width)
        {
            // Cache the measured height — CalcHeight is expensive to run every frame.
            if (e.cachedHeight >= 0f && Mathf.Approximately(e.cachedForWidth, width))
                return e.cachedHeight;

            float h = 24f + Text.CalcHeight(e.rawMessage ?? "", width - 8f);
            if (e.HasAdvice)
                h += 22f * 3f + 6f;
            if (!string.IsNullOrEmpty(e.culpritMod))
                h += 22f;
            h += 12f;

            e.cachedHeight = h;
            e.cachedForWidth = width;
            return h;
        }

        private void DrawRow(Rect r, LogEntry e)
        {
            Widgets.DrawHighlightIfMouseover(r);
            var inner = r.ContractedBy(4f);
            float y = inner.y;

            // Header: severity chip + occurrence count
            GUI.color = SeverityColor(e.severity);
            var head = new Rect(inner.x, y, inner.width, 22f);
            string occ = e.occurrences > 1 ? $"  (x{e.occurrences})" : "";
            Widgets.Label(head, $"{e.severity.ToString().ToUpperInvariant()}{occ}");
            GUI.color = Color.white;
            y += 22f;

            // Raw message
            float mh = Text.CalcHeight(e.rawMessage ?? "", inner.width);
            Widgets.Label(new Rect(inner.x, y, inner.width, mh), e.rawMessage ?? "");
            y += mh + 2f;

            // Advice
            if (e.HasAdvice)
            {
                GUI.color = new Color(0.7f, 0.9f, 1f);
                Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "▸ " +
                    "RimDoctor.LogDoctor.Meaning".TranslateSafe("Meaning: ") + e.advice.meaning); y += 22f;
                Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "   " +
                    "RimDoctor.LogDoctor.Cause".TranslateSafe("Likely cause: ") + e.advice.likelyCause); y += 22f;
                GUI.color = new Color(0.7f, 1f, 0.7f);
                Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "   " +
                    "RimDoctor.LogDoctor.Fix".TranslateSafe("Fix: ") + e.advice.suggestedFix); y += 22f;
                GUI.color = Color.white;
            }

            if (!string.IsNullOrEmpty(e.culpritMod))
            {
                GUI.color = new Color(1f, 0.85f, 0.5f);
                Widgets.Label(new Rect(inner.x, y, inner.width, 22f),
                    "RimDoctor.LogDoctor.Culprit".TranslateSafe("Likely culprit: ") + e.culpritMod);
                GUI.color = Color.white;
            }

            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            Widgets.DrawLineHorizontal(r.x, r.yMax - 1f, r.width);
            GUI.color = Color.white;
        }

        private static Color SeverityColor(LogSeverity s)
        {
            switch (s)
            {
                case LogSeverity.Error: return new Color(1f, 0.45f, 0.45f);
                case LogSeverity.Warning: return new Color(1f, 0.85f, 0.4f);
                default: return new Color(0.7f, 0.85f, 1f);
            }
        }
    }
}
