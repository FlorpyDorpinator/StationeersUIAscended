using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Fades a UGUI graphic's mesh toward its own extremes: the outer <c>FadeX</c> fraction of
    /// the width (from EACH side) and <c>FadeY</c> of the height ramp the vertex ALPHA smoothly
    /// to zero, so a wide panel dissolves into the visor at its ends instead of stopping on a
    /// hard edge — the concept-art top bar whose left/right melt away. Pure vertex-alpha, like
    /// <see cref="VisorWarp"/>: no shader, so it warps, batches and curves with everything else.
    ///
    /// Runs AFTER VisorWarp (added later in component order), so the band is measured against the
    /// WARPED shape's true extent. Self-contained — the band is a fraction of the mesh's own x/y
    /// span, needing no RectTransform or panel-internal state, so it is robust to curvature and
    /// to the glow halo (which simply fades along with the rest of the visual).
    ///
    /// Fades the BOX/border/glow it sits on, not sibling text/icons — matching the concept art,
    /// where the frame melts at the ends while the readouts stay crisp.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class HudEdgeFade : BaseMeshEffect
    {
        private float _fadeX;
        private float _fadeY;

        /// <summary>Set the fade fractions. Each is 0..0.5 — the share of the FULL width/height
        /// over which the alpha ramps 1 → 0 inward from each edge (0.5 = the two bands meet in the
        /// middle, so the whole thing fades from centre out). 0 on both = inert = untouched mesh.
        /// Dirty-guarded so the HUD can push it every frame for one param read.</summary>
        public void SetFade(float x, float y)
        {
            x = float.IsNaN(x) ? 0f : Mathf.Clamp(x, 0f, 0.5f);
            y = float.IsNaN(y) ? 0f : Mathf.Clamp(y, 0f, 0.5f);
            if (Mathf.Approximately(x, _fadeX) && Mathf.Approximately(y, _fadeY)) return;
            _fadeX = x; _fadeY = y;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || (_fadeX <= 0.001f && _fadeY <= 0.001f)) return;
            int count = vh.currentVertCount;
            if (count == 0) return;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            UIVertex v = default(UIVertex);
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                if (v.position.x < minX) minX = v.position.x;
                if (v.position.x > maxX) maxX = v.position.x;
                if (v.position.y < minY) minY = v.position.y;
                if (v.position.y > maxY) maxY = v.position.y;
            }

            // Band = fraction of the full span; a vertex within `band` of a side fades toward it.
            float bandX = _fadeX * (maxX - minX);
            float bandY = _fadeY * (maxY - minY);

            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float a = 1f;
                if (bandX > 0.5f)
                    a *= Mathf.Clamp01(Mathf.Min(v.position.x - minX, maxX - v.position.x) / bandX);
                if (bandY > 0.5f)
                    a *= Mathf.Clamp01(Mathf.Min(v.position.y - minY, maxY - v.position.y) / bandY);
                // Smoothstep for a soft, natural falloff rather than a linear wedge.
                a = a * a * (3f - 2f * a);

                Color32 c = v.color;
                c.a = (byte)(c.a * a);
                v.color = c;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
