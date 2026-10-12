using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI
{

    public static class UnityRadialView
    {
        private const float HoverBulge = 13f;      // px the hovered wedge grows outward
        private const float AnimSpeed = 16f;       // lerp rate (1/s) - snappier modern feel
        // Border thickness comes from config (UIAConfig.RadialBorderWidth) so it is tunable live.
        private const float HoverContentPop = 6f;  // extra outward for icon+label on hover
        private const float HoverScale = 1.04f;    // subtle label/icon scale on hover
        private const float SwipeChevronSize = 10f;  // D-022: the "you can swipe this" chevron (was 5)
        private const float SwipeChevronInset = 12f; // its centre, px inside the outer radius (was 9)

        private static Canvas _canvas;
        private static CanvasGroup _group;
        private static TMP_FontAsset _font;
        private static string _fontFor;            // config value the cache was resolved for
        private static bool _fontIsBoldAsset;
        private static RingView _main;
        private static RingView _satellite;
        private static ReadoutView _readout;      // the detail readout (follows the action)
        private static ReadoutView _readoutHome;  // keeps the MAIN hub dressed while the detail moved
        private static RectTransform _closeRoot;   // hub CLOSE button (always at the MAIN hub)
        private static RadialWedgeGraphic _closeBand;
        private static TextMeshProUGUI _closeLabel;
        private static TextMeshProUGUI _pageMain;  // "1/2  -  Q: next page" above a paged ring
        private static TextMeshProUGUI _pageSat;
        private static float _openAnim;
        // #12: this frame's canvas scale (WheelGridScale.Resolution) and the player's text size
        // (WheelGridScale.Text). Set at the top of Render; everything below works in 1080p units.
        private static float _k = 1f;
        private static float _tx = 1f;

        // --- D-022: the curved ACTION WORD over the main ring's top outside edge ("TAKE", "OPEN") —
        // what a click on the hovered wedge will do. Rebuilt with the canvas; nothing survives Shutdown.
        private static ArcPlateLabel _verbArc;
        private static string _verbSrc;            // the verb the display cache was built from
        private static bool _verbSrcUpper;         // ...and the ALL-CAPS option at the time
        private static string _verbDisplay;        // WedgeText(_verbSrc), cached (no per-frame alloc)
        private static float _verbLen;             // its straight (= arc) length at the cached style
        private static float _verbLenSize = -1f;
        private static TMP_FontAsset _verbLenFont;
        // Visible clearance between a hovered wedge's FULLY bulged rim (bulge + hover border + AA fringe)
        // and the plate's own AA fringe. Was 8 px added on top of the un-grown border/fringe (~5 px
        // visible) and — worse — measured against a wedge that usually wasn't the one hovered.
        private const float VerbGapPx = 3f;
        private const float VerbFadeSpeed = 9f;    // alpha per second: ~0.11 s in/out — neat, not sluggish
        // 2026-09-26: the plate follows the HOVERED wedge round the ring (FlorpyDorp: "dynamically
        // adjusts to each wedge, not just exist at the top"). Angles are the wedges' ImGui convention
        // (radians, -PI/2 = 12 o'clock, increasing clockwise on screen).
        private const float VerbSlideSpeed = 22f;  // 1/s exponential ease: ~90% of a hop in ~0.1 s, fps-independent
        private static float _verbAng;             // where the plate is drawn now (kept in [-PI, PI))
        private static float _verbAngTarget;       // the hovered wedge's mid angle it eases toward
        private static bool _verbOnSat;            // anchored on the child (satellite) ring, not the main ring

        // --- 0.9.0 radial glass FX (frosted backdrop + sheen/edge-light). Resolved once per
        // Render, pushed onto every wedge and the close band. Statics so the nested RingView and
        // UpdateCloseButton read one consistent frame's state.
        private static bool _fxFrostActive;    // assign the shared glass material to wedges this frame
        private static float _fxFrostStrength; // uv0.x volume when active, else 0
        private static float _fxSheen;
        private static float _fxEdgeLight;
        private static bool _fxDemand;         // a frost-enabled radial is visible (backdrop demand)

        /// <summary>True while a frost-enabled radial is on screen and the HUD's Tier C master is on.
        /// HudSystem folds this into its backdrop gate so the blurred-screen capture keeps running for
        /// the radial even when the HUD's own elements wouldn't ask for it. Cleared on Hide/Shutdown.</summary>
        public static bool RadialFrostWanted => _fxDemand;

        // ---------- public API ----------

        /// <param name="actionVerb">D-022: the curved action word to show outside the hovered wedge (null =
        /// none). Honoured only when <paramref name="verbFromCaller"/> is true — the live radial decides
        /// it (it knows where a click would actually land). Otherwise (the F10 editor preview) it is
        /// derived from the hovered main-ring wedge via <see cref="RadialEntry.ClickVerb"/>.</param>
        public static void Render(Vector2 center, float innerR, float outerR,
            IList<RadialEntry> entries, int hovered, string title,
            Vector2? satCenter, float satInnerR, float satOuterR,
            IList<RadialEntry> satEntries, int satHovered, string satTitle,
            RadialEntry readoutEntry, string readoutHint, bool sticky,
            DynamicThing dragging = null, bool closeHovered = false,
            string pageText = null, string satPageText = null,
            string actionVerb = null, bool verbFromCaller = false)
        {
            EnsureCanvas();
            _group.alpha = 1f;
            _canvas.gameObject.SetActive(true);

            bool satActive = satEntries != null && satCenter.HasValue;

            // D-021: publish where this ring sits so the key-hint strip can wrap around its bottom.
            // Both the live radial and the F10 editor preview render through here. SCREEN px (what
            // the callers hit-test in), so it is published before the unit conversion below.
            RadialHintContext.PublishGeometry(center, outerR, satActive, satCenter ?? Vector2.zero, satOuterR);

            // #12: the canvas renders in 1080p reference UNITS, scaled up by the resolution factor
            // (x2 at 4K, 1 when ScaleWithResolution is off), so every wedge, font and fixed pixel
            // constant below keeps its designed proportion on any screen. The callers' radii are
            // SCREEN px (RadialMenu / RadialEditorMode scale the configured radii by the SAME factor
            // for hit-testing), so convert them once here; centres convert in Pos().
            _k = WheelGridScale.Resolution();
            if (!Mathf.Approximately(_canvas.scaleFactor, _k)) _canvas.scaleFactor = _k;
            float invK = 1f / _k;
            innerR *= invK; outerR *= invK; satInnerR *= invK; satOuterR *= invK;
            _tx = WheelGridScale.Text();

            UpdateRadialFx();
            UpdateCloseButton(center, innerR, closeHovered);

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            // Slightly nicer open curve (ease out)
            float openT = EaseOut01(_openAnim);
            _openAnim = Mathf.Lerp(_openAnim, 1f, dt * AnimSpeed);

            // D-022: the action word, now riding the HOVERED wedge round the ring. The page counters
            // yield to it: each sits above the band the word may occupy when it passes 12 o'clock.
            if (!verbFromCaller)
                actionVerb = dragging == null && readoutEntry != null && entries != null
                    && hovered >= 0 && hovered < entries.Count && ReferenceEquals(entries[hovered], readoutEntry)
                    ? readoutEntry.ClickVerb() : null;
            // The live radial's verb can name a CHILD-ring wedge (hold mode: HoldReleaseEntry); the F10
            // preview derives it from the main ring's hovered wedge only, so it anchors there.
            bool verbMayUseSat = satActive && verbFromCaller;
            float verbBandTop = UpdateActionWord(center, outerR, actionVerb, openT, dt,
                entries != null ? entries.Count : 0, hovered,
                verbMayUseSat, satCenter ?? center, satOuterR, satEntries != null ? satEntries.Count : 0, satHovered);
            // Main counter: reserved whenever the word could appear (not only while it shows), so it
            // never bobs as the pointer moves. Child counter: only hold mode ever puts the word on a
            // child ring (sticky mode names nothing while one is open), so only there is it reserved.
            float pageHop = Mathf.Max(0f, verbBandTop - 24f);
            float satPageHop = verbMayUseSat && !sticky ? Mathf.Max(0f, verbBandTop - 18f) : 0f;

            UpdatePageLabel(_pageMain, pageText, center, outerR + 24f + pageHop);
            UpdatePageLabel(_pageSat, satPageText, satCenter ?? center, satOuterR + 18f + satPageHop);

            _main.Render(center, innerR, outerR, entries, hovered, openT, dimmed: satActive, dragging);

            if (satActive)
                _satellite.Render(satCenter.Value, satInnerR, satOuterR, satEntries, satHovered, openT, dimmed: false, dragging);
            else
                _satellite.Hide();

            // The DETAIL readout moves into the middle of the child radial while one is open,
            // but the main hub keeps its circle, title and colours — both hubs stay dressed
            // (visible in the editor side by side).
            if (satActive)
            {
                _readout.Render(satCenter.Value, Mathf.Max(satInnerR - 2f, 46f), satTitle ?? title, null,
                    readoutEntry, readoutHint, sticky);
                // B1: the main hub names what RMB does, from the SAME CanGoBack the hint strip reads
                // (RadialMenu.Draw publishes it just before this call) — a sticky child ring at the
                // root is a close, not a back. The F10 editor preview publishes no interaction, so it
                // keeps the generic "back". String literals only: no per-frame allocation.
                string rmbHint = !RadialHintContext.InteractionFresh || RadialHintContext.CanGoBack ? "RMB: back"
                    : RadialHintContext.Parked > 0 ? "RMB: close, drop parked" : "RMB: close";
                _readoutHome.Render(center, innerR, title, null, null, rmbHint, sticky);
            }
            else
            {
                _readout.Render(center, innerR, title, satTitle, readoutEntry, readoutHint, sticky);
                _readoutHome.Hide();
            }
        }

        public static void Hide()
        {
            _fxDemand = false; // no visible radial ⇒ drop backdrop demand (HudSystem stands it down)
            if (_canvas == null) return;
            _canvas.gameObject.SetActive(false);
            _openAnim = 0f;
            // Hand every wedge back to the stock UI material so a pooled wedge never keeps the glass
            // material while hidden (idempotent; a no-op when nothing was assigned).
            _main?.ClearFx();
            _satellite?.ClearFx();
            Hud.HudFxMaterials.Unassign(_closeBand);
            if (_verbArc != null)
            {
                _verbArc.HideNow(); // a reopened radial never flashes the last menu's word
                Hud.HudFxMaterials.Unassign(_verbArc.Plate);
            }
        }

        /// <summary>Full teardown — required for clean ScriptEngine hot reloads.</summary>
        public static void Shutdown()
        {
            if (_verbArc != null) Hud.HudFxMaterials.Unassign(_verbArc.Plate);
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _verbArc = null;          // destroyed with the canvas
            _verbSrc = null;
            _verbSrcUpper = false;
            _verbDisplay = null;
            _verbLen = 0f;
            _verbLenSize = -1f;
            _verbLenFont = null;
            _verbAng = _verbAngTarget = -Mathf.PI * 0.5f;
            _verbOnSat = false;
            ArcPlateLabel.ResetOccluder();
            _group = null;
            _main = null;
            _satellite = null;
            _readout = null;
            _readoutHome = null;
            _closeRoot = null;
            _closeBand = null;
            _closeLabel = null;
            _pageMain = null;
            _pageSat = null;
            _font = null;
            _fontFor = null;
            _openAnim = 0f;
            _fxDemand = false;
            _fxFrostActive = false;
            _fxFrostStrength = 0f;
            _k = 1f;
            _tx = 1f;
            WheelGridScale.Reset();
        }

        // ---------- 0.9.0 glass FX ----------

        /// <summary>Resolve this frame's radial glass-FX state from config + Tier C availability, and
        /// declare backdrop demand. Cheap (a handful of reads); called once per Render.</summary>
        private static void UpdateRadialFx()
        {
            _fxSheen = UIAConfig.RadialSheen != null ? UIAConfig.RadialSheen.Value : 0f;
            _fxEdgeLight = UIAConfig.RadialEdgeLight != null ? UIAConfig.RadialEdgeLight.Value : 0f;

            // Demand = the user wants frost AND the HUD's Tier C master is on. Set true even before
            // the backdrop is running so HudSystem's gate spins the capture up for us; the material
            // assignment below then engages once it is Active + the bundle resolved.
            bool frostCfg = UIAConfig.RadialFrost != null && UIAConfig.RadialFrost.Value
                && Hud.HudConfig.FxTierC != null && Hud.HudConfig.FxTierC.Value;
            _fxDemand = frostCfg;

            _fxFrostActive = frostCfg && Hud.HudBackdrop.Active
                && HudShaderStore.TierBAvailable && Hud.HudFxMaterials.Available;
            _fxFrostStrength = _fxFrostActive
                ? (UIAConfig.RadialFrostStrength != null ? UIAConfig.RadialFrostStrength.Value : 0.85f)
                : 0f;
        }

        /// <summary>Push this frame's glass FX onto one wedge (or the close band): sheen/edge-light are
        /// pure vertex colour (always applied, default 0 = the flat look), the shared glass material is
        /// assigned only while frost is active. Idempotent + per-frame cheap — the setters are
        /// dirty-guarded and Assign/Unassign are dictionary lookups.</summary>
        private static void ApplyWedgeFx(RadialWedgeGraphic w)
        {
            if (w == null) return;
            w.Sheen = _fxSheen;
            w.EdgeLight = _fxEdgeLight;
            w.FxStrength = _fxFrostStrength;
            if (_fxFrostActive)
            {
                if (!Hud.HudFxMaterials.Assign(w, "glass")) Hud.HudFxMaterials.Unassign(w);
            }
            else Hud.HudFxMaterials.Unassign(w);
        }

        // ---------- infrastructure ----------

        private static void EnsureCanvas()
        {
            if (_canvas != null) return;

            var go = new GameObject("UIAscended_RadialCanvas");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the vanilla HUD. NOTE: the game's ImGui output canvas jumps to 32767 when
            // ImGuiManager.SetBlockUguiClicks(true) is active, so our radial would render
            // *under* the ImGui layer while a modal blocks clicks. Kept below that on purpose.
            _canvas.sortingOrder = 5000;
            _group = go.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;   // hover is driven by RadialMenu, not the EventSystem

            _main = new RingView(go.transform, "MainRing");
            _satellite = new RingView(go.transform, "SatelliteRing");
            _readout = new ReadoutView(go.transform, Font());
            _readoutHome = new ReadoutView(go.transform, Font());

            // Hub CLOSE button — created last so it draws over the hub backing.
            var cgo = new GameObject("CloseButton", typeof(RectTransform));
            cgo.transform.SetParent(go.transform, false);
            _closeRoot = (RectTransform)cgo.transform;
            _closeRoot.anchorMin = _closeRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _closeRoot.sizeDelta = Vector2.zero;

            var bandGo = new GameObject("Band", typeof(RectTransform));
            bandGo.transform.SetParent(_closeRoot, false);
            _closeBand = bandGo.AddComponent<RadialWedgeGraphic>();
            _closeBand.raycastTarget = false;

            var clGo = new GameObject("Label", typeof(RectTransform));
            clGo.transform.SetParent(_closeRoot, false);
            _closeLabel = clGo.AddComponent<TextMeshProUGUI>();
            _closeLabel.font = Font();
            _closeLabel.alignment = TextAlignmentOptions.Center;
            _closeLabel.enableAutoSizing = true;
            _closeLabel.fontSizeMin = 8f;
            _closeLabel.fontSizeMax = 15f;
            _closeLabel.enableWordWrapping = false;
            _closeLabel.raycastTarget = false;

            _pageMain = MakePageLabel(go.transform, "PageMain");
            _pageSat = MakePageLabel(go.transform, "PageSat");

            // D-022: created last so it draws over everything on this canvas (it floats outside the
            // hovered wedge, whose fully bulged rim VerbGapPx keeps it clear of; it can overlap the
            // dimmed main ring only when it names an inward-facing CHILD-ring wedge in hold mode).
            _verbArc = new ArcPlateLabel(go.transform, "ActionWord");
            _verbArc.Text.font = Font();
            _verbArc.Occludes = true; // the key-hint strip under the ring yields where the word swings over it
        }

        /// <summary>
        /// D-022 — the curved ACTION WORD on a glass plate hugging the ring's outside edge: "TAKE",
        /// "OPEN", "SWAP", "EQUIP"... — what a click (or, in hold mode, the release) on the hovered
        /// wedge will do (<see cref="RadialEntry.ClickVerb"/>). Pie-menu practice (Hopkins' "Pie Menu
        /// Cookbook"; Kurtenbach/Buxton's marking menus) is to preview what a selection will do before
        /// it commits. Styled from the theme (ArcAccent / ArcPlateFill / ArcPlateBorder — AUTO = the
        /// wedge colours) and sized by the hub's own verb size knob, so it travels with the profile
        /// theme. Fades in/out (~0.1 s) instead of popping.
        /// <para>2026-09-26 (FlorpyDorp): it no longer sits at 12 o'clock — it rides OUTSIDE the
        /// HOVERED wedge, centred on that wedge's mid angle, 3 px clear of its fully bulged rim, and
        /// eases round the ring along the SHORTEST wrap-around path when the hover moves (exponential,
        /// frame-rate independent). A word fading in from hidden appears AT its wedge (no sweep in from
        /// the last spot); one fading out keeps easing to where it was headed and dissolves there. The
        /// text follows the seal convention (<see cref="ArcPlateLabel.LayoutAt"/>): upright and
        /// left-to-right on the lower half. In hold mode the word can name a CHILD-ring wedge — it then
        /// rides the child ring (a jump between rings re-fades instead of sweeping across the wheel).
        /// The key-hint strip under the ring yields (fades) where the word swings over it — see
        /// <see cref="ArcPlateLabel.Occludes"/>.</para>
        /// <para>Returns the outer extent of the word's band above a ring's rim (plate + border + a
        /// 12 px clearance): the page counters sit above it. That space is RESERVED whenever the word
        /// could appear (not only while it is showing — it can pass 12 o'clock at any time), so a
        /// counter stays put instead of bobbing as the pointer moves between wedges and the hub.</para>
        /// </summary>
        private static float UpdateActionWord(Vector2 center, float outerR, string verb, float openT, float dt,
            int mainCount, int mainHovered, bool satActive, Vector2 satCenter, float satOuterR, int satCount, int satHovered)
        {
            float bw = UIAConfig.RadialBorderWidth != null ? UIAConfig.RadialBorderWidth.Value : 3.2f;
            float fs = (UIAConfig.RadialTextVerb != null ? UIAConfig.RadialTextVerb.Value : 15f) * _tx; // #12
            float feather = UIAConfig.RadialEdgeFeather != null ? Mathf.Max(0f, UIAConfig.RadialEdgeFeather.Value) : 1.25f;
            // The hovered wedge's FULL visible extent past outerR (RingView + RadialWedgeGraphic): the
            // bulge (HoverBulge — the lerp target, never exceeded), its hover border (bw * 1.2) and its
            // AA fringe. The plate's own inner AA fringe reaches `feather` inside its inner radius, so it is
            // added again; VerbGapPx is then the true visible gap. Using the TARGET bulge (not the live,
            // still-growing one) keeps the plate from pumping in and out as the hover moves.
            float lift = HoverBulge + Mathf.Max(0f, bw) * 1.2f + 2f * feather + VerbGapPx;
            float thickness = fs * 1.3f + 10f;
            // The band's outer extent above the rim: plate + its border + 12 px clearance for a counter.
            float bandTop = lift + thickness + Mathf.Max(0f, bw) + 12f;

            if (_verbArc == null) return bandTop;
            bool show = !string.IsNullOrEmpty(verb);

            // Where the word belongs this frame. Resolved only while there IS a word: a fading-out
            // word keeps its last target (it dissolves where it was going).
            if (show)
            {
                bool onSat;
                float target;
                if (ResolveVerbAnchor(center, mainCount, mainHovered, satActive, satCenter, satCount, satHovered,
                        out onSat, out target))
                {
                    bool hidden = _verbArc.Alpha <= 0.001f;
                    if (!hidden && onSat != _verbOnSat)
                    {
                        // A different ring: never sweep across the wheel between two centres — re-fade.
                        _verbArc.HideNow();
                        hidden = true;
                    }
                    _verbOnSat = onSat;
                    _verbAngTarget = WrapPi(target);
                    if (hidden) _verbAng = _verbAngTarget; // appear AT the wedge, no sweep in
                }
                else if (_verbArc.Alpha <= 0.001f)
                {
                    show = false; // no hovered wedge to sit on (never expected): don't pop up at a stale spot
                }
            }

            if (!_verbArc.StepFade(show, dt, VerbFadeSpeed)) return bandTop;

            if (show)
            {
                // While fading OUT the last word and style are kept (it dissolves where it was headed).
                bool upper = UIAConfig.RadialUppercaseLabels != null && UIAConfig.RadialUppercaseLabels.Value;
                if (!ReferenceEquals(verb, _verbSrc) || upper != _verbSrcUpper || _verbDisplay == null)
                {
                    _verbSrc = verb;
                    _verbSrcUpper = upper;
                    _verbDisplay = WedgeText(verb);
                    _verbLenSize = -1f; // re-measure
                }

                var tmp = _verbArc.Text;
                SyncFont(tmp);
                tmp.fontStyle = WedgeFontStyle();
                if (!Mathf.Approximately(tmp.fontSize, fs)) tmp.fontSize = fs;
                if (tmp.text != _verbDisplay) tmp.text = _verbDisplay;
                tmp.color = RadialPalette.ArcAccent.Value;
                if (!Mathf.Approximately(_verbLenSize, fs) || !ReferenceEquals(_verbLenFont, tmp.font))
                {
                    // preferredWidth measures the text ALREADY set (the flat hint bar's proven pattern),
                    // so the measured and the displayed (then bent) string can never differ.
                    _verbLen = tmp.preferredWidth;
                    _verbLenSize = _verbLen > 0.5f ? fs : -1f; // a zero width (font not ready) re-measures
                    _verbLenFont = tmp.font;
                }

                _verbArc.StylePlate(RadialPalette.ArcPlateFill.Value, RadialPalette.ArcPlateBorder.Value, bw * 0.5f);
                ApplyWedgeFx(_verbArc.Plate); // same glass as the wedges (frost / sheen / edge light)
            }

            // Ease toward the hovered wedge along the shortest way round (never the long way through
            // 180+ degrees); 1 - e^(-k dt) makes the step frame-rate independent. Runs while fading out
            // too, so a word caught mid-hop finishes the hop as it dissolves.
            float d = DeltaRad(_verbAng, _verbAngTarget);
            _verbAng = Mathf.Abs(d) < 1e-4f ? _verbAngTarget
                : WrapPi(_verbAng + d * (1f - Mathf.Exp(-VerbSlideSpeed * dt)));

            // Lay out on the ring it belongs to (following a hub drag, fading or not). A word fading
            // out on a child ring that has just closed has no centre to follow: it dissolves in place.
            bool haveRing = !_verbOnSat || satActive;
            if (haveRing && _verbDisplay != null)
            {
                Vector2 ringCenter = _verbOnSat ? satCenter : center;
                float ringOuter = _verbOnSat ? satOuterR : outerR;
                _verbArc.LayoutAt(Pos(ringCenter), ringOuter + lift, thickness, _verbAng, _verbLen, fs * 0.8f);
            }
            _verbArc.Root.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, openT); // rides the open animation
            return bandTop;
        }

        /// <summary>The ring + mid angle (ImGui convention, like the wedges) the action word belongs on
        /// this frame. A hovered CHILD-ring wedge wins (hold mode names what its release runs); a child
        /// ring with nothing hovered means the release runs the SOURCE wedge, which the child ring now
        /// covers — so the word sits at the child ring's far tip, straight along the swipe (the child's
        /// centre lies on the source wedge's mid line: RadialMenu.OpenSatellite). Otherwise the hovered
        /// main-ring wedge. False = no hovered wedge anywhere.</summary>
        private static bool ResolveVerbAnchor(Vector2 center, int mainCount, int mainHovered,
            bool satActive, Vector2 satCenter, int satCount, int satHovered, out bool onSat, out float angle)
        {
            if (satActive && satCount > 0)
            {
                onSat = true;
                if (satHovered >= 0 && satHovered < satCount)
                    angle = WedgeMidAngle(satHovered, satCount);
                else
                {
                    Vector2 d = satCenter - center; // ImGui coords (y-down) — atan2 gives the wedge convention
                    angle = d.sqrMagnitude > 1f ? Mathf.Atan2(d.y, d.x) : -Mathf.PI * 0.5f;
                }
                return true;
            }
            onSat = false;
            if (mainHovered >= 0 && mainHovered < mainCount)
            {
                angle = WedgeMidAngle(mainHovered, mainCount);
                return true;
            }
            angle = 0f;
            return false;
        }

        /// <summary>Mid angle of wedge <paramref name="index"/> of <paramref name="count"/> — exactly
        /// RingView's aMid (a0 + sector/2) and RadialMenu.SectorFromMouse's partition: wedge 0 centred
        /// at 12 o'clock (-PI/2), then clockwise on screen (ImGui y-down, angle increasing).</summary>
        private static float WedgeMidAngle(int index, int count)
            => -Mathf.PI * 0.5f + Mathf.PI * 2f / Mathf.Max(1, count) * index;

        /// <summary>Signed shortest angular step from <paramref name="from"/> to <paramref name="to"/>,
        /// radians in [-PI, PI).</summary>
        private static float DeltaRad(float from, float to)
            => Mathf.Repeat(to - from + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;

        /// <summary>An angle wrapped into [-PI, PI), so the eased value never drifts unbounded.</summary>
        private static float WrapPi(float a) => Mathf.Repeat(a + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;

        private static TextMeshProUGUI MakePageLabel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = Font();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 9f;
            tmp.fontSizeMax = 14f;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(260f, 18f);
            return tmp;
        }

        /// <summary>The "1/2 - Q: next page" counter above a paged ring.</summary>
        private static void UpdatePageLabel(TextMeshProUGUI label, string text, Vector2 imguiCenter, float yAbove)
        {
            if (label == null) return;
            bool show = !string.IsNullOrEmpty(text);
            label.gameObject.SetActive(show);
            if (!show) return;
            SyncFont(label);
            label.fontStyle = WedgeFontStyle();
            label.text = WedgeText(text);
            label.color = RadialPalette.TextDim.Value;
            // #12: the counter's box grows with the text size; its BOTTOM edge stays where it was
            // (the reserved band above the action word), so a bigger counter only grows upward.
            SetMax(label, 14f * _tx);
            label.rectTransform.sizeDelta = new Vector2(260f * _tx, 18f * _tx);
            label.rectTransform.anchoredPosition = Pos(imguiCenter) + new Vector2(0f, yAbove + 9f * (_tx - 1f));
        }

        /// <summary>The always-available exit: a band hugging the bottom of the hub circle.
        /// Anchored to the MAIN hub even while the readout has moved into a satellite.</summary>
        private static void UpdateCloseButton(Vector2 center, float innerR, bool hovered)
        {
            if (_closeRoot == null) return;
            _closeRoot.anchoredPosition = Pos(center);
            float hubR = innerR - 6f;

            _closeBand.SetGeometry(hubR * 0.52f, hubR * 0.94f, Mathf.PI / 3f, Mathf.PI * 2f / 3f, fullRing: false);
            _closeBand.SideBorders = false;
            _closeBand.FeatherSides = true;
            _closeBand.SideFeatherLimit = float.MaxValue;
            _closeBand.BorderWidth = 1.6f;
            _closeBand.BorderColor = RadialPalette.HubBorder.Value;
            _closeBand.color = hovered ? RadialPalette.HubCloseHover.Value : RadialPalette.HubCloseFill.Value;
            _closeBand.RimHighlight = 0f;
            ApplyWedgeFx(_closeBand);
            _closeBand.RefreshGeometry();

            SyncFont(_closeLabel);
            _closeLabel.fontStyle = WedgeFontStyle();
            _closeLabel.text = WedgeText("Close");
            _closeLabel.color = RadialPalette.HubCloseText.Value;
            // #12: taller text box with the text size, but never taller than the band itself
            // (0.52..0.94 of the hub radius); auto-size fits the word inside it.
            SetMax(_closeLabel, 15f * _tx);
            _closeLabel.rectTransform.sizeDelta = new Vector2(hubR * 1.1f,
                Mathf.Max(8f, Mathf.Min(18f * _tx, hubR * 0.42f)));
            _closeLabel.rectTransform.anchoredPosition = new Vector2(0f, -hubR * 0.73f);
        }

        /// <summary>
        /// The radial font. Resolution order: the configured name (substring match), else a
        /// font whose name says Bold (the Option A bold-caps look), else whatever the game
        /// loaded first. Re-resolves live when the config value changes (radial editor).
        /// </summary>
        internal static TMP_FontAsset Font()
        {
            string want = UIAConfig.RadialFontName != null ? UIAConfig.RadialFontName.Value : "";
            if (_font != null && _fontFor == want) return _font;
            _font = ResolveFont(want);
            _fontFor = want;
            _fontIsBoldAsset = _font != null
                && _font.name.IndexOf("bold", StringComparison.OrdinalIgnoreCase) >= 0;
            return _font;
        }

        /// <summary>Faux-bold only when the asset itself isn't a Bold face already.</summary>
        internal static FontStyles WedgeFontStyle()
            => _fontIsBoldAsset ? FontStyles.Normal : FontStyles.Bold;

        private static TMP_FontAsset ResolveFont(string want)
        {
            TMP_FontAsset first = null;
            TMP_FontAsset bold = null;
            TMP_FontAsset named = null;
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (all != null)
            {
                foreach (var f in all)
                {
                    if (f == null) continue;
                    if (first == null) first = f;
                    if (bold == null && f.name.IndexOf("bold", StringComparison.OrdinalIgnoreCase) >= 0)
                        bold = f;
                    if (named == null && !string.IsNullOrEmpty(want)
                        && f.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                        named = f;
                }
            }
            var chosen = named ?? bold ?? first;
            if (chosen == null) chosen = TMP_Settings.defaultFontAsset;
            return chosen;
        }

        /// <summary>Every distinct TMP font the game has loaded — the radial editor's dropdown.</summary>
        internal static List<string> AllFontNames()
        {
            var names = new List<string>();
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (all == null) return names;
            foreach (var f in all)
                if (f != null && !names.Contains(f.name))
                    names.Add(f.name);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>Cheap per-frame font sync (no-op unless the config changed) — shared by
        /// every overlay view so a font picked in the radial editor propagates everywhere.</summary>
        internal static void SyncFont(TextMeshProUGUI tmp)
        {
            var f = Font();
            if (f != null && tmp.font != f) tmp.font = f;
        }

        /// <summary>ImGui packs colors R-in-low-byte (IM_COL32). Decoding as ARGB swaps red
        /// and blue — which is exactly why the prefab wedges rendered orange accents as blue.</summary>
        internal static Color FromImGui(uint c)
        {
            byte r = (byte)(c & 0xFF);
            byte g = (byte)((c >> 8) & 0xFF);
            byte b = (byte)((c >> 16) & 0xFF);
            byte a = (byte)((c >> 24) & 0xFF);
            return new Color32(r, g, b, a);
        }

        /// <summary>ImGui screen coords (y-down) -> canvas local coords around a ring center.</summary>
        internal static Vector2 ToLocal(Vector2 imguiPoint, Vector2 imguiCenter)
            => new Vector2(imguiPoint.x - imguiCenter.x, -(imguiPoint.y - imguiCenter.y));

        internal static Vector2 CanvasAnchoredPos(Vector2 imguiCenter)
            => new Vector2(imguiCenter.x - Screen.width * 0.5f,
                           (Screen.height - imguiCenter.y) - Screen.height * 0.5f);

        /// <summary>#12: <see cref="CanvasAnchoredPos"/> for a canvas whose scaleFactor is
        /// <paramref name="scale"/> (anchored positions are in canvas UNITS = screen px / scale).
        /// The radial family's canvases (this one, the hint strip, the search panel) all use it.</summary>
        internal static Vector2 CanvasAnchoredPos(Vector2 imguiCenter, float scale)
            => CanvasAnchoredPos(imguiCenter) / (scale > 0.0001f ? scale : 1f);

        /// <summary>This canvas' anchored position for an ImGui screen point (this frame's scale).</summary>
        private static Vector2 Pos(Vector2 imguiCenter) => CanvasAnchoredPos(imguiCenter, _k);

        /// <summary>Set an auto-size ceiling only when it moved (pooled labels, written per frame).</summary>
        private static void SetMax(TextMeshProUGUI t, float max)
        {
            if (!Mathf.Approximately(t.fontSizeMax, max)) t.fontSizeMax = max;
        }

        /// <summary>The usable wedge one label box must stay inside (canvas units, ring-centre origin):
        /// the annulus [RIn, ROut] and the angular span AMid +/- HalfSpan (radians, the ring's own
        /// convention: a direction is (cos a, -sin a)), every edge inset by Pad.</summary>
        private struct WedgeBounds
        {
            public float RIn, ROut, AMid, HalfSpan, Pad;
            public bool FullRing;

            public bool Contains(float x, float y)
            {
                float r = Mathf.Sqrt(x * x + y * y);
                if (r < RIn + Pad || r > ROut - Pad) return false;
                if (FullRing) return true;
                float d = Mathf.Abs(Mathf.DeltaAngle(AMid * Mathf.Rad2Deg, Mathf.Atan2(-y, x) * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                float rem = HalfSpan - d;
                if (rem <= 0f) return false;
                return r * Mathf.Sin(Mathf.Min(rem, Mathf.PI * 0.5f)) >= Pad;   // distance to the side edge
            }

            /// <summary>Does the axis-aligned box centred at <paramref name="c"/> (half sizes hw, hh) fit?
            /// Corners bound the outer rim and the (convex) side edges; the edge midpoints bound the
            /// inner rim (the nearest point of a straight edge to the centre is its foot, which for a box
            /// straddling an axis is a midpoint) — enough for the annular sector.</summary>
            public bool Fits(Vector2 c, float hw, float hh)
            {
                for (int ix = -1; ix <= 1; ix++)
                    for (int iy = -1; iy <= 1; iy++)
                        if (!Contains(c.x + ix * hw, c.y + iy * hh)) return false;
                // The nearest point of the top/bottom edge to the ring centre (x = 0 when the box
                // straddles the vertical axis) and of the side edges (y = 0 likewise).
                if (c.x - hw < 0f && c.x + hw > 0f && (!Contains(0f, c.y - hh) || !Contains(0f, c.y + hh))) return false;
                if (c.y - hh < 0f && c.y + hh > 0f && (!Contains(c.x - hw, 0f) || !Contains(c.x + hw, 0f))) return false;
                return true;
            }

            /// <summary>C: the widest half-width (up to <paramref name="maxHalf"/>) a horizontal label box
            /// of half-height <paramref name="hh"/> centred at <paramref name="c"/> can have and still sit
            /// inside the wedge. 0 when not even a sliver fits (the caller then lowers the box).</summary>
            public float FitHalfWidth(Vector2 c, float hh, float maxHalf)
            {
                if (maxHalf <= 0f) return 0f;
                if (Fits(c, maxHalf, hh)) return maxHalf;
                if (!Fits(c, 0f, hh)) return 0f;
                float lo = 0f, hi = maxHalf;
                for (int it = 0; it < 12; it++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (Fits(c, mid, hh)) lo = mid; else hi = mid;
                }
                return lo;
            }
        }

        private static float EaseOut01(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t); // quadratic ease out
        }

        internal static string WedgeText(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return UIAConfig.RadialUppercaseLabels != null && UIAConfig.RadialUppercaseLabels.Value
                ? s.ToUpperInvariant() : s;
        }

        // ---------- one ring ----------

        private sealed class RingView
        {
            private readonly RectTransform _root;
            private readonly List<RadialWedgeGraphic> _wedges = new List<RadialWedgeGraphic>();
            private readonly List<Image> _icons = new List<Image>();
            private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
            private readonly List<TextMeshProUGUI> _states = new List<TextMeshProUGUI>();
            private readonly List<TriangleGraphic> _triUp = new List<TriangleGraphic>();
            private readonly List<TriangleGraphic> _triDown = new List<TriangleGraphic>();
            // A small outward chevron on any wedge that can be swiped past the rim (has a
            // slide-out satellite or is a click-in branch) — the "you can swipe this" hint.
            private readonly List<TriangleGraphic> _swipe = new List<TriangleGraphic>();
            // #4: the bound hotkey letter, badged near the HUB side of a setting wedge.
            private readonly List<TextMeshProUGUI> _hotkey = new List<TextMeshProUGUI>();
            // 1B.3: a grey tool-type binding label on a stable-geometry belt slot wedge
            // (occupied OR empty-but-bound), near the HUB side. Parallels _hotkey.
            private readonly List<TextMeshProUGUI> _binding = new List<TextMeshProUGUI>();
            // D-023: a persistent descriptive tag (RadialEntry.CornerTag, e.g. "RECENT ITEM") along the
            // wedge's OUTER rim — the binding label's twin at the other end of the wedge.
            private readonly List<TextMeshProUGUI> _tags = new List<TextMeshProUGUI>();
            private readonly List<string> _tagSrc = new List<string>();
            private readonly List<string> _tagDisplay = new List<string>();
            // B9: each tag's bend cache — the arc bend (a ForceMeshUpdate + autosize passes) re-runs
            // only when its text, size, radius or font actually changed (ArcPlateLabel's own cache).
            private readonly List<ArcBendCache> _tagBend = new List<ArcBendCache>();

            // Per-wedge string caches (parallel to the pools above), so the per-frame draw
            // allocates nothing while a radial is open. WedgeText (ToUpperInvariant) is cached
            // keyed on the source-label reference AND the ALL-CAPS option it was cased for
            // (_cachedUpper, ring-wide — B10); the hotkey badge letter caches its one-char string
            // keyed on the char. All self-heal on any change — no reset needed.
            private readonly List<string> _labelSrc = new List<string>();
            private readonly List<string> _labelDisplay = new List<string>();
            private readonly List<char> _lastBadgeLetter = new List<char>();
            private readonly List<string> _badgeStr = new List<string>();
            // Binding-label display cache (WedgeText'd), keyed on the source-label reference.
            private readonly List<string> _bindingSrc = new List<string>();
            private readonly List<string> _bindingDisplay = new List<string>();
            // Ctrl+digit bag badges ("^0".."^9") are constant — no per-frame concat.
            private static readonly string[] BagDigitBadges =
                { "^0", "^1", "^2", "^3", "^4", "^5", "^6", "^7", "^8", "^9" };

            // B10: the ALL-CAPS option the cased caches above were built for. Flipping it (F10 / the
            // radial editor) re-cases every cached wedge string on the next Render — the verb cache's
            // _verbSrcUpper rule, ring-wide. StaleSrc is a private sentinel no real label can be
            // reference-equal to (a null label included), so a stamped slot always recomputes.
            private bool _cachedUpper;
            private static readonly string StaleSrc = new string('\u0001', 1);
            // B13: how far a tagged wedge's icon may step inward to clear its CornerTag before it
            // shrinks instead (so a rim tag and its icon never collide).
            private const float TagIconInsetMax = 10f;
            private const float TagIconGap = 2f;

            public RingView(Transform parent, string name)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                _root = (RectTransform)go.transform;
                _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
                _root.pivot = new Vector2(0.5f, 0.5f);
                _root.sizeDelta = Vector2.zero;
            }

            public void Hide() => _root.gameObject.SetActive(false);

            /// <summary>Hand every pooled wedge back to the stock UI material (radial closed).</summary>
            public void ClearFx()
            {
                for (int i = 0; i < _wedges.Count; i++)
                    Hud.HudFxMaterials.Unassign(_wedges[i]);
            }

            public void Render(Vector2 center, float innerR, float outerR,
                IList<RadialEntry> entries, int hovered, float openAnim, bool dimmed,
                DynamicThing dragging)
            {
                _root.gameObject.SetActive(true);
                _root.anchoredPosition = Pos(center);
                _root.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, openAnim);

                int count = entries?.Count ?? 0;
                EnsureCapacity(count);

                // B10: the ALL-CAPS option changed since the cased caches were built — stamp every
                // slot stale so each wedge string is re-cased on this Render.
                bool upper = UIAConfig.RadialUppercaseLabels != null && UIAConfig.RadialUppercaseLabels.Value;
                if (upper != _cachedUpper)
                {
                    _cachedUpper = upper;
                    for (int k = 0; k < _labelSrc.Count; k++)
                    {
                        _labelSrc[k] = StaleSrc;
                        _bindingSrc[k] = StaleSrc;
                        _tagSrc[k] = StaleSrc;
                    }
                }

                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.04f);
                float sector = count > 0 ? Mathf.PI * 2f / count : 0f;
                float ringWidth = outerR - innerR;

                float gapRad = UIAConfig.RadialWedgeGapDeg != null
                    ? UIAConfig.RadialWedgeGapDeg.Value * Mathf.Deg2Rad : 0f;
                if (count > 1) gapRad = Mathf.Min(gapRad, sector * 0.3f);
                bool sideBorders = UIAConfig.RadialSideBorders == null || UIAConfig.RadialSideBorders.Value;
                float dimStrength = UIAConfig.RadialDimShading != null && !UIAConfig.RadialDimShading.Value ? 0f
                    : UIAConfig.RadialDimStrength != null ? UIAConfig.RadialDimStrength.Value : 1f;

                for (int i = 0; i < _wedges.Count; i++)
                {
                    bool used = i < count;
                    _wedges[i].gameObject.SetActive(used);
                    _icons[i].gameObject.SetActive(used);
                    _labels[i].gameObject.SetActive(used);
                    _states[i].gameObject.SetActive(used);
                    _triUp[i].gameObject.SetActive(used);
                    _triDown[i].gameObject.SetActive(used);
                    if (!used) { _swipe[i].gameObject.SetActive(false); _hotkey[i].gameObject.SetActive(false); _binding[i].gameObject.SetActive(false); _tags[i].gameObject.SetActive(false); Hud.HudFxMaterials.Unassign(_wedges[i]); continue; }

                    var entry = entries[i];
                    var wedge = _wedges[i];

                    float a0 = -Mathf.PI * 0.5f - sector * 0.5f + sector * i;
                    bool lone = count == 1;
                    float g = lone ? 0f : gapRad * 0.5f;
                    wedge.SetGeometry(innerR, outerR, a0 + g, a0 + sector - g, fullRing: lone);
                    wedge.SideBorders = sideBorders && !lone;
                    wedge.FeatherSides = !lone && gapRad > 0.0005f;
                    // Outward side fringe may fill at most half the gap, or it bleeds into
                    // the neighbour (gap is angular; convert to px at the band's mid radius).
                    wedge.SideFeatherLimit = lone ? float.MaxValue
                        : gapRad * 0.5f * (innerR + outerR) * 0.5f;
                    wedge.SideWidthInner = UIAConfig.RadialSideWidthInner != null
                        ? UIAConfig.RadialSideWidthInner.Value : 3.2f;
                    wedge.SideWidthOuter = UIAConfig.RadialSideWidthOuter != null
                        ? UIAConfig.RadialSideWidthOuter.Value : 3.2f;

                    bool isHovered = i == hovered && entry.Enabled;

                    // Dynamic background shading: while one wedge is highlighted the others
                    // recede. Strength is user-tunable, 0 disables (Jackson's shading, exposed).
                    Color fillTarget = ResolveUguiFill(entry, isHovered, dimmed, dragging);
                    Color borderTarget = ResolveUguiBorder(entry, isHovered, dimmed);

                    if (hovered >= 0 && !isHovered && dimStrength > 0f)
                    {
                        float rgbMult = 1f - 0.38f * dimStrength;
                        float aMult = 1f - 0.48f * dimStrength;
                        fillTarget = new Color(fillTarget.r * rgbMult, fillTarget.g * rgbMult,
                            fillTarget.b * rgbMult, fillTarget.a * aMult);
                    }

                    // Lerp current toward target (smooth)
                    wedge.color = Color.Lerp(wedge.color, fillTarget, dt * AnimSpeed);

                    float bulgeTarget = isHovered ? HoverBulge : 0f;
                    if (!Mathf.Approximately(wedge.OuterBulge, bulgeTarget))
                    {
                        wedge.OuterBulge = Mathf.Lerp(wedge.OuterBulge, bulgeTarget, dt * AnimSpeed);
                    }

                    // Border: always on (the orange outline), hover shifts its colour.
                    float bw = UIAConfig.RadialBorderWidth.Value;
                    float borderWTarget = isHovered ? bw * 1.2f : bw;
                    wedge.BorderWidth = Mathf.Lerp(wedge.BorderWidth, borderWTarget, dt * AnimSpeed * 0.7f);
                    wedge.BorderColor = Color.Lerp(wedge.BorderColor, borderTarget, dt * AnimSpeed);

                    // Rim gloss: subtle, and only really visible on the selected wedge.
                    // Intensity is the "shader look" knob (0 = flat), colour comes from the
                    // RimShine palette entry.
                    float shineIntensity = UIAConfig.RadialShineIntensity != null
                        ? UIAConfig.RadialShineIntensity.Value : 1f;
                    float shineTarget = (isHovered ? 0.55f : 0.10f) * shineIntensity;
                    wedge.RimHighlight = Mathf.Lerp(wedge.RimHighlight, shineTarget, dt * AnimSpeed);

                    // 0.9.0 glass FX (frost material + sheen/edge-light) — set before the rebuild
                    // below bakes sheen/edge-light/FxStrength into the mesh.
                    ApplyWedgeFx(wedge);

                    // Any geometry property (bulge/border/rim) changed -> rebuild mesh this frame
                    wedge.RefreshGeometry();

                    // === Content placement (icon + label + state) with hover pop ===
                    float aMid = a0 + sector * 0.5f;
                    float baseMidR = (innerR + outerR) * 0.5f;
                    float hoverExtra = isHovered ? (HoverBulge * 0.32f + HoverContentPop) : 0f;
                    float midR = baseMidR + hoverExtra;

                    var dir = new Vector2(Mathf.Cos(aMid), -Mathf.Sin(aMid));
                    var slot = dir * midR;

                    // C (2026-10-11, "SPLIT COUNT" running past its wedge at 1440p): every flat text box
                    // on this wedge is fitted INSIDE the wedge's real outline — the annulus (hover bulge
                    // included) and the two side edges at the gap — inset by the border + a margin, so
                    // auto-size / wrapping shrink the text instead of letting it spill, at any wedge
                    // count, wheel size, text size or resolution factor.
                    var wb = new WedgeBounds
                    {
                        RIn = innerR,
                        ROut = outerR + wedge.OuterBulge,
                        AMid = aMid,
                        HalfSpan = lone ? Mathf.PI : Mathf.Max(0.01f, sector * 0.5f - g),
                        Pad = Mathf.Max(1f, UIAConfig.RadialBorderWidth.Value) + 3f,
                        FullRing = lone,
                    };

                    // Swipe affordance: a small outward chevron at the rim of any wedge that
                    // opens a satellite (slide-out) or a click-in branch — "you can swipe this".
                    // Scroll wedges never show it (they carry their own up/down triangles).
                    // A slide-out that yields NOTHING (a tool with no settings/slots — wire
                    // cutters, a wrench) must not advertise a swipe it can't honour, so the
                    // slide-out arm is gated on real content, not just a non-null provider.
                    bool swipeable = !entry.IsScrollAdjust && entry.Enabled
                        && ((entry.HasSlideOut && entry.SlideOutHasContent()) || entry.IsBranch);
                    var sw = _swipe[i];
                    sw.gameObject.SetActive(swipeable);
                    if (swipeable)
                    {
                        // D-022: doubled (5 -> 10 px wide) — the old chevron was too small to notice.
                        // A drawn mesh, not a glyph (the game font has no arrows); pulled 3 px further
                        // in so the bigger tip still clears the rim border.
                        sw.Configure(pointsUp: true, size: SwipeChevronSize);
                        float swAlpha = (isHovered ? 0.95f : 0.45f) * (dimmed ? 0.4f : 1f);
                        var swc = RadialPalette.TextPrimary.Value; swc.a *= swAlpha;
                        sw.color = swc;
                        var swRt = sw.rectTransform;
                        swRt.anchoredPosition = dir * (outerR - SwipeChevronInset);
                        swRt.localEulerAngles = new Vector3(0f, 0f,
                            Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
                        sw.SetVerticesDirty();
                    }

                    // The bound-hotkey badge near the HUB (inner) side of a wedge: a device
                    // SETTING wedge shows its bound LETTER (#4); a bindable BAG shows its
                    // Ctrl+digit as "^N" (#3). Set before the scroll-wedge early-return so scroll
                    // settings (suit pressure/temp) show their letter too.
                    var hk = _hotkey[i];
                    string badge = null;
                    if (entry.CanHotkey)
                    {
                        char letter = Core.WedgeHotkeys.LetterFor(entry.HotkeyInteractable);
                        if (letter != '\0')
                        {
                            // Rebuild the one-char badge string only when the bound letter changes.
                            if (letter != _lastBadgeLetter[i]) { _lastBadgeLetter[i] = letter; _badgeStr[i] = letter.ToString(); }
                            badge = _badgeStr[i];
                        }
                    }
                    else if (entry.BindableBag != null)
                    {
                        int digit = global::StationeersUIMod.Features.BagHotkeyStore.DigitForBag(entry.BindableBag);
                        if (digit >= 0) badge = digit < BagDigitBadges.Length ? BagDigitBadges[digit] : "^" + digit; // ^ = Ctrl
                    }
                    bool showHk = badge != null;
                    hk.gameObject.SetActive(showHk);
                    if (showHk)
                    {
                        // #12: the badge (and its box) grow with the text size, centre stepping out
                        // in step so its inner edge keeps hugging the hub.
                        float hkFs = 15f * _tx;
                        if (!Mathf.Approximately(hk.fontSize, hkFs))
                        {
                            hk.fontSize = hkFs;
                            hk.rectTransform.sizeDelta = new Vector2(22f * _tx, 22f * _tx);
                        }
                        hk.rectTransform.anchoredPosition = dir * (innerR + 11f * _tx);
                        var hkc = RadialPalette.TextPrimary.Value; hkc.a *= (dimmed ? 0.5f : 1f);
                        hk.color = hkc;
                        if (hk.text != badge) hk.text = badge;
                    }

                    // 1B.3: grey tool-type binding label on a stable-geometry belt slot wedge. Sits
                    // just outside the hub so an empty-but-bound reserved slot still reads as "the
                    // <tool> goes here". Only when the entry carries one, stable geometry is on AND
                    // the player wants the ghost labels at all (ShowBindingLabels, the post-0.9.2.5 play-test round — it
                    // replaced the old curved/straight toggle; curved is the only rendering now).
                    // Cached WedgeText per slot (self-heals on ref change) — no per-frame alloc.
                    var bind = _binding[i];
                    bool showBinding = !entry.IsScrollAdjust && entry.BindingLabel != null
                        && UIAConfig.ToolbeltStableGeometry != null && UIAConfig.ToolbeltStableGeometry.Value
                        && (UIAConfig.RadialShowBindingLabels == null || UIAConfig.RadialShowBindingLabels.Value);
                    bind.gameObject.SetActive(showBinding);
                    if (showBinding)
                    {
                        if (!ReferenceEquals(entry.BindingLabel, _bindingSrc[i]))
                        {
                            _bindingSrc[i] = entry.BindingLabel;
                            _bindingDisplay[i] = WedgeText(entry.BindingLabel);
                        }
                        SyncFont(bind);
                        bind.fontStyle = WedgeFontStyle();
                        if (bind.text != _bindingDisplay[i]) bind.text = _bindingDisplay[i];
                        Color bc = RadialPalette.TextBinding.Value;   // its own colour, default grey
                        if (dimmed) bc.a *= 0.5f;
                        bind.color = bc;
                        // Sit the bound-tool name at the BOTTOM of the wedge — its inner end — hugging
                        // the arc where the HUB begins, and run it TANGENTIALLY along that arc rather
                        // than out along the spoke (FlorpyDorp's sketch). Two consequences worth
                        // knowing: the text follows the ring instead of pointing at the centre, and the
                        // whole middle of the wedge is freed for the icon + its live value.
                        float bH = 13f * _tx;                           // #12: box grows with the text size
                        SetMax(bind, 11.5f * _tx);
                        float bR = innerR + 4f + bH * 0.5f;            // just clear of the hub rim
                        float bTheta = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                        // Tangent to the hub circle; flipped on the LOWER half so a bottom wedge's
                        // name is never upside-down (top half reads with "up" pointing outward,
                        // bottom half with "up" pointing inward — the usual arc-label convention).
                        float bAng = dir.y >= 0f ? bTheta - 90f : bTheta + 90f;
                        // Bound by the wedge's CHORD at this radius, so a long tool name auto-sizes
                        // down (the pool enables autosizing 7..11.5) instead of running into the
                        // neighbouring wedge's border.
                        float bHalf = Mathf.Min(sector * 0.5f, Mathf.PI * 0.5f);
                        float bLen = Mathf.Clamp(2f * bR * Mathf.Sin(bHalf) - 8f, 12f, 220f);   // C: never a floor wider than the wedge
                        var brt = bind.rectTransform;
                        brt.pivot = new Vector2(0.5f, 0.5f);
                        brt.localRotation = Quaternion.Euler(0f, 0f, bAng);
                        brt.anchoredPosition = dir * bR;
                        brt.sizeDelta = new Vector2(bLen, bH);
                        bind.alignment = TextAlignmentOptions.Center;
                        bind.enableWordWrapping = false;
                        bind.overflowMode = TextOverflowModes.Ellipsis;
                        // Bend it GLYPH BY GLYPH around the hub so it truly follows the arc instead
                        // of being a straight line merely rotated to the tangent. The rect placement
                        // above already put it on the ring at the right angle; this only re-lays the
                        // glyphs inside that local space, so the two compose. Must run AFTER the
                        // text/size/rotation are final — it reads the built mesh. (Unconditional
                        // since the post-0.9.2.5 play-test round: curved was always the intended look, so the straight-line
                        // fallback retired with the BindingLabelCurved toggle.)
                        RadialArcText.Curve(bind, bR, dir.y >= 0f);
                    }

                    // D-023: a small PERSISTENT tag inside the wedge, hugging its OUTER rim ("RECENT
                    // ITEM") — the binding label's twin at the other end of the wedge, bent along the
                    // arc the same way. It rides the hover bulge so a popped-out icon never covers it,
                    // and steps inward past the swipe chevron when the wedge has one. Theme colour
                    // (TextDim), dimmed with the ring. Content, not a knob: the builder decides which
                    // wedge carries a tag (RadialEntry.CornerTag).
                    var tag = _tags[i];
                    bool showTag = !string.IsNullOrEmpty(entry.CornerTag) && !entry.IsScrollAdjust;
                    tag.gameObject.SetActive(showTag);
                    float tH = 12f * _tx; // #12: the rim tag's box grows with the text size
                    float tagInnerR = 0f; // B13: the tag's inner edge, for the icon clearance below
                    if (showTag)
                    {
                        if (!ReferenceEquals(entry.CornerTag, _tagSrc[i]))
                        {
                            _tagSrc[i] = entry.CornerTag;
                            _tagDisplay[i] = WedgeText(entry.CornerTag);
                        }
                        SyncFont(tag);
                        tag.fontStyle = WedgeFontStyle();
                        if (!Mathf.Approximately(tag.fontSizeMax, 10.5f * _tx))
                        {
                            tag.fontSizeMax = 10.5f * _tx;
                            _tagBend[i].Invalidate(); // a new ceiling re-sizes the glyphs: bend again
                        }
                        if (tag.text != _tagDisplay[i]) tag.text = _tagDisplay[i];
                        Color tc = RadialPalette.TextDim.Value;
                        tc.a *= dimmed ? 0.45f : 0.9f;
                        tag.color = tc;
                        float tR = outerR + wedge.OuterBulge - tH * 0.5f - 5f
                                 - (swipeable ? SwipeChevronSize + 2f : 0f);
                        tagInnerR = tR - tH * 0.5f;
                        float tTheta = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                        float tAng = dir.y >= 0f ? tTheta - 90f : tTheta + 90f; // upright on both halves
                        float tHalf = Mathf.Min(sector * 0.5f, Mathf.PI * 0.5f);
                        float tLen = Mathf.Clamp(2f * tR * Mathf.Sin(tHalf) - 14f, 12f, 220f);   // the wedge's chord (C: no floor wider than the wedge)
                        var trt = tag.rectTransform;
                        trt.pivot = new Vector2(0.5f, 0.5f);
                        trt.localRotation = Quaternion.Euler(0f, 0f, tAng);
                        trt.anchoredPosition = dir * tR;
                        var tSize = new Vector2(tLen, tH);
                        if ((trt.sizeDelta - tSize).sqrMagnitude > 0.01f)
                        {
                            trt.sizeDelta = tSize;
                            _tagBend[i].Invalidate(); // a resize dirties TMP's layout: bend it again
                        }
                        // B9: bent only when the text / size / radius / font actually changed (or TMP
                        // rebuilt it flat) — not a ForceMeshUpdate + autosize pass every frame.
                        _tagBend[i].CurveIfStale(tag, tR, dir.y >= 0f);
                    }

                    // Icons scale WITH the wedge: bounded by the band's thickness and by the
                    // wedge's width at mid radius, times the user's ratio.
                    float halfAngleIc = Mathf.Min(sector * 0.5f, Mathf.PI * 0.5f);
                    float chordIc = 2f * baseMidR * Mathf.Sin(halfAngleIc);
                    float iconRatio = UIAConfig.RadialIconRatio != null ? UIAConfig.RadialIconRatio.Value : 0.62f;
                    float iconSize = Mathf.Min(ringWidth, chordIc) * iconRatio;
                    float contentScale = isHovered ? HoverScale : 1f;
                    bool showLabels = UIAConfig.RadialShowWedgeLabels.Value;

                    if (entry.IsScrollAdjust)
                    {
                        RenderScrollWedge(i, entry, slot, dir, chordIc, ringWidth, isHovered, dimmed, wb);
                        continue;
                    }
                    _triUp[i].gameObject.SetActive(false);
                    _triDown[i].gameObject.SetActive(false);

                    var icon = _icons[i];
                    // STOW wedges show the slot; hovering previews the item that would go in.
                    Sprite sprite = isHovered && entry.HoverIcon != null ? entry.HoverIcon : entry.Icon;
                    icon.sprite = sprite;
                    icon.enabled = sprite != null;
                    float iconAlpha = (entry.Enabled ? 1f : 0.32f) * (dimmed ? 0.45f : 1f);
                    icon.color = new Color(1f, 1f, 1f, iconAlpha);
                    // With labels hidden the icon owns the wedge, so centre it in the band.
                    Vector2 iconPos = showLabels
                        ? slot + dir * (ringWidth * 0.05f) + new Vector2(0f, ringWidth * 0.12f)
                        : slot;
                    if (showTag)
                    {
                        // B13: a CornerTag hugs the outer rim — keep the icon's outer edge clear of it.
                        // Step the icon inward by just what it takes (up to TagIconInsetMax), then
                        // shrink it for anything left over, so tag and icon never collide. A square's
                        // reach along dir is half its side times (|dx| + |dy|). Default sizes with the
                        // item-name labels OFF already clear it (no change); labels ON lift the icon
                        // into the tag's band on a top wedge — which is where the recent item sits.
                        float reach = Mathf.Abs(dir.x) + Mathf.Abs(dir.y);
                        float over = Vector2.Dot(iconPos, dir) + iconSize * contentScale * 0.5f * reach
                                     - (tagInnerR - TagIconGap);
                        if (over > 0f)
                        {
                            float step = Mathf.Min(over, TagIconInsetMax);
                            iconPos -= dir * step;
                            over -= step;
                            if (over > 0f)
                                iconSize = Mathf.Max(iconSize * 0.6f, iconSize - 2f * over / (contentScale * reach));
                        }
                    }
                    icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize) * contentScale;
                    icon.rectTransform.anchoredPosition = iconPos;

                    // Live value (kPa / % / xN): horizontal, sitting just BELOW the icon's bottom
                    // edge (never on top of it), and auto-sized to the wedge's chord width AT THE
                    // VALUE'S OWN RADIUS so it can never reach a neighbouring wedge's border. No
                    // backing — white on the dark wedge reads fine.
                    var state = _states[i];
                    bool showState = UIAConfig.RadialShowStateText.Value && !string.IsNullOrEmpty(entry.StateText);
                    state.gameObject.SetActive(showState);
                    if (showState)
                    {
                        SyncFont(state);
                        state.fontStyle = WedgeFontStyle();
                        state.text = entry.StateText;
                        Color sc = RadialPalette.TextPrimary.Value;
                        if (dimmed) sc.a *= 0.5f;
                        state.color = sc;
                        state.rectTransform.localEulerAngles = Vector3.zero;
                        state.enableWordWrapping = false;
                        SetMax(state, 13f * _tx); // #12
                        // Clear of the icon (below its bottom edge, not overlapping it). The box is
                        // 14 tall (x the text size), its top edge 2 below the icon.
                        Vector2 sp = icon.rectTransform.anchoredPosition
                            - new Vector2(0f, iconSize * contentScale * 0.5f + 2f + 7f * _tx);
                        state.rectTransform.anchoredPosition = sp;
                        // Box = the wedge's chord at this value's radius, so auto-size shrinks the
                        // text to fit within the wedge (down to the pool's min font) — never wider
                        // than the wedge, never onto a neighbour.
                        float vChord = 2f * sp.magnitude * Mathf.Sin(halfAngleIc);
                        float vMax = Mathf.Clamp(vChord - 10f, 34f, 220f);
                        // C: and never wider than the wedge actually is around the value's box.
                        float vHalf = wb.FitHalfWidth(sp, 7f * _tx, vMax * 0.5f);
                        state.rectTransform.sizeDelta = new Vector2(Mathf.Max(10f, vHalf * 2f), 14f * _tx);
                    }

                    var label = _labels[i];
                    // The hub readout already names whatever is selected; per-wedge names just
                    // collide with neighbouring icons on a crowded belt.
                    bool labelOn = showLabels || (sprite == null && !string.IsNullOrEmpty(entry.Label));
                    label.gameObject.SetActive(labelOn);
                    if (!labelOn) continue;

                    // Cache WedgeText (ToUpperInvariant) per wedge-slot: recompute only when the
                    // source-label reference changes, then reuse it for the assignment and the
                    // length check below.
                    if (!ReferenceEquals(entry.Label, _labelSrc[i]))
                    {
                        _labelSrc[i] = entry.Label;
                        _labelDisplay[i] = WedgeText(entry.Label);
                    }
                    string labelDisplay = _labelDisplay[i];

                    SyncFont(label);
                    label.fontStyle = WedgeFontStyle();
                    // #12: a bigger ceiling; the box stays the wedge's own (chord x band), so a long
                    // name still auto-sizes down to fit the wedge rather than spilling over.
                    SetMax(label, 15f * _tx);
                    label.text = labelDisplay;
                    label.color = entry.Enabled ? RadialPalette.TextPrimary.Value : RadialPalette.TextDisabled.Value;
                    label.rectTransform.localScale = Vector3.one * contentScale;

                    float halfAngle = Mathf.Min(sector * 0.5f, Mathf.PI * 0.5f);
                    float chord = 2f * midR * Mathf.Sin(halfAngle);
                    float boxW = Mathf.Clamp(Mathf.Abs(Mathf.Cos(aMid)) * ringWidth + Mathf.Abs(Mathf.Sin(aMid)) * chord - 6f,
                                             42f, ringWidth * 2.5f);
                    label.rectTransform.localEulerAngles = Vector3.zero; // pooled label: clear prior rotation
                    label.enableWordWrapping = true;                    // ...and prior no-wrap
                    if (sprite == null)
                    {
                        // #5: a long control label ("STABILIZER OFF") overflows a narrow wedge when
                        // drawn flat. Angle it along the wedge's radial spoke (kept upright, one line)
                        // so it runs down the band's length instead of clipping out the sides. Gated
                        // on a genuinely long label and a not-very-wide wedge (few-wedge rings, where
                        // the label already fits, stay flat).
                        float radialLen = ringWidth + (isHovered ? HoverBulge : 0f);
                        bool rotate = (UIAConfig.RadialRotateLongLabels == null || UIAConfig.RadialRotateLongLabels.Value)
                            && labelDisplay.Length >= 10
                            && boxW < ringWidth * 1.5f;
                        if (rotate)
                        {
                            float angDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                            if (angDeg > 90f) angDeg -= 180f; else if (angDeg < -90f) angDeg += 180f;
                            label.rectTransform.localEulerAngles = new Vector3(0f, 0f, angDeg);
                            // C: along the spoke the box stops short of both rims, and its thickness is the
                            // wedge's width at the box's INNER end (the narrowest part it spans).
                            float len = Mathf.Min(radialLen * 1.05f, (wb.ROut - innerR) - 2f * wb.Pad);
                            float rNear = Mathf.Max(innerR + wb.Pad, midR - len * 0.5f);
                            float thick = lone ? ringWidth * 0.42f
                                : Mathf.Min(ringWidth * 0.42f, 2f * (rNear * Mathf.Sin(Mathf.Min(wb.HalfSpan, Mathf.PI * 0.5f)) - wb.Pad));
                            label.rectTransform.sizeDelta = new Vector2(Mathf.Max(10f, len), Mathf.Max(8f, thick)) / contentScale;
                            label.enableWordWrapping = false; // run as one line down the spoke
                        }
                        else
                        {
                            // C: the widest box that still fits the wedge around the label; if a tall box
                            // leaves only a sliver (a narrow wedge), trade height for width.
                            float hh = ringWidth * 0.25f;
                            float half = wb.FitHalfWidth(slot, hh, boxW * 0.5f);
                            if (half < 22f)
                            {
                                float hh2 = ringWidth * 0.16f;
                                float half2 = wb.FitHalfWidth(slot, hh2, boxW * 0.5f);
                                if (half2 > half) { hh = hh2; half = half2; }
                            }
                            label.rectTransform.sizeDelta = new Vector2(Mathf.Max(10f, half * 2f), hh * 2f) / contentScale;
                        }
                        label.rectTransform.anchoredPosition = slot;
                        label.alignment = TextAlignmentOptions.Center;
                    }
                    else
                    {
                        Vector2 lc = slot - dir * (ringWidth * 0.02f) -
                            new Vector2(0f, iconSize * contentScale * 0.48f + 1f);
                        // C: Top-aligned under the icon — keep the box's TOP edge where it always was,
                        // but only as tall as two lines need, and only as wide as the wedge is there.
                        float hh0 = ringWidth * 0.24f;
                        float top = lc.y + hh0;
                        float hh = Mathf.Min(hh0, 18f * _tx * contentScale);
                        lc.y = top - hh;
                        float half = wb.FitHalfWidth(lc, hh, boxW * 0.5f);
                        if (half < 22f)
                        {
                            float hh2 = Mathf.Min(hh, 9f * _tx * contentScale);   // one line
                            var lc2 = new Vector2(lc.x, top - hh2);
                            float half2 = wb.FitHalfWidth(lc2, hh2, boxW * 0.5f);
                            if (half2 > half) { hh = hh2; half = half2; lc = lc2; }
                        }
                        label.rectTransform.sizeDelta = new Vector2(Mathf.Max(10f, half * 2f), hh * 2f) / contentScale;
                        label.rectTransform.anchoredPosition = lc;
                        label.alignment = TextAlignmentOptions.Top;
                    }
                }
            }

            /// <summary>A scroll-adjustable value wedge: NAME on top, then an up triangle,
            /// the live value, and a down triangle — nudged by the mouse wheel.</summary>
            private void RenderScrollWedge(int i, RadialEntry entry, Vector2 slot, Vector2 dir,
                float chordIc, float ringWidth, bool isHovered, bool dimmed, WedgeBounds wb)
            {
                _icons[i].enabled = false;
                var accent = isHovered ? RadialPalette.WedgeBorderHover.Value : RadialPalette.WedgeBorder.Value;
                if (dimmed) accent.a *= 0.5f;

                // Horizontal NAME / value / ▲▼ stack, auto-sized to the wedge width so a long name
                // ("THRUST") shrinks to fit instead of clipping (kept readable, never rotated).
                float sw = Mathf.Clamp(chordIc - 6f, 48f, 220f);
                // #12: the whole NAME / up / value / down stack scales with the text size, capped so
                // the ~56-unit-tall stack still fits three quarters of the band (never below 1x).
                float ts = _tx <= 1f ? _tx : Mathf.Min(_tx, Mathf.Max(1f, ringWidth * 0.75f / 56f));

                var label = _labels[i];
                label.gameObject.SetActive(true);
                SyncFont(label);
                label.fontStyle = WedgeFontStyle();
                label.text = WedgeText(entry.Label);
                label.color = entry.Enabled ? RadialPalette.TextPrimary.Value : RadialPalette.TextDisabled.Value;
                label.alignment = TextAlignmentOptions.Center;
                label.enableWordWrapping = false;
                label.rectTransform.localScale = Vector3.one;
                label.rectTransform.localEulerAngles = Vector3.zero;
                SetMax(label, 15f * ts);
                // C: the chord is the wedge's width at its MIDDLE radius along the spoke; a flat box
                // offset above the slot on a side wedge is narrower than that ("SPLIT COUNT" ran past
                // the border at 1440p). Fit it to the wedge where it actually sits; auto-size shrinks.
                Vector2 lp = slot + new Vector2(0f, 26f * ts);
                float lHalf = wb.FitHalfWidth(lp, 8f * ts, sw * 0.5f);
                label.rectTransform.sizeDelta = new Vector2(Mathf.Max(10f, lHalf * 2f), 16f * ts);
                label.rectTransform.anchoredPosition = lp;

                var up = _triUp[i];
                up.gameObject.SetActive(true);
                up.Configure(pointsUp: true, size: 11f * ts);
                up.color = accent;
                up.rectTransform.localEulerAngles = Vector3.zero;
                up.rectTransform.anchoredPosition = slot + new Vector2(0f, 11f * ts);
                up.SetVerticesDirty();

                var state = _states[i];
                state.gameObject.SetActive(true);
                SyncFont(state);
                state.fontStyle = WedgeFontStyle();
                string v = null;
                try { v = entry.ValueText?.Invoke(); } catch { }
                state.text = v ?? entry.StateText ?? "";
                Color sc = RadialPalette.TextPrimary.Value;
                if (dimmed) sc.a *= 0.5f;
                state.color = sc;
                state.rectTransform.localEulerAngles = Vector3.zero;
                SetMax(state, 13f * ts);
                Vector2 vp = slot - new Vector2(0f, 2f * ts);
                float vHalf = wb.FitHalfWidth(vp, 8f * ts, sw * 0.5f);   // C: same fit for the value ("10")
                state.rectTransform.sizeDelta = new Vector2(Mathf.Max(10f, vHalf * 2f), 16f * ts);
                state.rectTransform.anchoredPosition = vp;

                var down = _triDown[i];
                down.gameObject.SetActive(true);
                down.Configure(pointsUp: false, size: 11f * ts);
                down.color = accent;
                down.rectTransform.localEulerAngles = Vector3.zero;
                down.rectTransform.anchoredPosition = slot - new Vector2(0f, 15f * ts);
                down.SetVerticesDirty();
            }

            private static Color ResolveUguiFill(RadialEntry entry, bool hovered, bool dimmed,
                DynamicThing dragging)
            {
                Color col;
                if (!entry.Enabled)
                    col = RadialPalette.WedgeDisabled.Value;
                else if (dragging != null && hovered)
                    // Dragging an item: the hovered wedge turns orange when it would accept
                    // the drop, and reads disabled when it wouldn't.
                    col = entry.AcceptsDrop && entry.ResolveDrop(dragging) != null
                        ? RadialPalette.WedgeStowHover.Value
                        : RadialPalette.WedgeDisabled.Value;
                else if (entry.StowStyle)
                    // Option A STOW wedge: quiet until hovered, then the orange "goes here".
                    col = hovered ? RadialPalette.WedgeStowHover.Value : RadialPalette.WedgeBg.Value;
                else if (entry.GroupStyle)
                    // Sorting-class GROUP wedge: its own fill (defaults to the normal fill),
                    // normal hover fill on hover. The blue edge (below) is what marks it.
                    col = hovered ? RadialPalette.WedgeHover.Value : RadialPalette.GroupWedgeFill.Value;
                else if (entry.FillOverride.HasValue)
                    col = hovered ? RadialPalette.WedgeStowHover.Value : RadialPalette.WedgeStow.Value;
                else if (hovered)
                    col = RadialPalette.WedgeHover.Value;
                else
                    col = RadialPalette.WedgeBg.Value;

                if (dimmed) col.a *= 0.48f;
                return col;
            }

            /// <summary>The always-on outline. Hover shifts it to the hover border colour.
            /// Sorting-class GROUP wedges keep their distinct edge colour in every state, so a
            /// category wedge reads as one at a glance (hover feedback comes from the bulge/shine).</summary>
            private static Color ResolveUguiBorder(RadialEntry entry, bool hovered, bool dimmed)
            {
                Color border = !entry.Enabled
                    ? RadialPalette.WedgeDisabled.Value
                    : entry.GroupStyle ? RadialPalette.GroupWedgeBorder.Value
                    : entry.DeviceSlotStyle && !hovered ? RadialPalette.DeviceSlotBorderColor.Value
                    : hovered ? RadialPalette.WedgeBorderHover.Value
                              : RadialPalette.WedgeBorder.Value;
                if (dimmed) border.a *= 0.55f;
                return border;
            }

            private void EnsureCapacity(int n)
            {
                while (_wedges.Count < n)
                {
                    int idx = _wedges.Count;

                    var wgo = new GameObject("Wedge" + idx, typeof(RectTransform));
                    wgo.transform.SetParent(_root, false);
                    var wedge = wgo.AddComponent<RadialWedgeGraphic>();
                    wedge.raycastTarget = false;
                    wedge.BorderWidth = 0f;
                    wedge.RimHighlight = 0f;
                    _wedges.Add(wedge);

                    var igo = new GameObject("Icon" + idx, typeof(RectTransform));
                    igo.transform.SetParent(_root, false);
                    var img = igo.AddComponent<Image>();
                    img.raycastTarget = false;
                    img.preserveAspect = true;   // aspect handled by UGUI: icons can't warp
                    _icons.Add(img);

                    var tgo = new GameObject("Label" + idx, typeof(RectTransform));
                    tgo.transform.SetParent(_root, false);
                    var tmp = tgo.AddComponent<TextMeshProUGUI>();
                    tmp.font = Font();
                    tmp.alignment = TextAlignmentOptions.Top;
                    tmp.enableAutoSizing = true;
                    tmp.fontSizeMin = 7f;   // C: a fitted wedge box may need a notch smaller
                    tmp.fontSizeMax = 15f;
                    tmp.enableWordWrapping = true;
                    tmp.overflowMode = TextOverflowModes.Truncate;
                    tmp.raycastTarget = false;
                    _labels.Add(tmp);

                    var sgo = new GameObject("State" + idx, typeof(RectTransform));
                    sgo.transform.SetParent(_root, false);
                    var stmp = sgo.AddComponent<TextMeshProUGUI>();
                    stmp.font = Font();
                    stmp.alignment = TextAlignmentOptions.Center;
                    stmp.enableAutoSizing = true;
                    stmp.fontSizeMin = 7f;   // C: likewise for the value under the icon
                    stmp.fontSizeMax = 13f;
                    stmp.enableWordWrapping = false;
                    stmp.overflowMode = TextOverflowModes.Overflow;
                    stmp.raycastTarget = false;
                    _states.Add(stmp);

                    var ugo = new GameObject("TriUp" + idx, typeof(RectTransform));
                    ugo.transform.SetParent(_root, false);
                    var triU = ugo.AddComponent<TriangleGraphic>();
                    triU.raycastTarget = false;
                    triU.rectTransform.sizeDelta = new Vector2(14f, 12f);
                    _triUp.Add(triU);

                    var dgo = new GameObject("TriDown" + idx, typeof(RectTransform));
                    dgo.transform.SetParent(_root, false);
                    var triD = dgo.AddComponent<TriangleGraphic>();
                    triD.raycastTarget = false;
                    triD.rectTransform.sizeDelta = new Vector2(14f, 12f);
                    _triDown.Add(triD);

                    var swgo = new GameObject("Swipe" + idx, typeof(RectTransform));
                    swgo.transform.SetParent(_root, false);
                    var triW = swgo.AddComponent<TriangleGraphic>();
                    triW.raycastTarget = false;
                    triW.rectTransform.sizeDelta = new Vector2(SwipeChevronSize + 6f, SwipeChevronSize); // drawn size is Configure()'s; this is just its box
                    _swipe.Add(triW);

                    var hkgo = new GameObject("Hotkey" + idx, typeof(RectTransform));
                    hkgo.transform.SetParent(_root, false);
                    var hktmp = hkgo.AddComponent<TextMeshProUGUI>();
                    hktmp.font = Font();
                    hktmp.alignment = TextAlignmentOptions.Center;
                    hktmp.enableAutoSizing = false;
                    hktmp.fontSize = 15f;
                    hktmp.fontStyle = FontStyles.Bold;
                    hktmp.enableWordWrapping = false;
                    hktmp.overflowMode = TextOverflowModes.Overflow;
                    hktmp.raycastTarget = false;
                    hktmp.rectTransform.sizeDelta = new Vector2(22f, 22f);
                    _hotkey.Add(hktmp);

                    var bngo = new GameObject("Binding" + idx, typeof(RectTransform));
                    bngo.transform.SetParent(_root, false);
                    var bntmp = bngo.AddComponent<TextMeshProUGUI>();
                    bntmp.font = Font();
                    bntmp.alignment = TextAlignmentOptions.Center;
                    bntmp.enableAutoSizing = true;
                    bntmp.fontSizeMin = 7f;
                    bntmp.fontSizeMax = 11.5f;
                    bntmp.enableWordWrapping = false;
                    bntmp.overflowMode = TextOverflowModes.Ellipsis;
                    bntmp.raycastTarget = false;
                    _binding.Add(bntmp);

                    var tggo = new GameObject("Tag" + idx, typeof(RectTransform));
                    tggo.transform.SetParent(_root, false);
                    var tgtmp = tggo.AddComponent<TextMeshProUGUI>();
                    tgtmp.font = Font();
                    tgtmp.alignment = TextAlignmentOptions.Center;
                    tgtmp.enableAutoSizing = true;
                    tgtmp.fontSizeMin = 7f;
                    tgtmp.fontSizeMax = 10.5f;
                    tgtmp.enableWordWrapping = false;
                    // B10: never "..." — the game font has no ellipsis glyph, it tofus (see the note at
                    // GridRegionView's profile chip). A too-long tag just clips.
                    tgtmp.overflowMode = TextOverflowModes.Truncate;
                    tgtmp.raycastTarget = false;
                    tggo.SetActive(false);
                    _tags.Add(tgtmp);

                    // Parallel per-wedge string caches (see field declarations). The cased caches start
                    // STALE (B10), so a fresh slot always computes its display string.
                    _labelSrc.Add(StaleSrc);
                    _labelDisplay.Add(null);
                    _lastBadgeLetter.Add('\0');
                    _badgeStr.Add(null);
                    _bindingSrc.Add(StaleSrc);
                    _bindingDisplay.Add(null);
                    _tagSrc.Add(StaleSrc);
                    _tagDisplay.Add(null);
                    _tagBend.Add(new ArcBendCache());
                }
            }
        }

        // ---------- center readout ----------

        private sealed class ReadoutView
        {
            /// <summary>D-017 (FlorpyDorp: "jump suit should be bold and a bit larger ... so its clear
            /// what item it is"): the NAME line (line 3) is set bold and this much larger than its
            /// configured size (F10 "Readout 3: item name", RadialTextLabel). Deliberate SHARED content
            /// typography, not a new knob: the readout's hierarchy (context / verb / NAME / detail /
            /// stat) is part of its design and scales with the one size knob that already travels with
            /// the profile theme. Not per-tier: the radial is not a tiered HUD element.</summary>
            private const float NameEmphasis = 1.18f;

            private readonly RectTransform _root;
            private readonly CircleGraphic _hubBacking;
            private readonly TextMeshProUGUI _title, _verb, _label, _sub, _warn;

            public ReadoutView(Transform parent, TMP_FontAsset font)
            {
                var go = new GameObject("Readout", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                _root = (RectTransform)go.transform;
                _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);

                // A CircleGraphic, not an Image: an Image with no sprite renders a white QUAD,
                // which is why the hub used to be a square.
                var hub = new GameObject("HubBacking", typeof(RectTransform));
                hub.transform.SetParent(_root, false);
                _hubBacking = hub.AddComponent<CircleGraphic>();
                _hubBacking.raycastTarget = false;

                _title = Make(font, 13f, out var t0); t0.SetParent(_root, false);
                _verb = Make(font, 15f, out var t1); t1.SetParent(_root, false);
                _label = Make(font, 15f, out var t2); t2.SetParent(_root, false);
                _sub = Make(font, 12f, out var t3); t3.SetParent(_root, false);
                _warn = Make(font, 12f, out var t4); t4.SetParent(_root, false);
            }

            private static TextMeshProUGUI Make(TMP_FontAsset font, float size, out RectTransform rt)
            {
                var go = new GameObject("Line", typeof(RectTransform));
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.font = font;
                tmp.fontSize = size;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableWordWrapping = false;
                tmp.raycastTarget = false;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 8f;
                tmp.fontSizeMax = size;
                rt = (RectTransform)go.transform;
                return tmp;
            }

            public void Hide() => _root.gameObject.SetActive(false);

            public void Render(Vector2 center, float innerR, string title, string satTitle,
                RadialEntry hovered, string hint, bool sticky)
            {
                _root.gameObject.SetActive(true);
                _root.anchoredPosition = Pos(center);
                float w = (innerR - 6f) * 1.85f;
                // The readout also serves small satellite hubs (Option A): squeeze the
                // line spacing down with the radius so five lines still fit the circle.
                float s = Mathf.Clamp(innerR / 110f, 0.45f, 1f);

                SyncFont(_title); SyncFont(_verb); SyncFont(_label); SyncFont(_sub); SyncFont(_warn);

                // #5: each of the five readout lines is sized separately (line 1 bold), applied to
                // BOTH the main hub and the child (satellite) readout. When dynamic-text is on, the
                // sizes scale with the hub (s) so a small child hub never overlaps its lines while
                // the big main hub stays full-size; off = fixed sizes everywhere.
                float textScale = (UIAConfig.RadialDynamicReadoutText == null || UIAConfig.RadialDynamicReadoutText.Value) ? s : 1f;
                float lSz0 = (UIAConfig.RadialTextLabel != null ? UIAConfig.RadialTextLabel.Value : 15f) * textScale * NameEmphasis;

                // Each line's box is clamped to the hub CIRCLE's chord at its own height (minus the
                // border + a small margin), so no line — however wide — can spill onto the orange
                // ring; text stays entirely inside the blue interior and auto-sizes to fit.
                float rIn = Mathf.Max(8f, (innerR - 6f) - (UIAConfig.RadialBorderWidth.Value * 0.8f + 7f));

                // #12: the player's text size scales the five lines AND their spacing together (so
                // they never overlap), but only as far as the stack still fits the hub: its half
                // height at 1x is the bottom line's offset + the name's extra height + half a line;
                // the stack may fill 85% of the hub radius. Past that the text tops out (a bigger
                // Hub size makes room). Never below 1x on the way up; a smaller size always applies.
                float h0 = 43f * s + (Mathf.Max(LineH, lSz0 * 1.32f) - LineH) * 0.5f + LineH * 0.5f;
                float tr = _tx <= 1f ? _tx : Mathf.Min(_tx, Mathf.Max(1f, rIn * 0.85f / Mathf.Max(1f, h0)));
                textScale *= tr;
                float lineH = LineH * tr;
                float sp = s * tr;   // line spacing scale

                float tSz = (UIAConfig.RadialHubTitleSize != null ? UIAConfig.RadialHubTitleSize.Value : 18f) * textScale;
                float vSz = (UIAConfig.RadialTextVerb != null ? UIAConfig.RadialTextVerb.Value : 15f) * textScale;
                float lSz = lSz0 * tr;
                float sSz = (UIAConfig.RadialTextSub != null ? UIAConfig.RadialTextSub.Value : 12f) * textScale;
                float wSz = (UIAConfig.RadialTextWarn != null ? UIAConfig.RadialTextWarn.Value : 12f) * textScale;
                _title.fontSizeMax = tSz; _title.fontSizeMin = Mathf.Min(8f, tSz); _title.fontStyle = WedgeFontStyle();
                _verb.fontSizeMax = vSz; _verb.fontSizeMin = Mathf.Min(8f, vSz);
                // D-017: the name is ALWAYS bold (faux-bold on top of a bold face too) so it is the
                // heaviest line in the hub whatever font the theme picks.
                _label.fontSizeMax = lSz; _label.fontSizeMin = Mathf.Min(8f, lSz); _label.fontStyle = FontStyles.Bold;
                _sub.fontSizeMax = sSz; _sub.fontSizeMin = Mathf.Min(8f, sSz);
                _warn.fontSizeMax = wSz; _warn.fontSizeMin = Mathf.Min(8f, wSz);

                if (_hubBacking != null)
                {
                    _hubBacking.rectTransform.anchoredPosition = Vector2.zero;
                    _hubBacking.SetRadius(innerR - 6f);
                    _hubBacking.color = RadialPalette.HubFill.Value;
                    _hubBacking.BorderColor = RadialPalette.HubBorder.Value;
                    _hubBacking.BorderWidth = UIAConfig.RadialBorderWidth.Value * 0.8f;
                    _hubBacking.Refresh();
                }

                // D-017: the bigger NAME needs a taller box, or TMP's autosize (which also fits the box
                // HEIGHT) would shrink it straight back. The extra height is split evenly: the lines
                // above move up and the lines below move down by half each, the name stays centred.
                float nameH = Mathf.Max(lineH, lSz * 1.32f);
                float grow = (nameH - lineH) * 0.5f;
                Place(_title, 38f * sp + grow, Mathf.Min(w, FitWidth(38f * sp + grow, rIn, lineH)), lineH);
                Place(_verb, 13f * sp + grow, Mathf.Min(w, FitWidth(13f * sp + grow, rIn, lineH)), lineH);
                Place(_label, -7f * sp, Mathf.Min(w, FitWidth(-7f * sp, rIn, nameH)), nameH);
                Place(_sub, -25f * sp - grow, Mathf.Min(w, FitWidth(-25f * sp - grow, rIn, lineH)), lineH);
                Place(_warn, -43f * sp - grow, Mathf.Min(w, FitWidth(-43f * sp - grow, rIn, lineH)), lineH);

                _title.text = satTitle ?? title ?? string.Empty;
                _title.color = RadialPalette.TextDim.Value;

                if (hovered == null)
                {
                    // D-021: no "LMB select" here any more — "select" was wrong (a click takes, opens,
                    // swaps...), and the curved word over the ring names the real action on hover.
                    _verb.text = hint ?? (sticky
                        ? "click a wedge"
                        : "rest to open | let go to close");
                    _verb.color = RadialPalette.TextDim.Value;
                    _label.text = _sub.text = _warn.text = string.Empty;
                    return;
                }

                // The verb line must agree with the curved action word (D-021/D-022): a take that will
                // actually SWAP the busy active hand says so, in the toolbelt's own wording. An entry
                // with no ActionText borrows the derived verb (never the old catch-all "Select").
                string clickVerb = hovered.ClickVerb();
                string verbLine = hovered.ActionText;
                if (verbLine != null && hovered.IsTakePhrase
                    && string.Equals(clickVerb, "Swap", StringComparison.Ordinal))
                    verbLine = "Swap into hand";
                if (string.IsNullOrEmpty(verbLine))
                    verbLine = clickVerb ?? (hovered.IsBranch ? "Open" : string.Empty);
                _verb.text = verbLine;
                _verb.color = hovered.Enabled ? RadialPalette.TextAccent.Value : RadialPalette.TextDim.Value;
                // Action wedges often ARE their verb ("Replace", "Stabilizer Off") — don't
                // print the same word twice in the readout.
                _label.text = string.Equals(hovered.Label, _verb.text, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty : hovered.Label ?? string.Empty;
                _label.color = hovered.Enabled ? RadialPalette.TextPrimary.Value : RadialPalette.TextDim.Value;
                _sub.text = hovered.Sublabel ?? string.Empty;
                _sub.color = RadialPalette.TextDim.Value;

                if (!hovered.Enabled && !string.IsNullOrEmpty(hovered.DisabledReason))
                {
                    _warn.text = hovered.DisabledReason;
                    _warn.color = FromImGui(Theme.Critical);
                }
                else if (!string.IsNullOrEmpty(hovered.Warning))
                {
                    _warn.text = hovered.Warning;
                    _warn.color = FromImGui(Theme.Warn);
                }
                else
                {
                    // Bug 6: the hovered item's live stat (canister kPa, battery %, stack xN,
                    // filter %, device value) in the center readout, so EVERY radial surfaces
                    // it the same way — not just the toolbelt radial (which baked it into its
                    // sublabel). StateText carries TMP tags; the readout is TMP, so they render.
                    string stat = null;
                    try { stat = hovered.ValueText != null ? hovered.ValueText() : hovered.StateText; }
                    catch { }
                    _warn.text = stat ?? string.Empty;
                    _warn.color = RadialPalette.TextAccent.Value;
                }
            }

            /// <summary>The standard readout line box height (px).</summary>
            private const float LineH = 20f;

            private static void Place(TextMeshProUGUI t, float y, float w, float h)
            {
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                t.rectTransform.sizeDelta = new Vector2(w, h);
                t.rectTransform.anchoredPosition = new Vector2(0f, y);
            }

            /// <summary>The full chord width of a circle of radius <paramref name="rIn"/> at height
            /// <paramref name="y"/>, accounting for the line box's half-height, so a line placed there
            /// fits entirely inside the circle. Zero when the height is already past the circle.</summary>
            private static float FitWidth(float y, float rIn, float boxH)
            {
                float dy = Mathf.Abs(y) + boxH * 0.5f; // the box edge farther from the centre constrains
                float inside = rIn * rIn - dy * dy;
                return inside <= 0f ? 0f : 2f * Mathf.Sqrt(inside);
            }
        }
    }
}
