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

        public static bool Enabled => Active != Kind.None && Strength > 0.001f;

        /// <summary>The shared barrel term: +1 pushes rows AWAY from the horizontal
        /// axis at the screen edges (corners flare outward), −1 pinches them inward.</summary>
        public static Vector2 Barrel(Vector2 p, float k, float halfW, float halfH)
        {
            float nx = p.x / halfW;
            float ny = p.y / halfH;
            p.y += p.y * k * 0.30f * nx * nx;
            p.x += p.x * k * 0.12f * ny * ny;
            return p;
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
