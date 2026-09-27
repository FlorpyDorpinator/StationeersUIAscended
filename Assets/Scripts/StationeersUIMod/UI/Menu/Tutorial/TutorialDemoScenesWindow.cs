using StationeersUIMod.UI.Hud;
using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    // Window- and visor-centred lesson scenes (lessons 3T, 4.3, 5.3, 6.4, 8.3, 9, 12, 14, 15).
    // FULL layout = 440x220 (cards / Watch mode), MINI = 150x76 (strip slot: shapes and keycaps
    // only). See TutorialDemoStage.cs for the stage, toolkit and contracts.
    internal sealed partial class TutorialDemoStage
    {
        /// <summary>Scene-space position of cell <paramref name="i"/> of a grid that lives directly
        /// under window node <paramref name="win"/>.</summary>
        private static Vector2 CellAt(RectTransform win, Cells c, int i)
            => win.anchoredPosition + c.Root.anchoredPosition + c.PosOf(i);

        /// <summary>A drawn tablet icon (body + screen), for cells and slots.</summary>
        private static RectTransform TabletIcon(RectTransform parent, string name, float w, Pal p)
        {
            var root = Node(parent, name);
            Panel(root, "body", w, w * 0.72f, MulA(p.Raised, 1f), p.Accent, w * 0.14f, 1.1f);
            Panel(root, "screen", w * 0.72f, w * 0.44f, MulA(p.Accent, 0.45f), Color.clear, w * 0.08f, 0f);
            return root;
        }

        // =====================================================================================
        //  devicewindow (3.4T, 12.3) — click a tablet in the big window: its slots open in a small
        //  window beside it; its X closes it; DRAG the tablet instead and it goes to your hand.
        // =====================================================================================

        private sealed class DeviceWindowScene : Scene
        {
            internal const float Loop = 10f;
            private const int TabletCell = 4;
            private RectTransform _win, _tablet;
            private Cells _cells;
            private CanvasGroup _pop;
            private RectTransform _popRt, _popX;
            private Cursor _cur;
            private PanelGraphic _hand, _handItem;
            private Vector2 _handAt, _popAt, _xAt;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            protected override void Make(RectTransform root)
            {
                PanelGraphic panel;
                _win = WindowMock(root, "win", L(180f, 66f), L(150f, 54f), P, "UNIVERSAL INVENTORY", !Mini, out panel);
                _win.anchoredPosition = new Vector2(L(-100f, -38f), L(4f, 2f));
                _cells = Cells.Make(_win, "cells", Mini ? 2 : 3, Mini ? 2 : 3, L(48f, 22f), L(32f, 16f), L(6f, 3f), P);
                _cells.SetPos(0f, L(-12f, -5f));
                int tabletCell = Mini ? 0 : TabletCell;
                for (int i = 0; i < _cells.Cell.Length; i++) _cells.Occupy(i, i != tabletCell && i != (Mini ? 3 : 7));
                _tablet = TabletIcon(_win, "tablet", L(24f, 12f), P);
                _tablet.anchoredPosition = _cells.Root.anchoredPosition + _cells.PosOf(tabletCell);

                // The device window: the item's own slots — battery (charged) and an empty cartridge.
                _popAt = new Vector2(L(100f, 40f), L(30f, 12f));
                _pop = GroupNode(root, "pop");
                _popRt = (RectTransform)_pop.transform;
                _popRt.anchoredPosition = _popAt;
                PanelGraphic popPanel;
                float pw = L(140f, 56f), ph = L(90f, 38f);
                WindowMock(_popRt, "mock", pw, ph, P, "TABLET", !Mini, out popPanel);
                popPanel.BorderColor = P.Accent;
                float sq = L(38f, 14f);
                for (int i = 0; i < 2; i++)
                {
                    float x = (i == 0 ? -1f : 1f) * L(30f, 12f);
                    var s = Panel(_popRt, "slot" + i, sq, sq, MulA(P.Raised, 0.85f), i == 0 ? MulA(P.Border, 0.7f) : MulA(P.TextDim, 0.8f), 4f, 1.2f);
                    At(s, x, -L(8f, 4f));
                    if (i == 0)
                    {
                        At(Panel(_popRt, "batt", sq * 0.5f, sq * 0.3f, MulA(P.Good, 0.85f), Color.clear, 2f, 0f), x, -L(8f, 4f));
                        if (!Mini) Label(_popRt, "BATTERY 80%", 7f, P.TextDim, x, -L(34f, 0f), 64f);
                    }
                    else if (!Mini) Label(_popRt, "CARTRIDGE", 7f, P.TextDim, x, -L(34f, 0f), 64f);
                }
                _xAt = _popAt + new Vector2(pw * 0.5f - L(12f, 5f), ph * 0.5f - L(8f, 3f));
                if (Mini)
                {
                    _popX = XGlyph(root, "x", 5f, P.TextDim);
                    _popX.SetParent(_popRt, false);
                    _popX.anchoredPosition = _xAt - _popAt;
                }
                _pop.alpha = 0f;

                _handAt = new Vector2(L(100f, 42f), L(-72f, -26f));
                _hand = Panel(root, "hand", L(60f, 28f), L(40f, 18f), MulA(P.Panel, 0.95f), P.Selected, 5f, 2f);
                At(_hand, _handAt);
                _handItem = Node(root, "handItem").gameObject.AddComponent<PanelGraphic>();
                RtOf(_handItem).sizeDelta = new Vector2(L(22f, 10f), L(16f, 7f));
                _handItem.raycastTarget = false;
                _handItem.color = MulA(P.Accent, 0.6f);
                _handItem.BorderColor = P.Accent;
                _handItem.BorderWidth = 1.1f;
                _handItem.SetShape(L(22f, 10f), L(16f, 7f), 3f);
                At(_handItem, _handAt);
                _handItem.gameObject.SetActive(false);
                if (!Mini)
                {
                    Label(root, "ACTIVE HAND", 7f, P.TextDim, _handAt.x, _handAt.y + 26f, 90f);
                    _caps = new[]
                    {
                        "CLICK A TOOL OR TABLET - ITS PARTS OPEN BESIDE IT",
                        "ITS X CLOSES IT AGAIN",
                        "DRAG IT INSTEAD - IT GOES WHERE YOU DROP IT"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 10f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                Vector2 cell = _win.anchoredPosition + _tablet.anchoredPosition;
                Vector2 at = Vector2.Lerp(cell + new Vector2(L(40f, 16f), -L(40f, 16f)), cell, Ease(u, 0.5f, 1.2f));
                at = Vector2.Lerp(at, _xAt, Ease(u, 4.3f, 5.0f));
                at = Vector2.Lerp(at, cell, Ease(u, 5.7f, 6.3f));
                // The drag carries the tablet to the hand.
                float carry = Ease(u, 6.6f, 7.5f);
                at = Vector2.Lerp(at, _handAt, carry);
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.3f, 0.5f), 1f - Ease(u, 8.2f, 8.5f)));
                _cur.SetClick(Mathf.Max(Mathf.Max(Bump(u, 1.4f, 1.65f), Bump(u, 5.1f, 5.35f)), Held(u, 6.4f, 7.6f)));

                float pop = Mathf.Clamp01(Ease(u, 1.55f, 1.95f) - Ease(u, 5.2f, 5.6f));
                _pop.alpha = pop;
                float ps = Mathf.Lerp(0.5f, 1f, pop);
                _popRt.localScale = new Vector3(ps, ps, 1f);
                _popRt.anchoredPosition = Vector2.Lerp(cell, _popAt, pop);

                bool dragging = u >= 6.4f && u < 7.6f;
                bool inHand = u >= 7.6f && u < 9.4f;
                Vector2 tab = _cells.Root.anchoredPosition + _cells.PosOf(Mini ? 0 : TabletCell);
                _tablet.anchoredPosition = dragging ? at - _win.anchoredPosition + new Vector2(L(10f, 5f), -L(10f, 5f)) : tab;
                bool showTablet = !inHand && u < 9.4f || u >= 9.7f;
                if (_tablet.gameObject.activeSelf != showTablet) _tablet.gameObject.SetActive(showTablet);
                if (_handItem.gameObject.activeSelf != inHand) _handItem.gameObject.SetActive(inHand);
                _hand.BorderWidth = Mathf.Lerp(2f, 3f, Bump(u, 7.5f, 8.0f));

                if (_cap != null)
                {
                    int cap = u < 4.0f ? 0 : u < 5.6f ? 1 : 2;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _win = null; _cells = null; _cur = null; }
        }

        // =====================================================================================
        //  splitpopup (4.3) — click a stack in the big window: compact SQUARE buttons pop (1, 1/2,
        //  and a number you set with the scroll wheel, each over a small SPLIT caption).
        // =====================================================================================

        private sealed class SplitPopupScene : Scene
        {
            internal const float Loop = 9f;
            private const int StackCell = 4;
            private RectTransform _win;
            private Cells _cells;
            private CanvasGroup _pop;
            private RectTransform _popRt;
            private PanelGraphic[] _sq;
            private TextMeshProUGUI _count, _stackCount;
            private TriangleGraphic _up, _down;
            private Mouse _scroll;
            private Cursor _cur;
            private Flyer _fly;
            private PanelGraphic _hand, _handItem;
            private Vector2 _popAt, _handAt;
            private string[] _nums;
            private int _numIdx = -1, _stackIdx = -1;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            protected override void Make(RectTransform root)
            {
                PanelGraphic panel;
                _win = WindowMock(root, "win", L(170f, 54f), L(150f, 54f), P, "UNIVERSAL INVENTORY", !Mini, out panel);
                _win.anchoredPosition = new Vector2(L(-118f, -48f), L(0f, 2f));
                _cells = Cells.Make(_win, "cells", Mini ? 1 : 3, Mini ? 1 : 3, L(44f, 30f), L(32f, 30f), L(6f, 3f), P);
                _cells.SetPos(0f, L(-12f, -4f));
                for (int i = 0; i < _cells.Cell.Length; i++) _cells.Occupy(i, true);
                int sc = Mini ? 0 : StackCell;
                if (!Mini)
                {
                    Vector2 cp = _cells.Root.anchoredPosition + _cells.PosOf(sc);
                    _stackCount = Label(_win, "x17", 7.5f, P.Text, cp.x + 10f, cp.y - 9f, 30f);
                }

                _nums = new string[8];
                for (int i = 0; i < 8; i++) _nums[i] = (i + 1).ToString();
                _popAt = new Vector2(L(62f, 28f), L(24f, 6f));
                _pop = GroupNode(root, "pop");
                _popRt = (RectTransform)_pop.transform;
                PanelGraphic popPanel;
                float pw = L(176f, 94f), ph = L(86f, 42f);
                WindowMock(_popRt, "mock", pw, ph, P, "CABLE COIL x17", !Mini, out popPanel);
                popPanel.BorderColor = P.Accent;
                float s = L(46f, 24f);
                string[] glyph = { "1", "1/2", _nums[0] };
                _sq = new PanelGraphic[3];
                for (int i = 0; i < 3; i++)
                {
                    float x = (i - 1) * (s + L(10f, 5f));
                    float y = -L(8f, 3f);
                    _sq[i] = Panel(_popRt, "sq" + i, s, s, MulA(P.Raised, 0.95f), MulA(P.Border, 0.9f), L(6f, 4f), 1.4f);
                    At(_sq[i], x, y);
                    var g = Label(_popRt, glyph[i], L(14f, 10f), P.Text, x, y + L(4f, 1f), s);
                    if (i == 2) _count = g;
                    if (!Mini) Label(_popRt, "SPLIT", 6.5f, P.TextDim, x, y - 13f, s);
                    if (i == 2)
                    {
                        _up = Tri(_popRt, "up", L(7f, 4f), P.Accent, true, 0f);
                        At(_up, x + s * 0.5f - L(7f, 4f), y + s * 0.5f - L(7f, 4f));
                        _down = Tri(_popRt, "down", L(7f, 4f), P.Accent, false, 0f);
                        At(_down, x + s * 0.5f - L(7f, 4f), y - s * 0.5f + L(7f, 4f));
                    }
                }
                _pop.alpha = 0f;

                _scroll = Mouse.Make(root, "scroll", P, L(30f, 16f));
                _scroll.SetAlpha(0f);
                _fly = Flyer.Make(root, "fly", L(16f, 8f), P);
                _handAt = new Vector2(L(62f, 58f), L(-70f, -26f));
                _hand = Panel(root, "hand", L(60f, 26f), L(40f, 18f), MulA(P.Panel, 0.95f), P.Selected, 5f, 2f);
                At(_hand, _handAt);
                _handItem = Panel(root, "handItem", L(16f, 9f), L(16f, 9f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem, _handAt);
                _handItem.gameObject.SetActive(false);
                if (!Mini)
                {
                    Label(root, "YOUR HAND", 7f, P.TextDim, _handAt.x, _handAt.y + 26f, 90f);
                    _caps = new[]
                    {
                        "CLICK A STACK WITH THE MOUSE FREE",
                        "SPLIT ONE, HALF - OR SCROLL A NUMBER AND CLICK IT",
                        "THE SPLIT PART LANDS IN YOUR HAND"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 10f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                Vector2 cell = CellAt(_win, _cells, Mini ? 0 : StackCell);
                Vector2 num = _popAt + Pos(_sq[2]);
                Vector2 at = Vector2.Lerp(cell + new Vector2(L(36f, 14f), -L(40f, 16f)), cell, Ease(u, 0.4f, 1.1f));
                at = Vector2.Lerp(at, num + new Vector2(-L(6f, 3f), -L(4f, 2f)), Ease(u, 2.0f, 2.7f));
                at = Vector2.Lerp(at, num + new Vector2(L(40f, 12f), -L(30f, 10f)), Ease(u, 5.4f, 6.0f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.25f, 0.45f), 1f - Ease(u, 7.6f, 7.9f)));
                _cur.SetClick(Mathf.Max(Bump(u, 1.25f, 1.5f), Bump(u, 4.6f, 4.85f)));

                float pop = Mathf.Clamp01(Ease(u, 1.4f, 1.8f) - Ease(u, 7.4f, 7.8f));
                _pop.alpha = pop;
                float ps = Mathf.Lerp(0.6f, 1f, pop);
                _popRt.localScale = new Vector3(ps, ps, 1f);
                _popRt.anchoredPosition = _popAt;

                float scrolling = Window01(u, 2.8f, 4.4f, 0.15f);
                _scroll.SetAlpha(scrolling);
                _scroll.SetPos(num + new Vector2(L(40f, 0f), L(2f, 24f)));
                _scroll.Scroll(Seg(u, 2.9f, 4.3f) * 4f, scrolling);
                int ni = Mathf.Clamp((int)(Seg(u, 2.9f, 4.3f) * 7.999f), 0, 7);
                if (ni != _numIdx) { _numIdx = ni; _count.text = _nums[ni]; }
                _up.color = WithA(P.Accent, 0.4f + 0.6f * scrolling);
                _down.color = WithA(P.Accent, 0.4f + 0.6f * scrolling);
                float hot = Mathf.Max(Window01(u, 2.6f, 4.9f, 0.15f) * 0.6f, Bump(u, 4.6f, 5.1f));
                _sq[2].BorderColor = Color.Lerp(MulA(P.Border, 0.9f), P.Accent, hot);
                _sq[2].color = Hot(MulA(P.Raised, 0.95f), P.Accent, hot * 0.3f);

                bool flying = u >= 4.85f && u < 5.5f;
                if (flying) _fly.Set(Arc(num, _handAt, Seg(u, 4.85f, 5.5f), L(24f, 10f)), 1f);
                else _fly.Hide();
                bool inHand = u >= 5.45f && u < 8.6f;
                if (_handItem.gameObject.activeSelf != inHand) _handItem.gameObject.SetActive(inHand);
                if (_stackCount != null)
                {
                    int sIdx = u >= 4.85f ? 1 : 0;
                    if (sIdx != _stackIdx) { _stackIdx = sIdx; _stackCount.text = sIdx == 1 ? "x9" : "x17"; }
                }
                if (_cap != null)
                {
                    int cap = u < 1.6f ? 0 : u < 4.85f ? 1 : 2;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _win = null; _cells = null; _cur = null; _scroll = null; }
        }

        // =====================================================================================
        //  worldslot (5.3) — a dragged item over a machine's slot within reach: the game's own
        //  placement box, green (place / swap), yellow (merge), blue (insert), red (refused).
        // =====================================================================================

        private sealed class WorldSlotScene : Scene
        {
            internal const float Loop = 8f;
            private Wheel _w;
            private PanelGraphic _box, _chip;
            private Cursor _cur;
            private Vector2 _slot;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private static readonly string[] Caps =
            {
                "GREEN - IT FITS: PLACE OR SWAP",
                "YELLOW - IT MERGES WITH THE STACK",
                "BLUE - IT GOES INSIDE",
                "RED - IT WON'T GO THERE"
            };
            // Vanilla's InputMouse.GetWorldResultColor, which WorldSlotCue mirrors exactly.
            private static readonly Color[] Cue = { Color.green, Color.yellow, Color.blue, Color.red };

            protected override void Make(RectTransform root)
            {
                _w = Wheel.Make(root, "src", L(50f, 16f), L(22f, 7f), 6, P, true, false, 7f, false);
                _w.SetPos(L(-160f, -58f), L(10f, 8f));
                _w.SetCount(6);
                _w.MakeIcons(L(5f, 3f), MulA(P.Accent, 0.7f));
                _w.SetBloom(1f);
                _w.Dim = 0.55f;
                _w.SetBloom(1f);

                Vector2 body = new Vector2(L(70f, 30f), L(0f, 0f));
                Panel(root, "machine", L(140f, 60f), L(128f, 58f), MulA(P.Raised, 0.9f), P.Border, L(10f, 5f), 1.5f)
                    .rectTransform.anchoredPosition = body;
                Panel(root, "vent", L(90f, 36f), L(8f, 4f), MulA(P.Window, 0.9f), Color.clear, 3f, 0f)
                    .rectTransform.anchoredPosition = body + new Vector2(0f, L(46f, 20f));
                _slot = body + new Vector2(0f, L(4f, 2f));
                Panel(root, "slot", L(40f, 20f), L(40f, 20f), MulA(P.Window, 0.95f), MulA(P.Border, 0.9f), 4f, 1.2f)
                    .rectTransform.anchoredPosition = _slot;
                _box = Panel(root, "cue", L(54f, 27f), L(54f, 27f), Color.clear, Color.green, 3f, L(2.6f, 1.8f));
                At(_box, _slot);
                if (!Mini) Label(root, "LOCKER SLOT", 8f, P.TextDim, body.x, body.y - 40f, 120f);
                _chip = Panel(root, "chip", L(20f, 10f), L(20f, 10f), MulA(P.Accent, 0.8f), P.Accent, 3f, 1.2f);
                _cur = Cursor.Make(root, "cursor", P, L(16f, 10f));
                if (!Mini) _cap = Label(root, Caps[0], 10f, P.TextDim, 0f, 100f, 430f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                Vector2 src = _w.ScenePointOn(1, (_w.Inner + _w.Outer) * 0.5f);
                Vector2 over = _slot + new Vector2(-L(6f, 3f), L(6f, 3f));
                Vector2 at = Vector2.Lerp(src, over, Ease(u, 0.3f, 1.2f));
                int phase = Mathf.Clamp((int)((u - 1.3f) / 1.5f), 0, 3);
                // The refused phase shakes a little, like a "no".
                if (phase == 3) at.x += Mathf.Sin(u * 40f) * L(2f, 1f) * Window01(u, 5.8f, 6.6f, 0.1f);
                _cur.SetPos(at);
                float alive = Mathf.Min(Ease(u, 0.1f, 0.3f), 1f - Ease(u, 7.3f, 7.8f));
                _cur.SetAlpha(alive);
                _cur.SetClick(0.8f);
                At(_chip, at + new Vector2(L(10f, 5f), -L(10f, 5f)));
                _chip.color = WithA(P.Accent, 0.8f * alive);
                _chip.BorderColor = WithA(P.Accent, alive);

                float boxOn = u >= 1.3f ? Mathf.Min(Ease(u, 1.3f, 1.45f), 1f - Ease(u, 7.3f, 7.6f)) : 0f;
                _box.BorderColor = WithA(Cue[phase], boxOn * (0.75f + 0.25f * Mathf.Sin(u * 6f)));
                if (_cap != null && phase != _capIdx) { _capIdx = phase; _cap.text = Caps[phase]; }
            }

            public override void Destroy() { _w = null; _cur = null; }
        }

        // =====================================================================================
        //  ingrid (6.4) — Shift + the back gear key opens that bag INSIDE the big window; the same
        //  again (or a click on its HUD box, mouse free) folds it away.
        // =====================================================================================

        private sealed class InGridScene : Scene
        {
            internal const float Loop = 8.4f;
            private KeyVis _shift, _key;
            private Ripple _rip;
            private RectTransform _win;
            private PanelGraphic _tab, _hudBox;
            private TriangleGraphic _chev;
            private CanvasGroup _cells;
            private Cursor _cur;
            private Color _tabFill, _tabOpen;
            private Vector2 _hudAt;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            protected override void Make(RectTransform root)
            {
                string four = K("{V:BackSlot}");
                _shift = KeyVis.Make(root, "shift", "Shift", P, L(28f, 14f), L(12f, 8f), 30f);
                _key = KeyVis.Make(root, "key", four, P, L(28f, 14f), L(13f, 8f), L(40f, 22f));
                float gap = L(26f, 10f);
                float total = _shift.W + gap + _key.W;
                Vector2 c = new Vector2(L(-150f, -48f), L(-40f, -24f));
                _shift.SetPos(c.x - total * 0.5f + _shift.W * 0.5f, c.y);
                _key.SetPos(c.x + total * 0.5f - _key.W * 0.5f, c.y);
                PlusGlyph(root, "plus", L(10f, 6f), L(2.4f, 1.6f), P.TextDim).anchoredPosition =
                    new Vector2(c.x - total * 0.5f + _shift.W + gap * 0.5f, c.y);
                _rip = Ripple.Make(root, "rip", P.Accent, L(14f, 8f), L(28f, 15f), L(2f, 1.4f));
                _rip.SetPos(_key.PressPoint);

                PanelGraphic panel;
                float w = L(214f, 86f), h = L(160f, 60f);
                _win = WindowMock(root, "win", w, h, P, "UNIVERSAL INVENTORY", !Mini, out panel);
                _win.anchoredPosition = new Vector2(L(70f, 28f), L(0f, 6f));
                TriangleGraphic c1;
                FolderTab(_win, "belt", new Vector2(-w * 0.5f + L(58f, 24f), L(50f, 17f)), L(100f, 42f), L(20f, 8f), P, "TOOLBELT", !Mini, out c1);
                Vector2 tabAt = new Vector2(-w * 0.5f + L(58f, 24f), L(24f, 7f));
                _tab = FolderTab(_win, "back", tabAt, L(100f, 42f), L(20f, 8f), P, "BACKPACK", !Mini, out _chev);
                _tabFill = MulA(P.Warn, 0.30f);
                _tabOpen = MulA(P.Warn, 0.6f);
                _cells = GroupNode(_win, "cellsG");
                var cells = Cells.Make((RectTransform)_cells.transform, "cells", 4, 2, L(42f, 16f), L(26f, 10f), L(6f, 3f), P);
                cells.SetPos(0f, L(-22f, -9f));
                for (int i = 0; i < 8; i++) cells.Occupy(i, i != 3 && i != 6);
                _cells.alpha = 0f;

                if (!Mini)
                {
                    _hudAt = new Vector2(-150f, 42f);
                    _hudBox = Panel(root, "hud", 70f, 30f, MulA(P.Panel, 0.95f), P.Border, 6f, 1.4f);
                    At(_hudBox, _hudAt);
                    Label(root, "BACK", 7.5f, P.TextDim, _hudAt.x, _hudAt.y + 8f, 60f);
                    At(Panel(root, "bag", 18f, 12f, MulA(P.Accent, 0.7f), Color.clear, 3f, 0f), _hudAt + new Vector2(0f, -4f));
                    Label(root, "HUD BOX", 7f, P.TextMute, _hudAt.x, _hudAt.y - 24f, 80f);
                    _caps = new[]
                    {
                        "SHIFT + " + four + " - THAT BAG OPENS IN THE BIG WINDOW",
                        "AGAIN - OR CLICK ITS HUD BOX, MOUSE FREE - FOLDS IT AWAY"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                    _cur = Cursor.Make(root, "cursor", P, 16f);
                    _cur.SetAlpha(0f);
                }
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                // Beat 1: the chord unfolds the bag. Beat 2: a HUD-box click (full) / the chord (mini) folds it.
                _shift.SetPressed(Mathf.Max(Held(u, 0.5f, 1.7f), Mini ? Held(u, 4.4f, 5.6f) : 0f));
                _key.SetPressed(Mathf.Max(Bump(u, 0.95f, 1.25f), Mini ? Bump(u, 4.85f, 5.15f) : 0f));
                float r1 = Seg(u, 1.05f, 1.6f), r2 = Mini ? Seg(u, 4.95f, 5.5f) : 0f;
                _rip.Play(r1 > 0f && r1 < 1f ? r1 : r2);
                float open = Mathf.Clamp01(Ease(u, 1.15f, 1.6f) - Ease(u, 5.0f, 5.4f));
                _chev.transform.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(-90f, 0f, open));
                _tab.color = Color.Lerp(_tabFill, _tabOpen, open);
                _tab.BorderColor = Color.Lerp(MulA(P.Warn, 0.85f), P.Accent, Bump(u, 1.1f, 1.7f));
                _cells.alpha = open;

                if (_cur != null)
                {
                    Vector2 at = Vector2.Lerp(_hudAt + new Vector2(40f, -50f), _hudAt, Ease(u, 3.9f, 4.7f));
                    _cur.SetPos(at);
                    _cur.SetAlpha(Mathf.Min(Ease(u, 3.7f, 3.9f), 1f - Ease(u, 5.6f, 5.9f)));
                    float click = Bump(u, 4.8f, 5.05f);
                    _cur.SetClick(click);
                    _hudBox.BorderColor = Color.Lerp(P.Border, P.Accent, Bump(u, 4.8f, 5.3f));
                }
                if (_cap != null)
                {
                    int cap = u < 3.6f ? 0 : 1;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _shift = null; _key = null; _win = null; _cur = null; }
        }

        // =====================================================================================
        //  search (8.3) — click SEARCH and just type: matches from every bag narrow as you type;
        //  click one to take it.
        // =====================================================================================

        private sealed class SearchScene : Scene
        {
            internal const float Loop = 9f;
            private RectTransform _panel;
            private CanvasGroup _panelGroup;
            private TextMeshProUGUI _query;
            private PanelGraphic _caret;
            private CanvasGroup[] _rows;
            private TextMeshProUGUI[] _rowName, _rowWhere;
            private PanelGraphic[] _rowBg;
            private PanelGraphic[] _typed;   // mini: letter marks in the field
            private KeyVis[] _keys;
            private Cursor _cur;
            private Flyer _fly;
            private PanelGraphic _handItem;
            private Vector2 _handAt;
            private int _stage = -1;
            private int[] _count = { 0, 4, 3, 2 };
            private string[][] _names, _wheres;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;
            private float _fieldX0, _letterW;

            private static readonly string[] Query = { "", "c", "ca", "cab" };

            protected override void Make(RectTransform root)
            {
                _panelGroup = GroupNode(root, "panel");
                _panel = (RectTransform)_panelGroup.transform;
                _panel.anchoredPosition = new Vector2(L(-56f, -24f), L(-2f, 2f));
                float w = L(250f, 96f), h = L(176f, 66f);
                Panel(_panel, "bg", w, h, MulA(P.Window, 0.96f), P.Border, L(10f, 6f), 1.5f);
                float fieldY = h * 0.5f - L(22f, 9f);
                Panel(_panel, "field", w - L(20f, 8f), L(26f, 12f), MulA(P.Raised, 0.95f), P.Accent, L(6f, 3f), 1.3f)
                    .rectTransform.anchoredPosition = new Vector2(0f, fieldY);
                // A drawn magnifier: ring + handle.
                float mx = -w * 0.5f + L(24f, 10f);
                At(Circle(_panel, "lens", L(5.5f, 2.6f), Color.clear, P.TextDim, L(1.6f, 1f)), mx, fieldY + L(1f, 0.5f));
                var handle = Panel(_panel, "handle", L(6f, 3f), L(1.8f, 1.1f), P.TextDim, Color.clear, 0.9f, 0f);
                handle.transform.localEulerAngles = new Vector3(0f, 0f, -45f);
                At(handle, mx + L(5f, 2.4f), fieldY - L(4f, 2f));
                _fieldX0 = mx + L(12f, 6f);
                _caret = Panel(_panel, "caret", L(1.6f, 1.2f), L(14f, 7f), P.Text, Color.clear, 0.8f, 0f);
                At(_caret, _fieldX0, fieldY);

                int rows = 4;
                _rows = new CanvasGroup[rows];
                _rowBg = new PanelGraphic[rows];
                if (!Mini)
                {
                    _query = Label(_panel, "", 11f, P.Text, _fieldX0 + 60f, fieldY, 120f, TextAlignmentOptions.Left);
                    _rowName = new TextMeshProUGUI[rows];
                    _rowWhere = new TextMeshProUGUI[rows];
                    _names = new[]
                    {
                        new[] { "", "", "", "" },
                        new[] { "Cable Coil x50", "Canister (O2)", "Crowbar", "Cartridge" },
                        new[] { "Cable Coil x50", "Canister (O2)", "Cartridge", "" },
                        new[] { "Cable Coil x50", "Cable Coil x12", "", "" }
                    };
                    _wheres = new[]
                    {
                        new[] { "", "", "", "" },
                        new[] { "Toolbelt", "Backpack", "Toolbelt", "Jetpack" },
                        new[] { "Toolbelt", "Backpack", "Jetpack", "" },
                        new[] { "Toolbelt", "Backpack", "", "" }
                    };
                }
                else
                {
                    _letterW = 5f;
                    _typed = new PanelGraphic[3];
                    for (int i = 0; i < 3; i++)
                    {
                        _typed[i] = Panel(_panel, "ch" + i, 3.6f, 5f, P.Text, Color.clear, 1f, 0f);
                        At(_typed[i], _fieldX0 + 3f + i * _letterW, fieldY);
                        _typed[i].gameObject.SetActive(false);
                    }
                }
                float rowH = L(26f, 9f), rowGap = L(6f, 3f);
                float top = fieldY - L(26f, 12f);
                for (int i = 0; i < rows; i++)
                {
                    _rows[i] = GroupNode(_panel, "row" + i);
                    var rr = (RectTransform)_rows[i].transform;
                    rr.anchoredPosition = new Vector2(0f, top - i * (rowH + rowGap));
                    _rowBg[i] = Panel(rr, "bg", w - L(20f, 8f), rowH, MulA(P.Panel, 0.95f), MulA(P.Border, 0.7f), L(5f, 2.5f), 1.1f);
                    Panel(rr, "icon", rowH * 0.62f, rowH * 0.62f, MulA(P.Accent, 0.7f), Color.clear, 3f, 0f)
                        .rectTransform.anchoredPosition = new Vector2(-w * 0.5f + L(26f, 10f), 0f);
                    if (!Mini)
                    {
                        _rowName[i] = Label(rr, "", 9f, P.Text, -w * 0.5f + 40f + 60f, 0f, 120f, TextAlignmentOptions.Left);
                        _rowWhere[i] = Label(rr, "", 8f, P.TextDim, w * 0.5f - 60f, 0f, 90f, TextAlignmentOptions.Right);
                    }
                    else
                    {
                        Panel(rr, "name", 44f, 2.6f, MulA(P.TextDim, 0.9f), Color.clear, 1.3f, 0f)
                            .rectTransform.anchoredPosition = new Vector2(4f, 0f);
                    }
                    _rows[i].alpha = 0f;
                }
                // The typed keys, pressed in turn (typing is literal: letters, not bindings).
                _keys = new KeyVis[3];
                string[] letters = { "C", "A", "B" };
                for (int i = 0; i < 3; i++)
                {
                    _keys[i] = KeyVis.Make(root, "key" + i, letters[i], P, L(24f, 14f), L(11f, 8f), 30f);
                    _keys[i].SetPos(L(126f, 50f) + (Mini ? 0f : (i - 1) * 32f), Mini ? 26f - i * 16f : 52f);
                }
                _fly = Flyer.Make(root, "fly", L(14f, 7f), P);
                _handAt = new Vector2(L(150f, 60f), L(-56f, -28f));
                At(Panel(root, "hand", L(60f, 24f), L(40f, 16f), MulA(P.Panel, 0.95f), P.Selected, 5f, 2f), _handAt);
                _handItem = Panel(root, "handItem", L(16f, 8f), L(16f, 8f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem, _handAt);
                _handItem.gameObject.SetActive(false);
                if (!Mini)
                {
                    Label(root, "YOUR HAND", 7f, P.TextDim, _handAt.x, _handAt.y + 26f, 90f);
                    _caps = new[]
                    {
                        "CLICK SEARCH AND JUST TYPE",
                        "MATCHES FROM EVERY BAG - CLICK ONE TO TAKE IT",
                        "Esc OR RIGHT-CLICK LEAVES THE SEARCH"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 10f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                _panelGroup.alpha = Mathf.Min(Ease(u, 0.2f, 0.5f), 1f - Ease(u, 5.2f, 5.6f));
                int stage = u < 1.0f ? 0 : u < 1.6f ? 1 : u < 2.2f ? 2 : 3;
                if (stage != _stage)
                {
                    _stage = stage;
                    if (!Mini)
                    {
                        _query.text = Query[stage];
                        for (int i = 0; i < _rows.Length; i++)
                        {
                            _rowName[i].text = _names[stage][i];
                            _rowWhere[i].text = _wheres[stage][i];
                        }
                    }
                    else
                        for (int i = 0; i < 3; i++) _typed[i].gameObject.SetActive(i < stage);
                }
                for (int i = 0; i < _rows.Length; i++)
                    _rows[i].alpha = i < _count[stage] ? 1f : Mathf.MoveTowards(_rows[i].alpha, 0f, 0.2f);
                for (int i = 0; i < 3; i++) _keys[i].SetPressed(Bump(u, 0.85f + i * 0.6f, 1.1f + i * 0.6f));
                float caretX = Mini ? _fieldX0 + 1f + stage * _letterW : _fieldX0 + stage * 6.2f;
                At(_caret, caretX, Pos(_caret).y);
                _caret.color = WithA(P.Text, Mathf.Repeat(u, 0.9f) < 0.5f ? 1f : 0.15f);

                Vector2 row0 = _panel.anchoredPosition + ((RectTransform)_rows[0].transform).anchoredPosition;
                Vector2 at = Vector2.Lerp(row0 + new Vector2(L(60f, 24f), -L(60f, 24f)), row0 + new Vector2(-L(20f, 8f), 0f), Ease(u, 2.8f, 3.6f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 2.6f, 2.8f), 1f - Ease(u, 5.0f, 5.3f)));
                _cur.SetClick(Bump(u, 3.8f, 4.05f));
                _rowBg[0].BorderColor = Color.Lerp(MulA(P.Border, 0.7f), P.Accent, Window01(u, 3.3f, 4.3f, 0.15f));

                bool flying = u >= 4.05f && u < 4.7f;
                if (flying) _fly.Set(Arc(row0, _handAt, Seg(u, 4.05f, 4.7f), L(26f, 10f)), 1f);
                else _fly.Hide();
                bool inHand = u >= 4.65f && u < 8.6f;
                if (_handItem.gameObject.activeSelf != inHand) _handItem.gameObject.SetActive(inHand);

                if (_cap != null)
                {
                    int cap = u < 2.6f ? 0 : u < 5.0f ? 1 : 2;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _panel = null; _rows = null; _keys = null; _cur = null; }
        }

        // =====================================================================================
        //  stowroute (9.1, 9.3) — where the stow key sends things (Simple SmartStow, the default):
        //  back where it came from; new items try their belt slot, a matching stack, an empty
        //  socket. Then Complex mode's bag rules.
        // =====================================================================================

        private sealed class StowRouteScene : Scene
        {
            internal const float Loop = 12.6f;
            private PanelGraphic[] _chip;
            private PanelGraphic[] _box;
            private PanelGraphic _handItem;
            private Flyer _fly;
            private KeyVis _g;
            private Ripple _rip;
            private Vector2 _handAt;
            private Vector2[] _boxAt;
            private Color _chipFill, _boxFill;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            private static readonly string[] Chips = { "CAME FROM", "BELT SLOT", "STACK", "SOCKET", "BAG RULES" };
            private static readonly string[] Boxes = { "BELT", "SUIT", "ORES BAG" };

            protected override void Make(RectTransform root)
            {
                string g = K("{V:SmartStow}");
                _chipFill = MulA(P.Panel, 0.95f);
                _boxFill = MulA(P.Panel, 0.95f);
                _handAt = new Vector2(L(-172f, -58f), L(-6f, 8f));
                At(Panel(root, "hand", L(60f, 28f), L(44f, 22f), MulA(P.Panel, 0.95f), P.Selected, 6f, 2f), _handAt);
                _handItem = Panel(root, "item", L(20f, 10f), L(20f, 10f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem, _handAt);
                _g = KeyVis.Make(root, "g", g, P, L(28f, 16f), L(13f, 9f), L(40f, 22f));
                _g.SetPos(_handAt.x, L(-62f, -24f));
                _rip = Ripple.Make(root, "rip", P.Accent, L(15f, 8f), L(30f, 15f), L(2f, 1.4f));
                _rip.SetPos(_g.PressPoint);

                _chip = new PanelGraphic[5];
                for (int i = 0; i < 5; i++)
                {
                    Vector2 c = Mini
                        ? new Vector2(-28f + i * 13f + (i == 4 ? 6f : 0f), 24f)
                        : (i < 4 ? new Vector2(-96f + i * 90f, 56f) : new Vector2(174f, 20f));
                    _chip[i] = Panel(root, "chip" + i, L(80f, 9f), L(26f, 9f), _chipFill, P.Border, L(7f, 2.5f), 1.3f);
                    At(_chip[i], c);
                    if (!Mini)
                    {
                        Label(root, Chips[i], 8f, P.Text, c.x, c.y, 76f);
                        if (i > 0 && i < 4) At(Tri(root, "sep" + i, 7f, P.TextMute, true, -90f), c.x - 45f, c.y);
                    }
                }
                if (!Mini) Label(root, "COMPLEX MODE", 7f, P.TextMute, 174f, 38f, 90f);

                _box = new PanelGraphic[3];
                _boxAt = new Vector2[3];
                for (int i = 0; i < 3; i++)
                {
                    _boxAt[i] = Mini ? new Vector2(-22f + i * 30f, -18f) : new Vector2(-40f + i * 92f, -52f);
                    _box[i] = Panel(root, "box" + i, L(76f, 24f), L(40f, 18f), _boxFill, P.Border, 6f, 1.4f);
                    At(_box[i], _boxAt[i]);
                    if (!Mini) Label(root, Boxes[i], 7.5f, P.TextDim, _boxAt[i].x, _boxAt[i].y + 11f, 72f);
                }
                _fly = Flyer.Make(root, "fly", L(18f, 9f), P);
                if (!Mini)
                {
                    _caps = new[]
                    {
                        g + " SENDS IT BACK WHERE IT CAME FROM",
                        "NEW ITEMS: THEIR BELT SLOT, A MATCHING STACK, AN EMPTY SOCKET",
                        "COMPLEX MODE ADDS BAG RULES - ORES GO TO THE ORES BAG"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                int beat = u < 4f ? 0 : u < 8.4f ? 1 : 2;
                float b0 = beat == 0 ? 0f : beat == 1 ? 4f : 8.4f;
                float v = u - b0;
                // Which chip catches it this beat, and where it goes.
                int hit = beat == 0 ? 0 : beat == 1 ? 3 : 4;
                int dest = beat == 0 ? 0 : beat == 1 ? 1 : 2;
                float g = Bump(v, 0.4f, 0.65f);
                _g.SetPressed(g);
                _rip.Play(Seg(v, 0.5f, 1.1f));
                // The item stays in the hand until the chip that catches it sends it flying.
                float fly0 = 0.7f + hit * 0.3f + 0.3f;
                bool itemInHand = v < fly0;
                if (_handItem.gameObject.activeSelf != itemInHand) _handItem.gameObject.SetActive(itemInHand);

                for (int i = 0; i < 5; i++)
                {
                    float at = 0.7f + i * 0.3f;
                    float trying = i <= hit ? Bump(v, at, at + 0.35f) : 0f;
                    float caught = i == hit ? Mathf.Min(Seg(v, at + 0.15f, at + 0.25f), 1f - Seg(v, 3.4f, 3.8f)) : 0f;
                    bool dimmed = beat < 2 && i == 4;
                    Color border = dimmed ? MulA(P.Border, 0.4f) : P.Border;
                    _chip[i].BorderColor = Color.Lerp(Color.Lerp(border, P.Accent, trying), P.Good, caught);
                    _chip[i].color = Hot(_chipFill, P.Good, caught * 0.35f);
                }

                bool flying = v >= fly0 && v < fly0 + 0.6f;
                if (flying) _fly.Set(Arc(_handAt, _boxAt[dest], Seg(v, fly0, fly0 + 0.6f), L(44f, 14f)), 1f);
                else _fly.Hide();
                for (int i = 0; i < 3; i++)
                {
                    float k = i == dest ? Mathf.Min(Seg(v, fly0 + 0.55f, fly0 + 0.62f), 1f - Seg(v, fly0 + 0.9f, fly0 + 1.3f)) : 0f;
                    _box[i].BorderColor = Color.Lerp(P.Border, P.Accent, k);
                    _box[i].color = Hot(_boxFill, P.Accent, k * 0.3f);
                    _box[i].BorderWidth = Mathf.Lerp(1.4f, 2.4f, k);
                }
                if (_cap != null && beat != _capIdx) { _capIdx = beat; _cap.text = _caps[beat]; }
            }

            public override void Destroy() { _chip = null; _box = null; _g = null; _fly = null; }
        }

        // =====================================================================================
        //  gridmoves (12.2) — mouse free in the big window: a click takes, a right-click opens the
        //  item's wheel, a drag moves it anywhere (here: into the other hand).
        // =====================================================================================

        private sealed class GridMovesScene : Scene
        {
            internal const float Loop = 9f;
            private RectTransform _win;
            private Cells _cells;
            private Wheel _small;
            private Cursor _cur;
            private Mouse _rmb;
            private Flyer _fly;
            private HandPair _hands;
            private PanelGraphic _handBox, _handItem0, _handItem1;
            private Vector2 _hand0, _hand1;
            private int _tookCell, _wheelCell, _dragCell;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;
            private int _stateIdx = -1;

            protected override void Make(RectTransform root)
            {
                PanelGraphic panel;
                _win = WindowMock(root, "win", L(200f, 70f), L(150f, 54f), P, "UNIVERSAL INVENTORY", !Mini, out panel);
                _win.anchoredPosition = new Vector2(L(-80f, -38f), L(4f, 4f));
                _cells = Cells.Make(_win, "cells", Mini ? 3 : 4, Mini ? 2 : 3, L(40f, 18f), L(28f, 14f), L(6f, 3f), P);
                _cells.SetPos(0f, L(-12f, -5f));
                for (int i = 0; i < _cells.Cell.Length; i++) _cells.Occupy(i, true);
                _tookCell = Mini ? 0 : 1;
                _wheelCell = Mini ? 4 : 6;
                _dragCell = Mini ? 2 : 9;

                _small = Wheel.Make(root, "itemWheel", L(34f, 14f), L(14f, 6f), 4, P, true, false, 6.5f, !Mini);
                _small.SetCount(4);
                if (!Mini) _small.SetLabels(new[] { "TAKE", "SPLIT", "DROP", "STOW" });
                else _small.MakeIcons(2.6f, MulA(P.Accent, 0.7f));
                Vector2 wc = CellAt(_win, _cells, _wheelCell);
                _small.SetPos(wc.x, wc.y);
                _small.SetBloom(0f);

                if (Mini)
                {
                    _hand0 = new Vector2(34f, -24f);
                    _hand1 = new Vector2(58f, -24f);
                    _handBox = Panel(root, "h0", 22f, 18f, MulA(P.Panel, 0.95f), P.Selected, 4f, 1.8f);
                    At(_handBox, _hand0);
                    At(Panel(root, "h1", 22f, 18f, MulA(P.Panel, 0.95f), P.Border, 4f, 1.4f), _hand1);
                    _rmb = Mouse.Make(root, "rmb", P, 24f);
                    _rmb.SetPos(52f, 18f);
                }
                else
                {
                    _hands = HandPair.Make(root, "hands", P, 60f, 40f, 10f, true, 6.5f);
                    _hands.Root.anchoredPosition = new Vector2(140f, -62f);
                    _hand0 = _hands.SceneCenter(0);
                    _hand1 = _hands.SceneCenter(1);
                    _rmb = Mouse.Make(root, "rmb", P, 44f);
                    _rmb.SetPos(140f, 40f);
                    Label(root, "RIGHT-CLICK", 8.5f, P.TextDim, 140f, 6f, 110f);
                    _caps = new[]
                    {
                        "CLICK - IT GOES TO YOUR HAND",
                        "RIGHT-CLICK - ITS OWN WHEEL",
                        "DRAG - ANYWHERE: A BAG, A HAND, A LOCKER, THE FLOOR"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
                _handItem0 = Panel(root, "hi0", L(16f, 8f), L(16f, 8f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem0, _hand0);
                _handItem1 = Panel(root, "hi1", L(16f, 8f), L(16f, 8f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem1, _hand1);
                _fly = Flyer.Make(root, "fly", L(16f, 8f), P);
                _cur = Cursor.Make(root, "cursor", P, L(16f, 10f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                Vector2 c1 = CellAt(_win, _cells, _tookCell);
                Vector2 c2 = CellAt(_win, _cells, _wheelCell);
                Vector2 c3 = CellAt(_win, _cells, _dragCell);
                Vector2 at = Vector2.Lerp(c1 + new Vector2(L(40f, 16f), -L(50f, 20f)), c1, Ease(u, 0.4f, 1.0f));
                at = Vector2.Lerp(at, c2, Ease(u, 2.4f, 3.0f));
                at = Vector2.Lerp(at, c3, Ease(u, 5.3f, 5.9f));
                at = Vector2.Lerp(at, _hand1, Ease(u, 6.1f, 7.1f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.25f, 0.45f), 1f - Ease(u, 7.6f, 7.9f)));
                float rmb = Bump(u, 3.1f, 3.4f);
                _cur.SetClick(Mathf.Max(Mathf.Max(Bump(u, 1.15f, 1.4f), rmb), Held(u, 5.95f, 7.25f)));
                _rmb.SetButton(1, rmb);

                // Click: the item hops to the active hand.
                bool hop = u >= 1.35f && u < 1.95f;
                bool drag = u >= 6.0f && u < 7.25f;
                if (hop) _fly.Set(Arc(c1, _hand0, Seg(u, 1.35f, 1.95f), L(30f, 12f)), 1f);
                else if (drag) _fly.Set(at + new Vector2(L(9f, 5f), -L(9f, 5f)), 1f);
                else _fly.Hide();
                _cells.Occupy(_tookCell, !(u >= 1.35f && u < 8.6f));
                _cells.Occupy(_dragCell, !(u >= 6.0f && u < 8.6f));
                bool h0 = u >= 1.9f && u < 8.6f, h1 = u >= 7.25f && u < 8.6f;
                if (_handItem0.gameObject.activeSelf != h0) _handItem0.gameObject.SetActive(h0);
                if (_handItem1.gameObject.activeSelf != h1) _handItem1.gameObject.SetActive(h1);

                // Right-click: the item's own wheel blooms over its cell.
                _small.SetBloom(Mathf.Min(Ease(u, 3.3f, 3.7f), 1f - Ease(u, 4.8f, 5.1f)));

                if (_hands != null)
                {
                    int st = (h0 ? 1 : 0) + (h1 ? 2 : 0);
                    if (st != _stateIdx)
                    {
                        _stateIdx = st;
                        _hands.State[0].text = h0 ? "Crowbar" : "empty";
                        _hands.State[1].text = h1 ? "Canister" : "empty";
                    }
                    int cap = u < 2.2f ? 0 : u < 5.2f ? 1 : 2;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _win = null; _cells = null; _small = null; _cur = null; _hands = null; }
        }

        // =====================================================================================
        //  shiftdrag (12.4, 18.5) — hold Shift and drag one cable: every item of that type lifts
        //  and flies with it; the others stay put.
        // =====================================================================================

        private sealed class ShiftDragScene : Scene
        {
            internal const float Loop = 8.2f;
            private RectTransform _win;
            private Cells _src, _dst;
            private CanvasGroup[] _fly;
            private int[] _from, _to;
            private KeyVis _shift;
            private Cursor _cur;
            private TextMeshProUGUI _cap;

            protected override void Make(RectTransform root)
            {
                PanelGraphic panel;
                float w = L(380f, 146f), h = L(150f, 56f);
                _win = WindowMock(root, "win", w, h, P, "UNIVERSAL INVENTORY", !Mini, out panel);
                _win.anchoredPosition = new Vector2(0f, L(10f, 8f));
                float cw = L(36f, 14f), ch = L(26f, 11f), gap = L(6f, 3f);
                int cols = Mini ? 3 : 4, rows = Mini ? 2 : 3;
                TriangleGraphic c1, c2;
                FolderTab(_win, "bag", new Vector2(-w * 0.25f, h * 0.5f - L(30f, 11f)), L(110f, 40f), L(18f, 7f), P, "BACKPACK", !Mini, out c1);
                FolderTab(_win, "jet", new Vector2(w * 0.25f, h * 0.5f - L(30f, 11f)), L(110f, 40f), L(18f, 7f), P, "JETPACK", !Mini, out c2);
                c1.transform.localEulerAngles = Vector3.zero;
                c2.transform.localEulerAngles = Vector3.zero;
                _src = Cells.Make(_win, "src", cols, rows, cw, ch, gap, P);
                _src.SetPos(-w * 0.25f, -L(14f, 5f));
                _dst = Cells.Make(_win, "dst", cols, rows, cw, ch, gap, P);
                _dst.SetPos(w * 0.25f, -L(14f, 5f));
                // Three cables (accent) among other items (dim) that must stay put.
                _from = Mini ? new[] { 0, 2, 4 } : new[] { 1, 6, 8 };
                _to = Mini ? new[] { 0, 1, 2 } : new[] { 0, 1, 2 };
                for (int i = 0; i < _src.Cell.Length; i++)
                {
                    bool cable = System.Array.IndexOf(_from, i) >= 0;
                    bool other = !cable && (i % 3 != 2 || Mini);
                    if (!cable && other)
                    {
                        _src.Occupy(i, true);
                        _src.Item[i].color = MulA(P.TextDim, 0.55f);
                    }
                }
                for (int i = 0; i < _dst.Cell.Length; i++) _dst.Occupy(i, !Mini && i > 7);
                _fly = new CanvasGroup[3];
                float s = Mathf.Min(cw, ch) * 0.56f;
                for (int i = 0; i < 3; i++)
                {
                    _fly[i] = GroupNode(root, "cable" + i);
                    Panel((RectTransform)_fly[i].transform, "c", s, s, MulA(P.Accent, 0.8f), P.Accent, 2.5f, 1.1f);
                }
                // The modifier is vanilla's own rebindable "move all of this type" (default LeftShift),
                // which the mod's move-all-of-type check reads - so the LIVE glyph, never a literal Shift.
                string mod = K("{V:MoveAllOfType}");
                _shift = KeyVis.Make(root, "shift", mod, P, L(26f, 14f), L(12f, 8f), L(40f, 22f));
                _shift.SetPos(L(-150f, 0f), L(-88f, -28f));
                if (!Mini)
                {
                    _cap = Label(root, "HOLD " + mod + " AND DRAG ONE - EVERY ITEM OF THAT TYPE COMES ALONG", 10f, P.TextDim, 0f, 100f, 430f);
                    Label(root, "same bag, same type - the other items stay put", 8.5f, P.TextMute, 60f, -88f, 240f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 10f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                _shift.SetPressed(Held(u, 0.6f, 3.3f));
                Vector2 lead0 = _win.anchoredPosition + _src.Root.anchoredPosition + _src.PosOf(_from[0]);
                Vector2 dest0 = _win.anchoredPosition + _dst.Root.anchoredPosition + _dst.PosOf(_to[0]);
                float lift = Mathf.Clamp01(Ease(u, 1.2f, 1.4f));
                float alpha = Mathf.Min(Ease(u, 0f, 0.3f), 1f - Ease(u, 6.9f, 7.5f));
                Vector2 cur0 = lead0;
                for (int i = 0; i < 3; i++)
                {
                    Vector2 a = _win.anchoredPosition + _src.Root.anchoredPosition + _src.PosOf(_from[i]);
                    Vector2 b = _win.anchoredPosition + _dst.Root.anchoredPosition + _dst.PosOf(_to[i]);
                    float k = Seg(u, 1.5f + i * 0.08f, 2.7f + i * 0.08f);
                    Vector2 p = Arc(a, b, k, L(20f, 8f));
                    ((RectTransform)_fly[i].transform).anchoredPosition = p;
                    float pop = 1f + 0.18f * lift * (1f - Seg(u, 2.8f, 3.1f));
                    _fly[i].transform.localScale = new Vector3(pop, pop, 1f);
                    _fly[i].alpha = alpha;
                    if (i == 0) cur0 = p;
                    _dst.Cell[_to[i]].BorderColor = Color.Lerp(_dst.Border, P.Accent, Bump(u, 2.7f + i * 0.08f, 3.3f + i * 0.08f));
                }
                Vector2 at = Vector2.Lerp(lead0 + new Vector2(L(30f, 12f), -L(40f, 14f)), lead0, Ease(u, 0.3f, 0.9f));
                if (u >= 1.1f) at = cur0;
                at += new Vector2(-L(8f, 4f), L(8f, 4f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.2f, 0.4f), 1f - Ease(u, 3.4f, 3.7f)));
                _cur.SetClick(Held(u, 1.1f, 2.95f));
            }

            public override void Destroy() { _win = null; _src = null; _dst = null; _fly = null; _cur = null; }
        }

        // =====================================================================================
        //  pintear (12.5, legacy pinflow) — drag a folder tab OUT: that bag gets its own window; the
        //  grid key leaves it up; its X folds it back. (The stale click-a-HUD-box-to-pin beat is
        //  gone: since 0.9.7.4 that opens the bag INSIDE the big window.)
        // =====================================================================================

        private sealed class PinTearScene : Scene
        {
            internal const float Loop = 9.6f;
            private CanvasGroup _mainGroup, _tabGroup, _pinGroup;
            private RectTransform _main, _tabRt, _pinRt, _pinX;
            private PanelGraphic _tab;
            private KeyVis _b;
            private Ripple _rip;
            private Cursor _cur;
            private Vector2 _mainHome, _tabHome, _pinAt, _xAt;
            private Color _tabFill, _tabHot;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            protected override void Make(RectTransform root)
            {
                string b = K("{UIA_Grid}");
                _mainHome = new Vector2(L(-124f, -44f), L(8f, 2f));
                _mainGroup = GroupNode(root, "mainG");
                _main = (RectTransform)_mainGroup.transform;
                _main.anchoredPosition = _mainHome;
                PanelGraphic panel;
                float mw = L(154f, 56f), mh = L(130f, 52f);
                WindowMock(_main, "mock", mw, mh, P, "UNIVERSAL INVENTORY", !Mini, out panel);
                var cells = Cells.Make(_main, "cells", 3, 2, L(42f, 14f), L(24f, 10f), L(5f, 3f), P);
                cells.SetPos(0f, L(-24f, -10f));
                for (int i = 0; i < 6; i++) cells.Occupy(i, i != 2);

                _tabHome = _mainHome + new Vector2(-mw * 0.5f + L(46f, 18f), L(30f, 10f));
                _tabGroup = GroupNode(root, "tabG");
                _tabRt = (RectTransform)_tabGroup.transform;
                _tabFill = MulA(P.Warn, 0.32f);
                _tabHot = MulA(P.Warn, 0.62f);
                TriangleGraphic chev;
                _tab = FolderTab(_tabRt, "tab", Vector2.zero, L(76f, 30f), L(18f, 8f), P, "BACKPACK", !Mini, out chev);

                _pinAt = new Vector2(L(40f, 34f), L(14f, 6f));
                _pinGroup = GroupNode(root, "pin");
                _pinRt = (RectTransform)_pinGroup.transform;
                PanelGraphic pinPanel;
                float pw = L(110f, 50f), ph = L(84f, 40f);
                WindowMock(_pinRt, "mock", pw, ph, P, "BACKPACK", !Mini, out pinPanel);
                pinPanel.BorderColor = P.Accent;
                var pc = Cells.Make(_pinRt, "pc", 2, 2, L(40f, 16f), L(22f, 9f), L(5f, 3f), P);
                pc.SetPos(0f, L(-10f, -5f));
                for (int i = 0; i < 4; i++) pc.Occupy(i, i != 3);
                _xAt = new Vector2(pw * 0.5f - L(10f, 5f), ph * 0.5f - L(8f, 3f));
                _pinX = XGlyph(_pinRt, "x", L(8f, 5f), P.TextDim);
                _pinX.anchoredPosition = _xAt;
                _pinGroup.alpha = 0f;

                if (!Mini)
                {
                    _b = KeyVis.Make(root, "b", b, P, 28f, 13f, 40f);
                    _b.SetPos(164f, -72f);
                    _rip = Ripple.Make(root, "rip", P.Accent, 15f, 30f, 2f);
                    _rip.SetPos(_b.PressPoint);
                    _caps = new[]
                    {
                        "DRAG A FOLDER TAB OUT - THAT BAG GETS ITS OWN WINDOW",
                        b + " LEAVES IT UP",
                        "ITS X FOLDS IT BACK"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
                _cur = Cursor.Make(root, "cursor", P, L(16f, 10f));
                _cur.SetAlpha(0f);
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                // Tear-out: the tab rides the pointer out, then dissolves into the new window.
                float tear = Ease(u, 1.2f, 2.1f);
                float fold = Ease(u, 6.2f, 6.8f);
                Vector2 pinPos = Vector2.Lerp(Vector2.Lerp(_tabHome, _pinAt, Ease(u, 1.9f, 2.4f)), _tabHome, fold);
                _tabRt.anchoredPosition = Vector2.Lerp(_tabHome, _pinAt, Mathf.Clamp01(tear - fold));
                _tabGroup.alpha = 1f - Mathf.Clamp01(Seg(u, 1.95f, 2.2f) - Seg(u, 6.6f, 6.9f));
                _tab.color = Color.Lerp(_tabFill, _tabHot, Mathf.Min(Seg(u, 0.95f, 1.15f), 1f - Seg(u, 2.0f, 2.2f)));
                float pinOn = Mathf.Min(Ease(u, 1.95f, 2.45f), 1f - Ease(u, 6.2f, 6.8f));
                _pinGroup.alpha = pinOn;
                float ps = Mathf.Lerp(0.35f, 1f, pinOn);
                _pinRt.localScale = new Vector3(ps, ps, 1f);
                _pinRt.anchoredPosition = pinPos;
                _pinX.localScale = Vector3.one * (1f + 0.35f * Bump(u, 5.9f, 6.3f));

                // The grid key: the big window goes, the pinned one stays; again: back.
                float away = Mini ? 0f : Mathf.Clamp01(Ease(u, 3.1f, 3.5f) - Ease(u, 4.5f, 4.9f));
                _mainGroup.alpha = 1f - away;
                _main.anchoredPosition = _mainHome + new Vector2(0f, -L(40f, 0f) * away);
                if (_b != null)
                {
                    _b.SetPressed(Mathf.Max(Bump(u, 3.0f, 3.25f), Bump(u, 4.4f, 4.65f)));
                    float r1 = Seg(u, 3.1f, 3.7f), r2 = Seg(u, 4.5f, 5.1f);
                    _rip.Play(r1 > 0f && r1 < 1f ? r1 : r2);
                }

                Vector2 at = Vector2.Lerp(_tabHome + new Vector2(-L(20f, 8f), -L(50f, 20f)), _tabHome, Ease(u, 0.45f, 1.1f));
                at = Vector2.Lerp(at, _pinAt, tear);
                at = Vector2.Lerp(at, _pinAt + new Vector2(L(30f, 10f), -L(40f, 12f)), Ease(u, 2.4f, 3.0f));
                at = Vector2.Lerp(at, _pinAt + _xAt, Ease(u, 5.2f, 5.8f));
                _cur.SetPos(at);
                _cur.SetAlpha(Mathf.Min(Ease(u, 0.3f, 0.5f), 1f - Ease(u, 6.4f, 6.7f)));
                _cur.SetClick(Mathf.Max(Held(u, 1.1f, 2.2f), Bump(u, 5.95f, 6.25f)));

                if (_cap != null)
                {
                    int cap = u < 2.9f ? 0 : u < 5.0f ? 1 : 2;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _main = null; _tabRt = null; _pinRt = null; _pinX = null; _cur = null; _b = null; }
        }

        // =====================================================================================
        //  scrollselect (12.6) — mouse not free: the scroll wheel walks a highlight through your
        //  bags; the select key takes or places; the tool key opens a tool's parts.
        // =====================================================================================

        private sealed class ScrollSelectScene : Scene
        {
            internal const float Loop = 9f;
            private RectTransform _win;
            private Cells _cells;
            private PanelGraphic _hilite, _handItem;
            private RectTransform _tablet;
            private Mouse _mouse;
            private KeyVis _f, _r;
            private Ripple _ripF, _ripR;
            private Flyer _fly;
            private CanvasGroup _pop;
            private Vector2 _handAt;
            private int[] _steps;
            private int _tabletCell, _takeCell;
            private TextMeshProUGUI _cap;
            private int _capIdx = -1;
            private string[] _caps;

            protected override void Make(RectTransform root)
            {
                string f = K("{V:InventorySelect}"), r = K("{UIA_ToolRadial}");
                PanelGraphic panel;
                _win = WindowMock(root, "win", L(196f, 70f), L(150f, 54f), P, "UNIVERSAL INVENTORY", !Mini, out panel);
                _win.anchoredPosition = new Vector2(L(-80f, -38f), L(8f, 4f));
                _cells = Cells.Make(_win, "cells", Mini ? 3 : 4, Mini ? 2 : 3, L(40f, 18f), L(28f, 14f), L(6f, 3f), P);
                _cells.SetPos(0f, L(-12f, -5f));
                _takeCell = Mini ? 1 : 2;
                _tabletCell = Mini ? 5 : 9;
                _steps = Mini ? new[] { 0, 1, 1, 2, 5 } : new[] { 0, 1, 2, 2, 5, 9 };
                for (int i = 0; i < _cells.Cell.Length; i++) _cells.Occupy(i, i != _tabletCell && i != (Mini ? 3 : 11));
                _tablet = TabletIcon(_win, "tablet", L(22f, 10f), P);
                _tablet.anchoredPosition = _cells.Root.anchoredPosition + _cells.PosOf(_tabletCell);
                _hilite = Panel(_win, "hi", L(44f, 21f), L(32f, 17f), Color.clear, P.Accent, 4f, L(2.2f, 1.5f));

                _mouse = Mouse.Make(root, "mouse", P, L(56f, 26f));
                _mouse.SetPos(L(120f, 24f), L(44f, 18f));
                if (!Mini)
                    Crosshair(root, "aim", 14f, 1.8f, P.Accent).anchoredPosition = new Vector2(160f, 60f);
                _f = KeyVis.Make(root, "f", f, P, L(28f, 16f), L(13f, 9f), L(40f, 22f));
                _f.SetPos(L(100f, 50f), L(-26f, 18f));
                _r = KeyVis.Make(root, "r", r, P, L(28f, 16f), L(13f, 9f), L(40f, 22f));
                _r.SetPos(L(160f, 50f), L(-26f, -2f));
                _ripF = Ripple.Make(root, "ripF", P.Accent, L(15f, 8f), L(30f, 15f), L(2f, 1.4f));
                _ripF.SetPos(_f.PressPoint);
                _ripR = Ripple.Make(root, "ripR", P.Accent, L(15f, 8f), L(30f, 15f), L(2f, 1.4f));
                _ripR.SetPos(_r.PressPoint);

                _handAt = new Vector2(L(130f, 50f), L(-78f, -24f));
                At(Panel(root, "hand", L(58f, 26f), L(34f, 16f), MulA(P.Panel, 0.95f), P.Selected, 5f, 2f), _handAt);
                _handItem = Panel(root, "handItem", L(16f, 8f), L(16f, 8f), MulA(P.Accent, 0.75f), P.Accent, 3f, 1.1f);
                At(_handItem, _handAt);
                _handItem.gameObject.SetActive(false);
                _fly = Flyer.Make(root, "fly", L(16f, 8f), P);

                _pop = GroupNode(root, "pop");
                var popRt = (RectTransform)_pop.transform;
                popRt.anchoredPosition = CellAt(_win, _cells, _tabletCell) + new Vector2(L(56f, 18f), L(20f, 10f));
                PanelGraphic pp;
                WindowMock(popRt, "mock", L(78f, 30f), L(50f, 22f), P, "TABLET", !Mini, out pp);
                pp.BorderColor = P.Accent;
                for (int i = 0; i < 2; i++)
                    At(Panel(popRt, "s" + i, L(22f, 8f), L(22f, 8f), MulA(P.Raised, 0.85f), MulA(P.Border, 0.7f), 3f, 1.1f),
                        (i == 0 ? -1f : 1f) * L(15f, 6f), -L(5f, 2f));
                _pop.alpha = 0f;
                if (!Mini)
                {
                    Label(root, "YOUR HAND", 7f, P.TextDim, _handAt.x, _handAt.y + 24f, 90f);
                    _caps = new[]
                    {
                        "MOUSE NOT FREE? SCROLL MOVES A HIGHLIGHT",
                        f + " TAKES OR PLACES",
                        r + " OPENS A TOOL"
                    };
                    _cap = Label(root, _caps[0], 10f, P.TextDim, 0f, 100f, 430f);
                }
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                // Steps 0..: walk to the item F takes, then on to the tablet R opens.
                float walk1 = Seg(u, 0.4f, 2.0f), walk2 = Seg(u, 3.5f, 4.6f);
                int half = Mini ? 2 : 3;
                int si = u < 3.5f ? Mathf.Clamp((int)(walk1 * (half - 0.001f)), 0, half - 1)
                    : half + Mathf.Clamp((int)(walk2 * (_steps.Length - half - 0.001f)), 0, _steps.Length - half - 1);
                if (u < 3.5f && walk1 >= 1f) si = half - 1;
                int cell = u < 3.5f ? (walk1 >= 1f ? _takeCell : _steps[si]) : _steps[si];
                At(_hilite, _cells.Root.anchoredPosition + _cells.PosOf(cell));
                _hilite.BorderColor = WithA(P.Accent, 0.65f + 0.35f * Mathf.Sin(u * 6f));
                float scrolling = Mathf.Max(Window01(u, 0.35f, 2.05f, 0.1f), Window01(u, 3.45f, 4.65f, 0.1f));
                _mouse.Scroll(walk1 * 3f + walk2 * 3f, scrolling);

                _f.SetPressed(Bump(u, 2.4f, 2.65f));
                _ripF.Play(Seg(u, 2.5f, 3.1f));
                bool flying = u >= 2.6f && u < 3.2f;
                Vector2 from = CellAt(_win, _cells, _takeCell);
                if (flying) _fly.Set(Arc(from, _handAt, Seg(u, 2.6f, 3.2f), L(30f, 12f)), 1f);
                else _fly.Hide();
                _cells.Occupy(_takeCell, u < 2.6f || u >= 8.6f);
                bool inHand = u >= 3.15f && u < 8.6f;
                if (_handItem.gameObject.activeSelf != inHand) _handItem.gameObject.SetActive(inHand);

                _r.SetPressed(Bump(u, 5.0f, 5.25f));
                _ripR.Play(Seg(u, 5.1f, 5.7f));
                float pop = Mathf.Min(Ease(u, 5.15f, 5.55f), 1f - Ease(u, 7.3f, 7.7f));
                _pop.alpha = pop;
                float ps = Mathf.Lerp(0.5f, 1f, pop);
                _pop.transform.localScale = new Vector3(ps, ps, 1f);

                if (_cap != null)
                {
                    int cap = u < 2.3f ? 0 : u < 4.8f ? 1 : 2;
                    if (cap != _capIdx) { _capIdx = cap; _cap.text = _caps[cap]; }
                }
            }

            public override void Destroy() { _win = null; _cells = null; _mouse = null; _f = null; _r = null; }
        }

        // =====================================================================================
        //  baresenses (15.2) — an empty visor; COLD fades in bright and settles dim; THIRSTY joins;
        //  both fade away. No words means you're fine. (The words ARE the subject, so the mini
        //  layout keeps them — large.)
        // =====================================================================================

        private sealed class BareSensesScene : Scene
        {
            internal const float Loop = 8.6f;
            private TextMeshProUGUI _cold, _thirsty;
            private CanvasGroup _coldG, _thirstyG;
            private TextMeshProUGUI _fine;
            private Color _word;

            protected override void Make(RectTransform root)
            {
                // Rule-8 content exemption (deliberate): the fixed near-white of the bare visor's
                // status words (COLD / THIRSTY) depicts the in-world readout this scene is about -
                // demo content, not a HUD knob (no theme travel, no per-tier fork, nothing to migrate).
                _word = new Color(0.86f, 0.90f, 0.95f, 1f);
                Panel(root, "visor", L(400f, 144f), L(190f, 70f), MulA(P.Window, 0.35f), MulA(P.Border, 0.55f), L(28f, 12f), 1.4f);
                // The bare HUD keeps only the two hands at the bottom.
                for (int i = 0; i < 2; i++)
                    At(Panel(root, "hand" + i, L(64f, 26f), L(30f, 12f), MulA(P.Panel, 0.8f), MulA(P.Border, 0.6f), 5f, 1.2f),
                        (i == 0 ? -1f : 1f) * L(38f, 16f), L(-70f, -26f));
                _coldG = GroupNode(root, "cold");
                _cold = Label((RectTransform)_coldG.transform, "COLD", L(22f, 13f), _word, 0f, L(34f, 13f), L(200f, 80f));
                _cold.fontStyle = FontStyles.Bold;
                _cold.characterSpacing = 8f;
                _thirstyG = GroupNode(root, "thirsty");
                _thirsty = Label((RectTransform)_thirstyG.transform, "THIRSTY", L(22f, 13f), _word, 0f, L(2f, -4f), L(220f, 90f));
                _thirsty.fontStyle = FontStyles.Bold;
                _thirsty.characterSpacing = 8f;
                _coldG.alpha = 0f;
                _thirstyG.alpha = 0f;
                if (!Mini)
                {
                    _fine = Label(root, "NO WORDS MEANS YOU'RE FINE", 10f, P.TextDim, 0f, -30f, 300f);
                    Label(root, "WORDS APPEAR WHEN SOMETHING IS WRONG - AND FADE WHEN IT PASSES", 9.5f, P.TextDim, 0f, 100f, 430f);
                }
            }

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                // Flare on change, then settle dim; both pass.
                _coldG.alpha = Mathf.Lerp(0f, 1f, Ease(u, 0.6f, 0.9f)) * Mathf.Lerp(1f, 0.45f, Ease(u, 1.2f, 2.2f))
                    * (1f - Ease(u, 5.8f, 6.5f));
                Scale(_cold, 1f + 0.14f * Bump(u, 0.6f, 1.3f));
                _thirstyG.alpha = Mathf.Lerp(0f, 1f, Ease(u, 2.8f, 3.1f)) * Mathf.Lerp(1f, 0.45f, Ease(u, 3.4f, 4.4f))
                    * (1f - Ease(u, 6.0f, 6.7f));
                Scale(_thirsty, 1f + 0.14f * Bump(u, 2.8f, 3.5f));
                if (_fine != null) _fine.color = WithA(P.TextDim, Window01(u, 6.9f, 8.4f, 0.3f));
            }

            public override void Destroy() { _cold = null; _thirsty = null; }
        }

        // =====================================================================================
        //  lowpower (14.6) — the suit battery reads 9%; a HUD box flickers and drops out; the cue:
        //  tap the suit's gear key, slide out on the battery, REPLACE.
        // =====================================================================================

        private sealed class LowPowerScene : Scene
        {
            internal const float Loop = 8f;
            private CanvasGroup[] _box;
            private PanelGraphic _battFill;
            private TextMeshProUGUI _pct;
            private KeyVis _three;
            private Ripple _rip;
            private TextMeshProUGUI _cue;
            private CanvasGroup _visor;

            private static readonly string[] Reads = { "PRESSURE 101 kPa", "TEMP 21 C", "OXYGEN 98%" };

            protected override void Make(RectTransform root)
            {
                string three = K("{V:SuitSlot}");
                _visor = GroupNode(root, "visor");
                var vr = (RectTransform)_visor.transform;
                Panel(vr, "frame", L(400f, 144f), L(190f, 70f), MulA(P.Window, 0.35f), MulA(P.Border, 0.55f), L(28f, 12f), 1.4f);
                _box = new CanvasGroup[3];
                for (int i = 0; i < 3; i++)
                {
                    _box[i] = GroupNode(vr, "read" + i);
                    var br = (RectTransform)_box[i].transform;
                    br.anchoredPosition = new Vector2((i - 1) * L(122f, 44f), L(58f, 22f));
                    Panel(br, "b", L(112f, 38f), L(28f, 11f), MulA(P.Panel, 0.9f), P.Border, 5f, 1.3f);
                    if (!Mini) Label(br, Reads[i], 8.5f, P.Text, 0f, 0f, 108f);
                    else Panel(br, "v", 20f, 2.6f, MulA(P.Text, 0.85f), Color.clear, 1.3f, 0f);
                }
                // The suit battery: a drawn cell with a 9% fill, and its number in the critical colour.
                Vector2 bat = new Vector2(L(-24f, -16f), L(-10f, -4f));
                float bw = L(56f, 30f), bh = L(24f, 13f);
                Panel(vr, "cell", bw, bh, Color.clear, P.Crit, 4f, 1.6f).rectTransform.anchoredPosition = bat;
                Panel(vr, "nub", L(5f, 3f), bh * 0.45f, P.Crit, Color.clear, 1.5f, 0f).rectTransform.anchoredPosition =
                    bat + new Vector2(bw * 0.5f + L(3f, 2f), 0f);
                float fw = Mathf.Max(3f, (bw - 6f) * 0.09f);
                _battFill = Panel(vr, "fill", fw, bh - L(8f, 5f), P.Crit, Color.clear, 1.5f, 0f);
                At(_battFill, bat + new Vector2(-bw * 0.5f + 3f + fw * 0.5f, 0f));
                _pct = Label(vr, "9%", L(16f, 11f), P.Crit, bat.x + bw * 0.5f + L(30f, 16f), bat.y, L(60f, 30f));
                _pct.fontStyle = FontStyles.Bold;
                if (!Mini) Label(vr, "SUIT BATTERY", 8f, P.TextDim, bat.x, bat.y + 22f, 100f);

                _three = KeyVis.Make(root, "three", three, P, L(28f, 16f), L(13f, 9f), L(40f, 22f));
                _three.SetPos(L(-24f, -56f), L(-66f, -24f));
                _rip = Ripple.Make(root, "rip", P.Accent, L(15f, 8f), L(30f, 15f), L(2f, 1.4f));
                _rip.SetPos(_three.PressPoint);
                if (!Mini)
                {
                    _cue = Label(root, "TAP " + three + ", SLIDE OUT ON THE BATTERY, REPLACE", 9.5f, P.TextDim, 40f, -94f, 360f);
                    Label(root, "WHEN THE SUIT BATTERY RUNS LOW, THE VISOR FLICKERS", 10f, P.TextDim, 0f, 100f, 430f);
                }
            }

            /// <summary>A deterministic on/off stutter (no RNG, no allocation).</summary>
            private static float Stutter(float u)
                => Mathf.Sin(u * 53f) + Mathf.Sin(u * 31f + 1.3f) + Mathf.Sin(u * 17f + 0.7f) > 0.35f ? 1f : 0.12f;

            public override void Tick(float t)
            {
                float u = Mathf.Repeat(t, Loop);
                // The TEMP box stutters, drops out, stutters back.
                float flick = u >= 1.5f && u < 3.0f ? Stutter(u) : u >= 3.0f && u < 4.8f ? 0f : u >= 4.8f && u < 5.3f ? Stutter(u) : 1f;
                _box[1].alpha = flick;
                _box[0].alpha = u >= 2.0f && u < 2.6f ? Mathf.Lerp(1f, Stutter(u + 0.4f), 0.6f) : 1f;
                _visor.alpha = u >= 1.5f && u < 5.3f ? 0.9f + 0.1f * Stutter(u * 0.7f) : 1f;
                float blink = 0.55f + 0.45f * Mathf.Sin(u * 5f);
                _battFill.color = WithA(P.Crit, blink);
                _pct.color = WithA(P.Crit, 0.6f + 0.4f * blink);
                float cue = Window01(u, 5.5f, 7.7f, 0.25f);
                _three.SetAlpha(Mini ? 1f : 0.3f + 0.7f * cue);
                _three.SetPressed(Mathf.Max(Bump(u, 6.0f, 6.3f), Bump(u, 6.9f, 7.2f)));
                float r1 = Seg(u, 6.1f, 6.7f), r2 = Seg(u, 7.0f, 7.6f);
                _rip.Play(r1 > 0f && r1 < 1f ? r1 : r2);
                if (_cue != null) _cue.color = WithA(P.TextDim, cue);
            }

            public override void Destroy() { _box = null; _three = null; }
        }
    }
}
