using UnityEngine;
using Verse;

namespace RimDoctor
{
    // Full implementation lands in Milestone 1.
    public class PanelSorter : IRimDoctorPanel
    {
        public string Label => "RimDoctor.Tab.Sorter".TranslateSafe("Load Order");
        public void OnSelected() { }
        public void Draw(Rect rect)
        {
            Widgets.Label(rect, "RimDoctor.Sorter.Coming".TranslateSafe("Load-order sorter — coming in Milestone 1."));
        }
    }
}
