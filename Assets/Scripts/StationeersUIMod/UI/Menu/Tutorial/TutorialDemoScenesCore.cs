using StationeersUIMod.UI.Hud;
using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    // The keys micro-scene and the First Steps (lesson 1) scenes. Every scene has a FULL layout
    // (440x220, the coach card / Watch mode) and a MINI layout (150x76, the strip slot: big shapes,
    // no words except keycaps). See TutorialDemoStage.cs for the stage, toolkit and contracts.
    internal sealed partial class TutorialDemoStage
    {
        // =====================================================================================
        //  keys:<pattern>[:<token>] — one keycap or mouse playing a press pattern with the LIVE
        //  glyph. Key patterns (tap, hold, double, chord) need a token; the mouse patterns (lmb,
        //  rmb, mmb, scroll, drag, shiftdrag) take an optional one = a key held during the gesture.
        //  Chord tokens are "Mod+Key" (Shift/Ctrl/Alt or a token, e.g. "Shift+{V:BackSlot}",
        //  "Ctrl+7"); a chord token without '+' means Shift + that key.
        // =====================================================================================

        private sealed class KeysScene : Scene
        {
            private const int PTap = 0, PHold = 1, PDouble = 2, PChord = 3, PLmb = 4, PRmb = 5, PMmb = 6,
                PScroll = 7, PDrag = 8, PShiftDrag = 9;

            internal static int PatternOf(string pattern)
            {
                switch (pattern)
                {
                    case "tap": return PTap;
                    case "hold": return PHold;
                    case "double":
                    case "doubletap": return PDouble;
                    case "chord": return PChord;
                    case "lmb": return PLmb;
                    case "rmb": return PRmb;
                    case "mmb": return PMmb;
                    case "scroll": return PScroll;
                    case "drag": return PDrag;
                    case "shiftdrag": return PShiftDrag;
                    default: return -1;
                }
            }

            /// <summary>Known pattern, and a token where the pattern needs one.</summary>
            internal static bool Known(string pattern, string token)
            {
                int p = PatternOf(pattern);
                if (p < 0) return false;
                return p > PChord || !string.IsNullOrEmpty(token);
            }

            private int _pat;
            private float _loop = 3f;
            private KeyVis _key, _mod;
            private Mouse _mouse;
            private int _button;
            private Ripple _rip, _rip2;
            private PanelGraphic _meterFill;
            private float _meterW, _meterH, _meterX0;
            private CircleGraphic[] _dots;
            private Color _dotOff, _dotOn;
            // drag
            private Cursor _cur;
            private CanvasGroup[] _chips;
            private PanelGraphic[] _dest;
            private Vector2[] _from, _to;
            private Color _cellBorder;

            protected override void Make(RectTransform root)
            {
                _pat = PatternOf(C.Pattern);
                float capH = L(58f, 30f), font = L(26f, 14f), mouseH = L(100f, 50f);
                float y = L(10f, 3f);
                string token = C.Token;
                string caption = null;
                switch (_pat)
                {
                    case PTap:
                    case PHold:
                    case PDouble:
                    {
                        string k = K(token);
                        _key = KeyVis.Make(root, "key", k, P, capH, font, mouseH);
                        _key.SetPos(0f, y);
                        caption = (_pat == PTap ? "TAP " : _pat == PHold ? "HOLD " : "DOUBLE-TAP ") + k;
                        _loop = _pat == PTap ? 2.4f : _pat == PHold ? 4f : 2.8f;
                        break;
                    }
                    case PChord:
                    {
                        string modTok, keyTok;
                        SplitChord(token, out modTok, out keyTok);
                        string m = K(modTok), k = K(keyTok);
                        _mod = KeyVis.Make(root, "mod", m, P, capH, font, mouseH);
                        _key = KeyVis.Make(root, "key", k, P, capH, font, mouseH);
                        float gap = L(38f, 18f);
                        float total = _mod.W + gap + _key.W;
                        _mod.SetPos(-total * 0.5f + _mod.W * 0.5f, y);
                        _key.SetPos(total * 0.5f - _key.W * 0.5f, y);
                        PlusGlyph(root, "plus", L(16f, 9f), L(3f, 2f), P.TextDim).anchoredPosition =
                            new Vector2(-total * 0.5f + _mod.W + gap * 0.5f, y);
                        caption = m + " + " + k;
                        _loop = 3.4f;
                        break;
                    }
                    case PLmb:
                    case PRmb:
                    case PMmb:
                    case PScroll:
                    {
                        _button = _pat == PLmb ? 0 : _pat == PRmb ? 1 : 2;
                        _mouse = Mouse.Make(root, "mouse", P, mouseH);
                        string m = token != null ? K(token) : null;
                        if (m != null)
                        {
                            _mod = KeyVis.Make(root, "mod", m, P, capH * 0.8f, font * 0.8f, mouseH * 0.7f);
                            float gap = L(34f, 14f);
                            float total = _mod.W + gap + _mouse.W;
                            _mod.SetPos(-total * 0.5f + _mod.W * 0.5f, y);
                            _mouse.SetPos(total * 0.5f - _mouse.W * 0.5f, y);
                            PlusGlyph(root, "plus", L(14f, 8f), L(3f, 2f), P.TextDim).anchoredPosition =
                                new Vector2(-total * 0.5f + _mod.W + gap * 0.5f, y);
                        }
                        else _mouse.SetPos(0f, y);
                        string what = _pat == PLmb ? "LEFT-CLICK" : _pat == PRmb ? "RIGHT-CLICK"
                            : _pat == PMmb ? "MIDDLE-CLICK" : "SCROLL WHEEL";
                        caption = m != null ? "HOLD " + m + " + " + what : what;
                        _loop = _pat == PScroll ? 3.2f : 2.4f;
                        break;
                    }
                    default:
                        BuildDrag(root, token, out caption);
                        _loop = _pat == PDrag ? 3.8f : 4.2f;
                        break;
                }

                // Press feedback: one ripple (two for a double tap) where the press lands.
                if (_pat != PScroll && _pat != PDrag && _pat != PShiftDrag)
                {
                    bool onMouse = _mouse != null || (_key != null && _key.Mouse != null);
                    float r0 = onMouse ? L(6f, 4f) : L(capH * 0.55f, 13f);
                    float r1 = onMouse ? L(30f, 16f) : L(capH * 1.05f, 25f);
                    Vector2 at = _mouse != null ? _mouse.ButtonPos(_button) : _key.PressPoint;
                    _rip = Ripple.Make(root, "rip", P.Accent, r0, r1, L(2f, 1.4f));
                    _rip.SetPos(at);
                    if (_pat == PDouble)
                    {
                        _rip2 = Ripple.Make(root, "rip2", P.Accent, r0, r1, L(2f, 1.4f));
                        _rip2.SetPos(at);
                    }
                }

                // Hold: a meter that fills while the key is down. Double: two dots, one per press.
                float below = (_key != null ? _key.Position.y - _key.H * 0.5f : y) - L(16f, 7f);
                if (_pat == PHold)
                {
                    _meterW = L(140f, 52f);
                    _meterH = L(6f, 4f);
                    _meterX0 = -_meterW * 0.5f;
                    At(Panel(root, "track", _meterW, _meterH, MulA(P.Track, 0.9f), Color.clear, _meterH * 0.5f, 0f), 0f, below);
                    _meterFill = Panel(root, "fill", _meterH, _meterH, P.Accent, Color.clear, _meterH * 0.5f, 0f);
                    At(_meterFill, _meterX0 + _meterH * 0.5f, below);
                }
                else if (_pat == PDouble)
                {
                    _dotOff = MulA(P.TextMute, 0.5f);
                    _dotOn = P.Accent;
                    _dots = new CircleGraphic[2];
                    for (int i = 0; i < 2; i++)
                    {
                        _dots[i] = Circle(root, "dot" + i, L(4f, 2.6f), _dotOff, Color.clear, 0f);
                        At(_dots[i], (i == 0 ? -1f : 1f) * L(11f, 6.5f), below);
                    }
                }

                if (!Mini && caption != null) Label(root, caption, 12f, P.TextDim, 0f, -90f, 420f);
            }

            private void BuildDrag(RectTransform root, string token, out string caption)
            {
                bool shift = _pat == PShiftDrag;
                // Shift-drag's modifier is vanilla's rebindable MoveAllOfType (default LeftShift).
                string m = token != null ? K(token) : shift ? K("{V:MoveAllOfType}") : null;
                int n = shift ? 3 : 1;
                _mouse = Mouse.Make(root, "mouse", P, L(64f, 34f));
                _button = 0;
                _mouse.SetPos(L(-162f, -58f), L(24f, 12f));
                if (m != null)
                {
                    _mod = KeyVis.Make(root, "mod", m, P, L(30f, 16f), L(13f, 8.5f), L(40f, 22f));
                    _mod.SetPos(L(-162f, -58f), L(-44f, -24f));
                }
                float cell = L(40f, 18f), gap = L(8f, 3f);
                float sx = L(-40f, -12f), tx = L(120f, 50f);
                float y0 = L(10f, 3f);
                _cellBorder = MulA(P.Border, 0.7f);
                _from = new Vector2[n];
                _to = new Vector2[n];
                _chips = new CanvasGroup[n];
                _dest = new PanelGraphic[n];
                for (int i = 0; i < n; i++)
                {
                    float yy = y0 + (n == 1 ? 0f : (1 - i) * (cell + gap));
                    _from[i] = new Vector2(sx, yy);
                    _to[i] = new Vector2(tx, yy);
                    At(Panel(root, "src" + i, cell, cell, MulA(P.Raised, 0.85f), _cellBorder, cell * 0.12f, 1.1f), _from[i]);
                    _dest[i] = Panel(root, "dst" + i, cell, cell, MulA(P.Raised, 0.85f), _cellBorder, cell * 0.12f, 1.1f);
                    At(_dest[i], _to[i]);
                }
                for (int i = 0; i < n; i++)
                {
                    _chips[i] = GroupNode(root, "chip" + i);
                    Panel((RectTransform)_chips[i].transform, "c", cell * 0.62f, cell * 0.62f,
                        MulA(P.Accent, 0.75f), P.Accent, cell * 0.12f, 1.2f);
                    At(_chips[i], _from[i]);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 10f));
                _cur.SetAlpha(0f);
                caption = m != null ? m + " + DRAG" : "DRAG";
            }

            /// <summary>"Mod+Key" -> (Mod, Key); a '+' inside braces never splits. No '+' = Shift.</summary>
            private static void SplitChord(string token, out string mod, out string key)
            {
                mod = "Shift";
                key = token ?? "?";
                if (string.IsNullOrEmpty(token)) return;
                int depth = 0;
                for (int i = 0; i < token.Length; i++)
                {
                    char ch = token[i];
                    if (ch == '{') depth++;
                    else if (ch == '}') depth--;
                    else if (ch == '+' && depth == 0 && i > 0 && i < token.Length - 1)
                    {
                        mod = token.Substring(0, i).Trim();
                        key = token.Substring(i + 1).Trim();
                        return;
                    }
                }
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, _loop);
                switch (_pat)
                {
                    case PTap:
                        _key.SetPressed(Bump(u, 0.55f, 0.85f));
                        _rip.Play(Seg(u, 0.68f, 1.3f));
                        break;
                    case PHold:
                    {
                        float h = Held(u, 0.5f, 3.0f, 0.12f);
                        _key.SetPressed(h);
                        float fill = h > 0.001f ? Seg(u, 0.62f, 2.88f) : 0f;
                        float w = Mathf.Max(_meterH, _meterW * fill);
                        _meterFill.SetShape(w, _meterH, _meterH * 0.5f);
                        ((RectTransform)_meterFill.transform).sizeDelta = new Vector2(w, _meterH);
                        At(_meterFill, _meterX0 + w * 0.5f, Pos(_meterFill).y);
                        _meterFill.color = WithA(P.Accent, h > 0.001f ? 1f : 0.25f);
                        _rip.Play(Seg(u, 0.62f, 1.25f));
                        break;
                    }
                    case PDouble:
                        _key.SetPressed(Mathf.Max(Bump(u, 0.5f, 0.72f), Bump(u, 0.86f, 1.08f)));
                        _rip.Play(Seg(u, 0.58f, 1.15f));
                        _rip2.Play(Seg(u, 0.94f, 1.5f));
                        _dots[0].color = Color.Lerp(_dotOff, _dotOn, Window01(u, 0.55f, 2.3f, 0.12f));
                        _dots[1].color = Color.Lerp(_dotOff, _dotOn, Window01(u, 0.92f, 2.3f, 0.12f));
                        break;
                    case PChord:
                        _mod.SetPressed(Held(u, 0.45f, 2.6f));
                        _key.SetPressed(Bump(u, 1.2f, 1.5f));
                        _rip.Play(Seg(u, 1.32f, 1.9f));
                        break;
                    case PLmb:
                    case PRmb:
                    case PMmb:
                        if (_mod != null) _mod.SetPressed(Held(u, 0.3f, 1.7f));
                        _mouse.SetButton(_button, Bump(u, 0.7f, 1.0f));
                        _rip.Play(Seg(u, 0.82f, 1.45f));
                        break;
                    case PScroll:
                    {
                        if (_mod != null) _mod.SetPressed(Held(u, 0.15f, 3.0f));
                        float phase = Seg(u, 0.4f, 1.4f) * 3f - Seg(u, 1.8f, 2.8f) * 3f;
                        float dir = u < 1.6f ? Window01(u, 0.35f, 1.45f, 0.1f) : -Window01(u, 1.75f, 2.85f, 0.1f);
                        _mouse.Scroll(phase, dir);
                        break;
                    }
                    default:
                        TickDrag(u);
                        break;
                }
            }

            private void TickDrag(float u)
            {
                float held = Held(u, 0.55f, 2.4f);
                _mouse.SetButton(0, held);
                if (_mod != null) _mod.SetPressed(Held(u, 0.25f, 2.7f));
                float alpha = Mathf.Min(Ease(u, 0f, 0.3f), 1f - Ease(u, _loop - 0.5f, _loop - 0.2f));
                Vector2 lead = _from[0];
                for (int i = 0; i < _chips.Length; i++)
                {
                    float lag = i * 0.09f;
                    Vector2 at = Arc(_from[i], _to[i], Seg(u, 0.75f + lag, 2.15f + lag), L(18f, 7f));
                    At(_chips[i], at);
                    _chips[i].alpha = alpha;
                    if (i == 0) lead = at;
                    _dest[i].BorderColor = Color.Lerp(_cellBorder, P.Accent, Bump(u, 2.1f + lag, 2.7f + lag));
                }
                float off = L(10f, 6f);
                Vector2 grab = _from[0] + new Vector2(-off, off);
                Vector2 cur = Vector2.Lerp(grab + new Vector2(-L(30f, 14f), -L(24f, 10f)), grab, Ease(u, 0.15f, 0.5f));
                if (u >= 0.5f) cur = lead + new Vector2(-off, off);
                cur = Vector2.Lerp(cur, cur + new Vector2(L(24f, 10f), -L(22f, 9f)), Ease(u, 2.45f, 2.9f));
                _cur.SetPos(cur);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.1f, 0.35f), 1f - Ease(u, 2.9f, 3.2f)));
                _cur.SetClick(held);
            }

            public override void Destroy() { _key = null; _mod = null; _mouse = null; _cur = null; _chips = null; }
        }

        // =====================================================================================
        //  handswap (1.1) — two hand boxes; the swap key presses; the active border and its side
        //  bar hop across.
        // =====================================================================================

        private sealed class HandSwapScene : Scene
        {
            internal const float Loop = 4.6f;
            private HandPair _hands;
            private KeyVis _key;
            private Ripple _rip;

            protected override void Make(RectTransform root)
            {
                string e = K("{V:SwapHands}");
                if (Mini)
                {
                    _hands = HandPair.Make(root, "hands", P, 54f, 32f, 10f, false);
                    _hands.Root.anchoredPosition = new Vector2(0f, 10f);
                    _key = KeyVis.Make(root, "key", e, P, 20f, 11f, 30f);
                    _key.SetPos(0f, -24f);
                }
                else
                {
                    _hands = HandPair.Make(root, "hands", P, 124f, 76f, 28f, true, 8f);
                    _hands.Root.anchoredPosition = new Vector2(0f, 4f);
                    _hands.State[0].text = "Duct Tape";
                    _key = KeyVis.Make(root, "key", e, P, 30f, 14f, 44f);
                    _key.SetPos(0f, -78f);
                    Label(root, e + " SWITCHES HANDS - THE LIT ONE IS ACTIVE", 10f, P.TextDim, 0f, 94f, 430f);
                }
                _hands.Item[0].gameObject.SetActive(true);   // the left hand holds something
                _rip = Ripple.Make(root, "rip", P.Accent, L(18f, 11f), L(36f, 20f), L(2f, 1.4f));
                _rip.SetPos(_key.PressPoint);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                _hands.SetActive(Ease(u, 1.05f, 1.45f) - Ease(u, 3.35f, 3.75f));
                _key.SetPressed(Mathf.Max(Bump(u, 0.8f, 1.1f), Bump(u, 3.1f, 3.4f)));
                float r1 = Seg(u, 0.92f, 1.5f), r2 = Seg(u, 3.22f, 3.8f);
                _rip.Play(r1 > 0f && r1 < 1f ? r1 : r2);
            }

            public override void Destroy() { _hands = null; _key = null; }
        }

        // =====================================================================================
        //  actionword (1.3, 18.2) — a bag level: the cursor glides from the TABLET (a tool) over the
        //  top wedge to the MINING BELT (a bag); the curved word rides the hovered wedge and flips
        //  TAKE <-> OPEN; the doubled chevron on the bag pulses. (A bag level is where exactly those
        //  two words appear; on the BELT ring a tool reads EQUIP — see takeandstow.)
        // =====================================================================================

        private sealed class ActionWordScene : Scene
        {
            internal const float Loop = 6.6f;
            private const int Top = 0, Belt = 1, Tablet = 5;
            private Wheel _w;
            private Cursor _cur;
            private WordRing _words;
            private TriangleGraphic _chev;
            private int _line = -2;
            private PanelGraphic _legTake, _legOpen;

            private static readonly string[] Labels =
                { "CARTRIDGE", "MINING BELT", "FLARES", "CARTRIDGE", "CARTRIDGE", "TABLET" };
            private static readonly string[] Names =
                { "Cartridge", "Mining Belt", "Road Flares", "Cartridge", "Cartridge", "Tablet" };

            protected override void Make(RectTransform root)
            {
                float outer = L(72f, 24f), inner = L(34f, 10f);
                _w = Wheel.Make(root, "bag", outer, inner, 6, P, true, false, 7.5f, !Mini);
                _w.SetPos(L(-66f, -22f), L(-22f, -11f));
                _w.SetCount(6);
                if (Mini) _w.MakeIcons(4.5f, MulA(P.Accent, 0.75f));
                else
                {
                    _w.SetLabels(Labels);
                    _w.Line0.text = "JETPACK";
                    _w.Line1.text = "click a wedge";
                }
                _chev = _w.Chevron(Belt, L(12f, 7f), P.Text);
                float fs = L(12f, 9.5f);
                Color plate = MulA(P.Panel, 0.92f), rim = MulA(P.Border, 0.8f);
                var take = ArcWord.Make(C, _w.Root, "TAKE", outer, L(8f, 5f), fs, P.Accent, plate, rim);
                var open = ArcWord.Make(C, _w.Root, "OPEN", outer, L(8f, 5f), fs, P.Accent, plate, rim);
                _words = new WordRing(_w, take, open);
                _w.SetBloom(0f);

                if (!Mini)
                {
                    _legTake = Panel(root, "legTake", 150f, 44f, MulA(P.Panel, 0.95f), P.Border, 8f, 1.4f);
                    At(_legTake, 138f, 26f);
                    Label(root, "TAKE", 13f, P.Accent, 138f, 34f, 140f).fontStyle = FontStyles.Bold;
                    Label(root, "puts it in your hand", 8.5f, P.TextDim, 138f, 16f, 140f);
                    _legOpen = Panel(root, "legOpen", 150f, 44f, MulA(P.Panel, 0.95f), P.Border, 8f, 1.4f);
                    At(_legOpen, 138f, -30f);
                    Label(root, "OPEN", 13f, P.Accent, 138f, -22f, 140f).fontStyle = FontStyles.Bold;
                    Label(root, "looks inside", 8.5f, P.TextDim, 138f, -40f, 140f);
                    Label(root, "THE WORD SAYS WHAT A CLICK WILL DO", 9f, P.TextMute, 138f, -84f, 180f);
                }

                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                float open = Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 6.0f, 6.4f));
                _w.SetBloom(open);
                Vector2 hub = _w.Root.anchoredPosition;
                float midR = (_w.Inner + _w.Outer) * 0.5f;
                Vector2 at = Vector2.Lerp(hub, _w.ScenePointOn(Tablet, midR), Ease(u, 0.6f, 1.3f));
                at = Vector2.Lerp(at, _w.ScenePointOn(Top, midR), Ease(u, 2.7f, 3.2f));
                at = Vector2.Lerp(at, _w.ScenePointOn(Belt, midR), Ease(u, 3.2f, 3.7f));
                at = Vector2.Lerp(at, hub, Ease(u, 5.2f, 5.8f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.45f, 0.65f), 1f - Ease(u, 5.6f, 5.9f)));

                int hov = open > 0.7f ? _w.HitTest(at, _w.Inner) : -1;
                _w.Tint(hov, P.Selected, 0.5f);
                _w.SetBulge(hov, L(5f, 3f));
                _words.Update(t, hov, hov < 0 ? -1 : hov == Belt ? 1 : 0);

                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5.5f);
                _chev.color = WithA(P.Text, hov == Belt ? 0.95f : 0.45f + 0.4f * pulse);
                Scale(_chev, 1f + 0.14f * pulse);

                if (Mini) return;
                if (hov != _line)
                {
                    _line = hov;
                    _w.Line1.text = hov < 0 ? "click a wedge" : Names[hov];
                }
                float take = _words.Mix(0), op = _words.Mix(1);
                _legTake.BorderColor = Color.Lerp(P.Border, P.Accent, take);
                _legTake.color = Hot(MulA(P.Panel, 0.95f), P.Accent, take * 0.25f);
                _legOpen.BorderColor = Color.Lerp(P.Border, P.Accent, op);
                _legOpen.color = Hot(MulA(P.Panel, 0.95f), P.Accent, op * 0.25f);
            }

            public override void Destroy() { _w = null; _cur = null; _words = null; }
        }

        // =====================================================================================
        //  takeandstow (1.4 first half, 1.5 second half) — click a belt tool (the word reads EQUIP,
        //  as the live belt ring does with an empty hand); it arcs into the lit hand; the stow key
        //  sends it home to the BELT box, which flashes.
        // =====================================================================================

        private sealed class TakeAndStowScene : Scene
        {
            internal const float Loop = 8f;
            private const int Drill = 1;
            private Wheel _w;
            private Cursor _cur;
            private WordRing _words;
            private HandPair _hands;
            private PanelGraphic _handItem;
            private PanelGraphic _beltBox, _beltIcon;
            private Color _beltFill;
            private CanvasGroup _fly;
            private KeyVis _g;
            private Ripple _rip;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private int _stateIdx = -1;
            private string[] _caps;
            private Vector2 _handAt, _beltAt;

            private static readonly string[] Belt =
                { "THE HUB", "DRILL", "WRENCH", "CROWBAR", "CUTTERS", "WELDER", "GRINDER", "CABLE" };

            protected override void Make(RectTransform root)
            {
                string g = K("{V:SmartStow}");
                float outer = L(64f, 26f), inner = L(29f, 11f);
                _w = Wheel.Make(root, "belt", outer, inner, 8, P, true, false, 7.5f, !Mini);
                _w.SetPos(L(-116f, -46f), L(10f, 2f));
                _w.SetCount(8);
                if (Mini)
                {
                    _w.MakeIcons(4f, MulA(P.Accent, 0.75f));
                    _w.ShowIcon(0, false);
                }
                else
                {
                    _w.SetLabels(Belt);
                    _w.Line0.text = "TOOLBELT";
                    _w.Line1.text = "click a tool";
                    var equip = ArcWord.Make(C, _w.Root, "EQUIP", outer, 8f, 11f, P.Accent,
                        MulA(P.Panel, 0.92f), MulA(P.Border, 0.8f));
                    _words = new WordRing(_w, equip);
                }
                _w.SetBloom(0f);

                _beltFill = MulA(P.Panel, 0.95f);
                if (Mini)
                {
                    var hand = Panel(root, "hand", 34f, 26f, MulA(P.Panel, 0.95f), P.Selected, 5f, 2f);
                    _handAt = new Vector2(12f, 14f);
                    At(hand, _handAt);
                    _beltAt = new Vector2(54f, 14f);
                    _beltBox = Panel(root, "belt", 32f, 26f, _beltFill, P.Border, 5f, 1.4f);
                    At(_beltBox, _beltAt);
                    _beltIcon = Panel(root, "beltIcon", 20f, 6f, MulA(P.TextDim, 0.9f), Color.clear, 3f, 0f);
                    At(_beltIcon, _beltAt);
                    _g = KeyVis.Make(root, "g", g, P, 18f, 10f, 28f);
                    _g.SetPos(33f, -21f);
                }
                else
                {
                    _hands = HandPair.Make(root, "hands", P, 64f, 44f, 12f, true, 6.5f);
                    _hands.Root.anchoredPosition = new Vector2(40f, 44f);
                    _handAt = _hands.SceneCenter(0);
                    string[] eq = { "SUIT", "BACK", "BELT" };
                    for (int i = 0; i < 3; i++)
                    {
                        float y = 44f - i * 36f;
                        var box = Panel(root, "eq" + i, 78f, 30f, _beltFill, P.Border, 6f, 1.4f);
                        At(box, 176f, y);
                        Label(root, eq[i], 7f, P.TextDim, 176f, y + 8f, 74f);
                        if (i == 2) { _beltBox = box; _beltAt = new Vector2(176f, y - 3f); }
                    }
                    _beltIcon = Panel(root, "beltIcon", 34f, 7f, MulA(P.TextDim, 0.9f), Color.clear, 3.5f, 0f);
                    At(_beltIcon, _beltAt);
                    _g = KeyVis.Make(root, "g", g, P, 28f, 13f, 40f);
                    _g.SetPos(40f, -30f);
                    _caps = new[]
                    {
                        "CLICK A TOOL - IT GOES INTO YOUR HAND",
                        g + " SENDS IT HOME - WATCH THE BELT BOX FLASH"
                    };
                    _cap = Label(root, _caps[0], 9.5f, P.TextDim, 0f, -88f, 430f);
                }
                _handItem = Panel(root, "handItem", L(18f, 12f), L(18f, 12f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem, _handAt);
                _handItem.gameObject.SetActive(false);
                _fly = GroupNode(root, "fly");
                Panel((RectTransform)_fly.transform, "f", L(18f, 12f), L(18f, 12f), MulA(P.Accent, 0.85f), P.Accent, 3f, 1.2f);
                _fly.alpha = 0f;
                _rip = Ripple.Make(root, "rip", P.Accent, L(16f, 10f), L(30f, 18f), L(2f, 1.4f));
                _rip.SetPos(_g.PressPoint);
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                float open = Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 1.95f, 2.25f));
                _w.SetBloom(open);
                float midR = (_w.Inner + _w.Outer) * 0.5f;
                Vector2 hub = _w.Root.anchoredPosition;
                Vector2 tool = _w.ScenePointOn(Drill, midR);
                Vector2 at = Vector2.Lerp(hub, tool, Ease(u, 0.6f, 1.35f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.45f, 0.65f), 1f - Ease(u, 1.9f, 2.1f)));
                float click = Bump(u, 1.55f, 1.85f);
                _cur.SetClick(click);

                int hov = open > 0.7f ? _w.HitTest(at, _w.Inner) : -1;
                _w.Tint(hov, P.Selected, 0.5f + 0.5f * Bump(u, 1.55f, 2.0f));
                _w.SetBulge(hov, L(5f, 3f));
                if (_words != null) _words.Update(t, hov, hov == Drill ? 0 : -1);
                if (Mini) _w.ShowIcon(Drill, u < 1.9f || u > 5.2f);

                // Take: wedge -> hand. Stow: hand -> the belt box it came from.
                bool fly1 = u >= 1.9f && u < 2.5f;
                bool fly2 = u >= 4.45f && u < 5.05f;
                _fly.alpha = fly1 || fly2 ? 1f : 0f;
                if (fly1) At(_fly, Arc(tool, _handAt, Seg(u, 1.9f, 2.5f), L(34f, 12f)));
                else if (fly2) At(_fly, Arc(_handAt, _beltAt, Seg(u, 4.45f, 5.05f), L(40f, 14f)));
                bool inHand = u >= 2.45f && u < 4.5f;
                if (_handItem.gameObject.activeSelf != inHand) _handItem.gameObject.SetActive(inHand);
                if (_hands != null)
                {
                    _hands.Flash(0, Bump(u, 2.4f, 2.9f));
                    int st = inHand ? 1 : 0;
                    if (st != _stateIdx) { _stateIdx = st; _hands.State[0].text = st == 1 ? "Drill" : "empty"; }
                }

                _g.SetPressed(Bump(u, 4.1f, 4.4f));
                _rip.Play(Seg(u, 4.2f, 4.8f));
                float k = Mathf.Min(Seg(u, 5.0f, 5.08f), 1f - Seg(u, 5.3f, 5.7f));
                _beltBox.BorderColor = Color.Lerp(P.Border, P.Accent, k);
                _beltBox.BorderWidth = Mathf.Lerp(1.4f, 2.4f, k);
                _beltBox.color = Hot(_beltFill, P.Accent, k * 0.3f);
                Scale(_beltIcon, 1f + 0.22f * k);

                if (_cap != null)
                {
                    int cap = u < 3.6f ? 0 : 1;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _w = null; _cur = null; _hands = null; _g = null; }
        }

        // =====================================================================================
        //  hubback (1.6) — click THE HUB (the word reads OPEN): the ring relabels; a right-button
        //  badge lights: one step back; the Close band closes it.
        // =====================================================================================

        private sealed class HubBackScene : Scene
        {
            internal const float Loop = 8.4f;
            private Wheel _w;
            private Cursor _cur;
            private WordRing _words;
            private Mouse _rmb;
            private Ripple _rip;
            private int _level = -1;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;
            private Color _hubTint, _recentTint;

            private static readonly string[] Belt =
                { "THE HUB", "DRILL", "WRENCH", "CROWBAR", "CUTTERS", "WELDER", "GRINDER", "CABLE" };
            private static readonly string[] Hub =
                { "RECENT", "SEARCH", "HELMET", "GLASSES", "SUIT", "BACKPACK", "UNIFORM", "TOOLBELT" };
            private static readonly string[] Title = { "TOOLBELT", "THE HUB" };

            protected override void Make(RectTransform root)
            {
                string mmb = K("{UIA_ToolbeltRadial}");
                float outer = L(76f, 30f), inner = L(35f, 13f);
                _w = Wheel.Make(root, "ring", outer, inner, 8, P, true, true, 7.5f, !Mini);
                _w.SetPos(L(-70f, -28f), L(0f, 0f));
                _w.SetCount(8);
                if (Mini) _w.MakeIcons(4f, MulA(P.Accent, 0.7f));
                else
                {
                    _w.Line1.text = "click a wedge";
                    var open = ArcWord.Make(C, _w.Root, "OPEN", outer, 8f, 11f, P.Accent,
                        MulA(P.Panel, 0.92f), MulA(P.Border, 0.8f));
                    _words = new WordRing(_w, open);
                }
                _hubTint = Hot(_w.Fill, P.Accent, 0.16f);
                _recentTint = Hot(_w.Fill, P.Accent, 0.42f);
                _w.SetBloom(0f);

                _rmb = Mouse.Make(root, "rmb", P, L(48f, 32f));
                _rmb.SetPos(L(126f, 48f), L(34f, 4f));
                _rip = Ripple.Make(root, "rip", P.Accent, L(5f, 3f), L(26f, 15f), L(2f, 1.4f));
                _rip.SetPos(_rmb.ButtonPos(1));
                if (!Mini)
                {
                    Label(root, "RIGHT-CLICK", 9f, P.TextDim, 126f, -4f, 120f);
                    _caps = new[]
                    {
                        "CLICK THE HUB - IT OPENS INTO EVERYTHING YOU CARRY",
                        "RIGHT-CLICK - ONE STEP BACK",
                        "RIGHT-CLICK, Esc, " + mmb + " OR THE CLOSE BAND - CLOSED"
                    };
                    _cap = Label(root, _caps[0], 9.5f, P.TextDim, 0f, -97f, 430f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                int level = u >= 1.85f && u < 3.7f ? 1 : 0;
                if (level != _level)
                {
                    _level = level;
                    if (!Mini)
                    {
                        _w.SetLabels(level == 0 ? Belt : Hub);
                        _w.Line0.text = Title[level];
                    }
                    for (int i = 0; i < 8; i++)
                        _w.SetBase(i, level == 0 ? _w.Fill : (i == 0 ? _recentTint : _hubTint));
                    if (Mini) _w.ShowIcon(0, level == 1);
                }
                float open = Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 5.85f, 6.25f));
                float dip = 1f - 0.5f * (Bump(u, 1.7f, 2.0f) + Bump(u, 3.55f, 3.85f));
                _w.SetBloom(open * dip);

                Vector2 hub = _w.Root.anchoredPosition;
                float midR = (_w.Inner + _w.Outer) * 0.5f;
                Vector2 at = Vector2.Lerp(hub, _w.ScenePointOn(0, midR), Ease(u, 0.6f, 1.3f));
                at = Vector2.Lerp(at, hub + new Vector2(L(8f, 3f), L(6f, 2f)), Ease(u, 2.2f, 2.9f));
                at = Vector2.Lerp(at, _w.ClosePoint, Ease(u, 4.5f, 5.3f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.45f, 0.65f), 1f - Ease(u, 5.9f, 6.2f)));
                float rmb = Bump(u, 3.3f, 3.65f);
                _cur.SetClick(Mathf.Max(Mathf.Max(Bump(u, 1.45f, 1.75f), Bump(u, 5.45f, 5.75f)), rmb));
                _rmb.SetButton(1, rmb);
                _rip.Play(Seg(u, 3.4f, 4.0f));

                bool settled = open * dip > 0.7f;
                int hov = settled && level == 0 && u < 4.4f ? _w.HitTest(at, _w.Inner) : -1;
                _w.Tint(hov, P.Selected, 0.5f + 0.5f * Bump(u, 1.45f, 1.9f));
                _w.SetBulge(hov, L(5f, 3f));
                if (_words != null) _words.Update(t, hov, hov == 0 ? 0 : -1);
                _w.SetClose(Color.Lerp(MulA(P.Raised, 0.85f), P.Selected, Bump(u, 5.4f, 6.0f)));

                if (_cap != null)
                {
                    int cap = u < 1.85f ? 0 : u < 3.7f ? 1 : 2;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _w = null; _cur = null; _rmb = null; _words = null; }
        }

        // =====================================================================================
        //  pushout (1.7, 18.2) — a slow approach, the doubled chevron, the cursor STOPS past the rim
        //  while a dwell ring fills, the child blooms, pulling back dismisses it.
        // =====================================================================================

        private sealed class PushOutScene : Scene
        {
            internal const float Loop = 7.4f;
            private const int Branch = 1;
            private Wheel _main, _child;
            private Cursor _cur;
            private TriangleGraphic _chev;
            private RadialWedgeGraphic _dwell;
            private RectTransform _dwellRoot;
            private WordRing _words;
            private float _dIn, _dOut;

            private static readonly string[] MainLabels =
                { "THE HUB", "DRILL", "WRENCH", "CROWBAR", "CUTTERS", "CABLE" };
            private static readonly string[] ChildLabels = { "BATTERY", "SETTINGS", "STOW" };

            protected override void Make(RectTransform root)
            {
                float outer = L(62f, 24f), inner = L(28f, 10f);
                _main = Wheel.Make(root, "main", outer, inner, 6, P, true, false, 7.5f, !Mini);
                _main.SetPos(L(-64f, -34f), L(-18f, -12f));
                _main.SetCount(6);
                if (Mini) { _main.MakeIcons(4f, MulA(P.Accent, 0.7f)); _main.ShowIcon(0, false); }
                else
                {
                    _main.SetLabels(MainLabels);
                    _main.Line0.text = "TOOLBELT";
                    var equip = ArcWord.Make(C, _main.Root, "EQUIP", outer, 8f, 11f, P.Accent,
                        MulA(P.Panel, 0.92f), MulA(P.Border, 0.8f));
                    _words = new WordRing(_main, equip);
                }
                _main.SetBloom(0f);
                _chev = _main.Chevron(Branch, L(12f, 7f), P.Text);

                _dIn = L(7.5f, 4f);
                _dOut = L(10.5f, 6f);
                _dwellRoot = Node(root, "dwellRoot");
                _dwell = Wedge(_dwellRoot, "dwell", P.Accent, Color.clear, 0f);
                _dwell.SetGeometry(_dIn, _dOut, -Mathf.PI * 0.5f, -Mathf.PI * 0.5f + 0.01f, false);
                _dwell.RefreshGeometry();
                _dwell.color = Color.clear;

                _child = Wheel.Make(root, "child", L(36f, 13f), L(15f, 5f), 3, P, false, false, 7f, !Mini);
                _child.SetCount(3);
                if (!Mini) _child.SetLabels(ChildLabels);
                else _child.MakeIcons(3f, MulA(P.Accent, 0.75f));
                _child.SetBloom(0f);
                Vector2 childAt = _main.ScenePointOn(Branch, outer + L(46f, 19f));
                _child.SetPos(childAt.x, childAt.y);

                _cur = Cursor.Make(root, "cursor", P, L(16f, 11f));
                _cur.SetAlpha(0f);
                if (!Mini)
                {
                    Label(root, "SLIDE OUT PAST THE ARROW - AND STOP", 10f, P.TextDim, 0f, 98f, 430f);
                    Label(root, "no need to be quick - pull back to close", 8.5f, P.TextMute, 120f, -86f, 200f);
                }
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                _main.SetBloom(Mathf.Min(Ease(u, 0.05f, 0.45f), 1f - Ease(u, 6.8f, 7.2f)));
                Vector2 hub = _main.Root.anchoredPosition;
                Vector2 mid = _main.ScenePointOn(Branch, (_main.Inner + _main.Outer) * 0.5f);
                Vector2 past = _main.ScenePointOn(Branch, _main.Outer + L(16f, 7f));

                // A deliberately SLOW approach, a full stop past the rim, then a calm pull back.
                Vector2 at = Vector2.Lerp(hub, mid, Ease(u, 0.35f, 1.75f));
                at = Vector2.Lerp(at, past, Ease(u, 1.75f, 2.45f));
                at = Vector2.Lerp(at, hub, Ease(u, 4.9f, 5.7f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.25f, 0.45f), 1f - Ease(u, 6.4f, 6.7f)));
                _cur.SetClick(0f);

                int hov = _main.HitTest(at, _main.Inner);
                if (u < 0.3f || u > 6.6f) hov = -1;
                _main.Tint(hov, P.Selected, 0.55f);
                _main.SetBulge(hov, L(5f, 3f));

                float fill = Mathf.Min(Seg(u, 2.45f, 2.5f), 1f - Seg(u, 3.2f, 3.4f));
                _dwellRoot.anchoredPosition = past;
                if (fill > 0.002f)
                {
                    float sweep = Mathf.Max(0.02f, Seg(u, 2.45f, 3.2f) * Mathf.PI * 2f);
                    _dwell.SetGeometry(_dIn, _dOut, -Mathf.PI * 0.5f, -Mathf.PI * 0.5f + sweep, false);
                }
                _dwell.color = WithA(P.Accent, fill);

                float bloom = Mathf.Min(Ease(u, 3.2f, 3.6f), 1f - Ease(u, 5.1f, 5.5f));
                _child.SetBloom(bloom);
                // Sticky mode names nothing while a child ring is open: the word goes with it.
                if (_words != null) _words.Update(t, hov, hov == Branch && bloom < 0.05f ? 0 : -1);

                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5.5f);
                _chev.color = WithA(P.Text, hov == Branch ? 0.95f : 0.45f + 0.4f * pulse);
                Scale(_chev, 1f + 0.14f * pulse);
            }

            public override void Destroy() { _main = null; _child = null; _cur = null; _words = null; }
        }

        // =====================================================================================
        //  gridintro (1.8, 12.1) — tap the grid key: the window slides up; double-tap the mouse key:
        //  the crosshair becomes a cursor; a folder tab unfolds; tap again, and again: aim, closed.
        // =====================================================================================

        private sealed class GridIntroScene : Scene
        {
            internal const float Loop = 9.4f;
            private RectTransform _win;
            private CanvasGroup _winGroup;
            private PanelGraphic _tab;
            private TriangleGraphic _chev;
            private CanvasGroup _cellsGroup;
            private CanvasGroup _crossGroup;
            private Cursor _cur;
            private KeyVis _b, _alt;
            private Ripple _ripB, _ripAlt;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;
            private Vector2 _home, _hidden, _tabAt, _crossAt;
            private Color _tabFill, _tabOpen;

            protected override void Make(RectTransform root)
            {
                string b = K("{UIA_Grid}"), alt = K("{V:MouseControl}");
                float w = L(216f, 74f), h = L(150f, 54f);
                _home = new Vector2(L(-50f, -28f), L(-4f, 0f));
                _hidden = new Vector2(_home.x, L(-150f, -72f));
                _crossAt = _home + new Vector2(0f, L(6f, 3f));

                _winGroup = GroupNode(root, "win");
                _win = (RectTransform)_winGroup.transform;
                PanelGraphic panel;
                var inner = WindowMock(_win, "mock", w, h, P, "UNIVERSAL INVENTORY", !Mini, out panel);
                TriangleGraphic chev1;
                FolderTab(inner, "tabBelt", new Vector2(L(-46f, -10f), L(44f, 14f)), L(100f, 44f), L(20f, 9f),
                    P, "TOOLBELT", !Mini, out chev1);
                _tabAt = new Vector2(L(-46f, -10f), L(18f, 3f));
                _tab = FolderTab(inner, "tabBack", _tabAt, L(100f, 44f), L(20f, 9f), P, "BACKPACK", !Mini, out _chev);
                _tabFill = MulA(P.Warn, 0.30f);
                _tabOpen = MulA(P.Warn, 0.55f);
                _cellsGroup = GroupNode(inner, "cellsG");
                var cells = Cells.Make((RectTransform)_cellsGroup.transform, "cells", 3, 2,
                    L(58f, 17f), L(26f, 9f), L(6f, 3f), P);
                cells.SetPos(0f, L(-26f, -11f));
                for (int i = 0; i < 6; i++) cells.Occupy(i, i != 4);
                _cellsGroup.alpha = 0f;
                _winGroup.alpha = 0f;
                _win.anchoredPosition = _hidden;

                _crossGroup = GroupNode(root, "crossG");
                ((RectTransform)_crossGroup.transform).anchoredPosition = _crossAt;
                Crosshair((RectTransform)_crossGroup.transform, "cross", L(30f, 12f), L(2.4f, 1.6f), P.Accent);

                _b = KeyVis.Make(root, "b", b, P, L(30f, 18f), L(14f, 10f), L(44f, 26f));
                _b.SetPos(L(140f, 46f), L(40f, 18f));
                _alt = KeyVis.Make(root, "alt", alt, P, L(30f, 18f), L(14f, 10f), L(44f, 26f));
                _alt.SetPos(L(140f, 46f), L(-30f, -16f));
                _ripB = Ripple.Make(root, "ripB", P.Accent, L(16f, 10f), L(32f, 18f), L(2f, 1.4f));
                _ripB.SetPos(_b.PressPoint);
                _ripAlt = Ripple.Make(root, "ripAlt", P.Accent, L(16f, 10f), L(32f, 18f), L(2f, 1.4f));
                _ripAlt.SetPos(_alt.PressPoint);

                if (!Mini)
                {
                    _caps = new[]
                    {
                        "TAP " + b + " - EVERY BAG IN ONE WINDOW",
                        "DOUBLE-TAP " + alt + " - FREE THE MOUSE",
                        "CLICK A FOLDER TAB TO OPEN A BAG",
                        "TAP " + alt + " - AIM AGAIN.  TAP " + b + " - CLOSED"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 98f, 430f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 10f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                _b.SetPressed(Mathf.Max(Bump(u, 0.35f, 0.65f), Bump(u, 7.1f, 7.4f)));
                float rb1 = Seg(u, 0.45f, 1.0f), rb2 = Seg(u, 7.2f, 7.75f);
                _ripB.Play(rb1 > 0f && rb1 < 1f ? rb1 : rb2);
                _alt.SetPressed(Mathf.Max(Mathf.Max(Bump(u, 1.75f, 1.95f), Bump(u, 2.1f, 2.3f)), Bump(u, 6.4f, 6.62f)));
                float ra1 = Seg(u, 2.15f, 2.7f), ra2 = Seg(u, 6.5f, 7.0f);
                _ripAlt.Play(ra1 > 0f && ra1 < 1f ? ra1 : ra2);

                float show = Mathf.Clamp01(Ease(u, 0.55f, 1.05f) - Ease(u, 7.3f, 7.8f));
                _winGroup.alpha = show;
                _win.anchoredPosition = Vector2.Lerp(_hidden, _home, show);

                float free = Mathf.Clamp01(Ease(u, 2.3f, 2.55f) - Ease(u, 6.55f, 6.8f));
                _crossGroup.alpha = 1f - free;
                Vector2 tab = _home + _tabAt;
                Vector2 at = Vector2.Lerp(_crossAt, tab, Ease(u, 2.8f, 3.6f));
                at = Vector2.Lerp(at, _home + new Vector2(L(20f, 6f), L(-30f, -12f)), Ease(u, 4.6f, 5.6f));
                at = Vector2.Lerp(at, _crossAt, Ease(u, 5.9f, 6.5f));
                _cur.SetPos(at);
                _cur.SetAlpha(free);
                _cur.SetClick(Bump(u, 3.75f, 4.0f));

                float unfold = Mathf.Clamp01(Ease(u, 3.95f, 4.4f) - Ease(u, 7.4f, 7.7f));
                _chev.transform.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(-90f, 0f, unfold));
                _tab.color = Color.Lerp(_tabFill, _tabOpen, unfold);
                _cellsGroup.alpha = unfold;

                if (_cap != null)
                {
                    int cap = u < 1.6f ? 0 : u < 2.8f ? 1 : u < 6.3f ? 2 : 3;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _win = null; _cur = null; _b = null; _alt = null; }
        }
    }
}
