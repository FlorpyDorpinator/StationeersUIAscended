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

        private bool _borrowed;
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
            var su = Assets.Scripts.UI.StatusUpdates.Instance;
            var strip = su != null ? su.StatusTransform : null;
            var rt = strip as RectTransform;
            if (rt == null) return;

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
        }

        private static readonly List<RectTransform> _cells = new List<RectTransform>();

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
        }
    }
}
