using System;
using ImGuiNET;
using StationeersUIAscended.Core;
using UnityEngine;

namespace StationeersUIAscended.Overlay
{
    /// <summary>Low-level ImGui draw-list helpers shared by the radial and the HUD.</summary>
    public static class DrawUtil
    {
        public static Vector2 ScreenCenter => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        /// <summary>Annular sector drawn as a thick stroked arc (avoids concave poly fills).</summary>
        public static void RingSector(ImDrawListPtr dl, Vector2 center, float innerR, float outerR,
            float a0, float a1, uint color, float inset = 0f)
        {
            float mid = (innerR + outerR) * 0.5f;
            float thickness = (outerR - innerR) - inset * 2f;
            if (thickness <= 0f) return;
            dl.PathClear();
            PathArc(dl, center, mid, a0, a1);
            dl.PathStroke(color, ImDrawFlags.None, thickness);
        }

        /// <summary>Arc outline along a radius.</summary>
        public static void ArcLine(ImDrawListPtr dl, Vector2 center, float radius,
            float a0, float a1, uint color, float thickness)
        {
            dl.PathClear();
            PathArc(dl, center, radius, a0, a1);
            dl.PathStroke(color, ImDrawFlags.None, thickness);
        }

        private static void PathArc(ImDrawListPtr dl, Vector2 center, float radius, float a0, float a1)
        {
            int segments = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(a1 - a0) * radius / 8f), 6, 96);
            for (int i = 0; i <= segments; i++)
            {
                float t = a0 + (a1 - a0) * (i / (float)segments);
                dl.PathLineTo(center + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * radius);
            }
        }

        public static void Line(ImDrawListPtr dl, Vector2 a, Vector2 b, uint color, float thickness = 1f)
            => dl.AddLine(a, b, color, thickness);

        public static Vector2 TextSize(string text) => ImGui.CalcTextSize(text ?? string.Empty);

        public static void Text(ImDrawListPtr dl, Vector2 pos, uint color, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            dl.AddText(pos, color, text);
        }

        public static void TextCentered(ImDrawListPtr dl, Vector2 center, uint color, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var sz = ImGui.CalcTextSize(text);
            dl.AddText(center - sz * 0.5f, color, text);
        }

        public static void TextShadowCentered(ImDrawListPtr dl, Vector2 center, uint color, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var sz = ImGui.CalcTextSize(text);
            var p = center - sz * 0.5f;
            dl.AddText(p + new Vector2(1f, 1f), Theme.C(0f, 0f, 0f, 0.8f), text);
            dl.AddText(p, color, text);
        }

        public static void Panel(ImDrawListPtr dl, Vector2 min, Vector2 max, uint bg, uint border, float rounding = 6f)
        {
            dl.AddRectFilled(min, max, bg, rounding);
            dl.AddRect(min, max, border, rounding);
        }

        public static void Icon(ImDrawListPtr dl, Sprite sprite, Vector2 center, float size, float alpha = 1f)
        {
            var info = IconCache.Get(sprite);
            if (!info.Valid) return;
            var half = new Vector2(size, size) * 0.5f;
            uint tint = Theme.C(1f, 1f, 1f, alpha);
            dl.AddImage(info.TextureId, center - half, center + half, info.Uv0, info.Uv1, tint);
        }

        /// <summary>Horizontal progress bar with themed state color.</summary>
        public static void Bar(ImDrawListPtr dl, Vector2 min, Vector2 max, float ratio01, uint fillColor)
        {
            ratio01 = Mathf.Clamp01(ratio01);
            dl.AddRectFilled(min, max, Theme.C(0f, 0f, 0f, 0.45f), 2f);
            if (ratio01 > 0f)
                dl.AddRectFilled(min, new Vector2(min.x + (max.x - min.x) * ratio01, max.y), fillColor, 2f);
            dl.AddRect(min, max, Theme.PanelBorder, 2f);
        }

        /// <summary>Mouse position in ImGui space.</summary>
        public static Vector2 MousePos()
        {
            var io = ImGui.GetIO();
            return io.MousePos;
        }

        public static float NormalizeAngle(float a)
        {
            while (a < 0f) a += Mathf.PI * 2f;
            while (a >= Mathf.PI * 2f) a -= Mathf.PI * 2f;
            return a;
        }
    }
}
