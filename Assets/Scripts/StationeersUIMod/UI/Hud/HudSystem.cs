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

        private static void EnsureBuilt()
        {
            if (_canvas != null) return;

            var go = new GameObject("UIAscended_HudCanvas");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.layer = HudLayerUi;
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 3800; // above vanilla HUD (0), under radials (5000)
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
                Features.HudProfileStore.LoadActive(name, BuildStarterDocument);

                // A stale shipped-default (the schema-1 primitive demo) upgrades to the
                // current starter — but ONLY the literal "Default" profile; anything the
                // user named themselves is theirs, whatever its age.
                var active = Features.HudProfileStore.Active;
                if (active != null && active.Schema < 3
                    && string.Equals(name, "Default", System.StringComparison.OrdinalIgnoreCase))
                {
                    var fresh = BuildStarterDocument();
                    Features.HudProfileStore.SetActive(fresh, name);
                    Features.HudProfileStore.MarkChanged(); // persist the upgrade
                }
                _docRebuildNeeded = false; // SetActive fired the event; we build right after
            }
        }

        private static void OnActiveDocReplaced() => _docRebuildNeeded = true;

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
                case HudElementType.MoodletDashboard: return new Widgets.MoodletDashboardWidget();
                case HudElementType.BodyDoll: return new Widgets.BodyDollWidget();
                case HudElementType.SuitChips: return new Widgets.SuitChipsWidget();
                case HudElementType.EquipmentColumn: return new Widgets.EquipmentColumnWidget();
                case HudElementType.HandBoxes: return new Widgets.HandBoxesWidget();
                case HudElementType.KeybindChips: return new Widgets.KeybindChipsWidget();
                case HudElementType.BareSenses: return new Widgets.BareSensesWidget();
                case HudElementType.Portrait: return new Widgets.PortraitWidget();
                case HudElementType.Readout: return new Widgets.ReadoutWidget();
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
                if (pw == null) continue;
                try { pw.RestorePortrait(); } catch { }
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

        /// <summary>The shipped default layout: the 0.5.0 visor arrangement rebuilt from
        /// widgets — the parity baseline every element of which can now be moved, resized,
        /// restyled or deleted in the designer. Written to HudProfiles/Default.xml on
        /// first run (and whenever the file goes missing).</summary>
        private static HudDocument BuildStarterDocument()
        {
            // Schema 3 = the concept-art layout (1 was the primitive demo, 2 the plain
            // parity port; EnsureActiveDocument silently upgrades a stale shipped Default).
            var doc = new HudDocument { Name = "Default", Schema = 3 };
            var els = doc.Elements;

            // --- top bar: backdrop + badge, clock, external cells, compass, day, world ---
            var bar = El("top-bar", HudElementType.Box, HudAnchor.TopCenter, 0f, -44f, 1840f, 66f, SuitOnly, 0);
            bar.WPct = 0.96f; // span ~the screen at ANY resolution, not just 1920
            bar.RTL = 4f; bar.RTR = 4f; bar.RBR = 16f; bar.RBL = 16f;
            els.Add(bar);
            els.Add(El("hand-badge", HudElementType.ActiveHandBadge, HudAnchor.TopLeft, 70f, -44f, 44f, 36f));
            els.Add(El("clock", HudElementType.Clock, HudAnchor.TopLeft, 185f, -44f, 190f, 40f, SuitOnly));

            var extP = El("ext-pressure", HudElementType.Readout, HudAnchor.TopCenter, -330f, -44f, 200f, 56f, SuitOnly);
            extP.Set("src", "ExternalPressure"); extP.Set("label", "EXTERNAL");
            extP.SetB("box", false); extP.SetB("bar", false);
            els.Add(extP);

            els.Add(El("compass", HudElementType.Compass, HudAnchor.TopCenter, 0f, -44f, 240f, 46f, SuitOnly));

            var extT = El("ext-temp", HudElementType.Readout, HudAnchor.TopCenter, 330f, -44f, 200f, 56f, SuitOnly);
            extT.Set("src", "ExternalTemp"); extT.Set("label", "EXTERNAL");
            extT.SetB("box", false); extT.SetB("bar", false);
            els.Add(extT);

            els.Add(El("day", HudElementType.DayCounter, HudAnchor.TopRight, -270f, -44f, 140f, 40f, SuitOnly));
            els.Add(El("world", HudElementType.WorldName, HudAnchor.TopRight, -95f, -44f, 150f, 40f, SuitOnly));

            // --- left equipment column, bottom hand tray + key chips ---
            els.Add(El("equipment", HudElementType.EquipmentColumn, HudAnchor.MiddleLeft, 70f, 0f, 84f, 520f));
            els.Add(El("hands", HudElementType.HandBoxes, HudAnchor.BottomCenter, 0f, 78f, 400f, 126f));
            var chips = El("key-chips", HudElementType.KeybindChips, HudAnchor.BottomCenter, -320f, 66f, 110f, 96f);
            chips.SetB("vertical", true);
            els.Add(chips);

            // --- bottom-right vitals: four readout rows + the round hologram portrait ---
            // --- moodlet dashboard: chips center-out under the top bar, like a car ---
            els.Add(El("moodlets", HudElementType.MoodletDashboard, HudAnchor.TopCenter, 0f, -104f, 760f, 36f));

            // --- vertical instrument cards (the concept's gauge columns) ---
            // Bottom-left pair: the outside world.
            AddGaugeCard(els, "card-ext-pressure", "ExternalPressure", "EXT PRESS", "Gauge", -1, 62f, 200f);
            AddGaugeCard(els, "card-ext-temp", "ExternalTemp", "EXT TEMP", "Thermometer", -1, 158f, 200f);
            // Bottom-right trio: the suit's own instruments.
            AddGaugeCard(els, "card-int-pressure", "InternalPressure", "INTERNAL\nPRESSURE", "Gauge", 1, -560f, 175f);
            AddGaugeCard(els, "card-int-temp", "InternalTemp", "INTERNAL\nTEMP", "Thermometer", 1, -455f, 175f);
            AddGaugeCard(els, "card-jetpack", "JetpackPropellant", "JETPACK", "Jetpack", 1, -350f, 175f);

            // --- needs pills + suit chips + the round hologram + the damage doll ---
            var hyd = El("pill-hydration", HudElementType.Readout, HudAnchor.BottomRight, -430f, 40f, 120f, 40f, SuitOnly);
            hyd.Set("src", "Hydration"); hyd.Set("label", ""); hyd.SetB("bar", false);
            hyd.Icon = "Droplet"; hyd.SetF("valueSize", 15f);
            els.Add(hyd);
            var san = El("pill-sanitation", HudElementType.Readout, HudAnchor.BottomRight, -300f, 40f, 120f, 40f, SuitOnly);
            san.Set("src", "Sanitation"); san.Set("label", ""); san.SetB("bar", false);
            san.Icon = "Toilet"; san.SetF("valueSize", 15f);
            els.Add(san);

            els.Add(El("suit-chips", HudElementType.SuitChips, HudAnchor.BottomRight, -560f, 40f, 170f, 44f, SuitOnly));
            els.Add(El("portrait", HudElementType.Portrait, HudAnchor.BottomRight, -105f, 110f, 160f, 160f, SuitOnly));
            els.Add(El("body-doll", HudElementType.BodyDoll, HudAnchor.BottomRight, -205f, 110f, 74f, 165f));

            // A compact power+health pair rides over the portrait's shoulder — the doll
            // and moodlets carry the story, these carry the numbers.
            var pow = El("pill-power", HudElementType.Readout, HudAnchor.BottomRight, -105f, 215f, 150f, 36f, SuitOnly);
            pow.Set("src", "SuitPower"); pow.Set("label", ""); pow.SetB("bar", true); pow.SetB("box", false);
            pow.Icon = "Bolt"; pow.SetF("valueSize", 14f);
            els.Add(pow);

            // --- bare tier: the felt-sense words own the middle of the view ---
            els.Add(El("bare-senses", HudElementType.BareSenses, HudAnchor.Center, 0f, -40f, 420f, 320f, HudTierMask.Bare));

            return doc;
        }

        /// <summary>One of the concept art's VERTICAL instrument cards: boxed, icon at the
        /// bottom, big value + target line, upright threshold bar. side −1 anchors
        /// bottom-left, +1 bottom-right (x is the offset from that corner).</summary>
        private static void AddGaugeCard(List<HudElementDef> els, string id, string src,
            string label, string icon, int side, float x, float h)
        {
            var e = El(id, HudElementType.Readout,
                side < 0 ? HudAnchor.BottomLeft : HudAnchor.BottomRight,
                x, 30f + h * 0.5f, 92f, h, SuitOnly);
            e.Set("src", src);
            e.Set("label", label);
            e.SetB("box", true);
            e.SetB("bar", true);
            e.SetB("barVertical", true);
            e.Icon = icon;
            e.SetF("valueSize", 15f);
            els.Add(e);
        }

        public static void Shutdown()
        {
            RestoreAnyPortraits();
            RestoreVanillaIfNeeded();
            RestoreWorldMaterials();
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

            var snap = HudSampler.Sample();
            LastSnapshot = snap;
            if (!snap.Valid) return;

            HudTier tier = ForceTier ?? snap.Tier;

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

            if (mode != _appliedMode)
            {
                // Tear down the old mode.
                if (_appliedMode == HudCurvature.CurvedWorldCanvas) RestoreWorldMaterials();
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
            float dist = HudConfig.WorldCanvasDistance.Value;
            var rt = (RectTransform)_canvas.transform;
            rt.sizeDelta = new Vector2(Screen.width, Screen.height);
            var t = cam.transform;
            rt.SetPositionAndRotation(t.position + t.rotation * new Vector3(0f, 0f, dist), t.rotation);
            float worldH = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float s = worldH / Mathf.Max(1f, Screen.height);
            rt.localScale = new Vector3(s, s, s);
            _canvas.worldCamera = cam;
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

        private static bool _restorePlayerState;
        private static int _playerStateWarns;

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
                    HideCluster(psw.InfoInternal);
                    HideCluster(psw.InfoExternal);
                    HideCluster(psw.InfoJetpack);
                    HideCluster(psw.InfoHealth);
                    _restorePlayerState = true;
                }
                else if (_restorePlayerState)
                {
                    _restorePlayerState = false;
                    ShowCluster(psw.InfoInternal);
                    ShowCluster(psw.InfoExternal);
                    ShowCluster(psw.InfoJetpack);
                    ShowCluster(psw.InfoHealth);
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

        public static void RestoreVanillaIfNeeded()
        {
            // The instrument cluster restores independently of the three big panels.
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
