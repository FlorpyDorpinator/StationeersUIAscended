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

    /// <summary>
    /// The bend cache for one curved TMP label: <see cref="RadialArcText.Curve"/> (one
    /// ForceMeshUpdate + autosize passes) runs only when the radius / side moved, TMP has a pending
    /// rebuild, or TMP regenerated its (flat) layout into the glyph buffer since the last bend —
    /// detected by a one-vertex probe of the buffer TMP writes in place. A steady label costs a
    /// handful of compares a frame. Shared by <see cref="ArcPlateLabel"/> and the radial's pooled
    /// per-wedge CornerTags (B9). Plain instance state owned by its label — nothing static.
    /// <para>A label that was not serviced on the previous frame may have been disabled and re-enabled
    /// meanwhile (menu closed and reopened, a satellite ring hidden and shown): TMP rebuilds it FLAT on
    /// enable without flagging <c>havePropertiesChanged</c>, so such a label is re-bent unconditionally.
    /// A caller that resizes the label's rect must <see cref="Invalidate"/> (TMP marks the layout dirty,
    /// not the properties).</para>
    /// </summary>
    public sealed class ArcBendCache
    {
        private float _curvedR = -1f;
        private bool _curvedTop;
        private Vector3[] _probeArray;
        private int _probeVert = -1;
        private Vector3 _probePos;
        private int _lastFrame = -100;

        /// <summary>Force the next <see cref="CurveIfStale"/> to bend the text again.</summary>
        public void Invalidate()
        {
            _curvedR = -1f;
            _probeArray = null;
            _probeVert = -1;
        }

        /// <summary>Bend <paramref name="text"/> around <paramref name="radius"/> (see
        /// <see cref="RadialArcText.Curve"/> for <paramref name="outwardUp"/>) only when needed. Call
        /// AFTER the text, font, size, colour and rect are final — the bend reads the built mesh.</summary>
        public void CurveIfStale(TextMeshProUGUI text, float radius, bool outwardUp)
        {
            if (text == null) return;
            int frame = Time.frameCount;
            if (frame - _lastFrame > 1) Invalidate(); // unserviced last frame: may be a fresh flat mesh
            // ForceMeshUpdate no-ops on inactive text. Not counted as serviced either, so the first
            // active frame after this one re-bends through the rule above.
            if (!text.gameObject.activeInHierarchy) return;
            _lastFrame = frame;
            if (!NeedsCurve(text, radius, outwardUp)) return;
            RadialArcText.Curve(text, radius, outwardUp);
            _curvedR = radius;
            _curvedTop = outwardUp;
            RecordProbe(text);
        }

        /// <summary>The probe is the first visible glyph's first vertex — off the arc's centre, so its
        /// bent and flat positions differ whenever the text has more than one glyph.</summary>
        private bool NeedsCurve(TextMeshProUGUI text, float radius, bool top)
        {
            if (_probeArray == null || top != _curvedTop || !Mathf.Approximately(radius, _curvedR)) return true;
            if (text.havePropertiesChanged) return true;
            var ti = text.textInfo;
            if (ti == null || ti.meshInfo == null || ti.meshInfo.Length == 0) return true;
            var verts = ti.meshInfo[0].vertices;
            if (!ReferenceEquals(verts, _probeArray) || _probeVert < 0 || _probeVert >= verts.Length) return true;
            return verts[_probeVert] != _probePos;
        }

        private void RecordProbe(TextMeshProUGUI text)
        {
            _probeArray = null;
            _probeVert = -1;
            try
            {
                var ti = text.textInfo;
                if (ti == null || ti.meshInfo == null || ti.meshInfo.Length == 0) return;
                for (int c = 0; c < ti.characterCount; c++)
                {
                    var ci = ti.characterInfo[c];
                    if (!ci.isVisible || ci.materialReferenceIndex != 0) continue;
                    var verts = ti.meshInfo[0].vertices;
                    if (verts == null || ci.vertexIndex < 0 || ci.vertexIndex >= verts.Length) return;
                    _probeArray = verts;
                    _probeVert = ci.vertexIndex;
                    _probePos = verts[ci.vertexIndex];
                    return;
                }
            }
            catch { _probeArray = null; _probeVert = -1; }
        }
    }

    /// <summary>
    /// A curved label on a glass PLATE hugging a ring from the OUTSIDE — the D-022 action word outside
    /// the hovered wedge and the D-021 key hints under the ring's bottom. The plate is an annular sector drawn
    /// by the same <see cref="UI.RadialWedgeGraphic"/> as the wedges (so it takes the radial's
    /// border, feather and glass FX and reads as part of the wheel), and the text is bent glyph by
    /// glyph by <see cref="RadialArcText.Curve"/>.
    /// <para>Conventions (the seal / coin rule): over the TOP the text reads left-to-right with the
    /// glyphs pointing outward; under the BOTTOM it still reads left-to-right, upright, glyphs
    /// pointing inward — neither half is ever upside down. Local x is arc length, so the plate's
    /// angular width follows the text: (text length / 2 + padding) / radius each side.</para>
    /// <para>Cost: the bend is cached (<see cref="ArcBendCache"/>). <see cref="RadialArcText.Curve"/>
    /// (one ForceMeshUpdate) runs only when the radius / side changed or TMP regenerated its mesh since
    /// the last bend — detected by a one-vertex probe of the glyph buffer TMP writes in place — so a
    /// steady label costs a handful of compares a frame. Fades through a CanvasGroup (alpha never
    /// regenerates the text mesh). Owned by the canvas it is parented to: destroyed with it; the only
    /// static state is the value-type occluder record below.</para>
    /// <para>Any angle (2026-09-26, FlorpyDorp: the action word "dynamically adjusts to each wedge"):
    /// <see cref="LayoutAt"/> centres the plate on ANY angle around the ring. The text keeps the same
    /// seal convention everywhere — on the upper half (and exactly at 3 / 9 o'clock) its "up" points
    /// outward and it reads clockwise; on the lower half its "up" points inward and it reads
    /// counter-clockwise, i.e. left-to-right and upright. The top/bottom <see cref="Layout"/> is
    /// just LayoutAt at -90 / +90 degrees.</para>
    /// <para>Yield: a label flagged <see cref="Occludes"/> (the action word) publishes where it sits
    /// each frame; any OTHER arc label around the same centre whose band it overlaps (the key-hint
    /// strip under the ring) fades out smoothly while it does — the same "yield to what swung over
    /// you" rule the hint strip already applies to a child ring. The published record is plain value
    /// types gated by frame count (never a reference): a stale record is ignored, and
    /// <see cref="ResetOccluder"/> clears it on teardown.</para>
    /// </summary>
    public sealed class ArcPlateLabel
    {
        public readonly RectTransform Root;          // sits at the ring centre; plate + text are relative to it
        public readonly UI.RadialWedgeGraphic Plate;
        public readonly TextMeshProUGUI Text;
        private readonly CanvasGroup _group;
        private float _alpha;

        /// <summary>This label floats OVER other arc labels (the action word): it publishes its
        /// footprint for them to yield to, and never yields itself.</summary>
        public bool Occludes;

        // text placement cache (setters on RectTransform are only touched when the value moved)
        private Vector2 _textPos = new Vector2(float.NaN, float.NaN);
        private float _textRotDeg = float.NaN;

        // ---- the published occluder (see class remarks). Value types only; frame-gated. ----
        private static int _occFrame = -100;
        private static Vector2 _occCenter;
        private static float _occMid, _occHalf, _occRIn, _occROut, _occAlpha;
        /// <summary>How close (px of arc) the occluder may come before a yielding label starts to fade:
        /// the fade ramps over this distance, so a plate sliding toward the strip dims it smoothly.</summary>
        private const float YieldSoftPx = 14f;
        /// <summary>|sin| below which a label counts as "on the horizontal" and keeps the upper-half
        /// convention, so 3 and 9 o'clock never flicker between the two on float noise.</summary>
        private const float UprightEps = 1e-3f;

        /// <summary>Hot-reload / teardown: forget the published occluder.</summary>
        public static void ResetOccluder()
        {
            _occFrame = -100;
            _occCenter = Vector2.zero;
            _occMid = _occHalf = _occRIn = _occROut = _occAlpha = 0f;
        }

        // plate style cache (RadialWedgeGraphic's border fields don't dirty the mesh themselves)
        private Color _plateBorder = new Color(-1f, 0f, 0f, 0f);
        private float _plateBorderW = -1f;

        // bend cache (see ArcBendCache)
        private readonly ArcBendCache _bend = new ArcBendCache();

        public ArcPlateLabel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Root = (RectTransform)go.transform;
            Root.anchorMin = Root.anchorMax = new Vector2(0.5f, 0.5f);
            Root.pivot = new Vector2(0.5f, 0.5f);
            Root.sizeDelta = Vector2.zero;
            _group = go.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _group.alpha = 0f;

            var pgo = new GameObject("Plate", typeof(RectTransform));
            pgo.transform.SetParent(Root, false);
            var prt = (RectTransform)pgo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = Vector2.zero;
            Plate = pgo.AddComponent<UI.RadialWedgeGraphic>();
            Plate.raycastTarget = false;
            Plate.RimHighlight = 0f;
            Plate.FeatherSides = true;

            var tgo = new GameObject("Text", typeof(RectTransform));
            tgo.transform.SetParent(Root, false);
            Text = tgo.AddComponent<TextMeshProUGUI>();
            Text.alignment = TextAlignmentOptions.Center;
            Text.enableWordWrapping = false;
            Text.enableAutoSizing = false;
            Text.overflowMode = TextOverflowModes.Overflow;
            Text.richText = true;
            Text.raycastTarget = false;
            var trt = Text.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);

            go.SetActive(false);
        }

        /// <summary>Current fade, 0..1.</summary>
        public float Alpha => _alpha;

        /// <summary>Fade toward shown (<paramref name="show"/>) or hidden at <paramref name="speed"/>
        /// per second. Fully hidden deactivates the objects (no draw cost). Returns true while the
        /// label is on screen at all — lay it out only then.</summary>
        public bool StepFade(bool show, float dt, float speed)
        {
            _alpha = Mathf.MoveTowards(_alpha, show ? 1f : 0f, Mathf.Max(0f, dt) * speed);
            bool active = _alpha > 0.001f;
            if (Root.gameObject.activeSelf != active)
            {
                Root.gameObject.SetActive(active);
                if (active) Invalidate(); // a re-enabled TMP rebuilds a FLAT mesh: bend it again
            }
            _group.alpha = _alpha;
            // Fully faded out: LayoutAt stops publishing, so zero the record now instead of leaving
            // the last (small) fade alpha for a yielding label to read for up to two more frames.
            if (Occludes && !active) _occAlpha = 0f;
            return active;
        }

        /// <summary>Gone at once (menu closed / canvas hidden). Idempotent.</summary>
        public void HideNow()
        {
            _alpha = 0f;
            _group.alpha = 0f;
            if (Occludes) _occAlpha = 0f; // nothing to yield to any more
            if (Root.gameObject.activeSelf) Root.gameObject.SetActive(false);
        }

        /// <summary>Force the next <see cref="Layout"/> to bend the text again.</summary>
        public void Invalidate() => _bend.Invalidate();

        /// <summary>Plate paint. Only dirties the plate mesh when something actually changed.</summary>
        public void StylePlate(Color fill, Color border, float borderWidth)
        {
            borderWidth = Mathf.Max(0f, borderWidth);
            if (Plate.color != fill) Plate.color = fill; // Graphic.color dirties itself
            if (border != _plateBorder || !Mathf.Approximately(borderWidth, _plateBorderW))
            {
                _plateBorder = border;
                _plateBorderW = borderWidth;
                Plate.BorderColor = border;
                Plate.BorderWidth = borderWidth;
                Plate.SideBorders = borderWidth > 0.05f;
                Plate.SideWidthInner = Plate.SideWidthOuter = Mathf.Max(0.5f, borderWidth);
                Plate.RefreshGeometry();
            }
        }

        /// <summary>The plate's angular half-width (radians) for a text of straight length
        /// <paramref name="textLen"/> with <paramref name="padPx"/> of plate beyond EACH end, when
        /// the text midline runs at radius <paramref name="rMid"/>.</summary>
        public static float HalfSpanFor(float textLen, float padPx, float rMid)
            => rMid <= 1f ? Mathf.PI : (Mathf.Max(0f, textLen) * 0.5f + Mathf.Max(0f, padPx)) / rMid;

        /// <summary>
        /// Lay the label around a ring: <paramref name="centerAnchored"/> is the ring centre in canvas
        /// anchored coords; the plate spans radii [<paramref name="innerR"/>, innerR + thickness],
        /// centred straight over the top (<paramref name="top"/>) or straight under the bottom; the
        /// text runs along the plate's mid radius. Call AFTER the text, font, size and colour are
        /// final — the bend reads the built mesh.
        /// </summary>
        public void Layout(Vector2 centerAnchored, float innerR, float thickness, bool top, float textLen, float padPx)
            // RadialWedgeGraphic takes ImGui-convention angles (y-down): -PI/2 = top, +PI/2 = bottom.
            => LayoutAt(centerAnchored, innerR, thickness, top ? -Mathf.PI * 0.5f : Mathf.PI * 0.5f, textLen, padPx);

        /// <summary>
        /// Lay the label around a ring centred on ANY angle. <paramref name="midAngle"/> is in the
        /// wedges' ImGui convention (radians, y-down, clockwise on screen): -PI/2 = 12 o'clock, 0 = 3,
        /// +PI/2 = 6, PI = 9 — so the outward direction in canvas (y-up) space is (cos a, -sin a).
        /// The plate spans radii [<paramref name="innerR"/>, innerR + thickness] and
        /// <see cref="HalfSpanFor"/> either side of the angle; the text rides the plate's mid radius,
        /// rotated to the tangent and bent glyph by glyph, upright on both halves (class remarks).
        /// Call AFTER the text, font, size and colour are final — the bend reads the built mesh.
        /// </summary>
        public void LayoutAt(Vector2 centerAnchored, float innerR, float thickness, float midAngle, float textLen, float padPx)
        {
            if (Root.anchoredPosition != centerAnchored) Root.anchoredPosition = centerAnchored;
            float rMid = innerR + thickness * 0.5f;
            float half = HalfSpanFor(textLen, padPx, rMid);
            Plate.SetGeometry(innerR, innerR + thickness, midAngle - half, midAngle + half, fullRing: false);

            // Outward unit vector in canvas space, and which half of the ring we are on. The upper
            // half (incl. the exact horizontal) reads with "up" OUTWARD (rotation = theta - 90), the
            // lower half with "up" INWARD (theta + 90) — the binding label / CornerTag rule, so every
            // curved word on the wheel follows one convention.
            var dir = new Vector2(Mathf.Cos(midAngle), -Mathf.Sin(midAngle));
            bool outwardUp = dir.y >= -UprightEps;
            float thetaDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float rotDeg = outwardUp ? thetaDeg - 90f : thetaDeg + 90f;

            var trt = Text.rectTransform;
            var pos = dir * rMid;
            if (!(Mathf.Abs(pos.x - _textPos.x) < 0.001f && Mathf.Abs(pos.y - _textPos.y) < 0.001f))
            {
                _textPos = pos;
                trt.anchoredPosition = pos;
            }
            // Rotating the rect never touches the TMP mesh (it lives in local space), so the bend
            // cache stays valid while the label slides round the ring — only the side matters to it.
            if (!(Mathf.Abs(Mathf.DeltaAngle(rotDeg, _textRotDeg)) < 0.001f))
            {
                _textRotDeg = rotDeg;
                trt.localRotation = Quaternion.Euler(0f, 0f, rotDeg);
            }
            var size = new Vector2(Mathf.Max(8f, textLen + 8f), Mathf.Max(8f, thickness));
            if ((trt.sizeDelta - size).sqrMagnitude > 0.01f)
            {
                trt.sizeDelta = size;
                _bend.Invalidate(); // a resize dirties TMP's LAYOUT (not its properties): bend it again
            }

            _bend.CurveIfStale(Text, rMid, outwardUp);

            if (Occludes)
            {
                _occFrame = Time.frameCount;
                _occCenter = centerAnchored;
                _occMid = midAngle;
                _occHalf = half;
                _occRIn = innerR;
                _occROut = innerR + thickness;
                _occAlpha = _alpha;
            }
            else
            {
                // Yield to a fresh occluder around the SAME ring centre whose band overlaps ours: fade
                // by how close its angular span comes to ours (full at touching, none YieldSoftPx of arc
                // apart), times its own fade — so it tracks the plate's slide and fade with no pops.
                // The occluder is published from the radial's draw hook and read here from Update, so
                // "fresh" allows the one-step lag between the two.
                float yieldAmt = 0f;
                if (Time.frameCount - _occFrame <= 2 && _occAlpha > 0.001f
                    && (_occCenter - centerAnchored).sqrMagnitude < 4f
                    && _occRIn < innerR + thickness && _occROut > innerR)
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(_occMid * Mathf.Rad2Deg, midAngle * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                    float sep = (d - _occHalf - half) * rMid;          // px of arc between the two spans
                    yieldAmt = _occAlpha * Mathf.Clamp01(1f - sep / YieldSoftPx);
                }
                _group.alpha = _alpha * (1f - yieldAmt);
            }
        }
    }
}
