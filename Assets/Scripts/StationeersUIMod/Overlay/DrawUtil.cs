using System;
using System.Collections.Generic;
using ImGuiNET;
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.Overlay
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

        /// <summary>A complete annulus with no seam — used when a ring holds exactly one entry.
        /// Stroking a full 2*PI arc leaves a visible notch where the path's two ends meet.</summary>
        public static void RingFull(ImDrawListPtr dl, Vector2 center, float innerR, float outerR, uint color)
        {
            float mid = (innerR + outerR) * 0.5f;
            float thickness = outerR - innerR;
            if (thickness <= 0f) return;
            dl.AddCircle(center, mid, color, 96, thickness);
        }

        /// <summary>Radial divider drawn between two touching wedges.</summary>
        public static void RingSeparator(ImDrawListPtr dl, Vector2 center, float innerR, float outerR,
            float angle, uint color, float thickness = 1f)
        {
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            dl.AddLine(center + dir * innerR, center + dir * outerR, color, thickness);
        }

        /// <summary>Arc outline along a radius.</summary>
        public static void ArcLine(ImDrawListPtr dl, Vector2 center, float radius,
            float a0, float a1, uint color, float thickness)
        {
            dl.PathClear();
            PathArc(dl, center, radius, a0, a1);
            dl.PathStroke(color, ImDrawFlags.None, thickness);
        }

        /// <summary>Full-circle outline with no seam (companion to RingFull).</summary>
        public static void CircleOutline(ImDrawListPtr dl, Vector2 center, float radius, uint color, float thickness)
            => dl.AddCircle(center, radius, color, 96, thickness);

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

        /// <summary>Draws a sprite fitted into a size×size box, preserving its aspect ratio.</summary>
        /// <summary>Draws a sprite fitted into a size x size box, preserving aspect. The aspect
        /// comes from IconCache.PixelSize — the exact region the UVs sample — so trimmed and
        /// atlas-packed sprites are never stretched.</summary>
        public static void Icon(ImDrawListPtr dl, Sprite sprite, Vector2 center, float size, float alpha = 1f)
        {
            var info = IconCache.Get(sprite);
            if (!info.Valid) return;
            float w = size, h = size;
            var px = info.PixelSize;
            if (px.x > 1f && px.y > 1f)
            {
                float scale = size / Mathf.Max(px.x, px.y);
                w = px.x * scale;
                h = px.y * scale;
            }
            var half = new Vector2(w, h) * 0.5f;
            uint tint = Theme.C(1f, 1f, 1f, alpha);
            dl.AddImage(info.TextureId, center - half, center + half, info.Uv0, info.Uv1, tint);
        }

        // ---------- auto-fitting label block ----------
        //
        // Wedge labels used to hard-truncate ("Mk II Minin.."). Instead: word-wrap to at most
        // maxLines, and if it still doesn't fit, shrink the font (ImGui's AddText takes an
        // explicit font_size) down to MinFontScale. Ellipsis is a last resort for a single
        // unbreakable word. Text is never rotated: ImGui's AddText is axis-aligned, so true
        // curved-to-the-arc text would need per-glyph vertex rotation — that's the UGUI/TMP
        // phase, not this one.

        private const float MinFontScale = 0.60f;
        private static readonly float[] ScaleSteps = { 1.00f, 0.92f, 0.84f, 0.76f, 0.68f, MinFontScale };
        private static readonly List<string> WrapBuffer = new List<string>(4);

        /// <summary>
        /// Draws <paramref name="text"/> centered in a maxWidth x maxHeight box, shrinking and
        /// wrapping so the WHOLE word survives wherever possible. Returns the scale used.
        /// </summary>
        public static float TextFittedCentered(ImDrawListPtr dl, Vector2 center, float maxWidth, float maxHeight,
            uint color, string text, int maxLines = 2, bool shadow = true)
        {
            if (string.IsNullOrEmpty(text) || maxWidth < 8f) return 1f;

            var font = ImGui.GetFont();
            float baseSize = ImGui.GetFontSize();
            if (baseSize <= 0f) baseSize = 13f;

            foreach (float scale in ScaleSteps)
            {
                // Measure in base-font units, so the budget grows as the font shrinks.
                float widthBudget = maxWidth / scale;
                float lineH = baseSize * scale * 1.05f;
                if (maxLines * lineH > maxHeight && lineH > maxHeight) continue;

                if (!TryWrap(text, widthBudget, maxLines, WrapBuffer)) continue;
                if (WrapBuffer.Count * lineH > maxHeight) continue;

                DrawLines(dl, center, WrapBuffer, font, baseSize * scale, lineH, color, shadow);
                return scale;
            }

            // Nothing fit: smallest font, one ellipsized line.
            float small = baseSize * MinFontScale;
            WrapBuffer.Clear();
            WrapBuffer.Add(FitText(text, maxWidth / MinFontScale));
            DrawLines(dl, center, WrapBuffer, font, small, small * 1.05f, color, shadow);
            return MinFontScale;
        }

        private static void DrawLines(ImDrawListPtr dl, Vector2 center, List<string> lines,
            ImFontPtr font, float fontSize, float lineH, uint color, bool shadow)
        {
            float startY = center.y - (lines.Count - 1) * lineH * 0.5f;
            uint shadowCol = Theme.C(0f, 0f, 0f, 0.85f);
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                float w = ImGui.CalcTextSize(line).x * (fontSize / ImGui.GetFontSize());
                var pos = new Vector2(center.x - w * 0.5f, startY + i * lineH - fontSize * 0.5f);
                if (shadow) dl.AddText(font, fontSize, pos + new Vector2(1f, 1f), shadowCol, line);
                dl.AddText(font, fontSize, pos, color, line);
            }
        }

        /// <summary>Greedy word wrap in base-font units. False if any line still overflows
        /// (an unbreakable word) or it needs more than maxLines.</summary>
        private static bool TryWrap(string text, float widthBudget, int maxLines, List<string> outLines)
        {
            outLines.Clear();
            if (ImGui.CalcTextSize(text).x <= widthBudget)
            {
                outLines.Add(text);
                return true;
            }

            string[] words = text.Split(' ');
            string current = string.Empty;
            foreach (string word in words)
            {
                if (ImGui.CalcTextSize(word).x > widthBudget) return false; // unbreakable
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (ImGui.CalcTextSize(candidate).x <= widthBudget)
                {
                    current = candidate;
                    continue;
                }
                outLines.Add(current);
                if (outLines.Count >= maxLines) return false;
                current = word;
            }
            if (current.Length > 0) outLines.Add(current);
            return outLines.Count > 0 && outLines.Count <= maxLines;
        }

        /// <summary>Truncates text (with ellipsis) so its rendered width fits maxWidth.</summary>
        public static string FitText(string s, float maxWidth)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (ImGui.CalcTextSize(s).x <= maxWidth) return s;
            int len = s.Length;
            while (len > 1 && ImGui.CalcTextSize(s.Substring(0, len) + "..").x > maxWidth)
                len--;
            return s.Substring(0, len) + "..";
        }

        /// <summary>
        /// Fits text into at most two lines of maxWidth: word-wraps at spaces, truncating the
        /// second line if needed. line2 is null when one line suffices.
        /// </summary>
        public static void FitTextTwoLines(string s, float maxWidth, out string line1, out string line2)
        {
            line2 = null;
            if (string.IsNullOrEmpty(s) || ImGui.CalcTextSize(s).x <= maxWidth)
            {
                line1 = s;
                return;
            }
            int split = -1;
            for (int i = s.Length - 1; i > 0; i--)
            {
                if (s[i] != ' ') continue;
                if (ImGui.CalcTextSize(s.Substring(0, i)).x <= maxWidth) { split = i; break; }
            }
            if (split <= 0)
            {
                line1 = FitText(s, maxWidth); // one unbreakable word
                return;
            }
            line1 = s.Substring(0, split);
            line2 = FitText(s.Substring(split + 1), maxWidth);
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

