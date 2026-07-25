using System.Collections.Generic;
using Assets.Scripts;              // CursorManager (world-pick block while the popup is up)
using Assets.Scripts.Objects;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// The profile-assign dropdown for the Universal Inventory's PROFILE MODE (design O4a): click
    /// a bag's chip and this small themed list opens under it — "(no profile)" plus every loaded
    /// <see cref="BagProfileStore"/> profile, the current assignment marked. Selecting a row writes
    /// ONE per-save assignment through <see cref="BagProfileStore.Assign"/> (config state only —
    /// nothing in the world moves) and bumps <see cref="GridProfileMode.Version"/> so every chip
    /// and badge refreshes on the next Tick. NO radial is involved (FlorpyDorp Q1).
    ///
    /// <para>Lifetime: TRANSIENT BY CONSTRUCTION — the whole canvas is built on <see cref="Open"/>
    /// and destroyed on <see cref="Close"/>, so no pooled UI can strand across a hot reload; the
    /// only statics are the object handles themselves, nulled by every Close, and
    /// <see cref="Shutdown"/> IS Close. A full-screen transparent scrim behind the panel gives the
    /// dropdown its modality: any click outside the list closes it (and reaches nothing under it),
    /// and vanilla's world-pick is blocked the whole time (<c>BlockCursorRaycast</c>, re-asserted
    /// per frame, released on Close — the TheGridPanel idiom). Cursor: registers NO modal; it can
    /// only open from an interactive click and closes itself the moment the Grid stops being
    /// interactive or profile mode ends (<see cref="Tick"/>, pumped by TheGridPanel).</para>
    ///
    /// <para>ASCII only in every displayed string (profile names are ASCII-sanitised at creation;
    /// the "(no profile)" and "*" marker literals are ASCII).</para>
    /// </summary>
    public static class GridProfilePopup
    {
        private const float RowH = 20f;
        private const float PadAll = 6f;
        private const float MinW = 150f;
        private const float MaxW = 320f;
        /// <summary>Above both windows (5020/5030) and the tab drag ghost (5100); below the
        /// capture confirm panel (5200), which is the more modal of the two.</summary>
        private const int SortOrder = 5150;

        private static GameObject _root;
        private static RectTransform _panel;
        private static PanelGraphic _panelBg;
        private static RectTransform _content;
        private static DynamicThing _bag;
        private static bool _open;
        private static bool _blockHeld;
        private static float _w, _h;

        private sealed class Row
        {
            public RectTransform Rect;
            public TextMeshProUGUI Label;
            public RowClick Click;
            public bool IsCurrent;
            public bool IsNone;
        }

        private static readonly List<Row> _rows = new List<Row>(12);

        /// <summary>True while the popup is on screen (TheGridPanel's Esc chain checks it).</summary>
        public static bool IsOpen { get { return _open; } }

        /// <summary>Open the list for <paramref name="bag"/>, anchored just under the chip's
        /// screen-space centre. Re-opening (same or another chip) rebuilds from scratch.</summary>
        public static void Open(DynamicThing bag, Vector2 chipScreenPos)
        {
            Close();
            if (bag == null) return;
            _bag = bag;

            string current = null;
            try { current = BagProfileStore.GetAssignedProfileName(bag); } catch { }

            BuildShell();
            if (_root == null) { _bag = null; return; }

            // Rows: "(no profile)" first, then every loaded profile in store order.
            AddRow(null, "(no profile)", string.IsNullOrEmpty(current));
            var profiles = BagProfileStore.Profiles;
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                if (p == null || string.IsNullOrEmpty(p.Name)) continue;
                AddRow(p.Name, p.Name, p.Name == current);
            }

            // Measure the widest label, then size + place everything (rows already exist, so the
            // TMP metrics come from the real font).
            float maxLabel = 60f;
            for (int i = 0; i < _rows.Count; i++)
            {
                var l = _rows[i].Label;
                if (l == null) continue;
                HudText.Sync(l);
                Vector2 m = l.GetPreferredValues(l.text);
                if (!float.IsNaN(m.x) && !float.IsInfinity(m.x)) maxLabel = Mathf.Max(maxLabel, m.x);
            }
            _w = Mathf.Clamp(maxLabel + PadAll * 2f + 16f, MinW, MaxW);
            float contentH = _rows.Count * RowH;
            _h = Mathf.Min(PadAll * 2f + contentH, Screen.height * 0.7f);   // overflow scrolls

            float contentW = _w - PadAll * 2f;
            for (int i = 0; i < _rows.Count; i++)
            {
                var rt = _rows[i].Rect;
                if (rt == null) continue;
                rt.anchoredPosition = new Vector2(0f, -i * RowH);
                rt.sizeDelta = new Vector2(contentW, RowH);
            }
            _content.sizeDelta = new Vector2(contentW, contentH);

            // Panel top-left just below the chip, clamped on-screen (screen Y is bottom-up; the
            // stored layout Y grows downward, the Grid convention).
            float x = chipScreenPos.x - 10f;
            float yTop = Screen.height - chipScreenPos.y + 12f;
            x = Mathf.Clamp(x, 0f, Mathf.Max(0f, Screen.width - _w));
            yTop = Mathf.Clamp(yTop, 0f, Mathf.Max(0f, Screen.height - _h));
            _panel.anchoredPosition = new Vector2(x + _w * 0.5f, -(yTop + _h * 0.5f));   // centre pivot
            _panel.sizeDelta = new Vector2(_w, _h);

            _open = true;
        }

        /// <summary>Pumped by TheGridPanel every open Tick: auto-close when the mode or the mouse
        /// goes away, restyle to the live theme, repaint row hovers, and hold the world-pick block
        /// (the scrim eats every click, so none of them may also pick the world).</summary>
        public static void Tick()
        {
            if (!_open) return;
            if (_root == null) { _open = false; return; }
            if (!GridProfileMode.Active || !TheGridPanel.IsInteractive) { Close(); return; }

            // Region surface: a themed interior box (no glow halo / frost — this is a transient
            // dropdown, not a window shell). Dirty-guarded downstream, so per-frame is free.
            GridTheme.ApplyBox(_panelBg, _w, _h, GridTheme.GridSurface.Region, false);

            Color text = GridTheme.Text;
            Color accent = HudPalette.LineAccent != null ? HudPalette.LineAccent.Value : text;
            Color muted = HudPalette.TextLabel != null ? HudPalette.TextLabel.Value : text;
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                if (r == null || r.Label == null) continue;
                bool hover = r.Click != null && r.Click.Hover;
                Color c = hover || r.IsCurrent ? accent : (r.IsNone ? muted : text);
                r.Label.color = c;   // Graphic.color is equality-guarded: free when unchanged
            }

            _blockHeld = true;
            Core.CursorBlockArbiter.Hold("profilepopup");
        }

        /// <summary>Tear the popup down (idempotent). Unassigns any shared glass material BEFORE
        /// the destroy so HudFxMaterials keeps no dead key, and releases the world-pick block.</summary>
        public static void Close()
        {
            _open = false;
            _bag = null;
            _rows.Clear();
            ReleaseBlock();
            if (_panelBg != null) HudFxMaterials.Unassign(_panelBg);
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _panel = null;
            _panelBg = null;
            _content = null;
            _w = 0f;
            _h = 0f;
        }

        /// <summary>Hot-reload teardown — the popup is transient, so this IS <see cref="Close"/>.</summary>
        public static void Shutdown()
        {
            Close();
        }

        // ---------- internals ----------

        private static void ReleaseBlock()
        {
            if (!_blockHeld) return;
            _blockHeld = false;
            Core.CursorBlockArbiter.Release("profilepopup");
        }

        /// <summary>Canvas + scrim + panel + scroll shell (no rows yet).</summary>
        private static void BuildShell()
        {
            _root = new GameObject("UIAscended_GridProfilePopup");
            Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortOrder;
            // The extra vertex streams the shared glass materials may consume (the same opt-in
            // every Grid canvas makes — see TheGridPanel.EnsureBuilt for the full rationale).
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.TexCoord2
                | AdditionalCanvasShaderChannels.TexCoord3
                | AdditionalCanvasShaderChannels.Normal
                | AdditionalCanvasShaderChannels.Tangent;
            _root.AddComponent<GraphicRaycaster>();

            // Scrim: full-screen, invisible, raycast-eating. Clicking anywhere outside the list
            // closes the popup — the standard dropdown modality.
            var sGo = new GameObject("Scrim", typeof(RectTransform));
            sGo.transform.SetParent(_root.transform, false);
            var srt = (RectTransform)sGo.transform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;
            var sImg = sGo.AddComponent<Image>();
            sImg.color = new Color(0f, 0f, 0f, 0f);   // alpha 0 still raycasts (rect hit test)
            sImg.raycastTarget = true;
            sGo.AddComponent<ScrimClick>();

            // The list panel (centre pivot, top-left anchor — the Grid convention).
            var pGo = new GameObject("Panel", typeof(RectTransform));
            pGo.transform.SetParent(_root.transform, false);
            _panel = (RectTransform)pGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 1f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panelBg = pGo.AddComponent<PanelGraphic>();
            _panelBg.raycastTarget = true;

            // Scroll shell: viewport (masked, with a transparent wheel-hit) -> content -> rows.
            var vGo = new GameObject("Viewport", typeof(RectTransform));
            vGo.transform.SetParent(_panel, false);
            var vrt = (RectTransform)vGo.transform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.pivot = new Vector2(0.5f, 0.5f);
            vrt.offsetMin = new Vector2(PadAll, PadAll);
            vrt.offsetMax = new Vector2(-PadAll, -PadAll);
            vGo.AddComponent<RectMask2D>();

            var hGo = new GameObject("ScrollHit", typeof(RectTransform));
            hGo.transform.SetParent(vrt, false);
            var hrt = (RectTransform)hGo.transform;
            hrt.anchorMin = Vector2.zero;
            hrt.anchorMax = Vector2.one;
            hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.offsetMin = Vector2.zero;
            hrt.offsetMax = Vector2.zero;
            var hImg = hGo.AddComponent<Image>();
            hImg.color = new Color(0f, 0f, 0f, 0f);
            hImg.raycastTarget = true;

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vrt, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.anchoredPosition = Vector2.zero;

            var scroll = vGo.AddComponent<ScrollRect>();
            scroll.viewport = vrt;
            scroll.content = _content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            scroll.inertia = false;
        }

        private static void AddRow(string profileName, string label, bool isCurrent)
        {
            var row = new Row();
            row.IsCurrent = isCurrent;
            row.IsNone = profileName == null;

            var rGo = new GameObject("Row", typeof(RectTransform));
            rGo.transform.SetParent(_content, false);
            row.Rect = (RectTransform)rGo.transform;
            row.Rect.anchorMin = row.Rect.anchorMax = new Vector2(0f, 1f);
            row.Rect.pivot = new Vector2(0f, 1f);

            var img = rGo.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);   // hit surface only
            img.raycastTarget = true;
            row.Click = rGo.AddComponent<RowClick>();
            row.Click.ProfileName = profileName;

            row.Label = HudText.Make(row.Rect, "Label", HudText.Size(11f),
                TextAlignmentOptions.Left, warp: false);
            row.Label.overflowMode = TextOverflowModes.Truncate;   // never "..." — the glyph tofus
            var lrt = row.Label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = new Vector2(6f, 0f);
            lrt.offsetMax = new Vector2(-4f, 0f);
            HudText.Set(row.Label, isCurrent ? "* " + label : label);   // ASCII marker for "assigned now"

            _rows.Add(row);
        }

        /// <summary>A row was clicked: write the assignment (null clears it), signal the chrome,
        /// close. The bag reference is captured before Close nulls it.</summary>
        private static void RowClicked(string profileName)
        {
            var bag = _bag;
            Close();
            if (bag == null) return;
            try { BagProfileStore.Assign(bag, profileName); } catch { }
            GridProfileMode.BumpVersion();
        }

        /// <summary>Left-click surface for one row. Instance data is just the profile name; the
        /// action routes to the static owner, so a destroyed popup strands nothing.</summary>
        private sealed class RowClick : MonoBehaviour,
            IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public string ProfileName;
            public bool Hover;

            public void OnPointerClick(PointerEventData e)
            {
                if (e == null || e.button != PointerEventData.InputButton.Left) return;
                RowClicked(ProfileName);
            }

            public void OnPointerEnter(PointerEventData e) { Hover = true; }
            public void OnPointerExit(PointerEventData e) { Hover = false; }
        }

        /// <summary>The click-outside-closes surface.</summary>
        private sealed class ScrimClick : MonoBehaviour, IPointerClickHandler
        {
            public void OnPointerClick(PointerEventData e)
            {
                Close();
            }
        }
    }
}
