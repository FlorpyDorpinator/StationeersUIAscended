using System.Collections.Generic;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using StationeersUIMod.UI.Hud;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>
    /// The tutorial's DEMO STAGE (plan A.9): a self-contained RectTransform that plays one looping,
    /// purely schematic mock per lesson step. It NEVER touches the real radial, the Universal
    /// Inventory, or any game state — every shape here is a throwaway <see cref="PanelGraphic"/> /
    /// <see cref="RadialWedgeGraphic"/> / <see cref="CircleGraphic"/> / <see cref="TriangleGraphic"/> /
    /// TMP label built at scene-build time and merely MOVED, SCALED, ROTATED and TINTED afterwards.
    ///
    /// <para>One instance per host: the coach card's demo area (FULL layout, authored in a 440x220
    /// box) and the lesson strip's slot (MINI layout, authored in a 150x76 box: big shapes and no
    /// words except keycaps — full-layout labels would shrink to ~3 px there). Both boxes are
    /// uniformly fitted to whatever rect the host hands over (<see cref="FitStage"/>).</para>
    ///
    /// <para>Ids: every scene id in plan A.9 (see <see cref="SceneFor"/>), the legacy 0.9.7 step ids,
    /// and the parameterised <c>keys:&lt;pattern&gt;:&lt;token&gt;</c> micro-scene (patterns tap, hold,
    /// double, chord, lmb, rmb, mmb, scroll, drag, shiftdrag). <see cref="IsKnownDemo"/> answers for
    /// all of them. An unknown id shows a blank stage and never throws.</para>
    ///
    /// Contracts this file keeps (all load-bearing):
    /// <list type="bullet">
    /// <item>All clocks are <see cref="Time.unscaledTime"/> — the game is PAUSED behind a card, so
    /// <c>Time.time</c> would freeze every animation.</item>
    /// <item><see cref="Tick"/> allocates NOTHING. Every graphic, transform and displayed string is
    /// built at scene build; ticking only writes anchoredPosition / localScale / localEulerAngles /
    /// color / CanvasGroup.alpha / dirty-guarded geometry. No LINQ, no lambdas, no string building.
    /// Text swaps are guarded by a cached index.</item>
    /// <item>Every key a scene SHOWS comes from the host's glyph resolver at build (the player's live
    /// binding) — never a hard-coded "Alt" / "B" / "G": a demo must not lie after a rebind. Keys the
    /// plan writes literally (Shift, Ctrl, RMB, in-wheel digits) stay literal.</item>
    /// <item>Curved words (the action word, the hint ring, corner tags) are bent ONCE at build with
    /// <see cref="RadialArcText.Curve"/> and only re-bent if TMP rebuilt the mesh flat (a host
    /// re-enable), through the same allocation-free <see cref="ArcBendCache"/> the real wheel uses.
    /// They are faded through CanvasGroups only — a TMP colour write would regenerate them flat.</item>
    /// <item>ASCII only in displayed strings (the TMP tofu rule). Chevrons, crosses, plus signs,
    /// cursors, mice and ticks are DRAWN, never typed.</item>
    /// <item>No mutable statics — a hot reload leaves nothing behind, and <see cref="Destroy"/>
    /// destroys every GameObject this stage created.</item>
    /// <item>Colours come from <see cref="UiaTheme"/> (captured per scene build), so the demos re-skin
    /// with the active theme exactly like the rest of the menu. The few fixed mock colours (a greyed
    /// wedge, bag paint, the other theme's palette...) are demo CONTENT, each marked "Rule-8 content
    /// exemption" at its site.</item>
    /// </list>
    ///
    /// Scenes are deliberately SCHEMATIC — clean mock shapes rather than pixel replicas — so a
    /// cosmetic UI change never invalidates them. Loops run 2-12 s. The scenes live in the partial
    /// files TutorialDemoScenes*.cs; this file holds the stage, the toolkit and the legacy scenes.
    /// </summary>
    internal sealed partial class TutorialDemoStage
    {
        // ---- design space -------------------------------------------------------------------
        // FULL scenes are authored in a fixed 440x220 box, MINI scenes in the strip's 150x76 slot,
        // both centred on (0,0) and uniformly scaled to fit whatever rect the host provides, so a
        // card or strip resize never re-lays anything.
        private const float DesignW = 440f;
        private const float DesignH = 220f;
        private const float MiniW = 150f;
        private const float MiniH = 76f;

        private RectTransform _root;    // fills the parent
        private RectTransform _stage;   // design space, uniformly scaled
        private RectTransform _sceneRoot;
        private IDemoScene _scene;
        private Ctx _ctx;
        private string _sceneId;        // normalised id of the scene on screen (kept after a failure)
        private bool _sceneMini;
        private System.Func<string, string> _sceneGlyph;
        private string _lastRaw;        // the raw id reference last passed to Show (zero-alloc no-op)
        private float _t0;
        private float _lastW, _lastH;
        private float _designW = DesignW, _designH = DesignH;

        /// <summary>The default glyph resolver (contract: <c>Show(id)</c> =
        /// <c>Show(id, TutorialTokens.Glyph, false)</c>). Cached once so the legacy overload never
        /// allocates a delegate per call; immutable, so hot reload is unaffected.</summary>
        private static readonly System.Func<string, string> DefaultGlyph = TokenGlyph;

        private static string TokenGlyph(string token) => TutorialTokens.Glyph(token);

        private TutorialDemoStage() { }

        /// <summary>Build a stage that fills <paramref name="parent"/>. Never throws; a null parent
        /// just yields a detached (harmless) stage.</summary>
        internal static TutorialDemoStage Create(RectTransform parent)
        {
            var stage = new TutorialDemoStage();
            var rootGo = new GameObject("UiaTutorialDemoStage", typeof(RectTransform));
            rootGo.transform.SetParent(parent, false);
            var root = (RectTransform)rootGo.transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            stage._root = root;

            var stageGo = new GameObject("scenes", typeof(RectTransform));
            stageGo.transform.SetParent(root, false);
            var st = (RectTransform)stageGo.transform;
            st.anchorMin = st.anchorMax = new Vector2(0.5f, 0.5f);
            st.pivot = new Vector2(0.5f, 0.5f);
            st.sizeDelta = new Vector2(DesignW, DesignH);
            st.anchoredPosition = Vector2.zero;
            stage._stage = st;
            return stage;
        }

        /// <summary>Legacy entry: the FULL layout with the live key glyphs of
        /// <see cref="TutorialTokens.Glyph"/>.</summary>
        internal void Show(string demoId) => Show(demoId, DefaultGlyph, false);

        /// <summary>Switch to a demo. <paramref name="glyph"/> turns a key token ("{V:MouseControl}",
        /// "{UIA_Grid}") into the player's CURRENT key; null = <see cref="TutorialTokens.Glyph"/>.
        /// <paramref name="mini"/> = the strip's 150x76 layout. Scene ids are matched
        /// case/space/underscore/hyphen-insensitively; a <c>keys:</c> id keeps its token verbatim.
        /// Re-showing the scene already on screen (same id, layout and resolver) is a deliberate
        /// NO-OP — the loop keeps running, so a host that calls this every frame never thrashes the
        /// scene graph (and the same raw string reference costs zero allocation). Key glyphs are
        /// resolved when a scene is BUILT; a rebind lands on the next scene change. An unknown id
        /// shows a blank stage — this method never throws.</summary>
        internal void Show(string demoId, System.Func<string, string> glyph, bool mini)
        {
            if (_root == null) return;
            if (glyph == null) glyph = DefaultGlyph;
            if (_lastRaw != null && ReferenceEquals(demoId, _lastRaw) && mini == _sceneMini && glyph == _sceneGlyph)
                return;

            string pattern, token;
            bool isKeys = TryParseKeys(demoId, out pattern, out token);
            string key = isKeys ? "keys:" + pattern + ":" + (token ?? "") : Normalize(demoId);
            _lastRaw = demoId;
            // Also a no-op when the previous build/tick of this id gave up: a broken demo must
            // never turn into a rebuild-and-throw loop.
            if (key == _sceneId && mini == _sceneMini && glyph == _sceneGlyph) return;

            DestroyScene();
            _sceneId = key;
            _sceneMini = mini;
            _sceneGlyph = glyph;
            _t0 = Time.unscaledTime;

            IDemoScene scene = null;
            if (isKeys) { if (KeysScene.Known(pattern, token)) scene = new KeysScene(); }
            else scene = SceneFor(key);
            if (scene == null) scene = new EmptyScene();
            // A card-only scene asked for in a mini slot keeps its FULL layout, uniformly shrunk into
            // the slot: small, but contained (a 440x220 layout in the 150x76 box would spill out).
            bool useMini = mini && scene.SupportsMini;

            _designW = useMini ? MiniW : DesignW;
            _designH = useMini ? MiniH : DesignH;
            _stage.sizeDelta = new Vector2(_designW, _designH);
            _lastW = _lastH = 0f;   // the design box changed: refit on the next tick

            var go = new GameObject("scene", typeof(RectTransform));
            go.transform.SetParent(_stage, false);
            _sceneRoot = (RectTransform)go.transform;
            _sceneRoot.anchorMin = _sceneRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _sceneRoot.pivot = new Vector2(0.5f, 0.5f);
            _sceneRoot.sizeDelta = new Vector2(_designW, _designH);
            _sceneRoot.anchoredPosition = Vector2.zero;

            _ctx = new Ctx { Glyph = glyph, Mini = useMini, Pattern = pattern, Token = token };
            try
            {
                scene.Build(_sceneRoot, _ctx);
                _scene = scene;
                FitStage();
            }
            catch (System.Exception e)
            {
                UIALog.Warn("TutorialDemoStage: demo '" + key + "' failed to build: " + e.Message);
                try { scene.Destroy(); } catch { }
                Object.Destroy(_sceneRoot.gameObject);
                _sceneRoot = null;
                _scene = null;
                _ctx = null;
            }
        }

        /// <summary>Advance the looping animation. Call every frame while the host is open.</summary>
        internal void Tick()
        {
            if (_root == null || _scene == null) return;
            FitStage();
            try
            {
                _scene.Tick(Time.unscaledTime - _t0);
                // Curved words: a steady label costs a few compares; one that TMP rebuilt flat (a
                // host re-enable) is bent again — allocation-free (ArcBendCache).
                if (_ctx != null)
                {
                    var curves = _ctx.Curves;
                    for (int i = 0; i < curves.Count; i++) curves[i].Service();
                }
            }
            catch (System.Exception e)
            {
                UIALog.Warn("TutorialDemoStage: demo '" + _sceneId + "' stopped: " + e.Message);
                DestroyScene();
            }
        }

        /// <summary>Tear the whole stage down. Safe to call twice.</summary>
        internal void Destroy()
        {
            DestroyScene();
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
            _stage = null;
            _sceneId = null;
            _sceneGlyph = null;
            _lastRaw = null;
            _lastW = _lastH = 0f;
        }

        /// <summary>Drops the live scene but deliberately KEEPS <see cref="_sceneId"/> — see the
        /// no-op guard in <see cref="Show(string, System.Func{string, string}, bool)"/>.
        /// <see cref="Destroy"/> clears the id itself.</summary>
        private void DestroyScene()
        {
            if (_scene != null) { try { _scene.Destroy(); } catch { } _scene = null; }
            if (_sceneRoot != null) { Object.Destroy(_sceneRoot.gameObject); _sceneRoot = null; }
            _ctx = null;
        }

        /// <summary>Uniform fit of the design box into the host rect. Only recomputed when the host
        /// actually changes size (layout can resolve a frame or two after Build).</summary>
        private void FitStage()
        {
            Rect r = _root.rect;
            if (r.width < 2f || r.height < 2f) return;
            if (Mathf.Approximately(r.width, _lastW) && Mathf.Approximately(r.height, _lastH)) return;
            _lastW = r.width;
            _lastH = r.height;
            float k = Mathf.Clamp(Mathf.Min(r.width / _designW, r.height / _designH), 0.15f, 3f);
            _stage.localScale = new Vector3(k, k, 1f);
        }

        // =====================================================================================
        //  Ids
        // =====================================================================================

        /// <summary>True for every id <see cref="Show(string, System.Func{string, string}, bool)"/>
        /// draws (not blank): the A.9 scene ids and their aliases, the legacy 0.9.7 step ids, and
        /// <c>keys:&lt;pattern&gt;[:&lt;token&gt;]</c> with a known pattern. The key patterns tap /
        /// hold / double / chord need a token; the mouse patterns take an optional one (a key held
        /// during the gesture). For the lesson lint and the editor; not a per-frame call.</summary>
        internal static bool IsKnownDemo(string demoId)
        {
            if (string.IsNullOrEmpty(demoId)) return false;
            string pattern, token;
            if (TryParseKeys(demoId, out pattern, out token)) return KeysScene.Known(pattern, token);
            return SceneFor(Normalize(demoId)) != null;
        }

        private static string Normalize(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            return id.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Split <c>keys:&lt;pattern&gt;[:&lt;token&gt;]</c>. The pattern is lower-cased; the
        /// token is kept verbatim (it may itself contain ':' — "{V:MouseControl}" — so only the
        /// FIRST colon after the pattern splits).</summary>
        private static bool TryParseKeys(string id, out string pattern, out string token)
        {
            pattern = null;
            token = null;
            if (id == null) return false;
            string s = id.Trim();
            if (s.Length < 5 || string.Compare(s, 0, "keys:", 0, 5, System.StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            int c = s.IndexOf(':', 5);
            pattern = (c < 0 ? s.Substring(5) : s.Substring(5, c - 5)).Trim().ToLowerInvariant();
            token = c < 0 ? null : s.Substring(c + 1).Trim();
            if (token != null && token.Length == 0) token = null;
            return true;
        }

        /// <summary>The scene registry. Null = unknown (the stage then shows a blank).</summary>
        private static IDemoScene SceneFor(string key)
        {
            switch (key)
            {
                // ---- cards ----
                case "welcome":
                case "static.welcome":
                case "staticwelcome": return new WelcomeScene();
                case "finish":
                case "static.finish":
                case "staticfinish": return new FinishScene(-1);
                case "designer": return new FinishScene(2);          // 17.1: only the F9 row pulses
                case "themeswitch": return new ThemeSwitchScene();
                case "whatsnew": return new WhatsNewScene();          // 18.1: cycles four scenes

                // ---- First Steps (lesson 1) ----
                case "handswap": return new HandSwapScene();
                case "actionword": return new ActionWordScene();
                case "takeandstow": return new TakeAndStowScene();
                case "hubback": return new HubBackScene();
                case "pushout": return new PushOutScene();
                case "gridintro": return new GridIntroScene();

                // ---- lessons 2-15 ----
                case "readout": return new ReadoutScene();
                case "stowwedge": return new StowWedgeScene();
                case "hintring": return new HintRingScene();
                case "toolreplace": return new ToolReplaceScene(0);
                case "toolreplace.canister":
                case "toolreplacecanister": return new ToolReplaceScene(1);   // 3.2 canister wording
                case "toolreplace.cartridge":
                case "toolreplacecartridge": return new ToolReplaceScene(2);  // 3.3T (labels CARTRIDGE)
                case "tabletinstall": return new TabletInstallScene();
                case "devicewindow": return new DeviceWindowScene();
                case "split": return new SplitScene();
                case "splitpopup": return new SplitPopupScene();
                case "dragpark": return new DragParkScene();
                case "worldslot": return new WorldSlotScene();
                case "altreach": return new AltReachScene();
                case "equiptaphold": return new EquipTapHoldScene();
                case "ingrid": return new InGridScene();
                case "beltswap": return new BeltSwapScene();
                case "ghostlabel": return new GhostLabelScene();
                case "hubroot": return new HubRootScene();
                case "search": return new SearchScene();
                case "bindbag": return new BindBagScene();
                case "stowroute": return new StowRouteScene();
                case "valuescroll": return new ValueScrollScene();
                case "hotkey": return new HotkeyScene();
                case "shiftkeep": return new ShiftKeepScene();
                case "holdflick": return new HoldFlickScene();
                case "hubdrag": return new HubDragScene();
                case "gridmoves": return new GridMovesScene();
                case "shiftdrag": return new ShiftDragScene();
                case "pintear":
                case "pinflow": return new PinTearScene();            // pinflow's stale HUD-box beat is gone
                case "scrollselect": return new ScrollSelectScene();
                case "bagnames": return new BagNamesScene();
                case "baresenses": return new BareSensesScene();
                case "lowpower": return new LowPowerScene();

                // ---- legacy 0.9.7 step scenes (kept while TutorialSteps.cs exists) ----
                case "cursorlatch": return new CursorLatchScene();
                case "tapvshold": return new TapVsHoldScene();
                case "beltwheel": return new BeltWheelScene();
                case "static.equipkeys":
                case "staticequipkeys":
                case "equipkeys": return new EquipKeysScene();
                case "swipechild": return new SwipeChildScene();
                case "batteryswap": return new BatterySwapScene();
                case "valueandpark":
                case "valuepark": return new ValueParkScene();
                case "gridpeeklatch": return new GridPeekLatchScene();
                case "gridregimes": return new GridRegimesScene();
                case "stowflash": return new StowFlashScene();
                default: return null;
            }
        }

        // =====================================================================================
        //  Build context + live key glyphs
        // =====================================================================================

        /// <summary>What a scene gets besides its root: the live glyph resolver, the layout (mini or
        /// full), the keys micro-scene's parsed pattern/token, and the list of curved labels the
        /// stage services each tick.</summary>
        private sealed class Ctx
        {
            public System.Func<string, string> Glyph;
            public bool Mini;
            public string Pattern;
            public string Token;
            public readonly List<CurvedText> Curves = new List<CurvedText>();

            /// <summary>The player's CURRENT key for a token, as bare display text ("Alt", "MMB",
            /// "F10"). Build time only (allocates).</summary>
            public string Key(string token) => ResolveKey(Glyph, token);
        }

        /// <summary>Token -> bare key text. Accepts "{UIA_X}" / "{V:X}" (braced), "UIA_X" / "V:X"
        /// (bare) and literals ("Shift", "Ctrl", "7" — shown as written). The resolver may take the
        /// inner token or the braced one and may answer with or without the coach's [brackets]: both
        /// shapes are tried and cleaned, so this keeps working whichever form TutorialTokens.Glyph
        /// settles on. An unbound key degrades to its friendly name, never to a raw token.</summary>
        private static string ResolveKey(System.Func<string, string> glyph, string token)
        {
            if (string.IsNullOrEmpty(token)) return "?";
            string t = token.Trim();
            bool braced = t.Length >= 2 && t[0] == '{' && t[t.Length - 1] == '}';
            string inner = braced ? t.Substring(1, t.Length - 2).Trim() : t;
            if (inner.Length == 0) return "?";
            bool isToken = braced
                || inner.StartsWith("UIA_", System.StringComparison.Ordinal)
                || inner.StartsWith("V:", System.StringComparison.Ordinal);
            if (!isToken) return AsciiOnly(inner);

            string r = null;
            if (glyph != null)
            {
                r = CleanGlyph(CallGlyph(glyph, inner));
                if (Unresolved(r, inner))
                {
                    string r2 = CleanGlyph(CallGlyph(glyph, "{" + inner + "}"));
                    if (!Unresolved(r2, inner)) r = r2;
                }
            }
            if (Unresolved(r, inner)) r = FriendlyName(inner);
            return AsciiOnly(r);
        }

        private static string CallGlyph(System.Func<string, string> glyph, string arg)
        {
            try { return glyph(arg); }
            catch { return null; }
        }

        private static string CleanGlyph(string s)
        {
            if (s == null) return null;
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '[' && s[s.Length - 1] == ']') s = s.Substring(1, s.Length - 2).Trim();
            return s;
        }

        private static bool Unresolved(string r, string inner)
            => string.IsNullOrEmpty(r) || r == "-" || r == "?" || r.IndexOf('{') >= 0 || r.IndexOf('}') >= 0
               || string.Equals(r, inner, System.StringComparison.Ordinal);

        private static string FriendlyName(string inner)
        {
            if (inner.StartsWith("UIA_", System.StringComparison.Ordinal)) return inner.Substring(4);
            if (inner.StartsWith("V:", System.StringComparison.Ordinal)) return inner.Substring(2);
            return inner;
        }

        /// <summary>Printable Basic Latin only (the game's TMP font tofus anything else).</summary>
        private static string AsciiOnly(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            bool clean = true;
            for (int i = 0; i < s.Length; i++)
                if (s[i] < ' ' || s[i] > '~') { clean = false; break; }
            if (clean) return s;
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] < ' ' || chars[i] > '~') chars[i] = '?';
            return new string(chars);
        }

        /// <summary>-1 = a keyboard key; 0 left, 1 right, 2 middle mouse button — the glyphs
        /// UiaKeybinds.Glyph (and so every lesson line) uses for the mouse.</summary>
        private static int MouseButtonOf(string glyph)
        {
            if (string.IsNullOrEmpty(glyph)) return -1;
            string g = glyph.Trim();
            if (SameText(g, "LMB") || SameText(g, "Mouse0")) return 0;
            if (SameText(g, "RMB") || SameText(g, "Mouse1")) return 1;
            if (SameText(g, "MMB") || SameText(g, "Mouse2")) return 2;
            return -1;
        }

        private static bool SameText(string a, string b)
            => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        // =====================================================================================
        //  Scene contract
        // =====================================================================================

        private interface IDemoScene
        {
            /// <summary>False = full layout only (a card scene); a mini host then shows it shrunk.</summary>
            bool SupportsMini { get; }
            void Build(RectTransform root, Ctx ctx);
            /// <param name="t">Seconds of UNSCALED time since this scene was built.</param>
            void Tick(float t);
            void Destroy();
        }

        /// <summary>Base for every scene: the frozen palette, the build context and the layout flag,
        /// plus build-time helpers that honour the mini rule (no words in the strip slot).</summary>
        private abstract class Scene : IDemoScene
        {
            protected Pal P;
            protected Ctx C;
            protected bool Mini;

            /// <summary>Every lesson scene authors a mini layout; card-only scenes override false.</summary>
            public virtual bool SupportsMini => true;

            public void Build(RectTransform root, Ctx ctx)
            {
                C = ctx ?? new Ctx();
                Mini = C.Mini;
                P = Pal.Read();
                Make(root);
            }

            protected abstract void Make(RectTransform root);
            public abstract void Tick(float t);
            public virtual void Destroy() { }

            /// <summary>The live key text for a token (build time only).</summary>
            protected string K(string token) => C.Key(token);

            /// <summary>Pick a layout value: full box or mini slot.</summary>
            protected float L(float full, float mini) => Mini ? mini : full;

            /// <summary>A full-layout caption line; null in the mini layout.</summary>
            protected TextMeshProUGUI Caption(RectTransform root, string text, float y, float size = 9f,
                float w = 430f, float x = 0f)
                => Mini ? null : Label(root, text, size, P.TextMute, x, y, w);
        }

        // =====================================================================================
        //  Shared toolkit — every scene builds from these. All pure/static, no mutable statics.
        // =====================================================================================

        /// <summary>The scene's frozen copy of the live menu theme. Captured once per build so the
        /// getters (which walk the HUD palette) never run inside Tick. <see cref="Selected"/> IS the
        /// active-hand accent (UiaTheme derives it from HudPalette.ActiveHandAccent).</summary>
        private struct Pal
        {
            public Color Accent, AccentDim, Panel, Raised, Hover, Window, Border, Track;
            public Color Text, TextDim, TextMute, Selected, Good, Warn, Crit, Ink;

            public static Pal Read()
            {
                Pal p = new Pal();
                p.Accent = UiaTheme.Accent;
                p.AccentDim = UiaTheme.AccentDim;
                p.Panel = UiaTheme.Panel;
                p.Raised = UiaTheme.PanelRaised;
                p.Hover = UiaTheme.PanelHover;
                p.Window = UiaTheme.Window;
                p.Border = UiaTheme.Border;
                p.Track = UiaTheme.Track;
                p.Text = UiaTheme.Text;
                p.TextDim = UiaTheme.TextDim;
                p.TextMute = UiaTheme.TextMute;
                p.Selected = UiaTheme.Selected;
                p.Good = UiaTheme.Good;
                p.Warn = UiaTheme.Warn;
                p.Crit = UiaTheme.Critical;
                p.Ink = new Color(p.Window.r * 0.5f, p.Window.g * 0.5f, p.Window.b * 0.5f, 0.92f);
                return p;
            }
        }

        private static RectTransform Node(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>A node carrying a CanvasGroup — the cheap way to fade a whole sub-tree
        /// (multiplied by the canvas, so it costs no mesh rebuild).</summary>
        private static CanvasGroup GroupNode(RectTransform parent, string name)
        {
            var rt = Node(parent, name);
            var cg = rt.gameObject.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;
            return cg;
        }

        private static RectTransform RtOf(Component c) => (RectTransform)c.transform;

        private static PanelGraphic Panel(RectTransform parent, string name, float w, float h,
            Color fill, Color border, float radius = 6f, float borderW = 1.4f)
        {
            var rt = Node(parent, name);
            rt.sizeDelta = new Vector2(w, h);
            var g = rt.gameObject.AddComponent<PanelGraphic>();
            g.raycastTarget = false;
            g.color = fill;
            g.BorderColor = border;
            g.BorderWidth = border.a > 0.004f ? borderW : 0f;
            g.SetShape(w, h, radius);
            return g;
        }

        /// <summary>A trapezoid panel (hand tray shoulders, manila folder tabs).</summary>
        private static PanelGraphic Trap(RectTransform parent, string name, float w, float h,
            Color fill, Color border, float radius, float topInset, float bottomInset)
        {
            var g = Panel(parent, name, w, h, fill, border, radius);
            g.SetShape(w, h, radius, topInset, bottomInset);
            return g;
        }

        private static TextMeshProUGUI Label(RectTransform parent, string text, float size,
            Color col, float x, float y, float w = 150f,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var t = UiaUi.Text(parent, text, size, col, align);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, size * 1.7f);
            rt.anchoredPosition = new Vector2(x, y);
            t.raycastTarget = false;
            return t;
        }

        private static void At(Component c, float x, float y)
            => ((RectTransform)c.transform).anchoredPosition = new Vector2(x, y);

        private static void At(Component c, Vector2 p)
            => ((RectTransform)c.transform).anchoredPosition = p;

        private static Vector2 Pos(Component c) => ((RectTransform)c.transform).anchoredPosition;

        private static void Scale(Component c, float s)
            => c.transform.localScale = new Vector3(s, s, 1f);

        /// <summary>Radial direction in the mod's own wheel convention (ImGui y-down angles, so
        /// angle -PI/2 is straight UP and wedge 0 sits top-centre — see UnityRadialView).</summary>
        private static Vector2 Dir(float ang) => new Vector2(Mathf.Cos(ang), -Mathf.Sin(ang));

        private static float Seg(float t, float a, float b)
            => Mathf.Clamp01((t - a) / Mathf.Max(0.0001f, b - a));

        private static float Ease(float t, float a, float b) => Mathf.SmoothStep(0f, 1f, Seg(t, a, b));

        /// <summary>0 -> 1 -> 0 over [a,b]; the standard "flash / pulse once" envelope.</summary>
        private static float Bump(float t, float a, float b) => Mathf.Sin(Seg(t, a, b) * Mathf.PI);

        /// <summary>Up over [a, a+ramp], held, down over [b-ramp, b] — a key or button HELD.</summary>
        private static float Held(float t, float a, float b, float ramp = 0.1f)
            => Mathf.Min(Seg(t, a, a + ramp), 1f - Seg(t, b - ramp, b));

        /// <summary>Visible over [a,b] with eased edges.</summary>
        private static float Window01(float t, float a, float b, float fade = 0.25f)
            => Mathf.Min(Ease(t, a, a + fade), 1f - Ease(t, b - fade, b));

        private static Color WithA(Color c, float a) { c.a = a; return c; }
        private static Color MulA(Color c, float m) { c.a *= m; return c; }

        /// <summary>Pull a surface toward <paramref name="hot"/>'s HUE while KEEPING its own alpha.
        /// A straight Lerp toward a low-alpha accent dissolves the panel instead of lighting it,
        /// which reads as the box disappearing exactly when it is supposed to flash.</summary>
        private static Color Hot(Color c, Color hot, float t)
            => new Color(Mathf.Lerp(c.r, hot.r, t), Mathf.Lerp(c.g, hot.g, t),
                Mathf.Lerp(c.b, hot.b, t), c.a);

        /// <summary>Eased travel from a to b with a parabolic lift — the "item hops" arc.</summary>
        private static Vector2 Arc(Vector2 a, Vector2 b, float u, float lift)
        {
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
            return new Vector2(Mathf.Lerp(a.x, b.x, s),
                Mathf.Lerp(a.y, b.y, s) + lift * Mathf.Sin(s * Mathf.PI));
        }

        /// <summary>Signed shortest angular step, radians in [-PI, PI).</summary>
        private static float DeltaRad(float from, float to)
            => Mathf.Repeat(to - from + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;

        private static float WrapPi(float a) => Mathf.Repeat(a + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;

        private static TriangleGraphic Tri(RectTransform parent, string name, float size,
            Color col, bool up, float rotZ)
        {
            var rt = Node(parent, name);
            rt.sizeDelta = new Vector2(size, size);
            var g = rt.gameObject.AddComponent<TriangleGraphic>();
            g.raycastTarget = false;
            g.color = col;
            g.Configure(up, size);
            rt.localEulerAngles = new Vector3(0f, 0f, rotZ);
            return g;
        }

        private static CircleGraphic Circle(RectTransform parent, string name, float radius,
            Color fill, Color border, float borderW = 1.4f)
        {
            var rt = Node(parent, name);
            rt.sizeDelta = new Vector2(radius * 2f, radius * 2f);
            var g = rt.gameObject.AddComponent<CircleGraphic>();
            g.raycastTarget = false;
            g.color = fill;
            g.BorderColor = border;
            g.BorderWidth = border.a > 0.004f ? borderW : 0f;
            g.SetRadius(radius);
            return g;
        }

        private static RadialWedgeGraphic Wedge(RectTransform parent, string name, Color fill,
            Color border, float borderW = 1.4f)
        {
            var rt = Node(parent, name);
            var g = rt.gameObject.AddComponent<RadialWedgeGraphic>();
            g.raycastTarget = false;
            g.color = fill;
            g.BorderColor = border;
            g.BorderWidth = border.a > 0.004f ? borderW : 0f;
            g.SideBorders = false;
            g.FeatherSides = true;
            g.RimHighlight = 0f;
            return g;
        }

        /// <summary>The close "X" — drawn, never typed (TMP has no reliable glyph for it).</summary>
        private static RectTransform XGlyph(RectTransform parent, string name, float size, Color col)
        {
            var root = Node(parent, name);
            var a = Panel(root, "a", size, 1.9f, col, Color.clear, 0.95f, 0f);
            a.transform.localEulerAngles = new Vector3(0f, 0f, 45f);
            var b = Panel(root, "b", size, 1.9f, col, Color.clear, 0.95f, 0f);
            b.transform.localEulerAngles = new Vector3(0f, 0f, -45f);
            return root;
        }

        /// <summary>A drawn "+" (chords: Shift + key).</summary>
        private static RectTransform PlusGlyph(RectTransform parent, string name, float size, float thick, Color col)
        {
            var root = Node(parent, name);
            Panel(root, "h", size, thick, col, Color.clear, thick * 0.5f, 0f);
            Panel(root, "v", thick, size, col, Color.clear, thick * 0.5f, 0f);
            return root;
        }

        /// <summary>A drawn success tick (two bars), never a glyph.</summary>
        private static RectTransform TickGlyph(RectTransform parent, string name, float size, Color col)
        {
            var root = Node(parent, name);
            float th = Mathf.Max(1.8f, size * 0.16f);
            var a = Panel(root, "a", size * 0.42f, th, col, Color.clear, th * 0.5f, 0f);
            a.transform.localEulerAngles = new Vector3(0f, 0f, -45f);
            At(a, -size * 0.2f, -size * 0.08f);
            var b = Panel(root, "b", size * 0.8f, th, col, Color.clear, th * 0.5f, 0f);
            b.transform.localEulerAngles = new Vector3(0f, 0f, 50f);
            At(b, size * 0.14f, size * 0.06f);
            return root;
        }

        private static RectTransform Crosshair(RectTransform parent, string name, float size,
            float thick, Color col)
        {
            var root = Node(parent, name);
            float arm = size * 0.34f;
            float off = size * 0.5f - arm * 0.5f;
            At(Panel(root, "up", thick, arm, col, Color.clear, thick * 0.5f, 0f), 0f, off);
            At(Panel(root, "dn", thick, arm, col, Color.clear, thick * 0.5f, 0f), 0f, -off);
            At(Panel(root, "lt", arm, thick, col, Color.clear, thick * 0.5f, 0f), -off, 0f);
            At(Panel(root, "rt", arm, thick, col, Color.clear, thick * 0.5f, 0f), off, 0f);
            Panel(root, "dot", thick * 1.2f, thick * 1.2f, col, Color.clear, thick * 0.6f, 0f);
            return root;
        }

        /// <summary>TMP's measured width of its CURRENT text, with a character-count estimate when
        /// the font/layout is not ready (a host built while inactive). Build time only.</summary>
        private static float TextWidth(TextMeshProUGUI t, string text, float size)
        {
            float w = 0f;
            try { w = t.preferredWidth; } catch { w = 0f; }
            if (!(w > 0.5f) || w > 4000f) w = (text != null ? text.Length : 0) * size * 0.58f;
            return w;
        }

        /// <summary>A Universal-Inventory-style window mock: glass panel, title bar and (words on) its
        /// caption and close X. Returns the window's root node.</summary>
        private static RectTransform WindowMock(RectTransform parent, string name, float w, float h, Pal p,
            string title, bool words, out PanelGraphic panel)
        {
            var root = Node(parent, name);
            panel = Panel(root, "panel", w, h, MulA(p.Window, 0.96f), p.Border, Mathf.Min(9f, h * 0.12f), 1.5f);
            float barH = Mathf.Clamp(h * 0.12f, 6f, 17f);
            var bar = Panel(root, "bar", w, barH, MulA(p.Raised, 0.9f), Color.clear, Mathf.Min(6f, barH * 0.45f), 0f);
            At(bar, 0f, h * 0.5f - barH * 0.5f);
            if (words && !string.IsNullOrEmpty(title))
                Label(root, title, Mathf.Min(7.5f, barH * 0.5f), p.TextDim, -6f, h * 0.5f - barH * 0.5f, w - 24f);
            if (words)
                XGlyph(root, "x", barH * 0.5f, p.TextDim).anchoredPosition =
                    new Vector2(w * 0.5f - barH * 0.7f, h * 0.5f - barH * 0.5f);
            return root;
        }

        /// <summary>A manila folder tab (the grid's bag header) at <paramref name="at"/>, with its
        /// fold chevron. The chevron points right (-90) while folded and down (0) when open; scenes
        /// rotate it.</summary>
        private static PanelGraphic FolderTab(RectTransform parent, string name, Vector2 at, float w, float h, Pal p,
            string title, bool words, out TriangleGraphic chevron)
        {
            var tab = Trap(parent, name, w, h, MulA(p.Warn, 0.30f), MulA(p.Warn, 0.85f), Mathf.Min(4f, h * 0.2f), h * 0.4f, 0f);
            At(tab, at);
            chevron = Tri(parent, name + "Chev", Mathf.Max(5f, h * 0.4f), p.Text, true, -90f);
            At(chevron, at + new Vector2(-w * 0.5f + h * 0.6f, 0f));
            if (words && !string.IsNullOrEmpty(title))
                Label(parent, title, Mathf.Min(8f, h * 0.42f), p.Text, at.x + h * 0.35f, at.y, w - h * 1.2f);
            return tab;
        }

        // ---- curved text ------------------------------------------------------------------------

        /// <summary>A TMP label bent round a circle ONCE at build (<see cref="RadialArcText.Curve"/>),
        /// re-bent by the stage's tick only if TMP rebuilt it flat (<see cref="ArcBendCache"/> — a
        /// steady label costs a few compares, no allocation). Never recolour it after build (a TMP
        /// colour write regenerates the mesh flat): fade it through a CanvasGroup.</summary>
        private sealed class CurvedText
        {
            public TextMeshProUGUI Text;
            public float Radius;
            public bool OutwardUp;
            public float Width;
            private readonly ArcBendCache _bend = new ArcBendCache();

            /// <summary>Create (unplaced) and register for servicing. Rich text is allowed.</summary>
            public static CurvedText Make(Ctx c, RectTransform parent, string text, float size, Color col,
                FontStyles style = FontStyles.Normal)
            {
                var ct = new CurvedText();
                var t = UiaUi.Text(parent, text, size, col, TextAlignmentOptions.Center);
                t.fontStyle = style;
                t.richText = true;
                t.raycastTarget = false;
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                ct.Text = t;
                ct.Width = TextWidth(t, text, size);
                rt.sizeDelta = new Vector2(Mathf.Max(8f, ct.Width + 8f), size * 1.6f);
                if (c != null) c.Curves.Add(ct);
                return ct;
            }

            /// <summary>Place the label's midline at <paramref name="pos"/> (on the circle of
            /// <paramref name="radius"/>), rotated to the tangent, and bend it once.</summary>
            public void Place(Vector2 pos, float rotDeg, float radius, bool outwardUp)
            {
                var rt = Text.rectTransform;
                rt.anchoredPosition = pos;
                rt.localRotation = Quaternion.Euler(0f, 0f, rotDeg);
                Radius = radius;
                OutwardUp = outwardUp;
                RadialArcText.Curve(Text, radius, outwardUp);
            }

            public void Service() { if (Text != null) _bend.CurveIfStale(Text, Radius, OutwardUp); }
        }

        /// <summary>A curved label centred on ImGui angle <paramref name="ang"/> of a ring (centre
        /// <paramref name="center"/>, text midline at radius <paramref name="r"/>), upright on both
        /// halves — the wheel's seal convention (ArcPlateLabel.LayoutAt): "up" outward on the upper
        /// half, inward on the lower half.</summary>
        private static CurvedText CurvedAt(Ctx c, RectTransform parent, string text, float size, Color col,
            Vector2 center, float r, float ang, FontStyles style = FontStyles.Normal)
        {
            Vector2 d = Dir(ang);
            bool outward = d.y >= -1e-3f;
            float theta = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var ct = CurvedText.Make(c, parent, text, size, col, style);
            ct.Place(center + d * r, outward ? theta - 90f : theta + 90f, r, outward);
            return ct;
        }

        /// <summary>
        /// The D-022 ACTION WORD: a curved word on a glass plate OUTSIDE a ring, riding whichever
        /// wedge is hovered (the 2026-09-26 behaviour — no longer pinned to 12 o'clock). Two
        /// pre-bent copies (upper-half: glyphs outward; lower-half: glyphs inward, still reading
        /// left-to-right) each on a pivot at the ring centre; <see cref="Set"/> rotates the right one
        /// to the angle and fades them through CanvasGroups — the text is never rewritten or
        /// recoloured after build. Parent it to the ring's root so it blooms with the ring (the real
        /// word rides the open animation too).
        /// </summary>
        private sealed class ArcWord
        {
            private RectTransform _upPivot, _dnPivot;
            private CanvasGroup _upGroup, _dnGroup;

            public static ArcWord Make(Ctx c, RectTransform ringRoot, string word, float ringOuter, float gap,
                float size, Color textCol, Color plateFill, Color plateBorder)
            {
                var w = new ArcWord();
                float thick = size * 1.3f + 6f;
                float rIn = ringOuter + gap;
                float rMid = rIn + thick * 0.5f;
                w._upGroup = GroupNode(ringRoot, "word+" + word);
                w._upPivot = (RectTransform)w._upGroup.transform;
                w._dnGroup = GroupNode(ringRoot, "word-" + word);
                w._dnPivot = (RectTransform)w._dnGroup.transform;
                for (int half = 0; half < 2; half++)
                {
                    bool up = half == 0;
                    var pivot = up ? w._upPivot : w._dnPivot;
                    var ct = CurvedText.Make(c, pivot, word, size, textCol, FontStyles.Bold);
                    float span = (ct.Width * 0.5f + size * 0.8f) / rMid;
                    float mid = up ? -Mathf.PI * 0.5f : Mathf.PI * 0.5f;
                    var plate = Wedge(pivot, "plate", plateFill, plateBorder, 1.2f);
                    plate.SideBorders = true;
                    plate.SideWidthInner = plate.SideWidthOuter = 1.2f;
                    plate.SetGeometry(rIn, rIn + thick, mid - span, mid + span, false);
                    plate.RefreshGeometry();
                    plate.transform.SetAsFirstSibling();   // behind its word
                    ct.Place(new Vector2(0f, up ? rMid : -rMid), 0f, rMid, up);
                }
                w._upGroup.alpha = 0f;
                w._dnGroup.alpha = 0f;
                return w;
            }

            /// <summary>Show the word centred on ImGui angle <paramref name="ang"/> at
            /// <paramref name="alpha"/>. The half flips at 3 / 9 o'clock like the real label.</summary>
            public void Set(float ang, float alpha)
            {
                alpha = Mathf.Clamp01(alpha);
                Vector2 d = Dir(ang);
                bool upper = d.y >= -1e-3f;
                float theta = -ang * Mathf.Rad2Deg;
                if (upper) _upPivot.localEulerAngles = new Vector3(0f, 0f, theta - 90f);
                else _dnPivot.localEulerAngles = new Vector3(0f, 0f, theta + 90f);
                _upGroup.alpha = upper ? alpha : 0f;
                _dnGroup.alpha = upper ? 0f : alpha;
            }
        }

        /// <summary>Drives a set of <see cref="ArcWord"/>s the way UnityRadialView drives the real
        /// one: it appears AT the hovered wedge (no sweep in), eases round the SHORTEST way when the
        /// hover moves, crossfades when the word changes, and dissolves where it was headed. Plain
        /// instance state; dt comes from the scene clock.</summary>
        private sealed class WordRing
        {
            private readonly Wheel _w;
            private readonly ArcWord[] _words;
            private readonly float[] _mix;
            private float _alpha, _ang = -Mathf.PI * 0.5f, _lastT = -1f;
            private const float SlideSpeed = 12f;   // slower than the live 22/s: legible at demo scale
            private const float FadeSpeed = 7f;
            private const float SwapSpeed = 12f;

            public WordRing(Wheel w, params ArcWord[] words)
            {
                _w = w;
                _words = words;
                _mix = new float[words.Length];
            }

            public float Mix(int i) => i >= 0 && i < _mix.Length ? _mix[i] * _alpha : 0f;

            /// <param name="wedge">hovered wedge, -1 = none</param>
            /// <param name="word">which word that wedge earns, -1 = none</param>
            public void Update(float t, int wedge, int word)
            {
                float dt = _lastT < 0f ? 0f : Mathf.Clamp(t - _lastT, 0f, 0.1f);
                _lastT = t;
                bool show = wedge >= 0 && word >= 0 && word < _words.Length;
                if (show)
                {
                    float target = _w.MidAngle(wedge);
                    if (_alpha <= 0.001f)
                    {
                        _ang = target;
                        for (int i = 0; i < _mix.Length; i++) _mix[i] = i == word ? 1f : 0f;
                    }
                    else
                    {
                        float d = DeltaRad(_ang, target);
                        _ang = Mathf.Abs(d) < 1e-4f ? target : WrapPi(_ang + d * (1f - Mathf.Exp(-SlideSpeed * dt)));
                    }
                    for (int i = 0; i < _mix.Length; i++)
                        _mix[i] = Mathf.MoveTowards(_mix[i], i == word ? 1f : 0f, dt * SwapSpeed);
                }
                _alpha = Mathf.MoveTowards(_alpha, show ? 1f : 0f, dt * FadeSpeed);
                for (int i = 0; i < _words.Length; i++) _words[i].Set(_ang, _alpha * _mix[i]);
            }
        }

        /// <summary>The D-021 key-hint strip wrapped under a ring on one glass plate. Each hint is its
        /// own curved label so it can fade alone (the per-hint 25-opens fade). Items are laid out
        /// left-to-right along the bottom arc; ones that do not fit the arc are dropped from the end
        /// (the real strip's trim rule). Rich-text keys in the accent colour.</summary>
        private sealed class HintArc
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public CanvasGroup[] Items;
            public RadialWedgeGraphic Plate;
            public int Count;

            public static HintArc Make(Ctx c, RectTransform parent, string name, Vector2 center, float innerR,
                float thick, string[] keys, string[] actions, float size, Pal p, float maxHalfDeg)
            {
                var h = new HintArc();
                h.Group = GroupNode(parent, name);
                h.Root = (RectTransform)h.Group.transform;
                h.Root.anchoredPosition = center;
                float rMid = innerR + thick * 0.5f;
                string keyHex = ColorUtility.ToHtmlStringRGB(p.Accent);
                string sepHex = ColorUtility.ToHtmlStringRGBA(MulA(p.TextDim, 0.55f));
                int n = keys.Length;
                var pivots = new CanvasGroup[n];
                var texts = new CurvedText[n];
                var widths = new float[n];
                for (int i = 0; i < n; i++)
                {
                    string s = "<color=#" + keyHex + ">" + keys[i] + "</color>  " + actions[i]
                        + (i < n - 1 ? "   <color=#" + sepHex + ">/</color>" : "");
                    pivots[i] = GroupNode(h.Root, "hint" + i);
                    texts[i] = CurvedText.Make(c, (RectTransform)pivots[i].transform, s, size, p.TextDim);
                    widths[i] = texts[i].Width;
                }
                // Trim from the end until the run fits the arc budget.
                float gap = size * 0.9f;
                float budget = 2f * maxHalfDeg * Mathf.Deg2Rad * rMid;
                int count = n;
                float total = 0f;
                for (; count > 0; count--)
                {
                    total = 0f;
                    for (int i = 0; i < count; i++) total += widths[i] + (i > 0 ? gap : 0f);
                    if (total <= budget) break;
                }
                if (count == 0) { count = 1; total = widths[0]; }
                h.Count = count;
                h.Items = new CanvasGroup[count];
                float s0 = -total * 0.5f;
                for (int i = 0; i < n; i++)
                {
                    if (i >= count) { pivots[i].gameObject.SetActive(false); continue; }
                    float mid = s0 + widths[i] * 0.5f;
                    s0 += widths[i] + gap;
                    texts[i].Place(new Vector2(0f, -rMid), 0f, rMid, false);
                    // Rotating the pivot CCW by (arc length / r) walks the label right along the bottom.
                    pivots[i].transform.localEulerAngles = new Vector3(0f, 0f, mid / rMid * Mathf.Rad2Deg);
                    h.Items[i] = pivots[i];
                }
                float half = (total * 0.5f + size) / rMid;
                h.Plate = Wedge(h.Root, "plate", MulA(p.Panel, 0.9f), MulA(p.Border, 0.75f), 1.2f);
                h.Plate.SideBorders = true;
                h.Plate.SideWidthInner = h.Plate.SideWidthOuter = 1.2f;
                h.Plate.SetGeometry(innerR, innerR + thick, Mathf.PI * 0.5f - half, Mathf.PI * 0.5f + half, false);
                h.Plate.RefreshGeometry();
                h.Plate.transform.SetAsFirstSibling();
                return h;
            }

            /// <summary>Wordless variant for the mini slot: rounded pills along the arc.</summary>
            public static HintArc MakePills(RectTransform parent, string name, Vector2 center, float innerR,
                float thick, int n, float pillLen, Pal p)
            {
                var h = new HintArc();
                h.Group = GroupNode(parent, name);
                h.Root = (RectTransform)h.Group.transform;
                h.Root.anchoredPosition = center;
                float rMid = innerR + thick * 0.5f;
                float gap = pillLen * 0.35f;
                float total = n * pillLen + (n - 1) * gap;
                h.Count = n;
                h.Items = new CanvasGroup[n];
                for (int i = 0; i < n; i++)
                {
                    h.Items[i] = GroupNode(h.Root, "pill" + i);
                    var pivot = (RectTransform)h.Items[i].transform;
                    float mid = -total * 0.5f + pillLen * 0.5f + i * (pillLen + gap);
                    pivot.localEulerAngles = new Vector3(0f, 0f, mid / rMid * Mathf.Rad2Deg);
                    var key = Panel(pivot, "k", pillLen * 0.3f, thick * 0.42f, p.Accent, Color.clear, thick * 0.2f, 0f);
                    At(key, -pillLen * 0.33f, -rMid);
                    var act = Panel(pivot, "a", pillLen * 0.55f, thick * 0.3f, MulA(p.TextDim, 0.8f), Color.clear, thick * 0.15f, 0f);
                    At(act, pillLen * 0.2f, -rMid);
                }
                float half = (total * 0.5f + pillLen * 0.3f) / rMid;
                h.Plate = Wedge(h.Root, "plate", MulA(p.Panel, 0.9f), MulA(p.Border, 0.75f), 1f);
                h.Plate.SetGeometry(innerR, innerR + thick, Mathf.PI * 0.5f - half, Mathf.PI * 0.5f + half, false);
                h.Plate.RefreshGeometry();
                h.Plate.transform.SetAsFirstSibling();
                return h;
            }
        }

        // ---- composite widgets ---------------------------------------------------------------

        /// <summary>A rounded key cap with ASCII text ("Alt", "B", "G"). Pressing is a scale squash
        /// plus a fill/text swap, driven by <see cref="SetPressed"/>.</summary>
        private sealed class Keycap
        {
            public RectTransform Root;
            public PanelGraphic Cap;
            public TextMeshProUGUI Text;
            public CanvasGroup Group;
            public float W, H;
            private Color _up, _down, _borderUp, _borderDown, _textUp, _textDown;

            public static Keycap Make(RectTransform parent, string name, string glyph, Pal p,
                float w = 34f, float h = 26f, float size = 13f)
            {
                var k = new Keycap();
                k.Group = GroupNode(parent, name);
                k.Root = (RectTransform)k.Group.transform;
                k.W = w;
                k.H = h;
                k._up = p.Raised;
                k._down = MulA(Color.Lerp(p.Raised, p.Accent, 0.75f), 1f);
                k._borderUp = MulA(p.Border, 0.9f);
                k._borderDown = p.Accent;
                k._textUp = p.Text;
                k._textDown = p.Text;
                k.Cap = Panel(k.Root, "cap", w, h, k._up, k._borderUp, Mathf.Clamp(h * 0.27f, 3f, 7f), 1.5f);
                k.Text = Label(k.Root, glyph, size, k._textUp, 0f, 0.5f, w + 12f);
                return k;
            }

            /// <summary>A cap as wide as its text needs ("Shift", "Enter", "F10"), never narrower
            /// than square-ish.</summary>
            public static Keycap Auto(RectTransform parent, string name, string glyph, Pal p, float h, float size)
            {
                var k = Make(parent, name, glyph, p, h, h, size);
                float tw = TextWidth(k.Text, glyph, size);
                float w = Mathf.Max(h * 1.05f, tw + h * 0.62f);
                k.W = w;
                k.Cap.SetShape(w, h, Mathf.Clamp(h * 0.27f, 3f, 7f));
                ((RectTransform)k.Cap.transform).sizeDelta = new Vector2(w, h);
                k.Text.rectTransform.sizeDelta = new Vector2(w + 12f, size * 1.7f);
                return k;
            }

            public void SetPos(float x, float y) => Root.anchoredPosition = new Vector2(x, y);

            public void SetPressed(float p)
            {
                p = Mathf.Clamp01(p);
                Root.localScale = new Vector3(1f - 0.07f * p, 1f - 0.11f * p, 1f);
                Cap.color = Color.Lerp(_up, _down, p);
                Cap.BorderColor = Color.Lerp(_borderUp, _borderDown, p);
                Text.color = Color.Lerp(_textUp, _textDown, p);
            }

            public void SetAlpha(float a) => Group.alpha = Mathf.Clamp01(a);
        }

        /// <summary>A drawn mouse: capsule body, two button pads that light when pressed, the wheel
        /// (lights on a middle press, rolls its ridges on a scroll, with up/down arrows).</summary>
        private sealed class Mouse
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public float W, H;
            private PanelGraphic _padL, _padR, _wheel;
            private RectTransform _ridgeA, _ridgeB;
            private TriangleGraphic _up, _down;
            private Color _padIdle, _padHot, _wheelIdle, _wheelHot, _arrow;
            private float _btnY, _wheelH;

            public static Mouse Make(RectTransform parent, string name, Pal p, float h)
            {
                var m = new Mouse();
                m.Group = GroupNode(parent, name);
                m.Root = (RectTransform)m.Group.transform;
                float w = h * 0.64f;
                m.W = w;
                m.H = h;
                Color border = MulA(p.Border, 0.95f);
                Panel(m.Root, "body", w, h, p.Raised, border, w * 0.48f, 1.5f);
                float btnH = h * 0.40f;
                float top = h * 0.5f;
                m._btnY = top - btnH * 0.5f;
                m._padIdle = WithA(p.Accent, 0f);
                m._padHot = MulA(p.Accent, 0.85f);
                float padW = w * 0.5f - w * 0.17f;
                float padH = btnH - h * 0.12f;
                float padR = Mathf.Min(padW, padH) * 0.35f;
                m._padL = Panel(m.Root, "padL", padW, padH, m._padIdle, Color.clear, padR, 0f);
                At(m._padL, -w * 0.25f - w * 0.01f, m._btnY - h * 0.015f);
                m._padR = Panel(m.Root, "padR", padW, padH, m._padIdle, Color.clear, padR, 0f);
                At(m._padR, w * 0.25f + w * 0.01f, m._btnY - h * 0.015f);
                At(Panel(m.Root, "split", 1.4f, btnH, border, Color.clear, 0.7f, 0f), 0f, m._btnY);
                At(Panel(m.Root, "base", w * 0.86f, 1.4f, border, Color.clear, 0.7f, 0f), 0f, top - btnH);
                float ww = Mathf.Max(4f, w * 0.18f);
                m._wheelH = btnH * 0.62f;
                m._wheelIdle = MulA(p.Window, 0.95f);
                m._wheelHot = p.Accent;
                m._wheel = Panel(m.Root, "wheel", ww, m._wheelH, m._wheelIdle, border, ww * 0.5f, 1.2f);
                At(m._wheel, 0f, m._btnY);
                m._ridgeA = RtOf(Panel(m.Root, "ridgeA", ww * 0.55f, 1.2f, p.TextDim, Color.clear, 0.6f, 0f));
                m._ridgeB = RtOf(Panel(m.Root, "ridgeB", ww * 0.55f, 1.2f, p.TextDim, Color.clear, 0.6f, 0f));
                m._arrow = p.Accent;
                float triS = Mathf.Max(6f, w * 0.24f);
                m._up = Tri(m.Root, "up", triS, WithA(p.Accent, 0f), true, 0f);
                At(m._up, 0f, top + triS * 0.9f);
                m._down = Tri(m.Root, "down", triS, WithA(p.Accent, 0f), false, 0f);
                At(m._down, 0f, -top - triS * 0.9f);
                m.Scroll(0f, 0f);
                return m;
            }

            public void SetPos(float x, float y) => Root.anchoredPosition = new Vector2(x, y);
            public void SetPos(Vector2 v) => Root.anchoredPosition = v;
            public void SetAlpha(float a) => Group.alpha = Mathf.Clamp01(a);

            /// <summary>Where a button sits, in the mouse's parent space (ripples, badges).</summary>
            public Vector2 ButtonPos(int b)
            {
                float x = b == 0 ? -W * 0.26f : b == 1 ? W * 0.26f : 0f;
                return Root.anchoredPosition + new Vector2(x, _btnY);
            }

            /// <summary>Button press states, 0..1 each.</summary>
            public void SetButtons(float left, float right, float middle)
            {
                _padL.color = Color.Lerp(_padIdle, _padHot, Mathf.Clamp01(left));
                _padR.color = Color.Lerp(_padIdle, _padHot, Mathf.Clamp01(right));
                _wheel.color = Color.Lerp(_wheelIdle, _wheelHot, Mathf.Clamp01(middle));
            }

            /// <summary>0 left, 1 right, 2 middle.</summary>
            public void SetButton(int button, float amt)
                => SetButtons(button == 0 ? amt : 0f, button == 1 ? amt : 0f, button == 2 ? amt : 0f);

            /// <summary>Roll the wheel: <paramref name="phase"/> moves the ridges (feed it an
            /// increasing value to roll up, decreasing to roll down); <paramref name="dir"/> lights
            /// the up (+) or down (-) arrow by its magnitude.</summary>
            public void Scroll(float phase, float dir)
            {
                float span = _wheelH * 0.34f;
                float a = Mathf.Repeat(phase, 1f);
                float b = Mathf.Repeat(phase + 0.5f, 1f);
                _ridgeA.anchoredPosition = new Vector2(0f, _btnY + Mathf.Lerp(-span, span, a));
                _ridgeB.anchoredPosition = new Vector2(0f, _btnY + Mathf.Lerp(-span, span, b));
                _up.color = WithA(_arrow, dir > 0f ? Mathf.Clamp01(dir) : 0f);
                _down.color = WithA(_arrow, dir < 0f ? Mathf.Clamp01(-dir) : 0f);
            }
        }

        /// <summary>A key as the player will press it: a keycap, or — when the live glyph is a mouse
        /// button (MMB / LMB / RMB) — a drawn mouse with that button lighting up.</summary>
        private sealed class KeyVis
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Keycap Cap;
            public Mouse Mouse;
            public int Button = -1;
            public float W, H;

            public static KeyVis Make(RectTransform parent, string name, string glyph, Pal p,
                float capH, float fontSize, float mouseH)
            {
                var k = new KeyVis();
                k.Group = GroupNode(parent, name);
                k.Root = (RectTransform)k.Group.transform;
                k.Button = MouseButtonOf(glyph);
                if (k.Button >= 0)
                {
                    k.Mouse = Mouse.Make(k.Root, "mouse", p, mouseH);
                    k.W = k.Mouse.W;
                    k.H = mouseH;
                }
                else
                {
                    k.Cap = Keycap.Auto(k.Root, "cap", glyph, p, capH, fontSize);
                    k.W = k.Cap.W;
                    k.H = capH;
                }
                return k;
            }

            public void SetPos(float x, float y) => Root.anchoredPosition = new Vector2(x, y);
            public Vector2 Position => Root.anchoredPosition;
            public void SetAlpha(float a) => Group.alpha = Mathf.Clamp01(a);

            public void SetPressed(float a)
            {
                if (Cap != null) Cap.SetPressed(a);
                else if (Mouse != null) Mouse.SetButton(Button, a);
            }

            /// <summary>Where the press lands (for a ripple), in the parent's space.</summary>
            public Vector2 PressPoint => Mouse != null ? Root.anchoredPosition + Mouse.ButtonPos(Button) : Root.anchoredPosition;
        }

        /// <summary>A press ripple: a ring that grows and fades once per <see cref="Play"/> sweep.</summary>
        private sealed class Ripple
        {
            public CircleGraphic Ring;
            private float _r0, _r1;
            private Color _c;
            private bool _hidden;

            public static Ripple Make(RectTransform parent, string name, Color c, float r0, float r1, float width = 1.6f)
            {
                var r = new Ripple();
                r._r0 = r0;
                r._r1 = r1;
                r._c = c;
                r.Ring = Circle(parent, name, r0, Color.clear, WithA(c, 0f), width);
                r.Ring.BorderWidth = width;
                r._hidden = true;
                return r;
            }

            public void SetPos(Vector2 v) => ((RectTransform)Ring.transform).anchoredPosition = v;

            /// <summary><paramref name="k"/> = progress 0..1; outside (0,1) the ring is hidden.</summary>
            public void Play(float k)
            {
                if (k <= 0f || k >= 1f)
                {
                    if (!_hidden) { Ring.BorderColor = WithA(_c, 0f); _hidden = true; }
                    return;
                }
                _hidden = false;
                Ring.SetRadius(Mathf.Lerp(_r0, _r1, k));
                Ring.BorderColor = WithA(_c, (1f - k) * 0.85f);
            }
        }

        /// <summary>The two hand boxes — never a hotbar. The ACTIVE hand wears the active-hand
        /// accent border and an accent side bar on its OUTER edge (HandBoxesWidget); here that bar
        /// physically hops between the two outer edges when the hand switches.</summary>
        private sealed class HandPair
        {
            public RectTransform Root;
            public PanelGraphic[] Box = new PanelGraphic[2];
            public PanelGraphic[] Item = new PanelGraphic[2];
            public TextMeshProUGUI[] Title;   // null when words are off (mini)
            public TextMeshProUGUI[] State;
            public PanelGraphic Hopper;
            public float W, H, Gap;
            private Color _fill, _border, _accent, _titleOn, _titleOff;
            private float _right = -1f;
            private float _barW;

            public static HandPair Make(RectTransform parent, string name, Pal p, float w, float h, float gap,
                bool words, float textSize = 7f)
            {
                var hp = new HandPair();
                hp.Root = Node(parent, name);
                hp.W = w;
                hp.H = h;
                hp.Gap = gap;
                hp._fill = MulA(p.Panel, 0.95f);
                hp._border = p.Border;
                hp._accent = p.Selected;
                hp._titleOn = p.Text;
                hp._titleOff = p.TextDim;
                for (int i = 0; i < 2; i++)
                {
                    Vector2 c = hp.LocalCenter(i);
                    hp.Box[i] = Panel(hp.Root, "box" + i, w, h, hp._fill, hp._border, Mathf.Min(8f, h * 0.2f), 1.4f);
                    At(hp.Box[i], c);
                    float s = h * (words ? 0.36f : 0.46f);
                    hp.Item[i] = Panel(hp.Root, "item" + i, s, s, MulA(p.Accent, 0.7f), p.Accent, s * 0.22f, 1.1f);
                    At(hp.Item[i], c);
                    hp.Item[i].gameObject.SetActive(false);
                }
                if (words)
                {
                    hp.Title = new TextMeshProUGUI[2];
                    hp.State = new TextMeshProUGUI[2];
                    for (int i = 0; i < 2; i++)
                    {
                        Vector2 c = hp.LocalCenter(i);
                        hp.Title[i] = Label(hp.Root, i == 0 ? "LEFT HAND" : "RIGHT HAND", textSize, p.TextDim,
                            c.x, c.y + h * 0.5f - textSize * 1.15f, w - 6f);
                        hp.State[i] = Label(hp.Root, "empty", textSize - 0.5f, p.TextMute,
                            c.x, c.y - h * 0.5f + textSize * 1.1f, w - 6f);
                    }
                }
                hp._barW = Mathf.Max(2.4f, w * 0.045f);
                hp.Hopper = Panel(hp.Root, "activeBar", hp._barW, h * 0.72f, p.Selected, Color.clear, hp._barW * 0.45f, 0f);
                hp.SetActive(0f);
                return hp;
            }

            public Vector2 LocalCenter(int i) => new Vector2((i == 0 ? -1f : 1f) * (Gap * 0.5f + W * 0.5f), 0f);
            public Vector2 SceneCenter(int i) => Root.anchoredPosition + LocalCenter(i);

            private Vector2 BarAt(int i)
                => LocalCenter(i) + new Vector2((i == 0 ? -1f : 1f) * (W * 0.5f - _barW * 1.1f), 0f);

            /// <summary>0 = left hand active, 1 = right; in between = the hop.</summary>
            public void SetActive(float right)
            {
                right = Mathf.Clamp01(right);
                if (Mathf.Approximately(right, _right)) return;
                _right = right;
                for (int i = 0; i < 2; i++)
                {
                    float k = i == 0 ? 1f - right : right;
                    Box[i].BorderColor = Color.Lerp(_border, _accent, k);
                    Box[i].BorderWidth = Mathf.Lerp(1.4f, 2.3f, k);
                    if (Title != null) Title[i].color = Color.Lerp(_titleOff, _titleOn, k);
                }
                At(Hopper, Arc(BarAt(0), BarAt(1), right, H * 0.6f));
            }

            /// <summary>A receiving flash on one box (0..1), e.g. a dropped item landing.</summary>
            public void Flash(int i, float k)
            {
                Box[i].color = Hot(_fill, _accent, Mathf.Clamp01(k) * 0.35f);
            }
        }

        /// <summary>The fake mouse pointer: a triangle whose TIP sits exactly on the group origin,
        /// so scenes drive it by plain anchoredPosition.</summary>
        private sealed class Cursor
        {
            public RectTransform Root;
            public CanvasGroup Group;
            private RectTransform _rot;
            private TriangleGraphic _tri;
            private PanelGraphic _tail;
            private Color _idle, _hot;

            public static Cursor Make(RectTransform parent, string name, Pal p, float size = 17f)
            {
                var c = new Cursor();
                c.Group = GroupNode(parent, name);
                c.Root = (RectTransform)c.Group.transform;
                c._rot = Node(c.Root, "rot");
                c._rot.localEulerAngles = new Vector3(0f, 0f, 22f);
                c._idle = p.Text;
                c._hot = p.Selected;
                c._tri = Tri(c._rot, "tip", size, c._idle, true, 0f);
                At(c._tri, 0f, -size * 0.36f);
                // The tail hangs straight down the arrow's OWN axis (no counter-rotation - it
                // inherits the parent's 22 degree lean). A local twist here reads as a kink
                // jutting out of the triangle's side (FlorpyDorp play-test, 2026-08-02).
                c._tail = Panel(c._rot, "tail", Mathf.Max(2.2f, size * 0.2f), size * 0.52f, c._idle, Color.clear,
                    Mathf.Max(1.1f, size * 0.1f), 0f);
                At(c._tail, size * 0.06f, -size * 0.98f);
                return c;
            }

            public void SetPos(float x, float y) => Root.anchoredPosition = new Vector2(x, y);
            public void SetPos(Vector2 v) => Root.anchoredPosition = v;
            public Vector2 Position => Root.anchoredPosition;
            public void SetAlpha(float a) => Group.alpha = Mathf.Clamp01(a);

            /// <summary>0 = idle, 1 = mouse button held (shrinks + takes the selection accent).</summary>
            public void SetClick(float c)
            {
                c = Mathf.Clamp01(c);
                Root.localScale = new Vector3(1f - 0.18f * c, 1f - 0.18f * c, 1f);
                Color col = Color.Lerp(_idle, _hot, c);
                _tri.color = col;
                _tail.color = col;
            }
        }

        /// <summary>A schematic wheel: N annular wedges, a hub disc with two readout lines and the
        /// bottom "Close" band — the anatomy the reference doc describes (§4), not a pixel clone.</summary>
        private sealed class Wheel
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public RadialWedgeGraphic[] Wedges;
            public TextMeshProUGUI[] Labels;
            public Color[] BaseFill;
            public PanelGraphic[] Icons;
            public CircleGraphic Hub;
            public RadialWedgeGraphic Close;
            public TextMeshProUGUI CloseLabel;
            public TextMeshProUGUI Line0, Line1;
            public float Inner, Outer;
            public int Count;
            public Color Fill, Border;
            /// <summary>Multiplies the bloom alpha (a dimmed ring, e.g. while reaching into the world).</summary>
            public float Dim = 1f;

            private string[] _labelSet;
            private float _sector;
            private bool[] _iconOn;

            public static Wheel Make(RectTransform parent, string name, float outer, float inner,
                int maxWedges, Pal p, bool withHub, bool withClose, float labelSize = 8.5f, bool words = true)
            {
                var w = new Wheel();
                w.Group = GroupNode(parent, name);
                w.Root = (RectTransform)w.Group.transform;
                w.Inner = inner;
                w.Outer = outer;
                w.Fill = MulA(p.Panel, 0.95f);
                w.Border = MulA(p.Border, 0.85f);
                w.Wedges = new RadialWedgeGraphic[maxWedges];
                w.Labels = new TextMeshProUGUI[maxWedges];
                w.BaseFill = new Color[maxWedges];
                for (int i = 0; i < maxWedges; i++)
                {
                    w.Wedges[i] = Wedge(w.Root, "w" + i, w.Fill, w.Border, 1.4f);
                    w.BaseFill[i] = w.Fill;
                    if (words) w.Labels[i] = Label(w.Root, "", labelSize, p.Text, 0f, 0f, 60f);
                }
                if (withHub)
                {
                    float hubR = Mathf.Max(4f, inner - 4f);
                    w.Hub = Circle(w.Root, "hub", hubR, MulA(p.Window, 0.92f), w.Border, 1.4f);
                    if (words)
                    {
                        w.Line0 = Label(w.Root, "", labelSize + 0.5f, p.Text, 0f, hubR * 0.30f, hubR * 1.8f);
                        w.Line1 = Label(w.Root, "", labelSize, p.TextDim, 0f, hubR * 0.03f, hubR * 1.8f);
                    }
                    if (withClose)
                    {
                        w.Close = Wedge(w.Root, "close", MulA(p.Raised, 0.85f), w.Border, 1.2f);
                        w.Close.SetGeometry(hubR * 0.52f, hubR * 0.94f,
                            Mathf.PI / 3f, Mathf.PI * 2f / 3f, false);
                        w.Close.RefreshGeometry();
                        if (words)
                            w.CloseLabel = Label(w.Root, "Close", labelSize - 0.5f, p.TextDim,
                                0f, -hubR * 0.73f, hubR * 1.6f);
                    }
                }
                return w;
            }

            public void SetPos(float x, float y) => Root.anchoredPosition = new Vector2(x, y);

            /// <summary>Re-sector the ring. Guarded: only a real count change rebuilds geometry.</summary>
            public void SetCount(int n)
            {
                n = Mathf.Clamp(n, 1, Wedges.Length);
                if (n == Count) return;
                Count = n;
                _sector = Mathf.PI * 2f / n;
                float gap = n > 1 ? 0.055f : 0f;
                float midR = (Inner + Outer) * 0.5f;
                for (int i = 0; i < Wedges.Length; i++)
                {
                    bool used = i < n;
                    Wedges[i].gameObject.SetActive(used);
                    if (Labels[i] != null) Labels[i].gameObject.SetActive(used);
                    if (!used) continue;
                    float a0 = -Mathf.PI * 0.5f - _sector * 0.5f + _sector * i;
                    Wedges[i].SetGeometry(Inner, Outer, a0 + gap * 0.5f, a0 + _sector - gap * 0.5f, n == 1);
                    Wedges[i].SideFeatherLimit = gap * 0.5f * midR;
                    Wedges[i].RefreshGeometry();
                    if (Labels[i] != null)
                    {
                        Labels[i].rectTransform.sizeDelta =
                            new Vector2(Mathf.Max(26f, _sector * midR * 0.96f), Labels[i].fontSize * 1.6f);
                        Labels[i].rectTransform.anchoredPosition = DirOf(i) * midR;
                    }
                }
                LayoutIcons();
            }

            /// <summary>Wedge label text. Reference-compared, so re-passing the same array is free.</summary>
            public void SetLabels(string[] names)
            {
                if (ReferenceEquals(_labelSet, names)) return;
                _labelSet = names;
                if (names == null) return;
                for (int i = 0; i < Count; i++)
                    if (Labels[i] != null) Labels[i].text = i < names.Length ? names[i] : "";
            }

            /// <summary>Small item marks at each wedge's mid radius (the wordless mini layout's
            /// stand-in for icons).</summary>
            public void MakeIcons(float size, Color col)
            {
                Icons = new PanelGraphic[Wedges.Length];
                _iconOn = new bool[Wedges.Length];
                for (int i = 0; i < Wedges.Length; i++)
                {
                    Icons[i] = Panel(Root, "icon" + i, size, size, col, Color.clear, size * 0.25f, 0f);
                    _iconOn[i] = true;
                }
                LayoutIcons();
            }

            public void ShowIcon(int i, bool on)
            {
                if (_iconOn == null || i < 0 || i >= _iconOn.Length) return;
                _iconOn[i] = on;
                Icons[i].gameObject.SetActive(on && i < Count);
            }

            private void LayoutIcons()
            {
                if (Icons == null || Count == 0) return;
                float midR = (Inner + Outer) * 0.5f;
                for (int i = 0; i < Icons.Length; i++)
                {
                    bool used = i < Count;
                    Icons[i].gameObject.SetActive(used && _iconOn[i]);
                    if (used) At(Icons[i], DirOf(i) * midR);
                }
            }

            /// <summary>Unit direction of wedge i's centre (wedge 0 = top).</summary>
            public Vector2 DirOf(int i) => Dir(-Mathf.PI * 0.5f + _sector * i);

            /// <summary>Wedge i's mid angle in the ImGui convention (the action word's anchor).</summary>
            public float MidAngle(int i) => -Mathf.PI * 0.5f + _sector * i;

            /// <summary>A point on wedge i at radius r, in the wheel's LOCAL space.</summary>
            public Vector2 PointOn(int i, float r) => DirOf(i) * r;

            /// <summary>A point on wedge i at radius r, in the SCENE's space.</summary>
            public Vector2 ScenePointOn(int i, float r) => Root.anchoredPosition + DirOf(i) * r;

            /// <summary>Which wedge a SCENE point is over (-1 = inside <paramref name="minR"/>, i.e.
            /// the hub) — RadialMenu.SectorFromMouse's partition, so a cursor path hovers exactly the
            /// wedges it crosses.</summary>
            public int HitTest(Vector2 scenePos, float minR)
            {
                if (Count <= 0) return -1;
                Vector2 d = scenePos - Root.anchoredPosition;
                if (d.sqrMagnitude < minR * minR) return -1;
                float a = Mathf.Atan2(-d.y, d.x);
                float rel = Mathf.Repeat(a + Mathf.PI * 0.5f + _sector * 0.5f, Mathf.PI * 2f);
                return Mathf.Clamp((int)(rel / _sector), 0, Count - 1);
            }

            /// <summary>The swipe chevron (D-022: doubled) at wedge i's rim, pointing outward.</summary>
            public TriangleGraphic Chevron(int i, float size, Color col)
            {
                Vector2 d = DirOf(i);
                var tri = Tri(Root, "chev" + i, size, col, true, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);
                At(tri, d * (Outer - size * 1.15f));
                return tri;
            }

            /// <summary>Per-wedge base fill (a recent-item accent, a grey disabled wedge...).</summary>
            public void SetBase(int i, Color c) { if (i >= 0 && i < BaseFill.Length) BaseFill[i] = c; }

            /// <summary>Tint exactly one wedge toward <paramref name="hot"/>; every other wedge
            /// returns to its base fill. One call per frame drives hover AND commit flashes.</summary>
            public void Tint(int idx, Color hot, float amount)
            {
                for (int i = 0; i < Count; i++)
                    Wedges[i].color = i == idx ? Color.Lerp(BaseFill[i], hot, Mathf.Clamp01(amount)) : BaseFill[i];
            }

            public void TintLabel(int idx, Color hot, Color baseCol, float amount)
            {
                for (int i = 0; i < Count; i++)
                    if (Labels[i] != null)
                        Labels[i].color = i == idx ? Color.Lerp(baseCol, hot, Mathf.Clamp01(amount)) : baseCol;
            }

            /// <summary>The hover pop: wedge idx grows outward by <paramref name="amount"/> px.</summary>
            public void SetBulge(int idx, float amount)
            {
                for (int i = 0; i < Count; i++)
                {
                    float b = i == idx ? amount : 0f;
                    if (!Mathf.Approximately(Wedges[i].OuterBulge, b))
                    {
                        Wedges[i].OuterBulge = b;
                        Wedges[i].RefreshGeometry();
                    }
                }
            }

            /// <summary>Bloom envelope: 0 = gone, 1 = fully open.</summary>
            public void SetBloom(float p)
            {
                p = Mathf.Clamp01(p);
                Group.alpha = p * Dim;
                float s = Mathf.Lerp(0.42f, 1f, Mathf.SmoothStep(0f, 1f, p));
                Root.localScale = new Vector3(s, s, 1f);
            }

            public void SetClose(Color fill) { if (Close != null) Close.color = fill; }

            /// <summary>The Close band's centre, in the SCENE's space.</summary>
            public Vector2 ClosePoint => Root.anchoredPosition + new Vector2(0f, -(Inner - 4f) * 0.72f);
        }

        /// <summary>A rectangular cell grid (the Universal Inventory's slots, schematically).</summary>
        private sealed class Cells
        {
            public RectTransform Root;
            public PanelGraphic[] Cell;
            public PanelGraphic[] Item;
            public int Cols, Rows;
            public float CellW, CellH, GapX, GapY;
            public Color Base, Border;

            public static Cells Make(RectTransform parent, string name, int cols, int rows,
                float cellW, float cellH, float gap, Pal p, float itemFrac = 0.56f)
            {
                var c = new Cells();
                c.Root = Node(parent, name);
                c.Cols = cols; c.Rows = rows;
                c.CellW = cellW; c.CellH = cellH; c.GapX = gap; c.GapY = gap;
                c.Base = MulA(p.Raised, 0.85f);
                c.Border = MulA(p.Border, 0.7f);
                int n = cols * rows;
                c.Cell = new PanelGraphic[n];
                c.Item = new PanelGraphic[n];
                for (int i = 0; i < n; i++)
                {
                    Vector2 at = c.PosOf(i);
                    c.Cell[i] = Panel(c.Root, "c" + i, cellW, cellH, c.Base, c.Border, Mathf.Min(4f, cellH * 0.2f), 1.1f);
                    At(c.Cell[i], at);
                    float s = Mathf.Min(cellW, cellH) * itemFrac;
                    c.Item[i] = Panel(c.Root, "i" + i, s, s, MulA(p.Accent, 0.55f), Color.clear, Mathf.Min(3f, s * 0.25f), 0f);
                    At(c.Item[i], at);
                    c.Item[i].gameObject.SetActive(false);
                }
                return c;
            }

            public Vector2 PosOf(int i)
            {
                int cx = i % Cols;
                int cy = i / Cols;
                float w = Cols * CellW + (Cols - 1) * GapX;
                float h = Rows * CellH + (Rows - 1) * GapY;
                return new Vector2(
                    -w * 0.5f + CellW * 0.5f + cx * (CellW + GapX),
                    h * 0.5f - CellH * 0.5f - cy * (CellH + GapY));
            }

            /// <summary>Position of cell i in the grid PARENT's space (the grid root may be offset).</summary>
            public Vector2 ScenePosOf(int i) => Root.anchoredPosition + PosOf(i);

            public void Occupy(int i, bool on)
            {
                if (i >= 0 && i < Item.Length && Item[i].gameObject.activeSelf != on) Item[i].gameObject.SetActive(on);
            }

            public void SetPos(float x, float y) => Root.anchoredPosition = new Vector2(x, y);
        }

        // =====================================================================================
        //  static.welcome — the title card: two hands, a wheel, one window.
        // =====================================================================================

        private sealed class WelcomeScene : Scene
        {
            public override bool SupportsMini => false;
            private const float Loop = 6f;
            private Pal _p;
            private Wheel _wheel;
            private PanelGraphic _win, _winBar;
            private Cells _cells;
            private PanelGraphic[] _hand;
            private TextMeshProUGUI _capWheel, _capWin, _capHand;

            private static readonly string[] Ghost = { "HUB", "", "", "", "", "" };

            protected override void Make(RectTransform root)
            {
                _p = P;

                _wheel = Wheel.Make(root, "wheel", 52f, 25f, 6, _p, true, true);
                _wheel.SetPos(-140f, 24f);
                _wheel.SetCount(6);
                _wheel.SetLabels(Ghost);
                _wheel.SetBloom(1f);
                _wheel.Line0.text = "WHEELS";

                var winRoot = Node(root, "window");
                winRoot.anchoredPosition = new Vector2(140f, 24f);
                _win = Panel(winRoot, "win", 122f, 128f, MulA(_p.Window, 0.95f), _p.Border, 8f, 1.5f);
                _winBar = Panel(winRoot, "bar", 122f, 18f, MulA(_p.Raised, 0.9f), Color.clear, 6f, 0f);
                At(_winBar, 0f, 55f);
                Label(winRoot, "UNIVERSAL INVENTORY", 7f, _p.TextDim, 0f, 55f, 118f);
                _cells = Cells.Make(winRoot, "cells", 3, 3, 32f, 26f, 5f, _p);
                _cells.SetPos(0f, -12f);
                _cells.Occupy(0, true); _cells.Occupy(1, true); _cells.Occupy(3, true);
                _cells.Occupy(4, true); _cells.Occupy(5, true); _cells.Occupy(7, true);

                var handRoot = Node(root, "hands");
                handRoot.anchoredPosition = new Vector2(0f, -72f);
                // Just the two boxes - no tray shoulders behind them (FlorpyDorp 2026-08-02:
                // the trapezoid read as clutter at demo scale).
                _hand = new PanelGraphic[2];
                for (int i = 0; i < 2; i++)
                {
                    _hand[i] = Panel(handRoot, "box" + i, 62f, 42f, _p.Panel, _p.Border, 6f, 1.4f);
                    At(_hand[i], i == 0 ? -35f : 35f, 0f);
                    Label(handRoot, i == 0 ? "LEFT" : "RIGHT", 8f, _p.TextDim,
                        i == 0 ? -35f : 35f, 13f, 60f);
                }

                _capWheel = Label(root, "RADIALS", 9f, _p.TextMute, -140f, -44f, 120f);
                _capWin = Label(root, "ONE WINDOW", 9f, _p.TextMute, 140f, -54f, 130f);
                _capHand = Label(root, "TWO HANDS - NO HOTBAR", 9f, _p.TextMute, 0f, -26f, 220f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                float a = Bump(u, 0.2f, 2.0f);
                float b = Bump(u, 2.1f, 3.9f);
                float c = Bump(u, 4.0f, 5.8f);

                _wheel.Tint(0, _p.Accent, a * 0.85f);
                _wheel.Line0.color = Color.Lerp(_p.Text, _p.Accent, a);
                _capWheel.color = Color.Lerp(_p.TextMute, _p.Accent, a);

                _win.BorderColor = Color.Lerp(_p.Border, _p.Accent, b);
                _winBar.color = Hot(MulA(_p.Raised, 0.9f), _p.Accent, b * 0.5f);
                _capWin.color = Color.Lerp(_p.TextMute, _p.Accent, b);

                for (int i = 0; i < 2; i++)
                {
                    _hand[i].BorderColor = Color.Lerp(_p.Border, _p.Selected, c);
                    _hand[i].BorderWidth = Mathf.Lerp(1.4f, 2.2f, c);
                }
                _capHand.color = Color.Lerp(_p.TextMute, _p.Selected, c);
            }

            public override void Destroy() { _wheel = null; _cells = null; _hand = null; }
        }

        // =====================================================================================
        //  cursorlatch (legacy) — crosshair vs freed cursor; hold, double-tap latch, tap to re-lock.
        // =====================================================================================

        private sealed class CursorLatchScene : Scene
        {
            public override bool SupportsMini => false;
            private const float Loop = 8f;
            private Pal _p;
            private RectTransform _crossRt, _ringRoot;
            private CanvasGroup _crossGroup;
            private Cursor _cur;
            private CircleGraphic _latchRing;
            private KeyVis _alt;
            private TextMeshProUGUI _cap, _latchTag;
            private int _capIndex = -1;
            private string[] _caps;

            protected override void Make(RectTransform root)
            {
                _p = P;
                string alt = K("{V:MouseControl}");
                _caps = new[]
                {
                    "MOUSE AIMS - WINDOWS ARE LOOK-ONLY",
                    "HOLD " + alt + " = CURSOR FREE (WHILE HELD)",
                    "DOUBLE-TAP " + alt + " = LATCHED FREE",
                    "TAP ONCE MORE = BACK TO AIMING"
                };
                Panel(root, "field", 300f, 132f, MulA(_p.Window, 0.55f),
                    MulA(_p.Border, 0.5f), 10f, 1.2f).rectTransform.anchoredPosition =
                    new Vector2(0f, 14f);

                _crossGroup = GroupNode(root, "crossG");
                _crossRt = (RectTransform)_crossGroup.transform;
                _crossRt.anchoredPosition = new Vector2(0f, 14f);
                Crosshair(_crossRt, "cross", 34f, 2.4f, _p.Accent);

                _ringRoot = Node(root, "ringRoot");
                _latchRing = Circle(_ringRoot, "latch", 17f, Color.clear, _p.Selected, 2f);
                _latchTag = Label(_ringRoot, "latched", 8f, _p.Selected, 0f, -26f, 80f);

                _cur = Cursor.Make(root, "cursor", _p, 18f);
                _cur.SetAlpha(0f);

                _alt = KeyVis.Make(root, "alt", alt, _p, 27f, 13f, 40f);
                _alt.SetPos(0f, -74f);

                _cap = Label(root, _caps[0], 9.5f, _p.TextDim, 0f, 92f, 400f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);

                // Key envelope: hold 1.2-2.6, double tap 3.7/4.05, single tap 6.5.
                float press = 0f;
                if (u >= 1.15f && u < 2.65f) press = Mathf.Min(Seg(u, 1.15f, 1.32f), 1f - Seg(u, 2.48f, 2.65f));
                else if (u >= 3.65f && u < 3.85f) press = Bump(u, 3.65f, 3.85f);
                else if (u >= 3.98f && u < 4.18f) press = Bump(u, 3.98f, 4.18f);
                else if (u >= 6.45f && u < 6.68f) press = Bump(u, 6.45f, 6.68f);
                _alt.SetPressed(press);

                // Free-cursor windows: the temporary hold, then the latched stretch.
                float freeHold = Mathf.Min(Ease(u, 1.22f, 1.44f), 1f - Ease(u, 2.52f, 2.72f));
                float freeLatch = Mathf.Min(Ease(u, 4.05f, 4.30f), 1f - Ease(u, 6.52f, 6.74f));
                float free = Mathf.Clamp01(Mathf.Max(freeHold, freeLatch));

                _crossGroup.alpha = 1f - free;
                _crossRt.localScale = new Vector3(1f + 0.25f * free, 1f + 0.25f * free, 1f);
                _cur.SetAlpha(free);

                // A calm drift so the freed cursor reads as usable, not parked.
                float wx = Mathf.Sin(u * 1.15f) * 46f;
                float wy = 14f + Mathf.Cos(u * 0.83f) * 22f;
                _cur.SetPos(Mathf.Lerp(0f, wx, free), Mathf.Lerp(14f, wy, free));
                _cur.SetClick(0f);

                // Latch glow: only during the latched stretch, breathing so it reads as "held".
                float breathe = 0.5f + 0.5f * Mathf.Sin(u * 5f);
                Color ring = _p.Selected;
                _latchRing.BorderColor = WithA(ring, Mathf.Clamp01(freeLatch * (0.45f + 0.35f * breathe)));
                _latchRing.SetRadius(Mathf.Lerp(13f, 17f, breathe));
                _latchTag.color = WithA(ring, Mathf.Clamp01(freeLatch));
                At(_ringRoot, Pos(_cur.Root));

                int cap = 0;
                if (u >= 1.0f && u < 3.2f) cap = 1;
                else if (u >= 3.5f && u < 6.3f) cap = 2;
                else if (u >= 6.3f) cap = 3;
                if (cap != _capIndex) { _capIndex = cap; _cap.text = _caps[cap]; }
            }

            public override void Destroy() { _cur = null; _alt = null; _crossRt = null; _ringRoot = null; }
        }

        // =====================================================================================
        //  tapvshold (legacy) — the sticky wheel vs the transient flick-release, side by side.
        // =====================================================================================

        private sealed class TapVsHoldScene : Scene
        {
            public override bool SupportsMini => false;
            private const float Loop = 6.5f;
            private Pal _p;
            private Wheel _tapW, _holdW;
            private Cursor _tapC, _holdC;
            private KeyVis _tapK, _holdK;

            private static readonly string[] Faces = { "", "", "", "", "", "" };

            protected override void Make(RectTransform root)
            {
                _p = P;
                string key = K("{UIA_ToolbeltRadial}");
                Label(root, "TAP - THE WHEEL STAYS", 9.5f, _p.TextDim, -108f, 92f, 200f);
                Label(root, "HOLD - RELEASE TO ACT", 9.5f, _p.TextDim, 108f, 92f, 200f);

                _tapW = Wheel.Make(root, "tap", 46f, 22f, 6, _p, true, false);
                _tapW.SetPos(-108f, 14f);
                _tapW.SetCount(6);
                _tapW.SetLabels(Faces);
                _tapW.SetBloom(0f);

                _holdW = Wheel.Make(root, "hold", 46f, 22f, 6, _p, true, false);
                _holdW.SetPos(108f, 14f);
                _holdW.SetCount(6);
                _holdW.SetLabels(Faces);
                _holdW.SetBloom(0f);

                _tapC = Cursor.Make(root, "tapCur", _p, 15f);
                _tapC.SetAlpha(0f);
                _holdC = Cursor.Make(root, "holdCur", _p, 15f);
                _holdC.SetAlpha(0f);

                _tapK = KeyVis.Make(root, "tapKey", key, _p, 24f, 11f, 36f);
                _tapK.SetPos(-108f, -80f);
                _holdK = KeyVis.Make(root, "holdKey", key, _p, 24f, 11f, 36f);
                _holdK.SetPos(108f, -80f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);

                // ---- LEFT: a tap opens a wheel that STAYS, then a click commits and closes.
                _tapK.SetPressed(Bump(u, 0.05f, 0.35f));
                float tapOpen = Mathf.Min(Ease(u, 0.20f, 0.65f), 1f - Ease(u, 2.55f, 2.95f));
                _tapW.SetBloom(tapOpen);
                Vector2 tapHub = _tapW.Root.anchoredPosition;
                Vector2 tapTarget = _tapW.ScenePointOn(2, (_tapW.Inner + _tapW.Outer) * 0.5f);
                float tapMove = Ease(u, 0.85f, 2.05f);
                _tapC.SetAlpha(Mathf.Min(Ease(u, 0.6f, 0.85f), 1f - Ease(u, 2.6f, 2.9f)));
                _tapC.SetPos(Vector2.Lerp(tapHub, tapTarget, tapMove));
                float tapClick = Bump(u, 2.05f, 2.35f);
                _tapC.SetClick(tapClick);
                float tapHot = Mathf.Max(Seg(u, 1.7f, 2.0f) * (1f - Seg(u, 2.05f, 2.10f)) * 0.55f,
                    Bump(u, 2.05f, 2.45f));
                _tapW.Tint(2, _p.Selected, tapHot);

                // ---- RIGHT: the key stays DOWN, the wheel vanishes the instant it is released.
                float holdDown = Mathf.Min(Seg(u, 0.05f, 0.22f), 1f - Seg(u, 2.20f, 2.32f));
                _holdK.SetPressed(holdDown);
                float holdOpen = Mathf.Min(Ease(u, 0.18f, 0.60f), 1f - Ease(u, 2.22f, 2.42f));
                _holdW.SetBloom(holdOpen);
                Vector2 holdHub = _holdW.Root.anchoredPosition;
                Vector2 holdTarget = _holdW.ScenePointOn(4, (_holdW.Inner + _holdW.Outer) * 0.5f);
                _holdC.SetAlpha(Mathf.Min(Ease(u, 0.55f, 0.8f), 1f - Ease(u, 2.3f, 2.5f)));
                _holdC.SetPos(Vector2.Lerp(holdHub, holdTarget, Ease(u, 0.80f, 2.05f)));
                _holdC.SetClick(0f);
                float holdHot = Mathf.Max(Seg(u, 1.75f, 2.05f) * 0.55f, Bump(u, 2.15f, 2.45f));
                _holdW.Tint(4, _p.Selected, holdHot);
            }

            public override void Destroy() { _tapW = null; _holdW = null; _tapC = null; _holdC = null; }
        }

        // =====================================================================================
        //  beltwheel (legacy) — the belt ring, a tool equip, then a dive into THE HUB.
        // =====================================================================================

        private sealed class BeltWheelScene : Scene
        {
            public override bool SupportsMini => false;
            private const float Loop = 8f;
            private Pal _p;
            private Wheel _w;
            private Cursor _cur;
            private int _set = -1;
            private int _line = -1;

            private static readonly string[] Belt =
                { "THE HUB", "WRENCH", "DRILL", "WELDER", "CROWBAR", "CABLE", "GRINDER", "SCANNER" };
            // The 0.9.8.0 Hub root order (BagRadialFeature.BuildHubRoot): recent item, search, worn gear.
            private static readonly string[] Hub =
                { "RECENT", "SEARCH", "HELMET", "GLASSES", "SUIT", "BACKPACK", "UNIFORM", "TOOLBELT" };
            private static readonly string[] Line0 = { "TOOLBELT", "THE HUB" };
            // D-021 retired "LMB select": a click takes, opens, swaps... per wedge.
            private static readonly string[] Line1 = { "click a wedge", "DRILL to hand", "everything you carry" };

            protected override void Make(RectTransform root)
            {
                _p = P;
                _w = Wheel.Make(root, "belt", 80f, 38f, 8, _p, true, true, 8f);
                _w.SetPos(0f, 6f);
                _w.SetCount(8);
                _w.SetLabels(Belt);
                _w.SetBloom(0f);
                _w.Line0.text = Line0[0];
                _w.Line1.text = Line1[0];
                _cur = Cursor.Make(root, "cursor", _p, 16f);
                _cur.SetAlpha(0f);
                Label(root, "TOP WEDGE = THE HUB - BOTTOM BAND = Close", 9f, _p.TextMute, 0f, 100f, 420f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);

                // Two blooms: the belt ring, then the Hub level it dives into.
                float openA = Mathf.Min(Ease(u, 0.05f, 0.50f), 1f - Ease(u, 3.60f, 3.85f));
                float openB = Mathf.Min(Ease(u, 3.90f, 4.35f), 1f - Ease(u, 6.55f, 6.85f));
                bool hubLevel = u >= 3.85f;
                int wantSet = hubLevel ? 1 : 0;
                if (wantSet != _set)
                {
                    _set = wantSet;
                    _w.SetLabels(wantSet == 0 ? Belt : Hub);
                    _w.Line0.text = Line0[wantSet];
                }
                _w.SetBloom(hubLevel ? openB : openA);

                float midR = (_w.Inner + _w.Outer) * 0.5f;
                Vector2 hub = _w.Root.anchoredPosition;
                int hot = -1;
                float hotAmt = 0f;
                Vector2 cursor = hub;
                float click = 0f;
                float cursorA = 0f;

                if (u < 3.85f)
                {
                    // Sweep onto the DRILL wedge (index 2) and click it: the tool goes to hand.
                    cursorA = Mathf.Min(Ease(u, 0.45f, 0.70f), 1f - Ease(u, 3.60f, 3.80f));
                    Vector2 tool = _w.ScenePointOn(2, midR);
                    Vector2 hubWedge = _w.ScenePointOn(0, midR);
                    cursor = Vector2.Lerp(hub, tool, Ease(u, 0.70f, 1.60f));
                    cursor = Vector2.Lerp(cursor, hubWedge, Ease(u, 2.60f, 3.45f));
                    click = Mathf.Max(Bump(u, 1.60f, 1.95f), Bump(u, 3.45f, 3.80f));
                    if (u < 2.6f) { hot = 2; hotAmt = Mathf.Max(Seg(u, 1.35f, 1.60f) * 0.5f, Bump(u, 1.60f, 2.10f)); }
                    else { hot = 0; hotAmt = Mathf.Max(Seg(u, 3.2f, 3.45f) * 0.5f, Bump(u, 3.45f, 3.85f)); }
                }
                else
                {
                    // Inside the Hub: hover a worn slot, then leave through the Close band.
                    cursorA = Mathf.Min(Ease(u, 4.30f, 4.55f), 1f - Ease(u, 6.60f, 6.85f));
                    Vector2 slot = _w.ScenePointOn(3, midR);
                    Vector2 close = _w.ClosePoint;
                    cursor = Vector2.Lerp(hub, slot, Ease(u, 4.40f, 5.20f));
                    cursor = Vector2.Lerp(cursor, close, Ease(u, 5.70f, 6.45f));
                    click = Bump(u, 6.45f, 6.75f);
                    hot = 3;
                    hotAmt = Seg(u, 5.00f, 5.25f) * (1f - Seg(u, 5.70f, 5.95f)) * 0.55f;
                }

                // Set unconditionally: leaving the flash on when the branch stops running would
                // strand the band lit for the rest of the loop.
                _w.SetClose(Color.Lerp(MulA(_p.Raised, 0.85f), _p.Selected, Bump(u, 6.30f, 6.80f)));
                _w.Tint(hot, _p.Selected, hotAmt);
                _w.TintLabel(hot, _p.Selected, _p.Text, hotAmt);
                _cur.SetAlpha(cursorA);
                _cur.SetPos(cursor);
                _cur.SetClick(click);

                int wantLine = 0;
                if (u >= 1.95f && u < 3.85f) wantLine = 1;
                else if (u >= 3.85f) wantLine = 2;
                if (wantLine != _line) { _line = wantLine; _w.Line1.text = Line1[wantLine]; }
            }

            public override void Destroy() { _w = null; _cur = null; }
        }

        // =====================================================================================
        //  static.equipkeys (legacy) — the six worn slots, tap vs hold, with a travelling highlight.
        // =====================================================================================

        private sealed class EquipKeysScene : Scene
        {
            public override bool SupportsMini => false;
            private const float Loop = 6f;
            private Pal _p;
            private PanelGraphic[] _box;
            private PanelGraphic[] _digit;
            private TextMeshProUGUI[] _name;
            private TextMeshProUGUI[] _num;
            private PanelGraphic _tapChip, _holdChip;
            private Color _ink;

            private static readonly string[] Slots =
                { "HELMET", "GLASSES", "SUIT", "BACK", "UNIFORM", "BELT" };

            protected override void Make(RectTransform root)
            {
                _p = P;
                // Opaque dark ink for the digit while its chip is lit — lerping toward the (part
                // transparent) window colour would fade the number out instead of inverting it.
                // Rule-8 content exemption (deliberate): a fixed legibility ink inside the mock, not a
                // HUD knob - no theme travel, no per-tier fork, nothing to migrate.
                _ink = new Color(0.05f, 0.06f, 0.07f, 1f);
                _box = new PanelGraphic[6];
                _digit = new PanelGraphic[6];
                _name = new TextMeshProUGUI[6];
                _num = new TextMeshProUGUI[6];

                var col = Node(root, "column");
                col.anchoredPosition = new Vector2(-72f, 0f);
                for (int i = 0; i < 6; i++)
                {
                    float y = 82f - i * 33f;
                    _box[i] = Panel(col, "b" + i, 152f, 27f, MulA(_p.Panel, 0.95f), _p.Border, 5f, 1.3f);
                    At(_box[i], 0f, y);
                    _digit[i] = Panel(col, "d" + i, 21f, 19f, MulA(_p.Raised, 0.95f),
                        MulA(_p.Border, 0.8f), 4f, 1.1f);
                    At(_digit[i], -62f, y);
                    _num[i] = Label(col, K(GearTokens[i]), 11f, _p.Text, -62f, y, 26f);
                    _name[i] = Label(col, Slots[i], 10f, _p.TextDim, 14f, y, 110f,
                        TextAlignmentOptions.Left);
                }

                var chips = Node(root, "chips");
                chips.anchoredPosition = new Vector2(126f, 0f);
                _tapChip = Panel(chips, "tap", 118f, 30f, MulA(_p.Raised, 0.95f), _p.Border, 7f, 1.4f);
                At(_tapChip, 0f, 26f);
                Label(chips, "TAP = wheel", 10f, _p.Text, 0f, 26f, 118f);
                _holdChip = Panel(chips, "hold", 118f, 30f, MulA(_p.Raised, 0.95f), _p.Border, 7f, 1.4f);
                At(_holdChip, 0f, -14f);
                Label(chips, "HOLD = equip", 10f, _p.Text, 0f, -14f, 118f);
                Label(chips, "empty slot: don it", 8.5f, _p.TextMute, 0f, -46f, 130f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                int lit = Mathf.Clamp((int)(u / (Loop / 6f)), 0, 5);
                float local = Mathf.Repeat(u, Loop / 6f) / (Loop / 6f);
                float amt = Mathf.Sin(local * Mathf.PI);

                for (int i = 0; i < 6; i++)
                {
                    float k = i == lit ? amt : 0f;
                    _box[i].color = Hot(MulA(_p.Panel, 0.95f), _p.Accent, k * 0.3f);
                    _box[i].BorderColor = Color.Lerp(_p.Border, _p.Accent, k);
                    _box[i].BorderWidth = Mathf.Lerp(1.3f, 2.1f, k);
                    _digit[i].color = Color.Lerp(MulA(_p.Raised, 0.95f), _p.Accent, k * 0.8f);
                    _num[i].color = Color.Lerp(_p.Text, _ink, k * 0.8f);
                    _name[i].color = Color.Lerp(_p.TextDim, _p.Text, k);
                }

                // The two chips trade emphasis so both halves of the grammar keep reading.
                float tap = 0.5f + 0.5f * Mathf.Sin(u * 1.05f);
                _tapChip.BorderColor = Color.Lerp(_p.Border, _p.Accent, tap);
                _holdChip.BorderColor = Color.Lerp(_p.Border, _p.Selected, 1f - tap);
            }

            public override void Destroy() { _box = null; _digit = null; _name = null; _num = null; }
        }

        /// <summary>The six vanilla gear keys, in worn-slot order (KeyManager names, read live).</summary>
        private static readonly string[] GearTokens =
        {
            "{V:HelmetSlot}", "{V:GlassesSlot}", "{V:SuitSlot}", "{V:BackSlot}", "{V:UniformSlot}", "{V:ToolBeltSlot}"
        };

        // =====================================================================================
        //  swipechild (legacy) — push THROUGH a wedge past the rim, dwell, a satellite blooms.
        // =====================================================================================

        private sealed class SwipeChildScene : Scene
        {
            public override bool SupportsMini => false;
            // The gesture is the whole scene, so it simply repeats: two full runs per ~8 s of
            // screen time (plan: "loop, twice per cycle").
            private const float Cycle = 4f;
            private Pal _p;
            private Wheel _main, _child;
            private Cursor _cur;
            private TriangleGraphic _chev;
            private RadialWedgeGraphic _dwell;
            private RectTransform _dwellRoot;
            private const int Branch = 1;

            private static readonly string[] MainLabels = { "", "BATTERY", "", "", "", "" };
            private static readonly string[] ChildLabels = { "TAKE", "REPLACE", "SETTINGS" };

            protected override void Make(RectTransform root)
            {
                _p = P;
                _main = Wheel.Make(root, "main", 64f, 30f, 6, _p, true, false, 8f);
                _main.SetPos(-52f, -6f);
                _main.SetCount(6);
                _main.SetLabels(MainLabels);
                _main.SetBloom(1f);
                _main.Line0.text = "SLIDE OUT";
                _main.Line1.text = "past the rim";

                // The rim chevron that advertises real child content (D-022: doubled).
                _chev = _main.Chevron(Branch, 12f, _p.Accent);

                _dwellRoot = Node(root, "dwellRoot");
                _dwell = Wedge(_dwellRoot, "dwell", _p.Accent, Color.clear, 0f);
                _dwell.FeatherSides = true;
                _dwell.SetGeometry(7.5f, 10.5f, -Mathf.PI * 0.5f, -Mathf.PI * 0.5f + 0.01f, false);
                _dwell.RefreshGeometry();

                _child = Wheel.Make(root, "child", 36f, 15f, 3, _p, false, false, 7.5f);
                _child.SetCount(3);
                _child.SetLabels(ChildLabels);
                _child.SetBloom(0f);
                Vector2 childAt = _main.ScenePointOn(Branch, _main.Outer + 44f);
                _child.SetPos(childAt.x, childAt.y);

                _cur = Cursor.Make(root, "cursor", _p, 16f);
                Label(root, "PUSH THROUGH THE WEDGE - HOLD STILL - A CHILD WHEEL POPS OUT",
                    8.5f, _p.TextMute, 0f, 101f, 430f);
                Label(root, "pull back to dismiss", 8.5f, _p.TextMute, 70f, -90f, 200f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Cycle);
                Vector2 hub = _main.Root.anchoredPosition;
                Vector2 mid = _main.ScenePointOn(Branch, (_main.Inner + _main.Outer) * 0.5f);
                Vector2 past = _main.ScenePointOn(Branch, _main.Outer + 17f);

                // in -> push past the rim -> dwell -> hold -> pull back
                Vector2 at = Vector2.Lerp(hub, mid, Ease(u, 0.05f, 1.00f));
                at = Vector2.Lerp(at, past, Ease(u, 1.00f, 1.55f));
                at = Vector2.Lerp(at, hub, Ease(u, 3.00f, 3.50f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.0f, 0.18f), 1f - Ease(u, 3.50f, 3.75f)));
                _cur.SetClick(0f);

                float hover = Mathf.Min(Ease(u, 0.65f, 1.00f), 1f - Ease(u, 3.20f, 3.45f));
                _main.Tint(Branch, _p.Accent, hover * 0.7f);
                _chev.color = Color.Lerp(MulA(_p.Accent, 0.55f), _p.Accent, hover);

                // The dwell ring literally fills (0.18 s in the real thing, stretched for legibility).
                float fill = Mathf.Min(Seg(u, 1.55f, 1.95f), 1f - Seg(u, 2.25f, 2.40f));
                _dwellRoot.anchoredPosition = past;
                if (fill > 0.002f)
                {
                    float sweep = Mathf.Max(0.02f, Seg(u, 1.55f, 1.95f) * Mathf.PI * 2f);
                    _dwell.SetGeometry(7.5f, 10.5f, -Mathf.PI * 0.5f, -Mathf.PI * 0.5f + sweep, false);
                }
                _dwell.color = WithA(_p.Accent, fill);

                float bloom = Mathf.Min(Ease(u, 1.95f, 2.40f), 1f - Ease(u, 3.05f, 3.35f));
                _child.SetBloom(bloom);
                _child.Tint(-1, _p.Accent, 0f);
            }

            public override void Destroy() { _main = null; _child = null; _cur = null; }
        }

        // =====================================================================================
        //  batteryswap (legacy) — wedge -> child -> Replace list -> atomic swap.
        // =====================================================================================

        private sealed class BatterySwapScene : Scene
        {
            public override bool SupportsMini => false;
            private const float Loop = 8f;
            private Pal _p;
            private Wheel _main, _child;
            private Cursor _cur;
            private RadialWedgeGraphic _dwell;
            private RectTransform _dwellRoot;
            private PanelGraphic _swapChip;
            private CanvasGroup _swapGroup;
            private TriangleGraphic _chev;
            private int _stage = -1;
            private int _line = -1;
            private const int Batt = 1;

            private static readonly string[] ToolRing =
                { "MODE", "BATTERY 12%", "SETTINGS", "OPEN", "STOW" };
            // The Replace list leads with EJECT (take the old one out), as ItemMenuBuilder builds it.
            private static readonly string[] ReplaceRing = { "EJECT", "98%", "45%" };
            private static readonly string[] ChildRing = { "TAKE", "REPLACE" };
            private static readonly string[] Line0 = { "POWER TOOL", "REPLACE WITH" };
            private static readonly string[] Line1 =
                { "slide out on the battery", "TAKE or REPLACE", "every battery you carry", "swapped" };

            protected override void Make(RectTransform root)
            {
                _p = P;
                _main = Wheel.Make(root, "main", 70f, 33f, 5, _p, true, false, 8f);
                _main.SetPos(-74f, 2f);
                _main.SetCount(5);
                _main.SetLabels(ToolRing);
                _main.SetBloom(0f);
                _main.Line0.text = Line0[0];
                _main.Line1.text = Line1[0];

                _chev = _main.Chevron(Batt, 12f, _p.Accent);

                _dwellRoot = Node(root, "dwellRoot");
                _dwell = Wedge(_dwellRoot, "dwell", _p.Accent, Color.clear, 0f);
                _dwell.SetGeometry(7.5f, 10.5f, -Mathf.PI * 0.5f, -Mathf.PI * 0.5f + 0.01f, false);
                _dwell.RefreshGeometry();

                _child = Wheel.Make(root, "child", 34f, 14f, 2, _p, false, false, 7.5f);
                _child.SetCount(2);
                _child.SetLabels(ChildRing);
                _child.SetBloom(0f);
                Vector2 childAt = _main.ScenePointOn(Batt, _main.Outer + 44f);
                _child.SetPos(childAt.x, childAt.y);

                _swapGroup = GroupNode(root, "swapChip");
                ((RectTransform)_swapGroup.transform).anchoredPosition = new Vector2(120f, -66f);
                _swapChip = Panel((RectTransform)_swapGroup.transform, "chip", 86f, 26f,
                    MulA(_p.Good, 0.25f), _p.Good, 7f, 1.6f);
                Label((RectTransform)_swapGroup.transform, "swapped", 10f, _p.Good, 0f, 0f, 86f);
                _swapGroup.alpha = 0f;

                _cur = Cursor.Make(root, "cursor", _p, 16f);
                _cur.SetAlpha(0f);
                Label(root, "TAP " + K("{UIA_ToolRadial}") + " - SLIDE OUT ON THE BATTERY - REPLACE - PICK ONE",
                    8.5f, _p.TextMute, 0f, 100f, 430f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                bool listLevel = u >= 3.55f;
                int wantStage = listLevel ? 1 : 0;
                if (wantStage != _stage)
                {
                    _stage = wantStage;
                    _main.SetCount(wantStage == 0 ? 5 : 3);
                    _main.SetLabels(wantStage == 0 ? ToolRing : ReplaceRing);
                    _main.Line0.text = Line0[wantStage];
                    // Reset every label first — the two rings colour different indices, and a
                    // leftover tint would repaint the wrong entry next loop.
                    for (int i = 0; i < _main.Labels.Length; i++) _main.Labels[i].color = _p.Text;
                    // Only the low-charge battery reads red; the replacement list reads neutral.
                    if (wantStage == 0) _main.Labels[Batt].color = _p.Crit;
                    else _main.Labels[0].color = _p.TextMute;   // the dim EJECT entry
                    _chev.gameObject.SetActive(wantStage == 0);
                }

                float ringA = Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 3.30f, 3.50f));
                float ringB = Mathf.Min(Ease(u, 3.60f, 4.05f), 1f - Ease(u, 5.55f, 5.90f));
                _main.SetBloom(listLevel ? ringB : ringA);

                Vector2 hub = _main.Root.anchoredPosition;
                Vector2 at = hub;
                float click = 0f;
                float curA = 0f;
                int hot = -1;
                float hotAmt = 0f;

                if (!listLevel)
                {
                    Vector2 mid = _main.ScenePointOn(Batt, (_main.Inner + _main.Outer) * 0.5f);
                    Vector2 past = _main.ScenePointOn(Batt, _main.Outer + 17f);
                    Vector2 replace = _child.Root.anchoredPosition
                        + _child.DirOf(1) * ((_child.Inner + _child.Outer) * 0.5f);
                    at = Vector2.Lerp(hub, mid, Ease(u, 0.40f, 1.20f));
                    at = Vector2.Lerp(at, past, Ease(u, 1.20f, 1.65f));
                    at = Vector2.Lerp(at, replace, Ease(u, 2.35f, 3.05f));
                    curA = Mathf.Min(Ease(u, 0.30f, 0.50f), 1f - Ease(u, 3.30f, 3.50f));
                    click = Bump(u, 3.05f, 3.35f);
                    hot = Batt;
                    hotAmt = Mathf.Min(Ease(u, 0.90f, 1.20f), 1f - Ease(u, 3.10f, 3.30f)) * 0.65f;

                    float fill = Mathf.Min(Seg(u, 1.65f, 1.95f), 1f - Seg(u, 2.15f, 2.30f));
                    _dwellRoot.anchoredPosition = past;
                    if (fill > 0.002f)
                    {
                        float sweep = Mathf.Max(0.02f, Seg(u, 1.65f, 1.95f) * Mathf.PI * 2f);
                        _dwell.SetGeometry(7.5f, 10.5f, -Mathf.PI * 0.5f, -Mathf.PI * 0.5f + sweep, false);
                    }
                    _dwell.color = WithA(_p.Accent, fill);

                    float childBloom = Mathf.Min(Ease(u, 1.95f, 2.35f), 1f - Ease(u, 3.20f, 3.50f));
                    _child.SetBloom(childBloom);
                    _child.Tint(1, _p.Selected,
                        Mathf.Max(Seg(u, 2.80f, 3.05f) * 0.55f, Bump(u, 3.05f, 3.45f)));
                }
                else
                {
                    _dwell.color = Color.clear;
                    _child.SetBloom(0f);
                    Vector2 pick = _main.ScenePointOn(1, (_main.Inner + _main.Outer) * 0.5f);
                    at = Vector2.Lerp(hub, pick, Ease(u, 4.05f, 4.95f));
                    curA = Mathf.Min(Ease(u, 3.95f, 4.15f), 1f - Ease(u, 5.35f, 5.60f));
                    click = Bump(u, 4.95f, 5.25f);
                    hot = 1;
                    hotAmt = Mathf.Max(Seg(u, 4.70f, 4.95f) * 0.55f, Bump(u, 4.95f, 5.40f));
                }

                _main.Tint(hot, _p.Selected, hotAmt);
                _cur.SetAlpha(curA);
                _cur.SetPos(at);
                _cur.SetClick(click);

                _swapGroup.alpha = Mathf.Min(Ease(u, 5.45f, 5.70f), 1f - Ease(u, 7.10f, 7.45f));
                float pop = Bump(u, 5.45f, 6.10f);
                ((RectTransform)_swapGroup.transform).localScale =
                    new Vector3(1f + 0.16f * pop, 1f + 0.16f * pop, 1f);
                _swapChip.BorderWidth = Mathf.Lerp(1.6f, 2.6f, pop);

                int wantLine = 0;
                if (u >= 2.35f && u < 3.05f) wantLine = 1;
                else if (u >= 3.05f && u < 5.30f) wantLine = 2;
                else if (u >= 5.30f) wantLine = 3;
                if (wantLine != _line) { _line = wantLine; _main.Line1.text = Line1[wantLine]; }
            }

            public override void Destroy() { _main = null; _child = null; _cur = null; }
        }

        // =====================================================================================
        //  valueandpark (legacy) — a scroll-value wedge (+ fine steps) and chip parking / dumping.
        // =====================================================================================

        private sealed class ValueParkScene : Scene
        {
            public override bool SupportsMini => false;
            private const float Loop = 8f;
            private Pal _p;

            // left
            private PanelGraphic _valueBox;
            private TextMeshProUGUI _value;
            private TriangleGraphic _up, _down;
            private PanelGraphic _scrollChip, _scrollDot;
            private KeyVis _fine;
            private int _valIndex = -1;
            private string[] _coarse, _fineVals;

            // right
            private Wheel _w;
            private Cursor _cur;
            private PanelGraphic[] _chip;
            private CircleGraphic[] _ring;
            private CanvasGroup[] _chipGroup;
            private static readonly string[] Ring = { "TANK", "CANISTER", "SPARE" };

            protected override void Make(RectTransform root)
            {
                _p = P;
                string fine = K("{UIA_FineAdjust}");
                _coarse = new string[6];
                for (int i = 0; i < 6; i++) _coarse[i] = "PRESSURE " + (101 + i * 10) + ".3";
                _fineVals = new string[6];
                for (int i = 0; i < 6; i++) _fineVals[i] = "PRESSURE " + (151 + i) + ".3";

                var left = Node(root, "left");
                left.anchoredPosition = new Vector2(-118f, 0f);
                Label(left, "SCROLL A VALUE WEDGE", 9f, _p.TextMute, 0f, 92f, 190f);
                _valueBox = Panel(left, "val", 168f, 46f, MulA(_p.Panel, 0.95f), _p.Border, 9f, 1.5f);
                At(_valueBox, 0f, 40f);
                _value = Label(left, _coarse[0], 12f, _p.Text, -10f, 40f, 140f);
                _up = Tri(left, "up", 11f, _p.Accent, true, 0f);
                At(_up, 66f, 50f);
                _down = Tri(left, "dn", 11f, _p.Accent, false, 0f);
                At(_down, 66f, 30f);

                _scrollChip = Panel(left, "scroll", 74f, 30f, MulA(_p.Raised, 0.95f), _p.Border, 7f, 1.4f);
                At(_scrollChip, -44f, -18f);
                Label(left, "SCROLL", 9f, _p.Text, -38f, -18f, 66f);
                _scrollDot = Panel(left, "dot", 4.5f, 9f, _p.Accent, Color.clear, 2.2f, 0f);
                At(_scrollDot, -74f, -18f);
                _fine = KeyVis.Make(left, "fine", fine, _p, 24f, 12f, 34f);
                _fine.SetPos(34f, -18f);
                _fine.SetAlpha(0f);
                Label(left, "hold " + fine + " = fine +-1", 8.5f, _p.TextMute, 6f, -46f, 180f);

                var right = Node(root, "right");
                right.anchoredPosition = new Vector2(118f, 0f);
                Label(right, "DRAG OFF - PARK - Close DROPS", 9f, _p.TextMute, 0f, 92f, 200f);
                _w = Wheel.Make(right, "wheel", 44f, 21f, 3, _p, true, true, 7f);
                _w.SetPos(0f, 34f);
                _w.SetCount(3);
                _w.SetLabels(Ring);
                _w.SetBloom(0f);
                _w.Line0.text = "BAG";

                _chip = new PanelGraphic[2];
                _ring = new CircleGraphic[2];
                _chipGroup = new CanvasGroup[2];
                for (int i = 0; i < 2; i++)
                {
                    _chipGroup[i] = GroupNode(right, "chip" + i);
                    var cr = (RectTransform)_chipGroup[i].transform;
                    _ring[i] = Circle(cr, "ring", 16f, Color.clear, MulA(_p.Accent, 0.6f), 1.4f);
                    _chip[i] = Panel(cr, "c", 22f, 22f, MulA(_p.Accent, 0.5f), _p.Border, 4f, 1.2f);
                    _chipGroup[i].alpha = 0f;
                }

                _cur = Cursor.Make(right, "cursor", _p, 15f);
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                TickLeft(u);
                TickRight(u);
            }

            private void TickLeft(float u)
            {
                // Coarse +-10 steps, then the fine key appears and the steps become +-1.
                int idx;
                if (u < 3.0f) idx = Mathf.Clamp((int)(Seg(u, 0.30f, 3.00f) * 5.999f), 0, 5);
                else idx = 6 + Mathf.Clamp((int)(Seg(u, 3.40f, 6.00f) * 5.999f), 0, 5);
                if (idx != _valIndex)
                {
                    _valIndex = idx;
                    _value.text = idx < 6 ? _coarse[idx] : _fineVals[idx - 6];
                }

                bool scrolling = (u > 0.30f && u < 3.00f) || (u > 3.40f && u < 6.00f);
                float dot = Mathf.Repeat(u * 3.2f, 1f);
                At(_scrollDot, -74f, -18f + (scrolling ? Mathf.Lerp(6f, -6f, dot) : 0f));
                _scrollChip.BorderColor = Color.Lerp(_p.Border, _p.Accent, scrolling ? 1f : 0f);
                _up.color = WithA(_p.Accent, scrolling ? 1f : 0.35f);
                _down.color = WithA(_p.Accent, scrolling ? 1f : 0.35f);
                _valueBox.BorderColor = Color.Lerp(_p.Border, _p.Accent, scrolling ? 0.6f : 0f);

                float fineOn = Mathf.Min(Ease(u, 3.05f, 3.40f), 1f - Ease(u, 6.60f, 6.95f));
                _fine.SetAlpha(fineOn);
                _fine.SetPressed(fineOn);
                _value.color = Color.Lerp(_p.Text, _p.Accent, fineOn * 0.45f);
            }

            private void TickRight(float u)
            {
                float open = Mathf.Min(Ease(u, 0.10f, 0.45f), 1f - Ease(u, 5.05f, 5.40f));
                _w.SetBloom(open);

                Vector2 hub = _w.Root.anchoredPosition;
                float midR = (_w.Inner + _w.Outer) * 0.5f;
                Vector2 srcA = _w.ScenePointOn(1, midR);
                Vector2 srcB = _w.ScenePointOn(2, midR);
                Vector2 parkA = new Vector2(-56f, -46f);
                Vector2 parkB = new Vector2(52f, -46f);
                Vector2 close = _w.ClosePoint;

                Vector2 at = Vector2.Lerp(hub, srcA, Ease(u, 0.60f, 1.30f));
                at = Vector2.Lerp(at, parkA, Ease(u, 1.30f, 2.05f));
                at = Vector2.Lerp(at, srcB, Ease(u, 2.25f, 2.95f));
                at = Vector2.Lerp(at, parkB, Ease(u, 2.95f, 3.65f));
                at = Vector2.Lerp(at, close, Ease(u, 3.95f, 4.75f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.45f, 0.65f), 1f - Ease(u, 5.10f, 5.35f)));
                _cur.SetClick(Mathf.Max(
                    Mathf.Min(Seg(u, 1.25f, 1.32f), 1f - Seg(u, 2.00f, 2.08f)),
                    Mathf.Max(Mathf.Min(Seg(u, 2.90f, 2.97f), 1f - Seg(u, 3.60f, 3.68f)),
                        Bump(u, 4.75f, 5.05f))));

                // Chip A rides the cursor while dragged, then parks with its dot ring.
                float dropY = -Mathf.Max(0f, Seg(u, 5.05f, 5.75f)) * 190f;
                TickChip(0, u, 1.25f, 2.05f, at, parkA, dropY);
                TickChip(1, u, 2.90f, 3.65f, at, parkB, dropY);

                _w.SetClose(Color.Lerp(MulA(_p.Raised, 0.85f), _p.Selected, Bump(u, 4.60f, 5.10f)));
                _w.Tint(u < 2.2f ? 1 : (u < 3.9f ? 2 : -1), _p.Accent,
                    Mathf.Max(Seg(u, 1.05f, 1.30f) * (1f - Seg(u, 1.30f, 1.55f)),
                        Seg(u, 2.70f, 2.95f) * (1f - Seg(u, 2.95f, 3.20f))) * 0.6f);
            }

            private void TickChip(int i, float u, float grab, float drop, Vector2 cursor,
                Vector2 park, float dropY)
            {
                float alive = Mathf.Min(Seg(u, grab, grab + 0.06f), 1f - Seg(u, 5.75f, 6.05f));
                _chipGroup[i].alpha = alive;
                bool dragging = u >= grab && u < drop;
                Vector2 at = dragging ? cursor + new Vector2(10f, -10f) : park;
                if (u >= 5.05f) at = new Vector2(park.x, park.y + dropY);
                ((RectTransform)_chipGroup[i].transform).anchoredPosition = at;
                float parked = Mathf.Clamp01(Seg(u, drop, drop + 0.25f));
                _ring[i].BorderColor = WithA(_p.Accent, 0.65f * parked);
                _ring[i].SetRadius(Mathf.Lerp(11f, 16f, parked));
            }

            public override void Destroy() { _w = null; _cur = null; _chip = null; _ring = null; _chipGroup = null; }
        }

        // =====================================================================================
        //  gridpeeklatch (legacy) — hold the grid key peeks, tap latches, a manila tab folds open.
        // =====================================================================================

        private sealed class GridPeekLatchScene : Scene
        {
            public override bool SupportsMini => false;
            private const float Loop = 8f;
            private Pal _p;
            private RectTransform _win;
            private CanvasGroup _winGroup;
            private PanelGraphic _panel, _bar, _tab;
            private TriangleGraphic _chev;
            private Cells _cells;
            private CanvasGroup _cellGroup;
            private KeyVis _key;
            private TextMeshProUGUI _cap;
            private int _capIndex = -1;
            private string[] _caps;

            protected override void Make(RectTransform root)
            {
                _p = P;
                string b = K("{UIA_Grid}");
                _caps = new[]
                {
                    "HOLD " + b + " - A QUICK PEEK (HIDES ON RELEASE)",
                    "TAP " + b + " - IT STAYS OPEN",
                    "CLICK A FOLDER TAB TO OPEN THAT BAG",
                    "TAP " + b + " AGAIN - CLOSED"
                };
                _cap = Label(root, _caps[0], 9.5f, _p.TextDim, 0f, 96f, 430f);

                _winGroup = GroupNode(root, "win");
                _win = (RectTransform)_winGroup.transform;
                _panel = Panel(_win, "panel", 210f, 148f, MulA(_p.Window, 0.96f), _p.Border, 9f, 1.6f);
                _bar = Panel(_win, "bar", 210f, 20f, MulA(_p.Raised, 0.92f), Color.clear, 7f, 0f);
                At(_bar, 0f, 64f);
                Label(_win, "UNIVERSAL INVENTORY", 8f, _p.TextDim, -6f, 64f, 190f);
                XGlyph(_win, "x", 9f, _p.TextDim).anchoredPosition = new Vector2(92f, 64f);

                _tab = Trap(_win, "tab", 84f, 20f, MulA(_p.Warn, 0.30f), MulA(_p.Warn, 0.85f),
                    4f, 8f, 0f);
                At(_tab, -56f, 40f);
                Label(_win, "TOOLBELT", 8f, _p.Text, -52f, 40f, 70f);
                _chev = Tri(_win, "chev", 8f, _p.Text, true, -90f);
                At(_chev, -88f, 40f);

                _cellGroup = GroupNode(_win, "cellG");
                _cells = Cells.Make((RectTransform)_cellGroup.transform, "cells", 3, 3, 52f, 27f, 6f, _p);
                _cells.SetPos(0f, -22f);
                for (int i = 0; i < 9; i++) _cells.Occupy(i, i != 4 && i != 7);
                _cellGroup.alpha = 0f;

                _key = KeyVis.Make(root, "key", b, _p, 26f, 13f, 40f);
                _key.SetPos(158f, -78f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);

                float press = 0f;
                if (u >= 0.40f && u < 2.05f) press = Mathf.Min(Seg(u, 0.40f, 0.55f), 1f - Seg(u, 1.90f, 2.05f));
                else if (u >= 2.90f && u < 3.10f) press = Bump(u, 2.90f, 3.10f);
                else if (u >= 6.40f && u < 6.60f) press = Bump(u, 6.40f, 6.60f);
                _key.SetPressed(press);

                // Peek: up while held. Latch: up until the second tap.
                float peek = Mathf.Min(Ease(u, 0.55f, 0.95f), 1f - Ease(u, 2.05f, 2.45f));
                float latch = Mathf.Min(Ease(u, 3.05f, 3.50f), 1f - Ease(u, 6.55f, 7.00f));
                float show = Mathf.Max(peek, latch);
                _winGroup.alpha = show;
                _win.anchoredPosition = new Vector2(0f, Mathf.Lerp(-118f, 8f, show));

                float folded = Mathf.Min(Ease(u, 3.90f, 4.25f), 1f - Ease(u, 6.55f, 6.85f));
                _cellGroup.alpha = Mathf.Min(Ease(u, 4.20f, 4.80f), 1f - Ease(u, 6.55f, 6.85f));
                _chev.transform.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(-90f, 0f, folded));
                _tab.color = Color.Lerp(MulA(_p.Warn, 0.30f), MulA(_p.Warn, 0.55f), folded);
                float latchFlash = Bump(u, 3.05f, 3.60f);
                _panel.BorderColor = Color.Lerp(_p.Border, _p.Accent, latchFlash * 0.6f);
                _bar.color = Hot(MulA(_p.Raised, 0.92f), _p.Accent, latchFlash * 0.45f);

                int cap = 0;
                if (u >= 2.55f && u < 3.85f) cap = 1;
                else if (u >= 3.85f && u < 6.35f) cap = 2;
                else if (u >= 6.35f) cap = 3;
                if (cap != _capIndex) { _capIndex = cap; _cap.text = _caps[cap]; }
            }

            public override void Destroy() { _cells = null; _key = null; }
        }

        // =====================================================================================
        //  gridregimes (legacy) — captured (highlight + select key) vs freed (click + drag).
        // =====================================================================================

        private sealed class GridRegimesScene : Scene
        {
            public override bool SupportsMini => false;
            private const float Loop = 8f;
            private Pal _p;
            private Cells _cells;
            private PanelGraphic _hand, _handItem, _hilite;
            private RectTransform _flyRt;
            private CanvasGroup _flyGroup, _capturedChips, _freeChips;
            private KeyVis _fKey;
            private Cursor _cur;
            private TextMeshProUGUI _cap;
            private int _capIndex = -1;
            private string[] _caps;
            /// <summary>The window node's offset inside the scene — the flyer and the cursor live on
            /// the scene root, the cells live inside the window, so one of the two needs it.</summary>
            private Vector2 _gridOff;
            private static readonly int[] Steps = { 0, 1, 5, 6, 7 };
            private static readonly Vector2 HandAt = new Vector2(-42f, -88f);
            private const int TakenCell = 5;   // Steps[2] — the cell the select key empties in phase 1

            protected override void Make(RectTransform root)
            {
                _p = P;
                string f = K("{V:InventorySelect}");
                _caps = new[]
                {
                    "MOUSE CAPTURED - SCROLL MOVES A HIGHLIGHT, " + f + " TAKES",
                    "MOUSE FREE - CLICK TAKES, DRAG MOVES ANYTHING"
                };
                _cap = Label(root, _caps[0], 9.5f, _p.TextDim, 0f, 98f, 430f);

                _gridOff = new Vector2(-42f, 18f);
                var win = Node(root, "win");
                win.anchoredPosition = _gridOff;
                Panel(win, "panel", 196f, 152f, MulA(_p.Window, 0.96f), _p.Border, 9f, 1.6f);
                Panel(win, "bar", 196f, 17f, MulA(_p.Raised, 0.9f), Color.clear, 7f, 0f)
                    .rectTransform.anchoredPosition = new Vector2(0f, 66f);
                Label(win, "UNIVERSAL INVENTORY", 7.5f, _p.TextDim, 0f, 66f, 180f);
                _cells = Cells.Make(win, "cells", 4, 4, 42f, 27f, 5f, _p);
                _cells.SetPos(0f, -10f);
                for (int i = 0; i < 16; i++) _cells.Occupy(i, i != 3 && i != 9 && i != 12 && i != 14);
                _hilite = Panel(win, "hi", 46f, 31f, Color.clear, _p.Accent, 5f, 2.2f);

                _hand = Panel(root, "hand", 64f, 40f, _p.Panel, _p.Selected, 6f, 1.8f);
                At(_hand, -42f, -84f);
                Label(root, "ACTIVE HAND", 8f, _p.TextDim, -42f, -66f, 90f);
                _handItem = Panel(root, "handItem", 18f, 18f, MulA(_p.Accent, 0.6f), Color.clear, 3f, 0f);
                At(_handItem, -42f, -88f);
                _handItem.gameObject.SetActive(false);

                _flyGroup = GroupNode(root, "flyer");
                _flyRt = (RectTransform)_flyGroup.transform;
                Panel(_flyRt, "f", 18f, 18f, MulA(_p.Accent, 0.8f), _p.Accent, 3f, 1.2f);
                _flyGroup.alpha = 0f;

                _capturedChips = GroupNode(root, "capChips");
                var cc = (RectTransform)_capturedChips.transform;
                cc.anchoredPosition = new Vector2(150f, 24f);
                Panel(cc, "c1", 74f, 34f, MulA(_p.Raised, 0.95f), _p.Border, 7f, 1.4f)
                    .rectTransform.anchoredPosition = new Vector2(0f, 30f);
                Crosshair(cc, "cross", 18f, 2f, _p.Accent).anchoredPosition = new Vector2(-20f, 30f);
                Label(cc, "AIM", 9f, _p.Text, 10f, 30f, 44f);
                Panel(cc, "c2", 74f, 30f, MulA(_p.Raised, 0.95f), _p.Border, 7f, 1.4f)
                    .rectTransform.anchoredPosition = new Vector2(0f, -8f);
                Label(cc, "SCROLL", 9f, _p.Text, 0f, -8f, 70f);
                _fKey = KeyVis.Make(cc, "f", f, _p, 26f, 13f, 36f);
                _fKey.SetPos(0f, -46f);

                _freeChips = GroupNode(root, "freeChips");
                var fc = (RectTransform)_freeChips.transform;
                fc.anchoredPosition = new Vector2(150f, 24f);
                Panel(fc, "f1", 88f, 32f, MulA(_p.Raised, 0.95f), _p.Border, 7f, 1.4f)
                    .rectTransform.anchoredPosition = new Vector2(0f, 20f);
                Label(fc, "CLICK", 9f, _p.Text, 12f, 20f, 60f);
                Panel(fc, "f2", 88f, 32f, MulA(_p.Raised, 0.95f), _p.Border, 7f, 1.4f)
                    .rectTransform.anchoredPosition = new Vector2(0f, -18f);
                Label(fc, "DRAG", 9f, _p.Text, 12f, -18f, 60f);
                _freeChips.alpha = 0f;

                _cur = Cursor.Make(root, "cursor", _p, 16f);
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                bool freed = u >= 4f;
                _capturedChips.alpha = freed ? 1f - Ease(u, 4.00f, 4.25f) : 1f;
                _freeChips.alpha = freed ? Ease(u, 4.05f, 4.35f) : 0f;

                if (!freed) TickCaptured(u); else TickFreed(u);

                int cap = freed ? 1 : 0;
                if (cap != _capIndex) { _capIndex = cap; _cap.text = _caps[cap]; }
            }

            private void TickCaptured(float u)
            {
                // One cheap frame per loop re-arms every cell the previous pass emptied.
                if (u < 0.15f)
                {
                    _cells.Occupy(TakenCell, true);
                    _cells.Occupy(2, true);
                    _cells.Occupy(8, true);
                    _cells.Occupy(12, false);
                    _handItem.gameObject.SetActive(false);
                }

                int step = Mathf.Clamp((int)(Seg(u, 0.20f, 3.60f) * 4.999f), 0, 4);
                _hilite.gameObject.SetActive(true);
                At(_hilite, _cells.ScenePosOf(Steps[step]));     // hilite lives INSIDE the window
                _hilite.BorderColor = WithA(_p.Accent, 0.65f + 0.35f * Mathf.Sin(u * 6f));

                _fKey.SetPressed(Bump(u, 1.90f, 2.15f));
                _cur.SetAlpha(0f);

                // The select key takes the highlighted item into the active hand.
                bool flying = u >= 2.10f && u < 2.70f;
                _flyGroup.alpha = flying ? 1f : 0f;
                if (flying)
                {
                    _cells.Occupy(TakenCell, false);
                    _flyRt.anchoredPosition = Arc(_cells.ScenePosOf(TakenCell) + _gridOff,
                        HandAt, Seg(u, 2.10f, 2.65f), 26f);
                }
                if (u >= 2.65f) _handItem.gameObject.SetActive(true);
                _hand.BorderWidth = Mathf.Lerp(1.8f, 3f, Bump(u, 2.60f, 3.00f));
            }

            private void TickFreed(float u)
            {
                _hilite.gameObject.SetActive(false);
                Vector2 c2 = _cells.ScenePosOf(2) + _gridOff;
                Vector2 c8 = _cells.ScenePosOf(8) + _gridOff;
                Vector2 c12 = _cells.ScenePosOf(12) + _gridOff;

                Vector2 at = Vector2.Lerp(new Vector2(60f, -40f), c2, Ease(u, 4.30f, 4.95f));
                at = Vector2.Lerp(at, c8, Ease(u, 5.70f, 6.25f));
                at = Vector2.Lerp(at, c12, Ease(u, 6.45f, 7.05f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 4.15f, 4.40f), 1f - Ease(u, 7.55f, 7.85f)));
                _cur.SetClick(Mathf.Max(Bump(u, 4.95f, 5.20f),
                    Mathf.Min(Seg(u, 6.35f, 6.45f), 1f - Seg(u, 7.05f, 7.15f))));

                // Click -> the item hops to hand; then a drag carries one cell's item to another.
                bool hop = u >= 5.15f && u < 5.72f;
                bool drag = u >= 6.45f && u < 7.15f;
                _flyGroup.alpha = (hop || drag) ? 1f : 0f;
                if (hop)
                {
                    _cells.Occupy(2, false);
                    _flyRt.anchoredPosition = Arc(c2, HandAt, Seg(u, 5.15f, 5.70f), 24f);
                }
                else if (drag)
                {
                    _cells.Occupy(8, false);
                    _flyRt.anchoredPosition = at + new Vector2(9f, -9f);
                }
                if (u >= 7.15f) _cells.Occupy(12, true);
                _cells.Cell[12].BorderColor = Color.Lerp(_cells.Border, _p.Accent,
                    Mathf.Min(Seg(u, 6.60f, 6.80f), 1f - Seg(u, 7.30f, 7.60f)));
                _handItem.gameObject.SetActive(u >= 5.70f);
            }

            public override void Destroy() { _cells = null; _cur = null; _fKey = null; _flyRt = null; }
        }

        // =====================================================================================
        //  stowflash (legacy) — the stow key routes the held item; the receiving box flashes.
        // =====================================================================================

        private sealed class StowFlashScene : Scene
        {
            public override bool SupportsMini => false;
            private const float Loop = 6f;
            private const float Slot = 2f;
            private Pal _p;
            private PanelGraphic _handItem;
            private PanelGraphic[] _box;
            private PanelGraphic[] _flash;
            private RectTransform _flyRt;
            private CanvasGroup _flyGroup;
            private KeyVis _g;
            // Belt, back, suit in rotation — never a consumable box (SmartStow excludes those).
            private static readonly int[] Route = { 2, 1, 0 };
            private static readonly string[] Names = { "SUIT", "BACK", "BELT" };

            protected override void Make(RectTransform root)
            {
                _p = P;
                string g = K("{V:SmartStow}");
                Label(root, "PRESS " + g + " - THE ITEM ROUTES ITSELF, THE BOX FLASHES", 9f,
                    _p.TextMute, 0f, 98f, 430f);

                At(Panel(root, "hand", 76f, 52f, _p.Panel, _p.Selected, 7f, 1.9f), -128f, -6f);
                Label(root, "ACTIVE HAND", 8f, _p.TextDim, -128f, 18f, 100f);
                _handItem = Panel(root, "item", 24f, 24f, MulA(_p.Accent, 0.75f), _p.Accent, 4f, 1.2f);
                At(_handItem, -128f, -12f);

                _box = new PanelGraphic[3];
                _flash = new PanelGraphic[3];
                var col = Node(root, "boxes");
                col.anchoredPosition = new Vector2(122f, 0f);
                for (int i = 0; i < 3; i++)
                {
                    float y = 56f - i * 52f;
                    _box[i] = Panel(col, "b" + i, 96f, 44f, _p.Panel, _p.Border, 6f, 1.4f);
                    At(_box[i], 0f, y);
                    Label(col, Names[i], 8.5f, _p.TextDim, 0f, y + 14f, 92f);
                    _flash[i] = Panel(col, "f" + i, 22f, 22f, MulA(_p.Accent, 0.85f), Color.clear, 4f, 0f);
                    At(_flash[i], 0f, y - 5f);
                    _flash[i].gameObject.SetActive(false);
                }

                _flyGroup = GroupNode(root, "fly");
                _flyRt = (RectTransform)_flyGroup.transform;
                Panel(_flyRt, "f", 22f, 22f, MulA(_p.Accent, 0.85f), _p.Accent, 4f, 1.2f);
                _flyGroup.alpha = 0f;

                _g = KeyVis.Make(root, "g", g, _p, 26f, 13f, 40f);
                _g.SetPos(-128f, -74f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                int slot = Mathf.Clamp((int)(u / Slot), 0, 2);
                float v = u - slot * Slot;
                int target = Route[slot];

                _handItem.gameObject.SetActive(v < 0.52f);
                float appear = Ease(v, 0f, 0.28f);
                float hs = Mathf.Lerp(0.5f, 1f, appear);
                _handItem.transform.localScale = new Vector3(hs, hs, 1f);

                _g.SetPressed(Bump(v, 0.34f, 0.56f));

                bool flying = v >= 0.50f && v < 1.12f;
                _flyGroup.alpha = flying ? 1f : 0f;
                if (flying)
                {
                    Vector2 from = new Vector2(-128f, -12f);
                    Vector2 to = new Vector2(122f, 56f - target * 52f - 5f);
                    _flyRt.anchoredPosition = Arc(from, to, Seg(v, 0.50f, 1.10f), 46f);
                }

                // The receiving box flash: 1.2x pop fading over ~0.35 s (SlotFlash parity).
                for (int i = 0; i < 3; i++)
                {
                    bool on = i == target && v >= 1.08f && v < 1.55f;
                    _flash[i].gameObject.SetActive(on);
                    float k = i == target ? Mathf.Min(Seg(v, 1.08f, 1.14f), 1f - Seg(v, 1.35f, 1.55f)) : 0f;
                    if (on)
                    {
                        float pop = 1f + 0.2f * (1f - Seg(v, 1.08f, 1.45f));
                        _flash[i].transform.localScale = new Vector3(pop, pop, 1f);
                        _flash[i].color = MulA(_p.Accent, 0.85f * k);
                    }
                    _box[i].BorderColor = Color.Lerp(_p.Border, _p.Accent, k);
                    _box[i].BorderWidth = Mathf.Lerp(1.4f, 2.4f, k);
                    _box[i].color = Hot(_p.Panel, _p.Accent, k * 0.3f);
                }
            }

            public override void Destroy() { _box = null; _flash = null; _g = null; _flyRt = null; }
        }

        // =====================================================================================
        //  themeswitch — one mini HUD, two palettes, crossfading with their captions.
        // =====================================================================================

        private sealed class ThemeSwitchScene : Scene
        {
            private const float Loop = 6f;
            private Pal _p;
            private PanelGraphic[] _fills;
            private PanelGraphic[] _accents;
            private RadialWedgeGraphic[] _arc;
            private TextMeshProUGUI[] _texts;
            private TextMeshProUGUI _capA, _capB;
            private Color _fillA, _fillB, _accA, _accB, _txtA, _txtB, _bordA, _bordB;

            protected override void Make(RectTransform root)
            {
                _p = P;
                _fillA = MulA(_p.Panel, 0.95f);
                _accA = _p.Accent;
                _txtA = _p.Text;
                _bordA = _p.Border;
                // "Pure HUD": a deliberately desaturated near-monochrome set.
                // Rule-8 content exemption (deliberate): palette B depicts the OTHER shipped theme for
                // the theme-switch story, so it must NOT follow the active theme - demo content, not a
                // HUD knob (no theme travel, no per-tier fork, nothing to migrate).
                _fillB = new Color(0.055f, 0.058f, 0.065f, 0.90f);
                _accB = new Color(0.90f, 0.92f, 0.94f, 1f);
                _txtB = new Color(0.95f, 0.96f, 0.97f, 1f);
                _bordB = new Color(0.72f, 0.75f, 0.78f, 0.75f);

                var mock = Node(root, "mock");
                mock.anchoredPosition = new Vector2(0f, L(6f, 0f));

                var wheelRoot = Node(mock, "arc");
                wheelRoot.anchoredPosition = new Vector2(L(-118f, -42f), L(4f, 2f));
                _arc = new RadialWedgeGraphic[5];
                float sector = Mathf.PI * 2f / 5f;
                for (int i = 0; i < 5; i++)
                {
                    _arc[i] = Wedge(wheelRoot, "a" + i, _fillA, _bordA, 1.4f);
                    float a0 = -Mathf.PI * 0.5f - sector * 0.5f + sector * i;
                    _arc[i].SetGeometry(L(30f, 12f), L(58f, 26f), a0 + 0.03f, a0 + sector - 0.03f, false);
                    _arc[i].RefreshGeometry();
                }

                var read = Node(mock, "read");
                read.anchoredPosition = new Vector2(L(74f, 26f), L(30f, 14f));
                _fills = new PanelGraphic[3];
                _accents = new PanelGraphic[4];
                _fills[0] = Panel(read, "panel", L(152f, 66f), L(66f, 26f), _fillA, _bordA, L(8f, 5f), 1.5f);
                for (int i = 0; i < 3; i++)
                {
                    _accents[i] = Panel(read, "bar" + i, L(108f - i * 26f, 46f - i * 11f), L(6f, 3f), _accA, Color.clear, L(3f, 1.5f), 0f);
                    At(_accents[i], L(-12f + i * 6f, -6f + i * 3f), L(18f - i * 17f, 7f - i * 7f));
                }

                var hands = Node(mock, "hands");
                hands.anchoredPosition = new Vector2(L(74f, 26f), L(-60f, -22f));
                _fills[1] = Panel(hands, "l", L(68f, 30f), L(40f, 17f), _fillA, _bordA, L(6f, 4f), 1.4f);
                At(_fills[1], L(-38f, -17f), 0f);
                _fills[2] = Panel(hands, "r", L(68f, 30f), L(40f, 17f), _fillA, _bordA, L(6f, 4f), 1.4f);
                At(_fills[2], L(38f, 17f), 0f);
                _accents[3] = Panel(hands, "acc", L(4f, 2f), L(28f, 12f), _accA, Color.clear, L(2f, 1f), 0f);
                At(_accents[3], L(68f, 30f), 0f);

                if (Mini)
                {
                    _texts = new TextMeshProUGUI[0];
                    return;
                }
                _texts = new TextMeshProUGUI[2];
                _texts[0] = Label(hands, "LEFT", 8f, _txtA, -38f, 12f, 64f);
                _texts[1] = Label(hands, "RIGHT", 8f, _txtA, 38f, 12f, 64f);

                _capA = Label(root, "Stationeers Blue", 12f, _p.Text, 0f, 96f, 300f);
                _capB = Label(root, "Pure HUD", 12f, _p.Text, 0f, 96f, 300f);
                _capB.color = WithA(_p.Text, 0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                float k = Mathf.Clamp01(Ease(u, 2.40f, 3.00f) - Ease(u, 5.40f, 6.00f));

                Color fill = Color.Lerp(_fillA, _fillB, k);
                Color acc = Color.Lerp(_accA, _accB, k);
                Color txt = Color.Lerp(_txtA, _txtB, k);
                Color bord = Color.Lerp(_bordA, _bordB, k);

                for (int i = 0; i < _arc.Length; i++)
                {
                    _arc[i].color = fill;
                    // RadialWedgeGraphic.BorderColor is a plain property (no dirty guard); the
                    // colour write above already dirtied the mesh, so the new border lands with it.
                    _arc[i].BorderColor = bord;
                }
                for (int i = 0; i < _fills.Length; i++)
                {
                    _fills[i].color = fill;
                    _fills[i].BorderColor = bord;
                }
                for (int i = 0; i < _accents.Length; i++) _accents[i].color = acc;
                for (int i = 0; i < _texts.Length; i++) _texts[i].color = txt;

                if (_capA == null) return;
                _capA.color = WithA(_p.Text, 1f - k);
                _capB.color = WithA(_p.Text, k);
            }

            public override void Destroy() { _fills = null; _accents = null; _arc = null; _texts = null; }
        }

        // =====================================================================================
        //  finish / designer — where to go next: GUIDE / CONTROLS / [F9] + Handbook.
        // =====================================================================================

        private sealed class FinishScene : Scene
        {
            public override bool SupportsMini => false;
            private const float Loop = 6f;
            private readonly int _focus;   // -1 = the three rows in turn; else only that row pulses
            private PanelGraphic[] _chip;
            private TextMeshProUGUI[] _text;
            private static readonly string[] Subs =
                { "replay any lesson", "rebind every key", "design your own HUD" };

            public FinishScene(int focus) { _focus = focus; }

            protected override void Make(RectTransform root)
            {
                string[] rows = { "GUIDE tab", "CONTROLS tab", K("{UIA_HudDesigner}") + " + Handbook" };
                _chip = new PanelGraphic[3];
                _text = new TextMeshProUGUI[3];
                for (int i = 0; i < 3; i++)
                {
                    float y = 58f - i * 58f;
                    _chip[i] = Panel(root, "c" + i, 244f, 46f, MulA(P.Panel, 0.95f), P.Border, 9f, 1.5f);
                    At(_chip[i], 0f, y);
                    _text[i] = Label(root, rows[i], 13f, P.Text, 0f, y + 8f, 236f);
                    Label(root, Subs[i], 9f, P.TextMute, 0f, y - 10f, 236f);
                }
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                int lit;
                float k;
                if (_focus >= 0)
                {
                    lit = _focus;
                    k = 0.5f + 0.5f * Mathf.Sin(u / Loop * Mathf.PI * 4f);
                }
                else
                {
                    lit = Mathf.Clamp((int)(u / 2f), 0, 2);
                    k = Mathf.Sin(Mathf.Repeat(u, 2f) / 2f * Mathf.PI);
                }
                for (int i = 0; i < 3; i++)
                {
                    float a = i == lit ? k : 0f;
                    _chip[i].color = Hot(MulA(P.Panel, 0.95f), P.Accent, a * 0.28f);
                    _chip[i].BorderColor = Color.Lerp(P.Border, P.Accent, a);
                    _chip[i].BorderWidth = Mathf.Lerp(1.5f, 2.3f, a);
                    Color rest = _focus >= 0 && i != _focus ? P.TextDim : P.Text;
                    _text[i].color = Color.Lerp(rest, P.Accent, a * 0.8f);
                }
            }

            public override void Destroy() { _chip = null; _text = null; }
        }

        // =====================================================================================
        //  whatsnew — 18.1's card: cycles actionword / dragpark / beltswap / shiftdrag, each for one
        //  of its own loops, crossfading at the seams.
        // =====================================================================================

        private sealed class WhatsNewScene : Scene
        {
            public override bool SupportsMini => false;
            private IDemoScene[] _parts;
            private CanvasGroup[] _groups;
            private float[] _start, _len;
            private float _loop;

            protected override void Make(RectTransform root)
            {
                _parts = new IDemoScene[] { new ActionWordScene(), new DragParkScene(), new BeltSwapScene(), new ShiftDragScene() };
                _len = new[] { ActionWordScene.Loop, DragParkScene.Loop, BeltSwapScene.Loop, ShiftDragScene.Loop };
                _start = new float[_parts.Length];
                _groups = new CanvasGroup[_parts.Length];
                float s = 0f;
                for (int i = 0; i < _parts.Length; i++)
                {
                    _start[i] = s;
                    s += _len[i];
                    _groups[i] = GroupNode(root, "part" + i);
                    var rt = (RectTransform)_groups[i].transform;
                    rt.sizeDelta = root.sizeDelta;
                    _parts[i].Build(rt, C);
                    _groups[i].alpha = 0f;
                }
                _loop = s;
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, _loop);
                for (int i = 0; i < _parts.Length; i++)
                {
                    float s = _start[i], e = s + _len[i];
                    float a = u >= s && u < e ? Mathf.Min(Ease(u, s, s + 0.35f), 1f - Ease(u, e - 0.35f, e)) : 0f;
                    _groups[i].alpha = a;
                    if (a > 0f) _parts[i].Tick(u - s);
                }
            }

            public override void Destroy()
            {
                if (_parts != null)
                    for (int i = 0; i < _parts.Length; i++) { try { _parts[i].Destroy(); } catch { } }
                _parts = null;
                _groups = null;
            }
        }

        // =====================================================================================
        //  Fallback — an unknown demo id: a faint neutral frame in a card, nothing at all in the
        //  strip. Never throws.
        // =====================================================================================

        private sealed class EmptyScene : Scene
        {
            private PanelGraphic _panel;

            protected override void Make(RectTransform root)
            {
                if (Mini) return;
                _panel = Panel(root, "empty", 260f, 110f, MulA(P.Panel, 0.55f),
                    MulA(P.Border, 0.55f), 10f, 1.3f);
            }

            public override void Tick(float t)
            {
                if (_panel == null) return;
                float k = 0.5f + 0.5f * Mathf.Sin(Mathf.Repeat(t, 5f) / 5f * Mathf.PI * 2f);
                _panel.BorderColor = MulA(P.Border, 0.35f + 0.25f * k);
            }

            public override void Destroy() { _panel = null; }
        }
    }
}
