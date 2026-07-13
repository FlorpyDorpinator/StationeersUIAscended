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

        private void Apply(float scale)
        {
            if (_stripRt == null) { _borrowed = false; return; }

            if (_grid != null)
            {
                // Horizontal row, centred. Keep vanilla's 192px cell so each moodlet renders
                // pixel-identical; the whole strip is scaled down instead (localScale below),
                // which preserves the icon/text proportions inside every cell.
                if (_grid.startAxis != GridLayoutGroup.Axis.Horizontal) _grid.startAxis = GridLayoutGroup.Axis.Horizontal;
                if (_grid.constraint != GridLayoutGroup.Constraint.FixedRowCount) _grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                if (_grid.constraintCount != 1) _grid.constraintCount = 1;
                if (_grid.childAlignment != TextAnchor.MiddleCenter) _grid.childAlignment = TextAnchor.MiddleCenter;
                var wantSpacing = new Vector2(10f, 0f);
                if (_grid.spacing != wantSpacing) _grid.spacing = wantSpacing;
            }

            // Scale the whole 192px-cell strip down to a bar-friendly size. The default puts
            // a cell at ~62px on screen; the F9 slider tunes it.
            float sc = Mathf.Clamp(Def.GetF("moodletScale", 0.32f), 0.08f, 1.5f) * scale;
            var want = new Vector3(sc, sc, 1f);
            if (_stripRt.localScale != want) _stripRt.localScale = want;
            if (_stripRt.anchoredPosition != Vector2.zero) _stripRt.anchoredPosition = Vector2.zero;
        }

        /// <summary>Hand the moodlet strip back to vanilla — parent, sibling index, geometry,
        /// AND its original grid settings (axis/constraint/spacing/cell/alignment). Public
        /// because the HUD orchestrator calls it the moment the widget stops being wanted; it
        /// MUST run before Root is destroyed. Safe when nothing is borrowed.</summary>
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
                }
                _grid = null;
            }
            catch { }
            try
            {
                if (_stripRt != null)
                {
                    if (_origParent != null) _stripRt.SetParent(_origParent, false);
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
