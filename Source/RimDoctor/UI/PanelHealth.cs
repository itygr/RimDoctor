using UnityEngine;
using Verse;

namespace RimDoctor
{
    // Full implementation lands in Milestone 2.
    public class PanelHealth : IRimDoctorPanel
    {
        public string Label => "RimDoctor.Tab.Health".TranslateSafe("Content Health");
        public void OnSelected() { }
        public void Draw(Rect rect)
        {
            Widgets.Label(rect, "RimDoctor.Health.Coming".TranslateSafe("Content health scanner — coming in Milestone 2."));
        }
    }
}
