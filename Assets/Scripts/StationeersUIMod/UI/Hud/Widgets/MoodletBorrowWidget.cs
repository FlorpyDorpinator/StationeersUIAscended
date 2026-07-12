using System.Collections.Generic;
using UnityEngine;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// BORROWS vanilla's own moodlet strip (<c>StatusUpdates.StatusTransform</c>) into our
    /// moodlet bar, the same way <see cref="PortraitWidget"/> borrows the portrait and
    /// <see cref="DamageDollBorrowWidget"/> borrows the damage doll. Rather than re-implement
    /// moodlet rendering (icons, dedupe, flashing, layout) — which drifts from vanilla — we
    /// reparent the REAL strip so the game keeps driving it exactly as it always does; we only
    /// move it to where the element sits and optionally scale it.
    ///
    /// Vanilla still toggles the strip's activeSelf every frame (shown only when a status is
    /// live) and lays out its own children via its own layout group, so all of that keeps
    /// working after the reparent. <see cref="RestoreMoodlets"/> hands it back (parent, sibling
    /// index, geometry, scale) on teardown/tier-drop — it MUST run before our Root is destroyed
    /// or the strip would die with it (HudSystem calls it from RestoreAnyPortraits, which runs
    /// before every destroy loop).
    /// </summary>
    internal sealed class MoodletBorrowWidget : HudElementView
    {
        private RectTransform _holder;

        private bool _borrowed;
        private RectTransform _stripRt;
        private Transform _origParent;
        private int _origSiblingIndex;
        private Vector3 _origLocalPos;
        private Vector2 _origAnchoredPos;
        private Vector2 _origAnchorMin, _origAnchorMax, _origPivot;
        private Vector3 _origScale;

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
                if (_borrowed) Place(scale);
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
            _origAnchorMin = rt.anchorMin;
            _origAnchorMax = rt.anchorMax;
            _origPivot = rt.pivot;
            _origScale = rt.localScale;

            // Reparent + centre. Vanilla's own layout group arranges the moodlet children
            // relative to the strip, so it stays a tidy row wherever we put it.
            rt.SetParent(_holder, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            _borrowed = true;
        }

        /// <summary>Keep the strip centred in the element and apply the F9 scale each frame
        /// (dirty-guarded). We never touch its activeSelf — vanilla owns show/hide.</summary>
        private void Place(float scale)
        {
            if (_stripRt == null) { _borrowed = false; return; }
            float s = Mathf.Clamp(Def.GetF("moodletScale", 1.4f), 0.3f, 5f) * scale;
            var want = new Vector3(s, s, 1f);
            if (_stripRt.localScale != want) _stripRt.localScale = want;
            if (_stripRt.anchoredPosition != Vector2.zero) _stripRt.anchoredPosition = Vector2.zero;
        }

        /// <summary>Hand the moodlet strip back to vanilla (parent, sibling index, geometry,
        /// scale). Public because the HUD orchestrator calls it the moment the widget stops
        /// being wanted; it MUST run before Root is destroyed. Safe when nothing is borrowed.</summary>
        public void RestoreMoodlets()
        {
            if (!_borrowed) return;
            _borrowed = false;
            try
            {
                if (_stripRt != null)
                {
                    if (_origParent != null) _stripRt.SetParent(_origParent, false);
                    _stripRt.anchorMin = _origAnchorMin;
                    _stripRt.anchorMax = _origAnchorMax;
                    _stripRt.pivot = _origPivot;
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
            into.Add(HudProp.F("Moodlet scale", () => d.GetF("moodletScale", 1.4f),
                v => d.SetF("moodletScale", Mathf.Clamp(v, 0.3f, 5f)), 0.3f, 5f));
        }
    }
}
