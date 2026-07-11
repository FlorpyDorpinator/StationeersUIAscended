using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Curvature option B's display surface: the whole HUD canvas renders into a
    /// RenderTexture and this graphic shows it on a dome-warped grid — one true
    /// projection for everything. Scanlines are a separate graphic
    /// (<see cref="ScanlineGraphic"/>): they must not sample the RT, whose texels
    /// under a strip are usually transparent.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DomeDisplayGraphic : MaskableGraphic
    {
        private const int Cols = 48;
        private const int Rows = 27;

        public Texture Texture;
        public float Strength;
        /// <summary>Signed bend direction, mirrors HudWarp.Direction.</summary>
        public float Direction = 1f;

        public override Texture mainTexture => Texture != null ? Texture : s_WhiteTexture;

        public void Refresh() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            float hw = rect.width * 0.5f, hh = rect.height * 0.5f;
            if (hw < 1f || hh < 1f) return;

            // The dome grid: uv is the flat RT, position is the warped screen point.
            // Uses the shared barrel function directly (independent of HudWarp's live
            // state, which is None while dome mode owns the curvature).
            for (int r = 0; r <= Rows; r++)
            {
                float v = r / (float)Rows;
                float y = Mathf.Lerp(-hh, hh, v);
                for (int c = 0; c <= Cols; c++)
                {
                    float u = c / (float)Cols;
                    float x = Mathf.Lerp(-hw, hw, u);
                    var q = HudWarp.Barrel(new Vector2(x, y), Strength * Direction, hw, hh);
                    vh.AddVert(new Vector3(q.x, q.y, 0f), color, new Vector2(u, v));
                }
            }
            int stride = Cols + 1;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    int i = r * stride + c;
                    vh.AddTriangle(i, i + stride, i + stride + 1);
                    vh.AddTriangle(i, i + stride + 1, i + 1);
                }
        }
    }

    /// <summary>Thin translucent strips over the dome display — the CRT/hologram
    /// scanline dressing. Colour+strength via the HudScanline palette entry (alpha 0
    /// disables; the mesh is skipped entirely).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ScanlineGraphic : MaskableGraphic
    {
        private const float Spacing = 5f;

        public void Refresh() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (color.a <= 0.004f) return;
            var rect = GetPixelAdjustedRect();
            float hw = rect.width * 0.5f, hh = rect.height * 0.5f;
            if (hw < 1f || hh < 1f) return;

            int idx = 0;
            var uv = Vector2.zero;
            for (float y = -hh; y < hh; y += Spacing)
            {
                vh.AddVert(new Vector3(-hw, y, 0f), color, uv);
                vh.AddVert(new Vector3(hw, y, 0f), color, uv);
                vh.AddVert(new Vector3(hw, y + 1f, 0f), color, uv);
                vh.AddVert(new Vector3(-hw, y + 1f, 0f), color, uv);
                vh.AddTriangle(idx, idx + 1, idx + 2);
                vh.AddTriangle(idx, idx + 2, idx + 3);
                idx += 4;
            }
        }
    }
}
