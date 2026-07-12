using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Shared warp math for the visor curvature. HudSystem sets the active mode each frame;
    /// the mesh hooks below apply it. All functions operate in CANVAS space (origin centre).
    /// </summary>
    public static class HudWarp
    {
        public enum Kind { None, Barrel, Cylinder }

        public static Kind Active;
        public static float Strength;
        /// <summary>+1 or −1 — the bend direction (play-tested default flipped 0.5.0:
        /// the original sign read as inside-out on all three modes; the F9 editor
        /// exposes the choice).</summary>
        public static float Direction = 1f;
        public static float HalfW = 960f, HalfH = 540f;

        /// <summary>Set while the player is BARE (suit off / powered down): the visor is
        /// gone, so the HUD reads FLAT — HudSystem folds this into the layout hash so the
        /// whole HUD re-lays-out and re-meshes without curvature on the transition.</summary>
        public static bool BareFlat;

        public static bool Enabled => Active != Kind.None && Strength > 0.001f && !BareFlat;

        /// <summary>The shared barrel term — a GENTLE visor bow. The old 0.30/0.12
        /// coefficients turned a full-width top bar into a grotesque funnel (the play-test
        /// "very broken" curve), and the aggressive fit-scale then sucked its contents down
        /// toward screen centre ("everything is too close to the screen"). Now: much smaller
        /// coefficients, the normalised coords CLAMPED so the flare saturates instead of
        /// running away at the edges, and only a MILD matching fit-scale (kept here so the
        /// Unwarp inverse and the editor hit-test track it automatically).</summary>
        public static Vector2 Barrel(Vector2 p, float k, float halfW, float halfH)
        {
            float nx = Mathf.Clamp(p.x / halfW, -1f, 1f);
            float ny = Mathf.Clamp(p.y / halfH, -1f, 1f);
            p.y += p.y * k * 0.12f * nx * nx;
            p.x += p.x * k * 0.06f * ny * ny;
            // A light inward nudge so the worst-case flare stays on-screen without pulling
            // the whole HUD toward the middle.
            float fit = 1f / (1f + Mathf.Abs(k) * 0.10f);
            p.x *= fit;
            p.y *= fit;
            return p;
        }

        /// <summary>FORWARD of the configured screen warp — where a canvas point actually
        /// lands on screen in VertexWarp/DomeProjection (identity otherwise). The designer
        /// places selection handles with this so they hug curved elements.</summary>
        public static Vector2 WarpPoint(Vector2 p)
        {
            if (BareFlat) return p; // flat while bare — keep editor handles unwarped too
            var mode = HudConfig.Curvature != null ? HudConfig.Curvature.Value : HudCurvature.Flat;
            if (mode != HudCurvature.VertexWarp && mode != HudCurvature.DomeProjection) return p;
            float k = HudConfig.CurveStrength.Value * (HudConfig.CurveInvert.Value ? -1f : 1f);
            if (Mathf.Abs(k) <= 0.001f) return p;
            return Barrel(p, k, Screen.width * 0.5f, Screen.height * 0.5f);
        }

        /// <summary>Inverse of the CONFIGURED screen warp (VertexWarp and DomeProjection
        /// modes) by fixed-point iteration — one negative pass drifts 15-25px at the
        /// corners at full strength; three iterations land sub-pixel. Canvas coords
        /// (centre origin, y up). Shared by the F9 editor hit-testing and the radial
        /// chip-drop zones so mouse points always land where the warped HUD draws.</summary>
        public static Vector2 Unwarp(Vector2 p)
        {
            if (BareFlat) return p; // flat while bare — mouse maps 1:1
            var mode = HudConfig.Curvature != null ? HudConfig.Curvature.Value : HudCurvature.Flat;
            if (mode != HudCurvature.VertexWarp && mode != HudCurvature.DomeProjection) return p;
            float k = HudConfig.CurveStrength.Value * (HudConfig.CurveInvert.Value ? -1f : 1f);
            if (Mathf.Abs(k) <= 0.001f) return p;
            float hw = Screen.width * 0.5f, hh = Screen.height * 0.5f;
            var q = p;
            for (int i = 0; i < 3; i++)
                q += p - Barrel(q, k, hw, hh);
            return q;
        }

        public static Vector3 Warp(Vector3 p)
        {
            float k = Strength * Direction;
            switch (Active)
            {
                case Kind.Barrel:
                {
                    var q = Barrel(new Vector2(p.x, p.y), k, HalfW, HalfH);
                    p.x = q.x;
                    p.y = q.y;
                    return p;
                }
                case Kind.Cylinder:
                {
                    // World-canvas visor (mode C): parabolic cylinder toward/away from
                    // the camera at the sides, with a touch of vertical dome.
                    float nx = p.x / HalfW;
                    float ny = p.y / HalfH;
                    p.z += k * (HalfW * 0.25f * nx * nx + HalfH * 0.08f * ny * ny);
                    return p;
                }
                default:
                    return p;
            }
        }
    }

    /// <summary>
    /// Applies the visor warp to any UGUI Graphic mesh (panels, icons, bars). TMP text
    /// bypasses IMeshModifier entirely — that path is <see cref="TmpWarp"/>.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class VisorWarp : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || !HudWarp.Enabled || graphic == null) return;
            var canvas = graphic.canvas;
            if (canvas == null) return;

            Matrix4x4 toCanvas = canvas.transform.worldToLocalMatrix
                               * graphic.transform.localToWorldMatrix;
            Matrix4x4 back = toCanvas.inverse;

            UIVertex v = default(UIVertex);
            int count = vh.currentVertCount;
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.position = back.MultiplyPoint3x4(HudWarp.Warp(toCanvas.MultiplyPoint3x4(v.position)));
                vh.SetUIVertex(v, i);
            }
        }
    }

    /// <summary>
    /// TMP text warp. TextMeshPro generates its own mesh and ignores BaseMeshEffect, but it
    /// exposes OnPreRenderText — vertices edited there are what gets uploaded. SDF glyphs
    /// stay crisp under the bend. Re-fires on every regeneration; HudSystem force-dirties
    /// all text when the warp mode/strength or layout changes.
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class TmpWarp : MonoBehaviour
    {
        private TextMeshProUGUI _tmp;

        private void OnEnable()
        {
            _tmp = GetComponent<TextMeshProUGUI>();
            if (_tmp != null) _tmp.OnPreRenderText += Apply;
        }

        private void OnDisable()
        {
            if (_tmp != null) _tmp.OnPreRenderText -= Apply;
        }

        private void Apply(TMP_TextInfo info)
        {
            if (!HudWarp.Enabled || _tmp == null || info == null) return;
            var canvas = _tmp.canvas;
            if (canvas == null) return;

            Matrix4x4 toCanvas = canvas.transform.worldToLocalMatrix
                               * _tmp.transform.localToWorldMatrix;
            Matrix4x4 back = toCanvas.inverse;

            for (int m = 0; m < info.meshInfo.Length; m++)
            {
                var verts = info.meshInfo[m].vertices;
                if (verts == null) continue;
                int used = info.meshInfo[m].vertexCount;
                for (int i = 0; i < used && i < verts.Length; i++)
                    verts[i] = back.MultiplyPoint3x4(HudWarp.Warp(toCanvas.MultiplyPoint3x4(verts[i])));
            }
        }
    }
}
