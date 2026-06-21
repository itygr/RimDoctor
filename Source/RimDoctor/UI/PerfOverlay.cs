using UnityEngine;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// The compact, draggable on-screen performance HUD. Reads ONLY pre-rolled snapshot
    /// fields from PerfMonitor + TickAttribution and draws with DrawBoxSolid + Label
    /// (no textures, no per-frame allocation). Position persists in settings.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PerfOverlay
    {
        private const float W = 200f, H = 96f;
        private static bool dragging;
        private static Vector2 dragOff;

        // Built on the main thread at startup (StaticConstructorOnStartup) so RimWorld
        // doesn't warn about a Texture2D field and the icon is never created off-thread.
        private static Texture2D icon;
        static PerfOverlay() { try { icon = BuildIcon(); } catch { } }
        public static Texture2D HudIcon { get { if (icon == null) { try { icon = BuildIcon(); } catch { } } return icon; } }

        public static void Draw(RimDoctorSettings s)
        {
            float x = s.overlayX, y = s.overlayY;
            if (x <= 0f && y <= 0f) { x = 8f; y = 8f; }
            x = Mathf.Clamp(x, 0f, UI.screenWidth - W);
            y = Mathf.Clamp(y, 0f, UI.screenHeight - H);
            var rect = new Rect(x, y, W, H);

            HandleDrag(rect, s);

            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.62f));
            var inner = rect.ContractedBy(7f);
            var prevFont = Text.Font;
            Text.Font = GameFont.Tiny;

            GUI.color = PerfColor();
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 18f),
                PerfMonitor.Paused ? "TPS  — (paused)" : $"TPS  {PerfMonitor.Tps:0} / {PerfMonitor.TargetTps}");
            GUI.color = Color.white;
            Widgets.Label(new Rect(inner.x, inner.y + 18f, inner.width, 18f),
                $"ms/tick {PerfMonitor.MsPerTick:0.0}    FPS {PerfMonitor.Fps:0}");
            Widgets.Label(new Rect(inner.x, inner.y + 36f, inner.width, 18f),
                $"components {TickAttribution.ComponentsMsPerSec:0.0} ms/s");
            GUI.color = new Color(1f, 0.85f, 0.5f);
            Widgets.Label(new Rect(inner.x, inner.y + 54f, inner.width, 18f),
                string.IsNullOrEmpty(TickAttribution.TopLine)
                    ? "top: enable per-thing timing"
                    : "top: " + TickAttribution.TopLine);
            GUI.color = Color.white;

            Text.Font = prevFont;
        }

        private static void HandleDrag(Rect rect, RimDoctorSettings s)
        {
            var e = Event.current;
            if (e == null) return;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            { dragging = true; dragOff = e.mousePosition - new Vector2(rect.x, rect.y); e.Use(); }
            else if (e.type == EventType.MouseDrag && dragging)
            { s.overlayX = e.mousePosition.x - dragOff.x; s.overlayY = e.mousePosition.y - dragOff.y; e.Use(); }
            else if (e.type == EventType.MouseUp && dragging)
            { dragging = false; e.Use(); try { RimDoctorMod.Instance?.WriteSettings(); } catch { } }
        }

        private static Color PerfColor()
        {
            if (PerfMonitor.Paused || !PerfMonitor.HasData) return new Color(0.7f, 0.7f, 0.7f);
            if (PerfMonitor.Tps >= PerfMonitor.TargetTps * 0.92f) return new Color(0.55f, 1f, 0.55f);
            if (PerfMonitor.Tps >= PerfMonitor.TargetTps * 0.6f) return new Color(1f, 0.85f, 0.45f);
            return new Color(1f, 0.5f, 0.45f);
        }

        // tiny in-code bar-chart glyph for the play-settings toggle (no asset file needed)
        private static Texture2D BuildIcon()
        {
            var t = new Texture2D(24, 24, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var clear = new Color(0, 0, 0, 0);
            for (int px = 0; px < 24; px++)
                for (int py = 0; py < 24; py++)
                    t.SetPixel(px, py, clear);
            void Bar(int x0, int x1, int h)
            {
                for (int px = x0; px < x1; px++)
                    for (int py = 4; py < 4 + h; py++)
                        t.SetPixel(px, py, Color.white);
            }
            Bar(4, 9, 8); Bar(10, 15, 16); Bar(16, 21, 11);
            t.Apply();
            return t;
        }
    }
}
