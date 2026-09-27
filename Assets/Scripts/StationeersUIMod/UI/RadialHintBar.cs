using System.Collections.Generic;
using System.Text;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.Overlay;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI
{
    /// <summary>
    /// The contextual key-hint strip of an open radial — rebuilt for D-021 (2026-09-25; FlorpyDorp:
    /// "come up with words that make more sense for what they actually are ... default better to the
    /// theme colors ... wrapped around the bottom of the radial in a curve").
    ///
    /// <para>WORDS. Each hint names what the input actually DOES right now, derived from the real
    /// handlers in <see cref="RadialMenu"/> via the per-frame <see cref="RadialHintContext"/>: RMB backs
    /// out of a nested level (and, in hold mode, a child ring; out of the belt picker it returns to the
    /// belt ring) and closes at the root — saying so when that drops the parked items, D-004; a
    /// press-drag moves an item; Shift inverts close-after-action per <c>ShiftKeepsRadialOpen</c>; the
    /// page key pages (or changes belt on the toolbelt ring); a drag in flight says where a release
    /// lands; hold mode says what letting go does; search mode speaks its own keys. The old
    /// "LMB select" is gone: a click takes, opens, swaps, stows… per wedge, and the curved ACTION WORD
    /// over the ring (D-022, <see cref="UnityRadialView"/>) names exactly that, so the strip never
    /// duplicates it. "What can I do here" hints are level-wide, so the strip stays still while the
    /// pointer sweeps the ring. Every string is ASCII (the game's TMP font).</para>
    ///
    /// <para>LOOK. Wrapped along an arc hugging the wheel's bottom OUTSIDE edge, on a glass plate cut
    /// from the same annular-sector mesh as the wedges (<see cref="ArcPlateLabel"/>): plate, rim, words
    /// and key names come from RadialPalette ArcPlateFill / ArcPlateBorder / ArcText / ArcAccent, all
    /// AUTO = derived from the active theme's wedge colours, so they travel with the profile theme and
    /// match every shipped theme without a re-stamp. Geometry reuses the existing hint-bar knobs (gap =
    /// DropBelowWheel, thickness = Height, FontSize, Bold, SidePadding, frost / sheen / edge light), so
    /// no new theme key is needed. The text reads upright (coin convention). FAIL-SOFT: when the text
    /// cannot fit a legible arc (a small wheel, a long list) the lowest-priority hints are trimmed, and
    /// if even the first cannot fit, the classic FLAT strip (its own authored HintBar* look) is used.
    /// A child ring that swings over the strip fades it out and back.</para>
    ///
    /// <para>Progressive fade (R10): each hint KIND carries a per-save exposure count in
    /// <see cref="HintUsageStore"/>; once a kind passes <see cref="FadeThreshold"/> uses it is dropped so
    /// the hints teach then get out of the way. Gated by <c>UIAConfig.RadialHintFade</c>; counters reset
    /// from the F10 menu (<see cref="HintUsageStore.ResetCounters"/>). Own screen-space overlay canvas,
    /// hot-reload safe (<see cref="Shutdown"/>).</para>
    /// </summary>
    public static class RadialHintBar
    {
        // ---- the flat strip (fallback + the F10 look preview) ----
        private static GameObject _root;
        private static Canvas _canvas;
        private static CanvasGroup _rootGroup;   // fades the whole strip while a child ring covers it
        private static RectTransform _panel;
        // The mod's own rounded/glass renderer rather than a square UGUI Image, so the strip gets
        // curved corners, a proper rim and the same glass treatment as the rest of the UI.
        private static Hud.PanelGraphic _bg;
        private static TextMeshProUGUI _text;
        /// <summary>Editor aid only: a plain WHITE swatch drawn BEHIND the flat strip while previewing,
        /// so a translucent black panel can actually be judged (against the sky it reads as a
        /// different colour entirely).</summary>
        private static Image _preview;
        private static float _styleHash = float.NaN;
        private static string _last;

        // ---- the curved strip (D-021) ----
        private static ArcPlateLabel _arc;

        /// <summary>Fallback height when config is unbound; the live value is <c>HintBarHeight</c>.</summary>
        private const float BarH = 30f;
        /// <summary>How far either side of straight-down the hint TEXT may run. Past ~55 degrees the end
        /// glyphs lean too far to read comfortably (Hopkins' pie-menu guidance: horizontal labels read
        /// best, wide ones belong near the top/bottom) — longer lists trim, then fall back to flat.</summary>
        private const float MaxArcHalfDeg = 55f;
        /// <summary>Below this text-midline radius the arc is too tight for small text: flat instead.</summary>
        private const float MinArcRadius = 120f;
        private const float ArcFadeSpeed = 10f;   // alpha/s — the arc eases in/out rather than popping
        private const float CoverFadeSpeed = 8f;  // alpha/s — yielding to a child ring

        // ---- progressive fade (R10) ----
        /// <summary>Exposures of a hint kind before it stops being drawn.</summary>
        private const int FadeThreshold = 25;

        // Hint-kind storage keys: they key the persisted counters, so they must stay STABLE. (The
        // retired "select" kind — the old "LMB select" — keeps its counter in the store, unused, and
        // so does the retired "worldplace" — the old Alt-mode "release over a wedge to place".)
        // B11: a hint whose MEANING changed gets a NEW key, so a veteran who long since faded the old
        // one (past FadeThreshold) is still taught the new meaning once.
        private const string K_Back = "back";            // RMB back / close — what "RMB back" always taught
        private const string K_Reach = "reach";
        private const string K_Swap = "swaphand";
        private const string K_Page = "page";
        private const string K_Grab = "worldgrab";
        // D-021 additions
        private const string K_Cancel = "cancel";
        private const string K_Drag = "drag";
        private const string K_Wheel = "wheel";
        private const string K_Shift = "shift";
        private const string K_Belt = "belt";
        private const string K_Release = "release";
        private const string K_Tap = "tapopen";
        private const string K_SearchTake = "searchtake";
        private const string K_SearchBack = "searchback";
        // B11: changed meanings, fresh counters
        private const string K_DropParked = "dropparked";  // RMB now DROPS the parked items (D-004)
        private const string K_PlaceOrPark = "placeorpark"; // a drag's release: place into a slot or park
        private static readonly string[] AllKinds =
        {
            K_Back, K_Reach, K_Swap, K_Page, K_Grab, K_Cancel, K_Drag, K_Wheel, K_Shift,
            K_Belt, K_Release, K_Tap, K_SearchTake, K_SearchBack, K_DropParked, K_PlaceOrPark,
        };

        private static bool _sessionActive;
        private static readonly HashSet<string> _faded = new HashSet<string>();       // kinds past threshold this session
        private static readonly HashSet<string> _sessionShown = new HashSet<string>(); // kinds actually drawn this session
        private static int _fadeVer;                                                  // bumps when _faded is recomputed

        // ---- hint list + composition caches (rebuilt only when an input changes: no steady-state alloc) ----
        private struct Hint
        {
            public readonly string Kind;    // fade-counter key (null = preview sample, never counted)
            public readonly string Key;     // the input, drawn in the accent colour ("RMB", "E", "Drag")
            public readonly string Action;  // what it does right now ("back", "switch hand")
            public Hint(string kind, string key, string action) { Kind = kind; Key = key; Action = action; }
        }
        private static readonly List<Hint> _hints = new List<Hint>(10);   // priority order: first = most important
        private static readonly StringBuilder _sb = new StringBuilder(256);

        private static int _cFlags = -1;
        private static KeyCode _cSwapKey = KeyCode.None, _cPageKey = KeyCode.None, _cReachKey = KeyCode.None;
        private static int _cFadeVer = -1;
        private static Color32 _cAccent, _cArcText, _cFlatText;
        private static int _hintsVer;          // bumps whenever _hints / the colours change

        private static string _flatText;       // every hint, flat-strip colours
        private static int _flatVer = -1;

        private static string _arcText;        // the hints that fit the arc
        private static float _arcLen;          // its straight (= arc) length
        private static int _arcCount;          // how many hints made it onto the arc (0 = arc unusable)
        private static int _arcFitVer = -1;
        private static float _arcFitRadius = -1f, _arcFitSize = -1f;
        private static bool _arcFitBold;
        private static TMP_FontAsset _arcFitFont;
        private static float _arcRetryAt;      // throttle for a failed (zero-width) measure

        /// <summary>Pumped each frame from the mod's Update with whether a radial is currently open.</summary>
        public static void Tick(bool radialOpen)
        {
            bool fadeOn = UIAConfig.RadialHintFade != null && UIAConfig.RadialHintFade.Value;
            UpdateSession(radialOpen, fadeOn);

            // Preview pins a sample strip on screen even with no wheel open, so its look can be authored.
            // ON BY DEFAULT inside the RADIAL EDITOR (the editor blacks the screen out; the flat strip is
            // pinned over a white swatch there, and the curved one wraps the editor's example wheel).
            // Outside the editor it is opt-in via the config toggle. Ignores the show/fade gates.
            bool preview = Windows.RadialEditorMode.Active
                || (UIAConfig.HintBarPreview != null && UIAConfig.HintBarPreview.Value);
            bool live = radialOpen && UIAConfig.RadialHintBar != null && UIAConfig.RadialHintBar.Value
                && RadialHintContext.InteractionFresh;
            if (!preview && !live)
            {
                HideAll();
                return;
            }

            RefreshHints(live, fadeOn);
            if (_hints.Count == 0)
            {
                // every hint for this context has faded away — nothing left to teach.
                HideAll();
                return;
            }

            EnsureBuilt();
            if (_canvas == null) return;
            if (!_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(true);

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool ringKnown = RadialHintContext.GeometryFresh;

            // Curved under the ring whenever it fits; the live strip falls back to flat otherwise. The
            // preview ALSO shows the flat strip (its own authored look, over the white swatch) so both
            // presentations can be judged side by side in the radial editor.
            bool arcShown = ringKnown && LayoutArc(dt);
            if (!arcShown && _arc != null) _arc.StepFade(false, dt, ArcFadeSpeed);
            bool flatShown = !live || !arcShown;
            if (flatShown) LayoutFlat(live, ringKnown);
            else
            {
                if (_panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
                if (_preview != null && _preview.enabled) _preview.enabled = false; // never strand the editor swatch
            }

            // Session exposure = the kinds actually DRAWN (a trimmed arc hint was not taught).
            if (live)
            {
                int drawn = arcShown ? _arcCount : _hints.Count;
                for (int i = 0; i < drawn && i < _hints.Count; i++)
                    if (_hints[i].Kind != null) _sessionShown.Add(_hints[i].Kind);
            }

            // Yield to a child ring that swings over the strip (a satellite opened off a bottom wedge):
            // fade out neatly instead of peeking around it, and back when it closes.
            float target = live && CoveredByChildRing(arcShown) ? 0f : 1f;
            _rootGroup.alpha = Mathf.MoveTowards(_rootGroup.alpha, target, dt * CoverFadeSpeed);
        }

        // ---- hints: what each input does, from the live context ----

        /// <summary>Rebuild the hint list when (and only when) something it depends on changed.
        /// <c>internal</c> (not private) only so the uiatest harness (Testing/UiaTestHarness.cs) can
        /// assert the composed wording for a given RadialHintContext.</summary>
        internal static void RefreshHints(bool live, bool fadeOn)
        {
            KeyCode swapK = UiaKeybinds.Key("UIA_HandSwap");
            KeyCode pageK = UiaKeybinds.Key("UIA_Page");
            KeyCode reachK = KeyCode.None;
            try { reachK = KeyMap.MouseControl; } catch { }
            int flags = ContextFlags(live);
            Color32 accent = RadialPalette.ArcAccent.Value;
            Color32 arcText = RadialPalette.ArcText.Value;
            Color32 flatText = RadialPalette.HintBarText.Value;

            if (flags == _cFlags && swapK == _cSwapKey && pageK == _cPageKey && reachK == _cReachKey
                && _cFadeVer == _fadeVer && Same(accent, _cAccent) && Same(arcText, _cArcText)
                && Same(flatText, _cFlatText))
                return;

            _cFlags = flags; _cSwapKey = swapK; _cPageKey = pageK; _cReachKey = reachK; _cFadeVer = _fadeVer;
            _cAccent = accent; _cArcText = arcText; _cFlatText = flatText;
            _hintsVer++;

            // Glyph(KeyCode) falls through to enum.ToString() for letters — only on a change, here.
            string swap = swapK != KeyCode.None ? UiaKeybinds.Glyph(swapK) : null;
            string page = pageK != KeyCode.None ? UiaKeybinds.Glyph(pageK) : null;
            string reach = reachK != KeyCode.None ? UiaKeybinds.Glyph(reachK) : null;

            _hints.Clear();
            if (!live)
            {
                // The editor / toggle preview: a representative sample, never counted as exposure.
                _hints.Add(new Hint(null, "RMB", "back"));
                _hints.Add(new Hint(null, "Drag", "move item"));
                _hints.Add(new Hint(null, "Shift+click", "keep open"));
                if (swap != null) _hints.Add(new Hint(null, swap, "switch hand"));
                if (reach != null) _hints.Add(new Hint(null, reach, "grab from world"));
                return;
            }

            if (RadialHintContext.Search)
            {
                // SearchPanelView: Enter takes the top result, a result wedge press-drags out, Esc/RMB
                // return to the wheel (typing is the panel's own "type to search" line).
                Add(K_SearchTake, "Enter", "take first", fadeOn);
                Add(K_Drag, "Drag", "move item", fadeOn);
                Add(K_SearchBack, "Esc", "back to wheel", fadeOn);
                return;
            }
            if (RadialHintContext.Dragging)
            {
                // ResolveDragRelease: a wedge / HUD box / world slot (outside the wheel) takes it, open
                // screen parks it. RMB mid-drag cancels just the drag (UpdateSticky).
                Add(K_PlaceOrPark, "Release", "place or park", fadeOn);
                Add(K_Cancel, "RMB", "cancel drag", fadeOn);
                return;
            }
            // What RMB does right now (RadialMenu publishes CanGoBack from the real handlers: B1 — a
            // sticky child ring at the root is a close, not a back; B15b — the belt picker's RMB is a
            // back). Whenever it also DROPS the parked items (every close, and the picker's back, which
            // replaces the menu — D-004) it says so, under its own fade counter (B11).
            string rmb = "close";
            string rmbKind = K_Back;
            if (RadialHintContext.CanGoBack)
            {
                if (RadialHintContext.BackDropsParked) { rmb = "back, drop parked"; rmbKind = K_DropParked; }
                else rmb = "back";
            }
            else if (RadialHintContext.Parked > 0) { rmb = "close, drop parked"; rmbKind = K_DropParked; }
            if (RadialHintContext.AltReach)
            {
                // TryBeginDrag: with the world-reach key held, LMB grabs a free item or a world-slot
                // occupant into the drag layer.
                Add(K_Grab, "LMB", "grab from world", fadeOn);
                Add(rmbKind, "RMB", rmb, fadeOn);
                return;
            }
            if (!RadialHintContext.Sticky)
            {
                // Hold mode (OnHoldReleased / UpdateHoldB): letting go runs the hovered action, anything
                // else closes; resting on a branch dives in by itself; RMB backs out; no world reach.
                Add(K_Release, "Release", RadialHintContext.HoverAction ? "confirm" : "close", fadeOn);
                if (RadialHintContext.CanGoBack) Add(K_Back, "RMB", "back", fadeOn);
                if (ActiveOpensOnBoth()) Add(K_Tap, "Tap key", "stays open", fadeOn);
                return;
            }

            // Sticky (tap-opened): priority order — the arc trims from the END when it runs out of room.
            Add(rmbKind, "RMB", rmb, fadeOn);
            if (RadialHintContext.LevelDraggable) Add(K_Drag, "Drag", "move item", fadeOn);
            if (RadialHintContext.LevelScroll) Add(K_Wheel, "Wheel", "adjust value", fadeOn);
            if (RadialHintContext.LevelAction)
                Add(K_Shift, "Shift+click", ShiftKeepsOpen() ? "keep open" : "close after", fadeOn);
            if (page != null)
            {
                if (ActiveIsToolbelt()) Add(K_Belt, page, "change belt", fadeOn);          // RadialController: Q = belt picker here
                else if (RadialHintContext.Pageable) Add(K_Page, page, "next page", fadeOn);
            }
            if (swap != null) Add(K_Swap, swap, "switch hand", fadeOn);
            if (reach != null) Add(K_Reach, reach, "grab from world", fadeOn);
        }

        private static void Add(string kind, string key, string action, bool fadeOn)
        {
            if (fadeOn && _faded.Contains(kind)) return;   // this kind has been learned — drop it
            _hints.Add(new Hint(kind, key, action));
        }

        /// <summary>Every context bit the hint list depends on, packed for a cheap change test.</summary>
        private static int ContextFlags(bool live)
        {
            if (!live) return 1 << 30;
            int f = 1;
            if (RadialHintContext.Search) f |= 1 << 1;
            if (RadialHintContext.Dragging) f |= 1 << 2;
            if (RadialHintContext.AltReach) f |= 1 << 3;
            if (RadialHintContext.Sticky) f |= 1 << 4;
            if (RadialHintContext.CanGoBack) f |= 1 << 5;
            if (RadialHintContext.Parked > 0) f |= 1 << 6;
            if (RadialHintContext.LevelDraggable) f |= 1 << 7;
            if (RadialHintContext.LevelScroll) f |= 1 << 8;
            if (RadialHintContext.LevelAction) f |= 1 << 9;
            if (RadialHintContext.Pageable) f |= 1 << 10;
            // B8: only hold mode's "Release confirm/close" reads HoverAction. Packing it in sticky mode
            // made every wedge-to-wedge sweep rebuild the list and refit the arc for nothing.
            if (!RadialHintContext.Sticky && RadialHintContext.HoverAction) f |= 1 << 11;
            if (ShiftKeepsOpen()) f |= 1 << 12;
            if (ActiveIsToolbelt()) f |= 1 << 13;
            if (ActiveOpensOnBoth()) f |= 1 << 14;
            if (RadialHintContext.BackDropsParked) f |= 1 << 15;
            return f;
        }

        private static bool ShiftKeepsOpen()
            => UIAConfig.RadialShiftKeepsOpen == null || UIAConfig.RadialShiftKeepsOpen.Value;

        /// <summary>Is the open wheel the toolbelt ring — by the controller's OWN test (A12b / B3), so
        /// the 6-key ring (D-007: identical content, Q belt-swaps there too) gets the Q hint as well
        /// as the MMB Belt Wheel.</summary>
        private static bool ActiveIsToolbelt()
        {
            try { return RadialController.IsToolbeltRing(RadialController.Active?.ActiveFeature); }
            catch { return false; }
        }

        private static bool ActiveOpensOnBoth()
        {
            try { var f = RadialController.Active?.ActiveFeature; return f != null && f.OpensOnBoth; }
            catch { return false; }
        }

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        /// <summary>The first <paramref name="count"/> hints as TMP rich text: key names in the theme
        /// accent, separators a dimmed copy of the text colour. Tags are ASCII; only glyphs render.
        /// <c>internal</c> (not private) only for the uiatest harness (Testing/UiaTestHarness.cs).</summary>
        internal static string Compose(int count, Color32 baseText)
        {
            Color32 sep = baseText;
            sep.a = (byte)(sep.a * 0.45f);
            string keyHex = RadialPalette.ToHex(_cAccent);
            string sepHex = RadialPalette.ToHex(sep);
            _sb.Length = 0;
            for (int i = 0; i < count && i < _hints.Count; i++)
            {
                if (i > 0) _sb.Append("   <color=#").Append(sepHex).Append(">/</color>   ");
                var h = _hints[i];
                _sb.Append("<color=#").Append(keyHex).Append('>').Append(h.Key).Append("</color>  ").Append(h.Action);
            }
            return _sb.ToString();
        }

        // ---- the curved presentation ----

        /// <summary>Wrap the hints under the ring's bottom outside edge. Returns false (flat instead)
        /// when the ring is too small or not even the first hint fits a legible arc.</summary>
        private static bool LayoutArc(float dt)
        {
            if (_arc == null) return false;
            float outerR = RadialHintContext.OuterR;
            float h = HeightPx();
            float innerR = outerR + DropPx();
            float rMid = innerR + h * 0.5f;
            if (rMid < MinArcRadius) return false;

            // A cached "does not fit" stands until an input changes (no re-measuring every frame).
            bool refit = _arcFitVer != _hintsVer || !Mathf.Approximately(_arcFitRadius, rMid)
                || !Mathf.Approximately(_arcFitSize, FontSizePx()) || _arcFitBold != BoldOn()
                || !ReferenceEquals(_arcFitFont, UnityRadialView.Font());
            if (!refit && _arcCount == 0) return false;
            if (refit && Time.unscaledTime < _arcRetryAt) return false; // a failed measure retries, throttled

            // On screen (active) BEFORE measuring: the text is measured on the live object below.
            if (!_arc.StepFade(true, dt, ArcFadeSpeed)) return false;

            var tmp = _arc.Text;
            UnityRadialView.SyncFont(tmp);   // the radial's own font: it is part of the wheel now
            float fs = FontSizePx();
            bool bold = BoldOn();
            if (!Mathf.Approximately(tmp.fontSize, fs)) tmp.fontSize = fs;
            var style = bold ? FontStyles.Bold : FontStyles.Normal;
            if (tmp.fontStyle != style) tmp.fontStyle = style;

            // Fit: trim from the lowest-priority end until the text spans at most MaxArcHalfDeg either
            // side of straight-down. Re-run only when the hints, radius or text style changed.
            if (refit)
            {
                _arcFitVer = _hintsVer; _arcFitRadius = rMid; _arcFitSize = fs; _arcFitBold = bold; _arcFitFont = tmp.font;
                float budget = 2f * MaxArcHalfDeg * Mathf.Deg2Rad * rMid;
                _arcCount = 0;
                _arcText = null;
                _arcLen = 0f;
                for (int n = _hints.Count; n > 0; n--)
                {
                    // Set, then measure: preferredWidth reads the CURRENT text (the flat strip's proven
                    // pattern), so what is measured is exactly what gets bent and shown.
                    string s = Compose(n, _cArcText);
                    tmp.text = s;
                    float len = tmp.preferredWidth;
                    if (len <= 0.5f)
                    {
                        // The font/layout isn't ready: don't cache a bogus width — measure again shortly
                        // (throttled, so a broken font can't churn the object every frame), flat meanwhile.
                        _arcFitVer = -1;
                        _arcRetryAt = Time.unscaledTime + 0.5f;
                        _arc.HideNow();
                        return false;
                    }
                    if (len <= budget)
                    {
                        _arcCount = n;
                        _arcText = s;
                        _arcLen = len;
                        break;
                    }
                }
                if (_arcCount == 0)
                {
                    // Not even the first hint fits: flat strip instead. Hide at once — a fade-out would
                    // show the half-measured text un-bent for a few frames.
                    _arc.HideNow();
                    return false;
                }
            }

            if (tmp.text != _arcText) tmp.text = _arcText;
            tmp.color = RadialPalette.ArcText.Value;

            float bw = UIAConfig.HintBarBorderWidth != null && UIAConfig.HintBarBorderWidth.Value > 0f
                ? UIAConfig.HintBarBorderWidth.Value
                : (UIAConfig.RadialBorderWidth != null ? UIAConfig.RadialBorderWidth.Value : 3.2f) * 0.5f;
            _arc.StylePlate(RadialPalette.ArcPlateFill.Value, RadialPalette.ArcPlateBorder.Value, bw);
            ApplyArcFx(_arc.Plate);
            _arc.Layout(UnityRadialView.CanvasAnchoredPos(RadialHintContext.Center), innerR, h, top: false,
                _arcLen, PaddingPx() * 0.5f);
            return true;
        }

        /// <summary>The flat strip's glass recipe (frost / sheen / edge light) on the curved plate.
        /// Frost engages only when asked for AND the HUD's Tier C master is on AND the backdrop capture
        /// runs AND the shader bundle resolved — anything missing degrades to plain translucency.</summary>
        private static void ApplyArcFx(RadialWedgeGraphic plate)
        {
            plate.Sheen = UIAConfig.HintBarSheen != null ? UIAConfig.HintBarSheen.Value : 0f;
            plate.EdgeLight = UIAConfig.HintBarSpec != null ? UIAConfig.HintBarSpec.Value : 0f;
            bool frostWanted = UIAConfig.HintBarFrost != null && UIAConfig.HintBarFrost.Value
                && Hud.HudConfig.FxTierC != null && Hud.HudConfig.FxTierC.Value;
            bool frostActive = frostWanted && Hud.HudBackdrop.Active
                && Core.HudShaderStore.TierBAvailable && Hud.HudFxMaterials.Available;
            plate.FxStrength = frostActive
                ? (UIAConfig.HintBarFrostStrength != null ? UIAConfig.HintBarFrostStrength.Value : 0.85f)
                : 0f;
            if (frostActive)
            {
                if (!Hud.HudFxMaterials.Assign(plate, "glass")) Hud.HudFxMaterials.Unassign(plate);
            }
            else Hud.HudFxMaterials.Unassign(plate);
        }

        /// <summary>Is a child ring (satellite) sitting over the strip? Angular overlap of the child's
        /// disc with the strip's arc around straight-down, plus a radial-band check.</summary>
        private static bool CoveredByChildRing(bool arc)
        {
            if (!RadialHintContext.SatelliteShown || !RadialHintContext.GeometryFresh) return false;
            float inner = RadialHintContext.OuterR + DropPx();
            float outer = inner + HeightPx();
            float half = arc
                ? ArcPlateLabel.HalfSpanFor(_arcLen, PaddingPx() * 0.5f, inner + HeightPx() * 0.5f)
                : Mathf.Atan2((_panel != null ? _panel.sizeDelta.x : 300f) * 0.5f, Mathf.Max(1f, inner));
            Vector2 d = RadialHintContext.SatCenter - RadialHintContext.Center;   // ImGui coords, y-down
            float dist = d.magnitude;
            float rs = RadialHintContext.SatOuterR + 12f;
            if (dist - rs > outer || dist + rs < inner) return false;
            float off = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 90f)) * Mathf.Deg2Rad;
            float satHalf = dist > rs ? Mathf.Asin(Mathf.Clamp01(rs / dist)) : Mathf.PI;
            return off - satHalf < half;
        }

        // ---- the flat presentation (fallback + F10 preview) ----

        private static void LayoutFlat(bool live, bool ringKnown)
        {
            if (!_panel.gameObject.activeSelf) _panel.gameObject.SetActive(true);
            if (_flatVer != _hintsVer)
            {
                _flatVer = _hintsVer;
                _flatText = Compose(_hints.Count, _cFlatText);
            }

            // Push the authored look every frame. Everything here is dirty-guarded (PanelGraphic's
            // setters and SetShape all early-out on an unchanged value), so an untouched strip costs
            // a handful of float compares — and a slider dragged in the F10 editor updates live.
            ApplyStyle();

            // Re-measure on a text change OR a style change: font size and padding are what actually
            // set the strip's length, so a slider drag has to re-run the layout, not just the text.
            float hash = StyleHash();
            if (_flatText != _last || !Mathf.Approximately(hash, _styleHash))
            {
                _last = _flatText;
                _styleHash = hash;
                _text.text = _flatText;
                float h = HeightPx();
                float w = Mathf.Clamp(_text.preferredWidth + PaddingPx(), 96f, 1400f);
                _panel.sizeDelta = new Vector2(w, h);
                _bg.SetShape(w, h, Mathf.Min(CornerPx(), h * 0.5f));
                if (_preview != null) _preview.rectTransform.sizeDelta = new Vector2(w + 40f, h + 24f);
            }

            // Preview (no live wheel): park the strip somewhere always visible with a WHITE swatch
            // behind it — the only honest way to judge a translucent black panel. Live: under the ring,
            // following a hub-dragged wheel when the ring's position is known.
            bool showPreviewBox = !live;
            if (_preview != null && _preview.enabled != showPreviewBox) _preview.enabled = showPreviewBox;
            float drop = DropPx();
            if (!live)
                _panel.anchoredPosition = new Vector2(0f, -140f);
            else if (ringKnown)
                _panel.anchoredPosition = UnityRadialView.CanvasAnchoredPos(RadialHintContext.Center)
                    + new Vector2(0f, -(RadialHintContext.OuterR + drop));
            else
            {
                float outerR = UIAConfig.RadialOuterRadius != null ? UIAConfig.RadialOuterRadius.Value : 240f;
                _panel.anchoredPosition = new Vector2(0f, -(outerR + drop));
            }
            if (_preview != null) _preview.rectTransform.anchoredPosition = _panel.anchoredPosition;
        }

        // ---- authored look (F10 → Radial → Hint bar; colours live in RadialPalette) ----

        private static float CornerPx() => UIAConfig.HintBarCorner != null ? UIAConfig.HintBarCorner.Value : 12f;
        private static float HeightPx() => UIAConfig.HintBarHeight != null ? UIAConfig.HintBarHeight.Value : BarH;
        private static float PaddingPx() => UIAConfig.HintBarPadding != null ? UIAConfig.HintBarPadding.Value : 24f;
        private static float DropPx() => UIAConfig.HintBarDrop != null ? UIAConfig.HintBarDrop.Value : 34f;
        private static float FontSizePx() => UIAConfig.HintBarFontSize != null ? UIAConfig.HintBarFontSize.Value : 11.2f;
        private static bool BoldOn() => UIAConfig.HintBarBold == null || UIAConfig.HintBarBold.Value;

        /// <summary>Cheap change-detector over the values that affect LAYOUT (not just paint), so the
        /// strip re-measures when one of them moves.</summary>
        private static float StyleHash()
        {
            return FontSizePx() * 31f + HeightPx() * 57f + PaddingPx() * 91f + CornerPx() * 7f + (BoldOn() ? 3f : 0f);
        }

        private static void ApplyStyle()
        {
            if (_bg == null || _text == null) return;

            _bg.color = RadialPalette.HintBarFill.Value;
            _bg.BorderColor = RadialPalette.HintBarBorder.Value;
            _bg.BorderWidth = UIAConfig.HintBarBorderWidth != null ? UIAConfig.HintBarBorderWidth.Value : 0f;
            _bg.FeatherOverride = UIAConfig.HintBarFeather != null ? UIAConfig.HintBarFeather.Value : 1.25f;
            _bg.Sheen = UIAConfig.HintBarSheen != null ? UIAConfig.HintBarSheen.Value : 0.35f;
            _bg.Spec = UIAConfig.HintBarSpec != null ? UIAConfig.HintBarSpec.Value : 0f;
            _bg.Glow = UIAConfig.HintBarGlow != null ? UIAConfig.HintBarGlow.Value : 0f;
            _bg.GlowWidth = UIAConfig.HintBarGlowWidth != null ? UIAConfig.HintBarGlowWidth.Value : 24f;

            // FROST — the blurred-screen glass. Exactly the radial's own recipe (UnityRadialView
            // .UpdateRadialFx/ApplyWedgeFx): frost only engages when the user asked for it AND the
            // HUD's Tier C master is on AND the backdrop capture is running AND the shader bundle
            // resolved. Anything missing degrades to plain translucency rather than a broken panel.
            bool frostWanted = UIAConfig.HintBarFrost != null && UIAConfig.HintBarFrost.Value
                && Hud.HudConfig.FxTierC != null && Hud.HudConfig.FxTierC.Value;
            bool frostActive = frostWanted && Hud.HudBackdrop.Active
                && Core.HudShaderStore.TierBAvailable && Hud.HudFxMaterials.Available;
            _bg.FxStrength = frostActive
                ? (UIAConfig.HintBarFrostStrength != null ? UIAConfig.HintBarFrostStrength.Value : 0.85f)
                : 0f;
            if (frostActive)
            {
                if (!Hud.HudFxMaterials.Assign(_bg, "glass")) Hud.HudFxMaterials.Unassign(_bg);
            }
            else Hud.HudFxMaterials.Unassign(_bg);

            _text.color = RadialPalette.HintBarText.Value;
            float fs = FontSizePx();
            if (!Mathf.Approximately(_text.fontSize, fs)) _text.fontSize = fs;
            var style = BoldOn() ? FontStyles.Bold : FontStyles.Normal;
            if (_text.fontStyle != style) _text.fontStyle = style;

            // Keep the text inset in step with the padding so the two never fight over the ends.
            float inset = Mathf.Max(2f, PaddingPx() * 0.5f);
            var trt = _text.rectTransform;
            if (!Mathf.Approximately(trt.offsetMin.x, inset))
            {
                trt.offsetMin = new Vector2(inset, 0f);
                trt.offsetMax = new Vector2(-inset, 0f);
            }
        }

        private static void HideAll()
        {
            if (_canvas != null && _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
            if (_arc != null) _arc.HideNow();
            if (_rootGroup != null) _rootGroup.alpha = 1f; // a reopened wheel never starts "covered"
        }

        // ---- session bookkeeping: one exposure per drawn kind per open→close of the ring ----

        private static void UpdateSession(bool radialOpen, bool fadeOn)
        {
            if (radialOpen && !_sessionActive)
            {
                _sessionActive = true;
                _sessionShown.Clear();
                RecomputeFaded(fadeOn);
            }
            else if (!radialOpen && _sessionActive)
            {
                _sessionActive = false;
                if (fadeOn && _sessionShown.Count > 0)
                {
                    foreach (var k in _sessionShown) HintUsageStore.Add(k);
                    HintUsageStore.Flush();
                }
                _sessionShown.Clear();
            }
        }

        private static void RecomputeFaded(bool fadeOn)
        {
            _faded.Clear();
            if (fadeOn)
            {
                for (int i = 0; i < AllKinds.Length; i++)
                    if (HintUsageStore.Get(AllKinds[i]) >= FadeThreshold) _faded.Add(AllKinds[i]);
            }
            _fadeVer++;        // invalidate the hint-list cache
        }

        private static void EnsureBuilt()
        {
            if (_root != null) return;
            _root = new GameObject("UIAscended_RadialHintBar");
            Object.DontDestroyOnLoad(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // BEHIND all radial content. The strip sits spatially BELOW the ring, but child radials,
            // satellites and other generated wedges expand into its area — at 5010 (above the radial's
            // 5000) the strip drew ON TOP of them. Park it just UNDER the radial canvas (5000) so the
            // ring and everything it spawns always render over the strip, while staying above the radial
            // editor's black backdrop (4900, so the F10 hint-bar preview still shows) and the HUD (3800,
            // so the strip stays visible where nothing overlaps it).
            _canvas.sortingOrder = 4999;
            _rootGroup = _root.AddComponent<CanvasGroup>();
            _rootGroup.interactable = false;
            _rootGroup.blocksRaycasts = false;

            // Created FIRST so it sits behind the strip in sibling order.
            var previewGo = new GameObject("previewSwatch", typeof(RectTransform));
            previewGo.transform.SetParent(_root.transform, false);
            var prt = (RectTransform)previewGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(640f, 54f);
            _preview = previewGo.AddComponent<Image>();
            _preview.color = Color.white;
            _preview.raycastTarget = false;
            _preview.enabled = false;

            var panelGo = new GameObject("bar", typeof(RectTransform));
            panelGo.transform.SetParent(_root.transform, false);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(600f, BarH);
            // BLACK, curved, glassy (FlorpyDorp). PanelGraphic gives the rounded corners and the
            // rim/sheen for free; the old square Image + 1px Outline could do neither.
            // Look is AUTHORED, not hard-coded: ApplyStyle() pushes the F10 "Hint bar" settings and
            // the RadialPalette colours every frame. Only the raycast flag is structural.
            _bg = panelGo.AddComponent<Hud.PanelGraphic>();
            _bg.raycastTarget = false;

            var txtGo = new GameObject("text", typeof(RectTransform));
            txtGo.transform.SetParent(_panel, false);
            _text = txtGo.AddComponent<TextMeshProUGUI>();
            _text.font = UiaTheme.Font();
            // Size/colour/weight are pushed by ApplyStyle() from the F10 settings. NOTE the strip
            // auto-sizes to its text, so FONT SIZE is also the knob that makes the bar longer or
            // shorter — there is no separate "length" to set.
            _text.alignment = TextAlignmentOptions.Center;
            _text.raycastTarget = false;
            _text.enableWordWrapping = false;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.richText = true; // key names carry the theme accent colour
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 0f); trt.offsetMax = new Vector2(-10f, 0f);

            // D-021: the curved strip that wraps under the wheel (last = drawn over the flat strip).
            _arc = new ArcPlateLabel(_root.transform, "arc");
            _arc.Text.font = UnityRadialView.Font();
        }

        public static void Shutdown()
        {
            if (_arc != null) Hud.HudFxMaterials.Unassign(_arc.Plate);
            if (_bg != null) Hud.HudFxMaterials.Unassign(_bg);
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _rootGroup = null;
            _panel = null;
            _bg = null;
            _text = null;
            _preview = null;
            _arc = null;
            _last = null;
            _styleHash = float.NaN;   // force a re-measure on the next build (hot-reload safe)

            // progressive-fade + composition statics (hot-reload safe: a double-F6 leaves nothing stranded)
            _sessionActive = false;
            _faded.Clear();
            _sessionShown.Clear();
            _fadeVer = 0;
            _hints.Clear();
            _sb.Length = 0;
            _cFlags = -1;
            _cSwapKey = _cPageKey = _cReachKey = KeyCode.None;
            _cFadeVer = -1;
            _cAccent = _cArcText = _cFlatText = default(Color32);
            _hintsVer = 0;
            _flatText = null;
            _flatVer = -1;
            _arcText = null;
            _arcLen = 0f;
            _arcCount = 0;
            _arcFitVer = -1;
            _arcFitRadius = -1f;
            _arcFitSize = -1f;
            _arcFitBold = false;
            _arcFitFont = null;
            _arcRetryAt = 0f;
            HintUsageStore.Reset();
            RadialHintContext.Reset();
        }
    }
}
