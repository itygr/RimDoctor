using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// Pick a mod and reset its settings (backed up first). For fixing a mod whose
    /// corrupt config crashes a screen or the menu.
    /// </summary>
    public class Dialog_ResetModSettings : Window
    {
        private List<ModSettingsRepair.ModSettingsEntry> entries;
        private Vector2 scroll;
        private string filter = "";

        public Dialog_ResetModSettings()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            onlyOneOfTypeAllowed = true;
            optionalTitle = "RimDoctor — Reset a mod's settings";
            entries = ModSettingsRepair.ModsWithSettings();
        }

        public override Vector2 InitialSize => new Vector2(620f, 560f);

        public override void DoWindowContents(Rect inRect)
        {
            try
            {
                var info = new Rect(inRect.x, inRect.y, inRect.width, 48f);
                Widgets.Label(info, "RimDoctor.ResetSettings.Info".TranslateSafe(
                    "Resets a mod's saved settings to defaults (the file is backed up first). "
                    + "Use this when a mod's corrupt config crashes a screen. Restart afterwards."));

                var filterRect = new Rect(inRect.x, info.yMax + 4f, inRect.width, 28f);
                filter = Widgets.TextField(filterRect, filter ?? "");

                var listArea = new Rect(inRect.x, filterRect.yMax + 6f, inRect.width, inRect.yMax - (filterRect.yMax + 6f));
                var shown = new List<ModSettingsRepair.ModSettingsEntry>();
                foreach (var e in entries)
                    if (string.IsNullOrEmpty(filter) || e.modName.ToLowerInvariant().Contains(filter.ToLowerInvariant()))
                        shown.Add(e);

                var view = new Rect(0, 0, listArea.width - 18f, Mathf.Max(shown.Count * 34f, listArea.height));
                Widgets.BeginScrollView(listArea, ref scroll, view);
                float y = 0f;
                foreach (var e in shown)
                {
                    var row = new Rect(0, y, view.width, 32f);
                    if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                    Widgets.Label(new Rect(row.x + 4f, row.y + 5f, row.width - 130f, 26f),
                        $"{e.modName}  ({e.files.Count} file)");
                    if (Widgets.ButtonText(new Rect(row.xMax - 120f, row.y + 2f, 116f, 28f),
                            "RimDoctor.ResetSettings.Reset".TranslateSafe("Reset")))
                        Confirm(e);
                    y += 34f;
                }
                if (shown.Count == 0)
                    Widgets.Label(new Rect(0, 0, view.width, 30f),
                        "RimDoctor.ResetSettings.None".TranslateSafe("No active mods have saved settings."));
                Widgets.EndScrollView();
            }
            catch (Exception ex)
            {
                RDLog.Exception("Reset-settings dialog failed", ex);
            }
        }

        private void Confirm(ModSettingsRepair.ModSettingsEntry e)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "RimDoctor.ResetSettings.Confirm".TranslateSafe(
                    $"Reset settings for \"{e.modName}\"? The current settings are backed up first; the mod will use defaults after a restart."),
                () =>
                {
                    int n = ModSettingsRepair.Reset(e);
                    Messages.Message(n > 0
                        ? "RimDoctor.ResetSettings.Done".TranslateSafe($"Reset {e.modName}. Restart to apply.")
                        : "RimDoctor.ResetSettings.Fail".TranslateSafe("Nothing reset (see log)."),
                        MessageTypeDefOf.TaskCompletion, false);
                    entries = ModSettingsRepair.ModsWithSettings();
                }, destructive: true));
        }
    }
}
