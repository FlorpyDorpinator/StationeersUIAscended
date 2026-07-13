using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// BORROWS vanilla's own moodlet strip (<c>StatusUpdates.StatusTransform</c>, the scene
    /// object "StatusIcons") into our bar — 1-to-1 vanilla behaviour, just moved and
    /// re-oriented. Rather than re-implement moodlet rendering (icons, dedupe, flashing) we
    /// reparent the REAL strip so the game keeps driving it exactly as always.
    ///
    /// FIRST PRINCIPLES: vanilla lays the strip out with a UGUI <see cref="GridLayoutGroup"/>
    /// — 192×192 cells, StartAxis=Vertical, FixedColumnCount=1 = a giant single-column stack.
    /// So we don't add a second layout group (that just fights the grid, which is why the
    /// strip stayed a huge vertical column). We RE-CONFIGURE vanilla's own grid in place:
    /// one horizontal ROW, centred, and we scale the whole strip down so the 192px cells read
    /// at a sane size while every cell renders EXACTLY as vanilla draws it. Everything is
    /// cached and restored on release, so vanilla gets its strip back untouched.
    ///
    /// Vanilla still toggles the strip's activeSelf and fills its cells every frame, so all
    /// of that keeps working after the reparent. <see cref="RestoreMoodlets"/> hands it back
    /// (parent, sibling index, geometry, grid settings) on teardown/tier-drop — it MUST run
    /// before our Root is destroyed (HudSystem calls it from RestoreAnyPortraits, which runs
    /// before every destroy loop) or the strip would die with our Root.
    /// </summary>
    internal sealed class MoodletBorrowWidget : HudElementView
    {
        private RectTransform _holder;
        private CanvasGroup _holderGroup; // drives the F9 "Moodlet transparency" fade

        private bool _borrowed;
        // Only ONE widget may hold the single vanilla strip at a time (see TryBorrow) — a
        // duplicate would fight over the reparent and crash vanilla's StatusUpdates.
        private static MoodletBorrowWidget _stripOwner;
        private RectTransform _stripRt;
        private Transform _origParent;
        private int _origSiblingIndex;
        private Vector3 _origLocalPos;
        private Vector2 _origAnchoredPos, _origSizeDelta, _origAnchorMin, _origAnchorMax, _origPivot;
        private Vector3 _origScale;

        // Vanilla's GridLayoutGroup, captured so restore puts its column back exactly.
        private GridLayoutGroup _grid;
        private GridLayoutGroup.Axis _gStartAxis;
        private GridLayoutGroup.Constraint _gConstraint;
        private int _gConstraintCount;
        private Vector2 _gSpacing, _gCellSize;
        private TextAnchor _gChildAlign;

        protected override void BuildContent(RectTransform root)
        {
            var holderGo = new GameObject("MoodletHolder", typeof(RectTransform));
            holderGo.transform.SetParent(root, false);
            _holder = (RectTransform)holderGo.transform;
            _holder.anchorMin = _holder.anchorMax = new Vector2(0.5f, 0.5f);

            // Opt this subtree back INTO raycasts (the HUD root CanvasGroup blocks them) so
            // vanilla's moodlet hover tooltip still fires after we reparent the strip here.
            var grp = holderGo.AddComponent<CanvasGroup>();
            grp.ignoreParentGroups = true;
            grp.interactable = true;
            grp.blocksRaycasts = true;
            _holderGroup = grp;
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;
            _holder.anchoredPosition = CenterFor(scale);
            _holder.sizeDelta = SizeFor(scale);
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            try
            {
                if (!_borrowed) TryBorrow();
                if (_borrowed) Apply(scale);
            }
            catch { }
        }

        private void TryBorrow()
        {
            // Only a WANTED widget borrows. Without this, a suit-mode copy that is fading OUT
            // re-borrows every frame during its ~0.4s fade, so the incoming bare-mode copy never
            // gets the strip and it snaps back to vanilla ("moodlets return to the right side when
            // I hit bare mode"). Fader.Target is the panel's visible/hidden goal.
            if (Fader != null && !Fader.Target) return;

            var su = Assets.Scripts.UI.StatusUpdates.Instance;
            var strip = su != null ? su.StatusTransform : null;
            var rt = strip as RectTransform;
            if (rt == null) return;

            // One owner at a time (two copies reparenting the SAME vanilla strip crashed
            // StatusUpdates.ManagerUpdate). A wanted widget may take the strip from an owner that
            // is itself fading OUT (not wanted) so the bare↔suit handoff completes; it never steals
            // from another WANTED widget, so two same-tier copies can't fight — the extra one just
            // renders empty. Prefer ONE element with a per-tier layout override over duplicates.
            var owner = _stripOwner;
            if (owner != null && owner != this && owner._borrowed)
            {
                if (owner.Fader != null && owner.Fader.Target) return; // a wanted owner keeps it
                owner.RestoreMoodlets();                               // unwanted holder → take over
            }

            _stripRt = rt;
            _origParent = rt.parent;
            _origSiblingIndex = rt.GetSiblingIndex();
            _origLocalPos = rt.localPosition;
            _origAnchoredPos = rt.anchoredPosition;
            _origSizeDelta = rt.sizeDelta;
            _origAnchorMin = rt.anchorMin;
            _origAnchorMax = rt.anchorMax;
            _origPivot = rt.pivot;
            _origScale = rt.localScale;

            // Cache + flip vanilla's grid to a single centred horizontal row.
            _grid = rt.GetComponent<GridLayoutGroup>();
            if (_grid != null)
            {
                _gStartAxis = _grid.startAxis;
                _gConstraint = _grid.constraint;
                _gConstraintCount = _grid.constraintCount;
                _gSpacing = _grid.spacing;
                _gCellSize = _grid.cellSize;
                _gChildAlign = _grid.childAlignment;
            }

            rt.SetParent(_holder, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            _borrowed = true;
            _stripOwner = this;
        }

        private static readonly List<RectTransform> _cells = new List<RectTransform>();
        private static readonly List<Graphic> _gfxBuf = new List<Graphic>(); // brightness-tint scratch

        private void Apply(float scale)
        {
            if (_stripRt == null) { _borrowed = false; return; }

            // Take layout OVER from vanilla's grid: the GridLayoutGroup re-flattens any
            // per-cell offset every layout pass, so it can't coexist with bending the row
            // onto the visor curve. We hand-place the cells instead (a centred horizontal
            // row = what the reconfigured grid produced) and warp each cell's position, the
            // same technique the compass ticks use. Flat mode → warp is identity → the row
            // is exactly the plain centred row, so nothing regresses when the curve is off.
            if (_grid != null && _grid.enabled) _grid.enabled = false;

            // Scale the whole 192px-cell strip down to a bar-friendly size; the F9 slider
            // tunes it. Cells keep their 192px cell so each moodlet renders pixel-identical.
            float sc = Mathf.Clamp(Def.GetF("moodletScale", 0.32f), 0.08f, 1.5f) * scale;
            var want = new Vector3(sc, sc, 1f);
            if (_stripRt.localScale != want) _stripRt.localScale = want;
            if (_stripRt.anchoredPosition != Vector2.zero) _stripRt.anchoredPosition = Vector2.zero;

            // F9 "Moodlet transparency" (0 = solid, 1 = invisible): faded via the holder's own
            // CanvasGroup. It already ignores the HUD-root group (for the hover tooltip), so this
            // alpha is the moodlets' final opacity — nothing else fights it.
            if (_holderGroup != null)
            {
                float a = 1f - Mathf.Clamp01(Def.GetF("moodletTransparency", 0f));
                if (!Mathf.Approximately(_holderGroup.alpha, a)) _holderGroup.alpha = a;
            }

            LayoutCellsCurved(sc);
        }

        /// <summary>Centred horizontal row of the currently-active moodlet cells, each bent
        /// onto the visor curve. Vanilla still owns which cells are active (SetActive); we
        /// only place the live ones.</summary>
        private void LayoutCellsCurved(float sc)
        {
            _cells.Clear();
            for (int i = 0; i < _stripRt.childCount; i++)
            {
                var ch = _stripRt.GetChild(i) as RectTransform;
                if (ch != null && ch.gameObject.activeSelf) _cells.Add(ch);
            }
            int n = _cells.Count;
            if (n == 0) return;

            float cell = _gCellSize.x > 1f ? _gCellSize.x : 192f;   // strip-local px
            float stride = cell + 10f;                              // gap between centres
            float startX = -(n - 1) * 0.5f * stride;
            Vector2 center = _holder.anchoredPosition;              // element centre, canvas coords
            var mid = new Vector2(0.5f, 0.5f);
            var cellSize = new Vector2(cell, cell);

            // F9 "Moodlet brightness" (1 = native, 0 = black): a CanvasRenderer colour tint that
            // MULTIPLIES on top of whatever colour vanilla drew the cell (its own flash included),
            // so it can only darken — a colour multiply can't lift a coloured sprite to pure white.
            // Alpha stays 1 so icon transparency and the holder's transparency slider are untouched.
            // At 1.0 the tint is white = identity, which also resets any earlier darkening for free.
            float bright = Mathf.Clamp01(Def.GetF("moodletBrightness", 1f));
            var tint = new Color(bright, bright, bright, 1f);

            for (int k = 0; k < n; k++)
            {
                var ch = _cells[k];
                // Centre-anchor each cell so its position maths is about its middle (the grid
                // had anchored them upper-left).
                if (ch.anchorMin != mid || ch.anchorMax != mid || ch.pivot != mid)
                { ch.anchorMin = ch.anchorMax = mid; ch.pivot = mid; }
                if (ch.sizeDelta != cellSize) ch.sizeDelta = cellSize;

                float lx = startX + k * stride;                    // flat strip-local x
                Vector3 abs = new Vector3(center.x + lx * sc, center.y, 0f); // canvas coords
                if (HudWarp.Enabled) abs = HudWarp.Warp(abs);
                // Back to strip-local (strip sits at centre, scaled by sc).
                ch.anchoredPosition3D = new Vector3(
                    (abs.x - center.x) / sc, (abs.y - center.y) / sc, abs.z / Mathf.Max(0.0001f, sc));

                // Re-assert the tint every frame: vanilla owns Graphic.color (the mesh), while this
                // is the CanvasRenderer multiplier on top — the two never collide.
                ch.GetComponentsInChildren<Graphic>(true, _gfxBuf);
                for (int g = 0; g < _gfxBuf.Count; g++)
                {
                    var cr = _gfxBuf[g].canvasRenderer;
                    if (cr != null) cr.SetColor(tint);
                }
            }
        }

        /// <summary>Hand the moodlet strip back to vanilla — parent, sibling index, geometry,
        /// AND its original grid settings (axis/constraint/spacing/cell/alignment). Public
        /// because the HUD orchestrator calls it the moment the widget stops being wanted; it
        /// MUST run before Root is destroyed. Safe when nothing is borrowed.</summary>
        /// <summary>Self-heal: hand the strip back the instant our Root is about to die,
        /// whatever destroyed it (profile switch, mode flip, hot reload, shutdown).</summary>
        protected override void OnBeforeDestroy() => RestoreMoodlets();

        public void RestoreMoodlets()
        {
            if (!_borrowed) return;
            _borrowed = false;
            if (ReferenceEquals(_stripOwner, this)) _stripOwner = null;
            try
            {
                if (_grid != null)
                {
                    _grid.startAxis = _gStartAxis;
                    _grid.constraint = _gConstraint;
                    _grid.constraintCount = _gConstraintCount;
                    _grid.spacing = _gSpacing;
                    _grid.cellSize = _gCellSize;
                    _grid.childAlignment = _gChildAlign;
                    _grid.enabled = true; // we disabled it to own the layout; give it back
                }
                _grid = null;
            }
            catch { }
            try
            {
                if (_stripRt != null)
                {
                    // Detach from OUR (possibly dying) subtree no matter what: a destroyed
                    // original parent (fake-null) must still send the strip to the scene root,
                    // never leave it under our canvas to be destroyed with it.
                    _stripRt.SetParent(_origParent != null ? _origParent : null, false);
                    _stripRt.anchorMin = _origAnchorMin;
                    _stripRt.anchorMax = _origAnchorMax;
                    _stripRt.pivot = _origPivot;
                    _stripRt.sizeDelta = _origSizeDelta;
                    _stripRt.anchoredPosition = _origAnchoredPos;
                    _stripRt.localPosition = _origLocalPos;
                    _stripRt.localScale = _origScale;
                    _stripRt.SetSiblingIndex(_origSiblingIndex);
                }
            }
            catch { }
            _stripRt = null;
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.F("Moodlet scale", () => d.GetF("moodletScale", 0.32f),
                v => d.SetF("moodletScale", Mathf.Clamp(v, 0.08f, 1.5f)), 0.08f, 1.5f));
            into.Add(HudProp.F("Moodlet transparency", () => d.GetF("moodletTransparency", 0f),
                v => d.SetF("moodletTransparency", Mathf.Clamp01(v)), 0f, 1f));
            into.Add(HudProp.F("Moodlet brightness", () => d.GetF("moodletBrightness", 1f),
                v => d.SetF("moodletBrightness", Mathf.Clamp01(v)), 0f, 1f));
        }
    }
}
