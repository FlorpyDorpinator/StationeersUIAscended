using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// BORROWS vanilla's body-damage doll (PlayerStateWindow.PersonDamageObject, the scene
    /// object 'PanelBodyHeath') into our HUD box, exactly the way <see cref="PortraitWidget"/>
    /// borrows the portrait. We do NOT synthesise the silhouette (that is BodyDollWidget's job)
    /// — we reparent the REAL vanilla GameObject so its seven colored part-images keep being
    /// tinted every frame by StatusUpdates.HandleDamageIndicators for free, no matter where the
    /// transform lives.
    ///
    /// Only the grey body-background Image directly on PersonDamageObject is disabled while
    /// borrowed, so the colored parts read against OUR box background instead of vanilla's
    /// silhouette; it is re-enabled on restore. Nothing is ever nulled or destroyed.
    ///
    /// Vanilla keeps toggling the object's activeSelf every frame — it is shown only when the
    /// player is injured (PlayerStateWindow.Update → Human.IsDamaged). FlorpyDorp WANTS that
    /// injured-only behaviour, so we accept it: uninjured means vanilla-inactive means we
    /// simply show an empty box. <see cref="RestoreDoll"/> hands the transform back to vanilla
    /// (original parent, sibling index, geometry and the bg Image) the moment the widget stops
    /// being wanted. Because the borrowed doll lives UNDER our Root, integration MUST call
    /// RestoreDoll() before the panel is Destroyed (HudSystem does this via RestoreAnyPortraits,
    /// which runs before the destroy loop) — otherwise destroying Root would take the vanilla
    /// doll with it.
    /// </summary>
    internal sealed class DamageDollBorrowWidget : HudElementView
    {
        private PanelGraphic _box;      // optional frame behind the borrowed doll
        private RectTransform _holder;  // empty anchor the doll reparents into

        // Borrow bookkeeping (all captured on the first successful borrow, restored on release).
        private bool _borrowed;
        // Only ONE widget may hold the single vanilla doll at a time (see TryBorrow).
        private static DamageDollBorrowWidget _dollOwner;
        private RectTransform _dollRt;
        private Transform _origParent;
        private int _origSiblingIndex;
        private Vector3 _origLocalPos;
        private Vector2 _origAnchoredPos;
        private Vector2 _origSizeDelta;
        private Vector2 _origAnchorMin;
        private Vector2 _origAnchorMax;
        private Vector2 _origPivot;
        private Vector3 _origScale;
        private Image _bgImage;         // the 'healthbodybg' silhouette on the doll root
        private bool _bgWasEnabled;

        private Vector2 _nativeSize = new Vector2(100f, 100f);
        private float _appliedScale = -1f;

        // The borrowed doll's background box takes the trapezoid insets (base supplies the sliders).
        protected override bool SupportsTrapezoid => true;

        // The optional frame IS a real PanelGraphic (fill/border/glass all apply to it).
        protected override bool SupportsPanelAppearance => true;

        // The borrowed doll is vanilla's own body silhouette — no accent-tinted surface or
        // scalable text of ours anywhere in this widget.
        protected override bool UsesAccentColor => false;
        protected override bool UsesFontScale => false;

        // The frame is optional, "box" key, default ON — its inspector row is named "Box frame".
        protected override bool OptionalPanelBackgroundIsOff => !Def.GetBFor(EditBare(Def), "box", true);
        protected override string OptionalPanelBackgroundToggleName => "Box frame";

        protected override void BuildContent(RectTransform root)
        {
            _box = MakePanel(root, "DamageDollBox");

            var holderGo = new GameObject("DollHolder", typeof(RectTransform));
            holderGo.transform.SetParent(root, false);
            _holder = (RectTransform)holderGo.transform;
            _holder.anchorMin = _holder.anchorMax = new Vector2(0.5f, 0.5f);
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;
            var c = CenterFor(scale);
            var s = SizeFor(scale);

            ((RectTransform)_box.transform).anchoredPosition = c;
            _box.SetShape(s.x, s.y,
                Radius(Def.RTLFor(LayoutBare)), Radius(Def.RTRFor(LayoutBare)), Radius(Def.RBRFor(LayoutBare)), Radius(Def.RBLFor(LayoutBare)),
                InsetTop(scale), InsetBottom(scale));

            _holder.anchoredPosition = c;
            _holder.sizeDelta = s;

            // Force a re-fit next frame (box size changed).
            _appliedScale = -1f;
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            // Retry the borrow every frame until the singleton and its doll exist (both are
            // null until in-world, and the doll may be freshly created after a reload).
            try
            {
                if (!_borrowed) TryBorrow();
                if (_borrowed) FitDoll(scale);
            }
            catch { }

            // Vanilla toggles the doll's activeSelf by injury (only shown when damaged). Our
            // box follows: when the player is healthy the doll is inactive → hide the whole
            // box so an empty frame never sits there (FlorpyDorp: "hidden if not needed").
            bool dollActive = false;
            try { dollActive = _borrowed && _dollRt != null && _dollRt.gameObject.activeSelf; } catch { }

            bool showBox = Def.GetBFor(LayoutBare, "box", true) && dollActive;
            _box.enabled = showBox;
            if (showBox)
            {
                _box.color = FillColor();
                _box.BorderColor = BorderColor();
                _box.BorderWidth = BorderWidthFor();
                ApplyGlass(_box);
            }
        }

        private void TryBorrow()
        {
            // Only a WANTED widget borrows — a suit-mode copy fading OUT must not keep re-grabbing
            // the doll during its fade, or the incoming bare-mode copy never gets it.
            if (Fader != null && !Fader.Target) return;

            var psw = Assets.Scripts.UI.PlayerStateWindow.Instance;
            if (psw == null) return;
            var doll = psw.PersonDamageObject;
            if (doll == null) return;

            var rt = doll.transform as RectTransform;
            if (rt == null) return;

            // One owner at a time (two copies reparenting the SAME vanilla doll would fight). A
            // wanted widget may take the doll from an owner that is itself fading OUT (not wanted)
            // so the bare↔suit handoff completes; it never steals from a WANTED owner, so two
            // same-tier copies can't fight — the extra one just renders an empty box.
            var owner = _dollOwner;
            if (owner != null && owner != this && owner._borrowed)
            {
                if (owner.Fader != null && owner.Fader.Target) return;
                owner.RestoreDoll();
            }

            // Cache everything needed to hand the doll back untouched.
            _dollRt = rt;
            _origParent = rt.parent;
            _origSiblingIndex = rt.GetSiblingIndex();
            _origLocalPos = rt.localPosition;
            _origAnchoredPos = rt.anchoredPosition;
            _origSizeDelta = rt.sizeDelta;
            _origAnchorMin = rt.anchorMin;
            _origAnchorMax = rt.anchorMax;
            _origPivot = rt.pivot;
            _origScale = rt.localScale;

            // Native size for fitting: the rect it occupied in vanilla (fall back to sizeDelta).
            Vector2 native = rt.rect.size;
            if (native.x < 1f || native.y < 1f) native = _origSizeDelta;
            if (native.x < 1f || native.y < 1f) native = new Vector2(100f, 100f);
            _nativeSize = native;

            // Hide only the grey body-background silhouette on the doll root; the colored
            // part-images are separate children and keep rendering.
            _bgImage = doll.GetComponent<Image>();
            if (_bgImage != null)
            {
                _bgWasEnabled = _bgImage.enabled;
                _bgImage.enabled = false;
            }

            // Reparent into our box. Centre anchors AND centre pivot so the borrowed rect
            // sits perfectly centred in the holder (the vanilla pivot was a corner, which
            // pushed the doll off-centre).
            rt.SetParent(_holder, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            _borrowed = true;
            _dollOwner = this;
            _appliedScale = -1f;
        }

        /// <summary>Scale the borrowed doll to fit the box each frame (dirty-guarded). We do
        /// NOT touch activeSelf — vanilla owns that (injured-only), by design.</summary>
        private void FitDoll(float scale)
        {
            if (_dollRt == null) { _borrowed = false; return; }

            var s = SizeFor(scale);
            float pad = Def.GetFFor(LayoutBare, "padPx", 4f) * scale;
            float availW = Mathf.Max(1f, s.x - pad * 2f);
            float availH = Mathf.Max(1f, s.y - pad * 2f);
            float fit = Mathf.Min(availW / _nativeSize.x, availH / _nativeSize.y);
            if (fit <= 0f || float.IsNaN(fit) || float.IsInfinity(fit)) fit = 1f;

            if (!Mathf.Approximately(_appliedScale, fit))
            {
                _dollRt.localScale = new Vector3(fit, fit, 1f);
                _appliedScale = fit;
            }
            // Keep it centered even if vanilla nudged the anchored position.
            if (_dollRt.anchoredPosition != Vector2.zero)
                _dollRt.anchoredPosition = Vector2.zero;
        }

        /// <summary>Give the doll back to vanilla: original parent + sibling index, original
        /// geometry and anchors, and the grey background Image's enabled state. Public because
        /// the HUD orchestrator calls it the moment the widget stops being wanted (tier drop,
        /// toggle, document swap, or teardown) — and it MUST run before Root is destroyed, or
        /// the reparented doll dies with it. Safe to call when nothing is borrowed.</summary>
        /// <summary>Self-heal: hand the doll back the instant our Root is about to die.</summary>
        protected override void OnBeforeDestroy() => RestoreDoll();

        public void RestoreDoll()
        {
            if (!_borrowed) return;
            _borrowed = false;
            if (ReferenceEquals(_dollOwner, this)) _dollOwner = null;
            try
            {
                if (_bgImage != null) _bgImage.enabled = _bgWasEnabled;
            }
            catch { }
            try
            {
                if (_dollRt != null)
                {
                    // Detach from our (possibly dying) subtree even if the original parent is
                    // gone (fake-null) — send it to the scene root rather than let it die with us.
                    _dollRt.SetParent(_origParent != null ? _origParent : null, false);
                    _dollRt.anchorMin = _origAnchorMin;
                    _dollRt.anchorMax = _origAnchorMax;
                    _dollRt.pivot = _origPivot;
                    _dollRt.sizeDelta = _origSizeDelta;
                    _dollRt.anchoredPosition = _origAnchoredPos;
                    _dollRt.localPosition = _origLocalPos;
                    _dollRt.localScale = _origScale;
                    _dollRt.SetSiblingIndex(_origSiblingIndex);
                }
            }
            catch { }
            _dollRt = null;
            _bgImage = null;
            _appliedScale = -1f;
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.Bool("Box frame", () => d.GetBFor(EditBare(d), "box", true), v => d.SetBFor(EditBare(d), "box", v)));
            into[into.Count - 1].Group = HudPropGroup.Appearance;

            into.Add(HudProp.F("Inner padding px", () => d.GetFFor(EditBare(d), "padPx", 4f),
                v => d.SetFFor(EditBare(d), "padPx", Mathf.Max(0f, v)), 0f, 40f));
            into[into.Count - 1].Group = HudPropGroup.Layout;
        }
    }
}
