using System;
using System.Collections.Generic;
using System.IO;
using Assets.Scripts;              // IModal, MouseModeController, CursorManager
using StationeersUIMod.Core;       // Guards, CursorBlockArbiter, UIALog
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu
{
    /// <summary>
    /// The in-game Designer Handbook reader (tutorial plan section 9.3).
    ///
    /// <para>WHAT IT IS. A page FLIPBOOK, not a PDF parser. A real runtime PDF renderer on
    /// Unity/net48 means a native PDFium dependency (megabytes, platform risk, one more thing SLP
    /// has to load) and was rejected in the plan. The package therefore ships the handbook twice:
    /// pre-rendered page images under <c>&lt;mod&gt;/Handbook/pages/</c> (what this window shows) and
    /// the real PDF at <c>&lt;mod&gt;/Handbook/HUD-Designer-Handbook.pdf</c> (what the [Open PDF]
    /// button hands to the system viewer for reading outside the game / printing). To the player it
    /// reads as "a PDF reader in the game"; implementation-wise it is Prev/Next over textures.</para>
    ///
    /// <para>CONTENT SOURCE. The mod's own install folder, via
    /// <see cref="StationeersUIMod.ModDirectory"/> — the SAME single source of truth that
    /// <c>Features.HudProfileStore.SyncShipped</c> is handed at startup (SLP's injected
    /// <c>ModData.DirectoryPath</c>). It is NULL under the F6 ScriptEngine dev flow (no ModData, so
    /// no mod folder at all), which is simply one more flavour of "the handbook is not on disk" —
    /// the window still opens and says so. Nothing here re-derives the install path by another
    /// route, so it can never drift from the profile store's answer.</para>
    ///
    /// <para>MEMORY. Page images are large-ish textures and Texture2D is UNMANAGED memory the GC
    /// does not reclaim, so exactly three may be resident at once (previous, current, next). Every
    /// texture that leaves that window is destroyed the moment it leaves it, and
    /// <see cref="Close"/>/<see cref="Shutdown"/> destroy the lot. Decoding is lazy: the current
    /// page decodes on the frame you turn to it, its neighbours one frame later (so a page turn
    /// costs ONE decode, not three).</para>
    ///
    /// <para>MODALITY. It takes its OWN modal exactly like <see cref="UiaControlCenter"/> (a
    /// <c>Typing</c> KeyManager input state under its own key, its own <see cref="IModal"/> with
    /// <c>UnlockCursor</c>, its own <see cref="CursorBlockArbiter"/> hold), so it STACKS: it may be
    /// opened over the Control Center or the tutorial coach and its Escape closes only itself.
    /// KeyManager's input state is a keyed MAP (verified <c>KeyManager.SetInputState</c>/
    /// <c>RemoveInputState</c>, V27758) — removing our key falls back to whatever key the window
    /// underneath still holds, so the stack unwinds correctly in any order.</para>
    ///
    /// <para>ONE WIRING NOTE FOR THE HOST: <see cref="UiaControlCenter.Update"/> also closes on a
    /// bare <c>Escape</c>. Both windows read the same key state in the same frame, so the Control
    /// Center's Escape branch must be gated on <c>!HandbookViewer.IsOpen</c> or one Escape closes
    /// both. That is the reason <see cref="IsOpen"/> is part of the public surface.</para>
    ///
    /// Static + pumped from StationeersUIMod.Update, mirroring HudSystem/UiaControlCenter.
    /// </summary>
    internal static class HandbookViewer
    {
        private sealed class Modal : IModal { public bool UnlockCursor { get { return true; } } }

        internal const string InputStateKey = "UIA_Handbook";   // ModalInputChain reasserts by this name
        private const string CursorHoldId = "handbook";

        /// <summary>Above the Control Center (5200) and the tutorial coach (5600), below the
        /// tooltip layer (6000): the handbook is opened FROM those windows and must sit over them,
        /// but a tooltip must still sit over it.</summary>
        private const int CanvasSortingOrder = 5700;

        private const float CardW = 860f;
        private const float CardH = 760f;
        private const float NavH = 32f;

        private const string HandbookFolder = "Handbook";
        private const string PagesFolder = "pages";
        private const string PdfFileName = "HUD-Designer-Handbook.pdf";

        private static readonly Modal _modal = new Modal();

        // ---- window ----
        private static GameObject _root;
        private static Image _scrim;
        private static RectTransform _card;
        private static Image _cardImg;
        private static RectTransform _pageArea;
        private static Image _pageAreaImg;
        private static RawImage _pageImage;
        private static TextMeshProUGUI _pageMsg;
        private static TextMeshProUGUI _title;
        private static TextMeshProUGUI _counter;
        private static TextMeshProUGUI _hint;
        private static UiaControls.UiaButton _prevBtn, _nextBtn, _pdfBtn;

        // ---- state ----
        private static bool _open;
        private static bool _modalHeld;
        private static int _index;
        private static bool _prefetchPending;
        private static readonly List<string> _pages = new List<string>();
        private static string _pdfPath;

        // Resident page textures (page index -> texture) and the pages that failed to decode, so a
        // corrupt/locked file is reported once instead of retried on every frame we look at it.
        private static readonly Dictionary<int, Texture2D> _tex = new Dictionary<int, Texture2D>();
        private static readonly HashSet<int> _failed = new HashSet<int>();

        // Last applied letterbox fit, so the per-frame guard only touches sizeDelta when something
        // actually changed (a RectTransform size write dirties layout).
        private static float _fitAreaW, _fitAreaH, _fitTexW, _fitTexH;

        internal static bool IsOpen { get { return _open; } }

        // ================= open / close =================

        /// <summary>Show the handbook. Rebuilds the window from scratch every time (a dozen
        /// GameObjects — cheaper than tracking a live theme change on a window that is opened once
        /// in a while, and it guarantees the chrome matches the CURRENT palette, since kit buttons
        /// freeze their colours at build time). Refuses, with a log line and no exception, whenever
        /// gameplay UI must not be on screen at all.</summary>
        internal static void Open()
        {
            if (_open) return;
            if (!Guards.CanDraw())
            {
                UIALog.Info("HandbookViewer: refusing to open - no world to draw over right now.");
                return;
            }
            try
            {
                ScanContent();
                Build();
            }
            catch (Exception e)
            {
                // A build failure must not strand a half-built canvas or an unbalanced modal.
                UIALog.Error("HandbookViewer: build failed: " + e);
                DestroyRoot();
                return;
            }
            if (_root == null) return;
            _open = true;
            _index = 0;
            AcquireModal();
            ShowPage(0);
        }

        /// <summary>Hide + fully release: every page texture destroyed, the canvas destroyed, the
        /// modal handed back in the established order.</summary>
        internal static void Close()
        {
            if (!_open) return;
            _open = false;
            _prefetchPending = false;
            DropAllTextures();
            DestroyRoot();
            ReleaseModal();
        }

        // ================= per-frame =================

        /// <summary>Pumped every frame from StationeersUIMod.Update (like the Control Center).
        /// Owns Escape, the arrow keys and the wheel while it is up.</summary>
        internal static void Update()
        {
            if (!_open) return;

            // World gone (loading screen, main menu, master switch off): never leave the window
            // stranded over nothing.
            if (!Guards.CanDraw()) { Close(); return; }

            // The console and vanilla input windows read the same raw keys - not ours then.
            if (!Guards.CanToggleMenus()) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Vanilla's Escape fires on key-UP: starve it until the key is physically
                // released, or the pause menu opens the moment this window closes.
                Core.ModalInputChain.BeginEscSwallow();
                Close();
                return;
            }

            if (_pages.Count > 1)
            {
                if (Input.GetKeyDown(KeyCode.RightArrow)) { Turn(1); return; }
                if (Input.GetKeyDown(KeyCode.LeftArrow)) { Turn(-1); return; }

                // Wheel, but only while the pointer is actually over the card — a scroll aimed at
                // something else on screen should not silently flip pages. The overlay canvas has
                // no camera, hence the null camera argument (same test as UiaControlCenter's
                // HitTestWindow).
                float wheel = Input.mouseScrollDelta.y;
                if ((wheel > 0.01f || wheel < -0.01f) && _card != null
                    && RectTransformUtility.RectangleContainsScreenPoint(_card, Input.mousePosition, null))
                {
                    Turn(wheel < 0f ? 1 : -1);   // wheel down = forward, like any document reader
                    return;
                }
            }

            // Neighbour pages decode one frame AFTER the turn, so a page turn costs a single decode.
            if (_prefetchPending)
            {
                _prefetchPending = false;
                EnsureResidentWindow(_index, true);
            }

            RepaintTheme();
            FitPageImage();
        }

        private static void Turn(int delta)
        {
            if (_pages.Count == 0) return;
            int next = Mathf.Clamp(_index + delta, 0, _pages.Count - 1);
            if (next == _index) return;
            ShowPage(next);
        }

        // ================= content =================

        /// <summary>Find the handbook on disk. Never throws: every failure mode (no mod folder
        /// under F6, a hand-installed partial copy, an unreadable directory) just leaves the page
        /// list empty and/or the PDF path null, which the window renders as its "not found" note.
        ///
        /// Naming: the packaged pages are <c>page-*.png</c>/<c>.jpg</c>. If a build of the render
        /// pipeline ever stages them under a different stem (the plan's own sketch says
        /// <c>NN.jpg</c>), the fallback sweep picks up every image in the folder instead, so a
        /// naming mismatch degrades to "still works" rather than "handbook not found".</summary>
        private static void ScanContent()
        {
            _pages.Clear();
            _failed.Clear();
            _pdfPath = null;

            string modDir = null;
            // The mod's install folder: SLP's injected ModData.DirectoryPath, cached by the plugin
            // and handed to HudProfileStore.SyncShipped — the one source of truth for "where am I
            // installed". Null under the F6 dev flow.
            try { modDir = global::StationeersUIMod.StationeersUIMod.ModDirectory; }
            catch { modDir = null; }
            if (string.IsNullOrEmpty(modDir)) return;

            try
            {
                string root = Path.Combine(modDir, HandbookFolder);
                string pdf = Path.Combine(root, PdfFileName);
                if (File.Exists(pdf)) _pdfPath = pdf;

                string pagesDir = Path.Combine(root, PagesFolder);
                if (!Directory.Exists(pagesDir)) return;

                AddImages(Directory.GetFiles(pagesDir, "page-*"));
                if (_pages.Count == 0) AddImages(Directory.GetFiles(pagesDir));
                _pages.Sort(ComparePageNames);
            }
            catch (Exception e)
            {
                UIALog.Warn("HandbookViewer: reading the handbook folder failed: " + e.Message);
                _pages.Clear();
            }
        }

        /// <summary>Filter a raw directory listing down to images we can actually decode. The
        /// extension is re-checked in managed code on purpose: Windows' wildcard matching also
        /// matches 8.3 short names, so a <c>*.jpg</c> pattern can return files that are not .jpg.</summary>
        private static void AddImages(string[] files)
        {
            if (files == null) return;
            for (int i = 0; i < files.Length; i++)
            {
                string f = files[i];
                if (string.IsNullOrEmpty(f)) continue;
                string ext = Path.GetExtension(f);
                if (string.IsNullOrEmpty(ext)) continue;
                ext = ext.ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    _pages.Add(f);
            }
        }

        private static int ComparePageNames(string a, string b)
        {
            return NaturalCompare(Path.GetFileName(a), Path.GetFileName(b));
        }

        /// <summary>"Natural" order: digit runs compare as NUMBERS, everything else ordinally and
        /// case-insensitively. Without this, plain string order puts page-10 before page-2 — the
        /// classic flipbook bug.</summary>
        private static int NaturalCompare(string a, string b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                char ca = a[i], cb = b[j];
                if (ca >= '0' && ca <= '9' && cb >= '0' && cb <= '9')
                {
                    int si = i, sj = j;
                    while (i < a.Length && a[i] >= '0' && a[i] <= '9') i++;
                    while (j < b.Length && b[j] >= '0' && b[j] <= '9') j++;
                    string da = a.Substring(si, i - si).TrimStart('0');
                    string db = b.Substring(sj, j - sj).TrimStart('0');
                    if (da.Length != db.Length) return da.Length - db.Length;  // more digits = bigger
                    int cd = string.CompareOrdinal(da, db);
                    if (cd != 0) return cd;
                }
                else
                {
                    int c = char.ToUpperInvariant(ca).CompareTo(char.ToUpperInvariant(cb));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (a.Length - i) - (b.Length - j);
        }

        // ================= textures =================

        /// <summary>Show page <paramref name="i"/>: decode it if needed, drop anything that just
        /// fell out of the three-page residency window, repaint the chrome, re-fit the image.</summary>
        private static void ShowPage(int i)
        {
            if (_pages.Count == 0)
            {
                _index = 0;
                RefreshPageVisuals(null);
                RefreshChrome();
                return;
            }
            _index = Mathf.Clamp(i, 0, _pages.Count - 1);
            EnsureResidentWindow(_index, false);   // current only; neighbours next frame
            _prefetchPending = true;
            Texture2D tex;
            _tex.TryGetValue(_index, out tex);
            RefreshPageVisuals(tex);
            RefreshChrome();
        }

        /// <summary>Enforce the residency rule: exactly {current-1, current, current+1} may be
        /// resident. Everything else is destroyed NOW (Texture2D is unmanaged memory — leaving it
        /// to the GC is how a flipbook eats a few hundred MB). <paramref name="withNeighbours"/>
        /// false loads only the current page (the responsive path on a page turn).</summary>
        private static void EnsureResidentWindow(int center, bool withNeighbours)
        {
            if (_pages.Count == 0) { DropAllTextures(); return; }

            int lo = Mathf.Max(0, center - 1);
            int hi = Mathf.Min(_pages.Count - 1, center + 1);

            // Evict first, so the peak resident count never exceeds the window.
            List<int> evict = null;
            foreach (var kv in _tex)
            {
                if (kv.Key < lo || kv.Key > hi)
                {
                    if (evict == null) evict = new List<int>();
                    evict.Add(kv.Key);
                }
            }
            if (evict != null)
                for (int i = 0; i < evict.Count; i++) DropTexture(evict[i]);

            EnsureLoaded(center);
            if (withNeighbours)
            {
                for (int i = lo; i <= hi; i++)
                    if (i != center) EnsureLoaded(i);
            }
        }

        private static void EnsureLoaded(int i)
        {
            if (i < 0 || i >= _pages.Count) return;
            if (_tex.ContainsKey(i) || _failed.Contains(i)) return;
            Texture2D tex = LoadPage(_pages[i]);
            if (tex == null) { _failed.Add(i); return; }
            _tex[i] = tex;
        }

        /// <summary>Decode one page file. All IO and decoding is guarded — a missing, locked or
        /// corrupt page becomes a null (and an ASCII "could not be loaded" label), never a throw.
        /// <c>markNonReadable</c> frees the CPU-side copy right after upload, halving what a page
        /// costs while it is resident.</summary>
        private static Texture2D LoadPage(string path)
        {
            Texture2D tex = null;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                byte[] bytes = File.ReadAllBytes(path);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                if (!tex.LoadImage(bytes, true))
                {
                    UnityEngine.Object.Destroy(tex);
                    UIALog.Warn("HandbookViewer: could not decode page '" + Path.GetFileName(path) + "'.");
                    return null;
                }
                return tex;
            }
            catch (Exception e)
            {
                if (tex != null) { try { UnityEngine.Object.Destroy(tex); } catch { } }
                UIALog.Warn("HandbookViewer: reading page '" + path + "' failed: " + e.Message);
                return null;
            }
        }

        private static void DropTexture(int i)
        {
            Texture2D tex;
            if (!_tex.TryGetValue(i, out tex)) return;
            _tex.Remove(i);
            if (_pageImage != null && ReferenceEquals(_pageImage.texture, tex)) _pageImage.texture = null;
            if (tex != null) { try { UnityEngine.Object.Destroy(tex); } catch { } }
        }

        private static void DropAllTextures()
        {
            if (_pageImage != null) _pageImage.texture = null;
            foreach (var kv in _tex)
                if (kv.Value != null) { try { UnityEngine.Object.Destroy(kv.Value); } catch { } }
            _tex.Clear();
            _fitTexW = _fitTexH = 0f;
        }

        // ================= window =================

        private static void Build()
        {
            DestroyRoot();

            _root = new GameObject("UIAscended_Handbook");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            // Full-screen scrim: absorbs clicks aimed at whatever is underneath (the Control Center,
            // the tutorial). Like the Control Center's, it does NOT close on click.
            _scrim = UiaUi.Panel(_root.transform, UiaTheme.Scrim, "scrim");

            // The card. Fixed reference size + explicit anchoring for its three bands (title / page /
            // nav) so every rect is known the moment it is built — no layout pass needed before the
            // letterbox fit can be computed.
            var cardGo = UiaUi.Go("card", _root.transform);
            _card = (RectTransform)cardGo.transform;
            _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.anchoredPosition = Vector2.zero;
            _card.sizeDelta = new Vector2(CardW, CardH);
            _cardImg = cardGo.AddComponent<Image>();
            _cardImg.color = UiaTheme.Window;
            UiaImages.Round(_cardImg);
            // The kit's glass decoration: the themed HUD border + edge light/ripple, no glow halo.
            // Self-contained (no statics, dies with the host), so Close/Shutdown need nothing extra.
            cardGo.AddComponent<UiaGlassSkin>();

            BuildTitleBar();
            BuildPageArea();
            BuildNavBar();
        }

        private static void BuildTitleBar()
        {
            var barGo = UiaUi.Go("titlebar", _card);
            var bar = (RectTransform)barGo.transform;
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(UiaTheme.Pad, 0f);
            bar.offsetMax = new Vector2(-UiaTheme.Pad, 0f);
            bar.sizeDelta = new Vector2(bar.sizeDelta.x, UiaTheme.TitleH);
            bar.anchoredPosition = new Vector2(0f, -UiaTheme.Pad);
            UiaUi.HLayout(bar, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft);

            var titleGo = UiaUi.Go("title", bar);
            _title = titleGo.AddComponent<TextMeshProUGUI>();
            _title.font = UiaTheme.Font();
            _title.fontSize = UiaTheme.TitleSize;
            _title.color = UiaTheme.Text;
            _title.alignment = TextAlignmentOptions.Left;
            _title.raycastTarget = false;
            _title.characterSpacing = 8f;
            _title.fontStyle = FontStyles.Bold;
            _title.text = "DESIGNER HANDBOOK";
            var tle = titleGo.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;
            tle.minWidth = 220f;

            var cntGo = UiaUi.Go("counter", bar);
            UiaUi.Size(cntGo, UiaTheme.RowH, 150f, flexW: 0f);
            _counter = cntGo.AddComponent<TextMeshProUGUI>();
            _counter.font = UiaTheme.Font();
            _counter.fontSize = UiaTheme.SmallSize;
            _counter.color = UiaTheme.TextDim;
            _counter.alignment = TextAlignmentOptions.Right;
            _counter.raycastTarget = false;

            _pdfBtn = UiaControls.Button(bar, "Open PDF", OpenPdf, 120f, 30f, UiaControls.ButtonStyle.Panel);
            if (string.IsNullOrEmpty(_pdfPath)) _pdfBtn.SetEnabled(false);

            UiaControls.Button(bar, "X", Close, 34f, 30f, UiaControls.ButtonStyle.Panel);
        }

        private static void BuildPageArea()
        {
            var areaGo = UiaUi.Go("page-area", _card);
            _pageArea = (RectTransform)areaGo.transform;
            _pageArea.anchorMin = Vector2.zero;
            _pageArea.anchorMax = Vector2.one;
            _pageArea.offsetMin = new Vector2(UiaTheme.Pad, UiaTheme.Pad + NavH + UiaTheme.Gap);
            _pageArea.offsetMax = new Vector2(-UiaTheme.Pad, -(UiaTheme.Pad + UiaTheme.TitleH + UiaTheme.Gap));
            _pageAreaImg = areaGo.AddComponent<Image>();
            _pageAreaImg.color = UiaTheme.Panel;
            UiaImages.Round(_pageAreaImg);

            // The page itself: a RawImage, because a page is a runtime Texture2D and RawImage takes
            // one directly (an Image would need a throwaway Sprite wrapper per page). Letterboxed by
            // hand in FitPageImage rather than with preserveAspect, so the drawn rect is exactly the
            // scaled page and never a stretched or cropped one.
            var imgGo = UiaUi.Go("page", _pageArea);
            _pageImage = imgGo.AddComponent<RawImage>();
            _pageImage.raycastTarget = false;
            _pageImage.color = Color.white;
            var irt = _pageImage.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            irt.anchoredPosition = Vector2.zero;
            irt.sizeDelta = new Vector2(16f, 16f);

            var msgGo = UiaUi.Go("message", _pageArea);
            _pageMsg = msgGo.AddComponent<TextMeshProUGUI>();
            _pageMsg.font = UiaTheme.Font();
            _pageMsg.fontSize = UiaTheme.LabelSize;
            _pageMsg.color = UiaTheme.TextDim;
            _pageMsg.alignment = TextAlignmentOptions.Center;
            _pageMsg.enableWordWrapping = true;
            _pageMsg.raycastTarget = false;
            var mrt = (RectTransform)msgGo.transform;
            UiaUi.Fill(mrt, 40f);
        }

        private static void BuildNavBar()
        {
            var navGo = UiaUi.Go("navbar", _card);
            var nav = (RectTransform)navGo.transform;
            nav.anchorMin = new Vector2(0f, 0f);
            nav.anchorMax = new Vector2(1f, 0f);
            nav.pivot = new Vector2(0.5f, 0f);
            nav.offsetMin = new Vector2(UiaTheme.Pad, 0f);
            nav.offsetMax = new Vector2(-UiaTheme.Pad, 0f);
            nav.sizeDelta = new Vector2(nav.sizeDelta.x, NavH);
            nav.anchoredPosition = new Vector2(0f, UiaTheme.Pad);
            UiaUi.HLayout(nav, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft);

            _prevBtn = UiaControls.Button(nav, "Prev", () => Turn(-1), 150f, NavH, UiaControls.ButtonStyle.Panel);

            var hintGo = UiaUi.Go("hint", nav);
            _hint = hintGo.AddComponent<TextMeshProUGUI>();
            _hint.font = UiaTheme.Font();
            _hint.fontSize = UiaTheme.SmallSize;
            _hint.color = UiaTheme.TextMute;
            _hint.alignment = TextAlignmentOptions.Center;
            _hint.raycastTarget = false;
            _hint.text = "Arrow keys or the mouse wheel turn pages - Esc closes";
            var hle = hintGo.AddComponent<LayoutElement>();
            hle.flexibleWidth = 1f;
            hle.minWidth = 120f;

            _nextBtn = UiaControls.Button(nav, "Next", () => Turn(1), 150f, NavH, UiaControls.ButtonStyle.Panel);
        }

        private static void DestroyRoot()
        {
            // Hide BEFORE Destroy: Destroy takes effect at end of frame, so a close-then-reopen in
            // the same frame would otherwise draw both canvases at the same sorting order once.
            if (_root != null)
            {
                try { _root.SetActive(false); } catch { }
                try { UnityEngine.Object.Destroy(_root); } catch { }
            }
            _root = null;
            _scrim = null;
            _card = null;
            _cardImg = null;
            _pageArea = null;
            _pageAreaImg = null;
            _pageImage = null;
            _pageMsg = null;
            _title = null;
            _counter = null;
            _hint = null;
            _prevBtn = null;
            _nextBtn = null;
            _pdfBtn = null;
            _fitAreaW = _fitAreaH = _fitTexW = _fitTexH = 0f;
        }

        // ================= painting =================

        /// <summary>Point the RawImage at this page's texture, or hide it and put up an ASCII note.
        /// The "nothing on disk" note is deliberately actionable (it names the fix) — a partial
        /// hand-install is the case the plan calls out.</summary>
        private static void RefreshPageVisuals(Texture2D tex)
        {
            if (_pageImage == null || _pageMsg == null) return;

            if (_pages.Count == 0)
            {
                _pageImage.texture = null;
                _pageImage.gameObject.SetActive(false);
                _pageMsg.gameObject.SetActive(true);
                _pageMsg.text = "Handbook not found in the mod folder."
                    + "\nIt ships with the mod package - reinstalling the mod restores it.";
                return;
            }

            if (tex == null)
            {
                _pageImage.texture = null;
                _pageImage.gameObject.SetActive(false);
                _pageMsg.gameObject.SetActive(true);
                _pageMsg.text = "This page could not be loaded.";
                return;
            }

            _pageMsg.gameObject.SetActive(false);
            _pageImage.gameObject.SetActive(true);
            _pageImage.texture = tex;
            _fitTexW = _fitTexH = 0f;   // force a re-fit for the new page's aspect
            FitPageImage();
        }

        /// <summary>Letterbox: scale the page to fit inside the page area, preserving aspect,
        /// centred. Only writes sizeDelta when the area size or the texture changed.</summary>
        private static void FitPageImage()
        {
            if (_pageImage == null || _pageArea == null) return;
            Texture t = _pageImage.texture;
            if (t == null || !_pageImage.gameObject.activeSelf) return;

            float aw = _pageArea.rect.width - 12f;
            float ah = _pageArea.rect.height - 12f;
            if (aw <= 1f || ah <= 1f) return;
            float tw = t.width, th = t.height;
            if (tw <= 0f || th <= 0f) return;

            if (Mathf.Approximately(aw, _fitAreaW) && Mathf.Approximately(ah, _fitAreaH)
                && Mathf.Approximately(tw, _fitTexW) && Mathf.Approximately(th, _fitTexH))
                return;

            float s = Mathf.Min(aw / tw, ah / th);
            _pageImage.rectTransform.sizeDelta = new Vector2(Mathf.Floor(tw * s), Mathf.Floor(th * s));
            _pageImage.rectTransform.anchoredPosition = Vector2.zero;
            _fitAreaW = aw; _fitAreaH = ah; _fitTexW = tw; _fitTexH = th;
        }

        /// <summary>Page counter + Prev/Next enablement.</summary>
        private static void RefreshChrome()
        {
            if (_counter != null)
                _counter.text = _pages.Count > 0
                    ? "Page " + (_index + 1) + " / " + _pages.Count
                    : "No pages";
            if (_prevBtn != null) _prevBtn.SetEnabled(_pages.Count > 0 && _index > 0);
            if (_nextBtn != null) _nextBtn.SetEnabled(_pages.Count > 0 && _index < _pages.Count - 1);
            // Blank the hint rather than deactivating it: it is also the flexible spacer that keeps
            // Prev at the left edge and Next at the right, and a disabled child contributes no
            // flexible width (the buttons would bunch up together on a one-page handbook).
            if (_hint != null)
                _hint.text = _pages.Count > 1
                    ? "Arrow keys or the mouse wheel turn pages - Esc closes"
                    : "";
        }

        /// <summary>Follow a live F9 palette change on the surfaces whose colour is NOT frozen at
        /// build time (every setter here is dirty-guarded, so this is ~free). Kit buttons DO freeze
        /// their palette in Init, so their fill only catches up on the next open — acceptable for a
        /// window that is opened deliberately and read, not left up during theme work.</summary>
        private static void RepaintTheme()
        {
            if (_scrim != null) _scrim.color = UiaTheme.Scrim;
            if (_cardImg != null) _cardImg.color = UiaTheme.Window;
            if (_pageAreaImg != null) _pageAreaImg.color = UiaTheme.Panel;
            if (_title != null) _title.color = UiaTheme.Text;
            if (_counter != null) _counter.color = UiaTheme.TextDim;
            if (_hint != null) _hint.color = UiaTheme.TextMute;
            if (_pageMsg != null) _pageMsg.color = UiaTheme.TextDim;
        }

        // ================= PDF =================

        /// <summary>Hand the real PDF to the system viewer. Disabled (never even reachable) when the
        /// file is absent — see BuildTitleBar. Fail-soft: a blocked/failed shell open is a log line.</summary>
        private static void OpenPdf()
        {
            if (string.IsNullOrEmpty(_pdfPath)) return;
            try
            {
                string full = Path.GetFullPath(_pdfPath).Replace('\\', '/');
                // A real install path contains spaces ("...\steamapps\common\Stationeers\..."), and
                // an unescaped space truncates the URL in some shells. EscapeUriString leaves ':'
                // and '/' alone and turns ' ' into %20; '#' would otherwise read as a fragment.
                string url = "file:///" + Uri.EscapeUriString(full).Replace("#", "%23");
                Application.OpenURL(url);
            }
            catch (Exception e)
            {
                UIALog.Warn("HandbookViewer: opening the PDF failed: " + e.Message);
            }
        }

        // ================= modal =================

        private static void AcquireModal()
        {
            if (_modalHeld) return;
            _modalHeld = true;
            try { KeyManager.SetInputState(InputStateKey, KeyInputState.Typing); } catch { }
            try { MouseModeController.AddModal(_modal); } catch { }
            CursorBlockArbiter.Hold(CursorHoldId);
        }

        private static void ReleaseModal()
        {
            if (!_modalHeld) return;
            _modalHeld = false;
            try { KeyManager.RemoveInputState(InputStateKey); } catch { }
            // Our key was current while we were front-most: KeyManager pops to the map's LAST
            // entry, which is "WorldManager"/Paused while a mod pause is latched - hand Typing
            // back to the still-open layer underneath (coach or Control Center) instead.
            Core.ModalInputChain.ReassertTop();
            try { MouseModeController.RemoveModal(_modal); } catch { }
            CursorBlockArbiter.Release(CursorHoldId);
            // Re-lock the cursor next frame ONLY if we were the last UIA surface holding it. The
            // handbook stacks over the Control Center / tutorial, and re-locking while one of those
            // is still up would fight it for a frame.
            if (CursorBlockArbiter.AnyHold) return;
            try
            {
                if (CursorManager.Instance != null)
                    CursorManager.Instance.OnApplicationFocus(true);
            }
            catch { }
        }

        // ================= teardown =================

        /// <summary>Hot-reload teardown (called from the plugin's OnDestroy). Every static goes back
        /// to its initial value, the canvas is destroyed, every page texture is destroyed (unmanaged
        /// memory the GC will not reclaim), and the modal is handed back so the game's modal list /
        /// KeyManager map never keeps a pointer into the dead assembly.</summary>
        internal static void Shutdown()
        {
            ReleaseModal();
            _open = false;
            _prefetchPending = false;
            _index = 0;
            DropAllTextures();
            _failed.Clear();
            _pages.Clear();
            _pdfPath = null;
            DestroyRoot();
        }
    }
}
