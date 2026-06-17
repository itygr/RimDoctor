using UnityEngine;

namespace RimDoctor
{
    /// <summary>A selectable tool panel inside the RimDoctor tab.</summary>
    public interface IRimDoctorPanel
    {
        string Label { get; }
        void Draw(Rect rect);
        /// <summary>Called when the panel becomes the active one (lazy work hook).</summary>
        void OnSelected();
    }
}
