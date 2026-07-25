using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameStatus = Assets.Scripts.UI.StatusUpdates;
using GameStatusItem = Assets.Scripts.UI.StatusUpdate;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// BORROWS vanilla's own moodlet strip (<c>StatusUpdates.StatusTransform</c>, the scene
    /// object "StatusIcons") into our bar — 1-to-1 vanilla behaviour, just moved and
    /// re-oriented. Rather than re-implement moodlet rendering (icons, dedupe, flashing) we
    /// reparent the REAL strip so the game keeps driving it exactly as always.
    ///
    /// FIRST PRINCIPLES: vanilla lays the strip out with a UGUI <see cref="GridLayoutGroup"/>
    /// — 192×192 cells, StartAxis=Vertical, FixedColumnCount=1 = a giant single-column stack.
    /// So we don't add a second layout group (that just fights the grid, which is why the
    /// strip stayed a huge vertical column). We RE-CONFIGURE vanilla's own grid in place:
    /// one horizontal ROW, centred, and we scale the whole strip down so the 192px cells read
    /// at a sane size while every cell renders EXACTLY as vanilla draws it. Everything is
    /// cached and restored on release, so vanilla gets its strip back untouched.
    ///
    /// Vanilla still toggles the strip's activeSelf and fills its cells every frame, so all
    /// of that keeps working after the reparent. <see cref="RestoreMoodlets"/> hands it back
    /// (parent, sibling index, geometry, grid settings) on teardown/tier-drop — it MUST run
    /// before our Root is destroyed (HudSystem calls it from RestoreAnyPortraits, which runs
    /// before every destroy loop) or the strip would die with our Root.
    /// </summary>
    internal sealed class MoodletBorrowWidget : HudElementView
    {
        private RectTransform _holder;
        private CanvasGroup _holderGroup; // drives the F9 "Moodlet transparency" fade

        private bool _borrowed;
        // Only ONE widget may hold the single vanilla strip at a time (see TryBorrow) — a
        // duplicate would fight over the reparent and crash vanilla's StatusUpdates.
        private static MoodletBorrowWidget _stripOwner;
        private RectTransform _stripRt;
        private Transform _origParent;
        private int _origSiblingIndex;
        private Vector3 _origLocalPos;
        private Vector2 _origAnchoredPos, _origSizeDelta, _origAnchorMin, _origAnchorMax, _origPivot;
        private Vector3 _origScale;

        // Vanilla's GridLayoutGroup, captured so restore puts its column back exactly.
        private GridLayoutGroup _grid;
        private GridLayoutGroup.Axis _gStartAxis;
        private GridLayoutGroup.Constraint _gConstraint;
        private int _gConstraintCount;
        private Vector2 _gSpacing, _gCellSize;
        private TextAnchor _gChildAlign;

        // --- words mode (#1): render active moodlets as ALL-CAPS words instead of the borrowed
        // icon strip. The strip stays BORROWED (captured, so it never reappears in vanilla's own
        // spot) but hidden; we enumerate the SAME active set vanilla shows and draw words with full
        // size / separation / columns / colour control (like the bare-senses box). ---
        private const int WordPool = 16;
        private const int WGroupCap = 24;
        private TextMeshProUGUI[] _words;                 // lazily created on first words-mode use
        private readonly GameStatusItem[] _wGrpWin = new GameStatusItem[WGroupCap];
        private readonly int[] _wGrpKey = new int[WGroupCap];
        private readonly int[] _wGrpPri = new int[WGroupCap];
        private readonly int[] _wGrpLvl = new int[WGroupCap];
        private readonly string[] _wordStr = new string[WordPool];
        private readonly int[] _wordLvl = new int[WordPool];
        private int _wordCount;
        private int _wordSig = int.MinValue;
        private bool _wordsBuilt;

        protected override void BuildContent(RectTransform root)
        {
            var holderGo = new GameObject("MoodletHolder", typeof(RectTransform));
            holderGo.transform.SetParent(root, false);
            _holder = (RectTransform)holderGo.transform;
            _holder.anchorMin = _holder.anchorMax = new Vector2(0.5f, 0.5f);

            // Opt this subtree back INTO raycasts (the HUD root CanvasGroup blocks them) so
            // vanilla's moodlet hover tooltip still fires after we reparent the strip here.
            var grp = holderGo.AddComponent<CanvasGroup>();
            grp.ignoreParentGroups = true;
            grp.interactable = true;
            grp.blocksRaycasts = true;
            _holderGroup = grp;
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;
            _holder.anchoredPosition = CenterFor(scale);
            _holder.sizeDelta = SizeFor(scale);
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            bool words = Def.GetBFor(LayoutBare, "words", false);
            try
            {
                // Keep the vanilla strip BORROWED either way, so it never reappears in its own
                // default screen location. In words mode we HIDE the borrowed strip and render the
                // same active moodlets as words in its place; otherwise we show it as before.
                if (!_borrowed) TryBorrow();
                if (words)
                {
                    HideBorrowedStrip();
                    EnsureWordPool();
                    UpdateWords(scale);
                }
                else
                {
                    if (_wordsBuilt) HideWords();
                    if (_borrowed) Apply(scale);
                }
            }
            catch { }
        }

        private void TryBorrow()
        {
            // Only a WANTED widget borrows. Without this, a suit-mode copy that is fading OUT
            // re-borrows every frame during its ~0.4s fade, so the incoming bare-mode copy never
            // gets the strip and it snaps back to vanilla ("moodlets return to the right side when
            // I hit bare mode"). Fader.Target is the panel's visible/hidden goal.
            if (Fader != null && !Fader.Target) return;

            var su = Assets.Scripts.UI.StatusUpdates.Instance;
            var strip = su != null ? su.StatusTransform : null;
            var rt = strip as RectTransform;
            if (rt == null) return;

            // One owner at a time (two copies reparenting the SAME vanilla strip crashed
            // StatusUpdates.ManagerUpdate). A wanted widget may take the strip from an owner that
            // is itself fading OUT (not wanted) so the bare↔suit handoff completes; it never steals
            // from another WANTED widget, so two same-tier copies can't fight — the extra one just
            // renders empty. Prefer ONE element with a per-tier layout override over duplicates.
            var owner = _stripOwner;
            if (owner != null && owner != this && owner._borrowed)
            {
                if (owner.Fader != null && owner.Fader.Target) return; // a wanted owner keeps it
                owner.RestoreMoodlets();                               // unwanted holder → take over
            }

            _stripRt = rt;
            _origParent = rt.parent;
            _origSiblingIndex = rt.GetSiblingIndex();
            _origLocalPos = rt.localPosition;
            _origAnchoredPos = rt.anchoredPosition;
            _origSizeDelta = rt.sizeDelta;
            _origAnchorMin = rt.anchorMin;
            _origAnchorMax = rt.anchorMax;
            _origPivot = rt.pivot;
            _origScale = rt.localScale;

            // Cache + flip vanilla's grid to a single centred horizontal row.
            _grid = rt.GetComponent<GridLayoutGroup>();
            if (_grid != null)
            {
                _gStartAxis = _grid.startAxis;
                _gConstraint = _grid.constraint;
                _gConstraintCount = _grid.constraintCount;
                _gSpacing = _grid.spacing;
                _gCellSize = _grid.cellSize;
                _gChildAlign = _grid.childAlignment;
            }

            rt.SetParent(_holder, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            _borrowed = true;
            _stripOwner = this;
        }

        private static readonly List<RectTransform> _cells = new List<RectTransform>();
        private static readonly List<Graphic> _gfxBuf = new List<Graphic>(); // brightness-tint scratch

        private void Apply(float scale)
        {
            if (_stripRt == null) { _borrowed = false; return; }

            // Take layout OVER from vanilla's grid: the GridLayoutGroup re-flattens any
            // per-cell offset every layout pass, so it can't coexist with bending the row
            // onto the visor curve. We hand-place the cells instead (a centred horizontal
            // row = what the reconfigured grid produced) and warp each cell's position, the
            // same technique the compass ticks use. Flat mode → warp is identity → the row
            // is exactly the plain centred row, so nothing regresses when the curve is off.
            if (_grid != null && _grid.enabled) _grid.enabled = false;

            // Scale the whole 192px-cell strip down to a bar-friendly size; the F9 slider
            // tunes it. Cells keep their 192px cell so each moodlet renders pixel-identical.
            float sc = Mathf.Clamp(Def.GetFFor(LayoutBare, "moodletScale", 0.32f), 0.08f, 1.5f) * scale;
            var want = new Vector3(sc, sc, 1f);
            if (_stripRt.localScale != want) _stripRt.localScale = want;
            if (_stripRt.anchoredPosition != Vector2.zero) _stripRt.anchoredPosition = Vector2.zero;

            // F9 "Moodlet transparency" (0 = solid, 1 = invisible): faded via the holder's own
            // CanvasGroup. It already ignores the HUD-root group (for the hover tooltip), so this
            // alpha is the moodlets' final opacity — nothing else fights it.
            if (_holderGroup != null)
            {
                float a = 1f - Mathf.Clamp01(Def.GetFFor(LayoutBare, "moodletTransparency", 0f));
                if (!Mathf.Approximately(_holderGroup.alpha, a)) _holderGroup.alpha = a;
                if (!_holderGroup.blocksRaycasts) _holderGroup.blocksRaycasts = true; // re-enable after words mode
            }

            LayoutCellsCurved(sc);
        }

        /// <summary>Centred row (or, with the "vertical" toggle, a centred column) of the
        /// currently-active moodlet cells, each bent onto the visor curve. Vanilla still owns which
        /// cells are active (SetActive); we only place the live ones.</summary>
        private void LayoutCellsCurved(float sc)
        {
            _cells.Clear();
            for (int i = 0; i < _stripRt.childCount; i++)
            {
                var ch = _stripRt.GetChild(i) as RectTransform;
                if (ch != null && ch.gameObject.activeSelf) _cells.Add(ch);
            }
            int n = _cells.Count;
            if (n == 0) return;

            float cell = _gCellSize.x > 1f ? _gCellSize.x : 192f;   // strip-local px
            float stride = cell + 10f;                              // gap between centres
            float startX = -(n - 1) * 0.5f * stride;
            bool vertical = Def.GetBFor(LayoutBare, "vertical", false); // stack down instead of across
            Vector2 center = _holder.anchoredPosition;              // element centre, canvas coords
            var mid = new Vector2(0.5f, 0.5f);
            var cellSize = new Vector2(cell, cell);

            // F9 "Moodlet brightness" (1 = native, 0 = black): a CanvasRenderer colour tint that
            // MULTIPLIES on top of whatever colour vanilla drew the cell (its own flash included),
            // so it can only darken — a colour multiply can't lift a coloured sprite to pure white.
            // Alpha stays 1 so icon transparency and the holder's transparency slider are untouched.
            // At 1.0 the tint is white = identity, which also resets any earlier darkening for free.
            // (Safe: vanilla flashes these icons by SPRITE SWAP + gameObject.SetActive, never by the
            // CanvasRenderer colour — verified 27701, no CrossFade in the build — so re-asserting the
            // CR colour here never collides with vanilla's flash/clear.)
            float bright = Mathf.Clamp01(Def.GetFFor(LayoutBare, "moodletBrightness", 1f));
            var tint = new Color(bright, bright, bright, 1f);

            for (int k = 0; k < n; k++)
            {
                var ch = _cells[k];
                // Centre-anchor each cell so its position maths is about its middle (the grid
                // had anchored them upper-left).
                if (ch.anchorMin != mid || ch.anchorMax != mid || ch.pivot != mid)
                { ch.anchorMin = ch.anchorMax = mid; ch.pivot = mid; }
                if (ch.sizeDelta != cellSize) ch.sizeDelta = cellSize;

                float lx = startX + k * stride;                    // flat strip-local offset
                // Horizontal: the offset runs along x (a centred row). Vertical: down the y axis,
                // top -> bottom (a centred column). Warp + strip-local conversion are unchanged.
                Vector3 abs = vertical
                    ? new Vector3(center.x, center.y - lx * sc, 0f)
                    : new Vector3(center.x + lx * sc, center.y, 0f); // canvas coords
                if (HudWarp.Enabled) abs = HudWarp.Warp(abs);
                // Back to strip-local (strip sits at centre, scaled by sc).
                ch.anchoredPosition3D = new Vector3(
                    (abs.x - center.x) / sc, (abs.y - center.y) / sc, abs.z / Mathf.Max(0.0001f, sc));

                // Re-assert the tint every frame: vanilla owns Graphic.color (the mesh), while this
                // is the CanvasRenderer multiplier on top — the two never collide.
                ch.GetComponentsInChildren<Graphic>(true, _gfxBuf);
                for (int g = 0; g < _gfxBuf.Count; g++)
                {
                    var cr = _gfxBuf[g].canvasRenderer;
                    if (cr != null) cr.SetColor(tint);
                }
            }
        }

        /// <summary>Hand the moodlet strip back to vanilla — parent, sibling index, geometry,
        /// AND its original grid settings (axis/constraint/spacing/cell/alignment). Public
        /// because the HUD orchestrator calls it the moment the widget stops being wanted; it
        /// MUST run before Root is destroyed. Safe when nothing is borrowed.</summary>
        /// <summary>Self-heal: hand the strip back the instant our Root is about to die,
        /// whatever destroyed it (profile switch, mode flip, hot reload, shutdown).</summary>
        protected override void OnBeforeDestroy() => RestoreMoodlets();

        public void RestoreMoodlets()
        {
            if (!_borrowed) return;
            _borrowed = false;
            if (ReferenceEquals(_stripOwner, this)) _stripOwner = null;
            try
            {
                if (_grid != null)
                {
                    _grid.startAxis = _gStartAxis;
                    _grid.constraint = _gConstraint;
                    _grid.constraintCount = _gConstraintCount;
                    _grid.spacing = _gSpacing;
                    _grid.cellSize = _gCellSize;
                    _grid.childAlignment = _gChildAlign;
                    _grid.enabled = true; // we disabled it to own the layout; give it back
                }
                _grid = null;
            }
            catch { }
            try
            {
                if (_stripRt != null)
                {
                    // Detach from OUR (possibly dying) subtree no matter what: a destroyed
                    // original parent (fake-null) must still send the strip to the scene root,
                    // never leave it under our canvas to be destroyed with it.
                    _stripRt.SetParent(_origParent != null ? _origParent : null, false);
                    _stripRt.anchorMin = _origAnchorMin;
                    _stripRt.anchorMax = _origAnchorMax;
                    _stripRt.pivot = _origPivot;
                    _stripRt.sizeDelta = _origSizeDelta;
                    _stripRt.anchoredPosition = _origAnchoredPos;
                    _stripRt.localPosition = _origLocalPos;
                    _stripRt.localScale = _origScale;
                    _stripRt.SetSiblingIndex(_origSiblingIndex);
                }
            }
            catch { }
            _stripRt = null;
        }

        // ---- words mode (#1) ----

        /// <summary>Hide the borrowed strip (words mode owns the display). It stays reparented
        /// under our holder so it never shows in vanilla's default spot, just invisible + inert.</summary>
        private void HideBorrowedStrip()
        {
            if (_holderGroup != null) { _holderGroup.alpha = 0f; _holderGroup.blocksRaycasts = false; }
        }

        private void EnsureWordPool()
        {
            if (_words != null) return;
            _words = new TextMeshProUGUI[WordPool];
            for (int i = 0; i < WordPool; i++)
            {
                var t = HudText.Make(Root, "MWord" + i, 14f, TextAlignmentOptions.Center);
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                t.enabled = false;
                _words[i] = t;
            }
        }

        private void HideWords()
        {
            _wordsBuilt = false;
            _wordSig = int.MinValue;
            if (_words == null) return;
            for (int i = 0; i < WordPool; i++) if (_words[i] != null) _words[i].enabled = false;
        }

        private void UpdateWords(float scale)
        {
            int sig = WordSignature();
            if (sig != _wordSig || !_wordsBuilt) { RebuildWords(); _wordSig = sig; }
            PlaceWords(scale);
        }

        /// <summary>A moodlet shows iff its image is live and it isn't a dedicated-display internal
        /// (jetpack/light). Level: critical flash (2) beats static caution (1). All game reads are
        /// guarded — a broken mirror yields "not showing".</summary>
        private static bool Showing(GameStatusItem su, out int level)
        {
            level = 0;
            try
            {
                if (su == null || su.UsesDedicatedDisplay) return false;
                var img = su.Image;
                if (img == null || !img.gameObject.activeSelf) return false;
                if (su._lastFlashState) level = 2;
                else if (su._lastStaticState) level = 1;
                return true;
            }
            catch { return false; }
        }

        private static int Priority(GameStatusItem su)
        {
            int type = 0; bool flash = false;
            try { type = (int)su.Type; } catch { }
            try { flash = su._lastFlashState; } catch { }
            return type * 2 + (flash ? 1 : 0);
        }

        /// <summary>Alloc-free fingerprint of the visible moodlet set (identity + type + level) so
        /// the word list only rebuilds when the set — or a severity — actually changes.</summary>
        private int WordSignature()
        {
            int sig = 17;
            try
            {
                var all = GameStatus.AllStatusUpdates;
                if (all == null) return 0;
                for (int i = 0; i < all.Count; i++)
                {
                    int level;
                    if (!Showing(all[i], out level)) continue;
                    int h; try { h = all[i].GetHashCode(); } catch { h = 0; }
                    int type = 0; try { type = (int)all[i].Type; } catch { }
                    unchecked { sig = sig * 31 + h * 3 + level * 7 + type; }
                }
            }
            catch { return 0; }
            return sig;
        }

        /// <summary>Resolve the visible moodlets into UPPERCASE words. DEDUPE first: several logical
        /// updates share ONE physical Image (PowerLow/PowerCritical, the Waste pair, the 4-way
        /// Pressure group), so group by that image's instance id and keep the highest displayed
        /// level (then static priority) — one word per shared image, mirroring vanilla's single
        /// flashing chip. Read-only + fail-soft; mutates nothing (MP-safe on a client).</summary>
        private void RebuildWords()
        {
            _wordCount = 0;
            int grpCount = 0;
            try
            {
                var all = GameStatus.AllStatusUpdates;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        var su = all[i];
                        int level;
                        if (!Showing(su, out level)) continue;
                        int key; try { key = su.Image.gameObject.GetInstanceID(); } catch { continue; }
                        int pri = Priority(su);
                        int g = -1;
                        for (int k = 0; k < grpCount; k++) if (_wGrpKey[k] == key) { g = k; break; }
                        if (g < 0)
                        {
                            if (grpCount >= WGroupCap) continue;
                            g = grpCount++;
                            _wGrpKey[g] = key; _wGrpWin[g] = su; _wGrpPri[g] = pri; _wGrpLvl[g] = level;
                        }
                        else if (level > _wGrpLvl[g] || (level == _wGrpLvl[g] && pri > _wGrpPri[g]))
                        { _wGrpWin[g] = su; _wGrpPri[g] = pri; _wGrpLvl[g] = level; }
                    }
                    for (int g = 0; g < grpCount && _wordCount < WordPool; g++)
                    {
                        string raw = "";
                        try { raw = _wGrpWin[g].GetDisplayName(); } catch { }
                        // GetDisplayName can carry unresolved localization ERROR markers — the
                        // "<A:EN:hash>" placeholders vanilla's Localization emits for a missing
                        // string (Localization.ErrorAction = "<A:{0}:{1}>"). The game's own TMP
                        // hides/expands them; our plain text would print them raw. Strip the tags;
                        // if nothing readable remains, fall back to the moodlet's stable id name.
                        raw = Core.StateText.Strip(raw);
                        if (raw != null) raw = raw.Trim();
                        if (string.IsNullOrEmpty(raw)) { try { raw = _wGrpWin[g].DisplayName; } catch { } }
                        if (string.IsNullOrEmpty(raw)) continue;
                        _wordStr[_wordCount] = raw.ToUpperInvariant();
                        _wordLvl[_wordCount] = _wGrpLvl[g];
                        _wordCount++;
                    }
                }
            }
            catch { _wordCount = 0; }
            for (int g = 0; g < grpCount; g++) _wGrpWin[g] = null;

            for (int i = 0; i < WordPool; i++)
            {
                bool on = i < _wordCount;
                if (_words[i] != null)
                {
                    _words[i].enabled = on;
                    if (on) HudText.Set(_words[i], _wordStr[i]);
                }
            }
            _wordsBuilt = true;
        }

        /// <summary>Place the active words in a centred grid flowing from the top of the element
        /// down, coloured by severity (normal = the Word colour ref, caution = HudWarn, critical =
        /// HudCritical). Size / separation / columns / alignment are all F9-driven.</summary>
        private void PlaceWords(float scale)
        {
            if (_words == null || _wordCount == 0) return;
            var center = CenterFor(scale);
            var size = SizeFor(scale);
            float halfW = size.x * 0.5f, halfH = size.y * 0.5f;

            int cols = Mathf.Clamp(Def.GetIFor(LayoutBare, "wordCols", 1), 1, 8);
            float gap = Mathf.Max(0f, Def.GetFFor(LayoutBare, "wordGap", 4f)) * scale;
            float wsize = HudText.Size(Mathf.Max(4f, Def.GetFFor(LayoutBare, "wordSize", 16f))
                * Def.FontScaleFor(LayoutBare)) * scale;
            float lineH = wsize * 1.18f;
            float rowStep = lineH + gap;
            float colW = cols > 1 ? (size.x - gap * (cols - 1)) / cols : size.x;
            bool vertical = Def.GetBFor(LayoutBare, "vertical", false);
            int rows = Mathf.CeilToInt(_wordCount / (float)cols); // used for column-major (vertical) fill

            int alignIx = Mathf.Clamp(Def.GetIFor(LayoutBare, "wordAlign", 1), 0, 2);
            var tmpAlign = alignIx == 0 ? TextAlignmentOptions.Left
                : alignIx == 2 ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;

            Color normal = GlobalOr(Def.GetSFor(LayoutBare, "wordColor", ""), HudPalette.BareWord.Value);
            Color warn = HudPalette.Warn.Value;
            Color crit = HudPalette.Critical.Value;

            float topY = center.y + halfH;
            float leftX = center.x - halfW;

            for (int i = 0; i < _wordCount; i++)
            {
                var t = _words[i];
                if (t == null) continue;
                // Horizontal fills across-then-down (row-major); vertical fills down-then-across.
                int r, c;
                if (vertical) { c = i / rows; r = i % rows; }
                else { r = i / cols; c = i % cols; }
                HudText.Sync(t);
                t.fontSize = wsize;
                t.alignment = tmpAlign;
                t.enableWordWrapping = false;
                t.color = _wordLvl[i] >= 2 ? crit : _wordLvl[i] == 1 ? warn : normal;
                float cellX = leftX + c * (colW + gap) + colW * 0.5f;
                float cellY = topY - r * rowStep - lineH * 0.5f;
                t.rectTransform.sizeDelta = new Vector2(colW, lineH);
                t.rectTransform.anchoredPosition = new Vector2(cellX, cellY);
            }
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;

            // #1: words mode — render moodlets as words (like the bare-senses box) instead of the
            // borrowed icon strip, with full size / separation / columns / alignment / colour.
            into.Add(HudProp.Bool("Words mode (moodlets as words)",
                () => d.GetBFor(EditBare(d), "words", false), v => d.SetBFor(EditBare(d), "words", v)));

            int wStart = into.Count;
            // Orientation for BOTH modes: the borrowed icon strip stacks as a column instead of a
            // row; the words grid fills top-to-bottom (column-major) instead of across.
            into.Add(HudProp.Bool("Vertical (column instead of row)",
                () => d.GetBFor(EditBare(d), "vertical", false), v => d.SetBFor(EditBare(d), "vertical", v)));
            into.Add(HudProp.F("Word text size", () => d.GetFFor(EditBare(d), "wordSize", 16f),
                v => d.SetFFor(EditBare(d), "wordSize", Mathf.Clamp(v, 6f, 48f)), 6f, 48f));
            into.Add(HudProp.F("Word separation", () => d.GetFFor(EditBare(d), "wordGap", 4f),
                v => d.SetFFor(EditBare(d), "wordGap", Mathf.Clamp(v, 0f, 40f)), 0f, 40f));
            into.Add(HudProp.I("Columns", () => d.GetIFor(EditBare(d), "wordCols", 1),
                v => d.SetIFor(EditBare(d), "wordCols", Mathf.Clamp(v, 1, 8)), 1, 8));
            into.Add(HudProp.Enum("Text alignment",
                () => Mathf.Clamp(d.GetIFor(EditBare(d), "wordAlign", 1), 0, 2),
                v => d.SetIFor(EditBare(d), "wordAlign", v), new[] { "Left", "Center", "Right" }));
            for (int i = wStart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;

            into.Add(HudProp.Color("Word colour", () => d.GetSFor(EditBare(d), "wordColor", ""),
                v => d.SetSFor(EditBare(d), "wordColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.BareWord.Value));
            into[into.Count - 1].Group = HudPropGroup.Appearance;

            into.Add(HudProp.F("Moodlet scale", () => d.GetFFor(EditBare(d), "moodletScale", 0.32f),
                v => d.SetFFor(EditBare(d), "moodletScale", Mathf.Clamp(v, 0.08f, 1.5f)), 0.08f, 1.5f));
            into[into.Count - 1].Group = HudPropGroup.Layout;

            int appearanceStart = into.Count;
            into.Add(HudProp.F("Moodlet transparency", () => d.GetFFor(EditBare(d), "moodletTransparency", 0f),
                v => d.SetFFor(EditBare(d), "moodletTransparency", Mathf.Clamp01(v)), 0f, 1f));
            into.Add(HudProp.F("Moodlet brightness", () => d.GetFFor(EditBare(d), "moodletBrightness", 1f),
                v => d.SetFFor(EditBare(d), "moodletBrightness", Mathf.Clamp01(v)), 0f, 1f));
            for (int i = appearanceStart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;
        }
    }
}
