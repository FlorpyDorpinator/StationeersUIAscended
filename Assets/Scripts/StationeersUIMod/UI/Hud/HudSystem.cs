using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using BepInEx.Configuration;
using StationeersUIMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The visor HUD: one canvas (under the radials), a panel registry, the diegetic
    /// state machine (BARE / SUITED / ROBOT), the flicker animator, and all three
    /// curvature implementations from the feasibility report:
    ///   A. VertexWarp   — per-element mesh bend on the overlay canvas,
    ///   B. DomeProjection — the canvas renders through an off-screen camera into a
    ///      RenderTexture shown on a dome grid (disabled camera + manual Render(), the
    ///      vanilla off-screen pattern — enabled cameras get water/decal command buffers),
    ///   C. CurvedWorldCanvas — the canvas floats in world space in front of the camera,
    ///      cylinder-bent, on ZTest-Always materials (TMP's own Overlay shader ships).
    /// Driven from the plugin's Update (pure UGUI — no ImGui frame needed).
    /// </summary>
    public static class HudSystem
    {
        private const int HudLayerDome = 6;   // verified unused in 27701 (no objects, no masks)
        private const int HudLayerUi = 5;     // in both runtime culling masks (world canvas)

        private static Canvas _canvas;
        private static CanvasGroup _rootGroup;
        private static Canvas _domeCanvas;
        private static DomeDisplayGraphic _domeDisplay;
        private static ScanlineGraphic _domeScan;
        private static Camera _rtCam;
        private static RenderTexture _rt;
        private static VignetteGraphic _vignette;
        private static CanvasGroup _vignetteGroup;
        private static HudAnimator.Fader _vignetteFader;

        private static readonly List<HudPanel> _panels = new List<HudPanel>();
        private static VitalsCard _vitals;
        private static readonly HudAnimator _animator = new HudAnimator();

        private static HudTier _prevTier = HudTier.Bare;
        private static HudTier? _prevForceTier;
        private static bool _prevPowered;
        private static bool _hasPrev;
        private static float _layoutHash;
        private static int _screenW, _screenH;
        private static HudCurvature _appliedMode = HudCurvature.Flat;

        // vanilla panel hiding (hide, never destroy). Tracked as "we owe this panel a
        // restore" — the reconciliation below works off the panels' ACTUAL state, because
        // vanilla re-asserts them itself (unconscious watcher, character customisation).
        private static bool _restoreHands, _restoreClothing, _restoreStatus;

        // mode C material bookkeeping
        private static Material _ztestMat;
        private static readonly Dictionary<TextMeshProUGUI, Material> _tmpOriginalMats
            = new Dictionary<TextMeshProUGUI, Material>();
        private static readonly Dictionary<Material, Material> _overlayFontMats
            = new Dictionary<Material, Material>();
        private static bool _worldMatsApplied;
        // Render-time re-pose hook for mode C (see OnWorldCanvasPreCull). Subscribed only while
        // the curved world canvas is live; must be unhooked on every teardown (hot-reload safety).
        private static bool _preCullHooked;

        public static bool Built => _canvas != null;
        public static HudSnapshot LastSnapshot { get; private set; }

        /// <summary>Editor hooks: while set, the HUD renders this tier regardless of the
        /// sampled one (style BARE without undressing).</summary>
        public static HudTier? ForceTier;

        /// <summary>While a test animation plays, the per-frame visibility reconciliation
        /// stands down — otherwise it would cancel the demo on the very next frame.</summary>
        private static float _demoHoldUntil;

        public static void TestPowerDeath()
        {
            _animator.PowerDeath();
            _demoHoldUntil = Time.unscaledTime + 2.4f;
        }

        public static void TestBoot()
        {
            // Cut the suit-tier panels dark first (BootUp only lifts hidden faders),
            // then boot the ones the user actually has enabled.
            foreach (var p in _panels)
                if (p.SuitTier) _animator.SetVisible(p.Fader, false, instant: true);
            _animator.BootUp(BootEligible);
            _demoHoldUntil = Time.unscaledTime + 2.4f;
        }

        /// <summary>Config-disabled panels must stay dark through a boot sequence.</summary>
        private static bool BootEligible(HudAnimator.Fader f)
        {
            if (ReferenceEquals(f, _vignetteFader)) return HudConfig.ShowVignette.Value;
            foreach (var p in _panels)
                if (ReferenceEquals(p.Fader, f))
                    return (p.Toggle == null || p.Toggle.Value) && p.VisibleAt(HudTier.Suited);
            return true;
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Which mode the current canvas was built for — a live flag flip
        /// tears down and rebuilds (see Update).</summary>
        private static bool _builtDocMode;

        private static void EnsureBuilt()
        {
            if (_canvas != null) return;
            _builtDocMode = DocumentMode;

            var go = new GameObject("UIAscended_HudCanvas");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.layer = HudLayerUi;
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 3800; // above vanilla HUD (0), under radials (5000)
            // A raycaster so BORROWED vanilla widgets (the moodlet strip) can still receive
            // their own hover tooltips. Only graphics with raycastTarget=true are hit, and
            // the root CanvasGroup blocks raycasts by default — a subtree opts back in with
            // its own ignoreParentGroups CanvasGroup (see MoodletBorrowWidget), so the rest
            // of the HUD never intercepts the mouse.
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            _rootGroup = go.AddComponent<CanvasGroup>();
            _rootGroup.interactable = false;
            _rootGroup.blocksRaycasts = false;

            // Vignette first: behind every panel.
            var vgo = new GameObject("UIA_Vignette", typeof(RectTransform));
            vgo.transform.SetParent(go.transform, false);
            _vignette = vgo.AddComponent<VignetteGraphic>();
            _vignette.raycastTarget = false;
            _vignetteGroup = vgo.AddComponent<CanvasGroup>();
            var vrt = (RectTransform)vgo.transform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = vrt.offsetMax = Vector2.zero;

            _panels.Clear();
            _animator.Clear();
            _vignetteFader = _animator.Register(vrt, _vignetteGroup, suitTier: false, seed: 91);

            if (DocumentMode)
            {
                EnsureActiveDocument();
                BuildViewsFromDocument();
            }
            else
            {
                AddPanel(new TopStatusBar(), 1);
                AddPanel(new CompassRibbon(), 2);
                AddPanel(new EquipmentColumn(), 3);
                AddPanel(new HandBoxes(), 4);
                _vitals = new VitalsCard();
                AddPanel(_vitals, 5);
                AddPanel(new BareSensesPanel(), 6);
            }

            SetLayerRecursively(go, HudLayerUi);
            _screenW = Screen.width;
            _screenH = Screen.height;
            RelayoutAll();
        }

        private static void AddPanel(HudPanel p, int seed)
        {
            p.Build(_canvas.transform);
            p.Fader = _animator.Register(p.Root, p.Group, p.SuitTier, seed);
            _panels.Add(p);
        }

        // ------------------------------------------------------------------ document mode

        internal static bool DocumentMode => HudConfig.UseDocumentHud != null
            && HudConfig.UseDocumentHud.Value;

        /// <summary>Set when the store swaps documents (profile switch, editor load) —
        /// views are renderers of a dead document at that point and must rebuild.</summary>
        private static bool _docRebuildNeeded;
        private static bool _docEventHooked;

        private static void EnsureActiveDocument()
        {
            if (!_docEventHooked)
            {
                Features.HudProfileStore.ActiveReplaced += OnActiveDocReplaced;
                _docEventHooked = true;
            }
            if (Features.HudProfileStore.Active == null)
            {
                string name = HudConfig.HudActiveProfile != null
                    ? HudConfig.HudActiveProfile.Value : "Default";
                // Self-heal with the factory that MATCHES the profile name: a missing/
                // corrupt Glassy must respawn as the glass design, not get the flat
                // starter silently written under its name (review find, 2026-07-12).
                System.Func<HudDocument> factory =
                    string.Equals(name, Glassy40Default.Name, System.StringComparison.OrdinalIgnoreCase)
                        ? (System.Func<HudDocument>)BuildGlassy40Document
                    : string.Equals(name, "Glassy 2.0", System.StringComparison.OrdinalIgnoreCase)
                        ? (System.Func<HudDocument>)BuildGlassy2Document
                    : string.Equals(name, "Glassy", System.StringComparison.OrdinalIgnoreCase)
                        ? (System.Func<HudDocument>)BuildGlassyDocument
                        : BuildStarterDocument;
                Features.HudProfileStore.LoadActive(name, factory);

                // A stale shipped-default (the schema-1 primitive demo) upgrades to the
                // current starter — but ONLY the literal "Default" profile; anything the
                // user named themselves is theirs, whatever its age.
                var active = Features.HudProfileStore.Active;
                if (active != null && active.Schema < 6
                    && string.Equals(name, "Default", System.StringComparison.OrdinalIgnoreCase))
                {
                    var fresh = BuildStarterDocument();
                    Features.HudProfileStore.SetActive(fresh, name);
                    Features.HudProfileStore.MarkChanged(); // persist the upgrade
                }
                else if (active != null && active.Schema < 12
                    && string.Equals(name, "Glassy 2.0", System.StringComparison.OrdinalIgnoreCase))
                {
                    var fresh = BuildGlassy2Document();
                    Features.HudProfileStore.SetActive(fresh, name);
                    Features.HudProfileStore.MarkChanged();
                }
                _docRebuildNeeded = false; // SetActive fired the event; we build right after

                // Ship the alternate "Glassy" look alongside Default. Seeded only when
                // the file is ABSENT, so a user's edits to it are never overwritten;
                // deleting it respawns a fresh copy next session (Default's contract).
                bool haveGlassy2 = false, haveGlassy40 = false;
                foreach (var n in Features.HudProfileStore.ListProfiles())
                {
                    if (string.Equals(n, "Glassy 2.0", System.StringComparison.OrdinalIgnoreCase)) haveGlassy2 = true;
                    if (string.Equals(n, Glassy40Default.Name, System.StringComparison.OrdinalIgnoreCase)) haveGlassy40 = true;
                }
                // Glassy 2.0: the full car-dashboard redesign. Seeded only when absent
                // (user edits survive); switch to it in F9 → Profiles.
                if (!haveGlassy2)
                    Features.HudProfileStore.Save(BuildGlassy2Document(), "Glassy 2.0");
                // Glassy 4.0: the SHIPPED DEFAULT (FlorpyDorp's hand-arranged layout). Seeded
                // when absent; it's also the default active profile.
                if (!haveGlassy40)
                    Features.HudProfileStore.Save(BuildGlassy40Document(), Glassy40Default.Name);
            }
        }

        private static bool _debugForcedBare;

        // The moodlet icons WE force-activated in debug show-all — tracked so we can turn
        // exactly those back off when the flag clears. Vanilla does NOT re-hide them (it only
        // manages statuses it considers active), so "they won't go away" without this.
        private static readonly List<GameObject> _forcedMoodlets = new List<GameObject>();

        /// <summary>Debug show-all: light every vanilla moodlet whose icon actually has a
        /// sprite (a sprite-less status renders as a white box — that was the "toxins moodlet
        /// is a white box"). Records each one we switch on so <see cref="ClearForcedMoodlets"/>
        /// can switch it back off. Display-only, no game state touched.</summary>
        private static void ForceAllMoodlets()
        {
            try
            {
                var all = Assets.Scripts.UI.StatusUpdates.AllStatusUpdates;
                if (all == null) return;
                for (int i = 0; i < all.Count; i++)
                {
                    var su = all[i];
                    if (su == null || su.UsesDedicatedDisplay) continue;
                    var img = su.Image;
                    if (img == null || img.sprite == null) continue; // no art → white box
                    if (!img.gameObject.activeSelf)
                    {
                        img.gameObject.SetActive(true);
                        if (!_forcedMoodlets.Contains(img.gameObject)) _forcedMoodlets.Add(img.gameObject);
                    }
                }
            }
            catch { }
        }

        /// <summary>Turn back off the moodlets WE forced on (debug cleared / teardown).</summary>
        private static void ClearForcedMoodlets()
        {
            for (int i = 0; i < _forcedMoodlets.Count; i++)
            {
                var go = _forcedMoodlets[i];
                try { if (go != null) go.SetActive(false); } catch { }
            }
            _forcedMoodlets.Clear();
        }

        private static void OnActiveDocReplaced() => _docRebuildNeeded = true;

        /// <summary>The designer mutated the document STRUCTURE (add/delete/duplicate) —
        /// views must rebuild next frame. Geometry-only edits never need this.</summary>
        internal static void RequestViewRebuild() => _docRebuildNeeded = true;

        /// <summary>One view per document element, Z-sorted into sibling order. The
        /// animator seed derives from the element Id so flicker desync survives both
        /// rebuilds and hot reloads.</summary>
        private static void BuildViewsFromDocument()
        {
            var doc = Features.HudProfileStore.Active;
            if (doc == null) return;

            _docSorted.Clear();
            _docSorted.AddRange(doc.Elements);
            _docSorted.Sort((a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z)
                : string.CompareOrdinal(a.Id, b.Id)); // stable tie-break: no z-fighting churn

            foreach (var def in _docSorted)
            {
                if (def == null) continue;
                // Fail-soft per element: one broken element (bad params, hand-edited
                // profile) degrades to a missing element, never a missing HUD.
                try
                {
                    HudElementView view = CreateViewFor(def);
                    view.Def = def;
                    AddPanel(view, HudDocument.StableSeed(def.Id));
                }
                catch (System.Exception e)
                {
                    Core.UIALog.Warn("HUD element '" + def.Id + "' (" + def.Type + ") failed to build: " + e.Message);
                }
            }
        }

        private static readonly List<HudElementDef> _docSorted = new List<HudElementDef>();

        /// <summary>The type→view registry. Everything unregistered renders as
        /// PrimitiveView's named placeholder (newer profiles degrade visibly).</summary>
        private static HudElementView CreateViewFor(HudElementDef def)
        {
            switch (def.Type)
            {
                case HudElementType.Compass: return new Widgets.CompassWidget();
                // Borrow vanilla's own moodlet strip into our bar (FlorpyDorp: "just the
                // vanilla moodlet behavior, but in the moodlet bar"). MoodletDashboardWidget
                // (our re-implementation) is retired in favour of the real thing.
                case HudElementType.MoodletDashboard: return new Widgets.MoodletBorrowWidget();
                case HudElementType.BodyDoll: return new Widgets.BodyDollWidget();
                case HudElementType.SuitChips: return new Widgets.SuitChipsWidget();
                case HudElementType.EquipmentColumn: return new Widgets.EquipmentColumnWidget();
                case HudElementType.HandBoxes: return new Widgets.HandBoxesWidget();
                case HudElementType.KeybindChips: return new Widgets.KeybindChipsWidget();
                case HudElementType.BareSenses: return new Widgets.BareSensesWidget();
                case HudElementType.Portrait: return new Widgets.PortraitWidget();
                case HudElementType.Readout: return new Widgets.ReadoutWidget();
                case HudElementType.VitalsPanel: return new Widgets.VitalsPanelWidget();
                case HudElementType.DamageDoll: return new Widgets.DamageDollBorrowWidget();
                case HudElementType.JetpackBox: return new Widgets.JetpackBoxWidget();
                case HudElementType.StateChips: return new Widgets.StateChipsWidget();
                case HudElementType.PngDoll: return new Widgets.PngDollWidget();
                case HudElementType.Clock:
                case HudElementType.WorldName:
                case HudElementType.DayCounter:
                case HudElementType.ActiveHandBadge:
                    return new Widgets.DynamicTextWidget();
                default:
                    return new PrimitiveView();
            }
        }

        /// <summary>Hand the vanilla portrait back from WHOEVER borrowed it — the legacy
        /// vitals card or any document PortraitWidget. Safe on empty/none.</summary>
        private static void RestoreAnyPortraits()
        {
            try { _vitals?.RestorePortrait(); } catch { }
            foreach (var p in _panels)
            {
                var pw = p as Widgets.PortraitWidget;
                if (pw != null) { try { pw.RestorePortrait(); } catch { } continue; }
                // Borrowed vanilla body doll: hand it back to PlayerStateWindow too.
                var dw = p as Widgets.DamageDollBorrowWidget;
                if (dw != null) { try { dw.RestoreDoll(); } catch { } continue; }
                // Borrowed vanilla moodlet strip: hand it back too.
                var mw = p as Widgets.MoodletBorrowWidget;
                if (mw != null) { try { mw.RestoreMoodlets(); } catch { } }
            }
        }

        /// <summary>Torn down and rebuilt in place (document swap): panels die, the
        /// canvas, vignette and animator survive.</summary>
        private static void RebuildViews()
        {
            RestoreAnyPortraits();
            foreach (var p in _panels) p.Destroy();
            _panels.Clear();
            _vitals = null;
            _animator.Clear();
            if (_vignette != null)
                _vignetteFader = _animator.Register((RectTransform)_vignette.transform,
                    _vignetteGroup, suitTier: false, seed: 91);
            BuildViewsFromDocument();
            SetLayerRecursively(_canvas.gameObject,
                _appliedMode == HudCurvature.DomeProjection ? HudLayerDome : HudLayerUi);
            // Mode C swaps every Graphic's material at apply time — views built AFTER
            // that would render with stock materials (z-fight into the world) unless
            // the swap is re-run over the fresh subtree.
            if (_appliedMode == HudCurvature.CurvedWorldCanvas) ApplyWorldMaterials();
            _hasPrev = false; // fresh views must snap to visibility, not flicker in
            RelayoutAll();
        }

        private const HudTierMask SuitOnly = HudTierMask.Suited | HudTierMask.Robot;

        private static HudElementDef El(string id, HudElementType type, HudAnchor anchor,
            float x, float y, float w, float h, HudTierMask tiers = HudTierMask.All, int z = 1)
        {
            return new HudElementDef
            {
                Id = id, Type = type, Anchor = anchor,
                X = x, Y = y, W = w, H = h, Tiers = tiers, Z = z,
            };
        }

        /// <summary>The shipped default layout — built to FlorpyDorp's sketch: minimal
        /// top bar (time + compass), vertical gauge pairs mid-left (external) and
        /// mid-right (internal + jetpack) with suit chips beneath, needs list + damage
        /// doll + round portrait bottom-right, and the equipment keys SPLIT 1-2-3 /
        /// 4-5-6 around the hand boxes. Written to HudProfiles/Default.xml on first run
        /// (and whenever the file goes missing).</summary>
        private static HudDocument BuildStarterDocument()
        {
            // Schema 6 = the sketch layout with the game's own ramp-bar art on the
            // pressure/temp gauges (EnsureActiveDocument upgrades stale shipped Defaults).
            var doc = new HudDocument { Name = "Default", Schema = 6 };
            var els = doc.Elements;

            // --- top bar: time left, compass dead center, day + world right ---
            var bar = El("top-bar", HudElementType.Box, HudAnchor.TopCenter, 0f, -44f, 1840f, 60f, SuitOnly, 0);
            bar.WPct = 0.94f;
            bar.RTL = 4f; bar.RTR = 4f; bar.RBR = 14f; bar.RBL = 14f;
            els.Add(bar);
            els.Add(El("clock", HudElementType.Clock, HudAnchor.TopLeft, 190f, -44f, 200f, 40f, SuitOnly));
            els.Add(El("compass", HudElementType.Compass, HudAnchor.TopCenter, 0f, -44f, 260f, 44f, SuitOnly));
            els.Add(El("day", HudElementType.DayCounter, HudAnchor.TopRight, -300f, -44f, 130f, 40f, SuitOnly));
            els.Add(El("world", HudElementType.WorldName, HudAnchor.TopRight, -120f, -44f, 190f, 40f, SuitOnly));

            // --- moodlets: the game's own icons, center-out under the bar, no boxes ---
            els.Add(El("moodlets", HudElementType.MoodletDashboard, HudAnchor.TopCenter, 0f, -102f, 820f, 40f));

            // --- mid-left: the outside world as two vertical gauges ---
            AddGauge(els, "ext-pressure", "ExternalPressure", "EXT\nPRESS", "Pressure", HudAnchor.MiddleLeft, 70f, 0f);
            AddGauge(els, "ext-temp", "ExternalTemp", "EXT\nTEMP", "Temp", HudAnchor.MiddleLeft, 165f, 0f);

            // --- mid-right: the suit's instruments + jetpack, chips underneath ---
            AddGauge(els, "int-temp", "FeltTemp", "INT\nTEMP", "Temp", HudAnchor.MiddleRight, -260f, 30f);
            AddGauge(els, "int-pressure", "InternalPressure", "INT\nPRESS", "Pressure", HudAnchor.MiddleRight, -165f, 30f);
            AddGauge(els, "jetpack", "JetpackPropellant", "JET\nPACK", "Jetpack", HudAnchor.MiddleRight, -70f, 30f);
            var chips = El("suit-chips", HudElementType.SuitChips, HudAnchor.MiddleRight, -165f, -150f, 220f, 44f, SuitOnly);
            chips.SetB("internals", true);
            els.Add(chips);

            // --- bottom-right: needs list + damage doll + the round portrait ---
            var needsBox = El("needs-panel", HudElementType.Box, HudAnchor.BottomRight, -330f, 110f, 240f, 160f, SuitOnly, 0);
            needsBox.RTL = 12f; needsBox.RTR = 12f; needsBox.RBR = 12f; needsBox.RBL = 12f;
            els.Add(needsBox);
            AddRow(els, "row-hunger", "Nutrition", "HGR", "Hunger", -330f, 158f, 216f, 44f);
            AddRow(els, "row-water", "Hydration", "WTR", "Water", -330f, 110f, 216f, 44f);
            AddRow(els, "row-toilet", "Sanitation", "TLT", "Toilet", -330f, 62f, 216f, 44f);

            els.Add(El("body-doll", HudElementType.BodyDoll, HudAnchor.BottomRight, -480f, 110f, 70f, 160f));
            els.Add(El("portrait", HudElementType.Portrait, HudAnchor.BottomRight, -110f, 110f, 170f, 170f, SuitOnly));

            // --- bottom center: 1 2 3 | LEFT HAND | RIGHT HAND | 4 5 6 ---
            var eqLeft = El("equipment-left", HudElementType.EquipmentColumn, HudAnchor.BottomCenter, -350f, 60f, 260f, 78f);
            eqLeft.SetB("horizontal", true);
            eqLeft.SetI("first", 0); eqLeft.SetI("count", 3);
            els.Add(eqLeft);
            var hands = El("hands", HudElementType.HandBoxes, HudAnchor.BottomCenter, 0f, 70f, 380f, 110f);
            hands.SetB("tray", false); // just the two boxes — no trapezoid shelf
            els.Add(hands);
            var eqRight = El("equipment-right", HudElementType.EquipmentColumn, HudAnchor.BottomCenter, 350f, 60f, 260f, 78f);
            eqRight.SetB("horizontal", true);
            eqRight.SetI("first", 3); eqRight.SetI("count", 3);
            els.Add(eqRight);

            // --- bare tier: the felt-sense words own the middle of the view ---
            els.Add(El("bare-senses", HudElementType.BareSenses, HudAnchor.Center, 0f, -40f, 420f, 320f, HudTierMask.Bare));

            return doc;
        }

        /// <summary>The shipped "Glassy" profile: the sketch layout restyled after the
        /// vanilla visor — smoked-glass panels with a top-lit sheen, pale hairline
        /// borders that catch light (bright→dim runs along each edge), and a faint
        /// full-screen visor frame. Colours are LITERALS on purpose: switching to this
        /// profile must not touch the shared palette or the Default profile.</summary>
        private static HudDocument BuildGlassyDocument()
        {
            var doc = BuildStarterDocument();
            doc.Name = "Glassy";

            const string glassFill = "#05080D96";    // smoked near-black, ~59% present
            const string glassBorder = "#D9E6EE59";  // pale steel hairline; spec adds the hot spots

            foreach (var el in doc.Elements)
            {
                switch (el.Type)
                {
                    case HudElementType.Box:
                    case HudElementType.Readout:
                    case HudElementType.Compass:
                    case HudElementType.HandBoxes:
                    case HudElementType.EquipmentColumn:
                    case HudElementType.KeybindChips:
                    case HudElementType.SuitChips:
                    case HudElementType.MoodletDashboard:
                    case HudElementType.Clock:
                    case HudElementType.WorldName:
                    case HudElementType.DayCounter:
                    case HudElementType.ActiveHandBadge:
                        el.Fill = glassFill;
                        el.Border = glassBorder;
                        el.SetF("sheen", 0.55f);
                        el.SetF("spec", 0.85f);
                        break;
                    case HudElementType.Portrait:
                        // The ring is a CircleGraphic (no glass mesh) — colours only.
                        el.Border = glassBorder;
                        break;
                        // BodyDoll/BareSenses/Label/Polyline/Icon keep their own colours —
                        // fill drives doll parts and line/text art, not a glass pane.
                }
            }

            // The visor frame from the vanilla screenshot: a huge, fill-less rounded
            // rect hugging the screen, its border catching light at the top corners.
            var frame = El("visor-frame", HudElementType.Box, HudAnchor.Center, 0f, 8f,
                1830f, 990f, SuitOnly, 0);
            frame.WPct = 0.965f;
            frame.HPct = 0.93f;
            frame.Fill = "#FFFFFF00";
            frame.Border = "#DFEDF542";
            frame.BorderWidth = 1.6f;
            frame.RTL = 48f; frame.RTR = 48f; frame.RBR = 48f; frame.RBL = 48f;
            frame.SetF("spec", 1f);
            doc.Elements.Insert(0, frame);

            return doc;
        }

        // ---- Glassy 2.0: the full car-dashboard redesign (FlorpyDorp's Big Prompt) ----

        private const string G2Fill = "#05080DA6";     // smoked near-black glass
        private const string G2Border = "#B9BEC259";   // grey hairline (play-test: "greyer")
        private const float G2Sheen = 0.5f;
        private const float G2Spec = 0.8f;

        /// <summary>Glass-style a boxy element in place (fill/border + sheen/spec).</summary>
        private static HudElementDef G2Glass(HudElementDef e)
        {
            e.Fill = G2Fill; e.Border = G2Border;
            e.SetF("sheen", G2Sheen); e.SetF("spec", G2Spec);
            return e;
        }

        /// <summary>The Glassy 2.0 layout, schema 7 — built element-by-element to the Big
        /// Prompt: a full-width trapezoid top bar (time / ext-pressure+ramp / boxless compass
        /// / ext-temp+icon / day), a subtle wrapping moodlet strip under it, the bottom hand+
        /// equipment row with a visor-rim arc, and the bottom-right instrument cluster
        /// (portrait with camera zoom, state chips, jetpack box, stacked internal pressure/
        /// temp with the game ramp, vanilla vitals + borrowed damage doll + speed). Bare tier
        /// gets a words-mode vitals panel and everything flattens (BareFlattens).</summary>
        /// <summary>The SHIPPED DEFAULT "Glassy 4.0": FlorpyDorp's hand-arranged layout,
        /// deserialized from the embedded document (Glassy40Default.Xml). Sanitize() runs on
        /// it like any loaded profile. Falls back to Glassy 2.0 if the embed ever fails to
        /// parse, so the starter is never null.</summary>
        private static HudDocument BuildGlassy40Document()
        {
            try
            {
                var ser = new System.Xml.Serialization.XmlSerializer(typeof(HudDocument));
                using (var r = new System.IO.StringReader(Glassy40Default.Xml))
                {
                    var doc = (HudDocument)ser.Deserialize(r);
                    if (doc != null && doc.Elements != null && doc.Elements.Count > 0)
                    {
                        doc.Sanitize();
                        doc.Name = Glassy40Default.Name;
                        return doc;
                    }
                }
            }
            catch (System.Exception e)
            {
                Core.UIALog.Warn("Glassy 4.0 embed failed to parse: " + e.Message);
            }
            return BuildGlassy2Document();
        }

        private static HudDocument BuildGlassy2Document()
        {
            var doc = new HudDocument { Name = "Glassy 2.0", Schema = 12 };
            var els = doc.Elements;

            // ===== 1. TOP BAR (full-width trapezoid, top edge wider than bottom) =====
            var bar = El("g2-topbar", HudElementType.Box, HudAnchor.TopCenter, 0f, -38f, 1860f, 62f, SuitOnly, 0);
            bar.WPct = 0.99f;
            bar.RTL = 4f; bar.RTR = 4f; bar.RBR = 20f; bar.RBL = 20f;
            bar.SetF("insetBottom", 46f); // bottom corners pull in → angled sides, wider top
            els.Add(G2Glass(bar));

            els.Add(El("g2-clock", HudElementType.Clock, HudAnchor.TopLeft, 150f, -38f, 200f, 40f, SuitOnly, 2));

            // External pressure: value with the vanilla ramp bar UNDER the text.
            var extP = El("g2-extpress", HudElementType.Readout, HudAnchor.TopLeft, 470f, -38f, 250f, 50f, SuitOnly, 2);
            extP.Set("src", "ExternalPressure"); extP.Set("label", "EXTERNAL PRESSURE");
            extP.SetB("box", false); extP.SetB("bar", true); extP.Set("barStyle", "game");
            extP.SetB("barVertical", false); extP.SetB("target", false);
            els.Add(extP);

            // Compass: boxless, fades into the bar, degrees underneath.
            var comp = El("g2-compass", HudElementType.Compass, HudAnchor.TopCenter, 0f, -36f, 300f, 46f, SuitOnly, 2);
            comp.SetB("box", false);
            els.Add(comp);

            // External temp: label + conditional hot/cold icon + value.
            var extT = El("g2-exttemp", HudElementType.Readout, HudAnchor.TopRight, -360f, -38f, 240f, 50f, SuitOnly, 2);
            extT.Set("src", "ExternalTemp"); extT.Set("label", "EXTERNAL TEMP");
            extT.SetB("box", false); extT.SetB("bar", false); extT.SetB("target", false);
            extT.SetB("tempIcon", true); extT.Icon = "Temp";
            els.Add(extT);

            els.Add(El("g2-day", HudElementType.DayCounter, HudAnchor.TopRight, -120f, -38f, 130f, 40f, SuitOnly, 2));

            // ===== 2. MOODLET STRIP (subtle, wrapping, game icons) =====
            // The vanilla moodlet strip, relocated here (MoodletBorrowWidget) and scaled up.
            var mood = El("g2-moodlets", HudElementType.MoodletDashboard, HudAnchor.TopCenter, 0f, -104f, 1100f, 90f, HudTierMask.All, 1);
            mood.SetF("moodletScale", 0.34f);
            els.Add(mood);

            // ===== 3. BOTTOM: 1 2 3 | hands | 4 5 6, with the visor-rim arc =====
            var eqL = El("g2-eqleft", HudElementType.EquipmentColumn, HudAnchor.BottomCenter, -360f, 56f, 264f, 78f, HudTierMask.All, 2);
            eqL.SetB("horizontal", true); eqL.SetI("first", 0); eqL.SetI("count", 3);
            els.Add(G2Glass(eqL));
            var hands = El("g2-hands", HudElementType.HandBoxes, HudAnchor.BottomCenter, 0f, 66f, 390f, 110f, HudTierMask.All, 2);
            hands.SetB("tray", false);
            els.Add(G2Glass(hands));
            var eqR = El("g2-eqright", HudElementType.EquipmentColumn, HudAnchor.BottomCenter, 360f, 56f, 264f, 78f, HudTierMask.All, 2);
            eqR.SetB("horizontal", true); eqR.SetI("first", 3); eqR.SetI("count", 3);
            els.Add(G2Glass(eqR));

            // The visor's lower rim: a shallow arc over the hand row that fades at both ends.
            var arc = El("g2-visor-arc", HudElementType.Polyline, HudAnchor.BottomCenter, 0f, 150f, 900f, 60f, SuitOnly, 1);
            arc.TextColor = "#CFE6F088";
            arc.SetF("width", 2.4f); arc.SetF("fadeEnds", 0.32f);
            arc.SetPoints("pts", new List<Vector2>
            {
                new Vector2(-440f, -14f), new Vector2(-220f, 8f), new Vector2(0f, 14f),
                new Vector2(220f, 8f), new Vector2(440f, -14f),
            });
            els.Add(arc);

            // ===== 4. BOTTOM-RIGHT INSTRUMENT CLUSTER =====
            // Round portrait (rightmost) + state chips above it.
            var portrait = El("g2-portrait", HudElementType.Portrait, HudAnchor.BottomRight, -120f, 118f, 184f, 184f, SuitOnly, 3);
            portrait.Border = G2Border;
            portrait.SetF("camFov", 24f); // pull the too-close vanilla framing back a touch
            els.Add(portrait);
            var chips = El("g2-chips", HudElementType.StateChips, HudAnchor.BottomRight, -120f, 226f, 168f, 40f, SuitOnly, 3);
            els.Add(G2Glass(chips));

            // Internal pressure (stacked: title / TARGET / value / game ramp bar).
            var intP = El("g2-intpress", HudElementType.Readout, HudAnchor.BottomRight, -300f, 176f, 168f, 128f, SuitOnly, 3);
            intP.Set("src", "InternalPressure"); intP.Set("label", "INTERNAL PRESSURE");
            intP.SetB("box", true); intP.SetB("stack", true); intP.SetB("bar", true);
            intP.Set("barStyle", "game"); intP.SetB("barVertical", false); intP.SetB("target", true);
            els.Add(G2Glass(intP));

            // Internal temp (stacked: title / TARGET / value, conditional hot/cold icon, no bar).
            var intT = El("g2-inttemp", HudElementType.Readout, HudAnchor.BottomRight, -300f, 52f, 168f, 108f, SuitOnly, 3);
            intT.Set("src", "InternalTemp"); intT.Set("label", "INTERNAL TEMP");
            intT.SetB("box", true); intT.SetB("stack", true); intT.SetB("bar", false);
            intT.SetB("target", true); intT.SetB("tempIcon", true); intT.Icon = "Temp";
            els.Add(G2Glass(intT));

            // Jetpack box (title / THRUST / delta kPa + green canister).
            var jet = El("g2-jetpack", HudElementType.JetpackBox, HudAnchor.BottomRight, -480f, 176f, 168f, 128f, SuitOnly, 3);
            els.Add(G2Glass(jet));

            // Vitals (vanilla icons + %; dynamic membership). Tall enough for all four
            // rows (hunger/water/toilet/health) so a damaged+dirty player doesn't clip.
            var vit = El("g2-vitals", HudElementType.VitalsPanel, HudAnchor.BottomRight, -480f, 60f, 168f, 140f, SuitOnly, 3);
            els.Add(G2Glass(vit));

            // Borrowed damage doll (left of vitals) + speed readout.
            els.Add(G2Glass(El("g2-doll", HudElementType.DamageDoll, HudAnchor.BottomRight, -648f, 130f, 120f, 150f, HudTierMask.All, 3)));
            var spd = El("g2-speed", HudElementType.Readout, HudAnchor.BottomRight, -648f, 32f, 120f, 52f, SuitOnly, 3);
            spd.Set("src", "Speed"); spd.Set("label", "SPEED");
            spd.SetB("box", true); spd.SetB("bar", false); spd.SetB("target", false);
            spd.Icon = "speed"; // vanilla running-man (SymbolVelocity)
            els.Add(G2Glass(spd));

            // ===== 5. BARE TIER: words-mode vitals + pressure/temp words =====
            var bare = El("g2-bare-vitals", HudElementType.VitalsPanel, HudAnchor.BottomRight, -300f, 90f, 300f, 220f, HudTierMask.Bare, 3);
            bare.Fill = "#0A0E14C0"; bare.Border = "#00000000"; // borderless grey panel
            bare.SetF("sheen", 0.25f); bare.SetF("spec", 0f);
            bare.SetB("box", true); bare.SetB("words", true);
            bare.SetB("rowPressure", true); bare.SetB("rowTemp", true);
            els.Add(bare);
            // Bare felt-senses keep the center of view.
            els.Add(El("g2-bare-senses", HudElementType.BareSenses, HudAnchor.Center, 0f, -40f, 420f, 320f, HudTierMask.Bare, 2));

            return doc;
        }

        /// <summary>A vertical instrument gauge (the sketch's columns): boxed, two-line
        /// label, value, the GAME'S own ramp-bar art, game icon at the foot.</summary>
        private static void AddGauge(List<HudElementDef> els, string id, string src,
            string label, string icon, HudAnchor anchor, float x, float y)
        {
            var e = El(id, HudElementType.Readout, anchor, x, y, 86f, 230f, SuitOnly);
            e.Set("src", src);
            e.Set("label", label);
            e.SetB("box", true);
            e.SetB("bar", true);
            e.SetB("barVertical", true);
            e.SetB("target", false);
            e.Set("barStyle", "game");
            e.Icon = icon;
            e.SetF("valueSize", 14f);
            els.Add(e);
        }

        /// <summary>One of the mockup's compact readout rows: icon + label + value with a
        /// thin horizontal threshold bar underneath, no box of its own (the cluster panel
        /// is the backdrop).</summary>
        private static void AddRow(List<HudElementDef> els, string id, string src,
            string label, string icon, float x, float y, float w, float h)
        {
            var e = El(id, HudElementType.Readout, HudAnchor.BottomRight, x, y, w, h, SuitOnly);
            e.Set("src", src);
            e.Set("label", label);
            e.SetB("box", false);
            e.SetB("bar", true);
            e.SetB("target", false);
            e.Icon = icon;
            e.SetF("valueSize", 15f);
            els.Add(e);
        }

        public static void Shutdown()
        {
            RestoreAnyPortraits();
            RestoreVanillaIfNeeded();
            RestoreWorldMaterials();
            UnhookWorldCanvasPreCull(); // Shutdown bypasses ApplyCurvature's teardown; a leaked
                                        // static Camera.onPreCull delegate would call into a dead
                                        // assembly after an F6 hot reload.
            foreach (var p in _panels) p.Destroy();
            _panels.Clear();
            _animator.Clear();
            _vitals = null;
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _rootGroup = null;
            _vignette = null;
            if (_domeCanvas != null) UnityEngine.Object.Destroy(_domeCanvas.gameObject);
            _domeCanvas = null;
            _domeDisplay = null;
            _domeScan = null;
            if (_rtCam != null) UnityEngine.Object.Destroy(_rtCam.gameObject);
            _rtCam = null;
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
            if (_ztestMat != null) { UnityEngine.Object.Destroy(_ztestMat); _ztestMat = null; }
            foreach (var m in _overlayFontMats.Values)
                if (m != null) UnityEngine.Object.Destroy(m);
            _overlayFontMats.Clear();
            _tmpOriginalMats.Clear();
            HudText.Shutdown();
            HudWarp.Active = HudWarp.Kind.None;
            HudWarp.BareFlat = false; // reset alongside Active so a reload starts un-flattened
            HudSampler.DebugShowAll = false;
            ClearForcedMoodlets();
            _debugForcedBare = false;
            _hasPrev = false;
            _appliedMode = HudCurvature.Flat;
            ForceTier = null;
            _prevForceTier = null;
            _demoHoldUntil = 0f;
            LastSnapshot = null;
            HudSampler.Clear();
            // Document mode: flush any pending autosave and drop the active document +
            // event hook (hot-reload rule — a reloaded assembly re-subscribes cleanly).
            if (_docEventHooked)
            {
                Features.HudProfileStore.ActiveReplaced -= OnActiveDocReplaced;
                _docEventHooked = false;
            }
            _docRebuildNeeded = false;
            Features.HudProfileStore.Shutdown();
        }

        // ------------------------------------------------------------------ main loop

        public static void Update(bool editorActive)
        {
            bool enabled = HudConfig.VisorHudEnabled != null && HudConfig.VisorHudEnabled.Value
                && (HudConfig.LegacyImGuiHud == null || !HudConfig.LegacyImGuiHud.Value);
            if (editorActive) enabled = true;

            if (!enabled || !Guards.CanDraw())
            {
                if (_canvas != null && _canvas.gameObject.activeSelf)
                {
                    RestoreAnyPortraits();
                    _canvas.gameObject.SetActive(false);
                    if (_domeCanvas != null) _domeCanvas.gameObject.SetActive(false);
                }
                // Never pin the last world's Human across unloads / hot reloads.
                LastSnapshot = null;
                HudSampler.Clear();
                // MasterEnable off is a full stand-down: give vanilla its panels back
                // (Guards.CanDraw is false then, so no later frame would do it).
                if (!enabled || !UIAConfig.MasterEnable.Value) RestoreVanillaIfNeeded();
                return;
            }

            // Flipping the Document-HUD toggle live must rebuild the whole surface —
            // panel set and document views are different worlds (review finding: the
            // in-window checkbox otherwise left a stale mix on screen).
            if (_canvas != null && _builtDocMode != DocumentMode)
            {
                Shutdown();
            }

            EnsureBuilt();
            _canvas.gameObject.SetActive(true);
            SyncVanillaVisibility();

            if (DocumentMode)
            {
                // Profile switched / editor loaded a different document: views render a
                // dead object now — rebuild in place (canvas + animator survive).
                if (_docRebuildNeeded)
                {
                    _docRebuildNeeded = false;
                    RebuildViews();
                }
                Features.HudProfileStore.Tick(Time.unscaledTime); // debounced autosave
            }

            // Debug "show everything": fill the snapshot before sampling reads the flag, and
            // the bare variant additionally forces the power-off tier so the suit-off layout
            // is previewable full. Both light every vanilla moodlet too.
            bool dbgBare = HudConfig.DebugShowAllBare != null && HudConfig.DebugShowAllBare.Value;
            bool dbgAll = (HudConfig.DebugShowAll != null && HudConfig.DebugShowAll.Value) || dbgBare;
            HudSampler.DebugShowAll = dbgAll;
            if (dbgBare) ForceTier = HudTier.Bare;
            else if (_debugForcedBare) ForceTier = null; // clear only what WE forced
            _debugForcedBare = dbgBare;
            if (dbgAll) ForceAllMoodlets();
            else if (_forcedMoodlets.Count > 0) ClearForcedMoodlets(); // flag cleared → hide them

            var snap = HudSampler.Sample();
            LastSnapshot = snap;
            if (!snap.Valid) return;

            HudTier tier = ForceTier ?? snap.Tier;

            // Bare = no visor = FLAT HUD (FlorpyDorp: suit off/dead → everything goes flat,
            // uncurved). Only honoured when the user opted into flattening; the LayoutHash
            // below folds it in, so the transition triggers a relayout + re-mesh.
            HudWarp.BareFlat = (tier == HudTier.Bare)
                && (HudConfig.BareFlattens == null || HudConfig.BareFlattens.Value);

            // Setting/clearing the editor's preview tier must never REPLAY a transition
            // (_prevTier tracked the forced tier while real events were suppressed).
            bool forcedChanged = ForceTier != _prevForceTier;
            _prevForceTier = ForceTier;

            // --- state transitions (flicker events) ---
            if (_hasPrev && HudConfig.FlickerAnimations.Value && !ForceTier.HasValue && !forcedChanged)
            {
                if (_prevTier != HudTier.Bare && tier == HudTier.Bare)
                {
                    _animator.PowerDeath();
                    // Document element views have no Toggle (null = always on) — same
                    // guard as every sibling loop, or a Bare-only element NREs here and
                    // wedges _prevTier so the transition re-throws every frame.
                    foreach (var p in _panels)
                        if (p.VisibleAt(HudTier.Bare) && !p.VisibleAt(HudTier.Suited)
                            && (p.Toggle == null || p.Toggle.Value))
                            _animator.SlowShow(p.Fader);
                }
                else if (_prevTier == HudTier.Bare && tier != HudTier.Bare)
                {
                    _animator.BootUp(BootEligible);
                }
            }
            _prevTier = tier;
            _prevPowered = snap.SuitPowered;

            // --- per-panel desired visibility (stands down while a test anim plays) ---
            if (Time.unscaledTime >= _demoHoldUntil)
            {
                foreach (var p in _panels)
                {
                    bool want = p.Toggle == null || p.Toggle.Value;
                    want &= p.VisibleAt(tier);
                    _animator.SetVisible(p.Fader, want, instant: !_hasPrev);
                    // Portrait holders borrow the vanilla portrait camera; the moment one
                    // is no longer wanted (tier drop, toggle, document edit) the portrait
                    // goes back to vanilla.
                    if (!want && ReferenceEquals(p, _vitals)) _vitals.RestorePortrait();
                    else if (!want && p is Widgets.PortraitWidget pw) pw.RestorePortrait();
                    // The borrowed vanilla damage doll goes back the instant it's unwanted.
                    else if (!want && p is Widgets.DamageDollBorrowWidget dw) dw.RestoreDoll();
                    // Same for the borrowed vanilla moodlet strip.
                    else if (!want && p is Widgets.MoodletBorrowWidget mw) mw.RestoreMoodlets();
                }
                bool vignetteWant = HudConfig.ShowVignette.Value;
                _animator.SetVisible(_vignetteFader, vignetteWant, instant: !_hasPrev);
            }
            _hasPrev = true;

            // --- layout / curvature ---
            float scale = HudConfig.HudScale.Value;
            float hash = LayoutHash(scale);
            if (!Mathf.Approximately(hash, _layoutHash) || _screenW != Screen.width || _screenH != Screen.height)
            {
                _layoutHash = hash;
                _screenW = Screen.width;
                _screenH = Screen.height;
                RelayoutAll();
            }
            ApplyCurvature();

            // --- content ---
            _vignette.color = HudPalette.Vignette.Value;
            foreach (var p in _panels)
            {
                if (p.Group != null && p.Group.gameObject.activeSelf)
                {
                    try { p.UpdatePanel(snap, scale); }
                    catch (Exception e)
                    {
                        // Fail soft, but never spam: a persistently-throwing panel would
                        // otherwise log (and allocate) every frame.
                        if (++p.UpdateFailures <= 3)
                            UIALog.Warn("HUD panel " + p.Id + " update failed: " + e.Message
                                + (p.UpdateFailures == 3 ? " (further errors suppressed)" : ""));
                    }
                }
            }

            // --- animations ---
            float now = Time.unscaledTime;
            _animator.Update(Mathf.Min(Time.unscaledDeltaTime, 0.1f), now);
            _rootGroup.alpha = _animator.DropoutMultiplier(snap.LowPower && tier != HudTier.Bare, now);

            // --- dome render ---
            if (_appliedMode == HudCurvature.DomeProjection && _rtCam != null)
            {
                try { _rtCam.Render(); } catch { }
            }
        }

        private static void RelayoutAll()
        {
            float scale = HudConfig.HudScale.Value;
            HudWarp.HalfW = Screen.width * 0.5f;
            HudWarp.HalfH = Screen.height * 0.5f;
            foreach (var p in _panels)
            {
                try { p.Layout(scale); } catch (Exception e) { UIALog.Warn("HUD layout " + p.Id + ": " + e.Message); }
            }
            DirtyAllMeshes();
        }

        private static float LayoutHash(float scale)
        {
            return scale * 3.1f
                + HudConfig.TopBarHeight.Value * 1.01f + HudConfig.TopBarCurve.Value * 1.37f
                + HudConfig.TopBarWidthPct.Value * 211f
                + HudConfig.CompassWidthPct.Value * 401f + HudConfig.CompassHeight.Value * 1.61f
                + HudConfig.EquipBoxSize.Value * 2.03f + HudConfig.EquipSpacing.Value * 2.71f
                + HudConfig.HandBoxWidth.Value * 0.97f + HudConfig.HandBoxHeight.Value * 1.13f
                + HudConfig.VitalsWidth.Value * 0.89f + HudConfig.VitalsHeight.Value * 1.19f
                + HudConfig.CornerRadius.Value * 5.3f + HudConfig.BareWordFontSize.Value * 3.7f
                + HudConfig.FontScale.Value * 97f
                + HudConfig.EdgeFeather.Value * 41f   // read inside OnPopulateMesh — meshes
                                                      // must rebuild when the slider moves
                + (HudConfig.CurveInvert.Value ? 313f : 0f)
                + HudConfig.CurveStrength.Value * 631f + (int)HudConfig.Curvature.Value * 977f
                + (HudWarp.BareFlat ? 1289f : 0f) // bare→flat transition re-lays-out + re-meshes
                // Document mode: any element edit bumps the store version — geometry
                // lives in the document, so this replaces the per-panel size entries.
                + (DocumentMode ? Features.HudProfileStore.Version * 3571f : 0f);
        }

        // ------------------------------------------------------------------ curvature

        private static void ApplyCurvature()
        {
            var mode = HudConfig.Curvature.Value;
            float strength = HudConfig.CurveStrength.Value;
            HudWarp.Direction = HudConfig.CurveInvert.Value ? -1f : 1f;
            // Mode C's natural visor is the INVERTED cylinder (edges bulging toward the
            // camera approximate a shell around your head; the outward bend reads
            // inside-out there). Flip the semantic so the DEFAULT is the good look and
            // the invert checkbox still offers the other.
            if (mode == HudCurvature.CurvedWorldCanvas) HudWarp.Direction = -HudWarp.Direction;

            if (mode != _appliedMode)
            {
                // Tear down the old mode.
                if (_appliedMode == HudCurvature.CurvedWorldCanvas)
                {
                    RestoreWorldMaterials();
                    UnhookWorldCanvasPreCull();
                }
                if (_appliedMode == HudCurvature.DomeProjection) ReleaseDomeRt();
                if (_domeCanvas != null) _domeCanvas.gameObject.SetActive(false);

                switch (mode)
                {
                    case HudCurvature.DomeProjection:
                        EnsureDome();
                        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        _canvas.worldCamera = _rtCam;
                        _canvas.planeDistance = 5f;
                        SetLayerRecursively(_canvas.gameObject, HudLayerDome);
                        _domeCanvas.gameObject.SetActive(true);
                        break;
                    case HudCurvature.CurvedWorldCanvas:
                        _canvas.renderMode = RenderMode.WorldSpace;
                        SetLayerRecursively(_canvas.gameObject, HudLayerUi);
                        ApplyWorldMaterials();
                        HookWorldCanvasPreCull(); // re-pose at render time, not one frame late
                        break;
                    default:
                        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                        _canvas.sortingOrder = 3800;
                        SetLayerRecursively(_canvas.gameObject, HudLayerUi);
                        break;
                }
                _appliedMode = mode;
                DirtyAllMeshes();
            }

            switch (mode)
            {
                case HudCurvature.VertexWarp:
                    HudWarp.Active = HudWarp.Kind.Barrel;
                    HudWarp.Strength = strength;
                    break;
                case HudCurvature.CurvedWorldCanvas:
                    HudWarp.Active = HudWarp.Kind.Cylinder;
                    HudWarp.Strength = strength;
                    UpdateWorldCanvasPose();
                    break;
                case HudCurvature.DomeProjection:
                    HudWarp.Active = HudWarp.Kind.None;
                    UpdateDome(strength);
                    break;
                default:
                    HudWarp.Active = HudWarp.Kind.None;
                    break;
            }
        }

        // ---- mode B: RenderTexture dome ----

        private static void EnsureDome()
        {
            if (_rtCam == null)
            {
                var go = new GameObject("UIAscended_HudRtCamera");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.transform.position = new Vector3(0f, -5000f, 0f); // far below the world; kill plane only affects Things
                _rtCam = go.AddComponent<Camera>();
                _rtCam.enabled = false;                 // manual Render(): enabled cameras
                                                        // collect water/decal command buffers
                _rtCam.orthographic = true;
                _rtCam.clearFlags = CameraClearFlags.SolidColor;
                _rtCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _rtCam.cullingMask = 1 << HudLayerDome;
                _rtCam.nearClipPlane = 0.1f;
                _rtCam.farClipPlane = 20f;
                _rtCam.useOcclusionCulling = false;
                _rtCam.allowHDR = false;
                _rtCam.allowMSAA = false;
            }
            if (_domeCanvas == null)
            {
                var go = new GameObject("UIAscended_HudDomeDisplay");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _domeCanvas = go.AddComponent<Canvas>();
                _domeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _domeCanvas.sortingOrder = 3800;

                var dgo = new GameObject("Dome", typeof(RectTransform));
                dgo.transform.SetParent(go.transform, false);
                _domeDisplay = dgo.AddComponent<DomeDisplayGraphic>();
                _domeDisplay.raycastTarget = false;
                Stretch((RectTransform)dgo.transform);

                var sgo = new GameObject("Scanlines", typeof(RectTransform));
                sgo.transform.SetParent(go.transform, false);
                _domeScan = sgo.AddComponent<ScanlineGraphic>();
                _domeScan.raycastTarget = false;
                Stretch((RectTransform)sgo.transform);
            }
        }

        private static void UpdateDome(float strength)
        {
            if (_rtCam == null || _domeDisplay == null) return;
            if (_rt == null || _rt.width != Screen.width || _rt.height != Screen.height)
            {
                if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); }
                _rt = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32)
                {
                    filterMode = FilterMode.Bilinear,
                };
                _rtCam.targetTexture = _rt;
            }
            _rtCam.orthographicSize = Screen.height * 0.5f;
            _domeDisplay.Texture = _rt;
            float dir = HudConfig.CurveInvert.Value ? -1f : 1f;
            if (!Mathf.Approximately(_domeDisplay.Strength, strength)
                || !Mathf.Approximately(_domeDisplay.Direction, dir))
            {
                _domeDisplay.Strength = strength;
                _domeDisplay.Direction = dir;
                _domeDisplay.Refresh();
            }
            _domeDisplay.color = Color.white;
            var scan = HudPalette.Scanline.Value;
            if (_domeScan.color != scan) { _domeScan.color = scan; _domeScan.Refresh(); }
        }

        /// <summary>A screen-sized RT is real memory — hand it back when leaving mode B.</summary>
        private static void ReleaseDomeRt()
        {
            if (_rtCam != null) _rtCam.targetTexture = null;
            if (_domeDisplay != null) _domeDisplay.Texture = null;
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
                _rt = null;
            }
        }

        // ---- mode C: curved world canvas ----

        private static void UpdateWorldCanvasPose()
        {
            Camera cam = null;
            try { cam = CameraController.CurrentCamera; } catch { }
            if (cam == null) return;
            // Keep the plane safely beyond the camera's near clip — inside it, whole edges
            // of the HUD vanished ("UI elements totally clip out").
            float dist = Mathf.Max(HudConfig.WorldCanvasDistance.Value, cam.nearClipPlane + 0.06f);
            var rt = (RectTransform)_canvas.transform;
            rt.sizeDelta = new Vector2(Screen.width, Screen.height);
            var t = cam.transform;
            rt.SetPositionAndRotation(t.position + t.rotation * new Vector3(0f, 0f, dist), t.rotation);
            // Size from a FIXED reference distance, not from `dist` — sizing from dist made
            // the canvas fill the frustum EXACTLY at every distance, so the "visor distance"
            // slider visibly did nothing. Anchored to 0.6 m, the HUD now genuinely recedes
            // (smaller, margins appear) as the slider goes up and looms closer below it.
            const float refDist = 0.6f;
            float worldH = 2f * refDist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float s = worldH / Mathf.Max(1f, Screen.height);
            rt.localScale = new Vector3(s, s, s);

            // INVERTED curve bulges the canvas TOWARD the camera (negative cylinder Z at
            // the edges) — the bulging edges projected huge and left the screen entirely.
            // Push the whole plane back by the worst-case bulge so its nearest point still
            // sits at the configured distance.
            float k = HudWarp.Strength * HudWarp.Direction;
            if (HudWarp.Active == HudWarp.Kind.Cylinder && k < 0f)
            {
                float bulgePx = -k * (HudWarp.HalfW * 0.25f + HudWarp.HalfH * 0.08f);
                rt.position += t.rotation * new Vector3(0f, 0f, bulgePx * s);
            }
            _canvas.worldCamera = cam;
        }

        // The world canvas is head-locked to the camera, but the game finalizes the camera
        // transform in CameraController.LateUpdate (mouse-look + body-follow, via
        // CacheCameraPosition) — AFTER every Update. Posing the canvas in Update (the
        // ApplyCurvature fallback above) therefore locks it to the PREVIOUS frame's camera, so
        // the curved plane trails the view by one frame and swims/warps while you move (a flat
        // canvas would only translate; the z-displaced curve makes the same lag read as
        // distortion). Re-posing in Camera.onPreCull — after all LateUpdates, immediately before
        // the main camera culls/renders — makes the camera→canvas relative transform identical
        // every frame, so the curve is invariant to walking and turning, and cam.fieldOfView is
        // read at render time (no size "breathing" on an FOV kick). Built-in render pipeline only;
        // under SRP onPreCull is silent and the lagged Update pose simply stands in.
        private static void HookWorldCanvasPreCull()
        {
            if (_preCullHooked) return;
            Camera.onPreCull += OnWorldCanvasPreCull;
            _preCullHooked = true;
        }

        private static void UnhookWorldCanvasPreCull()
        {
            if (!_preCullHooked) return;
            Camera.onPreCull -= OnWorldCanvasPreCull;
            _preCullHooked = false;
        }

        // Fires for EVERY camera (the vanilla portrait cam, our dome _rtCam, reflection probes);
        // only the game's main camera — the one about to render the world canvas — drives the
        // head-lock, and only while mode C is live. Guards are load-bearing: UpdateWorldCanvasPose
        // does not null-check _canvas, and a throw here would break the game's render loop.
        private static void OnWorldCanvasPreCull(Camera cam)
        {
            if (_canvas == null
                || _appliedMode != HudCurvature.CurvedWorldCanvas
                || !_canvas.gameObject.activeInHierarchy) return;
            Camera main = null;
            try { main = CameraController.CurrentCamera; } catch { }
            if (cam != main) return;
            try { UpdateWorldCanvasPose(); } catch { }
        }

        private static void ApplyWorldMaterials()
        {
            if (_worldMatsApplied || _canvas == null) return;
            try
            {
                if (_ztestMat == null)
                {
                    var sh = Shader.Find("UI/Default");
                    if (sh == null) return;
                    _ztestMat = new Material(sh) { renderQueue = 4000 };
                    _ztestMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
                }
                var overlayShader = Shader.Find("TextMeshPro/Distance Field Overlay"); // ships with the game
                foreach (var g in _canvas.GetComponentsInChildren<Graphic>(true))
                {
                    // Anything under a UGUI Mask (the round portrait) KEEPS its default
                    // material — our z-test material carries no stencil op, so swapping it
                    // killed the circular clip in mode C (play-test: "the 3d guy no longer
                    // is getting clipped by the circle in C mode").
                    if (g.GetComponentInParent<UnityEngine.UI.Mask>() != null) continue;

                    var tmp = g as TextMeshProUGUI;
                    if (tmp != null)
                    {
                        if (overlayShader == null) continue;
                        var orig = tmp.fontSharedMaterial;
                        if (orig == null) continue;
                        if (!_tmpOriginalMats.ContainsKey(tmp)) _tmpOriginalMats[tmp] = orig;
                        Material overlay;
                        if (!_overlayFontMats.TryGetValue(orig, out overlay) || overlay == null)
                        {
                            overlay = new Material(orig) { shader = overlayShader, renderQueue = 4001 };
                            _overlayFontMats[orig] = overlay;
                        }
                        tmp.fontSharedMaterial = overlay;
                    }
                    else
                    {
                        g.material = _ztestMat;
                    }
                }
                _worldMatsApplied = true;
            }
            catch (Exception e)
            {
                UIALog.Warn("CurvedWorldCanvas materials: " + e.Message);
            }
        }

        private static void RestoreWorldMaterials()
        {
            if (!_worldMatsApplied) return;
            _worldMatsApplied = false;
            try
            {
                foreach (var kv in _tmpOriginalMats)
                    if (kv.Key != null && kv.Value != null)
                        kv.Key.fontSharedMaterial = kv.Value;
                _tmpOriginalMats.Clear();
                if (_canvas != null)
                    foreach (var g in _canvas.GetComponentsInChildren<Graphic>(true))
                        if (!(g is TextMeshProUGUI))
                            g.material = null;
            }
            catch { }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>After a warp/layout change every mesh must rebuild (position-dependent
        /// warp offsets are baked into vertices).</summary>
        private static void DirtyAllMeshes()
        {
            if (_canvas == null) return;
            foreach (var g in _canvas.GetComponentsInChildren<Graphic>(true))
            {
                var tmp = g as TextMeshProUGUI;
                if (tmp != null) tmp.SetAllDirty();
                else g.SetVerticesDirty();
            }
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------ editor

        public static void CollectEditTargets(List<HudEditTarget> into)
        {
            float scale = HudConfig.HudScale.Value;
            foreach (var p in _panels)
            {
                try { p.CollectEditTargets(into, scale); } catch { }
            }
            into.Add(new HudEditTarget
            {
                Title = "Vignette / screen",
                Palette = new[] { "HudVignette", "HudScanline" },
                Values = new ConfigEntryBase[] { HudConfig.ShowVignette, HudConfig.Curvature,
                    HudConfig.CurveStrength, HudConfig.CurveInvert,
                    HudConfig.WorldCanvasDistance, HudConfig.HudScale },
                CanvasRect = new Rect(-HudWarp.HalfW, -HudWarp.HalfH, 60f, HudWarp.HalfH * 2f),
            });
        }

        /// <summary>The live document views, for the designer's hit-testing/selection.
        /// Empty when document mode is off or nothing is built.</summary>
        internal static void CollectElementViews(List<HudElementView> into)
        {
            if (_canvas == null) return;
            foreach (var p in _panels)
            {
                var v = p as HudElementView;
                if (v != null && v.Def != null) into.Add(v);
            }
        }

        /// <summary>Targeted relayout for a live drag: reposition ONE element and re-warp
        /// only its own meshes — a full RelayoutAll+DirtyAllMeshes per dragged frame would
        /// rebuild the whole canvas. The drag's single MarkChanged on release does the
        /// full pass (store version feeds the layout hash).</summary>
        internal static void RelayoutElement(HudElementView view)
        {
            if (view == null || view.Root == null) return;
            try
            {
                view.Layout(HudConfig.HudScale.Value);
                foreach (var g in view.Root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                {
                    var tmp = g as TMPro.TextMeshProUGUI;
                    if (tmp != null) tmp.SetAllDirty();
                    else g.SetVerticesDirty();
                }
            }
            catch { }
        }

        /// <summary>Chip drop targets currently on screen (0.6.2): the hand boxes and the
        /// six equipment boxes, canvas coords. Empty when the visor HUD is off, hidden, or
        /// a panel is faded out (its zones must not be invisible-but-active).</summary>
        public static void CollectDropZones(List<HudDropZone> into)
        {
            if (_canvas == null || !_canvas.gameObject.activeSelf) return;
            // CurvedWorldCanvas draws with real 3D perspective — flat canvas rects can
            // shift 60-150px from where boxes render (review finding), which would send
            // drops to the WRONG slot. No zones there; drops park as before.
            if (HudConfig.Curvature != null
                && HudConfig.Curvature.Value == HudCurvature.CurvedWorldCanvas) return;
            var snap = LastSnapshot;
            if (snap == null || !snap.Valid) return;
            float scale = HudConfig.HudScale.Value;
            foreach (var p in _panels)
            {
                try
                {
                    if (p.Group == null || p.Group.alpha < 0.5f || !p.Root.gameObject.activeSelf)
                        continue;
                    p.CollectDropZones(into, snap, scale);
                }
                catch { }
            }
        }

        private static readonly List<HudDropZone> _zoneScratch = new List<HudDropZone>();

        /// <summary>The HUD box (hand / equipment slot) under the current mouse position, or
        /// null. Shared by the radial drag layer so a HUD box is BOTH a drop target and a drag
        /// SOURCE — mouse coords are inverse-warped exactly like the F9 editor's hit-testing so
        /// curved-HUD boxes are grabbed where they DRAW. Returns null off any box (or when the
        /// visor HUD is off / a panel is faded).</summary>
        public static HudDropZone ZoneAt()
        {
            try
            {
                _zoneScratch.Clear();
                CollectDropZones(_zoneScratch);
                if (_zoneScratch.Count == 0) return null;
                var m = (Vector2)Input.mousePosition;   // bottom-left origin, y up
                var p = new Vector2(m.x - Screen.width * 0.5f, m.y - Screen.height * 0.5f);
                p = HudWarp.Unwarp(p);
                foreach (var zone in _zoneScratch)
                    if (zone.Slot != null && zone.CanvasRect.Contains(p))
                        return zone;
            }
            catch { }
            return null;
        }

        // ---------- vanilla panel visibility (hide, never destroy) ----------
        // Reconciles against the panels' ACTUAL active state every frame:
        // - Hide* on + panel visible  -> hide it (also re-hides after vanilla's
        //   unconscious watcher re-shows everything on wake).
        // - Hide* off + we had hidden -> restore it exactly once.
        // - Unmanaged panels are passed through with their CURRENT state, so we never
        //   force-show something vanilla itself hid (sleep, character customisation) —
        //   and a fresh world with nothing to hide makes NO call at all (no HUD sounds).

        public static void SyncVanillaVisibility()
        {
            try
            {
                var m = InventoryManager.Instance;
                if (m == null) return;
                var hands = m.PanelHandsGameObject;
                var clothing = m.ClothingPanel != null ? m.ClothingPanel.gameObject : null;
                var status = m.StatusPanel;
                bool actHands = hands == null || hands.activeSelf;
                bool actClothing = clothing == null || clothing.activeSelf;
                bool actStatus = status == null || status.activeSelf;

                bool hideHands = UIAConfig.HideVanillaHands.Value;
                bool hideClothing = UIAConfig.HideVanillaClothing.Value;
                bool hideStatus = UIAConfig.HideVanillaStatus.Value;

                bool need = (hideHands ? actHands : _restoreHands)
                    || (hideClothing ? actClothing : _restoreClothing)
                    || (hideStatus ? actStatus : _restoreStatus);
                if (!need)
                {
                    _restoreHands = hideHands;
                    _restoreClothing = hideClothing;
                    _restoreStatus = hideStatus;
                    return;
                }

                m.StartCoroutine(m.SetUIPanelVisibility(
                    hideHands ? false : (_restoreHands ? true : actHands),
                    hideClothing ? false : (_restoreClothing ? true : actClothing),
                    hideStatus ? false : (_restoreStatus ? true : actStatus), 0f));
                _restoreHands = hideHands;
                _restoreClothing = hideClothing;
                _restoreStatus = hideStatus;
            }
            catch (Exception e)
            {
                UIALog.Warn("SetUIPanelVisibility failed: " + e.Message);
            }

            SyncPlayerStateCluster();
        }
        // (The vanilla moodlet strip is now RELOCATED into our bar by MoodletBorrowWidget —
        // reparenting moves it off vanilla's spot, so no separate alpha-hide is needed.)

        private static bool _restorePlayerState;
        private static int _playerStateWarns;

        /// <summary>True while the vanilla instrument cluster is being hidden. Read by the
        /// Harmony prefix that skips PlayerStateWindow.UpdateJetpackPanels — vanilla
        /// re-SHOWS InfoJetpack every frame a jetpack is worn, and reconciling against
        /// that means both sides rewrite the panel's alpha every frame (mesh churn and a
        /// hide that never sticks). Skipping vanilla's re-show is display-only and stops
        /// the moment the toggle clears.</summary>
        internal static bool PlayerStateClusterHidden;

        /// <summary>The vanilla bottom-right instrument cluster (internal/external/jetpack/
        /// health boxes on PlayerStateWindow) — hidden through the game's OWN alpha path
        /// (UserInterfaceBase.SetVisible), reconciled against actual state every frame
        /// because vanilla re-asserts the jetpack panel itself on gear changes. Restored
        /// once when the toggle clears; vanilla's own update re-settles the right states.</summary>
        private static void SyncPlayerStateCluster()
        {
            try
            {
                var psw = Assets.Scripts.UI.PlayerStateWindow.Instance;
                if (psw == null) return;
                bool hide = UIAConfig.HideVanillaPlayerState != null
                    && UIAConfig.HideVanillaPlayerState.Value;
                if (hide)
                {
                    PlayerStateClusterHidden = true; // arms the UpdateJetpackPanels skip
                    HideCluster(psw.InfoInternal);
                    HideCluster(psw.InfoExternal);
                    HideCluster(psw.InfoJetpack);
                    HideCluster(psw.InfoHealth);
                    // InfoHealth is UNASSIGNED in the shipped scene (fileID 0), so the
                    // vanilla vitals list (VitalsObject/PanelHealth) leaked through the
                    // panel path above — alpha it out via a CanvasGroup that vanilla's
                    // per-frame SetActive can't fight.
                    SetVitalsAlpha(psw, 0f);
                    _restorePlayerState = true;
                }
                else if (_restorePlayerState)
                {
                    PlayerStateClusterHidden = false;
                    _restorePlayerState = false;
                    ShowCluster(psw.InfoInternal);
                    ShowCluster(psw.InfoExternal);
                    ShowCluster(psw.InfoJetpack);
                    ShowCluster(psw.InfoHealth);
                    SetVitalsAlpha(psw, 1f);
                }
                else
                {
                    PlayerStateClusterHidden = false;
                }
            }
            catch (Exception e)
            {
                if (++_playerStateWarns <= 3)
                    UIALog.Warn("PlayerState cluster visibility failed: " + e.Message
                        + (_playerStateWarns == 3 ? " (further errors suppressed)" : ""));
            }
        }

        private static void HideCluster(Assets.Scripts.UI.UserInterfaceBase p)
        {
            try { if (p != null && p.IsVisible) p.SetVisible(false); } catch { }
        }

        private static void ShowCluster(Assets.Scripts.UI.UserInterfaceBase p)
        {
            try { if (p != null && !p.IsVisible) p.SetVisible(true); } catch { }
        }

        /// <summary>Alpha the vanilla vitals list (VitalsObject) via a CanvasGroup —
        /// vanilla SetActives it per frame, but never touches a CanvasGroup, so alpha 0
        /// sticks while every child stays readable. Idempotent per-frame call.</summary>
        private static void SetVitalsAlpha(Assets.Scripts.UI.PlayerStateWindow psw, float alpha)
        {
            try
            {
                var go = psw.VitalsObject;
                if (go == null) return;
                var grp = go.GetComponent<UnityEngine.CanvasGroup>()
                    ?? go.AddComponent<UnityEngine.CanvasGroup>();
                if (!Mathf.Approximately(grp.alpha, alpha)) grp.alpha = alpha;
            }
            catch { }
        }

        public static void RestoreVanillaIfNeeded()
        {
            // The borrowed moodlet strip is handed back by RestoreAnyPortraits (called first
            // on every teardown), so nothing to undo here.

            // The instrument cluster restores independently of the three big panels.
            PlayerStateClusterHidden = false;
            if (_restorePlayerState)
            {
                _restorePlayerState = false;
                try
                {
                    var psw = Assets.Scripts.UI.PlayerStateWindow.Instance;
                    if (psw != null)
                    {
                        ShowCluster(psw.InfoInternal);
                        ShowCluster(psw.InfoExternal);
                        ShowCluster(psw.InfoJetpack);
                        ShowCluster(psw.InfoHealth);
                        SetVitalsAlpha(psw, 1f);
                    }
                }
                catch { }
            }

            if (!_restoreHands && !_restoreClothing && !_restoreStatus) return;
            bool hands = _restoreHands, clothing = _restoreClothing, status = _restoreStatus;
            _restoreHands = _restoreClothing = _restoreStatus = false;
            try
            {
                var m = InventoryManager.Instance;
                if (m == null) return;
                var clothingGo = m.ClothingPanel != null ? m.ClothingPanel.gameObject : null;
                m.StartCoroutine(m.SetUIPanelVisibility(
                    hands || m.PanelHandsGameObject == null || m.PanelHandsGameObject.activeSelf,
                    clothing || clothingGo == null || clothingGo.activeSelf,
                    status || m.StatusPanel == null || m.StatusPanel.activeSelf, 0f));
            }
            catch (Exception e)
            {
                UIALog.Warn("SetUIPanelVisibility restore failed: " + e.Message);
            }
        }
    }
}
