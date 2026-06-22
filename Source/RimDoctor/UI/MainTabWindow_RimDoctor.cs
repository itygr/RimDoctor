using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// The RimDoctor tab. Hosts the tool panels (Sorter, Health, Log Doctor) with
    /// a row of selector buttons at the top. Each panel is self-contained and
    /// guarded so one panel's failure can't take down the window.
    /// </summary>
    public class MainTabWindow_RimDoctor : MainTabWindow
    {
        public override Vector2 RequestedTabSize => new Vector2(1000f, 720f);

        private static List<IRimDoctorPanel> panels;
        private int active;

        private static List<IRimDoctorPanel> Panels
        {
            get
            {
                if (panels == null)
                {
                    panels = new List<IRimDoctorPanel>
                    {
                        new PanelSorter(),
                        new PanelHealth(),
                        new PanelLogDoctor(),
                        new PanelPerformance(),
                        new PanelAnalytics(),
                        new PanelDiagnostics()
                    };
                }
                return panels;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            try
            {
                var title = new Rect(inRect.x, inRect.y, inRect.width, 36f);
                Text.Font = GameFont.Medium;
                Widgets.Label(title, "RimDoctor  v" + RimDoctorMod.Version);
                Text.Font = GameFont.Small;

                // Tool selector buttons
                float bx = inRect.x;
                float by = title.yMax + 4f;
                const float bw = 150f, bh = 32f;
                var list = Panels;
                for (int i = 0; i < list.Count; i++)
                {
                    var r = new Rect(bx + i * (bw + 6f), by, bw, bh);
                    bool isActive = i == active;
                    if (isActive) Widgets.DrawHighlightSelected(r);
                    if (Widgets.ButtonText(r, list[i].Label))
                    {
                        active = i;
                        try { list[i].OnSelected(); } catch (Exception e) { RDLog.Exception("Panel OnSelected failed", e); }
                    }
                }

                var body = new Rect(inRect.x, by + bh + 8f, inRect.width, inRect.height - (by + bh + 8f - inRect.y));
                Widgets.DrawMenuSection(body);
                var inner = body.ContractedBy(10f);
                try
                {
                    list[active].Draw(inner);
                }
                catch (Exception e)
                {
                    Widgets.Label(inner, "RimDoctor.Panel.Error".TranslateSafe(
                        "This panel hit an error and was disabled for safety. See the log."));
                    RDLog.Exception($"Panel '{list[active].Label}' draw failed", e);
                }
            }
            catch (Exception e)
            {
                RDLog.Exception("RimDoctor tab render failed", e);
            }
        }
    }
}
