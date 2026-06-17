using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// The RimDoctor tab. In the foundation milestone this just confirms the mod
    /// is alive and lists the sub-tools; Milestones 1/2/4 fill in the real panels
    /// (sorter diff, health report, Log Doctor).
    /// </summary>
    public class MainTabWindow_RimDoctor : MainTabWindow
    {
        public override Vector2 RequestedTabSize => new Vector2(900f, 700f);

        public override void DoWindowContents(Rect inRect)
        {
            try
            {
                var title = new Rect(inRect.x, inRect.y, inRect.width, 42f);
                Text.Font = GameFont.Medium;
                Widgets.Label(title, "RimDoctor");
                Text.Font = GameFont.Small;

                var body = new Rect(inRect.x, title.yMax + 6f, inRect.width, inRect.height - title.height - 6f);
                Widgets.Label(body,
                    "RimDoctor.Tab.Foundation".TranslateSafe(
                        "RimDoctor is loaded and running.\n\n" +
                        "Tools (filling in milestone by milestone):\n" +
                        "  • Load-order sorter — diff your current vs. a smart-sorted order, then Apply & Restart.\n" +
                        "  • Content health scanner — find missing textures and broken/incomplete mods.\n" +
                        "  • Texture fallback — already active: missing textures show a placeholder instead of crashing.\n" +
                        "  • Log Doctor — explains errors in plain language with likely culprit + fix.\n\n" +
                        "Open Mod Settings → RimDoctor to configure protection and repair tiers."));
            }
            catch (Exception e)
            {
                RDLog.Exception("RimDoctor tab render failed", e);
            }
        }
    }
}
