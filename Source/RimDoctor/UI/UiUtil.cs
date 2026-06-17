using UnityEngine;

namespace RimDoctor
{
    /// <summary>
    /// Small UI helpers. The key one is viewport culling for scroll lists — the
    /// same technique vanilla RimWorld uses for long lists: only draw the rows
    /// actually visible in the viewport instead of every row every frame.
    /// </summary>
    public static class UiUtil
    {
        /// <summary>
        /// For a uniform-height list, compute the inclusive index range visible in
        /// the viewport given the current scroll offset. Pad by one row each side
        /// so partial rows at the edges render.
        /// </summary>
        public static void VisibleRange(float scrollY, float viewportHeight, float rowHeight,
            int count, out int first, out int last)
        {
            if (rowHeight <= 0f || count <= 0)
            {
                first = 0;
                last = -1;
                return;
            }
            first = Mathf.Max(0, Mathf.FloorToInt(scrollY / rowHeight) - 1);
            last = Mathf.Min(count - 1, Mathf.CeilToInt((scrollY + viewportHeight) / rowHeight) + 1);
        }

        /// <summary>True if a row spanning [y, y+height] intersects the visible viewport.</summary>
        public static bool RowVisible(float y, float height, float scrollY, float viewportHeight)
        {
            return y + height >= scrollY && y <= scrollY + viewportHeight;
        }
    }
}
