using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// Content Health panel: runs the scanner on demand and shows a grouped,
    /// searchable, color-coded report (clean / missing textures / likely broken),
    /// with an export-to-clipboard button.
    /// </summary>
    public class PanelHealth : IRimDoctorPanel
    {
        public string Label => "RimDoctor.Tab.Health".TranslateSafe("Content Health");

        private Vector2 scroll;
        private string filter = "";
        private bool hideClean = true;
        private readonly HashSet<string> expanded = new HashSet<string>();

        public void OnSelected() { }

        public void Draw(Rect rect)
        {
            var result = ContentHealthScanner.LastResult;

            // Toolbar
            var bar = new Rect(rect.x, rect.y, rect.width, 30f);
            float x = bar.x;
            if (Widgets.ButtonText(new Rect(x, bar.y, 130f, 28f),
                    "RimDoctor.Health.Scan".TranslateSafe("Scan now")))
            {
                result = ContentHealthScanner.Scan();
                Messages.Message("RimDoctor.Health.Done".TranslateSafe(
                    $"Scan complete: {result.MissingMods} mod(s) with missing textures, {result.BrokenMods} likely broken."),
                    MessageTypeDefOf.TaskCompletion, false);
            }
            x += 136f;
            if (result != null && Widgets.ButtonText(new Rect(x, bar.y, 130f, 28f),
                    "RimDoctor.Health.Export".TranslateSafe("Copy report")))
            {
                GUIUtility.systemCopyBuffer = ContentHealthScanner.BuildReport(result);
                Messages.Message("RimDoctor.Health.Copied".TranslateSafe("Health report copied to clipboard."),
                    MessageTypeDefOf.TaskCompletion, false);
            }
            x += 136f;
            Widgets.CheckboxLabeled(new Rect(x, bar.y, 150f, 28f),
                "RimDoctor.Health.HideClean".TranslateSafe("Hide clean"), ref hideClean);

            var filterRect = new Rect(rect.xMax - 230f, bar.y, 230f, 28f);
            filter = Widgets.TextField(filterRect, filter ?? "");

            // Repair bar (row 2)
            var rbar = new Rect(rect.x, bar.yMax + 4f, rect.width, 28f);
            DrawRepairBar(rbar, result);

            // Summary
            var summary = new Rect(rect.x, rbar.yMax + 4f, rect.width, 22f);
            if (result == null)
            {
                Widgets.Label(summary, "RimDoctor.Health.Prompt".TranslateSafe(
                    "Press 'Scan now' to check all active mods for missing textures and incomplete downloads."));
                return;
            }
            Text.Font = GameFont.Tiny;
            Widgets.Label(summary, "RimDoctor.Health.SummaryLine".TranslateSafe(
                $"{result.defsScanned} defs scanned • ✅ {result.CleanMods} clean • ⚠ {result.MissingMods} missing textures • ⛔ {result.BrokenMods} likely broken"));
            Text.Font = GameFont.Small;

            // List
            var listArea = new Rect(rect.x, summary.yMax + 4f, rect.width, rect.yMax - (summary.yMax + 4f));
            var reports = Filtered(result.reports);

            float viewH = 0f;
            foreach (var rep in reports) viewH += RowHeight(rep);
            var view = new Rect(0, 0, listArea.width - 18f, Mathf.Max(viewH, listArea.height));

            Widgets.BeginScrollView(listArea, ref scroll, view);
            float y = 0f;
            foreach (var rep in reports)
            {
                float h = RowHeight(rep);
                if (UiUtil.RowVisible(y, h, scroll.y, listArea.height))
                    DrawRow(new Rect(0, y, view.width, h), rep);
                y += h;
            }
            if (reports.Count == 0)
                Widgets.Label(new Rect(0, 0, view.width, 40f),
                    "RimDoctor.Health.AllClean".TranslateSafe("Nothing to show. Everything scanned looks clean!"));
            Widgets.EndScrollView();
        }

        private string lastRepairNote;

        private void DrawRepairBar(Rect rbar, HealthScanResult result)
        {
            var tier = RimDoctorMod.Instance?.Settings?.repairTier ?? RepairTier.ReportOnly;
            float x = rbar.x;

            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            Widgets.Label(new Rect(x, rbar.y + 3f, 230f, 24f),
                "RimDoctor.Health.Tier".TranslateSafe("Repair tier: ") + TierLabel(tier)
                + "  " + "RimDoctor.Health.TierHint".TranslateSafe("(change in Mod Settings)"));
            GUI.color = Color.white;
            x += 360f;

            bool canRepair = result != null && tier != RepairTier.ReportOnly && result.MissingMods > 0;
            GUI.color = canRepair ? new Color(0.6f, 1f, 0.6f) : Color.gray;
            if (Widgets.ButtonText(new Rect(x, rbar.y, 150f, 26f),
                    "RimDoctor.Health.Repair".TranslateSafe("Run repairs")) && result != null)
                ConfirmRepair(tier, result);
            GUI.color = Color.white;
            x += 156f;

            if (Widgets.ButtonText(new Rect(x, rbar.y, 150f, 26f),
                    "RimDoctor.Health.Undo".TranslateSafe("Undo last repair")))
            {
                bool ok = RepairEngine.UndoLast();
                lastRepairNote = ok
                    ? "RimDoctor.Health.Undone".TranslateSafe("Removed the generated RimDoctor Overrides mod. Restart to apply.")
                    : "RimDoctor.Health.NothingUndo".TranslateSafe("Nothing to undo (no generated override mod found).");
                Messages.Message(lastRepairNote, MessageTypeDefOf.TaskCompletion, false);
            }
            x += 156f;

            if (!string.IsNullOrEmpty(lastRepairNote))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.7f, 1f, 0.7f);
                Widgets.Label(new Rect(x, rbar.y + 3f, rbar.xMax - x, 24f), lastRepairNote);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
        }

        private void ConfirmRepair(RepairTier tier, HealthScanResult result)
        {
            string msg = "RimDoctor.Health.ConfirmRepair".TranslateSafe(
                $"Run {TierLabel(tier)} repairs?\n\n" +
                "• Fixes are written ONLY into a generated 'RimDoctor Overrides' mod — your other mods are never touched.\n" +
                "• Any existing override mod is backed up first.\n" +
                "• You'll need to enable that mod (it loads last) and restart.");
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(msg, () =>
            {
                var summary = RepairEngine.Run(tier, result);
                lastRepairNote = string.Join("  ", summary.notes.ToArray());
                if (summary.didWrite)
                    PromptRestart();
            }, destructive: false));
        }

        private void PromptRestart()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "RimDoctor.Health.RestartPrompt".TranslateSafe(
                    "Repairs written. Enable 'RimDoctor Overrides' and restart RimWorld now?\n\n" +
                    "RimWorld only loads mods at startup, so the fixes take effect after a restart. " +
                    "RimDoctor will enable the override mod (it loads last) for you."),
                () =>
                {
                    try
                    {
                        OverrideModWriter.RegisterAndActivate();
                        GenCommandLine.Restart();
                    }
                    catch { }
                },
                destructive: false));
        }

        private static string TierLabel(RepairTier t)
        {
            switch (t)
            {
                case RepairTier.SafeAutoFix: return "Safe auto-fix";
                case RepairTier.Maximum: return "Maximum";
                default: return "Report only";
            }
        }

        private List<ModHealthReport> Filtered(List<ModHealthReport> all)
        {
            var res = new List<ModHealthReport>();
            foreach (var r in all)
            {
                if (hideClean && r.status == HealthStatus.Clean) continue;
                if (!string.IsNullOrEmpty(filter))
                {
                    var f = filter.ToLowerInvariant();
                    if (!(r.modName.ToLowerInvariant().Contains(f) || r.packageId.ToLowerInvariant().Contains(f)))
                        continue;
                }
                res.Add(r);
            }
            return res;
        }

        private float RowHeight(ModHealthReport rep)
        {
            float h = 30f; // header
            if (!string.IsNullOrEmpty(rep.note)) h += 36f;
            if (expanded.Contains(rep.packageId))
                h += Mathf.Min(rep.MissingCount, 200) * 20f;
            return h + 6f;
        }

        private void DrawRow(Rect r, ModHealthReport rep)
        {
            Widgets.DrawHighlightIfMouseover(r);
            var inner = r.ContractedBy(4f);
            float y = inner.y;

            string icon; Color col;
            switch (rep.status)
            {
                case HealthStatus.LikelyBroken: icon = "⛔"; col = new Color(1f, 0.45f, 0.45f); break;
                case HealthStatus.MissingTextures: icon = "⚠"; col = new Color(1f, 0.85f, 0.4f); break;
                default: icon = "✅"; col = new Color(0.6f, 1f, 0.6f); break;
            }

            var header = new Rect(inner.x, y, inner.width, 26f);
            if (rep.MissingCount > 0 && Widgets.ButtonInvisible(header))
            {
                if (!expanded.Remove(rep.packageId)) expanded.Add(rep.packageId);
            }
            GUI.color = col;
            string extra = rep.status == HealthStatus.MissingTextures ? $"  —  {rep.MissingCount} missing" : "";
            string caret = rep.MissingCount > 0 ? (expanded.Contains(rep.packageId) ? "▾ " : "▸ ") : "   ";
            Widgets.Label(header, $"{caret}{icon}  {rep.modName}{extra}");
            GUI.color = Color.white;
            y += 28f;

            if (!string.IsNullOrEmpty(rep.note))
            {
                GUI.color = new Color(1f, 0.8f, 0.8f);
                var nr = new Rect(inner.x + 18f, y, inner.width - 18f, 34f);
                Widgets.Label(nr, rep.note);
                GUI.color = Color.white;
                y += 36f;
            }

            if (expanded.Contains(rep.packageId))
            {
                int shown = 0;
                foreach (var m in rep.missing)
                {
                    if (shown++ >= 200) break;
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(inner.x + 24f, y, inner.width - 24f, 20f),
                        $"{m.defType} '{m.defName}' → {m.texPath}");
                    Text.Font = GameFont.Small;
                    y += 20f;
                }
            }

            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawLineHorizontal(r.x, r.yMax - 1f, r.width);
            GUI.color = Color.white;
        }
    }
}
