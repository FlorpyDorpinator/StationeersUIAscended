using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// The Control Center's COMPOSITE components (F10 plan §7 / Kit v2): the pieces every tab
    /// used to hand-roll, built once. Section cards with a remembered "More options" disclosure,
    /// the segmented control, the kebab MenuButton and its transient popup, the inline
    /// ConfirmButton (one confirm idiom instead of three), the (i) Popover, the per-page
    /// InlineStatus line, and the FolderTabs strip (the manila-folder tab language from
    /// mockup 3, selected tab merging into the panel below).
    ///
    /// <para>Popups (menu / popover) are TRANSIENT BY CONSTRUCTION — built on open, destroyed on
    /// close, statics nulled (the GridProfilePopup pattern), mounted on
    /// <see cref="UiaControls.PopupLayer"/> behind a full-layer click-catcher so any outside
    /// click closes them. <see cref="CloseTransients"/> is called by the window's Close/Restyle
    /// and <see cref="Reset"/> by Shutdown, so nothing can strand across a hot reload.</para>
    ///
    /// <para>All colours read <see cref="UiaTheme"/>; a theme restyle rebuilds the menu (and
    /// with it everything here), the kit-wide contract.</para>
    /// </summary>
    public static class UiaComposite
    {
        // ================= transient popup plumbing (shared by MenuButton + Popover) =================

        private static GameObject _popup;
        private static GameObject _catcher;

        /// <summary>Close any open kebab menu / popover (idempotent — safe when the popup layer
        /// was already torn down under us, e.g. the window's Close destroyed its children).</summary>
        public static void CloseTransients()
        {
            if (_popup != null) UnityEngine.Object.Destroy(_popup);
            if (_catcher != null) UnityEngine.Object.Destroy(_catcher);
            _popup = null;
            _catcher = null;
        }

        /// <summary>Close the open kebab menu / popover for the window's Esc chain. True =
        /// one was open (Esc consumed). A popup destroyed under us (the layer sweep) counts as
        /// closed but still nulls the stale statics.</summary>
        internal static bool CloseTopTransient()
        {
            if (_popup == null)   // Unity-null: also true for a destroyed-but-referenced popup
            {
                _popup = null;
                _catcher = null;
                return false;
            }
            CloseTransients();
            return true;
        }

        /// <summary>The one currently-armed inline ConfirmButton (arming a second disarms
        /// nothing — each drives its own 5s clock — but Esc only needs the most recent).
        /// Cleared by Disarm and Reset; a destroyed driver reads as Unity-null.</summary>
        internal static ConfirmDriver ArmedConfirm;

        /// <summary>Esc chain: disarm the armed ConfirmButton, if any. True = consumed. A
        /// driver that already disarmed itself (timeout, OnDisable) consumes nothing.</summary>
        internal static bool DisarmArmedConfirm()
        {
            var d = ArmedConfirm;
            ArmedConfirm = null;
            if (d == null || !d.Armed) return false;   // none, died with its page, or lapsed
            d.Disarm();
            return true;
        }

        /// <summary>Hot-reload / shutdown teardown (UiaControlCenter.Shutdown).</summary>
        public static void Reset()
        {
            CloseTransients();
            ArmedConfirm = null;
        }

        /// <summary>Mount a fresh popup shell (bg + outline + catcher) on the popup layer and
        /// return its RectTransform, or null when no layer exists yet. The caller fills and
        /// then places it with <see cref="PlacePopup"/>.</summary>
        private static RectTransform OpenPopupShell(out RectTransform layer)
        {
            layer = UiaControls.PopupLayer;
            if (layer == null) return null;
            CloseTransients();

            _catcher = UiaUi.Go("kit-popup-catcher", layer);
            var cimg = _catcher.AddComponent<Image>();
            cimg.color = new Color(0f, 0f, 0f, 0.001f);
            UiaUi.Fill((RectTransform)_catcher.transform);
            _catcher.transform.SetAsLastSibling();
            _catcher.AddComponent<UiaControls.UiaButton>()
                .Init(cimg, cimg.color, cimg.color, cimg.color).OnClick = CloseTransients;

            _popup = UiaUi.Go("kit-popup", layer);
            var prt = (RectTransform)_popup.transform;
            var bg = _popup.AddComponent<Image>();
            bg.color = UiaTheme.PanelRaised;
            UiaImages.Round(bg);
            UiaUi.OutlineOf(bg, UiaTheme.Border, 1f);
            prt.SetAsLastSibling();
            return prt;
        }

        /// <summary>Place a popup of the given size beside <paramref name="anchor"/> on
        /// <paramref name="layer"/>: preferred position is below, left-aligned; it clamps
        /// horizontally at the layer edges and FLIPS above the anchor when the space below is
        /// too short and above is roomier. Shared by the kebab menu, the popover, the search
        /// results and Dropdown v2 (same math everywhere = same feel everywhere).</summary>
        internal static void PlacePopup(RectTransform popup, RectTransform anchor,
            RectTransform layer, float width, float height)
        {
            if (popup == null || anchor == null || layer == null) return;
            Vector3[] corners = new Vector3[4];
            anchor.GetWorldCorners(corners);   // 0=BL,1=TL,2=TR,3=BR
            Vector2 blLocal, trLocal;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(layer,
                RectTransformUtility.WorldToScreenPoint(null, corners[0]), null, out blLocal);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(layer,
                RectTransformUtility.WorldToScreenPoint(null, corners[2]), null, out trLocal);

            Rect lr = layer.rect;
            float spaceBelow = blLocal.y - lr.yMin - 6f;
            float spaceAbove = lr.yMax - trLocal.y - 6f;
            bool flipUp = height > spaceBelow && spaceAbove > spaceBelow;
            if (flipUp) height = Mathf.Min(height, Mathf.Max(60f, spaceAbove));
            else height = Mathf.Min(height, Mathf.Max(60f, spaceBelow));

            float x = blLocal.x;
            if (x + width > lr.xMax - 4f) x = lr.xMax - 4f - width;
            if (x < lr.xMin + 4f) x = lr.xMin + 4f;

            popup.anchorMin = popup.anchorMax = new Vector2(0.5f, 0.5f);
            popup.pivot = flipUp ? new Vector2(0f, 0f) : new Vector2(0f, 1f);
            popup.anchoredPosition = flipUp
                ? new Vector2(x, trLocal.y + 2f)
                : new Vector2(x, blLocal.y - 2f);
            popup.sizeDelta = new Vector2(width, height);
        }

        // ================= Section =================

        /// <summary>The live handle a <see cref="Section"/> returns. Rows go into
        /// <see cref="Body"/>; disclosure rows into <see cref="Extras"/> (null when the section
        /// was built without More options). <see cref="SetExpanded"/> also persists the state,
        /// and is what a search jump uses to reveal a row hiding in a collapsed disclosure.</summary>
        public sealed class SectionHandle
        {
            public string Id;
            public RectTransform Root;
            public RectTransform Body;
            public RectTransform Extras;
            internal UiaIconGraphic Chevron;
            internal GameObject ExtrasGo;

            public bool Expanded => ExtrasGo != null && ExtrasGo.activeSelf;

            public void SetExpanded(bool open)
            {
                if (ExtrasGo == null) return;
                if (ExtrasGo.activeSelf != open) ExtrasGo.SetActive(open);
                if (Chevron != null)
                    Chevron.Configure(open ? UiaIcon.ChevronUp : UiaIcon.ChevronDown);
                UiaMenuPrefs.SetDisclosure(Id, open);
            }
        }

        /// <summary>A titled card group: rounded themed card, accent title, optional (i) help
        /// popover, optional "More options" disclosure whose open state is remembered per
        /// <paramref name="id"/> across launches (<see cref="UiaMenuPrefs"/>). The disclosure
        /// toggles child visibility IN PLACE — no Refresh, so page state is untouched.</summary>
        public static SectionHandle Section(Transform parent, string id, string title,
            string help = null, bool moreOptions = false)
        {
            var h = new SectionHandle();
            h.Id = id;

            var cardGo = UiaUi.Go("section-" + (id ?? "x"), parent);
            h.Root = (RectTransform)cardGo.transform;
            var bg = cardGo.AddComponent<Image>();
            bg.color = UiaTheme.Panel;
            UiaImages.Round(bg);
            cardGo.AddComponent<UiaGlassSkin>();
            // No ContentSizeFitter anywhere in the card: a VerticalLayoutGroup is itself an
            // ILayoutElement, so the scroll content's VLayout reads the card's preferred height
            // straight off this layout (nested fitters inside layout groups fight the system).
            UiaUi.VLayout(h.Root, 6f, 10, 10, 8, 10);

            // Header row: title (accent, spaced caps — the kit Header voice) + optional (i).
            var headGo = UiaUi.Go("head", cardGo.transform);
            UiaUi.Size(headGo, 24f);
            UiaUi.HLayout((RectTransform)headGo.transform, UiaTheme.Gap);
            var t = UiaUi.Text(headGo.transform, (title ?? "").ToUpperInvariant(),
                UiaTheme.SmallSize, UiaTheme.Accent, TextAlignmentOptions.BottomLeft);
            t.characterSpacing = 6f;
            var tle = t.gameObject.AddComponent<LayoutElement>(); tle.flexibleWidth = 1f;
            if (!string.IsNullOrEmpty(help)) InfoButton(headGo.transform, help);

            // Hairline under the header.
            var line = UiaUi.Image(cardGo.transform, UiaTheme.Divider, "rule");
            UiaUi.Size(line.gameObject, 1f);

            // Body.
            var bodyGo = UiaUi.Go("body", cardGo.transform);
            h.Body = (RectTransform)bodyGo.transform;
            UiaUi.VLayout(h.Body, 6f);

            if (moreOptions)
            {
                bool open = UiaMenuPrefs.GetDisclosure(id);

                // Disclosure row: drawn chevron + "More options", the whole row clickable.
                var discGo = UiaUi.Go("disclose", cardGo.transform);
                UiaUi.Size(discGo, 22f);
                var dimg = discGo.AddComponent<Image>();
                dimg.color = new Color(0f, 0f, 0f, 0.001f);   // hit surface only
                var dh = UiaUi.HLayout((RectTransform)discGo.transform, 6f);
                var chevHost = UiaUi.Go("chev", discGo.transform);
                UiaUi.Size(chevHost, 16f, 16f);
                h.Chevron = UiaIcons.Attach(chevHost.transform,
                    open ? UiaIcon.ChevronUp : UiaIcon.ChevronDown, 12f, UiaTheme.TextDim);
                var dt = UiaUi.Text(discGo.transform, "More options", UiaTheme.SmallSize,
                    UiaTheme.TextDim, TextAlignmentOptions.Left);
                var dtle = dt.gameObject.AddComponent<LayoutElement>(); dtle.flexibleWidth = 1f;

                // Extras container (rows added by the caller; toggled inactive when collapsed —
                // layout groups skip inactive children, so collapse is free).
                var exGo = UiaUi.Go("extras", cardGo.transform);
                h.ExtrasGo = exGo;
                h.Extras = (RectTransform)exGo.transform;
                UiaUi.VLayout(h.Extras, 6f);
                exGo.SetActive(open);

                var handleRef = h;
                discGo.AddComponent<UiaControls.UiaButton>()
                    .Init(dimg, dimg.color, dimg.color, dimg.color)
                    .OnClick = () => handleRef.SetExpanded(!handleRef.Expanded);
            }

            return h;
        }

        // ================= Segmented control =================

        public sealed class SegmentedHandle
        {
            internal List<SegChip> Chips;
            public int Selected;

            public void SetSelected(int index)
            {
                Selected = index;
                if (Chips == null) return;
                for (int i = 0; i < Chips.Count; i++)
                    if (Chips[i] != null) Chips[i].SetSelected(i == index);
            }
        }

        /// <summary>N options, one selected (the Smart Stow Simple/Complex switch, and any
        /// hand-built strip that isn't a folder tab). Clicking the already-selected segment is a
        /// no-op; <paramref name="onChanged"/> fires with the new index otherwise. The selected
        /// segment is the concept's warm orange chip with a soft inner glow, the rest are dark
        /// teal chips; every chip is a SEPARATE rounded rect with a thin rim.
        ///
        /// <para>Concept round (2026-09-26, "make the lines around simple/complex thinner and
        /// look more like the concept"): the opaque trough behind the chips is gone — the
        /// concept shows two free-standing chips with a clear gap (~4px, ~3.7% of a 108px chip)
        /// joined only by a faint hairline along their top and bottom edges. Outer padding
        /// dropped 2px -> 1px horizontally so the extra gap keeps the strip's total width (and
        /// every chip's size / hit area) exactly as before.</para></summary>
        public static SegmentedHandle SegmentedControl(Transform parent, string[] options,
            int selectedIndex, Action<int> onChanged, float segWidth = 110f, float height = -1f)
        {
            if (height <= 0f) height = UiaTheme.RowH;
            var h = new SegmentedHandle();
            h.Chips = new List<SegChip>(options != null ? options.Length : 0);

            const int PadX = 1, PadY = 2;
            const float Gap = 4f;
            var rowGo = UiaUi.Go("segmented", parent);
            UiaUi.Size(rowGo, height, flexW: 0f);
            UiaUi.HLayout((RectTransform)rowGo.transform, Gap, PadX, PadX, PadY, PadY);

            int n = options != null ? options.Length : 0;

            // The concept's faint connecting hairlines: in each gap, one 1px line along the
            // chips' top edge and one along their bottom edge, tucked a few px under each
            // neighbour (siblings BEFORE the chips, so the chips draw over the tucked ends).
            // Out of the layout (ignoreLayout) and non-raycast; theme-derived dim teal.
            Color bridge = UiaFolderPalette.ChipBridge;
            const float Tuck = 4f;
            for (int i = 0; i + 1 < n; i++)
            {
                float x = PadX + (i + 1) * segWidth + i * Gap - Tuck;
                for (int edge = 0; edge < 2; edge++)
                {
                    var line = UiaUi.Image(rowGo.transform, bridge, edge == 0 ? "seg-bridge-top" : "seg-bridge-bot");
                    line.raycastTarget = false;
                    line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    var lrt = (RectTransform)line.transform;
                    float ay = edge == 0 ? 1f : 0f;
                    lrt.anchorMin = lrt.anchorMax = new Vector2(0f, ay);
                    lrt.pivot = new Vector2(0f, ay);
                    lrt.sizeDelta = new Vector2(Gap + 2f * Tuck, 1f);
                    // The mesh-path rim sits just OUTSIDE the chip rect (0..~1px outward), so
                    // the hairline occupies that same 1px band: flush with the chips' rims.
                    lrt.anchoredPosition = new Vector2(x, edge == 0 ? -(PadY - 1f) : (PadY - 1f));
                }
            }

            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var chipGo = UiaUi.Go("seg", rowGo.transform);
                UiaUi.Size(chipGo, height - 2f * PadY, segWidth, flexW: 0f);
                var chip = chipGo.AddComponent<SegChip>();
                chip.Init(options[i], () =>
                {
                    if (h.Selected == idx) return;
                    h.SetSelected(idx);
                    if (onChanged != null) onChanged(idx);
                });
                h.Chips.Add(chip);
            }
            h.SetSelected(Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, n - 1)));
            return h;
        }

        /// <summary>One segment of the SegmentedControl, restyled to the concept's mode chips
        /// (concept round, 2026-09-26): each chip is a closed rounded rect with a HAIRLINE rim.
        /// The ACTIVE chip is the warm orange body (ChipActiveBody, ~(147,74,17) on the shipped
        /// accent) with a thin lighter-orange rim and a soft luminous INNER glow that brightens
        /// the body toward its rim (PanelGraphic.GlowInner — light, not a thick border); the
        /// resting chip is a near-black teal fill with a thin dim teal rim. Labels are the same
        /// neutral white in both states, REGULAR weight, centred — no bold, no drop edge.
        /// Everything stays on the STATIC mesh path (interior control: no moving light).</summary>
        internal sealed class SegChip : MonoBehaviour,
            IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            // Rim recipe. The mesh path draws a border as: fill -> (ramp over Feather) -> solid
            // BorderWidth -> (fade over Feather), so the perceived line is roughly
            // BorderWidth + Feather wide. The old 1.5px / 1px widths on the global ~1.25px
            // feather read as ~2.2-2.7px — "thick". 0.4px solid + a 0.7px per-chip feather
            // lands at ~1.1px, the concept's hairline, while keeping a real AA ramp (the canvas
            // has no MSAA; a feather much under ~0.6px starts to stair-step).
            private const float RimWidth = 0.4f;
            private const float RimFeather = 0.7f;
            // Selected chip's inner glow: ~6px deep, mostly diffuse so it wraps the perimeter
            // instead of hot-spotting at the key-light corner. Stronger/deeper (1.6 / 8px, full
            // diffuse) pinched visibly dark toward the four corners at 1x.
            private const float ActiveGlowIn = 1.1f;
            private const float ActiveGlowWidth = 6f;
            private const float ActiveGlowDiffuse = 0.7f;

            private UI.Hud.PanelGraphic _panel;
            private TextMeshProUGUI _label;
            private RectTransform _rt;
            private Action _onClick;
            private bool _selected;
            private bool _hover;

            internal void Init(string title, Action onClick)
            {
                _onClick = onClick;
                _rt = (RectTransform)transform;
                _panel = gameObject.AddComponent<UI.Hud.PanelGraphic>();
                _panel.raycastTarget = true;
                _label = UiaUi.Text(transform, title ?? "", UiaTheme.LabelSize, UiaFolderPalette.ChipLabel,
                    TextAlignmentOptions.Center);
                var lrt = (RectTransform)_label.transform;
                UiaUi.Fill(lrt);
                lrt.offsetMin = new Vector2(4f, 0f);
                lrt.offsetMax = new Vector2(-4f, 0f);
                _label.fontStyle = FontStyles.Normal;
                // No ellipsis: a long option shrinks toward 9pt, then wraps inside the chip.
                UiaControls.FitText(_label, 9f);
            }

            internal void SetSelected(bool s)
            {
                _selected = s;
            }

            private void LateUpdate()
            {
                if (_panel == null || _rt == null) return;
                var size = _rt.rect.size;
                if (size.x < 2f || size.y < 2f) return;
                if (_selected)
                {
                    _panel.color = UiaFolderPalette.ChipActiveBody;
                    _panel.BorderColor = UiaFolderPalette.ChipActiveRimLight;
                    _panel.GlowInner = ActiveGlowIn;
                    _panel.GlowWidth = ActiveGlowWidth;
                    _panel.GlowDiffuse = ActiveGlowDiffuse;
                    _panel.GlowExtraDiffuse = ActiveGlowDiffuse;
                }
                else
                {
                    Color fill = UiaFolderPalette.ChipRestFill;
                    if (_hover) fill = Color.Lerp(fill, UiaFolderPalette.ChipRestBorder, 0.18f);
                    _panel.color = fill;
                    _panel.BorderColor = UiaFolderPalette.ChipRestBorder;
                    _panel.GlowInner = 0f;
                }
                _panel.BorderWidth = RimWidth;
                _panel.FeatherOverride = RimFeather;
                _panel.Glow = 0f;          // no outer halo: it would bleed into the 4px gap
                _panel.Sheen = 0f;
                _panel.Spec = 0f;
                _panel.BorderSides = 15;   // a segment is a CLOSED chip; nothing to merge into
                _panel.DenseFill = true;   // star-shaped interior: the sparse fan creased the glow
                _panel.SetShape(size.x, size.y, 5.5f);   // the concept's chip radius
                // Static: the mode chips are interior controls, and only the tabs, the window
                // exterior and the title-bar row animate (FlorpyDorp, 2026-09-26).
                UiaGlassSkin.ApplySdfFlow(_panel, false);
                if (_label != null) _label.color = UiaFolderPalette.ChipLabel;
            }

            public void OnPointerClick(PointerEventData e)
            {
                if (e != null && e.button != PointerEventData.InputButton.Left) return;
                if (_onClick != null) _onClick();
            }

            public void OnPointerEnter(PointerEventData e) { _hover = true; }
            public void OnPointerExit(PointerEventData e) { _hover = false; }

            private void OnDestroy()
            {
                if (_panel != null) UI.Hud.HudFxMaterials.Unassign(_panel);
            }
        }

        // ================= MenuButton (kebab) =================

        /// <summary>One row of a kebab menu. Compose with the factory helpers; a row is exactly
        /// one of: a plain action, a danger action (optionally armed inline), a switch.</summary>
        public sealed class MenuItem
        {
            public string Label;
            public Action OnClick;
            public bool Danger;
            /// <summary>Danger rows only: first click arms the row into "Sure? [Yes] [Cancel]"
            /// INSIDE the popup; Yes fires <see cref="OnClick"/>.</summary>
            public bool Confirm;
            public Func<bool> SwitchGet;
            public Action<bool> SwitchSet;

            public static MenuItem Do(string label, Action onClick)
            {
                var m = new MenuItem(); m.Label = label; m.OnClick = onClick; return m;
            }

            public static MenuItem DangerDo(string label, Action onClick, bool confirm = true)
            {
                var m = new MenuItem();
                m.Label = label; m.OnClick = onClick; m.Danger = true; m.Confirm = confirm;
                return m;
            }

            public static MenuItem Switch(string label, Func<bool> get, Action<bool> set)
            {
                var m = new MenuItem(); m.Label = label; m.SwitchGet = get; m.SwitchSet = set; return m;
            }
        }

        /// <summary>A kebab button (three DRAWN dots) that opens a small themed popup menu
        /// anchored under it. <paramref name="items"/> is invoked at OPEN time, so the rows
        /// always reflect current state. Outside click closes; a destructive entry with
        /// Confirm arms inline inside the popup.</summary>
        public static UiaControls.UiaButton MenuButton(Transform parent, Func<List<MenuItem>> items,
            float width = 30f, float height = -1f)
        {
            if (height <= 0f) height = UiaTheme.RowH;
            var btn = UiaControls.Button(parent, "", null, width, height, UiaControls.ButtonStyle.Panel);
            UiaIcons.SetButtonIcon(btn, UiaIcon.Kebab, Mathf.Min(width, height) * 0.62f, UiaTheme.TextDim);
            var anchor = (RectTransform)btn.transform;
            btn.OnClick = () => OpenMenu(anchor, items);
            return btn;
        }

        private const float MenuRowH = 28f;

        private static void OpenMenu(RectTransform anchor, Func<List<MenuItem>> itemsProvider)
        {
            if (anchor == null || itemsProvider == null) return;
            List<MenuItem> items = null;
            try { items = itemsProvider(); } catch { }
            if (items == null || items.Count == 0) return;

            RectTransform layer;
            var prt = OpenPopupShell(out layer);
            if (prt == null) return;

            // Width: the longest label + chrome, measured with the real font.
            float maxLabel = 90f;
            var font = UiaTheme.Font();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null || string.IsNullOrEmpty(it.Label)) continue;
                // GetPreferredValues needs a live TMP; a conservative estimate avoids building
                // one just to measure: ~0.56em average advance at the kit's small size.
                float est = it.Label.Length * UiaTheme.SmallSize * 0.56f;
                if (it.SwitchGet != null) est += 56f;
                if (est > maxLabel) maxLabel = est;
            }
            float width = Mathf.Clamp(maxLabel + 24f, 150f, 340f);
            float height = items.Count * MenuRowH + 8f;

            BuildMenuRows(prt, items, -1);
            PlacePopup(prt, anchor, layer, width, height);
        }

        /// <summary>(Re)build the rows of the open kebab menu. <paramref name="armedIndex"/> is
        /// the row currently showing its inline "Sure? Yes / Cancel" (-1 = none). Rebuilding in
        /// place keeps the popup's position; only its children change.</summary>
        private static void BuildMenuRows(RectTransform prt, List<MenuItem> items, int armedIndex)
        {
            for (int i = prt.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(prt.GetChild(i).gameObject);

            var colGo = UiaUi.Go("rows", prt);
            var col = UiaUi.Fill((RectTransform)colGo.transform, 4f);
            UiaUi.VLayout(col, 0f);

            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                var it = items[i];
                if (it == null) continue;

                var rowGo = UiaUi.Go("mrow", colGo.transform);
                UiaUi.Size(rowGo, MenuRowH);
                var rimg = rowGo.AddComponent<Image>();
                rimg.color = new Color(0f, 0f, 0f, 0.001f);
                UiaImages.Round(rimg);
                UiaUi.HLayout((RectTransform)rowGo.transform, 6f, 8, 6, 0, 0);

                if (idx == armedIndex && it.Confirm)
                {
                    // The inline confirm face of a danger row.
                    var q = UiaUi.Text(rowGo.transform, "Sure?", UiaTheme.SmallSize,
                        UiaTheme.Critical, TextAlignmentOptions.Left);
                    var qle = q.gameObject.AddComponent<LayoutElement>(); qle.flexibleWidth = 1f;
                    UiaControls.Button(rowGo.transform, "Yes", () =>
                    {
                        CloseTransients();
                        if (it.OnClick != null) it.OnClick();
                    }, 52f, MenuRowH - 6f, UiaControls.ButtonStyle.Danger);
                    UiaControls.Button(rowGo.transform, "Cancel",
                        () => BuildMenuRows(prt, items, -1), 66f, MenuRowH - 6f);
                    continue;
                }

                var t = UiaUi.Text(rowGo.transform, it.Label ?? "", UiaTheme.SmallSize,
                    it.Danger ? UiaTheme.Critical : UiaTheme.Text, TextAlignmentOptions.Left);
                // No ellipsis: the popup is already sized to the longest label (clamped at
                // 340px); anything past that shrinks toward 9pt and wraps inside the 28px row.
                UiaControls.FitText(t, 9f);
                var tle = t.gameObject.AddComponent<LayoutElement>(); tle.flexibleWidth = 1f;

                if (it.SwitchGet != null)
                {
                    bool val = false;
                    try { val = it.SwitchGet(); } catch { }
                    UiaControls.Switch(rowGo.transform, val, v =>
                    {
                        if (it.SwitchSet != null) { try { it.SwitchSet(v); } catch { } }
                    });
                    // The row surface itself stays inert for switch rows (the switch is the target).
                    rowGo.AddComponent<UiaControls.UiaButton>()
                        .Init(rimg, rimg.color, UiaTheme.PanelHover, rimg.color).OnClick = null;
                }
                else
                {
                    rowGo.AddComponent<UiaControls.UiaButton>()
                        .Init(rimg, rimg.color, UiaTheme.PanelHover, rimg.color)
                        .OnClick = () =>
                        {
                            if (it.Danger && it.Confirm) { BuildMenuRows(prt, items, idx); return; }
                            CloseTransients();
                            if (it.OnClick != null) it.OnClick();
                        };
                }
            }
        }

        // ================= ConfirmButton =================

        /// <summary>The one inline confirm idiom (F10 plan pain 7): a danger button whose first
        /// click swaps it for "Sure? [Yes] [Cancel]" in place. Auto-disarms after 5 seconds, on
        /// section collapse (OnDisable) and — by construction — on page leave, since the widget
        /// dies with the page. Yes fires <paramref name="onConfirm"/>.</summary>
        public static ConfirmDriver ConfirmButton(Transform parent, string label, Action onConfirm,
            float width = -1f, float height = -1f)
        {
            if (height <= 0f) height = UiaTheme.RowH;
            var hostGo = UiaUi.Go("confirm", parent);
            UiaUi.Size(hostGo, height, width, flexW: width < 0f ? 1f : 0f);
            UiaUi.HLayout((RectTransform)hostGo.transform, 6f);
            var drv = hostGo.AddComponent<ConfirmDriver>();
            drv.Init(label, onConfirm, height);
            return drv;
        }

        /// <summary>The ConfirmButton's state machine. No statics — arming state dies with the
        /// widget, which is exactly the disarm-on-page-leave contract.</summary>
        public sealed class ConfirmDriver : MonoBehaviour
        {
            private string _label;
            private Action _onConfirm;
            private float _height;
            private float _armedUntil;   // 0 = not armed (unscaled clock)
            private bool _armedFace;     // which children are currently built

            internal void Init(string label, Action onConfirm, float height)
            {
                _label = label;
                _onConfirm = onConfirm;
                _height = height;
                BuildFace(false);
            }

            public bool Armed => _armedUntil > 0f;

            public void Disarm()
            {
                _armedUntil = 0f;
                if (ReferenceEquals(ArmedConfirm, this)) ArmedConfirm = null;
                if (_armedFace) BuildFace(false);
            }

            private void Arm()
            {
                _armedUntil = Time.unscaledTime + 5f;
                ArmedConfirm = this;   // so the window's Esc disarms this, not closes itself
                BuildFace(true);
            }

            private void BuildFace(bool armed)
            {
                _armedFace = armed;
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    var c = transform.GetChild(i);
                    c.gameObject.SetActive(false);
                    Destroy(c.gameObject);
                }
                if (!armed)
                {
                    UiaControls.Button(transform, _label, Arm, -1f, _height,
                        UiaControls.ButtonStyle.Danger);
                    return;
                }
                var q = UiaUi.Text(transform, "Sure?", UiaTheme.LabelSize, UiaTheme.Critical,
                    TextAlignmentOptions.Left);
                var qle = q.gameObject.AddComponent<LayoutElement>(); qle.flexibleWidth = 1f;
                UiaControls.Button(transform, "Yes", () =>
                {
                    var act = _onConfirm;
                    Disarm();
                    if (act != null) act();
                }, 70f, _height, UiaControls.ButtonStyle.Danger);
                UiaControls.Button(transform, "Cancel", Disarm, 80f, _height);
            }

            private void Update()
            {
                if (_armedUntil > 0f && Time.unscaledTime > _armedUntil) Disarm();
            }

            private void OnDisable()
            {
                // A collapsing disclosure (or tab hide) must never leave a live armed nuke to
                // come back to. Only the DEADLINE drops here (no object churn during a
                // teardown-driven disable); _armedFace stays true so OnEnable knows the built
                // children are the stale armed face and rebuilds the safe one.
                _armedUntil = 0f;
            }

            private void OnEnable()
            {
                // Re-entering with the armed face still built (disabled mid-arm): rebuild the
                // disarmed face so the stale "Yes" can never fire.
                if (_armedFace && !Armed) BuildFace(false);
            }
        }

        // ================= Popover =================

        /// <summary>A small (i) button that opens a help popover anchored to itself. The one
        /// help affordance (F10 plan principle 6): short labels + (i), Notes only for warnings.</summary>
        public static UiaControls.UiaButton InfoButton(Transform parent, string helpText)
        {
            var btn = UiaControls.Button(parent, "", null, 22f, 22f, UiaControls.ButtonStyle.Panel);
            UiaIcons.SetButtonIcon(btn, UiaIcon.Info, 15f, UiaTheme.TextDim);
            var anchor = (RectTransform)btn.transform;
            btn.OnClick = () => Popover(anchor, helpText);
            return btn;
        }

        /// <summary>Open a themed help popover beside <paramref name="anchor"/>. Outside click
        /// closes it (same transient shell as the kebab menu — opening one closes the other).</summary>
        public static void Popover(RectTransform anchor, string text, float width = 300f)
        {
            if (anchor == null || string.IsNullOrEmpty(text)) return;
            RectTransform layer;
            var prt = OpenPopupShell(out layer);
            if (prt == null) return;

            var t = UiaUi.Text(prt, text, UiaTheme.SmallSize, UiaTheme.Text,
                TextAlignmentOptions.TopLeft, true);
            var trt = (RectTransform)t.transform;
            UiaUi.Fill(trt, 10f);

            // Measure the wrapped height with the real font at the target width.
            float innerW = width - 20f;
            Vector2 pref = t.GetPreferredValues(text, innerW, 0f);
            float height = Mathf.Clamp(pref.y + 20f, 40f, 380f);
            PlacePopup(prt, anchor, layer, width, height);
        }

        // ================= InlineStatus =================

        public enum StatusKind { Info, Good, Warn, Error }

        /// <summary>The live handle of an <see cref="InlineStatus"/> line.</summary>
        public sealed class InlineStatusHandle
        {
            internal TextMeshProUGUI Text;

            public void Show(string msg, StatusKind kind)
            {
                if (Text == null) return;
                bool has = !string.IsNullOrEmpty(msg);
                if (Text.gameObject.activeSelf != has) Text.gameObject.SetActive(has);
                if (!has) return;
                Text.text = msg;
                Text.color = ColorOf(kind);
            }

            public void Clear() { Show(null, StatusKind.Info); }

            internal static Color ColorOf(StatusKind kind)
            {
                switch (kind)
                {
                    case StatusKind.Good: return UiaTheme.Good;
                    case StatusKind.Warn: return UiaTheme.Warn;
                    case StatusKind.Error: return UiaTheme.Critical;
                    default: return UiaTheme.TextDim;
                }
            }
        }

        /// <summary>A one-line per-page status text with severity colour — the menu's feedback
        /// channel, next to the control that caused it, instead of a centre-screen toast
        /// (pain 17). Hidden while empty. Because pages REBUILD on Refresh, a page that needs
        /// its status to survive a rebuild keeps the (msg, kind) pair in its own state and
        /// passes it back as the initial value.</summary>
        public static InlineStatusHandle InlineStatus(Transform parent,
            string initialMsg = null, StatusKind initialKind = StatusKind.Info)
        {
            var h = new InlineStatusHandle();
            var t = UiaUi.Text(parent, "", UiaTheme.SmallSize, UiaTheme.TextDim,
                TextAlignmentOptions.Left, true);
            // No ContentSizeFitter: TMP reports its own preferred height to the parent layout
            // (a fitter here is the driven-transform conflict — see the Section card note).
            // The LayoutElement pins the one-line minimum against squash.
            var sle = t.gameObject.AddComponent<LayoutElement>();
            sle.minHeight = 18f;
            t.margin = new Vector4(2f, 2f, 2f, 2f);
            h.Text = t;
            t.gameObject.SetActive(false);
            if (!string.IsNullOrEmpty(initialMsg)) h.Show(initialMsg, initialKind);
            return h;
        }

        /// <summary>The concept's card seat (combined visual round): a 1px near-black
        /// contact edge plus a soft bottom-biased penumbra hanging BELOW the host — one
        /// stretched vertex-gradient, no blur shader. ignoreLayout, non-raycast; call it on
        /// any card or floating panel after its own children are built. Cards later in a
        /// list naturally draw over the shadow of the card above.</summary>
        public static void SoftShadow(Transform host, float height = 6f)
        {
            if (host == null) return;
            var go = UiaUi.Go("soft-shadow", host);
            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(-4f, height);
            var g = go.AddComponent<ChipGradientGraphic>();
            g.raycastTarget = false;
            g.Configure(new Color(0f, 0f, 0f, 0.35f), new Color(0f, 0f, 0f, 0f), 0f, 0f, 0f, 0f);
        }

        // ================= FolderTabs =================

        /// <summary>The live handle of a <see cref="FolderTabs"/> strip. <see cref="Content"/> is
        /// where the selected page builds; <see cref="SetSelected"/> repaints the strip, applies
        /// the folder z-order (unselected tabs under the collar band, the selected tab over it)
        /// and re-breaks the band's bright top line under the selected tab. It does NOT invoke
        /// the callback — the owner drives its own page switch.</summary>
        public sealed class FolderTabsHandle
        {
            public RectTransform Zone;      // the whole strip + card block (a layout child)
            public RectTransform Strip;
            public RectTransform Card;      // the frame-ringed panel the selected tab pours into
            public RectTransform Content;   // build pages here (inset inside the frame)
            internal List<FolderTab> Tabs;
            internal FolderFrameGraphic FrameG;
            internal float TabW, GapPx, InsetX;

            public void SetSelected(int index)
            {
                if (Tabs == null) return;
                for (int i = 0; i < Tabs.Count; i++)
                    if (Tabs[i] != null) Tabs[i].SetSelected(i == index);
                // The folder z-order, by sibling order (tabs are manually positioned, so
                // sibling order is PURE draw order): unselected tabs, then the one-piece
                // frame ring (their tuck), then the selected tab (through the ring's band).
                if (FrameG != null) FrameG.transform.SetAsLastSibling();
                if (index >= 0 && index < Tabs.Count && Tabs[index] != null)
                    Tabs[index].transform.SetAsLastSibling();
                // No line-break bookkeeping any more: the band carries no bright line (the
                // closer round) — the junction is colour continuity + the fillet feet.
            }
        }

        /// <summary>The manila folder-tab system, rebuilt 1:1 against the measured concept
        /// (2026-09-26 geometry round; every number below is from the pixel-sampled spec):
        ///
        /// <list type="bullet">
        /// <item>SELECTED tab: one filled path from its raised top (+3px above neighbours)
        /// down THROUGH the collar band to the band's bottom — no bottom edge; r9 top arcs,
        /// 13-degree side slants, concave fillets flaring +4px outward at the bottom so the
        /// side strokes hand off into the band's top-edge line as one polyline; 3-stop
        /// gradient ending ON the band body colour (no seam). Its own soft gap shadows are
        /// baked into the same mesh.</item>
        /// <item>THE BAND: 10px (sub) / 12px (main, dimmer olive + brighter line): bright
        /// top line / gradient body / lighter bottom lip; drawn OVER the unselected tabs
        /// (which extend down through it — the tuck) and UNDER the selected tab; the top
        /// line breaks at the selected tab's fillets and carries its 16px recovery shadow.</item>
        /// <item>UNSELECTED tabs: open-bottomed, r6 top corners, 15-degree slants, side
        /// strokes fading over the lower half, richer teal gradient.</item>
        /// <item>THE FRAME (collar round 2): band + side rails + bottom rail are ONE
        /// closed ring mesh (<see cref="FolderFrameGraphic"/>) — soft-shaded gold with
        /// rounded outer corners and a larger-radius inner cutout, no joints, no
        /// hairline strips; its inner-edge AO seats the dark panel, and content sits
        /// flush to the band bottom (the old void is gone).</item>
        /// </list>
        ///
        /// <para>The card is the ONE visible framed panel; pages draw no outer chrome.
        /// Content insets inside the card: left/right 16, top 8, bottom 12 (game px).</para>
        ///
        /// <para><paramref name="mainLevel"/> selects the main strip's deltas (12px olive
        /// band, brighter line, main-tab teals); the default is the sub-strip look, which is
        /// what a page's sub-tab strip gets without changing its call.</para></summary>
        public static FolderTabsHandle FolderTabs(Transform parent, string[] titles, int selected,
            Action<int> onSelect, float tabWidth = 150f, float tabHeight = -1f, bool mainLevel = false)
        {
            if (tabHeight <= 0f) tabHeight = UiaTheme.TabH;
            float bandTh = mainLevel ? 12f : 10f;
            const float Raise = 3f;      // the selected tab rides this much higher
            const float GapPx = 11f;     // measured inter-tab gap
            const float InsetX = 10f;    // first tab's offset from the card edge
            float stripH = Raise + tabHeight + bandTh;

            var h = new FolderTabsHandle();
            h.Tabs = new List<FolderTab>(titles != null ? titles.Length : 0);
            h.TabW = tabWidth;
            h.GapPx = GapPx;
            h.InsetX = InsetX;

            var zoneGo = UiaUi.Go("foldertabs", parent);
            h.Zone = (RectTransform)zoneGo.transform;
            UiaUi.Size(zoneGo, flexH: 1f);

            // Card FIRST (drawn under the strip): opaque fill + the gold collar rails. Its
            // top edge IS the band's bottom — content flush, no void.
            var cardGo = UiaUi.Go("card", zoneGo.transform);
            h.Card = (RectTransform)cardGo.transform;
            h.Card.anchorMin = Vector2.zero;
            h.Card.anchorMax = Vector2.one;
            h.Card.offsetMin = Vector2.zero;
            h.Card.offsetMax = new Vector2(0f, -stripH);
            var cardPanel = cardGo.AddComponent<UI.Hud.PanelGraphic>();
            cardPanel.raycastTarget = false;
            cardGo.AddComponent<FolderCardDriver>().Panel = cardPanel;

            // Content: flush under the frame's band (top 8) and HUGGING the ring's inner
            // edge (sides 12 = the side-member width, bottom 10 — final collar micros: the
            // concept nests the dark panel directly against the ring's inner curve so its
            // AO fade lands ON the panel, no window-fill gutter). O's pages build here and
            // draw no outer chrome; the square column fills vs the r14 inner corners leave
            // a couple px at the corners, which the AO covers.
            var contentGo = UiaUi.Go("card-content", cardGo.transform);
            h.Content = (RectTransform)contentGo.transform;
            h.Content.anchorMin = Vector2.zero;
            h.Content.anchorMax = Vector2.one;
            h.Content.offsetMin = new Vector2(12f, 10f);
            h.Content.offsetMax = new Vector2(-12f, -8f);

            // Overlay SECOND: spans the WHOLE zone (the frame ring wraps the card), NO
            // layout group — tabs are manually placed so sibling order is pure z-order.
            var stripGo = UiaUi.Go("strip", zoneGo.transform);
            h.Strip = (RectTransform)stripGo.transform;
            UiaUi.Fill(h.Strip);

            // Opaque backdrop behind the TAB AREA only (down to the band top): the gap
            // floor reads as the window surface, and the world can never bleed through the
            // spaces between tabs. Below the band top the frame + card are the surface.
            var bdGo = UiaUi.Go("backdrop", stripGo.transform);
            var bdRt = (RectTransform)bdGo.transform;
            bdRt.anchorMin = new Vector2(0f, 1f);
            bdRt.anchorMax = new Vector2(1f, 1f);
            bdRt.pivot = new Vector2(0.5f, 1f);
            bdRt.anchoredPosition = Vector2.zero;
            bdRt.sizeDelta = new Vector2(0f, Raise + tabHeight);
            var bdImg = bdGo.AddComponent<Image>();
            Color bdc = UiaTheme.Window; bdc.a = 1f;
            bdImg.color = bdc;
            bdImg.raycastTarget = false;

            int n = titles != null ? titles.Length : 0;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var tabGo = UiaUi.Go("tab", stripGo.transform);
                var trt = (RectTransform)tabGo.transform;
                trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
                trt.pivot = new Vector2(0f, 1f);
                trt.anchoredPosition = new Vector2(InsetX + i * (tabWidth + GapPx), 0f);
                trt.sizeDelta = new Vector2(tabWidth, stripH);
                var tab = tabGo.AddComponent<FolderTab>();
                tab.Init(titles[i], mainLevel, tabHeight, bandTh, Raise, () =>
                {
                    if (onSelect != null) onSelect(idx);
                });
                h.Tabs.Add(tab);
            }

            // The frame (collar round 2 + ring-topology fix): the MAIN level draws the ONE
            // closed ring (band + rails + bottom as a single mesh — no joints, no square
            // ends, it curves around the content by construction). A SUB level draws ONLY
            // its band, as a crossbar whose ends overhang to melt into the outer ring's
            // side rails — nesting two full rings doubled every rail into a grooved pair.
            // Either way: drawn over the unselected tabs' tucks, under the selected tab.
            var frameGo = UiaUi.Go("frame", stripGo.transform);
            var frt = (RectTransform)frameGo.transform;
            if (mainLevel)
            {
                UiaUi.Fill(frt);
                frt.offsetMax = new Vector2(0f, -(Raise + tabHeight));
            }
            else
            {
                frt.anchorMin = new Vector2(0f, 1f);
                frt.anchorMax = new Vector2(1f, 1f);
                frt.pivot = new Vector2(0.5f, 1f);
                frt.anchoredPosition = new Vector2(0f, -(Raise + tabHeight));
                frt.sizeDelta = new Vector2(0f, bandTh);
            }
            h.FrameG = frameGo.AddComponent<FolderFrameGraphic>();
            h.FrameG.raycastTarget = false;
            h.FrameG.SetThickness(bandTh, 12f, 10f);
            h.FrameG.SetCrossbar(!mainLevel, 8f);
            var fDriver = frameGo.AddComponent<FolderFrameDriver>();
            fDriver.G = h.FrameG;
            fDriver.MainLevel = mainLevel;

            h.SetSelected(Mathf.Clamp(selected, 0, Mathf.Max(0, n - 1)));
            return h;
        }

        /// <summary>Pushes the live palette recipes onto the one-piece frame ring each
        /// frame (SetColors dirty-guards, so an unchanged theme costs three colour
        /// compares). Both levels share the soft FrameTop/FrameBot ramp; the surviving
        /// bright band line keeps its main/sub delta.</summary>
        internal sealed class FolderFrameDriver : MonoBehaviour
        {
            internal FolderFrameGraphic G;
            internal bool MainLevel;

            private void LateUpdate()
            {
                if (G == null) return;
                G.SetColors(UiaFolderPalette.FrameTop, UiaFolderPalette.FrameBot);
            }
        }

        /// <summary>The chip fill gradient: a vertex-coloured mesh tracing the chip's own
        /// silhouette (trapezoid slants + rounded corner arcs), sliced into horizontal bands
        /// whose colours follow the measured curve — flat at the top colour through
        /// <c>plateau</c>, then pow-1.35 roll-off to the bottom colour. Also serves as the
        /// kit's generic vertical-gradient strip (zero inset, zero corners): the collar
        /// rails and the AO washes are this same graphic with plateau 0. Opaque or alpha
        /// colours both work (Color.Lerp carries alpha); non-raycast; Configure is fully
        /// dirty-guarded, and a rebuild allocates nothing.</summary>
        internal sealed class ChipGradientGraphic : MaskableGraphic
        {
            // Row stops from the top (t=0) to the bottom (t=1): dense through the corner
            // zone so the arcs trace cleanly, spaced below where the ramp is smooth.
            private static readonly float[] RowT = { 0f, 0.06f, 0.15f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f, 1f };

            private Color _top = Color.white, _bottom = Color.black;
            private Color _mid;
            private bool _hasMid;
            private float _topInset;
            private float _cornerR;
            private float _bottomCornerR;
            private float _plateau = 0.35f;

            public void Configure(Color top, Color bottom, float topInset, float cornerR,
                float plateau, float bottomCornerR = 0f)
            {
                if (!_hasMid && _top == top && _bottom == bottom
                    && Mathf.Approximately(_topInset, topInset)
                    && Mathf.Approximately(_cornerR, cornerR)
                    && Mathf.Approximately(_bottomCornerR, bottomCornerR)
                    && Mathf.Approximately(_plateau, plateau)) return;
                _hasMid = false;
                _top = top;
                _bottom = bottom;
                _topInset = topInset;
                _cornerR = cornerR;
                _bottomCornerR = bottomCornerR;
                _plateau = plateau;
                SetVerticesDirty();
            }

            /// <summary>Three-stop variant (the measured selected-tab ramp: top / 50% / bottom,
            /// piecewise linear — no plateau): used by the folder tabs' fill overlay.</summary>
            public void ConfigureThreeStop(Color top, Color mid, Color bottom,
                float topInset, float cornerR, float bottomCornerR = 0f)
            {
                if (_hasMid && _top == top && _mid == mid && _bottom == bottom
                    && Mathf.Approximately(_topInset, topInset)
                    && Mathf.Approximately(_cornerR, cornerR)
                    && Mathf.Approximately(_bottomCornerR, bottomCornerR)) return;
                _hasMid = true;
                _top = top;
                _mid = mid;
                _bottom = bottom;
                _topInset = topInset;
                _cornerR = cornerR;
                _bottomCornerR = bottomCornerR;
                _plateau = 0f;
                SetVerticesDirty();
            }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                var r = GetPixelAdjustedRect();
                float w = r.width, h = r.height;
                if (w < 2f || h < 2f) return;
                const float EdgeInset = 0.75f;
                float yTop = r.yMax - EdgeInset;
                float yBot = r.yMin + EdgeInset;
                float cxm = r.center.x;
                float cy = r.yMax - _cornerR;   // corner-arc centre height

                for (int i = 0; i < RowT.Length; i++)
                {
                    float t = RowT[i];
                    float y = Mathf.Lerp(yTop, yBot, t);
                    // Half-width at this height: the slant, clamped by the corner arcs.
                    float tb = Mathf.Clamp01((y - r.yMin) / h);   // 0 bottom .. 1 top
                    float xr = w * 0.5f - _topInset * tb;
                    if (_cornerR > 0.5f && y > cy)
                    {
                        float dy = y - cy;
                        float arc = (w * 0.5f - _topInset - _cornerR)
                            + Mathf.Sqrt(Mathf.Max(0f, _cornerR * _cornerR - dy * dy));
                        xr = Mathf.Min(xr, arc);
                    }
                    // Bottom corner arcs (a closed rounded chip; folder tabs and plain
                    // gradient strips pass 0 — square bottoms).
                    float bcy = r.yMin + _bottomCornerR;
                    if (_bottomCornerR > 0.5f && y < bcy)
                    {
                        float dy = bcy - y;
                        float arc = (w * 0.5f - _bottomCornerR)
                            + Mathf.Sqrt(Mathf.Max(0f, _bottomCornerR * _bottomCornerR - dy * dy));
                        xr = Mathf.Min(xr, arc);
                    }
                    xr -= EdgeInset;
                    if (xr < 0.5f) xr = 0.5f;

                    Color c;
                    if (_hasMid)
                        c = t < 0.5f ? Color.Lerp(_top, _mid, t * 2f)
                                     : Color.Lerp(_mid, _bottom, (t - 0.5f) * 2f);
                    else
                    {
                        float k = t <= _plateau ? 0f
                            : Mathf.Pow((t - _plateau) / Mathf.Max(0.0001f, 1f - _plateau), 1.35f);
                        c = Color.Lerp(_top, _bottom, k);
                    }
                    vh.AddVert(new Vector3(cxm - xr, y), c, Vector2.zero);
                    vh.AddVert(new Vector3(cxm + xr, y), c, Vector2.zero);
                }
                for (int i = 0; i < RowT.Length - 1; i++)
                {
                    int a = i * 2;
                    vh.AddTriangle(a, a + 2, a + 3);
                    vh.AddTriangle(a, a + 3, a + 1);
                }
            }
        }

        /// <summary>Keeps the folder card's opaque fill in step with its rect and the live
        /// theme (the same LateUpdate idiom as UiaGlassSkin; every setter dirty-guards).
        /// Since the 1:1 geometry round the card draws NO border of its own — the gold
        /// collar (rails + band) is the frame; the panel is pure surface.</summary>
        internal sealed class FolderCardDriver : MonoBehaviour
        {
            public UI.Hud.PanelGraphic Panel;
            private RectTransform _rt;

            private void LateUpdate()
            {
                if (Panel == null) return;
                if (_rt == null) _rt = transform as RectTransform;
                if (_rt == null) return;
                var size = _rt.rect.size;
                if (size.x < 2f || size.y < 2f) return;
                Color fill = UiaTheme.Panel;
                fill.a = 1f;
                Panel.color = fill;
                Panel.BorderColor = Color.clear;
                Panel.BorderWidth = 0f;
                Panel.SetShape(size.x, size.y, UiaTheme.Corner);
            }
        }

        /// <summary>One folder tab (structural rethink 2026-09-26): the BODY is a stock
        /// <see cref="UI.Hud.PanelGraphic"/> — the same trapezoid + rounded-top-corners +
        /// open-bottom + Spec/BorderFade edge lighting every lit panel in the mod uses, fed
        /// through <see cref="UI.Hud.HudGlobalGlass"/> exactly like UiaGlassSkin's borders —
        /// with a <see cref="ChipGradientGraphic"/> overlay INSIDE it for the measured fill
        /// ramp. The body ENDS AT BAND-TOP: the band renders continuously beneath (only its
        /// bright line breaks), so no tab pixel exists inside the band body and the old
        /// square silhouette cannot exist. The selected tab's flow into the band = the ramp's
        /// bottom stop equalling the band crown, the <see cref="TabFeetGraphic"/> fillet feet
        /// overlapping band-top a few px, and the broken line. No drawn stroke ribbons, no
        /// baked shadows.</summary>
        internal sealed class FolderTab : MonoBehaviour,
            IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            private UI.Hud.PanelGraphic _panel;
            private ChipGradientGraphic _grad;
            private TabFeetGraphic _feet;
            private RectTransform _body;
            private TextMeshProUGUI _label;
            private Action _onClick;
            private bool _selected;
            private bool _hover;
            private bool _mainLevel;
            private float _visibleH, _bandTh, _raise;

            /// <summary>Read by the handle to place the band's top-line gap.</summary>
            internal bool Selected => _selected;

            internal void Init(string title, bool mainLevel, float visibleH, float bandTh,
                float raise, Action onClick)
            {
                _onClick = onClick;
                _mainLevel = mainLevel;
                _visibleH = visibleH;
                _bandTh = bandTh;
                _raise = raise;

                // The whole rect (face + band zone) stays the click target, as before.
                var hit = gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);   // alpha 0 still raycasts (rect hit test)
                hit.raycastTarget = true;

                // Body: face only — top of the rect down to BAND-TOP (never into the band).
                var bodyGo = UiaUi.Go("body", transform);
                _body = (RectTransform)bodyGo.transform;
                _body.anchorMin = Vector2.zero;
                _body.anchorMax = Vector2.one;
                _body.offsetMin = new Vector2(0f, bandTh);
                _body.offsetMax = new Vector2(0f, -raise);   // unselected default; SetSelected lifts it
                _panel = bodyGo.AddComponent<UI.Hud.PanelGraphic>();
                _panel.raycastTarget = false;

                // The fill ramp, tracing the same trapezoid inside the body.
                var gradGo = UiaUi.Go("grad", bodyGo.transform);
                UiaUi.Fill((RectTransform)gradGo.transform);
                _grad = gradGo.AddComponent<ChipGradientGraphic>();
                _grad.raycastTarget = false;

                // The fillet feet (selected only), on the FULL rect so they can dip past
                // band-top; toggled by SetSelected.
                var feetGo = UiaUi.Go("feet", transform);
                UiaUi.Fill((RectTransform)feetGo.transform);
                _feet = feetGo.AddComponent<TabFeetGraphic>();
                _feet.raycastTarget = false;
                feetGo.SetActive(false);

                _label = UiaUi.Text(transform, title ?? "", 11.5f, UiaTheme.TextDim,
                    TextAlignmentOptions.Center);
                var lrt = (RectTransform)_label.transform;
                UiaUi.Fill(lrt);
                lrt.offsetMin = new Vector2(6f, bandTh + 2f);
                lrt.offsetMax = new Vector2(-6f, -(raise + 1f));
                _label.fontStyle = FontStyles.Bold;
                // No ellipsis (FlorpyDorp: never cut anything off): a tab title too long for
                // its face shrinks toward 8.5pt and may wrap to a second line inside the face.
                UiaControls.FitText(_label, 8.5f);
            }

            internal void SetSelected(bool s)
            {
                _selected = s;
                if (_body != null)
                    _body.offsetMax = new Vector2(0f, s ? 0f : -_raise);   // selected rises 3px
                if (_feet != null && _feet.gameObject.activeSelf != s)
                    _feet.gameObject.SetActive(s);
            }

            private void LateUpdate()
            {
                if (_panel == null || _body == null) return;
                var size = _body.rect.size;
                if (size.x < 8f || size.y < 8f) return;

                float bw = UI.Hud.HudConfig.BorderWidth != null
                    ? UI.Hud.HudConfig.BorderWidth.Value : 1.4f;
                // The measured silhouettes: r9 arcs + 13-deg slants selected, r6 + 15-deg
                // unselected; the trapezoid's top narrowing = tan(slant) x face height.
                float rTop = _selected ? 9f : 6f;
                float topInset = (_selected ? 0.2309f : 0.2679f) * size.y;

                // The MOVING edge light first (the analytic sdfglass path — the shader-time
                // sweep the HUD boxes run; the strip is never masked, so it is always
                // eligible), then the mod's edge treatment (edge light + ripple, no glow
                // halo — UiaGlassSkin's interior recipe), THEN the per-tab overrides: Apply
                // stomps Spec/Sheen/BorderFade from the globals every call, and the global
                // Spec catch tints toward HudConfig.FxEdgeLightColor (theme cyan) — which is
                // exactly what turned the SELECTED tab's glow line cyan (FlorpyDorp's final
                // note). Order + re-assertion makes the selected edge unmistakably WARM: the
                // sweep tints from BorderColor, so it runs ORANGE on the selected tab and
                // cyan on the resting ones.
                bool sdfTab = UiaGlassSkin.ApplySdfFlow(_panel, true);
                UI.Hud.HudGlobalGlass.Apply(_panel, includeGlow: false, wantFrost: false,
                    wantTierB: false, externalMaterial: sdfTab);
                if (_selected)
                {
                    Color bot = UiaFolderPalette.FrameTop;   // == the band's crown: seam-free
                    _panel.color = bot;
                    // The glow line IS the bright gold border; the cyan-tinting global Spec
                    // is clamped low so the key-light catch cannot wash the orange out.
                    _panel.BorderColor = UiaFolderPalette.TabRimTop;
                    _panel.Spec = Mathf.Min(_panel.Spec, 0.15f);
                    _panel.BorderFade = 0.25f;   // edges soften as they descend
                    _grad.ConfigureThreeStop(UiaFolderPalette.TabFillTop, UiaFolderPalette.TabFillMid,
                        bot, topInset, rTop);
                    if (_feet != null)
                        _feet.Configure(size.y, _bandTh, topInset, bot);
                }
                else
                {
                    Color ft = _mainLevel ? UiaFolderPalette.MainTabFillTop : UiaFolderPalette.SubTabFillTop;
                    Color fb = _mainLevel ? UiaFolderPalette.MainTabFillBot : UiaFolderPalette.SubTabFillBot;
                    if (_hover) ft = Color.Lerp(ft, UiaTheme.Accent, 0.12f);
                    _panel.color = fb;
                    _panel.BorderColor = UiaTheme.Border;   // the same lit edge every kit panel gets
                    _panel.BorderFade = Mathf.Max(_panel.BorderFade, 0.65f);   // the measured side fade
                    _grad.Configure(ft, fb, topInset, rTop, 0f);
                }
                _panel.BorderWidth = bw;
                _panel.Sheen = 0f;                // the overlay owns the fill ramp
                _panel.BorderSides = 1 | 2 | 8;   // open bottom, both states
                _panel.SetShape(size.x, size.y, rTop, rTop, 0f, 0f, topInset, 0f);

                if (_label != null)
                    _label.color = _selected ? UiaFolderPalette.SelectedLabel
                                             : UiaFolderPalette.RestingLabel;
            }

            public void OnPointerClick(PointerEventData e)
            {
                if (e != null && e.button != PointerEventData.InputButton.Left) return;
                if (_onClick != null) _onClick();
            }

            public void OnPointerEnter(PointerEventData e) { _hover = true; }
            public void OnPointerExit(PointerEventData e) { _hover = false; }

            private void OnDestroy()
            {
                // Forget the analytic material assignment before the graphic dies (the
                // window-panel hygiene) so restyle churn can't fill the registry.
                if (_panel != null) UI.Hud.HudFxMaterials.Unassign(_panel);
            }
        }
    }
}
