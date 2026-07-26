using TMPro;
using UnityEngine;

namespace StationeersUIMod.Overlay
{
    /// <summary>
    /// Bends a TextMeshPro label around a circle so it GENUINELY follows an arc, glyph by glyph,
    /// rather than merely being rotated to the tangent. Used for the radial's bound-tool label,
    /// which hugs the arc where the hub begins.
    ///
    /// HOW: TMP builds its mesh in the label's own local space, so the caller keeps doing what it
    /// already does — position the RectTransform on the ring and rotate it to the tangent — and this
    /// only re-lays the glyphs WITHIN that local space. The two compose into true arc text.
    ///
    /// The label's local origin sits ON the circle, so the circle's centre is one radius straight
    /// "down" in local space when the label's up points outward (top half of the ring), and straight
    /// "up" when it points inward (bottom half, where the caller flips the rotation to keep the text
    /// upright). That single sign is the only difference between the two halves.
    ///
    /// Local x is treated as ARC LENGTH, which is what keeps letter spacing even around the curve.
    /// Each glyph is moved to its own point on the arc and rotated by its own subtended angle, so
    /// the glyph SHAPES stay undistorted (bending the quads directly would shear them).
    ///
    /// Cost: one ForceMeshUpdate per curved label per frame — bounded because it only ever runs
    /// for the handful of BOUND-TOOL labels on the wedges of an OPEN radial, and those can be
    /// switched off entirely (UIAConfig.RadialShowBindingLabels). Fail-soft: any exception leaves
    /// the label as ordinary straight text.
    /// </summary>
    public static class RadialArcText
    {
        /// <summary>Curve <paramref name="tmp"/> around a circle of <paramref name="radius"/> whose
        /// centre lies perpendicular to the label. <paramref name="outwardUp"/> is true when the
        /// label's local +Y points AWAY from the ring centre (the top half of the ring).</summary>
        public static void Curve(TextMeshProUGUI tmp, float radius, bool outwardUp)
        {
            if (tmp == null || radius <= 1f) return;
            try
            {
                // Resolve autosizing and the final glyph layout before we move anything.
                tmp.ForceMeshUpdate();
                var ti = tmp.textInfo;
                if (ti == null || ti.characterCount == 0) return;

                float sign = outwardUp ? 1f : -1f;

                for (int c = 0; c < ti.characterCount; c++)
                {
                    var ci = ti.characterInfo[c];
                    if (!ci.isVisible) continue;

                    int mi = ci.materialReferenceIndex;
                    int vi = ci.vertexIndex;
                    var verts = ti.meshInfo[mi].vertices;
                    if (verts == null || vi + 3 >= verts.Length) continue;

                    // TMP quad order is BL, TL, TR, BR — so 0 and 2 are opposite corners.
                    float midX = (verts[vi + 0].x + verts[vi + 2].x) * 0.5f;

                    float phi = midX / radius;          // arc length -> angle
                    float a = -sign * phi;              // this glyph's own rotation
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);

                    // Where this glyph's baseline midpoint lands on the circle. Derived from
                    // centre + radius * dir(phi) for both halves; the sign collapses them into one
                    // expression (top: (R sin, -R(1-cos)), bottom: (R sin, +R(1-cos))).
                    float bx = radius * Mathf.Sin(phi);
                    float by = -sign * radius * (1f - Mathf.Cos(phi));

                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 v = verts[vi + k];
                        float ox = v.x - midX;          // offset from this glyph's own midpoint
                        float oy = v.y;                 // height above the straight baseline
                        verts[vi + k] = new Vector3(
                            bx + (ox * ca - oy * sa),
                            by + (ox * sa + oy * ca),
                            v.z);
                    }
                }

                tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
            }
            catch
            {
                // Never let a text-mesh quirk take the whole radial down — straight text is fine.
            }
        }
    }
}
