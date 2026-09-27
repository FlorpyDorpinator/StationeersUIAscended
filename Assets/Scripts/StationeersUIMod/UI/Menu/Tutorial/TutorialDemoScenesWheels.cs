using StationeersUIMod.UI.Hud;
using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    // Wheel-centred lesson scenes (lessons 2-13). FULL layout = 440x220 (cards / Watch mode),
    // MINI = 150x76 (strip slot: shapes and keycaps only). See TutorialDemoStage.cs for the stage,
    // toolkit and the zero-allocation / live-glyph / curved-text contracts every scene keeps.
    internal sealed partial class TutorialDemoStage
    {
        // ---- two small helpers used by several scenes below --------------------------------------

        /// <summary>The slide-out DWELL ring: a small arc that fills while the pointer rests past a
        /// wedge's rim (0.18 s live; stretched in the demos so it can be seen).</summary>
        private sealed class DwellRing
        {
            private RectTransform _root;
            private RadialWedgeGraphic _g;
            private float _in, _out;
            private Color _c;

            public static DwellRing Make(RectTransform parent, string name, float rIn, float rOut, Color c)
            {
                var d = new DwellRing();
                d._root = Node(parent, name);
                d._in = rIn;
                d._out = rOut;
                d._c = c;
                d._g = Wedge(d._root, "arc", c, Color.clear, 0f);
                d._g.SetGeometry(rIn, rOut, -Mathf.PI * 0.5f, -Mathf.PI * 0.5f + 0.02f, false);
                d._g.RefreshGeometry();
                d._g.color = WithA(c, 0f);
                return d;
            }

            public void Set(Vector2 at, float fill01, float alpha)
            {
                _root.anchoredPosition = at;
                if (alpha > 0.002f)
                    _g.SetGeometry(_in, _out, -Mathf.PI * 0.5f, -Mathf.PI * 0.5f + Mathf.Max(0.02f, Mathf.Clamp01(fill01) * Mathf.PI * 2f), false);
                _g.color = WithA(_c, Mathf.Clamp01(alpha));
            }
        }

        /// <summary>An item chip that hops between places (wedge -> hand, hand -> box...).</summary>
        private sealed class Flyer
        {
            public CanvasGroup Group;
            public PanelGraphic Chip;

            public static Flyer Make(RectTransform parent, string name, float size, Pal p)
            {
                var f = new Flyer();
                f.Group = GroupNode(parent, name);
                f.Chip = Panel((RectTransform)f.Group.transform, "c", size, size, MulA(p.Accent, 0.85f), p.Accent,
                    Mathf.Min(4f, size * 0.22f), 1.2f);
                f.Group.alpha = 0f;
                return f;
            }

            public void Set(Vector2 at, float alpha)
            {
                ((RectTransform)Group.transform).anchoredPosition = at;
                Group.alpha = Mathf.Clamp01(alpha);
            }

            public void Hide() { Group.alpha = 0f; }
        }

        // =====================================================================================
        //  readout (2.1, 2.2) — the hub describes what you point at: name / what a click does /
        //  detail / live stat, line by line; then a GREY wedge and its red reason.
        // =====================================================================================

        private sealed class ReadoutScene : Scene
        {
            internal const float Loop = 9f;
            private const int Tool = 1, Grey = 2;
            private Wheel _w;
            private Cursor _cur;
            private TextMeshProUGUI[] _lines;       // full: title, verb, NAME, detail, stat/warn
            private PanelGraphic[] _bars;           // mini: name, verb, detail, warn
            private PanelGraphic[] _legend;
            private int _set = -1;
            private string[][] _text;
            private Color[] _lineCol;
            private Color _greyFill;

            private static readonly string[] Belt =
                { "THE HUB", "DRILL", "STOW", "WRENCH", "CROWBAR", "CUTTERS", "WELDER", "GRINDER" };

            protected override void Make(RectTransform root)
            {
                float outer = L(100f, 36f), inner = L(64f, 22f);
                _w = Wheel.Make(root, "ring", outer, inner, 8, P, true, false, 7f, !Mini);
                _w.SetPos(L(-100f, 0f), 0f);
                _w.SetCount(8);
                // Rule-8 content exemption (deliberate): the fixed neutral grey of a GREYED-OUT wedge is
                // what this scene teaches, so it must read "unavailable" under every theme - demo
                // content, not a HUD knob (no theme travel, no per-tier fork, nothing to migrate).
                _greyFill = new Color(0.30f, 0.31f, 0.33f, 0.55f);
                _w.SetBase(Grey, _greyFill);
                _w.SetBloom(0f);
                if (Mini)
                {
                    _w.MakeIcons(4f, MulA(P.Accent, 0.7f));
                    _w.ShowIcon(0, false);
                    _w.ShowIcon(Grey, false);
                    _bars = new PanelGraphic[4];
                    float[] ys = { 7f, 1.5f, -3.5f, -8.5f };
                    float[] ws = { 22f, 15f, 18f, 24f };
                    float[] hs = { 3.2f, 2.4f, 2f, 2.6f };
                    for (int i = 0; i < 4; i++)
                    {
                        _bars[i] = Panel(_w.Root, "bar" + i, ws[i], hs[i], Color.white, Color.clear, hs[i] * 0.5f, 0f);
                        At(_bars[i], 0f, ys[i]);
                    }
                }
                else
                {
                    _w.SetLabels(Belt);
                    _w.Labels[Grey].color = P.TextMute;
                    _lines = new TextMeshProUGUI[5];
                    float[] ys = { 34f, 16f, -1f, -18f, -33f };
                    float[] sz = { 8f, 9.5f, 12f, 8f, 7.5f };
                    for (int i = 0; i < 5; i++)
                    {
                        _lines[i] = Label(_w.Root, "", sz[i], P.Text, 0f, ys[i], 110f);
                        if (i == 2) _lines[i].fontStyle = FontStyles.Bold;
                    }
                    _text = new[]
                    {
                        new[] { "TOOLBELT", "Equip", "Drill", "Belt slot 1", "Battery 64%" },
                        new[] { "TOOLBELT", "Stow", "Empty slot", "Belt slot 2", "Held item doesn't fit" }
                    };
                    _lineCol = new[] { P.TextDim, P.Accent, P.Text, P.TextDim, P.Accent };
                    string[] leg = { "NAME", "WHAT A CLICK DOES", "DETAIL", "LIVE STAT - OR WHY NOT, IN RED" };
                    _legend = new PanelGraphic[4];
                    for (int i = 0; i < 4; i++)
                    {
                        float y = 45f - i * 30f;
                        _legend[i] = Panel(root, "leg" + i, 176f, 24f, MulA(P.Panel, 0.95f), P.Border, 6f, 1.3f);
                        At(_legend[i], 118f, y);
                        Label(root, leg[i], 8f, P.TextDim, 118f, y, 170f);
                    }
                    Label(root, "THE MIDDLE DESCRIBES WHAT YOU POINT AT", 9f, P.TextMute, 118f, 86f, 200f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                float open = Mathf.Min(Ease(u, 0.05f, 0.4f), 1f - Ease(u, 8.4f, 8.8f));
                _w.SetBloom(open);
                Vector2 hub = _w.Root.anchoredPosition;
                float midR = (_w.Inner + _w.Outer) * 0.5f;
                Vector2 at = Vector2.Lerp(hub, _w.ScenePointOn(Tool, midR), Ease(u, 0.6f, 1.3f));
                at = Vector2.Lerp(at, _w.ScenePointOn(Grey, midR), Ease(u, 4.3f, 4.9f));
                at = Vector2.Lerp(at, hub + new Vector2(0f, -_w.Inner * 0.2f), Ease(u, 7.6f, 8.1f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.45f, 0.65f), 1f - Ease(u, 8.0f, 8.3f)));
                int hov = open > 0.7f ? _w.HitTest(at, _w.Inner) : -1;
                _w.Tint(hov, hov == Grey ? P.TextMute : P.Selected, hov == Grey ? 0.25f : 0.5f);
                _w.SetBulge(hov == Grey ? -1 : hov, L(5f, 3f));

                bool greyPhase = u >= 4.9f;
                float t0 = greyPhase ? 5.0f : 1.4f;
                float step = greyPhase ? 0.35f : 0.5f;
                float clear = 1f - Ease(u, greyPhase ? 7.5f : 4.1f, greyPhase ? 7.8f : 4.35f);
                // Line order of appearance: name, verb, detail, stat — then the red reason.
                float aName = Ease(u, t0, t0 + 0.2f) * clear;
                float aVerb = Ease(u, t0 + step, t0 + step + 0.2f) * clear;
                float aSub = Ease(u, t0 + step * 2f, t0 + step * 2f + 0.2f) * clear;
                float aStat = Ease(u, t0 + step * 3f, t0 + step * 3f + 0.2f) * clear;
                float warnPulse = greyPhase ? 0.75f + 0.25f * Mathf.Sin(u * 9f) : 1f;

                if (Mini)
                {
                    _bars[0].color = WithA(P.Text, aName);
                    _bars[1].color = WithA(greyPhase ? P.TextMute : P.Accent, aVerb);
                    _bars[2].color = WithA(P.TextDim, aSub);
                    _bars[3].color = WithA(greyPhase ? P.Crit : P.Accent, aStat * warnPulse);
                    return;
                }
                int set = greyPhase ? 1 : 0;
                if (set != _set)
                {
                    _set = set;
                    for (int i = 0; i < 5; i++) _lines[i].text = _text[set][i];
                }
                float aTitle = Ease(u, 0.3f, 0.6f);
                _lines[0].color = WithA(_lineCol[0], aTitle);
                _lines[1].color = WithA(greyPhase ? P.TextDim : _lineCol[1], aVerb);
                _lines[2].color = WithA(greyPhase ? P.TextDim : _lineCol[2], aName);
                _lines[3].color = WithA(_lineCol[3], aSub);
                _lines[4].color = WithA(greyPhase ? P.Crit : _lineCol[4], aStat * warnPulse);
                LightLegend(0, aName);
                LightLegend(1, aVerb);
                LightLegend(2, aSub);
                LightLegend(3, aStat);
            }

            private void LightLegend(int i, float k)
            {
                bool red = i == 3 && _set == 1;
                _legend[i].BorderColor = Color.Lerp(P.Border, red ? P.Crit : P.Accent, k);
            }

            public override void Destroy() { _w = null; _cur = null; _lines = null; _bars = null; _legend = null; }
        }

        // =====================================================================================
        //  stowwedge (2.3) — an empty STOW wedge; hovering previews the held item's ghost there;
        //  a click slides it in.
        // =====================================================================================

        private sealed class StowWedgeScene : Scene
        {
            internal const float Loop = 8f;
            private const int Stow = 1;
            private Wheel _w;
            private Cursor _cur;
            private WordRing _words;
            private PanelGraphic _slot, _ghost, _placed;
            private HandPair _hands;
            private PanelGraphic _handBox, _handItem;
            private Flyer _fly;
            private Vector2 _handAt;
            private int _labelState = -1;

            private static readonly string[] Labels = { "WRENCH", "STOW", "DRILL", "CROWBAR", "WELDER", "GRINDER" };

            protected override void Make(RectTransform root)
            {
                float outer = L(76f, 28f), inner = L(34f, 11f);
                _w = Wheel.Make(root, "ring", outer, inner, 6, P, true, false, 7.5f, !Mini);
                _w.SetPos(L(-64f, -30f), L(-4f, -2f));
                _w.SetCount(6);
                if (Mini) { _w.MakeIcons(4f, MulA(P.Accent, 0.7f)); _w.ShowIcon(Stow, false); }
                else
                {
                    _w.SetLabels(Labels);
                    _w.Line0.text = "TOOLBELT";
                    _w.Line1.text = "holding: Cable Coil";
                    var stow = ArcWord.Make(C, _w.Root, "STOW", outer, 8f, 11f, P.Accent,
                        MulA(P.Panel, 0.92f), MulA(P.Border, 0.8f));
                    _words = new WordRing(_w, stow);
                }
                _w.SetBloom(0f);
                float midR = (inner + outer) * 0.5f;
                float s = L(16f, 8f);
                Vector2 slotAt = _w.PointOn(Stow, midR + L(11f, 0f));
                _slot = Panel(_w.Root, "slot", s, s, Color.clear, MulA(P.TextDim, 0.8f), s * 0.2f, 1.2f);
                At(_slot, slotAt);
                _ghost = Panel(_w.Root, "ghost", s * 0.8f, s * 0.8f, MulA(P.Accent, 0.5f), Color.clear, s * 0.18f, 0f);
                At(_ghost, slotAt);
                _placed = Panel(_w.Root, "placed", s * 0.8f, s * 0.8f, MulA(P.Accent, 0.85f), P.Accent, s * 0.18f, 1.1f);
                At(_placed, slotAt);
                _placed.gameObject.SetActive(false);

                if (Mini)
                {
                    _handAt = new Vector2(46f, -4f);
                    _handBox = Panel(root, "hand", 34f, 26f, MulA(P.Panel, 0.95f), P.Selected, 5f, 2f);
                    At(_handBox, _handAt);
                }
                else
                {
                    _hands = HandPair.Make(root, "hands", P, 60f, 42f, 10f, true, 6.5f);
                    _hands.Root.anchoredPosition = new Vector2(138f, -56f);
                    _handAt = _hands.SceneCenter(0);
                    Label(root, "POINT AT STOW - SEE IT THERE - CLICK TO PUT IT IN", 9.5f, P.TextDim, 0f, 98f, 430f);
                }
                _handItem = Panel(root, "handItem", L(18f, 12f), L(18f, 12f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem, _handAt);
                _fly = Flyer.Make(root, "fly", L(16f, 8f), P);
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                float open = Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 6.2f, 6.6f));
                _w.SetBloom(open);
                Vector2 hub = _w.Root.anchoredPosition;
                Vector2 slot = _w.Root.anchoredPosition + Pos(_slot);
                Vector2 at = Vector2.Lerp(hub, slot + new Vector2(-L(4f, 2f), -L(4f, 2f)), Ease(u, 0.7f, 1.5f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.55f, 0.75f), 1f - Ease(u, 5.6f, 5.9f)));
                float click = Bump(u, 2.6f, 2.9f);
                _cur.SetClick(click);

                int hov = open > 0.7f ? _w.HitTest(at, _w.Inner) : -1;
                bool placed = u >= 3.4f;
                _w.Tint(hov, P.Selected, 0.45f + 0.5f * Bump(u, 3.35f, 3.9f));
                _w.SetBulge(hov, L(5f, 3f));
                if (_words != null) _words.Update(t, hov, hov == Stow && u < 2.9f ? 0 : -1);

                // The ghost previews the held item while the wedge is hovered (before the click).
                float ghost = hov == Stow && !placed && u < 2.9f ? 0.35f + 0.25f * Mathf.Sin(u * 6f) : 0f;
                _ghost.color = WithA(P.Accent, ghost);
                if (_placed.gameObject.activeSelf != placed) _placed.gameObject.SetActive(placed);
                _slot.BorderColor = WithA(P.TextDim, placed ? 0f : 0.8f);

                bool flying = u >= 2.8f && u < 3.4f;
                if (flying) _fly.Set(Arc(_handAt, slot, Seg(u, 2.8f, 3.4f), L(30f, 10f)), 1f);
                else _fly.Hide();
                bool inHand = u < 2.8f;
                if (_handItem.gameObject.activeSelf != inHand) _handItem.gameObject.SetActive(inHand);

                if (!Mini)
                {
                    int ls = placed ? 1 : 0;
                    if (ls != _labelState)
                    {
                        _labelState = ls;
                        _w.Labels[Stow].text = placed ? "CABLE" : "STOW";
                        _w.Line1.text = placed ? "stowed" : "holding: Cable Coil";
                        _hands.State[0].text = placed ? "empty" : "Cable Coil";
                    }
                }
            }

            public override void Destroy() { _w = null; _cur = null; _hands = null; _words = null; }
        }

        // =====================================================================================
        //  hintring (2.4) — the curved hint line under the wheel; each hint fades on its own once
        //  you know it (per hint, after 25 wheel-opens).
        // =====================================================================================

        private sealed class HintRingScene : Scene
        {
            internal const float Loop = 9f;
            private Wheel _w;
            private HintArc _arc;

            protected override void Make(RectTransform root)
            {
                float outer = L(54f, 20f), inner = L(24f, 8f);
                _w = Wheel.Make(root, "ring", outer, inner, 8, P, true, false, 7f, false);
                Vector2 c = new Vector2(L(-20f, 0f), L(40f, 12f));
                _w.SetPos(c.x, c.y);
                _w.SetCount(8);
                _w.MakeIcons(L(6f, 3.5f), MulA(P.Accent, 0.6f));
                _w.SetBloom(0f);
                if (Mini)
                {
                    _arc = HintArc.MakePills(root, "hints", c, outer + 4f, 9f, 4, 13f, P);
                }
                else
                {
                    // What the strip says at the BELT ring's root (RadialHintBar.Rebuild, sticky): RMB
                    // closes, drag moves, the page key changes belt, the in-wheel hand swap (its
                    // "Shift+click keep open" entry is left out so the rest fit this small arc).
                    string[] keys = { "RMB", "Drag", K("{UIA_Page}"), K("{UIA_HandSwap}") };
                    string[] acts = { "close", "move item", "change belt", "switch hand" };
                    _arc = HintArc.Make(C, root, "hints", c, outer + 7f, 18f, keys, acts, 7f, P, 88f);
                    Label(root, "EACH HINT FADES ONCE YOU KNOW IT", 9f, P.TextDim, 166f, 70f, 150f);
                    Label(root, "(AFTER 25 WHEEL-OPENS)", 8f, P.TextMute, 166f, 56f, 150f);
                    Label(root, "WANT THEM BACK?", 8f, P.TextMute, 166f, 30f, 150f);
                    Label(root, K("{UIA_Menu}") + " > RADIAL", 8f, P.TextMute, 166f, 18f, 150f);
                }
                _arc.Group.alpha = 0f;
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                float open = Mathf.Min(Ease(u, 0.1f, 0.5f), 1f - Ease(u, 8.4f, 8.8f));
                _w.SetBloom(open);
                _arc.Group.alpha = Mathf.Min(Ease(u, 0.4f, 0.8f), 1f - Ease(u, 7.2f, 7.7f));
                for (int i = 0; i < _arc.Count; i++)
                {
                    float a0 = 2.4f + i * 1.0f;
                    _arc.Items[i].alpha = 1f - Ease(u, a0, a0 + 0.5f);
                }
                // The plate shrinks away with the last hint.
                _arc.Plate.color = WithA(MulA(P.Panel, 0.9f), 0.9f * (1f - Ease(u, 2.4f + _arc.Count * 1.0f, 2.9f + _arc.Count * 1.0f)));
            }

            public override void Destroy() { _w = null; _arc = null; }
        }

        // =====================================================================================
        //  toolreplace (3.1-3.4, 3.3T) — the tool's own wheel; hovering the part says TAKE in the
        //  warning colour; the cursor goes OUTWARD instead; REPLACE lists every spare with its
        //  charge; the pick SWAPS in one step. Variants: toolreplace (battery), toolreplace.canister,
        //  toolreplace.cartridge (the tablet's 3.3T labels).
        // =====================================================================================

        private sealed class ToolReplaceScene : Scene
        {
            internal const float Loop = 11f;
            private const int Part = 1;
            private readonly int _variant;   // 0 battery, 1 canister, 2 cartridge
            private Wheel _w, _child;
            private Cursor _cur;
            private WordRing _words;
            private TriangleGraphic _chev;
            private DwellRing _dwell;
            private KeyVis _r;
            private Ripple _rip;
            private CanvasGroup _done;
            private int _stage = -1;
            private string[] _ring, _list, _line0, _line1, _caps;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1, _lineIdx = -1;
            private PanelGraphic[] _listBars;   // mini: charge bars on the list wedges

            public ToolReplaceScene(int variant) { _variant = variant; }

            protected override void Make(RectTransform root)
            {
                string r = K("{UIA_ToolRadial}");
                string part = _variant == 1 ? "CANISTER" : _variant == 2 ? "CARTRIDGE" : "BATTERY";
                string partLow = _variant == 1 ? "canister" : _variant == 2 ? "cartridge" : "battery";
                _ring = _variant == 1
                    ? new[] { "FLAME", "CANISTER 18%", "POWER", "SETTINGS", "STOW" }
                    : _variant == 2
                        ? new[] { "BATTERY", "TRACKER", "POWER", "SETTINGS", "STOW" }
                        : new[] { "MODE", "BATTERY 12%", "POWER", "SETTINGS", "STOW" };
                _list = _variant == 1
                    ? new[] { "EJECT", "8200 kPa", "3100 kPa" }
                    : _variant == 2
                        ? new[] { "EJECT", "ATMOS", "NETWORK" }
                        : new[] { "EJECT", "98%", "45%" };
                string tool = _variant == 1 ? "WELDER" : _variant == 2 ? "TABLET" : "DRILL";
                _line0 = new[] { tool, "REPLACE WITH" };
                _line1 = new[] { "click a wedge", part.Substring(0, 1) + partLow.Substring(1) + (_variant == 2 ? " slot" : " low"),
                    "every " + partLow + " you carry", "swapped" };

                float outer = L(68f, 26f), inner = L(32f, 11f);
                _w = Wheel.Make(root, "ring", outer, inner, 5, P, true, false, 7.5f, !Mini);
                _w.SetPos(L(-64f, -26f), L(-2f, -8f));
                _w.SetCount(5);
                _w.SetBloom(0f);
                if (Mini)
                {
                    _w.MakeIcons(4f, MulA(P.Accent, 0.7f));
                    _listBars = new PanelGraphic[3];
                    for (int i = 0; i < 3; i++)
                    {
                        _listBars[i] = Panel(_w.Root, "charge" + i, 3f, 3f, P.Good, Color.clear, 1.2f, 0f);
                        _listBars[i].gameObject.SetActive(false);
                    }
                }
                _chev = _w.Chevron(Part, L(12f, 7f), P.Text);
                float fs = L(11.5f, 9.5f);
                var take = ArcWord.Make(C, _w.Root, "TAKE", outer, L(8f, 5f), fs, P.Warn,
                    MulA(P.Panel, 0.92f), MulA(P.Warn, 0.8f));
                var swap = ArcWord.Make(C, _w.Root, "SWAP", outer, L(8f, 5f), fs, P.Accent,
                    MulA(P.Panel, 0.92f), MulA(P.Border, 0.8f));
                _words = new WordRing(_w, take, swap);

                _child = Wheel.Make(root, "child", L(32f, 12f), L(13f, 5f), 2, P, false, false, 7f, !Mini);
                _child.SetCount(2);
                if (!Mini) _child.SetLabels(new[] { "TAKE", "REPLACE" });
                else _child.MakeIcons(3f, MulA(P.Accent, 0.75f));
                _child.SetBloom(0f);
                Vector2 childAt = _w.ScenePointOn(Part, outer + L(42f, 18f));
                _child.SetPos(childAt.x, childAt.y);
                _dwell = DwellRing.Make(root, "dwell", L(7.5f, 4f), L(10.5f, 6f), P.Accent);

                _r = KeyVis.Make(root, "r", r, P, L(28f, 18f), L(13f, 10f), L(40f, 26f));
                _r.SetPos(L(168f, 54f), L(70f, 22f));
                _rip = Ripple.Make(root, "rip", P.Accent, L(15f, 10f), L(30f, 18f), L(2f, 1.4f));
                _rip.SetPos(_r.PressPoint);

                _done = GroupNode(root, "done");
                ((RectTransform)_done.transform).anchoredPosition = new Vector2(L(156f, 54f), L(-72f, -20f));
                if (Mini) TickGlyph((RectTransform)_done.transform, "tick", 16f, P.Good);
                else
                {
                    Panel((RectTransform)_done.transform, "chip", 90f, 28f, MulA(P.Good, 0.25f), P.Good, 7f, 1.6f);
                    Label((RectTransform)_done.transform, "swapped", 10f, P.Good, 0f, 0f, 90f);
                }
                _done.alpha = 0f;

                if (!Mini)
                {
                    _caps = new[]
                    {
                        "TAP " + r + " - THE TOOL'S OWN WHEEL",
                        "CAREFUL: CLICKING THE " + part + " TAKES IT OUT",
                        "SLIDE OUT PAST ITS EDGE INSTEAD - AND STOP",
                        "REPLACE - EVERY " + part + " YOU CARRY",
                        "PICK ONE - SWAPPED IN ONE STEP"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                int stage = u < 5.75f ? 0 : 1;
                if (stage != _stage)
                {
                    _stage = stage;
                    _w.SetCount(stage == 0 ? 5 : 3);
                    _chev.gameObject.SetActive(stage == 0);
                    if (!Mini)
                    {
                        _w.SetLabels(stage == 0 ? _ring : _list);
                        _w.Line0.text = _line0[stage];
                        for (int i = 0; i < _w.Labels.Length; i++) _w.Labels[i].color = P.Text;
                        if (stage == 0) _w.Labels[Part].color = _variant == 2 ? P.Text : P.Crit;
                        else _w.Labels[0].color = P.TextMute;
                    }
                    else
                    {
                        for (int i = 0; i < 5; i++) _w.ShowIcon(i, stage == 0);
                        float midR = (_w.Inner + _w.Outer) * 0.5f;
                        for (int i = 0; i < 3; i++)
                        {
                            bool on = stage == 1 && i > 0;
                            _listBars[i].gameObject.SetActive(on);
                            if (!on) continue;
                            float len = i == 1 ? 9f : 4.5f;
                            _listBars[i].SetShape(len, 3f, 1.2f);
                            ((RectTransform)_listBars[i].transform).sizeDelta = new Vector2(len, 3f);
                            _listBars[i].color = i == 1 ? P.Good : P.Warn;
                            At(_listBars[i], _w.PointOn(i, midR));
                        }
                    }
                }

                _r.SetPressed(Bump(u, 0.2f, 0.45f));
                _rip.Play(Seg(u, 0.3f, 0.9f));
                float openA = Mathf.Min(Ease(u, 0.35f, 0.8f), 1f);
                float dip = 1f - 0.55f * Bump(u, 5.6f, 5.95f);
                float open = stage == 0 ? openA * dip : dip * (1f - Ease(u, 7.5f, 7.9f));
                _w.SetBloom(open);

                Vector2 hub = _w.Root.anchoredPosition;
                float midRr = (_w.Inner + _w.Outer) * 0.5f;
                Vector2 at;
                float click = 0f;
                float childBloom = 0f;
                int hov = -1;
                if (stage == 0)
                {
                    Vector2 mid = _w.ScenePointOn(Part, midRr);
                    Vector2 past = _w.ScenePointOn(Part, _w.Outer + L(16f, 7f));
                    Vector2 replace = _child.Root.anchoredPosition + _child.DirOf(1) * ((_child.Inner + _child.Outer) * 0.5f);
                    at = Vector2.Lerp(hub, mid, Ease(u, 1.0f, 1.8f));
                    // The hesitation: it hovers the part (TAKE, in the warning colour) and does NOT click.
                    at += new Vector2(0f, L(2f, 1f)) * Mathf.Sin(Seg(u, 1.9f, 2.9f) * Mathf.PI * 3f) * Window01(u, 1.9f, 2.9f, 0.1f);
                    at = Vector2.Lerp(at, past, Ease(u, 3.0f, 3.6f));
                    at = Vector2.Lerp(at, replace, Ease(u, 4.6f, 5.3f));
                    click = Bump(u, 5.35f, 5.65f);
                    float fill = Mathf.Min(Seg(u, 3.6f, 3.65f), 1f - Seg(u, 4.1f, 4.3f));
                    _dwell.Set(past, Seg(u, 3.6f, 4.1f), fill);
                    childBloom = Mathf.Min(Ease(u, 4.1f, 4.5f), 1f - Ease(u, 5.6f, 5.8f));
                    hov = open > 0.7f && u < 5.5f ? _w.HitTest(at, _w.Inner) : -1;
                    _child.Tint(u > 4.9f ? 1 : -1, P.Selected, Mathf.Max(0.5f, Bump(u, 5.35f, 5.75f)));
                }
                else
                {
                    _dwell.Set(hub, 0f, 0f);
                    Vector2 pick = _w.ScenePointOn(1, midRr);
                    at = Vector2.Lerp(hub, pick, Ease(u, 6.2f, 7.0f));
                    click = Bump(u, 7.2f, 7.5f);
                    hov = open > 0.6f ? _w.HitTest(at, _w.Inner) : -1;
                }
                _child.SetBloom(childBloom);
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.9f, 1.1f), 1f - Ease(u, 7.6f, 7.9f)));
                _cur.SetClick(click);
                _w.Tint(hov, stage == 0 && hov == Part ? P.Warn : P.Selected, stage == 1 ? 0.5f + 0.5f * Bump(u, 7.2f, 7.6f) : 0.45f);
                _w.SetBulge(hov, L(5f, 3f));
                int word = -1;
                if (stage == 0 && hov == Part && childBloom < 0.05f) word = 0;
                else if (stage == 1 && hov == 1) word = 1;
                _words.Update(t, hov, word);

                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5.5f);
                _chev.color = WithA(P.Text, hov == Part ? 0.95f : 0.45f + 0.4f * pulse);

                _done.alpha = Mathf.Min(Ease(u, 7.7f, 8.0f), 1f - Ease(u, 9.6f, 10.0f));
                Scale(_done, 1f + 0.16f * Bump(u, 7.7f, 8.3f));

                if (Mini) return;
                int cap = u < 1.8f ? 0 : u < 3.0f ? 1 : u < 4.6f ? 2 : u < 6.2f ? 3 : 4;
                if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                int line = stage == 1 ? (u >= 7.5f ? 3 : 2) : (hov == Part ? 1 : 0);
                if (line != _lineIdx) { _lineIdx = line; _w.Line1.text = _line1[line]; }
            }

            public override void Destroy() { _w = null; _child = null; _cur = null; _words = null; _r = null; }
        }

        // =====================================================================================
        //  tabletinstall (3.1T, 3.2T) — the tablet's own wheel; its CARTRIDGE slot is empty; slide
        //  out past it: the three cartridges you carry bloom; one flies in.
        // =====================================================================================

        private sealed class TabletInstallScene : Scene
        {
            internal const float Loop = 10f;
            private const int Slot = 1;
            private Wheel _w, _child;
            private Cursor _cur;
            private TriangleGraphic _chev;
            private DwellRing _dwell;
            private KeyVis _r;
            private Ripple _rip;
            private PanelGraphic _hollow, _fitted;
            private Flyer _fly;
            private int _state = -1;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            private static readonly string[] Ring = { "BATTERY", "CARTRIDGE", "POWER", "SETTINGS", "STOW" };
            private static readonly string[] Carts = { "ATMOS", "NETWORK", "TRACKER" };

            protected override void Make(RectTransform root)
            {
                string r = K("{UIA_ToolRadial}");
                float outer = L(68f, 26f), inner = L(32f, 11f);
                _w = Wheel.Make(root, "ring", outer, inner, 5, P, true, false, 7.5f, !Mini);
                _w.SetPos(L(-64f, -26f), L(-2f, -8f));
                _w.SetCount(5);
                if (Mini) { _w.MakeIcons(4f, MulA(P.Accent, 0.7f)); _w.ShowIcon(Slot, false); }
                else
                {
                    _w.SetLabels(Ring);
                    _w.Line0.text = "TABLET";
                    _w.Line1.text = "no cartridge";
                }
                _w.SetBloom(0f);
                _chev = _w.Chevron(Slot, L(12f, 7f), P.Text);
                float midR = (inner + outer) * 0.5f;
                float s = L(16f, 7f);
                Vector2 slotAt = _w.PointOn(Slot, midR - L(10f, 0f));
                _hollow = Panel(_w.Root, "hollow", s, s, Color.clear, MulA(P.TextDim, 0.85f), s * 0.2f, 1.2f);
                At(_hollow, slotAt);
                _fitted = Panel(_w.Root, "fitted", s, s, MulA(P.Good, 0.75f), P.Good, s * 0.2f, 1.2f);
                At(_fitted, slotAt);
                _fitted.gameObject.SetActive(false);

                _child = Wheel.Make(root, "child", L(36f, 13f), L(15f, 5f), 3, P, false, false, 7f, !Mini);
                _child.SetCount(3);
                if (!Mini) _child.SetLabels(Carts);
                else _child.MakeIcons(3f, MulA(P.Accent, 0.8f));
                _child.SetBloom(0f);
                Vector2 childAt = _w.ScenePointOn(Slot, outer + L(46f, 19f));
                _child.SetPos(childAt.x, childAt.y);
                _dwell = DwellRing.Make(root, "dwell", L(7.5f, 4f), L(10.5f, 6f), P.Accent);
                _fly = Flyer.Make(root, "fly", L(14f, 7f), P);

                _r = KeyVis.Make(root, "r", r, P, L(28f, 18f), L(13f, 10f), L(40f, 26f));
                _r.SetPos(L(168f, 54f), L(70f, 22f));
                _rip = Ripple.Make(root, "rip", P.Accent, L(15f, 10f), L(30f, 18f), L(2f, 1.4f));
                _rip.SetPos(_r.PressPoint);
                if (!Mini)
                {
                    _caps = new[]
                    {
                        "TAP " + r + " WHILE HOLDING THE TABLET",
                        "NO CARTRIDGE? SLIDE OUT PAST THE EMPTY SLOT",
                        "EVERY CARTRIDGE YOU CARRY - CLICK ONE",
                        "INSTALLED"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                _r.SetPressed(Bump(u, 0.2f, 0.45f));
                _rip.Play(Seg(u, 0.3f, 0.9f));
                float open = Mathf.Min(Ease(u, 0.35f, 0.8f), 1f - Ease(u, 8.2f, 8.6f));
                _w.SetBloom(open);

                Vector2 hub = _w.Root.anchoredPosition;
                float midR = (_w.Inner + _w.Outer) * 0.5f;
                Vector2 mid = _w.ScenePointOn(Slot, midR);
                Vector2 past = _w.ScenePointOn(Slot, _w.Outer + L(16f, 7f));
                Vector2 pickAt = _child.Root.anchoredPosition + _child.DirOf(0) * ((_child.Inner + _child.Outer) * 0.5f);
                Vector2 at = Vector2.Lerp(hub, mid, Ease(u, 1.0f, 1.8f));
                at = Vector2.Lerp(at, past, Ease(u, 1.8f, 2.4f));
                at = Vector2.Lerp(at, pickAt, Ease(u, 3.5f, 4.3f));
                at = Vector2.Lerp(at, hub, Ease(u, 5.6f, 6.3f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.9f, 1.1f), 1f - Ease(u, 8.0f, 8.3f)));
                _cur.SetClick(Bump(u, 4.5f, 4.8f));

                _dwell.Set(past, Seg(u, 2.4f, 2.9f), Mathf.Min(Seg(u, 2.4f, 2.45f), 1f - Seg(u, 2.9f, 3.1f)));
                float childBloom = Mathf.Min(Ease(u, 2.9f, 3.3f), 1f - Ease(u, 4.8f, 5.2f));
                _child.SetBloom(childBloom);
                _child.Tint(u > 4.0f ? 0 : -1, P.Selected, Mathf.Max(0.5f, Bump(u, 4.5f, 4.9f)));

                int hov = open > 0.7f && u < 5.5f ? _w.HitTest(at, _w.Inner) : -1;
                _w.Tint(hov, P.Selected, 0.45f);
                _w.SetBulge(hov, L(5f, 3f));
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5.5f);
                bool fitted = u >= 5.4f;
                _chev.color = WithA(P.Text, fitted ? 0.4f : hov == Slot ? 0.95f : 0.45f + 0.4f * pulse);

                bool flying = u >= 4.8f && u < 5.4f;
                if (flying) _fly.Set(Arc(pickAt, _w.Root.anchoredPosition + Pos(_hollow), Seg(u, 4.8f, 5.4f), L(24f, 10f)), 1f);
                else _fly.Hide();
                if (_fitted.gameObject.activeSelf != fitted) _fitted.gameObject.SetActive(fitted);
                _hollow.BorderColor = WithA(P.TextDim, fitted ? 0f : 0.85f);
                Scale(_fitted, 1f + 0.3f * Bump(u, 5.4f, 5.9f));

                if (Mini) return;
                int state = fitted ? 1 : 0;
                if (state != _state) { _state = state; _w.Line1.text = fitted ? "Atmos Analyser" : "no cartridge"; }
                int cap = u < 1.8f ? 0 : u < 3.3f ? 1 : u < 5.4f ? 2 : 3;
                if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
            }

            public override void Destroy() { _w = null; _child = null; _cur = null; _r = null; }
        }

        // =====================================================================================
        //  split (4.1, 4.2) — a stack wedge; slide out: SPLIT ONE / HALF / COUNT; the count ticks
        //  with the scroll wheel; the split part hops to the FREE hand.
        // =====================================================================================

        private sealed class SplitScene : Scene
        {
            internal const float Loop = 10f;
            private const int Stack = 1, Count = 2;
            private Wheel _w, _child;
            private Cursor _cur;
            private TriangleGraphic _chev, _up, _down;
            private DwellRing _dwell;
            private Mouse _scroll;
            private Flyer _fly;
            private HandPair _hands;
            private PanelGraphic _handFree, _handItem, _countBar;
            private Vector2 _handAt;
            private string[] _counts;
            private int _countIdx = -1;
            private int _stateIdx = -1;

            private static readonly string[] Belt =
                { "THE HUB", "CABLE x50", "DRILL", "WRENCH", "CROWBAR", "CUTTERS", "WELDER", "GRINDER" };

            protected override void Make(RectTransform root)
            {
                float outer = L(58f, 26f), inner = L(26f, 11f);
                _w = Wheel.Make(root, "ring", outer, inner, 8, P, true, false, 7f, !Mini);
                _w.SetPos(L(-100f, -34f), L(-34f, -12f));
                _w.SetCount(8);
                if (Mini) { _w.MakeIcons(4f, MulA(P.Accent, 0.7f)); _w.ShowIcon(0, false); }
                else
                {
                    _w.SetLabels(Belt);
                    _w.Line0.text = "TOOLBELT";
                    _w.Line1.text = "Cable Coil x50";
                }
                _w.SetBloom(0f);
                _chev = _w.Chevron(Stack, L(12f, 7f), P.Text);

                _child = Wheel.Make(root, "child", L(40f, 14f), L(16f, 5f), 3, P, false, false, 6.5f, !Mini);
                _child.SetCount(3);
                _child.SetBloom(0f);
                Vector2 childAt = _w.ScenePointOn(Stack, outer + L(46f, 18f));
                _child.SetPos(childAt.x, childAt.y);
                Vector2 cm = _child.PointOn(Count, (_child.Inner + _child.Outer) * 0.5f);
                if (!Mini)
                {
                    _counts = new string[10];
                    for (int i = 0; i < 10; i++) _counts[i] = "SPLIT " + (i + 1);
                    _child.SetLabels(new[] { "SPLIT ONE", "SPLIT HALF", _counts[0] });
                    _up = Tri(_child.Root, "up", 6f, P.Accent, true, 0f);
                    At(_up, cm + new Vector2(0f, 10f));
                    _down = Tri(_child.Root, "down", 6f, P.Accent, false, 0f);
                    At(_down, cm + new Vector2(0f, -10f));
                }
                else
                {
                    _child.MakeIcons(3f, MulA(P.Accent, 0.75f));
                    _child.ShowIcon(Count, false);
                    _countBar = Panel(_child.Root, "count", 2f, 2.4f, P.Accent, Color.clear, 1.2f, 0f);
                    At(_countBar, cm);
                }
                _dwell = DwellRing.Make(root, "dwell", L(7.5f, 4f), L(10.5f, 6f), P.Accent);
                _scroll = Mouse.Make(root, "scroll", P, L(30f, 18f));
                _scroll.SetAlpha(0f);
                _fly = Flyer.Make(root, "fly", L(16f, 8f), P);

                if (Mini)
                {
                    _handAt = new Vector2(52f, -18f);
                    _handFree = Panel(root, "hand", 32f, 24f, MulA(P.Panel, 0.95f), P.Border, 5f, 1.6f);
                    At(_handFree, _handAt);
                }
                else
                {
                    _hands = HandPair.Make(root, "hands", P, 60f, 42f, 10f, true, 6.5f);
                    _hands.Root.anchoredPosition = new Vector2(142f, -64f);
                    _hands.Item[0].gameObject.SetActive(true);
                    _hands.State[0].text = "Drill";
                    _handAt = _hands.SceneCenter(1);
                    Label(root, "SLIDE OUT ON A STACK - PICK HOW MANY", 10f, P.TextDim, 0f, 100f, 430f);
                    Label(root, "the split part lands in your free hand", 8.5f, P.TextMute, 142f, -30f, 200f);
                }
                _handItem = Panel(root, "handItem", L(18f, 11f), L(18f, 11f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem, _handAt);
                _handItem.gameObject.SetActive(false);
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                float open = Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 6.0f, 6.4f));
                _w.SetBloom(open);
                Vector2 hub = _w.Root.anchoredPosition;
                Vector2 mid = _w.ScenePointOn(Stack, (_w.Inner + _w.Outer) * 0.5f);
                Vector2 past = _w.ScenePointOn(Stack, _w.Outer + L(16f, 7f));
                Vector2 cnt = _child.Root.anchoredPosition + _child.PointOn(Count, (_child.Inner + _child.Outer) * 0.5f);
                Vector2 at = Vector2.Lerp(hub, mid, Ease(u, 0.6f, 1.4f));
                at = Vector2.Lerp(at, past, Ease(u, 1.4f, 2.0f));
                at = Vector2.Lerp(at, cnt, Ease(u, 3.1f, 3.8f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.45f, 0.65f), 1f - Ease(u, 6.0f, 6.3f)));
                _cur.SetClick(Bump(u, 5.7f, 6.0f));

                int hov = open > 0.7f && u < 3.0f ? _w.HitTest(at, _w.Inner) : -1;
                _w.Tint(hov, P.Selected, 0.45f);
                _w.SetBulge(hov, L(5f, 3f));
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5.5f);
                _chev.color = WithA(P.Text, hov == Stack ? 0.95f : 0.45f + 0.4f * pulse);
                _dwell.Set(past, Seg(u, 2.0f, 2.5f), Mathf.Min(Seg(u, 2.0f, 2.05f), 1f - Seg(u, 2.5f, 2.7f)));
                float childBloom = Mathf.Min(Ease(u, 2.5f, 2.9f), 1f - Ease(u, 6.0f, 6.3f));
                _child.SetBloom(childBloom);
                _child.Tint(u > 3.5f ? Count : -1, P.Selected, Mathf.Max(0.5f, Bump(u, 5.7f, 6.1f)));

                // The count ticks 1 -> 10 while the wheel rolls beside the pointer.
                float scrolling = Window01(u, 4.0f, 5.5f, 0.15f);
                _scroll.SetAlpha(scrolling);
                _scroll.SetPos(at + new Vector2(L(22f, 12f), -L(26f, 12f)));
                _scroll.Scroll(Seg(u, 4.1f, 5.4f) * 5f, scrolling);
                int ci = Mathf.Clamp((int)(Seg(u, 4.1f, 5.4f) * 9.999f), 0, 9);
                if (u < 3.0f) ci = 0;
                if (ci != _countIdx)
                {
                    _countIdx = ci;
                    if (!Mini) _child.Labels[Count].text = _counts[ci];
                    else
                    {
                        float len = 2f + ci * 1.1f;
                        _countBar.SetShape(len, 2.4f, 1.2f);
                        ((RectTransform)_countBar.transform).sizeDelta = new Vector2(len, 2.4f);
                    }
                }
                if (_up != null)
                {
                    _up.color = WithA(P.Accent, 0.4f + 0.6f * scrolling);
                    _down.color = WithA(P.Accent, 0.4f + 0.6f * scrolling);
                }

                bool flying = u >= 6.0f && u < 6.7f;
                if (flying) _fly.Set(Arc(cnt, _handAt, Seg(u, 6.0f, 6.7f), L(30f, 10f)), 1f);
                else _fly.Hide();
                bool inHand = u >= 6.65f && u < 9.4f;
                if (_handItem.gameObject.activeSelf != inHand) _handItem.gameObject.SetActive(inHand);
                if (_hands != null)
                {
                    int st = inHand ? 1 : 0;
                    if (st != _stateIdx) { _stateIdx = st; _hands.State[1].text = st == 1 ? "Cable x10" : "empty"; }
                    _hands.Flash(1, Bump(u, 6.6f, 7.1f));
                }
            }

            public override void Destroy() { _w = null; _child = null; _cur = null; _hands = null; _scroll = null; }
        }

        // =====================================================================================
        //  dragpark (5.1, 5.2, 18.3) — drag two items off their wedges onto open screen (they wait
        //  there); the right button closes the wheel; dragged-out items drop at your feet.
        // =====================================================================================

        private sealed class DragParkScene : Scene
        {
            internal const float Loop = 9f;
            private Wheel _w;
            private Cursor _cur;
            private Mouse _rmb;
            private Ripple _rip;
            private CanvasGroup[] _chip;
            private CircleGraphic[] _ring;
            private Vector2[] _park;
            private float _floorY;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            private static readonly string[] Bag = { "ICE", "ORE", "CANISTER", "FILTER", "FLARES" };

            protected override void Make(RectTransform root)
            {
                float outer = L(58f, 22f), inner = L(26f, 9f);
                _w = Wheel.Make(root, "ring", outer, inner, 5, P, true, true, 7.5f, !Mini);
                _w.SetPos(L(-92f, -44f), L(22f, 10f));
                _w.SetCount(5);
                if (Mini) _w.MakeIcons(4f, MulA(P.Accent, 0.7f));
                else { _w.SetLabels(Bag); _w.Line0.text = "BACKPACK"; }
                _w.SetBloom(0f);

                _floorY = L(-86f, -31f);
                At(Panel(root, "floor", L(420f, 146f), L(2.4f, 1.6f), MulA(P.TextMute, 0.8f), Color.clear, 1f, 0f), 0f, _floorY);
                _park = new[] { new Vector2(L(20f, 2f), L(62f, 24f)), new Vector2(L(78f, 26f), L(8f, 4f)) };
                _chip = new CanvasGroup[2];
                _ring = new CircleGraphic[2];
                float cs = L(22f, 10f);
                for (int i = 0; i < 2; i++)
                {
                    _chip[i] = GroupNode(root, "chip" + i);
                    var cr = (RectTransform)_chip[i].transform;
                    _ring[i] = Circle(cr, "ring", cs * 0.75f, Color.clear, MulA(P.Accent, 0.6f), L(1.4f, 1f));
                    Panel(cr, "c", cs, cs, MulA(P.Accent, 0.6f), P.Accent, cs * 0.18f, 1.2f);
                    _chip[i].alpha = 0f;
                }
                _rmb = Mouse.Make(root, "rmb", P, L(42f, 26f));
                _rmb.SetPos(L(170f, 58f), L(56f, 18f));
                _rip = Ripple.Make(root, "rip", P.Accent, L(4f, 3f), L(22f, 13f), L(2f, 1.4f));
                _rip.SetPos(_rmb.ButtonPos(1));
                if (!Mini)
                {
                    Label(root, "RIGHT-CLICK", 8.5f, P.TextDim, 170f, 24f, 110f);
                    Label(root, "AT YOUR FEET", 8f, P.TextMute, 160f, _floorY - 10f, 120f);
                    _caps = new[]
                    {
                        "DRAG AN ITEM OFF AND LET GO - IT WAITS THERE",
                        "CLOSE THE WHEEL - DRAGGED-OUT ITEMS DROP AT YOUR FEET"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(15f, 10f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                float open = Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 4.8f, 5.2f));
                _w.SetBloom(open);
                Vector2 hub = _w.Root.anchoredPosition;
                float midR = (_w.Inner + _w.Outer) * 0.5f;
                Vector2 srcA = _w.ScenePointOn(1, midR), srcB = _w.ScenePointOn(2, midR);
                Vector2 at = Vector2.Lerp(hub, srcA, Ease(u, 0.6f, 1.1f));
                at = Vector2.Lerp(at, _park[0], Ease(u, 1.2f, 2.0f));
                at = Vector2.Lerp(at, srcB, Ease(u, 2.3f, 2.8f));
                at = Vector2.Lerp(at, _park[1], Ease(u, 2.9f, 3.7f));
                at = Vector2.Lerp(at, _park[1] + new Vector2(L(26f, 10f), L(20f, 8f)), Ease(u, 3.9f, 4.4f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.45f, 0.65f), 1f - Ease(u, 5.2f, 5.5f)));
                float rmb = Bump(u, 4.5f, 4.85f);
                _cur.SetClick(Mathf.Max(Mathf.Max(Held(u, 1.15f, 2.05f), Held(u, 2.85f, 3.75f)), rmb));
                _rmb.SetButton(1, rmb);
                _rip.Play(Seg(u, 4.6f, 5.2f));
                int hov = open > 0.7f && u < 4.0f ? _w.HitTest(at, _w.Inner) : -1;
                _w.Tint(hov, P.Selected, 0.45f);

                TickChip(0, u, 1.15f, 2.05f, at);
                TickChip(1, u, 2.85f, 3.75f, at);

                if (_cap != null)
                {
                    int cap = u < 4.3f ? 0 : 1;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            private void TickChip(int i, float u, float grab, float drop, Vector2 cursor)
            {
                _chip[i].alpha = Mathf.Min(Seg(u, grab, grab + 0.06f), 1f - Ease(u, 7.8f, 8.3f));
                Vector2 at = u < drop ? cursor + new Vector2(L(10f, 6f), -L(10f, 6f)) : _park[i];
                // Closing the wheel drops them: fall (ease-in) to the floor line, a small settle.
                float fall = Seg(u, 5.0f + i * 0.08f, 5.6f + i * 0.08f);
                if (fall > 0f)
                {
                    float land = _floorY + L(12f, 5f);
                    at.y = Mathf.Lerp(_park[i].y, land, fall * fall) + L(4f, 2f) * Bump(u, 5.6f + i * 0.08f, 5.85f + i * 0.08f);
                }
                ((RectTransform)_chip[i].transform).anchoredPosition = at;
                float parked = u >= drop && fall <= 0f ? Seg(u, drop, drop + 0.25f) : 0f;
                _ring[i].BorderColor = WithA(P.Accent, 0.65f * parked);
            }

            public override void Destroy() { _w = null; _cur = null; _rmb = null; _chip = null; _ring = null; }
        }

        // =====================================================================================
        //  altreach (5.4) — hold the mouse key with a wheel open: the ring dims, the pointer reaches
        //  into the world, lifts an item off the floor and lets go over a wedge.
        // =====================================================================================

        private sealed class AltReachScene : Scene
        {
            internal const float Loop = 9f;
            private const int Target = 2;
            private Wheel _w;
            private Cursor _cur;
            private KeyVis _alt;
            private Flyer _item;
            private PanelGraphic _placed;
            private Vector2 _floorItem;
            private TextMeshProUGUI _hint;

            private static readonly string[] Bag = { "ICE", "ORE", "STOW", "CANISTER", "FILTER", "FLARES" };

            protected override void Make(RectTransform root)
            {
                string alt = K("{V:MouseControl}");
                float outer = L(56f, 22f), inner = L(25f, 9f);
                _w = Wheel.Make(root, "ring", outer, inner, 6, P, true, false, 7.5f, !Mini);
                _w.SetPos(L(-84f, -38f), L(30f, 12f));
                _w.SetCount(6);
                if (Mini) { _w.MakeIcons(4f, MulA(P.Accent, 0.7f)); _w.ShowIcon(Target, false); }
                else { _w.SetLabels(Bag); _w.Line0.text = "BACKPACK"; }
                _w.SetBloom(0f);
                _placed = Panel(_w.Root, "placed", L(14f, 7f), L(14f, 7f), MulA(P.Accent, 0.85f), P.Accent, 3f, 1.1f);
                At(_placed, _w.PointOn(Target, (inner + outer) * 0.5f));
                _placed.gameObject.SetActive(false);

                float floorY = L(-78f, -30f);
                At(Panel(root, "floor", L(240f, 104f), L(2.4f, 1.6f), MulA(P.TextMute, 0.8f), Color.clear, 1f, 0f),
                    L(96f, 22f), floorY);
                _floorItem = new Vector2(L(128f, 46f), floorY + L(9f, 5f));
                _item = Flyer.Make(root, "item", L(16f, 8f), P);
                _item.Chip.transform.localEulerAngles = new Vector3(0f, 0f, 12f);
                _alt = KeyVis.Make(root, "alt", alt, P, L(28f, 16f), L(13f, 9f), L(40f, 24f));
                _alt.SetPos(L(-172f, -58f), L(-66f, -24f));
                if (!Mini)
                {
                    Label(root, "HOLD " + alt + " WITH A WHEEL OPEN - REACH INTO THE WORLD", 10f, P.TextDim, 0f, 100f, 430f);
                    _hint = Label(root, "let go over a wedge to put it there", 8.5f, P.TextMute, 96f, 40f, 220f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(15f, 10f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                float open = Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 8.2f, 8.6f));
                float reach = Mathf.Clamp01(Ease(u, 1.0f, 1.3f) - Ease(u, 4.0f, 4.4f));
                _w.Dim = 1f - 0.55f * reach;
                _w.SetBloom(open);
                _alt.SetPressed(Held(u, 0.9f, 5.0f));

                Vector2 hub = _w.Root.anchoredPosition;
                Vector2 slot = _w.ScenePointOn(Target, (_w.Inner + _w.Outer) * 0.5f);
                Vector2 grab = _floorItem + new Vector2(-L(8f, 4f), L(8f, 4f));
                Vector2 at = Vector2.Lerp(hub, grab, Ease(u, 1.4f, 2.4f));
                at = Vector2.Lerp(at, slot + new Vector2(-L(8f, 4f), L(8f, 4f)), Ease(u, 2.9f, 4.2f));
                at = Vector2.Lerp(at, at + new Vector2(L(20f, 8f), -L(14f, 6f)), Ease(u, 4.6f, 5.2f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 1.05f, 1.3f), 1f - Ease(u, 5.3f, 5.6f)));
                float held = Held(u, 2.55f, 4.45f);
                _cur.SetClick(held);

                // The item: on the floor, lifted with the pointer, then placed into the wedge.
                bool placed = u >= 4.45f;
                if (u < 2.6f) _item.Set(_floorItem, Mathf.Min(Ease(u, 0f, 0.3f), 1f));
                else if (!placed) _item.Set(at + new Vector2(L(8f, 4f), -L(8f, 4f)), 1f);
                else _item.Hide();
                if (_placed.gameObject.activeSelf != placed) _placed.gameObject.SetActive(placed);
                _w.Tint(placed ? Target : -1, P.Good, Bump(u, 4.45f, 5.0f) * 0.6f);
                if (_hint != null) _hint.color = WithA(P.TextMute, Window01(u, 2.8f, 4.6f, 0.2f));
            }

            public override void Destroy() { _w = null; _cur = null; _alt = null; _item = null; }
        }

        // =====================================================================================
        //  equiptaphold (6.1, 6.2) — tap the suit's gear key: its small wheel blooms; hold it: the
        //  suit comes off into your hand.
        // =====================================================================================

        private sealed class EquipTapHoldScene : Scene
        {
            internal const float Loop = 9f;
            private Wheel _w;
            private KeyVis _key;
            private Ripple _rip;
            private PanelGraphic _suitBox, _suitIcon, _meterFill, _handItem;
            private PanelGraphic[] _rows;
            private Flyer _fly;
            private HandPair _hands;
            private Vector2 _handAt, _suitAt;
            private float _meterW;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;
            private int _stateIdx = -1;

            private static readonly string[] Worn = { "HELMET", "GLASSES", "SUIT", "BACK", "UNIFORM", "BELT" };
            private static readonly string[] SuitRing = { "BATTERY", "OXYGEN", "FILTER", "PRESSURE", "TEMP" };

            protected override void Make(RectTransform root)
            {
                string three = K("{V:SuitSlot}");
                if (Mini)
                {
                    _suitAt = new Vector2(-52f, 16f);
                    _suitBox = Panel(root, "suit", 40f, 24f, MulA(P.Panel, 0.95f), P.Border, 5f, 1.4f);
                    At(_suitBox, _suitAt);
                    _key = KeyVis.Make(root, "key", three, P, 18f, 10f, 26f);
                    _key.SetPos(-52f, -20f);
                    _handAt = new Vector2(56f, -18f);
                    At(Panel(root, "hand", 30f, 22f, MulA(P.Panel, 0.95f), P.Selected, 5f, 2f), _handAt);
                }
                else
                {
                    _rows = new PanelGraphic[6];
                    var col = Node(root, "column");
                    col.anchoredPosition = new Vector2(-150f, 6f);
                    for (int i = 0; i < 6; i++)
                    {
                        float y = 70f - i * 28f;
                        _rows[i] = Panel(col, "row" + i, 116f, 23f, MulA(P.Panel, 0.95f), P.Border, 5f, 1.2f);
                        At(_rows[i], 0f, y);
                        At(Panel(col, "d" + i, 18f, 16f, MulA(P.Raised, 0.95f), MulA(P.Border, 0.8f), 3.5f, 1f), -45f, y);
                        Label(col, K(GearTokens[i]), 9.5f, P.Text, -45f, y, 24f);
                        Label(col, Worn[i], 8.5f, P.TextDim, 12f, y, 90f, TextAlignmentOptions.Left);
                    }
                    _suitBox = _rows[2];
                    _suitAt = col.anchoredPosition + new Vector2(30f, 70f - 2 * 28f);
                    _key = KeyVis.Make(root, "key", three, P, 30f, 14f, 44f);
                    _key.SetPos(-20f, -74f);
                    _hands = HandPair.Make(root, "hands", P, 58f, 40f, 10f, true, 6.5f);
                    _hands.Root.anchoredPosition = new Vector2(146f, -62f);
                    _handAt = _hands.SceneCenter(0);
                    _caps = new[]
                    {
                        "TAP " + three + " - YOUR SUIT'S WHEEL",
                        "HOLD " + three + " - TAKE IT OFF INTO YOUR HAND"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
                _suitIcon = Panel(root, "suitIcon", L(14f, 11f), L(14f, 11f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_suitIcon, _suitAt);
                _handItem = Panel(root, "handItem", L(16f, 11f), L(16f, 11f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem, _handAt);
                _handItem.gameObject.SetActive(false);
                _fly = Flyer.Make(root, "fly", L(14f, 10f), P);

                _w = Wheel.Make(root, "suitWheel", L(54f, 22f), L(23f, 9f), 5, P, true, false, 7f, !Mini);
                _w.SetPos(L(40f, 12f), L(26f, 6f));
                _w.SetCount(5);
                if (Mini) _w.MakeIcons(3.5f, MulA(P.Accent, 0.7f));
                else { _w.SetLabels(SuitRing); _w.Line0.text = "SUIT"; }
                _w.SetBloom(0f);

                _meterW = L(56f, 26f);
                float my = _key.Position.y - _key.H * 0.5f - L(9f, 5f);
                At(Panel(root, "track", _meterW, L(4f, 3f), MulA(P.Track, 0.9f), Color.clear, 1.5f, 0f), _key.Position.x, my);
                _meterFill = Panel(root, "fill", 3f, L(4f, 3f), P.Selected, Color.clear, 1.5f, 0f);
                At(_meterFill, _key.Position.x - _meterW * 0.5f, my);
                _rip = Ripple.Make(root, "rip", P.Accent, L(16f, 10f), L(32f, 18f), L(2f, 1.4f));
                _rip.SetPos(_key.PressPoint);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                // Beat 1: tap -> the wheel; tap again -> closed. Beat 2: hold -> off into the hand.
                float tap = Mathf.Max(Bump(u, 0.4f, 0.7f), Bump(u, 2.9f, 3.2f));
                float hold = Held(u, 4.2f, 5.8f, 0.12f);
                _key.SetPressed(Mathf.Max(tap, hold));
                float r1 = Seg(u, 0.5f, 1.1f), r2 = Seg(u, 3.0f, 3.6f);
                _rip.Play(r1 > 0f && r1 < 1f ? r1 : r2);
                _w.SetBloom(Mathf.Min(Ease(u, 0.7f, 1.1f), 1f - Ease(u, 3.1f, 3.5f)));
                _w.Tint(-1, P.Accent, 0f);

                float fill = hold > 0.01f ? Seg(u, 4.3f, 5.0f) : 0f;
                float w = Mathf.Max(3f, _meterW * fill);
                _meterFill.SetShape(w, L(4f, 3f), 1.5f);
                ((RectTransform)_meterFill.transform).sizeDelta = new Vector2(w, L(4f, 3f));
                At(_meterFill, _key.Position.x - _meterW * 0.5f + w * 0.5f, Pos(_meterFill).y);
                _meterFill.color = WithA(P.Selected, hold > 0.01f ? 1f : 0f);

                float lit = Mathf.Max(Window01(u, 0.45f, 3.4f, 0.2f), Window01(u, 4.25f, 5.2f, 0.15f));
                _suitBox.BorderColor = Color.Lerp(P.Border, P.Accent, lit);
                _suitBox.color = Hot(MulA(P.Panel, 0.95f), P.Accent, lit * 0.25f);

                bool off = u >= 5.0f && u < 8.5f;
                bool flying = u >= 5.0f && u < 5.6f;
                if (flying) _fly.Set(Arc(_suitAt, _handAt, Seg(u, 5.0f, 5.6f), L(30f, 12f)), 1f);
                else _fly.Hide();
                bool inHand = u >= 5.55f && u < 8.5f;
                if (_handItem.gameObject.activeSelf != inHand) _handItem.gameObject.SetActive(inHand);
                _suitIcon.color = WithA(P.Accent, off ? 0f : 0.75f * Ease(u, 0f, 0.3f));
                _suitIcon.BorderColor = WithA(P.Accent, off ? 0f : Ease(u, 0f, 0.3f));

                if (Mini) return;
                int st = inHand ? 1 : 0;
                if (st != _stateIdx) { _stateIdx = st; _hands.State[0].text = st == 1 ? "Suit" : "empty"; }
                int cap = u < 3.9f ? 0 : 1;
                if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
            }

            public override void Destroy() { _w = null; _key = null; _hands = null; _rows = null; }
        }

        // =====================================================================================
        //  beltswap (7.1, 7.2, 18.4) — the page key turns the belt ring into every belt you carry;
        //  a click wears it (the word reads SWAP); the right button goes back without swapping.
        // =====================================================================================

        private sealed class BeltSwapScene : Scene
        {
            internal const float Loop = 11f;
            private Wheel _w;
            private Cursor _cur;
            private WordRing _words;
            private KeyVis _q;
            private Mouse _rmb;
            private Ripple _ripQ, _ripR;
            private int _level = -1;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;
            private Color _mineTint;

            private static readonly string[] Tools =
                { "THE HUB", "DRILL", "WRENCH", "CROWBAR", "CUTTERS", "WELDER", "GRINDER", "CABLE" };
            private static readonly string[] Pick1 = { "TOOLBELT", "MINING BELT" };
            private static readonly string[] Mine =
                { "THE HUB", "ORE", "ORE", "ICE", "ORE", "", "", "" };
            private static readonly string[] Pick2 = { "MINING BELT", "TOOLBELT" };
            private static readonly string[] Title = { "TOOLBELT", "BELTS", "MINING BELT", "BELTS" };
            private static readonly string[] Sub = { "click a tool", "pick one to wear", "worn", "pick one to wear" };

            protected override void Make(RectTransform root)
            {
                string q = K("{UIA_Page}");
                float outer = L(74f, 30f), inner = L(34f, 13f);
                _w = Wheel.Make(root, "ring", outer, inner, 8, P, true, false, 7.5f, !Mini);
                _w.SetPos(L(-64f, -30f), L(-2f, 0f));
                _w.SetCount(8);
                if (Mini) _w.MakeIcons(4f, MulA(P.Accent, 0.7f));
                else
                {
                    var swap = ArcWord.Make(C, _w.Root, "SWAP", outer, 8f, 11f, P.Accent,
                        MulA(P.Panel, 0.92f), MulA(P.Border, 0.8f));
                    _words = new WordRing(_w, swap);
                }
                _mineTint = Hot(_w.Fill, P.Warn, 0.22f);
                _w.SetBloom(0f);
                _q = KeyVis.Make(root, "q", q, P, L(30f, 18f), L(14f, 10f), L(44f, 26f));
                _q.SetPos(L(150f, 44f), L(58f, 20f));
                _ripQ = Ripple.Make(root, "ripQ", P.Accent, L(16f, 10f), L(32f, 18f), L(2f, 1.4f));
                _ripQ.SetPos(_q.PressPoint);
                _rmb = Mouse.Make(root, "rmb", P, L(44f, 26f));
                _rmb.SetPos(L(150f, 44f), L(-24f, -16f));
                _ripR = Ripple.Make(root, "ripR", P.Accent, L(4f, 3f), L(22f, 13f), L(2f, 1.4f));
                _ripR.SetPos(_rmb.ButtonPos(1));
                if (!Mini)
                {
                    Label(root, "RIGHT-CLICK", 8.5f, P.TextDim, 150f, -58f, 110f);
                    _caps = new[]
                    {
                        "PRESS " + q + " - EVERY BELT YOU CARRY",
                        "CLICK ONE - YOU WEAR IT, THE OLD ONE TAKES ITS PLACE",
                        "RIGHT-CLICK - BACK WITHOUT SWAPPING"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                // 0 tool ring -> 1 picker -> 2 the mining belt, worn -> 3 picker again -> 2 (RMB back)
                int level = u < 1.15f ? 0 : u < 2.95f ? 1 : u < 5.25f ? 2 : u < 6.8f ? 3 : 2;
                if (level != _level)
                {
                    _level = level;
                    bool picker = level == 1 || level == 3;
                    _w.SetCount(picker ? 2 : 8);
                    Color baseFill = level == 2 ? _mineTint : _w.Fill;
                    for (int i = 0; i < 8; i++) _w.SetBase(i, baseFill);
                    if (!Mini)
                    {
                        _w.SetLabels(level == 0 ? Tools : level == 1 ? Pick1 : level == 2 ? Mine : Pick2);
                        _w.Line0.text = Title[level];
                        _w.Line1.text = Sub[level];
                        for (int i = 0; i < _w.Labels.Length; i++) _w.Labels[i].color = P.Text;
                        if (picker) _w.Labels[0].color = P.TextMute;   // the worn belt
                    }
                    else
                    {
                        for (int i = 0; i < 8; i++) _w.ShowIcon(i, !picker && i > 0 && (level == 0 || i < 5));
                        if (picker) { _w.ShowIcon(0, true); _w.ShowIcon(1, true); }
                    }
                }
                float open = Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 9.8f, 10.2f));
                float dip = 1f - 0.5f * (Bump(u, 1.05f, 1.3f) + Bump(u, 2.85f, 3.1f) + Bump(u, 5.15f, 5.4f) + Bump(u, 6.7f, 6.95f));
                _w.SetBloom(open * dip);

                _q.SetPressed(Mathf.Max(Bump(u, 0.9f, 1.15f), Bump(u, 5.0f, 5.25f)));
                float rq1 = Seg(u, 1.0f, 1.6f), rq2 = Seg(u, 5.1f, 5.7f);
                _ripQ.Play(rq1 > 0f && rq1 < 1f ? rq1 : rq2);
                float rmb = Bump(u, 6.4f, 6.75f);
                _rmb.SetButton(1, rmb);
                _ripR.Play(Seg(u, 6.5f, 7.1f));

                Vector2 hub = _w.Root.anchoredPosition;
                float midR = (_w.Inner + _w.Outer) * 0.5f;
                // The picker's second belt sits at the bottom (two wedges: 12 and 6 o'clock).
                Vector2 at = Vector2.Lerp(hub, hub + new Vector2(0f, -midR), Ease(u, 1.5f, 2.3f));
                at = Vector2.Lerp(at, hub + new Vector2(L(10f, 4f), L(8f, 3f)), Ease(u, 3.2f, 3.9f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 1.3f, 1.5f), 1f - Ease(u, 9.6f, 9.9f)));
                _cur.SetClick(Mathf.Max(Bump(u, 2.6f, 2.9f), rmb));

                bool settled = open * dip > 0.7f;
                int hov = settled && level == 1 ? _w.HitTest(at, _w.Inner) : -1;
                _w.Tint(hov, P.Selected, 0.5f + 0.5f * Bump(u, 2.6f, 3.0f));
                _w.SetBulge(hov, L(5f, 3f));
                if (_words != null) _words.Update(t, hov, hov == 1 ? 0 : -1);
                // A swap flash on the new ring.
                if (_w.Hub != null)
                    _w.Hub.BorderColor = Color.Lerp(_w.Border, P.Good, Bump(u, 2.95f, 3.7f));

                if (_cap != null)
                {
                    int cap = u < 1.5f ? 0 : u < 5.8f ? 1 : 2;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _w = null; _cur = null; _q = null; _rmb = null; _words = null; }
        }

        // =====================================================================================
        //  ghostlabel (7.3) — an empty belt slot shows its tool's name in grey along its inner edge;
        //  the stow key sends the tool home.
        // =====================================================================================

        private sealed class GhostLabelScene : Scene
        {
            internal const float Loop = 9f;
            private const int Drill = 1;
            private Wheel _w;
            private CanvasGroup _ghost;
            private RadialWedgeGraphic _ghostArc;   // mini: a grey arc instead of the word
            private PanelGraphic _icon, _handItem;
            private Flyer _fly;
            private KeyVis _g;
            private Ripple _rip;
            private HandPair _hands;
            private Vector2 _handAt;
            private int _state = -1;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            private static readonly string[] Belt =
                { "THE HUB", "DRILL", "WRENCH", "CROWBAR", "CUTTERS", "WELDER", "GRINDER", "CABLE" };

            protected override void Make(RectTransform root)
            {
                string g = K("{V:SmartStow}");
                float outer = L(80f, 30f), inner = L(36f, 13f);
                _w = Wheel.Make(root, "ring", outer, inner, 8, P, true, false, 7.5f, !Mini);
                _w.SetPos(L(-60f, -30f), L(-2f, 0f));
                _w.SetCount(8);
                if (Mini) { _w.MakeIcons(4f, MulA(P.Accent, 0.7f)); _w.ShowIcon(0, false); _w.ShowIcon(Drill, false); }
                else { _w.SetLabels(Belt); _w.Line0.text = "TOOLBELT"; }
                _w.SetBloom(0f);
                float midR = (inner + outer) * 0.5f;
                _icon = Panel(_w.Root, "drill", L(14f, 7f), L(14f, 7f), MulA(P.Accent, 0.8f), P.Accent, 3f, 1.1f);
                At(_icon, _w.PointOn(Drill, midR + L(13f, 0f)));

                _ghost = GroupNode(_w.Root, "ghost");
                if (Mini)
                {
                    float a = _w.MidAngle(Drill);
                    _ghostArc = Wedge((RectTransform)_ghost.transform, "arc", MulA(P.TextDim, 0.8f), Color.clear, 0f);
                    _ghostArc.SetGeometry(inner + 2f, inner + 4.5f, a - 0.28f, a + 0.28f, false);
                    _ghostArc.RefreshGeometry();
                }
                else
                {
                    // The real ghost: the bound tool's name, grey, bent along the wedge's inner edge.
                    // Rule-8 content exemption (deliberate): the fixed grey IS the lesson - an empty belt
                    // slot's ghost name reads grey on the real wheel - demo content, not a HUD knob.
                    CurvedAt(C, (RectTransform)_ghost.transform, "DRILL", 7.5f, new Color(0.62f, 0.64f, 0.67f, 0.95f),
                        Vector2.zero, inner + 10f, _w.MidAngle(Drill));
                }
                _ghost.alpha = 0f;

                _fly = Flyer.Make(root, "fly", L(16f, 8f), P);
                if (Mini)
                {
                    _handAt = new Vector2(48f, 16f);
                    At(Panel(root, "hand", 32f, 24f, MulA(P.Panel, 0.95f), P.Selected, 5f, 2f), _handAt);
                    _g = KeyVis.Make(root, "g", g, P, 18f, 10f, 26f);
                    _g.SetPos(48f, -20f);
                }
                else
                {
                    _hands = HandPair.Make(root, "hands", P, 60f, 42f, 10f, true, 6.5f);
                    _hands.Root.anchoredPosition = new Vector2(144f, 34f);
                    _handAt = _hands.SceneCenter(0);
                    _g = KeyVis.Make(root, "g", g, P, 28f, 13f, 40f);
                    _g.SetPos(144f, -36f);
                    _caps = new[]
                    {
                        "AN EMPTY SLOT KEEPS ITS TOOL'S NAME - IN GREY",
                        g + " - THE TOOL FINDS ITS WAY HOME"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
                _handItem = Panel(root, "handItem", L(16f, 11f), L(16f, 11f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem, _handAt);
                _handItem.gameObject.SetActive(false);
                _rip = Ripple.Make(root, "rip", P.Accent, L(15f, 10f), L(30f, 18f), L(2f, 1.4f));
                _rip.SetPos(_g.PressPoint);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                _w.SetBloom(Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 8.4f, 8.8f)));
                Vector2 slot = _w.Root.anchoredPosition + Pos(_icon);
                bool away = u >= 0.9f && u < 5.6f;
                bool out1 = u >= 0.9f && u < 1.5f;
                bool back = u >= 5.0f && u < 5.6f;
                if (out1) _fly.Set(Arc(slot, _handAt, Seg(u, 0.9f, 1.5f), L(30f, 12f)), 1f);
                else if (back) _fly.Set(Arc(_handAt, slot, Seg(u, 5.0f, 5.6f), L(30f, 12f)), 1f);
                else _fly.Hide();
                if (_icon.gameObject.activeSelf == away) _icon.gameObject.SetActive(!away);
                bool inHand = u >= 1.45f && u < 5.0f;
                if (_handItem.gameObject.activeSelf != inHand) _handItem.gameObject.SetActive(inHand);

                _ghost.alpha = Mathf.Clamp01(Ease(u, 1.4f, 1.8f) - Ease(u, 5.4f, 5.7f)) * (0.75f + 0.25f * Mathf.Sin(u * 3.5f));
                _g.SetPressed(Bump(u, 4.6f, 4.9f));
                _rip.Play(Seg(u, 4.7f, 5.3f));
                _w.Tint(Drill, P.Accent, Bump(u, 5.55f, 6.1f) * 0.5f);

                if (Mini) return;
                int state = away ? 1 : 0;
                if (state != _state)
                {
                    _state = state;
                    _w.Labels[Drill].text = away ? "" : "DRILL";
                    _hands.State[0].text = away ? "Drill" : "empty";
                }
                int cap = u < 4.4f ? 0 : 1;
                if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
            }

            public override void Destroy() { _w = null; _g = null; _hands = null; _fly = null; }
        }

        // =====================================================================================
        //  hubroot (8.1, 8.2, 8.4) — the bag key opens THE HUB: RECENT ITEM (tagged), SEARCH and
        //  all the gear you wear; the recent item hands you another; a bag wedge dives inside.
        // =====================================================================================

        private sealed class HubRootScene : Scene
        {
            internal const float Loop = 11f;
            private const int Recent = 0, Backpack = 5;
            private Wheel _w;
            private Cursor _cur;
            private WordRing _words;
            private KeyVis _tab;
            private Ripple _rip;
            private CanvasGroup _tag;
            private Flyer _fly;
            private HandPair _hands;
            private PanelGraphic _handItem;
            private Vector2 _handAt;
            private int _level = -1, _stateIdx = -1;
            private Color _recentFill;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            private static readonly string[] Root =
                { "CABLE COIL", "SEARCH", "HELMET", "GLASSES", "SUIT", "BACKPACK", "UNIFORM", "TOOLBELT" };
            private static readonly string[] Inside =
                { "ICE", "ORE", "ORE", "CANISTER", "FILTER", "FLARES", "TORCH", "SORT" };

            protected override void Make(RectTransform root)
            {
                string tab = K("{UIA_BagRadial}");
                float outer = L(78f, 30f), inner = L(36f, 13f);
                _w = Wheel.Make(root, "ring", outer, inner, 8, P, true, false, 7f, !Mini);
                _w.SetPos(L(-52f, -26f), L(-4f, -2f));
                _w.SetCount(8);
                _recentFill = Hot(_w.Fill, P.Accent, 0.38f);
                if (Mini) _w.MakeIcons(4f, MulA(P.Accent, 0.7f));
                else
                {
                    var take = ArcWord.Make(C, _w.Root, "TAKE", outer, 8f, 11f, P.Accent, MulA(P.Panel, 0.92f), MulA(P.Border, 0.8f));
                    var open = ArcWord.Make(C, _w.Root, "OPEN", outer, 8f, 11f, P.Accent, MulA(P.Panel, 0.92f), MulA(P.Border, 0.8f));
                    _words = new WordRing(_w, take, open);
                    // D-023: the persistent tag inside the recent wedge, along its outer rim.
                    _tag = GroupNode(_w.Root, "tag");
                    CurvedAt(C, (RectTransform)_tag.transform, "RECENT ITEM", 6.5f, P.TextDim, Vector2.zero,
                        outer - 10f, _w.MidAngle(Recent));
                }
                _w.SetBloom(0f);

                _tab = KeyVis.Make(root, "tab", tab, P, L(28f, 18f), L(13f, 10f), L(40f, 26f));
                _tab.SetPos(L(170f, 52f), L(66f, 20f));
                _rip = Ripple.Make(root, "rip", P.Accent, L(15f, 10f), L(30f, 18f), L(2f, 1.4f));
                _rip.SetPos(_tab.PressPoint);
                _fly = Flyer.Make(root, "fly", L(16f, 8f), P);
                if (Mini)
                {
                    _handAt = new Vector2(52f, -16f);
                    At(Panel(root, "hand", 30f, 22f, MulA(P.Panel, 0.95f), P.Selected, 5f, 2f), _handAt);
                }
                else
                {
                    _hands = HandPair.Make(root, "hands", P, 58f, 40f, 10f, true, 6.5f);
                    _hands.Root.anchoredPosition = new Vector2(152f, -62f);
                    _handAt = _hands.SceneCenter(0);
                    _caps = new[]
                    {
                        tab + " OR THE HUB: SEARCH, RECENT ITEM, AND ALL YOUR GEAR",
                        "RECENT ITEM - ANOTHER OF THE LAST THING YOU TOOK",
                        "CLICK A BAG TO LOOK INSIDE"
                    };
                    // Bottom, not top: the word over the RECENT wedge (12 o'clock) reaches y ~103.
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, -100f, 430f);
                }
                _handItem = Panel(root, "handItem", L(16f, 11f), L(16f, 11f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem, _handAt);
                _handItem.gameObject.SetActive(false);
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                int level = u < 5.95f ? 0 : 1;
                if (level != _level)
                {
                    _level = level;
                    for (int i = 0; i < 8; i++) _w.SetBase(i, level == 0 && i == Recent ? _recentFill : _w.Fill);
                    if (!Mini)
                    {
                        _w.SetLabels(level == 0 ? Root : Inside);
                        _w.Line0.text = level == 0 ? "THE HUB" : "BACKPACK";
                        _w.Line1.text = level == 0 ? "click a wedge" : "grouped by type";
                        _tag.alpha = level == 0 ? 1f : 0f;
                    }
                }
                _tab.SetPressed(Mathf.Max(Bump(u, 0.2f, 0.45f), Bump(u, 3.6f, 3.85f)));
                float r1 = Seg(u, 0.3f, 0.9f), r2 = Seg(u, 3.7f, 4.3f);
                _rip.Play(r1 > 0f && r1 < 1f ? r1 : r2);
                float openA = Mathf.Min(Ease(u, 0.35f, 0.8f), 1f - Ease(u, 2.4f, 2.8f));
                float openB = Mathf.Min(Ease(u, 3.75f, 4.2f), 1f - Ease(u, 9.6f, 10.0f)) * (1f - 0.5f * Bump(u, 5.8f, 6.1f));
                float open = u < 3.2f ? openA : openB;
                _w.SetBloom(open);

                Vector2 hub = _w.Root.anchoredPosition;
                float midR = (_w.Inner + _w.Outer) * 0.5f;
                Vector2 at;
                if (u < 3.2f) at = Vector2.Lerp(hub, _w.ScenePointOn(Recent, midR), Ease(u, 1.1f, 1.8f));
                else
                {
                    at = Vector2.Lerp(hub, _w.ScenePointOn(Backpack, midR), Ease(u, 4.5f, 5.3f));
                    at = Vector2.Lerp(at, hub + new Vector2(L(8f, 3f), L(6f, 2f)), Ease(u, 6.4f, 7.2f));
                }
                _cur.SetPos(at);
                _cur.SetAlpha(u < 3.2f ? Mathf.Min(Ease(u, 0.95f, 1.15f), 1f - Ease(u, 2.4f, 2.6f))
                    : Mathf.Min(Ease(u, 4.3f, 4.5f), 1f - Ease(u, 9.4f, 9.7f)));
                _cur.SetClick(Mathf.Max(Bump(u, 2.1f, 2.4f), Bump(u, 5.6f, 5.9f)));

                bool settled = open > 0.7f;
                int hov = settled && level == 0 ? _w.HitTest(at, _w.Inner) : -1;
                _w.Tint(hov, P.Selected, 0.45f + 0.5f * Mathf.Max(Bump(u, 2.1f, 2.5f), Bump(u, 5.6f, 6.0f)));
                _w.SetBulge(hov, L(5f, 3f));
                if (_words != null) _words.Update(t, hov, hov == Recent ? 0 : hov == Backpack ? 1 : -1);

                bool flying = u >= 2.4f && u < 3.0f;
                if (flying) _fly.Set(Arc(_w.ScenePointOn(Recent, midR), _handAt, Seg(u, 2.4f, 3.0f), L(30f, 12f)), 1f);
                else _fly.Hide();
                bool inHand = u >= 2.95f && u < 10.4f;
                if (_handItem.gameObject.activeSelf != inHand) _handItem.gameObject.SetActive(inHand);

                if (Mini) return;
                int st = inHand ? 1 : 0;
                if (st != _stateIdx) { _stateIdx = st; _hands.State[0].text = st == 1 ? "Cable Coil" : "empty"; }
                int cap = u < 1.1f ? 0 : u < 3.4f ? 1 : u < 4.5f ? 0 : 2;
                if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
            }

            public override void Destroy() { _w = null; _cur = null; _tab = null; _words = null; _hands = null; }
        }

        // =====================================================================================
        //  bindbag (8.5) — point at a bag and press a number: a Ctrl-digit badge; later Ctrl + that
        //  number opens the bag's own wheel from anywhere.
        // =====================================================================================

        private sealed class BindBagScene : Scene
        {
            internal const float Loop = 10f;
            private const int Bag = 5;
            private Wheel _w;
            private Cursor _cur;
            private KeyVis _seven, _ctrl, _seven2;
            private Ripple _rip;
            private CanvasGroup _badge;
            private int _level = -1;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            private static readonly string[] Hub =
                { "RECENT", "SEARCH", "HELMET", "GLASSES", "SUIT", "BACKPACK", "UNIFORM", "TOOLBELT" };
            private static readonly string[] Inside =
                { "ICE", "ORE", "ORE", "CANISTER", "FILTER", "FLARES", "TORCH", "SORT" };

            protected override void Make(RectTransform root)
            {
                float outer = L(72f, 30f), inner = L(33f, 13f);
                _w = Wheel.Make(root, "ring", outer, inner, 8, P, true, false, 7f, !Mini);
                _w.SetPos(L(-70f, -30f), L(-2f, 0f));
                _w.SetCount(8);
                if (Mini) _w.MakeIcons(4f, MulA(P.Accent, 0.7f));
                _w.SetBloom(0f);

                // The Ctrl-digit badge near the hub side of the bag wedge (the live ring's "^7").
                _badge = GroupNode(_w.Root, "badge");
                At(_badge, _w.PointOn(Bag, inner + L(11f, 5f)));
                Panel((RectTransform)_badge.transform, "b", L(20f, 10f), L(14f, 9f), MulA(P.Accent, 0.35f), P.Accent, 3f, 1.1f);
                Label((RectTransform)_badge.transform, Mini ? "7" : "^7", L(8.5f, 7f), P.Text, 0f, 0f, 24f);
                _badge.alpha = 0f;

                _seven = KeyVis.Make(root, "seven", "7", P, L(30f, 16f), L(14f, 9f), 30f);
                _seven.SetPos(L(112f, 40f), L(50f, 20f));
                _ctrl = KeyVis.Make(root, "ctrl", "Ctrl", P, L(28f, 14f), L(12f, 8f), 30f);
                _seven2 = KeyVis.Make(root, "seven2", "7", P, L(28f, 14f), L(12f, 8f), 30f);
                float gap = L(26f, 10f);
                float total = _ctrl.W + gap + _seven2.W;
                float cx = L(112f, 44f), cy = L(-34f, -20f);
                _ctrl.SetPos(cx - total * 0.5f + _ctrl.W * 0.5f, cy);
                _seven2.SetPos(cx + total * 0.5f - _seven2.W * 0.5f, cy);
                PlusGlyph(root, "plus", L(10f, 6f), L(2.4f, 1.6f), P.TextDim).anchoredPosition =
                    new Vector2(cx - total * 0.5f + _ctrl.W + gap * 0.5f, cy);
                _rip = Ripple.Make(root, "rip", P.Accent, L(15f, 8f), L(30f, 16f), L(2f, 1.4f));
                _rip.SetPos(_seven.PressPoint);
                if (!Mini)
                {
                    _caps = new[]
                    {
                        "POINT AT A BAG AND PRESS A NUMBER (1 - 0)",
                        "CTRL + THAT NUMBER OPENS IT FROM ANYWHERE"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                    Label(root, "saved with this world", 8.5f, P.TextMute, 112f, 20f, 160f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                int level = u < 4.5f ? 0 : 1;
                if (level != _level)
                {
                    _level = level;
                    if (!Mini)
                    {
                        _w.SetLabels(level == 0 ? Hub : Inside);
                        _w.Line0.text = level == 0 ? "THE HUB" : "BACKPACK";
                    }
                }
                float openA = Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 3.3f, 3.7f));
                float openB = Mathf.Min(Ease(u, 5.3f, 5.8f), 1f - Ease(u, 8.6f, 9.0f));
                _w.SetBloom(level == 0 ? openA : openB);
                Vector2 hub = _w.Root.anchoredPosition;
                Vector2 at = Vector2.Lerp(hub, _w.ScenePointOn(Bag, (_w.Inner + _w.Outer) * 0.5f), Ease(u, 0.6f, 1.4f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.45f, 0.65f), 1f - Ease(u, 3.2f, 3.5f)));
                int hov = level == 0 && openA > 0.7f ? _w.HitTest(at, _w.Inner) : -1;
                _w.Tint(hov, P.Selected, 0.45f);
                _w.SetBulge(hov, L(5f, 3f));

                _seven.SetPressed(Bump(u, 1.8f, 2.05f));
                _rip.Play(Seg(u, 1.9f, 2.5f));
                _badge.alpha = level == 0 ? Ease(u, 1.95f, 2.2f) : 0f;
                Scale(_badge, 1f + 0.35f * Bump(u, 1.95f, 2.45f));
                _ctrl.SetPressed(Held(u, 4.6f, 5.9f));
                _seven2.SetPressed(Bump(u, 5.1f, 5.35f));

                if (_cap != null)
                {
                    int cap = u < 4.0f ? 0 : 1;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _w = null; _cur = null; _seven = null; _ctrl = null; _seven2 = null; }
        }

        // =====================================================================================
        //  valuescroll (10.1) — a value between two arrows ticks with the scroll wheel; holding the
        //  fine key makes it tick by one; a plain click nudges it up one step.
        // =====================================================================================

        private sealed class ValueScrollScene : Scene
        {
            internal const float Loop = 9f;
            private PanelGraphic _box;
            private TextMeshProUGUI _value;
            private TriangleGraphic _up, _down;
            private Mouse _mouse;
            private KeyVis _fine;
            private Cursor _cur;
            private string[] _vals;
            private int _idx = -1;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            protected override void Make(RectTransform root)
            {
                string c = K("{UIA_FineAdjust}");
                // 101 -> 151 by tens, 152 -> 156 by ones, then a click: +10.
                _vals = new string[12];
                for (int i = 0; i < 6; i++) _vals[i] = (101 + i * 10) + " kPa";
                for (int i = 0; i < 5; i++) _vals[6 + i] = (152 + i) + " kPa";
                _vals[11] = "166 kPa";

                Vector2 boxAt = new Vector2(L(-70f, -30f), L(16f, 8f));
                float bw = L(170f, 70f), bh = L(56f, 28f);
                _box = Panel(root, "value", bw, bh, MulA(P.Panel, 0.95f), P.Border, L(10f, 6f), 1.5f);
                At(_box, boxAt);
                if (!Mini) Label(root, "SUIT PRESSURE", 8f, P.TextDim, boxAt.x - 10f, boxAt.y + 16f, 140f);
                _value = Label(root, _vals[0], L(16f, 11f), P.Text, boxAt.x - L(12f, 6f), boxAt.y - L(4f, 0f), bw - L(30f, 16f));
                _up = Tri(root, "up", L(12f, 7f), P.Accent, true, 0f);
                At(_up, boxAt + new Vector2(bw * 0.5f - L(16f, 8f), L(11f, 6f)));
                _down = Tri(root, "down", L(12f, 7f), P.Accent, false, 0f);
                At(_down, boxAt + new Vector2(bw * 0.5f - L(16f, 8f), -L(11f, 6f)));

                _mouse = Mouse.Make(root, "mouse", P, L(70f, 32f));
                _mouse.SetPos(L(120f, 44f), L(22f, 8f));
                _fine = KeyVis.Make(root, "fine", c, P, L(30f, 16f), L(14f, 9f), L(40f, 24f));
                _fine.SetPos(L(120f, -30f), L(-62f, -24f));
                _fine.SetAlpha(0.35f);
                _cur = Cursor.Make(root, "cursor", P, L(15f, 10f));
                _cur.SetPos(boxAt + new Vector2(-L(20f, 6f), -L(6f, 4f)));
                if (!Mini)
                {
                    _caps = new[]
                    {
                        "THE SCROLL WHEEL - BIG STEPS",
                        "HOLD " + c + " - STEPS OF ONE",
                        "A PLAIN CLICK NUDGES IT UP ONE STEP"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                int idx;
                if (u < 3.8f) idx = Mathf.Clamp((int)(Seg(u, 1.0f, 3.4f) * 5.999f), 0, 5);
                else if (u < 7.0f) idx = 6 + Mathf.Clamp((int)(Seg(u, 4.2f, 6.4f) * 4.999f), 0, 4);
                else idx = u < 7.3f ? 10 : 11;
                if (idx != _idx) { _idx = idx; _value.text = _vals[idx]; }

                bool coarse = u > 1.0f && u < 3.4f, fine = u > 4.2f && u < 6.4f;
                float dir = Mathf.Max(Window01(u, 0.95f, 3.45f, 0.1f), Window01(u, 4.15f, 6.45f, 0.1f));
                _mouse.Scroll(Seg(u, 1.0f, 3.4f) * 5f + Seg(u, 4.2f, 6.4f) * 4f, dir);
                float fineHeld = Held(u, 3.8f, 6.7f, 0.15f);
                _fine.SetAlpha(0.35f + 0.65f * fineHeld);
                _fine.SetPressed(fineHeld);
                _up.color = WithA(P.Accent, coarse || fine ? 1f : 0.35f);
                _down.color = WithA(P.Accent, coarse || fine ? 1f : 0.35f);
                float click = Bump(u, 7.0f, 7.3f);
                _cur.SetClick(click);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.2f, 0.4f), 1f - Ease(u, 8.4f, 8.7f)));
                _box.BorderColor = Color.Lerp(P.Border, P.Accent, Mathf.Max(dir * 0.7f, Bump(u, 7.2f, 7.8f)));
                _value.color = Color.Lerp(P.Text, P.Accent, fineHeld * 0.45f);

                if (_cap != null)
                {
                    int cap = u < 3.8f ? 0 : u < 6.9f ? 1 : 2;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _mouse = null; _fine = null; _cur = null; }
        }

        // =====================================================================================
        //  hotkey (10.2) — point at a setting and press a free letter: a letter badge; outside the
        //  wheel that letter flips the setting (a lamp icon toggles). Session-only.
        // =====================================================================================

        private sealed class HotkeyScene : Scene
        {
            internal const float Loop = 9f;
            private const int Setting = 0;
            private Wheel _w;
            private Cursor _cur;
            private KeyVis _k;
            private Ripple _rip;
            private CanvasGroup _badge;
            private CircleGraphic _lamp, _glow;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            private static readonly string[] Ring = { "LIGHT ON", "MODE", "LOCK", "BATTERY", "SETTINGS" };

            protected override void Make(RectTransform root)
            {
                float outer = L(70f, 26f), inner = L(32f, 11f);
                _w = Wheel.Make(root, "ring", outer, inner, 5, P, true, false, 7.5f, !Mini);
                _w.SetPos(L(-70f, -34f), L(-6f, 4f));
                _w.SetCount(5);
                if (Mini) _w.MakeIcons(4f, MulA(P.Accent, 0.7f));
                else { _w.SetLabels(Ring); _w.Line0.text = "HELMET"; }
                _w.SetBloom(0f);
                _badge = GroupNode(_w.Root, "badge");
                At(_badge, _w.PointOn(Setting, inner + L(12f, 5f)));
                Panel((RectTransform)_badge.transform, "b", L(16f, 10f), L(14f, 9f), MulA(P.Accent, 0.35f), P.Accent, 3f, 1.1f);
                Label((RectTransform)_badge.transform, "K", L(9f, 7f), P.Text, 0f, 0f, 20f);
                _badge.alpha = 0f;

                // "K": an example of a letter neither vanilla nor UI Ascended binds by default.
                _k = KeyVis.Make(root, "k", "K", P, L(30f, 18f), L(14f, 10f), 30f);
                _k.SetPos(L(96f, 24f), L(-58f, -22f));
                _rip = Ripple.Make(root, "rip", P.Accent, L(16f, 10f), L(32f, 18f), L(2f, 1.4f));
                _rip.SetPos(_k.PressPoint);
                Vector2 lampAt = new Vector2(L(150f, 58f), L(30f, 12f));
                _glow = Circle(root, "glow", L(26f, 13f), WithA(P.Warn, 0f), Color.clear, 0f);
                At(_glow, lampAt);
                _lamp = Circle(root, "lamp", L(13f, 7f), MulA(P.Raised, 0.95f), P.Border, 1.4f);
                At(_lamp, lampAt);
                if (!Mini)
                {
                    Label(root, "HELMET LIGHT", 8f, P.TextDim, 150f, 6f, 120f);
                    _caps = new[]
                    {
                        "POINT AT A SETTING, PRESS A LETTER THE GAME DOESN'T USE",
                        "NOW THAT LETTER FLIPS IT FROM ANYWHERE - THIS SESSION"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                float open = Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 3.0f, 3.4f));
                _w.SetBloom(open);
                Vector2 hub = _w.Root.anchoredPosition;
                Vector2 at = Vector2.Lerp(hub, _w.ScenePointOn(Setting, (_w.Inner + _w.Outer) * 0.5f), Ease(u, 0.6f, 1.3f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.45f, 0.65f), 1f - Ease(u, 2.9f, 3.2f)));
                int hov = open > 0.7f ? _w.HitTest(at, _w.Inner) : -1;
                _w.Tint(hov, P.Selected, 0.45f);
                _w.SetBulge(hov, L(5f, 3f));

                float press = Mathf.Max(Bump(u, 1.7f, 1.95f), Mathf.Max(Bump(u, 4.2f, 4.45f), Bump(u, 6.0f, 6.25f)));
                _k.SetPressed(press);
                float r1 = Seg(u, 1.8f, 2.4f), r2 = Seg(u, 4.3f, 4.9f), r3 = Seg(u, 6.1f, 6.7f);
                _rip.Play(r1 > 0f && r1 < 1f ? r1 : r2 > 0f && r2 < 1f ? r2 : r3);
                _badge.alpha = Ease(u, 1.85f, 2.1f);
                Scale(_badge, 1f + 0.35f * Bump(u, 1.85f, 2.35f));

                float on = Mathf.Clamp01(Ease(u, 4.3f, 4.45f) - Ease(u, 6.1f, 6.25f));
                _lamp.color = Color.Lerp(MulA(P.Raised, 0.95f), P.Warn, on);
                _glow.color = WithA(P.Warn, 0.25f * on);
                Scale(_glow, 1f + 0.08f * on * Mathf.Sin(u * 7f));

                if (_cap != null)
                {
                    int cap = u < 3.4f ? 0 : 1;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _w = null; _cur = null; _k = null; }
        }

        // =====================================================================================
        //  shiftkeep (10.3) — click with Shift held: the wheel stays; click without: it closes.
        // =====================================================================================

        private sealed class ShiftKeepScene : Scene
        {
            internal const float Loop = 7.5f;
            private Wheel _a, _b;      // full: side by side. mini: _a only, two beats
            private Cursor _ca, _cb;
            private KeyVis _shiftA;

            protected override void Make(RectTransform root)
            {
                float outer = L(48f, 26f), inner = L(22f, 11f);
                _a = Wheel.Make(root, "keep", outer, inner, 6, P, true, false, 7f, false);
                _a.SetPos(L(-108f, -26f), L(16f, 8f));
                _a.SetCount(6);
                _a.MakeIcons(L(5f, 4f), MulA(P.Accent, 0.7f));
                _a.SetBloom(0f);
                _ca = Cursor.Make(root, "curA", P, L(15f, 10f));
                _shiftA = KeyVis.Make(root, "shiftA", "Shift", P, L(26f, 16f), L(12f, 8.5f), 30f);
                _shiftA.SetPos(L(-108f, 46f), L(-66f, -24f));
                if (!Mini)
                {
                    _b = Wheel.Make(root, "close", outer, inner, 6, P, true, false, 7f, false);
                    _b.SetPos(108f, 16f);
                    _b.SetCount(6);
                    _b.MakeIcons(5f, MulA(P.Accent, 0.7f));
                    _b.SetBloom(0f);
                    _cb = Cursor.Make(root, "curB", P, 15f);
                    Label(root, "SHIFT + CLICK - IT STAYS OPEN", 10f, P.TextDim, -108f, 94f, 210f);
                    Label(root, "CLICK - IT CLOSES", 10f, P.TextDim, 108f, 94f, 210f);
                    Label(root, "flip it for good: " + K("{UIA_Menu}") + " > RADIAL", 8.5f, P.TextMute, 108f, -66f, 200f);
                }
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                if (!Mini)
                {
                    TickKeep(_a, _ca, u, 0f);
                    _shiftA.SetPressed(Held(u, 0.6f, 4.6f));
                    // Right: one click, and the wheel is gone.
                    _b.SetBloom(Mathf.Min(Ease(u, 0.05f, 0.4f), 1f - Ease(u, 2.0f, 2.3f)));
                    Vector2 hub = _b.Root.anchoredPosition;
                    Vector2 at = Vector2.Lerp(hub, _b.ScenePointOn(4, (_b.Inner + _b.Outer) * 0.5f), Ease(u, 0.9f, 1.6f));
                    _cb.SetPos(at);
                    _cb.SetAlpha(Mathf.Min(Ease(u, 0.7f, 0.9f), 1f - Ease(u, 2.1f, 2.4f)));
                    _cb.SetClick(Bump(u, 1.75f, 2.0f));
                    _b.Tint(4, P.Good, Bump(u, 1.8f, 2.2f) * 0.7f);
                    return;
                }
                // Mini: beat 1 = Shift + click, stays (0-3.7); beat 2 = click, closes (3.7-7.5).
                if (u < 3.7f)
                {
                    TickKeep(_a, _ca, u, 0f);
                    _shiftA.SetPressed(Held(u, 0.6f, 3.2f));
                }
                else
                {
                    _shiftA.SetPressed(0f);
                    float v = u - 3.7f;
                    _a.SetBloom(Mathf.Min(Ease(v, 0.05f, 0.4f), 1f - Ease(v, 1.9f, 2.2f)));
                    Vector2 hub = _a.Root.anchoredPosition;
                    Vector2 at = Vector2.Lerp(hub, _a.ScenePointOn(4, (_a.Inner + _a.Outer) * 0.5f), Ease(v, 0.6f, 1.3f));
                    _ca.SetPos(at);
                    _ca.SetAlpha(Mathf.Min(Ease(v, 0.45f, 0.65f), 1f - Ease(v, 2.0f, 2.3f)));
                    _ca.SetClick(Bump(v, 1.5f, 1.8f));
                    _a.Tint(4, P.Good, Bump(v, 1.55f, 1.95f) * 0.7f);
                }
            }

            /// <summary>Two Shift-clicks, two flashes — the wheel never closes.</summary>
            private void TickKeep(Wheel w, Cursor c, float u, float t0)
            {
                float v = u - t0;
                w.SetBloom(Mathf.Min(Ease(v, 0.05f, 0.4f), 1f - Ease(v, Mini ? 3.3f : 6.8f, Mini ? 3.6f : 7.2f)));
                Vector2 hub = w.Root.anchoredPosition;
                float midR = (w.Inner + w.Outer) * 0.5f;
                Vector2 at = Vector2.Lerp(hub, w.ScenePointOn(1, midR), Ease(v, 0.8f, 1.4f));
                at = Vector2.Lerp(at, w.ScenePointOn(2, midR), Ease(v, 2.0f, 2.6f));
                c.SetPos(at);
                c.SetAlpha(Mathf.Min(Ease(v, 0.6f, 0.8f), 1f - Ease(v, Mini ? 3.2f : 6.6f, Mini ? 3.5f : 6.9f)));
                float c1 = Bump(v, 1.55f, 1.8f), c2 = Bump(v, 2.75f, 3.0f);
                c.SetClick(Mathf.Max(c1, c2));
                int hot = v < 2.3f ? 1 : 2;
                w.Tint(hot, P.Good, Mathf.Max(Bump(v, 1.6f, 2.0f), Bump(v, 2.8f, 3.2f)) * 0.7f);
            }

            public override void Destroy() { _a = null; _b = null; _ca = null; _cb = null; }
        }

        // =====================================================================================
        //  holdflick (11.1, 11.2) — HOLD the belt key, point, let go: it's in your hand. Holding,
        //  rest on THE HUB for a moment to go inside, then let go on what you want.
        // =====================================================================================

        private sealed class HoldFlickScene : Scene
        {
            internal const float Loop = 10f;
            private Wheel _w;
            private Cursor _cur;
            private WordRing _words;
            private KeyVis _key;
            private DwellRing _dwell;
            private Flyer _fly;
            private HandPair _hands;
            private PanelGraphic _handItem;
            private Vector2 _handAt;
            private int _level = -1, _stateIdx = -1;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            private static readonly string[] Belt =
                { "THE HUB", "DRILL", "WRENCH", "CROWBAR", "CUTTERS", "WELDER", "GRINDER", "CABLE" };
            private static readonly string[] Hub =
                { "RECENT", "SEARCH", "HELMET", "GLASSES", "SUIT", "BACKPACK", "UNIFORM", "TOOLBELT" };

            protected override void Make(RectTransform root)
            {
                string key = K("{UIA_ToolbeltRadial}");
                float outer = L(72f, 28f), inner = L(33f, 12f);
                _w = Wheel.Make(root, "ring", outer, inner, 8, P, true, false, 7.5f, !Mini);
                _w.SetPos(L(-20f, 4f), L(-2f, 2f));
                _w.SetCount(8);
                if (Mini) _w.MakeIcons(4f, MulA(P.Accent, 0.7f));
                else
                {
                    var equip = ArcWord.Make(C, _w.Root, "EQUIP", outer, 8f, 11f, P.Accent, MulA(P.Panel, 0.92f), MulA(P.Border, 0.8f));
                    var open = ArcWord.Make(C, _w.Root, "OPEN", outer, 8f, 11f, P.Accent, MulA(P.Panel, 0.92f), MulA(P.Border, 0.8f));
                    var take = ArcWord.Make(C, _w.Root, "TAKE", outer, 8f, 11f, P.Accent, MulA(P.Panel, 0.92f), MulA(P.Border, 0.8f));
                    _words = new WordRing(_w, equip, open, take);
                }
                _w.SetBloom(0f);
                _key = KeyVis.Make(root, "key", key, P, L(30f, 18f), L(14f, 10f), L(56f, 30f));
                _key.SetPos(L(-164f, -56f), L(-18f, -4f));
                _dwell = DwellRing.Make(root, "dwell", L(7.5f, 4f), L(10.5f, 6f), P.Accent);
                _fly = Flyer.Make(root, "fly", L(16f, 8f), P);
                if (Mini)
                {
                    _handAt = new Vector2(56f, -18f);
                    At(Panel(root, "hand", 30f, 22f, MulA(P.Panel, 0.95f), P.Selected, 5f, 2f), _handAt);
                }
                else
                {
                    _hands = HandPair.Make(root, "hands", P, 58f, 40f, 10f, true, 6.5f);
                    _hands.Root.anchoredPosition = new Vector2(152f, -62f);
                    _handAt = _hands.SceneCenter(0);
                    _caps = new[]
                    {
                        "HOLD " + key + ", POINT, LET GO - IT'S IN YOUR HAND",
                        "HOLDING: REST ON THE HUB TO GO INSIDE, LET GO ON WHAT YOU WANT"
                    };
                    // Bottom, not top: the word over THE HUB (12 o'clock) reaches y ~99.
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, -100f, 430f);
                    Label(root, "let go in the middle to cancel", 8.5f, P.TextMute, 152f, -24f, 180f);
                }
                _handItem = Panel(root, "handItem", L(16f, 11f), L(16f, 11f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem, _handAt);
                _handItem.gameObject.SetActive(false);
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                int level = u >= 6.4f && u < 8.4f ? 1 : 0;
                if (level != _level)
                {
                    _level = level;
                    if (!Mini) { _w.SetLabels(level == 0 ? Belt : Hub); _w.Line0.text = level == 0 ? "TOOLBELT" : "THE HUB"; }
                    _w.SetBase(0, level == 1 ? Hot(_w.Fill, P.Accent, 0.38f) : _w.Fill);
                }
                bool down = (u >= 0.3f && u < 2.2f) || (u >= 4.8f && u < 8.4f);
                _key.SetPressed(Mathf.Max(Held(u, 0.3f, 2.2f), Held(u, 4.8f, 8.4f)));
                // Hold mode: the wheel is up only while the key is down, and vanishes on release.
                float open = Mathf.Max(Mathf.Min(Ease(u, 0.35f, 0.65f), 1f - Seg(u, 2.2f, 2.32f)),
                    Mathf.Min(Ease(u, 4.85f, 5.15f), 1f - Seg(u, 8.4f, 8.52f)));
                open *= 1f - 0.5f * Bump(u, 6.3f, 6.55f);
                _w.SetBloom(open);

                Vector2 hub = _w.Root.anchoredPosition;
                float midR = (_w.Inner + _w.Outer) * 0.5f;
                Vector2 at;
                if (u < 4.5f) at = Vector2.Lerp(hub, _w.ScenePointOn(1, midR), Ease(u, 0.8f, 1.6f));
                else
                {
                    at = Vector2.Lerp(hub, _w.ScenePointOn(0, midR + L(6f, 2f)), Ease(u, 5.3f, 5.9f));
                    at = Vector2.Lerp(at, _w.ScenePointOn(0, midR - L(4f, 2f)), Ease(u, 6.6f, 7.2f));
                }
                _cur.SetPos(at);
                _cur.SetAlpha(down ? (u < 4.5f ? Ease(u, 0.5f, 0.7f) : Ease(u, 5.0f, 5.2f)) : 0f);
                _cur.SetClick(0f);
                int hov = open > 0.6f ? _w.HitTest(at, _w.Inner) : -1;
                _w.Tint(hov, P.Selected, 0.5f);
                _w.SetBulge(hov, L(5f, 3f));
                int word = hov < 0 ? -1 : level == 1 ? (hov == 0 ? 2 : -1) : (hov == 0 ? 1 : 0);
                if (_words != null) _words.Update(t, hov, word);
                // The dive: resting on THE HUB fills the dwell ring, then the ring becomes the Hub.
                _dwell.Set(at, Seg(u, 5.95f, 6.35f), Mathf.Min(Seg(u, 5.95f, 6.0f), 1f - Seg(u, 6.35f, 6.5f)));

                bool fly1 = u >= 2.2f && u < 2.8f, fly2 = u >= 8.4f && u < 9.0f;
                if (fly1) _fly.Set(Arc(_w.ScenePointOn(1, midR), _handAt, Seg(u, 2.2f, 2.8f), L(30f, 12f)), 1f);
                else if (fly2) _fly.Set(Arc(_w.ScenePointOn(0, midR), _handAt, Seg(u, 8.4f, 9.0f), L(30f, 12f)), 1f);
                else _fly.Hide();
                bool inHand = (u >= 2.75f && u < 4.4f) || u >= 8.95f;
                if (_handItem.gameObject.activeSelf != inHand) _handItem.gameObject.SetActive(inHand);

                if (Mini) return;
                int st = u >= 2.75f && u < 4.4f ? 1 : u >= 8.95f ? 2 : 0;
                if (st != _stateIdx) { _stateIdx = st; _hands.State[0].text = st == 1 ? "Drill" : st == 2 ? "Cable Coil" : "empty"; }
                int cap = u < 4.5f ? 0 : 1;
                if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
            }

            public override void Destroy() { _w = null; _cur = null; _key = null; _words = null; _hands = null; }
        }

        // =====================================================================================
        //  hubdrag (11.3) — drag the middle of the wheel to move it; it re-centres the next time.
        // =====================================================================================

        private sealed class HubDragScene : Scene
        {
            internal const float Loop = 7.5f;
            private Wheel _w;
            private Cursor _cur;
            private Vector2 _home, _moved;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            protected override void Make(RectTransform root)
            {
                float outer = L(62f, 24f), inner = L(29f, 10f);
                _w = Wheel.Make(root, "ring", outer, inner, 8, P, true, false, 7f, false);
                _home = new Vector2(L(-80f, -36f), L(0f, 0f));
                _moved = new Vector2(L(70f, 34f), L(-8f, -2f));
                _w.SetPos(_home.x, _home.y);
                _w.SetCount(8);
                _w.MakeIcons(L(5f, 4f), MulA(P.Accent, 0.7f));
                _w.SetBloom(0f);
                if (!Mini)
                {
                    _caps = new[] { "WHEEL IN YOUR WAY? DRAG ITS MIDDLE", "IT RE-CENTERS THE NEXT TIME" };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                    Label(root, "more speed options: " + K("{UIA_Menu}") + " > RADIAL", 8.5f, P.TextMute, 0f, -96f, 300f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                float drag = Ease(u, 1.6f, 2.9f);
                bool second = u >= 5.4f;
                Vector2 pos = second ? _home : Vector2.Lerp(_home, _moved, drag);
                _w.SetPos(pos.x, pos.y);
                float open = second ? Mathf.Min(Ease(u, 5.6f, 6.0f), 1f - Ease(u, 7.0f, 7.4f))
                    : Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 4.6f, 5.0f));
                _w.SetBloom(open);
                float held = Held(u, 1.35f, 3.05f);
                _w.Hub.BorderColor = Color.Lerp(_w.Border, P.Selected, held);
                Vector2 at = Vector2.Lerp(pos + new Vector2(L(40f, 14f), -L(40f, 14f)), pos + new Vector2(L(3f, 1f), -L(3f, 1f)), Ease(u, 0.6f, 1.2f));
                if (u >= 1.2f && u < 3.2f) at = pos + new Vector2(L(3f, 1f), -L(3f, 1f));
                if (u >= 3.2f) at = Vector2.Lerp(_moved + new Vector2(L(3f, 1f), -L(3f, 1f)), _moved + new Vector2(L(40f, 14f), -L(30f, 12f)), Ease(u, 3.2f, 3.8f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.45f, 0.65f), 1f - Ease(u, 3.9f, 4.2f)));
                _cur.SetClick(held);
                if (_cap != null)
                {
                    int cap = u < 5.2f ? 0 : 1;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _w = null; _cur = null; }
        }

        // =====================================================================================
        //  bagnames (13.1, 13.2) — two identical BACKPACK wedges; named and painted, one becomes
        //  TOOLS in blue and the other ORES in orange.
        // =====================================================================================

        private sealed class BagNamesScene : Scene
        {
            internal const float Loop = 8f;
            private const int A = 1, B = 5;
            private Wheel _w;
            private Color _blue, _orange, _base;
            private int _named = -1;
            private TextMeshProUGUI _q;

            private static readonly string[] Same = { "SEARCH", "BACKPACK", "HELMET", "SUIT", "UNIFORM", "BACKPACK" };
            private static readonly string[] Named = { "SEARCH", "TOOLS", "HELMET", "SUIT", "UNIFORM", "ORES" };

            protected override void Make(RectTransform root)
            {
                // Rule-8 content exemption (deliberate): blue and orange are the two PAINT colours the
                // player picks for the bags in this story (TOOLS / ORES) - demo content, not theme
                // colours, so they stay the same under every UI Theme and need no per-tier fork.
                _blue = new Color(0.28f, 0.56f, 0.96f, 1f);
                _orange = new Color(1f, 0.56f, 0.18f, 1f);
                float outer = L(80f, 32f), inner = L(36f, 13f);
                _w = Wheel.Make(root, "ring", outer, inner, 6, P, true, false, 8f, !Mini);
                _w.SetPos(L(-56f, 0f), L(-4f, 0f));
                _w.SetCount(6);
                _w.MakeIcons(L(10f, 5f), MulA(P.Accent, 0.75f));
                if (!Mini)
                {
                    _w.Line0.text = "THE HUB";
                    Label(root, "SAME NAME, SAME LOOK?", 10f, P.TextDim, 130f, 44f, 190f);
                    Label(root, "NAME THEM AND PAINT THEM", 10f, P.TextDim, 130f, 26f, 190f);
                    Label(root, "or know them by place:", 8.5f, P.TextMute, 130f, -20f, 190f);
                    Label(root, "the big window keeps every bag put", 8.5f, P.TextMute, 130f, -34f, 190f);
                    _q = Label(_w.Root, "?", 16f, P.Warn, 0f, -outer - 12f, 30f);
                }
                _base = _w.Fill;
                _w.SetBloom(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                _w.SetBloom(Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 7.5f, 7.9f)));
                float paint = Mathf.Clamp01(Ease(u, 3.4f, 4.2f));
                int named = u >= 3.8f ? 1 : 0;
                if (named != _named)
                {
                    _named = named;
                    if (!Mini) _w.SetLabels(named == 0 ? Same : Named);
                }
                _w.SetBase(A, Hot(_base, _blue, 0.45f * paint));
                _w.SetBase(B, Hot(_base, _orange, 0.45f * paint));
                // Before: the eye flicks between two identical wedges.
                float flick = u < 3.4f ? 0.5f + 0.5f * Mathf.Sin(u * 4f) : 0f;
                int hot = u < 3.4f ? (flick > 0.5f ? A : B) : -1;
                _w.Tint(hot, P.Selected, 0.35f);
                _w.Icons[A].color = Color.Lerp(MulA(P.Accent, 0.75f), _blue, paint);
                _w.Icons[B].color = Color.Lerp(MulA(P.Accent, 0.75f), _orange, paint);
                if (_q != null) _q.color = WithA(P.Warn, u < 3.4f ? 0.6f + 0.4f * flick : 1f - Ease(u, 3.4f, 3.7f));
            }

            public override void Destroy() { _w = null; }
        }
    }
}
